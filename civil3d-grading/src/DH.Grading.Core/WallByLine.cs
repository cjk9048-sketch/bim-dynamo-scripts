using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Polygonize;

namespace DH.Grading.Core;

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] <b>경계 폴리선 옹벽</b>(옹벽 목록 종류 1)의 기하 — 계획 3장.
/// <code>
///   옛 경계(데이라잇) ──A━━━━━━━━━━━━━B── R_out(원지반으로)
///                       │▒ 맨 바깥 벽 ▒│   ← 폴리선 위
///                  측선 │▒ 단 · 소단  ▒│ 측선(수직 마감)
///                       │   바닥 ───   │
///   계획선 ────────────A′──────────────B′───
/// </code>
/// <para>단 수는 폴리선 전체에 하나(절토 = 폴리선 위 원지반 − 바닥 최대 · 성토 = 최소 · JACK 답 1). 단높이마다 폴리선에서 소단만큼 안으로 물러나고, 바닥(그 자리 계획면 높이)에 닿으면 멈춘다.
/// <b>바닥이 기울면 단도 따라 기운다</b>(JACK 답 4) — 줄의 점마다 높이 = 가장 가까운 계획선 점의 높이 ± (남은 단 수 × 단높이).</para>
/// <para>양 끝은 측선(A → A′ · B → B′)에서 수직으로 막는다(JACK 답 2) — 측선은 합성 전 면(P0) 높이 그대로, 그 안쪽 vEps에 단 높이 단면을 한 줄 더 둔다.
/// 단이 다 안 들어가면 막는다(JACK 답 3). 절 · 성이 섞이면 이번 판은 막는다(계획 3-6의 가르기는 다음 판 — 까닭과 자리를 알린다).</para>
/// <para>가상옹벽 면은 Civil이 아래 <see cref="Built.Lines"/>(브레이크라인 — 첫 줄이 바꿀 자리 Z의 닫힌 테두리)로 짓고 Z로 가둔다. 하네스는 <see cref="Mesh"/>로 같은 선을 삼각형으로 잇는다.</para></summary>
public static class WallByLine
{
    public sealed class Input
    {
        /// <summary>BoundaryCut 결과의 A..B 폴리선 · 경계 · 새 경계 · 잘라낼 자리.</summary>
        public IReadOnlyList<Point3> Line = Array.Empty<Point3>(), Bd = Array.Empty<Point3>(), NewRing = Array.Empty<Point3>(), CutRing = Array.Empty<Point3>();
        /// <summary>계획선(부지 바닥 고리 · z = 그 자리 계획면 높이).</summary>
        public IReadOnlyList<Point3> Plan = Array.Empty<Point3>();
        public double H = 5, T = 1, SlopeW = 0.01, FaceRun = 0.05, Grid = 1, Margin = 10;
        public Func<double, double, double?> Ground = (_, _) => null;
        /// <summary>합성 전 면(P0) 높이 — 측선 높이(삼각형이 있으면 변 건넘까지 정확히).</summary>
        public Func<double, double, double?> P0 = (_, _) => null;
        public IReadOnlyList<WallDaylight.Tri>? P0Tris;
        public IEnumerable<(string Name, IReadOnlyList<Point3> Ring)>? Blockers;
        /// <summary>★[v113.0 · 설치 전 검토 M5] R_out 여유(경계 밖)가 비켜 갈 다각형 — 다른 옹벽 폴리곤(여유 포함).</summary>
        public IEnumerable<IReadOnlyList<Point3>>? MarginClip;
    }

    public sealed class Built
    {
        public bool Ok; public string Code = "", Why = "";
        public List<(double X, double Y)> Marks = new();
        public bool Up; public int Tiers;
        public double PadMin, PadMax, HeadZ, CapZ, Need, Narrowest, Face, Step, VEps, GroundTop;
        /// <summary>★[v113.0] 측선 수직 마감 폭(옹벽 면과 같은 1:0.01 · 단 n개 높이 · 최소 vEps).</summary>
        public double SideW;
        public Point3 A, B, A2, B2;
        /// <summary>바꿀 자리 Z(= 띠 S ∪ R_out(+여유)) · 첫 점 반복 없음 · z = HeadZ(목록 Poly) · 점마다 측선인가.</summary>
        public List<Point3> Zone = new(); public List<bool> ZoneSide = new();
        public List<Point3> Strip = new(), CutExt = new();
        /// <summary>줄(0번 = 폴리선 · 단마다 면 끝 · 소단 끝) — 점마다 높이. 터파기 · 횡단 표시가 쓴다.</summary>
        public List<List<Point3>> Rows = new();
        /// <summary>Civil 가상옹벽 브레이크라인 — [0] = Z 테두리(닫힘 · 높이) · 줄 · 측선 안 단면 둘 · 뚜껑 줄.</summary>
        public List<List<Point3>> Lines = new();
        public bool MarginUsed;
        public List<string> Warn = new();
        public string Log = "";
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

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

    /// <summary>선분 a→b 위 P0 높이 — 삼각형 변을 건너는 자리마다 점(정확한 꺾은선). 못 읽은 점이 있으면 null.</summary>
    /// <param name="edgeFb">★[v113.0 · 설치 전 검토 열린 질문] 끝 점(경계 위 A · B)이 정지면 삼각망 바깥 µm에 떨어져 못 읽을 때 — 선분 안으로 1 mm 민 자리 → 그래도 못 읽으면 이 값(데이라잇 = 원지반).</param>
    public static List<Point3>? ProfileOnTris(Point3 a, Point3 b, IReadOnlyList<WallDaylight.Tri>? tris, Func<double, double, double?> f, double grid,
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
                if (zn != null) z = zn;   // 1 mm 안쪽 값(삼각망이 1차라 차이 ≤ 기울기 × 1 mm)
            }
            if (z == null && edgeFb != null) z = edgeFb(x, y);
            if (z == null) return null;
            r.Add(new Point3(x, y, z.Value));
        }
        return r;
    }

    static double? ZOnTris(IReadOnlyList<WallDaylight.Tri> tris, double x, double y)
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

    /// <summary>고리의 두 자리 사이 앞으로 가는 호(두 끝 포함 · 높이는 그 변에서 잇는다).</summary>
    static List<Point3> RingArc(IReadOnlyList<Point3> ring, (int J, double T) p0, (int J, double T) p1)
    {
        int n = ring.Count; if (n >= 2 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y) n--;
        Point3 At((int J, double T) p) { var a = ring[p.J]; var b = ring[(p.J + 1) % n]; return new Point3(a.X + (b.X - a.X) * p.T, a.Y + (b.Y - a.Y) * p.T, a.Z + (b.Z - a.Z) * p.T); }
        var r = new List<Point3> { At(p0) };
        if (!(p0.J == p1.J && p1.T >= p0.T))
        {
            int j = p0.J;
            for (int g = 0; g <= n + 1; g++) { j = (j + 1) % n; r.Add(ring[j]); if (j == p1.J) break; }
        }
        r.Add(At(p1));
        var o = new List<Point3>();
        foreach (var q in r) if (o.Count == 0 || Math.Abs(o[^1].X - q.X) > 1e-9 || Math.Abs(o[^1].Y - q.Y) > 1e-9) o.Add(q);
        return o;
    }

    static double SegDist(double x, double y, Point3 a, Point3 b)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
        double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
        double dx = x - (a.X + ex * t), dy = y - (a.Y + ey * t);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>★[v113.0] 기하를 짓는다 — 못 지으면 Ok = 거짓 · 까닭(코드 · 글 · 자리). 아무것도 안 바꾼다(순수 셈).</summary>
    public static Built Build(Input inp)
    {
        var R = new Built();
        var log = new System.Text.StringBuilder();
        Built No(string code, string why, params (double, double)[] at) { R.Ok = false; R.Code = code; R.Why = why; R.Marks.AddRange(at); R.Log = log.ToString(); return R; }
        var L = inp.Line;
        if (L == null || L.Count < 2) return No("재료", "폴리선이 없습니다");
        if (inp.Plan == null || inp.Plan.Count < 3) return No("재료", "계획선이 없습니다");
        double H = inp.H, T = Math.Max(0, inp.T);
        if (H <= 1e-6) return No("재료", "단높이가 0입니다");
        double face = Math.Max(H * Math.Max(0, inp.SlopeW), Math.Max(1e-4, inp.FaceRun));
        double step = face + T;
        double vEps = Math.Max(1e-3, inp.FaceRun);
        R.Face = face; R.Step = step; R.VEps = vEps;
        R.A = L[0]; R.B = L[^1];
        double ox = R.A.X, oy = R.A.Y;
        var gf = new GeometryFactory();
        Coordinate C(Point3 q) => new(q.X - ox, q.Y - oy);
        (double, double) Wd(Coordinate c) => (c.X + ox, c.Y + oy);
        double ZB(double x, double y) => Nearest(inp.Plan, x, y).Z;

        // ① 절토 · 성토 — 폴리선 위 원지반 − 바닥(Grid 간격)
        var dense = new List<Point3>();
        for (int i = 0; i + 1 < L.Count; i++)
        {
            double dx = L[i + 1].X - L[i].X, dy = L[i + 1].Y - L[i].Y, l = Math.Sqrt(dx * dx + dy * dy);
            int k = Math.Max(1, (int)Math.Ceiling(l / Math.Max(0.25, inp.Grid)));
            for (int q = 0; q < k; q++) dense.Add(new Point3(L[i].X + dx * q / k, L[i].Y + dy * q / k, 0));
        }
        dense.Add(L[^1]);
        int pos = 0, neg = 0, miss = 0; double dMax = double.MinValue, dMin = double.MaxValue; (double, double)? firstPos = null, firstNeg = null;
        foreach (var q in dense)
        {
            var g = inp.Ground(q.X, q.Y);
            if (g == null) { miss++; continue; }
            double d = g.Value - ZB(q.X, q.Y);
            dMax = Math.Max(dMax, d); dMin = Math.Min(dMin, d);
            if (d > 0.01) { pos++; firstPos ??= (q.X, q.Y); }
            else if (d < -0.01) { neg++; firstNeg ??= (q.X, q.Y); }
        }
        log.Append(string.Format(Ci, "  ① 폴리선 {0}점(표본 {1} · 원지반 못 읽음 {2}) — 원지반 − 바닥 {3:F2} ~ {4:F2}m · 절토 표본 {5} · 성토 표본 {6}\n",
            L.Count, dense.Count, miss, dMin == double.MaxValue ? 0 : dMin, dMax == double.MinValue ? 0 : dMax, pos, neg));
        if (pos + neg == 0) return No("원지반", miss == dense.Count ? "폴리선 위 원지반을 못 읽었습니다(측량 범위 밖?)" : "폴리선 위 원지반이 바닥과 같은 높이입니다 — 옹벽이 설 자리가 없습니다");
        if (pos > 0 && neg > 0)
        {
            var at = pos >= neg ? firstNeg!.Value : firstPos!.Value;
            return No("섞임", string.Format(Ci, "폴리선을 따라 절토 · 성토가 섞입니다(섞인 자리 ({0:F1}, {1:F1})) — 이번 판은 한쪽만 됩니다. 절토 구간 · 성토 구간을 따로 그리세요", at.Item1, at.Item2), at);
        }
        R.Up = pos > 0;
        double sgn = R.Up ? 1.0 : -1.0;
        R.GroundTop = R.Up ? dMax : dMin;
        int n = R.Up ? WallInPoly.BenchCount(0, dMax, H) : WallInPoly.BenchCountDown(0, dMin, H);
        R.Tiers = n;
        R.Need = (n - 1) * step + face + 2 * vEps;

        // ② A′ · B′ · 띠 S
        var nA = Nearest(inp.Plan, R.A.X, R.A.Y); var nB = Nearest(inp.Plan, R.B.X, R.B.Y);
        R.A2 = new Point3(nA.X, nA.Y, nA.Z); R.B2 = new Point3(nB.X, nB.Y, nB.Z);
        // ★[v113.0 · 화면 없는 Civil 시험 1104] 폴리선 첫 · 끝 토막이 측선(교점 → 가장 가까운 계획선 점)을 따라 들어오면 띠가 0폭으로 접힌다(«ㄷ» 꼴) —
        //   «측선이 부지를 가로지름»이 아니라 그 까닭으로 알린다(그 다리에 벽을 어떻게 세울지는 정하지 않았다 — 비스듬히 지나게)
        {
            (bool, (double, double)) Along(Point3 e, Point3 e2, Point3 next)
            {
                double sx = e2.X - e.X, sy = e2.Y - e.Y, sl = Math.Sqrt(sx * sx + sy * sy);
                double lx = next.X - e.X, ly = next.Y - e.Y, ll = Math.Sqrt(lx * lx + ly * ly);
                if (sl < 1e-9 || ll < 1e-9) return (false, (0, 0));
                double cos = (sx * lx + sy * ly) / (sl * ll);
                return (cos > Math.Cos(5 * Math.PI / 180), (e.X, e.Y));   // 5° 안 · 같은 방향
            }
            var (ba, aa) = Along(R.A, R.A2, L[1]); var (bb, ab) = Along(R.B, R.B2, L[^2]);
            if (ba || bb)
                return No("측선 겹침", "폴리선이 경계에서 계획선 쪽으로 곧게(5° 안) 들어와 측선과 겹칩니다 — 경계를 비스듬히 지나게 그리세요", ba ? aa : ab);
        }
        var planP = BoundaryCutPoly(gf, inp.Plan, ox, oy);
        if (planP == null) return No("재료", "계획선을 다각형으로 못 만들었습니다");
        var arc1 = RingArc(inp.Plan, (nB.J, nB.T), (nA.J, nA.T));             // B′ → A′ 앞으로
        var arc2 = RingArc(inp.Plan, (nA.J, nA.T), (nB.J, nB.T)); arc2.Reverse();   // B′ → A′ 뒤로
        Polygon? StripOf(List<Point3> arc, out List<Point3> ringPts)
        {
            ringPts = new List<Point3>();
            foreach (var q in L) ringPts.Add(new Point3(q.X, q.Y, 0));
            foreach (var q in arc) if (Math.Abs(ringPts[^1].X - q.X) > 1e-9 || Math.Abs(ringPts[^1].Y - q.Y) > 1e-9) ringPts.Add(q);
            if (Math.Abs(ringPts[^1].X - ringPts[0].X) < 1e-9 && Math.Abs(ringPts[^1].Y - ringPts[0].Y) < 1e-9) ringPts.RemoveAt(ringPts.Count - 1);
            var cs = ringPts.Select(C).Append(C(ringPts[0])).ToArray();
            try { var p = gf.CreatePolygon(cs); return p.IsValid ? p : null; } catch { return null; }
        }
        var s1 = StripOf(arc1, out var r1); var s2 = StripOf(arc2, out var r2);
        double ov1 = s1 == null ? double.MaxValue : s1.Intersection(planP).Area, ov2 = s2 == null ? double.MaxValue : s2.Intersection(planP).Area;
        Polygon? S; List<Point3> sRing;
        if (ov1 <= ov2) { S = s1; sRing = r1; } else { S = s2; sRing = r2; }
        double ovS = Math.Min(ov1, ov2);
        if (S == null || ovS > 1e-3 + 1e-6 * planP.Area)
            return No("측선", "측선(폴리선 끝 → 가장 가까운 계획선 점)이 부지를 가로지르거나 띠가 꼬입니다 — 폴리선 끝을 옮기세요", Wd(C(R.A)), Wd(C(R.B)));
        R.Strip = sRing;
        // ★[v113.0 · 설치 전 검토 M2] 절 · 성은 폴리선 위만이 아니라 띠 S 안 · 두 측선에서도 — 바꿔 끼움(min(W, G))은 P0를 안 보므로
        //   띠 안에 성토(원지반 < 바닥) 자리가 있으면 그 성토가 사라진다(낭떠러지 · 합성 판정 3). 폴리선과 같은 «섞임» 거절로 막고 자리를 알린다
        {
            double gs = Math.Max(0.5, inp.Grid);
            (double, double)? bad = null; int nS = 0;
            void Probe(double x, double y)
            {
                if (bad != null) return;
                var g = inp.Ground(x, y);
                if (g == null) return;
                nS++;
                double dz = g.Value - ZB(x, y);
                if (R.Up ? dz < -0.01 : dz > 0.01) bad = (x, y);
            }
            double sx0 = sRing.Min(q => q.X), sx1 = sRing.Max(q => q.X), sy0 = sRing.Min(q => q.Y), sy1 = sRing.Max(q => q.Y);
            for (double x = sx0 + gs / 2; x < sx1 && bad == null; x += gs)
                for (double y = sy0 + gs / 2; y < sy1 && bad == null; y += gs)
                    if (GradingGeometry.PointInRing(sRing, x, y)) Probe(x, y);
            foreach (var (a, b) in new[] { (R.A, R.A2), (R.B, R.B2) })
            {
                double l = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int k = Math.Max(1, (int)Math.Ceiling(l / gs));
                for (int q = 1; q < k; q++) Probe(a.X + (b.X - a.X) * q / k, a.Y + (b.Y - a.Y) * q / k);   // 두 끝(경계 · 계획선 위)은 뺀다
            }
            log.Append(string.Format(Ci, "  ②-b 띠 · 측선 절성 표본 {0}{1}\n", nS, bad == null ? "" : string.Format(Ci, " · ⚠{0} 아닌 자리 ({1:F1}, {2:F1})", R.Up ? "절토" : "성토", bad.Value.Item1, bad.Value.Item2)));
            if (bad != null)
                return No("섞임", string.Format(Ci, "폴리선 안쪽 띠(폴리선 ~ 계획선 · 측선)에 {0} 자리가 섞입니다 ({1:F1}, {2:F1}) — 이번 판은 한쪽만 됩니다. 폴리선을 {3} 구간만 덮게 그리세요",
                    R.Up ? "성토" : "절토", bad.Value.Item1, bad.Value.Item2, R.Up ? "절토" : "성토"), bad.Value);
        }
        var nP = BoundaryCutPoly(gf, inp.NewRing, ox, oy);
        if (nP != null && S.Difference(nP.Buffer(1e-3)).Area > 1e-3)
            return No("측선", "측선이 새 경계 밖으로 나갑니다 — 폴리선 끝을 옮기세요", Wd(C(R.A)), Wd(C(R.B)));
        // 바닥 높이 폭(계획선 호)
        R.PadMin = double.MaxValue; R.PadMax = double.MinValue;
        foreach (var q in (ov1 <= ov2 ? arc1 : arc2)) { R.PadMin = Math.Min(R.PadMin, q.Z); R.PadMax = Math.Max(R.PadMax, q.Z); }
        foreach (var q in dense) { double z = ZB(q.X, q.Y); R.PadMin = Math.Min(R.PadMin, z); R.PadMax = Math.Max(R.PadMax, z); }
        bool sloped = R.PadMax - R.PadMin > 1e-6;
        R.HeadZ = R.Up ? R.PadMin + n * H : R.PadMax - n * H;
        log.Append(string.Format(Ci, "  ② {0} · 단 {1}개(단높이 {2:0.##} · 소단 {3:0.##} · 면 {4:0.###} · 한 단 {5:0.###}m) · 바닥 {6:F3}{7} · 머리 {8:F3}m · A′({9:F2},{10:F2}) · B′({11:F2},{12:F2}) · 띠 {13:F1}㎡(계획선과 겹침 {14:F4}㎡)\n",
            R.Up ? "절토" : "성토", n, H, T, face, step, R.PadMin, sloped ? string.Format(Ci, " ~ {0:F3}(기운 바닥 — 단이 따라 기운다)", R.PadMax) : "", R.HeadZ,
            R.A2.X, R.A2.Y, R.B2.X, R.B2.Y, S.Area, ovS));

        // ③ 자리 — 폴리선에서 계획선까지 가장 짧은 거리 ≥ 필요 폭
        var Lg = gf.CreateLineString(L.Select(C).ToArray());
        R.Narrowest = Lg.Distance(planP);
        if (R.Narrowest < R.Need)
        {
            var np = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(Lg, planP);
            return No("폭", string.Format(Ci, "폴리선이 바닥(계획선)에 너무 가깝습니다 — 단 {0}개에 필요한 폭 {1:F2} m · 가장 좁은 곳 {2:F2} m ({3:F1}, {4:F1})",
                n, R.Need, R.Narrowest, np[0].X + ox, np[0].Y + oy), Wd(np[0]));
        }

        // ④ 줄 — 폴리선(양 끝 늘임)에서 d만큼 안쪽 · 측선 띠(vEps)를 뺀다
        var SLA = gf.CreateLineString(new[] { C(R.A), C(R.A2) }); var SLB = gf.CreateLineString(new[] { C(R.B), C(R.B2) });
        var flat = new BufferParameters { EndCapStyle = NetTopologySuite.Operation.Buffer.EndCapStyle.Flat, QuadrantSegments = 8 };
        // ★[v113.0 · 화면 없는 Civil 시험 1104 — 현장 합성 실패] 측선 수직 마감 폭 = 옹벽 면과 같은 기울기(1:0.01 · 단 n개 높이) — 3 cm에 12 m를 몰면 1:0.0025라
        //   합성 다듬기(옹벽 면 1:0.01 기준)가 µm 옮김에 cm를 잃었다(«한 점 한 높이 25.6 mm» · 판정 3). 눈으로는 여전히 수직
        double sideW = Math.Max(vEps, n * H * Math.Max(0.01, inp.SlopeW));
        R.SideW = sideW;
        Geometry sideBuf = SLA.Buffer(sideW, flat).Union(SLB.Buffer(sideW, flat));
        double maxOff = (n - 1) * step + face;
        var Lx = WallInPoly.ExtendEnds(L, maxOff + 5.0);
        var Lxg = gf.CreateLineString(Lx.Select(C).ToArray());
        double dens = sloped ? Math.Max(0.5, inp.Grid) : double.PositiveInfinity;
        var rows = new List<(int Level, List<List<Point3>> Pieces)>();
        List<List<Point3>> Pieces(Geometry g, int level)
        {
            var lm = new LineMerger(); lm.Add(g);
            var outp = new List<List<Point3>>();
            foreach (var o in lm.GetMergedLineStrings())
            {
                if (o is not LineString l || l.Length < 10 * vEps) continue;
                var cs = l.Coordinates;
                var pts = new List<Point3>();
                for (int i = 0; i < cs.Length; i++)
                {
                    if (i > 0 && !double.IsInfinity(dens))
                    {
                        double sl = cs[i - 1].Distance(cs[i]); int k = (int)Math.Ceiling(sl / dens);
                        for (int q = 1; q < k; q++) { double x = cs[i - 1].X + (cs[i].X - cs[i - 1].X) * q / k + ox, y = cs[i - 1].Y + (cs[i].Y - cs[i - 1].Y) * q / k + oy; pts.Add(new Point3(x, y, ZB(x, y) + sgn * (n - level) * H)); }
                    }
                    double wx = cs[i].X + ox, wy = cs[i].Y + oy;
                    pts.Add(new Point3(wx, wy, ZB(wx, wy) + sgn * (n - level) * H));
                }
                // 끝(측선 띠에서 자른 자리) 곁 1 mm 안 꼭짓점은 뺀다 — 버퍼 호 꼭짓점이 자른 자리에 µm로 붙으면 합성이 못 푸는 쌍이 된다
                for (int i = pts.Count - 2; i >= 1; i--)
                {
                    var p = pts[i]; var q = pts[i + 1]; var pv = pts[i - 1];
                    double dq = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)), dd = Math.Sqrt((p.X - pv.X) * (p.X - pv.X) + (p.Y - pv.Y) * (p.Y - pv.Y));
                    if (dq < 1e-3 || dd < 1e-3) pts.RemoveAt(i);
                }
                outp.Add(pts);
            }
            return outp;
        }
        try
        {
            rows.Add((0, Pieces(Lg.Difference(sideBuf), 0)));
            var bp = new BufferParameters { QuadrantSegments = 8 };
            for (int k = 1; k <= n; k++)
            {
                double dF = (k - 1) * step + face;
                rows.Add((k, Pieces(Lxg.Buffer(dF, bp).Boundary.Intersection(S).Difference(sideBuf), k)));
                if (k < n && T > 1e-6) rows.Add((k, Pieces(Lxg.Buffer(k * step, bp).Boundary.Intersection(S).Difference(sideBuf), k)));   // ★[설치 전 검토 L3] 소단 0이면 소단 끝 줄 = 다음 면 끝 줄과 같은 자리(겹친 브레이크라인) — 안 넣음
            }
        }
        catch (Exception ex) { return No("줄", $"줄을 못 지었습니다({ex.GetType().Name}: {ex.Message})"); }
        int multi = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Pieces.Count == 0) return No("줄", $"{i}번 줄을 못 지었습니다(띠 안에 남는 자리가 없음 — 단이 다 안 들어갑니다)");
            if (rows[i].Pieces.Count > 1) multi++;
            foreach (var p in rows[i].Pieces) R.Rows.Add(p);
        }
        if (multi > 0) R.Warn.Add($"줄 {multi}개가 여러 토막으로 갈렸다(띠가 좁은 자리)");

        // ⑤ 측선 안 단면(IP) — 줄 끝을 측선 쪽으로 모아 측선 방향 차례로 + 계획선 앞 vEps까지
        List<Point3> Ip(Point3 a, Point3 a2, Geometry sl, string nm)
        {
            double ux = a2.X - a.X, uy = a2.Y - a.Y, ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
            var ends = new List<(double S, Point3 P)>();
            foreach (var row in R.Rows)
                foreach (var e in new[] { row[0], row[^1] })
                {
                    double d = SegDist(e.X, e.Y, a, a2);
                    if (Math.Abs(d - sideW) < Math.Max(1e-4, sideW * 0.05)) ends.Add(((e.X - a.X) * ux + (e.Y - a.Y) * uy, e));
                }
            ends.Sort((p, q) => p.S.CompareTo(q.S));
            var r = new List<Point3>();
            foreach (var (s, p) in ends) if (r.Count == 0 || Math.Abs(p.X - r[^1].X) > 1e-7 || Math.Abs(p.Y - r[^1].Y) > 1e-7) r.Add(p);
            if (r.Count == 0) return r;
            // 마지막 줄 끝에서 측선 방향으로 띠 테두리까지 → vEps 앞
            var last = r[^1];
            var ray = gf.CreateLineString(new[] { new Coordinate(last.X - ox, last.Y - oy), new Coordinate(last.X - ox + ux * (ul + 10), last.Y - oy + uy * (ul + 10)) });
            var hit = ray.Intersection(S.ExteriorRing);
            double best = double.MaxValue; Coordinate? hc = null;
            foreach (var c in hit.Coordinates) { double dd = c.Distance(new Coordinate(last.X - ox, last.Y - oy)); if (dd > 1e-6 && dd < best) { best = dd; hc = c; } }
            if (hc != null && best > 3 * vEps)
            {
                double ex = hc.X - ux * vEps + ox, ey = hc.Y - uy * vEps + oy;
                r.Add(new Point3(ex, ey, ZB(ex, ey)));
            }
            else R.Warn.Add($"{nm} 측선 안 단면을 계획선 앞까지 못 늘였다(남은 {best:F3}m)");
            return r;
        }
        var ipA = Ip(R.A, R.A2, SLA, "A"); var ipB = Ip(R.B, R.B2, SLB, "B");
        int nRowPieces = R.Rows.Count;
        // 자가검증 — 줄마다 두 끝이 측선 안 단면에 하나씩(안 맞으면 수직 마감이 그 줄에서 끊긴다 — 합성 판정이 마지막 그물)
        int ipEnds = ipA.Count + ipB.Count - 2;   // 끝(계획선 앞) 점 뺌
        if (ipEnds != 2 * nRowPieces) R.Warn.Add($"측선 안 단면 점 {ipEnds}개 ≠ 줄 끝 {2 * nRowPieces}개(줄 끝이 측선 띠에 안 닿은 줄이 있다)");
        if (ipA.Count < 2 || ipB.Count < 2) return No("측선", $"측선 안 단면을 못 지었습니다(A {ipA.Count}점 · B {ipB.Count}점 — 줄 끝이 측선에 안 닿음)", Wd(C(R.A)), Wd(C(R.B)));

        // ⑥ R_out(+여유) — 폴리선을 바깥으로 곧게 늘여 «경계 + 여유»를 가른다(여유 밖 P0 = 원지반이라 테두리 턱이 없다)
        var cutP = BoundaryCutPoly(gf, inp.CutRing, ox, oy);
        if (cutP == null) return No("재료", "잘라낼 자리(R_out)를 다각형으로 못 만들었습니다");
        var bdP = BoundaryCutPoly(gf, inp.Bd, ox, oy);
        Polygon rExt = cutP; Point3 aOut = R.A, bOut = R.B;
        string mNote = "여유 없음";
        if (inp.Margin > 0 && bdP != null)
        {
            var bdM = GradingGeometry.BufferedRingOutward(inp.Bd, inp.Margin);
            if (bdM != null && bdM.Count >= 3)
            {
                (double, double) Dir(Point3 e, IReadOnlyList<Point3> l, bool atStart)
                {
                    int i0 = atStart ? 1 : l.Count - 2, di = atStart ? 1 : -1;
                    for (int i = i0; i >= 0 && i < l.Count; i += di)
                    {
                        double dx = e.X - l[i].X, dy = e.Y - l[i].Y, dl = Math.Sqrt(dx * dx + dy * dy);
                        if (dl > 0.01) return (dx / dl, dy / dl);
                    }
                    return (0, 0);
                }
                var (uax, uay) = Dir(R.A, L, true); var (ubx, uby) = Dir(R.B, L, false);
                bool okA = GradingGeometry.RayRingHit(bdM, R.A.X, R.A.Y, uax, uay, out double ax, out double ay, out _, 1e-6);
                bool okB = GradingGeometry.RayRingHit(bdM, R.B.X, R.B.Y, ubx, uby, out double bx, out double by, out _, 1e-6);
                if (okA && okB)
                {
                    var eA = gf.CreateLineString(new[] { C(R.A), new Coordinate(ax - ox, ay - oy) });
                    var eB = gf.CreateLineString(new[] { C(R.B), new Coordinate(bx - ox, by - oy) });
                    double inA = eA.Intersection(bdP).Length, inB = eB.Intersection(bdP).Length;
                    if (inA < 1e-3 && inB < 1e-3)
                    {
                        try
                        {
                            var lext = new List<Coordinate> { new(ax - ox, ay - oy) }; lext.AddRange(L.Select(C)); lext.Add(new Coordinate(bx - ox, by - oy));
                            var bmP = BoundaryCutPoly(gf, bdM, ox, oy)!;
                            var noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(new Geometry[] { bmP.ExteriorRing, gf.CreateLineString(lext.ToArray()) }), new PrecisionModel());
                            var pz = new Polygonizer(); pz.Add(noded);
                            var ip = cutP.InteriorPoint;
                            foreach (var g in pz.GetPolygons())
                                if (g is Polygon f && f.Contains(ip) && f.Area > cutP.Area - 1e-3 && cutP.Difference(f).Area < 1e-3 && (nP == null || f.Intersection(nP).Area < 1e-2))
                                { rExt = f; aOut = new Point3(ax, ay, 0); bOut = new Point3(bx, by, 0); R.MarginUsed = true; break; }
                            mNote = R.MarginUsed ? string.Format(Ci, "여유 {0:0.#}m(경계 밖까지 {1:F1}㎡)", inp.Margin, rExt.Area - cutP.Area) : "⚠여유 조각을 못 골라 여유 없이(옛 경계까지)";
                        }
                        catch (Exception ex) { mNote = $"⚠여유를 못 붙여 여유 없이({ex.GetType().Name})"; }
                    }
                    else mNote = string.Format(Ci, "⚠늘인 끝이 경계 안을 지나 여유 없이(A {0:F2} · B {1:F2}m)", inA, inB);
                }
                else mNote = "⚠늘인 끝이 여유 테두리에 안 닿아 여유 없이";
            }
            else mNote = "⚠여유 테두리를 못 만들어 여유 없이";
        }
        if (!R.MarginUsed) R.Warn.Add("R_out에 여유를 못 붙였다 — 옛 경계에서 합성 테두리 턱이 날 수 있다(" + mNote + ")");
        // ★[v113.0 · 설치 전 검토 M5] 여유 자리는 다른 옹벽 폴리곤(여유 포함)을 비켜 간다 — 겹치면 차례 합성이 «앞 옹벽 폴리곤 안 삼각형을 건드림»으로 멈춘다(R_out 자체가 겹치면 아래 막을 다각형이 거절)
        if (R.MarginUsed && inp.MarginClip != null)
        {
            try
            {
                Geometry cut = rExt;
                foreach (var cr in inp.MarginClip) { var cp = BoundaryCutPoly(gf, cr, ox, oy); if (cp != null && cp.Intersects(cut)) cut = cut.Difference(cp); }
                Polygon? keep = null; var ipc = cutP.InteriorPoint;
                for (int i = 0; i < cut.NumGeometries; i++) if (cut.GetGeometryN(i) is Polygon pc && pc.Contains(ipc)) { keep = pc; break; }
                if (keep != null && keep.NumInteriorRings == 0 && cutP.Difference(keep).Area < 1e-3)
                {
                    if (keep.Area < rExt.Area - 1e-6) log.Append(string.Format(Ci, "  ⑥-b 여유에서 다른 옹벽 자리 {0:F1}㎡를 뺐다\n", rExt.Area - keep.Area));
                    rExt = keep;
                }
            }
            catch (Exception ex) { R.Warn.Add($"여유에서 다른 옹벽 자리를 못 뺐다({ex.GetType().Name})"); }
        }

        // ⑦ Z = S ∪ R_out(+여유)
        Geometry zG;
        try { zG = S.Union(rExt); } catch (Exception ex) { return No("자리", $"바꿀 자리를 못 합쳤습니다({ex.GetType().Name})"); }
        // 함께 쓰는 폴리선에서 부동소수로 생긴 바늘 구멍(1 cm² 미만)은 메운다 — 그보다 크면 진짜 구멍이라 막는다
        if (zG is Polygon zH && zH.NumInteriorRings > 0)
        {
            double holes = 0; for (int i = 0; i < zH.NumInteriorRings; i++) holes += Math.Abs(NetTopologySuite.Algorithm.Area.OfRingSigned(zH.GetInteriorRingN(i).Coordinates));
            if (holes < 1e-4) zG = gf.CreatePolygon((LinearRing)zH.ExteriorRing.Copy());
            else return No("자리", string.Format(Ci, "바꿀 자리에 구멍이 있습니다({0:F4}㎡)", holes));
        }
        if (zG is not Polygon zP || zP.NumInteriorRings > 0 || !zP.IsValid)
            return No("자리", $"바꿀 자리가 한 다각형이 아닙니다({zG.GeometryType} · 구멍 {(zG as Polygon)?.NumInteriorRings ?? -1})");
        if (inp.Blockers != null)
            foreach (var (name, br) in inp.Blockers)
            {
                var bp2 = BoundaryCutPoly(gf, br, ox, oy);
                if (bp2 == null) continue;
                double ov = 0; try { ov = zP.Intersection(bp2).Area; } catch { }
                if (ov > 1.0) return No("겹침", string.Format(Ci, "{0}과 {1:F1}㎡ 겹칩니다", name, ov), Wd(bp2.InteriorPoint.Coordinate));
            }
        R.CutExt = rExt.ExteriorRing.Coordinates.Take(rExt.ExteriorRing.NumPoints - 1).Select(c => new Point3(c.X + ox, c.Y + oy, 0)).ToList();

        // ⑧ 뚜껑 높이 — R_out(+여유) 원지반 최고 + 1(절토) · 최저 − 1(성토) · 머리보다도 1 m 바깥
        double? gx = R.Up ? WallInPoly.MaxGroundIn(R.CutExt, inp.Ground, Math.Max(1, inp.Grid), out _, out _) : WallInPoly.MinGroundIn(R.CutExt, inp.Ground, Math.Max(1, inp.Grid), out _, out _);
        if (gx == null) return No("원지반", "잘라낼 자리에서 원지반을 못 읽었습니다");
        R.CapZ = R.Up ? Math.Max(gx.Value, R.PadMax + n * H) + 1.0 : Math.Min(gx.Value, R.PadMin - n * H) - 1.0;

        // ⑨ Z 테두리 높이 — 측선 = P0(변 건넘까지) · 계획선 = 바닥 · 그 밖(옛 경계 + 여유 · 늘인 끝) = 뚜껑
        var zc = zP.ExteriorRing.Coordinates;
        var zr = new List<(Point3 P, int K)>();   // K: 0 측선 · 1 계획선 · 2 뚜껑
        for (int i = 0; i + 1 < zc.Length; i++)
        {
            double x = zc[i].X + ox, y = zc[i].Y + oy;
            int k = SegDist(x, y, R.A, R.A2) < 1e-6 || SegDist(x, y, R.B, R.B2) < 1e-6 ? 0 : Nearest(inp.Plan, x, y).D < 1e-6 ? 1 : 2;
            zr.Add((new Point3(x, y, 0), k));
        }
        static double Dist2(Point3 u, Point3 v) => (u.X - v.X) * (u.X - v.X) + (u.Y - v.Y) * (u.Y - v.Y);
        int abFb = 0;
        // 경계 위 A · B — 정지면을 1 mm 안쪽에서도 못 읽으면 원지반(데이라잇 자리라 P0 = 원지반 · 하네스 S196 ⑦)
        double? AtAB(double x, double y)
        {
            var pt = new Point3(x, y, 0);
            if (Dist2(pt, R.A) > 1e-12 && Dist2(pt, R.B) > 1e-12) return null;
            var g = inp.Ground(x, y); if (g != null) abFb++;
            return g;
        }
        var ring3 = new List<Point3>(); var side = new List<bool>();
        int nz = zr.Count;
        for (int i = 0; i < nz; i++)
        {
            var (p, k) = zr[i]; var (q, kq) = zr[(i + 1) % nz];
            if (k == 0 && kq == 0)
            {
                var pr = ProfileOnTris(p, q, inp.P0Tris, inp.P0, inp.Grid, AtAB);
                if (pr == null) return No("정지면", string.Format(Ci, "측선 위 합성 전 면(정지면) 높이를 못 읽었습니다 ({0:F1}, {1:F1})", p.X, p.Y), (p.X, p.Y));
                for (int j = 0; j + 1 < pr.Count; j++) { ring3.Add(pr[j]); side.Add(true); }
                continue;
            }
            double z;
            if (k == 0)
            {
                // 측선 끝 점 하나 — 그 측선을 따라 안쪽으로 읽는다(끝이 정지면 삼각망 바깥 µm여도 · 경계 위 A · B는 원지반으로 물러섬)
                var other = SegDist(p.X, p.Y, R.A, R.A2) < 1e-6 ? (Dist2(p, R.A) < Dist2(p, R.A2) ? R.A2 : R.A) : (Dist2(p, R.B) < Dist2(p, R.B2) ? R.B2 : R.B);
                var pz0 = ProfileOnTris(p, other, inp.P0Tris, inp.P0, inp.Grid, AtAB);
                if (pz0 == null || pz0.Count == 0) return No("정지면", "측선 끝 정지면 높이를 못 읽었습니다", (p.X, p.Y));
                z = pz0[0].Z;
            }
            else z = k == 1 ? Nearest(inp.Plan, p.X, p.Y).Z : R.CapZ;
            ring3.Add(new Point3(p.X, p.Y, z)); side.Add(k == 0);
        }
        var closed = new List<Point3>(ring3) { ring3[0] };
        R.Zone = ring3.Select(q => new Point3(q.X, q.Y, R.HeadZ)).ToList(); R.ZoneSide = side;

        // ⑩ 뚜껑 줄 — 늘인 폴리선을 R_out 쪽으로 vEps 민 선(뚜껑 높이) · R_out 안 점만
        var lfull = new List<Point3>();
        if (R.MarginUsed) lfull.Add(aOut);
        lfull.AddRange(L);
        if (R.MarginUsed) lfull.Add(bOut);
        var capLines = new List<List<Point3>>();
        {
            var prepR = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(rExt);
            double sideSgn = 0;
            {
                var a0 = lfull[0]; var a1 = lfull[1];
                double dx = a1.X - a0.X, dy = a1.Y - a0.Y, dl = Math.Sqrt(dx * dx + dy * dy);
                var mid = new Coordinate((a0.X + a1.X) / 2 - dy / dl * vEps - ox, (a0.Y + a1.Y) / 2 + dx / dl * vEps - oy);
                sideSgn = prepR.Contains(gf.CreatePoint(mid)) ? 1 : -1;
            }
            var cur = new List<Point3>();
            for (int i = 0; i < lfull.Count; i++)
            {
                double nx = 0, ny = 0;
                if (i > 0) { double dx = lfull[i].X - lfull[i - 1].X, dy = lfull[i].Y - lfull[i - 1].Y, dl = Math.Sqrt(dx * dx + dy * dy); if (dl > 1e-12) { nx += -dy / dl; ny += dx / dl; } }
                if (i + 1 < lfull.Count) { double dx = lfull[i + 1].X - lfull[i].X, dy = lfull[i + 1].Y - lfull[i].Y, dl = Math.Sqrt(dx * dx + dy * dy); if (dl > 1e-12) { nx += -dy / dl; ny += dx / dl; } }
                double nl = Math.Sqrt(nx * nx + ny * ny);
                if (nl < 1e-12) continue;
                double x = lfull[i].X + sideSgn * nx / nl * vEps, y = lfull[i].Y + sideSgn * ny / nl * vEps;
                var pt = gf.CreatePoint(new Coordinate(x - ox, y - oy));
                bool inside = prepR.Contains(pt) && rExt.ExteriorRing.Distance(pt) > vEps * 0.5;
                if (inside) cur.Add(new Point3(x, y, R.CapZ));
                else { if (cur.Count >= 2) capLines.Add(cur); cur = new List<Point3>(); }
            }
            if (cur.Count >= 2) capLines.Add(cur);
        }
        if (capLines.Count == 0) R.Warn.Add("뚜껑 줄을 못 그었다 — R_out 높이가 폴리선과 옛 경계 사이에서 이어 붙는다");

        R.Lines.Add(closed);
        R.Lines.AddRange(R.Rows);
        R.Lines.Add(ipA); R.Lines.Add(ipB);
        R.Lines.AddRange(capLines);
        R.Ok = true; R.Code = "통과";
        // 자가검증 — 맨 안 줄(면 끝 n)이 폴리선에서 물러난 거리(가운데 점)
        double want = maxOff, got = double.NaN;
        {
            var lastRow = R.Rows.Count > 0 ? R.Rows[^1] : null;
            if (lastRow != null) { var mid = lastRow[lastRow.Count / 2]; got = Lg.Distance(gf.CreatePoint(new Coordinate(mid.X - ox, mid.Y - oy))); }
        }
        log.Append(string.Format(Ci, "  ③ 자리 — 필요 폭 {0:F2} · 가장 좁은 곳 {1:F2}m · ④ 줄 {2}개(토막 {3} · 여러 토막 {4}) · 맨 안 줄 물러난 거리 {5:F3}m(기대 {6:F3}) · ⑤ 측선 안 단면 A {7}점 · B {8}점 · ⑥ {9} · ⑦ Z {10:F1}㎡(띠 {11:F1} + R_out {12:F1}) · ⑧ 뚜껑 {13:F2}m · ⑨ Z 테두리 {14}점(측선 {15}) · ⑩ 뚜껑 줄 {16}\n",
            R.Need, R.Narrowest, rows.Count, nRowPieces, multi, got, want, ipA.Count, ipB.Count, mNote, zP.Area, S.Area, rExt.Area, R.CapZ, ring3.Count, side.Count(b => b), capLines.Count));
        if (abFb > 0) log.Append($"  ⑨-b 경계 위 A · B 정지면을 못 읽어 원지반으로 {abFb}번(정지면 삼각망 바깥 끝)\n");
        if (!double.IsNaN(got) && Math.Abs(got - want) > 0.01 + 1e-3) R.Warn.Add(string.Format(Ci, "맨 안 줄이 {0:F3}m 물러났다(기대 {1:F3}) — 폴리선이 굽은 자리", got, want));
        R.Log = log.ToString();
        return R;
    }

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

    /// <summary>★[v113.0] 판정 + 짓기를 한 번에 — 화면 없는 탐침 · 하네스 · 다시 짓기가 같은 길로 부른다(그린 폴리선 원본 → 자르기 → 기하).</summary>
    public static (BoundaryCut.Result Cut, Built? Built) Make(IReadOnlyList<Point3> src, IReadOnlyList<Point3> bd, IReadOnlyList<Point3> plan,
        IEnumerable<(string Name, IReadOnlyList<Point3> Ring)>? blockers, double h, double t, double slopeW, double faceRun, double grid, double margin,
        Func<double, double, double?> ground, Func<double, double, double?> p0, IReadOnlyList<WallDaylight.Tri>? p0Tris,
        IEnumerable<IReadOnlyList<Point3>>? marginClip = null)
    {
        var bl = blockers?.ToList();
        var cut = BoundaryCut.Classify(src, bd, plan, bl);
        if (!cut.Ok) return (cut, null);
        var b = Build(new Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = plan, H = h, T = t, SlopeW = slopeW, FaceRun = faceRun, Grid = grid, Margin = margin,
            Ground = ground, P0 = p0, P0Tris = p0Tris, Blockers = bl, MarginClip = marginClip,
        });
        return (cut, b);
    }

    /// <summary>★[v113.0] 정지면을 다시 지은 뒤 제자리인가 — 두 교점이 1 mm 안 · 단 수 · 방향이 같고 바닥이 1 mm 안(계획 1-3 · 3-7).</summary>
    public static string SamePlace(WallRec w, BoundaryCut.Result cut, Built b)
    {
        if (w.Line.Count < 2) return "저장한 폴리선이 없다";
        double dA = Math.Sqrt((w.Line[0].X - b.A.X) * (w.Line[0].X - b.A.X) + (w.Line[0].Y - b.A.Y) * (w.Line[0].Y - b.A.Y));
        double dB = Math.Sqrt((w.Line[^1].X - b.B.X) * (w.Line[^1].X - b.B.X) + (w.Line[^1].Y - b.B.Y) * (w.Line[^1].Y - b.B.Y));
        var why = new List<string>();
        if (dA > 1e-3 || dB > 1e-3) why.Add(string.Format(Ci, "경계 교점이 움직임(A {0:F3} · B {1:F3}m)", dA, dB));
        if (b.Up != w.Up) why.Add("절토/성토가 바뀜");
        if (b.Tiers != w.Tiers) why.Add($"단 수 {w.Tiers} → {b.Tiers}");
        double pad = w.Up ? b.PadMin : b.PadMax;
        if (Math.Abs(pad - w.PadZ) > 1e-3) why.Add(string.Format(Ci, "바닥 {0:F3} → {1:F3}m", w.PadZ, pad));
        return string.Join(" · ", why);
    }

    /// <summary>★[v113.0 · 하네스] 브레이크라인을 삼각형으로 잇는다(Civil TIN 대신 — 선으로 가른 조각마다 제 꼭짓점만으로 삼각분할 · 높이는 선 위 값).</summary>
    public static List<WallDaylight.Tri> Mesh(Built b)
    {
        var res = new List<WallDaylight.Tri>();
        if (!b.Ok || b.Lines.Count == 0) return res;
        double ox = b.A.X, oy = b.A.Y;
        var gf = new GeometryFactory();
        var zmap = new Dictionary<(long, long), double>();
        (long, long) K(double x, double y) => ((long)Math.Round((x - ox) * 1e6), (long)Math.Round((y - oy) * 1e6));
        var segs = new List<(Point3 A, Point3 B)>();
        var geoms = new List<Geometry>();
        foreach (var l in b.Lines)
        {
            foreach (var q in l) zmap[K(q.X, q.Y)] = q.Z;
            for (int i = 0; i + 1 < l.Count; i++) segs.Add((l[i], l[i + 1]));
            geoms.Add(gf.CreateLineString(l.Select(q => new Coordinate(q.X - ox, q.Y - oy)).ToArray()));
        }
        double ZOf(Coordinate c)
        {
            double x = c.X + ox, y = c.Y + oy;
            if (zmap.TryGetValue(K(x, y), out double z)) return z;
            double bd = double.MaxValue, bz = double.NaN;
            foreach (var (a, e) in segs)
            {
                double ex = e.X - a.X, ey = e.Y - a.Y, l2 = ex * ex + ey * ey;
                double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - a.X) * ex + (y - a.Y) * ey) / l2, 0, 1);
                double dx = x - (a.X + ex * t), dy = y - (a.Y + ey * t), d2 = dx * dx + dy * dy;
                if (d2 < bd) { bd = d2; bz = a.Z + (e.Z - a.Z) * t; }
            }
            return bz;
        }
        // 매달린 선 끝(측선 안 단면 끝 · 뚜껑 줄 끝)은 Z 테두리의 가장 가까운 점까지 곧게 잇는다(조각이 닫히게 · 이은 선 높이는 두 끝 사이 1차) —
        //   제약 들로네(덧점)는 vEps 띠에서 점을 µm로 몰아 넣어 쓰지 않는다
        var zoneL = b.Lines[0];
        var lsAll = geoms.Cast<LineString>().ToList();
        for (int li = 1; li < b.Lines.Count; li++)
        {
            var l = b.Lines[li];
            foreach (var e in new[] { l[0], l[^1] })
            {
                var pe = gf.CreatePoint(new Coordinate(e.X - ox, e.Y - oy));
                bool touches = false;
                for (int k = 0; k < lsAll.Count && !touches; k++) if (k != li && lsAll[k].Distance(pe) < 1e-7) touches = true;
                if (touches) continue;
                var nz = Nearest(zoneL, e.X, e.Y);
                var np = new Point3(nz.X, nz.Y, nz.Z);
                segs.Add((e, np));
                geoms.Add(gf.CreateLineString(new[] { new Coordinate(e.X - ox, e.Y - oy), new Coordinate(np.X - ox, np.Y - oy) }));
            }
        }
        var noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(geoms.ToArray()), new PrecisionModel());
        var pz = new Polygonizer(); pz.Add(noded);
        foreach (var g in pz.GetPolygons())
        {
            if (g is not Polygon f || f.Area <= 1e-14) continue;
            Geometry trs;
            try { trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); } catch { continue; }
            for (int q = 0; q < trs.NumGeometries; q++)
            {
                if (trs.GetGeometryN(q) is not Polygon tq || tq.Area <= 0) continue;
                var c = tq.ExteriorRing.Coordinates;
                Point3 P(Coordinate k) => new(k.X + ox, k.Y + oy, ZOf(k));
                res.Add(new WallDaylight.Tri(P(c[0]), P(c[1]), P(c[2])));
            }
        }
        return res;
    }
}
