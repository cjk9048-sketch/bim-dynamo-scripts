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
    /// <summary>★[JACK 1124 «모형 뷰에서는 -----U-----u----- 이런 식으로 선형만»] 측구 선 선종류 — «—— U —— U ——»(m · 글 «U» 높이 1 m · DH-한글).</summary>
    internal const string Ltype = "DH_U측구";
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

    /// <summary>선 → 평면 점(WCS) — 폴리선(호는 <paramref name="arcStep"/> m마다) · 3D 폴리선 · 선. <paramref name="z3"/> = 3D 폴리선의 Z(지표면이 없을 때 대신 씀).
    /// ★[JACK 1123 «닫힌 폴리선 → 출구에서 두 갈래»] <paramref name="closed"/> = 닫힌 선(닫힘 표시 · 첫 점 = 끝 점) — 점은 둘레 전체(닫는 변 · 그 호 포함 · 끝 점 = 첫 점).</summary>
    internal static bool ReadLine(Transaction tr, ObjectId id, double arcStep, out List<double[]> pts, out bool z3, out bool closed, out string why)
    {
        pts = new(); z3 = false; closed = false; why = "";
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
                    bool last = i + 1 >= nv;
                    if ((!last || pl.Closed) && System.Math.Abs(pl.GetBulgeAt(i)) > 1e-9)
                    {
                        double len = (last ? pl.Length : pl.GetDistanceAtParameter(i + 1)) - pl.GetDistanceAtParameter(i);
                        int k = System.Math.Max(2, (int)System.Math.Ceiling(len / arcStep));
                        for (int j = 1; j < k; j++) { var q = pl.GetPointAtParameter(i + (double)j / k); pts.Add(new[] { q.X, q.Y, double.NaN }); }
                    }
                }
                if (pl.Closed && pts.Count >= 3) { pts.Add(new[] { pts[0][0], pts[0][1], double.NaN }); closed = true; }
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
                if (p3.Closed && pts.Count >= 3) { pts.Add((double[])pts[0].Clone()); closed = true; }
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
        // 닫힘 표시 없이 첫 점 = 끝 점(1 mm 안)인 선도 닫힌 선
        if (!closed && pts.Count >= 4 && System.Math.Abs(pts[0][0] - pts[^1][0]) < 1e-3 && System.Math.Abs(pts[0][1] - pts[^1][1]) < 1e-3) closed = true;
        return true;
    }

    /// <summary>지표면 높이 묻기(면 네모 밖 · 빈 곳 = NaN) — 표본 · 닫힌 선 나누기가 같이 씀.</summary>
    static System.Func<double, double, double> GroundFunc(Transaction tr, ObjectId sid)
    {
        CivilDb.TinSurface? ts = null;
        try { if (!sid.IsNull && !sid.IsErased) ts = tr.GetObject(sid, OpenMode.ForRead) as CivilDb.TinSurface; } catch { }
        double bx0 = double.NegativeInfinity, by0 = double.NegativeInfinity, bx1 = double.PositiveInfinity, by1 = double.PositiveInfinity;
        if (ts != null)
            try { var gp = ts.GetGeneralProperties(); bx0 = gp.MinimumCoordinateX; bx1 = gp.MaximumCoordinateX; by0 = gp.MinimumCoordinateY; by1 = gp.MaximumCoordinateY; } catch { }
        return (x, y) =>
        {
            if (ts == null || x < bx0 || x > bx1 || y < by0 || y > by1) return double.NaN;
            try { return ts.FindElevationAtXY(x, y); } catch { return double.NaN; }   // 면 밖
        };
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
        // ★[v117.16 검토 LOW] 면 네모 밖은 묻지 않음(표본마다 예외 나지 않게) — 네모 안 빈 곳은 그대로 예외 → NaN
        UDitch.FillSamples(d, GroundFunc(tr, sid), s);
        d.Surface = sid.IsNull ? (z3 ? "(3D 선 Z)" : "(없음)") : name + (miss > 0 ? $" · 면 밖 {miss}점" : "");
    }

    /// <summary>★[JACK 1123] 원 선 하나(핸들)를 다시 읽어 그 선의 측구를 맞춤(선을 고친 뒤 [모델 생성] · [지표면 맞춤] · 다시 고르기).
    /// 닫힌 선 = 두 갈래(<see cref="UDitch.SplitLoop"/> · 없으면 만듦 · 옛 «한 변 빠진» 측구도 바뀜 · 규격 · 덮개 · 집수 그대로) · 열린 선 = 하나(갈래였으면 합침).
    /// 원 선이 없거나 못 읽으면 저장된 점 그대로. 돌려줌 = 알림(빈 글 = 바뀐 꼴 없음).</summary>
    internal static string RefreshLine(Database db, Transaction tr, UDitch.Record rec, string handle, UDitch.Settings s)
    {
        var grp = rec.Ditches.Where(d => d.LineHandle == handle).ToList();
        if (grp.Count == 0 || handle.Length == 0) return "";
        try
        {
            if (!db.TryGetObjectId(new Handle(System.Convert.ToInt64(handle, 16)), out var id) || id.IsErased) return "";
            if (!ReadLine(tr, id, s.ArcStep, out var pts, out bool z3, out bool closed, out _)) return "";
            if (!closed)
            {
                var d = grp[0];
                string note = "";
                if (d.Loop > 0 || grp.Count > 1)   // 닫힌 선을 다시 열었음 → 하나로
                {
                    foreach (var o in grp.Skip(1)) rec.Ditches.Remove(o);
                    if (d.Loop > 0) d.Id = UDitch.LoopBase(d.Id);
                    d.Loop = 0; d.LoopNote = "";
                    note = $"{d.Id} — 선이 열림 → 측구 하나로";
                }
                FillGround(db, tr, d, pts, z3, s);
                UDitch.ApplyAuto(d, s);
                return note;
            }
            // 닫힌 선 → 두 갈래(시작 = 가장 높은 곳 → 양쪽 → 출구 = 가장 낮은 곳)
            //   ★[검토 M4] 나누기가 된 뒤에만 기록을 바꿈(못 나누면 옛 측구 그대로 — 점 없는 갈래가 저장되지 않게)
            var a = grp.FirstOrDefault(x => x.Loop == 1) ?? grp[0];
            var (sid, name) = FindSurface(db, tr);
            var gf = GroundFunc(tr, sid);
            int miss = 0;
            foreach (var p in pts)
            {
                double g = gf(p[0], p[1]);
                if (!double.IsFinite(g)) { g = z3 ? p[2] : double.NaN; miss++; }
                p[2] = g;
            }
            var z = a.Size;
            double snap = System.Math.Max(System.Math.Max(s.BasinInner, z.B + 2 * s.BasinMargin) + 2 * s.BasinWall, z.B + 2 * z.Tw + (z.Tf > 0 ? 2 * z.Pf : 0));
            var sp = UDitch.SplitLoop(pts, gf, s, snap);
            if (sp == null || sp.A.Count < 2 || sp.B.Count < 2) return "닫힌 선 점이 모자람 → 선 확인";
            string msg = "";
            if (a.Loop != 1)
            {
                msg = $"{a.Id} → {UDitch.LoopId(a.Id, 1)} · {UDitch.LoopId(a.Id, 2)}({UDitch.LoopText})"
                      + (a.InvertAuto ? "" : " · 손 바닥고 — 두 갈래 확인");   // ★[검토 L1] 옛 손 바닥고는 선 전체 값 → 갈래마다 다시 넣어야 함
                a.Id = UDitch.LoopId(a.Id, 1); a.Loop = 1;
            }
            var b = grp.FirstOrDefault(x => x.Loop == 2 && !ReferenceEquals(x, a));
            if (b == null) { b = UDitch.LoopSibling(a, 2); rec.Ditches.Insert(rec.Ditches.IndexOf(a) + 1, b); }
            foreach (var o in grp.Where(x => !ReferenceEquals(x, a) && !ReferenceEquals(x, b))) rec.Ditches.Remove(o);
            string surf = sid.IsNull ? (z3 ? "(3D 선 Z)" : "(없음)") : name + (miss > 0 ? $" · 면 밖 {miss}점" : "");
            foreach (var (d, bp) in new[] { (a, sp.A), (b, sp.B) })
            {
                d.Reversed = false; d.LineHandle = handle; d.LoopNote = sp.Note;
                d.Pts = bp;
                UDitch.FillSamples(d, gf, s);
                d.Surface = surf;
                UDitch.ApplyAuto(d, s);
            }
            UDitch.SyncLoop(rec, a, s);   // 규격 · 덮개 · 집수 같게
            return msg;
        }
        catch (System.Exception ex)
        {
            // ★[검토 L10] 초보용 글 · 예외는 진단 로그로
            try { DiagLog.Append($"\n■ U형 측구 선 다시 읽기 실패({handle}) — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
            return "선을 다시 못 읽음 → 선 확인(저장된 점 그대로)";
        }
    }

    /// <summary>모든 원 선을 다시 읽음(선마다 한 번) · 합류 · 교차 · 출구 집수정 다시 셈. 알림 줄은 <paramref name="log"/>에.</summary>
    internal static void RefreshAll(Database db, Transaction tr, UDitch.Record rec, UDitch.Settings s, System.Text.StringBuilder log)
    {
        foreach (string h in rec.Ditches.Select(d => d.LineHandle).Where(h => h.Length > 0).Distinct().ToList())
        {
            string n = RefreshLine(db, tr, rec, h, s);
            if (n.Length > 0) log.AppendLine("  " + n);
        }
        // ★[검토 L4] 집수정 셈이 깨져도 측구는 지음(집수정 없이) · 까닭은 로그로
        try { UDitch.Link(rec, s); }
        catch (System.Exception ex)
        {
            rec.Junctions = new();
            foreach (var d in rec.Ditches) { d.Cuts = new(); d.StartPrev = null; }
            log.AppendLine($"  ⚠집수정 셈 실패 — {ex.GetType().Name}: {ex.Message}");
        }
        LogJunctions(rec, log);
    }

    internal static void LogJunctions(UDitch.Record rec, System.Text.StringBuilder log)
    {
        foreach (var j in rec.Junctions)
        {
            foreach (var l in j.Log) log.AppendLine("    " + l);
            foreach (var w in j.Warn) log.AppendLine("    ⚠" + w);
        }
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
                // ★[JACK 1123] 합류 · 교차 · 출구 집수정(이름 J1 · 넷째 칸 = 이어진 측구 «D-001,D-002»)도 그 측구를 지우면 같이
                if (id.Length > 0 && (v[1].Value as string) != id
                    && !(v.Length > 3 && (v[2].Value as string ?? "").StartsWith("합류 집수정", System.StringComparison.Ordinal) && ((v[3].Value as string) ?? "").Split(',').Contains(id))) continue;
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
        /// <summary>★[JACK 1123] 합류 · 교차 · 출구 집수정 수(<see cref="Basins"/>에도 들어감) · 알림 수.</summary>
        public int Junctions, JunctionWarn;
        /// <summary>★[JACK 1124] 선종류 U를 입힌 선 수(원 선 · 대신 그린 선) · 그중 대신 그린 선.</summary>
        public int Lines, StandIn;
    }

    /// <summary>★[JACK 1124 «측구는 모형 뷰에서는 선형만 · 3D 뷰에서는 실제 모양으로 지표면엔 영향을 주지 않게»] 모든 측구를 다시 셈 —
    /// 옛 솔리드 · 메시(레이어 DH-U형측구 + XData — 옛 [모델 생성])를 지우고 · 원 선을 다시 읽고(닫힌 선 → 두 갈래) · 합류 · 교차 · 출구 집수정을 셈 →
    /// 도면에는 <b>원 선에 선종류 «U»만</b>(<see cref="Ltype"/> · 솔리드 없음 · 지표면 안 건드림) · 수량(콘크리트 · 집수정 · 관)은 셈으로.
    /// 지을 수 없는 측구(꺾임 · 바닥고 모름)는 «못 지음»(<see cref="UDitch.Meshes"/>로 판정 — 도면에는 안 넣음). 3D 보기는 <see cref="Web3DHead"/>가 같은 셈으로 그림.</summary>
    internal static BuildStats Build(Database db, UDitch.Record rec, UDitch.Settings s, System.Text.StringBuilder log)
    {
        var st = new BuildStats();
        using var tr = db.TransactionManager.StartTransaction();
        st.Erased = Erase(db, tr, "");   // 옛 솔리드 · 메시 · 대신 그린 선(모두 레이어 DH-U형측구 + XData)
        RefreshAll(db, tr, rec, s, log);
        var lt = EnsureLinetype(db, tr);
        var marked = new HashSet<string>();
        foreach (var d in rec.Ditches)
        {
            try
            {
                d.Solids.Clear();
                var h = UDitch.Check(d, rec.Rain, s);
                var sols = UDitch.Meshes(d, s, out string why);   // 지을 수 있나 · 부분 부피(도면에는 안 넣음)
                if (sols.Count == 0) { st.Skipped++; if (st.FirstFail.Length == 0) st.FirstFail = $"{d.Id}: {why}"; log.AppendLine($"  {d.Id}: 못 지음 — {why}"); continue; }
                // 도면 = 원 선에 선종류 U(닫힌 선 두 갈래는 선 하나) · 원 선이 없으면 점으로 대신 그림(레이어 DH-U형측구 · 다음 셈에 지움)
                if (d.LineHandle.Length == 0 || !marked.Contains(d.LineHandle))
                {
                    if (MarkLine(db, tr, d.LineHandle, lt)) { marked.Add(d.LineHandle); st.Lines++; }
                    else if (DrawStandIn(db, tr, d, lt, h.Verdict)) { st.Lines++; st.StandIn++; }
                }
                var q = UDitch.Qty(d, s);
                var xq = UDitch.Extras(d, s);   // ★[v117.16 · JACK 1112] 낙차 · 집수정 · 관로 · 그레이팅
                st.BodyVol += q.BodyVol; st.FootVol += q.FootVol; st.Length += q.Length;
                st.Drops += xq.Drops; st.Basins += xq.Basins; st.Pipes += xq.Pipes; st.PipeLen += xq.PipeLen; st.ExtraVol += xq.Concrete; st.GrateLen += xq.GrateLen;
                var ly = UDitch.LayoutOf(d, s);
                foreach (var ln in ly.Log) log.AppendLine($"    {d.Id} {ln}");
                foreach (var w in ly.Warn) log.AppendLine($"    {d.Id} ⚠{w}");
                log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0}: U형 {1} · 길이 {2:F2} m · 경사 {3:0.###} % · 수리 {4} · 콘크리트 {5:F3} ㎥{6}{7}{8}{9} · {10}",
                               d.Id, d.Size.Label, q.Length, h.SlopePct, h.Status, q.BodyVol + q.FootVol + xq.Concrete,
                               ly.Follow ? $" · 지표 따라감 구간 {ly.Segs.Count} · 낙차 {xq.Drops} · 집수정 {xq.Basins} · 관 {xq.PipeLen:F2} m" : " · 한 경사",
                               d.Grating ? $" · 그레이팅 {xq.GrateLen:F2} m" : "",
                               d.Loop > 0 ? $" · {UDitch.LoopText} 갈래 {d.Loop}({d.LoopNote})" : "",
                               ly.Cuts.Count > 0 ? " · 집수정 자리 " + string.Join("/", ly.Cuts.Select(c => $"{c.Name} {c.Kind} {c.A:F2}~{c.B:F2}")) : "",
                               string.Join("/", sols.GroupBy(x => x.Part).Select(gp => $"{gp.Key} {gp.Count()}개 부피 {gp.Sum(x => x.Volume):F3}"))));
            }
            catch (System.Exception ex)
            {
                d.Solids.Clear();
                st.Skipped++;
                if (st.FirstFail.Length == 0) st.FirstFail = $"{d.Id}: {ex.Message}";
                log.AppendLine($"  {d.Id}: 못 지음 — {ex.Message}");
            }
        }
        // ★[JACK 1123] 합류 · 교차 · 출구 집수정 — 수량 · 알림만(도면에는 안 넣음 · 3D 보기는 그림)
        foreach (var j in rec.Junctions)
        {
            try
            {
                var sols = UDitch.JunctionSolids(j, s);
                if (sols.Count == 0) { log.AppendLine($"  {j.Label}: 못 지음 — 바닥고 모름"); continue; }
                st.Basins++; st.Junctions++; st.ExtraVol += j.Volume; st.JunctionWarn += j.Warn.Count;
                log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  {0}: 안 {1:0.00} · 바깥 {2:0.00} m · 깊이 {3:0.00} m · 콘크리트 {4:F3} ㎥",
                                             j.Label, j.B.Inner, j.B.Outer, j.B.Depth, j.Volume));
            }
            catch (System.Exception ex)
            {
                if (st.FirstFail.Length == 0) st.FirstFail = $"{j.Name}: {ex.Message}";
                log.AppendLine($"  {j.Label}: 못 지음 — {ex.Message}");
            }
        }
        rec.Updated = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        Save(db, tr, rec);
        tr.Commit();
        return st;
    }

    /// <summary>★[JACK 1124] 측구 선종류 «—— U —— U ——» — <b>종이 mm 기준</b>(줄 6 · 틈 1.6 + 1.6 가운데 글 «U» 높이 2.5 · 글꼴 DH-한글 · 없으면 지금 글꼴).
    /// ★[JACK 1124 «배치 탭에서 U가 1 mm로 작게»] 같은 선이 모형 탭과 배치 탭 뷰포트에 함께 보이고 AutoCAD는 선종류를 공간마다 따로 키운다 —
    /// 뷰포트(PSLTSCALE 1)는 종이 단위 그대로(1:500 · 1:1000 어느 뷰포트든 U 2.5 mm) · 모형 탭(MSLTSCALE 1)은 주석 축척만큼(DH 1:500이면 U 1.25 m · 줄 3 m).
    /// 도면 축척을 선 축척에 구워 넣으면 뷰포트 축척이 다르거나 축척을 바꾸면 어긋나므로 정의를 종이 기준으로. 선마다 선종류 축척 = 1 / LTSCALE(도면 LTSCALE과 상관없게).
    /// 옛 정의(길이 다름 — 레인 시험판의 m 기준)가 있으면 고쳐 씀.</summary>
    internal static ObjectId EnsureLinetype(Database db, Transaction tr)
    {
        const double Dash = 6.0, Gap = 1.6, H = 2.5, Pattern = Dash + 2 * Gap;
        var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        LinetypeTableRecord? r = null;
        if (ltt.Has(Ltype))
        {
            r = (LinetypeTableRecord)tr.GetObject(ltt[Ltype], OpenMode.ForRead);
            if (System.Math.Abs(r.PatternLength - Pattern) < 1e-6 && r.NumDashes == 3) return r.ObjectId;
            r.UpgradeOpen();
        }
        var style = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
        if (style.IsNull) style = db.Textstyle;
        bool add = r == null;
        r ??= new LinetypeTableRecord { Name = Ltype };
        r.AsciiDescription = "U형 측구 ---- U ---- U ----";
        r.IsScaledToFit = false;
        r.NumDashes = 3;
        r.PatternLength = Pattern;
        r.SetDashLengthAt(0, Dash);
        r.SetDashLengthAt(1, -Gap);
        r.SetShapeStyleAt(1, style);
        r.SetTextAt(1, "U");
        r.SetShapeScaleAt(1, H);
        r.SetShapeRotationAt(1, 0.0);
        r.SetShapeOffsetAt(1, new Vector2d(-0.32 * H, -0.5 * H));   // 글 너비 ≈ 0.65 H → 두 틈 사이 가운데 · 높이 가운데
        r.SetShapeIsUcsOrientedAt(1, false);
        r.SetDashLengthAt(2, -Gap);
        if (!add) return r.ObjectId;
        ltt.UpgradeOpen();
        var id = ltt.Add(r);
        tr.AddNewlyCreatedDBObject(r, true);
        return id;
    }

    static double LtScale(Database db)
    {
        double g = 1.0;
        try { g = db.Ltscale; } catch { }
        return g > 1e-9 && double.IsFinite(g) ? 1.0 / g : 1.0;
    }

    /// <summary>★[JACK 1124] 원 선(핸들)에 선종류 U — 돌려줌 = 원 선이 있음(잠긴 레이어면 못 바꿔도 참). 선 · 지표면 모양은 안 바꿈.</summary>
    internal static bool MarkLine(Database db, Transaction tr, string handle, ObjectId lt)
    {
        if (handle.Length == 0 || lt.IsNull) return false;
        try
        {
            if (!db.TryGetObjectId(new Handle(System.Convert.ToInt64(handle, 16)), out var id) || id.IsErased) return false;
            if (tr.GetObject(id, OpenMode.ForRead) is not Curve e) return false;
            if (tr.GetObject(e.LayerId, OpenMode.ForRead) is LayerTableRecord lr && lr.IsLocked) return true;
            double lts = LtScale(db);
            bool plg = e is Polyline p0 && !p0.Plinegen;
            if (e.LinetypeId == lt && System.Math.Abs(e.LinetypeScale - lts) < 1e-9 && !plg) return true;
            e.UpgradeOpen();
            e.LinetypeId = lt;
            e.LinetypeScale = lts;
            if (e is Polyline pl) pl.Plinegen = true;   // 꺾인 점마다 무늬가 끊기지 않게
            return true;
        }
        catch { return false; }
    }

    /// <summary>★[JACK 1124] [측구 지우기] — 원 선의 선종류 U를 되돌림(레이어 따름).</summary>
    internal static void UnmarkLine(Database db, Transaction tr, string handle)
    {
        if (handle.Length == 0) return;
        try
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!ltt.Has(Ltype)) return;
            if (!db.TryGetObjectId(new Handle(System.Convert.ToInt64(handle, 16)), out var id) || id.IsErased) return;
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.LinetypeId != ltt[Ltype]) return;
            if (tr.GetObject(e.LayerId, OpenMode.ForRead) is LayerTableRecord lr && lr.IsLocked) return;
            e.UpgradeOpen();
            e.Linetype = "ByLayer";
            e.LinetypeScale = 1.0;
        }
        catch { }
    }

    /// <summary>원 선이 없을 때(지워짐) — 기록 점으로 선을 대신 그림(레이어 DH-U형측구 · 선종류 U · XData [앱, 이름, «선», 상태] — 다음 셈 · 지우기에 지움).</summary>
    static bool DrawStandIn(Database db, Transaction tr, UDitch.Ditch d, ObjectId lt, UDitch.Verdict v)
    {
        var pts = UDitch.CleanPts(d.Pts);
        if (pts.Count < 2) return false;
        var pl = new Polyline();
        for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i][0], pts[i][1]), 0, 0, 0);
        pl.LayerId = GradingBuilder.EnsureLayer(db, tr, Layer, 253);
        GradingBuilder.EnsureRegApp(db, tr, App);
        if (!lt.IsNull) { pl.LinetypeId = lt; pl.LinetypeScale = LtScale(db); }
        pl.Plinegen = true;
        pl.ColorIndex = v == UDitch.Verdict.Short ? 1 : v == UDitch.Verdict.Caution ? 2 : 256;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        using (var xd = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, d.Id),
                                         new TypedValue((int)DxfCode.ExtendedDataAsciiString, "선"), new TypedValue((int)DxfCode.ExtendedDataAsciiString, v.ToString())))
            pl.XData = xd;
        return true;
    }

    /// <summary>★[JACK 1124 «빗물 모드에서 측구가 반영된 빗물 모델»] 빗물 셈용 측구 물길(도면 좌표 · 시점 → 종점 · Z = 바닥고) — 기록이 없거나 못 읽으면 빈 목록. <b>읽기만</b>.</summary>
    internal static List<IReadOnlyList<Point3>> FlowPaths(Database db, System.Text.StringBuilder log)
    {
        var o = new List<IReadOnlyList<Point3>>();
        try
        {
            UDitch.Record rec;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool ok = TryLoad(db, tr, out rec, out _);
                tr.Commit();
                if (!ok || rec.Ditches.Count == 0) return o;
            }
            var s = LoadSettings(out _, out _);
            UDitch.Link(rec, s);
            foreach (var d in rec.Ditches)
            {
                var p = UDitch.FlowPath(d, s);
                if (p.Count >= 2) o.Add(p);
            }
        }
        catch (System.Exception ex) { log.AppendLine("  빗물 측구: 못 읽음 — " + ex.Message); }
        return o;
    }

    // ══════════════════════════════ 초기화 ══════════════════════════════

    /// <summary>★[JACK 1124 «전체 초기화 시 측구가 안 지워져»] 초기화가 고른 측구를 지움 — 기록에서 빼고(닫힌 선 갈래는 짝도) ·
    /// 표식 붙은 것(옛 솔리드 · 메시 · 대신 그린 선 · 합류 집수정) 지움 · 원 선은 우리가 그린 선(레이어 DH-U형측구-선)이면 지우고 남의 선이면 선종류 U만 뺌.
    /// 남은 측구가 없으면 표식 붙은 것 모두 · 강우는 남김(입력값 — 측구도 강우도 없으면 기록 자리째 지움). 기록을 못 읽으면 아무것도 안 함. 돌려줌 = (측구 수, 원 선 수, 표식 개체 수).</summary>
    internal static (int Ditches, int Lines, int Ents) EraseForReset(Database db, Transaction tr, System.Func<UDitch.Ditch, bool> pick, System.Text.StringBuilder log)
    {
        if (!TryLoad(db, tr, out var rec, out string why)) { log.AppendLine("U형 측구: 기록 못 읽음 — " + why + " → 그대로"); return (0, 0, 0); }
        var hit = rec.Ditches.Where(pick).ToList();
        var handles = hit.Select(d => d.LineHandle).Where(h => h.Length > 0).ToHashSet();
        var gone = rec.Ditches.Where(d => hit.Contains(d) || (d.LineHandle.Length > 0 && handles.Contains(d.LineHandle))).ToList();
        int ents = 0, lines = 0;
        foreach (var d in gone) { ents += Erase(db, tr, d.Id); rec.Ditches.Remove(d); }
        foreach (var h in handles) if (!rec.Ditches.Any(x => x.LineHandle == h) && DropLine(db, tr, h)) lines++;
        if (rec.Ditches.Count == 0) ents += Erase(db, tr, "");
        if (gone.Count > 0 || ents > 0) Save(db, tr, rec);
        if (gone.Count > 0 || ents > 0) log.AppendLine($"U형 측구: {gone.Count}개 지움({string.Join(" · ", gone.Select(d => d.Id))}) · 원 선 {lines} · 표식 개체 {ents} · 남은 측구 {rec.Ditches.Count}");
        return (gone.Count, lines, ents);
    }

    /// <summary>원 선 — 우리가 그린 선([선 그리기] · 레이어 DH-U형측구-선)은 지우고, 고른 남의 선은 선종류 U만 뺌(선은 남김). 돌려줌 = 손댐.</summary>
    static bool DropLine(Database db, Transaction tr, string handle)
    {
        try
        {
            if (!db.TryGetObjectId(new Handle(System.Convert.ToInt64(handle, 16)), out var id) || id.IsErased) return false;
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) return false;
            if (e.Layer == LineLayer)
            {
                if (tr.GetObject(e.LayerId, OpenMode.ForRead) is LayerTableRecord lr && lr.IsLocked) return false;
                e.UpgradeOpen(); e.Erase(); return true;
            }
            UnmarkLine(db, tr, handle);
            return true;
        }
        catch { return false; }
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
        UDitch.Link(rec, s);   // ★[JACK 1123] 합류 · 교차 · 출구 집수정 · 닫힌 선 갈래(시작 맞댐)
        var jparts = UDitch.JunctionParts(rec, s, ox, oy);
        object JBasin(UDitch.ScenePart b) => new Dictionary<string, object>
        {
            ["x"] = Web3DScene.Num(b.V[0], 3), ["y"] = Web3DScene.Num(b.V[1], 3), ["zTop"] = Web3DScene.Num(b.V[2], 3), ["zBottom"] = Web3DScene.Num(b.V[3], 3),
            ["w"] = Web3DScene.Num(b.V[4], 3), ["l"] = Web3DScene.Num(b.V[5], 3), ["rot"] = Web3DScene.Num(b.V[6], 5), ["inner"] = Web3DScene.Num(b.V[7], 3),
            ["name"] = b.Name, ["title"] = b.Name, ["rows"] = b.Rows.Select(r => (object)new[] { r.K, r.V }).ToList(),
        };
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
                ["basins"] = bs.Select(JBasin).Concat(jparts.Where(q => q.Owner == d.Id).Select(q => JBasin(q.Part))).ToList(),   // ★[JACK 1123] 합류 · 교차 · 출구 집수정은 첫 측구에
                ["pipes"] = ps.Select(p => (object)new Dictionary<string, object>
                {
                    ["x1"] = Web3DScene.Num(p.V[0], 3), ["y1"] = Web3DScene.Num(p.V[1], 3), ["z1"] = Web3DScene.Num(p.V[2], 3),
                    ["x2"] = Web3DScene.Num(p.V[3], 3), ["y2"] = Web3DScene.Num(p.V[4], 3), ["z2"] = Web3DScene.Num(p.V[5], 3), ["d"] = Web3DScene.Num(p.V[6], 3),
                    ["name"] = p.Name, ["title"] = p.Name, ["rows"] = Rows(p.Rows),
                }).ToList(),
                ["grating"] = d.Grating,
            });
        }
        // 붙일 측구가 못 지어졌으면 집수정만 따로 한 칸
        var shown = o.Select(x => (string)((Dictionary<string, object>)x)["name"]).ToHashSet();
        foreach (var (own, part) in jparts.Where(q => !shown.Contains(q.Owner)))
            o.Add(new Dictionary<string, object> { ["name"] = part.Name, ["title"] = part.Name, ["status"] = "", ["rows"] = new List<object>(), ["basins"] = new List<object> { JBasin(part) }, ["pipes"] = new List<object>(), ["grating"] = false });
        nb += jparts.Count;
        log.AppendLine($"  U형 측구: {o.Count}개 · tris {tris:N0} · 집수정 {nb}(합류 · 교차 · 출구 {jparts.Count}) · 관로 {np}");
        return o.Count > 0 ? o : null;
    }
}
