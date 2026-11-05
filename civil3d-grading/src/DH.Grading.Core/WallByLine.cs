using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Polygonize;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v114.0 · JACK 1105 «옹벽 추가 새 개념 — 선에서 바깥으로 수평 계단»] <b>선 옹벽</b>(옹벽 목록 종류 2)의 기하 — 계획 `2026-11-05_옹벽추가_새개념.md` 1장 + critic 2차 검토.
/// <code>
///   바닥 ─── 옛 사면(그대로) ───L━━━━━━━━━━━━━━━━━━━  ← 그린 선(경계 A → 안 → 경계 B)
///                               │▒ 성토: 선에서 H씩 내려가며 소단 T가 바깥으로 · 땅에 닿으면 멈춤
///                               │▒ 절토: 선에서 H씩 올라가며 소단 T가 바깥으로 · 땅에 닿으면 멈춤
///                               └── F(새 데이라잇) = 벽 = 원지반인 등치선(L에 붙은 성분의 바깥 테두리)
/// </code>
/// <para>① 선 위 δ = P0 − G 부호로 절토 · 성토 부분을 가른다(가름점 C). ② 부분마다 <b>수평 높이</b> Z_k = 성토 Ztop − k·H · 절토 Zbot + k·H.
/// ③ 선 위 점마다 첫 높이 j(«P0보다 낮은(성토) · 높은(절토) 첫 Z_k») — 같은 j의 토막이 줄기 · 줄기 갈림 S에는 가로 마구리(폭 vEps).
/// 면 폭 f보다 짧은 줄기는 첫 면 0 &lt; h ≤ H + 1 cm 안에서 이웃과 합친다(critic 2차 5).
/// ④ 줄 = 선에서 거리 0 · f · f+T · 2f+T …(줄마다 높이 하나) · 부분 끝과 바깥 끝은 vEps 띠로 원지반에 내린다.
/// ⑤ <b>바꿀 자리 Zone = 벽이 원지반 위(성토) · 아래(절토)인 자리 가운데 L에 붙은 이어진 성분</b>(critic 2차 1 — 광선의 «처음 닿음» 아님). 테두리는 늘 W = G라 이어진다.</para>
/// <para>계산은 «절토 꼴»로 한다 — 성토는 z′ = −z로 뒤집어 같은 셈(절토 ×1 · 성토 ×−1은 정확).</para></summary>
public static class WallByLine
{
    /// <summary>★[v114.0 · 계획 2절 «기능 스위치»] 켜면 [옹벽 추가]의 선 길이 숨고 종류 2 기록은 쉰다(지우지 않음 · ⚠) — Civil <c>GradingSettings.WallLineOff</c>가 옮겨 준다.</summary>
    public static bool Off;
    /// <summary>★[v114.0 · 소단 높이 관문] 하네스 전용 — 참이면 관문이 재기만 하고 거절하지 않는다(«예전엔 조용히 지었을» 꼴을 보이려고 · 출하 DLL 밖에서 못 건드리게 internal).</summary>
    internal static bool BenchGateOff;

    public sealed class Input
    {
        /// <summary>BoundaryCut 결과의 A..B 선 · 경계 · 새 경계 N · 잘라낼 자리 R_out.</summary>
        public IReadOnlyList<Point3> Line = Array.Empty<Point3>(), Bd = Array.Empty<Point3>(), NewRing = Array.Empty<Point3>(), CutRing = Array.Empty<Point3>();
        /// <summary>계획선(부지 바닥 고리) — 판정 표시용(기하는 안 쓴다).</summary>
        public IReadOnlyList<Point3> Plan = Array.Empty<Point3>();
        public double Hc = 5, Tc = 1, Hf = 5, Tf = 1, SlopeW = 0.01, FaceRun = 0.05, Grid = 1;
        /// <summary>선에서 벽이 갈 수 있는 거리 한도 여유(m) — 한도 = R_out 가장 먼 점까지 + 이 값.</summary>
        public double Reach = 10;
        public Func<double, double, double?> Ground = (_, _) => null;
        /// <summary>원지반 삼각형(있으면 F를 그 변 건넘까지 정확히) — 없으면 함수로.</summary>
        public IReadOnlyList<Tri>? GroundTris;
        public Func<double, double, double?> P0 = (_, _) => null;
        public IReadOnlyList<Tri>? P0Tris;
        public IEnumerable<(string Name, IReadOnlyList<Point3> Ring)>? Blockers;
    }

    /// <summary>줄기 — 선 위 [S0, S1](호 길이) · 첫 높이 번호 J(1부터).</summary>
    public sealed class Stem { public double S0, S1; public int J; public int Tiers; }

    /// <summary>부분(절토 · 성토 한쪽) — 선 위 [S0, S1] · 단높이 · 소단 · 기준 높이(성토 Ztop · 절토 Zbot) · 줄기.</summary>
    public sealed class Part
    {
        public bool Up; public double S0, S1, H, T, Zref, DeltaMax, Dmax, FootMax;
        public (double X, double Y) FootAt;
        public List<Stem> Stems = new();
        public int Tiers => Stems.Count == 0 ? 0 : Stems.Max(s => s.Tiers);
        /// <summary>k번 수평 높이(실제 z).</summary>
        public double Level(int k) => Up ? Zref + k * H : Zref - k * H;
    }

    /// <summary>★[v114.0 · 계획 2절 Faces()] 벽 면 한 토막 — 소비처(표지 · 길이 · 단면)는 이것만 본다(종류 2에는 하나뿐인 Up · HeadZ · Tiers가 없다).</summary>
    public sealed class FaceSeg
    {
        /// <summary>절토면이면 참(토막마다).</summary>
        public bool Up;
        /// <summary>단(줄기 안 1..) · 수평 높이 Z_k.</summary>
        public int Tier; public double Level;
        /// <summary>0 = 앞면 · 1 = 가로 마구리.</summary>
        public int Kind;
        public int Part;
        public double Len, HMax;
        /// <summary>벽 머리(첫 면이면 L 위 P0) · 점마다 발(성토 max(Level, G) · 절토 min(Level, G)).</summary>
        public List<Point3> Top = new(), Foot = new();
    }

    public sealed class Built
    {
        public bool Ok; public string Code = "", Why = "";
        public List<(double X, double Y)> Marks = new();
        public Point3 A, B;
        public double Face, VEps, LineLen;
        public List<Part> Parts = new();
        /// <summary>가름점 C(0) · 줄기 갈림 S(1) — 미리보기 점.</summary>
        public List<(double X, double Y, int Kind)> Points = new();
        /// <summary>줄(바꿀 자리 안 · 높이) — 터파기 · 미리보기.</summary>
        public List<List<Point3>> Rows = new();
        /// <summary>자르기 전 줄(미리보기 옅게).</summary>
        public List<List<Point3>> RawRows = new();
        /// <summary>마구리 선(바꿀 자리 안).</summary>
        public List<List<Point3>> EndFaces = new();
        /// <summary>바꿀 자리(성토 · 절토) 바깥 고리 — 첫 점 반복 없음 · z = 그 자리 벽(L 위 P0 · F 위 G).</summary>
        public List<List<Point3>> ZoneF = new(), ZoneC = new();
        /// <summary>F(새 데이라잇) — 바꿀 자리 테두리에서 L을 뺀 꺾은선(z = G).</summary>
        public List<List<Point3>> Foot = new();
        /// <summary>목록 Poly = Zone ∪ R_out(첫 점 반복 없음 · z = 0) · 점마다 그 변이 L 위인가.</summary>
        public List<Point3> Poly = new(); public List<bool> IsWall = new();
        /// <summary>Civil 가상옹벽 브레이크라인 — [0] = Poly 테두리(닫힘) · 바꿀 자리 고리(닫힘) · 줄 · 마구리.</summary>
        public List<List<Point3>> Lines = new();
        /// <summary>하네스 · 합성용 벽 삼각형(바꿀 자리 안만 — Civil이 같은 선으로 짓는 면).</summary>
        public List<Tri> WallTrisF = new(), WallTrisC = new();
        /// <summary>원 벽 삼각형(자르기 전 · 진단 · 하네스).</summary>
        public List<Tri> RawTris = new();
        public double FootMax; public (double X, double Y) FootAt;
        /// <summary>계수 — 발 폭을 1 mm로 올린 점 · 0.1 mm 안이라 L에 합친 점 · 0.1 mm 안 점 쌍(0이어야) · 바늘 세모(0이어야) · 마구리 · 부분 끝 마구리 · 한도에 닿음.</summary>
        public int WMinBumped, MergedPts, PairsUnder01, Needles, EndFaceN, EndCliff, EdgeHit, Corners;
        /// <summary>★[v114.0 · JACK 1105 소단 높이 관문] 표본 — Kind 0 = 소단(단마다 소단 폭 가운데 ± 0.5 m · 0.5 m 간격 · Z = Z_k) · 1 = F(0.5 m 간격 · Z = G).
        /// 제 바꿀 자리 밖 · 땅이 이기는 자리(절토 꼴 G′ − Z′_k ≤ 1 cm)는 안 넣는다. 합성 뒤 관문(<see cref="BenchGate"/>)도 이것을 쓴다.</summary>
        public List<(double X, double Y, double Z, int Kind)> BenchPts = new();
        /// <summary>★[v114.0 · 소단 높이 관문] 계수(까닭별) — 소단 표본 · 소단 높이 틀림(1 cm 넘음) · 소단에 벽 면 없음 · F 표본 · F 턱(1 cm 넘음) · F 못 읽음 · 가장 큰 차와 그 자리.</summary>
        public int BenchN, BenchSag, BenchMiss, FootN, FootBad, FootMiss; public double BenchMax; public (double X, double Y) BenchAt;
        public List<FaceSeg> Faces = new();
        public bool HasCut => ZoneC.Count > 0; public bool HasFill => ZoneF.Count > 0;
        public List<string> Warn = new();
        public string Log = "";
        /// <summary>표시용 최댓값 — 기하를 정하지 않는다.</summary>
        public double HMax => Faces.Count == 0 ? 0 : Faces.Max(f => f.HMax);
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    // ══ 공통 도우미(옛 판에서 그대로) ══

    /// <summary>닫힌 고리 위 가장 가까운 점(높이는 그 변에서 잇는다).</summary>
    public static (double X, double Y, double Z, int J, double T, double D) Nearest(IReadOnlyList<Point3> ring, double x, double y)
    {
        int n = ring.Count; if (n >= 2 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y) n--;
        double bd = double.MaxValue; (double, double, double, int, double, double) best = (x, y, 0, 0, 0, double.MaxValue);
        for (int j = 0; j < n; j++)
        {
            var a = ring[j]; var b = ring[(j + 1) % n];
            double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
            double px = a.X + ex * t, py = a.Y + ey * t, d2 = (x - px) * (x - px) + (y - py) * (y - py);
            if (d2 < bd) { bd = d2; best = (px, py, a.Z + (b.Z - a.Z) * t, j, t, Math.Sqrt(d2)); }
        }
        return best;
    }

    /// <summary>선분 a→b 위 면 높이 — 삼각형 변을 건너는 자리마다 점(정확한 꺾은선). 못 읽은 점이 있으면 null.</summary>
    /// <param name="edgeFb">끝 점이 삼각망 바깥 µm에 떨어져 못 읽을 때 — 선분 안으로 1 mm 민 자리 → 그래도 못 읽으면 이 값.</param>
    public static List<Point3>? ProfileOnTris(Point3 a, Point3 b, IReadOnlyList<Tri>? tris, Func<double, double, double?> f, double grid,
        Func<double, double, double?>? edgeFb = null)
    {
        var ts = new List<double> { 0, 1 };
        double dx = b.X - a.X, dy = b.Y - a.Y, L = Math.Sqrt(dx * dx + dy * dy);
        if (tris != null && tris.Count > 0)
        {
            double mnx = Math.Min(a.X, b.X), mxx = Math.Max(a.X, b.X), mny = Math.Min(a.Y, b.Y), mxy = Math.Max(a.Y, b.Y);
            foreach (var t in tris)
            {
                if (Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) < mnx || Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) > mxx
                 || Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) < mny || Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) > mxy) continue;
                foreach (var (p, q) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                {
                    double ex = q.X - p.X, ey = q.Y - p.Y, den = dx * ey - dy * ex;
                    if (Math.Abs(den) < 1e-15) continue;
                    double s = ((p.X - a.X) * ey - (p.Y - a.Y) * ex) / den, u = ((p.X - a.X) * dy - (p.Y - a.Y) * dx) / den;
                    if (s > 1e-9 && s < 1 - 1e-9 && u >= -1e-12 && u <= 1 + 1e-12) ts.Add(s);
                }
            }
        }
        else if (L > grid) { int k = (int)Math.Ceiling(L / Math.Max(0.5, grid)); for (int i = 1; i < k; i++) ts.Add((double)i / k); }
        ts.Sort();
        var r = new List<Point3>();
        double last = -1;
        foreach (double s in ts)
        {
            if (s - last < 1e-9 / Math.Max(1, L)) continue;
            last = s;
            double x = a.X + dx * s, y = a.Y + dy * s;
            double? Read(double px, double py) => tris != null && tris.Count > 0 ? ZOnTris(tris, px, py) ?? f(px, py) : f(px, py);
            double? z = Read(x, y);
            if (z == null && L > 1e-6)
            {
                double nd = Math.Min(1e-3, 0.01 * L) * (s < 0.5 ? 1 : -1) / L;
                var zn = Read(x + dx * nd, y + dy * nd);
                if (zn != null) z = zn;
            }
            if (z == null && edgeFb != null) z = edgeFb(x, y);
            if (z == null) return null;
            r.Add(new Point3(x, y, z.Value));
        }
        return r;
    }

    internal static double? ZOnTris(IReadOnlyList<Tri> tris, double x, double y)
    {
        foreach (var t in tris)
        {
            double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
            if (Math.Abs(d) < 1e-18) continue;
            double l1 = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d;
            double l2 = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
            double l3 = 1 - l1 - l2;
            if (l1 >= -1e-9 && l2 >= -1e-9 && l3 >= -1e-9) return l1 * t.A.Z + l2 * t.B.Z + l3 * t.C.Z;
        }
        return null;
    }

    /// <summary>★[v114.0 · 소단 높이 관문] 삼각형 높이 읽기(STR 나무 — 표본 수천 × 삼각형 수만이어도 빠르게).</summary>
    internal static Func<double, double, double?> TriLookup(IReadOnlyList<Tri> tris)
    {
        var idx = new STRtree<Tri>();
        foreach (var t in tris)
            idx.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))), t);
        if (tris.Count > 0) idx.Build();
        return (x, y) => tris.Count == 0 ? null : ZOnTris(idx.Query(new Envelope(x - 1e-7, x + 1e-7, y - 1e-7, y + 1e-7)).ToList(), x, y);
    }

    /// <summary>★★[v114.0 · JACK 1105 소단 높이 관문] 지은 면(합성 · Civil이 지은 가상옹벽 면)에서 <see cref="Built.BenchPts"/>를 잰다 — 소단 = Z_k · F = G(1 cm).
    /// 거절 까닭 한 줄(빈 값이면 통과) · 까닭별 계수 · 가장 큰 차와 그 자리. 면이 없는 표본(합성 폴리곤 밖 등)은 «못 읽음»으로만 센다.</summary>
    public static (string Why, int BenchN, int BenchBad, int FootN, int FootBad, int Miss, double Max, double X, double Y) BenchGate(IReadOnlyList<(double X, double Y, double Z, int Kind)> pts, IReadOnlyList<Tri> surf)
    {
        var at = TriLookup(surf);
        int bn = 0, bb = 0, fn = 0, fb = 0, miss = 0, atKind = 0; double mx = 0, ax = 0, ay = 0;
        foreach (var (x, y, z0, kind) in pts)
        {
            if (kind == 0) bn++; else fn++;
            var z = at(x, y);
            if (z == null) { miss++; continue; }
            double dz = Math.Abs(z.Value - z0);
            if (dz > 1e-2) { if (kind == 0) bb++; else fb++; }
            if (dz > mx) { mx = dz; ax = x; ay = y; atKind = kind; }
        }
        string why = bb + fb == 0 ? "" : string.Format(Ci, "옹벽 {0} 높이가 맞지 않음({1:F1}, {2:F1} · 차 {3:F0} cm) — 소단 폭을 줄이거나 선을 펴세요", atKind == 0 ? "소단" : "발(F)", ax, ay, mx * 100);
        return (why, bn, bb, fn, fb, miss, mx, ax, ay);
    }

    /// <summary>★[v114.0 · 소단 높이 관문] 목록 칸(<see cref="WallRec.BenchSamples"/> · <see cref="WallRec.FootSamples"/>)으로 옮길 표본.</summary>
    public static (List<Point3> Bench, List<Point3> Foot) SamplesOf(Built b)
        => (b.BenchPts.Where(q => q.Kind == 0).Select(q => new Point3(q.X, q.Y, q.Z)).ToList(), b.BenchPts.Where(q => q.Kind == 1).Select(q => new Point3(q.X, q.Y, q.Z)).ToList());

    /// <summary>★[v114.0 · 설치 전 검토 4] 목록 칸의 표본으로 — 합성 되읽기(Civil이 지은 면)에서 부른다.</summary>
    public static (string Why, int BenchN, int BenchBad, int FootN, int FootBad, int Miss, double Max, double X, double Y) BenchGate(WallRec w, IReadOnlyList<Tri> surf)
        => BenchGate(w.BenchSamples.Select(q => (q.X, q.Y, q.Z, 0)).Concat(w.FootSamples.Select(q => (q.X, q.Y, q.Z, 1))).ToList(), surf);

    static Polygon? BoundaryCutPoly(GeometryFactory gf, IReadOnlyList<Point3> ring, double ox, double oy)
    {
        if (ring == null || ring.Count < 3) return null;
        var cs = new List<Coordinate>();
        foreach (var q in ring) { var c = new Coordinate(q.X - ox, q.Y - oy); if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c); }
        if (cs.Count >= 2 && cs[0].Equals2D(cs[^1])) cs.RemoveAt(cs.Count - 1);
        if (cs.Count < 3) return null;
        cs.Add(cs[0].Copy());
        try { Geometry g = gf.CreatePolygon(cs.ToArray()); if (!g.IsValid) g = g.Buffer(0); return NtsSupport.LargestPolygon(g); } catch { return null; }
    }

    // ══ 선(지역 좌표) ══
    sealed class PL
    {
        public readonly Coordinate[] C; public readonly double[] Cum; public readonly double Len;
        public PL(Coordinate[] c)
        {
            C = c; Cum = new double[c.Length];
            for (int i = 1; i < c.Length; i++) Cum[i] = Cum[i - 1] + c[i - 1].Distance(c[i]);
            Len = Cum[^1];
        }
        int Seg(double s) { int k = Array.BinarySearch(Cum, s); if (k < 0) k = ~k - 1; return Math.Clamp(k, 0, C.Length - 2); }
        public Coordinate At(double s)
        {
            s = Math.Clamp(s, 0, Len);
            int k = Seg(s); double l = Cum[k + 1] - Cum[k], t = l < 1e-15 ? 0 : (s - Cum[k]) / l;
            return new Coordinate(C[k].X + (C[k + 1].X - C[k].X) * t, C[k].Y + (C[k + 1].Y - C[k].Y) * t);
        }
        public (double X, double Y) Tan(double s)
        {
            int k = Seg(Math.Clamp(s, 0, Len));
            if (s >= Len - 1e-12) k = C.Length - 2;
            double dx = C[k + 1].X - C[k].X, dy = C[k + 1].Y - C[k].Y, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-15 ? (1, 0) : (dx / l, dy / l);
        }
        /// <summary>가장 가까운 점의 호 길이 · 거리 · 쪽(왼쪽 +).</summary>
        public (double S, double D, double Side) Near(double x, double y)
        {
            double bd = double.MaxValue, bs = 0, side = 0; int bi = 0; double bt = 0;
            for (int i = 0; i + 1 < C.Length; i++)
            {
                double ex = C[i + 1].X - C[i].X, ey = C[i + 1].Y - C[i].Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - C[i].X) * ex + (y - C[i].Y) * ey) / l2, 0, 1);
                double px = C[i].X + ex * t, py = C[i].Y + ey * t, d = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                if (d < bd) { bd = d; bs = Cum[i] + t * Math.Sqrt(l2); side = ex * (y - C[i].Y) - ey * (x - C[i].X); bi = i; bt = t; }
            }
            // ★ 가장 가까운 점이 안쪽 꼭짓점이면 쪽은 두 변 왼쪽 법선의 합으로 가린다 — 한 변의 연장선으로 가리면 볼록 쐐기 점이 반대쪽으로 갈린다
            int vi = bt <= 0 && bi > 0 ? bi : bt >= 1 && bi + 2 < C.Length ? bi + 1 : -1;
            if (vi > 0)
            {
                double ax = C[vi].X - C[vi - 1].X, ay = C[vi].Y - C[vi - 1].Y, bx = C[vi + 1].X - C[vi].X, by = C[vi + 1].Y - C[vi].Y;
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la > 1e-15 && lb > 1e-15)
                {
                    double nx = -ay / la - by / lb, ny = ax / la + bx / lb;
                    double sd = (x - C[vi].X) * nx + (y - C[vi].Y) * ny;
                    if (Math.Abs(sd) > 0) side = sd;
                }
            }
            return (bs, bd, Math.Sign(side));
        }
        public Coordinate[] Sub(double a, double b)
        {
            var r = new List<Coordinate> { At(a) };
            for (int i = 0; i < C.Length; i++) if (Cum[i] > a + 1e-9 && Cum[i] < b - 1e-9) r.Add(C[i]);
            var e = At(b); if (!r[^1].Equals2D(e)) r.Add(e);
            return r.ToArray();
        }
    }

    /// <summary>삼각형 묶음을 선 길로 잇는다 — 선(높이 실은)으로 가른 조각마다 제약 들로네 · 높이는 선 위 값(없으면 가장 가까운 선분에서).</summary>
    /// <summary>★[v114.0] 계측 — 들로네가 터져 고쳐 다시 한 조각 수 · 그래도 잃은 조각 수(누적).</summary>
    [ThreadStatic] internal static int LastMeshFail, LastMeshLost;
    internal static List<Tri> MeshLines(IReadOnlyList<List<Point3>> lines, double ox, double oy)
    {
        var res = new List<Tri>();
        if (lines.Count == 0) return res;
        var gf = new GeometryFactory();
        var zmap = new Dictionary<(long, long), double>();
        (long, long) K(double x, double y) => ((long)Math.Round((x - ox) * 1e6), (long)Math.Round((y - oy) * 1e6));
        var segs = new List<(Point3 A, Point3 B)>();
        var geoms = new List<Geometry>();
        // 0.1 µm 안 꼭짓점은 하나로 붙인다(교차 계산 부스러기 — 납작 세모가 생기지 않게 · 먼저 넣은 선의 점 = 높이가 이긴다는 차례는 아래 zmap이 정한다)
        var weld = new Dictionary<(long, long), Point3>();
        Point3 W(Point3 q)
        {
            long kx = (long)Math.Floor((q.X - ox) / 1e-7), ky = (long)Math.Floor((q.Y - oy) / 1e-7);
            for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++)
                    if (weld.TryGetValue((kx + a, ky + b), out var p) && Math.Abs(p.X - q.X) <= 1e-7 && Math.Abs(p.Y - q.Y) <= 1e-7) return new Point3(p.X, p.Y, q.Z);
            weld[(kx, ky)] = q; return q;
        }
        lines = lines.Select(l => { var o = new List<Point3>(); foreach (var q in l) { var w = W(q); if (o.Count == 0 || o[^1].X != w.X || o[^1].Y != w.Y) o.Add(w); } return o; }).ToList();
        foreach (var l in lines)
        {
            if (l.Count < 2) continue;
            foreach (var q in l) zmap[K(q.X, q.Y)] = q.Z;
            for (int i = 0; i + 1 < l.Count; i++) segs.Add((l[i], l[i + 1]));
            var cs = new List<Coordinate>();
            foreach (var q in l) { var c = new Coordinate(q.X - ox, q.Y - oy); if (cs.Count == 0 || !cs[^1].Equals2D(c)) cs.Add(c); }
            if (cs.Count >= 2) geoms.Add(gf.CreateLineString(cs.ToArray()));
        }
        var tree = new STRtree<int>();
        for (int i = 0; i < segs.Count; i++)
        {
            var (a, b) = segs[i];
            tree.Insert(new Envelope(Math.Min(a.X, b.X) - ox, Math.Max(a.X, b.X) - ox, Math.Min(a.Y, b.Y) - oy, Math.Max(a.Y, b.Y) - oy), i);
        }
        tree.Build();
        double ZOf(Coordinate c)
        {
            double x = c.X + ox, y = c.Y + oy;
            if (zmap.TryGetValue(K(x, y), out double z)) return z;
            double bd = double.MaxValue, bz = double.NaN;
            for (double r = 1e-6; r < 1e4 && double.IsNaN(bz); r *= 100)
                foreach (int i in tree.Query(new Envelope(c.X - r, c.X + r, c.Y - r, c.Y + r)))
                {
                    var (a, e) = segs[i];
                    double ex = e.X - a.X, ey = e.Y - a.Y, l2 = ex * ex + ey * ey;
                    double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
                    double dx = x - (a.X + ex * t), dy = y - (a.Y + ey * t), d2 = dx * dx + dy * dy;
                    if (d2 < bd) { bd = d2; bz = a.Z + (e.Z - a.Z) * t; }
                }
            return bz;
        }
        var noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(geoms.ToArray()), new PrecisionModel());
        var pz = new Polygonizer(); pz.Add(noded);
        foreach (var g in pz.GetPolygons())
        {
            if (g is not Polygon f || f.Area <= 1e-14) continue;
            Geometry trs;
            try { trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); }
            catch
            {
                // ★[v114.0 · S198 넓은 소단 계측] 한 점에서 닿는 고리 등으로 들로네가 터지면 그 조각을 통째로 잃어 원 면에 구멍이 났다 — 고쳐(Buffer 0) 조각마다 다시
                LastMeshFail++;
                try
                {
                    var fx = f.Buffer(0); var all = new List<Geometry>();
                    for (int pi = 0; pi < fx.NumGeometries; pi++) if (fx.GetGeometryN(pi) is Polygon pp && pp.Area > 1e-14) all.Add(NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(pp));
                    trs = gf.BuildGeometry(all);
                }
                catch { LastMeshLost++; continue; }
            }
            for (int q = 0; q < trs.NumGeometries; q++)
            {
                if (trs.GetGeometryN(q) is not Polygon tq || tq.Area <= 0) continue;
                var c = tq.ExteriorRing.Coordinates;
                Point3 P(Coordinate k) => new(k.X + ox, k.Y + oy, ZOf(k));
                res.Add(new Tri(P(c[0]), P(c[1]), P(c[2])));
            }
        }
        return res;
    }

    // ══ 짓기 ══

    /// <summary>★[v114.0] 기하를 짓는다 — 못 지으면 Ok = 거짓 · 까닭(코드 · 글 · 자리). 아무것도 안 바꾼다(순수 셈).</summary>
    public static Built Build(Input inp)
    {
        var R = new Built();
        var log = new System.Text.StringBuilder();
        Built No(string code, string why, params (double, double)[] at) { R.Ok = false; R.Code = code; R.Why = why; R.Marks.AddRange(at); R.Log = log.ToString(); return R; }
        var L = inp.Line;
        if (L == null || L.Count < 2) return No("재료", "선이 없습니다");
        R.A = L[0]; R.B = L[^1];
        double ox = R.A.X, oy = R.A.Y;
        var gf = new GeometryFactory();
        (double, double) Wd(Coordinate c) => (c.X + ox, c.Y + oy);
        var lc = new List<Coordinate>();
        foreach (var q in L) { var c = new Coordinate(q.X - ox, q.Y - oy); if (lc.Count == 0 || !lc[^1].Equals2D(c)) lc.Add(c); }
        if (lc.Count < 2) return No("재료", "선 점이 모자랍니다");
        var pl = new PL(lc.ToArray());
        R.LineLen = pl.Len;
        var lineG = gf.CreateLineString(pl.C);
        double vEps = Math.Max(1e-3, inp.FaceRun);
        R.VEps = vEps;
        double? Gw(double lx, double ly) => inp.Ground(lx + ox, ly + oy);

        // ① 선 위 P0(삼각형 변 건넘까지) · 표본 · δ = P0 − G
        var prof = new List<(double S, double Z)>();
        for (int i = 0; i + 1 < pl.C.Length; i++)
        {
            var a = new Point3(pl.C[i].X + ox, pl.C[i].Y + oy, 0); var b = new Point3(pl.C[i + 1].X + ox, pl.C[i + 1].Y + oy, 0);
            var pr = ProfileOnTris(a, b, inp.P0Tris, inp.P0, inp.Grid, inp.Ground);
            if (pr == null) return No("정지면", string.Format(Ci, "선 위 합성 전 면(정지면) 높이를 못 읽었습니다 ({0:F1}, {1:F1})", a.X, a.Y), (a.X, a.Y));
            double segL = pl.Cum[i + 1] - pl.Cum[i];
            foreach (var q in pr)
            {
                double t = segL < 1e-15 ? 0 : Math.Sqrt((q.X - a.X) * (q.X - a.X) + (q.Y - a.Y) * (q.Y - a.Y)) / segL;
                double s = pl.Cum[i] + t * segL;
                if (prof.Count == 0 || s - prof[^1].S > 1e-9) prof.Add((s, q.Z)); else prof[^1] = (prof[^1].S, q.Z);
            }
        }
        double P0At(double s)
        {
            if (s <= prof[0].S) return prof[0].Z;
            if (s >= prof[^1].S) return prof[^1].Z;
            int lo = 0, hi = prof.Count - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (prof[m].S <= s) lo = m; else hi = m; }
            double t = (s - prof[lo].S) / Math.Max(1e-15, prof[hi].S - prof[lo].S);
            return prof[lo].Z + (prof[hi].Z - prof[lo].Z) * t;
        }
        var ss = new List<double>(prof.Select(p => p.S));
        double ds = Math.Min(0.25, Math.Max(0.05, inp.Grid / 4));
        for (double s = 0; s < pl.Len; s += ds) ss.Add(s);
        ss.Add(pl.Len); ss.Sort();
        var smp = new List<(double S, double P, double G, double D)>();
        foreach (double s in ss)
        {
            if (smp.Count > 0 && s - smp[^1].S < 1e-9) continue;
            var c = pl.At(s);
            var g = Gw(c.X, c.Y);
            if (g == null) return No("원지반", string.Format(Ci, "선 위 원지반을 못 읽었습니다 ({0:F1}, {1:F1}) — 측량 범위 밖?", c.X + ox, c.Y + oy), Wd(c));
            double p = P0At(s);
            smp.Add((s, p, g.Value, p - g.Value));
        }
        double Delta(double s) { var c = pl.At(s); var g = Gw(c.X, c.Y) ?? double.NaN; return P0At(s) - g; }
        const double Eps0 = 1e-4;   // 0.1 mm — 이보다 작은 δ는 벽을 안 세운다(계획 1-6)
        int Sg(double d) => d > Eps0 ? 1 : d < -Eps0 ? -1 : 0;
        double Cross(double a, double b)
        {
            double da = Delta(a);
            for (int it = 0; it < 60 && b - a > 1e-5; it++) { double m = (a + b) / 2, dm = Delta(m); if (Math.Sign(dm) == Math.Sign(da) && dm != 0) { a = m; da = dm; } else b = m; }
            return (a + b) / 2;
        }
        // 부분 — 같은 부호(0.1 mm 넘게)의 이어진 토막 · 끝은 부호가 바뀌는 자리(이분법 0.01 mm) · 0 표본이면 그 자리
        var parts = new List<Part>();
        {
            int i = 0;
            while (i < smp.Count)
            {
                int sg = Sg(smp[i].D);
                if (sg == 0) { i++; continue; }
                int j = i;
                while (j + 1 < smp.Count && Sg(smp[j + 1].D) == sg) j++;
                double s0 = i == 0 ? smp[0].S : Sg(smp[i - 1].D) == 0 ? smp[i - 1].S : Cross(smp[i - 1].S, smp[i].S);
                double s1 = j == smp.Count - 1 ? smp[^1].S : Sg(smp[j + 1].D) == 0 ? smp[j + 1].S : Cross(smp[j].S, smp[j + 1].S);
                double dm = 0; for (int k = i; k <= j; k++) dm = Math.Max(dm, Math.Abs(smp[k].D));
                bool up = sg < 0;
                parts.Add(new Part { Up = up, S0 = s0, S1 = s1, H = up ? inp.Hc : inp.Hf, T = Math.Max(0, up ? inp.Tc : inp.Tf), DeltaMax = dm });
                i = j + 1;
            }
        }
        // 0.1 mm 남짓의 짧은 부분(벽 높이 1 mm 안 · 길이 1 cm 안)은 버린다 — 벽을 세울 자리가 아니다
        parts.RemoveAll(p => p.DeltaMax < 1e-3 && p.S1 - p.S0 < 0.01);
        // 선 끝 A · B에서 1 mm 안인 부분 끝(그 사이 δ < 0.1 mm)은 A · B로 — A · B 곁 0.1 mm 안 점 쌍이 생기지 않게
        foreach (var p in parts)
        {
            if (p.S0 > 0 && p.S0 < 1e-3 && Math.Abs(Delta(0)) < Eps0) p.S0 = 0;
            if (p.S1 < pl.Len && pl.Len - p.S1 < 1e-3 && Math.Abs(Delta(pl.Len)) < Eps0) p.S1 = pl.Len;
        }
        log.Append(string.Format(Ci, "  ① 선 {0:F2} m · 점 {1} · 표본 {2} · δ(P0−G) {3:F3} ~ {4:F3} m · 부분 {5}({6})\n",
            pl.Len, pl.C.Length, smp.Count, smp.Min(q => q.D), smp.Max(q => q.D), parts.Count, string.Join(" · ", parts.Select(p => string.Format(Ci, "{0} {1:F2}~{2:F2}", p.Up ? "절" : "성", p.S0, p.S1)))));
        if (parts.Count == 0) return No("원지반", "선 위에서 합성 전 면과 원지반 높이가 같습니다(0.1 mm 안) — 옹벽이 설 자리가 없습니다");
        foreach (var p in parts)
            if ((p.Up ? inp.Hc : inp.Hf) <= 1e-6) return No("재료", $"{(p.Up ? "절토" : "성토")} 단높이가 0입니다");
        for (int k = 0; k + 1 < parts.Count; k++)
            if (parts[k].S1 < parts[k + 1].S0 - 1e-9 || parts[k].Up != parts[k + 1].Up)
            {
                var c = pl.At((parts[k].S1 + parts[k + 1].S0) / 2);
                R.Points.Add((c.X + ox, c.Y + oy, 0));
            }

        // 바깥 쪽(R_out 쪽) — 왼쪽 + · 오른쪽 −
        var cutP = BoundaryCutPoly(gf, inp.CutRing, ox, oy);
        if (cutP == null) return No("재료", "잘라낼 자리(R_out)를 다각형으로 못 만들었습니다");
        var nP = BoundaryCutPoly(gf, inp.NewRing, ox, oy);
        double side;
        {
            int best = 0; double bl = -1;
            for (int i = 0; i + 1 < pl.C.Length; i++) { double l = pl.C[i].Distance(pl.C[i + 1]); if (l > bl) { bl = l; best = i; } }
            var a = pl.C[best]; var b = pl.C[best + 1];
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, tx = (b.X - a.X) / bl, ty = (b.Y - a.Y) / bl, e = Math.Min(1e-5, bl * 0.01);   // R_out이 mm 띠여도(데이라잇 곁 선)
            bool left = cutP.Contains(gf.CreatePoint(new Coordinate(mx - ty * e, my + tx * e)));
            bool right = cutP.Contains(gf.CreatePoint(new Coordinate(mx + ty * e, my - tx * e)));
            if (left == right) return No("재료", "선의 어느 쪽이 잘라낼 자리인지 못 가렸습니다");
            side = left ? 1 : -1;
        }
        double reachLim = inp.Reach;
        foreach (var c in cutP.ExteriorRing.Coordinates) reachLim = Math.Max(reachLim, lineG.Distance(gf.CreatePoint(c)) + inp.Reach);

        // ② · ③ 부분마다 높이 · 줄기 · 줄
        var ssB = new BufferParameters { IsSingleSided = true, EndCapStyle = EndCapStyle.Flat, QuadrantSegments = 8 };
        var rnd = new BufferParameters { QuadrantSegments = 8 };
        double Gn(double lx, double ly, double sg) { var g = Gw(lx, ly); return g == null ? double.NaN : sg * g.Value; }
        var lfW = new List<List<Point3>>(); var lfC = new List<List<Point3>>();
        var linesAll = new List<List<Point3>>();
        // ★[v114.0 · 소단 높이 관문] 부분마다 원 벽 삼각형(소단 표본이 바꿀 자리 구멍에 빠졌을 때 읽는다) · 표본마다 그 부분 번호(F = −1)
        var rawByPart = new List<List<Tri>>(); var benchSrc = new List<int>();
        int partNo = 0;
        foreach (var part in parts)
        {
            partNo++;
            double sg = part.Up ? 1.0 : -1.0;
            double H = part.H, T = part.T;
            double f = Math.Max(H * Math.Max(0, inp.SlopeW), Math.Max(1e-4, inp.FaceRun));
            R.Face = Math.Max(R.Face, f);
            // 기준 = 절토 꼴 P0′ 최소(성토는 P0 최대)
            var pS = smp.Where(q => q.S >= part.S0 - 1e-12 && q.S <= part.S1 + 1e-12).Select(q => (q.S, P: sg * q.P)).ToList();
            pS.Insert(0, (part.S0, sg * P0At(part.S0))); pS.Add((part.S1, sg * P0At(part.S1)));
            pS = pS.OrderBy(q => q.S).ToList();
            double zr = pS.Min(q => q.P);
            part.Zref = sg * zr;
            double Lv(int k) => zr + k * H;   // 절토 꼴 높이
            int Jof(double p) => (int)Math.Floor((p - zr) / H) + 1;
            // 줄기 — j가 바뀌는 자리(P0′ = Z′_k)는 표본 사이 1차(표본이 P0 꺾임을 다 품는다 → 정확)
            var stems = new List<Stem>();
            {
                double s0 = pS[0].S; int j0 = Jof(pS[0].P);
                for (int i = 1; i < pS.Count; i++)
                {
                    int j1 = Jof(pS[i].P);
                    if (j1 == j0) continue;
                    double pa = pS[i - 1].P, pb = pS[i].P, sa = pS[i - 1].S, sb = pS[i].S;
                    int step = j1 > j0 ? 1 : -1;
                    for (int jj = j0; jj != j1; jj += step)
                    {
                        double lev = step > 0 ? Lv(jj) : Lv(jj - 1);
                        double t = Math.Abs(pb - pa) < 1e-15 ? 0.5 : Math.Clamp((lev - pa) / (pb - pa), 0, 1);
                        double sx = sa + (sb - sa) * t;
                        stems.Add(new Stem { S0 = s0, S1 = sx, J = jj });
                        s0 = sx;
                    }
                    j0 = j1;
                }
                stems.Add(new Stem { S0 = s0, S1 = pS[^1].S, J = j0 });
                stems.RemoveAll(st => st.S1 - st.S0 < 1e-9 && stems.Count > 1);
            }
            // ★[critic 2차 5] f보다 짧은 줄기는 이웃과 합친다 — 합친 뒤 첫 면 0 < h ≤ H + 1 cm여야(P0이 Z_k ± 5 mm로 흔들려도 마구리 하나)
            {
                bool Fits(Stem st, int jn)
                {
                    foreach (var q in pS) if (q.S >= st.S0 - 1e-12 && q.S <= st.S1 + 1e-12) { double h = Lv(jn) - q.P; if (!(h > -1e-9 && h <= H + 0.01)) return false; }
                    foreach (double s in new[] { st.S0, st.S1 }) { double h = Lv(jn) - sg * P0At(s); if (!(h > -1e-9 && h <= H + 0.01)) return false; }
                    return true;
                }
                var stuck = new HashSet<Stem>();
                for (int guard = 0; guard < 10000; guard++)
                {
                    Stem? sh = null;
                    // ★ 짧은 줄기(< f) + 길어도 «P0이 높이 경계 1 cm 안에만 있는» 줄기(이웃 높이로 합쳐도 첫 면 ≤ H + 1 cm) — 삼각망 정지면이 Z_k ± 몇 mm로 흔들려 생기는 줄기 갈림을 마구리 하나로
                    for (int si = 0; si < stems.Count; si++)
                    {
                        var st = stems[si];
                        if (stuck.Contains(st)) continue;
                        bool cand = st.S1 - st.S0 < f
                            || (si > 0 && stems[si - 1].J != st.J && Fits(st, stems[si - 1].J))
                            || (si + 1 < stems.Count && stems[si + 1].J != st.J && Fits(st, stems[si + 1].J));
                        if (cand && (sh == null || st.S1 - st.S0 < sh.S1 - sh.S0)) sh = st;
                    }
                    if (sh == null || stems.Count < 2) break;
                    int i = stems.IndexOf(sh);
                    var cands = new List<Stem>();
                    if (i > 0) cands.Add(stems[i - 1]);
                    if (i + 1 < stems.Count) cands.Add(stems[i + 1]);
                    var ok = cands.Where(nb => Fits(sh, nb.J)).OrderByDescending(nb => nb.S1 - nb.S0).FirstOrDefault();
                    if (ok == null) { stuck.Add(sh); continue; }
                    ok.S0 = Math.Min(ok.S0, sh.S0); ok.S1 = Math.Max(ok.S1, sh.S1);
                    stems.RemoveAt(i);
                    for (int k = stems.Count - 1; k >= 1; k--)
                        if (stems[k].J == stems[k - 1].J) { stems[k - 1].S1 = stems[k].S1; stems.RemoveAt(k); }
                }
                if (stuck.Count > 0) R.Warn.Add($"부분 {partNo}: 면 폭보다 짧은 줄기 {stuck.Count}개를 못 합쳤다(첫 면 높이가 H + 1 cm를 넘는다)");
            }
            part.Stems = stems;
            for (int k = 1; k < stems.Count; k++) { var c = pl.At(stems[k].S0); R.Points.Add((c.X + ox, c.Y + oy, 1)); }

            // 줄 — 부분 선(양 끝 곧게 늘임)에서 거리 d의 버퍼 테두리 ∩ 줄기 칸(바깥 쪽 한쪽 버퍼 · 끝 = 법선)
            double dBig = reachLim + 5;
            var subPart = pl.Sub(part.S0, part.S1);
            var lpExt = WallInPoly.ExtendEnds(subPart.Select(c => new Point3(c.X, c.Y, 0)).ToList(), dBig + 5);
            var lpG = gf.CreateLineString(lpExt.Select(q => new Coordinate(q.X, q.Y)).ToArray());
            var bufCache = new Dictionary<double, Geometry>();
            Geometry BufB(double d) { if (!bufCache.TryGetValue(d, out var g)) bufCache[d] = g = lpG.Buffer(d, rnd).Boundary; return g; }
            int nSt = stems.Count;
            // ★ 가름점 C 쪽 부분 끝도 줄기 갈림처럼 vEps/2 물린다 — 두 부분 끝 띠가 C 법선에서 만나고(z = G) 서로 겹치지 않게(A · B 쪽은 끝 너머 vEps 띠)
            bool cA = part.S0 > 1e-9, cB = part.S1 < pl.Len - 1e-9;
            double shS(int k) => k > 0 || cA ? vEps / 2 : 0;
            double shE(int k) => k + 1 < nSt || cB ? vEps / 2 : 0;
            (double A, double B) Shr(int k) => (stems[k].S0 + shS(k), stems[k].S1 - shE(k));
            (double X, double Y) TanAt(double s, bool atStart) => pl.Tan(atStart ? Math.Min(s + 1e-9, pl.Len) : Math.Max(s - 1e-9, 0));
            var (pt0x, pt0y) = TanAt(part.S0, true); var (pt1x, pt1y) = TanAt(part.S1, false);
            var cP0 = pl.At(part.S0); var cP1 = pl.At(part.S1);
            var ext0 = cA ? cP0 : new Coordinate(cP0.X - pt0x * vEps, cP0.Y - pt0y * vEps);
            var ext1 = cB ? cP1 : new Coordinate(cP1.X + pt1x * vEps, cP1.Y + pt1y * vEps);
            for (int k = 0; k < nSt; k++)
            {
                var (a, b) = Shr(k);
                if (b - a < 1e-6) return No("줄", string.Format(Ci, "부분 {0}의 줄기 {1}이 마구리 폭({2:F3} m)보다 짧습니다 — 선을 조금 옮기세요", partNo, k + 1, vEps), Wd(pl.At(stems[k].S0)));
            }
            // 줄 조각 끝이 어느 쪽 경계(−1 = a 쪽 · +1 = b 쪽)에서 끝났나 — 끝 선 E를 모을 때 쓴다
            var tags = new Dictionary<List<Coordinate>, (int A, int B)>(ReferenceEqualityComparer.Instance);
            List<Coordinate> Shorten(List<Coordinate> pc, bool atStart, double len)
            {
                if (len <= 0 || pc.Count < 2) return pc;
                var l = atStart ? pc : Enumerable.Reverse(pc).ToList();
                double acc = 0; int i = 0;
                for (; i + 1 < l.Count; i++) { double sl = l[i].Distance(l[i + 1]); if (acc + sl >= len) break; acc += sl; }
                if (i + 1 >= l.Count) return new List<Coordinate>();
                double t = (len - acc) / l[i].Distance(l[i + 1]);
                var np = new Coordinate(l[i].X + (l[i + 1].X - l[i].X) * t, l[i].Y + (l[i + 1].Y - l[i].Y) * t);
                var o = new List<Coordinate> { np }; o.AddRange(l.Skip(i + 1));
                if (!atStart) o.Reverse();
                return o;
            }
            // 줄기 칸 = 바깥 쪽 · 늘인 부분 선 위 가장 가까운 점의 호 길이가 [a, b] — 버퍼 테두리를 따라 그 구간만(경계는 이분법 · 한쪽 버퍼는 큰 거리에서 믿을 수 없어 안 쓴다).
            //   ★ 이웃 줄기와 맞닿는 끝은 줄을 따라 vEps/2 줄인다 — 곧은 자리는 법선에서(가로 마구리) · 오목 꼭짓점 곁은 이등분선에서(계획 1-4 (c)) 같은 폭의 틈
            var lpx = new PL(lpExt.Select(q => new Coordinate(q.X, q.Y)).ToArray());
            double sOff = lpx.Near(subPart[0].X, subPart[0].Y).S;
            (double S, double Side) SOn(double x, double y) { var nr = lpx.Near(x, y); return (nr.S - sOff + part.S0, nr.Side); }
            List<List<Coordinate>> RowIn(double d, double a, double b, double shA, double shB)
            {
                var res = new List<(List<Coordinate> P, int A, int B)>();
                var g0 = BufB(d);
                for (int gi = 0; gi < g0.NumGeometries; gi++)
                {
                    if (g0.GetGeometryN(gi) is not LineString ring) continue;
                    var cs = ring.Coordinates;
                    List<Coordinate>? cur = null; int curA = 0;
                    Coordinate Bis(Coordinate p, Coordinate q, double lev)
                    {
                        double sp = SOn(p.X, p.Y).S, t0 = 0, t1 = 1;
                        bool up = SOn(q.X, q.Y).S > sp;
                        for (int it = 0; it < 60; it++)
                        {
                            double tm = (t0 + t1) / 2, sm = SOn(p.X + (q.X - p.X) * tm, p.Y + (q.Y - p.Y) * tm).S;
                            if ((sm < lev) == up) t0 = tm; else t1 = tm;
                        }
                        double tt = (t0 + t1) / 2;
                        return new Coordinate(p.X + (q.X - p.X) * tt, p.Y + (q.Y - p.Y) * tt);
                    }
                    for (int i = 0; i + 1 < cs.Length; i++)
                    {
                        var p = cs[i]; var q = cs[i + 1];
                        var (sp, dp) = SOn(p.X, p.Y); var (sq, dq) = SOn(q.X, q.Y);
                        bool pin = dp == side && sp >= a && sp <= b, qin = dq == side && sq >= a && sq <= b;
                        if (pin && cur == null) { cur = new List<Coordinate> { p }; curA = 0; }
                        if (pin && qin) { cur!.Add(q); continue; }
                        if (pin && !qin)
                        {
                            int tb = 0;
                            if (dq == side) { cur!.Add(Bis(p, q, sq > b ? b : a)); tb = sq > b ? 1 : -1; }
                            res.Add((cur!, curA, tb)); cur = null; continue;
                        }
                        if (!pin && qin)
                        {
                            int ta = dp == side ? (sp < a ? -1 : 1) : 0;
                            cur = new List<Coordinate> { dp == side ? Bis(p, q, sp < a ? a : b) : p, q }; curA = ta;
                            continue;
                        }
                        if (dp == side && dq == side && ((sp < a && sq > b) || (sp > b && sq < a)))
                        {
                            var e0 = Bis(p, q, sp < a ? a : b); var e1 = Bis(p, q, sp < a ? b : a);
                            res.Add((new List<Coordinate> { e0, e1 }, sp < a ? -1 : 1, sp < a ? 1 : -1));
                        }
                    }
                    if (cur != null && cur.Count >= 2) res.Add((cur, curA, 0));
                }
                // 고리의 첫 · 끝 토막 잇기(고리가 칸 한가운데서 시작했으면)
                for (int i = res.Count - 1; i >= 1; i--)
                    for (int j = 0; j < i; j++)
                        if (res[j].P[0].Distance(res[i].P[^1]) < 1e-9) { var m2 = new List<Coordinate>(res[i].P); m2.AddRange(res[j].P.Skip(1)); res[i] = (m2, res[i].A, res[j].B); res.RemoveAt(j); break; }
                var outL = new List<List<Coordinate>>();
                foreach (var (P, ta, tb) in res)
                {
                    var pc = P;
                    if (ta == -1 && shA > 0) pc = Shorten(pc, true, shA); else if (ta == 1 && shB > 0) pc = Shorten(pc, true, shB);
                    if (pc.Count >= 2) { if (tb == -1 && shA > 0) pc = Shorten(pc, false, shA); else if (tb == 1 && shB > 0) pc = Shorten(pc, false, shB); }
                    if (pc.Count < 2 || gf.CreateLineString(pc.ToArray()).Length < 1e-7) continue;
                    outL.Add(pc); tags[pc] = (ta, tb);
                }
                return outL;
            }
            List<List<Coordinate>> RowAt(int k, double d) => RowIn(d, stems[k].S0, stems[k].S1, shS(k), shE(k));
            bool Buried(List<List<Coordinate>> pcs, double lev)
            {
                foreach (var pc in pcs)
                    for (int q = 0; q < pc.Count; q++)
                    {
                        var c = pc[q];
                        double gn = Gn(c.X, c.Y, sg); if (!double.IsNaN(gn) && gn > lev + 1e-6) return true;
                        if (q + 1 >= pc.Count) continue;
                        double sl = c.Distance(pc[q + 1]); int nn = (int)Math.Ceiling(sl / 0.5);
                        for (int u = 1; u < nn; u++)
                        {
                            double x = c.X + (pc[q + 1].X - c.X) * u / nn, y = c.Y + (pc[q + 1].Y - c.Y) * u / nn;
                            double g2 = Gn(x, y, sg); if (!double.IsNaN(g2) && g2 > lev + 1e-6) return true;
                        }
                    }
                return false;
            }
            // 단 수(줄 멈춤 · 계획 L10) — 그 단 소단(소단 0이면 면 끝 + 0.5 m)이 땅 속(절토 꼴 G′ > 높이)인 자리가 있으면 한 단 더
            var tierRows = new List<List<(double D, double Z, List<List<Coordinate>> Pc)>>();
            var dEnd = new double[nSt];
            bool reachHit = false;
            for (int k = 0; k < nSt; k++)
            {
                var st = stems[k];
                var rows = new List<(double D, double Z, List<List<Coordinate>> Pc)>();
                int n = 0; double lastF = f, lastLev = Lv(st.J);
                for (int t = 1; t <= 200; t++)
                {
                    double lev = Lv(st.J + t - 1);
                    double dF = (t - 1) * (f + T) + f;
                    if (dF > reachLim) { reachHit = true; break; }
                    rows.Add((dF, lev, RowAt(k, dF)));
                    n = t; lastF = dF; lastLev = lev;
                    double dS = dF + Math.Max(T, 0.5);
                    bool more = Buried(RowAt(k, dS), lev) || (T > 1e-6 && (Buried(RowAt(k, dF + T / 2), lev) || Buried(RowAt(k, dF + T), lev)));
                    if (!more) break;
                    if (t == 200) reachHit = true;
                    if (T > 1e-6) rows.Add((dF + T, lev, RowAt(k, dF + T)));
                }
                st.Tiers = Math.Max(1, n);
                dEnd[k] = lastF + Math.Max(T, 0.5);
                rows.Add((dEnd[k], lastLev, RowAt(k, dEnd[k])));
                tierRows.Add(rows);
                part.Dmax = Math.Max(part.Dmax, dEnd[k]);
            }
            double dMax = part.Dmax;
            if (reachHit) return No("도달", string.Format(Ci, "부분 {0}의 벽이 선에서 {1:F1} m(한도) 안에서 원지반에 못 닿습니다 — 선을 옮기거나 단높이 · 소단을 바꾸세요", partNo, reachLim), Wd(pl.At((part.S0 + part.S1) / 2)));
            // 원(raw) 선 — L(부분 · S ± vEps/2) · 줄 · 끝 선 E · 틈 닫음 · 바깥 띠 O(줄기마다 끝 줄 + vEps · z = G) · 부분 끝 띠 E′(z = G)
            var raw = new List<List<Point3>>();
            var rowSet = new HashSet<List<Point3>>(ReferenceEqualityComparer.Instance);
            // 부분 끝(δ < 0.1 mm)의 L 점은 높이 = G — 끝 띠(L 이음 · z = G)에서 µm 부스러기 자리가 생기지 않게(계획 1-6 «L 점을 발 점으로»)
            Point3 PtL(double s)
            {
                var c = pl.At(s); double z = P0At(s);
                if (s == part.S0 || s == part.S1) { double dl = Delta(s); if (Math.Abs(dl) < Eps0) z -= dl; }
                return new Point3(c.X + ox, c.Y + oy, z);
            }
            Point3 PtG(Coordinate c, out bool ok) { var g = Gw(c.X, c.Y); ok = g != null; return new Point3(c.X + ox, c.Y + oy, g ?? 0); }
            {
                var cuts = new List<double> { Shr(0).A, Shr(nSt - 1).B };
                for (int k = 0; k < nSt; k++) { var (a, b) = Shr(k); cuts.Add(a); cuts.Add(b); }
                foreach (var q in prof) if (q.S > Shr(0).A && q.S < Shr(nSt - 1).B) cuts.Add(q.S);
                for (int i = 1; i < pl.C.Length - 1; i++) if (pl.Cum[i] > Shr(0).A && pl.Cum[i] < Shr(nSt - 1).B) cuts.Add(pl.Cum[i]);
                cuts.Sort();
                var lp = new List<Point3>(); double lastS = double.NegativeInfinity;
                foreach (double s in cuts) if (s - lastS > 1e-9) { lp.Add(PtL(s)); lastS = s; }
                raw.Add(lp);
            }
            var eLines = new List<(int Stem, bool AtStart, bool Inner, List<Point3> E)>();
            int eShort = 0;
            for (int k = 0; k < nSt; k++)
            {
                foreach (var (dd, z, pcs) in tierRows[k])
                    foreach (var pc in pcs) { var l = pc.Select(c => new Point3(c.X + ox, c.Y + oy, sg * z)).ToList(); raw.Add(l); rowSet.Add(l); R.RawRows.Add(l); }
                var (a, b) = Shr(k);
                foreach (var (se, atStart) in new[] { (a, true), (b, false) })
                {
                    // 끝 선 E — 그쪽 경계에서 끝난 줄 조각 끝들(법선 위 · 오목 꼭짓점 곁은 이등분선 곁) · L에서 먼 차례
                    int want = atStart ? -1 : 1;
                    var ends = new List<(double D, Point3 P)>();
                    foreach (var (dd, z, pcs) in tierRows[k])
                        foreach (var pc in pcs)
                        {
                            if (!tags.TryGetValue(pc, out var tg)) continue;
                            if (tg.A == want) ends.Add((dd, new Point3(pc[0].X + ox, pc[0].Y + oy, sg * z)));
                            if (tg.B == want) ends.Add((dd, new Point3(pc[^1].X + ox, pc[^1].Y + oy, sg * z)));
                        }
                    // ★[v114.0 · 오목 꼭짓점] 법선 ∩ 이등분선 모서리 — 법선을 따라가다 가장 가까운 점이 이 끝(se)에서 이웃 줄기 쪽으로 바뀌는 거리 d*를 이분법으로 찾아
                    //   끝 선에 넣는다(법선에서 끝난 줄 · 이등분선 곁에서 끝난 줄 사이 모서리를 건너뛰면 이웃 끝 선과 엇갈린다)
                    {
                        var p0e = pl.At(se); var (tx0, ty0) = TanAt(se, atStart);
                        double nx0 = -ty0 * side, ny0 = tx0 * side;
                        var rr = tierRows[k];
                        double dHi = rr.Count > 0 ? rr[^1].D : 0;
                        // 바뀜 = 가장 가까운 점이 이 끝(se)을 떠남. ★[v114.0 · JACK 1105 소단 높이 관문 계측] 옛 잣대 «이 줄기 밖으로 넘어감»은 오목 꼭짓점을 제 줄기 안에 품은 쪽
                        //   (법선 너머가 제 줄기의 다음 다리)에서 모서리를 못 찾아, 끝 선이 법선 위 줄 끝에서 이등분선 위 줄 끝으로 곧게 건너가며 이웃 끝 선(한 단 아래)과 엇갈렸다 —
                        //   이웃 모서리 점이 소단 안에 들어 소단이 0.4~1 m 처졌다(S198 거울 H 0.5 · T 1 (44.6, ±4.0) −103.42 / 소단 −103 · 넓은 소단 0.94 m)
                        bool Sw(double d) { var (s2, sd2) = SOn(p0e.X + nx0 * d, p0e.Y + ny0 * d); return sd2 != side || Math.Abs(s2 - se) > 1e-6; }
                        if (dHi > 0 && Sw(dHi) && !Sw(Math.Min(1e-4, dHi / 2)))
                        {
                            double lo = Math.Min(1e-4, dHi / 2), hi = dHi;
                            for (int it = 0; it < 60; it++) { double m = (lo + hi) / 2; if (Sw(m)) hi = m; else lo = m; }
                            double dc = lo;
                            // 그 거리의 벽 높이(줄 사이 1차 · 첫 면은 L 위 P0에서)
                            double zN;
                            double p0N = sg * P0At(se);
                            if (dc <= rr[0].D) zN = p0N + (rr[0].Z - p0N) * dc / rr[0].D;
                            else { zN = rr[^1].Z; for (int q = 0; q + 1 < rr.Count; q++) if (dc >= rr[q].D && dc <= rr[q + 1].D) { zN = rr[q].Z + (rr[q + 1].Z - rr[q].Z) * (dc - rr[q].D) / Math.Max(1e-12, rr[q + 1].D - rr[q].D); break; } }
                            if (ends.All(e => Math.Abs(e.D - dc) > 1e-6)) ends.Add((dc, new Point3(p0e.X + nx0 * dc + ox, p0e.Y + ny0 * dc + oy, sg * zN)));
                            R.Corners++;
                        }
                    }
                    ends.Sort((u, v) => u.D.CompareTo(v.D));
                    var E = new List<Point3> { PtL(se) };
                    foreach (var (_, q) in ends) if (Math.Abs(q.X - E[^1].X) > 1e-7 || Math.Abs(q.Y - E[^1].Y) > 1e-7) E.Add(q);
                    // ★[v114.0 · S198 넓은 소단 계측] 끝 선을 0.5 m마다 촘촘히(높이는 그 토막에서 1차) — 이등분선을 따라 줄 끝 사이가 m 단위로 비면
                    //   우리 들로네와 Civil 들로네가 다른 대각선을 골라(가늘고 긴 벽 세모) 합성의 «뒤집기 흉내»가 높이를 145 mm 바꿨다
                    {
                        var dn = new List<Point3> { E[0] };
                        for (int i = 1; i < E.Count; i++)
                        {
                            var a0 = E[i - 1]; var b0 = E[i];
                            double sl = Math.Sqrt((b0.X - a0.X) * (b0.X - a0.X) + (b0.Y - a0.Y) * (b0.Y - a0.Y));
                            int nn = (int)Math.Ceiling(sl / 0.5);
                            for (int u = 1; u < nn; u++) { double t = (double)u / nn; dn.Add(new Point3(a0.X + (b0.X - a0.X) * t, a0.Y + (b0.Y - a0.Y) * t, a0.Z + (b0.Z - a0.Z) * t)); }
                            dn.Add(b0);
                        }
                        E = dn;
                    }
                    if (E.Count - 1 < tierRows[k].Count) eShort++;
                    if (E.Count < 2) return No("줄", $"부분 {partNo} 줄기 {k + 1}의 끝 선을 못 지었습니다", Wd(pl.At(se)));
                    raw.Add(E);
                    eLines.Add((k, atStart, atStart ? k > 0 : k + 1 < nSt, E));
                }
            }
            if (eShort > 0) log.Append($"  ②-b 부분 {partNo}: 끝 선이 줄 수보다 적은 끝 {eShort}(오목 꼭짓점 곁 — 이등분선에서 끝난 줄)\n");
            static double D2(Point3 u, Point3 v) => (u.X - v.X) * (u.X - v.X) + (u.Y - v.Y) * (u.Y - v.Y);
            // 틈(줄기 갈림 S) — 두 끝 선의 바깥 끝 닫음 · 두 O의 가까운 끝 이음
            for (int k = 0; k + 1 < nSt; k++)
            {
                var ea = eLines.First(e => e.Stem == k && !e.AtStart).E; var eb = eLines.First(e => e.Stem == k + 1 && e.AtStart).E;
                raw.Add(new List<Point3> { ea[^1], eb[^1] });
                R.EndFaceN++;
            }
            // 부분 끝 띠(z = G) — L 이음(L 끝 → A · B 너머 vEps · C에선 C 위) · E′(그 점에서 법선으로 끝 줄기 끝 줄 거리까지) · 그 끝 → 끝 선 바깥 끝.
            //   바깥 띠 O(부분 하나 · 가장 먼 끝 줄 + vEps · z = G)가 있으면 그것으로 닫고, 오목한 선이라 그 거리에 칸이 없으면(줄기끼리 밀어냄) 끝 줄에서 닫는다(자리가 거기 닿으면 «도달»)
            List<Point3>? Oline = null;
            {
                var oP = RowIn(dMax + vEps, part.S0 - (cA ? 0 : vEps), part.S1 + (cB ? 0 : vEps), 0, 0);
                if (oP.Count > 0)
                {
                    var op = oP.OrderByDescending(q => gf.CreateLineString(q.ToArray()).Length).First();
                    Oline = new List<Point3>();
                    foreach (var c in op) { var q = PtG(c, out bool ok); if (!ok) { Oline = null; break; } Oline.Add(q); }
                    if (Oline != null) raw.Add(Oline);
                }
                if (Oline == null) log.Append($"  ②-c 부분 {partNo}: 바깥 띠 없음(오목한 선 — 끝 줄에서 닫음)\n");
            }
            foreach (var (atStart, ce, kk) in new[] { (true, ext0, 0), (false, ext1, nSt - 1) })
            {
                var pE = PtG(ce, out bool okE);
                if (!okE) return No("원지반", "선 끝 바깥에서 원지반을 못 읽었습니다", Wd(ce));
                double sEnd = atStart ? Shr(0).A : Shr(nSt - 1).B;
                raw.Add(new List<Point3> { PtL(sEnd), pE });
                var Ee = eLines.First(e => e.Stem == kk && e.AtStart == atStart).E;
                var tip = Ee[^1];
                var (etx, ety) = TanAt(atStart ? part.S0 : part.S1, atStart);
                double enx = -ety * side, eny = etx * side;
                // ★[v114.0 · S199 ⑤] 끝 띠를 사다리로 — E 점마다 같은 거리의 E′ 점(z = G)과 가로대. 두 점짜리 E′에 E 점들이 부채꼴로 붙으면 vEps/2 폭 · m 길이 바늘이 생겨 합성 다듬기가 Civil 뒤집기를 못 피했다
                var ladder = new List<Point3> { pE };
                {
                    var pl0 = new Coordinate(Ee[0].X - ox, Ee[0].Y - oy);
                    foreach (var q in Ee.Skip(1))
                    {
                        double along = (q.X - ox - pl0.X) * enx + (q.Y - oy - pl0.Y) * eny;
                        if (along <= 1e-6) continue;
                        var lc2 = new Coordinate(ce.X + enx * along, ce.Y + eny * along);
                        var lq = PtG(lc2, out bool okL);
                        if (!okL) return No("원지반", "선 끝 바깥에서 원지반을 못 읽었습니다", Wd(lc2));
                        ladder.Add(lq);
                        raw.Add(new List<Point3> { q, lq });
                    }
                }
                if (Oline != null)
                {
                    var oe = D2(Oline[0], pE) <= D2(Oline[^1], pE) ? Oline[0] : Oline[^1];
                    ladder.Add(oe); raw.Add(ladder);
                    if (ladder.Count == 2) raw.Add(new List<Point3> { tip, oe });
                    continue;
                }
                if (ladder.Count > 1) { raw.Add(ladder); continue; }
                double dTip = Math.Max(f, Math.Sqrt((tip.X - ox - ce.X) * (tip.X - ox - ce.X) + (tip.Y - oy - ce.Y) * (tip.Y - oy - ce.Y)));
                var farC = new Coordinate(ce.X + enx * dTip, ce.Y + eny * dTip);
                var far = PtG(farC, out bool okF);
                if (!okF) return No("원지반", "선 끝 바깥에서 원지반을 못 읽었습니다", Wd(farC));
                raw.Add(new List<Point3> { pE, far });
                raw.Add(new List<Point3> { far, tip });
            }
            // ★[v114.0 · S198 넓은 소단 — 계측: 이등분선 곁 원 면 구멍 1(이웃 아닌 줄기 0 · 줄 접힘 0)] 지킴 고리 — 원 선 전부를 vEps × 1.5 버퍼한 바깥 고리(구멍 메움 · N과 L 위 몫 뺌 · z = G).
            //   어느 칸에도 안 닫힌 자리(넓은 소단이 오목한 선 안을 채우며 이웃 줄기 칸이 끝난 자리)도 면이 되어 W가 이어지고, 자리 테두리는 늘 W = G가 된다(구성으로 이어짐)
            try
            {
                var gl = raw.Where(l => l.Count >= 2).Select(l => (Geometry)gf.CreateLineString(l.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray())).ToArray();
                var gb = NtsSupport.LargestPolygon(gf.CreateGeometryCollection(gl).Buffer(vEps * 1.5, new BufferParameters { QuadrantSegments = 4 }));
                if (gb != null)
                {
                    Geometry guard = gf.CreatePolygon((LinearRing)gb.ExteriorRing.Copy());
                    if (nP != null) guard = guard.Difference(nP);
                    var lineBuf = gf.CreateLineString(pl.C).Buffer(1e-6);
                    var gBnd = guard.Boundary.Difference(lineBuf);
                    var lmG = new LineMerger(); lmG.Add(gBnd);
                    int gn = 0;
                    foreach (var x in lmG.GetMergedLineStrings())
                    {
                        if (x is not LineString lsG || lsG.Length < 1e-6) continue;
                        var gp = new List<Point3>();
                        var cs = lsG.Coordinates;
                        for (int i = 0; i < cs.Length; i++)
                        {
                            if (i > 0) { double sl = cs[i - 1].Distance(cs[i]); int nn = (int)Math.Ceiling(sl / 0.5); for (int u = 1; u < nn; u++) { var cq = new Coordinate(cs[i - 1].X + (cs[i].X - cs[i - 1].X) * u / nn, cs[i - 1].Y + (cs[i].Y - cs[i - 1].Y) * u / nn); var qg = PtG(cq, out bool oq); if (!oq) return No("원지반", "벽 둘레 지킴 고리에서 원지반을 못 읽었습니다", Wd(cq)); gp.Add(qg); } }
                            var q2 = PtG(cs[i], out bool o2); if (!o2) return No("원지반", "벽 둘레 지킴 고리에서 원지반을 못 읽었습니다", Wd(cs[i])); gp.Add(q2);
                        }
                        // L 위 끝(지킴 고리가 L에 닿는 자리)은 L 높이로 — L과 같은 점 · 같은 높이
                        foreach (int ii in new[] { 0, gp.Count - 1 }) { var nrL = pl.Near(gp[ii].X - ox, gp[ii].Y - oy); if (nrL.D < 1e-5) gp[ii] = new Point3(gp[ii].X, gp[ii].Y, P0At(nrL.S)); }
                        raw.Add(gp); gn++;
                    }
                    if (gn == 0) log.Append($"  ②-d 부분 {partNo}: 지킴 고리 없음\n");
                }
            }
            catch (Exception gx) { log.Append($"  ②-d 부분 {partNo}: ⚠지킴 고리 못 지음({gx.GetType().Name})\n"); }
            linesAll.AddRange(raw);
            var rawTris = MeshLines(raw, ox, oy);
            R.RawTris.AddRange(rawTris);
            if (rawTris.Count == 0) return No("줄", $"부분 {partNo}의 벽 면을 못 지었습니다", Wd(pl.At((part.S0 + part.S1) / 2)));

            // ⑤ 바꿀 자리 Zone = {W′ < G′} 가운데 L에 붙은 성분(critic 2차 1)
            var pieces = ZonePieces(rawTris, inp, sg, ox, oy, gf, Math.Min(0.5, Math.Max(0.1, inp.Grid / 2)));
            Geometry zu;
            try { zu = pieces.Count == 0 ? gf.CreatePolygon() : NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union(gf.BuildGeometry(pieces), new PrecisionModel(1e9)); }
            catch (Exception ex) { return No("자리", $"부분 {partNo}의 바꿀 자리를 못 합쳤습니다({ex.GetType().Name})"); }
            var partLine = gf.CreateLineString(pl.Sub(part.S0, part.S1));
            var comps = new List<Polygon>();
            for (int i = 0; i < zu.NumGeometries; i++)
                if (zu.GetGeometryN(i) is Polygon pg && pg.Area > 1e-12)
                {
                    double touch = 0; try { touch = partLine.Intersection(pg.Buffer(1e-7)).Length; } catch { }
                    if (touch > 1e-5) comps.Add(pg);
                }
            if (comps.Count == 0) return No("자리", $"부분 {partNo}에 선에 붙은 바꿀 자리가 없습니다", Wd(pl.At((part.S0 + part.S1) / 2)));
            // 한도에 닿음(바깥 끝 줄에서 벽이 아직 땅 아래 · 위) · 부분 끝 마구리(끝 선에서 벽 ≠ 땅)
            {
                var oRows = tierRows.Select(r => r[^1]).ToList();
                foreach (var pg in comps)
                {
                    var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(pg);
                    foreach (var (dd, z, pcs) in oRows)
                        foreach (var pc in pcs)
                            foreach (var c in pc)
                                if (prep.Contains(gf.CreatePoint(c)) && Gn(c.X, c.Y, sg) - z > 1e-2) { R.EdgeHit++; if (R.Marks.Count == 0) R.Marks.Add(Wd(c)); }
                    foreach (var (_, _, _, E) in eLines.Where(e => !e.Inner))
                        foreach (var q in E.Skip(1))
                        {
                            var c = new Coordinate(q.X - ox, q.Y - oy);
                            if (prep.Contains(gf.CreatePoint(c)) && Gn(c.X, c.Y, sg) - sg * q.Z > 1e-3) { R.EndCliff++; break; }
                        }
                }
                if (R.EdgeHit > 0) return No("도달", string.Format(Ci, "부분 {0}의 벽이 바깥 끝({1:F1} m)에서도 원지반에 못 닿습니다(한도 닿음 {2}점)", partNo, dMax, R.EdgeHit), R.Marks.ToArray());
            }
            // ★[v114.0 · JACK 1105 소단 높이 관문] 소단 표본 — 단마다 소단 폭 가운데(± 0.5 m씩 · 면 · 소단 끝에서 vEps 떨어져) 줄을 0.5 m 간격으로(줄기 끝 · 마구리에서 vEps 더 물림).
            //   제 바꿀 자리(바깥 고리 — 구멍도 넣는다: 벽이 땅을 뚫고 처진 자리) 안 · 땅이 소단 너머(G′ − Z′_k > 1 cm)인 자리만 — 땅이 이기는 자리 · 다른 부분 자리는 안 잰다
            rawByPart.Add(rawTris);
            {
                var zShell = comps.Select(pg => NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(gf.CreatePolygon((LinearRing)pg.ExteriorRing.Copy()))).ToList();
                void Take(double x, double y, double lev)
                {
                    double gn = Gn(x, y, sg);
                    if (double.IsNaN(gn) || gn - lev <= 1e-2) return;
                    var pt = gf.CreatePoint(new Coordinate(x, y));
                    if (!zShell.Any(z => z.Contains(pt))) return;
                    R.BenchPts.Add((x + ox, y + oy, sg * lev, 0)); benchSrc.Add(rawByPart.Count - 1);
                }
                for (int k = 0; k < nSt; k++)
                    for (int t = 1; t <= stems[k].Tiers; t++)
                    {
                        double dF = (t - 1) * (f + T) + f, lev = Lv(stems[k].J + t - 1);
                        double w0 = dF + vEps, w1 = (t == stems[k].Tiers ? dEnd[k] : dF + T) - vEps;
                        if (w1 - w0 < 1e-6) continue;
                        double mid = (w0 + w1) / 2; int nh = (int)Math.Floor((w1 - w0) / 2 / 0.5 + 1e-9);
                        for (int h = -nh; h <= nh; h++)
                            foreach (var pc in RowIn(mid + h * 0.5, stems[k].S0, stems[k].S1, shS(k) + vEps, shE(k) + vEps))
                                for (int i = 0; i + 1 < pc.Count; i++)
                                {
                                    int nn = Math.Max(1, (int)Math.Ceiling(pc[i].Distance(pc[i + 1]) / 0.5));
                                    for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                                        Take(pc[i].X + (pc[i + 1].X - pc[i].X) * u / nn, pc[i].Y + (pc[i + 1].Y - pc[i].Y) * u / nn, lev);
                                }
                    }
            }

            // L 위 점은 0.1 mm 안의 L 꼭짓점(A · B · 꺾임 · 부분 · 줄기 끝)으로 — Poly(∪ R_out)에 0.1 mm 안 점 쌍이 생기지 않게
            var keyS = new List<double> { part.S0, part.S1 }; keyS.AddRange(pl.Cum);
            for (int k = 0; k < nSt; k++) { var (ka, kb) = Shr(k); keyS.Add(ka); keyS.Add(kb); }
            double SnapS(double s) { double best = s, bd = 1e-4; foreach (double kq in keyS) { double dk = Math.Abs(kq - s); if (dk < bd) { bd = dk; best = kq; } } return best; }
            // 끝 가늘어짐(계획 1-6) — F 점: δ < 0.1 mm면 L 점 · L에서 1 mm 안이면 1 mm로(높이는 그 자리 G)
            foreach (var pg in comps)
            {
                var ring = new List<Point3>();
                var ec = pg.ExteriorRing.Coordinates;
                for (int i = 0; i + 1 < ec.Length; i++)
                {
                    var c = ec[i];
                    var nr = pl.Near(c.X, c.Y);
                    if (nr.D < 1e-9) { ring.Add(PtL(SnapS(nr.S))); continue; }
                    if (nr.S > part.S0 + 1e-9 && nr.S < part.S1 - 1e-9 && nr.D < 1e-3 && nr.Side == side)
                    {
                        double dl = Delta(nr.S);
                        if (Math.Abs(dl) < Eps0) { ring.Add(PtL(SnapS(nr.S))); R.MergedPts++; continue; }
                        var lpC = pl.At(nr.S);
                        double ux = (c.X - lpC.X) / nr.D, uy = (c.Y - lpC.Y) / nr.D;
                        c = new Coordinate(lpC.X + ux * 1e-3, lpC.Y + uy * 1e-3);
                        R.WMinBumped++;
                    }
                    var g = Gw(c.X, c.Y);
                    if (g == null) return No("원지반", string.Format(Ci, "바꿀 자리 테두리에서 원지반을 못 읽었습니다 ({0:F1}, {1:F1})", c.X + ox, c.Y + oy), Wd(c));
                    ring.Add(new Point3(c.X + ox, c.Y + oy, g.Value));
                }
                // 0.1 mm 안 이웃 점 합침(Civil이 어차피 합친다 — 우리가 먼저 · L 점을 남긴다)
                var clean = new List<Point3>();
                foreach (var q in ring)
                {
                    if (clean.Count > 0)
                    {
                        var p = clean[^1];
                        // ★[v114.0 · S198 넓은 소단 계측] Civil과 같은 네모 잣대(CivilSafeMesh.Gap — 축마다 1.05e-4) — 원 거리 1e-4로 재면 (80 µm, 80 µm) 쌍이
                        //   살아남아 고리에 가는 조각이 생기고, 합성이 그 쌍을 «Civil이 반드시 합칠 쌍»(높이 차 149 mm)으로 거절했다
                        double dd = Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));
                        if (dd < CivilSafeMesh.Gap)
                        {
                            if (dd > 0) R.MergedPts++;
                            bool qOnL = pl.Near(q.X - ox, q.Y - oy).D < 1e-9;
                            if (qOnL) clean[^1] = q;
                            continue;
                        }
                    }
                    clean.Add(q);
                }
                while (clean.Count > 3 && Math.Max(Math.Abs(clean[0].X - clean[^1].X), Math.Abs(clean[0].Y - clean[^1].Y)) < CivilSafeMesh.Gap) clean.RemoveAt(clean.Count - 1);
                // 되돌아가는 바늘(앞 = 뒤) 걷기
                for (int it = 0; it < 3; it++)
                    for (int i = clean.Count - 1; i >= 0 && clean.Count > 3; i--)
                    {
                        var p = clean[(i - 1 + clean.Count) % clean.Count]; var q = clean[(i + 1) % clean.Count];
                        if (Math.Abs(p.X - q.X) < 1e-9 && Math.Abs(p.Y - q.Y) < 1e-9) { clean.RemoveAt(i); if (i < clean.Count) clean.RemoveAt(i % clean.Count); }
                    }
                // 되돌아가는 가시(거의 같은 길로 갔다 옴 · 각 1e-9 안) 걷기
                for (bool any = true; any && clean.Count > 3;)
                {
                    any = false;
                    for (int i = 0; i < clean.Count && clean.Count > 3; i++)
                    {
                        var a0 = clean[(i - 1 + clean.Count) % clean.Count]; var b0 = clean[i]; var c0 = clean[(i + 1) % clean.Count];
                        double ux = b0.X - a0.X, uy = b0.Y - a0.Y, wx = c0.X - b0.X, wy = c0.Y - b0.Y, lu = Math.Sqrt(ux * ux + uy * uy), lw = Math.Sqrt(wx * wx + wy * wy);
                        if (lu < 1e-12 || lw < 1e-12 || (ux * wx + uy * wy < 0 && Math.Abs(ux * wy - uy * wx) <= 1e-9 * lu * lw)) { clean.RemoveAt(i); any = true; i--; }
                    }
                }
                if (clean.Count < 3) continue;
                var zp = gf.CreatePolygon(clean.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(clean[0].X - ox, clean[0].Y - oy)).ToArray());
                if (!zp.IsValid)
                {
                    var fx = NtsSupport.LargestPolygon(zp.Buffer(0));
                    if (fx == null || Math.Abs(fx.Area - pg.Area) > 1e-4 + 1e-3 * pg.Area) return No("자리", string.Format(Ci, "부분 {0}의 바꿀 자리 테두리가 꼬입니다({1:F4}㎡)", partNo, pg.Area), Wd(pg.InteriorPoint.Coordinate));
                    var fc = fx.ExteriorRing.Coordinates;
                    var fr = new List<Point3>();
                    for (int i = 0; i + 1 < fc.Length; i++)
                    {
                        var c = fc[i]; var nr = pl.Near(c.X, c.Y);
                        fr.Add(nr.D < 1e-9 ? PtL(nr.S) : new Point3(c.X + ox, c.Y + oy, Gw(c.X, c.Y) ?? 0));
                    }
                    clean = fr; zp = fx;
                }
                // F 점을 원지반 삼각형 변 건넘까지(Civil 브레이크라인이 G를 정확히 따라가게)
                if (inp.GroundTris != null && inp.GroundTris.Count > 0)
                {
                    var dn = new List<Point3>();
                    for (int i = 0; i < clean.Count; i++)
                    {
                        var p = clean[i]; var q = clean[(i + 1) % clean.Count];
                        dn.Add(p);
                        bool pL = pl.Near(p.X - ox, p.Y - oy).D < 1e-9, qL = pl.Near(q.X - ox, q.Y - oy).D < 1e-9;
                        if (pL && qL) continue;
                        var pr = ProfileOnTris(p, q, inp.GroundTris, inp.Ground, 1e9);
                        if (pr != null) for (int k = 1; k + 1 < pr.Count; k++) dn.Add(pr[k]);
                    }
                    clean = dn;
                }
                (part.Up ? lfC : lfW).Add(clean);
                // 구멍(묻힌 섬)은 브레이크라인으로만
                var holeLines = new List<List<Point3>>();
                for (int h = 0; h < pg.NumInteriorRings; h++)
                {
                    var hr = pg.GetInteriorRingN(h);
                    if (Math.Abs(NetTopologySuite.Algorithm.Area.OfRingSigned(hr.Coordinates)) < 1e-3) continue;   // 1000 cm² 안 묻힌 섬은 브레이크라인으로 안 넣는다(합성 max/min이 맡는다)
                    var hl = new List<Point3>();
                    foreach (var c in hr.Coordinates) if (hl.Count == 0 || Math.Abs(hl[^1].X - c.X - ox) > 1e-7 || Math.Abs(hl[^1].Y - c.Y - oy) > 1e-7) hl.Add(new Point3(c.X + ox, c.Y + oy, Gw(c.X, c.Y) ?? 0));
                    if (hl.Count >= 4) { R.Lines.Add(hl); holeLines.Add(hl); }
                }
                // 이 성분 안의 줄 · 끝 선(자른 것)
                var zpp = zp;
                var ringHash = new Dictionary<(long, long), Point3>();
                foreach (var q in clean.Concat(holeLines.SelectMany(h => h))) ringHash[((long)Math.Floor((q.X - ox) / 1e-6), (long)Math.Floor((q.Y - oy) / 1e-6))] = q;
                foreach (var l in raw.Skip(1))
                {
                    if (l.Count < 2) continue;
                    bool isRow = rowSet.Contains(l);
                    int ei = eLines.FindIndex(e => ReferenceEquals(e.E, l));
                    bool isE = ei >= 0, inner = isE && eLines[ei].Inner;
                    if (!isRow && !isE) continue;
                    Geometry cut;
                    try { cut = gf.CreateLineString(l.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray()).Intersection(zpp); } catch { continue; }
                    var lm2 = new LineMerger(); lm2.Add(cut);
                    foreach (var x in lm2.GetMergedLineStrings())
                    {
                        if (x is not LineString ls || ls.Length < 1e-4) continue;
                        var pts = new List<Point3>();
                        foreach (var c in ls.Coordinates)
                        {
                            double wx = c.X + ox, wy = c.Y + oy, z = isRow ? l[0].Z : ZAlong(l, wx, wy);
                            // 자른 끝이 고리 꼭짓점과 1µm 안이면 그 꼭짓점으로(교차 계산 부스러기 — 0.1 mm 안 점 쌍 · 바늘이 되지 않게)
                            var kk = ((long)Math.Floor(c.X / 1e-6), (long)Math.Floor(c.Y / 1e-6));
                            for (long ia = -1; ia <= 1; ia++) for (long ib = -1; ib <= 1; ib++)
                                    if (ringHash.TryGetValue((kk.Item1 + ia, kk.Item2 + ib), out var rv) && Math.Abs(rv.X - wx) <= 1e-6 && Math.Abs(rv.Y - wy) <= 1e-6) { wx = rv.X; wy = rv.Y; }
                            if (pts.Count > 0 && Math.Abs(pts[^1].X - wx) < 1e-7 && Math.Abs(pts[^1].Y - wy) < 1e-7) continue;
                            pts.Add(new Point3(wx, wy, z));
                        }
                        if (pts.Count < 2) continue;
                        if (isRow) R.Rows.Add(pts); else if (inner) R.EndFaces.Add(pts);
                        R.Lines.Add(pts);
                    }
                }
            }
            part.FootMax = 0;
            foreach (var zr2 in (part.Up ? lfC : lfW))
                foreach (var q in zr2)
                {
                    var nr = pl.Near(q.X - ox, q.Y - oy);
                    if (nr.D > part.FootMax) { part.FootMax = nr.D; part.FootAt = (q.X, q.Y); }
                }
            log.Append(string.Format(Ci, "  ② 부분 {0} {1} {2:F2}~{3:F2} m · 기준 {4:F3} · 단높이 {5:0.##} · 소단 {6:0.##} · 면 {7:0.###} · 줄기 {8}({9}) · 줄 바깥 {10:F2} m · 선 바깥 최대 {11:F3} m\n",
                partNo, part.Up ? "절토" : "성토", part.S0, part.S1, part.Zref, H, T, f, stems.Count,
                string.Join(",", stems.Select(s => $"j{s.J}×{s.Tiers}")), dMax, part.FootMax));
            // 면 토막(Faces)
            BuildFaces(R, part, partNo, tierRows, eLines, pl, ox, oy, sg, side, f, T, P0At, Gw, gf, (part.Up ? lfC : lfW));
        }
        R.ZoneF = lfW; R.ZoneC = lfC;
        foreach (var p in parts) if (p.FootMax > R.FootMax) { R.FootMax = p.FootMax; R.FootAt = p.FootAt; }
        R.Parts = parts;

        // ⑥ 겹침 — 절 · 성 자리끼리 · 막을 다각형 · 옛 사면 쪽(N)
        var zonesG = new List<(bool Up, Polygon P)>();
        foreach (var r in lfW) { var pg = BoundaryCutPoly(gf, r, ox, oy); if (pg != null) zonesG.Add((false, pg)); }
        foreach (var r in lfC) { var pg = BoundaryCutPoly(gf, r, ox, oy); if (pg != null) zonesG.Add((true, pg)); }
        // 가름점 C 곁 끝 띠끼리 µm로 겹친 조각(1 cm² 안)은 절토 자리에서 뺀다 — 두 자리는 C에서만 만난다(S199 ④)
        for (int i = 0; i < zonesG.Count; i++)
        {
            if (!zonesG[i].Up) continue;
            for (int j = 0; j < zonesG.Count; j++)
            {
                if (zonesG[j].Up) continue;
                double ov0 = 0; try { ov0 = zonesG[i].P.Intersection(zonesG[j].P).Area; } catch { }
                if (ov0 <= 0 || ov0 > 1e-4) continue;
                Polygon? dpg = null; try { dpg = NtsSupport.LargestPolygon(zonesG[i].P.Difference(zonesG[j].P)); } catch { }
                if (dpg == null || dpg.Area < zonesG[i].P.Area - 1e-3) continue;
                zonesG[i] = (true, dpg);
                var dc = dpg.ExteriorRing.Coordinates; var nr2 = new List<Point3>();
                for (int q = 0; q + 1 < dc.Length; q++) { var nn = pl.Near(dc[q].X, dc[q].Y); nr2.Add(new Point3(dc[q].X + ox, dc[q].Y + oy, nn.D < 1e-9 ? P0At(nn.S) : Gw(dc[q].X, dc[q].Y) ?? P0At(nn.S))); }
                lfC[i - lfW.Count] = nr2;
            }
        }
        // ★[v114.0 · 설치 전 검토 MEDIUM] 겹침을 못 재면(위상 예외) 0으로 넘기지 않는다 — Buffer(0)로 한 번 더 · 그래도 못 재면 거절
        static Geometry? Ov(Geometry a, Geometry b)
        {
            try { return a.Intersection(b); } catch { }
            try { return a.Buffer(0).Intersection(b.Buffer(0)); } catch { return null; }
        }
        for (int i = 0; i < zonesG.Count; i++)
            for (int j = i + 1; j < zonesG.Count; j++)
            {
                var og = Ov(zonesG[i].P, zonesG[j].P);
                if (og == null) return No("섞임 겹침", "절토 · 성토 바꿀 자리끼리 겹침을 못 쟀습니다 — 가름점 곁에서 선을 옮기세요", Wd(zonesG[i].P.InteriorPoint.Coordinate));
                if (og.Area > 1e-4) return No("섞임 겹침", string.Format(Ci, "절토 · 성토 바꿀 자리가 {0:F4}㎡ 겹칩니다 — 가름점 곁에서 선을 옮기세요", og.Area), Wd(og.InteriorPoint.Coordinate));
            }
        if (nP != null)
            foreach (var (_, pg) in zonesG)
            {
                var og = Ov(pg, nP);
                if (og == null) return No("옛 사면", "바꿀 자리와 선 안쪽(남는 사면 · 바닥)의 겹침을 못 쟀습니다 — 선 끝을 옮기세요", Wd(pg.InteriorPoint.Coordinate));
                if (og.Area > 1e-3) return No("옛 사면", string.Format(Ci, "바꿀 자리가 선 안쪽(남는 사면 · 바닥) 쪽으로 {0:F3}㎡ 넘어갑니다 — 선 끝을 옮기세요", og.Area), Wd(og.InteriorPoint.Coordinate));
            }
        if (inp.Blockers != null)
            foreach (var (name, br) in inp.Blockers)
            {
                var bp2 = BoundaryCutPoly(gf, br, ox, oy);
                if (bp2 == null) continue;
                foreach (var (_, pg) in zonesG)
                {
                    var og = Ov(pg, bp2);
                    if (og == null) return No("겹침", $"바꿀 자리와 {name}의 겹침을 못 쟀습니다 — 막습니다", Wd(pg.InteriorPoint.Coordinate));
                    if (og.Area > 1e-2) return No("겹침", string.Format(Ci, "바꿀 자리가 {0}과 {1:F2}㎡ 겹칩니다 — 막습니다(자르면 턱이 생긴다)", name, og.Area), Wd(og.InteriorPoint.Coordinate));
                }
            }
        // 정지면이 바꿀 자리를 덮는가(합성은 정지면 삼각형만 바꾼다)
        {
            int miss = 0; (double, double)? at = null;
            foreach (var r in lfW.Concat(lfC)) foreach (var q in r) if (inp.P0(q.X, q.Y) == null && (inp.P0Tris == null || ZOnTris(inp.P0Tris, q.X, q.Y) == null)) { miss++; at ??= (q.X, q.Y); }
            if (miss > 0) return No("정지면 밖", $"바꿀 자리 {miss}점이 정지면_DH 밖입니다(넓은 소단이 정지면 끝을 넘음) — 소단을 줄이거나 선을 옮기세요", at!.Value);
        }

        // ⑦ Poly = Zone ∪ R_out(구멍은 메움 · critic 2차 2)
        Geometry pu;
        try { pu = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(zonesG.Select(z => (Geometry)z.P).Append(cutP).ToList()); }
        catch (Exception ex) { return No("자리", $"바꿀 자리와 R_out을 못 합쳤습니다({ex.GetType().Name})"); }
        if (pu is not Polygon puP) { var lp2 = NtsSupport.LargestPolygon(pu); if (lp2 == null || lp2.Area < pu.Area - 1e-4) return No("자리", $"바꿀 자리 ∪ R_out이 한 다각형이 아닙니다({pu.GeometryType})"); puP = lp2; }
        if (puP.NumInteriorRings > 0) R.Warn.Add(string.Format(Ci, "Poly 구멍 {0}개를 메웠다(옛 경계 밖 · 원지반)", puP.NumInteriorRings));
        var pr0 = puP.ExteriorRing.Coordinates;
        for (int i = 0; i + 1 < pr0.Length; i++) R.Poly.Add(new Point3(pr0[i].X + ox, pr0[i].Y + oy, 0));
        for (int i = 0; i < R.Poly.Count; i++)
        {
            var p = R.Poly[i]; var q = R.Poly[(i + 1) % R.Poly.Count];
            double mx = (p.X + q.X) / 2 - ox, my = (p.Y + q.Y) / 2 - oy;
            R.IsWall.Add(pl.Near(mx, my).D < 1e-6 && pl.Near(p.X - ox, p.Y - oy).D < 1e-6 && pl.Near(q.X - ox, q.Y - oy).D < 1e-6);
        }
        // Poly 테두리(브레이크라인 · z: L = P0 · 그 밖 = G)
        var polyLine = new List<Point3>();
        foreach (var p in R.Poly)
        {
            var nr = pl.Near(p.X - ox, p.Y - oy);
            double z = nr.D < 1e-6 ? P0At(nr.S) : (inp.Ground(p.X, p.Y) ?? P0At(nr.S));
            polyLine.Add(new Point3(p.X, p.Y, z));
        }
        polyLine.Add(polyLine[0]);
        var lines = new List<List<Point3>> { polyLine };
        foreach (var r in lfW.Concat(lfC)) { var cl = new List<Point3>(r) { r[0] }; lines.Add(cl); }
        lines.AddRange(R.Lines);
        R.Lines = lines;

        // ⑧ 벽 삼각형(바꿀 자리 안만 — Civil이 같은 선으로 짓는다) · F
        foreach (var (up, zs, dst) in new[] { (false, lfW, R.WallTrisF), (true, lfC, R.WallTrisC) })
            foreach (var r in zs)
            {
                var zpg = BoundaryCutPoly(gf, r, ox, oy);
                if (zpg == null) continue;
                var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(zpg.Buffer(1e-7));
                var ls = new List<List<Point3>>();
                foreach (var l in R.Lines.Skip(1 + lfW.Count + lfC.Count)) if (l.Count >= 2 && prep.Covers(gf.CreatePoint(new Coordinate(l[l.Count / 2].X - ox, l[l.Count / 2].Y - oy)))) ls.Add(l);
                ls.Add(new List<Point3>(r) { r[0] });   // 고리 높이(L = P0 · F = G)가 이긴다 — 자리 테두리는 늘 W = G(합성 턱 0)
                // 넓이 없는 세모(한 줄에 선 세 점 · 높이 1 nm 안)는 버린다 — 우리 들로네의 부스러기(Civil 면이 아니다)
                foreach (var t in MeshLines(ls, ox, oy))
                {
                    double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                    double lm = Math.Sqrt(Math.Max((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y), Math.Max((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y), (t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y))));
                    if (lm > 0 && ar / lm >= 1e-9) dst.Add(t);
                }
                // F = 고리에서 L 위 점을 뺀 이어진 토막
                var cur = new List<Point3>();
                for (int i = 0; i <= r.Count; i++)
                {
                    var q = r[i % r.Count];
                    bool onL = pl.Near(q.X - ox, q.Y - oy).D < 1e-9;
                    if (!onL) { if (cur.Count == 0 && i > 0) cur.Add(r[(i - 1) % r.Count]); cur.Add(q); }
                    else if (cur.Count > 0) { cur.Add(q); if (cur.Count >= 2) R.Foot.Add(cur); cur = new List<Point3>(); }
                }
                if (cur.Count >= 2) R.Foot.Add(cur);
            }
        // ⑨ 계수 — 0.1 mm 안 점 쌍 · 바늘 세모(Civil 합침 1e-4 · 뒤집힘 ~122 µm)
        {
            var all = new HashSet<(double, double)>();
            foreach (var l in R.Lines) foreach (var q in l) all.Add((q.X, q.Y));
            var cell = new Dictionary<(long, long), List<(double X, double Y)>>();
            foreach (var p in all) { var k = ((long)Math.Floor((p.Item1 - ox) / 1e-4), (long)Math.Floor((p.Item2 - oy) / 1e-4)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(p); }
            foreach (var p in all)
            {
                long kx = (long)Math.Floor((p.Item1 - ox) / 1e-4), ky = (long)Math.Floor((p.Item2 - oy) / 1e-4);
                for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++)
                        if (cell.TryGetValue((kx + a, ky + b), out var l))
                            foreach (var q in l)
                            {
                                if (q.X == p.Item1 && q.Y == p.Item2) continue;
                                if ((q.X - p.Item1) * (q.X - p.Item1) + (q.Y - p.Item2) * (q.Y - p.Item2) < 1e-8 && (q.X, q.Y).CompareTo(p) > 0) R.PairsUnder01++;
                            }
            }
            foreach (var t in R.WallTrisF.Concat(R.WallTrisC))
            {
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X));
                double lm = Math.Sqrt(Math.Max((t.A.X - t.B.X) * (t.A.X - t.B.X) + (t.A.Y - t.B.Y) * (t.A.Y - t.B.Y), Math.Max((t.B.X - t.C.X) * (t.B.X - t.C.X) + (t.B.Y - t.C.Y) * (t.B.Y - t.C.Y), (t.C.X - t.A.X) * (t.C.X - t.A.X) + (t.C.Y - t.A.Y) * (t.C.Y - t.A.Y))));
                if (lm > 0 && ar / lm < 1.22e-4) R.Needles++;
            }
        }
        // ⑩ ★[v114.0 · JACK 1105 소단 높이 관문] 지은 벽 면에서 소단 = Z_k · F = G(1 cm) — U자 선을 넓은 소단이 채우면 아래 단 점이 첫 소단에 끼어들어 소단이 처진다(S198 0.87 m).
        //   우연(바늘 부채꼴 → 판정 3)에 기대지 않고 여기서 막는다 — 그림 · 목록은 안 바뀐다(판정 3과 같은 거절 길 · Civil은 !Ok면 면을 안 짓는다)
        {
            foreach (var fl in R.Foot)
                for (int i = 0; i + 1 < fl.Count; i++)
                {
                    var a = fl[i]; var e = fl[i + 1];
                    int nn = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Y - a.Y) * (e.Y - a.Y)) / 0.5));
                    for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                    {
                        double x = a.X + (e.X - a.X) * u / nn, y = a.Y + (e.Y - a.Y) * u / nn;
                        if (pl.Near(x - ox, y - oy).D < 1e-6) continue;   // L 위(P0) — F가 아니다
                        var g = inp.Ground(x, y);
                        if (g != null) { R.BenchPts.Add((x, y, g.Value, 1)); benchSrc.Add(-1); }
                    }
                }
            var wallAt = TriLookup(R.WallTrisF.Concat(R.WallTrisC).ToList());
            int atKind = 0;
            for (int i = 0; i < R.BenchPts.Count; i++)
            {
                var (x, y, z0, kind) = R.BenchPts[i];
                double? z = wallAt(x, y);
                if (z == null && kind == 0) z = ZOnTris(rawByPart[benchSrc[i]], x, y);
                if (kind == 0) R.BenchN++; else R.FootN++;
                if (z == null)
                {
                    if (kind == 0) { R.BenchMiss++; if (R.BenchSag + R.FootBad == 0 && R.BenchMiss == 1) R.BenchAt = (x, y); } else R.FootMiss++;
                    continue;
                }
                double dz = Math.Abs(z.Value - z0);
                if (dz > 1e-2) { if (kind == 0) R.BenchSag++; else R.FootBad++; }
                if (dz > R.BenchMax) { R.BenchMax = dz; R.BenchAt = (x, y); atKind = kind; }
            }
            // F를 못 읽음(자리 테두리 위 수치 부스러기)은 거절하지 않는다 — 소단에 면이 없음(자리 바깥 고리 안 · 구멍)은 거절
            log.Append(string.Format(Ci, "  ④-b 소단 높이 관문 — 소단 {0}점(틀림 {1} · 면 없음 {2}) · F {3}점(턱 {4} · 못 읽음 {5}) · 최대 차 {6:F1} mm @({7:F2},{8:F2})\n",
                R.BenchN, R.BenchSag, R.BenchMiss, R.FootN, R.FootBad, R.FootMiss, R.BenchMax * 1000, R.BenchAt.X, R.BenchAt.Y));
            if (R.BenchSag + R.BenchMiss + R.FootBad > 0 && !BenchGateOff)
                return No("소단 높이", R.BenchSag + R.FootBad == 0
                    ? string.Format(Ci, "옹벽 소단에 벽 면이 없음({0:F1}, {1:F1} · {2}점) — 소단 폭을 줄이거나 선을 펴세요", R.BenchAt.X, R.BenchAt.Y, R.BenchMiss)
                    : string.Format(Ci, "옹벽 {0} 높이가 맞지 않음({1:F1}, {2:F1} · 차 {3:F0} cm) — 소단 폭을 줄이거나 선을 펴세요",
                        atKind == 0 ? "소단" : "발(F)", R.BenchAt.X, R.BenchAt.Y, R.BenchMax * 100), R.BenchAt);
        }
        R.Ok = true; R.Code = "통과";
        log.Append(string.Format(Ci, "  ③ 바꿀 자리 성토 {0} · 절토 {1} · Poly {2}점 · 선 {3} · 벽 세모 성토 {4} · 절토 {5} · 마구리 {6} · 부분 끝 마구리 {7} · 선 바깥 최대 {8:F3} m @({9:F1},{10:F1})\n",
            lfW.Count, lfC.Count, R.Poly.Count, R.Lines.Count, R.WallTrisF.Count, R.WallTrisC.Count, R.EndFaceN, R.EndCliff, R.FootMax, R.FootAt.X, R.FootAt.Y));
        log.Append($"  ④ 자가검증 — 발 폭 1 mm로 올림 {R.WMinBumped} · L에 합친 점 {R.MergedPts} · 0.1 mm 안 점 쌍 {R.PairsUnder01}(0이어야) · 바늘 세모 {R.Needles}(0이어야)\n");
        if (R.PairsUnder01 > 0) R.Warn.Add($"0.1 mm 안 점 쌍 {R.PairsUnder01}개(Civil이 합친다)");
        if (R.Needles > 0) R.Warn.Add($"바늘 세모 {R.Needles}개(Civil이 뒤집을 수 있다)");
        if (R.EndCliff > 0) R.Warn.Add($"부분 끝에서 벽이 원지반과 떨어진 채 끝나 마구리(폭 {vEps:0.###} m)로 내렸다 {R.EndCliff}곳");
        R.Log = log.ToString();
        return R;
    }

    static double ZAlong(List<Point3> l, double x, double y)
    {
        double bd = double.MaxValue, bz = l[0].Z;
        for (int i = 0; i + 1 < l.Count; i++)
        {
            var a = l[i]; var e = l[i + 1];
            double ex = e.X - a.X, ey = e.Y - a.Y, l2 = ex * ex + ey * ey;
            double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
            double dx = x - (a.X + ex * t), dy = y - (a.Y + ey * t), d2 = dx * dx + dy * dy;
            if (d2 < bd) { bd = d2; bz = a.Z + (e.Z - a.Z) * t; }
        }
        return bz;
    }

    /// <summary>원 벽 삼각형에서 W′ &lt; G′인 조각(지역 좌표) — 원지반 삼각형이 있으면 그와 겹친 볼록 조각마다(두 면 다 1차 → 0 등치선이 정확) · 없으면 함수(1차가 아니면 잘게 나눔).</summary>
    static List<Geometry> ZonePieces(List<Tri> wall, Input inp, double sg, double ox, double oy, GeometryFactory gf, double step)
    {
        var res = new List<Geometry>();
        STRtree<Tri>? gt = null;
        if (inp.GroundTris != null && inp.GroundTris.Count > 0)
        {
            gt = new STRtree<Tri>();
            foreach (var t in inp.GroundTris)
                gt.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox, Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy), t);
            gt.Build();
        }
        static (double, double, double)[] Plane(Point3 a, Point3 b, Point3 c, double ox, double oy, double sg)
            => new[] { (a.X - ox, a.Y - oy, sg * a.Z), (b.X - ox, b.Y - oy, sg * b.Z), (c.X - ox, c.Y - oy, sg * c.Z) };
        static double Bary((double X, double Y, double Z)[] p, double x, double y)
        {
            double d = (p[1].Y - p[2].Y) * (p[0].X - p[2].X) + (p[2].X - p[1].X) * (p[0].Y - p[2].Y);
            if (Math.Abs(d) < 1e-24) return (p[0].Z + p[1].Z + p[2].Z) / 3;
            double l1 = ((p[1].Y - p[2].Y) * (x - p[2].X) + (p[2].X - p[1].X) * (y - p[2].Y)) / d, l2 = ((p[2].Y - p[0].Y) * (x - p[2].X) + (p[0].X - p[2].X) * (y - p[2].Y)) / d;
            return l1 * p[0].Z + l2 * p[1].Z + (1 - l1 - l2) * p[2].Z;
        }
        void Emit(List<(double X, double Y, double D)> poly)
        {
            // d < 0 쪽만(Sutherland–Hodgman · d는 이 볼록 조각 위 1차)
            var o = new List<(double X, double Y)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                // ★ 벽 = 땅(1e-10 m 안)은 자리 밖 — 부분 끝 띠(L 이음 위 W = G)에서 부동소수 부스러기 조각이 생기지 않게
                const double Z0 = 1e-10;
                bool pin = p.D < -Z0, qin = q.D < -Z0;
                if (pin) o.Add((p.X, p.Y));
                if (pin != qin)
                {
                    double t = (p.D + Z0) / (p.D - q.D);
                    o.Add((p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t));
                }
            }
            if (o.Count < 3) return;
            double ar = 0; for (int i = 0; i < o.Count; i++) { var a = o[i]; var b = o[(i + 1) % o.Count]; ar += a.X * b.Y - b.X * a.Y; }
            if (Math.Abs(ar) < 1e-16) return;
            var cs = o.Select(p => new Coordinate(p.X, p.Y)).ToList(); cs.Add(cs[0].Copy());
            try { var pg = gf.CreatePolygon(cs.ToArray()); if (pg.IsValid) res.Add(pg); else res.Add(pg.Buffer(0)); } catch { }
        }
        foreach (var t in wall)
        {
            var W = Plane(t.A, t.B, t.C, ox, oy, sg);
            if (gt != null)
            {
                var env = new Envelope(Math.Min(W[0].Item1, Math.Min(W[1].Item1, W[2].Item1)), Math.Max(W[0].Item1, Math.Max(W[1].Item1, W[2].Item1)),
                                       Math.Min(W[0].Item2, Math.Min(W[1].Item2, W[2].Item2)), Math.Max(W[0].Item2, Math.Max(W[1].Item2, W[2].Item2)));
                foreach (var g in gt.Query(env))
                {
                    var G = Plane(g.A, g.B, g.C, ox, oy, sg);
                    var poly = ClipTri(W, G);
                    if (poly.Count < 3) continue;
                    Emit(poly.Select(p => (p.X, p.Y, Bary(W, p.X, p.Y) - Bary(G, p.X, p.Y))).ToList());
                }
                continue;
            }
            double? Gf(double x, double y) { var v = inp.Ground(x + ox, y + oy); return v == null ? null : sg * v.Value; }
            var g0 = Gf(W[0].Item1, W[0].Item2); var g1 = Gf(W[1].Item1, W[1].Item2); var g2 = Gf(W[2].Item1, W[2].Item2);
            if (g0 == null || g1 == null || g2 == null) continue;
            // 1차인가 — 변 가운데 · 무게중심에서 1e-7 안
            bool lin = true;
            foreach (var (x, y, wgt) in new[] { ((W[0].Item1 + W[1].Item1) / 2, (W[0].Item2 + W[1].Item2) / 2, (g0.Value + g1.Value) / 2), ((W[1].Item1 + W[2].Item1) / 2, (W[1].Item2 + W[2].Item2) / 2, (g1.Value + g2.Value) / 2),
                                                 ((W[2].Item1 + W[0].Item1) / 2, (W[2].Item2 + W[0].Item2) / 2, (g2.Value + g0.Value) / 2), ((W[0].Item1 + W[1].Item1 + W[2].Item1) / 3, (W[0].Item2 + W[1].Item2 + W[2].Item2) / 3, (g0.Value + g1.Value + g2.Value) / 3) })
            { var gm = Gf(x, y); if (gm == null || Math.Abs(gm.Value - wgt) > 1e-7) { lin = false; break; } }
            double me = Math.Max(Math.Max(Dist(W[0], W[1]), Dist(W[1], W[2])), Dist(W[2], W[0]));
            int n = lin ? 1 : Math.Clamp((int)Math.Ceiling(me / step), 1, 32);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n - i; j++)
                {
                    foreach (var tri in n == 1 ? new[] { ((0, 0), (1, 0), (0, 1)) } : j < n - i - 1 ? new[] { ((i, j), (i + 1, j), (i, j + 1)), ((i + 1, j), (i + 1, j + 1), (i, j + 1)) } : new[] { ((i, j), (i + 1, j), (i, j + 1)) })
                    {
                        var poly = new List<(double X, double Y, double D)>();
                        bool bad = false;
                        foreach (var (u, v) in new[] { tri.Item1, tri.Item2, tri.Item3 })
                        {
                            double a = (double)u / n, b = (double)v / n;
                            double x = W[0].Item1 + (W[1].Item1 - W[0].Item1) * a + (W[2].Item1 - W[0].Item1) * b;
                            double y = W[0].Item2 + (W[1].Item2 - W[0].Item2) * a + (W[2].Item2 - W[0].Item2) * b;
                            double w = W[0].Item3 + (W[1].Item3 - W[0].Item3) * a + (W[2].Item3 - W[0].Item3) * b;
                            double? gg = n == 1 ? (u == 0 && v == 0 ? g0 : u == 1 ? g1 : g2) : Gf(x, y);
                            if (gg == null) { bad = true; break; }
                            poly.Add((x, y, w - gg.Value));
                        }
                        if (!bad) Emit(poly);
                    }
                }
        }
        return res;
        static double Dist((double, double, double) a, (double, double, double) b) => Math.Sqrt((a.Item1 - b.Item1) * (a.Item1 - b.Item1) + (a.Item2 - b.Item2) * (a.Item2 - b.Item2));
    }

    /// <summary>두 삼각형(지역 xy)의 겹친 볼록 다각형.</summary>
    static List<(double X, double Y)> ClipTri((double X, double Y, double Z)[] a, (double X, double Y, double Z)[] b)
    {
        var poly = new List<(double X, double Y)> { (a[0].X, a[0].Y), (a[1].X, a[1].Y), (a[2].X, a[2].Y) };
        double orient = (b[1].X - b[0].X) * (b[2].Y - b[0].Y) - (b[1].Y - b[0].Y) * (b[2].X - b[0].X);
        if (Math.Abs(orient) < 1e-24) return new();
        double sgn = Math.Sign(orient);
        for (int e = 0; e < 3 && poly.Count >= 3; e++)
        {
            var p0 = b[e]; var p1 = b[(e + 1) % 3];
            double F((double X, double Y) q) => sgn * ((p1.X - p0.X) * (q.Y - p0.Y) - (p1.Y - p0.Y) * (q.X - p0.X));
            var o = new List<(double X, double Y)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                double fp = F(p), fq = F(q);
                if (fp >= 0) o.Add(p);
                if ((fp >= 0) != (fq >= 0)) { double t = fp / (fp - fq); o.Add((p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t)); }
            }
            poly = o;
        }
        return poly;
    }

    /// <summary>★[v114.0 · 계획 2절] 면 토막 — 줄기 · 단마다 앞면(머리 = 앞 단 소단 끝 줄 · 첫 면은 L 위 P0 · 발 = 면 폭 f 바깥의 min/max(높이, G)) · 줄기 갈림마다 가로 마구리. 바꿀 자리 안만.</summary>
    static void BuildFaces(Built R, Part part, int partNo, List<List<(double D, double Z, List<List<Coordinate>> Pc)>> tierRows,
        List<(int Stem, bool AtStart, bool Inner, List<Point3> E)> eLines, PL pl, double ox, double oy, double sg, double side, double f, double T,
        Func<double, double> p0At, Func<double, double, double?> gw, GeometryFactory gf, List<List<Point3>> zones)
    {
        var zp = zones.Select(r => BoundaryCutPoly(gf, r, ox, oy)).Where(p => p != null).Cast<Geometry>().ToList();
        if (zp.Count == 0) return;
        Geometry zu = zp.Count == 1 ? zp[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(zp);
        var zb = zu.Buffer(1e-6);
        double zr = sg * part.Zref;
        double LvN(int k) => zr + k * part.H;   // 절토 꼴
        List<LineString> Clip(Coordinate[] cs)
        {
            var o = new List<LineString>();
            if (cs.Length < 2) return o;
            Geometry cut;
            try { cut = gf.CreateLineString(cs).Intersection(zb); } catch { return o; }
            var lm = new LineMerger(); lm.Add(cut);
            foreach (var x in lm.GetMergedLineStrings()) if (x is LineString ls && ls.Length >= 1e-3) o.Add(ls);
            return o;
        }
        for (int k = 0; k < part.Stems.Count; k++)
        {
            var st = part.Stems[k];
            for (int t = 1; t <= st.Tiers; t++)
            {
                double levN = LvN(st.J + t - 1);
                List<Coordinate[]> tops;
                if (t == 1) tops = new() { pl.Sub(st.S0, st.S1) };
                else
                {
                    double dTop = (t - 1) * (f + T);
                    var near = tierRows[k].FirstOrDefault(r => Math.Abs(r.D - dTop) < 1e-9);
                    if (near.Pc == null) continue;
                    tops = near.Pc.Select(p => p.ToArray()).ToList();
                }
                foreach (var tc in tops)
                    foreach (var ls in Clip(tc))
                    {
                        var fs = new FaceSeg { Up = part.Up, Tier = t, Level = sg * levN, Kind = 0, Part = partNo, Len = ls.Length };
                        foreach (var c in ls.Coordinates)
                        {
                            var nr = pl.Near(c.X, c.Y);
                            var (tx, ty) = pl.Tan(nr.S);
                            double nx = -ty * side, ny = tx * side;
                            double zt = t == 1 ? p0At(nr.S) : sg * LvN(st.J + t - 2);
                            double fx = c.X + nx * f, fy = c.Y + ny * f;
                            var g = gw(fx, fy);
                            double footN = g == null ? levN : Math.Min(levN, sg * g.Value);
                            fs.Top.Add(new Point3(c.X + ox, c.Y + oy, zt));
                            fs.Foot.Add(new Point3(fx + ox, fy + oy, sg * footN));
                            fs.HMax = Math.Max(fs.HMax, Math.Abs(zt - sg * footN));
                        }
                        if (fs.HMax > 1e-4) R.Faces.Add(fs);
                    }
            }
        }
        // 가로 마구리(줄기 갈림 S마다) — 절토 꼴로 높은 쪽(J가 큰 쪽) 끝 선이 머리 · 발 = 낮은 쪽 끝 선과 땅 중 (절토 꼴) 낮은 것
        for (int k = 0; k + 1 < part.Stems.Count; k++)
        {
            var ea = eLines.First(e => e.Stem == k && !e.AtStart).E; var eb = eLines.First(e => e.Stem == k + 1 && e.AtStart).E;
            bool aHi = part.Stems[k].J > part.Stems[k + 1].J;
            var hi = aHi ? ea : eb; var lo = aHi ? eb : ea;
            foreach (var ls in Clip(hi.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray()))
            {
                var fs = new FaceSeg { Up = part.Up, Tier = 0, Kind = 1, Part = partNo, Len = ls.Length };
                double topN = double.MinValue;
                foreach (var c in ls.Coordinates)
                {
                    double wx = c.X + ox, wy = c.Y + oy;
                    double zt = ZAlong(hi, wx, wy), zl = ZAlong(lo, wx, wy);
                    var g = gw(c.X, c.Y);
                    double footN = Math.Min(sg * zl, g == null ? sg * zl : sg * g.Value);
                    fs.Top.Add(new Point3(wx, wy, zt)); fs.Foot.Add(new Point3(wx, wy, sg * footN));
                    fs.HMax = Math.Max(fs.HMax, Math.Abs(zt - sg * footN));
                    topN = Math.Max(topN, sg * zt);
                }
                fs.Level = sg * topN;   // 표시용(절토 꼴로 가장 높은 머리)
                if (fs.HMax > 1e-4) R.Faces.Add(fs);
            }
        }
    }

    /// <summary>★[v114.0] 판정 + 짓기를 한 번에 — 화면 없는 탐침 · 하네스 · 다시 짓기가 같은 길로 부른다(그린 선 원본 → 자르기 → 기하).</summary>
    public static (BoundaryCut.Result Cut, Built? Built) Make(IReadOnlyList<Point3> src, IReadOnlyList<Point3> bd, IReadOnlyList<Point3> plan,
        IEnumerable<(string Name, IReadOnlyList<Point3> Ring)>? blockers, double hc, double tc, double hf, double tf, double slopeW, double faceRun, double grid, double reach,
        Func<double, double, double?> ground, Func<double, double, double?> p0, IReadOnlyList<Tri>? p0Tris, IReadOnlyList<Tri>? groundTris = null)
    {
        var bl = blockers?.ToList();
        var cut = BoundaryCut.Classify(src, bd, plan, bl);
        if (!cut.Ok) return (cut, null);
        var b = Build(new Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = plan, Hc = hc, Tc = tc, Hf = hf, Tf = tf, SlopeW = slopeW, FaceRun = faceRun, Grid = grid, Reach = reach,
            Ground = ground, P0 = p0, P0Tris = p0Tris, GroundTris = groundTris, Blockers = bl,
        });
        return (cut, b);
    }

    /// <summary>★[v114.0 · 계획 5절 «같은 자리»] 정지면을 다시 지은 뒤 제자리인가 — 두 끝 A · B가 1 mm 안 · 제원(H · T 절 · 성)이 같다. 비었으면 제자리.</summary>
    public static string SamePlace(WallRec w, BoundaryCut.Result cut)
    {
        if (w.Line.Count < 2) return "저장한 선이 없다";
        double dA = Math.Sqrt((w.Line[0].X - cut.A.X) * (w.Line[0].X - cut.A.X) + (w.Line[0].Y - cut.A.Y) * (w.Line[0].Y - cut.A.Y));
        double dB = Math.Sqrt((w.Line[^1].X - cut.B.X) * (w.Line[^1].X - cut.B.X) + (w.Line[^1].Y - cut.B.Y) * (w.Line[^1].Y - cut.B.Y));
        var why = new List<string>();
        if (dA > 1e-3 || dB > 1e-3) why.Add(string.Format(Ci, "경계 교점이 움직임(A {0:F3} · B {1:F3}m)", dA, dB));
        if (w.Line.Count != cut.Line.Count) why.Add($"선 점 수 {w.Line.Count} → {cut.Line.Count}");
        return string.Join(" · ", why);
    }

    /// <summary>★[v114.0 · 설치 전 검토 7] 다시 지은 결과가 저장한 값과 10 % 넘게 다르면 ⚠ 한 줄(바꿀 자리 넓이 · 선 바깥 최대) — 빈 값이면 비슷함. 막지는 않는다.</summary>
    public static string Drift(WallRec w, Built b)
    {
        static double Area(IEnumerable<List<Point3>> rs) => rs.Sum(r => { double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.X * q.Y - q.X * p.Y; } return Math.Abs(a) / 2; });
        double a0 = Area(w.ZoneF.Concat(w.ZoneC)), a1 = Area(b.ZoneF.Concat(b.ZoneC));
        var why = new List<string>();
        if (Math.Abs(a1 - a0) > 0.1 * Math.Max(a0, 1e-9)) why.Add(string.Format(Ci, "바꿀 자리 넓이 {0:F1} → {1:F1}㎡", a0, a1));
        if (Math.Abs(b.FootMax - w.FootMax) > 0.1 * Math.Max(w.FootMax, 1e-9)) why.Add(string.Format(Ci, "선 바깥 최대 {0:F2} → {1:F2} m", w.FootMax, b.FootMax));
        return why.Count == 0 ? "" : "⚠다시 지은 옹벽이 저장한 값과 10 % 넘게 다름(" + string.Join(" · ", why) + ")";
    }

    /// <summary>★[v114.0] 목록 칸에서 면 토막(소비처가 부른다) — 종류 2만 · 그 밖은 빈 목록.</summary>
    public static IReadOnlyList<FaceSeg> Faces(WallRec w) => w.Kind == 2 ? w.Faces : Array.Empty<FaceSeg>();
}
