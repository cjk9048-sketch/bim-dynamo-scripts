using System.Globalization;
using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using Box = DH.Grading.Core.GroundCrop.Box;

namespace DH.Grading.Core;

/// <summary>★[JACK 1126 «관로 설계는 선형으로 긴 사업 영역 — 네모로 가져오면 불필요한 부분이 많아 무겁다»] <b>다각형 범위 — 순수 셈</b>(Civil 없이 시험).
/// <list type="bullet">
/// <item>만들기 세 길 — 점 목록(지도 · 도면 닫힌 선) · 노선 띠(<see cref="FromRoute"/> — 폭 = <b>전체 폭</b>, 양쪽 반씩 · 끝 평평 · 굽이 둥글게).</item>
/// <item>꼬인 다각형은 <b>고치지 않고 거절</b>(MakeValid는 조각을 말없이 버리거나 나눈다 — 사람이 그린 꼴이 바뀜). 띠는 늘 바름(안 빈 곳은 채움 · 알림).</item>
/// <item>꼭짓점이 <see cref="MaxVertices"/>보다 많으면 단순화(위상 보존 · 허용오차를 두 배씩 · 알림).</item>
/// <item>테두리 채움(<see cref="BorderFill"/>) · 바깥 숨김 조각(<see cref="HidePieces"/>) · 설계 자리 검사(<see cref="Covers"/>).</item>
/// </list>
/// 고리 = 반시계 · 닫는 점 없음(첫 점을 끝에 다시 넣지 않음).</summary>
public static class AreaPolygon
{
    /// <summary>노선 띠 기본 폭(m) — <b>전체 폭</b>(노선 양쪽 50 m씩). 관로 종횡단 · 터파기에 넉넉한 폭(내 판단).</summary>
    public const double DefaultWidth = 100.0;
    /// <summary>꼭짓점 상한 — 넘으면 단순화(서버 질의 · Civil 경계가 가벼워짐).</summary>
    public const int MaxVertices = 1000;
    /// <summary>호를 나눌 때 현 처짐 끝(m).</summary>
    public const double ArcSag = 0.05;
    public const double MinArea = 1.0;

    static readonly GeometryFactory Gf = new();

    /// <summary>만든 결과 — 거절이면 Ok 거짓 · Why = 짧은 명사형 글.</summary>
    public sealed record Made(bool Ok, List<(double X, double Y)> Ring, string Why, string Note)
    {
        public static Made No(string why) => new(false, new(), why, "");
    }

    // ══════════════════════════════ 만들기 ══════════════════════════════

    /// <summary>점 목록(지도 · 닫힌 선) → 다각형. 겹친 점 · 닫는 점은 뺌 · 꼬임 = 거절.</summary>
    public static Made FromPoints(IEnumerable<(double X, double Y)> pts)
    {
        var v = Dedupe(pts);
        if (v.Count < 3) return Made.No("꼭짓점 3개 미만 → 다시 그리기");
        Polygon poly;
        try { poly = Gf.CreatePolygon(Closed(v)); }
        catch { return Made.No("다각형 못 만듦 → 다시 그리기"); }
        if (!poly.IsValid) return Made.No("다각형 꼬임 → 다시 그리기");
        if (poly.Area < MinArea) return Made.No("다각형 너무 작음 → 다시 그리기");
        var bb = poly.EnvelopeInternal;
        if (bb.Width < 1 || bb.Height < 1) return Made.No("다각형 너무 좁음 → 다시 그리기");
        return Finish(poly, "");
    }

    /// <summary>노선(꺾은선) + 전체 폭 → 띠 다각형(끝 평평 · 굽이 둥글게). 띠 안 빈 곳(노선이 고리를 이룬 자리)은 채운다.</summary>
    public static Made FromRoute(IReadOnlyList<(double X, double Y)> path, double width)
    {
        var v = Dedupe(path, stripClose: false);
        if (v.Count < 2) return Made.No("노선 점 부족 → 다른 선 고르기");
        if (!(width >= 1)) return Made.No("폭 1 m 미만 → 폭 다시");
        var line = Gf.CreateLineString(v.Select(p => new Coordinate(p.X, p.Y)).ToArray());
        if (line.Length < 1) return Made.No("노선 너무 짧음 → 다른 선 고르기");
        var bp = new BufferParameters { EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Flat, JoinStyle = NetTopologySuite.Operation.Buffer.JoinStyle.Round, QuadrantSegments = 8 };
        Geometry g;
        try { g = BufferOp.Buffer(line, width / 2, bp); }
        catch { return Made.No("띠 못 만듦 → 다른 선 고르기"); }
        var poly = Largest(g);
        if (poly == null || poly.Area < MinArea) return Made.No("띠 못 만듦 → 다른 선 고르기");
        string note = "";
        if (poly.NumInteriorRings > 0) { note = $"띠 안 빈 곳 {poly.NumInteriorRings}곳 채움"; poly = Gf.CreatePolygon((LinearRing)poly.ExteriorRing.Copy()); }
        return Finish(poly, note);
    }

    /// <summary>꼭짓점 상한 · 반시계 · 닫는 점 뺌.</summary>
    static Made Finish(Polygon poly, string note)
    {
        int n0 = poly.ExteriorRing.NumPoints - 1;
        if (n0 > MaxVertices)
        {
            Geometry s = poly;
            // ★[검토 9] 0.05 m부터 두 배씩 · 마지막은 꼭 5 m — 알림에는 <b>실제로 쓴</b> 허용값(못 줄였으면 그렇다고)
            double used = 0;
            for (double tol = 0.05; ; tol = Math.Min(tol * 2, 5.0))
            {
                s = TopologyPreservingSimplifier.Simplify(poly, tol);
                used = tol;
                if ((s is Polygon sp && sp.ExteriorRing.NumPoints - 1 <= MaxVertices) || tol >= 5.0) break;
            }
            if (Largest(s) is Polygon ps && ps.IsValid)
            {
                poly = ps;
                int n1 = poly.ExteriorRing.NumPoints - 1;
                note = Join(note, string.Format(CultureInfo.InvariantCulture, "꼭짓점 {0} → {1}(단순화 {2:0.##} m{3})", n0, n1, used, n1 > MaxVertices ? $" · {MaxVertices:N0}개 아래로 못 줄임" : ""));
            }
        }
        var ring = poly.ExteriorRing.Coordinates.Take(poly.ExteriorRing.NumPoints - 1).Select(c => (c.X, c.Y)).ToList();
        if (SignedArea(ring) < 0) ring.Reverse();
        return new Made(true, ring, "", note);
    }

    static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + " · " + b;

    static List<(double X, double Y)> Dedupe(IEnumerable<(double X, double Y)> pts, bool stripClose = true)
    {
        var v = new List<(double X, double Y)>();
        foreach (var p in pts)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) continue;
            if (v.Count > 0 && Math.Abs(v[^1].X - p.X) <= 1e-6 && Math.Abs(v[^1].Y - p.Y) <= 1e-6) continue;
            v.Add(p);
        }
        while (stripClose && v.Count > 1 && Math.Abs(v[0].X - v[^1].X) <= 1e-6 && Math.Abs(v[0].Y - v[^1].Y) <= 1e-6) v.RemoveAt(v.Count - 1);
        return v;
    }

    static Coordinate[] Closed(IReadOnlyList<(double X, double Y)> v)
    {
        var c = new Coordinate[v.Count + 1];
        for (int i = 0; i < v.Count; i++) c[i] = new Coordinate(v[i].X, v[i].Y);
        c[^1] = c[0].Copy();
        return c;
    }

    static Polygon? Largest(Geometry g)
    {
        Polygon? best = null;
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon p && (best == null || p.Area > best.Area)) best = p;
        return best;
    }

    /// <summary>폴리선 꼭짓점(볼록 bulge) → 점 목록 — 호는 현 처짐 <paramref name="sag"/> 안으로 잘게(닫힘이면 끝 → 첫 점 호까지 · 닫는 점은 안 넣음).</summary>
    public static List<(double X, double Y)> Polyline(IReadOnlyList<(double X, double Y, double Bulge)> v, bool closed, double sag = ArcSag)
    {
        var res = new List<(double X, double Y)>();
        int n = v.Count;
        if (n == 0) return res;
        res.Add((v[0].X, v[0].Y));
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = v[i]; var b = v[(i + 1) % n];
            foreach (var p in ArcPoints((a.X, a.Y), (b.X, b.Y), a.Bulge, sag)) res.Add(p);
        }
        if (closed && res.Count > 1) res.RemoveAt(res.Count - 1);   // 첫 점으로 돌아온 끝
        return res;
    }

    /// <summary>호(시작 · 끝 · bulge = tan(사잇각/4)) 위 점 — 시작점은 빼고 끝점은 넣음. bulge 0 = 끝점 하나.</summary>
    public static List<(double X, double Y)> ArcPoints((double X, double Y) a, (double X, double Y) b, double bulge, double sag = ArcSag)
    {
        var res = new List<(double X, double Y)>();
        double dx = b.X - a.X, dy = b.Y - a.Y, chord = Math.Sqrt(dx * dx + dy * dy);
        if (Math.Abs(bulge) < 1e-9 || chord < 1e-9) { res.Add(b); return res; }
        double th = 4 * Math.Atan(bulge);                    // 사잇각(부호 = 방향 · 양 = 반시계)
        double r = chord / (2 * Math.Sin(Math.Abs(th) / 2));
        // 중심: 현 가운데에서 현에 수직으로
        double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
        double h = r * Math.Cos(Math.Abs(th) / 2);
        double nx = -dy / chord, ny = dx / chord;           // 현의 왼쪽
        double sgn = Math.Sign(bulge);                      // 반시계 호 = 현 왼쪽(큰 호는 cos 음수라 저절로 오른쪽)
        double cx = mx + sgn * nx * h, cy = my + sgn * ny * h;
        double a0 = Math.Atan2(a.Y - cy, a.X - cx);
        // 한 조각 각 — 처짐 r(1 − cos(φ/2)) ≤ sag
        double phi = sag >= r ? Math.PI / 4 : 2 * Math.Acos(1 - sag / r);
        int k = Math.Max(1, (int)Math.Ceiling(Math.Abs(th) / Math.Max(1e-6, phi)));
        k = Math.Min(k, 2000);
        for (int i = 1; i < k; i++)
        {
            double t = a0 + th * i / k;
            res.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        res.Add(b);
        return res;
    }

    // ══════════════════════════════ 재기 ══════════════════════════════

    public static double SignedArea(IReadOnlyList<(double X, double Y)> r)
    {
        double s = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) s += (r[j].X * r[i].Y - r[i].X * r[j].Y);
        return s / 2;
    }

    public static double Area(IReadOnlyList<(double X, double Y)> r) => Math.Abs(SignedArea(r));

    public static Box Bbox(IReadOnlyList<(double X, double Y)> r) => Box.Of(r);

    public static Polygon ToPolygon(IReadOnlyList<(double X, double Y)> r) => Gf.CreatePolygon(Closed(r));

    /// <summary>점이 다각형 안인가(경계 위 = 안).</summary>
    public static bool Contains(IReadOnlyList<(double X, double Y)> r, double x, double y)
    {
        bool c = false;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
        {
            if (OnSeg(r[j], r[i], x, y, 1e-9)) return true;
            if ((r[i].Y > y) != (r[j].Y > y) && x < (r[j].X - r[i].X) * (y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X) c = !c;
        }
        return c;
    }

    static bool OnSeg((double X, double Y) a, (double X, double Y) b, double x, double y, double tol) => SegDist(a, b, x, y) <= tol;

    static double SegDist((double X, double Y) a, (double X, double Y) b, double x, double y)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        double t = l2 <= 0 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / l2, 0, 1);
        double px = a.X + dx * t - x, py = a.Y + dy * t - y;
        return Math.Sqrt(px * px + py * py);
    }

    /// <summary>빠른 «안인가» — 다각형을 <paramref name="shrink"/>만큼 줄인 꼴(0 = 그대로 · 음수 = 키움). 줄여서 없어지면 늘 거짓.</summary>
    public static Func<double, double, bool> InsideFn(IReadOnlyList<(double X, double Y)> r, double shrink = 0)
    {
        Geometry g = ToPolygon(r);
        if (shrink != 0) g = g.Buffer(-shrink);
        if (g.IsEmpty) return (_, _) => false;
        var loc = new IndexedPointInAreaLocator(g);
        var env = g.EnvelopeInternal;
        return (x, y) => x >= env.MinX && x <= env.MaxX && y >= env.MinY && y <= env.MaxY && loc.Locate(new Coordinate(x, y)) != Location.Exterior;
    }

    /// <summary>WKT «POLYGON((x y, …))» — 서버 질의 매개변수(문자열 이어 붙이기 없이 @wkt로만 넘김).</summary>
    public static string Wkt(IReadOnlyList<(double X, double Y)> r)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder("POLYGON((");
        for (int i = 0; i <= r.Count; i++)
        {
            var p = r[i % r.Count];
            if (i > 0) sb.Append(',');
            sb.Append(p.X.ToString("R", ci)).Append(' ').Append(p.Y.ToString("R", ci));
        }
        return sb.Append("))").ToString();
    }

    // ══════════════════════════════ 테두리 채움 ══════════════════════════════

    /// <summary>★[JACK 1122 FitToBox → 1126 다각형] 테두리 표본 — 변마다 n = ⌈길이/step⌉ 등분(시작점 포함 · 끝점은 다음 변) · 재보는 점 = 1 cm 안쪽.
    /// <paramref name="rectProbe"/> = 네모 길(옛 FitToBox 그대로 — 가운데 쪽으로 x · y 각각 1 cm) · 아니면 변 안쪽 법선(꼭짓점은 안에 드는 쪽).</summary>
    public static List<(double X, double Y, double Tx, double Ty)> BorderSamples(IReadOnlyList<(double X, double Y)> ring, double step, bool rectProbe)
    {
        var res = new List<(double, double, double, double)>();
        int m = ring.Count;
        if (m < 3) return res;
        var bb = Box.Of(ring);
        double cxm = (bb.X0 + bb.X1) / 2, cym = (bb.Y0 + bb.Y1) / 2;
        bool ccw = SignedArea(ring) > 0;
        for (int e = 0; e < m; e++)
        {
            var a = ring[e]; var b = ring[(e + 1) % m];
            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            // 이 변의 안쪽 법선(반시계면 왼쪽)
            double nx = len > 0 ? -(b.Y - a.Y) / len : 0, ny = len > 0 ? (b.X - a.X) / len : 0;
            if (!ccw) { nx = -nx; ny = -ny; }
            for (int k = 0; k < n; k++)
            {
                double t = (double)k / n, x = a.X + (b.X - a.X) * t, y = a.Y + (b.Y - a.Y) * t;
                double tx, ty;
                if (rectProbe) { tx = x + Math.Sign(cxm - x) * 0.01; ty = y + Math.Sign(cym - y) * 0.01; }
                else if (k > 0) { tx = x + nx * 0.01; ty = y + ny * 0.01; }
                else (tx, ty) = CornerProbe(ring, e, nx, ny, ccw);
                res.Add((x, y, tx, ty));
            }
        }
        return res;
    }

    /// <summary>꼭짓점 e의 1 cm 안쪽 점 — 두 변 법선 · 사잇각 이등분 중 다각형 안에 드는 첫 것.</summary>
    static (double, double) CornerProbe(IReadOnlyList<(double X, double Y)> ring, int e, double nx, double ny, bool ccw)
    {
        int m = ring.Count;
        var v = ring[e]; var nxt = ring[(e + 1) % m]; var prv = ring[(e - 1 + m) % m];
        double ux = nxt.X - v.X, uy = nxt.Y - v.Y, ul = Math.Sqrt(ux * ux + uy * uy);
        double wx = prv.X - v.X, wy = prv.Y - v.Y, wl = Math.Sqrt(wx * wx + wy * wy);
        var cand = new List<(double, double)> { (nx, ny) };
        if (ul > 0 && wl > 0)
        {
            double bx = ux / ul + wx / wl, by = uy / ul + wy / wl, bl = Math.Sqrt(bx * bx + by * by);
            if (bl > 1e-9) { cand.Add((bx / bl, by / bl)); cand.Add((-bx / bl, -by / bl)); }
            // 들어오는 변(prv → v = −w)의 안쪽 법선
            double s = ccw ? 1 : -1;
            cand.Add((s * wy / wl, -s * wx / wl));
        }
        foreach (var (dx, dy) in cand)
        {
            double tx = v.X + dx * 0.01, ty = v.Y + dy * 0.01;
            if (Contains(ring, tx, ty)) return (tx, ty);
        }
        return (v.X + nx * 0.01, v.Y + ny * 0.01);
    }

    /// <summary>★테두리 채움 — 표본 중 지표면이 없는 자리(<paramref name="has"/> 거짓)만 가까운 꼭짓점 높이(거리 가중 8점)로 점을 만든다. 돌려줌 = 채울 점 · 가까운 점이 없어 못 채운 수.
    /// 격자 · 고리 찾기는 옛 FitToBox와 같다(네모면 결과 같음).</summary>
    public static (List<Point3> Add, int Miss) BorderFill(IReadOnlyList<(double X, double Y)> ring, double step, bool rectProbe, IReadOnlyList<Point3> vs, Func<double, double, bool> has)
    {
        var add = new List<Point3>();
        int miss = 0;
        var bb = Box.Of(ring);
        double x0 = bb.X0, y0 = bb.Y0, x1 = bb.X1, y1 = bb.Y1;
        if (vs.Count < 3 || !(x1 > x0) || !(y1 > y0)) return (add, 0);
        double cell = Math.Max(5, Math.Sqrt((x1 - x0) * (y1 - y0) / Math.Max(1, vs.Count)) * 3);
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < vs.Count; i++)
        {
            var k = ((int)Math.Floor((vs[i].X - x0) / cell), (int)Math.Floor((vs[i].Y - y0) / cell));
            if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
            l.Add(i);
        }
        double Idw(double x, double y)
        {
            int cx = (int)Math.Floor((x - x0) / cell), cy = (int)Math.Floor((y - y0) / cell);
            var near = new List<(double D, double Z)>();
            int maxR = (int)Math.Ceiling(Math.Max(x1 - x0, y1 - y0) / cell) + 2, extra = -1;
            for (int r = 0; r <= maxR; r++)
            {
                for (int gx = cx - r; gx <= cx + r; gx++)
                    for (int gy = cy - r; gy <= cy + r; gy++)
                    {
                        if (Math.Abs(gx - cx) != r && Math.Abs(gy - cy) != r) continue;
                        if (grid.TryGetValue((gx, gy), out var l))
                            foreach (int i in l) near.Add((Math.Sqrt((vs[i].X - x) * (vs[i].X - x) + (vs[i].Y - y) * (vs[i].Y - y)), vs[i].Z));
                    }
                if (near.Count >= 8 && extra < 0) extra = r + 1;
                if (extra >= 0 && r >= extra) break;
            }
            if (near.Count == 0) return double.NaN;
            double sw = 0, sz = 0;
            foreach (var (d, z) in near.OrderBy(n => n.D).Take(8)) { double w = 1 / Math.Max(0.01, d * d); sw += w; sz += w * z; }
            return sz / sw;
        }
        foreach (var (x, y, tx, ty) in BorderSamples(ring, step, rectProbe))
        {
            if (has(tx, ty)) continue;
            double z = Idw(x, y);
            if (double.IsFinite(z)) add.Add(new Point3(x, y, z)); else miss++;
        }
        return (add, miss);
    }

    /// <summary>테두리 표본 간격 — 둘레/800을 1 ~ 10 m로(옛 FitToBox 규칙).</summary>
    public static double BorderStep(IReadOnlyList<(double X, double Y)> ring)
    {
        double per = 0;
        for (int i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i + 1) % ring.Count]; per += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); }
        return Math.Clamp(per / 800, 1, 10);
    }

    // ══════════════════════════════ 원지형 자르기 ══════════════════════════════

    /// <summary>★[JACK 1126 원지형 자르기 다각형] 다각형 밖 숨김 조각 — «E − 다각형»(고리 꼴)을 다각형 맨 왼쪽 · 맨 오른쪽 꼭짓점에서 수평으로 밖 테두리까지 그은 두 선으로 갈라
    /// <b>구멍 없는 두 조각</b>(Civil Hide 경계는 고리 하나 = 구멍을 못 품음). E는 다각형 범위 + <paramref name="pad"/>까지 넓힘(다각형이 E를 넘어도 늘 감쌈).</summary>
    public static List<List<(double X, double Y)>> HidePieces(IReadOnlyList<(double X, double Y)> ring, Box e, double pad = GroundCrop.Pad)
    {
        var res = new List<List<(double X, double Y)>>();
        if (ring.Count < 3) return res;
        var bb = Box.Of(ring);
        var E = e.Grow(pad).Union(bb.Grow(pad));   // 네모 길(Frame)처럼 면 범위 + 여유 · 다각형이 넘어도 감쌈
        int il = 0, ir = 0;
        for (int i = 1; i < ring.Count; i++)
        {
            if (ring[i].X < ring[il].X) il = i;
            if (ring[i].X > ring[ir].X) ir = i;
        }
        var lines = new List<Geometry>
        {
            Gf.CreateLineString(new[] { new Coordinate(E.X0, E.Y0), new Coordinate(E.X1, E.Y0), new Coordinate(E.X1, E.Y1), new Coordinate(E.X0, E.Y1), new Coordinate(E.X0, E.Y0) }),
            Gf.CreateLineString(Closed(ring)),
            Gf.CreateLineString(new[] { new Coordinate(E.X0, ring[il].Y), new Coordinate(ring[il].X, ring[il].Y) }),
            Gf.CreateLineString(new[] { new Coordinate(ring[ir].X, ring[ir].Y), new Coordinate(E.X1, ring[ir].Y) }),
        };
        var noded = UnaryUnionOp.Union(lines);
        var pz = new Polygonizer();
        pz.Add(noded);
        var inside = InsideFn(ring);
        foreach (var g in pz.GetPolygons())
        {
            if (g is not Polygon p || p.Area <= 1e-9) continue;
            var ip = p.InteriorPoint;
            if (inside(ip.X, ip.Y)) continue;
            var c = p.ExteriorRing.Coordinates;
            res.Add(c.Take(c.Length - 1).Select(q => (q.X, q.Y)).ToList());
        }
        return res;
    }

    /// <summary>되읽은 경계 꼭짓점이 기록한 조각과 같은 꼴인가 — 서로의 꼭짓점이 상대 꼭짓점 · 변 위(<paramref name="tol"/>) · 닫는 점 · 시작점 · 방향과 무관.</summary>
    public static bool SameRing(IReadOnlyList<(double X, double Y)> got, IReadOnlyList<(double X, double Y)> rec, double tol = GroundCrop.VertexTol)
    {
        if (got.Count < 3 || rec.Count < 3) return false;
        var bg = Box.Of(got); var br = Box.Of(rec);
        if (Math.Abs(bg.X0 - br.X0) > tol || Math.Abs(bg.X1 - br.X1) > tol || Math.Abs(bg.Y0 - br.Y0) > tol || Math.Abs(bg.Y1 - br.Y1) > tol) return false;
        bool OnRing(IReadOnlyList<(double X, double Y)> r, (double X, double Y) p)
        {
            for (int i = 0; i < r.Count; i++) if (SegDist(r[i], r[(i + 1) % r.Count], p.X, p.Y) <= tol) return true;
            return false;
        }
        bool NearVertex(IReadOnlyList<(double X, double Y)> r, (double X, double Y) p) => r.Any(q => Math.Abs(q.X - p.X) <= tol && Math.Abs(q.Y - p.Y) <= tol);
        return got.All(p => OnRing(rec, p)) && rec.All(p => NearVertex(got, p));
    }

    /// <summary>★설계 자리 검사(다각형) — 모든 항목이 «다각형 − 여유» 안(<c>Covers</c> — 오목한 꼴은 꼭짓점만이 아니라 선분 전체로 봄).
    /// 나간 항목 = 밖 조각(빨간 실선) · 나간 길이 · 가장 먼 거리. 필요한 꼴 = 설계 자리 볼록 껍질 + 여유(<see cref="GroundCrop.Cover.NeedRing"/>).</summary>
    public static GroundCrop.Cover Covers(IReadOnlyList<(double X, double Y)> ring, IReadOnlyList<GroundCrop.Item> items, double margin = GroundCrop.Margin)
    {
        var inner = ToPolygon(ring).Buffer(-margin);
        var prep = inner.IsEmpty ? null : PreparedGeometryFactory.Prepare(inner);
        var outs = new List<GroundCrop.Out>();
        var allPts = new List<Coordinate>();
        foreach (var it in items)
        {
            if (it.Pts.Count == 0) continue;
            var cs = it.Pts.Select(p => new Coordinate(p.X, p.Y)).ToList();
            allPts.AddRange(cs);
            if (it.Edges)
            {
                // ★[검토 3] 설계 면의 세모 변(두 점씩) — 점만 보면 오목한 곳을 가로지르는 변을 놓침 → 변 전체를 MultiLineString 하나로 prepared Covers(빠른 길) · 못 덮으면 밖 조각(아래)
                var segs = new List<LineString>();
                for (int i = 0; i + 1 < cs.Count; i += 2) if (!cs[i].Equals2D(cs[i + 1])) segs.Add(Gf.CreateLineString(new[] { cs[i], cs[i + 1] }));
                if (segs.Count == 0) continue;
                if (prep != null && prep.Covers(Gf.CreateMultiLineString(segs.ToArray()))) continue;
                var eparts = new List<List<(double X, double Y)>>();
                double efar = 0;
                // 못 덮인 변만 모아 Difference 한 번(면이 통째로 밖이어도 변마다 겹침 셈을 하지 않음)
                var bad = Gf.CreateMultiLineString(segs.Where(sg => prep == null || !prep.Covers(sg)).ToArray());
                var d = prep == null ? bad : bad.Difference(inner);
                for (int i = 0; i < d.NumGeometries; i++)
                {
                    var gc = d.GetGeometryN(i).Coordinates;
                    if (gc.Length == 0) continue;
                    eparts.Add(gc.Select(c => (c.X, c.Y)).ToList());
                    // 나간 거리 — 조각 끝은 경계 위라 0 · 조각 가운데 점도 잼
                    for (int k = 0; k < gc.Length; k++)
                    {
                        var q = k + 1 < gc.Length ? new Coordinate((gc[k].X + gc[k + 1].X) / 2, (gc[k].Y + gc[k + 1].Y) / 2) : gc[k];
                        efar = Math.Max(efar, prep == null ? double.PositiveInfinity : Math.Max(inner.Distance(Gf.CreatePoint(gc[k])), inner.Distance(Gf.CreatePoint(q))));
                    }
                }
                if (eparts.Count > 0) outs.Add(new GroundCrop.Out(it.Label, eparts.Sum(GroundCrop.Len), efar, eparts.Take(200).ToList()));
                continue;
            }
            if (it.Closed && cs.Count > 2 && !cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
            Geometry g = cs.Count == 1 || cs.All(c => c.Equals2D(cs[0])) ? Gf.CreatePoint(cs[0]) : Gf.CreateLineString(cs.ToArray());
            if (!inner.IsEmpty && inner.Covers(g)) continue;
            var parts = new List<List<(double X, double Y)>>();
            double far;
            if (inner.IsEmpty) { parts.Add(it.Pts.ToList()); far = double.PositiveInfinity; }
            else
            {
                var d = g.Difference(inner);
                far = 0;
                for (int i = 0; i < d.NumGeometries; i++)
                {
                    var gi = d.GetGeometryN(i);
                    var pl = gi.Coordinates.Select(c => (c.X, c.Y)).ToList();
                    if (pl.Count > 0) parts.Add(pl);
                    foreach (var c in gi.Coordinates) far = Math.Max(far, inner.Distance(Gf.CreatePoint(c)));
                }
                if (parts.Count == 0) continue;
            }
            outs.Add(new GroundCrop.Out(it.Label, parts.Sum(GroundCrop.Len), far, parts));
        }
        var need = GroundCrop.DesignBox(items).Grow(margin);
        List<(double X, double Y)>? needRing = null;
        if (allPts.Count > 0)
        {
            // ★[검토 8] 둥근 모서리는 안에 접한 꺾은선이라 +여유보다 안쪽(조각 4 = 최대 ~0.1 m) → 조각 8 · 0.2 m 더함(최악 처짐 ~0.06 m를 넘어 늘 +여유 밖)
            var hull = Gf.CreateMultiPointFromCoords(allPts.ToArray()).ConvexHull().Buffer(margin + 0.2, 8);
            if (Largest(hull) is Polygon hp) needRing = hp.ExteriorRing.Coordinates.Take(hp.ExteriorRing.NumPoints - 1).Select(c => (c.X, c.Y)).ToList();
        }
        return new GroundCrop.Cover(outs.Count == 0, need, outs) { NeedRing = needRing };
    }

    /// <summary>★[검토 1] 면이 다각형과 닿나 — 무게중심이 다각형 안인 보이는 세모가 하나라도 있나(외접 네모만 보면 다각형 밖 · 네모 안 면을 대상으로 잘못 삼음).</summary>
    public static bool AnyInside(IEnumerable<WallDaylight.Tri> tris, Func<double, double, bool> inside, IReadOnlyList<(double X, double Y)>? ring = null)
    {
        var list = tris as IList<WallDaylight.Tri> ?? tris.ToList();
        foreach (var t in list)
            if (inside((t.A.X + t.B.X + t.C.X) / 3, (t.A.Y + t.B.Y + t.C.Y) / 3)) return true;
        if (ring == null || ring.Count < 3) return false;
        // ★[재검토 · 낮음] 경계에 걸친 큰 세모 — 무게중심은 밖이어도 다각형과 넓이로 겹치면 닿은 것(점 · 변만 맞닿음은 안 닿음 — 숨기면 세모 0이 되므로)
        var poly = ToPolygon(ring);
        var env = poly.EnvelopeInternal;
        var prep = PreparedGeometryFactory.Prepare(poly);
        foreach (var t in list)
        {
            double x0 = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), x1 = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
            double y0 = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), y1 = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
            if (x1 <= env.MinX || x0 >= env.MaxX || y1 <= env.MinY || y0 >= env.MaxY) continue;
            var tri = Gf.CreatePolygon(new[] { new Coordinate(t.A.X, t.A.Y), new Coordinate(t.B.X, t.B.Y), new Coordinate(t.C.X, t.C.Y), new Coordinate(t.A.X, t.A.Y) });
            if (!tri.IsValid || tri.Area < 1e-6 || !prep.Intersects(tri)) continue;
            try { if (poly.Intersection(tri).Area > 1e-4) return true; }
            catch (TopologyException) { if (RoadSmooth.Robust(poly, tri, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection).Area > 1e-4) return true; }
        }
        return false;
    }

    /// <summary>자를 면인가(다각형 길) — 보이는 세모 꼭짓점 하나라도 다각형(+ 끝) 밖.</summary>
    public static bool NeedsCrop(IEnumerable<WallDaylight.Tri> tris, Func<double, double, bool> insideGrown)
    {
        foreach (var t in tris)
            if (!insideGrown(t.A.X, t.A.Y) || !insideGrown(t.B.X, t.B.Y) || !insideGrown(t.C.X, t.C.Y)) return true;
        return false;
    }

    // ══════════════════════════════ 시험 고리 ══════════════════════════════

    /// <summary>등고선(표고 · 계곡선 · 점렬)을 다각형으로 자름 — 서버 질의(ST_Intersection)와 같은 일을 손으로(시험 고리 · 서버 없을 때).</summary>
    public static List<(double Elev, bool Index, List<(double X, double Y)> Pts)> ClipLines(IEnumerable<(double Elev, bool Index, IReadOnlyList<(double X, double Y)> Pts)> lines, IReadOnlyList<(double X, double Y)> ring)
    {
        var res = new List<(double, bool, List<(double X, double Y)>)>();
        var poly = ToPolygon(ring);
        foreach (var (z, idx, pts) in lines)
        {
            if (pts.Count < 2) continue;
            var ls = Gf.CreateLineString(pts.Select(p => new Coordinate(p.X, p.Y)).ToArray());
            if (!ls.EnvelopeInternal.Intersects(poly.EnvelopeInternal)) continue;
            var cut = ls.Intersection(poly);
            for (int i = 0; i < cut.NumGeometries; i++)
                if (cut.GetGeometryN(i) is LineString l && l.NumPoints >= 2) res.Add((z, idx, l.Coordinates.Select(c => (c.X, c.Y)).ToList()));
        }
        return res;
    }
}
