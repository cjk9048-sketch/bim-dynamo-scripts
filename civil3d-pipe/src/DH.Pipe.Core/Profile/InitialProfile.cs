using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;

namespace DH.Pipe.Core.Profile;

/// <summary>
/// 처음 종단 — «토피 띠 안 최소 꺾임».
/// JACK 1127: «지표면에서 오프셋해서 만들되 울퉁불퉁하면 PVI 가 너무 많으니 그렇게 생성되지 않게».
/// 윗 한계 = 최소 토피를 지키는 관저고 · 아랫 한계 = 윗 한계 − 여유. 그 사이를 곧게 갈 수 있는 데까지 가서 PVI 를 놓는다.
/// </summary>
public static class InitialProfile
{
    /// <summary>추진 수직구 안 수직 관의 가로 폭(m).</summary>
    public const double RiserGap = 0.05;
    /// <summary>역사이펀 앞뒤 경사 한계(45° = 1.0).</summary>
    public const double MaxRampGrade = 1.0;

    /// <summary>띠의 한 점.</summary>
    public sealed class Sample
    {
        public double S;
        public double Ground;
        /// <summary>관저고 윗 한계 · 아랫 한계.</summary>
        public double Up, Lo;
        /// <summary>수평 구간(하천 · 추진) · 지장물이 눌러 놓은 점.</summary>
        public bool Fixed;
    }

    public sealed class Result
    {
        public ProfileLine Line { get; set; } = new();
        public List<Sample> Band { get; set; } = new();
        public List<string> Notes { get; } = new();
        public double AvgCover { get; set; }
        public double MinCover { get; set; }
        public double MaxCover { get; set; }
    }

    /// <summary>
    /// 띠를 만든다. ground = 측점 → 지반고. avoid = 지장물을 피해 갈지(가이드안).
    /// </summary>
    public static List<Sample> Band(Pl ground, RouteData route, bool avoid, double step = 2.0)
    {
        var std = route.Std;
        double s0 = ground.X0, s1 = ground.X1;
        var cuts = new SortedSet<double> { s0, s1 };
        foreach (var p in ground.Points()) cuts.Add(p.X);
        foreach (var r in route.Runs) { Add(cuts, r.StaFrom, s0, s1); Add(cuts, r.StaTo, s0, s1); }
        foreach (var m in route.Methods)
        {
            Add(cuts, m.StaFrom, s0, s1);
            Add(cuts, m.StaTo, s0, s1);
            if (m.Kind == MethodKind.Jacking)
            {
                // 추진: 관은 수직구 안에서 수직으로 내려간다 → 구간 바로 밖에 점을 하나 더 두어 그 사이(5 cm)에서 꺾이게
                Add(cuts, m.StaFrom - RiserGap, s0, s1);
                Add(cuts, m.StaTo + RiserGap, s0, s1);
            }
        }
        if (avoid)
        {
            // 지장물의 가운데뿐 아니라 걸리는 범위의 양 끝에도 점을 둔다(가운데 한 점만 누르면 그 옆에서 이격이 깨진다 — 검토 보통)
            foreach (var o in route.Obstacles)
            {
                double h = Footprint(o, route.PipeAt(o.Sta), std);
                Add(cuts, o.Sta, s0, s1);
                Add(cuts, o.Sta - h, s0, s1);
                Add(cuts, o.Sta + h, s0, s1);
            }
        }
        // 사이가 넓으면 채운다(PVI 를 놓을 수 있는 자리)
        var xs = cuts.ToList();
        var all = new List<double>();
        for (int i = 0; i < xs.Count; i++)
        {
            all.Add(xs[i]);
            if (i + 1 >= xs.Count) break;
            double gap = xs[i + 1] - xs[i];
            int n = (int)Math.Floor(gap / step);
            for (int k = 1; k <= n; k++)
            {
                double x = xs[i] + gap * k / (n + 1);
                all.Add(x);
            }
        }
        // 추진 구간 끝의 수직 관 폭(5 cm) 안에는 점을 두지 않는다 — 지반 점이 그 안에 있으면 수직 관이 몇 mm 로 좁아진다
        foreach (var m in route.Methods)
        {
            if (m.Kind != MethodKind.Jacking) continue;
            all.RemoveAll(x => (x > m.StaFrom - RiserGap + 1e-9 && x < m.StaFrom - 1e-9) || (x > m.StaTo + 1e-9 && x < m.StaTo + RiserGap - 1e-9));
        }
        double band = Math.Max(0.05, std.SmoothBand);
        var res = new List<Sample>(all.Count);
        foreach (double s in all)
        {
            var pipe = route.PipeAt(s);
            double g = ground.At(s);
            double up = PipeGeom.InvertForCover(pipe, g, std.MinCover(pipe));
            res.Add(new Sample { S = s, Ground = g, Up = up, Lo = up - band });
        }

        // 수평 구간(하천 횡단 · 추진): 구간에서 가장 낮은 땅 아래 토피로 고정
        foreach (var m in route.Methods)
        {
            if (!m.IsLevel) continue;
            double a = Math.Max(m.StaFrom, s0), b = Math.Min(m.StaTo, s1);
            if (b - a < 1e-6) continue;
            double gMin = double.MaxValue;
            foreach (var q in res) if (q.S >= a - 1e-9 && q.S <= b + 1e-9) gMin = Math.Min(gMin, q.Ground);
            if (gMin == double.MaxValue) continue;
            double level = PipeGeom.LevelInvert(m, route.PipeAt(0.5 * (a + b)), std, gMin);
            foreach (var q in res)
            {
                if (q.S < a - 1e-9 || q.S > b + 1e-9) continue;
                q.Up = level; q.Lo = level; q.Fixed = true;
            }
            if (m.Kind != MethodKind.RiverOpen) continue;
            // 하천 횡단(역사이펀) 앞뒤 경사로: 수평 높이에서 «1:2(기본) 보다 급하고 45° 보다 완만한» 쐐기 안으로만 올라오게 한다.
            //   윗 한계 = 수평 높이 + 1.0 × 거리(45° — KDS 57 50 00 4.1.20 (2)) · 아랫 한계 = 수평 높이 + 경사로 × 거리.
            //   쐐기가 보통 띠와 만나는 곳부터는 보통 띠 — 끊김 없이 이어진다.
            double ramp = Math.Clamp(std.RiverRampGrade, 0.05, MaxRampGrade);
            foreach (var q in res)
            {
                if (q.Fixed || (q.S >= a && q.S <= b)) continue;
                double dist = q.S < a ? a - q.S : q.S - b;
                q.Up = Math.Min(q.Up, level + MaxRampGrade * dist);
                q.Lo = Math.Min(q.Lo, level + ramp * dist);
                if (q.Lo > q.Up) q.Lo = q.Up;
            }
        }

        // 지장물: 위로 넘어도 최소 토피가 되면 위, 아니면 아래로
        if (avoid)
        {
            double c = std.ClearanceToOthers;
            foreach (var o in route.Obstacles)
            {
                if (o.Sta < s0 || o.Sta > s1) continue;
                var pipe = route.PipeAt(o.Sta);
                double g = ground.At(o.Sta);
                double top = o.TopElev ?? g - o.Depth, bot = top - o.Height;
                double half = Footprint(o, pipe, std);                  // 노선 방향으로 이만큼은 같은 높이 제약
                double needUnder = bot - c - pipe.Od + pipe.Wall;       // 아래로 지날 때 관저고 윗 한계
                double needOver = top + c + pipe.Wall;                  // 위로 넘을 때 관저고 아랫 한계
                // 위/아래는 지장물 하나에 한 번만 정한다: 걸리는 범위 어디서나 위로 넘을 수 있어야 «위»
                // (점마다 따로 정하면 땅이 기운 곳에서 한 점은 위 · 옆 점은 아래가 되어 관이 지장물을 뚫는다 — 검토 보통)
                var hit = res.Where(q => !q.Fixed && q.S >= o.Sta - half - 1e-9 && q.S <= o.Sta + half + 1e-9).ToList();
                if (hit.Count == 0) continue;
                bool over = needOver <= hit.Min(q => q.Up) + 1e-9;
                foreach (var q in hit)
                {
                    if (over) q.Lo = Math.Max(q.Lo, needOver);          // 위로 — 토피는 그대로 지킴
                    else
                    {
                        q.Up = Math.Min(q.Up, needUnder);
                        q.Lo = Math.Min(q.Lo, q.Up - Math.Min(band, 0.3));
                    }
                }
            }
        }

        if (route.StartInvert.HasValue) { res[0].Up = res[0].Lo = route.StartInvert.Value; res[0].Fixed = true; }
        if (route.EndInvert.HasValue) { res[^1].Up = res[^1].Lo = route.EndInvert.Value; res[^1].Fixed = true; }
        return res;
    }

    /// <summary>지장물이 노선 방향으로 걸리는 반폭 = 지장물 반폭 + 관 반지름 + 이격.</summary>
    public static double Footprint(Obstacle o, PipeSpec pipe, Standards std) => o.Width * 0.5 + pipe.Od * 0.5 + std.ClearanceToOthers;

    private static void Add(SortedSet<double> set, double s, double s0, double s1)
    {
        if (s > s0 + 1e-6 && s < s1 - 1e-6) set.Add(s);
    }

    /// <summary>처음 종단을 만든다.</summary>
    public static Result Make(Pl ground, RouteData route, bool avoid = false)
    {
        var band = Band(ground, route, avoid);
        var res = new Result { Band = band };
        var v = Solve(band, res.Notes);
        v = Tidy(v, band, route.Std.MinPviSpacing);
        // 측점은 mm 까지(cm 로 맞추면 수직구 안 수직 관 5 cm 가 밀려 구간 끝 관저고가 달라진다 — 검토 낮음)
        // 반올림한 측점이 노선 끝을 넘으면 안 된다(끝 629.2437 → 629.244 는 선형 밖이라 Civil 이 PVI 를 거절 — 1127 화면 없는 Civil 실측)
        res.Line = new ProfileLine(v.Select(p => (Math.Clamp(Math.Round(p.S, 3), ground.X0, ground.X1), Math.Floor(p.Z * 1000.0 + 1e-6) / 1000.0)));
        Stats(res, ground, route);
        return res;
    }

    /// <summary>띠 안 최소 꺾임(앞보기 1단).</summary>
    public static List<(double S, double Z)> Solve(List<Sample> b, List<string>? notes = null)
    {
        int n = b.Count;
        var v = new List<(double S, double Z)>();
        if (n < 2) return v;
        // 띠가 뒤집힌 점(아랫 한계 > 윗 한계)은 윗 한계에 맞춘다 — 토피가 먼저(검토 보통: 그대로 두면 예외)
        foreach (var q in b)
        {
            if (q.Lo <= q.Up) continue;
            q.Lo = q.Up;
            notes?.Add($"띠 뒤집힘 {q.S:0.0}");
        }

        // a 에서 za 로 떠나 곧게 갈 수 있는 가장 먼 점과 그때의 기울기 범위
        (int Last, double SLo, double SHi) Reach(int a, double za)
        {
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            int last = a;
            for (int j = a + 1; j < n; j++)
            {
                double d = b[j].S - b[a].S;
                if (d < 1e-9) continue;
                double nl = Math.Max(lo, (b[j].Lo - za) / d), nh = Math.Min(hi, (b[j].Up - za) / d);
                if (nl > nh + 1e-12) break;
                lo = nl; hi = nh; last = j;
            }
            return (last, lo, hi);
        }

        // 시작 높이: 고정이면 그것, 아니면 얕은 · 가운데 · 깊은 쪽 가운데 가장 멀리 가는 것(같으면 얕은 쪽)
        double z0 = b[0].Up;
        if (b[0].Up - b[0].Lo > 1e-9)
        {
            int best = -1;
            foreach (double zc in new[] { b[0].Up, 0.5 * (b[0].Up + b[0].Lo), b[0].Lo })
            {
                int reach = Reach(0, zc).Last;
                if (reach > best) { best = reach; z0 = zc; }
            }
        }
        v.Add((b[0].S, z0));
        int cur = 0;
        double zc0 = z0;
        int guard = 0;
        while (cur < n - 1 && guard++ < n * 2)
        {
            var (last, slo, shi) = Reach(cur, zc0);
            if (last == cur)
            {
                // 다음 점조차 못 감(띠가 계단처럼 끊김 — 수평 구간 끝 · 지장물) → 다음 점의 띠 안으로 바로 꺾는다
                int nx = cur + 1;
                while (nx < n - 1 && b[nx].S - b[cur].S < 1e-9) nx++;
                double zn = Math.Clamp(zc0, b[nx].Lo, b[nx].Up);
                v.Add((b[nx].S, zn));
                if (!b[nx].Fixed && !b[cur].Fixed) notes?.Add($"띠 끊김 {b[nx].S:0.0}");   // 수평 구간 끝(수직구)은 당연한 꺾임
                cur = nx; zc0 = zn;
                continue;
            }
            double d = b[last].S - b[cur].S;
            if (last == n - 1)
            {
                v.Add((b[last].S, zc0 + shi * d));               // 끝점 — 얕은 쪽
                break;
            }
            double zBest = zc0 + shi * d;
            int reachBest = -1;
            foreach (double sl in new[] { shi, 0.5 * (slo + shi), slo })
            {
                double zc = zc0 + sl * d;
                int reach = Reach(last, zc).Last;
                if (reach > reachBest) { reachBest = reach; zBest = zc; }
            }
            v.Add((b[last].S, zBest));
            cur = last; zc0 = zBest;
        }
        return v;
    }

    /// <summary>너무 가까운 PVI 는 띠를 벗어나지 않으면 뺀다 · 거의 곧은 PVI 도 뺀다.</summary>
    public static List<(double S, double Z)> Tidy(List<(double S, double Z)> v, List<Sample> band, double minSpacing)
    {
        var r = new List<(double S, double Z)>(v);
        bool changed = true;
        while (changed && r.Count > 2)
        {
            changed = false;
            for (int i = 1; i < r.Count - 1; i++)
            {
                double g1 = (r[i].Z - r[i - 1].Z) / Math.Max(r[i].S - r[i - 1].S, 1e-9);
                double g2 = (r[i + 1].Z - r[i].Z) / Math.Max(r[i + 1].S - r[i].S, 1e-9);
                bool close = r[i].S - r[i - 1].S < minSpacing || r[i + 1].S - r[i].S < minSpacing;
                bool straight = Math.Abs(g2 - g1) < 5e-4;            // 0.05 %
                if (!close && !straight) continue;
                if (!Inside(r[i - 1], r[i + 1], band)) continue;
                r.RemoveAt(i);
                changed = true;
                break;
            }
        }
        return r;
    }

    private static bool Inside((double S, double Z) a, (double S, double Z) c, List<Sample> band)
    {
        double d = c.S - a.S;
        if (d < 1e-9) return false;
        foreach (var q in band)
        {
            if (q.S <= a.S + 1e-9 || q.S >= c.S - 1e-9) continue;
            double z = a.Z + (c.Z - a.Z) * ((q.S - a.S) / d);
            if (z > q.Up + 1e-6 || z < q.Lo - 1e-6) return false;
        }
        return true;
    }

    public static void Stats(Result res, Pl ground, RouteData route)
    {
        double sum = 0, len = 0, min = double.MaxValue, max = double.MinValue;
        var line = res.Line;
        if (line.IsEmpty) return;
        var xs = new SortedSet<double>();
        foreach (var p in ground.Points()) if (p.X >= line.S0 && p.X <= line.S1) xs.Add(p.X);
        foreach (var p in line.Pvi) xs.Add(p.S);
        var list = xs.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            double s = list[i];
            double c = PipeGeom.Cover(route.PipeAt(s), line.At(s), ground.At(s));
            if (route.MethodAt(s).Kind != MethodKind.Jacking)
            {
                min = Math.Min(min, c);
                max = Math.Max(max, c);
            }
            if (i + 1 < list.Count)
            {
                double s2 = list[i + 1];
                double c2 = PipeGeom.Cover(route.PipeAt(s2), line.At(s2), ground.At(s2));
                sum += 0.5 * (c + c2) * (s2 - s);
                len += s2 - s;
            }
        }
        res.AvgCover = len > 0 ? sum / len : 0;
        res.MinCover = min == double.MaxValue ? 0 : min;
        res.MaxCover = max == double.MinValue ? 0 : max;
    }
}
