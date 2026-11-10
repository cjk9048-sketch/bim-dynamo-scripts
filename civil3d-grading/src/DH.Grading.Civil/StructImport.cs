using System.Runtime.CompilerServices;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v115 · JACK 1105 Revit 구조물 · 계획 2026-11-05_Revit구조물_투영 3절 · 5.1] Revit 3D DWG → xref 덧씌우기(Overlay) · 형상 읽기 · 기록 · 문서마다 메모리 캐시.
/// <list type="bullet">
/// <item>원본 = xref(<see cref="LayXref"/> · 상대경로) — 화면과 계산이 같은 자료(D2). 형상은 붙인 xref 블록(호스트 안 사본)에서 읽고 삽입 변환을 매번 곱한다.</item>
/// <item>단위 계수(D7)는 붙이기 전에 옆 DB로 INSUNITS · 범위를 읽어 원지반 겹침으로 검산 — 삽입 축척에 <b>직접</b>(곱은 한 번 · 검토 낮음).</item>
/// <item>바뀜 검사(D10)는 명령 머리에서만(트랜잭션 밖 다시 싣기) — 그리는 함수는 캐시 · 지금 실린 xref만 쓴다.</item>
/// </list></summary>
internal static class StructImport
{
    internal const string LayXref = "DH-구조물-원본", LayPlan = "DH-구조물";
    const string DictName = "DH_GRADING", RecName = "STRUCTS";
    internal const string BlockBase = "DH구조물_";

    /// <summary>불러온 구조물 하나 — 기록 · 모형(캐시) · 삽입.</summary>
    internal sealed class Loaded
    {
        public StructRec Rec = new();
        public StructModel Model = null!;
        public ObjectId Insert, Btr;
        public string Note = "";
    }

    // ── 기록(NOD DH_GRADING/STRUCTS) ─────────────────────────────

    internal static bool TryLoad(Database db, Transaction tr, out StructHead head, out List<StructRec> recs, out string why)
    {
        head = new StructHead(); recs = new List<StructRec>(); why = "";
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return true;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!dict.Contains(RecName)) return true;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(RecName), OpenMode.ForRead);
            if (xr.Data == null) return true;
            var vals = new List<(char, object)>();
            foreach (TypedValue tv in xr.Data.AsArray())
            {
                switch (tv.TypeCode)
                {
                    case (short)DxfCode.Text: vals.Add(('S', tv.Value as string ?? "")); break;
                    case (short)DxfCode.Int32: vals.Add(('I', System.Convert.ToInt32(tv.Value))); break;
                    case (short)DxfCode.Real: vals.Add(('D', System.Convert.ToDouble(tv.Value))); break;
                    default: why = $"구조물 목록에 모르는 값 종류 {tv.TypeCode}"; return false;
                }
            }
            return StructList.TryDecode(vals, out head, out recs, out why);
        }
        catch (System.Exception ex) { why = "구조물 목록을 못 읽었다 — " + ex.Message; return false; }
    }

    internal static void Save(Database db, Transaction tr, StructHead head, IReadOnlyList<StructRec> recs)
    {
        var vals = new List<TypedValue>();
        foreach (var (k, v) in StructList.Encode(head, recs))
            vals.Add(k switch
            {
                'S' => new TypedValue((int)DxfCode.Text, (string)v),
                'I' => new TypedValue((int)DxfCode.Int32, (int)v),
                _ => new TypedValue((int)DxfCode.Real, (double)v),
            });
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dict;
        if (nod.Contains(DictName)) dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        else { dict = new DBDictionary(); nod.SetAt(DictName, dict); tr.AddNewlyCreatedDBObject(dict, true); }
        if (dict.Contains(RecName)) dict.Remove(RecName);
        if (recs.Count == 0 && head.SiteXsecScale <= 0) return;
        using var rb = new ResultBuffer(vals.ToArray());
        var xr = new Xrecord { Data = rb };
        dict.SetAt(RecName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    /// <summary>기록이 있나(그리기 갈래를 싸게 건너뛰려고).</summary>
    internal static bool Any(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            bool ok = TryLoad(db, tr, out _, out var recs, out _) && recs.Count > 0;
            tr.Commit();
            return ok;
        }
        catch { return false; }
    }

    // ── 파일 ─────────────────────────────

    internal static string Sha16(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(fs)).Substring(0, 16).ToLowerInvariant();
    }

    internal static string MtimeOf(string path) => File.GetLastWriteTimeUtc(path).ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>도면 폴더 — 저장한 .dwg일 때만(새 도면은 템플릿 경로라 상대경로 기준이 못 됨 → 빈 값 = 절대경로만).</summary>
    internal static string HostDir(Database db)
    {
        try { string f = db.Filename ?? ""; return f.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase) && File.Exists(f) ? Path.GetDirectoryName(f) ?? "" : ""; }
        catch { return ""; }
    }

    /// <summary>원본 경로 — xref 블록 PathName 먼저(eTransmit이 고치는 쪽 · 상대면 도면 폴더 기준) · 그다음 기록(상대 · 절대).</summary>
    internal static string? ResolvePath(Database db, Transaction tr, ObjectId btrId, StructRec r)
    {
        string dir = HostDir(db);
        var cands = new List<string>();
        try { if (!btrId.IsNull && tr.GetObject(btrId, OpenMode.ForRead) is BlockTableRecord b && !string.IsNullOrEmpty(b.PathName)) cands.Add(b.PathName); } catch { }
        if (r.PathRel.Length > 0) cands.Add(r.PathRel);
        if (r.PathAbs.Length > 0) cands.Add(r.PathAbs);
        foreach (var c in cands)
        {
            try
            {
                string p = Path.IsPathRooted(c) ? c : dir.Length > 0 ? Path.GetFullPath(Path.Combine(dir, c)) : c;
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    // ── 형상 읽기 ─────────────────────────────

    static double Det(Matrix3d m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    static string BareLayer(string ly) { int k = ly.LastIndexOf('|'); return k >= 0 ? ly[(k + 1)..] : ly; }

    /// <summary>xref 블록(호스트 안 사본)을 걸어 삼각형으로 — 중첩 블록 · 대칭(det &lt; 0이면 감김 뒤집기) · 블록 안 0 레이어는 삽입 레이어. 국소 원점 = 삽입 범위 최소 1 m 내림.</summary>
    internal static StructSoup ReadSoup(Transaction tr, BlockReference br, out string note)
    {
        var s = new StructSoup();
        var M0 = br.BlockTransform;
        try
        {
            var ex = br.GeometricExtents;
            var o = StructSoup.OriginFor(ex.MinPoint.X, ex.MinPoint.Y, ex.MinPoint.Z);
            s.Ox = o.X; s.Oy = o.Y; s.Oz = o.Z;
        }
        catch { }
        var toLocal = Matrix3d.Displacement(new Vector3d(-s.Ox, -s.Oy, -s.Oz));
        int nPfm = 0, nPm = 0, nFace = 0, nSub = 0, nSol = 0, nSolFail = 0, nBlk = 0;
        var kinds = new Dictionary<string, int>();
        Point3 L(Point3d p, Matrix3d M) { var q = p.TransformBy(M); return new Point3(q.X - s.Ox, q.Y - s.Oy, q.Z - s.Oz); }
        void Walk(ObjectId btrId, Matrix3d M, string inherit, int depth)
        {
            if (depth > 16 || tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr) return;
            bool flip = Det(M) < 0;
            foreach (ObjectId id in btr)
            {
                Entity? e = null;
                try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; } catch { }
                if (e == null || !e.Visible) continue;
                string ly = BareLayer(e.Layer ?? "");
                if (ly == "0" && inherit.Length > 0) ly = inherit;
                try
                {
                    switch (e)
                    {
                        case BlockReference b2:
                            nBlk++; Walk(b2.BlockTableRecord, M * b2.BlockTransform, ly, depth + 1); break;
                        case PolyFaceMesh pfm:
                            {
                                nPfm++;
                                var v = new List<Point3>(); var f = new List<int[]>();
                                foreach (ObjectId sid in pfm)
                                {
                                    var so = tr.GetObject(sid, OpenMode.ForRead);
                                    if (so is PolyFaceMeshVertex pv) v.Add(L(pv.Position, M));
                                    else if (so is FaceRecord fr) f.Add(new int[] { fr.GetVertexAt(0), fr.GetVertexAt(1), fr.GetVertexAt(2), fr.GetVertexAt(3) });
                                }
                                s.AddPolyFace(v, f, ly, flip);
                                break;
                            }
                        case PolygonMesh pm when pm.PolyMeshType == PolyMeshType.SimpleMesh:
                            {
                                nPm++;
                                var v = new List<Point3>();
                                foreach (ObjectId vid in pm) if (tr.GetObject(vid, OpenMode.ForRead) is PolygonMeshVertex q) v.Add(L(q.Position, M));
                                s.AddGrid(v, pm.MSize, pm.NSize, pm.IsMClosed, pm.IsNClosed, ly, flip);
                                break;
                            }
                        case Face f3:
                            {
                                nFace++;
                                var v = new List<Point3> { L(f3.GetVertexAt(0), M), L(f3.GetVertexAt(1), M), L(f3.GetVertexAt(2), M), L(f3.GetVertexAt(3), M) };
                                s.AddPolygon(v, new[] { 0, 1, 2, 3 }, ly, flip);
                                break;
                            }
                        case SubDMesh sd:
                            {
                                nSub++;
                                var v = new List<Point3>(); foreach (Point3d p in sd.Vertices) v.Add(L(p, M));
                                var fa = new List<int>(); foreach (int k in sd.FaceArray) fa.Add(k);
                                s.AddFaceList(v, fa, ly, flip);
                                break;
                            }
                        case Solid3d or Autodesk.AutoCAD.DatabaseServices.Surface or Body or PolygonMesh:
                            {
                                nSol++;
                                if (!Facet(e, toLocal * M, s, ly)) nSolFail++;
                                break;
                            }
                        default:
                            {
                                string k = e.GetType().Name;
                                kinds[k] = kinds.TryGetValue(k, out int c) ? c + 1 : 1;
                                s.Unused++;
                                break;
                            }
                    }
                }
                catch { s.Unused++; }
            }
        }
        Walk(br.BlockTableRecord, M0, BareLayer(br.Layer ?? ""), 0);
        note = $"폴리페이스 {nPfm} · 폴리곤메시 {nPm} · 3D면 {nFace} · 서브D {nSub} · 솔리드 {nSol}" + (nSolFail > 0 ? $"(⚠메시 실패 {nSolFail})" : "") + $" · 블록 {nBlk}"
             + (kinds.Count > 0 ? " · 안 쓴 " + string.Join(", ", kinds.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value}")) : "")
             + $" · 원점 ({s.Ox:F0}, {s.Oy:F0}, {s.Oz:F0})";
        return s;
    }

    /// <summary>솔리드 · 곡면 → 메시(<c>SubDMesh.GetObjectMesh</c>) — 옮긴 사본(국소 m)에서 먼저(큰 좌표 ACIS 정밀 · 계획 3절). 안 되면 원본을 메시로 하고 꼭짓점을 옮긴다.</summary>
    static bool Facet(Entity e, Matrix3d toLocalM, StructSoup s, string ly)
    {
        var fd = new MeshFaceterData { FaceterDevSurface = 0.005, FaceterDevNormal = 5, FaceterGridRatio = 0, FaceterMaxEdgeLength = 0, FaceterMaxGrid = 4096, FaceterMinUGrid = 0, FaceterMinVGrid = 0, FaceterMeshType = 2 };
        try
        {
            using var cl = (Entity)e.Clone();
            cl.TransformBy(toLocalM);
            var md = SubDMesh.GetObjectMesh(cl, fd);
            var v = new List<Point3>(); foreach (Point3d p in md.VertexArray) v.Add(new Point3(p.X, p.Y, p.Z));
            var fa = new List<int>(); foreach (int k in md.FaceArray) fa.Add(k);
            if (s.AddFaceList(v, fa, ly) > 0) return true;
        }
        catch { }
        try
        {
            double sc = System.Math.Pow(System.Math.Abs(Det(toLocalM)), 1.0 / 3);
            if (sc > 0) fd.FaceterDevSurface = 0.005 / sc;
            var md = SubDMesh.GetObjectMesh(e, fd);
            bool flip = Det(toLocalM) < 0;
            var v = new List<Point3>(); foreach (Point3d p in md.VertexArray) { var q = p.TransformBy(toLocalM); v.Add(new Point3(q.X, q.Y, q.Z)); }
            var fa = new List<int>(); foreach (int k in md.FaceArray) fa.Add(k);
            return s.AddFaceList(v, fa, ly, flip) > 0;
        }
        catch { return false; }
    }

    // ── 캐시 ─────────────────────────────

    sealed class Entry { public string Key = ""; public StructModel Model = null!; public string Note = ""; }
    static readonly ConditionalWeakTable<Database, Dictionary<int, Entry>> Cache = new();

    internal static string KeyOf(StructRec r, BlockReference br)
    {
        var m = br.BlockTransform;
        var sb = new System.Text.StringBuilder(r.Sha16);
        for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) sb.Append('|').Append(m[i, j].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    internal static void Forget(Database db, int id) { if (Cache.TryGetValue(db, out var d)) d.Remove(id); }

    /// <summary>원지반 XY 상자(세계) — 없으면 null.</summary>
    internal static StructUnits.Box2? GroundBox(Database db, out ObjectId gid)
    {
        gid = Commands.ImportGisCommand.FindGroundSurface(db, out _, out int tris);
        if (gid.IsNull || tris <= 0) return null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ts = (CivilDb.TinSurface)tr.GetObject(gid, OpenMode.ForRead);
            var gp = ts.GetGeneralProperties();
            tr.Commit();
            return new StructUnits.Box2(gp.MinimumCoordinateX, gp.MinimumCoordinateY, gp.MaximumCoordinateX, gp.MaximumCoordinateY);
        }
        catch { return null; }
    }

    /// <summary>모형 — 캐시(원본 해시 · 삽입 변환이 같으면) 또는 xref 블록에서 새로 읽음. <paramref name="read"/> = 새로 읽었다.</summary>
    internal static StructModel? ModelOf(Database db, Transaction tr, StructRec r, BlockReference br, System.Func<StructUnits.Box2?> groundOf, out string note, out bool read)
    {
        note = ""; read = false;
        var d = Cache.GetOrCreateValue(db);
        string key = KeyOf(r, br);
        if (d.TryGetValue(r.Id, out var en) && en.Key == key) { note = en.Note; return en.Model; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var soup = ReadSoup(tr, br, out string rn);
        long tRead = sw.ElapsedMilliseconds; sw.Restart();
        var m = StructModel.Build(soup);
        var ground = groundOf(); int cov = ground != null ? m.ExcludeCovering(ground.Value.X0, ground.Value.Y0, ground.Value.X1, ground.Value.Y1) : 0;
        note = $"{rn} · {m.Summary()}" + (cov > 0 ? $" · ⚠원지반 절반 이상 덮는 부재 {cov} 뺌(지형?)" : "") + $" · 읽기 {tRead} ms · 짓기 {sw.ElapsedMilliseconds} ms";
        d[r.Id] = new Entry { Key = key, Model = m, Note = note };
        read = true;
        return m;
    }

    /// <summary>기록된 구조물 모두 — 삽입이 없어진 것(전체 초기화 · 사람이 지움)은 기록에서 뺀다 · 원본을 못 찾은(xref 안 풀림) 것은 «⚠ 원본 없음»으로 건너뛴다(지우지도 그리지도 않음).</summary>
    internal static List<Loaded> LoadAll(Database db, System.Text.StringBuilder? log, out List<string> warn)
    {
        warn = new List<string>();
        var res = new List<Loaded>();
        var orphan = new List<ObjectId>();
        try
        {
            StructUnits.Box2? g0 = null; bool gDone = false;
            StructUnits.Box2? Ground() { if (!gDone) { g0 = GroundBox(db, out _); gDone = true; } return g0; }   // 새로 읽을 때만(원지반 찾기가 면을 다 훑는다)
            using var tr = db.TransactionManager.StartTransaction();
            if (!TryLoad(db, tr, out var head, out var recs, out string why)) { warn.Add("⚠" + why); tr.Commit(); return res; }
            if (recs.Count == 0) { tr.Commit(); return res; }
            var keep = new List<StructRec>(); bool dropped = false;
            foreach (var r in recs)
            {
                BlockReference? br = null;
                try
                {
                    if (r.XrefHandle.Length > 0 && db.TryGetObjectId(new Handle(System.Convert.ToInt64(r.XrefHandle, 16)), out var iid) && !iid.IsErased)
                        br = tr.GetObject(iid, OpenMode.ForRead) as BlockReference;
                }
                catch { }
                if (br == null)
                {
                    dropped = true; int gone = StructDraw.EraseTagged(db, tr, (_, _, sid, _) => sid == r.Id);
                    log?.AppendLine($"  [구조물] {r.Name} — xref 삽입이 없어 기록에서 뺌(그림 {gone} 지움)"); Forget(db, r.Id);
                    // ★[검토 낮음] 아무도 안 쓰는 우리 xref 정의도 뗀다(남으면 [구조물 지우기]가 못 찾는다)
                    try
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        if (bt.Has(BlockBase + r.Id) && tr.GetObject(bt[BlockBase + r.Id], OpenMode.ForRead) is BlockTableRecord ob && ob.IsFromExternalReference && ob.GetBlockReferenceIds(true, false).Count == 0) orphan.Add(ob.ObjectId);
                    }
                    catch { }
                    continue;
                }
                keep.Add(r);
                var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null || (btr.IsFromExternalReference && btr.XrefStatus != XrefStatus.Resolved)) { warn.Add($"⚠원본 없음: {r.Name}"); continue; }
                var m = ModelOf(db, tr, r, br, Ground, out string note, out bool read);
                if (m == null) continue;
                if (read) log?.AppendLine($"  [구조물] {r.Name} 읽음 — {note}");
                res.Add(new Loaded { Rec = r, Model = m, Insert = br.ObjectId, Btr = br.BlockTableRecord, Note = note });
            }
            if (dropped) Save(db, tr, head, keep);
            tr.Commit();
        }
        catch (System.Exception ex) { warn.Add("⚠구조물을 못 읽음 — " + ex.Message); }
        foreach (var ob in orphan) try { db.DetachXref(ob); } catch { }
        return res;
    }

    /// <summary>★[D10] 원본이 바뀌었나(크기 · 시각 → SHA-256) — 바뀐 것은 xref를 다시 싣고(트랜잭션 밖) 기록을 고친다. 명령 머리에서만. 돌려줌 = 알림 줄들.</summary>
    internal static List<string> ReloadChanged(Database db, System.Text.StringBuilder? log, HashSet<int>? missing = null)
    {
        var msgs = new List<string>();
        var reload = new ObjectIdCollection(); var changed = new List<(int Id, string Sha, double Size, string Mt, string Path)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (TryLoad(db, tr, out _, out var recs, out _))
                foreach (var r in recs)
                {
                    ObjectId btrId = ObjectId.Null;
                    try { if (db.TryGetObjectId(new Handle(System.Convert.ToInt64(r.XrefHandle, 16)), out var iid) && !iid.IsErased && tr.GetObject(iid, OpenMode.ForRead) is BlockReference br) btrId = br.BlockTableRecord; } catch { }
                    if (btrId.IsNull) continue;
                    string? p = ResolvePath(db, tr, btrId, r);
                    // ★[v116.5] IFC 원본이 옆 DWG보다 새것이면 옆 DWG를 다시 만든다(아래에서 바뀜으로 잡혀 다시 실림)
                    if (p != null && StructIfc.IsSide(p) && StructIfc.Stale(p))
                    {
                        if (StructIfc.ToDwg(StructIfc.IfcOf(p), out string inote, p) == null) msgs.Add($"⚠IFC가 바뀌었지만 다시 못 바꿈: {r.Name} — {inote}");
                        else log?.AppendLine($"  [구조물] IFC가 바뀌어 옆 DWG를 다시 만듦: {r.Name} · {inote}");
                    }
                    if (p == null && r.PathAbs.Length > 0 && StructIfc.IsSide(r.PathAbs) && File.Exists(StructIfc.IfcOf(r.PathAbs)) && StructIfc.ToDwg(StructIfc.IfcOf(r.PathAbs), out _, r.PathAbs) != null) p = r.PathAbs;   // 옆 DWG를 지웠으면 IFC에서 다시
                    if (p == null) { msgs.Add($"⚠원본 없음: {r.Name}"); missing?.Add(r.Id); continue; }
                    var fi = new FileInfo(p);
                    string mt = MtimeOf(p);
                    if (fi.Length == (long)r.Size && mt == r.Mtime) continue;
                    string sha = Sha16(p);
                    if (sha == r.Sha16) { changed.Add((r.Id, sha, fi.Length, mt, p)); continue; }   // 시각만 바뀜 — 기록만 고침
                    reload.Add(btrId); changed.Add((r.Id, sha, fi.Length, mt, p));
                    msgs.Add($"원본이 바뀌어 새로 읽음: {r.Name}");
                }
            tr.Commit();
        }
        catch (System.Exception ex) { msgs.Add("⚠원본 바뀜 검사 실패 — " + ex.Message); return msgs; }
        if (reload.Count > 0)
            try { db.ReloadXrefs(reload); }
            catch (System.Exception ex)
            {
                // ★[검토 중간] 못 다시 실었으면 기록을 고치지 않는다(옛 형상을 새 해시로 적으면 다음부터 바뀜을 못 알아본다) — 시각만 바뀐 것만 고침
                msgs.RemoveAll(m => m.StartsWith("원본이 바뀌어 새로 읽음", System.StringComparison.Ordinal));
                msgs.Add("⚠xref 다시 싣기 실패 — " + ex.Message);
                using var trC = db.TransactionManager.StartTransaction();
                if (TryLoad(db, trC, out _, out var recsC, out _)) changed.RemoveAll(c => recsC.Any(r => r.Id == c.Id && r.Sha16 != c.Sha));
                trC.Commit();
            }
        if (changed.Count > 0)
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (TryLoad(db, tr, out var head, out var recs, out _))
                {
                    foreach (var c in changed)
                    {
                        var r = recs.FirstOrDefault(x => x.Id == c.Id); if (r == null) continue;
                        r.Sha16 = c.Sha; r.Size = c.Size; r.Mtime = c.Mt;
                        Forget(db, r.Id);
                    }
                    Save(db, tr, head, recs);
                }
                tr.Commit();
            }
            catch { }
        foreach (var m in msgs) log?.AppendLine("  [구조물] " + m);
        return msgs;
    }

    /// <summary>바뀐 원본이 있나(크기 · 시각만 — 해시 안 셈) — 그리는 쪽이 «[다시 반영]» 한 줄을 남길지.</summary>
    internal static bool LooksChanged(Database db, Transaction tr, StructRec r, ObjectId btrId)
    {
        try
        {
            string? p = ResolvePath(db, tr, btrId, r);
            if (p == null) return false;
            if (StructIfc.IsSide(p) && StructIfc.Stale(p)) return true;   // ★[v116.5] IFC가 옆 DWG보다 새것
            return new FileInfo(p).Length != (long)r.Size || MtimeOf(p) != r.Mtime;
        }
        catch { return false; }
    }
}
