using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Pipe.Core.Geom;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;
using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Pipe.Civil;

/// <summary>도면 다루기 도우미(레이어 · 선종류 · 글자 스타일 · 스타일 고르기 · 손잡이 · 폴리선 읽기 · 옛 그림 지우기).</summary>
internal static class CivilUtil
{
    internal const string TextStyleName = "DHP";
    /// <summary>우리가 그린 객체에 다는 표식(확장 자료 앱 이름).</summary>
    internal const string TagApp = "DHPIPE";

    internal static string Handle(ObjectId id) => id.IsNull ? "" : id.Handle.ToString();

    /// <summary>손잡이(16진 글) → 객체. 없거나 지워졌으면 Null.</summary>
    internal static ObjectId FromHandle(Database db, string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return ObjectId.Null;
        try
        {
            long v = Convert.ToInt64(hex, 16);
            if (!db.TryGetObjectId(new Handle(v), out ObjectId id)) return ObjectId.Null;
            return id.IsNull || id.IsErased ? ObjectId.Null : id;
        }
        catch { return ObjectId.Null; }
    }

    /// <summary>선종류를 올린다. 못 올리면 Continuous.</summary>
    internal static ObjectId EnsureLinetype(Database db, Transaction tr, string name)
    {
        var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (string.IsNullOrEmpty(name) || name.Equals("Continuous", StringComparison.OrdinalIgnoreCase)) return db.ContinuousLinetype;
        if (lt.Has(name)) return lt[name];
        foreach (string file in new[] { "acadiso.lin", "acad.lin" })
        {
            try
            {
                db.LoadLineTypeFile(name, file);
                lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (lt.Has(name)) return lt[name];
            }
            catch { }
        }
        return db.ContinuousLinetype;
    }

    internal static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci, string? linetype = null)
    {
        var tbl = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (tbl.Has(name)) return tbl[name];
        tbl.UpgradeOpen();
        var rec = new LayerTableRecord
        {
            Name = name,
            Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci),
        };
        if (!string.IsNullOrEmpty(linetype)) rec.LinetypeObjectId = EnsureLinetype(db, tr, linetype);
        var id = tbl.Add(rec);
        tr.AddNewlyCreatedDBObject(rec, true);
        return id;
    }

    internal static ObjectId EnsureLayer(Database db, string name, short aci, string? linetype = null)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var id = EnsureLayer(db, tr, name, aci, linetype);
        tr.Commit();
        return id;
    }

    /// <summary>글자 스타일 «DHP»(맑은 고딕).</summary>
    internal static ObjectId EnsureTextStyle(Database db, Transaction tr)
    {
        var tbl = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (tbl.Has(TextStyleName)) return tbl[TextStyleName];
        tbl.UpgradeOpen();
        var rec = new TextStyleTableRecord { Name = TextStyleName };
        try { rec.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("맑은 고딕", false, false, 129, 0); }
        catch { try { rec.FileName = "malgun.ttf"; } catch { } }
        var id = tbl.Add(rec);
        tr.AddNewlyCreatedDBObject(rec, true);
        return id;
    }

    /// <summary>Civil 스타일 고르기: 이름에 prefer 가 든 첫 것, 없으면 첫 스타일.</summary>
    internal static ObjectId PickStyle(Database db, System.Collections.IEnumerable styleIds, params string[] prefer)
    {
        ObjectId first = ObjectId.Null;
        var all = new List<(ObjectId Id, string Name)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in styleIds)
            {
                if (first.IsNull) first = id;
                try { if (tr.GetObject(id, OpenMode.ForRead) is CivilStyles.StyleBase sb) all.Add((id, sb.Name ?? "")); } catch { }
            }
            tr.Commit();
        }
        catch { }
        foreach (string want in prefer)
            foreach (var (id, nm) in all)
                if (nm.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) return id;
        return first;
    }

    /// <summary>안 쓰는 노선 이름(«관로», «관로2» …).</summary>
    internal static string UniqueAlignName(Database db, CivilApp.CivilDocument cdoc, string baseName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
                if (tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al) used.Add(al.Name);
            tr.Commit();
        }
        catch { }
        return Unique(used, baseName);
    }

    /// <summary>그 선형에서 안 쓰는 종단 이름.</summary>
    internal static string UniqueProfileName(Database db, ObjectId alignId, string baseName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var al = (CivilDb.Alignment)tr.GetObject(alignId, OpenMode.ForRead);
            foreach (ObjectId pid in al.GetProfileIds())
                if (tr.GetObject(pid, OpenMode.ForRead) is CivilDb.Profile pr) used.Add(pr.Name);
            tr.Commit();
        }
        catch { }
        return Unique(used, baseName);
    }

    private static string Unique(HashSet<string> used, string baseName)
    {
        if (!used.Contains(baseName)) return baseName;
        for (int i = 2; i < 1000; i++) if (!used.Contains(baseName + i)) return baseName + i;
        return baseName + Guid.NewGuid().ToString("N")[..6];
    }

    /// <summary>
    /// 폴리선 · 선의 꼭짓점(세계 좌표의 평면). 호는 2° 마다 나눈 꺾은선으로. 닫힌 폴리선이면 첫 점을 끝에 다시 넣지 않는다.
    /// 옛 폴리선의 스플라인 조종점은 뺀다.
    /// ※ LW 폴리선의 2D 점은 그 폴리선의 좌표계(OCS)라 법선이 −Z 인 폴리선(거울 복사 · 변환본)은 거울 자리가 된다 →
    ///   세계 좌표로 읽는다(GetPoint3dAt · GetPointAtParameter) — 1127 검토 보통.
    /// </summary>
    internal static List<P2> PolyPoints(Transaction tr, ObjectId id, out bool closed)
    {
        var pts = new List<P2>();
        closed = false;
        switch (tr.GetObject(id, OpenMode.ForRead))
        {
            case Polyline lw:
                closed = lw.Closed;
                int n = lw.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    var p = lw.GetPoint3dAt(i);
                    pts.Add(new P2(p.X, p.Y));
                    bool last = i == n - 1;
                    if (last && !lw.Closed) break;
                    double bulge = 0;
                    try { bulge = lw.GetBulgeAt(i); } catch { }
                    if (Math.Abs(bulge) < 1e-9) continue;
                    double sweep = 4 * Math.Atan(Math.Abs(bulge));
                    int parts = Math.Max(2, (int)Math.Ceiling(sweep / (2 * Math.PI / 180)));
                    for (int k = 1; k < parts; k++)
                    {
                        try
                        {
                            var q = lw.GetPointAtParameter(i + (double)k / parts);
                            pts.Add(new P2(q.X, q.Y));
                        }
                        catch { }
                    }
                }
                break;
            case Polyline3d p3:
                closed = p3.Closed;
                foreach (ObjectId vid in p3)
                    if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v && v.VertexType != Vertex3dType.ControlVertex)
                        pts.Add(new P2(v.Position.X, v.Position.Y));
                break;
            case Polyline2d p2:
                closed = p2.Closed;
                foreach (ObjectId vid in p2)
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v && v.VertexType != Vertex2dType.SplineControlVertex)
                    {
                        var w = p2.VertexPosition(v);       // 세계 좌표
                        pts.Add(new P2(w.X, w.Y));
                    }
                break;
            case Line ln:
                pts.Add(new P2(ln.StartPoint.X, ln.StartPoint.Y));
                pts.Add(new P2(ln.EndPoint.X, ln.EndPoint.Y));
                break;
        }
        return pts;
    }

    /// <summary>
    /// 폴리선 · 선의 평평한(Z = 0 · 법선 +Z) 사본을 만든다. 노선을 만들 때 원본을 지우지 않으려고 쓴다. 호(볼록)는 그대로 옮긴다
    /// (법선이 −Z 인 폴리선은 볼록의 부호를 뒤집는다).
    /// </summary>
    internal static ObjectId FlatCopy(Database db, ObjectId srcId, ObjectId layerId, out int nv, out double len)
    {
        nv = 0; len = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var pts = new List<(Point2d P, double B)>();
        switch (tr.GetObject(srcId, OpenMode.ForRead))
        {
            case Polyline lw:
                double sign = lw.Normal.Z < 0 ? -1 : 1;
                for (int i = 0; i < lw.NumberOfVertices; i++)
                {
                    double b = 0;
                    try { b = lw.GetBulgeAt(i); } catch { }
                    var w = lw.GetPoint3dAt(i);
                    pts.Add((new Point2d(w.X, w.Y), b * sign));
                }
                break;
            case Polyline3d p3:
                foreach (ObjectId vid in p3)
                    if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v && v.VertexType != Vertex3dType.ControlVertex)
                        pts.Add((new Point2d(v.Position.X, v.Position.Y), 0));
                break;
            case Polyline2d p2:
                double sign2 = p2.Normal.Z < 0 ? -1 : 1;
                foreach (ObjectId vid in p2)
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v && v.VertexType != Vertex2dType.SplineControlVertex)
                    {
                        var w = p2.VertexPosition(v);
                        pts.Add((new Point2d(w.X, w.Y), v.Bulge * sign2));
                    }
                break;
            case Line ln:
                pts.Add((new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0));
                pts.Add((new Point2d(ln.EndPoint.X, ln.EndPoint.Y), 0));
                break;
        }
        // 겹친 점을 뺀다
        var clean = new List<(Point2d P, double B)>();
        foreach (var q in pts)
        {
            if (clean.Count > 0 && clean[^1].P.GetDistanceTo(q.P) < 1e-6) continue;
            clean.Add(q);
        }
        if (clean.Count < 2) { tr.Commit(); return ObjectId.Null; }
        var pl = new Polyline();
        for (int i = 0; i < clean.Count; i++) pl.AddVertexAt(i, clean[i].P, i == clean.Count - 1 ? 0 : clean[i].B, 0, 0);
        pl.Elevation = 0;
        if (!layerId.IsNull) pl.LayerId = layerId;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var id = ms.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        nv = clean.Count;
        len = pl.Length;
        tr.Commit();
        return id;
    }

    // ---------------------------------------------------------------- 우리가 그린 것 표시 · 지우기
    private static void EnsureTagApp(Database db, Transaction tr)
    {
        var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (rat.Has(TagApp)) return;
        rat.UpgradeOpen();
        var rec = new RegAppTableRecord { Name = TagApp };
        rat.Add(rec);
        tr.AddNewlyCreatedDBObject(rec, true);
    }

    /// <summary>묶음 이름(종류 + 노선 손잡이). 표식 글로도 쓴다.</summary>
    internal static string GroupName(string kind, string handle) => $"DHP_{kind}_{handle}";

    /// <summary>
    /// 객체들에 표식(확장 자료)을 달고 이름 붙은 묶음으로 만든다. 묶음은 «선택 안 됨» — 선 하나를 누를 때 수천 개가 함께 잡히지 않게.
    /// </summary>
    internal static void MakeGroup(Database db, Transaction tr, string name, ObjectIdCollection ids, string description)
    {
        if (ids.Count == 0) return;
        EnsureTagApp(db, tr);
        foreach (ObjectId id in ids)
        {
            try
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, TagApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, name));
                ent.XData = rb;
            }
            catch { }
        }
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
        var grp = new Group(description, false);
        gd.SetAt(name, grp);
        tr.AddNewlyCreatedDBObject(grp, true);
        grp.Append(ids);
    }

    /// <summary>
    /// 옛 그림을 지운다.
    /// 보통 길 = 묶음에 든 것 가운데 우리 표식이 달린 것만(사용자가 복사해 둔 그림은 표식이 따라가도 묶음에 안 들어 있으니 남는다).
    /// 묶음이 없으면(사용자가 풀었을 때) 모형 공간에서 같은 표식이 달린 것을 찾아 지운다 — 이때는 복사본도 함께 지워진다.
    /// 사용자가 묶음에 넣은 남의 객체(표식 없음)는 지우지 않는다. 잠긴 레이어의 객체는 건너뛴다.
    /// </summary>
    internal static int EraseGroup(Database db, Transaction tr, string name)
    {
        int n = 0;
        bool Ours(DBObject o)
        {
            using var xd = o.GetXDataForApplication(TagApp);
            if (xd == null) return false;
            foreach (TypedValue tv in xd)
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString && tv.Value is string s && s == name) return true;
            return false;
        }
        void TryErase(ObjectId id)
        {
            try
            {
                if (id.IsNull || id.IsErased) return;
                var ent = tr.GetObject(id, OpenMode.ForRead, false, true);
                if (!Ours(ent)) return;
                ent.UpgradeOpen();
                ent.Erase();
                n++;
            }
            catch { }
        }
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        if (gd.Contains(name))
        {
            var grp = (Group)tr.GetObject(gd.GetAt(name), OpenMode.ForWrite);
            foreach (ObjectId id in grp.GetAllEntityIds()) TryErase(id);
            grp.Erase();
            return n;
        }
        // 묶음이 없다 = 처음 그리거나 사용자가 묶음을 풀었다. 뒤의 경우에만 남은 그림이 있으므로 표식으로 찾는다.
        // 모형 공간 전체를 훑는 일이라 표식 앱이 도면에 있을 때만 한다(큰 도면에서 명령마다 훑지 않게 — 2차 검토 권장).
        RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!rat.Has(TagApp)) return n;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms) TryErase(id);
        return n;
    }

    /// <summary>묶음에 든(살아 있는) 객체 수 — 시험용.</summary>
    internal static int GroupCount(Database db, string name, string? dxfName = null)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        int n = 0;
        if (gd.Contains(name))
            n = ((Group)tr.GetObject(gd.GetAt(name), OpenMode.ForRead)).GetAllEntityIds().Count(id => !id.IsErased && (dxfName == null || id.ObjectClass.DxfName == dxfName));
        tr.Commit();
        return n;
    }
}
