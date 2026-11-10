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
        if (GradingSettings.WallLineOff || WallByLine.Off || RoadGrade.WallOff) { t.Why = "⚠선 옹벽(v114)이 꺼져 있습니다(정지옵션 WallLineOff)"; return t; }
        var (inp, r, p0, lines, path, why, ring) = s.P0Now();
        if (inp == null || r == null || p0 == null || lines == null) { t.Why = why; return t; }
        if (path == "칸 면") { t.Why = "칸 면 위에는 선 옹벽을 안 짓습니다(이 도로는 선 면 · 선 따라 TIN을 못 지음)"; return t; }
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
    internal static Trial Try(Document doc, RoadSession s, IReadOnlyList<Point3> src, int no, double tc, double tf, double reach, bool topStep = false, double blockLen = 9)
    {
        var t = new Trial();
        var (inp, r, p0, lines, path, why, ring) = s.P0Now();
        if (inp == null || r == null || p0 == null || lines == null) { t.Why = why; return t; }
        var all = inp.LineWalls.Where(w => w.No != no).Append(new RoadGrade.LineWall(no, src.Select(q => q.X).ToArray(), src.Select(q => q.Y).ToArray(), tc, tf, reach, topStep, topStep ? blockLen : 0)).ToList();
        t.Ws = RoadGrade.ApplyWalls(inp, r, p0, lines, path, all, SiteBlockers(doc.Database), ring: ring);
        t.St = t.Ws.Walls.LastOrDefault(w => w.No == no);
        if (t.St == null) t.Why = "시험 짓기에 안 들어감"; else if (!t.St.Ok) t.Why = t.St.Why;
        return t;
    }

    static int NextNo(RoadDesign d) => (d.BoundWalls == null || d.BoundWalls.Count == 0 ? 0 : d.BoundWalls.Max(b => b.No)) + 1;

    /// <summary>★[v114.3 · 화면 없는 탐침 · 대화 끝] 그린 점 · 소단 폭 → 시험 짓기 → 판 2 기록 · 저장 · 다시 계산(질문 없음). 번호(0 = 못 함 · 아무것도 안 바꿈) · 글.
    /// <paramref name="redraw"/>면 끝에 도로 면 · 종단 · 횡단까지 다시(<see cref="RoadCommand.RedrawStale"/> — 명령은 제 끝에서 한다).</summary>
    internal static (int No, string Note) Make(Document doc, IReadOnlyList<Point3> src, double tc, double tf, string srcHandle = "", bool redraw = true, double reach = 40, bool topStep = false, double blockLen = 9)
    {
        var s = RoadSession.Of(doc);
        if (s == null) return (0, "열린 진입도로가 없습니다(진입도로 창)");
        s.RecomputeIfDirty();
        bool ringWas = s.Pieces.Any(p => p.Ring != null);   // ★[v116 · 고리 엔진 §5] 벽을 넣기 전 고리 엔진이었나(넣으면 옛 엔진 — 안내 한 줄)
        var d = s.Fresh();
        int no = NextNo(d);
        var t = Try(doc, s, src, no, tc, tf, reach, topStep, blockLen);
        if (t.St == null || !t.St.Ok) return Stop(doc, t.Why.Length > 0 ? t.Why : "못 지음");
        var st = t.St;
        var rec = new RoadDesign.BoundWallRec
        {
            No = no, X = st.Cut!.Line.Select(q => q.X).ToArray(), Y = st.Cut.Line.Select(q => q.Y).ToArray(),
            SrcX = src.Select(q => q.X).ToArray(), SrcY = src.Select(q => q.Y).ToArray(), Side = st.Side, Tc = tc, Tf = tf, Reach = reach, SrcHandle = srcHandle ?? "",
            TopStep = topStep, BlockLen = topStep ? blockLen : 0,
        };
        rec.Sig = rec.SigNow();
        d.BoundWalls ??= new List<RoadDesign.BoundWallRec>();
        d.BoundWalls.Add(rec);
        s.SaveDesign(); s.Recompute();
        var b = st.Built!;
        string note = $"추가한 옹벽 {no} — {(st.Side < 0 ? "좌" : st.Side > 0 ? "우" : "끝")} · {string.Join("+", new[] { b.HasFill ? "성토" : "", b.HasCut ? "절토" : "" }.Where(q => q.Length > 0))} · 선 {b.LineLen:F1} m · 단높이 = 도로 사면 · 소단 절토 {tc:0.##} / 성토 {tf:0.##} m"
                    + (topStep ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " · 천단 계단 {0:0.##} m", blockLen) : "")
                    + $" · 선 바깥 최대 {b.FootMax:F2} m" + $" · 교차각 {st.Cut.AngleA:F0}°/{st.Cut.AngleB:F0}°" + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "") + (st.Cut.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", st.Cut.Warn) : "")
                    + (ringWas && s.Last != null && s.Pieces.All(p => p.Ring == null) ? " · 고리 면을 못 지어 옛 방식으로 다시 지었습니다" : "");   // ★[v116 · D2] 벽 기록 → 옛 엔진
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
        if (ol.Bd == null || ol.Band == null) { ed.WriteMessage($"\n[{Tag}] {ol.Why} — 아무것도 안 바꿨습니다."); return false; }
        var bd = ol.Bd; var band = ol.Band; var bl = ol.Bl;
        var got = WallLineDraw.Run(doc, Tag, bd, band, pts => BoundaryCut.Classify(pts, bd, band, bl));
        if (got == null) return false;
        var d = s.Fresh();
        int no = NextNo(d);
        double tc = d.CutBenchW, tf = d.FillBenchW, reach = 40;
        bool topStep = false; double blockLen = 9;   // ★[v116.3 · JACK 1109 옹벽 천단 선택] 기본 = 경사
        var t = Try(doc, s, got.Pts, no, tc, tf, reach, topStep, blockLen);
        double mk = System.Math.Max(0.3, System.Math.Sqrt(System.Math.Pow(bd.Max(q => q.X) - bd.Min(q => q.X), 2) + System.Math.Pow(bd.Max(q => q.Y) - bd.Min(q => q.Y), 2)) * 0.006);
        while (true)
        {
            var st = t.St; var b = st?.Built;
            bool ok = st != null && st.Ok && b != null;
            List<Autodesk.AutoCAD.DatabaseServices.Entity> live;
            if (ok)
            {
                var more = new List<IReadOnlyList<Point3>>();
                foreach (var z in b!.ZoneF.Concat(b.ZoneC)) more.Add(z.Append(z[0]).ToList());
                live = WallLineDraw.Show(b.Points.Select(q => (q.X, q.Y)), st!.Cut!.Line, mk, 2, more, 3);
                live.AddRange(WallLineDraw.Show(System.Array.Empty<(double, double)>(), null, mk, 2, b.Rows.Concat(b.EndFaces).Cast<IReadOnlyList<Point3>>(), 8));
                live.AddRange(WallLineDraw.Show(System.Array.Empty<(double, double)>(), null, mk, 2, b.Foot.Cast<IReadOnlyList<Point3>>(), 1));
            }
            else live = WallLineDraw.Show(st?.Cut?.Marks ?? new List<(double, double)>(), got.Pts, mk);
            string foot = ok ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " · 선 바깥 최대 {0:F2} m{1}", b!.FootMax, b.FootMax > 0 ? " ⚠" : "") : "";
            string sides = ok ? "단높이 = 도로 사면 · " + (b!.HasCut ? $"절토 소단 {tc:0.##}" : "") + (b.HasCut && b.HasFill ? " / " : "") + (b.HasFill ? $"성토 소단 {tf:0.##}" : "") : "";
            string top = topStep ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " · 천단 계단 {0:0.##} m", blockLen) : " · 천단 경사";
            string spec = ok ? $"〈{sides}{top}{foot}〉" : $"〈⚠{t.Why}〉";
            PromptResult r;
            try
            {
                bool hc = !ok || b!.HasCut, hf = !ok || b!.HasFill;
                var pk = new PromptKeywordOptions($"\n[{Tag}] 제원 {spec} [{(hc ? "절토(C)/" : "")}{(hf ? "성토(F)/" : "")}천단(P)/다시그리기(S)] (Enter = 적용): ");
                if (hc) pk.Keywords.Add("C", "C", "절토(C)");
                if (hf) pk.Keywords.Add("F", "F", "성토(F)");
                pk.Keywords.Add("P", "P", "천단(P)");
                pk.Keywords.Add("S", "S", "다시그리기(S)");
                pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
                r = ed.GetKeywords(pk);
            }
            finally { WallLineDraw.Drop(live); }
            if (r.Status == PromptStatus.Cancel) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return false; }
            string kw = r.Status == PromptStatus.None ? "" : (r.StringResult ?? "").Trim().ToUpperInvariant();
            if (kw == "C" || kw == "F")
            {
                bool c = kw == "C";
                var pdo = new PromptDoubleOptions($"\n{(c ? "절토" : "성토")} 소단 길이 (m)") { DefaultValue = c ? tc : tf, UseDefaultValue = true, AllowNegative = false, AllowZero = true };
                var dr = ed.GetDouble(pdo);
                if (dr.Status == PromptStatus.OK)
                {
                    if (dr.Value > 60) ed.WriteMessage("\n → 범위 밖 값입니다(소단 0~60 m).");
                    else if (c) tc = dr.Value; else tf = dr.Value;
                }
                t = Try(doc, s, got.Pts, no, tc, tf, reach, topStep, blockLen);
                continue;
            }
            if (kw == "P")
            {
                (topStep, blockLen) = WallLineFlow.AskTop(ed, topStep, blockLen);   // ★[v116.3 · JACK 1109 옹벽 천단 선택]
                t = Try(doc, s, got.Pts, no, tc, tf, reach, topStep, blockLen);
                continue;
            }
            if (kw == "S")
            {
                var g2 = WallLineDraw.Run(doc, Tag, bd, band, pts => BoundaryCut.Classify(pts, bd, band, bl));
                if (g2 == null) return false;
                got = g2; t = Try(doc, s, got.Pts, no, tc, tf, reach, topStep, blockLen);
                continue;
            }
            if (!ok) { ed.WriteMessage($"\n[{Tag}] {t.Why} — 값을 바꾸거나(C · F) 다시 그리세요(S)."); continue; }
            if (b!.FootMax > 0) ed.WriteMessage(string.Format(System.Globalization.CultureInfo.InvariantCulture, "\n[{0}] ⚠선 바깥으로 최대 {1:F2} m 나갑니다({2:F1}, {3:F1}) — 막지 않습니다.", Tag, b.FootMax, b.FootAt.X, b.FootAt.Y));
            break;
        }
        var (made, note) = Make(doc, got.Pts, tc, tf, got.SrcHandle, redraw: false, reach: reach, topStep: topStep, blockLen: blockLen);
        ed.WriteMessage(made > 0 ? $"\n[{Tag}] {note}" : $"\n[{Tag}] ⚠{note} — 아무것도 안 바꿨습니다.");
        return made > 0;
    }
}
