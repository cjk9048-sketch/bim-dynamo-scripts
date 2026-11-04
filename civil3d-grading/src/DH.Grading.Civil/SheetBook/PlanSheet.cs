using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using static DH.Grading.Core.SheetBookRules;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v112.0 · 계획_도면화.md 4.9 · JACK 1103 «평면도 = 참고도면처럼 + 범례 · 토공량»] <b>① 전체 계획평면도</b> — 범위 · 축척 · 자리 · 얼림 · 제목 · 방위표 · 범례 · 토공량표.
/// <para>자리(종이 mm · K-water 그림 자리 기준): 위 40 제목 · 오른쪽 140 띠(방위표 · 범례 · 토공량표) · 나머지 656 × 484 = 뷰포트(내 판단 5 — 띠 폭은 Core 한 줄).</para></summary>
internal static class PlanSheet
{
    internal const string NorthBlock = "DH_방위표";

    /// <summary>범례 줄 — (이름, 레이어들) · 평면 창에 실제로 있는 것만 그린다.</summary>
    static readonly (string Label, string[] Layers)[] LegendRows =
    {
        ("계획 경계", new[] { "DH-정지경계" }),
        ("절토 사면", new[] { "DH-사면선-절토" }),
        ("성토 사면", new[] { "DH-사면선-성토" }),
        ("소단", new[] { "DH-소단선-절토", "DH-소단선-성토" }),
        ("비탈 기호", new[] { "DH-노리선", RoadSession.LayNori }),
        ("옹벽", new[] { "DH-옹벽선", RoadSession.LayWall }),
        ("진입도로", new[] { RoadBuilder.LayAlign }),
        ("측점선", new[] { ProfileCommand.LayerSlMajor, RoadSheets.LayReview }),
        ("현황 등고선", new[] { ImportGisCommand.LayerContour, ImportGisCommand.LayerContourIndex }),
        ("시추공", new[] { StrataPalette.MarkLayer }),
    };

    internal sealed class Prep
    {
        public (double X0, double Y0, double X1, double Y1) Ext;
        public double Scale; public bool Fits;
        public double[] Vp = new double[4], Band = new double[4], Title = new double[4];
        public double Cx, Cy;
        /// <summary>★[v112.5] 도면 기울기(라디안 · 반시계 +).</summary>
        public double Rot;
        public List<ObjectId> Freeze = new();
        public List<string> FreezeNames = new();
        public List<(string Label, ObjectId Layer)> Legend = new();
        /// <summary>토공량표 줄 — (구분, 절토, 성토) · 면이 없으면 NaN.</summary>
        public List<(string Name, double Cut, double Fill)> Volume = new();
        public string Sig = "";
    }

    static bool PlanLayer(string ly)
        => ly == "DH-정지경계" || ly.StartsWith("DH-사면선-", StringComparison.Ordinal) || ly.StartsWith("DH-소단선-", StringComparison.Ordinal)
        || ly == "DH-옹벽선" || ly == "DH-노리선" || ly == "DH-FGL"
        || (ly.StartsWith(RoadBuilder.AlignBase + "-", StringComparison.Ordinal) && ly != RoadSheets.LayChain && ly != RoadBuilder.LayView && ly != RoadBuilder.LayProf);

    static bool InSheet(Extents3d b, List<Sheet> sheets)
        => sheets.Any(s => b.MinPoint.X >= s.X0 - 1e-6 && b.MaxPoint.X <= s.X1 + 1e-6 && b.MinPoint.Y >= s.Y0 - 1e-6 && b.MaxPoint.Y <= s.Y1 + 1e-6);

    /// <summary>범위 = 계획지표면 결과 선 ∪ 진입도로(지표면 · 종단 · 장 안 것 제외) 경계상자 — 없으면 null(사방 여유는 <see cref="Prepare"/>).</summary>
    internal static (double X0, double Y0, double X1, double Y1)? Extents(Database db, Transaction tr, List<Sheet> sheets)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                bool road = e is CivilDb.Alignment al && RoadBuilder.IsOurs(al);
                // ★[V98 실측] 그림 객체만(선 · 글 · 블록 · 해치 · 우리 노선) — Civil 라벨 묶음 등은 경계상자가 원점 근처 1×0으로 나와 범위를 원점까지 늘렸다
                bool shape = e is Curve || e is DBText || e is MText || e is BlockReference || e is Hatch || e is Solid;
                if (!road && (!shape || !PlanLayer(e.Layer) || SheetTag.IsRoad(e))) continue;
                var b = e.GeometricExtents;
                if (b.MaxPoint.X - b.MinPoint.X < 1e-9 && b.MaxPoint.Y - b.MinPoint.Y < 1e-9) continue;
                if (InSheet(b, sheets)) continue;
                x0 = Math.Min(x0, b.MinPoint.X); y0 = Math.Min(y0, b.MinPoint.Y); x1 = Math.Max(x1, b.MaxPoint.X); y1 = Math.Max(y1, b.MaxPoint.Y);
            }
            catch { }
        }
        return x1 > x0 && y1 > y0 ? (x0, y0, x1, y1) : null;
    }

    static (double X0, double Y0, double X1, double Y1) Grow((double X0, double Y0, double X1, double Y1) e)
    {
        double mx = (e.X1 - e.X0) * 0.05, my = (e.Y1 - e.Y0) * 0.05;
        return (e.X0 - mx, e.Y0 - my, e.X1 + mx, e.Y1 + my);
    }

    static string SurfStamp(Database db)
    {
        try
        {
            var parts = new List<string>();
            using var tr = db.TransactionManager.StartTransaction();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civ.GetSurfaceIds())
                if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts && (ts.Name == FinalSurfaceBuilder.Name || ts.Name == WallDaylightBuilder.PlanSurfaceBase || ts.Name == ImportGisCommand.GroundSurfaceName))
                {
                    var g = ts.GetGeneralProperties();
                    parts.Add($"{ts.Name}:{g.NumberOfPoints}:{g.MaximumElevation:F3}:{g.MinimumElevation:F3}");
                }
            tr.Commit();
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts);
        }
        catch { return ""; }
    }

    /// <summary>도킹창 — 축척 · 서명만(토공량은 안 셈 · 읽기만).</summary>
    internal static (double Scale, string Sig)? Quick(Database db, SheetBookScan scan, SheetBookInput inp, FrameDef def)
    {
        if (scan.PlanExt is not { } e0) return null;
        var e = Grow(e0);
        var (vp, _, _) = PlanLayout(def.DrawArea);
        var (rw, rh) = RotatedBox(e.X1 - e.X0, e.Y1 - e.Y0, inp.PlanRot * Math.PI / 180);
        var (sc, _) = PlanScale(rw, rh, vp[2] - vp[0], vp[3] - vp[1], inp.PlanScale);
        return (sc, Sig(e, sc, def, db, inp));
    }

    /// <summary>★[v112.5] 서명에 평면 꾸미기(기울기 · 등고값 · 측점 · 등고선 간격)도 — 바뀌면 «옛것».</summary>
    static string Sig((double X0, double Y0, double X1, double Y1) e, double sc, FrameDef def, Database db, SheetBookInput inp)
    {
        var (mi, ma) = ContourInterval.Read(db);
        return Signature(Array.Empty<Sheet>(), def.JsonHash4(), string.Create(CultureInfo.InvariantCulture,
            $"{e.X0:F3},{e.Y0:F3},{e.X1:F3},{e.Y1:F3}|{sc}|{SurfStamp(db)}"));   // ★[v112.6] 평면 꾸미기 값은 실시간으로 걸리므로 서명에서 뺌(돌림은 축척(sc)으로만 «옛것»)
    }

    /// <summary>★[v112.5 · JACK 1104 «측점들 껐다 켰다»] 평면의 측점 레이어 — 검토선 · 측점 글 · Civil 검토선(SampleLine) · 그 라벨.</summary>
    static HashSet<ObjectId> StationLayers(Database db, Transaction tr, List<SheetBookBuilder.ModelItem> idx)
    {
        var o = new HashSet<ObjectId>();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId lid in lt)
        {
            string n = ((LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead)).Name;
            if ((n.Contains("검토선") || n.Contains("측점")) && !n.Contains("체인") && !n.Contains("숨김")) o.Add(lid);   // ★[v112.6] 숨김 검토선(횡단용)은 늘 얼린 채 — 측점 켜기로 풀지 않음
        }
        foreach (var it in idx)
            try { if (tr.GetObject(it.Id, OpenMode.ForRead) is Entity e && e.GetType().Name.StartsWith("SampleLine", StringComparison.Ordinal)) o.Add(it.LayerId); } catch { }
        o.Remove(db.LayerZero); o.Remove(db.Clayer);
        return o;
    }

    /// <summary>범위 · 축척 · 얼림 · 범례 · 토공량(읽기만 · 면을 안 바꿈) — 범위가 없으면 null.</summary>
    /// <param name="useVolCache">★[v112.5 검토 높음] 평면 꾸미기(평면만 다시)일 때만 지난 토공량을 다시 씀 — [작성하기]는 늘 새로 셈.</param>
    internal static Prep? Prepare(Database db, SheetBookScan scan, SheetBookInput inp, FrameDef def, System.Text.StringBuilder log, List<string> warns, bool useVolCache = false)
    {
        if (scan.PlanExt is not { } e0) return null;
        var p = new Prep { Ext = Grow(e0) };
        var e = p.Ext;
        (p.Vp, p.Band, p.Title) = PlanLayout(def.DrawArea);
        double vw = p.Vp[2] - p.Vp[0], vh = p.Vp[3] - p.Vp[1];
        // ★[v112.5 · JACK 1104 «도면 기울기»] 돌린 범위가 뷰포트에 들어가는 축척 · 창 = 돌린 뷰포트를 감싸는 모형 상자
        p.Rot = inp.PlanRot * Math.PI / 180;
        var (rw, rh) = RotatedBox(e.X1 - e.X0, e.Y1 - e.Y0, p.Rot);
        (p.Scale, p.Fits) = PlanScale(rw, rh, vw, vh, inp.PlanScale);
        if (!p.Fits) warns.Add($"평면이 1:{p.Scale:0}에도 안 들어감 — 가장자리 잘림");
        p.Cx = (e.X0 + e.X1) / 2; p.Cy = (e.Y0 + e.Y1) / 2;
        var (bw, bh) = RotatedBox(vw * p.Scale / 1000, vh * p.Scale / 1000, p.Rot);
        double hw = bw / 2, hh = bh / 2;
        var win = (X0: p.Cx - hw, Y0: p.Cy - hh, X1: p.Cx + hw, Y1: p.Cy + hh);
        log.AppendLine($"■ 평면 — 범위 {e.X1 - e.X0:F1} × {e.Y1 - e.Y0:F1} m(5 % 여유) · 1:{p.Scale:0} → 종이 {(e.X1 - e.X0) * 1000 / p.Scale:F0} × {(e.Y1 - e.Y0) * 1000 / p.Scale:F0} mm(뷰포트 {vw:F0} × {vh:F0})");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var idx = SheetBookBuilder.ModelIndex(db, tr);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            // 얼림 — 모형 장 레이어(장 안에만 있는 레이어) + 위성 · 배경지도 · 지도 범위 + 늘 셋
            var inSheet = new HashSet<ObjectId>(); var outside = new HashSet<ObjectId>();
            var inSheetWin = new HashSet<ObjectId>();   // 평면 창 안에 든 장 내용의 레이어(못 숨기면 평면에 보임)
            foreach (var it in idx)
            {
                if (it.ByType) { inSheet.Add(it.LayerId); continue; }
                if (!it.HasBox) continue;
                bool ins = InSheet(it.Box, scan.Sheets);
                bool sheetish = it.CivilContent || it.Tagged || SheetBookBuilder.SheetLayers.Contains(it.Layer) || it.Layer.StartsWith("CR-", StringComparison.OrdinalIgnoreCase);
                if (ins && sheetish)
                {
                    inSheet.Add(it.LayerId);
                    if (it.Box.MaxPoint.X >= win.X0 && it.Box.MinPoint.X <= win.X1 && it.Box.MaxPoint.Y >= win.Y0 && it.Box.MinPoint.Y <= win.Y1) inSheetWin.Add(it.LayerId);
                }
                else outside.Add(it.LayerId);
            }
            var fz = inSheet.Where(l => !outside.Contains(l)).ToHashSet();
            foreach (var nm in SheetBookBuilder.AlwaysFreeze.Concat(new[] { MapPickCommand.BoxLayer, "Satellite", BasemapCommand.Layer })) if (lt.Has(nm)) fz.Add(lt[nm]);
            // ★[검토 1103 · 2] 못 숨긴 레이어(장 내용과 평면이 같은 레이어 · 0 · 현재 레이어) — 평면 창 안에 든 장 내용만 ⚠(창 밖 장은 어차피 안 보임)
            string LName(ObjectId l) => ((LayerTableRecord)tr.GetObject(l, OpenMode.ForRead)).Name;
            var mixed = inSheetWin.Where(l => !fz.Contains(l)).Select(LName)
                .Concat(inSheetWin.Where(l => fz.Contains(l) && (l == db.LayerZero || l == db.Clayer)).Select(l => LName(l) + "(현재/0)")).ToList();
            if (!inp.Stations) foreach (var l in StationLayers(db, tr, idx)) fz.Add(l);   // ★[v112.5] 측점 끔
            fz.Remove(db.LayerZero); fz.Remove(db.Clayer);
            p.Freeze = fz.ToList();
            p.FreezeNames = p.Freeze.Select(LName).ToList();
            if (mixed.Count > 0)
            {
                log.AppendLine($"  ⚠평면 뷰포트 못 숨김(장 내용과 평면이 함께): {string.Join(" · ", mixed)}");
                warns.Add($"평면도 창 안 종단도 · 횡단도 일부를 못 숨김(레이어 {string.Join(" · ", mixed)}에 평면 것도 있음)");
            }
            int over = scan.Sheets.Count(s => s.X1 >= win.X0 && s.X0 <= win.X1 && s.Y1 >= win.Y0 && s.Y0 <= win.Y1);
            if (over > 0) warns.Add($"평면도 창에 종단도 · 횡단도 {over}장이 겹침 — 종단도를 평면 밖으로");
            // 범례 — 평면 창에 실제로 있는 레이어만(장 안 것은 빼고)
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in idx)
                if (it.HasBox && it.Box.MaxPoint.X >= win.X0 && it.Box.MinPoint.X <= win.X1 && it.Box.MaxPoint.Y >= win.Y0 && it.Box.MinPoint.Y <= win.Y1 && !InSheet(it.Box, scan.Sheets))
                    present.Add(it.Layer);
            foreach (var (label, lays) in LegendRows)
            {
                var hit = lays.FirstOrDefault(l => present.Contains(l) && lt.Has(l) && !fz.Contains(lt[l]));   // ★[v112.5] 얼린(측점 끔) 레이어는 범례에서도 뺌
                if (hit != null) p.Legend.Add((label, lt[hit]));
            }
            tr.Commit();
        }
        log.AppendLine($"  평면 얼림 {p.FreezeNames.Count}개: {string.Join(" · ", p.FreezeNames)} · 범례 {string.Join(" · ", p.Legend.Select(l => l.Label))}");
        // ★[v112.5] 평면 꾸미기로 평면 탭만 자주 다시 — 지표면(최종 · 정지면 · 원지반 지문)이 같으면 지난 토공량 그대로(12초 셈 건너뜀)
        //   ★[검토 높음] 열쇠 = 면 머리(점 수 · 높이) + 계획면 세모 지문(꼭짓점 합 — 가운데 바닥만 바꿔도 바뀜) + 도로 면 · 평면 꾸미기 때만 · 다 맞은 셈만 남김
        string vk = useVolCache ? VolKey(db) : "";
        if (vk.Length > 0 && VolCache.TryGetValue(db, out var vc) && vc.Key == vk) { p.Volume = vc.Rows; log.AppendLine("  토공량 — 지표면 그대로라 지난 셈을 씀"); }
        else
        {
            p.Volume = Volumes(db, log, warns);
            string k2 = vk.Length > 0 ? vk : VolKey(db);
            if (k2.Length > 0 && p.Volume.Count > 0 && p.Volume.All(v => double.IsFinite(v.Cut) && double.IsFinite(v.Fill))) VolCache.AddOrUpdate(db, new VolEntry(k2, p.Volume));
        }
        p.Sig = Sig(e, p.Scale, def, db, inp);
        return p;
    }

    /// <summary>★[계획 4.9 · 검토 M7] 토공량 — 계 = (최종지표면_DH, 없으면 정지면_DH) vs 원지반 · 진입도로 = 도로 자리 안 · 계획지표면 = 그 밖(칸마다 0 이상 · 계 = 합).</summary>
    internal static List<(string Name, double Cut, double Fill)> Volumes(Database db, System.Text.StringBuilder log, List<string> warns)
    {
        var rows = new List<(string, double, double)>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string g = ImportGisCommand.GroundSurfaceName;
            var (gid, did) = EarthworkVolume.Find(db, g, FinalSurfaceBuilder.Name);
            string dn = FinalSurfaceBuilder.Name;
            if (did.IsNull) { (gid, did) = EarthworkVolume.Find(db, g, WallDaylightBuilder.PlanSurfaceBase); dn = WallDaylightBuilder.PlanSurfaceBase; }
            if (gid.IsNull || did.IsNull)
            {
                rows.Add(("계획지표면", double.NaN, double.NaN)); rows.Add(("계", double.NaN, double.NaN));
                warns.Add("토공량 — 원지반 · 계획면 없음(«-»)");
                return rows;
            }
            NetTopologySuite.Geometries.Geometry? fp = null; int nRoads = 0; string note = "";
            using (var tr = db.TransactionManager.StartTransaction()) { fp = RoadSession.Footprints(db, tr, out nRoads, out note); tr.Commit(); }
            var dt = EarthworkVolume.Tris(db, did); var gt = EarthworkVolume.Tris(db, gid);
            var all = TinVolume.Compute(dt, gt);
            if (fp != null && !fp.IsEmpty)
            {
                var r = TinVolume.Compute(dt, gt, fp, true);
                var s = TinVolume.Compute(dt, gt, fp, false);
                rows.Add(("계획지표면", s.Cut, s.Fill)); rows.Add(("진입도로", r.Cut, r.Fill)); rows.Add(("계", all.Cut, all.Fill));
                log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  토공량({dn} − {g}) 계 {all.Cut:F1}/{all.Fill:F1} · 계획지표면(도로 자리 밖) {s.Cut:F1}/{s.Fill:F1} · 진입도로(도로 {nRoads}) {r.Cut:F1}/{r.Fill:F1} · 합 오차 {s.Cut + r.Cut - all.Cut:E1}/{s.Fill + r.Fill - all.Fill:E1}{(note.Length > 0 ? " · " + note : "")}"));
            }
            else
            {
                rows.Add(("계획지표면", all.Cut, all.Fill)); rows.Add(("계", all.Cut, all.Fill));
                log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  토공량({dn} − {g}) {all.Cut:F1}/{all.Fill:F1}"));
            }
            // 정지 창 토량(정지면_DH vs 원지반 · 도로 자리 포함)과 나란히 — 진단 로그만(창에 설명 글 안 넣음)
            if (dn != WallDaylightBuilder.PlanSurfaceBase || fp != null)
            {
                var (g2, p2) = EarthworkVolume.Find(db, g, WallDaylightBuilder.PlanSurfaceBase);
                if (!p2.IsNull && !g2.IsNull)
                {
                    var pv = TinVolume.Compute(EarthworkVolume.Tris(db, p2), gt);
                    log.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  (견줌) 정지면_DH − 원지반 {pv.Cut:F1}/{pv.Fill:F1} — 정지 창 · [3D 보기]와 같은 셈"));
                }
            }
        }
        catch (System.Exception ex) { warns.Add("토공량 셈 실패 — " + ex.Message); rows.Clear(); rows.Add(("계", double.NaN, double.NaN)); }
        log.AppendLine($"  토공량 셈 {sw.ElapsedMilliseconds} ms" + (sw.ElapsedMilliseconds > 10000 ? " ⚠10초 넘음" : ""));
        return rows;
    }

    sealed record VolEntry(string Key, List<(string Name, double Cut, double Fill)> Rows);
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Database, VolEntry> VolCache = new();

    /// <summary>토공량 다시 쓰기 열쇠 — 못 만들면 빈 글(다시 안 씀).</summary>
    static string VolKey(Database db)
    {
        try
        {
            string st = SurfStamp(db);
            if (st.Length == 0) return "";
            var (gid, did) = EarthworkVolume.Find(db, ImportGisCommand.GroundSurfaceName, FinalSurfaceBuilder.Name);
            if (did.IsNull) (gid, did) = EarthworkVolume.Find(db, ImportGisCommand.GroundSurfaceName, WallDaylightBuilder.PlanSurfaceBase);
            if (did.IsNull) return "";
            double h = 0; int n = 0;
            foreach (var (a, b, c) in EarthworkVolume.Tris(db, did)) { foreach (var q in new[] { a, b, c }) h += q.X * 0.7071 + q.Y * 0.31 + q.Z * 1.618 * (1 + (n++ % 7)); }
            return st + "|" + h.ToString("R", CultureInfo.InvariantCulture) + "|" + n + "|" + DrawRoadsKey(db);
        }
        catch { return ""; }
    }

    /// <summary>토공량 칸 나눔(도로 자리)이 바뀌었나 — 우리 진입도로 면 핸들들.</summary>
    static string DrawRoadsKey(Database db)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var l = RoadRanges.Roads(tr, db).Select(id => RoadDesign.Read(tr, id)?.SurfaceHandle ?? "").OrderBy(s => s, StringComparer.Ordinal).ToList();
            tr.Commit();
            return string.Join(",", l);
        }
        catch { return "?"; }
    }

    /// <summary>★[v112.5] 뷰포트 TwistAngle 부호 — 종이에서 반시계로 <c>Rot</c>만큼 돌아 보이게(화면 없는 Civil 출력으로 잼).</summary>
    internal const double TwistSign = 1.0;

    /// <summary>★[v112.5] 참고도면 CE-SLOPE에 드는 레이어 — 계획 경계 · 사면선 · 소단선 · 노리선(부지 · 진입도로).</summary>
    internal static bool SlopeLayer(string n)
        => n == "DH-정지경계" || n == "DH-노리선" || n.StartsWith("DH-사면선", StringComparison.Ordinal) || n.StartsWith("DH-소단선", StringComparison.Ordinal)
        || n == RoadSession.LaySlope || n == RoadSession.LayBench || n == RoadSession.LayNori;

    /// <summary>★★[v112.5 · JACK 1104 «계획 구역 전체 회색»] 계획 구역 = 정지 경계(DH-정지경계의 닫힌 선 — 바닥 + 사면) ∪ 진입도로 면 자리(<see cref="RoadSession.Footprints"/>).</summary>
    internal static NetTopologySuite.Geometries.Geometry? PlanArea(Database db, Transaction tr, NetTopologySuite.Geometries.GeometryFactory gf, out string note)
    {
        var parts = new List<NetTopologySuite.Geometries.Geometry>();
        int nB = 0, nOpen = 0;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Curve c || !string.Equals(c.Layer, "DH-정지경계", StringComparison.OrdinalIgnoreCase)) continue;
                bool closed = c.Closed || c.StartPoint.DistanceTo(c.EndPoint) < 1e-3;
                if (!closed) { nOpen++; continue; }
                double end = c.EndParam, start = c.StartParam;
                var cs = new List<NetTopologySuite.Geometries.Coordinate>();
                if (c is Polyline || c is Polyline2d || c is Polyline3d)
                {
                    int n = Math.Max(8, (int)Math.Ceiling(end - start) * 8);   // 꼭짓점 = 정수 매개변수 · 사이 8칸(호 · 곡선 토막)
                    for (int i = 0; i < n; i++) { var q = c.GetPointAtParameter(start + (end - start) * i / n); cs.Add(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)); }
                }
                else
                {
                    // ★[검토 중간] 스플라인 등 — 길이로 0.5 m마다(최대 2만 점)
                    double L = c.GetDistanceAtParameter(end);
                    int n = (int)Math.Clamp(Math.Ceiling(L / 0.5), 8, 20000);
                    for (int i = 0; i < n; i++) { var q = c.GetPointAtDist(L * i / n); cs.Add(new NetTopologySuite.Geometries.Coordinate(q.X, q.Y)); }
                }
                cs.Add(cs[0]);
                if (cs.Count < 4) continue;
                var raw = gf.CreatePolygon(cs.ToArray());
                var pg = raw.IsValid ? (NetTopologySuite.Geometries.Geometry)raw : NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(raw);   // ★[검토 중간] 8자 꼬임도 두 쪽 다(Buffer(0)은 한쪽만)
                if (!pg.IsEmpty && pg.Area > 1e-3) { parts.Add(pg); nB++; }
            }
            catch { }
        }
        int nRoads = 0; string rn = "";
        try { var fp = RoadSession.Footprints(db, tr, out nRoads, out rn); if (fp != null && !fp.IsEmpty) parts.Add(fp); } catch (System.Exception ex) { rn = "⚠진입도로 자리 못 읽음 — " + ex.Message; }
        note = $"정지 경계 {nB}" + (nOpen > 0 ? $"(열린 선 {nOpen} 뺌)" : "") + $" · 진입도로 {nRoads}" + (rn.Length > 0 ? " · " + rn : "");
        if (parts.Count == 0) return null;
        var polys = parts.SelectMany(g => Enumerable.Range(0, g.NumGeometries).Select(g.GetGeometryN)).OfType<NetTopologySuite.Geometries.Polygon>().Cast<NetTopologySuite.Geometries.Geometry>().ToList();
        try { return NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(polys); }
        catch
        {
            // ★[검토 중간] 합치기 실패 — 하나씩(실패한 조각은 고쳐서)
            NetTopologySuite.Geometries.Geometry acc = gf.CreatePolygon();
            foreach (var g in polys) { try { acc = acc.Union(g); } catch { try { acc = acc.Union(NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(g)); } catch { } } }
            return acc;
        }
    }

    // ══════════════════════════ 그리기(종이 공간) ══════════════════════════

    /// <summary>★★[v112.6 · JACK 1104 «평면 꾸미기가 누를 때마다 도면화를 재실행 — 실시간으로»] 평면 탭을 다시 만들지 않고 그 자리에서 — 뷰포트 돌림 + 덧그림(<see cref="Overlays"/>)만.
    /// 축척 · 범례 · 토공량표 · 표제란은 그대로(돌려서 범위가 넘치면 알림 — [작성하기]로 축척 다시). 명령 안(문서 잠김)에서 부른다. 돌려줌 = 한 줄.</summary>
    internal static string Live(Database db, SheetBookInput inp, System.Text.StringBuilder log, List<string> warns)
    {
        var tab = SheetBookBuilder.OurTabs(db).FirstOrDefault(t => t.Kind == Kind.Plan && !t.Temp);
        if (tab == null) return "⚠전체 계획평면도 탭이 없습니다 — [작성하기]부터";
        using var tr = db.TransactionManager.StartTransaction();
        var lo = (Layout)tr.GetObject(tab.Id, OpenMode.ForRead);
        var ps = (BlockTableRecord)tr.GetObject(lo.BlockTableRecordId, OpenMode.ForWrite);   // ★[v112.6] 덧그림을 붙이므로 쓰기(읽기로 열면 AutoCAD 내부 오류 eNotOpenForWrite로 꺼짐 — 화면 없는 시험에서 실측)
        Viewport? vp = null;
        foreach (ObjectId id in ps)
            if (tr.GetObject(id, OpenMode.ForRead) is Viewport v && v.Layer == SheetBookBuilder.LayVp) { vp = v; break; }
        if (vp == null) { tr.Commit(); return "⚠평면 탭에 뷰포트가 없습니다 — [작성하기]로 다시"; }
        vp.UpgradeOpen();
        double rot = inp.PlanRot * Math.PI / 180;
        bool wasLocked = vp.Locked;
        if (wasLocked) vp.Locked = false;
        vp.TwistAngle = TwistSign * rot;
        if (wasLocked) vp.Locked = true;
        log.AppendLine($"■ 평면 꾸미기(실시간) — 기울기 {inp.PlanRot:0.###}° · 등고값 {(inp.ContourLabels ? "켬" : "끔")} · 측점 {(inp.Stations ? "켬" : "끔")}");
        Overlays(db, tr, ps, vp, inp, log, warns);
        // 돌린 범위가 뷰포트를 넘나(축척은 그대로) — 넘치면 알림
        try
        {
            var scan = SheetBookScan.Read(db);
            if (scan.PlanExt is { } e0)
            {
                var e = Grow(e0);
                var (rw, rh) = RotatedBox(e.X1 - e.X0, e.Y1 - e.Y0, rot);
                double sc = 1000.0 / vp.CustomScale;
                if (rw * 1000 / sc > vp.Width + 1e-6 || rh * 1000 / sc > vp.Height + 1e-6) warns.Add($"돌린 평면이 1:{sc:0} 뷰포트를 넘습니다 — [작성하기]로 축척을 다시 고르세요");
            }
        }
        catch { }
        tr.Commit();
        try { inp.Write(db); } catch (System.Exception ex) { warns.Add("평면 꾸미기 값 저장 실패 — " + ex.Message); }
        return "평면 꾸미기 적용";
    }

    /// <summary>★[v112.6] 평면 덧그림 레이어 — 계획 구역 외곽선(검정 굵게).</summary>
    internal const string LayOutline = "도면화-외곽";

    /// <summary>★★[v112.6 · JACK 1104 «평면 꾸미기 실시간»] 평면 뷰포트의 덧그림 — 처음 그릴 때와 [평면 꾸미기] 실시간 바꿈이 같이 씀(뷰포트 자체에서 가운데 · 축척 · 돌림을 읽음).
    /// <para>지우고 새로: 계획 구역 칠(뷰포트 안 것만 — 범례 견본은 그대로) · 외곽선 · 등고값 · 방위표 돌림 · 뷰포트 색(설계 선 검정) · 얼림(설계 지표면 · 측점).</para>
    /// 돌려줌 = 계획 구역 칠을 했나.</summary>
    internal static bool Overlays(Database db, Transaction tr, BlockTableRecord ps, Viewport vp, SheetBookInput inp, System.Text.StringBuilder log, List<string> warns)
    {
        double cs = vp.CustomScale, rot = vp.TwistAngle / TwistSign;
        double cxm = vp.ViewTarget.X, cym = vp.ViewTarget.Y;
        double[] V = { vp.CenterPoint.X - vp.Width / 2, vp.CenterPoint.Y - vp.Height / 2, vp.CenterPoint.X + vp.Width / 2, vp.CenterPoint.Y + vp.Height / 2 };
        double vcx = vp.CenterPoint.X, vcy = vp.CenterPoint.Y, cr = Math.Cos(rot), sr = Math.Sin(rot);
        (double X, double Y) ToPaper(double x, double y) { double dx = (x - cxm) * cs, dy = (y - cym) * cs; return (vcx + dx * cr - dy * sr, vcy + dx * sr + dy * cr); }
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        NetTopologySuite.Geometries.Polygon vpModel;
        {
            double hw = vp.Width / 2 / cs, hh = vp.Height / 2 / cs;
            var c = new[] { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh), (-hw, -hh) }
                .Select(q => new NetTopologySuite.Geometries.Coordinate(cxm + q.Item1 * cr + q.Item2 * sr, cym - q.Item1 * sr + q.Item2 * cr)).ToArray();
            vpModel = gf.CreatePolygon(c);
        }
        bool InVp(Extents3d x) => x.MinPoint.X >= V[0] - 1e-6 && x.MaxPoint.X <= V[2] + 1e-6 && x.MinPoint.Y >= V[1] - 1e-6 && x.MaxPoint.Y <= V[3] + 1e-6;

        // ① 지난 덧그림 지움(실시간 바꿈) — 칠은 뷰포트 안 것만(범례 견본은 띠에 있음)
        int nErased = 0;
        foreach (ObjectId id in ps)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                bool mine = e.Layer == SheetBookBuilder.LayContourText || e.Layer == LayOutline
                         || (e.Layer == SheetBookBuilder.LayFill && InVp(e.GeometricExtents));
                if (!mine) continue;
                e.UpgradeOpen(); e.Erase(); nErased++;
            }
            catch { }
        }

        // ② 방위표 — 도면을 돌린 만큼
        foreach (ObjectId id in ps)
            try { if (tr.GetObject(id, OpenMode.ForRead) is BlockReference nb && ((BlockTableRecord)tr.GetObject(nb.BlockTableRecord, OpenMode.ForRead)).Name == "DH_방위표") { nb.UpgradeOpen(); nb.Rotation = rot; } } catch { }

        // ②b ★[v112.6 · JACK 1104 «측점 끄면 진입도로 측점만 꺼짐»] 옛 도면의 부지 검토선(우리 «DH횡단…» 그룹 · 0 레이어) → «DH-검토선(Civil)»(0 레이어는 얼리면 블록 속 것까지 사라져 못 얼림)
        try
        {
            int nMv = 0;
            var msb = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            ObjectId laySl = ObjectId.Null;
            foreach (ObjectId id in msb)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.SampleLine sl || sl.LayerId != db.LayerZero) continue;
                    if (!(sl.Name ?? "").StartsWith(SectionCommand.GroupBase, StringComparison.Ordinal)) continue;
                    if (laySl.IsNull) laySl = SectionCommand.EnsureLayer(db, tr, ProfileCommand.SiteSampleLineLayer, 8);
                    sl.UpgradeOpen(); sl.LayerId = laySl; nMv++;
                }
                catch { }
            }
            if (nMv > 0) log.AppendLine($"  부지 검토선 {nMv}개 0 레이어 → «{ProfileCommand.SiteSampleLineLayer}»(측점 끄기)");
        }
        catch { }

        // ③ 얼림 — 설계 지표면(진입도로 · 정지면 · 최종면 — 등고선 · 노란 테두리가 칠 안에 비침 · JACK 1104 스샷) · 측점(끄면)
        try
        {
            var idx = SheetBookBuilder.ModelIndex(db, tr);
            var surfLayers = new HashSet<ObjectId>(); var groundLayers = new HashSet<ObjectId>();
            foreach (var it in idx)
                try
                {
                    if (tr.GetObject(it.Id, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface ts) continue;
                    if (ts.Name == ImportGisCommand.GroundSurfaceName || ts.Name.StartsWith("DH_지층", StringComparison.Ordinal)) groundLayers.Add(it.LayerId); else surfLayers.Add(it.LayerId);
                }
                catch { }
            surfLayers.ExceptWith(groundLayers);
            surfLayers.Remove(db.LayerZero); surfLayers.Remove(db.Clayer);
            if (surfLayers.Count > 0) vp.FreezeLayersInViewport(surfLayers.GetEnumerator());
            var st = StationLayers(db, tr, idx);
            if (st.Count > 0) { if (inp.Stations) vp.ThawLayersInViewport(st.GetEnumerator()); else vp.FreezeLayersInViewport(st.GetEnumerator()); }
            log.AppendLine($"  평면 얼림 — 설계 지표면 {surfLayers.Count}층 · 측점 {st.Count}층 {(inp.Stations ? "켬" : "끔")}");
        }
        catch (System.Exception exZ) { warns.Add("평면 얼림 못 함 — " + exZ.Message); }

        // ④ 계획 구역 칠 + 외곽선(검정 굵게 — 진입도로는 따로 외곽선 레이어가 없어 빠졌던 것 · JACK 1104)
        bool siteFill = false;
        NetTopologySuite.Geometries.Geometry? area = null;
        try
        {
            area = PlanArea(db, tr, gf, out string anote);
            if (area != null && !area.IsEmpty)
            {
                var layFill = SheetBookBuilder.EnsureFillLayer(db, tr);
                var layOut = SectionCommand.EnsureLayer(db, tr, LayOutline, 7);
                var g = area.Intersection(vpModel);
                int nf = 0, nl = 0;
                for (int i = 0; i < g.NumGeometries; i++)
                    if (g.GetGeometryN(i) is NetTopologySuite.Geometries.Polygon pg && pg.Area > 1e-6)
                        if (SheetBookBuilder.PaperFill(tr, ps, layFill, pg, ToPaper, 8, 0.22)) nf++;
                // 외곽선 = 계획 구역 둘레 ∩ 뷰포트 창(창 밖 토막은 안 그림)
                var ring = area.Boundary.Intersection(vpModel);
                for (int k = 0; k < ring.NumGeometries; k++)
                    if (ring.GetGeometryN(k) is NetTopologySuite.Geometries.LineString ls && ls.NumPoints >= 2)
                    {
                        var pl = new Polyline(ls.NumPoints) { LayerId = layOut, ConstantWidth = 0.35 };
                        for (int j = 0; j < ls.NumPoints; j++) { var (x, y) = ToPaper(ls.GetCoordinateN(j).X, ls.GetCoordinateN(j).Y); pl.AddVertexAt(j, new Point2d(x, y), 0, 0, 0); }
                        ps.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true); nl++;
                    }
                siteFill = nf > 0;
                log.AppendLine($"  계획 구역 칠 {nf}조각 · 외곽선 {nl}가닥 · {area.Area:F0} ㎡ · {anote}");
            }
            else log.AppendLine("  계획 구역 칠 — 정지 경계 · 진입도로 면 없음 · " + anote);
        }
        catch (System.Exception exF) { warns.Add("계획 구역 칠 실패 — " + exF.Message); }

        // ⑤ 뷰포트 색 — ★[v112.6 · JACK 1104 «노리선 · 소단선 · 사면선 · 부지경계 · 외곽선 다 검정으로 통일해서 잘 보이게 — 진입도로도»] 설계 선 = 검정(7) · 0.25 mm · 현황 등고선 = 회색
        try
        {
            int nBlack = 0, nGray = 0;
            var ltv = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lid in ltv)
            {
                try
                {
                    var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    string n = ltr.Name;
                    bool design = SlopeLayer(n) || n.StartsWith("DH-옹벽선", StringComparison.Ordinal) || n == RoadSession.LayWall;
                    bool contour = n == ImportGisCommand.LayerContour || n == ImportGisCommand.LayerContourIndex;
                    if (!design && !contour) continue;
                    ltr.UpgradeOpen();
                    var ov = ltr.GetViewportOverrides(vp.ObjectId);
                    ov.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, design ? (short)7 : (short)8);
                    if (design) { try { ov.LineWeight = LineWeight.LineWeight025; } catch { } nBlack++; } else nGray++;
                }
                catch (System.Exception exL) { log.AppendLine("  ⚠뷰포트 색 못 바꾼 층 — " + exL.Message); }
            }
            log.AppendLine($"  뷰포트 색 — 검정(사면 · 소단 · 노리선 · 경계 · 옹벽) {nBlack}층 · 회색(현황 등고선) {nGray}층");
        }
        catch (System.Exception exC) { warns.Add("평면 뷰포트 색 못 바꿈 — " + exC.Message); }

        // ⑥ 등고값(원지반 계곡선 — 종이 100 mm마다 · 계획 구역 안 · 뷰포트 가장자리 뺌)
        if (inp.ContourLabels)
            try
            {
                var (_, major) = ContourInterval.Read(db);
                var (gid, _) = EarthworkVolume.Find(db, ImportGisCommand.GroundSurfaceName, ImportGisCommand.GroundSurfaceName);
                if (!gid.IsNull && major > 0)
                {
                    var env = vpModel.EnvelopeInternal;
                    var lines = ContourLabels.Lines(EarthworkVolume.Tris(db, gid), major, (env.MinX, env.MinY, env.MaxX, env.MaxY));
                    var prepArea = area != null && !area.IsEmpty ? NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(area) : null;
                    double m = 1.0 / cs;
                    bool Skip(double x, double y)
                    {
                        var (px, py) = ToPaper(x, y);
                        if (px < V[0] + 6 || px > V[2] - 6 || py < V[1] + 4 || py > V[3] - 4) return true;
                        return prepArea != null && prepArea.Contains(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)));
                    }
                    var labs = ContourLabels.Place(lines, 100 * m, 30 * m, 2 * m, Skip);
                    var layC = SectionCommand.EnsureLayer(db, tr, SheetBookBuilder.LayContourText, 8);
                    var kst = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
                    foreach (var q in labs)
                    {
                        var (px, py) = ToPaper(q.X, q.Y);
                        double ra = q.AngleRad + rot;
                        while (ra > Math.PI / 2) ra -= Math.PI;
                        while (ra <= -Math.PI / 2) ra += Math.PI;
                        var mt = new MText { Contents = ContourLabels.Text(q.Z, major), TextHeight = 2.0, Attachment = AttachmentPoint.MiddleCenter, Location = new Point3d(px, py, 0), Rotation = ra, LayerId = layC };
                        if (!kst.IsNull) mt.TextStyleId = kst;
                        ps.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
                        try { mt.BackgroundFill = true; mt.UseBackgroundColor = true; mt.BackgroundScaleFactor = 1.15; } catch { }
                    }
                    log.AppendLine($"  등고값 {labs.Count}개(계곡선 {major:0.##} m · 선 {lines.Sum(k => k.Value.Count)}가닥)");
                }
                else log.AppendLine("  등고값 — 원지반 없음");
            }
            catch (System.Exception exL) { warns.Add("등고값 못 씀 — " + exL.Message); }
        else log.AppendLine("  등고값 끔");
        if (nErased > 0) log.AppendLine($"  (지난 덧그림 {nErased}개 지움)");
        return siteFill;
    }

    internal static void Draw(Database db, Transaction tr, BlockTableRecord ps, double X0, double Y0, FrameDef def, Prep p, AnnotationScale? anno,
                              ObjectId wire2d, ObjectId layVp, ObjectId textStyle, SheetBookInput inp, System.Text.StringBuilder log, List<string> warns)
    {
        var lay = SectionCommand.EnsureLayer(db, tr, SheetBookBuilder.LayPlan, 7);
        double[] V = { X0 + p.Vp[0], Y0 + p.Vp[1], X0 + p.Vp[2], Y0 + p.Vp[3] };
        double[] B = { X0 + p.Band[0], Y0 + p.Band[1], X0 + p.Band[2], Y0 + p.Band[3] };
        double[] T = { X0 + p.Title[0], Y0 + p.Title[1], X0 + p.Title[2], Y0 + p.Title[3] };
        // 뷰포트
        var vp = new Viewport();
        ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
        vp.LayerId = layVp;
        vp.CenterPoint = new Point3d((V[0] + V[2]) / 2, (V[1] + V[3]) / 2, 0);
        vp.Width = V[2] - V[0]; vp.Height = V[3] - V[1];
        // ★[v112.5 · JACK 1104 «도면 기울기»] 보는 곳 = 범위 가운데를 ViewTarget으로(돌려도 가운데 그대로) · 돌림 = TwistAngle
        vp.ViewDirection = Vector3d.ZAxis; vp.ViewTarget = new Point3d(p.Cx, p.Cy, 0); vp.TwistAngle = TwistSign * p.Rot;
        vp.ViewCenter = new Point2d(0, 0);
        if (!wire2d.IsNull) try { vp.VisualStyleId = wire2d; } catch { }
        if (anno != null) vp.AnnotationScale = anno;       // ★[검토 H2] 평면의 측점 글씨 · 라벨이 평면 축척 종이 크기로
        double cs = 1000.0 / p.Scale;
        vp.CustomScale = cs;
        vp.Locked = true;
        if (p.Freeze.Count > 0) vp.FreezeLayersInViewport(p.Freeze.GetEnumerator());
        if (Math.Abs(vp.CustomScale - cs) > 1e-9) throw new InvalidOperationException($"평면 뷰포트 축척 되읽기 {vp.CustomScale} ≠ {cs}");
        string an = ""; try { an = vp.AnnotationScale?.Name ?? ""; } catch { }
        if (anno != null && an != anno.Name) warns.Add($"평면 뷰포트 주석 축척이 {an}로 바뀜(1:{p.Scale:0}이 이김)");
        log.AppendLine($"  평면 뷰포트 1:{p.Scale:0} · 주석 {an} · 보는 곳 ({p.Cx:F3},{p.Cy:F3}) · 종이 {V[2] - V[0]:F0} × {V[3] - V[1]:F0}");

        void Add(Entity e) { e.LayerId = lay; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }

        bool siteFill = Overlays(db, tr, ps, vp, inp, log, warns);
        void Rect(double x0, double y0, double x1, double y1)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            pl.Closed = true; Add(pl);
        }
        // ★[v112.5 · JACK 1104 «범례 글씨체 수정»] 범례 · 토공량표 = 맑은 고딕(DH-한글) — 도곽 글꼴(굴림)은 표제란 · 제목만
        var legendStyle = ImportGisCommand.EnsureKoreanTextStyle(db, tr);
        if (legendStyle.IsNull) legendStyle = textStyle;
        DBText Text(string s, double x, double y, double h, TextHorizontalMode hm = TextHorizontalMode.TextCenter)
        {
            var t = new DBText { TextString = s, Height = h, HorizontalMode = hm, VerticalMode = TextVerticalMode.TextVerticalMid };
            t.Position = t.AlignmentPoint = new Point3d(x, y, 0);
            if (!legendStyle.IsNull) t.TextStyleId = legendStyle;
            Add(t);
            try { t.AdjustAlignment(db); } catch { }
            return t;
        }
        Rect(V[0], V[1], V[2], V[3]);                                         // 뷰포트 테두리(출력됨 — 참고도면처럼)
        Add(new Line(new Point3d(B[0], B[1], 0), new Point3d(B[0], B[3], 0)));    // 띠 경계

        // 제목 — 그림 자리 위 가운데(모형 장 제목과 같은 꼴 6 mm · 4 mm)
        // ★[v112.2 · JACK 1104] 다른 탭과 같은 꼴(기호 + 밑줄 + 축척) — 그림 자리 위 띠 전체 폭 가운데
        SheetBookBuilder.TopTitle(db, tr, ps, lay, textStyle, V[0], B[2], T[1], T[3], DrawingTitle(inp.Prefix, inp.Titles[Kind.Plan], 1, 1), $"1:{p.Scale:0}");

        // 방위표
        double bx = (B[0] + B[2]) / 2, top = B[3];
        var north = EnsureNorth(db, tr, textStyle);
        var nb = new BlockReference(new Point3d(bx, top - 22, 0), north) { LayerId = lay, Rotation = p.Rot };   // ★[v112.5] 도면을 돌린 만큼 방위표도(종이 위 북쪽 = 모형 +Y가 간 쪽)
        ps.AppendEntity(nb); tr.AddNewlyCreatedDBObject(nb, true);

        // 범례 — 칸 높이 7 · 견본 선 20 + 이름 3 mm
        double y = top - 50;
        Text("범    례", bx, y, 3.5);
        y -= 8;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (siteFill)
        {
            // ★[v112.2] 범례 첫 줄 — 회색 칠 견본
            try
            {
                var sw = new NetTopologySuite.Geometries.GeometryFactory().ToGeometry(new NetTopologySuite.Geometries.Envelope(B[0] + 10, B[0] + 30, y - 2.5, y + 2.5));
                SheetBookBuilder.PaperFill(tr, ps, SheetBookBuilder.EnsureFillLayer(db, tr), (NetTopologySuite.Geometries.Polygon)sw, (x, yy) => (x, yy), 8, 0.35);
                Rect(B[0] + 10, y - 2.5, B[0] + 30, y + 2.5);
            }
            catch { }
            Text("계획 구역", B[0] + 36, y, 3.0, TextHorizontalMode.TextLeft);
            y -= 7;
        }
        foreach (var (label, lid) in p.Legend)
        {
            var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
            var ln = new Line(new Point3d(B[0] + 10, y, 0), new Point3d(B[0] + 30, y, 0));
            Add(ln);
            ln.Color = SlopeLayer(ltr.Name) ? Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7) : ltr.Color; ln.LinetypeId = ltr.LinetypeObjectId; ln.LinetypeScale = 1.0;
            Text(label, B[0] + 36, y, 3.0, TextHorizontalMode.TextLeft);
            y -= 7;
        }
        Rect(B[0] + 4, y + 3.5, B[2] - 4, top - 45);

        // 토공량표 — 구분 | 절토 | 성토 | 순토량(㎥) · 띠 아래에서 위로
        double[] cw = { 30, 32, 32, 40 };
        double tw = cw.Sum(), tx0 = B[0] + (B[2] - B[0] - tw) / 2, rh = 7;
        int nr = p.Volume.Count + 1;
        double ty0 = B[1] + 6;
        string N(double v) => double.IsNaN(v) ? "-" : Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
        string Net(double cut, double fill)
        {
            if (double.IsNaN(cut) || double.IsNaN(fill)) return "-";
            double n = Math.Round(fill) - Math.Round(cut);
            return Math.Abs(n).ToString("N0", CultureInfo.InvariantCulture) + (n > 0 ? "(반입)" : n < 0 ? "(반출)" : "");
        }
        var rowsText = new List<string[]> { new[] { "구분", "절토(㎥)", "성토(㎥)", "순토량(㎥)" } };
        rowsText.AddRange(p.Volume.Select(v => new[] { v.Name, N(v.Cut), N(v.Fill), Net(v.Cut, v.Fill) }));
        Text("토  공  량  표", bx, ty0 + nr * rh + 6, 3.5);
        Rect(tx0, ty0, tx0 + tw, ty0 + nr * rh);
        for (int r = 1; r < nr; r++) Add(new Line(new Point3d(tx0, ty0 + r * rh, 0), new Point3d(tx0 + tw, ty0 + r * rh, 0)));
        double cx = tx0;
        for (int k = 0; k < cw.Length - 1; k++) { cx += cw[k]; Add(new Line(new Point3d(cx, ty0, 0), new Point3d(cx, ty0 + nr * rh, 0))); }
        for (int r = 0; r < nr; r++)
        {
            double yy = ty0 + (nr - r - 0.5) * rh; cx = tx0;
            for (int k = 0; k < cw.Length; k++)
            {
                var t = Text(rowsText[r][k], cx + cw[k] / 2, yy, 2.5);
                try
                {
                    var x = t.GeometricExtents; double w = x.MaxPoint.X - x.MinPoint.X;
                    var (hh, wf) = FitText(w, 2.5, cw[k], rh);
                    if (wf < 1 || hh < 2.5) { t.Height = hh; t.WidthFactor = wf; t.AdjustAlignment(db); }
                }
                catch { }
                cx += cw[k];
            }
        }
    }

    /// <summary>«DH_방위표» — 원 12 mm · 반쪽 채운 화살 · «N»(북 = 도면 +Y).</summary>
    static ObjectId EnsureNorth(Database db, Transaction tr, ObjectId textStyle)
    {
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(NorthBlock)) return bt[NorthBlock];
        bt.UpgradeOpen();
        var b = new BlockTableRecord { Name = NorthBlock, Units = UnitsValue.Undefined };
        var id = bt.Add(b); tr.AddNewlyCreatedDBObject(b, true);
        void Add(Entity e) { e.Layer = "0"; e.ColorIndex = 0; b.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
        Add(new Circle(Point3d.Origin, Vector3d.ZAxis, 6));
        Add(new Solid(new Point3d(0, 9, 0), new Point3d(-2.6, -6, 0), new Point3d(0, -3, 0)));
        var pl = new Polyline();
        pl.AddVertexAt(0, new Point2d(0, 9), 0, 0, 0); pl.AddVertexAt(1, new Point2d(2.6, -6), 0, 0, 0); pl.AddVertexAt(2, new Point2d(0, -3), 0, 0, 0);
        pl.Closed = true; Add(pl);
        var t = new DBText { TextString = "N", Height = 3.5, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid };
        t.Position = t.AlignmentPoint = new Point3d(0, 12.5, 0);
        if (!textStyle.IsNull) t.TextStyleId = textStyle;
        Add(t);
        return id;
    }
}
