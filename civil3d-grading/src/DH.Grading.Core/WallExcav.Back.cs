using System.Globalization;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Core;

/// <summary>★★★[v104 B · 계획_v104_B.md 2판 · JACK 1021 «터파기 끝에서 지정 경사로 내려오다 옹벽 꼭대기 높이에서 평탄 · 경사 첫 값 1.5 · 절토 옹벽만»]
/// <b>옹벽 뒷채움 배면사면</b> — 절토 옹벽 뒤 되메우기 윗면.
/// <para>B(p) = max(E, min(L, max(T, D))) — T = 절토 옹벽 줄 꼭대기에서 정한 평탄 높이(<see cref="TopAt"/> · ★v105.1 — 가까운 벽 꼭대기 · 낮은 평탄이 경계까지) ·
/// D = 데이라잇(터파기 법면이 땅에 닿는 선)마다 1:n 원뿔의 <b>위 덮개</b> max_q(z(q) − |p − q|/n)(연속 · 기울기 ≤ 1/n) · L = Lower(정지면, 원지반) ·
/// E = 옹벽 터파기면(그 아래로는 안 내려간다 — 배면 경사가 터파기 경사보다 가파를 때).</para>
/// <para>구역 Ω = 터파기 안(E &lt; L) ∩ 가장 가까운 절토 줄의 흙 쪽 ∩ <b>정지면 = 원지반</b>(벽 뒤가 자연 땅 — 설계 사면이 있으면 그 면이 되메우기 윗면이라 안 건드림 · 내 판단) ∩ B &lt; L
/// ∩ ★[v105.1] 배면 후보 가면 C(<see cref="BackMask"/> — 그림과 수량이 같은 가면을 본다).</para>
/// <para>수량은 이 해석 식으로(터파기면에 붙인 기록 <c>DH_WALLBACK</c>) — 면(격자 삼각망)은 종단 · 횡단 그림용이다.</para></summary>
public static partial class WallExcav
{
    /// <summary>배면 대상 옹벽 줄 — 앞면 선 점마다 꼭대기 높이 T(Z) · 흙 쪽.</summary>
    public sealed class BackWall
    {
        public int Soil;
        public List<Point3> Pts = new();   // Z = 벽 꼭대기(앞 땅 + 보이는 높이)
    }

    /// <summary>★[v105.1 · 계획_v105_1.md 2 · 8] 배면 후보 가면 C — 격자(원점 X0 · Y0 · 간격 H · 크기 Nx × Ny) 마디 중 터파기 안 자연 땅
    /// (E &lt; L · 정지면 ≈ 원지반 — <see cref="Natural"/> · 흙 쪽은 BackAt이 점마다)인 것 · 행마다 구간(J · I0 · 길이).
    /// <see cref="WallBackBuilder"/>가 한 번 지어 기록에 적고 그림 · 수량이 같은 가면에서 같은 평탄 격자를 짓는다(면 함수 없이).</summary>
    public sealed class BackMask
    {
        public double X0, Y0, H;
        public int Nx, Ny;
        public List<(int J, int I0, int Len)> Runs = new();
        public int Count => Runs.Sum(r => r.Len);
    }

    /// <summary>배면 기록 — 경사 n · 대상 옹벽 줄 · 데이라잇 꺾은선(Z = 땅) · ★[v105.1] 후보 가면.</summary>
    public sealed class BackRecord
    {
        public double N = 1.5;
        public List<BackWall> Walls = new();
        public List<List<Point3>> Daylight = new();
        /// <summary>★[v105.1] 없으면(또는 빈 가면) Ω가 비어 배면 없음 — 대체 가면 없음(계획 검토 높음 2).</summary>
        public BackMask? Mask;
        internal double[]? DlBox;          // 데이라잇 변마다 상자(가지치기)
        internal double[]? TopSeg;         // ★[v104.1] 줄 선분마다 (ax · ay · az · bx · by · bz · 흙 쪽 · 줄 번호) — 칸 SegW개
        internal double[]? SegBox;         // ★[v105.1] 줄 선분마다 상자(x0 · y0 · x1 · y1)
        internal int[]? WallSeg;           // ★[v105.1] 줄마다 첫 선분 번호(TopSeg 안 · 줄 차례대로 · 끝 = 다음 줄 첫 번호)
        internal double TopZMin, TopZMax;
        internal BackGrid? Grid;           // ★[v105.1] 평탄 높이 격자(가면에서 처음 쓸 때 한 번)
    }

    /// <summary>2 = 원뿔 덮개 · 늘임 칸 없음(v104 B 2판) · 3 = 평탄 높이 T가 끊기지 않는 식(v104.1) ·
    /// 4 = 평탄 높이 = 가까운 벽 꼭대기 · 낮은 평탄이 후보 구역 안에서 1:n으로 이어짐 + 후보 가면(v105.1 — 뜻이 바뀌어 옛 기록은 «[종단도] 먼저») ·
    /// ★5 = 꼴은 4와 같고 뜻이 바뀜 — 직선이 뒷채움사면에 닿으면 끝까지 그 사면(v105.2 · <see cref="Touched"/>) · 옛 그림 면이 같은 도장으로 되쓰이지 않게 판을 올린다.</summary>
    public const int BackRecordVersion = 5;

    /// <summary>기록 값 — [I 판 · D n · I 줄 수 · (I 흙 쪽 · I 점 수 · (D x · D y · D T)*)* · I 데이라잇 수 · (I 점 수 · (D x · D y · D z)*)*
    /// · D X0 · D Y0 · D H · I Nx · I Ny · I 구간 수 · (I J · I I0 · I 길이)*].</summary>
    public static List<(char K, object V)> EncodeBack(BackRecord r)
    {
        var l = new List<(char, object)> { ('I', BackRecordVersion), ('D', r.N), ('I', r.Walls.Count) };
        foreach (var w in r.Walls)
        {
            l.Add(('I', w.Soil)); l.Add(('I', w.Pts.Count));
            foreach (var p in w.Pts) { l.Add(('D', p.X)); l.Add(('D', p.Y)); l.Add(('D', p.Z)); }
        }
        l.Add(('I', r.Daylight.Count));
        foreach (var d in r.Daylight)
        {
            l.Add(('I', d.Count));
            foreach (var p in d) { l.Add(('D', p.X)); l.Add(('D', p.Y)); l.Add(('D', p.Z)); }
        }
        var m = r.Mask;
        l.Add(('D', m?.X0 ?? 0.0)); l.Add(('D', m?.Y0 ?? 0.0)); l.Add(('D', m?.H ?? 0.0));
        l.Add(('I', m?.Nx ?? 0)); l.Add(('I', m?.Ny ?? 0)); l.Add(('I', m?.Runs.Count ?? 0));
        if (m != null) foreach (var (j, i0, len) in m.Runs) { l.Add(('I', j)); l.Add(('I', i0)); l.Add(('I', len)); }
        return l;
    }

    public static bool TryDecodeBack(IReadOnlyList<(char K, object V)>? vals, out BackRecord r, out string why)
    {
        r = new BackRecord(); why = "";
        if (vals == null || vals.Count == 0) { why = "배면 기록 없음"; return false; }
        int i = 0;
        try
        {
            int I() { var e = vals[i++]; if (e.K != 'I') throw new FormatException($"{i}째 칸이 정수가 아니다"); return (int)e.V; }
            double D() { var e = vals[i++]; if (e.K != 'D') throw new FormatException($"{i}째 칸이 실수가 아니다"); return (double)e.V; }
            int ver = I();
            if (ver != BackRecordVersion) { why = $"배면 기록 판 {ver}(이 판은 {BackRecordVersion}만 읽는다)"; return false; }
            r.N = D();
            if (!(r.N > 0)) { why = $"배면 기록 경사 {r.N}"; return false; }
            int nw = I();
            for (int k = 0; k < nw; k++)
            {
                var w = new BackWall { Soil = I() };
                int np = I();
                for (int a = 0; a < np; a++) w.Pts.Add(new Point3(D(), D(), D()));
                r.Walls.Add(w);
            }
            int nd = I();
            for (int k = 0; k < nd; k++)
            {
                int np = I(); var d = new List<Point3>(np);
                for (int a = 0; a < np; a++) d.Add(new Point3(D(), D(), D()));
                r.Daylight.Add(d);
            }
            var m = new BackMask { X0 = D(), Y0 = D(), H = D(), Nx = I(), Ny = I() };
            int nr = I();
            for (int k = 0; k < nr; k++)
            {
                int j = I(), i0 = I(), len = I();
                if (j < 0 || j >= m.Ny || i0 < 0 || len < 1 || i0 + len > m.Nx) { why = $"배면 가면 {k + 1}째 구간이 격자 밖({j}, {i0}, {len})"; r = new BackRecord(); return false; }
                m.Runs.Add((j, i0, len));
            }
            if (nr > 0 && !(m.H > 0)) { why = $"배면 가면 간격 {m.H}"; r = new BackRecord(); return false; }
            r.Mask = nr > 0 ? m : null;
            if (i != vals.Count) { why = $"배면 기록 — 남는 칸 {vals.Count - i}"; r = new BackRecord(); return false; }
            return true;
        }
        catch (Exception ex) { why = $"배면 기록을 못 읽었다({i}째 칸 · {ex.GetType().Name}: {ex.Message})"; r = new BackRecord(); return false; }
    }

    /// <summary>꺾은선까지 부호 있는 거리(흙 쪽 +) — 가장 가까운 변의 쪽 · 끝 너머는 곧게 늘인 선의 쪽 ·
    /// <paramref name="zNear"/> = 가장 가까운 자리의 Z(선 따라 보간 · 끝 너머는 끝점 Z) · <paramref name="past"/> = 끝 너머로 나간 거리(늘인 선 따라 · 안이면 0).</summary>
    public static double SignedDist(IReadOnlyList<Point3> l, int soil, double x, double y, out double zNear, out double dist, out double past)
    {
        double best = double.MaxValue, sd = 0; zNear = double.NaN; dist = double.NaN; past = 0;
        int n = l.Count;
        for (int i = 0; i + 1 < n; i++)
        {
            var a = l[i]; var e = l[i + 1];
            double ex = e.X - a.X, ey = e.Y - a.Y, L2 = ex * ex + ey * ey;
            if (L2 < 1e-24) continue;
            double t = ((x - a.X) * ex + (y - a.Y) * ey) / L2, tc = Math.Max(0, Math.Min(1, t));
            double px = a.X + ex * tc - x, py = a.Y + ey * tc - y, d2 = px * px + py * py;
            double side = (ex * (y - a.Y) - ey * (x - a.X)) / Math.Sqrt(L2);
            if (d2 < best - 1e-18 || (Math.Abs(d2 - best) <= 1e-18 && Math.Abs(side) > Math.Abs(sd)))
            {
                best = d2; sd = side; dist = Math.Sqrt(d2);
                bool endOut = (i == 0 && t < 0) || (i == n - 2 && t > 1);
                past = endOut ? Math.Sqrt(L2) * Math.Max(-t, t - 1) : 0;
                zNear = a.Z + (e.Z - a.Z) * tc;
            }
        }
        return (soil > 0 ? 1 : -1) * sd;
    }

    /// <summary>원뿔 위 덮개 — 선분 a→b(높이 선형) 위 점 q마다 z(q) − |p − q|/n 의 가장 큰 값(닫힌 식 · 끝점과 꺾임점 셋 중 큰 것) ·
    /// ★[v105.2] <paramref name="tBest"/> = 이긴 자리(변 위 비율 — 0 · 1이면 끝점 · |k| ≥ 1이면 높은 끝점이 이긴다).</summary>
    static double ConeSeg(Point3 a, Point3 b, double x, double y, double n, out double tBest)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y, L = Math.Sqrt(ex * ex + ey * ey);
        double F(double t) { double qx = a.X + ex * t, qy = a.Y + ey * t; return a.Z + (b.Z - a.Z) * t - Math.Sqrt((qx - x) * (qx - x) + (qy - y) * (qy - y)) / n; }
        double f0 = F(0), f1 = F(1);
        double best = f0 >= f1 ? f0 : f1;
        tBest = f0 >= f1 ? 0 : 1;
        if (L < 1e-12) return best;
        double t0 = ((x - a.X) * ex + (y - a.Y) * ey) / (L * L);
        double h = Math.Abs(ex * (y - a.Y) - ey * (x - a.X)) / L;       // 수선 거리
        double k = n * (b.Z - a.Z) / L;                                  // u/√(u² + h²) = n·dz/L
        if (Math.Abs(k) < 1 - 1e-12 && h > 1e-12)
        {
            double t = t0 + k * h / Math.Sqrt(1 - k * k) / L;
            if (t > 0 && t < 1) { double ft = F(t); if (ft > best) { best = ft; tBest = t; } }
        }
        return best;
    }

    /// <summary>★[v105.2] 닿음 걷기 간격 · «아래로 들어감» 판정 깊이(m) — 직선이 뒷채움사면 아래로 <b>1 cm 넘게</b> 들어가야 닿음.
    /// <para>1 mm로 두니 소단 끝을 1 mm 스치는 선이 걸음 자리에 따라 닿음 · 안 닿음으로 갈려 그 아래가 통째로 바뀌었다(하네스 S170 ⑩ 기운 데이라잇 31 cm) — 스침은 «다시 절토»가 아니다.
    /// 걸음 2 cm · 두 기울기 차 ≤ 1.5라 깊이 2.5 cm 넘는 닿음은 늘 잡고 1 cm 안은 늘 안 잡는다(그 사이는 걸음 자리에 달림 — 그림 · 수량은 같은 걸음이라 같은 답).</para></summary>
    const double TouchStep = 0.02, TouchTol = 0.01;

    /// <summary>★★★[v105.2 · 계획_v105_2.md 2판 §2 · JACK 1023 «뒷채움사면을 다시 절토하면서 들어갈 것 같으면 직선으로 오다가 만나는 선부터는 같은 사면으로(소단 있어도 그대로)» · 인터뷰 «끝까지 뒷채움사면»]
    /// 이긴 데이라잇 자리 q*(높이 <paramref name="qz"/>)에서 p까지 곧은 배면 직선이 그 앞에서 뒷채움사면 아래로 들어갔나.
    /// <para>q*에서 p까지 곧은 선 위 모든 점에서 q*가 이긴다(삼각 부등식 — 원뿔 덮개의 별 모양) → 그 선의 높이 = qz − s/n(곧다).
    /// <see cref="TouchStep"/>마다 걸어 D(s) &lt; E(s) − <see cref="TouchTol"/>인 자리가 하나라도 있으면 참(E가 없는 자리는 건너뜀).
    /// 걸음 자리는 길이에서만 나와 그림 · 수량이 같은 E 함수(같은 옹벽터파기 면 · 같은 캐시)면 같은 답이다.</para></summary>
    static bool Touched(BackRecord r, double qx, double qy, double qz, double x, double y, Func<double, double, double?> excAt)
    {
        double dx = x - qx, dy = y - qy, len = Math.Sqrt(dx * dx + dy * dy);
        for (int k = 1; k * TouchStep < len; k++)
        {
            double s = k * TouchStep;
            var e = excAt(qx + dx * s / len, qy + dy * s / len);
            if (e != null && qz - s / r.N < e.Value - TouchTol) return true;
        }
        return false;
    }

    /// <summary>★[v105.1] 터파기 안 자연 땅 — L 있음 · E &lt; L · 정지면 ≈ 원지반(1 cm). 후보 가면 C의 뜻(평탄 격자가 이어지는 길) — 소단 · 길 · 벽 앞(정지면 ≠ 원지반)과 터파기 밖에서 끊긴다.</summary>
    static bool Natural(double P, double G, double E, out double L)
    {
        L = double.IsNaN(P) ? G : double.IsNaN(G) ? P : Math.Min(P, G);
        if (double.IsNaN(L) || double.IsNaN(E) || !(E < L - 1e-6)) return false;
        return !(!double.IsNaN(P) && !double.IsNaN(G) && Math.Abs(P - G) > 0.01);   // 벽 뒤에 설계 사면(정지면 ≠ 원지반) — 그 면이 되메우기 윗면
    }

    /// <summary>★[v105.1] BackAt 앞 조건(그림 · 수량이 이 하나를 쓴다): L 있음 · E &lt; L · 가장 가까운 줄의 흙 쪽. 자연 땅(정지면 ≈ 원지반)은 <b>가면</b>이 정한다(<see cref="TopAt"/> — 그 칸 네 마디에 C).
    /// <para>★[계획 검토 중간 2 · 화면 없는 Civil V39 ④] 점마다 P ≈ G를 보면 그림(정지면_DH)과 수량(정지순수옹벽_DH — 벽 뒤에서 패드 높이로 깎여 원지반과 다른 자리가 있다)이 서로 다른 Ω를 봤다 —
    /// 가면은 그림 쪽 면으로 한 번 지어 기록에 있으므로 둘이 같은 Ω를 본다. 높이 상한 L은 부르는 쪽 면 그대로(수량은 자기 계획면 위로 배면을 안 올린다).</para>
    /// 흙 쪽은 점마다 따로 — 가면에는 안 넣는다(위 단 끝 너머 늘인 벽선 앞 가는 쐐기가 «흙 쪽 아님»으로 빠져 평탄 격자를 가르는 벽이 됐다 · 하네스 S170 ⑨ 위 단 끝 기울기 1.46).</summary>
    static bool PreOmega(BackRecord r, double x, double y, double P, double G, double E, out double L, out double bestD)
    {
        bestD = double.MaxValue;
        L = double.IsNaN(P) ? G : double.IsNaN(G) ? P : Math.Min(P, G);
        if (r.Walls.Count == 0 || r.Daylight.Count == 0 || double.IsNaN(L) || double.IsNaN(E) || !(E < L - 1e-6)) return false;
        // Ω — 가장 가까운 절토 줄의 흙 쪽이어야(끝 너머는 늘인 선)
        double sdB = 0;
        foreach (var w in r.Walls)
        {
            if (w.Pts.Count < 2) continue;
            double sd = SignedDist(w.Pts, w.Soil, x, y, out _, out double d, out _);
            // ★[v105.1] 같은 거리(따로 된 두 줄이 만나는 모서리의 이등분선)면 쪽이 뚜렷한 줄 — 앞 줄의 늘인 선 위(쪽 0)를 먼저 잡아 후보에서 빠지면
            //   마디 한 줄이 장벽이 되어 평탄 격자가 두 쪽으로 갈렸다(하네스 S170 ⑨ 볼록 모서리 2 m 끊김)
            if (d < bestD - 1e-12 || (Math.Abs(d - bestD) <= 1e-12 && Math.Abs(sd) > Math.Abs(sdB))) { bestD = Math.Min(bestD, d); sdB = sd; }
        }
        return sdB > 0;
    }

    /// <summary>★계획 1 — 점 (x, y)의 배면사면 높이 · Ω 안인가. <paramref name="P"/> · <paramref name="G"/> · <paramref name="E"/>는 그 점의 정지면 · 원지반 · 옹벽 터파기면(NaN = 없음).
    /// <paramref name="excAt"/> = 옹벽 터파기면 함수(★[v105.2] 닿음 걷기 — 그림 · 수량이 같은 면 · 같은 캐시를 넘긴다).
    /// <paramref name="flatHit"/> = 벽 곁(0.5 m 안)에서 B가 꼭대기보다 1 cm 넘게 높다(평탄 없이 경사로 벽에 닿음 — ⚠ 셈용).</summary>
    public static double BackAt(BackRecord r, double x, double y, double P, double G, double E, Func<double, double, double?> excAt, out bool inside, out bool flatHit)
    {
        if (excAt == null) throw new ArgumentNullException(nameof(excAt));
        inside = false; flatHit = false;
        if (!PreOmega(r, x, y, P, G, E, out double L, out double bestD)) return double.NaN;
        double T = TopAt(r, x, y);
        if (double.IsNaN(T)) return double.NaN;   // ★[v105.1 · 계획 검토 중간 2] 가면 밖(그 칸 네 마디에 C가 없음) — 그림 · 수량이 같은 Ω를 본다
        // 데이라잇 원뿔 위 덮개(상자로 가지치기 — 그 변이 줄 수 있는 가장 큰 값이 지금 값보다 작으면 건너뜀) · ★[v105.2] 이긴 자리 q*도
        PrepareBack(r);
        double Dz = double.NegativeInfinity, qx = 0, qy = 0, qz = 0;
        int bi = 0;
        foreach (var dl in r.Daylight)
            for (int i = 0; i + 1 < dl.Count; i++, bi++)
            {
                var bx = r.DlBox!;
                double dx = Math.Max(0, Math.Max(bx[bi * 5] - x, x - bx[bi * 5 + 2])), dy = Math.Max(0, Math.Max(bx[bi * 5 + 1] - y, y - bx[bi * 5 + 3]));
                if (bx[bi * 5 + 4] - Math.Sqrt(dx * dx + dy * dy) / r.N <= Dz) continue;
                double v = ConeSeg(dl[i], dl[i + 1], x, y, r.N, out double t);
                if (v > Dz)
                {
                    Dz = v;
                    var a = dl[i]; var b = dl[i + 1];
                    qx = a.X + (b.X - a.X) * t; qy = a.Y + (b.Y - a.Y) * t; qz = a.Z + (b.Z - a.Z) * t;
                }
            }
        if (double.IsNegativeInfinity(Dz)) return double.NaN;
        // ★★★[v105.2 · 계획_v105_2.md 2판 §2] 직선이 앞에서 뒷채움사면 아래로 들어갔으면(닿음) 직선을 버리고 뒷채움사면 · 평탄 — max(E, min(L, T)).
        //   D ≤ max(T, E)면 닿기 앞 · 뒤 식이 같아(D ≤ T: max(T, D) = T · T &lt; D ≤ E: 둘 다 E) 걷지 않는다 — 걷는 것은 직선 띠뿐
        //   ★[설치 전 검토 중간 1] T ≥ L이어도 두 식이 같다(둘 다 max(E, L)) — 걷지 않는다
        double X = Dz > Math.Max(T, E) && T < L && Touched(r, qx, qy, qz, x, y, excAt) ? T : Math.Max(T, Dz);
        double B = Math.Max(E, Math.Min(L, X));
        if (!(B < L - 1e-6)) return double.NaN;
        inside = true;
        flatHit = bestD < 0.5 && B > T + 0.01;
        return B;
    }

    /// <summary>★★★[v105.1 · 계획_v105_1.md 2 · 8 · JACK 1023 «배면이 평평하지 않음 · 기울어져 있음» · «배면사면선이 안 닿음»] 평탄 높이 T.
    /// <para>v104.1 식 max_i[z_i − (e_i − e_min)/n]은 벽에서 멀어지면 e_i − e_min이 0으로 가서 같은 줄 다른 자리 · 날개 벽의 높은 꼭대기를 끌어왔다
    /// (화면 없는 Civil V38 · JACK 사본 — 벽 곁보다 15 m 뒤 +0.47 m · 20 m 뒤 +3.04 m · 경사가 터파기 끝 2 m 안쪽에서 시작).</para>
    /// <para>지금: T0 = 가장 가까운(벌점 거리 e = d + K·max(0, −s)) 선분의 발 꼭대기 · <b>T_ero = C 격자 위 측지 침식</b> min_q[T0(q) + 경로 길이/n]
    /// (낮은 평탄이 후보 구역 안에서만 1:n으로 올라가며 높은 쪽과 잇는다 — 곧은 거리면 길 건너 벽의 평탄이 새어 골이 생겼다 · 계획 검토 높음 1) ·
    /// <b>Cw = C 격자 위 측지 팽창</b> max_q[T0(q) − e(q)/n − 경로 길이/n](벽 꼭대기에서 1:n으로 내려오는 원뿔 — 높은 벽 바로 뒤가 낮은 평탄에 눌려 비지 않게 ·
    /// 구역 안 경로라 길 · 소단 건너 벽의 원뿔은 안 넘어온다) · 격자 값 = max(T_ero, Cw) · <b>T = min(칸 보간, U)</b>(★v105.2 칸 보간 = «/» 대각선 세모 평면 — <see cref="GridAt"/>) · U = min_i(z_i + M·e_i).
    /// 곧은 벽 · 꼭대기 기울기 &lt; 1/n이면 T_ero = T0 = 그 자리 꼭대기 · Cw ≤ 그 값 → 벽에 직각으로 정확히 평탄.
    /// 두 격자 값이 모두 1/n 립시츠라 기울기 한도는 칸 보간 √2/n(벽 곁은 U의 M) — 꼭대기가 1/n보다 가파르게 변하는 벽은 높은 쪽 원뿔이 낮은 쪽 벽면 곁을 덮는다(U가 벽면에서 꼭대기로 누름).</para>
    /// <para>가면 밖(그 칸 네 마디에 C가 없음)은 NaN.</para></summary>
    internal const double TopCapSlope = 10.0;
    const double TopK = 1.0;

    static double TopAt(BackRecord r, double x, double y)
    {
        EnsureGrid(r);
        double Tr = GridAt(r.Grid!, x, y);
        if (double.IsNaN(Tr)) return double.NaN;
        double M = TopCapSlope;
        var seg = r.TopSeg!; var box = r.SegBox!;
        int ns = seg.Length / SegW;
        double U = double.PositiveInfinity;
        var pasts = Pasts(r, x, y);
        for (int i = 0; i < ns; i++)
        {
            int o = i * SegW, b = i * 4;
            double lbx = Math.Max(0, Math.Max(box[b] - x, x - box[b + 2])), lby = Math.Max(0, Math.Max(box[b + 1] - y, y - box[b + 3]));
            double lb = Math.Sqrt(lbx * lbx + lby * lby);                   // e ≥ d ≥ 상자 거리
            if (Math.Min(seg[o + 2], seg[o + 5]) + M * lb >= U) continue;
            var (e, z) = EZ(seg, i, x, y, pasts[(int)seg[o + 7]]);
            U = Math.Min(U, z + M * e);
        }
        return Math.Min(Tr, U);
    }

    const int SegW = 8;

    /// <summary>선분 i의 (벌점 거리 e, 발 꼭대기 z) — e = d + K·max(0, −s)(그 벽 앞이면 벌점) + <paramref name="past"/>(★[v105.1] 그 줄 끝 너머로 나간 거리 — <see cref="Pasts"/>).</summary>
    static (double E, double Z) EZ(double[] seg, int i, double x, double y, double past)
    {
        int o = i * SegW;
        double ax = seg[o], ay = seg[o + 1], az = seg[o + 2], bx = seg[o + 3], by = seg[o + 4], bz = seg[o + 5], soil = seg[o + 6];
        double ex = bx - ax, ey = by - ay, L2 = ex * ex + ey * ey;
        double t = L2 < 1e-24 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * ex + (y - ay) * ey) / L2));
        double qx = ax + ex * t - x, qy = ay + ey * t - y;
        double d = Math.Sqrt(qx * qx + qy * qy);
        double s = L2 < 1e-24 ? 0 : (ex * (y - ay) - ey * (x - ax)) / Math.Sqrt(L2) * soil;
        return (d + TopK * Math.Max(0, -s) + past, az + (bz - az) * t);
    }

    /// <summary>★[v105.1 · 화면 없는 Civil V40 · JACK 사본 횡단] 줄마다 끝 너머로 나간 거리(<see cref="SignedDist"/>의 past — 그 줄에서 가장 가까운 자리가 끝점이고 그 너머일 때만) —
    /// 그 줄의 <b>모든</b> 선분 벌점에 더한다. 맨 위 단이 끝난 자리 2.9 m 남쪽 단면에서 «가장 가까운 벽»이 맨 위 단 끝이라
    /// 그 단면이 직각으로 지나는 가운데 단 평탄과 0.7 m 차를 1:1.5로 이었다 — 끝을 지나간 만큼 그 줄의 몫을 덜어 마주한 벽이 평탄을 정한다.</summary>
    /// <para>★[설치 전 검토 중간 — 마디 × 선분 성능 절벽] <see cref="SignedDist"/>와 같은 답(가장 가까운 선분 · 같은 거리 동률은 쪽이 뚜렷한 것)을 선분 상자 가지치기로 —
    /// 먼 선분은 제곱근 없이 건너뛴다(1 km 사선 시험 격자 5.0 → 재서 적음).</para>
    static double[] Pasts(BackRecord r, double x, double y)
    {
        PrepareTop(r);
        var seg = r.TopSeg!; var box = r.SegBox!; var ws = r.WallSeg!;
        var p = new double[r.Walls.Count];
        for (int w = 0; w < r.Walls.Count; w++)
        {
            int s0 = ws[w], s1 = ws[w + 1];
            if (s1 - s0 < 1) continue;
            double best = double.MaxValue, sdB = 0, pastB = 0;
            for (int i = s0; i < s1; i++)
            {
                int b = i * 4;
                double lbx = Math.Max(0, Math.Max(box[b] - x, x - box[b + 2])), lby = Math.Max(0, Math.Max(box[b + 1] - y, y - box[b + 3]));
                if (lbx * lbx + lby * lby > best + 1e-18) continue;
                int o = i * SegW;
                double ax = seg[o], ay = seg[o + 1], ex = seg[o + 3] - ax, ey = seg[o + 4] - ay, L2 = ex * ex + ey * ey;
                if (L2 < 1e-24) continue;
                double t = ((x - ax) * ex + (y - ay) * ey) / L2, tc = Math.Max(0, Math.Min(1, t));
                double px = ax + ex * tc - x, py = ay + ey * tc - y, d2 = px * px + py * py;
                double side = (ex * (y - ay) - ey * (x - ax)) / Math.Sqrt(L2);
                if (d2 < best - 1e-18 || (Math.Abs(d2 - best) <= 1e-18 && Math.Abs(side) > Math.Abs(sdB)))
                {
                    best = d2; sdB = side;
                    bool endOut = (i == s0 && t < 0) || (i == s1 - 1 && t > 1);
                    pastB = endOut ? Math.Sqrt(L2) * Math.Max(-t, t - 1) : 0;
                }
            }
            p[w] = pastB;
        }
        return p;
    }

    /// <summary>T0 — 가장 가까운(벌점 거리) 선분의 발 꼭대기 · 그 줄 번호 · 그 벌점 거리.</summary>
    static double NearTop(BackRecord r, double x, double y, out int wall) => NearTop(r, x, y, out wall, out _);

    static double NearTop(BackRecord r, double x, double y, out int wall, out double eNear)
    {
        var seg = r.TopSeg!; var box = r.SegBox!;
        int ns = seg.Length / SegW;
        double bestE = double.MaxValue, bestZ = double.NaN; wall = -1;
        var pasts = Pasts(r, x, y);
        for (int i = 0; i < ns; i++)
        {
            int b = i * 4;
            double lbx = Math.Max(0, Math.Max(box[b] - x, x - box[b + 2])), lby = Math.Max(0, Math.Max(box[b + 1] - y, y - box[b + 3]));
            if (lbx * lbx + lby * lby >= bestE * bestE) continue;
            var (e, z) = EZ(seg, i, x, y, pasts[(int)seg[i * SegW + 7]]);
            if (e < bestE) { bestE = e; bestZ = z; wall = (int)seg[i * SegW + 7]; }
        }
        eNear = bestE;
        return bestZ;
    }

    static void PrepareTop(BackRecord r)
    {
        if (r.TopSeg != null) return;
        var l = new List<double>(); var bx = new List<double>();
        var wsg = new int[r.Walls.Count + 1];
        double zLo = double.MaxValue, zHi = double.MinValue;
        for (int w = 0; w < r.Walls.Count; w++)
        {
            var wl = r.Walls[w];
            wsg[w] = l.Count / SegW;
            for (int i = 0; i + 1 < wl.Pts.Count; i++)
            {
                var a = wl.Pts[i]; var b = wl.Pts[i + 1];
                l.AddRange(new[] { a.X, a.Y, a.Z, b.X, b.Y, b.Z, wl.Soil > 0 ? 1.0 : -1.0, w });
                bx.AddRange(new[] { Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y) });
                zLo = Math.Min(zLo, Math.Min(a.Z, b.Z)); zHi = Math.Max(zHi, Math.Max(a.Z, b.Z));
            }
        }
        r.TopZMin = zLo == double.MaxValue ? 0 : zLo; r.TopZMax = zHi == double.MinValue ? 0 : zHi;
        wsg[r.Walls.Count] = l.Count / SegW;
        r.SegBox = bx.ToArray();
        r.WallSeg = wsg;
        r.TopSeg = l.ToArray();
    }

    static void PrepareBack(BackRecord r)
    {
        if (r.DlBox != null) return;
        var l = new List<double>();
        foreach (var dl in r.Daylight)
            for (int i = 0; i + 1 < dl.Count; i++)
                l.AddRange(new[] { Math.Min(dl[i].X, dl[i + 1].X), Math.Min(dl[i].Y, dl[i + 1].Y), Math.Max(dl[i].X, dl[i + 1].X), Math.Max(dl[i].Y, dl[i + 1].Y), Math.Max(dl[i].Z, dl[i + 1].Z) });
        r.DlBox = l.ToArray();
    }

    // ───────────────────────── ★[v105.1] 후보 가면 · 평탄 격자 ─────────────────────────

    /// <summary>후보 가면 마디가 이보다 많으면 못 짓는다(«못 지음 = 배면 기록 없음 + 까닭» · 계획 검토 중간 4).</summary>
    public const int MaskMaxNodes = 3_000_000;
    /// <summary>후보 가면 격자 간격(m).</summary>
    public const double MaskStep = 0.25;
    public const string MaskTooBig = "배면 후보 마디가 너무 많다";

    /// <summary>★[v105.1 · 계획 2 · 검토 중간 4] 후보 가면 C를 짓는다 — 터파기면 세모 <paramref name="exc"/> 발자국 안 마디만(E = 세모 무게중심 좌표) · 정지면 · 원지반은 함수.
    /// 성공이면 r.Mask(마디 0이면 빈 가면 = 배면 없음) · 마디가 <see cref="MaskMaxNodes"/>를 넘으면 r.Mask = null · 거짓 + 까닭.</summary>
    public static bool BuildBackMask(BackRecord r, Func<double, double, double?> P, Func<double, double, double?> G, IReadOnlyList<Tri> exc, double h, out string note)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var t in exc) foreach (var q in new[] { t.A, t.B, t.C }) { minX = Math.Min(minX, q.X); minY = Math.Min(minY, q.Y); maxX = Math.Max(maxX, q.X); maxY = Math.Max(maxY, q.Y); }
        var m = NewMask(r, h, minX, minY, maxX, maxY);
        var seen = new HashSet<long>();
        var rows = new Dictionary<int, List<int>>();
        int nCand = 0;
        foreach (var t in exc)
        {
            double tx0 = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), tx1 = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
            double ty0 = Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), ty1 = Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y));
            double det = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
            if (Math.Abs(det) < 1e-18) continue;
            int i0 = (int)Math.Ceiling((tx0 - m.X0) / h - 1e-9), i1 = (int)Math.Floor((tx1 - m.X0) / h + 1e-9);
            int j0 = (int)Math.Ceiling((ty0 - m.Y0) / h - 1e-9), j1 = (int)Math.Floor((ty1 - m.Y0) / h + 1e-9);
            for (int j = Math.Max(0, j0); j <= Math.Min(m.Ny - 1, j1); j++)
                for (int i = Math.Max(0, i0); i <= Math.Min(m.Nx - 1, i1); i++)
                {
                    long key = (long)j * m.Nx + i;
                    if (seen.Contains(key)) continue;
                    double x = m.X0 + i * h, y = m.Y0 + j * h;
                    double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / det, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / det;
                    if (u < -1e-12 || v < -1e-12 || 1 - u - v < -1e-12) continue;
                    seen.Add(key);
                    double e = u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z;
                    if (!Natural(P(x, y) ?? double.NaN, G(x, y) ?? double.NaN, e, out _)) continue;
                    if (++nCand > MaskMaxNodes) { r.Mask = null; note = $"{MaskTooBig}({nCand:N0}개 넘음 · 한도 {MaskMaxNodes:N0})"; return false; }
                    if (!rows.TryGetValue(j, out var lst)) rows[j] = lst = new List<int>();
                    lst.Add(i);
                }
        }
        FillRuns(m, rows);
        r.Mask = m; r.Grid = null;
        note = string.Format(CultureInfo.InvariantCulture, "후보 가면 {0:F2}m {1}×{2} · 마디 {3} · 구간 {4}", h, m.Nx, m.Ny, nCand, m.Runs.Count);
        return true;
    }

    /// <summary>시험용 · 작은 꼴 — 벽 · 데이라잇 상자(+ <paramref name="margin"/> · 기본 3h) 안 모든 마디를 훑는다(E는 함수). 출하 길과 같은 <see cref="Natural"/>.</summary>
    public static bool BuildBackMask(BackRecord r, Func<double, double, double?> P, Func<double, double, double?> G, Func<double, double, double?> E, double h, out string note, double margin = -1)
    {
        var pts = r.Walls.SelectMany(w => w.Pts).Concat(r.Daylight.SelectMany(d => d)).ToList();
        if (pts.Count == 0) { r.Mask = new BackMask { H = h }; note = "대상 없음"; return true; }
        if (margin < 0) margin = 3 * h;
        var m = NewMask(r, h, pts.Min(p => p.X) - margin, pts.Min(p => p.Y) - margin, pts.Max(p => p.X) + margin, pts.Max(p => p.Y) + margin);
        if ((long)m.Nx * m.Ny > MaskMaxNodes) { r.Mask = null; note = $"{MaskTooBig}({m.Nx}×{m.Ny})"; return false; }
        var rows = new Dictionary<int, List<int>>();
        int nCand = 0;
        for (int j = 0; j < m.Ny; j++)
            for (int i = 0; i < m.Nx; i++)
            {
                double x = m.X0 + i * h, y = m.Y0 + j * h;
                var e = E(x, y);
                if (e == null || !Natural(P(x, y) ?? double.NaN, G(x, y) ?? double.NaN, e.Value, out _)) continue;
                nCand++;
                if (!rows.TryGetValue(j, out var lst)) rows[j] = lst = new List<int>();
                lst.Add(i);
            }
        FillRuns(m, rows);
        r.Mask = m; r.Grid = null;
        note = string.Format(CultureInfo.InvariantCulture, "후보 가면 {0:F2}m {1}×{2} · 마디 {3} · 구간 {4}", h, m.Nx, m.Ny, nCand, m.Runs.Count);
        return true;
    }

    static BackMask NewMask(BackRecord r, double h, double minX, double minY, double maxX, double maxY)
    {
        foreach (var p in r.Walls.SelectMany(w => w.Pts).Concat(r.Daylight.SelectMany(d => d)))
        { minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        double x0 = Math.Floor(minX / h) * h - h, y0 = Math.Floor(minY / h) * h - h;
        return new BackMask { X0 = x0, Y0 = y0, H = h, Nx = (int)Math.Ceiling((maxX - x0) / h) + 2, Ny = (int)Math.Ceiling((maxY - y0) / h) + 2 };
    }

    static void FillRuns(BackMask m, Dictionary<int, List<int>> rows)
    {
        foreach (var j in rows.Keys.OrderBy(k => k))
        {
            var l = rows[j]; l.Sort();
            int s = l[0], prev = l[0];
            for (int k = 1; k <= l.Count; k++)
            {
                if (k < l.Count && l[k] == prev + 1) { prev = l[k]; continue; }
                if (k < l.Count && l[k] == prev) continue;
                m.Runs.Add((j, s, prev - s + 1));
                if (k < l.Count) { s = l[k]; prev = l[k]; }
            }
        }
    }

    /// <summary>평탄 높이 격자 — C 마디(측지 침식 값) + C 곁 마디(이어 붙인 값). 열쇠 = (j + 2)·(Nx + 4) + (i + 2).</summary>
    internal sealed class BackGrid
    {
        public double X0, Y0, H;
        public int Nx, Ny;
        public readonly Dictionary<long, int> Idx = new();
        public readonly List<double> V = new();
        public readonly List<bool> InC = new();
        /// <summary>★[v105.2] 원뿔(벽 꼭대기에서 1:n으로 내려옴)이 침식보다 높아 이긴 C 마디 — 칸 보간 대각선 고르기(<see cref="GridAt"/>).</summary>
        public readonly List<bool> ConeWon = new();
        public long Key(int i, int j) => (long)(j + 2) * (Nx + 4) + (i + 2);
        public string Note = "";
    }

    static readonly (int Di, int Dj, double Len)[] Steps16 = BuildSteps();
    static (int, int, double)[] BuildSteps()
    {
        var l = new List<(int, int, double)>();
        foreach (var (a, b) in new[] { (1, 0), (0, 1), (1, 1), (1, 2), (2, 1) })
            foreach (int sa in new[] { 1, -1 })
                foreach (int sb in new[] { 1, -1 })
                {
                    var s = (a * sa, b * sb, Math.Sqrt(a * a + b * b));
                    if (!l.Contains(s)) l.Add(s);
                }
        return l.ToArray();
    }

    /// <summary>★[v105.1 · 계획 2 · 검토 높음 1] 가면에서 평탄 격자를 한 번 짓는다(결정론 — 그림 · 수량이 같은 값).
    /// <list type="number">
    /// <item>C 마디마다 T0(가장 가까운 선분 발 꼭대기 · 줄 번호).</item>
    /// <item>측지 침식 — 모든 C 마디를 T0로 씨앗 삼은 다익스트라(16이웃 · 대각 · 나이트 걸음은 지나는 칸이 모두 C일 때만 — 길 · 벽 · 소단을 못 건넌다). 걸음 값 = 길이·h/n.</item>
    /// <item>C 곁 마디(C 밖 · 8이웃에 C) = 자기 T0 + 같은 줄이 가장 가까운 이웃 C 마디의 (V − T0) 평균(꼭대기 기울기 그대로 이어짐 — 검토 낮음 3) · 같은 줄 이웃이 없으면 이웃 V 평균.</item>
    /// </list></summary>
    static void EnsureGrid(BackRecord r)
    {
        if (r.Grid != null) return;
        lock (r)
        {
            if (r.Grid != null) return;
            PrepareTop(r);
            var m = r.Mask;
            var g = new BackGrid { X0 = m?.X0 ?? 0, Y0 = m?.Y0 ?? 0, H = m?.H ?? 1, Nx = m?.Nx ?? 0, Ny = m?.Ny ?? 0 };
            if (m == null || m.Runs.Count == 0 || !(m.H > 0) || r.TopSeg!.Length == 0) { g.Note = "빈 가면"; r.Grid = g; return; }
            double h = m.H, n = r.N;
            var ci = new List<int>(); var cj = new List<int>(); var t0 = new List<double>(); var cw = new List<int>(); var w0 = new List<double>();
            foreach (var (j, i0, len) in m.Runs)
                for (int i = i0; i < i0 + len; i++)
                {
                    long k = g.Key(i, j);
                    if (g.Idx.ContainsKey(k)) continue;
                    g.Idx[k] = ci.Count; ci.Add(i); cj.Add(j);
                    double tz = NearTop(r, g.X0 + i * h, g.Y0 + j * h, out int w, out double en);
                    t0.Add(tz); cw.Add(w); w0.Add(tz - en / n);
                    g.InC.Add(true);
                }
            int nc = ci.Count;
            bool IsC(int i, int j) => g.Idx.TryGetValue(g.Key(i, j), out int id) && id < nc;
            // 측지 걸음 — 지나는 칸이 모두 C일 때만(대각: 두 곁 · 나이트: 지나가는 두 칸) · 값 = 길이·h/n
            double[] Geo(double[] init, bool minMode)
            {
                var val = (double[])init.Clone();
                var pq = new PriorityQueue<int, double>(nc);
                for (int k = 0; k < nc; k++) pq.Enqueue(k, minMode ? val[k] : -val[k]);
                var done = new bool[nc];
                while (pq.TryDequeue(out int u, out double pu))
                {
                    if (done[u] || (minMode ? pu > val[u] : -pu < val[u])) continue;
                    done[u] = true;
                    int iu = ci[u], ju = cj[u];
                    foreach (var (di, dj, len) in Steps16)
                    {
                        if (!g.Idx.TryGetValue(g.Key(iu + di, ju + dj), out int v) || v >= nc || done[v]) continue;
                        int adi = Math.Abs(di), adj = Math.Abs(dj), si = Math.Sign(di), sj = Math.Sign(dj);
                        if (adi == 1 && adj == 1 && !(IsC(iu + di, ju) && IsC(iu, ju + dj))) continue;
                        if (adi == 1 && adj == 2 && !(IsC(iu, ju + sj) && IsC(iu + di, ju + sj))) continue;
                        if (adi == 2 && adj == 1 && !(IsC(iu + si, ju) && IsC(iu + si, ju + dj))) continue;
                        double step = len * h / n;
                        if (minMode) { double nd = val[u] + step; if (nd < val[v] - 1e-12) { val[v] = nd; pq.Enqueue(v, nd); } }
                        else { double nd = val[u] - step; if (nd > val[v] + 1e-12) { val[v] = nd; pq.Enqueue(v, -nd); } }
                    }
                }
                return val;
            }
            var ero = Geo(t0.ToArray(), true);      // 낮은 평탄이 1:n으로 오르며 높은 쪽과 잇는다
            var cone = Geo(w0.ToArray(), false);    // 벽 꼭대기에서 1:n으로 내려오는 원뿔
            var dist = new double[nc];
            int nCone = 0;
            for (int k = 0; k < nc; k++) { dist[k] = Math.Max(ero[k], cone[k]); bool coneWins = cone[k] > ero[k] + 1e-9; if (coneWins) nCone++; g.ConeWon.Add(coneWins); }
            g.V.AddRange(dist);
            // C 곁 마디
            for (int k = 0; k < nc; k++)
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int i = ci[k] + di, j = cj[k] + dj;
                        long key = g.Key(i, j);
                        if (g.Idx.ContainsKey(key)) continue;
                        double x = g.X0 + i * h, y = g.Y0 + j * h;
                        double tr = NearTop(r, x, y, out int wr);
                        double sumS = 0, sumA = 0; int nS = 0, nA = 0;
                        for (int ei = -1; ei <= 1; ei++)
                            for (int ej = -1; ej <= 1; ej++)
                            {
                                if ((ei == 0 && ej == 0) || !g.Idx.TryGetValue(g.Key(i + ei, j + ej), out int id) || id >= nc) continue;
                                sumA += dist[id]; nA++;
                                if (cw[id] == wr) { sumS += dist[id] - t0[id]; nS++; }
                            }
                        g.Idx[key] = g.V.Count;
                        g.V.Add(nS > 0 ? tr + sumS / nS : sumA / nA);
                        g.InC.Add(false);
                        g.ConeWon.Add(false);
                    }
            g.Note = $"평탄 격자 C {nc} · 곁 {g.V.Count - nc} · 원뿔이 이긴 마디 {nCone}";
            r.Grid = g;
        }
    }

    /// <summary>칸 안 보간 — 칸 네 마디 중 C가 하나도 없으면 NaN(가면 밖). C가 하나라도 있으면 나머지는 C 또는 C 곁이라 값이 있다.
    /// <para>★[v105.2 · 화면 없는 Civil V45] 쌍선형 → <b>칸을 대각선으로 나눈 세모 평면 둘</b>. 쌍선형은 벽 높이가 다른 모서리 곁(원뿔 · 침식이 대각선으로 접힘)에서
    /// 칸 안이 안장처럼 휘어 평평한 세모로 된 그림 면이 4.4 cm 못 따라갔다. 세모 평면이면 그림 면이 같은 세모로 정확히 그린다(<see cref="BackSurface"/> 잘게 나눈 칸 — 후보 점수로 같은 대각선을 고른다) ·
    /// 마디 값은 그대로 · 축 따라 기울기 ≤ 1/n이라 크기 ≤ √2/n(쌍선형과 같은 한도).</para>
    /// <para>★[v105.2 설치 전 검토 중간 2] 대각선은 마디 값으로 고른다(«/» 고정은 접힘이 «\»로 가면 칸 가운데 오차가 쌍선형의 두 배) — 원뿔이 이긴 마디가 낀 칸은 골(두 평면의 큰 쪽 — max)이라
    /// 두 대각선 중 평균이 <b>낮은</b> 쪽 · 침식만인 칸은 등(작은 쪽 — min)이라 <b>높은</b> 쪽(접힘 선 위 두 마디가 그 평균을 낸다) · 같으면 «/». 그림 · 수량이 같은 함수라 같은 답.</para></summary>
    static double GridAt(BackGrid g, double x, double y)
    {
        if (g.V.Count == 0) return double.NaN;
        double fx = (x - g.X0) / g.H, fy = (y - g.Y0) / g.H;
        if (!(fx > -1 && fy > -1 && fx < g.Nx && fy < g.Ny)) return double.NaN;   // ★[설치 전 검토 낮음] 열쇠가 이웃 줄과 겹치는 범위 밖 조회 막기
        int i = (int)Math.Floor(fx), j = (int)Math.Floor(fy);
        double u = fx - i, v = fy - j;
        bool anyC = false, anyCone = false;
        double V(int ii, int jj)
        {
            if (!g.Idx.TryGetValue(g.Key(ii, jj), out int id)) return double.NaN;
            if (g.InC[id]) { anyC = true; if (g.ConeWon[id]) anyCone = true; }
            return g.V[id];
        }
        double a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
        if (!anyC || double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c) || double.IsNaN(d)) return double.NaN;
        bool slash = anyCone ? a + c <= b + d : a + c >= b + d;
        if (slash) return u >= v ? a + (b - a) * u + (c - b) * v : a + (c - d) * u + (d - a) * v;   // «/» (a — c)
        return u + v <= 1 ? a + (b - a) * u + (d - a) * v : d + (c - d) * u + (b - c) * (1 - v);    // «\» (b — d)
    }

    /// <summary>★계획 1 — 단면 기록에서 배면 대상 줄: 절토 · 도랑 아님(패널식은 벽 뒤 터파기가 없다) · <b>모든 단</b>(점마다 흙 쪽 가장 가까운 줄 — 단 수가 길이 따라 바뀌어도 · 아래 단 뒤 소단은 L ≈ T라 Ω가 저절로 빈다).
    /// 꼭대기 T = 앞 땅 + 보이는 높이(H − 근입).</summary>
    public static List<BackWall> BackWallsOf(SecRecord rec)
    {
        Prepare(rec);
        var res = new List<BackWall>();
        foreach (var ln in rec.Lines)
        {
            if (!ln.Up || ln.Pts.Count < 2 || ln.Shapes == null || ln.Shapes.Length == 0 || ln.Shapes[0].Trench) continue;
            res.Add(new BackWall { Soil = ln.Soil, Pts = ln.Pts.Select(p => new Point3(p.X, p.Y, p.Toe + p.HTot - p.Df)).ToList() });
        }
        return res;
    }

    /// <summary>★계획 1 — 데이라잇 꺾은선: 터파기면 테두리 변 중 두 끝이 기준면과 1 mm 안(땅에 닿은 변)이고, 가운데가 가장 가까운 대상 줄의 흙 쪽인 것을 이어 붙인다(점 Z = 기준면).
    /// 거리로 거르지 않는다(검토 높음 2 — 오르는 뒤 땅 · 소단 · 예산 늘린 조각의 먼 데이라잇이 빠졌다) · 예산 끝 · 도랑 자른 가장자리처럼 땅보다 낮은 테두리는 저절로 빠진다.</summary>
    public static List<List<Point3>> DaylightOf(IReadOnlyList<Tri> exc, Func<double, double, double?> L, List<BackWall> walls)
    {
        var res = new List<List<Point3>>();
        if (exc.Count == 0 || walls.Count == 0) return res;
        static ((double, double), (double, double)) EK(Point3 a, Point3 b) { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
        static ((double, double), (double, double)) EKk((double, double) a, (double, double) b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        var cnt = new Dictionary<((double, double), (double, double)), int>();
        var zOf = new Dictionary<(double, double), double>();
        foreach (var t in exc)
        {
            foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) }) { var k = EK(a, b); cnt[k] = cnt.TryGetValue(k, out int c) ? c + 1 : 1; }
            zOf[(t.A.X, t.A.Y)] = t.A.Z; zOf[(t.B.X, t.B.Y)] = t.B.Z; zOf[(t.C.X, t.C.Y)] = t.C.Z;
        }
        bool Day((double X, double Y) v) { var l = L(v.X, v.Y); return l != null && Math.Abs(l.Value - zOf[v]) < 1e-3; }
        bool Behind(double x, double y)
        {
            double bestD = double.MaxValue, sdB = 0;
            foreach (var w in walls)
            {
                if (w.Pts.Count < 2) continue;
                double sd = SignedDist(w.Pts, w.Soil, x, y, out _, out double d, out _);
                if (d < bestD - 1e-12 || (Math.Abs(d - bestD) <= 1e-12 && Math.Abs(sd) > Math.Abs(sdB))) { bestD = Math.Min(bestD, d); sdB = sd; }   // ★[v105.1] PreOmega와 같은 동률 규칙
            }
            return sdB > 0;
        }
        var adj = new Dictionary<(double, double), List<(double, double)>>();
        foreach (var kv in cnt)
        {
            if (kv.Value != 1) continue;
            var (a, b) = kv.Key;
            if (!Day(a) || !Day(b) || !Behind((a.Item1 + b.Item1) / 2, (a.Item2 + b.Item2) / 2)) continue;
            if (!adj.TryGetValue(a, out var la)) adj[a] = la = new(); la.Add(b);
            if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new(); lb.Add(a);
        }
        var used = new HashSet<((double, double), (double, double))>();
        Point3 P((double X, double Y) v) => new(v.X, v.Y, zOf[v]);
        void Walk((double, double) s)
        {
            var line = new List<Point3> { P(s) };
            var cur = s;
            while (true)
            {
                bool found = false;
                foreach (var nx in adj[cur])
                {
                    var k = EKk(cur, nx);
                    if (used.Contains(k)) continue;
                    used.Add(k); line.Add(P(nx)); cur = nx; found = true; break;
                }
                if (!found) break;
            }
            if (line.Count >= 2) res.Add(line);
        }
        foreach (var v in adj.Keys.Where(v => adj[v].Count == 1).ToList()) if (adj[v].Any(n => !used.Contains(EKk(v, n)))) Walk(v);
        foreach (var v in adj.Keys.ToList()) while (adj[v].Any(n => !used.Contains(EKk(v, n)))) Walk(v);
        return res;
    }

    /// <summary>그림 면 격자가 너무 커 안 지었다는 표시(<see cref="BackSurface"/>의 note 앞머리) — «땅과 같다»와 가른다(설치 전 검토 낮음 4).</summary>
    public const string BackTooBig = "격자가 너무 크다";

    /// <summary>★계획 3 · ★★[v105.1 · 계획_v105_1.md 3 · JACK 1023 «배면사면선이 안 닿음»] 그림용 배면사면 삼각망 — 상자 위 <paramref name="h"/> 격자 + <b>칸마다 마칭 스퀘어</b>.
    /// <para>종전: 세 꼭짓점이 모두 Ω 안인 세모만 — 테두리가 격자 계단만큼 안쪽이라 종단 · 횡단 선이 땅에 못 닿았다(V38 노선 0.27 m 아래에서 끝).</para>
    /// <para>지금: 안 · 밖이 갈리는 변마다 이분법(Ω 판정 20번 · 약 0.5 µm)으로 경계점 · 높이 = 안쪽 끝의 B. 칸 다각형(모서리 · 경계점을 둘레 차례로 — 볼록) → 부채꼴.
    /// 바늘 막기(검토 중간 5): 경계점이 안쪽 모서리 1 cm 안이면 버림 · 바깥 모서리 1 cm 안이면 그 모서리 자리(모서리마다 한 점 · Z = 정해진 훑기 차례에서 처음 붙은 변) ·
    /// 칸 다각형에서 같은 점 연달음은 지움 · 넓이 0 세모 버림. 경계점은 변마다 한 번(사전) — 이웃 칸과 이음이 맞는다.</para></summary>
    public static List<Tri> BackSurface(BackRecord r, Func<double, double, double?> P, Func<double, double, double?> G, Func<double, double, double?> E, double h, out string note, out int flatHits)
    {
        note = ""; flatHits = 0;
        var res = new List<Tri>();
        var pts = r.Walls.SelectMany(w => w.Pts).Concat(r.Daylight.SelectMany(d => d)).ToList();
        if (pts.Count == 0) { note = "대상 없음"; return res; }
        double x0 = Math.Floor(pts.Min(p => p.X) / h) * h - h, x1 = pts.Max(p => p.X) + h;
        double y0 = Math.Floor(pts.Min(p => p.Y) / h) * h - h, y1 = pts.Max(p => p.Y) + h;
        if (r.Mask != null && r.Mask.Runs.Count > 0)
        {
            // 가면이 벽 · 데이라잇 상자보다 넓을 수 있다(터파기면 발자국) — 상자를 가면까지
            var mk = r.Mask;
            x0 = Math.Min(x0, Math.Floor((mk.X0 + mk.Runs.Min(q => q.I0) * mk.H) / h) * h - h);
            x1 = Math.Max(x1, mk.X0 + mk.Runs.Max(q => q.I0 + q.Len) * mk.H + h);
            y0 = Math.Min(y0, Math.Floor((mk.Y0 + mk.Runs.Min(q => q.J) * mk.H) / h) * h - h);
            y1 = Math.Max(y1, mk.Y0 + (mk.Runs.Max(q => q.J) + 1) * mk.H + h);
        }
        int nx = (int)Math.Ceiling((x1 - x0) / h) + 1, ny = (int)Math.Ceiling((y1 - y0) / h) + 1;
        if ((long)nx * ny > 4_000_000) { note = $"{BackTooBig}({nx}×{ny})"; return res; }
        double Bp(double x, double y, out bool fh)
        {
            fh = false;
            var e = E(x, y);
            return e == null ? double.NaN : BackAt(r, x, y, P(x, y) ?? double.NaN, G(x, y) ?? double.NaN, e.Value, E, out _, out fh);
        }
        var z = new double[nx, ny];
        int nIn = 0;
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double b = Bp(x0 + i * h, y0 + j * h, out bool fh);
                z[i, j] = b;
                if (!double.IsNaN(b)) { nIn++; if (fh) flatHits++; }
            }
        const double Eps = 0.01;
        var edgePt = new Dictionary<(int, int, int, int), Point3?>();
        var cornerPt = new Dictionary<(int, int), Point3>();
        int nEdge = 0, nSnap = 0, nDrop = 0, nFanFail = 0;
        bool In(int i, int j) => !double.IsNaN(z[i, j]);
        Point3 Q(int i, int j) => new(x0 + i * h, y0 + j * h, z[i, j]);
        // 안 마디 (ia, ja) → 밖 마디 (ib, jb) 변의 경계점
        Point3? Cross(int ia, int ja, int ib, int jb)
        {
            var key = ia < ib || (ia == ib && ja < jb) ? (ia, ja, ib, jb) : (ib, jb, ia, ja);
            if (edgePt.TryGetValue(key, out var got)) return got;
            double ax = x0 + ia * h, ay = y0 + ja * h, bx = x0 + ib * h, by = y0 + jb * h;
            double lo = 0, hi = 1, zlo = z[ia, ja];
            for (int it = 0; it < 20; it++)
            {
                double mid = (lo + hi) / 2;
                double bm = Bp(ax + (bx - ax) * mid, ay + (by - ay) * mid, out _);
                if (double.IsNaN(bm)) hi = mid; else { lo = mid; zlo = bm; }
            }
            Point3? p;
            if (lo * h < Eps) { p = null; nDrop++; }
            else if ((1 - lo) * h < Eps)
            {
                if (!cornerPt.TryGetValue((ib, jb), out var cp)) { cp = new Point3(bx, by, zlo); cornerPt[(ib, jb)] = cp; }
                p = cp; nSnap++;
            }
            else { p = new Point3(ax + (bx - ax) * lo, ay + (by - ay) * lo, zlo); nEdge++; }
            edgePt[key] = p;
            return p;
        }
        // ★★[v105.2 · 계획_v105_2.md 2판 §3 · JACK 1023 스샷 — 직선이 소단에 닿는 자리 +5.5 cm · 소단 끝 −6.3 cm · 평탄 시작 +4.4 cm(화면 없는 Civil V43 · 현장 최대 14.5 cm V44)]
        //   칸마다 직선 보간이라 칸 안을 지나는 꺾임(직선 → 소단 · 소단 끝 · 평탄 시작 · 원뿔 변 바뀜)이 깎였다 →
        //   변마다 꺾임점(변 사전 — 이웃 칸이 같은 점 · T자 0)을 찾고, 칸 안에서는 서로 다른 변의 꺾임점 둘을 현으로 이어 가른다(셋 이상 · 현이 틀리면 칸 가운데 별 모양).
        int nKink = 0, nKinkEdge = 0, nChord = 0, nStar = 0;
        // 두 끝(a · b — B가 있는 점)의 토막 위 꺾임점(a → b 차례) — 4분점 셋이 곧으면(1 mm) 없음 · 아니면 17점 · 더글라스-포이커(0.5 mm) ·
        //   남은 표본 k마다 바깥 두 직선((k−2, k−1) · (k+1, k+2))의 교점(괄호 구간 안 · 그 자리 B가 직선과 0.5 mm 안일 때만 · 아니면 표본 k) · 표본에 NaN이 끼면 없음 · 두 끝 · 서로 1 cm 안은 버림
        List<Point3> KinksOn(Point3 a, Point3 b)
        {
            var outp = new List<Point3>();
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 2 * Eps) return outp;
            double F(double u) => Bp(a.X + dx * u, a.Y + dy * u, out _);
            bool straight = true;
            foreach (double u in new[] { 0.25, 0.5, 0.75 })
            {
                double f = F(u);
                if (double.IsNaN(f)) return outp;
                if (Math.Abs(f - (a.Z + (b.Z - a.Z) * u)) > 0.001) { straight = false; break; }
            }
            if (straight) return outp;
            const int N = 16;
            var us = new double[N + 1]; var fs = new double[N + 1];
            for (int k = 0; k <= N; k++)
            {
                us[k] = (double)k / N;
                fs[k] = k == 0 ? a.Z : k == N ? b.Z : F(us[k]);
                if (double.IsNaN(fs[k])) return outp;
            }
            var keep = new bool[N + 1];
            void DP(int lo, int hi)
            {
                if (hi - lo < 2) return;
                double worst = 0; int wk = -1;
                for (int k = lo + 1; k < hi; k++)
                {
                    double dev = Math.Abs(fs[k] - (fs[lo] + (fs[hi] - fs[lo]) * (us[k] - us[lo]) / (us[hi] - us[lo])));
                    if (dev > worst) { worst = dev; wk = k; }
                }
                if (worst <= 0.0005) return;
                keep[wk] = true; DP(lo, wk); DP(wk, hi);
            }
            DP(0, N);
            double last = 0;
            for (int k = 1; k < N; k++)
            {
                if (!keep[k]) continue;
                double uk = us[k], fk = fs[k];
                if (k >= 2 && k <= N - 2)
                {
                    double s1 = (fs[k - 1] - fs[k - 2]) / (us[k - 1] - us[k - 2]), s2 = (fs[k + 2] - fs[k + 1]) / (us[k + 2] - us[k + 1]);
                    if (Math.Abs(s1 - s2) > 1e-9)
                    {
                        double u = (fs[k + 1] - s2 * us[k + 1] - fs[k - 1] + s1 * us[k - 1]) / (s1 - s2);
                        if (u >= us[k - 1] && u <= us[k + 1])
                        {
                            double fl = fs[k - 1] + s1 * (u - us[k - 1]), fb = F(u);
                            if (!double.IsNaN(fb) && Math.Abs(fb - fl) <= 0.0005) { uk = u; fk = fb; }
                        }
                    }
                }
                if (uk * len < Eps || (1 - uk) * len < Eps || (outp.Count > 0 && (uk - last) * len < Eps)) continue;
                outp.Add(new Point3(a.X + dx * uk, a.Y + dy * uk, fk)); last = uk;
            }
            return outp;
        }
        // 변 위 점(경계점 · 꺾임점) — 정한 방향(작은 마디 → 큰 마디)으로 한 번만 지어 두 칸이 같은 점을 쓴다 · 부르는 차례(a → b)로 돌려준다
        var edgeList = new Dictionary<(int, int, int, int), List<(Point3 P, bool Kink)>>();
        List<(Point3 P, bool Kink)> EdgeList(int ia, int ja, int ib, int jb)
        {
            bool fwd = ia < ib || (ia == ib && ja < jb);
            var key = fwd ? (ia, ja, ib, jb) : (ib, jb, ia, ja);
            if (!edgeList.TryGetValue(key, out var l))
            {
                var (ai, aj, bi, bj) = key;
                l = new List<(Point3, bool)>();
                bool ina = In(ai, aj), inb = In(bi, bj);
                if (ina && inb) foreach (var p in KinksOn(Q(ai, aj), Q(bi, bj))) l.Add((p, true));
                else if (ina)
                {
                    var c = Cross(ai, aj, bi, bj);
                    if (c != null) { foreach (var p in KinksOn(Q(ai, aj), c.Value)) l.Add((p, true)); l.Add((c.Value, false)); }
                }
                else if (inb)
                {
                    var c = Cross(bi, bj, ai, aj);
                    if (c != null) { l.Add((c.Value, false)); var ks = KinksOn(Q(bi, bj), c.Value); ks.Reverse(); foreach (var p in ks) l.Add((p, true)); }
                }
                int nk = l.Count(e => e.Kink);
                nKink += nk; if (nk > 0) nKinkEdge++;
                edgeList[key] = l;
            }
            if (fwd) return l;
            var rv = new List<(Point3, bool)>(l); rv.Reverse(); return rv;
        }
        // 세모 꼴 — 넓이 · 높이(가장 긴 변에 대한 · ★[v105.2] 1 mm 넘게 — Civil이 0.1 mm 안 점을 합치고 바늘 세모를 뒤집는다(메모 civil3d-tin-merges-points-1e-4))
        static double Area2(Point3 a, Point3 b, Point3 c) => ((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
        static bool Fat(Point3 a, Point3 b, Point3 c, double minH)
        {
            double ar = Area2(a, b, c);
            if (!(ar > 1e-7)) return false;
            double l = Math.Max(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)), Math.Max(Math.Sqrt((c.X - b.X) * (c.X - b.X) + (c.Y - b.Y) * (c.Y - b.Y)), Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Y - c.Y) * (a.Y - c.Y))));
            return 2 * ar / l >= minH;
        }
        // ★★[v105.2 · V43 · 하네스 S170 ⑩ 30° 돌린 꼴] 칸 나누기 = 후보 중 식에 가장 맞는 것 — 꺾임 선이 모서리 곁을 지나면 꺾임점이 모서리에 합쳐져 하나만 남는다(그때 아무 꼭짓점 부채꼴은 10 cm 틀렸다) ·
        //   꺾임점이 세 변 넘게 있으면 어느 꼭짓점에서도 부채꼴이 안 펴진다(구멍 — 같은 시험 «못 나눈 칸» 18).
        //   후보: 꼭짓점마다 부채꼴 · 가운데(무게중심) 별 · 꺾임점에서 다른 꼭짓점으로 현(현 가운데 B가 현과 1 mm 안일 때만 — 반쪽은 다시 후보 중 가장 맞는 것 · 두 번까지).
        //   점수 = 세모마다 무게중심에서 |B − 세모 평면|의 가장 큰 값(칸 안 B 기억) · 0.5 mm 안이면 거기서 멈춤. 다각형이 볼록이라 별은 늘 된다(가운데 B가 없으면 꼭짓점 평균 높이).
        var memo = new Dictionary<(long, long), double>();
        double Bm(double x, double y)
        {
            var key = ((long)Math.Round(x * 1e7), (long)Math.Round(y * 1e7));
            if (!memo.TryGetValue(key, out double v)) { v = Bp(x, y, out _); memo[key] = v; }
            return v;
        }
        // ★[설치 전 검토 낮음] 무게중심이 Ω 밖(B 없음)이면 무게중심과 꼭짓점 사이 가운데에서 잰다 · 그래도 하나도 못 잰 후보는 가장 나쁨(1 m)
        double Score(List<Tri> ts, int from)
        {
            double w = 0; int nMeas = 0;
            for (int k = from; k < ts.Count; k++)
            {
                var t = ts[k];
                double gx = (t.A.X + t.B.X + t.C.X) / 3, gy = (t.A.Y + t.B.Y + t.C.Y) / 3, gz = (t.A.Z + t.B.Z + t.C.Z) / 3;
                double b = Bm(gx, gy);
                if (double.IsNaN(b))
                    foreach (var q in new[] { t.A, t.B, t.C })
                    {
                        double mx = (gx + q.X) / 2, my = (gy + q.Y) / 2, bq = Bm(mx, my);
                        if (double.IsNaN(bq)) continue;
                        double zq = ZIn(new List<Tri> { t }, mx, my);
                        if (!double.IsNaN(zq)) { w = Math.Max(w, Math.Abs(bq - zq)); nMeas++; break; }
                    }
                else { w = Math.Max(w, Math.Abs(b - gz)); nMeas++; }
            }
            return nMeas == 0 && ts.Count > from ? 1.0 : w;
        }
        bool FanAt(List<Point3> pg, int s, List<Tri> into, double minH)
        {
            for (int k = 1; k + 1 < pg.Count; k++) if (!Fat(pg[s], pg[(s + k) % pg.Count], pg[(s + k + 1) % pg.Count], minH)) return false;
            for (int k = 1; k + 1 < pg.Count; k++) into.Add(new Tri(pg[s], pg[(s + k) % pg.Count], pg[(s + k + 1) % pg.Count]));
            return true;
        }
        bool StarOf(List<Point3> pg, List<Tri> into, double minH)
        {
            double cx = pg.Average(p => p.X), cy = pg.Average(p => p.Y);
            double cz = Bm(cx, cy);
            if (double.IsNaN(cz)) cz = pg.Average(p => p.Z);
            var c = new Point3(cx, cy, cz);
            for (int k = 0; k < pg.Count; k++) if (!Fat(c, pg[k], pg[(k + 1) % pg.Count], minH)) return false;
            for (int k = 0; k < pg.Count; k++) into.Add(new Tri(c, pg[k], pg[(k + 1) % pg.Count]));
            return true;
        }
        const double GoodFit = 0.0005;
        // ★[설치 전 검토 중간 1] 칸 하나에서 견주는 후보 수 상한 — 원뿔이 휘는 칸(0.5 mm에 끝내 못 닿음)에서 꺾임점이 많으면 후보를 끝까지 훑어 칸 하나에 수 초가 걸릴 수 있었다(그때까지의 가장 맞는 것 · 별은 늘 마지막에 견줌)
        const int BestBudget = 60;
        int budget = BestBudget;
        (List<Tri>? Tris, double Score, bool Chord, bool Star) Best(List<Point3> pg, List<bool> kink, int depth, double minH)
        {
            List<Tri>? best = null; double bs = double.MaxValue; bool bc = false, bst = false;
            void Take(List<Tri> c, bool chord, bool star) { budget--; double s = Score(c, 0); if (s < bs) { bs = s; best = c; bc = chord; bst = star; } }
            if (pg.Count == 3) { var t3 = new List<Tri>(); if (FanAt(pg, 0, t3, minH)) return (t3, Score(t3, 0), false, false); }
            for (int s = 0; s < pg.Count && bs > GoodFit && budget > 0; s++) { var c = new List<Tri>(); if (FanAt(pg, s, c, minH)) Take(c, false, false); }
            if (bs > GoodFit && depth < 2)
                for (int a = 0; a < pg.Count && bs > GoodFit && budget > 0; a++)
                {
                    if (!kink[a]) continue;
                    for (int b = 0; b < pg.Count && bs > GoodFit && budget > 0; b++)
                    {
                        if (b == a || b == (a + 1) % pg.Count || a == (b + 1) % pg.Count || (kink[b] && b < a)) continue;
                        var p1 = pg[a]; var p2 = pg[b];
                        double mz = Bm((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                        if (double.IsNaN(mz) || Math.Abs(mz - (p1.Z + p2.Z) / 2) > 0.001) continue;
                        var h1 = new List<Point3>(); var k1 = new List<bool>(); var h2 = new List<Point3>(); var k2 = new List<bool>();
                        for (int k = a; ; k = (k + 1) % pg.Count) { h1.Add(pg[k]); k1.Add(kink[k]); if (k == b) break; }
                        for (int k = b; ; k = (k + 1) % pg.Count) { h2.Add(pg[k]); k2.Add(kink[k]); if (k == a) break; }
                        k1[0] = k1[^1] = k2[0] = k2[^1] = false;   // 현 끝은 반쪽에서 더 안 쓴다
                        var r1 = Best(h1, k1, depth + 1, minH); var r2 = Best(h2, k2, depth + 1, minH);
                        if (r1.Tris == null || r2.Tris == null) continue;
                        var c = new List<Tri>(r1.Tris); c.AddRange(r2.Tris);
                        Take(c, true, false);
                    }
                }
            if (bs > GoodFit) { var c = new List<Tri>(); if (StarOf(pg, c, minH)) Take(c, false, true); }
            return (best, bs, bc, bst);
        }
        var cs = new (int Di, int Dj)[] { (0, 0), (1, 0), (1, 1), (0, 1) };
        static void Dedupe(List<Point3> pg, List<bool> kk)
        {
            for (int k = pg.Count - 1; k >= 0 && pg.Count > 1; k--)
            {
                var a = pg[k]; var b = pg[(k + 1) % pg.Count];
                if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9) { kk[(k + 1) % pg.Count] |= kk[k]; pg.RemoveAt(k); kk.RemoveAt(k); }
            }
        }
        static double PolyArea(List<Point3> pg)
        {
            double pa = 0;
            for (int k = 0; k < pg.Count; k++) pa += pg[k].X * pg[(k + 1) % pg.Count].Y - pg[(k + 1) % pg.Count].X * pg[k].Y;
            return Math.Abs(pa) / 2;
        }
        static double ZIn(List<Tri> ts, double x, double y)
        {
            foreach (var t in ts)
            {
                double d = (t.B.Y - t.C.Y) * (t.A.X - t.C.X) + (t.C.X - t.B.X) * (t.A.Y - t.C.Y);
                if (Math.Abs(d) < 1e-18) continue;
                double u = ((t.B.Y - t.C.Y) * (x - t.C.X) + (t.C.X - t.B.X) * (y - t.C.Y)) / d, v = ((t.C.Y - t.A.Y) * (x - t.C.X) + (t.A.X - t.C.X) * (y - t.C.Y)) / d;
                if (u >= -1e-9 && v >= -1e-9 && 1 - u - v >= -1e-9) return u * t.A.Z + v * t.B.Z + (1 - u - v) * t.C.Z;
            }
            return double.NaN;
        }
        bool AllIn(int i, int j) => In(i, j) && In(i + 1, j) && In(i + 1, j + 1) && In(i, j + 1);
        // 칸 하나 — 다각형(모서리 · 변 위 점 둘레 차례) → 대각선 둘(점 없는 안 칸) 또는 후보 중 가장 맞는 것
        (List<Tri>? Tris, bool Chord, bool Star) Cell(int i, int j)
        {
            var pg = new List<Point3>(12); var kk = new List<bool>(12);
            for (int k = 0; k < 4; k++)
            {
                var (ai, aj) = (i + cs[k].Di, j + cs[k].Dj); var (bi, bj) = (i + cs[(k + 1) % 4].Di, j + cs[(k + 1) % 4].Dj);
                if (In(ai, aj)) { pg.Add(Q(ai, aj)); kk.Add(false); }
                foreach (var (p, isK) in EdgeList(ai, aj, bi, bj)) { pg.Add(p); kk.Add(isK); }
            }
            Dedupe(pg, kk);
            // ★[v105.2 · 계획 검토 중간 4] 대각선 두 세모는 변에 점이 하나도 없을 때만 — 이웃이 쓰는 꺾임점을 안 쓰면 T자 이음
            //   ★[v105.2 · 화면 없는 Civil V45 — 45° 꺾임 선(소단 모서리 접힘)이 마디를 지나 칸 대각선으로 가면 변에 꺾임점이 없다 · 반대 대각선이면 4 cm 틀림] 칸 가운데 B에 더 맞는 대각선
            if (AllIn(i, j) && pg.Count == 4)
            {
                double zc = Bp(x0 + (i + 0.5) * h, y0 + (j + 0.5) * h, out _);
                bool other = !double.IsNaN(zc) && Math.Abs(zc - (z[i + 1, j] + z[i, j + 1]) / 2) < Math.Abs(zc - (z[i, j] + z[i + 1, j + 1]) / 2) - 1e-9;
                return (other ? new List<Tri> { new(Q(i, j), Q(i + 1, j), Q(i, j + 1)), new(Q(i + 1, j), Q(i + 1, j + 1), Q(i, j + 1)) }
                              : new List<Tri> { new(Q(i, j), Q(i + 1, j), Q(i + 1, j + 1)), new(Q(i, j), Q(i + 1, j + 1), Q(i, j + 1)) }, false, false);
            }
            // 넓이 0 다각형(Ω 끝이 격자 선과 겹쳐 점이 한 줄 — 그 변의 꺾임점은 이웃 칸이 쓴다)은 그릴 것이 없다
            if (pg.Count < 3 || !(PolyArea(pg) > 1e-7)) return (new List<Tri>(), false, false);
            memo.Clear();
            budget = BestBudget;
            var got = Best(pg, kk, 0, 0.001);
            if (got.Tris == null) { budget = BestBudget; got = Best(pg, kk, 0, 0); }   // 1 mm 높이로 안 되면 넓이만(구멍보다 낫다 — 셈)
            return (got.Tris, got.Chord, got.Star);
        }
        // ── ★★[v105.2 · 화면 없는 Civil V45 — 평탄 높이 격자(0.25 m)가 칸 안에서 접히는 자리(벽 높이가 다른 모서리 곁 원뿔 · 침식) 4.4 cm] 휘는 칸은 반으로 잘게 ──
        //   1차: 칸마다 그려 보고 네 모서리가 모두 안인 칸에서 가운데 · 작은 칸 가운데 넷의 B와 2 mm 넘게 다르면 «잘게».
        //   잘게 칸의 네 변 가운데 점을 변 사전에 넣는다(이웃 칸도 그 점을 써 T자 0) → 2차: 바뀐 변이 있는 칸은 다시 · 잘게 칸은 작은 칸 넷(가운데 점 · 안쪽 변 꺾임점)을 후보 중 가장 맞게.
        const double FineTol = 0.002;
        var first = new Dictionary<(int, int), (List<Tri>? Tris, bool Chord, bool Star)>();
        var fine = new HashSet<(int, int)>();
        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j + 1 < ny; j++)
            {
                if (!(In(i, j) || In(i + 1, j) || In(i + 1, j + 1) || In(i, j + 1))) continue;
                var r1 = Cell(i, j);
                first[(i, j)] = r1;
                if (r1.Tris == null || r1.Tris.Count == 0) continue;
                double worst = 0;
                foreach (var (fu, fv) in new[] { (0.5, 0.5), (0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75) })
                {
                    double px = x0 + (i + fu) * h, py = y0 + (j + fv) * h;
                    double b = Bp(px, py, out _), zt = ZIn(r1.Tris, px, py);
                    if (!double.IsNaN(b) && !double.IsNaN(zt)) worst = Math.Max(worst, Math.Abs(b - zt));
                }
                if (worst > FineTol) fine.Add((i, j));
            }
        var midOf = new Dictionary<(int, int, int, int), Point3>();
        var changed = new HashSet<(int, int, int, int)>();
        int nMid = 0;
        (int, int, int, int) Key(int ia, int ja, int ib, int jb) => ia < ib || (ia == ib && ja < jb) ? (ia, ja, ib, jb) : (ib, jb, ia, ja);
        // 변 가운데 점 — 1 cm 안에 점이 이미 있으면 그 점 · 없으면 B(밖이면 null) · <paramref name="put"/>면 새 점을 변 사전에 끼움(정한 방향 차례)
        Point3? Mid(int ia, int ja, int ib, int jb, bool put)
        {
            var key = Key(ia, ja, ib, jb);
            if (midOf.TryGetValue(key, out var got)) return got;
            EdgeList(ia, ja, ib, jb);
            var l = edgeList[key];
            var a = Q(key.Item1, key.Item2); var b = Q(key.Item3, key.Item4);
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            foreach (var (p, _) in l) if (Math.Abs(p.X - mx) < Eps && Math.Abs(p.Y - my) < Eps) { if (put) midOf[key] = p; return p; }
            double mz = Bp(mx, my, out _);
            if (double.IsNaN(mz)) return null;
            var m = new Point3(mx, my, mz);
            if (!put) return m;
            int at = l.Count;
            for (int k = 0; k < l.Count; k++) if (Math.Abs(l[k].P.X - a.X) + Math.Abs(l[k].P.Y - a.Y) > Math.Abs(mx - a.X) + Math.Abs(my - a.Y)) { at = k; break; }
            l.Insert(at, (m, true));
            midOf[key] = m; changed.Add(key); nMid++;
            return m;
        }
        // 잘게 칸 변 가운데 — 두 끝이 안이면 가운데도 안이어야 · 두 끝이 밖이면 가운데도 밖이어야(변 사전의 경계점이 맞는 반쪽에 있다 — 이분법 첫 걸음이 그 가운데다) · 아니면 잘게 안 함
        //   ★[설치 전 검토 낮음] 넷을 먼저 재기만 하고 다 맞으면 그때 끼운다 — 종전엔 끼운 뒤 «잘게 안 함»으로 돌아서도 점이 남아 그 곁 Ω 끝 모양이 바뀌었다
        bool MidOk(int ia, int ja, int ib, int jb)
        {
            var m = Mid(ia, ja, ib, jb, false);
            bool ina = In(ia, ja), inb = In(ib, jb);
            return ina && inb ? m != null : !(!ina && !inb && m != null);
        }
        foreach (var (i, j) in fine.ToList())
        {
            if (!MidOk(i, j, i + 1, j) || !MidOk(i + 1, j, i + 1, j + 1) || !MidOk(i + 1, j + 1, i, j + 1) || !MidOk(i, j + 1, i, j)) { fine.Remove((i, j)); continue; }
            Mid(i, j, i + 1, j, true); Mid(i + 1, j, i + 1, j + 1, true); Mid(i + 1, j + 1, i, j + 1, true); Mid(i, j + 1, i, j, true);
        }
        // 잘게 칸 — 작은 칸 넷(반시계: 00 · 10 · 11 · 01) · 꼭짓점이 밖일 수 있다(Ω 끝 칸) — 작은 칸마다 마칭 스퀘어(칸 변 반쪽 점은 변 사전 · 안쪽 변은 여기서 이분법 · 꺾임점)
        List<Tri>? FineCell(int i, int j)
        {
            Point3? V(int ii, int jj) => In(ii, jj) ? Q(ii, jj) : null;
            Point3? M(int ia, int ja, int ib, int jb) => midOf.TryGetValue(Key(ia, ja, ib, jb), out var m) && !double.IsNaN(m.Z) ? m : null;
            var c00 = V(i, j); var c10 = V(i + 1, j); var c11 = V(i + 1, j + 1); var c01 = V(i, j + 1);
            var mB = M(i, j, i + 1, j); var mR = M(i + 1, j, i + 1, j + 1); var mT = M(i + 1, j + 1, i, j + 1); var mL = M(i, j + 1, i, j);
            double cx = x0 + (i + 0.5) * h, cy = y0 + (j + 0.5) * h, czv = Bp(cx, cy, out _);
            Point3? C = double.IsNaN(czv) ? null : new Point3(cx, cy, czv);
            // 칸 변(반시계 차례 목록)을 기하 가운데에서 둘로 — 가운데 점 자신은 뺀다
            (List<Point3> A, List<Point3> B) Split(List<(Point3 P, bool Kink)> l, int ia, int ja, int ib, int jb)
            {
                var s = Q(ia, ja); var e = Q(ib, jb);
                double half = (Math.Abs(e.X - s.X) + Math.Abs(e.Y - s.Y)) / 2;
                var a = new List<Point3>(); var b = new List<Point3>();
                foreach (var (p, _) in l)
                {
                    double d = Math.Abs(p.X - s.X) + Math.Abs(p.Y - s.Y);
                    if (Math.Abs(d - half) < 1e-9) continue;
                    (d < half ? a : b).Add(p);
                }
                return (a, b);
            }
            // 안쪽 변(변 가운데 m → 칸 가운데 C) 점 — 둘 다 안: 꺾임점 · 한쪽만 안: 경계점(이분법) + 안쪽 끝과 그 사이 꺾임점 · m → C 차례
            List<Point3> Inner(Point3? m, double mx, double my)
            {
                if (m != null && C != null) return KinksOn(m.Value, C.Value);
                if (m == null && C == null) return new List<Point3>();
                var pin = (m ?? C)!.Value;
                double ox = m == null ? mx : cx, oy = m == null ? my : cy;
                double lo = 0, hi = 1, zlo = pin.Z;
                for (int it = 0; it < 20; it++)
                {
                    double md = (lo + hi) / 2, bm = Bp(pin.X + (ox - pin.X) * md, pin.Y + (oy - pin.Y) * md, out _);
                    if (double.IsNaN(bm)) hi = md; else { lo = md; zlo = bm; }
                }
                double len = Math.Sqrt((ox - pin.X) * (ox - pin.X) + (oy - pin.Y) * (oy - pin.Y));
                if (lo * len < Eps) return new List<Point3>();
                lo = Math.Min(lo, 1 - Eps / len);
                double bx = pin.X + (ox - pin.X) * lo, by = pin.Y + (oy - pin.Y) * lo, bz = Bp(bx, by, out _);
                var bp = new Point3(bx, by, double.IsNaN(bz) ? zlo : bz);
                var ks = KinksOn(pin, bp); ks.Add(bp);
                if (m == null) ks.Reverse();   // C → 경계 → (m 자리) 를 m → C 차례로
                return ks;
            }
            var (b1, b2) = Split(EdgeList(i, j, i + 1, j), i, j, i + 1, j);
            var (r1, r2) = Split(EdgeList(i + 1, j, i + 1, j + 1), i + 1, j, i + 1, j + 1);
            var (t1, t2) = Split(EdgeList(i + 1, j + 1, i, j + 1), i + 1, j + 1, i, j + 1);
            var (l1, l2) = Split(EdgeList(i, j + 1, i, j), i, j + 1, i, j);
            var kB = Inner(mB, cx, y0 + j * h); var kR = Inner(mR, x0 + (i + 1) * h, cy); var kT = Inner(mT, cx, y0 + (j + 1) * h); var kL = Inner(mL, x0 + i * h, cy);
            List<Point3> Rev(List<Point3> s) { var r = new List<Point3>(s); r.Reverse(); return r; }
            // ★[설치 전 검토 낮음] 작은 칸 다각형 — 형이 정해진 두 가지(꼭짓점 하나 · 변 위 점 목록)만 받는다(종전 object 상자 확인은 nullable 아닌 점을 말없이 뺄 수 있었다)
            static void One(List<Point3> pg, List<bool> kk, Point3? p, bool kink) { if (p != null) { pg.Add(p.Value); kk.Add(kink); } }
            static void Many(List<Point3> pg, List<bool> kk, List<Point3> ps) { foreach (var p in ps) { pg.Add(p); kk.Add(true); } }
            var all = new List<Tri>();
            memo.Clear();
            for (int q = 0; q < 4; q++)
            {
                var pg = new List<Point3>(16); var kk = new List<bool>(16);
                switch (q)
                {
                    case 0: One(pg, kk, c00, false); Many(pg, kk, b1); One(pg, kk, mB, true); Many(pg, kk, kB); One(pg, kk, C, true); Many(pg, kk, Rev(kL)); One(pg, kk, mL, true); Many(pg, kk, l2); break;
                    case 1: One(pg, kk, mB, true); Many(pg, kk, b2); One(pg, kk, c10, false); Many(pg, kk, r1); One(pg, kk, mR, true); Many(pg, kk, kR); One(pg, kk, C, true); Many(pg, kk, Rev(kB)); break;
                    case 2: One(pg, kk, C, true); Many(pg, kk, Rev(kR)); One(pg, kk, mR, true); Many(pg, kk, r2); One(pg, kk, c11, false); Many(pg, kk, t1); One(pg, kk, mT, true); Many(pg, kk, kT); break;
                    default: One(pg, kk, mL, true); Many(pg, kk, kL); One(pg, kk, C, true); Many(pg, kk, Rev(kT)); One(pg, kk, mT, true); Many(pg, kk, t2); One(pg, kk, c01, false); Many(pg, kk, l1); break;
                }
                Dedupe(pg, kk);
                if (pg.Count < 3 || !(PolyArea(pg) > 1e-7)) continue;
                budget = BestBudget;
                var got = Best(pg, kk, 0, 0.001);
                if (got.Tris == null) { budget = BestBudget; got = Best(pg, kk, 0, 0); }
                if (got.Tris == null) return null;
                all.AddRange(got.Tris);
            }
            return all.Count == 0 ? null : all;   // ★[설치 전 검토 낮음] 작은 칸 넷이 모두 비면 큰 칸으로(말없는 구멍 막기)
        }
        int nFine = 0;
        for (int i = 0; i + 1 < nx; i++)
            for (int j = 0; j + 1 < ny; j++)
            {
                if (!first.TryGetValue((i, j), out var rc)) continue;
                if (fine.Contains((i, j)))
                {
                    var ft = FineCell(i, j);
                    if (ft != null) { res.AddRange(ft); nFine++; continue; }
                    rc = Cell(i, j);   // 가운데 B가 없으면 큰 칸으로(넣은 가운데 점은 다각형에 들어간다)
                }
                else if (changed.Contains(Key(i, j, i + 1, j)) || changed.Contains(Key(i + 1, j, i + 1, j + 1)) || changed.Contains(Key(i + 1, j + 1, i, j + 1)) || changed.Contains(Key(i, j + 1, i, j)))
                    rc = Cell(i, j);
                if (rc.Tris == null) { nFanFail++; continue; }
                res.AddRange(rc.Tris);
                if (rc.Chord) nChord++;
                if (rc.Star) nStar++;
            }
        note = string.Format(CultureInfo.InvariantCulture, "격자 {0:F2}m {1}×{2} · 안 {3}점 · 경계점 {4}(모서리 붙임 {5} · 버림 {6}) · 꺾임점 {7}(변 {8} · 현 {9} · 별 {10}) · 잘게 칸 {11}(가운데 점 {12}) · 세모 {13}{14}{15}",
            h, nx, ny, nIn, nEdge, nSnap, nDrop, nKink, nKinkEdge, nChord, nStar, nFine, nMid, res.Count, nFanFail > 0 ? $" · ⚠못 나눈 칸 {nFanFail}" : "", flatHits > 0 ? $" · ⚠평탄 없이 벽에 닿는 점 {flatHits}" : "");
        return res;
    }
}
