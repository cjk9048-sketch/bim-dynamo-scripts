namespace DH.Grading.Core;

/// <summary>★★[JACK 1103 «옹벽 변환을 했는데 라운드부 구석이 깨져(계단 현상)»] 데이라잇 고리의 <b>옹벽 링 지름길</b>을 링으로 되돌린다.
///
/// <para><b>무엇이 깨졌나(실측 1103).</b> 원지반이 소단 높이와 거의 같은 둥근 모서리에서는 가상면 ↔ 원지반 교선이
/// 토막(섬 · 짧은 사슬)으로 끊긴다. 교선 이어닫기는 토막 끝을 <b>곧은 현</b>으로 잇는다 — 두 끝은 같은 계단 링 위(같은 높이)인데
/// 현은 링의 둥근 모서리를 가로질러 링 <b>안쪽</b>으로 처진다(반지름 9.6 m · 현 3.1 m면 12 cm).
/// 수직 옹벽(1:0.01)은 평면폭이 단높이 5 m에 5 cm뿐이라 현이 옹벽 면을 통째로 지나 <b>아랫단 소단 위</b>에 놓이고,
/// Civil은 경계를 평면으로 자르므로 그 사이 옹벽 면 세모를 버린다 → 경계가 아랫단 소단 높이로 떨어진 홈(톱니).</para>
///
/// <para><b>고침.</b> 한 변의 두 끝이 <b>같은 링 위</b>(평면 · 높이 모두 1 mm 안)이고, 두 끝 모두 <b>옹벽 면의 머리나 발</b>
/// (다른 높이의 링이 높이차 × <c>wallGate</c> 안에 있음)이며, 링이 그 사이에서 현을 벗어나 꺾이면 현 대신 <b>링 꼭짓점을 따라간다</b>(짧은 쪽).
/// 링 꼭짓점은 가상면 브레이크라인 꼭짓점이라 면 위에 있다. 옹벽이 없는 도면(사면 · 소단만)은 아무것도 안 바뀐다 —
/// 사면에서는 같은 현이 사면을 몇 cm 깎을 뿐이라 v111.10.0 그대로 둔다.</para>
///
/// <para><b>되돌림은 고친 변마다.</b> 바꾼 링 길이 같은 고리의 다른 변이나 함께 넘긴 다른 고리(같은 방향의 다른 고리 · 반대 방향 고리)와
/// 현보다 <b>더 많이</b> 엇갈리면 그 변만 현으로 되돌린다(원래 제 몸을 지르던 고리여도 셈은 변마다 견준다).</para></summary>
public static class DaylightRings
{
    /// <summary>«같은 링 위»와 «현에서 벗어남»의 판정 거리(m) — 같은 좌표를 다시 읽은 값끼리의 비교이지 증상을 덮는 여유가 아니다.</summary>
    public const double OnTol = 1e-3;

    /// <summary>현을 링으로 바꾸는 상한 — 링 길이가 현의 π/2배를 넘으면(반원보다 더 돌아가면) 지름길이 아니라 다른 자리다 · 그대로 두고 센다.</summary>
    public const double MaxDetour = Math.PI / 2;

    /// <summary>셈 결과(로그 한 줄용).</summary>
    public sealed class Stats
    {
        /// <summary>링으로 바꾼 변 수.</summary>
        public int Fixed;
        /// <summary>두 끝이 같은 링 위 · 옹벽이지만 링이 너무 돌아가 그대로 둔 변 수.</summary>
        public int TooLong;
        /// <summary>바꾸면 엇갈림이 늘어 그 변만 되돌린 수.</summary>
        public int Reverted;
        /// <summary>두 끝이 같은 링 위이고 링이 꺾이지만 옹벽 면이 아니라 그대로 둔 변 수(사면 · 소단).</summary>
        public int NotWall;
        /// <summary>바꾼 변에서 링 꼭짓점이 현에서 벗어난 최대 거리(m).</summary>
        public double MaxSag;
        public void Add(Stats o) { Fixed += o.Fixed; TooLong += o.TooLong; Reverted += o.Reverted; NotWall += o.NotWall; MaxSag = Math.Max(MaxSag, o.MaxSag); }
    }

    /// <summary>고리 <paramref name="loop"/>의 변 중 두 끝이 같은 옹벽 링 위에 있고 링이 그 사이에서 꺾이는 변을 링 꼭짓점으로 바꾼다.</summary>
    /// <param name="wallGate">옹벽 판정 구배(1:n의 n) — 다른 높이의 링이 평면 높이차 × 이 값 안에 있으면 옹벽 면이다(<c>GradingParams.WallGateSlope</c>).</param>
    /// <param name="others">엇갈림을 함께 셀 다른 고리들(없으면 null).</param>
    public static List<Point3> FollowRings(IReadOnlyList<Point3> loop, IReadOnlyList<IReadOnlyList<Point3>> rings, double wallGate,
                                           IReadOnlyList<IReadOnlyList<Point3>>? others, out Stats st)
    {
        st = new Stats();
        var src = new List<Point3>(loop);
        if (loop.Count < 3 || rings == null || rings.Count == 0 || wallGate <= 0) return src;
        int n = loop.Count;
        bool closed = Same2D(loop[0], loop[n - 1]);
        int m = closed ? n - 1 : n;
        if (m < 3) return src;
        int edges = closed ? m : m - 1;

        var info = new List<RingInfo>();
        foreach (var r in rings)
            if (r != null && r.Count >= 2) info.Add(new RingInfo(r));

        // 변마다 바꿀 길(끝 제외 링 꼭짓점) — null이면 현 그대로
        var repl = new List<Point3>?[edges];
        for (int i = 0; i < edges; i++)
        {
            var a = loop[i]; var b = loop[(i + 1) % m];
            foreach (var ri in info)
            {
                if (!ri.Near(a) || !ri.Near(b)) continue;
                if (!ri.On(a, out double ta, out _) || !ri.On(b, out double tb, out _)) continue;
                var path = ri.Between(ta, tb, out double pathLen);
                if (path.Count == 0) break;                       // 이웃 꼭짓점 사이 — 현이 곧 링
                double dev = 0;
                foreach (var v in path) dev = Math.Max(dev, SegDist(v, a, b));
                if (dev <= OnTol) break;                          // 링이 현과 나란하다(곧은 변)
                if (!OnWall(a, ri, info, wallGate) || !OnWall(b, ri, info, wallGate)) { st.NotWall++; break; }
                if (pathLen > Dist2D(a, b) * MaxDetour + OnTol) { st.TooLong++; break; }
                repl[i] = path;
                if (dev > st.MaxSag) st.MaxSag = dev;
                break;
            }
        }

        // 엇갈림 견주기 — 고친 변마다: 이 고리의 다른 변(앞서 받아들인 변은 링 길 · 나머지는 현) · 다른 고리와
        //   현의 엇갈림 수 vs 링 길의 엇갈림 수 — 늘면 그 변만 현으로 되돌린다
        var accepted = new bool[edges];
        List<(Point3 A, Point3 B)> SegsOf(int i, bool walk)
        {
            var s = new List<(Point3, Point3)>();
            var a = loop[i]; var b = loop[(i + 1) % m];
            if (!walk || repl[i] == null) { s.Add((a, b)); return s; }
            var prev = a;
            foreach (var v in repl[i]!) { s.Add((prev, v)); prev = v; }
            s.Add((prev, b));
            return s;
        }
        var otherSegs = new List<(Point3 A, Point3 B)>();
        if (others != null)
            foreach (var o in others)
                if (o != null)
                    for (int k = 0; k + 1 < o.Count; k++) otherSegs.Add((o[k], o[k + 1]));
        double maxSag = 0;
        for (int i = 0; i < edges; i++)
        {
            if (repl[i] == null) continue;
            var chord = SegsOf(i, false);
            var walk = SegsOf(i, true);
            int before = 0, after = 0;
            for (int j = 0; j < edges; j++)
            {
                if (j == i) continue;
                foreach (var s in SegsOf(j, accepted[j])) { before += Crossings(chord, s); after += Crossings(walk, s); }
            }
            foreach (var s in otherSegs) { before += Crossings(chord, s); after += Crossings(walk, s); }
            if (after > before) { repl[i] = null; st.Reverted++; continue; }
            accepted[i] = true;
            st.Fixed++;
            foreach (var v in repl[i]!) maxSag = Math.Max(maxSag, SegDist(v, loop[i], loop[(i + 1) % m]));
        }
        st.MaxSag = maxSag;
        if (st.Fixed == 0) return src;

        var outPts = new List<Point3>(n + 16);
        for (int i = 0; i < m; i++)
        {
            outPts.Add(loop[i]);
            if (i < edges && repl[i] != null) outPts.AddRange(repl[i]!);
        }
        if (closed) outPts.Add(outPts[0]);
        return outPts;
    }

    /// <summary>링 위 점 <paramref name="p"/>가 옹벽 면의 머리나 발인가 — 다른 높이의 링이 평면 (높이차 × <paramref name="gate"/>) 안에 있다.</summary>
    static bool OnWall(Point3 p, RingInfo own, List<RingInfo> all, double gate)
    {
        foreach (var r in all)
        {
            if (ReferenceEquals(r, own) || !r.NearPlan(p, r.ZSpan(p.Z) * gate + OnTol)) continue;
            double d = r.PlanDist(p, out double z);
            double dz = Math.Abs(z - p.Z);
            if (dz > OnTol && d <= dz * gate + OnTol) return true;
        }
        return false;
    }

    /// <summary>두 선분 묶음의 맞엇갈림 수(끝이 닿거나 한 줄로 겹치는 것은 안 셈 — 앞뒤를 같은 규칙으로 센다).</summary>
    static int Crossings(List<(Point3 A, Point3 B)> path, (Point3 A, Point3 B) s)
    {
        int c = 0;
        foreach (var p in path) if (Cross(p.A, p.B, s.A, s.B)) c++;
        return c;
    }

    static bool Cross(Point3 a, Point3 b, Point3 c, Point3 d)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X)
         || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;
        double o1 = Orient(a, b, c), o2 = Orient(a, b, d), o3 = Orient(c, d, a), o4 = Orient(c, d, b);
        return o1 * o2 < 0 && o3 * o4 < 0;
    }

    static double Orient(Point3 a, Point3 b, Point3 c)
    {
        double v = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return Math.Abs(v) < 1e-12 ? 0 : v;
    }

    sealed class RingInfo
    {
        readonly IReadOnlyList<Point3> _r;
        readonly double[] _cum;
        readonly int _m;
        readonly double _x0, _y0, _x1, _y1, _z0, _z1;

        public RingInfo(IReadOnlyList<Point3> r)
        {
            _r = r;
            _cum = GradingGeometry.CumLen2D(r);
            _m = _cum.Length - 1;
            _x0 = _y0 = _z0 = double.MaxValue; _x1 = _y1 = _z1 = double.MinValue;
            for (int k = 0; k < _m; k++)
            {
                var p = r[k];
                _x0 = Math.Min(_x0, p.X); _x1 = Math.Max(_x1, p.X);
                _y0 = Math.Min(_y0, p.Y); _y1 = Math.Max(_y1, p.Y);
                _z0 = Math.Min(_z0, p.Z); _z1 = Math.Max(_z1, p.Z);
            }
        }

        public bool Near(Point3 p) =>
            p.X >= _x0 - OnTol && p.X <= _x1 + OnTol && p.Y >= _y0 - OnTol && p.Y <= _y1 + OnTol
            && p.Z >= _z0 - OnTol && p.Z <= _z1 + OnTol;

        /// <summary>평면 상자가 점에서 <paramref name="d"/> 안인가.</summary>
        public bool NearPlan(Point3 p, double d) => p.X >= _x0 - d && p.X <= _x1 + d && p.Y >= _y0 - d && p.Y <= _y1 + d;

        /// <summary>이 링 높이와 <paramref name="z"/>의 가장 큰 차(상자 거르기용).</summary>
        public double ZSpan(double z) => Math.Max(Math.Abs(z - _z0), Math.Abs(z - _z1));

        /// <summary>가장 가까운 링 위 점까지 평면 거리 · 그 점의 높이.</summary>
        public double PlanDist(Point3 p, out double z)
        {
            On(p, out _, out double d2, out z);
            return Math.Sqrt(d2);
        }

        public bool On(Point3 p, out double t, out double d2) { bool ok = On(p, out t, out d2, out _); return ok; }

        /// <summary>평면 거리 · 높이 모두 <see cref="OnTol"/> 안에서 링 위인가 — 링 위 호길이 <paramref name="t"/>.</summary>
        bool On(Point3 p, out double t, out double best, out double bz)
        {
            t = 0; best = double.MaxValue; bz = 0;
            for (int k = 0; k < _m; k++)
            {
                var a = _r[k]; var b = _r[(k + 1) % _m];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-18 ? 0 : ((p.X - a.X) * ex + (p.Y - a.Y) * ey) / l2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double dx = a.X + ex * u - p.X, dy = a.Y + ey * u - p.Y, d2 = dx * dx + dy * dy;
                if (d2 < best) { best = d2; t = _cum[k] + Math.Sqrt(l2) * u; bz = a.Z + (b.Z - a.Z) * u; }
            }
            return best <= OnTol * OnTol && Math.Abs(p.Z - bz) <= OnTol;
        }

        /// <summary>호길이 <paramref name="ta"/> → <paramref name="tb"/>의 짧은 쪽 사이에 있는 링 꼭짓점(끝 제외 · 가는 차례).</summary>
        public List<Point3> Between(double ta, double tb, out double len)
        {
            double tot = _cum[_m];
            var res = new List<(double Rel, Point3 P)>();
            len = 0;
            if (tot < 1e-12) return new List<Point3>();
            double fw = ((tb - ta) % tot + tot) % tot;
            bool forward = fw <= tot - fw;
            len = forward ? fw : tot - fw;
            for (int k = 0; k < _m; k++)
            {
                double rel = forward ? ((_cum[k] - ta) % tot + tot) % tot : ((ta - _cum[k]) % tot + tot) % tot;
                if (rel > OnTol && rel < len - OnTol) res.Add((rel, _r[k]));
            }
            res.Sort((x, y) => x.Rel.CompareTo(y.Rel));
            return res.Select(q => q.P).ToList();
        }
    }

    static bool Same2D(Point3 a, Point3 b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
    static double Dist2D(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    static double SegDist(Point3 p, Point3 a, Point3 b)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
        double u = l2 < 1e-18 ? 0 : ((p.X - a.X) * ex + (p.Y - a.Y) * ey) / l2;
        u = u < 0 ? 0 : (u > 1 ? 1 : u);
        double dx = a.X + ex * u - p.X, dy = a.Y + ey * u - p.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
