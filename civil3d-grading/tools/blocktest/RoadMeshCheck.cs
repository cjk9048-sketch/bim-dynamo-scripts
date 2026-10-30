// ── S181 ★★★[v111 · 계획_v111.md 4 · JACK 1029 «소단선이 매끄럽지 못하고 끊어져» · «삼각망이 너무 촘촘» · «원지반과 닫는 부분이 억지로 끊었달까»] 선 기반 진입도로 면 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 좌표는 현장 크기(X 2e5 · Y 5e5)로 띄움 · 해석 답과 견줌
using DH.Grading.Core;

static class RoadMeshCheck
{
    sealed class G : IGroundSurface
    {
        readonly Func<double, double, double> _f;
        public G(Func<double, double, double> f) { _f = f; }
        public bool TryGetElevation(double x, double y, out double z) { z = _f(x, y); return double.IsFinite(z); }
    }

    const double OX = 200000, OY = 500000;

    static (double[] X, double[] Y, double[] Z) Line(double x0, double y0, double x1, double y1, double z0, double z1, double step = 1.0)
    {
        int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / step));
        var X = new double[n + 1]; var Y = new double[n + 1]; var Z = new double[n + 1];
        for (int i = 0; i <= n; i++) { double t = (double)i / n; X[i] = OX + x0 + (x1 - x0) * t; Y[i] = OY + y0 + (y1 - y0) * t; Z[i] = z0 + (z1 - z0) * t; }
        return (X, Y, Z);
    }

    static (List<WallDaylight.Tri> Tris, RoadGrade.MeshReport Rep, List<RoadGrade.TierLine> Lines, RoadGrade.SectionSet Set, RoadGrade.Result R) Build(RoadGrade.Input inp)
    {
        var r = RoadGrade.Run(inp);
        var ctx = new RoadGrade.RayCtx(inp, r);
        var set = RoadGrade.BuildSections(ctx);
        var tris = RoadGrade.MeshFromSections(set, inp.Base, out var rep);
        var lines = RoadGrade.SectionLines(ctx, set, out _, out _, out _);
        return (tris, rep, lines, set, r);
    }

    /// <summary>선이 곧은가 — 점마다 첫 점 · 끝 점을 이은 직선에서 떨어진 거리의 최대(m).</summary>
    static double Straightness(RoadGrade.TierLine l)
    {
        var a = l.Pts[0]; var b = l.Pts[^1];
        double dx = b.X - a.X, dy = b.Y - a.Y, L = Math.Sqrt(dx * dx + dy * dy), w = 0;
        if (L < 1e-9) return 0;
        foreach (var p in l.Pts) w = Math.Max(w, Math.Abs((p.X - a.X) * dy - (p.Y - a.Y) * dx) / L);
        return w;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S181 선 기반 진입도로 면(단면 광선 · 띠 세모 · 계단선) ==");
        var sec = new RoadGrade.Section();   // 반폭 3 · 2 % · 길어깨 0.5 · 4 % → 가장자리 낮춤 0.08

        // ① 곧은 성토 · 평지 — 세모 수(옛 칸 면의 1/20 미만) · 검사 · 데이라잇 = 해석(1 mm) · 데이라잇 선 곧음
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 5, 5);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 30 };
            var (tris, rep, lines, set, r) = Build(inp);
            var old = RoadGrade.SurfaceTris(r, out _);
            double want = (5 - 0.08) * 1.8, worst = 0; int nRay = 0;
            foreach (var x in set.X.Where(q => q.S > 5 && q.S < 95))
                foreach (var ray in new[] { x.L, x.R }) { worst = Math.Max(worst, Math.Abs(ray.Pts[^1].D - want)); nRay++; }
            // ★[v111.2] 횡단 간격 ≤ SectionGap(2 m) — 줄여도 길쭉한 띠 없음 · 잰 광선 0개면 실패(빈 통과 막음)
            double maxGap = set.X.Zip(set.X.Skip(1)).Max(z => z.Second.S - z.First.S);
            check("S181 ① 곧은 성토 — 검사 통과 · 세모 옛 칸 면의 1/20 미만 · 데이라잇 거리 = 해석(1 mm · 잰 광선 > 0) · 횡단 간격 ≤ 2 m",
                rep.Ok && tris.Count * 20 < old.Count && worst < 1e-3 && nRay > 0 && maxGap <= inp.SectionGap + 1e-9,
                $"{rep.Text} · 옛 {old.Count:N0} · 횡단 {set.X.Count} · 최대 간격 {maxGap:F2} m · 잰 광선 {nRay} · 데이라잇 최대 오차 {worst * 1000:F3} mm");
        }

        // ② 곧은 절토 · 평지 20 m — 가장자리 −0.08에서 20.08 m = 단 넷(5 m · 소단 1) + 남은 0.08 m 작은 면 → 데이라잇 4 × 8.5 + 0.12 = 34.12 m · 사면선 · 소단선 곧음(1 mm)
        //   ★[v111.2] 기대값 고침 — 옛 33.12(«5 · 5 · 5 · 5.08»)는 틀렸다. 줄이기가 중간 횡단을 다 빼 잴 횡단이 0개라 비어서 통과했다(SectionGap으로 2 m마다 남자 드러남) → 0개면 실패
        {
            var (X, Y, Z) = Line(0, 0, 120, 0, 0, 0);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 20), Cell = 0.5, Reach = 45 };
            var (tris, rep, lines, set, r) = Build(inp);
            double want = 4 * (7.5 + 1) + 0.08 * 1.5, worst = 0; int nRay = 0;
            foreach (var x in set.X.Where(q => q.S > 5 && q.S < 115))
                foreach (var ray in new[] { x.L, x.R }) { worst = Math.Max(worst, Math.Abs(ray.Pts[^1].D - want)); nRay++; }
            var side = lines.Where(l => l.Side != 0 && l.Pts.Count >= 2 && l.Pts.Max(p => p.S) - l.Pts.Min(p => p.S) > 50).ToList();
            double bend = side.Count == 0 ? 99 : side.Max(Straightness);
            check("S181 ② 곧은 절토(단 넷 + 작은 면) — 검사 통과 · 데이라잇 = 해석(잰 광선 > 0) · 사면선 · 소단선 쪽마다 단마다 곧음(1 mm)",
                rep.Ok && nRay > 0 && worst < 1e-3 && side.Count >= 2 * 7 && bend < 1e-3,
                $"{rep.Text} · 잰 광선 {nRay} · 데이라잇 오차 {worst * 1000:F3} mm · 긴 선 {side.Count} · 최대 휨 {bend * 1000:F3} mm");
        }

        // ③ 곡선(R 30 · 90° · 1 m 꺾은선) · 기운 땅 · 성토 — 검사 통과 · 선 매끈(이웃 선분 꺾임 최대)
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 20; i++) { X.Add(OX + i); Y.Add(OY); Z.Add(10); }
            double R = 30, cx = 20, cy = R;
            int na = (int)Math.Ceiling(R * Math.PI / 2);
            for (int i = 1; i <= na; i++) { double a = -Math.PI / 2 + Math.PI / 2 * i / na; X.Add(OX + cx + R * Math.Cos(a)); Y.Add(OY + cy + R * Math.Sin(a)); Z.Add(10); }
            for (int i = 1; i <= 20; i++) { X.Add(OX + cx + R); Y.Add(OY + cy + i); Z.Add(10); }
            var inp = new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 2 + 0.05 * (x - OX)), Cell = 0.5, Reach = 30 };
            var (tris, rep, lines, set, r) = Build(inp);
            double worstTurn = 0;
            foreach (var l in lines.Where(q => q.Side != 0 && q.Pts.Count >= 3))
                for (int i = 1; i + 1 < l.Pts.Count; i++)
                {
                    double ax = l.Pts[i].X - l.Pts[i - 1].X, ay = l.Pts[i].Y - l.Pts[i - 1].Y, bx = l.Pts[i + 1].X - l.Pts[i].X, by = l.Pts[i + 1].Y - l.Pts[i].Y;
                    if (ax * ax + ay * ay < 1e-6 || bx * bx + by * by < 1e-6) continue;
                    worstTurn = Math.Max(worstTurn, Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by)) * 180 / Math.PI);
                }
            check("S181 ③ 곡선 R 30 · 기운 땅 — 검사 통과 · 계단선 이웃 선분 꺾임 ≤ 10°",
                rep.Ok && worstTurn <= 10, $"{rep.Text} · 횡단 {set.X.Count} · 선 {lines.Count} · 최대 꺾임 {worstTurn:F2}°");
        }

        // ④ 옹벽 변환 기록 [30, 70] 오른쪽 · 1단부터 · 소단 1 — 기운 땅(높은 성토) · 검사 통과 · 옹벽선 측점 = 기록 경계 정확히 · 경계 짝(± 0.025)
        RoadGrade.Input inp4;
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 20, 20);
            inp4 = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 20 - 0.6 * Math.Abs(y - OY)), Cell = 0.5, Reach = 40 };
            inp4.TierWalls.Add(new RoadGrade.TierWall(30, 70, -1, 1, 1));
            var (tris, rep, lines, set, r) = Build(inp4);
            var walls = lines.Where(l => l.Role == RoadGrade.LineRole.Wall && l.Side == -1).ToList();
            double s0 = walls.Count == 0 ? double.NaN : walls.Min(l => l.Pts.Min(p => p.S)), s1 = walls.Count == 0 ? double.NaN : walls.Max(l => l.Pts.Max(p => p.S));
            bool pairs = set.X.Any(x => x.PairStart && Math.Abs(x.S - 29.975) < 1e-9) && set.X.Any(x => x.PairStart && Math.Abs(x.S - 69.975) < 1e-9);
            check("S181 ④ 옹벽 변환 [30, 70] — 검사 통과 · 옹벽선 측점 = 30 · 70 정확히 · 경계 짝(직각 끝면)",
                rep.Ok && walls.Count > 0 && Math.Abs(s0 - 30) < 1e-9 && Math.Abs(s1 - 70) < 1e-9 && pairs,
                $"{rep.Text} · 옹벽선 {walls.Count} · 측점 {s0:F6} ~ {s1:F6} · 짝 {pairs}");

            // ⑤ 이웃 선 [전체구간] 변환 → 새 기록이 경계(70)에 정확히 맞닿음 · 같은 단면이면 짝 없이 옹벽선 한 줄로 이어짐
            var next = lines.Where(l => l.Side == -1 && l.Prof == 0 && l.Role != RoadGrade.LineRole.Wall && l.Pts.Min(p => p.S) > 69.9).OrderBy(l => l.Pts.Min(p => p.S)).FirstOrDefault();
            double ns0 = double.NaN, ns1 = double.NaN;
            if (next != null) (ns0, ns1) = RoadGrade.RangeOnLine(next, null, null);
            var inp5 = new RoadGrade.Input { X = inp4.X, Y = inp4.Y, Z = inp4.Z, Sec = sec, Base = inp4.Base, Cell = 0.5, Reach = 40 };
            inp5.TierWalls.Add(new RoadGrade.TierWall(30, 70, -1, 1, 1));
            if (next != null) inp5.TierWalls.Add(new RoadGrade.TierWall(ns0, ns1, -1, 1, 1));
            var b5 = Build(inp5);
            var w5 = b5.Lines.Where(l => l.Role == RoadGrade.LineRole.Wall && l.Side == -1).ToList();
            bool noPair70 = !b5.Set.X.Any(x => x.PairStart && Math.Abs(x.S - 69.975) < 1e-9);
            bool oneLine = w5.Any(l => l.Pts.Min(p => p.S) <= 30 + 1e-9 && l.Pts.Max(p => p.S) >= ns1 - 1e-9);
            check("S181 ⑤ 기록 옆 이웃 선 [전체구간] 변환 — 새 기록 시작 = 70 정확히(5 cm 홈 없음) · 같은 단면이라 짝 없음 · 옹벽선 한 줄",
                next != null && Math.Abs(ns0 - 70) < 1e-9 && noPair70 && oneLine && b5.Rep.Ok,
                $"이웃 선 측점 {ns0:F6} ~ {ns1:F6} · 70 짝 없음 {noPair70} · 한 줄 {oneLine} · {b5.Rep.Text}");
        }

        // ⑥ 가장자리 옹벽(0단 · 소단 0) — 검사 통과 · 그 구간 광선은 가장자리에서 바로 땅
        {
            var (X, Y, Z) = Line(0, 0, 80, 0, 8, 8);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 30 };
            inp.TierWalls.Add(new RoadGrade.TierWall(20, 60, 1, 0, 0));
            var (tris, rep, lines, set, r) = Build(inp);
            var ray = new RoadGrade.RayCtx(inp, r).SideRay(1, 40);
            bool edge = ray.EdgeWall && ray.End == RoadGrade.RayEnd.EdgeWall && Math.Abs(ray.Pts[^1].D - (8 - 0.08) * 0.01) < 1e-6;
            bool pairs = set.X.Any(x => x.PairStart && Math.Abs(x.S - 19.975) < 1e-9) && set.X.Any(x => x.PairStart && Math.Abs(x.S - 59.975) < 1e-9);
            check("S181 ⑥ 가장자리 옹벽(0단 · 소단 0) — 검사 통과 · 광선은 가장자리에서 바로 땅(평면폭 = 높이 × 0.01) · 구간 끝 짝",
                rep.Ok && edge && pairs, $"{rep.Text} · 측점 40 광선 끝 {ray.End} {ray.Pts[^1].D:F4} m · 짝 {pairs}");
        }

        // ⑦ 점 평가기 = 격자 칸 계산(비트 같음) — ②와 ④ 판에서 칸 2,000개
        {
            int bad = 0, n = 0;
            var rnd = new Random(181);
            foreach (var inp in new[] { inp4 })
            {
                var r = RoadGrade.Run(inp);
                for (int q = 0; q < 2000; q++)
                {
                    int i = rnd.Next(r.W), j = rnd.Next(r.H), k = j * r.W + i;
                    var pe = r.Eval!(r.X0 + (i + 0.5) * r.Cell, r.Y0 + (j + 0.5) * r.Cell);
                    n++;
                    bool same = BitConverter.SingleToInt32Bits(pe.Z) == BitConverter.SingleToInt32Bits(r.Z[k]) && pe.Kind == r.Kind[k]
                                && BitConverter.SingleToInt32Bits(pe.F) == BitConverter.SingleToInt32Bits(r.F[k]) && (pe.Kind == 255 || (BitConverter.SingleToInt32Bits(pe.D) == BitConverter.SingleToInt32Bits(r.D[k]) && pe.FSide == r.FSide[k]));
                    if (!same) bad++;
                }
            }
            check("S181 ⑦ 점 평가기 = 격자 칸 계산(비트 같음 · 칸 2,000)", bad == 0 && n == 2000, $"다름 {bad}/{n}");
        }

        // ⑧ 1 km 도로(완만한 곡선 · 물결 땅) — 미리보기 선(1 m 칸 결과) 시간 · 면 검사
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 1000; i++) { X.Add(OX + i); Y.Add(OY + 40 * Math.Sin(i / 160.0)); Z.Add(50 + 0.04 * i); }
            var inp = new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 50 + 0.04 * (x - OX) + 3 * Math.Sin((x - OX) / 23.0) + 2 * Math.Cos((y - OY) / 17.0)), Cell = 1.0, Reach = 40 };
            var r = RoadGrade.Run(inp);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lines = RoadGrade.ExactLines(inp, r, out string note);
            long ms = sw.ElapsedMilliseconds;
            check("S181 ⑧ 1 km 도로 — 미리보기 선(1 m 칸 결과) 3초 안 · 물러섬 없음",
                ms < 3000 && !note.StartsWith("⚠") && lines.Count > 0, $"{ms} ms · {note} · 선 {lines.Count}");
            inp.Cell = 0.5;
            var r2 = RoadGrade.Run(inp);
            var tris = RoadGrade.ExactSurface(inp, r2, out string tn, out _);
            check("S181 ⑧ 1 km 도로 — 선 면 검사 통과(칸 면으로 안 물러섬)", !tn.StartsWith("⚠") && tris.Count > 0, tn);
        }

        // ⑩ 한쪽 길어깨만(오른쪽 0) — 도로 면 점 넷 · 검사 통과(★[설치 전 검토 M1])
        {
            var (X, Y, Z) = Line(0, 0, 80, 0, 6, 6);
            var s1 = new RoadGrade.Section { ShoulderR = 0 };
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = s1, Base = new G((x, y) => 2 + 0.1 * (y - OY)), Cell = 0.5, Reach = 30 };
            var r = RoadGrade.Run(inp);
            var tris = RoadGrade.ExactSurface(inp, r, out string tn, out _);
            check("S181 ⑩ 한쪽 길어깨만 — 선 면 검사 통과(칸 면으로 안 물러섬)", !tn.StartsWith("⚠") && tris.Count > 0, tn);
        }

        // ⑨ 헤어핀(두 다리 15 m 사이 · 반원 R 7.5) · 성토 — 두 다리 사면이 만나는 주름 · 물러서면 까닭 표시(검사 통과면 선 면)
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 60; i++) { X.Add(OX + i); Y.Add(OY); Z.Add(10 + 0.05 * i); }
            double R = 7.5; int na = 24;
            for (int i = 1; i <= na; i++) { double a = -Math.PI / 2 + Math.PI * i / na; X.Add(OX + 60 + R * Math.Cos(a)); Y.Add(OY + R + R * Math.Sin(a)); Z.Add(13 + 0.05 * R * Math.PI * i / na); }
            double zt = Z[^1];
            for (int i = 1; i <= 60; i++) { X.Add(OX + 60 - i); Y.Add(OY + 2 * R); Z.Add(zt + 0.05 * i); }
            var inp = new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 6), Cell = 0.5, Reach = 30 };
            var r = RoadGrade.Run(inp);
            var tris = RoadGrade.ExactSurface(inp, r, out string tn, out _);
            check("S181 ⑨ 헤어핀(두 다리 주름) — 면이 나옴(선 면이거나, 못 넘으면 칸 면 + 까닭)", tris.Count > 0 && (tn.StartsWith("선 면") || tn.Contains("→ 칸 면")), tn);
        }

        // ── ★★[v111.1 · 계획_v111_1.md 3 · JACK 1030 «노리선도 봐줘» · «합쳐진 노리선»] 노리선 = 광선 띠 · 도로 면 자리 ──
        static double Len((Point3 A, Point3 B) t) => Math.Sqrt((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y));
        // ⑪ ⑫ 곧은 절토 · 평지 12 m — 맨 윗단(단 2 · A 17 · B 24.5)은 윗선이 데이라잇(20.12) 너머 → 노리선이 데이라잇에서 시작(옛 칸 길 0개) · 끝점 = 해석
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 0, 0);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 12), Cell = 0.5, Reach = 30 };
            var r = RoadGrade.Run(inp);
            var lines = RoadGrade.ExactLines(inp, r, out _);
            var tk = RoadGrade.Ticks(r, lines, 1.0, 5.0).Where(t => t.A.X > OX + 1 && t.A.X < OX + 99 && t.B.X > OX + 1 && t.B.X < OX + 99).ToList();   // 끝 마구리 빼고(옆 사면만 — 해석 식)
            double dDay = 17 + (12.08 - 10) * 1.5;
            static double Off(Point3 p) => Math.Abs(p.Y - OY) - 3.5;
            var top = tk.Where(t => Off(t.A) > 17 - 1e-6 && Off(t.B) > 17 - 1e-6).ToList();
            var r0 = RoadGrade.Run(inp);   // 광선 없음 → 옛 칸 길
            var old = RoadGrade.Ticks(r0, RoadGrade.TierLines(r0), 1.0, 5.0).Where(t => Off(t.A) > 17 - 0.3 && Off(t.B) > 17 - 0.3).ToList();
            int nLong = top.Count(t => Math.Abs(Len(t) - (dDay - 17)) < 1e-6), nShort = top.Count(t => Math.Abs(Len(t) - (dDay - 17) / 2) < 1e-6);
            bool startDay = top.All(t => Math.Abs(Off(t.A) - dDay) < 1e-6 && Math.Abs(t.A.Z - 12) < 1e-6);
            check("S181 ⑪ 깎기 맨 윗단(윗선이 데이라잇 너머) — 노리선이 데이라잇에서 시작 · 긴 선 = 보이는 폭 · 짧은 선 = 절반 · 옛 칸 길은 0개",
                top.Count > 150 && nLong + nShort == top.Count && nLong > 30 && startDay && old.Count == 0,
                $"윗단 {top.Count}(긴 {nLong} · 짧은 {nShort}) · 데이라잇 시작 {startDay} · 옛 길 {old.Count}");
            // ⑫ 모든 긴 선 — 시작 = 윗선(7.5 · 16) 또는 데이라잇 · 끝 = 아랫선(0 · 8.5 · 17) — 횡단 위 해석(1e-6 m) · 직각(x 같음)
            double[] starts = { 7.5, 16, dDay }, ends = { 0, 8.5, 17 };
            var longs = tk.Where(t => ends.Any(e => Math.Abs(Off(t.B) - e) < 1e-6)).ToList();
            bool ok = longs.Count > 0 && longs.All(t => starts.Any(s => Math.Abs(Off(t.A) - s) < 1e-6) && Math.Abs(t.A.X - t.B.X) < 1e-6);
            int edgeEnd = tk.Count(t => Math.Abs(Off(t.B)) < 1e-6);
            check("S181 ⑫ 노리선 끝점 = 광선 선(윗선 · 데이라잇 → 아랫선 · 가장자리) 해석(1e-6 m) · 길이 방향에 직각",
                ok && edgeEnd > 20 && longs.Count >= (tk.Count - 20) / 5, $"노리선 {tk.Count} · 끝이 선 위 {longs.Count} · 가장자리 끝 {edgeEnd}");
        }
        // ⑬ 곡선 R 30 · 성토 — 노리선이 윗선(가장자리)에 직각(2°) · 1 m 간격(윗선 길이로) · 부채(시점 · 종점 모서리) 대각선
        {
            var X = new List<double>(); var Y = new List<double>(); var Z = new List<double>();
            for (int i = 0; i <= 20; i++) { X.Add(OX + i); Y.Add(OY); Z.Add(10); }
            double R = 30, cx = 20, cy = R;
            int na = (int)Math.Ceiling(R * Math.PI / 2);
            for (int i = 1; i <= na; i++) { double a = -Math.PI / 2 + Math.PI / 2 * i / na; X.Add(OX + cx + R * Math.Cos(a)); Y.Add(OY + cy + R * Math.Sin(a)); Z.Add(10); }
            for (int i = 1; i <= 20; i++) { X.Add(OX + cx + R); Y.Add(OY + cy + i); Z.Add(10); }
            var inp = new RoadGrade.Input { X = X.ToArray(), Y = Y.ToArray(), Z = Z.ToArray(), Sec = sec, Base = new G((x, y) => 6), Cell = 0.5, Reach = 30 };
            var r = RoadGrade.Run(inp);
            var lines = RoadGrade.ExactLines(inp, r, out _);
            var tk = RoadGrade.Ticks(r, lines, 1.0, 5.0);
            var crest = lines.Where(l => l.Role == RoadGrade.LineRole.Crest && l.K == 0 && l.Side != 0).ToList();
            double worst = 0; int n = 0;
            foreach (var t in tk)
            {
                if (crest.Any(l => Math.Abs(l.Pts[0].X - t.A.X) + Math.Abs(l.Pts[0].Y - t.A.Y) < 1e-6 || Math.Abs(l.Pts[^1].X - t.A.X) + Math.Abs(l.Pts[^1].Y - t.A.Y) < 1e-6)) continue;   // 모서리(부채 대각선 · 끝 횡단) 빼고
                double best = 1e9, bx = 0, by = 0;
                foreach (var l in crest)
                    for (int i = 0; i + 1 < l.Pts.Count; i++)
                    {
                        var p = l.Pts[i]; var q = l.Pts[i + 1];
                        double dx = q.X - p.X, dy = q.Y - p.Y, l2 = dx * dx + dy * dy; if (l2 < 1e-12) continue;
                        double u = Math.Clamp(((t.A.X - p.X) * dx + (t.A.Y - p.Y) * dy) / l2, 0, 1);
                        double d = Math.Sqrt(Math.Pow(p.X + dx * u - t.A.X, 2) + Math.Pow(p.Y + dy * u - t.A.Y, 2));
                        if (d < best) { best = d; bx = dx; by = dy; }
                    }
                if (best > 1e-6) continue;
                double tx = t.B.X - t.A.X, ty = t.B.Y - t.A.Y;
                worst = Math.Max(worst, Math.Abs((tx * bx + ty * by) / (Math.Sqrt(tx * tx + ty * ty) * Math.Sqrt(bx * bx + by * by))));
                n++;
            }
            // 부채 대각선 — 끝 모서리(가장자리 끝점)에서 나가는 노리선이 옆 · 끝 사이 방향(45° ± 10°)
            var e0 = (X: X[0], Y: Y[0] + 3.5);
            int fan = tk.Count(t => Math.Abs(t.A.X - e0.X) < 1e-6 && Math.Abs(t.A.Y - e0.Y) < 1e-6 && Math.Abs(Math.Atan2(t.B.Y - t.A.Y, t.B.X - t.A.X) * 180 / Math.PI - 135) < 10);
            check("S181 ⑬ 곡선 R 30 · 성토 — 노리선이 윗선에 직각(2° — 줄인 횡단 사이 현 꺾임 2.8°의 절반 안) · 시점 모서리 부채 대각선 하나",
                n > 100 && worst < Math.Sin(2 * Math.PI / 180) && fan == 1, $"윗선 위 시작 {n} · 최대 |cos| {worst:E2} · 부채 대각선 {fan}");
        }
        // ⑭ 도로 면 자리 — 넓이 = 세모 합 · 도로 안 땅 섬(구멍) 그대로
        {
            var (X, Y, Z) = Line(0, 0, 100, 0, 5, 5);
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G((x, y) => 0), Cell = 0.5, Reach = 30 };
            var (tris, rep, _, _, _) = Build(inp);
            double sum = tris.Sum(t => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2);
            var fp = RoadFootprint.FromTris(tris, out string fn);
            // 고리(10 × 10 − 가운데 4 × 4) 세모 8
            Point3 P(double x, double y) => new(OX + x, OY + y, 0);
            var o = new[] { P(0, 0), P(10, 0), P(10, 10), P(0, 10) }; var h = new[] { P(3, 3), P(7, 3), P(7, 7), P(3, 7) };
            var ring = new List<WallDaylight.Tri>();
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; ring.Add(new(o[i], o[j], h[j])); ring.Add(new(o[i], h[j], h[i])); }
            var fr = RoadFootprint.FromTris(ring, out string fn2);
            var cut = fr == null ? null : new RoadFootprint.Cutter(fr);
            check("S181 ⑭ 도로 면 자리 — 넓이 = 세모 합(1e-6) · 안 땅 섬(구멍) 그대로 · 자가 점검 조용함",
                fp != null && Math.Abs(fp.Area - sum) < 1e-6 * sum && fn.Length == 0 && fr != null && Math.Abs(fr.Area - 84) < 1e-9 && fn2.Length == 0
                && cut != null && !cut.Inside(OX + 5, OY + 5) && cut.Inside(OX + 1, OY + 1),
                $"넓이 {fp?.Area:F6}/{sum:F6} · 고리 {fr?.Area:F6}/84 · 구멍 안 {cut?.Inside(OX + 5, OY + 5)} · {fn}{fn2}");
        }
        // ⑮ 자르기 — 지나가는 노리선은 두 조각(경계 정확히) · 밖 노리선 그대로 · 꺾은선 두 조각 · 닫힌 고리는 한 줄로 이음
        {
            Point3 P(double x, double y) => new(OX + x, OY + y, x + y);
            var sqTris = new List<WallDaylight.Tri> { new(P(0, 0), P(10, 0), P(10, 10)), new(P(0, 0), P(10, 10), P(0, 10)) };
            var c = new RoadFootprint.Cutter(RoadFootprint.FromTris(sqTris)!);
            var t1 = c.Ticks(new[] { (P(-5, 5), P(15, 5)) });
            bool pass1 = t1.Count == 2 && Math.Abs(t1[0].B.X - OX) < 1e-9 && Math.Abs(t1[1].A.X - (OX + 10)) < 1e-9 && Math.Abs(t1[0].B.Z - 5) < 1e-9 && Math.Abs(t1[1].B.X - (OX + 15)) < 1e-9;
            var t2 = c.Ticks(new[] { (P(-5, -5), P(-1, -1)) });
            bool pass2 = t2.Count == 1 && t2[0].A.Equals(P(-5, -5)) && t2[0].B.Equals(P(-1, -1));
            var l1 = c.Line(new List<Point3> { P(-5, 2), P(5, 2), P(5, 15) });
            bool pass3 = l1.Count == 2 && Math.Abs(l1[0][^1].X - OX) < 1e-9 && Math.Abs(l1[1][0].Y - (OY + 10)) < 1e-9;
            var l2 = c.Line(new List<Point3> { P(-2, -2), P(5, -2), P(5, 5), P(-2, 5), P(-2, -2) });
            bool pass4 = l2.Count == 1 && Math.Abs(l2[0][0].X - OX) < 1e-9 && Math.Abs(l2[0][0].Y - (OY + 5)) < 1e-9 && Math.Abs(l2[0][^1].Y - OY) < 1e-9;
            check("S181 ⑮ 자르기 — 지나가는 노리선 두 조각(경계 1e-9) · 밖 그대로 · 꺾은선 두 조각 · 닫힌 고리 한 줄",
                pass1 && pass2 && pass3 && pass4, $"{pass1} {pass2} {pass3} {pass4} · 조각 {t1.Count}/{l1.Count}/{l2.Count}");
        }
        // ⑱ ★[v111.2 · 전체 검토 H] 비스듬한(45°) 도로 · 깊은 깎기(땅 30 m → 사면 폭 약 51 m > 기본 범위 40) — 범위를 넓혀 데이라잇까지(광선 끝 = 데이라잇 · 범위 끝 아님)
        {
            // 양 끝은 땅에 붙고(마구리 사면 없음 → 격자 테두리에 안 닿음) 가운데만 30 m 깎기 — 옛 판정(격자 테두리 칸만)은 범위 끝을 못 알아챔
            var (X, Y, Z) = Line(0, 0, 212, 212, 0, 0);
            static double Bump(double x, double y) { double t = Math.Clamp(((x - OX) + (y - OY)) / Math.Sqrt(2) / 300, 0, 1); return 30 * Math.Min(1, 3 * Math.Sin(Math.PI * t)); }
            var inp = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G(Bump), Cell = 1.0, Reach = 40 };
            var r = RoadGrade.Run(inp);
            var lines = RoadGrade.ExactLines(inp, r, out string ln);
            var set = r.Sections;
            int reachEnd = set == null ? -1 : set.X.Sum(x => (x.L.End == RoadGrade.RayEnd.Reach ? 1 : 0) + (x.R.End == RoadGrade.RayEnd.Reach ? 1 : 0));
            double maxD = set == null ? 0 : set.X.Max(x => Math.Max(x.L.Pts[^1].D, x.R.Pts[^1].D));
            // 반대 — 같은 꼴 · 얕은 깎기(사면 폭 < 범위)는 넓히지 않음(헛 계산 없음)
            static double Bump2(double x, double y) => Bump(x, y) / 3;
            var inp2 = new RoadGrade.Input { X = X, Y = Y, Z = Z, Sec = sec, Base = new G(Bump2), Cell = 1.0, Reach = 40 };
            var r2 = RoadGrade.Run(inp2);
            check("S181 ⑱ 45° 도로 깊은 깎기(폭 51 m) — 범위를 넓혀 데이라잇까지(범위 끝 광선 0 · 데이라잇 51 m 넘음) · 얕은 깎기는 안 넓힘",
                set != null && reachEnd == 0 && r.ReachUsed > 50 && maxD > 50 && Math.Abs(r2.ReachUsed - 40) < 1e-9 && !r2.ReachHit,
                $"범위 {r.ReachUsed:F0} m · 범위 끝 광선 {reachEnd} · 최대 광선 {maxD:F2} m · 얕은 깎기 범위 {r2.ReachUsed:F0} m · {ln}");
        }
        // ⑰ ★[v111.2 · JACK 1030 «주측점엔 PVI»] 주측점 PVI — 측점 간격 배수마다 · 모양 그대로 · 처음 · 끝 PVI 밖 없음 · 설계 PVI 곁 2.5 m(간격/4 · 2.5 m 중 작은 것) 안이면 안 넣음
        {
            var pvi = new List<(double S, double Z)> { (0, 105), (107, 78.34), (269, 78.89), (343.1, 70.01) };
            var (all, added) = RoadProfileDraft.AddStations(pvi, 20);
            bool on = all.All(p => Math.Abs(RoadProfileDraft.At(pvi, p.S) - p.Z) < 1e-12);
            bool sorted = all.Zip(all.Skip(1)).All(z => z.First.S < z.Second.S);
            var (near, addN) = RoadProfileDraft.AddStations(new List<(double S, double Z)> { (0, 10), (40.5, 12), (100, 8) }, 20);
            check("S181 ⑰ 주측점 PVI — 20 m마다 17개 · 모양 그대로 · 차례 · 설계 PVI(40.5) 2.5 m 안 40은 안 넣음",
                added == 17 && all.Count == 21 && on && sorted && all[0] == pvi[0] && all[^1] == pvi[^1] && addN == 3 && !near.Any(p => p.S == 40),
                $"더함 {added} · 합 {all.Count} · 선 위 {on} · 차례 {sorted} · 가까운 PVI 경우 {addN}개({string.Join(",", near.Select(p => p.S))})");
        }
        // ⑯ 부지 노리선 리듬 — 성토 사면 일부가 도로 면 밑: 긴 선 = 도로 경계까지(보이는 폭) · 짧은 선 = 그 절반 · 도로 밑 0 · 먼 곳은 전과 같음
        {
            var sq = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 60, 100), new(0, 60, 100) };
            var pa = new GradingParams { CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1, CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 20, MaxRise = 60, VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0 };
            var vs = GradingGeometry.Build(sq, new FlatGround(90), pa, false);
            var outer = vs.Rings.First(rg => Math.Abs(rg.Average(q => q.Z) - 90) < 0.01);   // 데이라잇(땅 90 = 2단 아랫선) — DHNORI의 최종 경계 자리
            Point3 Q(double x, double y) => new(x, y, 0);
            var area = RoadFootprint.FromTris(new List<WallDaylight.Tri> { new(Q(64, 20), Q(90, 20), Q(90, 40)), new(Q(64, 20), Q(90, 40), Q(64, 40)) })!;
            var (tA, _, _) = SlopeHatchGenerator.Generate(vs.Rings, new NullGround(), false, 1.0, 5.0, outer, sq, extraArea: area);
            var (tB, _, _) = SlopeHatchGenerator.Generate(vs.Rings, new NullGround(), false, 1.0, 5.0, outer, sq);
            var east = tA.Where(t => t.A.X > 59.9 && t.A.Y > 21 && t.A.Y < 39).ToList();
            int nL = east.Count(t => Math.Abs(Len(t) - 4) < 1e-6), nS = east.Count(t => Math.Abs(Len(t) - 2) < 1e-6);
            var c = new RoadFootprint.Cutter(area);
            int under = tA.Count(t => Enumerable.Range(1, 99).Any(k => c.Inside(t.A.X + (t.B.X - t.A.X) * k / 100.0, t.A.Y + (t.B.Y - t.A.Y) * k / 100.0)));
            static bool Far((Point3 A, Point3 B) t) => t.A.Y < 15 || t.A.Y > 45 || t.A.X < 55;
            var fa = tA.Where(Far).ToList(); var fb = tB.Where(Far).ToList();
            bool same = fa.Count == fb.Count && fa.Zip(fb).All(z => z.First.A.Equals(z.Second.A) && z.First.B.Equals(z.Second.B));
            check("S181 ⑯ 부지 노리선 — 도로 면 밑 사면: 긴 선 4 m(경계까지) · 짧은 선 2 m(보이는 폭 절반) · 도로 밑 0 · 먼 곳 그대로",
                east.Count >= 15 && nL + nS == east.Count && nL >= 3 && nS >= 10 && under == 0 && same,
                $"동쪽 띠 {east.Count}(긴 {nL} · 짧은 {nS}) · 도로 밑 {under} · 먼 곳 {fa.Count}/{fb.Count} 같음 {same} · 다른 것 " + string.Join(" ", east.Where(t => Math.Abs(Len(t) - 4) > 1e-6 && Math.Abs(Len(t) - 2) > 1e-6).Take(6).Select(t => $"({t.A.X:F2},{t.A.Y:F2},{t.A.Z:F2})→({t.B.X:F2},{t.B.Y:F2},{t.B.Z:F2})")));
        }
    }
}
