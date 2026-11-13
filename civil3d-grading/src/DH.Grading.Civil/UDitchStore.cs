using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[배수지 U형 측구 1단계 MVP · 명세서 §7] 도면 쪽 일 — 기록(NOD DH_GRADING/UDITCH · JSON 조각) · 설정 파일 · 부지 대표점 · 선 읽기 · 지표면 · 솔리드 짓기 · 보고서 · [3D 보기] 머리.
/// 셈은 모두 Core <see cref="UDitch"/>(하네스 S260 UD) — 여기서는 읽고 쓰기만.</summary>
internal static class UDitchStore
{
    internal const string Layer = "DH-U형측구", LineLayer = "DH-U형측구-선", App = "DH_UDITCH";
    const string DictName = "DH_GRADING", RecName = "UDITCH";
    internal const string SettingsFile = "UDitch.settings.json";
    const int Chunk = 200;

    // ══════════════════════════════ 설정 ══════════════════════════════

    /// <summary>설정 — %APPDATA%\DH.Grading\UDitch.settings.json(설치로 안 덮임) → 번들 Contents\UDitch.settings.json → 기본값.</summary>
    internal static UDitch.Settings LoadSettings(out string from, out string why)
    {
        why = ""; from = "기본값";
        string app = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "DH.Grading", SettingsFile);
        string dll = "";
        try { dll = Path.Combine(Path.GetDirectoryName(typeof(UDitchStore).Assembly.Location) ?? "", SettingsFile); } catch { }
        foreach (string p in new[] { app, dll })
            try
            {
                if (p.Length == 0 || !File.Exists(p)) continue;
                var s = UDitch.ParseSettings(File.ReadAllText(p), out why);
                from = p;
                return s;
            }
            catch (System.Exception ex) { why = $"설정 파일 못 읽음({p}) — {ex.Message}"; }
        return new UDitch.Settings();
    }

    // ══════════════════════════════ 기록 ══════════════════════════════

    internal static bool TryLoad(Database db, Transaction tr, out UDitch.Record rec, out string why)
    {
        rec = new UDitch.Record(); why = "";
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return true;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!dict.Contains(RecName)) return true;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(RecName), OpenMode.ForRead);
            if (xr.Data == null) return true;
            var sb = new System.Text.StringBuilder();
            foreach (TypedValue tv in xr.Data.AsArray()) if (tv.TypeCode == (short)DxfCode.Text) sb.Append(tv.Value as string ?? "");
            return sb.Length == 0 || UDitch.TryParse(sb.ToString(), out rec, out why);
        }
        catch (System.Exception ex) { why = "U형 측구 기록을 못 읽음 — " + ex.Message; return false; }
    }

    /// <summary>JSON을 200자 조각으로(긴 글 한 칸이 잘리지 않게). 측구가 없고 강우도 없으면 기록을 지운다.</summary>
    internal static void Save(Database db, Transaction tr, UDitch.Record rec)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dict;
        if (nod.Contains(DictName)) dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
        else { dict = new DBDictionary(); nod.SetAt(DictName, dict); tr.AddNewlyCreatedDBObject(dict, true); }
        if (dict.Contains(RecName)) dict.Remove(RecName);
        if (rec.Ditches.Count == 0 && !rec.Rain.Has && rec.Rain.Source.Length == 0) return;
        string js = UDitch.ToJson(rec);
        var vals = new List<TypedValue>();
        for (int i = 0; i < js.Length; i += Chunk) vals.Add(new TypedValue((int)DxfCode.Text, js.Substring(i, System.Math.Min(Chunk, js.Length - i))));
        using var rb = new ResultBuffer(vals.ToArray());
        var xr = new Xrecord { Data = rb };
        dict.SetAt(RecName, xr);
        tr.AddNewlyCreatedDBObject(xr, true);
    }

    // ══════════════════════════════ M01 부지 대표점 ══════════════════════════════

    /// <summary>계획부지 경계(번들 구역들) 넓이 무게중심 → 없으면 측구 선 점 평균 → 없으면 빈 값. 경위도 = <see cref="UDitch.LonLat"/>.</summary>
    internal static UDitch.Site SiteOf(Database db, int epsg, IEnumerable<UDitch.Ditch> ditches)
    {
        var st = new UDitch.Site { Epsg = epsg };
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            tr.Commit();
            var rings = (regs ?? new List<GradingBundle>()).Where(b => b?.Boundary != null && b.Boundary.Count >= 3)
                                                             .Select(b => (IReadOnlyList<(double X, double Y)>)b.Boundary.Select(p => (p.X, p.Y)).ToList()).ToList();
            var c = UDitch.Centroid(rings);
            if (c != null) { st.X = c.Value.X; st.Y = c.Value.Y; st.AreaM2 = c.Value.Area; st.Source = $"계획부지 경계 {rings.Count}개 무게중심"; }
        }
        catch { }
        if (!double.IsFinite(st.X))
        {
            var pts = ditches.SelectMany(d => d.Pts).Where(p => p.Length >= 2 && double.IsFinite(p[0]) && double.IsFinite(p[1])).ToList();
            if (pts.Count > 0) { st.X = pts.Average(p => p[0]); st.Y = pts.Average(p => p[1]); st.Source = "측구 선 가운데(계획부지 없음)"; }
            else st.Source = "계획부지 · 측구 선 없음";
        }
        var ll = UDitch.LonLat(st.X, st.Y, epsg);
        if (ll != null) { st.Lon = ll.Value.Lon; st.Lat = ll.Value.Lat; }
        return st;
    }

    // ══════════════════════════════ 지표면 · 선 ══════════════════════════════

    /// <summary>바닥고 기준 지표면 — 최종지표면_DH → 정지면_DH(이름만 · 메모리 합침 안 함) · 없으면 Null.</summary>
    internal static (ObjectId Id, string Name) FindSurface(Database db, Transaction tr)
    {
        ObjectId fin = ObjectId.Null, plan = ObjectId.Null; string fn = "", pn = "";
        try
        {
            foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db).GetSurfaceIds())
                try
                {
                    if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface s) continue;
                    if (s.Name == FinalSurfaceBuilder.Name) { fin = sid; fn = s.Name; }
                    else if (Commands.SectionCommand.IsBase(s.Name, Commands.WallCompositeCommand.PlanName) && plan.IsNull) { plan = sid; pn = s.Name; }
                }
                catch { }
        }
        catch { }
        return !fin.IsNull ? (fin, fn) : (plan, pn);
    }

    static double GroundAt(Transaction tr, ObjectId sid, double x, double y)
    {
        if (sid.IsNull || sid.IsErased) return double.NaN;
        try { return tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts ? ts.FindElevationAtXY(x, y) : double.NaN; }
        catch { return double.NaN; }   // 면 밖
    }

    /// <summary>선 → 평면 점(WCS) — 폴리선(호는 <paramref name="arcStep"/> m마다) · 3D 폴리선 · 선. <paramref name="z3"/> = 3D 폴리선의 Z(지표면이 없을 때 대신 씀).</summary>
    internal static bool ReadLine(Transaction tr, ObjectId id, double arcStep, out List<double[]> pts, out bool z3, out string why)
    {
        pts = new(); z3 = false; why = "";
        var ent = tr.GetObject(id, OpenMode.ForRead);
        switch (ent)
        {
            case Polyline pl:
            {
                int nv = pl.NumberOfVertices;
                for (int i = 0; i < nv; i++)
                {
                    var p = pl.GetPoint3dAt(i);
                    pts.Add(new[] { p.X, p.Y, double.NaN });
                    if (i + 1 < nv && System.Math.Abs(pl.GetBulgeAt(i)) > 1e-9)
                    {
                        double len = pl.GetDistanceAtParameter(i + 1) - pl.GetDistanceAtParameter(i);
                        int k = System.Math.Max(2, (int)System.Math.Ceiling(len / arcStep));
                        for (int j = 1; j < k; j++) { var q = pl.GetPointAtParameter(i + (double)j / k); pts.Add(new[] { q.X, q.Y, double.NaN }); }
                    }
                }
                if (pl.Closed) why = "닫힌 폴리선 — 첫 점 → 끝 점으로만 씀(닫는 변 뺌)";
                break;
            }
            case Polyline3d p3:
                foreach (ObjectId vid in p3)
                {
                    var v = (PolylineVertex3d)tr.GetObject(vid, OpenMode.ForRead);
                    if (v.VertexType == Vertex3dType.ControlVertex) continue;
                    pts.Add(new[] { v.Position.X, v.Position.Y, v.Position.Z });
                }
                z3 = true;
                break;
            case Line ln:
                pts.Add(new[] { ln.StartPoint.X, ln.StartPoint.Y, ln.StartPoint.Z });
                pts.Add(new[] { ln.EndPoint.X, ln.EndPoint.Y, ln.EndPoint.Z });
                z3 = true;
                break;
            default:
                why = "폴리선 · 3D 폴리선 · 선만 됩니다";
                return false;
        }
        pts = UDitch.CleanPts(pts);
        if (pts.Count < 2) { why = "점이 2개 미만"; return false; }
        return true;
    }

    /// <summary>측구 점의 지표면 z를 다시 읽음(지표면 → 3D 선 Z → NaN) · 지표면 이름 ·
    /// ★[v117.16 · JACK 1112 «애초에 폴리선이 단차 있게 만든 부지거든?»] 선 따라 지표 표본(<see cref="UDitch.FillSamples"/> — 마디 사이 단차를 찾으려고).</summary>
    internal static void FillGround(Database db, Transaction tr, UDitch.Ditch d, List<double[]> pts, bool z3, UDitch.Settings s)
    {
        var (sid, name) = FindSurface(db, tr);
        int miss = 0;
        foreach (var p in pts)
        {
            double g = GroundAt(tr, sid, p[0], p[1]);
            if (!double.IsFinite(g)) { g = z3 ? p[2] : double.NaN; miss++; }
            p[2] = g;
        }
        UDitch.SetLinePts(d, pts);   // ★[설치 전 검토 1] [방향 바꾸기]면 그린 차례를 뒤집어 씀
        CivilDb.TinSurface? ts = null;
        try { if (!sid.IsNull && !sid.IsErased) ts = tr.GetObject(sid, OpenMode.ForRead) as CivilDb.TinSurface; } catch { }
        // ★[v117.16 검토 LOW] 면 네모 밖은 묻지 않음(표본마다 예외 나지 않게) — 네모 안 빈 곳은 그대로 예외 → NaN
        double bx0 = double.NegativeInfinity, by0 = double.NegativeInfinity, bx1 = double.PositiveInfinity, by1 = double.PositiveInfinity;
        if (ts != null)
            try { var gp = ts.GetGeneralProperties(); bx0 = gp.MinimumCoordinateX; bx1 = gp.MaximumCoordinateX; by0 = gp.MinimumCoordinateY; by1 = gp.MaximumCoordinateY; } catch { }
        UDitch.FillSamples(d, (x, y) =>
        {
            if (ts == null || x < bx0 || x > bx1 || y < by0 || y > by1) return double.NaN;
            try { return ts.FindElevationAtXY(x, y); } catch { return double.NaN; }   // 면 밖
        }, s);
        d.Surface = sid.IsNull ? (z3 ? "(3D 선 Z)" : "(없음)") : name + (miss > 0 ? $" · 면 밖 {miss}점" : "");
    }

    /// <summary>원 선이 살아 있으면 점 · 지표면을 다시 읽음(선을 고친 뒤 [모델 생성]). 못 읽으면 저장된 점 그대로.</summary>
    internal static void Refresh(Database db, Transaction tr, UDitch.Ditch d, UDitch.Settings s)
    {
        try
        {
            if (d.LineHandle.Length == 0 || !db.TryGetObjectId(new Handle(System.Convert.ToInt64(d.LineHandle, 16)), out var id) || id.IsErased) return;
            if (!ReadLine(tr, id, s.ArcStep, out var pts, out bool z3, out _)) return;
            FillGround(db, tr, d, pts, z3, s);
            UDitch.ApplyAuto(d, s);
        }
        catch { }
    }

    // ══════════════════════════════ M06 솔리드 ══════════════════════════════

    /// <summary>우리가 지은 것(레이어 + XData) 지움 — <paramref name="id"/>가 빈 글이면 모두.</summary>
    internal static int Erase(Database db, Transaction tr, string id)
    {
        int n = 0;
        bool locked = SetLocked(db, tr, false);   // ★[설치 전 검토 3] 잠긴 레이어면 풀고 지운 뒤 되돌림(못 지운 채 새로 지어 겹치지 않게)
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId eid in ms)
        {
            try
            {
                if (eid.IsErased || tr.GetObject(eid, OpenMode.ForRead) is not Entity e || e.Layer != Layer) continue;
                using var rb = e.GetXDataForApplication(App);
                var v = rb?.AsArray();
                if (v == null || v.Length < 2) continue;
                if (id.Length > 0 && (v[1].Value as string) != id) continue;
                e.UpgradeOpen(); e.Erase(); n++;
            }
            catch { }
        }
        if (locked) SetLocked(db, tr, true);
        return n;
    }

    /// <summary>측구 레이어 잠금을 바꿈 — 돌려줌 = 바꾸기 전 잠겨 있었나(레이어 없음 = 거짓).</summary>
    static bool SetLocked(Database db, Transaction tr, bool locked)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(Layer)) return false;
        var l = (LayerTableRecord)tr.GetObject(lt[Layer], OpenMode.ForRead);
        bool was = l.IsLocked;
        if (was != locked) { l.UpgradeOpen(); l.IsLocked = locked; }
        return was;
    }

    internal sealed class BuildStats
    {
        public int Solids, Meshes, Erased, Skipped;
        public string FirstFail = "";
        public double BodyVol, FootVol, Length;
        /// <summary>★[v117.16] 낙차 · 집수정 · 관로(개수 · 관 길이) · 낙차벽 + 집수정 콘크리트 · 스틸 그레이팅 길이.</summary>
        public int Drops, Basins, Pipes;
        public double PipeLen, ExtraVol, GrateLen;
    }

    /// <summary>모든 측구를 다시 짓는다(옛것 지움 → 원 선 다시 읽기 → 본체 · 기초 솔리드) · 색 = 수리 판정(부족 빨강 · 주의 노랑 · 그 밖 레이어 색).
    /// 수리 결과와 무관하게 짓는다(§9 모델과 수리는 따로).</summary>
    internal static BuildStats Build(Database db, UDitch.Record rec, UDitch.Settings s, System.Text.StringBuilder log)
    {
        var st = new BuildStats();
        using var tr = db.TransactionManager.StartTransaction();
        bool locked = SetLocked(db, tr, false);   // ★[설치 전 검토 3] 잠긴 레이어 — 짓는 동안 풀고 끝에 되돌림
        st.Erased = Erase(db, tr, "");
        var lay = GradingBuilder.EnsureLayer(db, tr, Layer, 253);
        GradingBuilder.EnsureRegApp(db, tr, App);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        foreach (var d in rec.Ditches)
        {
            // ★[설치 전 검토 2] 측구마다 따로 — 하나가 못 지어지면 그 측구만 «못 지음»(지은 조각은 지움) · 나머지는 짓는다
            var built = new List<Entity>();
            int nSol = 0, nMesh = 0;
            try
            {
                Refresh(db, tr, d, s);
                d.Solids.Clear();
                var h = UDitch.Check(d, rec.Rain, s);
                var sols = UDitch.Meshes(d, s, out string why);
                if (sols.Count == 0) { st.Skipped++; if (st.FirstFail.Length == 0) st.FirstFail = $"{d.Id}: {why}"; log.AppendLine($"  {d.Id}: 못 지음 — {why}"); continue; }
                short color = h.Verdict == UDitch.Verdict.Short ? (short)1 : h.Verdict == UDitch.Verdict.Caution ? (short)2 : (short)256;
                foreach (var sol in sols)
                {
                    var o = sol.V[0];
                    var verts = new Point3dCollection();
                    foreach (var v in sol.V) verts.Add(new Point3d(v.X - o.X, v.Y - o.Y, v.Z - o.Z));   // 원점 가까이(대좌표 ACIS 오류 피함 — 옹벽 솔리드와 같은 법)
                    var fc = new Int32Collection();
                    foreach (var f in sol.F) { fc.Add(f.Length); foreach (int k in f) fc.Add(k); }
                    var mesh = new SubDMesh();
                    try { mesh.SetSubDMesh(verts, fc, 0); } catch { mesh.Dispose(); throw; }
                    mesh.LayerId = lay;
                    ms.AppendEntity(mesh); tr.AddNewlyCreatedDBObject(mesh, true);
                    built.Add(mesh);
                    var move = Matrix3d.Displacement(new Vector3d(o.X, o.Y, o.Z));
                    Entity made = mesh;
                    Solid3d? solid = null;
                    try
                    {
                        solid = mesh.ConvertToSolid(false, false);
                        bool live;
                        try { live = solid.MassProperties.Volume > 1e-9; } catch { live = false; }
                        if (live)
                        {
                            solid.LayerId = lay;
                            ms.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true);
                            built.Add(solid);
                            mesh.Erase();
                            made = solid; solid = null;
                            nSol++;
                        }
                        else if (st.FirstFail.Length == 0) st.FirstFail = $"{d.Id} {sol.Part}: 빈 솔리드(메시로 남김)";
                    }
                    catch (System.Exception ex) { if (st.FirstFail.Length == 0) st.FirstFail = $"{d.Id} {sol.Part}: {ex.Message}(메시로 남김)"; }
                    finally { solid?.Dispose(); }
                    if (made is SubDMesh) nMesh++;
                    made.TransformBy(move);
                    made.ColorIndex = sol.Part is "그레이팅" or "집수정 덮개" ? (short)8 : color;   // ★[v117.16] 스틸 그레이팅 = 회색
                    using (var xd = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, d.Id),
                                                     new TypedValue((int)DxfCode.ExtendedDataAsciiString, sol.Part), new TypedValue((int)DxfCode.ExtendedDataAsciiString, h.Status)))
                        made.XData = xd;
                    d.Solids.Add(made.Handle.ToString());
                }
                var q = UDitch.Qty(d, s);
                var xq = UDitch.Extras(d, s);   // ★[v117.16 · JACK 1112] 낙차 · 집수정 · 관로 · 그레이팅
                st.Solids += nSol; st.Meshes += nMesh;
                st.BodyVol += q.BodyVol; st.FootVol += q.FootVol; st.Length += q.Length;
                st.Drops += xq.Drops; st.Basins += xq.Basins; st.Pipes += xq.Pipes; st.PipeLen += xq.PipeLen; st.ExtraVol += xq.Concrete; st.GrateLen += xq.GrateLen;
                var ly = UDitch.LayoutOf(d, s);
                foreach (var ln in ly.Log) log.AppendLine($"    {d.Id} {ln}");
                foreach (var w in ly.Warn) log.AppendLine($"    {d.Id} ⚠{w}");
                log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0}: U형 {1} · 길이 {2:F2} m · 경사 {3:0.###} % · 수리 {4} · 콘크리트 {5:F3} ㎥{6}{7} · {8}",
                               d.Id, d.Size.Label, q.Length, h.SlopePct, h.Status, q.BodyVol + q.FootVol + xq.Concrete,
                               ly.Follow ? $" · 지표 따라감 구간 {ly.Segs.Count} · 낙차 {xq.Drops} · 집수정 {xq.Basins} · 관 {xq.PipeLen:F2} m" : " · 한 경사",
                               d.Grating ? $" · 그레이팅 {xq.GrateLen:F2} m" : "",
                               string.Join("/", sols.GroupBy(x => x.Part).Select(gp => $"{gp.Key} {gp.Count()}개 부피 {gp.Sum(x => x.Volume):F3}"))));
            }
            catch (System.Exception ex)
            {
                foreach (var e in built) try { if (!e.IsErased) e.Erase(); } catch { }
                d.Solids.Clear();
                st.Skipped++;
                if (st.FirstFail.Length == 0) st.FirstFail = $"{d.Id}: {ex.Message}";
                log.AppendLine($"  {d.Id}: 못 지음 — {ex.Message}");
            }
        }
        if (locked) SetLocked(db, tr, true);
        rec.Updated = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        Save(db, tr, rec);
        tr.Commit();
        return st;
    }

    // ══════════════════════════════ M08 보고서 ══════════════════════════════

    /// <summary>도면 옆(저장 안 한 도면 = 내 문서) «도면이름_U형측구.txt» · «.json»(기록 그대로). 돌려줌 = txt 경로(못 쓰면 빈 글 + 까닭).</summary>
    internal static string WriteReport(Database db, string docName, UDitch.Record rec, UDitch.Settings s, out string why)
    {
        why = "";
        try
        {
            string f = db.Filename ?? "";
            string dir = f.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase) && File.Exists(f) ? Path.GetDirectoryName(f) ?? "" : "";
            if (dir.Length == 0) dir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            string stem = Path.GetFileNameWithoutExtension(docName);
            if (string.IsNullOrWhiteSpace(stem)) stem = "도면";
            string txt = Path.Combine(dir, stem + "_U형측구.txt");
            File.WriteAllText(txt, UDitch.Report(rec, s, Path.GetFileName(docName), "DH.Grading " + GradingSettings.Version), new System.Text.UTF8Encoding(true));
            File.WriteAllText(Path.ChangeExtension(txt, ".json"), UDitch.ToJson(rec), new System.Text.UTF8Encoding(false));
            return txt;
        }
        catch (System.Exception ex) { why = "보고서를 못 씀 — " + ex.Message; return ""; }
    }

    // ══════════════════════════════ [3D 보기] ══════════════════════════════

    /// <summary>[3D 보기] 머리 «uditch» — 측구마다 메시(본체 + 기초 · 장면 좌표) · 풍선 줄. 기록이 없으면 null(옛 장면 바이트 그대로). <b>읽기만</b>.</summary>
    internal static List<object>? Web3DHead(Database db, double ox, double oy, Web3DScene sc, System.Text.StringBuilder log)
    {
        UDitch.Record rec;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (!TryLoad(db, tr, out rec, out string why)) { tr.Commit(); log.AppendLine("  U형 측구: 기록 못 읽음 — " + why); return null; }
            tr.Commit();
        }
        if (rec.Ditches.Count == 0) return null;
        var s = LoadSettings(out _, out _);
        var o = new List<object>();
        int tris = 0, nb = 0, np = 0;
        foreach (var d in rec.Ditches)
        {
            var (pos, idx) = UDitch.SceneMesh(d, s, ox, oy, out var parts);
            if (idx.Length == 0) { log.AppendLine($"  U형 측구: {d.Id} — 못 지음(건너뜀)"); continue; }
            var h = UDitch.Check(d, rec.Rain, s);
            tris += idx.Length / 3;
            // ★[v117.16 · JACK 1112] 집수정(위 · 아래 · inner = 안치수) · 관로(다리마다 한 칸)(뷰어가 그림 — 장면 좌표 = 도면 − ox, oy · z 그대로 · 관 d = 바깥 지름) · 스틸 그레이팅 여부(뷰어가 윗면에 그림 — 메시에는 판 없음) ·
            //   parts = 메시 덩어리(부분 · 구간 · 첫 꼭짓점 · 꼭짓점 수 — 구간이 여럿이면 덩어리가 여럿)
            var (bs, ps) = UDitch.SceneParts(d, s, ox, oy, rec.Rain);
            nb += bs.Count; np += ps.Count;
            static List<object> Rows(List<(string K, string V)> rs) => rs.Select(r => (object)new[] { r.K, r.V }).ToList();
            o.Add(new Dictionary<string, object>
            {
                ["name"] = d.Id, ["title"] = $"{d.Id} U형 측구", ["status"] = h.Status,
                ["rows"] = UDitch.InfoRows(d, rec.Rain, s).Select(r => (object)new[] { r.K, r.V }).ToList(),
                ["pos"] = sc.F32(pos), ["idx"] = sc.U32(idx),
                ["parts"] = parts.Select(q => (object)new Dictionary<string, object> { ["part"] = q.Part, ["seg"] = q.Seg, ["v0"] = q.V0, ["n"] = q.N }).ToList(),
                ["basins"] = bs.Select(b => (object)new Dictionary<string, object>
                {
                    ["x"] = Web3DScene.Num(b.V[0], 3), ["y"] = Web3DScene.Num(b.V[1], 3), ["zTop"] = Web3DScene.Num(b.V[2], 3), ["zBottom"] = Web3DScene.Num(b.V[3], 3),
                    ["w"] = Web3DScene.Num(b.V[4], 3), ["l"] = Web3DScene.Num(b.V[5], 3), ["rot"] = Web3DScene.Num(b.V[6], 5), ["inner"] = Web3DScene.Num(b.V[7], 3),
                    ["name"] = b.Name, ["title"] = b.Name, ["rows"] = Rows(b.Rows),
                }).ToList(),
                ["pipes"] = ps.Select(p => (object)new Dictionary<string, object>
                {
                    ["x1"] = Web3DScene.Num(p.V[0], 3), ["y1"] = Web3DScene.Num(p.V[1], 3), ["z1"] = Web3DScene.Num(p.V[2], 3),
                    ["x2"] = Web3DScene.Num(p.V[3], 3), ["y2"] = Web3DScene.Num(p.V[4], 3), ["z2"] = Web3DScene.Num(p.V[5], 3), ["d"] = Web3DScene.Num(p.V[6], 3),
                    ["name"] = p.Name, ["title"] = p.Name, ["rows"] = Rows(p.Rows),
                }).ToList(),
                ["grating"] = d.Grating,
            });
        }
        log.AppendLine($"  U형 측구: {o.Count}개 · tris {tris:N0} · 집수정 {nb} · 관로 {np}");
        return o.Count > 0 ? o : null;
    }
}
