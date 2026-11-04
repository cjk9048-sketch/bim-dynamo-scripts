// ── S194 CL ★★[v112.5 · JACK 1104 «배치탭 등고선에 등고값 · 계곡선에만»] 등고선 잇기 · 값 자리 ──
using DH.Grading.Core;

static class ContourLabelCheck
{
    static List<(Point3, Point3, Point3)> Grid(Func<double, double, double> z, int n, double h)
    {
        var t = new List<(Point3, Point3, Point3)>();
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                Point3 P(int a, int b) => new(a * h, b * h, z(a * h, b * h));
                t.Add((P(i, j), P(i + 1, j), P(i + 1, j + 1)));
                t.Add((P(i, j), P(i + 1, j + 1), P(i, j + 1)));
            }
        return t;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S194 CL 등고선 잇기 · 값 자리(계곡선 · 간격 · 방향 · 건너뛸 자리) ==");
        // ① 기울어진 평면 z = x — 계곡선 5 m마다 곧은 세로선 하나씩(0~20 → 0 · 5 · 10 · 15 · 20) · 한 줄로 이어짐 · 길이 20
        {
            var lines = ContourLabels.Lines(Grid((x, y) => x, 20, 1), 5);
            bool ok = lines.Keys.OrderBy(k => k).SequenceEqual(new[] { 0.0, 5, 10, 15 })
                      && lines[10].Count == 1 && lines[10][0].All(p => Math.Abs(p.X - 10) < 1e-6)
                      && Math.Abs(lines[10][0].Max(p => p.Y) - lines[10][0].Min(p => p.Y) - 20) < 1e-6;
            check("S194 CL① 평면 z = x — 높이 0 · 5 · 10 · 15(꼭대기 20은 땅 끝이라 없음) · 10 m 선 하나 · x = 10 · 길이 20", ok, string.Join(",", lines.Keys.OrderBy(k => k)) + $" · 10 m 선 {lines[10].Count}");
        }
        // ② 원뿔 z = 10 − r — 5 m 등고선 = 닫힌 고리 하나(처음 = 끝) · 반지름 5 ± 칸
        {
            var lines = ContourLabels.Lines(Grid((x, y) => 10 - Math.Sqrt((x - 10) * (x - 10) + (y - 10) * (y - 10)), 40, 0.5), 5);
            var ring = lines.TryGetValue(5, out var l5) ? l5 : new();
            bool closed = ring.Count == 1 && Math.Abs(ring[0][0].X - ring[0][^1].X) < 1e-6 && Math.Abs(ring[0][0].Y - ring[0][^1].Y) < 1e-6;
            double rmax = ring.Count == 1 ? ring[0].Max(p => Math.Abs(Math.Sqrt((p.X - 10) * (p.X - 10) + (p.Y - 10) * (p.Y - 10)) - 5)) : 99;
            check("S194 CL② 원뿔 — 5 m 등고선 닫힌 고리 하나 · 반지름 5(칸 0.5 안)", closed && rmax < 0.5, $"선 {ring.Count} · 반지름 차 최대 {rmax:F3} · " + string.Join(" | ", ring.Select(q => $"{q.Count}점 ({q[0].X:F6},{q[0].Y:F6})~({q[^1].X:F6},{q[^1].Y:F6})")));
        }
        // ③ 값 자리 — 길이 20 선 · 간격 8 → 4 · 12 · 20?(L 끝 전) = 4 · 12 두 자리 · 방향 90°(읽기 좋게) · 건너뛸 자리 y < 8 빼면 하나 · 짧은 선(minLen) 안 씀
        {
            var lines = new Dictionary<double, List<List<(double X, double Y)>>> { [10] = new() { new() { (10, 20), (10, 0) } } };
            var lab = ContourLabels.Place(lines, 8, 1, 0.5);
            var lab2 = ContourLabels.Place(lines, 8, 1, 0.5, (x, y) => y > 12);
            var none = ContourLabels.Place(lines, 8, 30, 0.5);
            bool ok = lab.Count == 2 && lab.All(q => Math.Abs(q.AngleRad - Math.PI / 2) < 1e-9 && q.Z == 10) && Math.Abs(lab[0].Y - 16) < 1e-9 && lab2.Count == 1 && none.Count == 0;
            check("S194 CL③ 값 자리 — 길이 20 · 간격 8 → 4 · 12 둘 · 방향 90° · 건너뛰기 하나 남음 · 짧은 선 안 씀", ok, string.Join(" ", lab.Select(q => $"({q.X:F1},{q.Y:F1},{q.AngleRad * 180 / Math.PI:F0}°)")) + $" · 뺀 뒤 {lab2.Count} · 짧음 {none.Count}");
        }
        // ④ 방향 — 왼쪽으로 가는 선(180°)도 0°로 읽힘 · 값 글
        {
            var lines = new Dictionary<double, List<List<(double X, double Y)>>> { [7.5] = new() { new() { (20, 0), (0, 0) } } };
            var lab = ContourLabels.Place(lines, 30, 1, 0.5);
            bool ok = lab.Count == 1 && Math.Abs(lab[0].AngleRad) < 1e-9 && ContourLabels.Text(105.0, 5) == "105" && ContourLabels.Text(7.5, 2.5) == "7.5";
            check("S194 CL④ 거꾸로 가는 선도 바로 읽힘(0°) · 값 글 105 · 7.5", ok, lab.Count > 0 ? $"{lab[0].AngleRad * 180 / Math.PI:F1}°" : "없음");
        }
    }
}
