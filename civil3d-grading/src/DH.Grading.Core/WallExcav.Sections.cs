using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v104 · 계획_v104.md 5 · 6] 옹벽 형식별 일반단면을 터파기 · 수량에 잇는다 —
/// 바닥 줄 점마다 단면(<see cref="WallSection"/>) → 같은 표 줄 구간 → 폭이 다른 띠 → 면 기록(<c>DH_WALLSEC</c>) → 횡단 수량의 구조물 공제(기둥법).</summary>
public static partial class WallExcav
{
    /// <summary>바닥 줄 점 하나의 단면 — 앞 땅(toe) · 보이는 높이 h · 전체 높이 H(근입 포함) · 근입 · 앞 · 뒤 기울기 · 표 줄 번호.</summary>
    public sealed class SecPoint
    {
        public double X, Y, S, Toe, H, HTot, Df, Rf, Rb;
        public int Row;
        public WallSection.Shape Shape = null!;
    }

    /// <summary>같은 표 줄 구간(★2판 · 검토 중간 4) — 점 I0..I1 · 띠를 만들 호 위치 S0..S1 · 폭은 구간 최댓값.</summary>
    public sealed class SecRun
    {
        public int I0, I1, Row;
        public double S0, S1;
        /// <summary>흙 쪽 폭(뒤 끝 + 여유 · 도랑은 0) · 앞쪽 폭(−앞 끝 + 여유).</summary>
        public double Back, Front;
        public bool Trench;
        public double Width => Back + Front;
    }

    /// <summary>바닥 줄 하나의 단면 — 형식 · 흙 쪽 · 절토인가 · 앞면 구배 · 점 · 구간.</summary>
    public sealed class SecLine
    {
        public WallSection.Kind Kind;
        public int Soil;
        public bool Up;
        /// <summary>★[v104 B] 그 옹벽의 맨 위 단인가(배면사면 대상 — 절토만).</summary>
        public bool Top;
        public double SlopeW;
        public string Src = "";
        public List<SecPoint> Pts = new();
        public List<SecRun> Runs = new();
        public bool Trench => Runs.Count > 0 && Runs[0].Trench;
        public double BackMax => Runs.Count == 0 ? 0 : Runs.Max(r => r.Back);
        public double FrontMax => Runs.Count == 0 ? 0 : Runs.Max(r => r.Front);
        /// <summary>바닥 깊이 최대(앞 땅 − 바닥).</summary>
        public double DepthMax => Pts.Count == 0 ? 0 : Pts.Max(p => -p.Shape.ZBottom);
    }

    /// <summary>단면 고르기 셈 — 까닭별 길이(m) · 첫 자리.</summary>
    public sealed class SecNote
    {
        public double Over, Below, SlopedBack, SlopedFront, Wedge, Len;
        public double OverHMax;
        public string OverAt = "", BelowAt = "", BackAt = "", FrontAt = "", WedgeAt = "";
        public bool Warn => Over > 0 || Below > 0 || SlopedBack > 0 || SlopedFront > 0 || Wedge > 0;
        public string Text => string.Join(" · ", new[]
        {
            Over > 0 ? string.Format(CultureInfo.InvariantCulture, "표 밖(마지막 줄보다 높음 · 최고 h {0:F1}m) {1:F1}m — 개별 설계 필요 @{2}", OverHMax, Over, OverAt) : "",
            Below > 0 ? string.Format(CultureInfo.InvariantCulture, "첫 줄보다 낮은 벽 {0:F1}m — 표 첫 줄 단면으로 셈(부풀 수 있음) @{1}", Below, BelowAt) : "",
            SlopedBack > 0 ? string.Format(CultureInfo.InvariantCulture, "배면이 고른 벽 뒤 조건보다 가파름 {0:F1}m — [단면 표]에서 벽 뒤 조건을 바꿀 것 @{1}", SlopedBack, BackAt) : "",
            SlopedFront > 0 ? string.Format(CultureInfo.InvariantCulture, "앞 땅 경사(1:3보다 가파름) {0:F1}m — 근입 검토 @{1}", SlopedFront, FrontAt) : "",
            Wedge > 0 ? string.Format(CultureInfo.InvariantCulture, "단면 앞면이 정지면보다 누움 {0:F1}m — 그 쐐기는 실제로는 더 깎는 자리(수량은 빈 공간으로 뺌) @{1}", Wedge, WedgeAt) : "",
        }.Where(s => s.Length > 0));
    }

    static bool IsConcrete(WallSection.Kind k) => k is WallSection.Kind.역T형 or WallSection.Kind.L형 or WallSection.Kind.역L형
                                                      or WallSection.Kind.중력식 or WallSection.Kind.반중력식 or WallSection.Kind.부벽식;

    /// <summary>★계획 5.2 · 5.3 — 바닥 줄 점마다 h(+ 앞 기울기)로 표 줄을 고르고 단면을 세워 <b>바닥 높이를 바꾼다</b>(Pts.Z = 앞 땅 + 단면 바닥) · 같은 줄 구간으로 묶는다.
    /// <list type="bullet">
    /// <item>폭: 보통 = 뒤 끝 + 여유 · −앞 끝 + 여유. 도랑(패널식) = 앞쪽만(뒤 0 — 바닥 뒤 끝 = 앞면 선 = 깎기면 · ★2판 검토 높음 1) ·
    /// 조각은 보통 경사로 지은 뒤 <see cref="ClipBehind"/>로 벽 뒤 법면을 들어낸다(벽 뒤를 안 판다 · 옆면을 거의 수직으로 세우면 Civil 삼각망이 못 받았다 — 1021 화면 없는 Civil ⑧b).</item>
    /// <item>구간 최소 <paramref name="minRun"/> — 짧은 구간은 넓은 이웃에 붙이고 폭은 큰 쪽. 구간 사이 틈은 넓은 쪽이 갖는다.</item>
    /// </list>
    /// 못 하면 null + 까닭(점마다 h가 없는 바닥 줄 — 옛 재기).</summary>
    public static SecLine? Sectionize(Bottom b, WallSection.Table t, WallSection.Common c, double margin, SecNote note, out string why,
                                      double minRun = 2.0)
    {
        why = "";
        int n = b.Pts.Count;
        if (n < 2) { why = "바닥 줄 점이 모자람"; return null; }
        if (b.Toe.Count != n || b.H.Count != n || b.Rf.Count != n || b.Rb.Count != n) { why = "바닥 줄에 점마다 벽 높이가 없다"; return null; }
        var sl = new SecLine { Kind = t.Kind, Soil = b.Soil, Up = b.Up, Top = b.Top, SlopeW = b.SlopeW, Src = b.Src };
        double s = 0;
        var back = new double[n]; var front = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (i > 0) s += Math.Sqrt(Sq(b.Pts[i].X - b.Pts[i - 1].X) + Sq(b.Pts[i].Y - b.Pts[i - 1].Y));
            double h = Math.Max(0, b.H[i]);
            var row = WallSection.Lookup(t, h, out bool over, out double hTot, out bool below, out double df, b.Rf[i]);
            var sh = WallSection.BuildRow(t.Kind, row, hTot, c, b.Up, df);
            sh.Over = over; sh.Below = below;
            var p = new SecPoint { X = b.Pts[i].X, Y = b.Pts[i].Y, S = s, Toe = b.Toe[i], H = h, HTot = hTot, Df = sh.Df, Rf = b.Rf[i], Rb = b.Rb[i], Row = t.Rows.IndexOf(row), Shape = sh };
            sl.Pts.Add(p);
            b.Pts[i] = new Point3(p.X, p.Y, p.Toe + sh.ZBottom);
            back[i] = sh.Trench ? 0 : Math.Max(0, sh.XBack + Math.Max(0, margin));
            front[i] = Math.Max(0, -sh.XFront + Math.Max(0, margin));
        }
        // 셈 — 점 사이 반씩
        string At(SecPoint p) => string.Format(CultureInfo.InvariantCulture, "{0} ({1:F2},{2:F2})", b.Src, p.X, p.Y);
        for (int i = 0; i < n; i++)
        {
            var p = sl.Pts[i];
            double w = (i > 0 ? (p.S - sl.Pts[i - 1].S) / 2 : 0) + (i + 1 < n ? (sl.Pts[i + 1].S - p.S) / 2 : 0);
            note.Len += w;
            if (p.Shape.Over) { note.Over += w; note.OverHMax = Math.Max(note.OverHMax, p.H); if (note.OverAt.Length == 0) note.OverAt = At(p); }
            if (p.Shape.Below) { note.Below += w; if (note.BelowAt.Length == 0) note.BelowAt = At(p); }
            // ★[v105 · 계획 검토 중간 2] 고른 벽 뒤 조건보다 가파른 배면만 ⚠ — 평평은 1:3 · 1:1.8 · 1:1.5 비탈은 그 경사(+ 0.02 여유)
            double rbMax = t.Cond.Back == 0 ? 1.0 / 3 : (t.Cond.Back == 1 ? 1 / 1.8 : 1 / 1.5) + 0.02;
            if (IsConcrete(t.Kind) && p.Rb > rbMax + 1e-9) { note.SlopedBack += w; if (note.BackAt.Length == 0) note.BackAt = At(p); }
            if (IsConcrete(t.Kind) && p.Rf > 1.0 / 3 + 1e-9) { note.SlopedFront += w; if (note.FrontAt.Length == 0) note.FrontAt = At(p); }
            if (!p.Shape.Trench && p.Shape.FaceBatter - b.SlopeW > 0.03) { note.Wedge += w; if (note.WedgeAt.Length == 0) note.WedgeAt = At(p); }
        }
        // 같은 줄 구간
        var runs = sl.Runs;
        for (int i = 0; i < n; i++)
        {
            var p = sl.Pts[i];
            if (runs.Count == 0 || runs[^1].Row != p.Row)
                runs.Add(new SecRun { I0 = i, I1 = i, Row = p.Row, Back = back[i], Front = front[i], Trench = p.Shape.Trench });
            else
            {
                var r = runs[^1];
                r.I1 = i; r.Back = Math.Max(r.Back, back[i]); r.Front = Math.Max(r.Front, front[i]);
            }
        }
        double S(int i) => sl.Pts[i].S;
        double Len(SecRun r) => (r.I1 == n - 1 ? S(n - 1) : (S(r.I1) + S(r.I1 + 1)) / 2) - (r.I0 == 0 ? 0 : (S(r.I0 - 1) + S(r.I0)) / 2);
        while (runs.Count > 1)
        {
            int k = 0;
            for (int i = 1; i < runs.Count; i++) if (Len(runs[i]) < Len(runs[k])) k = i;
            if (Len(runs[k]) >= minRun) break;
            int j = k == 0 ? 1 : k == runs.Count - 1 ? k - 1 : runs[k + 1].Width > runs[k - 1].Width ? k + 1 : k - 1;
            var a = runs[k]; var m = runs[j];
            if (a.Width > m.Width) m.Row = a.Row;
            m.I0 = Math.Min(m.I0, a.I0); m.I1 = Math.Max(m.I1, a.I1);
            m.Back = Math.Max(m.Back, a.Back); m.Front = Math.Max(m.Front, a.Front);
            runs.RemoveAt(k);
        }
        // ★[v104 · V34 L형 계측] 이웃 구간 폭 차이가 5 cm 미만이면 큰 쪽으로 맞춘다 — 앞 폭은 표 줄마다 저판 두께 × 앞면 경사만큼(1 ~ 3 mm)만 달라
        //   띠 테두리에 2 mm 계단이 생기고, Civil이 그 곁 점을 합치며 평면 폭 2 mm의 납작한 세모를 만들었다(현장 도면 판정 3).
        for (int pass = 0; pass < runs.Count; pass++)
        {
            bool changed = false;
            for (int k = 0; k + 1 < runs.Count; k++)
            {
                var a = runs[k]; var bb = runs[k + 1];
                if (a.Back != bb.Back && Math.Abs(a.Back - bb.Back) < 0.05) { a.Back = bb.Back = Math.Max(a.Back, bb.Back); changed = true; }
                if (a.Front != bb.Front && Math.Abs(a.Front - bb.Front) < 0.05) { a.Front = bb.Front = Math.Max(a.Front, bb.Front); changed = true; }
            }
            if (!changed) break;
        }
        foreach (var r in runs) { r.S0 = S(r.I0); r.S1 = S(r.I1); }
        for (int k = 0; k + 1 < runs.Count; k++)
        {
            var a = runs[k]; var bb = runs[k + 1];
            if (a.Width >= bb.Width) a.S1 = bb.S0; else bb.S0 = a.S1;   // 구간 사이 틈은 넓은 쪽이
        }
        return sl;
    }

    /// <summary>★계획 5.3 — 폭이 구간마다 다른 바닥 띠(링). 구간마다 Mitre 한쪽 버퍼(뒤 · 앞 따로)를 만들어 합친다.
    /// <list type="bullet">
    /// <item>좁은 쪽 구간은 넓은 이웃 구간 안으로 min(4 × 폭 + 0.5, 이웃 길이)만큼 늘린다 — 꺾임점이 구간 경계에 걸려도 바깥 V홈이 안 생기게(곧은 자리에서는 넓은 띠 안이라 더해지는 넓이가 없다).</item>
    /// <item>도랑은 뒤 폭 0 — 앞면 선 앞의 띠(뒤 끝 = 앞면 선).</item>
    /// <item>꼭짓점 z = 바닥 줄 위 가장 가까운 점의 z(<see cref="StripRing(Bottom,double,double,out string,double)"/>와 같은 법) · 변은 <paramref name="maxEdge"/>마다 나눈다.</item>
    /// </list></summary>
    public static List<Point3>? StripRing(Bottom b, SecLine sl, out string why, double maxEdge = 0.5)
    {
        why = "";
        if (b.Pts.Count < 2 || sl.Runs.Count == 0) { why = "바닥 줄 점이 모자람"; return null; }
        var gf = new GeometryFactory();
        double ox = b.Pts[0].X, oy = b.Pts[0].Y;
        var cs = new List<Coordinate>(); var zs = new List<double>(); var ss = new List<double>();
        for (int i = 0; i < b.Pts.Count; i++)
        {
            var c = new Coordinate(b.Pts[i].X - ox, b.Pts[i].Y - oy);
            if (cs.Count == 0 || !cs[^1].Equals2D(c)) { cs.Add(c); zs.Add(b.Pts[i].Z); ss.Add(i < sl.Pts.Count ? sl.Pts[i].S : (ss.Count == 0 ? 0 : ss[^1] + c.Distance(cs[^2]))); }
        }
        if (cs.Count < 2) { why = "바닥 줄이 점 하나"; return null; }
        double total = ss[^1];
        LineString? Sub(double a, double e)
        {
            a = Math.Max(0, a); e = Math.Min(total, e);
            if (e - a < 1e-6) return null;
            var l = new List<Coordinate>();
            Coordinate At(double s0)
            {
                int i = 0;
                while (i < ss.Count - 2 && ss[i + 1] < s0) i++;
                double L = ss[i + 1] - ss[i], t = L < 1e-12 ? 0 : (s0 - ss[i]) / L;
                return new Coordinate(cs[i].X + (cs[i + 1].X - cs[i].X) * t, cs[i].Y + (cs[i + 1].Y - cs[i].Y) * t);
            }
            l.Add(At(a));
            for (int i = 0; i < cs.Count; i++) if (ss[i] > a + 1e-9 && ss[i] < e - 1e-9 && !l[^1].Equals2D(cs[i])) l.Add(cs[i]);
            var ce = At(e);
            if (!l[^1].Equals2D(ce)) l.Add(ce);
            return l.Count >= 2 ? gf.CreateLineString(l.ToArray()) : null;
        }
        var bp = new BufferParameters { EndCapStyle = EndCapStyle.Flat, IsSingleSided = true, JoinStyle = JoinStyle.Mitre, MitreLimit = 4.0 };
        double sgnBack = b.Soil > 0 ? 1 : -1;
        var parts = new List<Geometry>();
        try
        {
            var runs = sl.Runs;
            for (int k = 0; k < runs.Count; k++)
            {
                var r = runs[k];
                SecRun? pv = k > 0 ? runs[k - 1] : null, nx = k + 1 < runs.Count ? runs[k + 1] : null;
                double Ext(SecRun? nb, double w, Func<SecRun, double> side) => nb != null && w <= side(nb) + 1e-9 ? Math.Min(4 * w + 0.5, nb.S1 - nb.S0) : 0;
                foreach (var (w, sgn, side) in new (double, double, Func<SecRun, double>)[] { (r.Back, sgnBack, q => q.Back), (r.Front, -sgnBack, q => q.Front) })
                {
                    if (w <= 1e-9) continue;
                    var ls = Sub(r.S0 - Ext(pv, w, side), r.S1 + Ext(nx, w, side));
                    if (ls != null) parts.Add(ls.Buffer(sgn * w, bp));
                }
            }
        }
        catch (Exception ex) { why = "띠 버퍼 실패 " + ex.GetType().Name; return null; }
        if (parts.Count == 0) { why = "띠 폭이 모두 0"; return null; }
        Geometry g;
        try { g = parts.Count == 1 ? parts[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts); }
        catch (Exception ex) { why = "띠 합치기 실패 " + ex.GetType().Name; return null; }
        return RingOf(g, cs, zs, ox, oy, maxEdge, ref why);
    }

    /// <summary>★[v104 · 1021 화면 없는 Civil ⑧b · V33 · V34] 조각 삼각형에서 <b>앞면 선 뒤쪽 띠</b>(흙 쪽으로 0 &lt; 거리 &lt; <paramref name="dist"/>)를 들어낸다.
    /// <para>거리 = 점에서 앞면 선(꺾은선 · 양 끝은 곧게 늘인 것)까지 <b>부호 있는 거리</b>(흙 쪽 +) — 가장 가까운 변의 쪽으로 가른다. 삼각형마다 꼭짓점 거리를 변 따라 선형으로
    /// 잇고 0 · dist 높이에서 잘라(행진 삼각형) 남는 몫만 다시 세모로 나눈다(높이 = 원래 평면 · 자른 점은 두 꼭짓점 차례를 맞춰 계산 — 이웃 세모와 같은 점).</para>
    /// <list type="bullet">
    /// <item>도랑(패널식) 제 조각 — dist = ∞: 보통 경사로 지은 조각의 벽 뒤 법면을 들어내 바닥 뒤 끝 = 앞면 선(깎기면이 도랑 뒷벽 · 벽 뒤 터파기 0).
    /// 옆면을 거의 수직으로 세우면 Civil 삼각망이 못 받았다(⑧b «뒤집기 흉내 315mm»).</item>
    /// <item>다른 조각 — dist = <see cref="TrenchGap"/>: 도랑 바닥(앞면 선 앞)과 벽 뒤 다른 조각(위 단 도랑 · 이웃 조각) 사이를 얇게 갈라 두 구역이 한 점에서 두 높이를 갖지 않게
    /// (V34 «한 점 한 높이 600mm» — 깎기면은 세로 벽이라 한 삼각망으로 이을 수 없다).</item>
    /// <item>넓은 한쪽 버퍼로 자르던 종전 길은 선 끝이 꺾인 자리(날개)에서 버퍼가 앞쪽으로 넘쳐 도랑 바닥까지 지웠다(V33 ⑧b «앞 0.5 m 터파기 없음»).</item>
    /// </list></summary>
    /// <param name="extend">선 끝 너머 이 거리까지만 자른다(늘인 선 — 그 너머 조각은 그대로).</param>
    public static List<Tri> ClipBehind(IReadOnlyList<Tri> tris, Bottom b, double dist, out int cut, out string why, double extend = 0)
    {
        cut = 0; why = "";
        var res = new List<Tri>(tris.Count);
        if (b.Pts.Count < 2) { why = "바닥 줄이 없다"; res.AddRange(tris); return res; }
        var cs = new List<(double X, double Y)>();
        foreach (var q in b.Pts) if (cs.Count == 0 || Math.Abs(cs[^1].X - q.X) > 1e-12 || Math.Abs(cs[^1].Y - q.Y) > 1e-12) cs.Add((q.X, q.Y));
        if (cs.Count < 2) { why = "바닥 줄이 점 하나"; res.AddRange(tris); return res; }
        double sgn = b.Soil > 0 ? 1 : -1;
        var memo = new Dictionary<(double, double), double>();
        // 부호 있는 거리 · 끝 너머는 늘인 선 · 늘인 길이 밖이면 −1(자르지 않음 — 값은 꼭짓점마다 하나라 변 자름이 이웃과 같다)
        double SD(double x, double y)
        {
            if (memo.TryGetValue((x, y), out double v)) return v;
            double best = double.MaxValue, sd = 0; bool beyond = false;
            int n = cs.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                var a = cs[i]; var e = cs[i + 1];
                double ex = e.X - a.X, ey = e.Y - a.Y, L2 = ex * ex + ey * ey;
                if (L2 < 1e-24) continue;
                double t = ((x - a.X) * ex + (y - a.Y) * ey) / L2;
                double tc = Math.Max(0, Math.Min(1, t));
                double px = a.X + ex * tc - x, py = a.Y + ey * tc - y, d2 = px * px + py * py;
                double side = (ex * (y - a.Y) - ey * (x - a.X)) / Math.Sqrt(L2);   // 왼쪽 +
                bool endOut = (i == 0 && t < 0) || (i == n - 2 && t > 1);
                if (d2 < best - 1e-18 || (Math.Abs(d2 - best) <= 1e-18 && Math.Abs(side) > Math.Abs(sd)))
                {
                    best = d2;
                    sd = side;                                                        // 가장 가까운 변의 쪽(꺾임 바깥 쐐기는 두 변이 같은 쪽)
                    beyond = endOut && Math.Sqrt(L2) * Math.Max(-t, t - 1) > extend;  // 늘인 길이 밖
                }
            }
            v = beyond ? -1 : sgn * sd;
            // 선 위 · 틈 끝에 1 nm 안으로 놓인 꼭짓점은 딱 그 값으로 — 그 곁을 자르면 1e-10 m짜리 세모가 생겨 버려지며 이웃과 T 이음이 됐다(도랑 바닥 줄 점은 선 위)
            if (Math.Abs(v) < 1e-9) v = 0;
            else if (!double.IsInfinity(dist) && Math.Abs(v - dist) < 1e-9) v = dist;     // 늘인 길이 밖 = 자르지 않는 쪽(앞) — NaN으로 두고 세모째 남기면 이웃이 자른 변과 T 이음이 생겼다(V34 덮개 «테두리 엮기 0면»)
            memo[(x, y)] = v;
            return v;
        }
        // 자른 점 — 두 꼭짓점을 정해진 차례로(이웃 세모가 같은 점을 얻게)
        Point3 Cut(Point3 p, double vp, Point3 q, double vq, double level)
        {
            if (p.X > q.X || (p.X == q.X && p.Y > q.Y)) { (p, q) = (q, p); (vp, vq) = (vq, vp); }
            double t = (level - vp) / (vq - vp);
            return new Point3(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);
        }
        // 다각형(꼭짓점 · 값)을 «값 ≤ level»(keepBelow) 또는 «값 ≥ level» 쪽으로 자른다
        List<(Point3 P, double V)> ClipPoly(List<(Point3 P, double V)> poly, double level, bool keepBelow)
        {
            var o = new List<(Point3, double)>();
            for (int i = 0; i < poly.Count; i++)
            {
                var (p, vp) = poly[i]; var (q, vq) = poly[(i + 1) % poly.Count];
                bool ip = keepBelow ? vp <= level : vp >= level, iq = keepBelow ? vq <= level : vq >= level;
                if (ip) o.Add((p, vp));
                if (ip != iq && vp != level && vq != level) o.Add((Cut(p, vp, q, vq, level), level));   // 딱 level인 꼭짓점은 그대로 쓴다(자른 점을 또 만들지 않는다)
            }
            return o;
        }
        void Emit(List<(Point3 P, double V)> poly)
        {
            if (poly.Count < 3) return;
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                var a = poly[0].P; var bb = poly[i].P; var c = poly[i + 1].P;
                double ar = (bb.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (bb.Y - a.Y);
                if (Math.Abs(ar) < 1e-12) continue;
                res.Add(ar > 0 ? new Tri(a, bb, c) : new Tri(a, c, bb));
            }
        }
        foreach (var t in tris)
        {
            // 상자로 미리 거르지 않는다 — 걸러 세모째 남긴 쪽과 자른 이웃이 같은 변에서 T 이음을 만들 수 있다(값은 꼭짓점마다 하나 · 메모)
            double va = SD(t.A.X, t.A.Y), vb = SD(t.B.X, t.B.Y), vc = SD(t.C.X, t.C.Y);
            bool allFront = va <= 0 && vb <= 0 && vc <= 0, allFar = va >= dist && vb >= dist && vc >= dist;
            if (allFront || allFar) { res.Add(t); continue; }
            cut++;
            var tri = new List<(Point3 P, double V)> { (t.A, va), (t.B, vb), (t.C, vc) };
            Emit(ClipPoly(tri, 0, keepBelow: true));
            if (!double.IsInfinity(dist)) Emit(ClipPoly(tri, dist, keepBelow: false));
        }
        return res;
    }

    /// <summary>도랑 바닥과 벽 뒤 다른 조각 사이 틈(m) — 0.1 mm 합침 · 0.25 mm 짧은 변보다 넉넉히 · 횡단 수량에서 빠지는 몫은 틈 × 깊이(2 mm × 4 m = 0.008㎡).</summary>
    public const double TrenchGap = 0.002;

    /// <summary>꺾은선 양 끝을 <paramref name="d"/>만큼 줄인다(높이는 선 따라) — 선이 2d보다 짧으면 가운데 한 점 대신 빈 목록.</summary>
    public static List<Point3> Trim(IReadOnlyList<Point3> l, double d)
    {
        var r = new List<Point3>();
        if (l.Count < 2) return r;
        var cum = new double[l.Count];
        for (int i = 1; i < l.Count; i++) cum[i] = cum[i - 1] + Math.Sqrt(Sq(l[i].X - l[i - 1].X) + Sq(l[i].Y - l[i - 1].Y));
        double a = d, e = cum[^1] - d;
        if (e - a < 1e-6) return r;
        Point3 At(double s)
        {
            int i = 0;
            while (i < l.Count - 2 && cum[i + 1] < s) i++;
            double L = cum[i + 1] - cum[i], t = L < 1e-12 ? 0 : (s - cum[i]) / L;
            return new Point3(l[i].X + (l[i + 1].X - l[i].X) * t, l[i].Y + (l[i + 1].Y - l[i].Y) * t, l[i].Z + (l[i + 1].Z - l[i].Z) * t);
        }
        r.Add(At(a));
        for (int i = 1; i + 1 < l.Count; i++) if (cum[i] > a + 1e-9 && cum[i] < e - 1e-9) r.Add(l[i]);
        r.Add(At(e));
        return r;
    }

    /// <summary>띠 도형 → 가장 큰 다각형의 바깥 링(반시계 · 변 나눔 · 바닥 줄 위 가장 가까운 점의 z).</summary>
    static List<Point3>? RingOf(Geometry g, List<Coordinate> cs, List<double> zs, double ox, double oy, double maxEdge, ref string why)
    {
        Polygon? big = null;
        int np = 0;
        for (int i = 0; i < g.NumGeometries; i++) if (g.GetGeometryN(i) is Polygon p && p.Area > 1e-6) { np++; if (big == null || p.Area > big.Area) big = p; }
        if (big == null || big.Area < 1e-4) { why = "띠가 비었다"; return null; }
        if (np > 1) why = $"띠가 {np}조각 — 가장 큰 것만";
        var ring0 = big.ExteriorRing.Coordinates.ToList();
        if (ring0.Count > 1 && ring0[0].Equals2D(ring0[^1])) ring0.RemoveAt(ring0.Count - 1);
        if (!NetTopologySuite.Algorithm.Orientation.IsCCW(big.ExteriorRing.Coordinates)) ring0.Reverse();
        // ★[v104 · V34 L형 계측] 변 나눔 점이 바닥 줄 끝점에 2 cm 안으로 오면 한 칸 더 나눈다 — 끝점과 겹친 점이 법면 쪽으로 밀려 바닥 줄과 한 줄로 서면
        //   Civil이 그 줄 위 점들을 이은 납작한 세모를 만들 수 있다(곧은 변 위 꼭짓점을 통째로 빼 보니 띠 안에 기운 큰 세모가 깔려 V33 바닥이 9 cm 떠서 되돌림 · 진짜 뿌리는 구간 폭 2 mm 계단).
        var avoid = new[] { cs[0], cs[^1] };
        var ring = new List<Coordinate>(ring0.Count * 2);
        for (int i = 0; i < ring0.Count; i++)
        {
            var p = ring0[i]; var q = ring0[(i + 1) % ring0.Count];
            ring.Add(p);
            double L = Math.Sqrt(Sq(q.X - p.X) + Sq(q.Y - p.Y));
            int k = maxEdge > 0 ? (int)Math.Ceiling(L / maxEdge) : 1;
            for (int tries = 0; tries < 6; tries++)
            {
                bool hit = false;
                for (int j = 1; j < k && !hit; j++)
                {
                    double x = p.X + (q.X - p.X) * j / k, y = p.Y + (q.Y - p.Y) * j / k;
                    foreach (var a in avoid) if (Sq(x - a.X) + Sq(y - a.Y) < 0.02 * 0.02) { hit = true; break; }
                }
                if (!hit) break;
                k++;
            }
            for (int j = 1; j < k; j++) ring.Add(new Coordinate(p.X + (q.X - p.X) * j / k, p.Y + (q.Y - p.Y) * j / k));
        }
        var res = new List<Point3>(ring.Count);
        foreach (var c in ring)
        {
            double best = double.MaxValue, z = zs[0];
            for (int i = 0; i + 1 < cs.Count; i++)
            {
                var a = cs[i]; var e = cs[i + 1];
                double ex = e.X - a.X, ey = e.Y - a.Y, L2 = ex * ex + ey * ey;
                double t = L2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((c.X - a.X) * ex + (c.Y - a.Y) * ey) / L2));
                double dx = a.X + ex * t - c.X, dy = a.Y + ey * t - c.Y, dd = dx * dx + dy * dy;
                if (dd < best) { best = dd; z = zs[i] + (zs[i + 1] - zs[i]) * t; }
            }
            res.Add(new Point3(c.X + ox, c.Y + oy, z));
        }
        return res;
    }

    /// <summary>★2판(검토 중간 3) 도장 머리에 넣는 두 표의 지문 — FNV-1a 64(표를 고치면 «종단 먼저»가 뜬다).</summary>
    public static string TablesPrint(WallSection.Table cut, WallSection.Table fill)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in cut.Print + "|" + fill.Print) { h ^= ch; h *= 1099511628211UL; }
        return h.ToString("X16", CultureInfo.InvariantCulture);
    }

    // ══ 면 기록 DH_WALLSEC(★2판 · 검토 중간 3 — 수량은 표를 다시 찾지 않고 짓던 때의 단면을 쓴다) ══════════════

    /// <summary>기록 점 — XY · 앞 땅 · 전체 높이 · 근입 · 표 줄 번호.</summary>
    public readonly record struct RecPt(double X, double Y, double Toe, double HTot, double Df, int Row);

    /// <summary>기록 줄 — 형식 · 흙 쪽 · 절토인가 · 앞면 구배 · 점.</summary>
    public sealed class RecLine
    {
        public WallSection.Kind Kind;
        public int Soil;
        public bool Up;
        public bool Top;
        public double SlopeW;
        public List<RecPt> Pts = new();
        internal WallSection.Shape[]? Shapes;
        internal double X0, Y0, X1, Y1;
    }

    /// <summary>터파기면 하나의 단면 기록 — 여유 · 공통 칸 · 쓴 표(스냅숏) · 줄.</summary>
    public sealed class SecRecord
    {
        public double Margin;
        public WallSection.Common Common = new();
        public Dictionary<WallSection.Kind, WallSection.Table> Tables = new();
        public List<RecLine> Lines = new();
    }

    public const int RecordVersion = 3;   // 2 = 맨 위 단 칸(v104 B) · ★[v105] 3 = 표 스냅숏에 조건 · 칸 수가 바뀜(키 · 중력 앞면)

    /// <summary>단면 줄들로 기록을 만든다(쓴 형식의 표만 스냅숏).</summary>
    public static SecRecord RecordOf(IEnumerable<SecLine> lines, double margin, WallSection.Common c, Func<WallSection.Kind, WallSection.Table> table)
    {
        var r = new SecRecord { Margin = margin, Common = c };
        foreach (var l in lines)
        {
            if (!r.Tables.ContainsKey(l.Kind)) r.Tables[l.Kind] = table(l.Kind).Clone();
            r.Lines.Add(new RecLine { Kind = l.Kind, Soil = l.Soil, Up = l.Up, Top = l.Top, SlopeW = l.SlopeW, Pts = l.Pts.Select(p => new RecPt(p.X, p.Y, p.Toe, p.HTot, p.Df, p.Row)).ToList() });
        }
        return r;
    }

    /// <summary>기록 값(판 3) — [I 판 · D 여유 · D 버림 두께 · D 버림 돌출 · D 콘크리트 앞면 · I 표 수 · (I 형식 · I 뒤 · I 쌓인 높이 · I 지진 · D 앞면 경사 · I 칸 · I 줄 · D…)* · I 줄 수 · (I 형식 · I 흙 쪽 · I 절토 · I 맨 위 단 · D 앞면 구배 · I 점 수 · (D x · D y · D 앞 땅 · D H · D 근입 · I 표 줄)*)*].</summary>
    public static List<(char K, object V)> EncodeRecord(SecRecord r)
    {
        var l = new List<(char, object)> { ('I', RecordVersion), ('D', r.Margin), ('D', r.Common.TLean), ('D', r.Common.ELean), ('D', r.Common.FrontBatter), ('I', r.Tables.Count) };
        foreach (var t in r.Tables.Values)
        {
            int nc = WallSection.Of(t.Kind).Cols.Length;
            l.Add(('I', (int)t.Kind)); l.Add(('I', t.Cond.Back)); l.Add(('I', t.Cond.Fill)); l.Add(('I', t.Cond.Seismic ? 1 : 0)); l.Add(('D', t.Cond.FrontN));
            l.Add(('I', nc)); l.Add(('I', t.Rows.Count));
            foreach (var row in t.Rows) foreach (var v in row) l.Add(('D', v));
        }
        l.Add(('I', r.Lines.Count));
        foreach (var ln in r.Lines)
        {
            l.Add(('I', (int)ln.Kind)); l.Add(('I', ln.Soil)); l.Add(('I', ln.Up ? 1 : 0)); l.Add(('I', ln.Top ? 1 : 0)); l.Add(('D', ln.SlopeW)); l.Add(('I', ln.Pts.Count));
            foreach (var p in ln.Pts) { l.Add(('D', p.X)); l.Add(('D', p.Y)); l.Add(('D', p.Toe)); l.Add(('D', p.HTot)); l.Add(('D', p.Df)); l.Add(('I', p.Row)); }
        }
        return l;
    }

    /// <summary>기록을 읽는다 — 꼴이 틀리면 거짓 + 까닭(부르는 쪽은 구조물 공제를 안 하고 ⚠).</summary>
    public static bool TryDecodeRecord(IReadOnlyList<(char K, object V)>? vals, out SecRecord r, out string why)
    {
        r = new SecRecord(); why = "";
        if (vals == null || vals.Count == 0) { why = "단면 기록 없음"; return false; }
        int i = 0;
        try
        {
            int I() { var e = vals[i++]; if (e.K != 'I') throw new FormatException($"{i}째 칸이 정수가 아니다"); return (int)e.V; }
            double D() { var e = vals[i++]; if (e.K != 'D') throw new FormatException($"{i}째 칸이 실수가 아니다"); return (double)e.V; }
            int ver = I();
            if (ver != RecordVersion) { why = $"단면 기록 판 {ver}(이 판은 {RecordVersion}만 읽는다)"; return false; }
            r.Margin = D();
            double tl = D(), el = D(), fb = D();
            r.Common = new WallSection.Common(tl, el, fb, 0.01);
            int nt = I();
            for (int k = 0; k < nt; k++)
            {
                var kind = (WallSection.Kind)I();
                if (!Enum.IsDefined(typeof(WallSection.Kind), kind)) { why = $"단면 기록 — 모르는 형식 {(int)kind}"; return false; }
                int cb = I(), cf = I(), cs = I(); double cn = D();
                int nc = I(), nr = I();
                if (nc != WallSection.Of(kind).Cols.Length || nr < 1) { why = $"단면 기록 — {kind} 표 꼴(칸 {nc} · 줄 {nr})"; return false; }
                var t = new WallSection.Table { Kind = kind, Cond = WallSection.Normalize(kind, new WallSection.Cond(cb, cf, cs != 0, cn)) };
                for (int a = 0; a < nr; a++) { var row = new double[nc]; for (int c = 0; c < nc; c++) row[c] = D(); t.Rows.Add(row); }
                if (!WallSection.Valid(t, out string bad)) { why = $"단면 기록 — {kind} 표 값 {bad}"; return false; }
                r.Tables[kind] = t;
            }
            int nl = I();
            for (int k = 0; k < nl; k++)
            {
                var ln = new RecLine { Kind = (WallSection.Kind)I(), Soil = I(), Up = I() != 0, Top = I() != 0, SlopeW = D() };
                if (!r.Tables.ContainsKey(ln.Kind)) { why = $"단면 기록 — {k + 1}째 줄 형식 {ln.Kind}의 표가 없다"; return false; }
                int np = I();
                for (int a = 0; a < np; a++)
                {
                    var p = new RecPt(D(), D(), D(), D(), D(), I());
                    if (p.Row < 0 || p.Row >= r.Tables[ln.Kind].Rows.Count) { why = $"단면 기록 — {k + 1}째 줄 표 줄 번호 {p.Row}"; return false; }
                    ln.Pts.Add(p);
                }
                r.Lines.Add(ln);
            }
            if (i != vals.Count) { why = $"단면 기록 — 남는 칸 {vals.Count - i}"; return false; }
            return true;
        }
        catch (Exception ex) { why = $"단면 기록을 못 읽었다({i}째 칸 · {ex.GetType().Name}: {ex.Message})"; r = new SecRecord(); return false; }
    }

    // ══ 구조물 공제 — 기둥법(★2판 · 검토 높음 2) ══════════════════════════════════════

    /// <summary>한 절단선의 공제 넓이(㎡) — 구조물 · 빈 공간(단면 앞면 ~ 정지면 앞면 쐐기) · 구조물 아닌 단면 재료(보강토체 · 배수층 · 조약돌 뒤채움 — 되메우기에 남김).</summary>
    public sealed class ColumnSum
    {
        public double Struct, Air, Reinf;
        public int Lines;
        public string Text => string.Format(CultureInfo.InvariantCulture, "구조물 {0:F2}㎡ · 빈 공간 {1:F2}㎡{2}", Struct, Air, Reinf > 0 ? $" · 보강토체 등 {Reinf:F2}㎡(되메우기에 듦)" : "");
    }

    static void Prepare(SecRecord rec)
    {
        foreach (var ln in rec.Lines)
        {
            if (ln.Shapes != null) continue;
            var t = rec.Tables[ln.Kind];
            var c = rec.Common with { GradeFace = ln.SlopeW };
            ln.Shapes = ln.Pts.Select(p => WallSection.BuildRow(ln.Kind, t.Rows[p.Row], p.HTot, c, ln.Up, p.Df)).ToArray();
            double reach = rec.Margin + 1.0;
            foreach (var sh in ln.Shapes) reach = Math.Max(reach, Math.Max(-sh.XFront, sh.XBack) + rec.Margin + 1.0);
            ln.X0 = ln.Pts.Min(p => p.X) - reach; ln.X1 = ln.Pts.Max(p => p.X) + reach;
            ln.Y0 = ln.Pts.Min(p => p.Y) - reach; ln.Y1 = ln.Pts.Max(p => p.Y) + reach;
        }
    }

    /// <summary>★계획 6 — 절단선 위 점 (px, py)의 세로 기둥: 기록 줄마다 가장 가까운 앞면 선 자리(선분 안으로 떨어지는 것 모두 · 바깥 꺾임은 Mitre — 두 변 직선 거리 중 큰 쪽 ·
    /// 선 끝 너머는 안 본다)의 부호 있는 거리 d(흙 쪽 +)로 단면 x = d 세로선이 지나는 조각들의 z 구간을 모은다(여러 줄 · 여러 변은 합집합 — 두 번 안 뺀다).</summary>
    /// <param name="frac">몫이 1보다 작은 구조물(부벽 평균) — (구간 · 몫) · 겹치면 큰 몫. null이면 안 모은다.</param>
    /// <param name="key">★[v105] 활동방지벽 구간만 따로(바닥 밑 몫을 재려고) · null이면 안 모은다.</param>
    public static void ColumnAt(SecRecord rec, double px, double py, List<(double Lo, double Hi)> st, List<(double Lo, double Hi)> air, List<(double Lo, double Hi)> rf,
                                List<(double Lo, double Hi, double F)>? frac = null, List<(double Lo, double Hi)>? key = null)
    {
        Prepare(rec);
        st.Clear(); air.Clear(); rf.Clear(); frac?.Clear(); key?.Clear();
        foreach (var ln in rec.Lines)
        {
            if (px < ln.X0 || px > ln.X1 || py < ln.Y0 || py > ln.Y1) continue;
            var pts = ln.Pts; int n = pts.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                double ex = b.X - a.X, ey = b.Y - a.Y, L2 = ex * ex + ey * ey;
                if (L2 < 1e-18) continue;
                double t = ((px - a.X) * ex + (py - a.Y) * ey) / L2;
                if (t < 0 || t > 1) continue;
                double d = (ex * (py - a.Y) - ey * (px - a.X)) / Math.Sqrt(L2) * ln.Soil;
                int near = t < 0.5 ? i : i + 1;
                Add(ln, near, d, a.Toe + (b.Toe - a.Toe) * t, st, air, rf, frac, key);
            }
            for (int i = 1; i + 1 < n; i++)
            {
                var p0 = pts[i - 1]; var v = pts[i]; var p2 = pts[i + 1];
                double e1x = v.X - p0.X, e1y = v.Y - p0.Y, l1 = e1x * e1x + e1y * e1y;
                double e2x = p2.X - v.X, e2y = p2.Y - v.Y, l2 = e2x * e2x + e2y * e2y;
                if (l1 < 1e-18 || l2 < 1e-18) continue;
                double t1 = ((px - p0.X) * e1x + (py - p0.Y) * e1y) / l1, t2 = ((px - v.X) * e2x + (py - v.Y) * e2y) / l2;
                if (!(t1 > 1 && t2 < 0)) continue;                     // 바깥 꺾임 쐐기만
                double d1 = (e1x * (py - p0.Y) - e1y * (px - p0.X)) / Math.Sqrt(l1) * ln.Soil;
                double d2 = (e2x * (py - v.Y) - e2y * (px - v.X)) / Math.Sqrt(l2) * ln.Soil;
                if (Math.Sign(d1) != Math.Sign(d2)) continue;
                Add(ln, i, Math.Abs(d1) >= Math.Abs(d2) ? d1 : d2, v.Toe, st, air, rf, frac, key);
            }
        }
        Merge(st);
        if (key != null) Merge(key);
        Merge(air); Subtract(air, st);
        Merge(rf); Subtract(rf, st); Subtract(rf, air);
        if (frac != null && frac.Count > 1)
        {
            frac.Sort((a, b) => a.Lo.CompareTo(b.Lo));
            var m = new List<(double Lo, double Hi, double F)> { frac[0] };
            for (int i = 1; i < frac.Count; i++)
            {
                var (lo, hi, f) = m[^1];
                if (frac[i].Lo <= hi) m[^1] = (lo, Math.Max(hi, frac[i].Hi), Math.Max(f, frac[i].F)); else m.Add(frac[i]);
            }
            frac.Clear(); frac.AddRange(m);
        }
    }

    /// <summary>★★[v104.1 · 계획_v104_1.md 2.2 · JACK 1022 «종단 · 횡단에 옹벽 단면을 직접 넣어줘» · 2차 «각도대로 늘여서 맞춤»] 자른 자리 단면 하나.
    /// <para><see cref="Dist"/> = 자르는 선분 시작에서 교점까지 · <see cref="Sign"/> = 흙 쪽이 자르는 선 방향이면 +1 · <see cref="Stretch"/> = 1/sinθ(θ = 자르는 선과 앞면 변의 사잇각) ·
    /// 링 점 (X, Z) → 자르는 선 위 거리 Dist + Sign·X·Stretch · 높이 Toe + Z.</para></summary>
    /// <param name="Seg">자르는 꺾은선의 몇째 변인가 · <c>SegT</c> = 그 변 위 비율(종단은 이것으로 측점을 정확히 낸다).</param>
    public sealed record CutSec(double Dist, int Sign, double Stretch, double SinT, double Toe, WallSection.Shape Shape, WallSection.Kind Kind, bool Up, int Line, int Seg, double SegT);

    /// <summary>★[v104.1] 자르는 선분 a → b(횡단 표본선)와 기록 줄이 만나는 자리마다 — <see cref="CutSections(SecRecord, IReadOnlyList{ValueTuple{double, double}}, double, out int)"/>.</summary>
    public static List<CutSec> CutSections(SecRecord rec, double ax, double ay, double bx, double by, double minSin, out int skipped)
        => CutSections(rec, new[] { (ax, ay), (bx, by) }, minSin, out skipped);

    /// <summary>★[v104.1] 자르는 꺾은선(횡단 = 표본선 두 점 · 종단 = 선형을 잘게 편 것)과 기록 줄(앞면 선)이 만나는 자리마다 <b>수량과 같은 단면</b>
    /// (<see cref="ColumnAt"/>처럼 가까운 끝점의 단면 · 앞 땅만 보간 — 검토 중간 4).
    /// sinθ &lt; <paramref name="minSin"/>(벽과 거의 나란함)은 안 돌려주고 <paramref name="skipped"/>로 센다.
    /// 같은 줄에서 꺾은선 거리 1 mm 안 교점(꼭짓점 통과)은 하나 — 그릴 수 있는 쪽을 남긴다. 거리순.</summary>
    public static List<CutSec> CutSections(SecRecord rec, IReadOnlyList<(double X, double Y)> path, double minSin, out int skipped)
    {
        Prepare(rec);
        skipped = 0;
        var res = new List<CutSec>();
        if (path.Count < 2) return res;
        var cum = new double[path.Count];
        for (int k = 1; k < path.Count; k++) cum[k] = cum[k - 1] + Math.Sqrt(Sq(path[k].X - path[k - 1].X) + Sq(path[k].Y - path[k - 1].Y));
        var hits = new List<(double Dist, bool Skip, CutSec? C)>();
        for (int li = 0; li < rec.Lines.Count; li++)
        {
            var ln = rec.Lines[li];
            var pts = ln.Pts;
            hits.Clear();
            for (int k = 0; k + 1 < path.Count; k++)
            {
                double ax = path[k].X, ay = path[k].Y;
                double ux = path[k + 1].X - ax, uy = path[k + 1].Y - ay, uL = Math.Sqrt(ux * ux + uy * uy);
                if (uL < 1e-9) continue;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var p = pts[i]; var q = pts[i + 1];
                    double ex = q.X - p.X, ey = q.Y - p.Y, eL = Math.Sqrt(ex * ex + ey * ey);
                    if (eL < 1e-9) continue;
                    double den = ux * ey - uy * ex;                               // cross(u, e)
                    double sinT = Math.Abs(den) / (uL * eL);
                    if (sinT < 1e-9) continue;                                     // 나란함 — 안 만난다(겹침은 셈 밖)
                    double wx = p.X - ax, wy = p.Y - ay;
                    double u = (wx * ey - wy * ex) / den, t = (wx * uy - wy * ux) / den;
                    if (u < -1e-12 || u > 1 + 1e-12 || t < -1e-12 || t > 1 + 1e-12) continue;
                    u = Math.Clamp(u, 0, 1); t = Math.Clamp(t, 0, 1);
                    double dist = cum[k] + u * uL;
                    if (sinT < minSin) { hits.Add((dist, true, null)); continue; }
                    double nx = -ey / eL * ln.Soil, ny = ex / eL * ln.Soil;        // 흙 쪽 법선(SignedDist와 같은 정의)
                    int sg = nx * ux + ny * uy >= 0 ? +1 : -1;
                    int near = t < 0.5 ? i : i + 1;
                    hits.Add((dist, false, new CutSec(dist, sg, 1.0 / sinT, sinT, p.Toe + (q.Toe - p.Toe) * t, ln.Shapes![near], ln.Kind, ln.Up, li, k, u)));
                }
            }
            hits.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            for (int h = 0; h < hits.Count;)
            {
                int e2 = h + 1;
                while (e2 < hits.Count && hits[e2].Dist - hits[h].Dist < 1e-3) e2++;   // 한 자리(꼭짓점 통과)
                var draw = hits.Skip(h).Take(e2 - h).FirstOrDefault(x => !x.Skip);
                if (draw.C != null) res.Add(draw.C); else skipped++;
                h = e2;
            }
        }
        res.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        return res;
    }

    /// <summary>★★[v105 · 계획 검토 높음 1] 자르는 선분 a → b 위 활동방지벽 자리 — a에서 잰 거리 구간(넓은 쪽으로 정렬 · 겹치면 합침).
    /// 횡단 수량이 이 끝을 표본 축에 넣고(양쪽 0.1 mm) 그 칸들의 터파기 바닥을 키 밑까지 내린다(<see cref="KeyBottomAt"/>) — 전체 · 지층별 · 5 m 구분이 한 배열로 같이 맞는다.</summary>
    public static List<(double Lo, double Hi)> KeySpans(SecRecord rec, double ax, double ay, double bx, double by) => KeySpans(rec, ax, ay, bx, by, null);

    /// <param name="corners">★[설치 전 검토 낮음] 키 링 꼭짓점 자리(중력식 사다리꼴의 밑 모서리 등 — 표본 축에 넣으면 기운 밑면을 따라간다) · null이면 안 모은다.</param>
    public static List<(double Lo, double Hi)> KeySpans(SecRecord rec, double ax, double ay, double bx, double by, List<double>? corners)
    {
        var res = new List<(double Lo, double Hi)>();
        double L = Math.Sqrt(Sq(bx - ax) + Sq(by - ay));
        bool In(double d) { double t = L < 1e-12 ? 0 : d / L; return !double.IsNaN(KeyBottomAt(rec, ax + (bx - ax) * t, ay + (by - ay) * t)); }
        // 끝을 ColumnAt과 같은 판정(점마다 그 자리의 단면)으로 다듬는다 — 자른 자리 단면(가까운 끝점)과 줄이 다르면 0.1 m 안팎 어긋났다
        double Refine(double d, bool lowEnd)
        {
            double a = d - 0.3, b = d + 0.3;
            bool ia = In(a), ib = In(b);
            if (ia == ib) return d;
            for (int it = 0; it < 30; it++) { double m = (a + b) / 2; if (In(m) == ia) a = m; else b = m; }
            return (a + b) / 2;
        }
        foreach (var c in CutSections(rec, ax, ay, bx, by, 1e-3, out _))
        {
            var key = c.Shape.Parts.FirstOrDefault(p => p.Material == "활동방지벽");
            if (key == null) continue;
            double x0 = key.Ring.Min(q => q.X), x1 = key.Ring.Max(q => q.X);
            double d0 = c.Dist + c.Sign * x0 * c.Stretch, d1 = c.Dist + c.Sign * x1 * c.Stretch;
            double lo = Math.Min(d0, d1), hi = Math.Max(d0, d1);
            res.Add((Refine(lo, true), Refine(hi, false)));
            if (corners != null) foreach (var q in key.Ring) corners.Add(c.Dist + c.Sign * q.X * c.Stretch);
        }
        Merge(res);
        return res;
    }

    /// <summary>★[v105] 점 (px, py)의 세로 기둥에서 활동방지벽 밑(가장 낮은 z) — 없으면 NaN(<see cref="ColumnAt"/>과 같은 단면 · 같은 법).</summary>
    public static double KeyBottomAt(SecRecord rec, double px, double py)
    {
        var st = new List<(double Lo, double Hi)>(); var air = new List<(double Lo, double Hi)>(); var rf = new List<(double Lo, double Hi)>(); var key = new List<(double Lo, double Hi)>();
        ColumnAt(rec, px, py, st, air, rf, null, key);
        return key.Count == 0 ? double.NaN : key.Min(k => k.Lo);
    }

    static void Add(RecLine ln, int idx, double d, double toe, List<(double, double)> st, List<(double, double)> air, List<(double, double)> rf,
                    List<(double Lo, double Hi, double F)>? frac, List<(double Lo, double Hi)>? key = null)
    {
        var sh = ln.Shapes![idx];
        foreach (var part in sh.Parts)
        {
            if (part.Structure && part.Fraction < 1 - 1e-12)
            {
                if (frac == null) continue;
                var tmp = new List<(double, double)>(2);
                CrossZ(part.Ring, d, toe, tmp);
                foreach (var (a, b) in tmp) frac.Add((a, b, part.Fraction));
                continue;
            }
            var target = part.Structure ? st : rf;
            CrossZ(part.Ring, d, toe, target);
            if (key != null && part.Material == "활동방지벽") CrossZ(part.Ring, d, toe, key);
        }
        double fb = sh.FaceBatter, sw = Math.Max(0, ln.SlopeW), zTop = sh.HTotal - sh.Df;
        if (!sh.Trench && d > 0 && fb > sw + 1e-9)
        {
            double lo = d / fb, hi = sw > 1e-9 ? d / sw : double.PositiveInfinity;
            lo = Math.Max(0, lo); hi = Math.Min(zTop, hi);
            if (hi > lo + 1e-9) air.Add((toe + lo, toe + hi));
        }
    }

    /// <summary>링과 세로선 x의 z 구간(교점을 짝지어) — 변은 반열림 [min x, max x)로 센다.</summary>
    static void CrossZ(IReadOnlyList<(double X, double Z)> r, double x, double toe, List<(double, double)> outv)
    {
        var zs = new List<double>(4);
        for (int i = 0; i < r.Count; i++)
        {
            var p = r[i]; var q = r[(i + 1) % r.Count];
            double lo = Math.Min(p.X, q.X), hi = Math.Max(p.X, q.X);
            if (x < lo || x >= hi) continue;
            zs.Add(p.Z + (q.Z - p.Z) * (x - p.X) / (q.X - p.X));
        }
        if (zs.Count < 2) return;
        zs.Sort();
        for (int i = 0; i + 1 < zs.Count; i += 2) if (zs[i + 1] > zs[i]) outv.Add((toe + zs[i], toe + zs[i + 1]));
    }

    static void Merge(List<(double Lo, double Hi)> v)
    {
        if (v.Count < 2) return;
        v.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        var r = new List<(double, double)> { v[0] };
        for (int i = 1; i < v.Count; i++)
        {
            var (lo, hi) = r[^1];
            if (v[i].Lo <= hi) r[^1] = (lo, Math.Max(hi, v[i].Hi)); else r.Add(v[i]);
        }
        v.Clear(); v.AddRange(r);
    }

    static void Subtract(List<(double Lo, double Hi)> v, List<(double Lo, double Hi)> cut)
    {
        if (v.Count == 0 || cut.Count == 0) return;
        var r = new List<(double, double)>();
        foreach (var (lo0, hi0) in v)
        {
            double lo = lo0;
            foreach (var (cl, ch) in cut)
            {
                if (ch <= lo || cl >= hi0) continue;
                if (cl > lo) r.Add((lo, cl));
                lo = Math.Max(lo, ch);
                if (lo >= hi0) break;
            }
            if (lo < hi0) r.Add((lo, hi0));
        }
        v.Clear(); v.AddRange(r);
    }

    static double Within(List<(double Lo, double Hi)> v, double lo, double hi)
    {
        double s = 0;
        foreach (var (a, b) in v) { double x0 = Math.Max(a, lo), x1 = Math.Min(b, hi); if (x1 > x0) s += x1 - x0; }
        return s;
    }

    /// <summary>★계획 6 — 절단선(왼끝 L → 오른끝 R · 오프셋 −wl ~ wr) 위에서 [lo, hi](터파기 바닥 ~ 그 칸 지표) 안의 공제 넓이를 잰다.
    /// 칸(xs 사이)마다 <paramref name="step"/> 간격 가운데 점으로 기둥을 세운다(구조물 가장자리 오차 ≤ step/2 × 높이). lo · hi가 칸 양끝 어디서든 NaN이면 그 칸은 안 센다(되메우기 셈과 같다).</summary>
    public static ColumnSum Integrate(SecRecord rec, double lx, double ly, double rx, double ry, double wl, double wr,
                                      double[] xs, double[] lo, double[] hi, double step = 0.01)
    {
        var sum = new ColumnSum();
        if (rec.Lines.Count == 0 || xs.Length < 2 || wl + wr <= 0) return sum;
        Prepare(rec);
        // 절단선이 상자를 지나는 줄만
        double cx0 = Math.Min(lx, rx), cx1 = Math.Max(lx, rx), cy0 = Math.Min(ly, ry), cy1 = Math.Max(ly, ry);
        var lines = rec.Lines.Where(l => !(l.X1 < cx0 || l.X0 > cx1 || l.Y1 < cy0 || l.Y0 > cy1)).ToList();
        sum.Lines = lines.Count;
        if (lines.Count == 0) return sum;
        var sub = new SecRecord { Margin = rec.Margin, Common = rec.Common, Tables = rec.Tables, Lines = lines };
        var st = new List<(double, double)>(); var air = new List<(double, double)>(); var rf = new List<(double, double)>();
        var frac = new List<(double Lo, double Hi, double F)>();
        for (int k = 0; k + 1 < xs.Length; k++)
        {
            if (double.IsNaN(lo[k]) || double.IsNaN(lo[k + 1]) || double.IsNaN(hi[k]) || double.IsNaN(hi[k + 1])) continue;
            double w = xs[k + 1] - xs[k];
            if (w <= 0) continue;
            int m = Math.Max(1, (int)Math.Ceiling(w / step - 1e-9));
            double dx = w / m;
            for (int j = 0; j < m; j++)
            {
                double x = xs[k] + (j + 0.5) * dx, f = (x - xs[k]) / w;
                double zl = lo[k] + (lo[k + 1] - lo[k]) * f, zh = hi[k] + (hi[k + 1] - hi[k]) * f;
                if (zh <= zl) continue;
                double tt = (x + wl) / (wl + wr);
                ColumnAt(sub, lx + (rx - lx) * tt, ly + (ry - ly) * tt, st, air, rf, frac);
                if (st.Count == 0 && air.Count == 0 && rf.Count == 0 && frac.Count == 0) continue;
                sum.Struct += Within(st, zl, zh) * dx;
                // 부벽(평균) — 몫 × (지표 안 길이 − 온 구조물과 겹친 길이)
                foreach (var (fa, fb, ff) in frac)
                {
                    double x0 = Math.Max(fa, zl), x1 = Math.Min(fb, zh);
                    if (x1 <= x0) continue;
                    sum.Struct += ff * (x1 - x0 - Within(st, x0, x1)) * dx;
                }
                sum.Air += Within(air, zl, zh) * dx;
                sum.Reinf += Within(rf, zl, zh) * dx;
            }
        }
        return sum;
    }
}
