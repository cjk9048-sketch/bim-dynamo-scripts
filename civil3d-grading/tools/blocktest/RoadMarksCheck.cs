// ── S187 SL ★★[v111.8 · 계획_v111_8.md 6장 · 10장 SL · JACK 1102 Q5 «차로 끝 안쪽 0.1 m · 폭 0.1 m · 양쪽»] [3D 보기] 흰 차선 띠 ──
//   만든 꼴(현장 값 없음) · 좌표는 현장 크기(X 2e5 · Y 5.5e5)로 띄움 · 노선 자리는 해석식(직선 · 원호)
using DH.Grading.Core;

static class RoadMarksCheck
{
    const double OX = 200000, OY = 550000;

    /// <summary>원호 노선(국소 원점에서 +x로 출발 · 왼쪽으로 돎 · 반경 R) — 오프셋 + = 오른쪽(바깥).</summary>
    static (double X, double Y)? Arc(double R, double s, double o)
    {
        double th = s / R, cx = R * Math.Sin(th), cy = R - R * Math.Cos(th);
        return (OX + cx + o * Math.Sin(th), OY + cy - o * Math.Cos(th));
    }

    static List<double> Stations(double sa, double sb, double step = 0.5)
    {
        var l = new List<double> { sa };
        for (double s = Math.Ceiling(sa / step + 1e-9) * step; s < sb - 1e-9; s += step) if (s > sa + 1e-9) l.Add(s);
        l.Add(sb);
        return l;
    }

    static bool Ccw(RoadMarks.RibbonResult r)
    {
        for (int i = 0; i < r.Idx.Count; i += 3)
        {
            var a = r.Pos[r.Idx[i]]; var b = r.Pos[r.Idx[i + 1]]; var c = r.Pos[r.Idx[i + 2]];
            if ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) <= 0) return false;
        }
        return true;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S187 SL 흰 차선 띠(차로 끝 안쪽 0.1 m · 폭 0.1 m) ==");
        static double? Z(double x, double y) => 5 + 0.01 * (x - OX) + 0.005 * (y - OY);

        // ① 직선 — 양쪽 띠 폭 0.1 m · 차로 끝(±3)에서 0.1 m · 높이 = 지형 + 0.03 · 위에서 반시계 · 칸 다 남음
        {
            var st = Stations(0, 50);
            (double X, double Y)? P(double s, double o) => (OX + s, OY - o);
            var lines = RoadMarks.LaneLines(st, P, _ => (3.0, 3.0));
            var r = RoadMarks.Lanes(st, P, _ => (3.0, 3.0), Z);
            double wErr = 0, eErr = 0;
            foreach (var (side, o, n, _) in lines)
                for (int i = 0; i < st.Count; i++)
                {
                    wErr = Math.Max(wErr, Math.Abs(Math.Abs(o[i].Y - n[i].Y) - 0.1) + Math.Abs(o[i].X - n[i].X));
                    eErr = Math.Max(eErr, Math.Abs(Math.Abs(o[i].Y - OY) - 2.9));
                }
            double zErr = r.Pos.Max(p => Math.Abs(p.Z - (Z(p.X, p.Y)!.Value + 0.03)));
            check("S187 SL① 직선 — 양쪽 두 줄 · 폭 0.1 m(1 mm) · 차로 끝에서 0.1 m(1 mm) · 높이 = 지형 + 0.03 · 반시계 · 칸 다 남음",
                lines.Count == 2 && wErr < 1e-3 && eErr < 1e-3 && zErr < 1e-9 && Ccw(r) && r.Kept == 2 * (st.Count - 1) && r.Dropped == 0 && r.Idx.Count == r.Kept * 6,
                $"{r.Text} · 폭 오차 {wErr * 1000:F4} mm · 끝 오차 {eErr * 1000:F4} mm · 높이 오차 {zErr:E1}");
        }

        // ② R 30 곡선(90°) — 폭 0.1 m · 차로 끝에서 0.1 m(반경으로 잼) · 곡선 따라감 · 칸 다 남음
        {
            double R = 30, L = R * Math.PI / 2;
            var st = Stations(0, L);
            var r = RoadMarks.Lanes(st, (s, o) => Arc(R, s, o), _ => (3.0, 3.0), Z);
            var lines = RoadMarks.LaneLines(st, (s, o) => Arc(R, s, o), _ => (3.0, 3.0));
            double wErr = 0, eErr = 0;
            foreach (var (side, o, n, _) in lines)
                for (int i = 0; i < st.Count; i++)
                {
                    double ro = Math.Sqrt((o[i].X - OX) * (o[i].X - OX) + (o[i].Y - OY - R) * (o[i].Y - OY - R));
                    double rn = Math.Sqrt((n[i].X - OX) * (n[i].X - OX) + (n[i].Y - OY - R) * (n[i].Y - OY - R));
                    wErr = Math.Max(wErr, Math.Abs(Math.Abs(ro - rn) - 0.1));
                    eErr = Math.Max(eErr, Math.Abs(Math.Abs(ro - R) - 2.9));
                }
            check("S187 SL② R 30 곡선 — 폭 0.1 m(1 mm) · 차로 끝에서 0.1 m(1 mm · 반경) · 반시계 · 칸 다 남음",
                lines.Count == 2 && wErr < 1e-3 && eErr < 1e-3 && Ccw(r) && r.Kept == 2 * (st.Count - 1) && r.Dropped == 0,
                $"{r.Text} · 폭 오차 {wErr * 1000:F4} mm · 끝 오차 {eErr * 1000:F4} mm");
        }

        // ③ 높이 없는 칸 버림 — 지형이 x 20 ~ 25에서 없음 → 그 칸만(네 점 중 하나라도)
        {
            var st = Stations(0, 50);
            (double X, double Y)? P(double s, double o) => (OX + s, OY - o);
            double? Zh(double x, double y) => x - OX > 20 && x - OX < 25 ? null : Z(x, y);
            var r = RoadMarks.Lanes(st, P, _ => (3.0, 3.0), Zh);
            int want = 2 * st.Zip(st.Skip(1)).Count(c => !(c.Second <= 20 || c.First >= 25));
            bool noneInside = r.Pos.All(p => !(p.X - OX > 20 && p.X - OX < 25));
            check("S187 SL③ 높이 없는 칸 버림(지형 없는 자리만 · 까닭 «지형 없음») · 그 자리 점 없음",
                r.DropNoZ == want && want > 0 && r.Kept == 2 * (st.Count - 1) - want && noneInside, $"{r.Text} · 기대 버림 {want}");
        }

        // ④ 좁은 안쪽 곡선 — R 3.2(안쪽 바깥 선 반경 0.3 < 0.5) 버림 · 바깥쪽은 남음 · R 2(안쪽 선이 뒤집힘) = 꼬임 버림
        {
            double R = 3.2, L = R * Math.PI / 2;
            var st = Stations(0, L, 0.1);
            var r = RoadMarks.Lanes(st, (s, o) => Arc(R, s, o), _ => (3.0, 3.0), Z);
            var lines = RoadMarks.LaneLines(st, (s, o) => Arc(R, s, o), _ => (3.0, 3.0));
            var right = RoadMarks.Ribbon(lines.First(q => q.Side > 0).Outer, lines.First(q => q.Side > 0).Inner, Z, center: lines[0].Center);
            var left = RoadMarks.Ribbon(lines.First(q => q.Side < 0).Outer, lines.First(q => q.Side < 0).Inner, Z, center: lines[0].Center);
            double R2 = 2.0, L2 = R2 * Math.PI / 2;
            var st2 = Stations(0, L2, 0.1);
            var lines2 = RoadMarks.LaneLines(st2, (s, o) => Arc(R2, s, o), _ => (3.0, 3.0));
            var left2 = RoadMarks.Ribbon(lines2.First(q => q.Side < 0).Outer, lines2.First(q => q.Side < 0).Inner, Z, center: lines2[0].Center);
            var left2bare = RoadMarks.Ribbon(lines2.First(q => q.Side < 0).Outer, lines2.First(q => q.Side < 0).Inner, Z);
            var right2 = RoadMarks.Ribbon(lines2.First(q => q.Side > 0).Outer, lines2.First(q => q.Side > 0).Inner, Z, center: lines2[0].Center);
            check("S187 SL④ 좁은 안쪽 곡선 — R 3.2 안쪽 칸 버림(곡선 안쪽 좁음) · 바깥쪽 다 남음 · R 2 안쪽 = 꼬임 버림(중심선과 반대 — 중심선 없이는 못 가름: 빈 시험 막기) · 바깥쪽 남음",
                left.Kept == 0 && left.DropTight == left.Cells && right.Kept == right.Cells && right.Dropped == 0
                && left2.Kept == 0 && left2.DropTwist + left2.DropTight == left2.Cells && left2.DropTwist > 0 && left2bare.Kept > 0 && right2.Kept == right2.Cells && Ccw(r),
                $"R 3.2 안 {left.Text} / 밖 {right.Text} · R 2 안 {left2.Text}(중심선 없이 {left2bare.Kept}칸 남음) / 밖 {right2.Text}");
        }

        // ⑤ 끝 측점에서 정확히 끊김 · 폭이 바뀌어도 따라감 · 차로가 0.2 m보다 좁은 쪽은 안 그림
        {
            double sa = 3.7, sb = 41.3;
            var st = Stations(sa, sb);
            (double X, double Y)? P(double s, double o) => (OX + s, OY - o);
            var r = RoadMarks.Lanes(st, P, s => (3.0 + 0.02 * s, 0.15), Z);
            double xmin = r.Pos.Min(p => p.X) - OX, xmax = r.Pos.Max(p => p.X) - OX;
            var lines = RoadMarks.LaneLines(st, P, s => (3.0 + 0.02 * s, 0.15));
            var left = lines.FirstOrDefault(q => q.Side < 0);
            int k40 = st.IndexOf(40.0);
            double at40 = left.Outer != null && k40 >= 0 ? left.Outer[k40].Y - OY : double.NaN;
            check("S187 SL⑤ 끝 측점(3.7 · 41.3)에서 정확히 끊김 · 폭 따라감(측점 40 바깥 선 = 3.8 − 0.1) · 0.15 m 차로 쪽은 줄 없음",
                Math.Abs(xmin - sa) < 1e-9 && Math.Abs(xmax - sb) < 1e-9 && lines.Count == 1 && left.Side < 0 && Math.Abs(at40 - 3.7) < 1e-9 && r.Lines == 1 && r.Dropped == 0,
                $"{r.Text} · x {xmin:F6} ~ {xmax:F6} · 측점 40 바깥 선 {at40:F6}");
        }

        // ⑥ ★[v116.5 · JACK 1110] 가운데 흰 점선 — 직선 0~50 · 좌 3 · 우 4(가운데 = 오른쪽 0.5) · 칠 3 · 빈 5 · 폭 0.15 → 토막 [0,3] [8,11] … [48,50] · 넓이 = 칠 길이 합 × 0.15
        {
            (double X, double Y)? P(double s, double o) => (OX + s, OY - o);
            var c = RoadMarks.CenterDashes(new[] { (0.0, 50.0) }, P, _ => (3.0, 4.0), Z);
            double area = 0; for (int i = 0; i < c.Idx.Count; i += 3) { var a = c.Pos[c.Idx[i]]; var b = c.Pos[c.Idx[i + 1]]; var d = c.Pos[c.Idx[i + 2]]; area += Math.Abs((b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X)) / 2; }
            double want = (3 * 6 + 2) * 0.15;
            double yMin = c.Pos.Min(p => p.Y - OY), yMax = c.Pos.Max(p => p.Y - OY);
            bool gapsEmpty = c.Pos.All(p => { double x = p.X - OX, m = x % 8; return m <= 3 + 1e-9 || m >= 8 - 1e-9; });
            var e = RoadMarks.Lanes(Stations(0, 50), P, _ => (3.0, 4.0), Z, centerSpans: new[] { (0.0, 50.0) });
            check("S187 SL⑥ 가운데 흰 점선 — 칠 3 m · 빈 5 m · 폭 0.15 m · 비대칭 차로 가운데(오른쪽 0.5) · 넓이 = 칠 길이 × 폭 · 빈 자리에 점 없음 · 반시계 · 양쪽 차선과 한 묶음(3줄)",
                Math.Abs(area - want) < 1e-6 && Math.Abs(yMin - (-0.575)) < 1e-9 && Math.Abs(yMax - (-0.425)) < 1e-9 && gapsEmpty && Ccw(c) && c.Dropped == 0 && c.Lines == 1 && e.Lines == 3,
                $"{c.Text} · 넓이 {area:F4} / {want:F4} ㎡ · y {yMin:F3} ~ {yMax:F3} · 묶음 {e.Text}");
        }
    }
}
