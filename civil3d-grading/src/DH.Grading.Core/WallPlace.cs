using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[v103.1 · JACK 1002 «옹벽이든 사면이든 부분 변환한 곳은 반대로 돌리면 그 모양으로 돌아가야 — 지금은 옹벽 부분을 인식 못 하고 사면 속성이 같다며 진행이 안 돼»]
/// 옹벽 자리 재기 — 목록의 옹벽이 <b>어느 계단선 위</b>에 있는지, 정지면을 다시 지은 뒤에도 <b>그 자리 그대로</b>인지.
/// <para>클릭선 · 옹벽 안쪽 변(Seg) · 머리 높이가 모두 <b>단 링</b>(ZoneEditCommon의 자)에서 나온다 — 같은 셈을 한 곳(<see cref="BenchRingsOf"/>)에 둔다(하네스가 잰다).</para></summary>
public static class WallPlace
{
    /// <summary>단마다 자 링 — 다시 지은 링을 (2k, 2k+1)로 짝짓고 평균 z로 윗선/아랫선을 가린다. 절토 = 아랫선 · 성토 = 윗선
    /// (ZoneEditCommon 클릭선 <c>GenerateEdgeLinesTagged</c>와 같은 짝짓기 · 같은 고르기 — 어긋나면 클릭한 선과 자가 갈린다).</summary>
    public static Dictionary<int, List<Point3>> BenchRingsOf(VirtualSlope vs, bool up)
    {
        var res = new Dictionary<int, List<Point3>>();
        static double AvgZ(List<Point3> r) { double t = 0; foreach (var q in r) t += q.Z; return r.Count > 0 ? t / r.Count : 0; }
        for (int k = 0; 2 * k + 1 < vs.Rings.Count; k++)
        {
            var rA = vs.Rings[2 * k]; var rB = vs.Rings[2 * k + 1];
            if (rA.Count < 3 || rB.Count < 3) continue;
            bool aHigher = AvgZ(rA) >= AvgZ(rB);
            var crest = aHigher ? rA : rB;
            var toe = aHigher ? rB : rA;
            res[k] = up ? toe : crest;
        }
        return res;
    }

    /// <summary>경계 · 제원 · 구간으로 단 링을 다시 짓는다(원지반 없이 — 클릭선과 같다). 사면이 없으면 빈 표.</summary>
    public static Dictionary<int, List<Point3>> BenchRings(IReadOnlyList<Point3> boundary, GradingParams p, bool up, IReadOnlyList<SlopeZone>? zones)
    {
        var vs = GradingGeometry.Build(boundary, new NullGround(), p, up, zones);
        return vs.HasSlope ? BenchRingsOf(vs, up) : new Dictionary<int, List<Point3>>();
    }

    /// <summary>자리 재기 결과 — 움직였나 · 밑선 평면 차 · 머리 높이 차 · 새 자 위 구간.</summary>
    public readonly record struct Place(bool Moved, double DXY, double DZ, double T0, double T1, string Why);

    /// <summary>저장한 안쪽 변(Seg — 지을 때 자를 1m 간격으로 뜬 점열 · 점은 자 위)과 머리 높이를 새 자에 대 본다.
    /// <para>평면 차 = Seg 점마다 새 자까지 거리의 최대(한 방향만 — Seg는 자를 1m로 뜬 현이라 거꾸로 재면 굽은 데서 현의 처짐만큼 헛경보가 난다 · 검토 v103.1 §5).
    /// 높이 차 = 새 자 [T0', T1']의 머리 높이(<see cref="WallInPoly.HeadZ"/> — 지을 때와 같은 함수) − 저장한 머리.</para></summary>
    public static Place Check(IReadOnlyList<Point3> seg, double headZ, bool up, IReadOnlyList<Point3>? ruler, double tolXY = 1e-3, double tolZ = 1e-3)
    {
        if (ruler == null || ruler.Count < 3) return new Place(true, double.PositiveInfinity, double.PositiveInfinity, 0, 0, "그 단의 자(링)가 없다");
        if (seg == null || seg.Count < 2) return new Place(true, double.PositiveInfinity, double.PositiveInfinity, 0, 0, "저장한 안쪽 변이 없다");
        var cum = GradingGeometry.CumLen2D(ruler);
        double tot = cum[^1];
        if (tot < 1e-9) return new Place(true, double.PositiveInfinity, double.PositiveInfinity, 0, 0, "자 길이가 0");
        double dxy = 0;
        foreach (var q in seg)
        {
            var c = GradingGeometry.PointAtParam(ruler, cum, GradingGeometry.ParamAt(ruler, cum, q.X, q.Y));
            double d = Math.Sqrt((c.X - q.X) * (c.X - q.X) + (c.Y - q.Y) * (c.Y - q.Y));
            if (d > dxy) dxy = d;
        }
        double t0 = GradingGeometry.ParamAt(ruler, cum, seg[0].X, seg[0].Y);
        double t1 = GradingGeometry.ParamAt(ruler, cum, seg[^1].X, seg[^1].Y);
        double tm = GradingGeometry.ParamAt(ruler, cum, seg[seg.Count / 2].X, seg[seg.Count / 2].Y);
        double Span(double a, double b) => b >= a ? b - a : tot - a + b;
        string why = "";
        if (Span(t0, tm) > Span(t0, t1) + 1e-6) { (t0, t1) = (t1, t0); why = "자 도는 방향이 지을 때와 반대"; }   // 가운데 점이 앞으로 가는 구간 밖 — 자 방향이 뒤집혔다
        double hz = WallInPoly.HeadZ(ruler, cum, t0, Span(t0, t1), up);
        double dz = Math.Abs(hz - headZ);
        bool moved = dxy > tolXY || dz > tolZ;
        if (moved)
        {
            var parts = new List<string>();
            if (dxy > tolXY) parts.Add($"밑선이 {dxy * 1000:F1}mm 움직였다");
            if (dz > tolZ) parts.Add($"머리 높이가 {dz * 1000:F1}mm 바뀌었다({headZ:F3} → {hz:F3}m)");
            if (why.Length > 0) parts.Add(why);
            why = string.Join(" · ", parts);
        }
        return new Place(moved, dxy, dz, t0, t1, why);
    }

    /// <summary>고른 선(점열)이 옹벽 폴리곤(<paramref name="tol"/> 넓힘) 안에 놓인 길이 — 사면 변환이 그 옹벽을 «덮었나»(0930 인터뷰 ② · 0.5m 넘으면 덮음).
    /// 같은 단이면 안쪽 변 위, 바깥 단이면 폴리곤 속이 잡힌다. 안쪽 단은 폴리곤 밖이라 안 잡힌다.</summary>
    public static double CoveredLength(IReadOnlyList<Point3> pick, IReadOnlyList<Point3> wallPoly, double tol = 0.01)
    {
        if (pick == null || pick.Count < 2 || wallPoly == null || wallPoly.Count < 3) return 0;
        double ox = wallPoly[0].X, oy = wallPoly[0].Y;
        var gf = new GeometryFactory();
        Geometry poly;
        try
        {
            var pc = wallPoly.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(wallPoly[0].X - ox, wallPoly[0].Y - oy)).ToArray();
            poly = gf.CreatePolygon(pc);
            if (!poly.IsValid) poly = poly.Buffer(0);
        }
        catch { return 0; }
        var lc = new List<Coordinate>();
        foreach (var q in pick) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
        if (lc.Count < 2) return 0;
        try { return gf.CreateLineString(lc.ToArray()).Intersection(poly.Buffer(tol)).Length; }
        catch { return 0; }
    }

    /// <summary>클릭선(열린 점열)을 옹벽 안쪽 변(<paramref name="tol"/> 곁)에 놓인 구간에서 끊는다 → (점열, 옹벽 번호 · 0 = 사면) 조각들을 선 차례로.
    /// 겹친 길이가 <paramref name="minLen"/> 이하인 옹벽은 안 끊는다(끝만 닿음). 클릭선 위에 없는 옹벽(단이 움직였다)도 안 끊는다.</summary>
    public static List<(List<Point3> Pts, int WallNo)> SplitAtWalls(IReadOnlyList<Point3> line,
        IReadOnlyList<(int No, IReadOnlyList<Point3> Seg)> walls, double tol = 0.05, double minLen = 0.5)
    {
        var res = new List<(List<Point3>, int)>();
        if (line == null || line.Count == 0) return res;
        if (line.Count < 2 || walls == null || walls.Count == 0) { res.Add((line.ToList(), 0)); return res; }
        int n = line.Count;
        var cum = new double[n];
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Math.Sqrt((line[i].X - line[i - 1].X) * (line[i].X - line[i - 1].X) + (line[i].Y - line[i - 1].Y) * (line[i].Y - line[i - 1].Y));
        double L = cum[n - 1];
        if (L < 1e-9) { res.Add((line.ToList(), 0)); return res; }
        double ox = line[0].X, oy = line[0].Y;
        var gf = new GeometryFactory();
        LineString? Ls(IReadOnlyList<Point3> p)
        {
            var cs = new List<Coordinate>();
            foreach (var q in p) { var c = new Coordinate(q.X - ox, q.Y - oy); if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c); }
            return cs.Count >= 2 ? gf.CreateLineString(cs.ToArray()) : null;
        }
        double ParamOn(double x, double y)
        {
            double best = double.MaxValue, bt = 0;
            for (int i = 0; i + 1 < n; i++)
            {
                var a = line[i]; var b = line[i + 1];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-18 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double px = a.X + ex * u, py = a.Y + ey * u, d2 = (x - px) * (x - px) + (y - py) * (y - py);
                if (d2 < best) { best = d2; bt = cum[i] + Math.Sqrt(l2) * u; }
            }
            return bt;
        }
        Point3 At(double t)
        {
            if (t <= 0) return line[0];
            if (t >= L) return line[n - 1];
            int i = Array.BinarySearch(cum, t); if (i < 0) i = ~i - 1; if (i >= n - 1) i = n - 2;
            double s = cum[i + 1] - cum[i], u = s < 1e-12 ? 0 : (t - cum[i]) / s;
            var a = line[i]; var b = line[i + 1];
            return new Point3(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);
        }
        var lg = Ls(line);
        var cuts = new List<(double A, double B, int No)>();
        if (lg != null)
            foreach (var (no, seg) in walls)
            {
                if (seg == null || seg.Count < 2) continue;
                var sg = Ls(seg);
                if (sg == null) continue;
                Geometry inter;
                try { inter = lg.Intersection(sg.Buffer(tol)); } catch { continue; }
                for (int g = 0; g < inter.NumGeometries; g++)
                {
                    if (inter.GetGeometryN(g) is not LineString piece || piece.Length <= minLen) continue;
                    double a = ParamOn(piece.StartPoint.X + ox, piece.StartPoint.Y + oy), b = ParamOn(piece.EndPoint.X + ox, piece.EndPoint.Y + oy);
                    if (b < a) (a, b) = (b, a);
                    if (b - a > minLen) cuts.Add((a, b, no));
                }
            }
        if (cuts.Count == 0) { res.Add((line.ToList(), 0)); return res; }
        cuts.Sort((p, q) => p.A.CompareTo(q.A));
        List<Point3> Sub(double a, double b)
        {
            var p = new List<Point3> { At(a) };
            for (int i = 0; i < n; i++) if (cum[i] > a + 1e-9 && cum[i] < b - 1e-9) p.Add(line[i]);
            p.Add(At(b));
            return p;
        }
        double cur = 0;
        foreach (var (a0, b0, no) in cuts)
        {
            double a = Math.Max(a0, cur), b = b0;
            if (b - a <= minLen) continue;                       // 앞 옹벽과 겹친 나머지 — 버림
            if (a - cur > 1e-6) res.Add((Sub(cur, a), 0));
            res.Add((Sub(a, b), no));
            cur = b;
        }
        if (L - cur > 1e-6) res.Add((Sub(cur, L), 0));
        return res;
    }
}
