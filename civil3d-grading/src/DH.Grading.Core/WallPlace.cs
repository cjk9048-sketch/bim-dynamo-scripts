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
        // ★★★[v103.1.3 · JACK 1007 «화면 · 기록 둘 다 고침» — 현장 1007: 옹벽 [70.09 ~ 122.19] 52.1m인데 주황 조각은 [70.04 ~ 122.24] 52.2m]
        //   곁(<paramref name="tol"/>) 띠는 끝이 둥글어, 선이 옹벽 끝을 지나 이어지면 조각이 옹벽 끝보다 더 나갔다(곧은 선 tol · 예각 모서리를 돌면 tol/sin). 그 조각을 사면으로 되돌리며
        //   구배를 바꾸면 옹벽보다 넓은 구간이 생기고, 그 옆 조각 끝은 옹벽 끝에서 떨어져 있었다. → 조각 끝 곁(4·tol 안)에 <b>옹벽 안쪽 변의 끝점</b>이 선 위(tol 안)에 놓여 있으면
        //   조각 끝을 그 점으로 당긴다. 선이 옹벽에서 옆으로 멀어져 끝난 조각 끝 · 선이 먼저 끝난 끝은 곁에 그 끝점이 없어 그대로다. 곁 띠는 «이 옹벽이 이 선 위에 있나»를 가리는 데만 쓴다
        double ParamNear(double x, double y, double e, double w)                  // 선 위 e 곁(±w) 안에서 (x,y)에 가장 가까운 자리 — 곁에 없으면 e 그대로
        {
            double best = double.MaxValue, bt = e;
            for (int i = 0; i + 1 < n; i++)
            {
                if (cum[i + 1] < e - w || cum[i] > e + w) continue;
                var a = line[i]; var b = line[i + 1];
                double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                double u = l2 < 1e-18 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u);
                double t = cum[i] + Math.Sqrt(l2) * u;
                if (t < e - w || t > e + w) continue;
                double px = a.X + ex * u, py = a.Y + ey * u, d2 = (x - px) * (x - px) + (y - py) * (y - py);
                if (d2 < best) { best = d2; bt = t; }
            }
            return bt;
        }
        double Pull(double e, IReadOnlyList<Point3> sgp)
        {
            double best = e, bestGap = double.MaxValue;
            for (int k = 0; k < 2; k++)
            {
                var p = k == 0 ? sgp[0] : sgp[sgp.Count - 1];
                double t = ParamNear(p.X, p.Y, e, 4 * tol);
                var q = At(t);
                double d2 = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
                if (d2 > tol * tol) continue;                                     // 그 끝점은 e 곁의 선 위(곁 띠 안)에 없다
                double gap = Math.Abs(t - e);
                if (gap < bestGap) { bestGap = gap; best = t; }
            }
            return best;
        }
        var lg = Ls(line);
        // ★★[v103.1.3 · 하네스 S153이 잡음 — v103.1부터 있던 틈] 닫힌 선(첫 점 = 끝 점)의 이음매 점은 선 위 자리가 <b>둘</b>(0 · L)인데 ParamOn은 늘 0을 준다 —
        //   옹벽이 이음매를 넘으면 뒤쪽 조각 [y, L]이 (0, y)로 읽혀 <b>옹벽 아닌 자리가 통째로 옹벽 조각</b>이 됐다(둘레 268m · 옹벽 13m → 옹벽 조각 7 + 255m).
        //   조각의 <b>가운데 점</b>이 어느 쪽에 놓였는지로 가른다(PieceSpans) — 띠와 만난 조각은 선을 따라 이어진 한 토막이라 가운데가 두 끝 사이에 있어야 한다
        bool closedLine = n >= 3 && Math.Abs(line[0].X - line[n - 1].X) < 1e-9 && Math.Abs(line[0].Y - line[n - 1].Y) < 1e-9;
        var cuts = new List<(double A, double B, int No, double Min)>();
        if (lg != null)
            foreach (var (no, seg) in walls)
            {
                if (seg == null || seg.Count < 2) continue;
                var sg = Ls(seg);
                if (sg == null) continue;
                Geometry inter;
                try { inter = lg.Intersection(sg.Buffer(tol)); } catch { continue; }
                var spans = new List<(double A, double B)>();
                for (int g = 0; g < inter.NumGeometries; g++)
                {
                    if (inter.GetGeometryN(g) is not LineString piece || piece.Length <= 1e-9) continue;
                    double a = ParamOn(piece.StartPoint.X + ox, piece.StartPoint.Y + oy), b = ParamOn(piece.EndPoint.X + ox, piece.EndPoint.Y + oy);
                    var mc = new NetTopologySuite.LinearReferencing.LengthIndexedLine(piece).ExtractPoint(piece.Length * 0.5);
                    foreach (var (sa, sb) in PieceSpans(a, b, ParamOn(mc.X + ox, mc.Y + oy), L, closedLine))
                    {
                        double pa = Pull(sa, seg), pb = Pull(sb, seg);
                        if (pb - pa > 1e-6) spans.Add((pa, pb));
                    }
                }
                // 닫힌 선의 이음매에 닿은 두 토막(0에서 시작 · L에서 끝)은 <b>한 옹벽 자리</b>다 — «끝만 닿음»(minLen)은 둘을 합친 길이로 잰다.
                //   따로 재면 옹벽이 이음매를 0.3m만 넘을 때 그 0.3m가 사면 토막으로 떨어져 나간다
                double seamLen = 0;
                if (closedLine)
                {
                    double l0 = 0, l1 = 0;
                    foreach (var (sa, sb) in spans) { if (sa < 1e-9) l0 = Math.Max(l0, sb - sa); if (L - sb < 1e-9) l1 = Math.Max(l1, sb - sa); }
                    if (l0 > 0 && l1 > 0) seamLen = l0 + l1;
                }
                foreach (var (sa, sb) in spans)
                {
                    bool seamPart = seamLen > 0 && (sa < 1e-9 || L - sb < 1e-9);
                    if ((seamPart ? seamLen : sb - sa) > minLen) cuts.Add((sa, sb, no, seamPart ? 1e-6 : minLen));
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
        foreach (var (a0, b0, no, min) in cuts)
        {
            double a = Math.Max(a0, cur), b = b0;
            if (b - a <= min) continue;                          // 앞 옹벽과 겹친 나머지 — 버림(이음매에 닿은 토막은 짧아도 그 옹벽 자리다)
            if (a - cur > 1e-6) res.Add((Sub(cur, a), 0));
            res.Add((Sub(a, b), no));
            cur = b;
        }
        if (L - cur > 1e-6) res.Add((Sub(cur, L), 0));
        return res;
    }

    /// <summary>★[v103.1.3] 곁 띠와 만난 조각 하나를 선 위 토막으로 — 두 끝의 자리(<paramref name="a"/> · <paramref name="b"/>)와 조각 가운데 점의 자리(<paramref name="m"/>)를 받는다.
    /// <para>열린 선이면 [작은 값, 큰 값] 하나. 닫힌 선(첫 점 = 끝 점)은 이음매 점의 자리가 0이기도 L이기도 해서 끝 자리만으로는 못 가른다 —
    /// 가운데가 놓인 쪽이 그 조각이다: 한 끝이 이음매면 가운데가 다른 끝보다 앞이면 [0, x] · 뒤면 [x, L] · 두 끝이 다 이음매면 한 바퀴 [0, L] ·
    /// 두 끝이 다 이음매가 아닌데 가운데가 그 사이에 없으면 조각이 이음매를 넘어 한 줄로 온 것이라 [큰 값, L]과 [0, 작은 값] 둘로 준다.</para></summary>
    public static List<(double A, double B)> PieceSpans(double a, double b, double m, double L, bool closedLine)
    {
        var res = new List<(double, double)>(2);
        if (!closedLine) { res.Add((Math.Min(a, b), Math.Max(a, b))); return res; }
        bool sa = a < 1e-9 || L - a < 1e-9, sb = b < 1e-9 || L - b < 1e-9;      // 이음매 점인가
        if (sa && sb) { res.Add((0, L)); return res; }
        if (sa || sb)
        {
            double x = sa ? b : a;                                               // 이음매가 아닌 끝
            res.Add(m < x ? (0, x) : (x, L));
            return res;
        }
        double lo = Math.Min(a, b), hi = Math.Max(a, b);
        if (m > lo && m < hi) res.Add((lo, hi));
        else { res.Add((hi, L)); res.Add((0, lo)); }
        return res;
    }

    /// <summary>옹벽 하나의 자리 재기 결과 — 그 옹벽 · 재기(<see cref="Place"/>) · 쓴 자 종류(0 단 링 · 1 계획선 — 판 1 옹벽은 계획선으로 물러날 수 있다).</summary>
    public readonly record struct WallCheck(WallRec Wall, Place Place, int RulerKind);

    /// <summary>★★★[v103.1.9 · JACK 1002 Q1 «옹벽이 서 있던 단이 움직이면 재생성 <b>전에</b> 묻고 진행 — 아니오면 아무것도 안 바뀜»]
    /// 옹벽마다 자리 재기 — 정지면을 다시 지으면 쓸 경계 · 제원 · 구간으로 단 링을 다시 짓고(<see cref="BenchRings"/> — 방향마다 한 번 · 그 방향 옹벽이 있을 때만) 저장한 안쪽 변 · 머리를 대 본다(<see cref="Check"/>).
    /// <para>정지면 생성 <b>끝</b>(재생성 뒤 확인 — v103.1 · 움직였으면 보류)과 재생성 <b>전</b>의 묻기(v103.1.9)가 <b>같은 함수</b>를 쓴다 — 같은 입력이면 같은 답(묻고 «예» 했는데 끝에서 다른 답이 나오지 않게).</para>
    /// <para>판 1 옹벽(자 종류 칸이 없어 단 링으로 읽힘 · <c>SlopeW &lt; 0</c>)이 단 링에서 움직였으면 계획선으로 한 번 더 대 본다(v103.1 코드 검토 낮음 7) — 그때 쓴 자 종류를 돌려준다(목록에 쓰는 것은 부르는 쪽).
    /// 옹벽을 고치지 않는다(읽기만).</para></summary>
    public static List<WallCheck> CheckAll(IEnumerable<WallRec> walls, IReadOnlyList<Point3> boundary, GradingParams p,
        IReadOnlyList<SlopeZone> cutZones, IReadOnlyList<SlopeZone> fillZones)
    {
        var rings = new Dictionary<bool, Dictionary<int, List<Point3>>>();
        Dictionary<int, List<Point3>> R(bool up)
        {
            if (!rings.TryGetValue(up, out var r)) { r = BenchRings(boundary, p, up, up ? cutZones : fillZones); rings[up] = r; }
            return r;
        }
        var res = new List<WallCheck>();
        foreach (var w in walls)
        {
            int rk = w.RulerKind;
            var ruler = rk == 1 ? boundary : (R(w.Up).TryGetValue(w.Bench, out var rr) ? rr : null);
            var chk = Check(w.Seg, w.HeadZ, w.Up, ruler);
            if (chk.Moved && w.SlopeW < 0 && rk == 0)
            {
                var chk2 = Check(w.Seg, w.HeadZ, w.Up, boundary);
                if (!chk2.Moved) { chk = chk2; rk = 1; }
            }
            res.Add(new WallCheck(w, chk, rk));
        }
        return res;
    }

    /// <summary>★[v103.1.9 · 계획 검토 중간 1] 재생성 전에 잴 옹벽 — 이 구역 목록(머리 계획선이 빈 값이거나 이 계획선) · 넣은 옹벽(State 1) · 보류 아님(빈 값 또는 «다시 짓기 실패» 보류 —
    /// DHWALLCOMP가 다시 시도하는 것) · 이번에 덮여 되돌릴 옹벽 아님. 미리 재기 · 끝 가르기 · 하네스가 같은 함수를 쓴다(세는 옹벽이 어긋나면 헛 ⚠).</summary>
    public static List<WallRec> Candidates(WallListHead head, IEnumerable<WallRec> walls, string region, ICollection<int>? coveredNos, string rebuildFailTag)
    {
        var res = new List<WallRec>();
        if (!(head.PlanHandle.Length == 0 || head.PlanHandle == region)) return res;
        foreach (var w in walls)
            // ★[v103.1.12] «합성 실패» 보류도 대상 — 자리는 그대로이고 다음 합성이 다시 해 본다(«다시 짓기 실패»와 같은 부류 · WallList.IsRetryHold)
            if (w.State == 1 && (w.Hold.Length == 0 || w.Hold.StartsWith(rebuildFailTag, StringComparison.Ordinal) || WallList.IsRetryHold(w.Hold)) && (coveredNos == null || !coveredNos.Contains(w.No)))
                res.Add(w);
        return res;
    }

    /// <summary>★[v103.1.14] 이어 짓기 판정의 갈래 — 없음(종전 길) · 멈춤(까닭 · 아무것도 안 바뀜) · 이어 짓기.</summary>
    public enum ExtendKind { None, Stop, Extend }

    /// <summary>★[v103.1.14] 이어 짓기 판정 결과. 앞 = 새 구간 첫 끝(T0)에 끝이 맞닿은 옹벽 · 뒤 = 새 구간 끝 끝(T1)에 첫 끝이 맞닿은 옹벽.</summary>
    public sealed class Extend
    {
        public ExtendKind Kind;
        public string Why = "";
        /// <summary>맞닿은 옹벽 — 앞 · 뒤 차례(1~2개).</summary>
        public List<int> Nos = new();
        public WallRec? Front, Back;
        /// <summary>합성이 되면 이은 옹벽이 물려받을 번호 — 맞닿은 옹벽마다 (물려받을 번호가 있으면 그것 · 없으면 제 번호) 중 작은 것.</summary>
        public int KeepNo;
        /// <summary>이은 구간(이 자 위).</summary>
        public double T0, T1;
        /// <summary>지킬 측선 방향 — FixA = 이은 구간 끝 끝(end1 · «시점» 쪽)에서 뒤 옹벽의 측선 · FixB = 첫 끝(end0 · «종점» 쪽)에서 앞 옹벽의 측선. null이면 그 끝은 찍는다(맞닿은 옹벽이 없는 끝).</summary>
        public (double X, double Y)? FixA, FixB;
        /// <summary>★[계획 검토 낮음 8] 이은 옹벽을 지을 앞면 구배 · 앞면 최소 너비 · 원지반 재는 간격 — 맞닿은 옹벽이 지은 값(옛 옹벽 몫 앞면이 말없이 바뀌지 않게). 음수 = 모름(지금 설정값).</summary>
        public double SlopeW = -1, FaceRun = -1, Grid = -1;
        public string Note = "";
    }

    /// <summary>★[v103.1.14] 고를 때 알림용 — 고른 구간 앞(첫 끝) · 뒤(끝 끝)에 맞닿은 옹벽(<see cref="ExtendPlan"/>과 같은 후보 · 같은 맞닿음 · 멈춤 판정은 안 함).
    /// ZoneEditCommon이 단높이 · 소단 기본값을 그 옹벽 값으로 띄우는 데만 쓴다 — 이어 짓기 여부는 늘 <see cref="ExtendPlan"/>이 정한다.</summary>
    public static (WallRec? Front, WallRec? Back) Touching(IReadOnlyList<Point3> ruler, double[] cum, double t0, double t1, bool up, int bench, IEnumerable<WallRec> walls, double touchTol = 0.01)
    {
        if (ruler == null || ruler.Count < 3 || cum == null || cum.Length < 2) return (null, null);
        var ws = walls as IReadOnlyCollection<WallRec> ?? walls.ToList();
        var seg = WallInPoly.SegmentPoints(ruler, cum, t0, t1);
        if (seg.Count < 2) return (null, null);
        static double D(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        var away = WallList.GoingAway(ws);
        WallRec? f = null, b = null;
        foreach (var w in ws)
        {
            // ★[코드 검토 낮음] 보류 옹벽(움직임 · 다시 해 볼 보류 모두)은 알림에서 뺀다 — 이어 짓지 못한다(Enter 뒤 판정이 까닭을 알린다)
            if (w.State != 1 || w.Up != up || w.Bench != bench || away.Contains(w.No) || w.Hold.Length > 0 || w.Seg == null || w.Seg.Count < 2) continue;
            if (f == null && D(seg[0], w.Seg[^1]) <= touchTol) f = w;
            if (b == null && D(seg[^1], w.Seg[0]) <= touchTol) b = w;
        }
        return (f, b);
    }

    /// <summary>★★★[v103.1.14 · JACK 1014 ② «새 구간이 기존 구간 옹벽 끝에 맞닿으면 단높이·소단이 같을 때만 그 옹벽을 늘려 한 옹벽으로(번호 그대로 · 양쪽 다 닿으면 셋을 하나로 · 다르면 멈춤+까닭)» ·
    /// 1019 «새 쪽 끝 한 점만 · 머리는 이어진 전체로»] 고른 구간 [<paramref name="t0"/>, <paramref name="t1"/>](<paramref name="ruler"/> 위)이 같은 선 옹벽 끝에 맞닿았나 · 이어 지을 수 있나.
    /// <para>후보 = 같은 방향 · 같은 단 · 넣음(State 1) · 미룬 옹벽이 바꿀 옛 옹벽(<see cref="WallList.GoingAway"/>) 아님 · 움직인 보류 아님(저장한 안쪽 변이 지금 자리가 아니다 — 종전처럼 판정에서 뺀다).
    /// 맞닿음 = 새 안쪽 변(<see cref="WallInPoly.SegmentPoints"/>) 끝 점과 옹벽 안쪽 변 끝 점이 <paramref name="touchTol"/> 안.</para>
    /// <para>멈춤: «다시 짓기 실패» · «합성 실패» 보류 · 단높이나 소단이 다름(1e-6) · 옹벽 안쪽 변이 이 자에서 벗어남(<see cref="Check"/>) · 같은 옹벽이 앞뒤(한 바퀴) · 앞(뒤)에 둘 · 이은 길이가 안 맞음(2cm) · 이으면 한 바퀴.
    /// 맞닿은 옹벽 쪽 측선 방향을 못 되살리거나(지은 그대로를 못 지킨다) 두 옹벽의 앞면 값이 달라도 멈춘다 · 미룬 옹벽이 바꿀 옛 옹벽에만 맞닿아도 멈춘다(폴리곤 그리기 전에).
    /// ZoneEditCommon(고를 때 · Enter 뒤) · BuildWallAdd(같은 입력으로 다시 — 화면과 같은 답인지) · 하네스 S160이 같은 함수를 쓴다.</para></summary>
    public static Extend ExtendPlan(IReadOnlyList<Point3> ruler, double[] cum, double t0, double t1, bool up, int bench,
        IEnumerable<WallRec> walls, double askH, double askT, double touchTol = 0.01)
    {
        var res = new Extend { T0 = t0, T1 = t1 };
        Extend Stop(string why) { res.Kind = ExtendKind.Stop; res.Why = why; return res; }
        if (ruler == null || ruler.Count < 3 || cum == null || cum.Length < 2) return res;
        double tot = cum[^1];
        var ws = walls as IReadOnlyCollection<WallRec> ?? walls.ToList();
        var seg = WallInPoly.SegmentPoints(ruler, cum, t0, t1);
        if (seg.Count < 2) return res;
        static double D(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        var e0 = seg[0]; var e1 = seg[^1];
        var away = WallList.GoingAway(ws);
        WallRec? front = null, back = null, awayF = null, awayB = null;
        foreach (var w in ws)
        {
            if (w.State != 1 || w.Up != up || w.Bench != bench || WallList.IsMovedHold(w.Hold) || w.Seg == null || w.Seg.Count < 2) continue;
            bool f = D(e0, w.Seg[^1]) <= touchTol, b = D(e1, w.Seg[0]) <= touchTol;
            // ★[계획 검토 낮음 10] 미룬 옹벽이 바꿀 옛 옹벽은 이어 짓지 않는다 — 그 끝에 이을 옹벽이 따로 없으면 아래에서 멈춘다(종전처럼 폴리곤을 그리기 전에)
            if (away.Contains(w.No)) { if (f) awayF ??= w; if (b) awayB ??= w; continue; }
            if (f) { if (front != null) return Stop($"새 구간 첫 끝에 옹벽 {front.No} · {w.No}이 함께 맞닿는다 — 이어 짓지 않고 멈춘다"); front = w; }
            if (b) { if (back != null) return Stop($"새 구간 끝 끝에 옹벽 {back.No} · {w.No}이 함께 맞닿는다 — 이어 짓지 않고 멈춘다"); back = w; }
            // (첫 끝끼리 · 끝 끝끼리 같은 것은 맞닿음이 아니다 — 옹벽 위를 덮는 구간(같은 선 바꿈 · 1002)이 그렇다 · 겹침 판정이 맡는다)
        }
        foreach (var (aw, has) in new[] { (awayF, front != null), (awayB, back != null) })
            if (aw != null && !has)
                return Stop($"옹벽 {aw.No} 끝에 맞닿는데 그 옹벽은 미룬 옹벽이 바꿀 옹벽이다(합성이 되면 지워진다) — [옹벽 합성]을 먼저 하거나 옹벽 끝에서 띄워 구간을 다시 지정하세요");
        if (front == null && back == null) return res;
        if (front != null && ReferenceEquals(front, back)) return Stop($"새 구간의 두 끝이 다 옹벽 {front.No}에 맞닿는다 — 이어 지으면 한 바퀴다(한 바퀴 옹벽은 전체구간 옹벽 변환으로)");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var spans = new Dictionary<WallRec, (double T0, double T1)>();
        foreach (var w in new[] { front, back })
        {
            if (w == null) continue;
            if (WallList.IsRetryHold(w.Hold))
                return Stop($"옹벽 {w.No}은 보류({(w.Hold.Length > 60 ? w.Hold.Substring(0, 60) + "…" : w.Hold)})라 이어 짓지 못한다 — [옹벽 합성]으로 넣거나 사면 변환으로 되돌린 뒤 다시 하세요");
            if (Math.Abs(w.H - askH) > 1e-6 || Math.Abs(w.T - askT) > 1e-6)
                return Stop(string.Format(ci, "옹벽 {0}은 단높이 {1:0.##}m · 소단 {2:0.##}m, 이번 값은 단높이 {3:0.##}m · 소단 {4:0.##}m — 같을 때만 이어 짓는다(값을 맞추거나 옹벽 끝에서 띄워 구간을 다시 지정하세요)", w.No, w.H, w.T, askH, askT));
            var pl = Check(w.Seg, w.HeadZ, up, ruler);
            if (pl.Moved) return Stop($"옹벽 {w.No}이 이 선(자)에서 벗어났다({pl.Why}) — 이어 짓지 않고 멈춘다");
            spans[w] = (pl.T0, pl.T1);
        }
        double mT0 = front != null ? spans[front].T0 : t0, mT1 = back != null ? spans[back].T1 : t1;
        double sNew = GradingGeometry.SpanOf(t0, t1, tot), sM = GradingGeometry.SpanOf(mT0, mT1, tot);
        double sSum = sNew + (front != null ? GradingGeometry.SpanOf(spans[front].T0, spans[front].T1, tot) : 0) + (back != null ? GradingGeometry.SpanOf(spans[back].T0, spans[back].T1, tot) : 0);
        if (sSum >= tot - 0.02) return Stop(string.Format(ci, "이으면 {0:F1}m로 한 바퀴(둘레 {1:F1}m)다 — 한 바퀴 옹벽은 전체구간 옹벽 변환으로", sSum, tot));
        if (Math.Abs(sM - sSum) > 0.02) return Stop(string.Format(ci, "이은 구간 길이 {0:F2}m가 옹벽 · 새 구간 길이 합 {1:F2}m와 안 맞는다 — 이어 짓지 않고 멈춘다", sM, sSum));
        res.Kind = ExtendKind.Extend; res.Front = front; res.Back = back; res.T0 = mT0; res.T1 = mT1;
        if (front != null) res.Nos.Add(front.No);
        if (back != null) res.Nos.Add(back.No);
        // ★[계획 검토 낮음 6] 물려받을 번호는 <b>미룬</b> 이어 짓기 옹벽의 것만 — 합성에 든 옹벽의 KeepNo는 이미 물려받았거나 못 받은 지난 값이다
        res.KeepNo = new[] { front, back }.Where(w => w != null).Select(w => w!.KeepNo > 0 && WallList.IsDeferred(w) ? w.KeepNo : w.No).Min();
        // ★[계획 검토 중간 1 · JACK 1019 «옛 옹벽 반대쪽 측선은 지은 그대로»] 방향을 못 되살리면 찍기로 물러나지 않고 멈춘다(모양이 바뀌는데 번호만 그대로가 된다)
        if (front != null)
        {
            var sd = WallInPoly.SideDirs(front.Poly, front.IsWall, front.Seg, out string why);
            if (sd == null) return Fail($"옹벽 {front.No} 쪽 측선을 지은 그대로 되살리지 못했다({why})");
            res.FixB = sd.Value.AtEnd0;
        }
        if (back != null)
        {
            var sd = WallInPoly.SideDirs(back.Poly, back.IsWall, back.Seg, out string why);
            if (sd == null) return Fail($"옹벽 {back.No} 쪽 측선을 지은 그대로 되살리지 못했다({why})");
            res.FixA = sd.Value.AtEnd1;
        }
        // ★[계획 검토 낮음 8] 앞면 구배 · 최소 너비 · 간격 — 맞닿은 옹벽이 지은 값(판 1 옹벽은 음수 = 모름). 두 옹벽이 서로 다르면 멈춘다
        {
            var known = new[] { front, back }.Where(w => w != null && w.SlopeW >= 0 && w.FaceRun >= 0 && w.Grid > 0).ToList();
            if (known.Count == 2 && (Math.Abs(known[0]!.SlopeW - known[1]!.SlopeW) > 1e-9 || Math.Abs(known[0]!.FaceRun - known[1]!.FaceRun) > 1e-9 || Math.Abs(known[0]!.Grid - known[1]!.Grid) > 1e-9))
                return Fail(string.Format(ci, "옹벽 {0} · {1}을 지은 앞면 값이 다르다(앞면 구배 1:{2:0.###} · {3:0.###} · 최소 너비 {4:0.###} · {5:0.###}m · 간격 {6:0.##} · {7:0.##}m)",
                    known[0]!.No, known[1]!.No, known[0]!.SlopeW, known[1]!.SlopeW, known[0]!.FaceRun, known[1]!.FaceRun, known[0]!.Grid, known[1]!.Grid));
            if (known.Count > 0) { res.SlopeW = known[0]!.SlopeW; res.FaceRun = known[0]!.FaceRun; res.Grid = known[0]!.Grid; }
        }
        res.Note = string.Format(ci, "옹벽 {0}에 이어 짓는다 — 구간 [{1:F2}..{2:F2}] → [{3:F2}..{4:F2}] {5:F1}m · 번호 {6} 그대로 · 찍을 끝 {7}개",
            string.Join(" · ", res.Nos), t0, t1, mT0, mT1, sM, res.KeepNo, (res.FixA == null ? 1 : 0) + (res.FixB == null ? 1 : 0))
            + (res.SlopeW >= 0 ? string.Format(ci, " · 앞면은 옹벽 {0}을 지은 값(1:{1:0.###} · {2:0.###}m · 간격 {3:0.##}m)", res.Nos[0], res.SlopeW, res.FaceRun, res.Grid) : "");
        return res;
        Extend Fail(string why)
        {
            var r = new Extend { Kind = ExtendKind.Stop, Why = why + " — 이어 짓지 않고 멈춘다", T0 = t0, T1 = t1 };
            return r;
        }
    }

    /// <summary>끝 확인에서 옹벽 하나를 어떻게 다루나.</summary>
    public enum EndKind
    {
        /// <summary>그대로 — 다시 합성한다.</summary>
        Keep,
        /// <summary>보류였는데 단이 제자리로 돌아왔다 — 보류를 풀고 다시 합성한다.</summary>
        Back,
        /// <summary>재생성 전에 묻고 «예»를 받았고 끝에서도 움직였다 — 지운다(목록 · 결과물).</summary>
        Remove,
        /// <summary>전부터 보류였고 아직 움직여 있다 — 보류 그대로(⚠ 아님 · 이번 작업 탓이 아니다).</summary>
        StillHeld,
        /// <summary>묻지 않았는데 움직였다(미리 잰 것과 다르다 · 미리 못 쟀다) — 보류 + ⚠.</summary>
        Unexpected,
        /// <summary>미리 «움직인다»고 묻고 «예»를 받았는데 끝에서는 그대로다 — 안 지운다 · ⚠(미리 잰 것과 다르다).</summary>
        AskedButStayed,
    }

    /// <summary>★[v103.1.9 · 계획 검토 중간 1 · 2] 끝 확인 가르기 — 재생성 뒤 자리 재기(<see cref="CheckAll"/> — 넣은 옹벽 전부)를 미리 잰 대상(<paramref name="preCand"/>) ·
    /// «예»를 받은 번호(<paramref name="preYes"/>)와 견준다. 미리 재지 않았으면(<paramref name="preCand"/> = null) 움직인 옹벽은 전부 «묻지 않았는데 움직임»(전부터 보류였던 것은 그대로 보류).
    /// <para>견주는 것은 <b>미리 잰 대상 가운데</b> 이번에 움직인 것 — 전부터 보류였던 옹벽(대상이 아님)은 미리 · 끝 어느 쪽에도 안 센다(검토 중간 1: 그것을 세면 첫 «예» 다음부터 매번 헛 ⚠).</para></summary>
    public static List<(WallCheck C, EndKind Kind)> SortEnd(IReadOnlyList<WallCheck> end, ICollection<int>? preCand, ICollection<int>? preYes, string rebuildFailTag)
    {
        var res = new List<(WallCheck, EndKind)>();
        foreach (var c in end)
        {
            var w = c.Wall;
            bool heldBefore = w.Hold.Length > 0 && !w.Hold.StartsWith(rebuildFailTag, StringComparison.Ordinal) && !WallList.IsRetryHold(w.Hold);
            bool cand = preCand != null ? preCand.Contains(w.No) : !heldBefore;
            bool yes = preYes != null && preYes.Contains(w.No);
            EndKind k;
            if (!c.Place.Moved) k = yes ? EndKind.AskedButStayed : heldBefore ? EndKind.Back : EndKind.Keep;
            else if (yes) k = EndKind.Remove;
            else if (heldBefore && !cand) k = EndKind.StillHeld;
            else k = EndKind.Unexpected;
            res.Add((c, k));
        }
        return res;
    }
}
