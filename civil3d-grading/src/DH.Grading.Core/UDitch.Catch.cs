using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★[JACK 1124 «측구 설계 시 집수면적 같은 걸 프로그램에서 기본적으로 제시 · 3D 뷰 빗물 기능으로 설계»] U형 측구 <b>집수 자동</b> —
/// 빗물 셈(<see cref="Drainage.Analyze"/> · 측구 물길을 판 격자)에서 칸마다 물이 처음 닿는 측구 = 그 측구의 집수 구역 · 넓이 · 토지 이용 C(넓이 가중) ·
/// 측구 시점 → 종점 측점마다 들어오는 넓이(위 측구가 흘러드는 몫 포함)를 누가 → 구간 유량 Q = C · I · A / 360.
/// <para>사용자 값이 이긴다: 집수면적을 친 측구(<see cref="Ditch.AreaAuto"/> 거짓 · 넓이 &gt; 0)는 그대로 · C를 친 측구(<see cref="Ditch.CUser"/>)도 그대로.
/// 구간 몫은 «친 전체 넓이 × 지형이 정한 누가 비율»(<see cref="ReachFlow"/>).</para></summary>
public static partial class UDitch
{
    public sealed partial class Settings
    {
        /// <summary>★[JACK 1124 집수] 유출계수 — 포장 · 도로 · 평탄한 계획부지(바뀐 자리 · 완만).</summary>
        public double CPave { get; set; } = 0.9;
        /// <summary>사면(바뀐 자리 · <see cref="CSlopePct"/> 이상 — 잔디 · 녹화).</summary>
        public double CSlope { get; set; } = 0.4;
        /// <summary>안 바뀐 원지반(산림 · 밭).</summary>
        public double CNatural { get; set; } = 0.3;
        /// <summary>이 경사(%) 이상인 바뀐 자리 = 사면.</summary>
        public double CSlopePct { get; set; } = 15;
        /// <summary>집수 셈 범위 = 측구 둘레 + 이 거리(m · [3D 보기] 빗물과 같은 200 m).</summary>
        public double CatchMargin { get; set; } = 200;
        /// <summary>집수 셈 칸(m · 칸 수가 100만을 넘으면 키움).</summary>
        public double CatchCell { get; set; } = 1.0;
    }

    static void CatchFix(Settings s, List<string> bad)
    {
        static bool C01(double v) => double.IsFinite(v) && v > 0 && v <= 1;
        if (!C01(s.CPave)) { bad.Add("CPave"); s.CPave = 0.9; }
        if (!C01(s.CSlope)) { bad.Add("CSlope"); s.CSlope = 0.4; }
        if (!C01(s.CNatural)) { bad.Add("CNatural"); s.CNatural = 0.3; }
        if (!double.IsFinite(s.CSlopePct) || s.CSlopePct <= 0 || s.CSlopePct > 200) { bad.Add("CSlopePct"); s.CSlopePct = 15; }
        if (!double.IsFinite(s.CatchMargin) || s.CatchMargin < 0 || s.CatchMargin > 2000) { bad.Add("CatchMargin"); s.CatchMargin = 200; }
        if (!double.IsFinite(s.CatchCell) || s.CatchCell < 0.25 || s.CatchCell > 20) { bad.Add("CatchCell"); s.CatchCell = 1.0; }
    }

    public sealed partial class Ditch
    {
        /// <summary>★[JACK 1124 집수] 참 = <see cref="AreaHa"/>를 [자동 집수]가 채움(창 «자동») · 거짓 = 사람이 친 값(창 «입력» — 자동이 안 덮음 · 0이면 채움).</summary>
        public bool AreaAuto { get; set; }
        /// <summary>참 = <see cref="C"/>를 자동(토지 이용 넓이 가중)이 채움.</summary>
        public bool CAuto { get; set; }
        /// <summary>참 = 사람이 C를 고쳐 침(자동이 안 덮음).</summary>
        public bool CUser { get; set; }
        /// <summary>마지막 자동 집수 결과(없으면 null — 구간 몫 없이 측구 전체 값).</summary>
        public CatchInfo? Auto { get; set; }
    }

    /// <summary>★[JACK 1124] 자동 집수 결과 — 직접 넓이(이 측구에 처음 닿는 땅) · 전체(위 측구 몫 포함) · C(넓이 가중 · 0 = 모름) · 위 · 아래 측구 · 누가 유입 [측점 m, 누가 넓이 ㎡, 누가 C·A ㎡].</summary>
    public sealed class CatchInfo
    {
        public double DirectHa { get; set; }
        public double TotalHa { get; set; }
        public double C { get; set; }
        public List<string> Up { get; set; } = new();
        public string Down { get; set; } = "";
        public string Src { get; set; } = "";
        public string When { get; set; } = "";
        /// <summary>참 = 구역이 셈 범위 끝에 닿음(범위 밖 위쪽 땅이 빠졌을 수 있음).</summary>
        public bool Edge { get; set; }
        [JsonConverter(typeof(CompactPairs))]
        public List<double[]> Inflow { get; set; } = new();
    }

    /// <summary>구간 끝 측점 <paramref name="sta"/>까지 모인 몫 — 넓이(ha) · C · 유량(m³/s · 강우 없음 = NaN).
    /// 자동 집수가 없으면 측구 전체 값. 있으면 넓이 = 측구 집수면적 × 누가 비율(끝 = 1) · C = 자동이면 그 자리까지 넓이 가중.</summary>
    public static (double Aha, double C, double Q) ReachFlow(Ditch d, Rain r, double sta, double length)
    {
        double A = d.AreaHa, C = d.C;
        var inf = d.Auto?.Inflow;
        if (inf != null && inf.Count > 0 && A > 0 && sta < length - 1e-6)
        {
            double tot = inf[^1].Length > 1 ? inf[^1][1] : 0;
            if (tot > 0)
            {
                double aa = 0, ca = 0;
                foreach (var p in inf)
                {
                    if (p.Length < 3 || p[0] > sta + 1e-6) break;
                    aa = p[1]; ca = p[2];
                }
                A = d.AreaHa * Math.Min(1, aa / tot);
                if (d.CAuto && aa > 0 && ca > 0) C = Math.Clamp(ca / aa, 0, 1);
            }
        }
        double Q = !r.Has ? double.NaN : A <= 0 ? 0 : C > 0 && C <= 1 ? RationalQ(C, r.IntensityMmh, A) : double.NaN;
        return (A, C, Q);
    }

    /// <summary>구간 유량(설계유량 <paramref name="Qall"/> = 측구 끝) — 자동 집수 누가 비율로 줄임(없으면 그대로).</summary>
    static double ReachQ(Ditch d, Rain r, double sta, double length, double Qall)
    {
        if (!(Qall > 0) || d.Auto == null || d.Auto.Inflow.Count == 0) return Qall;
        var f = ReachFlow(d, r, sta, length);
        return double.IsFinite(f.Q) ? Math.Min(Qall, f.Q) : Qall;
    }

    /// <summary>창 · 보고서 «집수» 줄 — «자동 0.1234 ha · C 0.62(직접 0.1 + 위 D-002)» · «입력».</summary>
    public static string CatchText(Ditch d)
    {
        var a = d.Auto;
        string src = d.AreaAuto ? "자동" : d.AreaHa > 0 ? "입력" : "없음";
        var sb = new StringBuilder(src);
        if (d.AreaHa > 0) sb.Append(' ').Append(d.AreaHa.ToString("0.####", Ic)).Append(" ha");
        sb.Append(" · C ").Append(d.C.ToString("0.##", Ic)).Append(d.CAuto ? "(자동)" : "");
        if (a != null && a.TotalHa > 0 && !d.AreaAuto) sb.Append(" · 자동 ").Append(a.TotalHa.ToString("0.####", Ic)).Append(" ha");
        if (a != null && a.Up.Count > 0) sb.Append(" · 직접 ").Append(a.DirectHa.ToString("0.####", Ic)).Append(" + 위 ").Append(string.Join("·", a.Up));
        if (a != null && a.Edge) sb.Append(" · ⚠범위 끝");
        return sb.ToString();
    }

    /// <summary>[자동 집수] 결과를 기록에 — <paramref name="order"/>[i] = <paramref name="res"/>.Zones[i]. 넓이 · C는 사람이 친 값이 없을 때만 채움. 돌려줌 = (넓이 채움, C 채움).</summary>
    public static (int Area, int C) ApplyCatch(IReadOnlyList<Ditch> order, DitchCatch.Result res, Settings s, string src, string when)
    {
        int na = 0, nc = 0;
        for (int i = 0; i < order.Count && i < res.Zones.Length; i++)
        {
            var d = order[i]; var z = res.Zones[i];
            double totHa = z.TotalM2 / 10000.0;
            double c = res.HasC && z.TotalM2 > 0 ? z.TotalCA / z.TotalM2 : 0;
            d.Auto = new CatchInfo
            {
                DirectHa = Math.Round(z.DirectM2 / 10000.0, 6), TotalHa = Math.Round(totHa, 6), C = Math.Round(c, 4),
                Up = z.Up.Where(u => u >= 0 && u < order.Count).Select(u => order[u].Id).ToList(),
                Down = z.Down >= 0 && z.Down < order.Count ? order[z.Down].Id : "",
                Src = src, When = when, Edge = z.Edge,
                Inflow = z.Inflow.Select(p => new[] { Math.Round(p[0], 2), Math.Round(p[1], 2), Math.Round(p[2], 2) }).ToList(),
            };
            if (totHa > 0 && (d.AreaAuto || !(d.AreaHa > 0))) { d.AreaHa = Math.Round(totHa, 4); d.AreaAuto = true; na++; }
            if (c > 0 && !d.CUser && (d.CAuto || Math.Abs(d.C - s.DefaultC) < 1e-9)) { d.C = Math.Round(c, 2); d.CAuto = true; nc++; }
        }
        return (na, nc);
    }
}

/// <summary>★★[JACK 1124 «집수면적 자동»] 측구 집수 구역 — <see cref="Drainage.Analyze"/>(측구 물길이 든 셈)의 흐름(<see cref="Drainage.Result.To"/>)을 따라
/// 칸마다 처음 닿는 측구를 찾는다. 측구 끝 물이 다른 측구로 가면 그 측구 측점에 위 측구 전체 몫을 더한다(고리 없음 — 흐름은 늘 채운 높이가 낮은 쪽).</summary>
public static class DitchCatch
{
    /// <summary>구역 색(평면 · 3D 같은 차례 · RGB).</summary>
    public static readonly (byte R, byte G, byte B)[] Palette =
    {
        (0x2E, 0x86, 0xDE), (0xE6, 0x7E, 0x22), (0x27, 0xAE, 0x60), (0x8E, 0x44, 0xAD), (0xD3, 0x54, 0x00),
        (0x16, 0xA0, 0x85), (0xC0, 0x39, 0x2B), (0x7F, 0x8C, 0x8D), (0xF1, 0xC4, 0x0F), (0x34, 0x49, 0x5E),
    };

    public sealed class Zone
    {
        public int Cells;
        public double DirectM2, DirectCA, TotalM2, TotalCA;
        /// <summary>끝 물이 흘러드는 측구(-1 = 없음 · 땅으로) · 그 측구 측점.</summary>
        public int Down = -1;
        public double DownSta = double.NaN;
        public readonly List<int> Up = new();
        public bool Edge;
        /// <summary>누가 유입 [측점 m, 누가 넓이 ㎡, 누가 C·A ㎡] — 측점 1 m 칸 · 끝 = 전체.</summary>
        public readonly List<double[]> Inflow = new();
    }

    public sealed class Result
    {
        public double X0, Y0, Cell; public int W, H, Valid;
        /// <summary>칸마다 구역(측구 차례 · -1 = 어느 측구에도 안 닿음 · 땅 없음).</summary>
        public int[] Owner = Array.Empty<int>();
        public Zone[] Zones = Array.Empty<Zone>();
        public bool HasC;
        public long Ms;
        public string Text => $"칸 {Cell:0.##} m · 구역 {Zones.Count(z => z.Cells > 0)}/{Zones.Length} · 직접 합 {Zones.Sum(z => z.DirectM2) / 10000.0:0.####} ha · 땅 {Valid * Cell * Cell / 10000.0:0.####} ha" + (HasC ? "" : " · C 모름") + $" · {Ms} ms";
    }

    /// <summary>구역 나누기 — <paramref name="nCh"/> = 측구 수(<c>channels</c> 차례) · <paramref name="cOf"/> = 칸 C(NaN · null = 모름).</summary>
    public static Result Split(Drainage.Result dr, int nCh, Func<int, double>? cOf = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int W = dr.W, H = dr.H, N = W * H;
        var res = new Result { X0 = dr.X0, Y0 = dr.Y0, Cell = dr.Cell, W = W, H = H, Valid = dr.Valid, Zones = new Zone[Math.Max(0, nCh)] };
        for (int i = 0; i < res.Zones.Length; i++) res.Zones[i] = new Zone();
        res.Owner = new int[N];
        Array.Fill(res.Owner, -1);
        if (N == 0 || dr.To.Length != N || dr.Z.Length != N || nCh <= 0) { res.Ms = sw.ElapsedMilliseconds; return res; }
        var chOf = new int[N]; Array.Fill(chOf, -1);
        var sta = new double[N];
        var last = new int[nCh]; var lastSta = new double[nCh];
        Array.Fill(last, -1); Array.Fill(lastSta, double.NegativeInfinity);
        foreach (var (ch, cells, ss) in dr.ChanPaths)
        {
            if (ch < 0 || ch >= nCh) continue;
            for (int q = 0; q < cells.Length; q++)
            {
                int k = cells[q];
                if (chOf[k] < 0) { chOf[k] = ch; sta[k] = ss[q]; }
                else if (chOf[k] == ch) sta[k] = Math.Min(sta[k], ss[q]);
                if (chOf[k] == ch && ss[q] >= lastSta[ch]) { lastSta[ch] = ss[q]; last[ch] = k; }
            }
        }
        // 칸마다 처음 닿는 측구 칸(-1 = 없음) — 흐름을 따라 내려가며 지난 칸을 한꺼번에(고리 없음 · 걸음 한도)
        var dest = new int[N]; Array.Fill(dest, -2);
        var to = dr.To;
        var stack = new List<int>();
        for (int k = 0; k < N; k++)
        {
            if (dest[k] != -2) continue;
            if (float.IsNaN(dr.Z[k])) { dest[k] = -1; continue; }
            int c = k, guard = 0;
            while (c >= 0 && dest[c] == -2 && chOf[c] < 0 && guard++ < N) { stack.Add(c); c = to[c]; }
            int d = -1;
            if (c >= 0)
            {
                if (chOf[c] >= 0) { dest[c] = c; d = c; }
                else if (dest[c] >= -1) d = dest[c];
            }
            foreach (int s in stack) dest[s] = d;
            stack.Clear();
        }
        res.HasC = cOf != null;
        double a1 = dr.Cell * dr.Cell;
        var bins = new SortedDictionary<double, (double A, double CA)>[nCh];
        for (int i = 0; i < nCh; i++) bins[i] = new();
        for (int k = 0; k < N; k++)
        {
            int d = dest[k];
            if (d < 0) continue;
            int o = chOf[d];
            res.Owner[k] = o;
            var z = res.Zones[o];
            double c = cOf?.Invoke(k) ?? double.NaN;
            double ca = double.IsFinite(c) ? c * a1 : 0;
            if (res.HasC && !double.IsFinite(c)) res.HasC = false;
            z.Cells++; z.DirectM2 += a1; z.DirectCA += ca;
            int i = k % W, j = k / W;
            if (i == 0 || j == 0 || i == W - 1 || j == H - 1) z.Edge = true;
            double key = Math.Floor(sta[d]);
            bins[o][key] = bins[o].TryGetValue(key, out var b) ? (b.A + a1, b.CA + ca) : (a1, ca);
        }
        // 측구 끝 물 → 다른 측구(그 측점)
        for (int ch = 0; ch < nCh; ch++)
        {
            int e = last[ch];
            if (e < 0) continue;
            int c = to[e];
            if (c < 0)
            {
                // 끝 칸이 웅덩이(빗물 셈이 측구 바닥을 하류로 낮춰 끝이 둘레보다 낮음) → 둘레 칸 중 이 측구로 안 돌아오는 가장 낮은 칸으로 넘침
                int ei = e % W, ej = e / W; double best = double.PositiveInfinity;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = ei + di, nj = ej + dj;
                        if ((di == 0 && dj == 0) || ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                        int nb = nj * W + ni;
                        if (float.IsNaN(dr.Z[nb]) || chOf[nb] == ch) continue;
                        int dn = chOf[nb] >= 0 ? nb : dest[nb];
                        if (dn >= 0 && chOf[dn] == ch) continue;
                        if (dr.Fill[nb] < best) { best = dr.Fill[nb]; c = nb; }
                    }
                if (c < 0) continue;
            }
            int d = chOf[c] >= 0 ? c : dest[c];
            if (d >= 0 && chOf[d] >= 0 && chOf[d] != ch) { res.Zones[ch].Down = chOf[d]; res.Zones[ch].DownSta = sta[d]; res.Zones[chOf[d]].Up.Add(ch); }
        }
        // 전체 = 직접 + 위 측구 전체(깊이 우선 · 고리 막음)
        var state = new int[nCh];
        (double A, double CA) Total(int ch)
        {
            var z = res.Zones[ch];
            if (state[ch] == 2) return (z.TotalM2, z.TotalCA);
            if (state[ch] == 1) return (0, 0);
            state[ch] = 1;
            double A = z.DirectM2, CA = z.DirectCA;
            foreach (int u in z.Up)
            {
                var t = Total(u);
                A += t.A; CA += t.CA;
                double key = Math.Floor(res.Zones[u].DownSta);
                bins[ch][key] = bins[ch].TryGetValue(key, out var b) ? (b.A + t.A, b.CA + t.CA) : (t.A, t.CA);
            }
            z.TotalM2 = A; z.TotalCA = CA; state[ch] = 2;
            return (A, CA);
        }
        for (int ch = 0; ch < nCh; ch++) Total(ch);
        for (int ch = 0; ch < nCh; ch++)
        {
            double ca = 0, cc = 0;
            foreach (var (key, v) in bins[ch]) { ca += v.A; cc += v.CA; res.Zones[ch].Inflow.Add(new[] { key, ca, cc }); }
        }
        res.Ms = sw.ElapsedMilliseconds;
        return res;
    }

    /// <summary>세모 면을 격자 칸 가운데 높이로(<see cref="Drainage.Analyze"/> ①과 같은 법 · 세모 밖 = NaN) — 원지반을 빗물 격자에 맞출 때.</summary>
    public static float[] Raster(double[] X, double[] Y, double[] Z, int[] F, double x0, double y0, double cell, int W, int H)
    {
        var z = new float[W * H];
        Array.Fill(z, float.NaN);
        for (int t = 0; t + 2 < F.Length; t += 3)
        {
            int a = F[t], b = F[t + 1], c = F[t + 2];
            double ax = X[a], ay = Y[a], bx = X[b], by = Y[b], cx = X[c], cy = Y[c];
            if (!double.IsFinite(ax + ay + bx + by + cx + cy + Z[a] + Z[b] + Z[c])) continue;
            double den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(den) < 1e-12) continue;
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, Math.Min(bx, cx)) - x0) / cell - 0.5)), i1 = Math.Min(W - 1, (int)Math.Ceiling((Math.Max(ax, Math.Max(bx, cx)) - x0) / cell - 0.5));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(ay, Math.Min(by, cy)) - y0) / cell - 0.5)), j1 = Math.Min(H - 1, (int)Math.Ceiling((Math.Max(ay, Math.Max(by, cy)) - y0) / cell - 0.5));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double px = x0 + (i + 0.5) * cell, py = y0 + (j + 0.5) * cell;
                    double l1 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / den;
                    double l2 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / den;
                    double l3 = 1 - l1 - l2;
                    if (!(l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9)) continue;
                    z[j * W + i] = (float)(l1 * Z[a] + l2 * Z[b] + l3 * Z[c]);
                }
        }
        return z;
    }

    /// <summary>칸 C(토지 이용) — 원지반 높이 격자 <paramref name="ground"/>(빗물 격자와 같은 칸)와 견줘 바뀐 자리(|Δ| &gt; 5 cm · 원지반 없음 칸도)는
    /// 경사 ≥ <see cref="UDitch.Settings.CSlopePct"/> = 사면(<see cref="UDitch.Settings.CSlope"/>) · 아니면 포장 · 부지(<see cref="UDitch.Settings.CPave"/>) ·
    /// 안 바뀐 자리 = 원지반(<see cref="UDitch.Settings.CNatural"/>). 원지반이 없으면 null(C 자동 안 함). <paramref name="counts"/> = [포장 · 사면 · 원지반] 칸 수.</summary>
    public static Func<int, double>? CoverC(Drainage.Result dr, float[]? ground, UDitch.Settings s, out int[] counts)
    {
        counts = new int[3];
        int W = dr.W, H = dr.H, N = W * H;
        if (ground == null || ground.Length != N || dr.Z.Length != N) return null;
        var cls = new sbyte[N];
        double cell = dr.Cell, lim = s.CSlopePct / 100.0;
        var Z = dr.Z;
        for (int k = 0; k < N; k++)
        {
            float z = Z[k];
            if (float.IsNaN(z)) { cls[k] = -1; continue; }
            float g = ground[k];
            bool changed = float.IsNaN(g) || Math.Abs(z - g) > 0.05;
            if (!changed) { cls[k] = 2; counts[2]++; continue; }
            int i = k % W, j = k / W;
            double Dz(int a, int b, double h) => float.IsNaN(Z[a]) || float.IsNaN(Z[b]) ? double.NaN : (Z[a] - Z[b]) / h;
            double gx = i > 0 && i < W - 1 ? Dz(k + 1, k - 1, 2 * cell) : double.NaN;
            if (double.IsNaN(gx)) gx = i < W - 1 ? Dz(k + 1, k, cell) : double.NaN;
            if (double.IsNaN(gx)) gx = i > 0 ? Dz(k, k - 1, cell) : 0;
            double gy = j > 0 && j < H - 1 ? Dz(k + W, k - W, 2 * cell) : double.NaN;
            if (double.IsNaN(gy)) gy = j < H - 1 ? Dz(k + W, k, cell) : double.NaN;
            if (double.IsNaN(gy)) gy = j > 0 ? Dz(k, k - W, cell) : 0;
            if (double.IsNaN(gx)) gx = 0;
            if (double.IsNaN(gy)) gy = 0;
            bool steep = Math.Sqrt(gx * gx + gy * gy) >= lim;
            cls[k] = (sbyte)(steep ? 1 : 0); counts[steep ? 1 : 0]++;
        }
        double c0 = s.CPave, c1 = s.CSlope, c2 = s.CNatural;
        return k => cls[k] switch { 0 => c0, 1 => c1, 2 => c2, _ => double.NaN };
    }

    /// <summary>구역 평면 꼴 — 줄마다 이어진 칸 네모의 합 → 단순화(칸 × <paramref name="simplify"/>). 칸이 없으면 null.</summary>
    public static Geometry? ZoneGeometry(Result res, int zone, GeometryFactory gf, double simplify = 0.75)
    {
        var rects = new List<Geometry>();
        int W = res.W, H = res.H; double c = res.Cell;
        for (int j = 0; j < H; j++)
        {
            int run = -1;
            for (int i = 0; i <= W; i++)
            {
                bool on = i < W && res.Owner[j * W + i] == zone;
                if (on && run < 0) run = i;
                if (!on && run >= 0)
                {
                    double x0 = res.X0 + run * c, x1 = res.X0 + i * c, y0 = res.Y0 + j * c, y1 = y0 + c;
                    rects.Add(gf.CreatePolygon(new[] { new Coordinate(x0, y0), new Coordinate(x1, y0), new Coordinate(x1, y1), new Coordinate(x0, y1), new Coordinate(x0, y0) }));
                    run = -1;
                }
            }
        }
        if (rects.Count == 0) return null;
        Geometry g;
        try { g = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(rects); }
        catch { g = gf.BuildGeometry(rects).Buffer(0); }
        if (simplify > 0)
            try { g = NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(g, c * simplify); } catch { }
        return g.IsEmpty ? null : g;
    }

    /// <summary>[3D 보기] 구역 띠 — 굵은 칸(<paramref name="k"/>칸 × <paramref name="k"/>칸 · 가운데 칸의 구역) 줄마다 이어진 칸:
    /// [x0, y0, 칸, n, 아래 가장자리 z × (n + 1), 위 가장자리 z × (n + 1)] 되풀이(장면 좌표 = 도면 − ox, oy · z = 빗물 격자 땅 + <paramref name="lift"/>).</summary>
    public static float[] Strips(Result res, float[] Z, int zone, int k, double ox, double oy, double lift)
    {
        var o = new List<float>();
        int W = res.W, H = res.H; double c = res.Cell;
        if (k < 1) k = 1;
        if (res.Owner.Length != W * H || Z.Length != W * H) return Array.Empty<float>();
        int cw = (W + k - 1) / k, chh = (H + k - 1) / k;
        bool On(int I, int J) { int i = Math.Min(W - 1, I * k + k / 2), j = Math.Min(H - 1, J * k + k / 2); return res.Owner[j * W + i] == zone; }
        double ZAt(double x, double y)
        {
            // 모서리 둘레 네 칸의 평균(땅 없는 칸은 뺌)
            int i = (int)Math.Round((x - res.X0) / c), j = (int)Math.Round((y - res.Y0) / c);
            double s = 0; int n = 0;
            for (int dj = -1; dj <= 0; dj++)
                for (int di = -1; di <= 0; di++)
                {
                    int ii = Math.Clamp(i + di, 0, W - 1), jj = Math.Clamp(j + dj, 0, H - 1);
                    float v = Z[jj * W + ii];
                    if (!float.IsNaN(v)) { s += v; n++; }
                }
            return n > 0 ? s / n : double.NaN;
        }
        var zb = new List<double>(); var zt = new List<double>();
        for (int J = 0; J < chh; J++)
        {
            int run = -1;
            for (int I = 0; I <= cw; I++)
            {
                bool on = I < cw && On(I, J);
                if (on && run < 0) run = I;
                if (!on && run >= 0)
                {
                    int n = I - run;
                    double x0 = res.X0 + run * k * c, y0 = res.Y0 + J * k * c, st = k * c, y1 = Math.Min(res.Y0 + H * c, y0 + st);
                    zb.Clear(); zt.Clear();
                    bool ok = true;
                    for (int q = 0; q <= n && ok; q++)
                    {
                        double x = Math.Min(res.X0 + W * c, x0 + q * st);
                        double a = ZAt(x, y0), b = ZAt(x, y1);
                        if (double.IsNaN(a) || double.IsNaN(b)) ok = false;
                        zb.Add(a); zt.Add(b);
                    }
                    if (ok)
                    {
                        o.Add((float)(x0 - ox)); o.Add((float)(y0 - oy)); o.Add((float)st); o.Add(n);
                        foreach (var v in zb) o.Add((float)(v + lift));
                        foreach (var v in zt) o.Add((float)(v + lift));
                    }
                    run = -1;
                }
            }
        }
        return o.ToArray();
    }

    /// <summary>[3D 보기] 굵은 칸 크기 — 모든 구역 칸이 <paramref name="maxCells"/>를 넘지 않게.</summary>
    public static int DisplayStep(Result res, int maxCells = 60_000)
    {
        long n = res.Zones.Sum(z => (long)z.Cells);
        return n <= maxCells ? 1 : (int)Math.Ceiling(Math.Sqrt((double)n / maxCells));
    }

    static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

    /// <summary>로그 한 줄 — 측구마다 직접 · 전체 넓이 · C · 위 · 아래.</summary>
    public static string ZoneText(Result res, IReadOnlyList<string> ids)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < res.Zones.Length; i++)
        {
            var z = res.Zones[i];
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(i < ids.Count ? ids[i] : "#" + i).Append(' ').Append((z.DirectM2 / 10000.0).ToString("0.####", Ic)).Append(" ha");
            if (z.Up.Count > 0) sb.Append("(전체 ").Append((z.TotalM2 / 10000.0).ToString("0.####", Ic)).Append(')');
            if (res.HasC && z.TotalM2 > 0) sb.Append(" C ").Append((z.TotalCA / z.TotalM2).ToString("0.00", Ic));
            if (z.Down >= 0) sb.Append(" → ").Append(z.Down < ids.Count ? ids[z.Down] : "#" + z.Down);
            if (z.Edge) sb.Append(" ⚠끝");
        }
        return sb.ToString();
    }
}
