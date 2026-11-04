namespace DH.Grading.Core;

/// <summary>★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단» · 계획 5-1] 진입도로 폴리선 옹벽의 <b>좌표 바꾸기</b> —
/// 폴리선 점마다 (측점 s, 띠 바깥 거리 u)를 엔진 칸 계산(EvalAt의 «가장 가까운 중심선 점»)과 같은 규칙으로 구한다.
/// <para>거절: 쪽이 바뀜(도로를 가로지름) · 끝 마구리 너머 · 측점이 되돌아감(한 횡단에서 폴리선을 두 번 만남) · 띠 안(u ≤ 0).
/// 통과하면 D_P(s) = (s, u) 꺾은선 보간 — 엔진 갈래(5-2)가 이 값으로 «폴리선 바깥 = 원지반 · 안쪽 = 횡단 거리로 센 단»을 낸다.</para>
/// <para>엔진(RoadGrade.RunOnce)이 이 값으로 바닥 폭 D_P(s) − 단 n개 폭을 정한다(하네스 S203 · S204~S206).</para></summary>
public static class RoadBound
{
    public sealed class Map
    {
        public bool Ok; public string Code = "", Why = "";
        public (double X, double Y)? At;
        /// <summary>−1 좌 · +1 우.</summary>
        public int Side;
        /// <summary>측점이 느는 차례로 — 표본 측점 · 띠 바깥 거리.</summary>
        public double[] S = Array.Empty<double>(), U = Array.Empty<double>();
        /// <summary>★[v113.0] 그 표본의 폴리선 위 자리(바닥 높이 · 원지반을 잴 때).</summary>
        public double[] X = Array.Empty<double>(), Y = Array.Empty<double>();
        public double S0 => S.Length > 0 ? S[0] : double.NaN;
        public double S1 => S.Length > 0 ? S[^1] : double.NaN;
        /// <summary>D_P(s) — 구간 밖이면 NaN.</summary>
        public double DP(double s)
        {
            if (S.Length == 0 || s < S[0] - 1e-9 || s > S[^1] + 1e-9) return double.NaN;
            int i = Array.BinarySearch(S, s); if (i >= 0) return U[i];
            i = ~i; if (i <= 0) return U[0]; if (i >= S.Length) return U[^1];
            double t = (s - S[i - 1]) / (S[i] - S[i - 1]);
            return U[i - 1] + (U[i] - U[i - 1]) * t;
        }
    }

    /// <param name="cx">중심선 점(진행 차례 · 엔진 Input.X · Y와 같은 것).</param>
    /// <param name="outer">(쪽, 측점) → 띠 바깥 옵셋(엔진 Input.Outer).</param>
    /// <param name="line">경계 두 교점 사이 폴리선(A..B).</param>
    public static Map Build(IReadOnlyList<double> cx, IReadOnlyList<double> cy, Func<int, double, double> outer, IReadOnlyList<Point3> line, double dens = 0.5)
    {
        var M = new Map();
        Map No(string code, string why, double x, double y) { M.Ok = false; M.Code = code; M.Why = why; M.At = (x, y); return M; }
        int n = cx.Count;
        if (n < 2 || line == null || line.Count < 2) { M.Code = "재료"; M.Why = "중심선 · 폴리선이 모자랍니다"; return M; }
        var S = new double[n];
        for (int i = 1; i < n; i++) S[i] = S[i - 1] + Math.Sqrt((cx[i] - cx[i - 1]) * (cx[i] - cx[i - 1]) + (cy[i] - cy[i - 1]) * (cy[i] - cy[i - 1]));
        // 0.5 m로 촘촘히
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double dx = line[i + 1].X - line[i].X, dy = line[i + 1].Y - line[i].Y, l = Math.Sqrt(dx * dx + dy * dy);
            int k = Math.Max(1, (int)Math.Ceiling(l / Math.Max(0.05, dens)));
            for (int q = 0; q < k; q++) pts.Add((line[i].X + dx * q / k, line[i].Y + dy * q / k));
        }
        pts.Add((line[^1].X, line[^1].Y));
        var ss = new List<double>(); var us = new List<double>();
        int side0 = 0;
        foreach (var (px, py) in pts)
        {
            // EvalAt «가장 가까운 중심선» — 선분마다 t 가두기 · 끝 너머 = 첫 선분 t0 < 0 · 끝 선분 t0 > 1
            double fb = double.MaxValue, fs = 0; int fsd = 0; bool fend = false;
            for (int si = 0; si + 1 < n; si++)
            {
                double ax = cx[si], ay = cy[si], dx = cx[si + 1] - ax, dy = cy[si + 1] - ay, l2 = dx * dx + dy * dy;
                double t0 = l2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0, t = Math.Clamp(t0, 0, 1);
                double qx = ax + dx * t - px, qy = ay + dy * t - py, d2 = qx * qx + qy * qy;
                if (d2 < fb)
                {
                    fb = d2; fs = S[si] + Math.Sqrt(l2) * t; fsd = dx * (py - ay) - dy * (px - ax) >= 0 ? -1 : 1;
                    fend = (si == 0 && t0 < 0) || (si == n - 2 && t0 > 1);
                }
            }
            if (fend) return No("마구리", $"폴리선이 도로 끝(마구리) 너머를 지납니다 ({px:F1}, {py:F1}) — 도로 옆으로만 그리세요", px, py);
            if (side0 == 0) side0 = fsd;
            else if (fsd != side0) return No("가로지름", $"폴리선이 도로를 가로지릅니다 ({px:F1}, {py:F1}) — 한쪽에만 그리세요", px, py);
            ss.Add(fs); us.Add(Math.Sqrt(fb) - outer(fsd, fs));
        }
        // 띠 안(쪽을 다 본 뒤 — 가로지르면 «가로지름»이 먼저)
        for (int i = 0; i < us.Count; i++)
            if (us[i] <= 1e-6) return No("띠 안", $"폴리선이 도로 띠(차로 · 길어깨 · 측구) 안을 지납니다 ({pts[i].X:F1}, {pts[i].Y:F1})", pts[i].X, pts[i].Y);
        // 측점이 한 방향으로만(1 mm 넘게 되돌아가면 거절)
        bool inc = ss[^1] >= ss[0];
        for (int i = 1; i < ss.Count; i++)
        {
            double step = inc ? ss[i] - ss[i - 1] : ss[i - 1] - ss[i];
            if (step < -1e-3) return No("되돌아감", $"폴리선이 도로를 따라 되돌아갑니다 — 한 횡단에서 폴리선을 두 번 만납니다 ({pts[i].X:F1}, {pts[i].Y:F1})", pts[i].X, pts[i].Y);
        }
        var px2 = pts.Select(q => q.X).ToList(); var py2 = pts.Select(q => q.Y).ToList();
        if (!inc) { ss.Reverse(); us.Reverse(); px2.Reverse(); py2.Reverse(); }
        // 같은 측점(1 mm 안 — 꺾인 중심선 바깥 부채꼴 자리)은 바깥 쪽 하나로
        var S2 = new List<double>(); var U2 = new List<double>(); var X2 = new List<double>(); var Y2 = new List<double>();
        for (int i = 0; i < ss.Count; i++)
        {
            if (S2.Count > 0 && ss[i] - S2[^1] < 1e-3) { if (us[i] > U2[^1]) { U2[^1] = us[i]; X2[^1] = px2[i]; Y2[^1] = py2[i]; } continue; }
            S2.Add(ss[i]); U2.Add(us[i]); X2.Add(px2[i]); Y2.Add(py2[i]);
        }
        if (S2.Count < 2) return No("짧음", "폴리선이 도로를 따라 1 mm도 안 갑니다", pts[0].X, pts[0].Y);
        M.Ok = true; M.Code = "통과"; M.Side = side0; M.S = S2.ToArray(); M.U = U2.ToArray(); M.X = X2.ToArray(); M.Y = Y2.ToArray();
        return M;
    }
}
