using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DH.Grading.Core;
using NetTopologySuite.Geometries;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단» · 계획 5-4] 진입도로 [옹벽 변환] 폴리선(L) 길.
/// <para>부지와 같은 그리기 도우미(<see cref="WallLineDraw"/>) → 도로 지표면 바깥 고리(선 면 세모 발자국)와 정확히 두 점 판정(<see cref="BoundaryCut"/> · 바닥 = 도로 띠) →
/// 단높이 · 소단(Enter = 그쪽 사면 값) — 값마다 시험 계산(<see cref="RoadGrade.Input.WithBounds"/>)으로 단 수 · 쉬는 까닭을 보여 준다 →
/// <see cref="RoadDesign.BoundWalls"/>에 경계 두 교점 사이 폴리선(세계 XY)을 적고 저장 · 다시 계산(부르는 쪽이 도로 면 · 종단 · 횡단까지 다시).</para>
/// <para>화면 없는 탐침: <see cref="Make"/>(그린 점 · H · T — 질문 없음).</para></summary>
internal static class RoadWallLine
{
    const string Tag = "진입도로 옹벽 추가";

    /// <summary>도로 지표면 바깥 고리 · 도로 띠(가장자리 표본 고리) · 막을 다각형(부지 계획선). 빈 구간(토막 둘 이상)은 이번 판에 막는다.</summary>
    internal static (List<Point3>? Bd, List<Point3>? Band, List<(string Name, IReadOnlyList<Point3> Ring)> Bl, string Why) OutlineOf(Database db, RoadSession s)
    {
        var bl = new List<(string, IReadOnlyList<Point3>)>();
        if (s.Last == null || s.Last.Error.Length > 0) return (null, null, bl, "계산된 도로가 없습니다");
        if (s.Pieces.Count != 1 || s.Pieces[0].Inp == null) return (null, null, bl, s.Pieces.Count > 1 ? "빈 구간이 있는 도로(토막 둘 이상)는 이번 판에 폴리선 옹벽을 못 합니다" : "도로 입력을 못 찾았습니다");
        var inp = s.Pieces[0].Inp; var r = s.Last;
        var gf = NtsSupport.Factory();
        Polygon? fpP = null;
        try
        {
            var tris = RoadGrade.ExactSurface(inp, r, out _, out _);
            fpP = NtsSupport.LargestPolygon(RoadFootprint.FromTris(tris) ?? gf.CreatePolygon());
        }
        catch { fpP = null; }
        if (fpP == null || fpP.IsEmpty)
        {
            // 선 면을 못 지으면 칸 경계(바뀐 자리 1 · 2 · 3 · 4)로
            var ol = RoadGrade.Outline(r, k => k is 1 or 2 or 3 or 4).Where(l => l.Count >= 4).OrderByDescending(l => System.Math.Abs(NetTopologySuite.Algorithm.Area.OfRingSigned(l.Select(q => new Coordinate(q.X, q.Y)).ToArray()))).FirstOrDefault();
            if (ol != null) fpP = NtsSupport.ToCleanPolygon(ol.Select(q => new Point3(q.X, q.Y, 0)).ToList(), gf);
        }
        if (fpP == null || fpP.IsEmpty) return (null, null, bl, "도로 지표면 바깥 고리를 못 만들었습니다");
        List<Point3> R(LineString l) => l.Coordinates.Take(l.NumPoints - 1).Select(c => new Point3(c.X, c.Y, 0)).ToList();
        var bd = R(fpP.ExteriorRing);
        for (int i = 0; i < fpP.NumInteriorRings; i++) bl.Add(($"도로 지표면 안 구멍 {i + 1}", R(fpP.GetInteriorRingN(i))));
        // 도로 띠 — 왼쪽 가장자리 표본 → 오른쪽(거꾸로)
        var band = r.EdgeL.Select(q => new Point3(q.X, q.Y, 0)).Concat(Enumerable.Reverse(r.EdgeR).Select(q => new Point3(q.X, q.Y, 0))).ToList();
        var bandP = NtsSupport.ToCleanPolygon(band, gf);
        if (bandP == null || bandP.IsEmpty) return (null, null, bl, "도로 띠를 다각형으로 못 만들었습니다");
        // ★[v113.0 · 화면 없는 시험 1104] 띠 = 가장자리 표본 띠 ∩ 지표면 바깥 고리 — 표본 띠가 도로 양 끝에서 삼각형 면보다 튀어나와(JACK 1104 도면 0.12㎡ · 세 조각 · 폭 ≤ 0.28 m)
        //   판정 ⑥(바닥이 폴리선 한쪽에 통째로 드나)이 어느 폴리선이든 «부지 가름»으로 거절했다. 바닥은 자를 경계 안의 것만 본다.
        bandP = NtsSupport.LargestPolygon(bandP.Intersection(gf.CreatePolygon(fpP.Shell)));
        if (bandP == null || bandP.IsEmpty) return (null, null, bl, "도로 띠가 도로 지표면 바깥 고리 밖에 있습니다");
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs != null) for (int k = 0; k < regs.Count; k++) if (regs[k]?.Boundary is { Count: >= 3 } b) bl.Add(($"부지 계획선 {k + 1}", b));
            tr.Commit();
        }
        catch { }
        return (bd, R(bandP.ExteriorRing), bl, "");
    }

    /// <summary>시험 계산 — 지금 입력 + 이 폴리선 옹벽(번호 <paramref name="no"/>) · 그 옹벽 상태(엔진).</summary>
    internal static RoadGrade.BoundState Trial(RoadSession s, IReadOnlyList<Point3> line, int no, double h, double t)
    {
        var inp = s.Pieces[0].Inp;
        var bw = new RoadGrade.BoundWall(no, line.Select(q => q.X).ToArray(), line.Select(q => q.Y).ToArray(), h, t);
        var r = RoadGrade.Run(inp.WithBounds(inp.BoundWalls.Where(b => b.No != no).Append(bw)));
        if (r.Error.Length > 0) return new RoadGrade.BoundState { No = no, Why = "시험 계산 실패 — " + r.Error };
        return r.Bounds.LastOrDefault(b => b.No == no) ?? new RoadGrade.BoundState { No = no, Why = "시험 계산에 안 들어감" };
    }

    static int NextNo(RoadDesign d) => (d.BoundWalls == null || d.BoundWalls.Count == 0 ? 0 : d.BoundWalls.Max(b => b.No)) + 1;

    /// <summary>★[v113.0 · 화면 없는 탐침 · 대화 끝] 그린 점 · H · T → 판정 · 시험 계산 → 기록 · 저장 · 다시 계산(질문 없음). 번호(0 = 못 함 · 아무것도 안 바꿈) · 글.
    /// <paramref name="redraw"/>면 끝에 도로 면 · 종단 · 횡단까지 다시(<see cref="RoadCommand.RedrawStale"/> — 명령 [옹벽 변환]은 제 끝에서 한다).</summary>
    internal static (int No, string Note) Make(Document doc, IReadOnlyList<Point3> src, double h, double t, string srcHandle = "", bool redraw = true)
    {
        var s = RoadSession.Of(doc);
        if (s == null) return (0, "열린 진입도로가 없습니다(진입도로 창)");
        s.RecomputeIfDirty();
        var (bd, band, bl, why) = OutlineOf(doc.Database, s);
        if (bd == null || band == null) return Stop(doc, why);
        var cut = BoundaryCut.Classify(src, bd, band, bl);
        if (!cut.Ok) return Stop(doc, $"{BoundaryCut.CodeName(cut.Code)} — {cut.Why}");
        var d = s.Fresh();
        int no = NextNo(d);
        var st = Trial(s, cut.Line, no, h, t);
        if (!st.Ok) return Stop(doc, st.Why);
        d.BoundWalls ??= new List<RoadDesign.BoundWallRec>();
        d.BoundWalls.Add(new RoadDesign.BoundWallRec
        {
            No = no, X = cut.Line.Select(q => q.X).ToArray(), Y = cut.Line.Select(q => q.Y).ToArray(),
            SrcX = src.Select(q => q.X).ToArray(), SrcY = src.Select(q => q.Y).ToArray(), H = h, T = t, SrcHandle = srcHandle ?? "",
        });
        s.SaveDesign(); s.Recompute();
        string note = $"폴리선 옹벽 {no} — {(st.Side < 0 ? "좌" : "우")} · {(st.Up ? "절토" : "성토")} {st.Tiers}단 · 측점 {st.S0 + s.S0:F1} ~ {st.S1 + s.S0:F1} m · 바닥 {st.FloorMin:F2}~{st.FloorMax:F2} m · 단높이 {h:0.##} · 소단 {t:0.##} m"
                    + $" · 교차각 {cut.AngleA:F0}°/{cut.AngleB:F0}°" + (cut.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", cut.Warn) : "");
        try { DiagLog.Append($"\n■ {Tag} {System.DateTime.Now:HH:mm:ss} — {note} · 옛 경계 {cut.AreaOld:F1} = 새 {cut.AreaNew:F1} + 잘라낼 {cut.AreaCut:F1}㎡"); } catch { }
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

    /// <summary>★[v113.0] 대화 — [옹벽 추가](DHROADWALLADD · JACK 1104 — 옛 [옹벽 변환]의 L). 바꿨으면 참(부르는 쪽이 도로 면 · 종단 · 횡단을 다시).</summary>
    internal static bool Run(Document doc, RoadSession s)
    {
        var ed = doc.Editor;
        var (bd, band, bl, why) = OutlineOf(doc.Database, s);
        if (bd == null || band == null) { ed.WriteMessage($"\n[{Tag}] {why} — 아무것도 안 바꿨습니다."); return false; }
        var got = WallLineDraw.Run(doc, Tag, bd, band, pts => BoundaryCut.Classify(pts, bd, band, bl));
        if (got == null) return false;
        var d = s.Fresh();
        int no = NextNo(d);
        // 절 · 성은 시험 계산이 정한다(폴리선 위 원지반 − 바닥) — 기본값 = 그쪽 사면 단높이 · 소단
        var st = Trial(s, got.Cut.Line, no, d.CutBenchH, d.CutBenchW);
        bool up = st.Ok || st.Tiers > 0 ? st.Up : true;   // 방향을 못 정한 거절(섞임 · 원지반)은 절토 기본값
        double h = up ? d.CutBenchH : d.FillBenchH, t = up ? d.CutBenchW : d.FillBenchW;
        if (!up) st = Trial(s, got.Cut.Line, no, h, t);
        double mk = System.Math.Max(0.3, System.Math.Sqrt(System.Math.Pow(bd.Max(q => q.X) - bd.Min(q => q.X), 2) + System.Math.Pow(bd.Max(q => q.Y) - bd.Min(q => q.Y), 2)) * 0.006);
        while (true)
        {
            var live = WallLineDraw.Show(st.Ok ? System.Array.Empty<(double, double)>() : got.Cut.Marks, got.Cut.Line, mk, 2,
                                         new[] { (IReadOnlyList<Point3>)got.Cut.NewRing.Append(got.Cut.NewRing[0]).ToList(), got.Cut.CutRing.Append(got.Cut.CutRing[0]).ToList() }, 3);
            string spec = st.Ok ? $"〈단높이 {h:0.##}m · 소단 {t:0.##}m · 수직 · {(st.Up ? "절토" : "성토")} {st.Tiers}단 · 바닥 {st.FloorMin:0.##}~{st.FloorMax:0.##}m〉" : $"〈⚠{st.Why}〉";
            PromptResult r;
            try
            {
                var pk = new PromptKeywordOptions($"\n[{Tag}] 제원 {spec} [단높이(H)/소단길이(T)/다시그리기(S)] (Enter = 적용): ");
                pk.Keywords.Add("H", "H", "단높이(H)"); pk.Keywords.Add("T", "T", "소단길이(T)"); pk.Keywords.Add("S", "S", "다시그리기(S)");
                pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
                r = ed.GetKeywords(pk);
            }
            finally { WallLineDraw.Drop(live); }
            if (r.Status == PromptStatus.Cancel) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return false; }
            string kw = r.Status == PromptStatus.None ? "" : (r.StringResult ?? "").Trim().ToUpperInvariant();
            if (kw == "H" || kw == "T")
            {
                var pdo = new PromptDoubleOptions(kw == "H" ? "\n단높이 (m)" : "\n소단 길이 (m)") { DefaultValue = kw == "H" ? h : t, UseDefaultValue = true, AllowNegative = false, AllowZero = kw == "T" };
                var dr = ed.GetDouble(pdo);
                if (dr.Status == PromptStatus.OK)
                {
                    if (kw == "H" && dr.Value >= 0.2 && dr.Value <= 15) h = dr.Value;
                    else if (kw == "T" && dr.Value <= 60) t = dr.Value;
                    else ed.WriteMessage("\n → 범위 밖 값입니다(단높이 0.2~15 · 소단 0~60 m).");
                }
                st = Trial(s, got.Cut.Line, no, h, t);
                continue;
            }
            if (kw == "S")
            {
                var g2 = WallLineDraw.Run(doc, Tag, bd, band, pts => BoundaryCut.Classify(pts, bd, band, bl));
                if (g2 == null) return false;
                got = g2; st = Trial(s, got.Cut.Line, no, h, t);
                continue;
            }
            if (!st.Ok) { ed.WriteMessage($"\n[{Tag}] {st.Why} — 값을 바꾸거나(H · T) 다시 그리세요(S)."); continue; }
            break;
        }
        var (made, note) = Make(doc, got.Pts, h, t, got.SrcHandle, redraw: false);
        ed.WriteMessage(made > 0 ? $"\n[{Tag}] {note}" : $"\n[{Tag}] ⚠{note} — 아무것도 안 바꿨습니다.");
        return made > 0;
    }
}
