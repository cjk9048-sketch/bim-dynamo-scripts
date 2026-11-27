using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Model3D;

namespace DH.Pipe.Civil;

/// <summary>엔진의 그림 목록(DrawList)을 도면 객체(선 · 폴리선 · 원 · 글자 · 해치)로 쓴다.</summary>
internal static class DrawWriter
{
    /// <summary>
    /// 그림 목록을 모형 공간에 쓴다. group = «횡단» · «종단»(레이어 이름 가운데 글) · scale = 축척 분모(해치 간격).
    /// 돌려줌 = 만든 객체들.
    /// </summary>
    internal static ObjectIdCollection Write(Database db, Transaction tr, DrawList list, string group, Point3d origin, double scale)
    {
        var ids = new ObjectIdCollection();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var layers = new Dictionary<string, ObjectId>();
        foreach (var (key, aci, lt) in Lay.Table0) layers[key] = CivilUtil.EnsureLayer(db, tr, Lay.Name(group, key), aci, lt);
        ObjectId style = CivilUtil.EnsureTextStyle(db, tr);
        ObjectId LayerOf(string key) => layers.TryGetValue(key, out var id) ? id : layers[Lay.Text];
        double ox = origin.X, oy = origin.Y;

        void Add(Entity e, string layer)
        {
            e.LayerId = LayerOf(layer);
            ids.Add(ms.AppendEntity(e));
            tr.AddNewlyCreatedDBObject(e, true);
        }

        int hatchFail = 0;
        double patUnit = db.Measurement == MeasurementValue.Metric ? 1.0 / 25.4 : 1.0;
        foreach (var it in list.Items)
        {
            switch (it.Kind)
            {
                case DrawKind.Line:
                    Add(new Line(new Point3d(it.Pts[0].X + ox, it.Pts[0].Y + oy, 0), new Point3d(it.Pts[1].X + ox, it.Pts[1].Y + oy, 0)), it.Layer);
                    break;
                case DrawKind.Poly:
                {
                    var pl = new Polyline();
                    for (int i = 0; i < it.Pts.Count; i++) pl.AddVertexAt(i, new Point2d(it.Pts[i].X + ox, it.Pts[i].Y + oy), 0, 0, 0);
                    pl.Closed = it.Closed;
                    pl.Plinegen = true;             // 선종류 무늬를 꼭짓점을 넘어 이어 그린다(점이 촘촘한 지층선 · 수위선이 실선으로 나오지 않게)
                    Add(pl, it.Layer);
                    break;
                }
                case DrawKind.Circle:
                    Add(new Circle(new Point3d(it.Pts[0].X + ox, it.Pts[0].Y + oy, 0), Vector3d.ZAxis, Math.Max(it.R, 1e-6)), it.Layer);
                    break;
                case DrawKind.Text:
                {
                    if (string.IsNullOrEmpty(it.Text)) break;
                    var at = new Point3d(it.Pts[0].X + ox, it.Pts[0].Y + oy, 0);
                    var t = new DBText { TextString = it.Text, Height = Math.Max(it.H, 1e-6), Rotation = it.Rot * Math.PI / 180.0, TextStyleId = style, Position = at };
                    if (it.Align != 0 || it.VAlign != 0)
                    {
                        t.HorizontalMode = it.Align == 1 ? TextHorizontalMode.TextCenter : it.Align == 2 ? TextHorizontalMode.TextRight : TextHorizontalMode.TextLeft;
                        t.VerticalMode = it.VAlign == 1 ? TextVerticalMode.TextVerticalMid : TextVerticalMode.TextBase;
                        t.AlignmentPoint = at;
                    }
                    Add(t, it.Layer);
                    if (it.Align != 0 || it.VAlign != 0) { try { t.AdjustAlignment(db); } catch { } }
                    break;
                }
                case DrawKind.Fill:
                {
                    if (it.Pts.Count < 3) break;
                    Hatch? h = null;
                    try
                    {
                        h = new Hatch();
                        Add(h, it.Layer);
                        string pat = it.Pattern switch { "SAND" => "AR-SAND", "GRAVEL" => "GRAVEL", "CONC" => "AR-CONC", _ => "SOLID" };
                        // 무늬 간격: 무늬 한 칸이 종이에서 알맞은 크기가 되게 축척을 곱한다.
                        // 미터 도면(MEASUREMENT 1)은 무늬 파일(acadiso.pat)의 AR- 무늬가 25.4배 크게 들어 있어 그만큼 줄인다(1127 화면 없는 Civil 실측 — 점이 듬성듬성).
                        double ps = it.Pattern switch { "SAND" => 0.0006, "GRAVEL" => 0.0030, "CONC" => 0.0006, _ => 1.0 } * scale * patUnit;
                        h.Normal = Vector3d.ZAxis;
                        h.Elevation = 0;
                        if (pat != "SOLID") h.PatternScale = ps;
                        try { h.SetHatchPattern(HatchPatternType.PreDefined, pat); }
                        catch { pat = "SOLID"; h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID"); h.Transparency = new Autodesk.AutoCAD.Colors.Transparency(160); }
                        var pts = new Point2dCollection();
                        var bul = new DoubleCollection();
                        foreach (var p in it.Pts) { pts.Add(new Point2d(p.X + ox, p.Y + oy)); bul.Add(0); }
                        pts.Add(pts[0]);
                        bul.Add(0);
                        h.AppendLoop(HatchLoopTypes.External | HatchLoopTypes.Polyline, pts, bul);
                        foreach (var (hx, hy, hr) in it.Holes)
                        {
                            var hp = new Point2dCollection();
                            var hb = new DoubleCollection();
                            // 원 = 볼록 1 인 꼭짓점 둘
                            hp.Add(new Point2d(hx + ox - hr, hy + oy)); hb.Add(1);
                            hp.Add(new Point2d(hx + ox + hr, hy + oy)); hb.Add(1);
                            hp.Add(new Point2d(hx + ox - hr, hy + oy)); hb.Add(0);
                            try { h.AppendLoop(HatchLoopTypes.Polyline, hp, hb); } catch { }
                        }
                        if (pat != "SOLID") h.PatternScale = ps;
                        h.EvaluateHatch(true);
                    }
                    catch (System.Exception ex)
                    {
                        if (hatchFail++ == 0) PipeLog.Line("[그리기] 해치 실패 — " + ex.Message);
                        // 테두리 없는 빈 해치가 도면에 남지 않게 지운다(남으면 AUDIT 오류 — 검토 보통)
                        try { if (h != null && !h.ObjectId.IsNull) { ids.Remove(h.ObjectId); h.Erase(); } } catch { }
                    }
                    break;
                }
            }
        }
        if (hatchFail > 0) PipeLog.Line($"[그리기] 해치 {hatchFail}개 못 그림");
        return ids;
    }

    /// <summary>3D 덩어리를 메시 객체로. 돌려줌 = 만든 객체들.</summary>
    internal static ObjectIdCollection WriteMeshes(Database db, Transaction tr, IReadOnlyList<MeshData> meshes)
    {
        var ids = new ObjectIdCollection();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var m in meshes)
        {
            var (name, aci) = m.Key switch
            {
                "TRENCH" => ("터파기", (short)30),
                "PIPE" => ("관", (short)4),
                "CASING" => ("케이싱", (short)6),
                "SHAFT" => ("수직구", (short)6),
                "SHORE" => ("흙막이", (short)6),
                _ => (m.Key, (short)7),
            };
            ObjectId layer = CivilUtil.EnsureLayer(db, tr, PipeLayers.Mesh + name, aci);
            Entity? ent = null;
            SubDMesh? sub = null;
            try
            {
                var verts = new Point3dCollection();
                foreach (var v in m.V) verts.Add(new Point3d(v.X, v.Y, v.Z));
                var faces = new Int32Collection();
                foreach (var f in m.F)
                {
                    faces.Add(f.Length);
                    foreach (int i in f) faces.Add(i);
                }
                sub = new SubDMesh();
                sub.SetDatabaseDefaults(db);
                sub.SetSubDMesh(verts, faces, 0);
                ent = sub;
            }
            catch (System.Exception ex)
            {
                PipeLog.Line($"[3D] {m.Key} 메시 실패 → 면 그물로 — {ex.Message}");
                // 도면에 안 붙은 객체는 바로 버린다(그냥 두면 나중에 다른 스레드에서 치워지다 Civil 이 죽을 수 있다 — 검토 보통)
                try { sub?.Dispose(); } catch { }
                ent = null;
            }
            if (ent != null)
            {
                ent.LayerId = layer;
                ids.Add(ms.AppendEntity(ent));
                tr.AddNewlyCreatedDBObject(ent, true);
                continue;
            }
            // 메시가 안 되면 면 그물(꼭짓점 3만 개씩)
            foreach (var id in PolyFace(db, tr, ms, m, layer)) ids.Add(id);
        }
        return ids;
    }

    private static List<ObjectId> PolyFace(Database db, Transaction tr, BlockTableRecord ms, MeshData m, ObjectId layer)
    {
        var res = new List<ObjectId>();
        const int MaxV = 30000;
        int fi = 0;
        while (fi < m.F.Count)
        {
            var map = new Dictionary<int, short>();
            var pfm = new PolyFaceMesh();
            pfm.SetDatabaseDefaults(db);
            pfm.LayerId = layer;
            res.Add(ms.AppendEntity(pfm));
            tr.AddNewlyCreatedDBObject(pfm, true);
            var faces = new List<short[]>();
            for (; fi < m.F.Count; fi++)
            {
                var f = m.F[fi];
                if (map.Count + f.Length > MaxV) break;
                var rec = new short[4];
                for (int k = 0; k < 4; k++)
                {
                    if (k >= f.Length) { rec[k] = 0; continue; }
                    if (!map.TryGetValue(f[k], out short idx))
                    {
                        idx = (short)(map.Count + 1);
                        map[f[k]] = idx;
                        var v = m.V[f[k]];
                        var pv = new PolyFaceMeshVertex(new Point3d(v.X, v.Y, v.Z));
                        pfm.AppendVertex(pv);
                        tr.AddNewlyCreatedDBObject(pv, true);
                    }
                    rec[k] = idx;
                }
                faces.Add(rec);
            }
            foreach (var r in faces)
            {
                var fr = new FaceRecord(r[0], r[1], r[2], r[3]);
                pfm.AppendFaceRecord(fr);
                tr.AddNewlyCreatedDBObject(fr, true);
            }
        }
        return res;
    }
}
