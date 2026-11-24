using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>★[JACK 1123] 닫힌 선 → 두 갈래(출구 집수정) · 합류 · 교차 집수정.</summary>
public class UDitchJunctionTests
{
    static readonly UDitch.Settings S = new();

    static UDitch.Ditch Make(string id, Func<double, double, double> gr, params (double X, double Y)[] p)
    {
        var d = new UDitch.Ditch { Id = id, Follow = true, InvertAuto = true, Pts = p.Select(q => new[] { q.X, q.Y, gr(q.X, q.Y) }).ToList() };
        UDitch.FillSamples(d, gr, S);
        UDitch.ApplyAuto(d, S);
        return d;
    }

    static (UDitch.Record Rec, UDitch.LoopSplit Sp) Loop(Func<double, double, double> gr, params (double X, double Y)[] ring)
    {
        var sp = UDitch.SplitLoop(ring.Select(q => new[] { q.X, q.Y, gr(q.X, q.Y) }).ToList(), gr, S, S.BasinInner + 2 * S.BasinWall)!;
        var rec = new UDitch.Record();
        foreach (var (pts, k) in new[] { (sp.A, 1), (sp.B, 2) })
        {
            var d = new UDitch.Ditch { Id = UDitch.LoopId("D-001", k), LineHandle = "AB", Loop = k, Follow = true, InvertAuto = true, Pts = pts, LoopNote = sp.Note };
            UDitch.FillSamples(d, gr, S);
            UDitch.ApplyAuto(d, S);
            rec.Ditches.Add(d);
        }
        UDitch.Link(rec, S);
        return (rec, sp);
    }

    static readonly (double, double)[] Rect = { (0, 0), (50, 0), (50, 30), (0, 30) };

    [Fact]
    public void Closed_line_splits_at_lowest_and_highest_and_uses_all_four_sides()
    {
        // 가장 낮은 곳 = (50, 0) · 가장 높은 곳 = (0, 30)
        var (rec, sp) = Loop((x, y) => 100 - 0.02 * x + 0.01 * y, Rect);
        Assert.False(sp.Flat);
        Assert.Equal(160, sp.Perim, 6);
        Assert.Equal(50, sp.SOut, 6);
        Assert.Equal(130, sp.SStart, 6);
        var a = rec.Ditches[0]; var b = rec.Ditches[1];
        Assert.Equal("D-001-1", a.Id); Assert.Equal("D-001-2", b.Id);
        Assert.Equal(new[] { 0.0, 30 }, a.Pts[0].Take(2)); Assert.Equal(new[] { 50.0, 0 }, a.Pts[^1].Take(2));
        Assert.Equal(new[] { 0.0, 30 }, b.Pts[0].Take(2)); Assert.Equal(new[] { 50.0, 0 }, b.Pts[^1].Take(2));
        // 네 변 모두 — 두 갈래 선 길이 합 = 둘레
        Assert.Equal(160, UDitch.Qty(a).Length + UDitch.Qty(b).Length, 6);
        // 출구 집수정 하나 · 두 갈래 끝 · 몸체는 집수정 벽에서 끊김
        var j = Assert.Single(rec.Junctions);
        Assert.Equal("출구", j.Kind);
        Assert.Equal(50, j.B.X, 6); Assert.Equal(0, j.B.Y, 6);
        Assert.Empty(j.Warn);   // ★[검토 L10] 출구 알림은 보고서에만
        Assert.Equal("J1 출구 — 내보낼 곳 없음 → 관 · 측구 연결 확인", Assert.Single(j.Notes));
        Assert.All(j.M, m => Assert.Equal("end", m.Role));
        double h = j.B.Outer / 2;
        foreach (var d in rec.Ditches)
        {
            var lay = UDitch.LayoutOf(d, S);
            Assert.Equal("", lay.Error);
            var c = Assert.Single(lay.Cuts);
            Assert.Equal(80 - h, c.A, 6);           // 모서리 집수정 — 바깥 반폭에서 끊음
            Assert.Equal(80 - h, lay.ULength, 6);
            Assert.NotEmpty(UDitch.Meshes(d, S, out string why));
            Assert.Equal("", why);
        }
        // 안바닥 = 두 갈래 끝 바닥 중 낮은 것 − 침전
        Assert.Equal(j.M.Min(m => m.InV) - S.BasinSump, j.B.Floor, 6);
        Assert.True(j.B.ZTop > j.B.Floor && j.Volume > 0);
        // 시작 마구리 맞댐 — 두 갈래 첫 단면이 같은 직선(둘레 이등분 = 모서리 (0,30)의 45°)
        var ma = UDitch.Meshes(a, S, out _).First(x => x.Part == "본체");
        var mb = UDitch.Meshes(b, S, out _).First(x => x.Part == "본체");
        var fa = ma.V.Take(4).ToList(); var fb = mb.V.Take(4).ToList();
        foreach (var v in fa.Concat(fb)) Assert.Equal(v.X, -(v.Y - 30), 6);   // x = 30 − y 직선(이등분)
        // 보고서 · 창 줄
        string rep = UDitch.Report(rec, S, "t.dwg", "test");
        Assert.Contains(UDitch.LoopText, rep);
        Assert.Contains("J1(출구 · D-001-1 + D-001-2)", rep);
        Assert.Contains("알림: J1 출구 — 내보낼 곳 없음 → 관 · 측구 연결 확인", rep);
        Assert.DoesNotContain(UDitch.ProfileOf(a, S).Warn, w => w.Contains("내보낼 곳"));
        var bt = UDitch.BasinTotals(rec, S);   // ★[검토 M3] 보고서 합계 = 수량산출서
        Assert.Equal(1, bt.Count);
        Assert.Contains(string.Format(System.Globalization.CultureInfo.InvariantCulture, "집수정 {0}개 {1:0.000} ㎥", bt.Count, bt.Volume), rep);
        Assert.Contains(UDitch.InfoRows(a, rec.Rain, S), r => r.K == "닫힌 선");
    }

    [Fact]
    public void Acute_outlet_corner_basin_turns_so_both_branches_enter_alike()
    {
        // 정삼각형 — 출구 = 꼭대기(60° 모서리) · 시작 = 밑변 가운데 → 두 갈래가 똑같이 비스듬히(각 15°) 들어오고 끊는 길이도 같음
        double t = 20 * Math.Sqrt(3) / 2;
        var (rec, sp) = Loop((x, y) => 100 - 0.02 * y - 0.001 * Math.Abs(x - 10), (0, 0), (20, 0), (10, t));
        Assert.Equal(10, sp.SStart, 6);
        var j = Assert.Single(rec.Junctions);
        Assert.Equal(10, j.B.X, 6); Assert.Equal(t, j.B.Y, 6);
        double l1 = UDitch.LayoutOf(rec.Ditches[0], S).ULength, l2 = UDitch.LayoutOf(rec.Ditches[1], S).ULength;
        Assert.Equal(l1, l2, 6);
        double rot = Math.Atan2(j.B.Ey, j.B.Ex) * 180 / Math.PI;
        Assert.Equal(0, Math.IEEERemainder(rot - 45, 90), 6);
    }

    [Fact]
    public void Flat_loop_uses_first_vertex_as_outlet_and_opposite_as_start()
    {
        var (rec, sp) = Loop((x, y) => 50.0, Rect);
        Assert.True(sp.Flat);
        Assert.Equal(0, sp.SOut, 6);
        Assert.Equal(80, sp.SStart, 6);
        Assert.Contains("평평", sp.Note);
        var j = Assert.Single(rec.Junctions);
        Assert.Equal(0, j.B.X, 6); Assert.Equal(0, j.B.Y, 6);
    }

    [Fact]
    public void Lowest_point_near_corner_snaps_to_corner()
    {
        // 가장 낮은 곳이 (49.4, 0) — 표본 0.25 m · 모서리 (50, 0)에서 집수정 바깥폭(0.9 m) 안 → 모서리로
        Func<double, double, double> gr = (x, y) => 100 + Math.Abs(x - 49.4) * 0.02 + 0.01 * y;
        var s = new UDitch.Settings { SampleStep = 0.25 };
        var sp = UDitch.SplitLoop(Rect.Select(q => new[] { q.Item1, q.Item2, gr(q.Item1, q.Item2) }).ToList(), gr, s, 0.9)!;
        Assert.Equal(50, sp.SOut, 6);
        Assert.Contains("꼭짓점으로 0.50 m", sp.Note);
        // 바깥폭 밖이면 그대로
        var sp2 = UDitch.SplitLoop(Rect.Select(q => new[] { q.Item1, q.Item2, gr(q.Item1, q.Item2) }).ToList(), gr, s, 0.3)!;
        Assert.Equal(49.5, sp2.SOut, 6);
    }

    [Fact]
    public void Tee_gets_one_junction_basin_and_through_ditch_is_cut_both_sides()
    {
        Func<double, double, double> gr = (x, y) => 100 - 0.01 * x + 0.02 * y;
        var d1 = Make("D-001", gr, (0, 0), (40, 0));
        var d2 = Make("D-002", gr, (20, 20), (20, 0.3));   // 끝이 D-001에서 0.3 m
        var rec = new UDitch.Record { Ditches = { d1, d2 } };
        UDitch.Link(rec, S);
        var j = Assert.Single(rec.Junctions);
        Assert.Equal("합류", j.Kind);
        Assert.Equal("J1(합류 · D-002 → D-001)", j.Label);
        Assert.Equal(20, j.B.X, 6); Assert.Equal(0, j.B.Y, 6);
        double h = j.B.Outer / 2;
        var c1 = Assert.Single(d1.Cuts); Assert.Equal(20 - h, c1.A, 6); Assert.Equal(20 + h, c1.B, 6);
        var c2 = Assert.Single(d2.Cuts); Assert.Equal(20 - h, c2.A, 6); Assert.Equal(19.7, c2.B, 6);   // 끝(y 0.3)에서 집수정 벽(y 0.45)까지
        var l1 = UDitch.LayoutOf(d1, S);
        Assert.Equal(2, l1.Segs.Count);
        Assert.Equal(40 - 2 * h, l1.ULength, 6);
        Assert.Equal(Math.Min(Math.Min(j.M[0].InV, j.M[0].OutV), j.M[1].InV) - S.BasinSump, j.B.Floor, 6);
        Assert.Empty(j.Warn);   // D-001 하류 바닥(내려감) ≤ 들어오는 바닥
        // 안치수 = max(0.6, 안폭 0.3 + 2 × 0.15)
        Assert.Equal(0.6, j.B.Inner, 6);
        // 솔리드 · 3D 칸
        Assert.Equal(2, UDitch.JunctionSolids(j, S).Count);
        var parts = UDitch.JunctionParts(rec, S, 0, 0);
        Assert.Equal("D-001", Assert.Single(parts).Owner);
    }

    [Fact]
    public void Crossing_gets_basin_and_reverse_flow_warning()
    {
        // D-003이 D-001보다 1 m 높이 지나감 → D-003 하류 바닥 > D-001 들어오는 바닥 → 알림
        var d1 = Make("D-001", (x, y) => 100 - 0.01 * x, (0, 0), (40, 0));
        var d3 = Make("D-003", (x, y) => 101 - 0.01 * (y + 20), (20, -20), (20, 20));
        var rec = new UDitch.Record { Ditches = { d1, d3 } };
        UDitch.Link(rec, S);
        var j = Assert.Single(rec.Junctions);
        Assert.Equal("교차", j.Kind);
        Assert.Equal("J1(교차 · D-001 × D-003)", j.Label);
        Assert.Equal(2, j.M.Count(m => m.Role == "cross"));
        Assert.Contains(j.Warn, w => w.Contains("D-003") && w.Contains("물이 거꾸로"));
        Assert.Contains(UDitch.ProfileOf(d3, S).Warn, w => w.Contains("물이 거꾸로"));
    }

    [Fact]
    public void Flat_ground_through_ditch_never_climbs_after_junction()
    {
        // ★[검토 H1] 평지(지표 70.000) — 집수정 뒤 하류 토막이 지표 − 높이로 다시 올라가면 역단차. 합류(20 m) · 교차(30 m) 둘 다
        Func<double, double, double> gr = (x, y) => 70.0;
        var d1 = Make("D-001", gr, (0, 0), (40, 0));
        var d2 = Make("D-002", gr, (20, 20), (20, 0.3));
        var d3 = Make("D-003", gr, (30, -15), (30, 15));
        var rec = new UDitch.Record { Ditches = { d1, d2, d3 } };
        UDitch.Link(rec, S);
        Assert.Equal(2, rec.Junctions.Count);
        double smin = S.MinSlopePct / 100;
        foreach (var d in rec.Ditches)
        {
            var lay = UDitch.LayoutOf(d, S);
            for (int i = 1; i < lay.Segs.Count; i++)
                Assert.True(lay.Segs[i].V0 <= lay.Segs[i - 1].V1 - smin * (lay.Segs[i].S0 - lay.Segs[i - 1].S1) + 1e-9,
                            $"{d.Id} 구간 {i + 1} 시작 {lay.Segs[i].V0:F4} > 앞 끝 {lay.Segs[i - 1].V1:F4}");
            Assert.All(lay.Segs, g => Assert.True(g.SMinPct >= S.MinSlopePct - 1e-6));
        }
        Assert.Equal(3, UDitch.LayoutOf(d1, S).Segs.Count);
        Assert.DoesNotContain(rec.Junctions.SelectMany(j => j.Warn), w => w.Contains("D-001 나가는") || w.Contains("D-002 나가는"));
    }

    [Fact]
    public void Overlapping_basins_merge_into_one()
    {
        // ★[검토 M2] 0.7 m 떨어진 두 합류(묶는 거리 0.5 m < 0.7 < 바깥 0.9) → 집수정 하나 · 한 번만 셈
        Func<double, double, double> gr = (x, y) => 100 - 0.01 * x;
        var d1 = Make("D-001", gr, (0, 0), (40, 0));
        var d2 = Make("D-002", gr, (20, 20), (20, 0.3));
        var d4 = Make("D-004", gr, (20.7, -15), (20.7, -0.3));
        var rec = new UDitch.Record { Ditches = { d1, d2, d4 } };
        UDitch.Link(rec, S);
        var j = Assert.Single(rec.Junctions);
        Assert.Equal("J1(합류 · D-002 + D-004 → D-001)", j.Label);
        Assert.Equal(20.35, j.B.X, 6);
        Assert.Single(d1.Cuts);
        Assert.Equal(1, UDitch.BasinTotals(rec, S).Count);
    }

    [Fact]
    public void Loop_sync_keeps_hand_inverts()
    {
        // ★[검토 M1] 짝 맞춤은 손 바닥고를 안 지움
        var (rec, _) = Loop((x, y) => 100 - 0.02 * x + 0.01 * y, Rect);
        var a = rec.Ditches[0]; var b = rec.Ditches[1];
        b.InvertAuto = false; b.StartInvert = 50; b.EndInvert = 49;
        a.Size = S.Sizes[1].Copy();
        UDitch.SyncLoop(rec, a, S);
        Assert.Equal("400×400", b.Size.Label);
        Assert.False(b.InvertAuto);
        Assert.Equal(50, b.StartInvert); Assert.Equal(49, b.EndInvert);
    }

    [Fact]
    public void Starts_only_meeting_is_a_divide_without_basin()
    {
        Func<double, double, double> gr = (x, y) => 100 - 0.01 * Math.Abs(x);
        var a = Make("D-001", gr, (0, 0), (30, 0));
        var b = Make("D-002", gr, (0, 0), (-30, 0));
        var rec = new UDitch.Record { Ditches = { a, b } };
        UDitch.Link(rec, S);
        Assert.Empty(rec.Junctions);
    }

    [Fact]
    public void Loop_record_is_version_3_and_round_trips()
    {
        var (rec, _) = Loop((x, y) => 100 - 0.02 * x + 0.01 * y, Rect);
        string js = UDitch.ToJson(rec);
        Assert.True(UDitch.TryParse(js, out var back, out string why), why);
        Assert.Equal(3, back.Version);
        Assert.Equal(new[] { 1, 2 }, back.Ditches.Select(d => d.Loop));
        Assert.All(back.Ditches, d => Assert.Contains("가장 낮은 곳", d.LoopNote));
        Assert.Equal("D-002", UDitch.NextId(back.Ditches));
        Assert.Equal("D-007-2", UDitch.LoopId("D-007-1", 2));
    }

    [Fact]
    public void Basin_list_is_one_source_for_count_groups_and_report()
    {
        // ★[JACK 1124 «집수정 블록 · 수량»] 합류 하나(D-002 → D-001) + 단차 측구(D-003 — 3 m 단) → 목록 = 합계 = 규격별 합 = 보고서
        Func<double, double, double> gr = (x, y) => 100 - 0.01 * x + 0.02 * y;
        var d1 = Make("D-001", gr, (0, 0), (40, 0));
        var d2 = Make("D-002", gr, (20, 20), (20, 0.3));
        Func<double, double, double> step = (x, y) => x < 30 ? 143.1 - 0.1 * x / 29 : 140.1 - 0.1 * (x - 30) / 30;
        var d3 = Make("D-003", step, (0, -50), (29, -50), (30, -50), (60, -50));
        var rec = new UDitch.Record { Ditches = { d1, d2, d3 } };
        UDitch.Link(rec, S);
        var l = UDitch.BasinList(rec, S);
        int steps = UDitch.LayoutOf(d3, S).Basins.Count;
        Assert.True(steps > 0);
        Assert.Equal(1 + steps, l.Count);
        Assert.Equal(1, l.Count(b => b.Junction && b.Kind == "합류" && b.Owner == "J1" && b.Members == "D-001,D-002"));
        Assert.All(l.Where(b => !b.Junction), b => { Assert.Equal("단차", b.Kind); Assert.Equal("D-003", b.Owner); });
        var (n, v) = UDitch.BasinTotals(rec, S);
        Assert.Equal(l.Count, n);
        Assert.Equal(l.Sum(b => b.Volume), v, 9);
        var g = UDitch.BasinGroups(l);
        Assert.Equal(l.Count, g.Sum(x => x.Count));
        Assert.Equal(v, g.Sum(x => x.Volume), 9);
        Assert.Contains($"집수정 {l.Count}개소(", UDitch.BasinSummary(l));
        Assert.Contains($"■ 집수정 수량 — {l.Count}개소", UDitch.Report(rec, S, "t.dwg", "t"));
        // 규격 = 안치수 × 깊이(0.1 m 올림)
        Assert.All(g, x => Assert.True(x.DepthKnown));
        Assert.Equal(0.9, UDitch.DepthClass(0.857), 9); Assert.Equal(0.9, UDitch.DepthClass(0.9), 9); Assert.Equal(1.0, UDitch.DepthClass(0.9001), 9);
        Assert.Equal(l.Count(b => Math.Round(b.B.Inner, 2) == 0.6 && UDitch.DepthClass(b.Depth) == g[0].Depth), g[0].Count);
    }

    [Fact]
    public void Junction_without_ditch_segments_estimates_floor_or_counts_depth_unknown()
    {
        // ★[JACK 1124 «못 지음으로 빼지 말 것»] 두 측구가 집수정보다 짧아 구간이 없음 → 시작 · 끝 바닥고 보간으로 안바닥 추정(3D · 콘크리트 셈)
        Func<double, double, double> gr = (x, y) => 100 - 0.01 * x;
        var a = Make("D-001", gr, (0, 0), (0.3, 0));
        var b = Make("D-002", gr, (0.3, 0), (0.3, 0.3));
        var rec = new UDitch.Record { Ditches = { a, b } };
        UDitch.Link(rec, S);
        var j = Assert.Single(rec.Junctions);
        Assert.True(UDitch.LayoutOf(a, S).Error.Length > 0);
        Assert.True(j.Ok);
        Assert.Contains(j.Notes, n => n.Contains("바닥고 추정"));
        double est = Math.Min(Math.Min(a.StartInvert, a.EndInvert), Math.Min(b.StartInvert, b.EndInvert));
        Assert.Equal(est - S.BasinSump, j.B.Floor, 6);
        var it = Assert.Single(UDitch.BasinList(rec, S));
        Assert.True(it.DepthKnown); Assert.True(it.Volume > 0);
        Assert.Single(UDitch.JunctionParts(rec, S, 0, 0));
        // 바닥고 · 지표 모두 없음 → «깊이 미정»으로 개소에 듦(빠뜨리지 않음) · 콘크리트 0 · 3D 없음
        UDitch.Ditch Bare(string id, params (double X, double Y)[] p) => new() { Id = id, Follow = true, InvertAuto = false, Pts = p.Select(q => new[] { q.X, q.Y, double.NaN }).ToList() };
        var rec2 = new UDitch.Record { Ditches = { Bare("D-001", (0, 0), (0.3, 0)), Bare("D-002", (0.3, 0), (0.3, 0.3)) } };
        UDitch.Link(rec2, S);
        var j2 = Assert.Single(rec2.Junctions);
        Assert.False(j2.Ok);
        var it2 = Assert.Single(UDitch.BasinList(rec2, S));
        Assert.False(it2.DepthKnown); Assert.Equal(0, it2.Volume);
        Assert.Equal((1, 0.0), UDitch.BasinTotals(rec2, S));
        var g2 = Assert.Single(UDitch.BasinGroups(rec2, S));
        Assert.False(g2.DepthKnown); Assert.Contains("깊이 미정", g2.Label);
        Assert.Contains("깊이 미정 1", UDitch.BasinSummary(UDitch.BasinList(rec2, S)));
        Assert.Empty(UDitch.JunctionParts(rec2, S, 0, 0));
        Assert.Contains("콘크리트 미정", UDitch.Report(rec2, S, "t.dwg", "t"));
    }
}
