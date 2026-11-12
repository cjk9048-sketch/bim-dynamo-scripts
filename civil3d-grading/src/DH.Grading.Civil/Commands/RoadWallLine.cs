using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using DH.Grading.Core;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 `2026-11-05_진입도로_옹벽추가_보완.md` 2판 4절] 진입도로 [옹벽 추가](DHROADWALLADD) — 부지와 같은 새 개념.
/// <para>그리기(<see cref="WallLineDraw"/>) → 판정(지금 경계 = 벽 없는 면 P0에 앞 선 옹벽을 얹은 면의 바깥 고리 · 바닥 = 도로 띠) → 시험 짓기(<see cref="RoadGrade.ApplyWalls"/> — 앞 기록 + 이 선) 미리보기
/// (바꿀 자리 · 줄 옅게 · F 빨강 · C · S 점 · «선 바깥 최대») → 제원 〈단높이 = 도로 사면 · 절토 T / 성토 T〉 [절토(C)/성토(F)/다시그리기(S)] (Enter = 적용 · H는 묻지 않음 · T는 Enter = 그쪽 소단 폭) →
/// 판 2 기록(Sig) → SaveDesign → Recompute → 도로 면 · 종단 · 횡단 다시(부르는 쪽).</para>
/// <para>화면 없는 탐침: <see cref="Make"/>(그린 점 · T — 질문 없음 · 키트 HLRW).</para></summary>
internal static class RoadWallLine
{
    const string Tag = "진입도로 옹벽 추가";

    /// <summary>시험 짓기 — 지금 기록(앞 선 옹벽) + 이 선(번호 <paramref name="no"/>) · 그 선 상태 · 앞 벽을 얹은 «지금 경계»(그리기 판정용).</summary>
    internal sealed class Trial
    {
        public RoadGrade.WallState? St; public RoadGrade.WallSurface? Ws;
        public List<Point3>? Bd, Band; public List<(string Name, IReadOnlyList<Point3> Ring)> Bl = new();
        public string Why = "";
    }

    static List<(string Name, IReadOnlyList<Point3> Ring)> SiteBlockers(Autodesk.AutoCAD.DatabaseServices.Database db)
    {
        var bl = new List<(string, IReadOnlyList<Point3>)>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs != null) for (int k = 0; k < regs.Count; k++) if (regs[k]?.Boundary is { Count: >= 3 } b) bl.Add(($"부지 계획선 {k + 1}", b));
            tr.Commit();
        }
        catch { }
        return bl;
    }

    /// <summary>지금 경계 — 벽 없는 P0 + 앞 기록 선 옹벽(이 번호는 뺌)을 얹은 면의 바깥 고리 · 도로 띠 · 막을 다각형(부지 계획선 · 앞 벽 바꿀 자리 · 도로 면 안 구멍).</summary>
    internal static Trial OutlineOf(Document doc, RoadSession s, int exceptNo = 0)
    {
        var t = new Trial();
        if (GradingSettings.WallLineOff || WallByLine.Off || RoadGrade.WallOff) { t.Why = "⚠선 옹벽(v114) 꺼짐(정지옵션 WallLineOff)"; return t; }
        var (inp, r, p0, lines, path, why, ring) = s.P0Now();
        if (inp == null || r == null || p0 == null || lines == null) { t.Why = why; return t; }
        if (path == "칸 면") { t.Why = "칸 면 위에는 선 옹벽 안 지음(이 도로는 선 면 · 선 따라 TIN을 못 지음)"; return t; }
        t.Bl = SiteBlockers(doc.Database);
        var prev = inp.LineWalls.Where(w => w.No != exceptNo).ToList();
        var cur = p0;
        if (prev.Count > 0)
        {
            var ws0 = RoadGrade.ApplyWalls(inp, r, p0, lines, path, prev, t.Bl, ring: ring);
            cur = ws0.Tris;
            foreach (var w in ws0.Built) foreach (var z in w.Built!.ZoneF.Concat(w.Built.ZoneC)) t.Bl.Add(($"앞 선 옹벽 {w.No}의 바꿀 자리", z));
        }
        var (bd, band, holes, why2) = RoadGrade.OutlineOf(r, cur);
        if (bd == null || band == null) { t.Why = why2; return t; }
        t.Bd = bd; t.Band = band; t.Bl.AddRange(holes);
        return t;
    }

    /// <summary>시험 짓기 — 앞 기록 + 이 선(<paramref name="src"/> · 소단 폭) → 이 선 상태(짓지 못하면 까닭).</summary>
    /// <param name="shape">★[v117.x · 계획 `2026-11-12_진입도로_옹벽추가_부지로직.md`] 모양 — 옛(Out · 기본) · 수직 · 다단(부지와 같은 Core) · 다단 값 NaN = 도로 사면 단높이 · 소단 폭 · 첫 소단 자동.</param>
    internal static Trial Try(Document doc, RoadSession s, IReadOnlyList<Point3> src, int no, double tc, double tf, double reach, bool topStep = false, double blockLen = 9,
        WallByLine.WallShape shape = WallByLine.WallShape.Out, double tierH = double.NaN, double tierB = double.NaN, double tierZ1 = double.NaN)
    {
        var t = new Trial();
        var (inp, r, p0, lines, path, why, ring) = s.P0Now();
        if (inp == null || r == null || p0 == null || lines == null) { t.Why = why; return t; }
        static double? Nv(double v) => double.IsNaN(v) ? null : v;
        bool site = shape is WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered;
        var lw = site
            ? new RoadGrade.LineWall(no, src.Select(q => q.X).ToArray(), src.Select(q => q.Y).ToArray(), tc, tf, reach, false, 0, shape,
                shape == WallByLine.WallShape.Tiered ? Nv(tierH) : null, shape == WallByLine.WallShape.Tiered ? Nv(tierB) : null, shape == WallByLine.WallShape.Tiered ? Nv(tierZ1) : null)
            : new RoadGrade.LineWall(no, src.Select(q => q.X).ToArray(), src.Select(q => q.Y).ToArray(), tc, tf, reach, topStep, topStep ? blockLen : 0);
        var all = inp.LineWalls.Where(w => w.No != no).Append(lw).ToList();
        t.Ws = RoadGrade.ApplyWalls(inp, r, p0, lines, path, all, SiteBlockers(doc.Database), ring: ring);
        t.St = t.Ws.Walls.LastOrDefault(w => w.No == no);
        if (t.St == null) t.Why = "시험 짓기에 안 들어감"; else if (!t.St.Ok) t.Why = t.St.Why;
        return t;
    }

    static int NextNo(RoadDesign d) => (d.BoundWalls == null || d.BoundWalls.Count == 0 ? 0 : d.BoundWalls.Max(b => b.No)) + 1;

    /// <summary>★[v114.3 · 화면 없는 탐침 · 대화 끝] 그린 점 · 소단 폭 → 시험 짓기 → 판 2 기록 · 저장 · 다시 계산(질문 없음). 번호(0 = 못 함 · 아무것도 안 바꿈) · 글.
    /// <paramref name="redraw"/>면 끝에 도로 면 · 종단 · 횡단까지 다시(<see cref="RoadCommand.RedrawStale"/> — 명령은 제 끝에서 한다).
    /// <para>★[v117.x] 옛 모양(Out) 그대로 — 키트 탐침 HLRW가 반사로 인자 9개를 넘긴다(바꾸지 말 것). 새 벽(수직 · 다단)은 <see cref="MakeShaped"/>.</para></summary>
    internal static (int No, string Note) Make(Document doc, IReadOnlyList<Point3> src, double tc, double tf, string srcHandle = "", bool redraw = true, double reach = 40, bool topStep = false, double blockLen = 9)
        => MakeCore(doc, src, tc, tf, srcHandle, redraw, reach, topStep, blockLen, WallByLine.WallShape.Out, double.NaN, double.NaN, double.NaN);

    /// <summary>★★[v117.x · JACK 1111 «옹벽 추가는 부지나 진입도로나 똑같은 로직» · 계획 `2026-11-12_진입도로_옹벽추가_부지로직.md`] 부지와 같은 모양으로 기록(질문 없음 · 화면 · 탐침) —
    /// <paramref name="shape"/> = 수직 · 다단(다단은 1차 최고 ≤ 단높이면 저절로 수직 · 기록 모양 2) · 다단 값 NaN = 도로 사면 단높이 · 설계 소단 폭 · 첫 소단 자동. 소단 폭 기록 = 설계 절토 · 성토 소단 폭.
    /// 이름이 <see cref="Make"/>와 다른 까닭: 키트 탐침이 이름으로 반사 호출(겹치면 모호).</summary>
    internal static (int No, string Note) MakeShaped(Document doc, IReadOnlyList<Point3> src, WallByLine.WallShape shape, double tierH = double.NaN, double tierB = double.NaN, double tierZ1 = double.NaN,
        string srcHandle = "", bool redraw = true)
        => shape is WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered
            ? MakeCore(doc, src, double.NaN, double.NaN, srcHandle, redraw, 40, false, 9, shape, tierH, tierB, tierZ1)
            : Stop(doc, $"모양 {shape}는 이 길에서 안 지음(수직 · 다단만)");

    static (int No, string Note) MakeCore(Document doc, IReadOnlyList<Point3> src, double tc, double tf, string srcHandle, bool redraw, double reach, bool topStep, double blockLen,
        WallByLine.WallShape shape, double tierH, double tierB, double tierZ1)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var s = RoadSession.Of(doc);
        if (s == null) return (0, "열린 진입도로 없음(진입도로 창)");
        s.RecomputeIfDirty();
        bool ringWas = s.Pieces.Any(p => p.Ring != null);   // ★[v116 · 고리 엔진 §5] 벽을 넣기 전 고리 엔진이었나(넣으면 옛 엔진 — 안내 한 줄)
        var d = s.Fresh();
        bool site = shape is WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered;
        if (double.IsNaN(tc)) tc = d.CutBenchW;
        if (double.IsNaN(tf)) tf = d.FillBenchW;
        int no = NextNo(d);
        var t = Try(doc, s, src, no, tc, tf, reach, topStep, blockLen, shape, tierH, tierB, tierZ1);
        if (site && t.St?.Built is { } b0)
        {
            // ★[부지 WallLineFlow.Make와 같음] 자가검증 한 줄(키트가 읽음 · 거절도) · 측점표 탐침 한 줄(보고만)
            SelfCheckLog.Line(Tag, b0.Ok, b0.SelfCheck, doc.Name);
            if (b0.Ok && b0.Profile != null) SelfCheckLog.Line(Tag, '✔', b0.Profile.Probe, doc.Name);
        }
        if (t.St != null && !t.St.Ok) { try { DiagLog.AppendCarry($"\n  [{Tag}] 거절(기술) — {t.St.Code}: {t.St.Why}"); } catch { } return Stop(doc, WallEasy.Build(t.St.Code, t.St.Why)); }
        if (t.St == null) return Stop(doc, t.Why.Length > 0 ? t.Why : "못 지음");
        var st = t.St;
        var b = st.Built!;
        var rec = new RoadDesign.BoundWallRec
        {
            No = no, X = st.Cut!.Line.Select(q => q.X).ToArray(), Y = st.Cut.Line.Select(q => q.Y).ToArray(),
            SrcX = src.Select(q => q.X).ToArray(), SrcY = src.Select(q => q.Y).ToArray(), Side = st.Side, Tc = tc, Tf = tf, Reach = reach, SrcHandle = srcHandle ?? "",
            TopStep = topStep, BlockLen = topStep ? blockLen : 0,
        };
        if (site)
        {
            // ★[v117.x · 계획 3절 · 검토 HIGH 1] 지어진 모양 그대로 — 다단이면 3 + 쓴 단높이 · 소단(숫자일 때만) · 첫 소단은 손 값만(자동 = null — 다시 지을 때마다 지금 면에서 다시 셈 · 코디네이터 결정 1112) · 단이 필요 없었으면 2
            rec.TopStep = false; rec.BlockLen = 0;
            rec.Shape = b.Shape == 3 ? 3 : 2;
            if (rec.Shape == 3)
            {
                if (!double.IsFinite(b.TierH) || !double.IsFinite(b.TierB) || !double.IsNaN(tierZ1) && !double.IsFinite(tierZ1))
                    return Stop(doc, string.Format(ci, "다단 값이 숫자 아님(단높이 {0} · 소단 {1} · 첫 소단 {2}) — 기록 안 함", b.TierH, b.TierB, tierZ1));
                rec.TierH = b.TierH; rec.TierB = b.TierB; rec.TierZ1 = double.IsNaN(tierZ1) ? null : tierZ1;
            }
        }
        rec.Sig = rec.SigNow();
        d.BoundWalls ??= new List<RoadDesign.BoundWallRec>();
        d.BoundWalls.Add(rec);
        s.SaveDesign(); s.Recompute();
        if (site)
        {
            // ★[v117.x · 검토 HIGH 2] 되읽기 — 저장한 기록이 Core로 같은 모양 · 같은 다단 값으로 가는가(아니면 다시 짓기가 엉뚱한 모양 · 높이)
            var (inpR, _, _, _, _, whyR, _) = s.P0Now();
            var lwR = inpR?.LineWalls.FirstOrDefault(q => q.No == no);
            var want = rec.Shape == 3 ? WallByLine.WallShape.Tiered : WallByLine.WallShape.Vertical;
            bool same = lwR != null && lwR.Shape == want && (rec.Shape != 3 || lwR.TierH == rec.TierH && lwR.TierB == rec.TierB && lwR.TierZ1 == rec.TierZ1);
            SelfCheckLog.Line(Tag, same, same ? $"기록 되읽기 — 옹벽 {no} 모양 {rec.Shape} 같음" : $"기록 되읽기 — 옹벽 {no} 모양 {rec.Shape}인데 Core {(lwR == null ? "없음(" + whyR + ")" : lwR.Shape.ToString())} — 다시 짓기가 다를 수 있음", doc.Name);
        }
        string sideT = st.Side < 0 ? "좌" : st.Side > 0 ? "우" : "끝", cf = string.Join("+", new[] { b.HasFill ? "성토" : "", b.HasCut ? "절토" : "" }.Where(q => q.Length > 0));
        string note = site
            ? $"추가한 옹벽 {no} — {sideT} · "
              + (b.Shape == 3
                  ? string.Format(ci, "다단 {0}단 · 단높이 {1:0.##} · 소단 {2:0.##} · 첫 소단 {3:F3} · 바깥 최고 {4:F2} m · 안쪽 최고 {5:F2} m · 끊김 {6}곳", b.TierN, b.TierH, b.TierB, b.TierZ1, b.OuterHMax, b.InnerHMax, b.BreakN)
                  : string.Format(ci, "수직 1:{0:0.###} · 최고 {1:F2} m · 면적 {2:F1} ㎡", b.Batter, b.HMax, b.Area))
              + string.Format(ci, " · {0} · 선 {1:F1} m(절토 {2:F2} · 성토 {3:F2})", cf, b.LineLen, b.LenC, b.LenF)
            : $"추가한 옹벽 {no} — {sideT} · {cf} · 선 {b.LineLen:F1} m · 단높이 = 도로 사면 · 소단 절토 {tc:0.##} / 성토 {tf:0.##} m"
              + (topStep ? string.Format(ci, " · 천단 계단 {0:0.##} m", blockLen) : "")
              + $" · 선 바깥 최대 {b.FootMax:F2} m";
        note += $" · 교차각 {st.Cut.AngleA:F0}°/{st.Cut.AngleB:F0}°" + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "") + (st.Cut.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", st.Cut.Warn) : "")
                + (ringWas && s.Last != null && s.Pieces.All(p => p.Ring == null) ? " · 고리 면 못 지어 옛 방식으로 다시 지음" : "");   // ★[v116 · D2] 벽 기록 → 옛 엔진
        try { DiagLog.Append($"\n■ {Tag} {System.DateTime.Now:HH:mm:ss} — {note} · 옛 경계 {st.Cut.AreaOld:F1} = 새 {st.Cut.AreaNew:F1} + 잘라낼 {st.Cut.AreaCut:F1}㎡ · {st.Ms} ms\n{st.Log.TrimEnd()}"); } catch { }
        SelfCheckLog.Line(Tag, true, note, doc.Name);
        if (redraw) RoadCommand.RedrawStale(doc, s);
        return (no, note);
    }

    static (int, string) Stop(Document doc, string why)
    {
        try { DiagLog.Append($"\n■ {Tag} — 멈춤(안 바꿈): {why}"); } catch { }
        SelfCheckLog.Line(Tag, '⚠', "멈춤 — " + why, doc.Name);
        return (0, why);
    }

    /// <summary>★[v114.3] 대화 — [옹벽 추가](DHROADWALLADD). 바꿨으면 참(부르는 쪽이 도로 면 · 종단 · 횡단을 다시).</summary>
    internal static bool Run(Document doc, RoadSession s)
    {
        var ed = doc.Editor;
        var ol = OutlineOf(doc, s);
        if (ol.Bd == null || ol.Band == null) { ed.WriteMessage($"\n[{Tag}] {ol.Why} — 안 바뀜"); return false; }
        var bd = ol.Bd; var band = ol.Band; var bl = ol.Bl;
        var got = WallLineDraw.Run(doc, Tag, bd, band, pts => BoundaryCut.Classify(pts, bd, band, bl));
        if (got == null) return false;
        var d = s.Fresh();
        int no = NextNo(d);
        double tc = d.CutBenchW, tf = d.FillBenchW, reach = 40;
        // ★★[v117.x · JACK 1111 «옹벽 추가는 부지나 진입도로나 똑같은 로직» · 계획 `2026-11-12_진입도로_옹벽추가_부지로직.md` 4절] 새 벽 = 부지 [옹벽 추가]와 같은 화면 —
        //   기본 = 다단(1차 최고 ≤ 단높이면 저절로 수직) · [수직만(V)] · 단높이(Enter = 도로 사면 단높이) · 소단 폭(Enter = 설계 소단 폭) · 첫 소단(Enter = 자동).
        //   옛 «제원 절토(C) · 성토(F) · 천단(P)» 묻기는 없앰(옛 기록 Shape 0은 다시 지을 때 옛 모양 그대로)
        bool tierMode = true; double tH = double.NaN, tB = double.NaN, tZ = double.NaN;
        WallByLine.WallShape Shape() => tierMode ? WallByLine.WallShape.Tiered : WallByLine.WallShape.Vertical;
        Trial Geo() => Try(doc, s, got.Pts, no, tc, tf, reach, false, 9, Shape(), tH, tB, tZ);
        var t = Geo();
        double mk = System.Math.Max(0.3, System.Math.Sqrt(System.Math.Pow(bd.Max(q => q.X) - bd.Min(q => q.X), 2) + System.Math.Pow(bd.Max(q => q.Y) - bd.Min(q => q.Y), 2)) * 0.006);
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        while (true)
        {
            var st = t.St; var b = st?.Built;
            bool ok = st != null && st.Ok && b != null;
            bool tierUi = tierMode && b != null && b.Shape == 3;
            // 미리보기(부지와 같음) — 벽 띠 · 소단 자리 · 안쪽 단 머리 줄(초록 3) · L(빨강 1) · 가름점 C · 바깥 단 끊김(빨간 X)
            List<Autodesk.AutoCAD.DatabaseServices.Entity> live;
            if (ok)
            {
                var more = new List<IReadOnlyList<Point3>>();
                foreach (var z in b!.ZoneF.Concat(b.ZoneC)) more.Add(z.Append(z[0]).ToList());
                more.AddRange(b.TierRows);
                live = WallLineDraw.Show(b.Points.Select(q => (q.X, q.Y)).Concat(b.BreakAt), st!.Cut!.Line, mk, 2, more, 3);
                live.AddRange(WallLineDraw.Show(System.Array.Empty<(double, double)>(), null, mk, 2, b.Foot.Cast<IReadOnlyList<Point3>>(), 1));
            }
            else live = WallLineDraw.Show((st?.Cut?.Marks ?? new List<(double, double)>()).Concat(b?.Marks ?? new List<(double, double)>()), got.Cut.Line.Count >= 2 ? got.Cut.Line : got.Pts, mk);   // ★[v117.9.2] 거절 때도 경계 위 A..B 선(판정용 1 m 늘임은 안 보임)
            string easyWhy = st != null && !st.Ok ? WallEasy.Build(st.Code, st.Why) : t.Why;
            if (!ok && st != null && !st.Ok) { try { DiagLog.AppendCarry($"\n  [{Tag}] 거절(기술) — {st.Code}: {st.Why}"); } catch { } }
            // ★[v117.9.3 · JACK 1112 «침범했을 경우만 소단선 미리보기»] 다단 «자리» 거절 — 소단선(초록) · 바닥 침범 몫(빨강)
            var bb = !ok && st != null && !st.Ok ? st.Built : null;   // 다단 짓기 결과(실패도 실림 · 없으면 null)
            bool benchHit = bb != null && bb.TierBenchHit.Count > 0;
            if (benchHit)
            {
                live.AddRange(WallLineDraw.Show(System.Array.Empty<(double, double)>(), null, mk, 2, bb!.TierBenchOk, 3));
                live.AddRange(WallLineDraw.Show(System.Array.Empty<(double, double)>(), null, mk, 2, bb!.TierBenchHit, 1));
            }
            if (benchHit) easyWhy += " · 빨간 소단선 = 바닥 침범 자리";
            string spec = !ok ? $"〈⚠{easyWhy}〉"
                : b!.Shape == 3 ? string.Format(ic, "〈다단 {0}단 · 단높이 {1:0.##} · 소단 {2:0.##} · 바깥 최고 {3:F2} m{4}〉", b.TierN, b.TierH, b.TierB,
                    b.OuterHMax, b.BreakN > 0 ? string.Format(ic, " · 끊김 {0}곳 → 선을 둘로 나눠 그리기 권함", b.BreakN) : "")
                : string.Format(ic, "〈수직 1:{0} · 길이 {1:F2} m(절토 {2:F2} · 성토 {3:F2}) · 최고 {4:F2} m · 면적 {5:F1} ㎡{6}〉", b.Batter.ToString("0.###", ic), b.LineLen, b.LenC, b.LenF, b.HMax, b.Area,
                    b.TierNone ? string.Format(ic, " · 단높이 {0:0.##} 안 — 단 없이 됨", b.TierH) : "");
            if (ok && b!.Profile is { Preview.Length: > 0 } pf) ed.WriteMessage($"\n[{Tag}] {pf.Preview}");   // 측점표 한 줄(보고만 · 부지와 같음)
            PromptResult r;
            try
            {
                // ★[v117.14 · JACK 1112 «첫소단 등 안 쓰면 없애»] 첫소단(Z) 없앰 — 첫 소단은 늘 자동(부지와 같음)
                var pk = new PromptKeywordOptions(tierUi ? $"\n[{Tag}] {spec} [단높이(H)/소단폭(B)/수직만(V)/다시그리기(S)] (Enter = 적용): "
                    : !tierMode ? $"\n[{Tag}] {spec} [다단(T)/다시그리기(S)] (Enter = 적용): " : $"\n[{Tag}] {spec} [다시그리기(S)] (Enter = 적용): ");
                pk.Keywords.Add("S", "S", "다시그리기(S)");
                if (tierUi) { pk.Keywords.Add("H", "H", "단높이(H)"); pk.Keywords.Add("B", "B", "소단폭(B)"); pk.Keywords.Add("V", "V", "수직만(V)"); }
                else pk.Keywords.Add("V", "V", "수직만(V)", false, true);   // 숨김 — 스크립트의 V 한 줄이 단이 필요 없는 선에서도 그대로 지나가게(부지와 같음)
                if (!tierMode) pk.Keywords.Add("T", "T", "다단(T)");
                pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
                r = ed.GetKeywords(pk);
            }
            finally { WallLineDraw.Drop(live); }
            if (r.Status == PromptStatus.Cancel) { ed.WriteMessage($"\n[{Tag}] 취소 — 안 바뀜"); return false; }
            string kw = r.Status == PromptStatus.None ? "" : (r.StringResult ?? "").Trim().ToUpperInvariant();
            if (kw == "S")
            {
                var g2 = WallLineDraw.Run(doc, Tag, bd, band, pts => BoundaryCut.Classify(pts, bd, band, bl));
                if (g2 == null) return false;
                got = g2; t = Geo();
                continue;
            }
            if (kw == "V") { if (tierMode) { tierMode = false; t = Geo(); } continue; }
            if (kw == "T") { tierMode = true; t = Geo(); continue; }
            if (kw is "H" or "B")
            {
                double cur = b == null ? double.NaN : kw == "H" ? b.TierH : b.TierB;
                string now = double.IsFinite(cur) ? cur.ToString("0.##", ic) : "—";
                // Enter = 그대로
                var pdo = new PromptDoubleOptions(kw == "H" ? $"\n단높이 (m · 지금 {now} · Enter = 그대로)" : $"\n소단 폭 (m · 지금 {now} · Enter = 그대로)")
                { AllowNegative = false, AllowZero = kw != "H", AllowNone = true };
                var dr = ed.GetDouble(pdo);
                if (dr.Status == PromptStatus.OK)
                {
                    if (kw == "H") { if (dr.Value < 0.5 || dr.Value > 30) ed.WriteMessage("\n → 범위 밖 값(단높이 0.5~30 m) — 그대로"); else tH = dr.Value; }
                    else if (kw == "B") { if (dr.Value > 20) ed.WriteMessage("\n → 범위 밖 값(소단 폭 0~20 m) — 그대로"); else tB = dr.Value; }
                }
                t = Geo();
                continue;
            }
            if (!ok) { ed.WriteMessage($"\n[{Tag}] {easyWhy} · {(tierUi ? "단 값 바꾸기 · " : "")}다시 그리기(S)"); continue; }
            break;
        }
        ed.WriteMessage($"\n[{Tag}] {(t.St?.Built?.Shape == 3 ? "다단 옹벽" : "수직 옹벽")} 짓는 중…");
        var (made, note) = MakeShaped(doc, got.Pts, Shape(), tH, tB, tZ, got.SrcHandle, redraw: false);
        ed.WriteMessage(made > 0 ? $"\n[{Tag}] {note}" : $"\n[{Tag}] ⚠{note} — 안 바뀜");
        return made > 0;
    }
}
