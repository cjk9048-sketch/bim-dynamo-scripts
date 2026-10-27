namespace DH.Grading.Core;

/// <summary>★★[v108 · 계획_v108.md 2.1 · JACK 1027 «빗물 흐름·고임» — 물길 + 고임 + 평지] <b>정지면 빗물 검토</b> — 세모 면을 격자로 바꿔 웅덩이 · 평지 · 물길을 가린다.
/// <list type="bullet">
/// <item>격자: 칸 가운데 높이(세모 평면 · 세모 밖 = 없음).</item>
/// <item>웅덩이 채우기 = priority-flood(Barnes 2014) — 경계 · 없음 칸 곁에서 시작해 낮은 칸부터 · 둘레보다 낮은 칸은 <see cref="Eps"/>만큼씩 올려 평지에도 흐름이 생기게.</item>
/// <item><b>고임</b> = 채운 높이 − 땅 > <see cref="PondMin"/> · 이어진 덩어리 넓이 ≥ 1 ㎡ · 물 높이 = 덩어리 최고 채움.</item>
/// <item><b>평지</b> = 땅 기울기 &lt; <see cref="FlatSlope"/> · 고임 아님 · 부르는 쪽이 고른 자리(바뀐 칸) 안.</item>
/// <item>흐름 D8(채운 높이 · 대각 √2) · <b>모인 넓이</b>(㎡) · <b>물길</b> = 모인 넓이 ≥ <c>streamMin</c> 칸을 아래로 이어 선(꼭짓점마다 모인 넓이).</item>
/// </list></summary>
public static class Drainage
{
    public const double Eps = 1e-5, PondMin = 0.02, FlatSlope = 0.005;
    /// <summary>물길 판정 — 모인 넓이가 «흐름 길이 × 칸»의 몇 배 넘어야 모인 물(골)로 보나.</summary>
    public const double ConvergeK = 4.0;

    public sealed class Pond
    {
        public double Level, Area, Depth, Volume;
        /// <summary>물 면 — 줄마다 이어진 칸 (x0, y0, x1, y1).</summary>
        public readonly List<(double X0, double Y0, double X1, double Y1)> Runs = new();
    }

    public sealed class Stream
    {
        public readonly List<Point3> Pts = new();
        public readonly List<double> Acc = new();
    }

    public sealed class Result
    {
        public double X0, Y0, Cell; public int W, H, Valid;
        public float[] Z = Array.Empty<float>(), Fill = Array.Empty<float>(), AccArea = Array.Empty<float>();
        public bool[] Flat = Array.Empty<bool>();
        public readonly List<Pond> Ponds = new();
        public readonly List<Stream> Streams = new();
        /// <summary>평지 — 줄마다 이어진 칸 (x0, y0, x1, y1, z0, z1).</summary>
        public readonly List<(double X0, double Y0, double X1, double Y1, double Z0, double Z1)> FlatRuns = new();
        public double FlatArea;
        public long Ms;
        public string Text => $"칸 {Cell:0.##} m · {W}×{H}(땅 {Valid:N0}) · 고임 {Ponds.Count}곳 {Ponds.Sum(p => p.Area):N0} ㎡(가장 깊은 {(Ponds.Count > 0 ? Ponds.Max(p => p.Depth) : 0):F2} m) · 평지 {FlatArea:N0} ㎡ · 물길 {Streams.Count}줄 · {Ms / 1000.0:F1}초";
    }

    static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 }, DY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>세모 면(점 X · Y · Z · 면 F 0부터 셋씩)을 [minX, maxX] × [minY, maxY] 격자(칸 <paramref name="cell"/> — 칸 수가 <paramref name="maxCells"/>를 넘으면 키움)로 셈한다.
    /// <paramref name="flatZone"/>이 있으면 그 안에서만 평지를 센다(바뀐 칸).</summary>
    public static Result Analyze(double[] X, double[] Y, double[] Z, int[] F, double minX, double minY, double maxX, double maxY,
        double cell = 1.0, int maxCells = 1_000_000, double streamMin = 100, Func<double, double, bool>? flatZone = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = new Result();
        // ★[v108 설치 전 검토 낮음] 칸 ≤ 0 · NaN이면 아래 키우기가 끝나지 않는다 · 범위가 NaN이면 2×2 헛격자 — 빈 결과로
        if (!(cell > 0) || !double.IsFinite(cell) || !double.IsFinite(minX + minY + maxX + maxY) || maxX <= minX || maxY <= minY || maxCells < 4)
            return r;
        while ((maxX - minX) / cell * ((maxY - minY) / cell) > maxCells) cell *= 1.25;
        int W = Math.Max(2, (int)Math.Ceiling((maxX - minX) / cell)), H = Math.Max(2, (int)Math.Ceiling((maxY - minY) / cell));
        r.X0 = minX; r.Y0 = minY; r.Cell = cell; r.W = W; r.H = H;
        int N = W * H;
        var z = new double[N];
        Array.Fill(z, double.NaN);
        // ① 격자 — 세모마다 상자 안 칸 가운데가 세모 안이면 그 평면 높이
        for (int t = 0; t + 2 < F.Length; t += 3)
        {
            int a = F[t], b = F[t + 1], c = F[t + 2];
            double ax = X[a], ay = Y[a], bx = X[b], by = Y[b], cx = X[c], cy = Y[c];
            // 점 하나라도 NaN · 무한이면 그 세모는 버린다(이웃 세모가 채운 칸을 NaN으로 덮지 않게)
            if (!double.IsFinite(ax + ay + bx + by + cx + cy + Z[a] + Z[b] + Z[c])) continue;
            double den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(den) < 1e-12) continue;
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, Math.Min(bx, cx)) - minX) / cell - 0.5)), i1 = Math.Min(W - 1, (int)Math.Ceiling((Math.Max(ax, Math.Max(bx, cx)) - minX) / cell - 0.5));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(ay, Math.Min(by, cy)) - minY) / cell - 0.5)), j1 = Math.Min(H - 1, (int)Math.Ceiling((Math.Max(ay, Math.Max(by, cy)) - minY) / cell - 0.5));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = minX + (i + 0.5) * cell, py = minY + (j + 0.5) * cell;
                    double l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den;
                    double l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den;
                    double l3 = 1 - l1 - l2;
                    if (!(l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9)) continue;
                    z[j * W + i] = l1 * Z[a] + l2 * Z[b] + l3 * Z[c];
                }
        }
        bool Ok(int k) => !double.IsNaN(z[k]);
        for (int k = 0; k < N; k++) if (Ok(k)) r.Valid++;

        // ② 웅덩이 채우기(priority-flood + ε) — 바깥(격자 끝 · 없음 칸 곁)에서 시작
        var fill = new double[N];
        var closed = new bool[N];
        var pq = new PriorityQueue<int, double>();
        for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int k = j * W + i;
                if (!Ok(k)) continue;
                bool edge = i == 0 || j == 0 || i == W - 1 || j == H - 1;
                for (int d = 0; d < 8 && !edge; d++) if (!Ok((j + DY[d]) * W + i + DX[d])) edge = true;
                if (!edge) continue;
                fill[k] = z[k]; closed[k] = true; pq.Enqueue(k, z[k]);
            }
        while (pq.TryDequeue(out int c, out double fc))
        {
            int ci = c % W, cj = c / W;
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + DX[d], nj = cj + DY[d];
                if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                int n = nj * W + ni;
                if (closed[n] || !Ok(n)) continue;
                closed[n] = true;
                fill[n] = Math.Max(z[n], fc + Eps);
                pq.Enqueue(n, fill[n]);
            }
        }

        // ③ 고임 덩어리
        var pondId = new int[N];
        Array.Fill(pondId, -1);
        var stack = new Stack<int>();
        for (int k = 0; k < N; k++)
        {
            if (!Ok(k) || pondId[k] >= 0 || fill[k] - z[k] <= PondMin) continue;
            var cells = new List<int>();
            pondId[k] = -2; stack.Push(k);
            while (stack.Count > 0)
            {
                int c = stack.Pop(); cells.Add(c);
                int ci = c % W, cj = c / W;
                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + DX[d], nj = cj + DY[d];
                    if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                    int n = nj * W + ni;
                    if (!Ok(n) || pondId[n] != -1 || fill[n] - z[n] <= PondMin) continue;
                    pondId[n] = -2; stack.Push(n);
                }
            }
            double area = cells.Count * cell * cell;
            if (area < 1.0) { foreach (int c in cells) pondId[c] = -3; continue; }
            var p = new Pond { Area = area, Level = cells.Max(c => fill[c]), Depth = cells.Max(c => fill[c] - z[c]), Volume = cells.Sum(c => (fill[c] - z[c]) * cell * cell) };
            int id = r.Ponds.Count;
            foreach (int c in cells) pondId[c] = id;
            r.Ponds.Add(p);
        }
        foreach (var (p, id) in r.Ponds.Select((p, i) => (p, i)))
            for (int j = 0; j < H; j++)
            {
                int run = -1;
                for (int i = 0; i <= W; i++)
                {
                    bool inP = i < W && pondId[j * W + i] == id;
                    if (inP && run < 0) run = i;
                    if (!inP && run >= 0) { p.Runs.Add((minX + run * cell, minY + j * cell, minX + i * cell, minY + (j + 1) * cell)); run = -1; }
                }
            }

        // ④ 평지 — 땅 기울기(가운데 차분) < 0.5 % · 고임 아님 · 고른 자리 안
        var flat = new bool[N];
        for (int j = 1; j < H - 1; j++)
            for (int i = 1; i < W - 1; i++)
            {
                int k = j * W + i;
                if (!Ok(k) || pondId[k] >= 0) continue;
                int e = k + 1, w = k - 1, nn = k + W, s = k - W;
                if (!Ok(e) || !Ok(w) || !Ok(nn) || !Ok(s)) continue;
                double gx = (z[e] - z[w]) / (2 * cell), gy = (z[nn] - z[s]) / (2 * cell);
                if (Math.Sqrt(gx * gx + gy * gy) >= FlatSlope) continue;
                double px = minX + (i + 0.5) * cell, py = minY + (j + 0.5) * cell;
                if (flatZone != null && !flatZone(px, py)) continue;
                flat[k] = true; r.FlatArea += cell * cell;
            }
        for (int j = 0; j < H; j++)
        {
            int run = -1;
            for (int i = 0; i <= W; i++)
            {
                bool f = i < W && flat[j * W + i];
                if (f && run < 0) run = i;
                if (!f && run >= 0) { r.FlatRuns.Add((minX + run * cell, minY + j * cell, minX + i * cell, minY + (j + 1) * cell, z[j * W + run], z[j * W + i - 1])); run = -1; }
            }
        }

        // ⑤ 흐름 D8(채운 높이) · 모인 넓이 — 높은 칸부터 받는 칸에 더한다
        var to = new int[N];
        Array.Fill(to, -1);
        for (int k = 0; k < N; k++)
        {
            if (!Ok(k)) continue;
            int ci = k % W, cj = k / W; double best = 0; int bi = -1;
            for (int d = 0; d < 8; d++)
            {
                int ni = ci + DX[d], nj = cj + DY[d];
                if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                int n = nj * W + ni;
                if (!Ok(n)) continue;
                double drop = (fill[k] - fill[n]) / (d % 2 == 0 ? 1.0 : 1.41421356);
                if (drop > best) { best = drop; bi = n; }
            }
            to[k] = bi;
        }
        var order = Enumerable.Range(0, N).Where(Ok).OrderByDescending(k => fill[k]).ToArray();
        var acc = new double[N];
        var flowLen = new double[N];   // 가장 긴 위쪽 흐름 길이(m) — 나란히 흐르는 판에서는 모인 넓이 ≈ 길이 × 칸
        foreach (int k in order) acc[k] += cell * cell;
        foreach (int k in order)
            if (to[k] >= 0)
            {
                acc[to[k]] += acc[k];
                int dx = to[k] % W - k % W, dy = to[k] / W - k / W;
                flowLen[to[k]] = Math.Max(flowLen[to[k]], flowLen[k] + cell * Math.Sqrt(dx * dx + dy * dy));
            }

        // ⑥ 물길 — 모인 넓이 ≥ streamMin · ★[계획 검토 중간 5] <b>정말 모이는 곳만</b>(모인 넓이 ≥ 4 × 흐름 길이 × 칸 — 기운 판의 나란한 흐름 · 평지 ε 흐름은 물길이 아니다) ·
        //   평지 · 고인 물 안 칸은 빼고 · 위에서 이어 오는 물길 칸이 없는 칸(머리)부터 아래로 · 이미 지난 칸에 닿으면 거기까지(합류)
        var isS = new bool[N]; var up = new int[N];
        foreach (int k in order)
            if (acc[k] >= streamMin && acc[k] >= ConvergeK * (flowLen[k] + cell) * cell && pondId[k] < 0 && !flat[k]) isS[k] = true;
        foreach (int k in order) if (isS[k] && to[k] >= 0 && isS[to[k]]) up[to[k]]++;
        var done = new bool[N];
        foreach (int k in order)
        {
            if (!isS[k] || up[k] > 0 || done[k]) continue;
            var st = new Stream();
            int c = k;
            while (c >= 0)
            {
                int ci = c % W, cj = c / W;
                st.Pts.Add(new Point3(minX + (ci + 0.5) * cell, minY + (cj + 0.5) * cell, z[c]));
                st.Acc.Add(acc[c]);
                if (done[c]) break;
                done[c] = true;
                c = to[c];
                if (c >= 0 && !isS[c]) break;   // 고임으로 흘러 들어감
            }
            if (st.Pts.Count >= 2) r.Streams.Add(st);
        }

        r.Z = z.Select(v => (float)v).ToArray(); r.Fill = fill.Select(v => (float)v).ToArray(); r.AccArea = acc.Select(v => (float)v).ToArray(); r.Flat = flat;
        r.Ms = sw.ElapsedMilliseconds;
        return r;
    }
}
