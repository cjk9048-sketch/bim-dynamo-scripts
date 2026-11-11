using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v117.4 · JACK 1111 «불러온 구조물 부피를 되메우기 · 성토에서 빼기» 인터뷰] 횡단 한 장에서 <b>구조물 단면이 차지한 넓이</b>를 수량 칸마다 잰다.
/// <para>구조물 단면 = 그림이 쓰는 그 자르기(<see cref="StructSlice.Cut"/> · 부재 합침 · 구멍 포함). 수량 선(원지반 G · 계획면 P · 터파기 바닥 E · 터파기 위끝 T)은
/// 수량이 쓰는 <b>같은 배열 · 같은 축</b>이라 «선 사이 넓이»(<see cref="CrossSectionArea.Above"/>)와 한 자로 잰다 — 구조물이 칸을 꽉 채우면 공제 = 그 칸 수량(시험 S250).</para>
/// <list type="bullet">
/// <item>되메우기 공제 = |S ∩ {E ≤ z ≤ T}| (+ 따로 센 옹벽 몫 띠 |S ∩ {옹벽 바닥 ≤ z ≤ 옹벽 위끝}|).</item>
/// <item>성토 공제 = |S ∩ {G ≤ z ≤ P}| − |S ∩ {max(G, E) ≤ z ≤ min(P, T)}| — 성토 칸 중 터파기 칸과 겹친 몫은 되메우기에서만 뺀다(한 부피를 두 번 안 뺌).
/// 원지반 기준은 T = min(G, P) ≤ G라 겹침이 없다(원지반 아래 = 되메우기 · 원지반 ~ 계획면 = 성토). 계획면 기준은 T = P라 터파기 안 구조물은 되메우기에서만
/// (성토는 먼저 다 쌓고 다시 판 실제 물량 — 표 밑 «겹칩니다» 안내 그대로).</item>
/// <item>계획면 위로 솟은 몫 · 터파기 밖 원지반 속 몫은 안 뺀다. 값이 없는(NaN) 칸은 넓이 셈처럼 건너뛴다.</item>
/// <item>★[계획 검토 HIGH 1 · MEDIUM 1] 공제는 바깥 껍질(<see cref="Shells"/> — 배수지 물칸 · 박스 안 빈 곳엔 흙이 없다) · 그린 옹벽 단면과 겹친 자리는 먼저 지움(<see cref="Minus"/> — 옹벽 공제가 이미 뺌).
/// 위가 열린 구조물(U형 수로) 안 빈 곳은 못 잡는다 — 표 밑 «미포함»에 남김.</item>
/// </list>
/// <para>셈: 칸 [x_i, x_i+1]마다 선은 곧으므로 영역 = 반평면 넷 이상의 교집합(볼록) → 고리마다 그 볼록 영역으로 자르고(서덜랜드–호지먼 — 오목 고리도 넓이는 정확) 신발끈 · 껍질 − 구멍.</para></summary>
public static class StructQty
{
    /// <summary>다각형들 ∩ {모든 아래선 ≤ z ≤ 모든 위선} 넓이(㎡). 선은 <paramref name="x"/> 위 값(오름차순) · 칸의 네 끝 중 NaN이 있으면 그 칸은 건너뜀.</summary>
    public static double AreaIn(IReadOnlyList<Polygon> polys, double[] x, IReadOnlyList<double[]> lowers, IReadOnlyList<double[]> uppers)
    {
        if (polys == null || polys.Count == 0 || x == null || x.Length < 2) return 0;
        int n = x.Length;
        foreach (var a in lowers) n = Math.Min(n, a.Length);
        foreach (var a in uppers) n = Math.Min(n, a.Length);
        double sum = 0;
        var buf = new List<(double X, double Y)>(); var tmp = new List<(double X, double Y)>();
        foreach (var poly in polys)
        {
            var env = poly.EnvelopeInternal;
            for (int i = 0; i + 1 < n; i++)
            {
                double x0 = x[i], x1 = x[i + 1];
                if (!(x1 > x0) || x1 <= env.MinX || x0 >= env.MaxX) continue;
                bool nan = false;
                foreach (var a in lowers) if (double.IsNaN(a[i]) || double.IsNaN(a[i + 1])) nan = true;
                foreach (var a in uppers) if (double.IsNaN(a[i]) || double.IsNaN(a[i + 1])) nan = true;
                if (nan) continue;
                double cell = Clip(poly.Shell.Coordinates, x0, x1, i, lowers, uppers, buf, tmp);
                foreach (var h in poly.Holes) cell -= Clip(h.Coordinates, x0, x1, i, lowers, uppers, buf, tmp);
                if (cell > 0) sum += cell;
            }
        }
        return sum;
    }

    /// <summary>(성토 공제, 되메우기 공제, 터파기 바닥 아래 구조물) ㎡. 배열은 모두 <paramref name="x"/> 위(없으면 null). <paramref name="wBot"/> · <paramref name="wTop"/> = 따로 센 옹벽 몫 띠(없으면 null).
    /// <para>Below = |S ∩ {z ≤ E}| — 크면 터파기 면이 구조물을 못 감쌌다는 뜻(알림만 · 공제 안 함).</para></summary>
    public static (double Fill, double Back, double Below) Deduct(IReadOnlyList<Polygon> s, double[] x, double[] G, double[] P, double[] E, double[] T, double[]? wBot = null, double[]? wTop = null)
    {
        if (s == null || s.Count == 0 || x == null) return (0, 0, 0);
        double back = 0, fill = 0, below = 0, wall = 0;
        if (E != null && T != null) back = AreaIn(s, x, new[] { E }, new[] { T });
        if (wBot != null && wTop != null) back += wall = AreaIn(s, x, new[] { wBot }, new[] { wTop });
        if (G != null && P != null)
        {
            fill = AreaIn(s, x, new[] { G }, new[] { P });
            if (E != null && T != null) fill -= AreaIn(s, x, new[] { G, E }, new[] { P, T });
            if (fill < 0) fill = 0;
        }
        if (E != null) below = Math.Max(0, AreaIn(s, x, Array.Empty<double[]>(), new[] { E }) - wall);   // 옹벽 몫 띠(E 아래)는 공제에 든 자리
        return (fill, back, below);
    }

    /// <summary>★[v117.4 · 계획 검토 HIGH 1] 바깥 껍질만(구멍 메움) — 배수지 물칸 · 박스 안 빈 곳엔 흙이 없으니 껍질 넓이를 통째로 뺀다. <paramref name="holes"/> = 메운 구멍 넓이.</summary>
    public static List<Polygon> Shells(IEnumerable<Polygon> ps, out double holes)
    {
        holes = 0;
        var gf = new GeometryFactory(); var r = new List<Polygon>();
        foreach (var p in ps)
        {
            if (p.Holes.Length == 0) { r.Add(p); continue; }
            foreach (var h in p.Holes) holes += Math.Abs(NetTopologySuite.Algorithm.Area.OfRingSigned(h.Coordinates));
            r.Add(gf.CreatePolygon(p.Shell.Coordinates));
        }
        return r;
    }

    /// <summary>★[v117.4 · 계획 검토 MEDIUM 1] 횡단 2D 옹벽 단면의 구조물 고리(축 a · 세계 z) — 옹벽 공제가 이미 뺀 자리를 구조물 공제에서 먼저 지우려고.</summary>
    public static List<Polygon> WallPolys(WallExcav.Xs2D? x2)
    {
        var r = new List<Polygon>();
        if (x2 == null) return r;
        var gf = new GeometryFactory();
        foreach (var p in x2.Profs)
        {
            var c = p.C;
            foreach (var part in c.Shape.Parts)
            {
                if (!part.Structure || part.Ring.Count < 3) continue;
                var cs = new Coordinate[part.Ring.Count + 1];
                for (int i = 0; i < part.Ring.Count; i++) cs[i] = new Coordinate(p.A0 + part.Ring[i].X * c.Stretch * c.Sign, c.Toe + part.Ring[i].Z);
                cs[^1] = cs[0].Copy();
                Geometry g = gf.CreatePolygon(cs);
                if (!g.IsValid) g = g.Buffer(0);
                for (int k = 0; k < g.NumGeometries; k++) if (g.GetGeometryN(k) is Polygon pg && !pg.IsEmpty) r.Add(pg);
            }
        }
        return r;
    }

    /// <summary>★[계획 검토 MEDIUM 2] 여러 구조물 단면을 합침(겹친 자리 한 번만).</summary>
    public static List<Polygon> Union(IReadOnlyList<Polygon> ps)
    {
        if (ps.Count < 2) return ps.ToList();
        var u = new GeometryFactory().BuildGeometry(ps.Cast<Geometry>().ToList()).Union();
        var r = new List<Polygon>();
        for (int k = 0; k < u.NumGeometries; k++) if (u.GetGeometryN(k) is Polygon pg && !pg.IsEmpty) r.Add(pg);
        return r;
    }

    /// <summary>a − b(다각형들) · <paramref name="overlap"/> = 겹쳐 지운 넓이. b가 비면 a 그대로.</summary>
    public static List<Polygon> Minus(IReadOnlyList<Polygon> a, IReadOnlyList<Polygon> b, out double overlap)
    {
        overlap = 0;
        if (a.Count == 0 || b.Count == 0) return a.ToList();
        var gf = new GeometryFactory();
        var A = gf.BuildGeometry(a.Cast<Geometry>().ToList()).Union();
        var B = gf.BuildGeometry(b.Cast<Geometry>().ToList()).Union();
        if (!A.EnvelopeInternal.Intersects(B.EnvelopeInternal)) return a.ToList();
        var D = A.Difference(B);
        overlap = Math.Max(0, A.Area - D.Area);
        var r = new List<Polygon>();
        for (int k = 0; k < D.NumGeometries; k++) if (D.GetGeometryN(k) is Polygon pg && !pg.IsEmpty) r.Add(pg);
        return r;
    }

    /// <summary>닫힌 고리(첫 = 끝) ∩ 칸 i의 볼록 영역 넓이(부호 없음).</summary>
    static double Clip(Coordinate[] ring, double x0, double x1, int i, IReadOnlyList<double[]> lowers, IReadOnlyList<double[]> uppers,
                       List<(double X, double Y)> buf, List<(double X, double Y)> tmp)
    {
        buf.Clear();
        for (int k = 0; k + 1 < ring.Length; k++) buf.Add((ring[k].X, ring[k].Y));
        // 반평면 a·X + b·Y + c ≥ 0
        void Cut(double a, double b, double c)
        {
            if (buf.Count == 0) return;
            tmp.Clear();
            int m = buf.Count;
            for (int k = 0; k < m; k++)
            {
                var P = buf[k]; var Q = buf[(k + 1) % m];
                double dp = a * P.X + b * P.Y + c, dq = a * Q.X + b * Q.Y + c;
                if (dp >= 0) tmp.Add(P);
                if ((dp >= 0) != (dq >= 0))
                {
                    double t = dp / (dp - dq);
                    tmp.Add((P.X + (Q.X - P.X) * t, P.Y + (Q.Y - P.Y) * t));
                }
            }
            buf.Clear(); buf.AddRange(tmp);
        }
        Cut(1, 0, -x0);    // X ≥ x0
        Cut(-1, 0, x1);    // X ≤ x1
        double w = x1 - x0;
        // 선 z = z0 + (z1 − z0)(X − x0)/w → 위쪽(z ≥ 선): −s·X + Y − (z0 − s·x0) ≥ 0
        foreach (var L in lowers) { double s = (L[i + 1] - L[i]) / w; Cut(-s, 1, -(L[i] - s * x0)); }
        foreach (var U in uppers) { double s = (U[i + 1] - U[i]) / w; Cut(s, -1, U[i] - s * x0); }
        if (buf.Count < 3) return 0;
        double ar = 0;
        for (int k = 0; k < buf.Count; k++) { var P = buf[k]; var Q = buf[(k + 1) % buf.Count]; ar += P.X * Q.Y - Q.X * P.Y; }
        return Math.Abs(ar) / 2;
    }
}
