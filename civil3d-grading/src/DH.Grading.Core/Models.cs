namespace DH.Grading.Core;

/// <summary>3D 점 (도면 좌표계, 단위 m). XY는 평면, Z는 표고.</summary>
public readonly record struct Point3(double X, double Y, double Z);

/// <summary>
/// 원지반(原地盤) 표면 조회 인터페이스 — Core는 AutoCAD/Civil3D에 의존하지 않으므로
/// 표고 조회를 이 인터페이스로 추상화한다. Civil3D 측에서 TinSurface를 감싸 구현한다.
/// </summary>
public interface IGroundSurface
{
    /// <summary>(x,y)에서의 원지반 표고. 표면 범위를 벗어나면 false.</summary>
    bool TryGetElevation(double x, double y, out double z);
}

/// <summary>
/// [구간 구배 0804 — JACK] 계획경계 둘레의 한 구간에서 '이 단부터는 이 구배' 규칙 묶음.
/// 옹벽·사면 복귀·구배 변경이 전부 이 하나로 표현된다:
///   · 옹벽      = (시작단, 최소구배 0.05)   — 그 단부터 수직
///   · 사면 복귀 = (되돌릴 단, 전역 구배)     — 그 단부터 원래 사면(옛 ToBench가 하던 일)
///   · 구배 변경 = (시작단, 새 구배)          — 그 단부터 다른 구배
/// 규칙은 시작단 오름차순. 어떤 단의 구배 = 시작단이 그 단 이하인 규칙 중 '마지막' 것(없으면 전역 구배).
/// 여러 번 클릭하면 규칙이 쌓여 '아래는 급하게 · 위는 완만하게'가 된다(JACK 0804).
/// </summary>
public sealed class SlopeZone
{
    /// <summary>계획경계 호길이 구간 [T0,T1] — T0 &gt; T1이면 0을 지나 이어지는(랩) 구간.</summary>
    public double T0 { get; set; }
    public double T1 { get; set; }

    /// <summary>(시작단, 구배 n, 소단폭 m) 목록 — <see cref="Normalize"/>로 시작단 오름차순 정렬해 둔다.
    /// 소단폭이 음수면 '전역값 따름'(옛 번들에서 올라온 옹벽 구간이 그렇다).
    /// ※ 단높이는 구간별로 둘 수 없다 — 링은 '같은 표고의 등고선'이라 링 하나에 표고가 하나뿐인데,
    ///   구간마다 단높이가 다르면 같은 링에서 구간 안/밖의 표고가 달라져야 해서 표현이 안 된다.
    ///   구배·소단폭은 표고 순서를 바꾸지 않고 가로 방향만 바꾸므로 구간별로 안전하다(JACK 0804 — B안).</summary>
    public List<(int FromBench, double Slope, double BenchW)> Rules { get; set; } = new();

    /// <summary>이 단에 적용될 (구배, 소단폭) — 해당 규칙이 없는 항목은 전역값. Rules는 정렬 전제.</summary>
    public (double Slope, double BenchW) At(int bench, double baseSlope, double baseW)
    {
        double s = baseSlope, w = baseW;
        foreach (var r in Rules)
        {
            if (bench < r.FromBench) break;
            s = r.Slope;
            if (r.BenchW >= 0) w = r.BenchW;   // 소단 0(소단 없음)은 유효한 값
        }
        return (s, w);
    }

    /// <summary>이 단에 적용될 구배 — 해당 규칙이 없으면 baseSlope(전역 구배).</summary>
    public double SlopeAt(int bench, double baseSlope) => At(bench, baseSlope, 1).Slope;

    /// <summary>가장 낮은 규칙 시작단 — 이 단 미만은 전역 구배 그대로라 구간이 영향을 주지 않는다. 규칙이 없으면 무한대.</summary>
    public int FirstBench => Rules.Count > 0 ? Rules[0].FromBench : int.MaxValue;

    private List<Point3>? _ref;
    private double[]? _refCum;

    /// <summary>★★★[JACK 0824 "단마다 해당 단의 가상 계획폴리곤을 기억하고 그걸로 시작한다"]
    /// 이 구간의 <see cref="T0"/>/<see cref="T1"/>을 **잰 자(기준 폴리곤)**. null이면 계획 폴리곤(옛 방식).
    /// <para><b>왜 필요한가.</b> 바깥 단의 링은 계획 폴리곤에서 아주 멀다 — 22×33m 부지에 성토 47m면
    /// 링이 70m 밖이다(우표 한 장에 훌라후프). 그 링의 코너 바깥 조각을 계획 폴리곤에 투영하면
    /// <b>모든 점이 코너 한 점</b>으로 모인다(수학적으로 맞다 — 코너는 원래 점 하나가 부채꼴로 펴진 자리다).
    /// 그런데 우리는 그 답을 '둘레 몇 m'라는 <b>1차원 자</b>로 적었다. 부채꼴은 그 자에 폭이 없다 →
    /// 구간 폭 0 → <see cref="Flatten"/>이 버린다 → <b>변환이 통째로 사라진다</b>(0820 실측).</para>
    /// <para>%로 하한을 두는 건 자가 모자란 걸 값으로 때우는 것이라 다른 지형에서 또 터진다
    /// (이 저장소가 그렇게 고친 자만 일곱 개다). <b>자를 바꾼다</b> — 그 단의 링 자신으로 재면
    /// 34m 조각은 그 링 위에서 34m다. 무너질 수가 없다.</para></summary>
    public List<Point3>? Ref
    {
        get => _ref;
        // ★[검토 0824 사소-11] 3점 미만은 자로 쓸 수 없다 — null로 정규화한다.
        //   안 그러면 RefCum만 null이 되어 poly는 링·cum은 계획인 짝이 어긋난 조합이 생긴다.
        set { _ref = value != null && value.Count >= 3 ? value : null; _refCum = null; _grid = null; }
    }

    /// <summary>기준 폴리곤의 누적 길이 — 처음 쓸 때 한 번 만든다. 자가 없으면 null.</summary>
    public double[]? RefCum
        => _ref == null || _ref.Count < 3 ? null : (_refCum ??= GradingGeometry.CumLen2D(_ref));

    // ★★[검토 0824 치명 C-1] **자에 격자 색인을 붙인다.**
    //   자가 계획 폴리곤(4~30점)일 땐 최근접 선분을 전수로 훑어도 공짜였다. 그런데 0824부터 자가
    //   그 단의 링(수백~1400점)이라, 링 점마다 자를 통째로 훑으면 구간 수 × 링 점 × 자 점이 된다
    //   (실측: 구간 16개에 Build 1회 1.4초, 변환 한 번에 4회 돈다).
    //   격자에 선분을 담아 두고 **가까운 칸부터** 넓혀 가며 찾는다 — 바깥 칸이 지금 최선보다 멀어지면 멈춘다.
    //   근사가 아니라 **정확히 같은 답**이다(멈추는 조건이 거리 하한이다).
    private System.Collections.Generic.List<int>[]? _grid;
    private double _gx0, _gy0, _gc;
    private int _gnx, _gny;

    private void BuildGrid()
    {
        var r = _ref!;
        int n = r.Count;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var q in r)
        {
            if (q.X < minX) minX = q.X; if (q.X > maxX) maxX = q.X;
            if (q.Y < minY) minY = q.Y; if (q.Y > maxY) maxY = q.Y;
        }
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        // 칸 하나에 선분 몇 개가 들어가도록 — 칸 수는 정점 수에 비례.
        double target = Math.Max(4.0, Math.Sqrt(n));
        _gc = Math.Max(Math.Max(w, h) / target, 1e-6);
        _gnx = Math.Min(512, (int)(w / _gc) + 1);
        _gny = Math.Min(512, (int)(h / _gc) + 1);
        _gx0 = minX; _gy0 = minY;
        _grid = new System.Collections.Generic.List<int>[_gnx * _gny];
        for (int i = 0; i < n; i++)
        {
            var a = r[i]; var b = r[(i + 1) % n];
            int x0 = Cx(Math.Min(a.X, b.X)), x1 = Cx(Math.Max(a.X, b.X));
            int y0 = Cy(Math.Min(a.Y, b.Y)), y1 = Cy(Math.Max(a.Y, b.Y));
            for (int yy = y0; yy <= y1; yy++)
                for (int xx = x0; xx <= x1; xx++)
                {
                    int k = yy * _gnx + xx;
                    (_grid[k] ??= new System.Collections.Generic.List<int>()).Add(i);
                }
        }
    }

    private int Cx(double x) { int i = (int)((x - _gx0) / _gc); return i < 0 ? 0 : (i >= _gnx ? _gnx - 1 : i); }
    private int Cy(double y) { int i = (int)((y - _gy0) / _gc); return i < 0 ? 0 : (i >= _gny ? _gny - 1 : i); }

    /// <summary>자 위에서 (x,y)의 최근접 호길이 — 격자로 찾는다. <see cref="GradingGeometry.ParamAt"/>과 같은 답.</summary>
    private double ParamOnRef(double x, double y)
    {
        var r = _ref!; var cum = RefCum!;
        if (_grid == null) BuildGrid();
        int n = r.Count;
        int ci = Cx(x), cj = Cy(y);
        double best = double.MaxValue, bestT = 0;
        for (int rad = 0; rad < Math.Max(_gnx, _gny) + 1; rad++)
        {
            // 이 반경까지 덮은 사각형 밖의 최소 거리 — 지금 최선보다 멀면 더 볼 것이 없다.
            if (best < double.MaxValue)
            {
                double lo = _gx0 + (ci - rad) * _gc, hi = _gx0 + (ci + rad + 1) * _gc;
                double lo2 = _gy0 + (cj - rad) * _gc, hi2 = _gy0 + (cj + rad + 1) * _gc;
                double margin = Math.Min(Math.Min(x - lo, hi - x), Math.Min(y - lo2, hi2 - y));
                if (margin > 0 && best <= margin * margin) break;
            }
            bool any = false;
            for (int jj = cj - rad; jj <= cj + rad; jj++)
            {
                if (jj < 0 || jj >= _gny) continue;
                for (int ii = ci - rad; ii <= ci + rad; ii++)
                {
                    if (ii < 0 || ii >= _gnx) continue;
                    if (rad > 0 && Math.Abs(ii - ci) != rad && Math.Abs(jj - cj) != rad) continue;  // 테두리만
                    any = true;
                    var cell = _grid![jj * _gnx + ii];
                    if (cell == null) continue;
                    foreach (int i in cell)
                    {
                        var a = r[i]; var b = r[(i + 1) % n];
                        double ex = b.X - a.X, ey = b.Y - a.Y;
                        double l2 = ex * ex + ey * ey;
                        double u = l2 < 1e-18 ? 0 : ((x - a.X) * ex + (y - a.Y) * ey) / l2;
                        u = u < 0 ? 0 : (u > 1 ? 1 : u);
                        double px = a.X + ex * u, py = a.Y + ey * u;
                        double d2 = (x - px) * (x - px) + (y - py) * (y - py);
                        if (d2 < best) { best = d2; bestT = cum[i] + Math.Sqrt(l2) * u; }
                    }
                }
            }
            if (!any && best < double.MaxValue) break;
        }
        return best < double.MaxValue ? bestT : GradingGeometry.ParamAt(r, cum, x, y);
    }

    /// <summary>이 점이 이 구간 안인가 — <b>자기 자</b>가 있으면 그 위에서, 없으면 계획 폴리곤 위에서 잰다.</summary>
    public bool ContainsAt(double x, double y, IReadOnlyList<Point3> planB, double[] planCum)
        => RefCum != null ? Contains(ParamOnRef(x, y))
                          : Contains(GradingGeometry.ParamAt(planB, planCum, x, y));

    /// <summary>★★[JACK 0824] 이 점·이 단에 적용될 (구배, 소단폭) — 구간을 <b>만들어진 순서대로</b> 겹쳐 본다.
    /// <para><see cref="Flatten"/>이 조각마다 <i>미리</i> 하던 합성을 <b>점마다</b> 한다. 구간이 저마다
    /// 다른 자를 쓰게 됐으므로 한 축에 올려 미리 합칠 수가 없다 — 대신 나중 구간이 자기 시작단부터
    /// 앞 규칙을 대체하는 규칙(Flatten과 동일)을 그대로 쓴다.</para></summary>
    public static (double Slope, double BenchW) ResolveAt(
        IReadOnlyList<SlopeZone>? zones, double x, double y, int bench,
        double baseSlope, double baseW, IReadOnlyList<Point3> planB, double[] planCum)
    {
        if (zones == null || zones.Count == 0) return (baseSlope, baseW);
        List<(int F, double S, double W)>? acc = null;
        foreach (var z in zones)
        {
            if (z == null || z.Rules.Count == 0) continue;
            if (!z.ContainsAt(x, y, planB, planCum)) continue;
            acc ??= new List<(int, double, double)>();
            int zf = z.FirstBench;
            acc.RemoveAll(r => r.F >= zf);
            foreach (var r in z.Rules) acc.Add((r.FromBench, r.Slope, r.BenchW));
        }
        if (acc == null) return (baseSlope, baseW);
        acc.Sort((a, b) => a.F.CompareTo(b.F));
        double s = baseSlope, w = baseW;
        foreach (var r in acc)
        {
            if (bench < r.F) break;
            s = r.S;
            if (r.W >= 0) w = r.W;      // 소단 0(소단 없음)은 유효한 값
        }
        return (s, w);
    }

    /// <summary>★[JACK 0824] 이 구간을 대표하는 점 — 자 위에서 T0~T1의 한가운데.
    /// 이 구간이 이기는 자리가 어떤 규칙 아래 놓이는지 물어보는 데 쓴다.</summary>
    public Point3 RepPoint(IReadOnlyList<Point3> planB, double[] planCum)
    {
        var poly = Ref ?? planB;
        var pc = RefCum ?? planCum;
        double tot = pc[pc.Length - 1];
        double a = T0, b = T1 >= T0 ? T1 : T1 + tot;
        return GradingGeometry.PointAtParam(poly, pc, (a + b) * 0.5);
    }


    /// <summary>이 점·이 단이 '수직(옹벽)'인가 — <see cref="ResolveAt"/>의 구배가 최소구배 이하면 벽.</summary>
    public static bool IsWallAtPoint(IReadOnlyList<SlopeZone>? zones, double x, double y, int bench,
        double baseSlope, double gateSlope, IReadOnlyList<Point3> planB, double[] planCum)
        => ResolveAt(zones, x, y, bench, baseSlope, 1.0, planB, planCum).Slope <= gateSlope + 1e-9;

    /// <summary>이 단이 '수직(옹벽)'인가 — 적용 구배가 최소구배 이하면 벽으로 본다.</summary>
    public bool IsWallAt(int bench, double baseSlope, double minSlope)
        => SlopeAt(bench, baseSlope) <= minSlope + 1e-9;

    /// <summary>(x,y)의 호길이 param t가 이 구간 안인가 — 랩 대응.</summary>
    public bool Contains(double t) => T0 <= T1 ? (t >= T0 && t <= T1) : (t >= T0 || t <= T1);

    /// <summary>규칙 정렬(시작단 오름차순) + 같은 시작단 중복 제거(나중 것이 이김).</summary>
    public void Normalize()
    {
        Rules.Sort((a, b) => a.FromBench.CompareTo(b.FromBench));
        for (int i = Rules.Count - 1; i > 0; i--)
            if (Rules[i].FromBench == Rules[i - 1].FromBench) Rules.RemoveAt(i - 1);
    }

    /// <summary>
    /// [스샷 버그 0804 — JACK] 겹치는 구간들을 '서로 겹치지 않는 조각'으로 재배열한다.
    /// ※ 종전 병합은 겹치는 두 구간을 **합집합 하나**로 뭉개고 규칙을 합쳤다 — 그래서 옹벽 구간의 일부만
    ///   사면으로 되돌려도 새 규칙이 노란선 범위가 아니라 **옹벽 구간 전체**에 퍼졌다(그 단 전부가 사면으로).
    /// 방법: 모든 구간의 경계 호길이로 둘레를 기본 조각으로 자르고, 조각마다 '그 조각을 덮는 구간들'의
    ///   규칙을 목록 순서(=시간 순서)로 합성한다. 나중 구간의 규칙은 자기 시작단부터 바깥의 기존 규칙을
    ///   지우고 대체한다 — "클릭한 단부터 바깥 끝까지"라는 명령 의미 그대로.
    ///   (낮은 단을 먼저 찍고 높은 단을 나중에 찍으면 층층이로 쌓이고, 반대로 나중에 더 낮은 단을 찍으면
    ///    그 단부터 바깥 전체가 새 값으로 바뀐다.)
    /// 마지막에 규칙이 같은 이웃 조각을 도로 합쳐 조각 수를 최소화한다(랩 포함). zones는 제자리 교체.
    /// </summary>
    public static void Flatten(List<SlopeZone> zones, double total)
    {
        if (zones == null || zones.Count <= 1 || total <= 1e-6) return;
        const double eps = 1e-6;
        double Mod(double t) { t %= total; if (t < 0) t += total; return t; }
        double LenOf(SlopeZone z) { double l = z.T1 - z.T0; if (z.T0 > z.T1) l += total; return l; }

        // ★★[JACK 0824] **자가 따로인 구간은 합치지 않는다.** T0/T1을 서로 다른 폴리곤에서 쟀으므로
        //   한 축에 올릴 수가 없다 — 억지로 올리면 엉뚱한 자리가 된다.
        //   순서를 지킨 채 그대로 두고, 겹침은 ResolveAt이 점마다 푼다.
        // ★★[검토 0824 심각-4] **자 없는 구간이 자 있는 구간보다 뒤에 오면 합치지 않는다.**
        //   합친 결과를 앞에, 자 있는 것을 뒤에 붙이면 순서가 뒤집혀 '나중 것이 이긴다'가 깨진다 —
        //   나중에 찍은 옹벽이 지고 옛 사면이 이겨 "변환을 했는데 아무 일도 안 일어남"이 된다.
        //   자 있는 구간이 하나라도 앞서 있으면 순서를 건드리지 않는 쪽이 안전하다.
        int firstRef = -1;
        for (int i = 0; i < zones.Count; i++)
            if (zones[i] != null && zones[i].Rules.Count > 0 && zones[i].Ref != null) { firstRef = i; break; }
        if (firstRef >= 0)
            for (int i = firstRef + 1; i < zones.Count; i++)
                if (zones[i] != null && zones[i].Rules.Count > 0 && zones[i].Ref == null) return;   // 섞였다 — 손대지 않는다

        var withRef = new List<SlopeZone>();
        var src = new List<SlopeZone>();
        foreach (var z in zones)
        {
            if (z == null || z.Rules.Count == 0) continue;
            if (z.Ref != null) { withRef.Add(z); continue; }
            if (LenOf(z) > eps) src.Add(z);
        }
        if (src.Count <= 1)
        {
            zones.Clear(); zones.AddRange(src); zones.AddRange(withRef); return;
        }

        // ① 경계점 수집(0..total 정규화 → 정렬 → eps 병합, 0≈total 랩 중복 제거).
        //    전체 둘레 구간(길이 total)은 경계점 0 하나로 수렴 — 조각 [0,total) 하나가 된다.
        var cuts = new List<double>();
        foreach (var z in src)
        {
            if (LenOf(z) >= total - eps) { cuts.Add(0.0); continue; }
            cuts.Add(Mod(z.T0)); cuts.Add(Mod(z.T1));
        }
        cuts.Sort();
        var uq = new List<double>();
        foreach (var c in cuts)
            if (uq.Count == 0 || c - uq[uq.Count - 1] > eps) uq.Add(c);
        if (uq.Count >= 2 && uq[0] + total - uq[uq.Count - 1] <= eps) uq.RemoveAt(uq.Count - 1);
        if (uq.Count == 0) uq.Add(0.0);

        // ② 기본 조각마다 중점으로 덮는 구간을 찾아 규칙 합성(시간 순서, 시작단부터 바깥 대체).
        var pieces = new List<(double A, double B, List<(int FromBench, double Slope, double BenchW)> R)>();
        for (int i = 0; i < uq.Count; i++)
        {
            double a = uq[i];
            double b = i + 1 < uq.Count ? uq[i + 1] : uq[0] + total;
            if (b - a <= eps) continue;
            double mid = Mod((a + b) * 0.5);
            var rules = new List<(int, double, double)>();
            foreach (var z in src)
            {
                if (!z.Contains(mid)) continue;
                int zFirst = z.FirstBench;
                rules.RemoveAll(r => r.Item1 >= zFirst);
                rules.AddRange(z.Rules);
            }
            if (rules.Count == 0) continue;
            rules.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            pieces.Add((a, b, rules));
        }

        // ③ 규칙이 같은 이웃 조각 병합(비랩 이웃 → 마지막·첫 조각의 랩 이웃 순).
        static bool SameRules(List<(int, double, double)> x, List<(int, double, double)> y)
        {
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
                if (x[i].Item1 != y[i].Item1 || Math.Abs(x[i].Item2 - y[i].Item2) > 1e-12
                    || Math.Abs(x[i].Item3 - y[i].Item3) > 1e-12) return false;
            return true;
        }
        for (int i = pieces.Count - 1; i > 0; i--)
            if (pieces[i].A - pieces[i - 1].B <= eps && SameRules(pieces[i].R, pieces[i - 1].R))
            { pieces[i - 1] = (pieces[i - 1].A, pieces[i].B, pieces[i - 1].R); pieces.RemoveAt(i); }
        if (pieces.Count >= 2 && SameRules(pieces[0].R, pieces[pieces.Count - 1].R)
            && Math.Abs(Mod(pieces[pieces.Count - 1].B) - pieces[0].A) <= eps)
        {
            var last = pieces[pieces.Count - 1];
            pieces[pieces.Count - 1] = (last.A, last.B + (pieces[0].B - pieces[0].A), last.R);
            pieces.RemoveAt(0);
        }

        zones.Clear();
        foreach (var (a, b, r) in pieces)
        {
            var z = b - a >= total - eps
                ? new SlopeZone { T0 = 0.0, T1 = total }
                : new SlopeZone { T0 = Mod(a), T1 = Mod(b) };
            z.Rules.AddRange(r);
            zones.Add(z);
        }
        // ★[JACK 0824] 자가 따로인 구간은 뒤에 그대로 붙인다 — '나중 것이 이긴다'는 순서를 지킨다.
        zones.AddRange(withRef);
    }

    /// <summary>★★[JACK 0824] <b>죽은 구간을 걷어낸다.</b>
    /// <para>구간은 변환할 때마다 하나씩 쌓이는데, 뒤에 같은 자리·같은(또는 더 낮은) 시작단으로 규칙이
    /// 하나 더 얹히면 앞 구간은 <see cref="ResolveAt"/>에서 통째로 지워져 <b>아무 일도 안 한다</b>
    /// (실측 0824: 구간 4개 중 2개가 완전 중복, 1개는 뒤 규칙에 덮여 죽어 있었다).
    /// 남겨 두면 번들만 커지고 로그를 읽을 수 없다 — 결과는 그대로 두고 <b>죽은 것만</b> 뺀다.</para>
    /// <para>종전엔 <see cref="Flatten"/>이 겹침을 갈라내며 이 일을 겸했는데, 자가 구간마다 달라진 뒤로는
    /// 그쪽을 못 타므로 여기서 따로 한다.</para></summary>
    /// <summary>★[검토 0824] 두 구간이 <b>실제로 같은 자리</b>를 덮는가 — 자가 달라도 물을 수 있다.
    /// T 숫자를 그대로 비교하면 자가 다를 때 서로 다른 축의 눈금을 견주는 셈이라 뜻이 없다.</summary>
    public static bool RegionsOverlap(SlopeZone a, SlopeZone b,
                                      IReadOnlyList<Point3> planB, double[] planCum)
    {
        if (a == null || b == null) return false;
        var poly = a.Ref ?? planB; var pc = a.RefCum ?? planCum;
        if (pc == null || pc.Length < 2) return false;
        double tot = pc[pc.Length - 1];
        double t0 = a.T0, t1 = a.T1 >= a.T0 ? a.T1 : a.T1 + tot;
        for (int k = 0; k <= 24; k++)
        {
            var q = GradingGeometry.PointAtParam(poly, pc, t0 + (t1 - t0) * k / 24.0);
            if (b.ContainsAt(q.X, q.Y, planB, planCum)) return true;
        }
        return false;
    }

    /// <param name="planRef">계획 폴리곤 — 자가 없는 구간을 재는 기준(없으면 좌표 비교로 물러난다).</param>
    public static void Compact(List<SlopeZone> zones,
                               IReadOnlyList<Point3>? planRef = null, double[]? planCumRef = null)
    {
        if (zones == null || zones.Count < 2) return;
        const double eps = 1e-6;
        // ★★[검토 0824 중간-7] 판정을 **기하로** 한다.
        //   종전엔 '같은 자 + 같은 T0/T1'을 요구했는데, 자는 변환할 때마다 실제로 달라진다
        //   (옹벽변환 한 번이면 그 단 링 둘레가 178m→120m). 그래서 거의 발동하지 않았다.
        //   자가 달라도 **같은 자리를 덮으면** 같은 구간이다 — 표본을 떠서 서로를 덮는지 본다.
        //   (판정이 틀려도 '남는' 쪽으로만 틀리게 표본 전부를 요구한다.)
        const int NS = 24;
        static bool CoversAll(SlopeZone a, SlopeZone b, IReadOnlyList<Point3> planB, double[] planCum)
        {
            var poly = a.Ref ?? planB; var pc = a.RefCum ?? planCum;
            if (pc == null || pc.Length < 2) return false;
            double tot = pc[pc.Length - 1];
            double t0 = a.T0, t1 = a.T1 >= a.T0 ? a.T1 : a.T1 + tot;
            for (int k = 0; k <= NS; k++)
            {
                var q = GradingGeometry.PointAtParam(poly, pc, t0 + (t1 - t0) * k / NS);
                if (!b.ContainsAt(q.X, q.Y, planB, planCum)) return false;
            }
            return true;
        }
        var dead = new bool[zones.Count];
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i] == null || zones[i].Rules.Count == 0) { dead[i] = true; continue; }
            for (int j = i + 1; j < zones.Count; j++)
            {
                if (zones[j] == null || zones[j].Rules.Count == 0) continue;
                // 뒤 구간이 더 낮은(또는 같은) 단부터 시작하고, 앞 구간을 **전부 덮으면**
                //   앞 구간의 규칙은 ResolveAt에서 통째로 지워져 아무 일도 안 한다.
                if (zones[j].FirstBench > zones[i].FirstBench) continue;
                if (planRef == null || planCumRef == null)
                {
                    if (Math.Abs(zones[i].T0 - zones[j].T0) > eps || Math.Abs(zones[i].T1 - zones[j].T1) > eps) continue;
                    if (!ReferenceEquals(zones[i].Ref, zones[j].Ref)) continue;
                }
                else if (!CoversAll(zones[i], zones[j], planRef, planCumRef)) continue;
                dead[i] = true; break;
            }
        }
        for (int i = zones.Count - 1; i >= 0; i--) if (dead[i]) zones.RemoveAt(i);
    }

    /// <summary>옛 표현(시작단부터 끝단까지 수직)에서 만들기 — 번들 v6 이하 하위호환·옹벽 변환용.
    /// toBench가 끝이 아니면 그 다음 단부터 전역 구배로 되돌리는 규칙을 함께 넣는다(옛 ToBench와 동일 의미).</summary>
    public static SlopeZone Wall(double t0, double t1, int fromBench, int toBench, double minSlope, double baseSlope)
    {
        var z = new SlopeZone { T0 = t0, T1 = t1 };
        z.Rules.Add((Math.Max(fromBench, 0), minSlope, -1));                // 소단폭은 전역값 따름
        if (toBench < int.MaxValue - 1) z.Rules.Add((toBench + 1, baseSlope, -1));
        z.Normalize();
        return z;
    }
}

/// <summary>정지(절성토) 파라미터. 모든 길이 단위는 m.</summary>
public sealed class GradingParams
{
    // ── [절성토 분리 0803 — JACK] 단높이·소단폭을 구배(CutSlope/FillSlope)와 같은 방식으로 방향별 분리. ──
    //   대소단(TerraceInterval/TerraceWidth)은 공용 유지(산지전용허가법 15m 규정 — JACK 확정).

    /// <summary>절토 한 단(段)의 수직 높이 (기본 5m).</summary>
    public double CutBenchHeight { get; init; } = 5.0;

    /// <summary>성토 한 단(段)의 수직 높이 (기본 5m).</summary>
    public double FillBenchHeight { get; init; } = 5.0;

    /// <summary>절토 소단(小段) 폭 — 단 사이 평탄부 (기본 1m).</summary>
    public double CutBenchWidth { get; init; } = 1.0;

    /// <summary>성토 소단(小段) 폭 — 단 사이 평탄부 (기본 1m).</summary>
    public double FillBenchWidth { get; init; } = 1.0;

    /// <summary>방향별 단높이 — up=true 절토 / false 성토.</summary>
    public double BenchHeightOf(bool up) => up ? CutBenchHeight : FillBenchHeight;

    /// <summary>★★★[JACK 0820 '해당 선택 지점부터 단높이를 바꿔서 할 순 없나'] <b>단높이 변경 규칙</b> —
    /// (그 단부터, 새 단높이). 방향별(절토/성토)로 따로 두고, <b>전 둘레에 똑같이</b> 적용된다.
    /// <para><b>왜 구간(SlopeZone)이 아니라 여기에 두는가</b> — v16.9가 "단높이는 구간별 불가"라고 적은 이유가
    /// 그대로 살아 있기 때문이다: 링은 같은 표고의 등고선이라 <b>링 하나에 표고가 하나</b>인데,
    /// 둘레의 일부만 단높이가 다르면 같은 링에서 구간 안/밖의 표고가 달라져야 해서 표현이 안 된다.
    /// </para>
    /// 그런데 <b>층(단) 전체</b>를 바꾸는 것은 다르다 — 그 단부터 위쪽 링들의 표고가 통째로 옮겨갈 뿐,
    /// 링 하나의 표고는 여전히 하나다. 그래서 <b>구간에 못 두는 것</b>이고 <b>여기에 두면 되는 것</b>이다.
    /// <para>이 목록을 구간별로 두면 v16.9의 그 문제로 정확히 되돌아간다 — 자료구조로 막아 둔다.</para></summary>
    public List<(int FromBench, double H)> CutBenchSteps { get; init; } = new();

    /// <summary>성토 단높이 변경 규칙 — <see cref="CutBenchSteps"/> 참조.</summary>
    public List<(int FromBench, double H)> FillBenchSteps { get; init; } = new();

    /// <summary>방향별 단높이 변경 규칙.</summary>
    public IReadOnlyList<(int FromBench, double H)> BenchStepsOf(bool up) => up ? CutBenchSteps : FillBenchSteps;

    /// <summary>그 단(0부터)의 단높이 — 규칙이 없으면 전역값. 규칙은 시작단 오름차순 전제(<see cref="NormalizeBenchSteps"/>).
    /// <para>여러 번 실행하면 규칙이 쌓여 '아래는 높은 단 · 위는 낮은 단'이 된다 — 구배·소단폭과 같은 규칙이다.</para></summary>
    public double BenchHeightAt(bool up, int bench)
    {
        double h = BenchHeightOf(up);
        foreach (var r in BenchStepsOf(up))
        {
            if (bench < r.FromBench) break;
            if (r.H > 1e-6) h = r.H;
        }
        return h;
    }

    /// <summary>이 방향에서 나올 수 있는 <b>가장 작은</b> 단높이 — 단수 예산(무한루프 백스톱)에 쓴다.
    /// 작은 단이 섞이면 같은 표고차를 오르는 데 단이 더 많이 필요하므로, 예산은 작은 쪽으로 잡아야 안 끊긴다.</summary>
    public double SmallestBenchHeightOf(bool up)
    {
        double h = BenchHeightOf(up);
        foreach (var r in BenchStepsOf(up)) if (r.H > 1e-6 && r.H < h) h = r.H;
        return h;
    }

    /// <summary>단높이 규칙 정렬(시작단 오름차순) + 같은 시작단 중복 제거(나중 것이 이김) — SlopeZone.Normalize와 같은 규칙.</summary>
    public void NormalizeBenchSteps()
    {
        foreach (var (list, global0) in new[]
                 { (CutBenchSteps, CutBenchHeight), (FillBenchSteps, FillBenchHeight) })
        {
            list.Sort((a, b) => a.FromBench.CompareTo(b.FromBench));
            for (int i = list.Count - 1; i > 0; i--)
                if (list[i].FromBench == list[i - 1].FromBench) list.RemoveAt(i - 1);
            // ★[JACK 0824] **앞과 같은 값인 규칙은 뺀다** — 변환할 때마다 하나씩 쌓이는데
            //   값이 같으면 아무 일도 안 한다(실측 로그: `1단~1m 15단~1m 16단~1m` — 뒤 둘은 무의미).
            //   결과는 그대로 두고 죽은 것만 뺀다 — 안 빼면 번들이 커지고 로그를 읽을 수 없다.
            double cur = global0;
            for (int i = 0; i < list.Count; )
            {
                if (Math.Abs(list[i].H - cur) < 1e-9) { list.RemoveAt(i); continue; }
                cur = list[i].H; i++;
            }
        }
    }

    /// <summary>방향별 소단폭 — up=true 절토 / false 성토.</summary>
    public double BenchWidthOf(bool up) => up ? CutBenchWidth : FillBenchWidth;

    /// <summary>
    /// [절성토 분리] 단수 산정 기준 = 둘 중 '작은' 단높이. 작은 쪽이 같은 표고차를 오르는 데 더 많은 단을
    /// 쓰므로, 이 기준으로 단수를 잡아야 양쪽 다 원지반(데이라잇)에 닿는다. 큰 쪽 기준으로 잡으면 작은 쪽 사면이
    /// 도중에 끊겨 정지면이 잘리거나 구멍이 난다. (절토=성토면 종전과 완전히 동일 — 회귀 없음.)
    /// </summary>
    public double SmallerBenchHeight => Math.Min(CutBenchHeight, FillBenchHeight);

    /// <summary>[절성토 분리] 여유 마진용 — 둘 중 '큰' 단높이.</summary>
    public double LargerBenchHeight => Math.Max(CutBenchHeight, FillBenchHeight);

    /// <summary>절토 구배 n. 표기 1:n = 수직 1 : 수평 n (예 1:1.0).</summary>
    public double CutSlope { get; init; } = 1.0;

    /// <summary>성토 구배 n. 표기 1:n = 수직 1 : 수평 n (예 1:1.5).</summary>
    public double FillSlope { get; init; } = 1.5;

    /// <summary>격자 해상도 (기본 1m). 작을수록 정밀·느림.</summary>
    public double CellSize { get; init; } = 1.0;

    /// <summary>안전 최대 단수 — daylight를 못 만나도 이 단수에서 멈춰 무한 확장 방지.
    /// [0803] <see cref="MaxRise"/>가 지정되면 높이 예산은 그쪽이 맡고, 이 값은 옛 번들 폴백에만 쓰인다.</summary>
    public int MaxBenches { get; init; } = 50;

    /// <summary>
    /// [절성토 분리 0803] 사면 링을 만들 '수직 예산' (m). 0=미지정 → 종전대로 MaxBenches×단높이로 계산(옛 번들).
    /// ※ 단 '개수' 상한(MaxBenches)에 단높이를 곱해 높이 예산을 만들면, 단높이가 작은 쪽이 개수 상한에 걸리는
    ///   순간 예산이 함께 주저앉아 사면이 원지반에 닿기 전에 끊긴다(표고차 &gt; 48×작은단높이에서 발동).
    ///   그래서 '개수 상한'과 '높이 예산'을 분리한다 — 높이 예산은 단높이와 무관한 실제 표고차에서 나와야 한다.
    /// CreateGradingCommand.BuildParams가 (표고차 + 여유)로 채운다.
    /// </summary>
    public double MaxRise { get; init; } = 0;

    /// <summary>★★★[JACK 0826] <b>절토 쪽 수직 예산</b>(m). 0이면 <see cref="MaxRise"/>를 쓴다.
    ///
    /// <para><b>왜 나누나.</b> 종전엔 예산 하나를 양쪽에 같이 썼다. 그런데 필요한 높이는 방향마다 다르다:
    /// 원지반 65~117m에 계획고 100m이면 <b>깎는 쪽은 17m</b>(117−100)면 땅에 닿고
    /// <b>쌓는 쪽은 35m</b>(100−65)가 필요하다. 그런데 큰 쪽(35m)에 여유를 더한 45m를
    /// 깎는 쪽에도 주니 계단이 <c>100+45 = 145m</c>까지 <b>허공으로</b> 올라갔다 —
    /// 땅이 117m까지인데 28m가 헛단이다.</para>
    ///
    /// <para><b>그 헛단이 실제 사고를 냈다.</b> 횡단 수량이 그 허공 계단을 계획면으로 읽어
    /// 성토 2000㎡가 잡혔다(JACK: <i>"원지반만 있는 측점인데 왜 정지순수가 나오는지 모르겠다"</i>).
    /// 화면에는 안 보이는데 데이터에는 있었던 것이다.</para>
    ///
    /// <para>★<b>번들 저장 형식은 안 바뀐다.</b> 이 값은 <b>파생값</b>이라 담지 않는다 —
    /// 옛 번들을 읽으면 0이고, 그러면 <see cref="MaxRise"/>로 물러나 <b>종전과 똑같이</b> 돈다.
    /// (0807에 이 수정을 미룬 이유가 저장형식 v9→v10 부담이었는데, 그 부담이 없어졌다.)</para></summary>
    public double MaxRiseCut { get; init; } = 0;

    /// <summary>★[JACK 0826] <b>성토 쪽 수직 예산</b>(m). 0이면 <see cref="MaxRise"/>를 쓴다.
    /// 설명은 <see cref="MaxRiseCut"/>에.</summary>
    public double MaxRiseFill { get; init; } = 0;

    /// <summary>이 방향에 실제로 쓸 예산 — 방향별 값이 있으면 그것, 없으면 공용값.</summary>
    public double RiseFor(bool up)
    {
        double v = up ? MaxRiseCut : MaxRiseFill;
        return v > 1e-9 ? v : MaxRise;
    }

    /// <summary>경계 둘레 샘플 간격 (m) — 정점 밀도. 작을수록 곡선 추종 좋고 폴리라인 많음.</summary>
    public double VertexSpacing { get; init; } = 2.0;

    /// <summary>
    /// 비탈 최소 구배 n (1:n). 구배 0(수직 옹벽) 입력 시 이 비율로 살짝 눕혀 TIN 붕괴를 막는다.
    ///
    /// ★★[JACK 0825] <b>0.05 → 0.01.</b> JACK: <i>"수직 지표면치고 0.05는 너무 과해.
    /// 단수가 많아지면 그만큼 부지 면적이 커지면서 나중엔 무시하지 못할 정도가 되고, 토공량에도 차이가 난다."</i>
    /// 맞다 — 실측: 100×100 부지 3단 옹벽에서 부지가 <b>8.16% 부풀어 있었다</b>(0.01이면 1.61%).
    /// <b>655㎡ · 토공 약 4,900㎥</b> 차이다. 단수가 많을수록 밀림이 누적되어 더 커진다.
    ///
    /// <para>종전 주석의 근거("0.05 미만은 Civil3D TIN 오류")는 <b>실측이 아니었다</b>("사례가 있어 미연 방지").
    /// 조사 결과 TIN에서 위험한 것은 간격이 <b>정확히 0</b>이거나 브레이크라인이 <b>교차</b>할 때뿐이고,
    /// Civil 3D 자체 Wall 브레이크라인은 <b>0.001ft(≈0.3mm)</b> 오프셋을 쓴다.
    /// 1:0.01·단높이 5m면 간격 50mm로 그 <b>167배</b>다.</para>
    ///
    /// <para>진짜 바닥은 <see cref="MinFaceRun"/>(5mm)이 이미 지킨다 — 단높이가 아무리 낮아도
    /// 링 간격이 5mm 밑으로 안 내려간다(하니스 S62 실측).</para>
    ///
    /// <para><b>판정 문턱은 <see cref="WallGateSlope"/>로 따로 있다.</b> 이 값을 낮춰도 그건 안 따라 내려간다.</para>
    /// </summary>
    public double MinSlope { get; init; } = 0.01;

    /// <summary>★★[JACK 0825] <b>옹벽이냐 사면이냐를 가르는 문턱</b> — 이 값 <b>이하</b>면 옹벽.
    ///
    /// <para><see cref="MinSlope"/>와 <b>떼어 놓은</b> 이유. 종전엔 한 값이 세 역할을 겸했다:
    /// ①구배 0 입력을 끌어올리는 <b>하한</b> ②옹벽/사면 <b>판정 문턱</b> ③옹벽의 <b>실제 구배 값</b>.
    /// 그래서 하한을 낮추면 판정 문턱까지 같이 내려가 <b>이미 만들어 둔 1:0.05 옹벽이 사면으로 재분류</b>됐다 —
    /// 모양은 수직인데 소프트웨어만 사면이라 믿는 상태가 되어 옹벽선·3D 매스·종단 막대가 전부 사라진다
    /// (하니스 S64에서 옹벽선 <b>16줄 → 0줄</b>로 실증).</para>
    ///
    /// <para>그래서 <b>문턱은 0.05에 동결</b>하고 하한만 내린다. 판정은 "간격이 한도 <b>이하</b>면 벽"이라
    /// 문턱을 넓게 두면 <b>더 얇은 벽도 함께 통과</b>한다 — 진짜 사면(1:1.5)은 간격이 두 자릿수 크므로
    /// 오분류될 여지가 없다.</para>
    ///
    /// <para><b>이 값은 낮추지 말 것.</b> 낮추는 순간 옛 도면의 옹벽이 사면이 된다.</para></summary>
    public double WallGateSlope { get; init; } = 0.05;

    /// <summary>비탈 최소 수평폭 절대 바닥 (m) — 단높이가 매우 작을 때만 작동하는 안전장치.</summary>
    public double MinFaceRun { get; init; } = 0.005;

    /// <summary>
    /// 사면형상 — 볼록(튀어나온) 모서리 처리. true=직각(마이터, 기본), false=라운드(원호).
    /// 직각 모드는 예각에서 <see cref="MiterLimit"/> 비율을 넘으면 자동으로 라운드로 폴백한다.
    /// [0805] 기본값은 GradingSettings.MiterConvex(직각)와 반드시 일치해야 한다 — 어긋나 있으면
    /// 명시 대입을 빠뜨린 코드가 '설정은 직각인데 결과는 라운드'를 조용히 만든다(직각/라운드는
    /// 옹벽 벽면 분할이 완전히 달라져 결과 차이가 극단적 — v17.6 옹벽 6장↔163장).
    /// </summary>
    public bool MiterConvex { get; init; } = true;

    /// <summary>직각(마이터) 모서리 최대 연장 비율 — 모서리 길이 ÷ 단거리. 이보다 뾰족하면 라운드로 폴백.</summary>
    public double MiterLimit { get; init; } = 2.0;

    /// <summary>
    /// 계단식 산지 적용 (산지전용허가법). true면 사면 수직 누적이 <see cref="TerraceInterval"/>(기본 15m)에
    /// 닿는 단마다 일반 소단 대신 폭 <see cref="TerraceWidth"/>(기본 15m)의 대소단(큰 평탄)을 넣고 누적을 리셋한다.
    /// 단높이로 간격이 딱 안 떨어지면 마지막 사면을 '간격−누적'만큼 자투리로 올려 정확히 간격에 맞춘다.
    /// </summary>
    public bool MountainTerrace { get; init; } = false;

    /// <summary>대소단 수직 간격 (m, 기본 15) — 누적 사면높이가 이 값에 닿으면 대소단 삽입.</summary>
    public double TerraceInterval { get; init; } = 15.0;

    /// <summary>대소단(큰 평탄) 폭 (m, 기본 15).</summary>
    public double TerraceWidth { get; init; } = 15.0;

    public void Validate()
    {
        if (CutBenchHeight <= 0) throw new ArgumentException("절토 단높이(CutBenchHeight)는 0보다 커야 합니다.");
        if (FillBenchHeight <= 0) throw new ArgumentException("성토 단높이(FillBenchHeight)는 0보다 커야 합니다.");
        if (CutBenchWidth < 0) throw new ArgumentException("절토 소단폭(CutBenchWidth)은 0 이상이어야 합니다.");
        if (FillBenchWidth < 0) throw new ArgumentException("성토 소단폭(FillBenchWidth)은 0 이상이어야 합니다.");
        if (CutSlope < 0 || FillSlope < 0) throw new ArgumentException("구배는 0 이상이어야 합니다.");
        if (CellSize <= 0) throw new ArgumentException("격자(CellSize)는 0보다 커야 합니다.");
        if (MaxBenches <= 0) throw new ArgumentException("최대 단수(MaxBenches)는 1 이상이어야 합니다.");
        if (VertexSpacing <= 0) throw new ArgumentException("정점 간격(VertexSpacing)은 0보다 커야 합니다.");
        if (MinSlope < 0) throw new ArgumentException("최소 구배(MinSlope)는 0 이상이어야 합니다.");
        if (MinFaceRun <= 0) throw new ArgumentException("최소 비탈폭(MinFaceRun)은 0보다 커야 합니다.");
        if (MountainTerrace)
        {
            if (TerraceInterval <= 0) throw new ArgumentException("대소단 수직 간격(TerraceInterval)은 0보다 커야 합니다.");
            if (TerraceWidth < 0) throw new ArgumentException("대소단 폭(TerraceWidth)은 0 이상이어야 합니다.");
        }
    }
}
