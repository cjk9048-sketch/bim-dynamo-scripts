// ── S191 SE ★★★[도로 마무리 ① · 계획_도로마무리.md 5.2 · 8장 SE] 도로 끝 판정(넘침 자름 · 못 미침 늘림 · 비스듬히 · 가로지름 빈 구간 · 가지 · 고정 PVI · 시점 옮김) ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)
using DH.Grading.Core;
using NetTopologySuite.Geometries;

static class RoadEndsCheck
{
    const double OX = 200000, OY = 550000;
    static readonly GeometryFactory Gf = new();

    static RoadEnds.Road Line(long h, double x0, double y0, double x1, double y1, double half = 3.5)
    {
        double L = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)); int n = Math.Max(1, (int)Math.Ceiling(L / 0.25));
        var S = new List<double>(); var X = new List<double>(); var Y = new List<double>();
        for (int i = 0; i <= n; i++) { double t = (double)i / n; S.Add(L * t); X.Add(OX + x0 + (x1 - x0) * t); Y.Add(OY + y0 + (y1 - y0) * t); }
        return new RoadEnds.Road(h, S, X, Y, half, half);
    }
    static Geometry Poly(params (double X, double Y)[] p) => Gf.CreatePolygon(p.Append(p[0]).Select(q => new Coordinate(OX + q.X, OY + q.Y)).ToArray());
    static RoadEnds.Floor Box(int reg, double x0, double y0, double x1, double y1) => new(reg, Poly((x0, y0), (x1, y0), (x1, y1), (x0, y1)));
    static double? Fz(double x, double y) => 50 + 0.01 * (x - OX);   // 바닥 높이(경사 계획선 흉내)

    public static void Run(Action<string, bool, string> check)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        RoadEnds.Result One(RoadEnds.Road r, params RoadEnds.Floor[] f) => RoadEnds.Solve(new[] { r }, f, Fz, null)[0];
        // ① 넘침 — 종점이 바닥 안 20 m · 경계 직각 → 측점 100(1 mm) · 높이 = 그 자리 바닥 높이
        {
            var R = One(Line(1, 0, 0, 120, 0), Box(7, 100, -30, 140, 30));
            check("S191 SE① 넘침 — 종점 바닥 안 → 자름(측점 100 · 1 mm) · 높이 = 바닥 높이 · 구역 · 범위 [0, 100] · 시점 자유",
                R.Finish.Kind == RoadEnds.Kind.Floor && Math.Abs(R.Finish.Station - 100) < 1e-3 && Math.Abs(R.Finish.Z - 51) < 1e-3 && R.Finish.Region == 7 && R.Start.Kind == RoadEnds.Kind.Free && R.Ranges.Count == 1 && Math.Abs(R.Ranges[0].B - 100) < 1e-3,
                $"{R.Finish.Kind} {R.Finish.Station:F4} z {R.Finish.Z:F4}");
        }
        // ② 못 미침 — 8 m 앞 = 곧게 8 m 늘림 · 25 m 앞 = 자유(한도 20 m · JACK Q1)
        {
            var a = One(Line(1, 0, 0, 92, 0), Box(7, 100, -30, 140, 30));
            var b = One(Line(1, 0, 0, 75, 0), Box(7, 100, -30, 140, 30));
            check("S191 SE② 못 미침 — 8 m 앞 = 곧게 8.000 m 늘림(측점 100 · 높이 바닥) / 25 m 앞 = 늘이지 않음(20 m 한도)",
                a.Finish.Kind == RoadEnds.Kind.Extend && a.Finish.Ext!.Straight && Math.Abs(a.Finish.Ext.Length - 8) < 1e-3 && Math.Abs(a.Finish.Station - 100) < 1e-3 && Math.Abs(a.Finish.Z - 51) < 1e-3 && b.Finish.Kind == RoadEnds.Kind.Free,
                $"{a.Finish.Kind} {a.Finish.Ext?.Length:F4} / {b.Finish.Kind}");
        }
        // ③ 꺾어 늘림 — 바닥이 끝 옆(곧게는 안 닿음) · 12 m 안 → 꺾어 · 꺾임 각 > 0 · 닿는 점이 바닥 경계 / 꺾임이 커 곡선이 안 들어가면 알림
        {
            var a = One(Line(1, 0, 0, 100, 0), Box(7, 108, 6, 140, 40));
            check("S191 SE③ 꺾어 늘림 — 곧게 안 닿는 바닥(앞 8 m · 옆 6 m) → 꺾어(곡선 반경 30) · 닿는 점 = 먼 모서리가 바닥 안 · 높이 바닥",
                a.Finish.Kind == RoadEnds.Kind.Extend && !a.Finish.Ext!.Straight && a.Finish.Ext.BendDeg > 5 && a.Finish.Ext.Length <= 20 + 1e-9 && double.IsFinite(a.Finish.Z),
                string.Format(ci, "{0} 곧게 {1} 꺾임 {2:F1}° 길이 {3:F2} {4}", a.Finish.Kind, a.Finish.Ext?.Straight, a.Finish.Ext?.BendDeg, a.Finish.Ext?.Length, string.Join(" ", a.Notes)));
        }
        // ④ 비스듬히 θ = 30° — 먼 쪽 포장 모서리가 경계에 닿는 측점(100 + 3.5·tan30) · θ = 65° = 안 맞춤 + 알림
        {
            double t30 = Math.Tan(Math.PI / 6), t65 = Math.Tan(65 * Math.PI / 180);
            var f30 = new RoadEnds.Floor(7, Poly((100 - 30 * t30, -30), (160, -30), (160, 30), (100 + 30 * t30, 30)));
            var a = One(Line(1, 0, 0, 120, 0), f30);
            var f65 = new RoadEnds.Floor(7, Poly((100 - 30 * t65, -30), (300, -30), (300, 30), (100 + 30 * t65, 30)));
            var b = One(Line(1, 0, 0, 140, 0), f65);
            check("S191 SE④ 비스듬히 30° — 자르는 측점 = 먼 모서리(100 + 3.5·tan30 = 102.021 · 1 mm) · 가까운 모서리는 바닥 안 / 65° = 안 맞춤 + 알림",
                a.Finish.Kind == RoadEnds.Kind.Floor && Math.Abs(a.Finish.Station - (100 + 3.5 * t30)) < 1e-3 && b.Finish.Kind == RoadEnds.Kind.Free && b.Notes.Any(n => n.Contains("비스듬히")),
                string.Format(ci, "{0:F4} 기대 {1:F4} / {2} {3}", a.Finish.Station, 100 + 3.5 * t30, b.Finish.Kind, string.Join(" ", b.Notes)));
        }
        // ⑤ 가로지름 — 가운데 바닥 [40, 60] → 빈 구간 · 범위 두 토막 · 두 자리 고정 높이 / 3 m 바닥 = 그냥 지나감 + 알림
        {
            var a = One(Line(1, 0, 0, 100, 0), Box(7, 40, -20, 60, 20));
            var b = One(Line(1, 0, 0, 100, 0), Box(8, 40, -20, 43, 20));
            check("S191 SE⑤ 가로지름 — 빈 구간 [40, 60](1 mm) · 범위 [0,40] [60,100] · 높이 고정 / 3 m 바닥은 안 끊고 알림",
                a.Gaps.Count == 1 && Math.Abs(a.Gaps[0].A - 40) < 1e-3 && Math.Abs(a.Gaps[0].B - 60) < 1e-3 && Math.Abs(a.Gaps[0].Za - 50.4) < 1e-3 && a.Ranges.Count == 2 && b.Gaps.Count == 0 && b.Notes.Any(n => n.Contains("지나감")),
                $"{string.Join(" ", a.Gaps.Select(g => $"[{g.A:F3},{g.B:F3}]"))} · {a.Ranges.Count} / {b.Gaps.Count} {string.Join(" ", b.Notes)}");
        }
        // ⑥ 가지 — 본 도로(핸들 1) 포장 안에서 끝나는 가지(핸들 2) → 본 포장 끝(먼 모서리) · 높이 = 본 도로 포장 높이 · 본 도로는 그대로
        {
            var main = Line(1, 0, 0, 100, 0); var br = Line(2, 50, -30, 50, -1);
            var rs = RoadEnds.Solve(new[] { main, br }, Array.Empty<RoadEnds.Floor>(), Fz, (h, x, y) => h == 1 ? 60.0 : null);
            var m = rs.First(q => q.Handle == 1); var b = rs.First(q => q.Handle == 2);
            check("S191 SE⑥ 가지 — 본 포장 끝에서 끝남(측점 26.5 · 1 mm) · 본 핸들 · 높이 = 본 도로 포장 높이 · 본 도로 끝은 자유",
                b.Finish.Kind == RoadEnds.Kind.Branch && b.Finish.Main == 1 && Math.Abs(b.Finish.Station - 26.5) < 1e-3 && b.Finish.Z == 60 && m.Start.Kind == RoadEnds.Kind.Free && m.Finish.Kind == RoadEnds.Kind.Free,
                $"{b.Finish.Kind} {b.Finish.Station:F4} z {b.Finish.Z}");
        }
        // ⑦ 자르면 5 m 미만 → 안 자름 + 알림 · ⑧ 같은 입력 두 번 = 같은 결과
        {
            var a = One(Line(1, 0, 0, 30, 0), Box(7, 3, -20, 60, 20));
            var x1 = One(Line(1, 0, 0, 120, 0), Box(7, 100, -30, 140, 30), Box(9, 40, -20, 60, 20));
            var x2 = One(Line(1, 0, 0, 120, 0), Box(7, 100, -30, 140, 30), Box(9, 40, -20, 60, 20));
            check("S191 SE⑦⑧ 자르면 5 m 미만 = 안 자름 + 알림 · 같은 입력 = 같은 결과(끝 · 빈 구간 · 범위)",
                a.Finish.Kind == RoadEnds.Kind.Free && a.Notes.Any(n => n.Contains("안 자름")) && x1.Finish == x2.Finish && x1.Gaps.SequenceEqual(x2.Gaps) && x1.Ranges.SequenceEqual(x2.Ranges),
                string.Join(" ", a.Notes));
        }
        // ⑨ 고정 PVI — 끝 높이 고정 · 빈 구간 안 PVI 지움 · 두 자리 고정
        {
            var pv = new List<(double, double)> { (0, 100), (30, 97), (50, 95), (100, 90) };
            var o = RoadEnds.PinPvis(pv, 0, double.NaN, 100, 88, new[] { new RoadEnds.Gap(40, 60, 94, 93, 1) });
            bool ok = o.Count == 5 && o[0] == (0, 100) && o[1] == (30, 97) && o[2] == (40, 94) && o[3] == (60, 93) && o[4] == (100, 88);
            check("S191 SE⑨ 고정 PVI — 끝 = 고정 높이 · 빈 구간 안 PVI(50) 지움 · 40 · 60 고정", ok, string.Join(" ", o.Select(p => $"({p.S},{p.Z})")));
            // ★[V98u 실측] 자른 끝이 옛 끝 PVI와 µm 차이(이분 1e-5 m) · 빈 구간 끝 곁 PVI — 고정 자리 1 cm 안 옛 PVI는 남기지 않음(겹친 PVI 없음)
            var pv2 = new List<(double, double)> { (0, 100), (0.004, 100.1), (30, 97), (39.995, 94.5), (60.008, 93.2), (70, 91) };
            var o2 = RoadEnds.PinPvis(pv2, 0, 100, 70.000004, 90.5, new[] { new RoadEnds.Gap(40, 60, 94, 93, 1) });
            bool ok2 = o2.Count == 5 && o2.Zip(o2.Skip(1)).All(q => q.Second.S - q.First.S > 0.01) && o2[^1] == (70.000004, 90.5) && o2[0] == (0, 100);
            check("S191 SE⑨b 고정 PVI — 고정 자리(시점 · 종점 · 빈 구간 끝) 1 cm 안 옛 PVI는 뺌 · 이웃 PVI 간격 > 1 cm", ok2, string.Join(" ", o2.Select(p => $"({p.S},{p.Z})")));
            // ★[설치 전 검토 M4 1104] 붙은 두 바닥을 곧게 지나감(40~60 · 60~80) → 빈 구간 하나(40~80) · 고정 PVI가 µm 사이로 겹치지 않음
            var g = One(Line(1, 0, 0, 150, 0), Box(3, 40, -20, 60, 20), Box(4, 60, -20, 80, 20));
            var o3 = RoadEnds.PinPvis(new List<(double, double)> { (0, 100), (150, 90) }, 0, double.NaN, 150, double.NaN, g.Gaps);
            check("S191 SE⑨c 붙은 두 바닥 — 빈 구간 합침(40~80 하나) · 알림 · 고정 PVI 간격 > 1 cm",
                g.Gaps.Count == 1 && Math.Abs(g.Gaps[0].A - 40) < 1e-3 && Math.Abs(g.Gaps[0].B - 80) < 1e-3 && g.Notes.Any(n => n.Contains("합침")) && o3.Zip(o3.Skip(1)).All(q => q.Second.S - q.First.S > 0.01),
                string.Join(" ", g.Gaps.Select(q => $"[{q.A:F4},{q.B:F4}]")) + " / " + string.Join(" ", o3.Select(p => $"({p.S:F4},{p.Z:F2})")));
            var o4 = RoadEnds.PinPvis(new List<(double, double)> { (0, 100), (100, 90) }, 0, 100, 100, 88, new[] { new RoadEnds.Gap(40, 99.995, 94, 89, 1) });
            check("S191 SE⑨d 빈 구간 끝이 도로 끝 1 cm 안 — 끝 고정만 남음(겹친 PVI 없음)", o4.Count == 3 && o4[^1] == (100, 88), string.Join(" ", o4.Select(p => $"({p.S},{p.Z})")));
        }
        // ⑩ 시점 옮김 — 늘림 +Δ · 자름 −Δ · 0 아래 몫 자름/뺌
        {
            bool ok = RoadEnds.ShiftSpan(3, 10, -5) == (0, 5) && RoadEnds.ShiftSpan(1, 4, -5) == null && RoadEnds.ShiftSpan(3, 10, 8) == (11, 18) && RoadEnds.Shift(2, -5) == null && RoadEnds.Shift(2, 8) == 10;
            check("S191 SE⑩ 시점 옮김 — 구간 · 측점 +Δ/−Δ · 0 아래는 자르거나 뺌", ok, "");
        }
    }
}
