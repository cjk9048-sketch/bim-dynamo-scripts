using System.Collections.Generic;

namespace DH.Grading.Core;

/// <summary>
/// [옹벽 재설계 0805 — 옹벽선_재설계.md] 옹벽선 한 줄 = 벽 한 폭.
/// <para>
/// Toe(아랫선)·Crest(윗선)는 같은 링 쌍에서 잘라낸 3D 폴리선이고, **정규화 호길이로 1:1 대응**한다
/// (1:0.05·단높이 5m면 두 선의 수평 차이가 0.25m라 정점 짝짓기 없이도 오차가 없다).
/// </para>
/// 이 구조체가 '정본'이다 — 정지면을 만드는 순간 확정해 번들에 저장하고, 내보내기는 읽기만 한다.
/// 종전처럼 내보내기가 링을 다시 계산하지 않으므로 지표면과 어긋날 여지가 없다.
/// </summary>
public sealed class WallRun
{
    /// <summary>true=절토 / false=성토.</summary>
    public bool Up { get; init; }

    /// <summary>단 번호(0 = 1단).</summary>
    public int Bench { get; init; }

    /// <summary>아랫선(토우) — 벽 밑동.</summary>
    public List<Point3> Toe { get; init; } = new();

    /// <summary>윗선(크레스트) — 벽 꼭대기.</summary>
    public List<Point3> Crest { get; init; } = new();

    /// <summary>이 벽의 대표 높이(m) — 판넬 한 변을 정하는 기준. 0이면 Toe/Crest 평균 Z 차이로 구한다.</summary>
    public double Height { get; init; }

    /// <summary>[하니스] 두 옹벽선이 같은가 — 번들 왕복(저장→복원) 검증용. 좌표 허용오차 tol.</summary>
    public bool SameAs(WallRun? o, double tol = 1e-9)
    {
        if (o == null) return false;
        if (Up != o.Up || Bench != o.Bench || System.Math.Abs(Height - o.Height) > tol) return false;
        static bool Eq(List<Point3> a, List<Point3> b, double t)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (System.Math.Abs(a[i].X - b[i].X) > t || System.Math.Abs(a[i].Y - b[i].Y) > t
                    || System.Math.Abs(a[i].Z - b[i].Z) > t) return false;
            return true;
        }
        return Eq(Toe, o.Toe, tol) && Eq(Crest, o.Crest, tol);
    }
}

/// <summary>
/// [옹벽 재설계 0805] 옹벽선(띠)을 판넬로 잘라내는 순수 기하 계산 — Civil3D에 의존하지 않는다.
/// <para>
/// 종전 방식(벽면마다 쪼개고 이웃 평면으로 서로 잘라내기)은 v17.6·v17.7·v17.8·v18.2의 버그가
/// 전부 한 덩어리에서 나올 만큼 취약했다. 여기서는 <b>판넬이 모서리를 가로지르지 않게</b> 띠를
/// 모서리에서 먼저 끊으므로, 이웃 평면 절단(miter·ClipHalf·keep 부호)이 통째로 필요 없다.
/// </para>
/// </summary>
public static class WallBand
{
    /// <summary>직전 <see cref="Slice"/>의 진단 문자열 — 조용히 버려지는 자리마다 사유별 계수기.</summary>
    public static string LastDiag { get; private set; } = "";

    /// <summary>판넬 한 변 상한(m) — 설계 규칙 '단높이 5m ÷ 3'.</summary>
    public const double MaxSide = 5.0 / 3.0;

    /// <summary>[0806] 짧은 벽면을 이웃에 합칠 때 <b>가로질러도 되는 모서리</b>의 한계 꺾임(도).
    /// 이보다 많이 꺾인 모서리는 판넬이 가로지르면 평면이 깨지므로 합치지 않는다 —
    /// 진짜 코너 사이에 낀 짧은 벽면은 좁은 판넬이 정답이다.</summary>
    public const double MergeMaxDeg = 45.0;

    /// <summary>[0806] 이보다 짧은 벽면은 이웃에 합친다 — 한 변의 절반.
    /// 이 값이면 어떤 벽면도 <c>길이/올림(길이÷한변)</c> ≥ 한변÷2 이 되어 <b>판넬 폭이 설계폭의 절반 밑으로 안 내려간다</b>.
    /// (현장 v19.29 실측: 6cm 벽면 → 6cm 판넬. 합치기 전에는 하한이 없었다.)</summary>
    public static double MinFaceLenFor(double side) => side * 0.5;

    /// <summary>[하니스 전용] 짧은 벽면 합치기를 끈다 — 자체검증(끄면 6cm 판넬이 되살아난다)에 쓴다.</summary>
    public static bool DisableShortFaceMergeForTest;

    /// <summary>[하니스 전용] 사다리꼴(아랫변=토우·윗변=크레스트)을 끄고 직사각형으로 되돌린다.
    /// 다른 방어(코너 분할·현 제한)의 자체검증은 이걸 같이 꺼야 성립한다 — 사다리꼴이 그 결함까지
    /// 덮어 주면 '방어를 껐는데도 멀쩡한' 결과가 나와 검사가 무력해진다.</summary>
    public static bool DisableTrapezoidForTest;



    /// <summary>[0806] 벽면 끝에 남는 자투리 판넬의 하한(m) — 이보다 짧으면 앞 판넬에 붙인다.
    /// 수 cm짜리 자투리는 줄눈 인셋에 통째로 죽어 그 자리가 구멍이 된다(v17.8 '줄눈 1690'의 정체).</summary>
    public const double MinTailLen = 0.40;

    /// <summary>
    /// 데이라잇에 잘리고 남은 조각의 하한 — 이보다 작으면 만들지 않는다(솔리드 압출이 퇴화하는 것만 막는 값).
    /// <para>
    /// [0805 이력] 처음엔 0.05㎡·0.10m로 크게 잡았다. 그때는 상한을 열 양 끝 2점으로만 재서
    /// 조각이 지반 위로 삐져나왔고(0.123m), 그런 조각은 버리는 편이 나았기 때문이다.
    /// 지금은 실루엣을 0.15m 간격으로 따라가 <b>지반 위 이탈이 0.000m</b>이므로, 작은 조각도
    /// 있는 그대로가 옳다 — 오히려 버리면 그 자리에 <b>구멍</b>이 남는다(JACK '판넬이 잘려 보임').
    /// 그래서 하한은 '솔리드로 만들 수 있는 최소'까지만 낮춘다.
    /// </para></summary>
    public const double SliverArea = 0.01;   // ㎡ (100㎠)
    public const double SliverEdge = 0.03;   // m — 한 변이 이보다 짧으면 압출이 퇴화한다

    /// <summary>[설계 규칙 — JACK 0721] 한 단을 몇 행으로 나눌지: 단높이 ≤1m→1 / ≤3m→2 / 그 이상→3.
    /// <see cref="SideFor"/>와 짝이다 — 한 변 = 단높이 ÷ 이 값.</summary>
    public static int RowsFor(double height)
    {
        double h = System.Math.Abs(height);
        return h <= 1.0 + 1e-9 ? 1 : h <= 3.0 + 1e-9 ? 2 : 3;
    }

    /// <summary>[치명 0805] 한 단의 실제 행 수 — 설계 규칙(<see cref="RowsFor"/>)에 <b>여유 0.5m</b>를 둔
    /// 상한 검사를 더한 값. 링 평균 Z는 완화 정점 때문에 설계 단높이보다 수 mm~수 cm 크게 나오므로
    /// (v18.0 실측 5.0002m) 여유 없이 걸면 3행이 4행이 되고 행 높이가 1.67→1.25m로 낮아진다.</summary>
    public static int RowsForBench(double height)
    {
        double h = System.Math.Abs(height);
        const double heightSlack = 0.5;
        return System.Math.Max(RowsFor(h), (int)System.Math.Ceiling((h - heightSlack) / MaxSide - 1e-9));
    }

    /// <summary>판넬 한 변 — <b>단높이 ÷ 행 수</b>. 정지옵션에서 단높이를 바꾸면 폭도 따라 바뀐다.
    /// <para>
    /// [0806] 종전엔 `≤1m→높이 / ≤3m→½ / 그 이상→⅓`을 <see cref="MaxSide"/>로 자르기만 했는데,
    /// 그러면 단높이 5m를 넘을 때 <b>행 높이와 폭이 어긋난다</b>(6m: 4행이라 행 높이 1.50m인데 폭은 1.67m).
    /// 행 수로 직접 나누면 어떤 단높이에서도 판넬이 <b>정사각</b>이고 상한도 저절로 지켜진다.
    /// </para></summary>
    public static double SideFor(double height)
    {
        double h = System.Math.Abs(height);
        if (h <= 1e-3) return 1e-3;
        return System.Math.Min(h / RowsForBench(h), MaxSide);
    }

    /// <summary>판넬 한 장 — 월드 3D 사각(또는 데이라잇에 잘린 다각) + 로컬 프레임.
    /// 프레임은 <b>항상 직교정규</b>다: U는 띠 진행의 <b>수평</b> 방향이고 V는 사면 상방이라
    /// U·V = 0이 구조적으로 보장된다(v18.2 'eCannotScaleNonUniformly'의 원천이 사라진다).</summary>
    public readonly record struct Tile(
        IReadOnlyList<Point3> Poly, bool IsFull,
        Point3 Origin,
        (double x, double y, double z) UAxis,
        (double x, double y, double z) VAxis,
        (double x, double y, double z) WAxis,
        IReadOnlyList<(double u, double v)> Local,
        double PocketU, double PocketV,
        int Bench, bool Up,
        /// <summary>[0806] 이 판넬이 그 열의 몇 번째 행인가(아래가 0) — 옆 판넬과 짝지을 때 **표고가 아니라 행 번호로**
        /// 맞추려고 둔다. 데이라잇에 잘린 맨 윗행은 표고가 이웃과 1m 가까이 어긋나서, 표고로 짝지으면
        /// 붙어 있는 판넬끼리도 '떨어졌다'고 잘못 세어진다(v19.34 '틈 10곳'이 전부 그 허위였다).</summary>
        int Row = 0);

    /// <summary>
    /// 새 <see cref="Tile"/>을 기존 DWG 작성기(WallPanelDwg)가 받는 <see cref="WallPanels.Panel"/>로 변환한다.
    /// <para>
    /// DWG 작성기(솔리드·홈·도넛·앵커·정착판·자연석 무늬)는 이미 현장 검증을 거쳤으므로 **그대로 재사용**한다 —
    /// 새로 쓰면 그 검증을 처음부터 다시 해야 한다. 바뀌는 것은 '판넬을 어디에 어떻게 놓을지'뿐이다.
    /// </para>
    /// 앵커 방향: 벽 뒤(흙 속) = −W. 거기서 <paramref name="anchorDeg"/>만큼 아래로 기울인다.
    /// (옛 코드의 '절토=−n / 성토=+n'과 같은 방향 — W가 이미 노출면을 향하므로 분기가 필요 없다.)
    /// </summary>
    public static WallPanels.Panel ToPanel(in Tile t, double anchorDeg = 20.0)
    {
        Point3 center = default, aPos = default;
        (double x, double y, double z) aDir = default;
        if (t.IsFull)
        {
            aPos = new Point3(
                t.Origin.X + t.PocketU * t.UAxis.x + t.PocketV * t.VAxis.x,
                t.Origin.Y + t.PocketU * t.UAxis.y + t.PocketV * t.VAxis.y,
                t.Origin.Z + t.PocketU * t.UAxis.z + t.PocketV * t.VAxis.z);
            center = aPos;
            double a = anchorDeg * System.Math.PI / 180.0;
            double ca = System.Math.Cos(a), sa = System.Math.Sin(a);
            double dx = -t.WAxis.x * ca, dy = -t.WAxis.y * ca, dz = -t.WAxis.z * ca - sa;
            double dl = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dl > 1e-9) aDir = (dx / dl, dy / dl, dz / dl);
        }
        return new WallPanels.Panel(
            t.Poly, t.IsFull, center, t.WAxis, aPos, aDir,
            t.Origin, t.UAxis, t.VAxis, t.WAxis, t.Local, t.PocketU, t.PocketV);
    }

    private static double Dist2D(Point3 a, Point3 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>로컬 (u,v) 다각형 안에 점이 있는가 — 도넛 네 모서리 검사용(v13.9에서 확립된 판정).</summary>
    internal static bool PointInPoly(double u, double v, IReadOnlyList<(double u, double v)> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = poly[i]; var b = poly[j];
            if ((a.v > v) != (b.v > v) &&
                u < (b.u - a.u) * (v - a.v) / (b.v - a.v + (b.v == a.v ? 1e-300 : 0)) + a.u)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// [0805 — 모델링 작업 오류 115094] 판넬 다각형에서 <b>중복 정점과 같은 직선 위의 점</b>을 없앤다.
    /// <para>
    /// 데이라잇 실루엣을 촘촘히(0.15m) 표본하면 잘리지 않은 구간의 윗변에 같은 높이의 점이 10개 넘게 생긴다.
    /// 그 자체로는 모양이 옳지만, ACIS는 중복·공선 정점이 있는 다각형에서 압출·불리언에 실패하고
    /// 명령창에 <c>모델링 작업 오류: Error Code Number is 115094</c>를 대량으로 뿜는다(현장 실측).
    /// 모양을 바꾸지 않는 선에서(수직거리 1mm) 점만 줄인다 — 잘린 자리의 꺾임은 그대로 남는다.
    /// </para>
    /// </summary>
    /// <summary>볼록한가 — 자연석 무늬 클립이 볼록한 창에서만 옳게 동작하므로(115094) 내보내기 전 확인한다.</summary>
    public static bool IsConvex(IReadOnlyList<(double u, double v)> p)
    {
        int n = p.Count;
        if (n < 3) return false;
        int sign = 0;
        for (int i = 0; i < n; i++)
        {
            var a = p[i]; var b = p[(i + 1) % n]; var c = p[(i + 2) % n];
            double cr = (b.u - a.u) * (c.v - b.v) - (b.v - a.v) * (c.u - b.u);
            if (System.Math.Abs(cr) < 1e-12) continue;
            int s = cr > 0 ? 1 : -1;
            if (sign == 0) sign = s; else if (s != sign) return false;
        }
        return true;
    }

    /// <summary>
    /// [0806 JACK '무늬패턴이 누락된 애들이 또 생겼어'] 오목한 판넬을 <b>볼록 조각들로 쪼갠다</b>.
    /// <para>
    /// 자연석 무늬는 돌을 판넬 모양에 맞춰 잘라내는데(Sutherland–Hodgman), 그 클립은 <b>볼록한 창에서만</b> 옳다.
    /// v19.20은 이 제약을 '오목하면 무늬를 통째로 건너뛴다'로 피했다 — 드물 거라 봤지만 현장에서
    /// 201장 중 25장이 민판으로 나와 눈에 띄었다(JACK 0806). 건너뛰는 대신 <b>창을 볼록하게 쪼갠다</b>.
    /// 조각이 전부 볼록하므로 자기교차가 원천적으로 없어 115094도 안 나고, 조각들의 합집합은
    /// 원래 판넬과 정확히 같으므로 무늬가 모양대로 꽉 찬다.
    /// </para>
    /// 귀 자르기(ear clipping)로 삼각분할한 뒤 Hertel–Mehlhorn으로 다시 합친다 — 대각선 하나를 지웠을 때
    /// 양쪽이 모두 볼록하면 지운다. 5·6각형에 오목점 하나면 보통 2조각이 된다.
    /// <para>볼록하면 자기 자신 1조각(빠른 길). 쪼개기에 실패하면 <b>빈 목록</b> — 호출부는 종전대로 무늬를 건너뛴다.</para>
    /// </summary>
    public static List<List<(double u, double v)>> ConvexPieces(IReadOnlyList<(double u, double v)> poly)
    {
        var outp = new List<List<(double u, double v)>>();
        if (poly == null || poly.Count < 3) return outp;
        if (IsConvex(poly)) { outp.Add(new List<(double u, double v)>(poly)); return outp; }

        // CCW로 맞춘다 — 귀 판정(cross > 0)이 방향에 의존한다.
        var v = new List<(double u, double v)>(poly);
        if (Area2(v) < 0) v.Reverse();
        int n = v.Count;

        // ── 귀 자르기 → 삼각형(원본 정점 인덱스로) ──
        var ring = new List<int>(n);
        for (int i = 0; i < n; i++) ring.Add(i);
        var tris = new List<List<int>>();
        int guard = 0;
        while (ring.Count > 3)
        {
            if (++guard > 4 * n) return new List<List<(double u, double v)>>();   // 안 잘리는 다각형(자기교차 등)
            bool cut = false;
            for (int k = 0; k < ring.Count; k++)
            {
                int ia = ring[(k - 1 + ring.Count) % ring.Count], ib = ring[k], ic = ring[(k + 1) % ring.Count];
                if (Cross(v[ia], v[ib], v[ic]) <= 1e-12) continue;                 // 오목하거나 일직선 — 귀 아님
                bool clean = true;
                for (int m = 0; m < ring.Count && clean; m++)
                {
                    int ip = ring[m];
                    if (ip == ia || ip == ib || ip == ic) continue;
                    if (Cross(v[ia], v[ib], v[ic]) > 0 && InTri(v[ip], v[ia], v[ib], v[ic])) clean = false;
                }
                if (!clean) continue;
                tris.Add(new List<int> { ia, ib, ic });
                ring.RemoveAt(k);
                cut = true; break;
            }
            if (!cut) return new List<List<(double u, double v)>>();
        }
        tris.Add(new List<int>(ring));

        // ── Hertel–Mehlhorn — 공유 변을 지웠을 때 합친 모양이 볼록하면 합친다 ──
        for (bool again = true; again;)
        {
            again = false;
            for (int a = 0; a < tris.Count && !again; a++)
                for (int b = a + 1; b < tris.Count && !again; b++)
                {
                    var merged = MergeOnSharedEdge(tris[a], tris[b], v);
                    if (merged == null) continue;
                    var shape = new List<(double u, double v)>(merged.Count);
                    foreach (int i in merged) shape.Add(v[i]);
                    if (!IsConvex(shape)) continue;
                    tris[a] = merged; tris.RemoveAt(b); again = true;
                }
        }

        foreach (var t in tris)
        {
            var shape = new List<(double u, double v)>(t.Count);
            foreach (int i in t) shape.Add(v[i]);
            outp.Add(shape);
        }
        return outp;
    }

    /// <summary>두 조각이 <b>변 하나</b>(a→b와 b→a)를 공유하면 합친 고리를 돌려준다. 아니면 null.</summary>
    private static List<int>? MergeOnSharedEdge(List<int> A, List<int> B, List<(double u, double v)> v)
    {
        for (int i = 0; i < A.Count; i++)
        {
            int a = A[i], b = A[(i + 1) % A.Count];
            for (int j = 0; j < B.Count; j++)
            {
                if (B[j] != b || B[(j + 1) % B.Count] != a) continue;
                var m = new List<int>(A.Count + B.Count - 2);
                for (int k = 1; k < A.Count; k++) m.Add(A[(i + k) % A.Count]);          // b … a 중 a 제외
                for (int k = 1; k < B.Count; k++) m.Add(B[(j + k) % B.Count]);          // a … b 중 b 제외
                return m;
            }
        }
        return null;
    }

    private static double Area2(IReadOnlyList<(double u, double v)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var s = p[i]; var t = p[(i + 1) % p.Count]; a += s.u * t.v - t.u * s.v; }
        return a / 2;
    }

    /// <summary>다각형 면적(부호 없음) — 볼록 분해 검증용(조각 합 = 원본).</summary>
    public static double PolyArea(IReadOnlyList<(double u, double v)> p) => System.Math.Abs(Area2(p));

    private static double Cross((double u, double v) a, (double u, double v) b, (double u, double v) c)
        => (b.u - a.u) * (c.v - a.v) - (b.v - a.v) * (c.u - a.u);

    private static bool InTri((double u, double v) p, (double u, double v) a, (double u, double v) b, (double u, double v) c)
        => Cross(a, b, p) > 1e-12 && Cross(b, c, p) > 1e-12 && Cross(c, a, p) > 1e-12;

    internal static List<(double u, double v)> Simplify(List<(double u, double v)> p, double tol = 1e-3)
    {
        var q = new List<(double u, double v)>(p.Count);
        foreach (var pt in p)
        {
            if (q.Count > 0 && System.Math.Abs(q[q.Count - 1].u - pt.u) < 1e-6
                            && System.Math.Abs(q[q.Count - 1].v - pt.v) < 1e-6) continue;
            q.Add(pt);
        }
        while (q.Count >= 2 && System.Math.Abs(q[0].u - q[q.Count - 1].u) < 1e-6
                            && System.Math.Abs(q[0].v - q[q.Count - 1].v) < 1e-6) q.RemoveAt(q.Count - 1);

        bool changed = true;
        while (changed && q.Count > 3)
        {
            changed = false;
            for (int i = 0; i < q.Count; i++)
            {
                var a = q[(i - 1 + q.Count) % q.Count]; var b = q[i]; var c = q[(i + 1) % q.Count];
                double ax = c.u - a.u, ay = c.v - a.v;
                double len = System.Math.Sqrt(ax * ax + ay * ay);
                if (len < 1e-9) continue;
                double cross = (b.u - a.u) * ay - (b.v - a.v) * ax;
                if (System.Math.Abs(cross) / len < tol) { q.RemoveAt(i); changed = true; break; }
            }
        }
        return q;
    }

    /// <summary>폴리선의 누적 2D 호길이.</summary>
    private static double[] Cum(IReadOnlyList<Point3> p)
    {
        var c = new double[p.Count];
        for (int i = 1; i < p.Count; i++) c[i] = c[i - 1] + Dist2D(p[i - 1], p[i]);
        return c;
    }

    /// <summary>판넬(직선)이 곡선 벽선에서 안쪽으로 파고들 수 있는 최대 깊이(m). 이보다 깊어지면 열을 좁힌다.
    /// 0.05m = 5cm — 줄눈(5cm)과 같은 수준이라 눈에 띄지 않는다.</summary>
    public const double ChordTol = 0.05;

    /// <summary>[하네스 전용] 현(弦) 이탈 제한을 꺼서 '커브에서 판넬이 안쪽으로 파고드는' 버그를 재현한다 —
    /// S24가 실제로 그 버그를 잡는 검사인지 확인하는 용도. 운영 코드에서는 절대 켜지 않는다.</summary>
    public static bool DisableChordLimitForTest;

    /// <summary>[하네스 전용] 토우↔크레스트 대응을 옛 방식(호길이)으로 되돌려 '모서리에서 판넬이 눕는' 버그를
    /// 재현한다. 운영 코드에서는 절대 켜지 않는다.</summary>
    public static bool DisableIndexPairingForTest;

    /// <summary>
    /// 구간 [f0,f1]을 ncol개 열로 나눴을 때, **각 열의 현(弦)이 실제 벽선에서 벗어나는 최대 깊이**.
    /// 열의 양 끝을 잇는 직선과, 그 사이 실제 정점들 사이의 거리 중 최대값.
    /// </summary>
    /// <summary>[0806] 토우 쪽 현(弦) 이탈 — 크레스트 호길이 구간 [f0,f1]에 <b>인덱스로 대응하는</b> 토우 구간에서,
    /// 그 사이 토우 정점들이 양 끝을 잇는 직선(=판넬 아랫변)에서 얼마나 벗어나는지.
    /// 오목 코너 부근에서는 토우가 크레스트보다 더 꺾이므로 이쪽을 안 보면 판넬 아랫변이 선을 벗어난다.</summary>
    private static double MaxToeChordDev(IReadOnlyList<Point3> toe, double[] cumC, double f0, double f1)
    {
        var la = LocOfFrac(cumC, f0); var lb = LocOfFrac(cumC, f1);
        var A = AtLoc(toe, la.Lo, la.T); var B = AtLoc(toe, lb.Lo, lb.T);
        double ax = B.X - A.X, ay = B.Y - A.Y, L = System.Math.Sqrt(ax * ax + ay * ay);
        if (L < 1e-9) return 0;
        double worst = 0;
        for (int i = la.Lo + 1; i <= lb.Lo && i < toe.Count; i++)
        {
            double d = System.Math.Abs((toe[i].X - A.X) * ay - (toe[i].Y - A.Y) * ax) / L;
            if (d > worst) worst = d;
        }
        return worst;
    }

    private static double MaxChordDev(IReadOnlyList<Point3> line, double[] cum, double f0, double f1, int ncol)
    {
        double total = cum[cum.Length - 1];
        if (total <= 1e-9 || ncol < 1) return 0;
        double worst = 0;
        for (int j = 0; j < ncol; j++)
        {
            double fa = f0 + (f1 - f0) * j / ncol, fb = f0 + (f1 - f0) * (j + 1) / ncol;
            var A = AtFrac(line, cum, fa); var B = AtFrac(line, cum, fb);
            double ax = B.X - A.X, ay = B.Y - A.Y, L = System.Math.Sqrt(ax * ax + ay * ay);
            if (L < 1e-9) continue;
            double ua = fa * total, ub = fb * total;
            // 이 열 안에 들어오는 실제 정점들만 본다.
            for (int i = 0; i < line.Count; i++)
            {
                if (cum[i] <= ua + 1e-9 || cum[i] >= ub - 1e-9) continue;
                double d = System.Math.Abs((line[i].X - A.X) * ay - (line[i].Y - A.Y) * ax) / L;
                if (d > worst) worst = d;
            }
        }
        return worst;
    }

    /// <summary>
    /// [치명 0805] 크레스트 호길이 비율 f가 놓인 **구간 번호와 그 안의 보간값**을 준다.
    /// <para>
    /// 옹벽선은 <b>인덱스 1:1</b>로 만들어진다(WallRunBuilder: <c>Toe[i] = Crest[i]의 최근접 토우점</c>).
    /// 그런데 쓰는 쪽이 같은 <b>호길이 비율</b>을 두 선에 각각 적용하면, 두 선의 전체 길이가 다를 때
    /// (볼록 모서리에서 크레스트가 더 길다 — 1:0.05·5m 벽이면 90° 코너당 약 0.5m) <b>토우 쪽이 미끄러진다</b>.
    /// 그러면 그 열의 V축(토우→크레스트)이 설계 0.25m가 아니라 수십 cm가 되어 <b>그 판넬만 확 눕는다</b>.
    /// → 구간 번호와 보간값을 크레스트에서 구해 <b>토우에 그대로</b> 쓴다.
    /// </para></summary>
    private static (int Lo, double T) LocOfFrac(double[] cum, double f)
    {
        double total = cum[cum.Length - 1];
        if (total <= 1e-12) return (0, 0);
        double u = System.Math.Clamp(f, 0, 1) * total;
        int lo = 0, hi = cum.Length - 1;
        while (lo < hi) { int m = (lo + hi + 1) / 2; if (cum[m] <= u) lo = m; else hi = m - 1; }
        if (lo >= cum.Length - 1) return (cum.Length - 2 < 0 ? 0 : cum.Length - 2, 1);
        double seg = cum[lo + 1] - cum[lo];
        return (lo, seg > 1e-12 ? (u - cum[lo]) / seg : 0);
    }

    /// <summary>구간 번호와 보간값으로 점을 낸다 — 두 선을 **같은 (구간, 보간)** 으로 읽어 대응을 보존한다.</summary>
    private static Point3 AtLoc(IReadOnlyList<Point3> p, int lo, double t)
    {
        if (p.Count == 0) return default;
        if (lo >= p.Count - 1) return p[p.Count - 1];
        var a = p[lo]; var b = p[lo + 1];
        return new Point3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }

    /// <summary>정규화 위치 f∈[0,1]에서의 점(2D 호길이 기준 보간).</summary>
    private static Point3 AtFrac(IReadOnlyList<Point3> p, double[] cum, double f)
    {
        double total = cum[cum.Length - 1];
        if (total <= 1e-12) return p[0];
        double u = System.Math.Clamp(f, 0, 1) * total;
        int lo = 0, hi = cum.Length - 1;
        while (lo < hi) { int m = (lo + hi + 1) / 2; if (cum[m] <= u) lo = m; else hi = m - 1; }
        if (lo >= p.Count - 1) return p[p.Count - 1];
        double seg = cum[lo + 1] - cum[lo];
        double t = seg > 1e-12 ? (u - cum[lo]) / seg : 0;
        var a = p[lo]; var b = p[lo + 1];
        return new Point3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }

    /// <summary>
    /// 띠를 <b>모서리에서 끊는다</b> — 진행 방향이 <paramref name="cornerDeg"/> 이상 꺾이는 정점에서 분할.
    /// 판넬이 모서리를 가로지르지 않게 하는 것이 이 방식의 핵심이다(가로지르면 평면이 아니게 되고,
    /// 그걸 억지로 맞추려던 것이 종전의 이웃 평면 절단 — 버그의 온상이었다).
    /// 반환값은 크레스트 기준 정규화 구간 [f0,f1] 목록.
    /// </summary>
    /// <param name="minFaceLen">이보다 짧은 벽면은 이웃에 합친다(m). 0이면 합치지 않는다.
    /// <para>
    /// ★[0806 JACK '중간에 판넬 가로 넓이가 달라졌어'] 현장 실측이 <c>벽면길이 0.06m를 1등분</c>을 짚었다 —
    /// <b>6cm짜리 벽면</b>이 자기 몫의 판넬을 한 장 받아, 1.67m 판넬들 사이에 6cm 널빤지가 서 있었다.
    /// 옹벽선을 1m 간격으로 조밀화할 때 남는 자투리가 모서리와 겹치면 이런 토막 벽면이 생긴다.
    /// 맨 윗행이 너무 얇으면 아래 행에 합치는 규칙(<c>minTopRow</c>)과 <b>같은 처방</b>이다.
    /// </para>
    /// 합치면 판넬이 작은 모서리를 가로지르지만, 현(弦) 이탈 검사(<see cref="ChordTol"/>)가 실제로 휘면
    /// 열을 좁혀 따라가므로 안전하다. 다만 <b>많이 꺾인 모서리는 가로지르면 안 되므로</b>
    /// 꺾임이 <see cref="MergeMaxDeg"/>를 넘는 경계로는 합치지 않는다 —
    /// 진짜 코너 사이에 낀 짧은 벽면은 좁은 판넬이 정답이다.</param>
    /// <param name="alt">짝이 되는 반대편 선(토우). 주면 <b>어느 한쪽이라도 꺾이면</b> 벽면을 끊는다.
    /// <para>
    /// ★[JACK 0806 '공백은 사라졌는데 어긋남은 여전히 있어'] 종전엔 **크레스트 코너에서만** 끊었다.
    /// 그런데 벽이 1:n으로 기울어 있어 토우와 크레스트의 코너는 **호길이 위치가 다르다** —
    /// 게다가 v19.44에서 토우의 진짜 코너 정점을 끼워 넣으면서, 그 자리의 크레스트 짝은
    /// <b>보간점(직선 위의 점)</b>이라 크레스트 쪽에서는 꺾임이 안 보인다.
    /// 결과: <b>토우 코너를 가로지르는 판넬</b>이 생기고, 그 판넬의 아랫변은 코너를 무시한 현(弦)이 되어
    /// 아랫선에서 벗어난다 — 틈은 없지만 <b>선형이 어긋나 보인다</b>(JACK 스샷).
    /// </para>
    /// 두 선 중 한쪽이라도 꺾이면 끊으면 판넬이 어느 쪽 코너도 가로지르지 않는다.</param>
    public static List<(double F0, double F1)> SplitAtCorners(IReadOnlyList<Point3> crest, double cornerDeg = 12.0,
                                                             double minFaceLen = 0,
                                                             IReadOnlyList<Point3>? alt = null)
    {
        var outp = new List<(double, double)>();
        if (crest == null || crest.Count < 2) return outp;
        var cum = Cum(crest);
        double total = cum[cum.Length - 1];
        if (total <= 1e-9) return outp;

        // 닫힌 고리(부지를 한 바퀴 도는 벽)면 시작점도 하나의 모서리 후보다 — 여기를 안 보면
        //   시작점이 실제 모서리인 경우 그 자리 판넬이 코너를 가로지른다.
        bool closed = Dist2D(crest[0], crest[crest.Count - 1]) < 1e-6;

        double cosLim = System.Math.Cos(cornerDeg * System.Math.PI / 180.0);
        // 경계 위치 b[]와 그 자리의 꺾임 cos c[] — 짧은 벽면을 합칠 때 '어느 쪽으로 합칠지' 고르는 데 쓴다.
        //   c[0]과 c[마지막]은 벽의 끝이라 모서리가 아니다(NaN) — 그쪽으로는 합칠 수 없다.
        var b = new List<double> { 0.0 };
        var c = new List<double> { double.NaN };
        for (int i = 1; i < crest.Count - 1; i++)
        {
            double ax = crest[i].X - crest[i - 1].X, ay = crest[i].Y - crest[i - 1].Y;
            double bx = crest[i + 1].X - crest[i].X, by = crest[i + 1].Y - crest[i].Y;
            double la = System.Math.Sqrt(ax * ax + ay * ay), lb = System.Math.Sqrt(bx * bx + by * by);
            if (la < 1e-9 || lb < 1e-9) continue;
            double cos = (ax * bx + ay * by) / (la * lb);
            // 반대편 선(토우)이 이 자리에서 꺾이면, 크레스트가 곧아도 벽면을 끊는다(위 alt 설명).
            if (alt != null && alt.Count == crest.Count)
            {
                double a2x = alt[i].X - alt[i - 1].X, a2y = alt[i].Y - alt[i - 1].Y;
                double b2x = alt[i + 1].X - alt[i].X, b2y = alt[i + 1].Y - alt[i].Y;
                double l2a = System.Math.Sqrt(a2x * a2x + a2y * a2y), l2b = System.Math.Sqrt(b2x * b2x + b2y * b2y);
                if (l2a > 1e-9 && l2b > 1e-9)
                    cos = System.Math.Min(cos, (a2x * b2x + a2y * b2y) / (l2a * l2b));
            }
            if (cos >= cosLim) continue;                       // 양쪽 다 꺾임이 작다 — 같은 벽면으로 이어간다
            double f = cum[i] / total;
            if (f - b[b.Count - 1] > 1e-6) { b.Add(f); c.Add(cos); }
        }
        if (1.0 - b[b.Count - 1] > 1e-6) { b.Add(1.0); c.Add(double.NaN); }
        else { b[b.Count - 1] = 1.0; c[c.Count - 1] = double.NaN; }

        // ── 너무 짧은 벽면을 이웃에 합친다(위 minFaceLen 설명 참조) ──
        if (minFaceLen > 0)
        {
            double cosMerge = System.Math.Cos(MergeMaxDeg * System.Math.PI / 180.0);
            for (int guard = 0; b.Count > 2 && guard < 500; guard++)
            {
                int k = -1, drop = -1; double shortest = double.MaxValue;
                for (int i = 0; i + 1 < b.Count; i++)
                {
                    double len = (b[i + 1] - b[i]) * total;
                    if (len >= minFaceLen || len >= shortest) continue;
                    // 합칠 수 있는 경계 = 벽 끝이 아니고, 꺾임이 MergeMaxDeg 이내인 쪽. 덜 꺾인 쪽을 고른다.
                    double cs = i > 0 ? c[i] : double.NaN;                       // 이 벽면의 시작 경계
                    double ce = i + 1 < b.Count - 1 ? c[i + 1] : double.NaN;     // 이 벽면의 끝 경계
                    bool okS = !double.IsNaN(cs) && cs >= cosMerge;
                    bool okE = !double.IsNaN(ce) && ce >= cosMerge;
                    if (!okS && !okE) continue;                                  // 진짜 코너 사이 — 좁은 판넬이 정답
                    shortest = len; k = i;
                    drop = (okS && okE) ? (cs >= ce ? i : i + 1) : (okS ? i : i + 1);
                }
                if (k < 0) break;
                b.RemoveAt(drop); c.RemoveAt(drop);
            }
        }

        for (int i = 0; i + 1 < b.Count; i++)
            if (b[i + 1] - b[i] > 1e-6) outp.Add((b[i], b[i + 1]));
        // 닫힌 고리인데 시작점이 모서리가 아니면 첫 조각과 마지막 조각은 사실 한 벽면이다 —
        //   그대로 두면 곧은 벽 한가운데에 쓸데없는 이음매가 생긴다(판넬 두 장이 억지로 갈림).
        if (closed && outp.Count >= 2)
        {
            double ax = crest[1].X - crest[0].X, ay = crest[1].Y - crest[0].Y;
            int last = crest.Count - 2;
            double bx = crest[crest.Count - 1].X - crest[last].X, by = crest[crest.Count - 1].Y - crest[last].Y;
            double la = System.Math.Sqrt(ax * ax + ay * ay), lb = System.Math.Sqrt(bx * bx + by * by);
            if (la > 1e-9 && lb > 1e-9 && (bx * ax + by * ay) / (la * lb) >= cosLim)
            {
                // 시작점이 모서리가 아님 → 마지막 조각을 첫 조각에 이어 붙인 것으로 표시한다.
                //   (호길이 구간은 랩을 못 쓰므로 '두 조각을 한 벽면으로 본다'는 뜻의 병합 플래그 대신
                //    첫 조각의 시작을 마지막 조각의 시작으로 옮겨 표현할 수 없다 — 대신 그대로 두고
                //    이음매가 곧은 벽 한가운데 생기는 것만 진단으로 남긴다.)
                LastSplitNote = "닫힌 고리 시작점 이음매(곧은 벽) — 판넬 2장이 갈림";
            }
            else LastSplitNote = "";
        }
        else LastSplitNote = "";
        return outp;
    }

    /// <summary>직전 <see cref="SplitAtCorners"/>에서 알아둘 만한 사항(닫힌 고리 이음매 등).</summary>
    public static string LastSplitNote { get; private set; } = "";

    /// <summary>
    /// 옹벽선(띠) 하나를 판넬로 자른다.
    /// </summary>
    /// <param name="run">옹벽선 — 정지면 생성 때 확정해 저장한 정본.</param>
    /// <param name="ground">원지반(데이라잇 상한). null이면 클립하지 않는다.</param>
    /// <param name="joint">줄눈 폭(m) — 판넬 각 변에서 절반씩 안으로 물린다.</param>
    /// <param name="cornerDeg">이 각도 이상 꺾이면 벽면을 끊는다.</param>
    /// <param name="cornerLap">모서리에서 벽면 끝 열을 더 내보내는 길이(m) — 두께의 절반이 기본.
    /// 볼록 모서리에 쐐기 틈이 남지 않게 한다(JACK '각진부 마감').</param>
    public static List<Tile> Slice(WallRun run, IGroundSurface? ground, double joint = 0.05,
                                   double cornerDeg = 12.0, double cornerLap = 0.10)
    {
        var tiles = new List<Tile>();
        if (run == null || run.Toe == null || run.Crest == null || run.Toe.Count < 2 || run.Crest.Count < 2)
        { LastDiag = "옹벽선 없음"; return tiles; }

        var toe = run.Toe; var crest = run.Crest;
        var cumT = Cum(toe); var cumC = Cum(crest);
        if (cumC[cumC.Length - 1] <= 1e-9) { LastDiag = "옹벽선 길이 0"; return tiles; }

        double height = run.Height;
        if (height <= 1e-9)
        {
            double zt = 0, zc = 0;
            foreach (var p in toe) zt += p.Z; zt /= toe.Count;
            foreach (var p in crest) zc += p.Z; zc /= crest.Count;
            height = System.Math.Abs(zc - zt);
        }
        if (height <= 0.1) { LastDiag = $"벽 높이 {height:F2}m — 너무 낮아 생략"; return tiles; }

        double side = SideFor(height);
        double jm = System.Math.Max(0, joint) / 2;
        // 조용히 버려지는 자리마다 사유별 계수기(0805 작업규칙).
        int colN = 0, rowN = 0, dGround = 0, dAbove = 0, dJoint = 0, dThin = 0, dSliver = 0;
        // [진단 0805] 데이라잇까지 못 올라온 열 — 조각이 버려져 그 열만 주저앉은 자리(JACK '판넬이 잘려 보임').
        int colShort = 0; double maxShort = 0, shortX = 0, shortY = 0;
        // [0806] '지반위' 버림이 정상인지 가르는 실측 — 토우가 원지반보다 높은 거리(m).
        int aboveN = 0; double aboveMin = 0, aboveMax = 0, aboveX = 0, aboveY = 0;
        // [0806 JACK '판넬 가로 넓이가 달라졌다' / '살짝 누락부'] 열 폭 분포와 실오라기 구멍의 실측.
        double minColW = double.MaxValue, maxColW = 0, narrowX = 0, narrowY = 0, narrowLen = 0;
        int narrowN = 0, narrowN2 = 0, faceCnt = 0, chordSplit = 0; string sliverFirst = ""; double noSplitDev = 0;
        // [0806] 토우가 크레스트보다 길어 열 폭을 늘린 횟수와 최대 증가량 — 오목 코너에서만 나와야 정상.
        int toeLong = 0; double toeLongMax = 0;
        // [0806] 열마다 '만들었나·왜 못 만들었나' — 벽 한가운데 구멍('길게 누락됨')을 끝단 데이라잇과 가르는 장치.
        var colLog = new List<(bool Made, string Why, double X, double Y, double W)>();
        // [0806] 이 줄의 코너 목록(볼록/오목)과 '코너 조각'(모서리 라운딩이 만든 규격 미만 벽면) 실측.
        var myCorners = new List<(double X, double Y, bool Convex)>();
        int facetCnv = 0, facetCav = 0; double facetMin = double.MaxValue, facetX = 0, facetY = 0; bool facetCav2 = false;
        // [진단 0805] 판넬 잘림 가설을 가르는 두 숫자 — ②상한 계산이 틀렸나 ③열 중간에 구멍이 났나.
        int capOff = 0; double maxCapOff = 0, capOffX = 0, capOffY = 0;
        int colHole = 0; double maxHole = 0, holeX = 0, holeY = 0;
        // [진단] 실루엣 윗변이 오목해져 옛 사다리꼴로 물러난 횟수 — 0이면 전부 제대로 5각/6각으로 잘렸다는 뜻.
        int nonConvex = 0; string firstConcave = "";
        int full = 0;

        var runs = SplitAtCorners(crest, cornerDeg,
                                  DisableShortFaceMergeForTest ? 0 : MinFaceLenFor(side),
                                  toe.Count == crest.Count ? toe : null);   // 토우 코너에서도 끊는다(0806)

        // ★[0806 JACK '판넬부는 오목부에서 자꾸 오류가 나는 것 같다 — 누더기 수리 말고 정확히 확인해봐']
        //   벽면 경계(코너)마다 **볼록/오목**을 판정해 좌표와 함께 모아 둔다.
        //   판정: 진행 방향이 도는 쪽과 노출면이 있는 쪽이 **같으면 볼록**(벽이 밖으로 돌출),
        //         **다르면 오목**(벽이 안으로 꺾임 — 이웃 벽면끼리 서로를 향해 다가온다).
        //   오목 코너에서는 두 벽면이 서로를 향하므로 모서리 겹침(cornerLap)이 **겹침이 아니라 관통**이 되고,
        //   토우/크레스트의 오프셋 길이 차이도 볼록과 부호가 반대다. 이 목록으로 결함이 정말
        //   오목 코너에 몰리는지 **세어서** 확인한다(스샷 심증 → 숫자로 확정).
        var cornerConcave = new bool[System.Math.Max(1, runs.Count)];
        var cornerDegAt = new double[System.Math.Max(1, runs.Count)];
        for (int rIdx = 0; rIdx + 1 < runs.Count; rIdx++)
        {
            var lc = LocOfFrac(cumC, runs[rIdx].F1);
            int vi = System.Math.Clamp(lc.Lo + (lc.T > 0.5 ? 1 : 0), 1, crest.Count - 2);
            double ix = crest[vi].X - crest[vi - 1].X, iy = crest[vi].Y - crest[vi - 1].Y;
            double ox = crest[vi + 1].X - crest[vi].X, oy = crest[vi + 1].Y - crest[vi].Y;
            double il = System.Math.Sqrt(ix * ix + iy * iy), ol = System.Math.Sqrt(ox * ox + oy * oy);
            if (il < 1e-9 || ol < 1e-9) continue;
            ix /= il; iy /= il; ox /= ol; oy /= ol;
            double turn = ix * oy - iy * ox;                       // >0 좌회전
            // ※방향만 쓰므로 토우 대응 방식(인덱스/호길이)에 안 민감하다 — 호길이로 단순하게 잡는다.
            var tp = AtFrac(toe, cumT, runs[rIdx].F1);
            double fx = tp.X - crest[vi].X, fy = tp.Y - crest[vi].Y;   // 크레스트→토우 = 노출면 방향
            double fl = System.Math.Sqrt(fx * fx + fy * fy);
            if (fl < 1e-9) continue;
            double faceSide = ix * (fy / fl) - iy * (fx / fl);      // >0 노출면이 진행 방향 왼쪽
            if (System.Math.Abs(turn) < 1e-9 || System.Math.Abs(faceSide) < 1e-9) continue;
            bool cvx = (turn > 0) == (faceSide > 0);
            cornerConcave[rIdx] = !cvx;
            cornerDegAt[rIdx] = System.Math.Atan2(System.Math.Abs(turn), ix * ox + iy * oy) * 180.0 / System.Math.PI;
            // ★[0806] '볼록/오목'은 **노출면에서 본** 이름이라 위에서 내려다본 JACK의 말과 반대일 수 있다.
            //   그래서 꺾임 각도(도)도 함께 남긴다 — 이름이 어긋나도 각도와 좌표로 같은 자리를 가리킬 수 있다.
            double deg = System.Math.Atan2(System.Math.Abs(turn), ix * ox + iy * oy) * 180.0 / System.Math.PI;
            tCorners.Add((crest[vi].X, crest[vi].Y, crest[vi].Z, cvx, deg));
            myCorners.Add((crest[vi].X, crest[vi].Y, cvx));
            // 이 코너를 낀 두 벽면의 길이 — 짧은 쪽이 '코너 조각'이다(모서리 라운딩이 만든 토막).
            double cTot = cumC[cumC.Length - 1];
            double lenA = (runs[rIdx].F1 - runs[rIdx].F0) * cTot;
            double lenB = (runs[rIdx + 1].F1 - runs[rIdx + 1].F0) * cTot;
            double shortSide = System.Math.Min(lenA, lenB);
            if (shortSide < side)
            {
                if (cvx) facetCnv++; else facetCav++;
                if (shortSide < facetMin) { facetMin = shortSide; facetX = crest[vi].X; facetY = crest[vi].Y; facetCav2 = !cvx; }
            }
        }
        double totalC = cumC[cumC.Length - 1];
        // 인덱스 대응이 성립하려면 두 선의 정점 수가 같아야 한다(WallRunBuilder가 그렇게 만든다).
        //   옛 번들 등으로 어긋나 있으면 호길이 대응으로 물러난다 — 그 사실을 진단에 남긴다.
        bool pairByIndex = toe.Count == crest.Count && !DisableIndexPairingForTest;

        // [진단 0805 — JACK '어긋나게 생성됨'] 판넬이 **옹벽선 위에** 놓였는지 직접 잰다.
        //   선이 멀쩡해도 배치가 어긋나면 벽이 딴 데로 간다 — 선 문제와 배치 문제를 갈라야 한다.
        //   각 판넬 아랫변 중점에서 토우선까지의 거리. 모서리 겹침(cornerLap)만큼은 정상이다.
        double offLine = 0, offX = 0, offY = 0; int offN = 0;
        // ★[0806 JACK '오목부에서 빈공간 + 방향도 어긋나 동일 선상에 생성되지 않음'] 판넬 이탈을
        //   **코너 종류별로** 나눠 잰다. 오목에서만 크면 원인이 코너 처리로 확정된다(전체 최대값 하나로는 안 갈린다).
        double offCav = 0, offCnv = 0, offFar = 0, offCavX = 0, offCavY = 0;
        void CheckOnLine(Point3 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < toe.Count; i++)
            {
                double ax = toe[i].X, ay = toe[i].Y, bx = toe[i + 1].X, by = toe[i + 1].Y;
                double dx = bx - ax, dy = by - ay, L2 = dx * dx + dy * dy;
                double tt = L2 > 1e-12 ? ((p.X - ax) * dx + (p.Y - ay) * dy) / L2 : 0;
                tt = System.Math.Clamp(tt, 0, 1);
                double px = ax + dx * tt, py = ay + dy * tt;
                double d2 = (p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py);
                if (d2 < best) best = d2;
            }
            double d = System.Math.Sqrt(best);
            // 문턱을 두지 않고 **최대값을 그대로** 남긴다 — 문턱(0.35m)을 두니 '전부 옹벽선 위'로 통과해
            //   실제로 얼마나 파고들었는지가 안 보였다(JACK '커브에서 한 판넬만 안쪽으로').
            //   판넬은 직선이고 벽선은 곡선이라, 한 판넬이 곡선의 **현(弦)**이 되어 가운데가 안쪽으로 들어간다.
            //   그 깊이가 이 값이다 — 곡률이 셀수록 커진다.
            if (d > offLine) { offLine = d; offX = p.X; offY = p.Y; }
            if (d > 0.35) offN++;
            // 가장 가까운 코너(2m 이내)에 이 이탈을 귀속시킨다 — 오목/볼록/코너밖으로 갈라 최대값을 남긴다.
            double cb = double.MaxValue; bool cbCav = false;
            foreach (var c in myCorners)
            {
                double dd = System.Math.Sqrt((c.X - p.X) * (c.X - p.X) + (c.Y - p.Y) * (c.Y - p.Y));
                if (dd < cb) { cb = dd; cbCav = !c.Convex; }
            }
            if (cb > 2.0) { if (d > offFar) offFar = d; }
            else if (cbCav) { if (d > offCav) { offCav = d; offCavX = p.X; offCavY = p.Y; } }
            else { if (d > offCnv) offCnv = d; }
        }

        // 벽선이 닫힌 고리면 모든 벽면 경계가 '코너'다. 열린 선이면 **양 끝은 코너가 아니라 벽의 끝**이다.
        bool closedRun = Dist2D(crest[0], crest[crest.Count - 1]) < 1e-6;
        for (int rIdx = 0; rIdx < runs.Count; rIdx++)
        {
            var (f0, f1) = runs[rIdx];
            // ★[0805] 모서리 겹침은 **이웃 벽면이 있는 쪽에만** 붙인다.
            //   벽이 끝나는 자리(첫 벽면의 시작 / 마지막 벽면의 끝)에 붙이면 판넬이 옹벽선 밖으로
            //   두께 절반(0.10m)만큼 튀어나온다 — 메울 코너가 없는데 메우려 든 것이다.
            //   좁은 커브에서는 이 튀어나옴이 '판넬이 안쪽/바깥으로 어긋난' 것처럼 보였다(실측 0.211m 중 큰 몫).
            bool lapStart = closedRun || rIdx > 0;
            bool lapEnd = closedRun || rIdx < runs.Count - 1;
            double segLen = (f1 - f0) * totalC;
            if (segLen < 1e-3) continue;
            // 열 폭을 **균등 분배** — ceil로 개수를 정하고 길이를 n등분한다.
            //   종전처럼 side로 자르고 나머지를 자투리 열로 두면 수 mm짜리 실오라기가 생겨
            //   줄눈 인셋에 통째로 죽었다(v17.8에서 '줄눈 1690'의 정체).
            // ★[JACK 0806 '가로길이가 계속 제각각 나오게 하지 말고 높이에 따라 통일하되 맨 마지막에서 잘림으로 조절해']
            //   종전엔 벽면 길이를 열 수로 **n등분**해서, 벽면마다 판넬 폭이 달랐다(현장 실측 0.06~1.67m —
            //   1.67m 판넬들 사이에 6cm 널빤지가 섰다). 이제 폭은 **언제나 한 변**(단높이 규칙에서 나온 값)이고,
            //   남는 자투리만 **맨 끝 한 장**을 잘라 맞춘다. 실제 옹벽도 규격 판넬을 깔고 끝에서 잘라 쓴다.
            var edge = new List<double> { 0.0 };                       // 벽면 안 누적 길이(m)
            for (int k = 1; k * side < segLen - 1e-9; k++) edge.Add(k * side);
            edge.Add(segLen);
            //   자투리가 너무 짧으면(수 cm짜리는 줄눈 인셋에 통째로 죽는다 — v17.8 '줄눈 1690')
            //   **마지막 두 장을 반씩 나눠** 맞춘다. 앞 판넬에 그냥 붙이면 그 한 장이 규격을 넘어
            //   1.72m가 되고 '판넬 한 변 ≤ 설계 상한'이 깨진다(하니스 S24가 잡아냈다 — v18.0 '거대 쐐기'의 문턱).
            //   반씩 나누면 둘 다 규격 이하이면서 한 변 절반보다는 넓다.
            if (edge.Count >= 3 && edge[edge.Count - 1] - edge[edge.Count - 2] < MinTailLen)
                edge[edge.Count - 2] = (edge[edge.Count - 3] + edge[edge.Count - 1]) / 2;

            // ★[JACK 0805 '커브쪽에 한 판넬만 안쪽으로' — 실측 0.285m] 판넬은 **직선**이고 벽선은 **곡선**이라,
            //   한 열이 여러 정점을 가로지르면 판넬이 곡선의 **현(弦)** 이 되어 가운데가 벽 안으로 파고든다
            //   (현장에서 28.5cm 파고들어 최종 지표면에 묻혔다).
            //   ※SplitAtCorners(12°)로는 못 막는다: NTS Buffer의 라운드 모서리는 사분면당 8조각 =
            //     **한 조각 11.25°** 라 12° 문턱에 안 걸리고 원호 전체가 한 벽면으로 묶인다.
            //   폭 통일이 우선이므로 **벽면 전체를 좁히지 않고**, 이탈이 한도를 넘는 **그 열만** 반으로 쪼갠다.
            //   곧은 구간은 전부 규격 폭 그대로 남고, 급커브에서만 좁은 판넬이 나온다(실제 옹벽과 같다).
            // [0806 계측] **분할하지 않았다면** 판넬이 벽선에서 얼마나 벗어났을지 — 규격 폭 그대로의 이탈.
            //   현장 v19.32에서 `급커브 분할 711열`이 나왔는데, 그게 진짜 급커브 때문인지
            //   옹벽선의 잔잔한 흔들림(1m 조밀화 잡음) 때문인지 개수만으론 모른다.
            //   이 값이 작으면(≈ChordTol) 잡음에 과민한 것이고, 크면 진짜 커브라 분할이 옳다.
            for (int i = 0; i + 1 < edge.Count; i++)
            {
                double d = MaxChordDev(crest, cumC, f0 + (f1 - f0) * edge[i] / segLen,
                                              f0 + (f1 - f0) * edge[i + 1] / segLen, 1);
                if (d > noSplitDev) noSplitDev = d;
            }
            if (!DisableChordLimitForTest)
                for (int guard = 0; guard < 6; guard++)
                {
                    bool anySplit = false;
                    for (int i = 0; i + 1 < edge.Count; i++)
                    {
                        if (edge[i + 1] - edge[i] < 2 * MinTailLen) continue;   // 더 쪼개면 실오라기
                        double fa2 = f0 + (f1 - f0) * edge[i] / segLen;
                        double fb2 = f0 + (f1 - f0) * edge[i + 1] / segLen;
                        // ★[JACK 0806 '어긋남은 여전히 있어'] 현(弦) 이탈을 **크레스트에서만** 재고 있었다.
                        //   판넬은 평면이라 아랫변도 곧은데, **오목 코너 부근에서는 토우가 크레스트보다 더 꺾인다**
                        //   — 크레스트 기준으로는 통과한 열이 토우 쪽에서 0.405m나 벗어났다(하니스 실측).
                        //   두 선 다 재고 **더 나쁜 쪽**으로 판단한다.
                        double dev2 = System.Math.Max(MaxChordDev(crest, cumC, fa2, fb2, 1),
                                                      pairByIndex ? MaxToeChordDev(toe, cumC, fa2, fb2) : 0);
                        if (dev2 <= ChordTol) continue;
                        edge.Insert(i + 1, (edge[i] + edge[i + 1]) / 2);
                        chordSplit++; anySplit = true; i++;
                    }
                    if (!anySplit) break;
                }
            int ncol = edge.Count - 1;
            faceCnt++;

            for (int j = 0; j < ncol; j++)
            {
                colN++;
                double colW = edge[j + 1] - edge[j];
                double fa = f0 + (f1 - f0) * edge[j] / segLen;
                double fb = f0 + (f1 - f0) * edge[j + 1] / segLen;

                // [0806] 폭 분포 실측 — 규격 폭에서 벗어난 판넬이 어디에 몇 장인지.
                if (colW < minColW)
                {
                    minColW = colW;
                    var lfn = LocOfFrac(cumC, fa); var pfn = AtLoc(crest, lfn.Lo, lfn.T);
                    narrowX = pfn.X; narrowY = pfn.Y; narrowLen = segLen; narrowN2 = ncol;
                }
                if (colW > maxColW) maxColW = colW;
                if (colW < side - 1e-6) narrowN++;
                // ★모서리 겹침 마감 — 벽면 끝 열은 모서리 쪽으로 두께 절반만큼 더 나간다.
                //   두 벽면이 코너에서 정확히 만나면 볼록 모서리에 쐐기 틈이 남는다(JACK '각진부 마감 이상').
                //   판넬은 자기 평면을 따라 조금 더 나가므로 이웃 벽 뒤로 물려 코너가 꽉 찬다.
                //   ※ 옛 방식의 '이웃 평면으로 잘라내기'와 달리 **자르지 않는다** — 그게 버그의 온상이었다.
                // ★[0806 JACK '오목부에서 빈공간' — 하니스 S36으로 오프라인 재현 확정]
                //   **오목 코너에서는 아랫변(토우)이 윗변(크레스트)보다 길다.** 벽이 1:n으로 기울어
                //   크레스트가 토우보다 수평으로 d = n×높이 만큼 바깥에 있는데, 오목 코너에서는 그 오프셋이
                //   경로를 **잘라내서**(90° 코너면 양쪽 합쳐 2d) 크레스트가 그만큼 짧아지기 때문이다.
                //   그런데 판넬은 **크레스트 호길이로 잘라 놓고** 폭을 위아래 똑같이 쓴다 —
                //   그래서 위는 맞물리는데 **아래에 2d 만큼 틈**이 남는다(S36 실측: 0.43m ≈ 2×0.25 − 줄눈).
                //   고정 0.10m 겹침은 볼록 코너 기준값이라 이걸 못 메운다.
                //   → 오목 코너에서는 겹침을 **d + 여유**로 키운다. 늘어난 살은 이웃 벽 **뒤(흙 속)**로 들어가
                //     밖에서 안 보이고, 볼록 코너는 종전값 그대로 둔다(거긴 키우면 허공으로 튀어나온다).
                //   ※이 처방은 **두 번 기각됐다가 세 번째에 유효해졌다** — 순서 때문이었다.
                //     그때는 틈이 코너에서 1.7m(판넬 한 장 폭) 떨어져 있어 겹침이 닿을 거리가 아니었다.
                //     진짜 1차 원인(옹벽선 짝짓기가 오목 코너에서 바닥면으로 스냅 — WallRunBuilder)을 먼저 고치니
                //     남은 틈이 **코너 바로 위(0.2m)** 로 옮겨왔고, 그제야 이 처방이 맞는 자리가 됐다.
                //     교훈: 처방이 안 들으면 처방이 틀린 게 아니라 **아직 1차 원인 위에 있는** 것일 수 있다.
                // ★[0806 폐기] 오목 코너 겹침 확대(0.10→0.30)는 **세 번 시도해 세 번 다 실패**했다.
                //   ①·②는 아직 1차 원인 위에 있어 효과 0, ③은 1차를 고친 뒤 90°에서는 틈을 없앴지만
                //   100° 코너에서는 오히려 **틈을 늘렸고**(24→36곳), 직각부에서는 JACK이 **겹침이 더 심해졌다**고
                //   눈으로 확인했다. 틈 하나를 메우려고 코너를 더 망치는 거래다 — 폐기한다.
                //   남은 틈(≈0.4m)은 판넬을 더 미는 것으로 풀 문제가 아니라 **열 배치**에서 풀어야 한다.
                // ★[JACK 0806 '각진부 마감하는 게 반대로 들어간 것 같다 — 각진부는 오히려 튀어나오고
                //   붙어야 할 곳은 쪼개졌다'] 정확한 진단이었다.
                //   겹침(0.10m)은 **볼록 코너에서 두 벽면이 벌어지며 생기는 쐐기 틈**을 메우려고 내미는 살이다.
                //   그런데 **오목 코너에서는 두 벽면이 서로를 향해 다가온다** — 이미 물려 있는데 더 내미니
                //   판넬이 이웃 벽을 뚫고 **튀어나온다**. 방향이 정반대인 자리에 같은 처방을 쓰고 있었다.
                //   → 볼록 코너에만 내민다. 오목 코너에서는 내밀지 않는다(이미 물려 있다).
                //   ※내가 앞서 세 번 시도한 건 오목 겹침을 **키우는** 쪽이었다 — 부호를 거꾸로 짚었던 것이고,
                //     그때마다 '효과 0'이거나 JACK이 '직각부 겹침이 더 심해졌다'고 신고했다. 그게 신호였다.
                bool cavA = rIdx > 0 ? cornerConcave[rIdx - 1] : (closedRun && cornerConcave[runs.Count - 1]);
                bool cavB = rIdx < runs.Count - 1 ? cornerConcave[rIdx] : (closedRun && cornerConcave[runs.Count - 1]);
                double lapA = (j == 0 && lapStart && !cavA) ? cornerLap : 0;
                double lapB = (j == ncol - 1 && lapEnd && !cavB) ? cornerLap : 0;

                // ★크레스트에서 구한 (구간, 보간)을 토우에도 **그대로** 쓴다 — 인덱스 대응 보존(치명 0805).
                var la = LocOfFrac(cumC, fa); var lb = LocOfFrac(cumC, fb);
                var cA = AtLoc(crest, la.Lo, la.T); var cB = AtLoc(crest, lb.Lo, lb.T);
                var tA = pairByIndex ? AtLoc(toe, la.Lo, la.T) : AtFrac(toe, cumT, fa);
                var tB = pairByIndex ? AtLoc(toe, lb.Lo, lb.T) : AtFrac(toe, cumT, fb);

                // ── 로컬 프레임 ──
                // V부터 구한다(진행 방향과 무관 — 토우/크레스트 중점 차이).
                double mx = (cA.X + cB.X) / 2 - (tA.X + tB.X) / 2;
                double my = (cA.Y + cB.Y) / 2 - (tA.Y + tB.Y) / 2;
                double mz = (cA.Z + cB.Z) / 2 - (tA.Z + tB.Z) / 2;

                // ★ 벽면이 어느 쪽을 보는가 — 평면에서 **크레스트→토우 방향이 곧 노출면 방향**이다.
                //   절토: 토우가 부지(파낸 쪽) 안, 크레스트가 산 쪽 → 노출면은 부지를 본다
                //   성토: 크레스트가 부지 안, 토우가 바깥 → 노출면은 바깥을 본다
                //   ⇒ 둘 다 '크레스트→토우'. 절/성토 분기가 필요 없다.
                //   (수평 거리 = 구배n×높이 = 1:0.05·5m면 0.25m — 잡음보다 충분히 크다.)
                double faceX = -mx, faceY = -my;

                // U = 띠 진행의 **수평** 방향. 수평으로 잡는 것이 이 설계의 핵심 —
                //   V의 수평 성분은 벽면 법선 방향이라 U와 직교하고 나머지는 수직이라 역시 직교
                //   ⇒ **U·V = 0이 구조적으로 보장**된다(비틀린 프레임이 원천적으로 안 생김).
                double ux = cB.X - cA.X, uy = cB.Y - cA.Y;
                double ul = System.Math.Sqrt(ux * ux + uy * uy);
                if (ul < 1e-9) { dThin++; continue; }
                ux /= ul; uy /= ul;
                // W = U × V 의 수평 성분 부호로 진행 방향을 정한다. 노출면과 어긋나면 U를 뒤집는다
                //   (U를 뒤집으면 W도 뒤집혀 오른손 좌표계가 유지된다: (−U)×V = −W).
                if ((uy * mz) * faceX + (-ux * mz) * faceY < 0)
                {
                    double sf = fa; fa = fb; fb = sf;            // 구간 방향까지 뒤집어 데이라잇 보간과 일치시킨다
                    double sl = lapA; lapA = lapB; lapB = sl;
                    var sw = tA; tA = tB; tB = sw;
                    var sw2 = cA; cA = cB; cB = sw2;
                    ux = -ux; uy = -uy;
                }

                // V — U 성분을 빼 U⊥V를 확정(수치오차 제거).
                double du = mx * ux + my * uy;
                double vxr = mx - du * ux, vyr = my - du * uy;
                double vl = System.Math.Sqrt(vxr * vxr + vyr * vyr + mz * mz);
                if (vl < 1e-9) { dThin++; continue; }
                double vx = vxr / vl, vy = vyr / vl, vz = mz / vl;

                double wx = uy * vz, wy = -ux * vz, wz = ux * vy - uy * vx;   // W = U × V (uz=0)
                double wl = System.Math.Sqrt(wx * wx + wy * wy + wz * wz);
                if (wl < 1e-9) { dThin++; continue; }
                wx /= wl; wy /= wl; wz /= wl;

                var org = tA;                                    // 로컬 원점 = 이 열의 토우 시작점
                double faceH = vl;                               // 벽면(사면) 길이 — 수직높이가 아니라 경사길이

                // ★[JACK 0806 '선은 딱 맞어, 이걸 기준으로 다시 옹벽객체 작성해봐'] 선이 옳아진 뒤 남은 원인.
                //   열 폭은 **크레스트 호길이**를 n등분해 정한다. 그런데 벽이 1:n으로 기울어 있으므로
                //   **오목 코너 부근에서는 토우가 크레스트보다 길다**(그 차이가 코너당 2d = 0.5m).
                //   판넬은 원점(토우 시작점)에서 U 방향으로 **크레스트 폭만큼만** 뻗으므로,
                //   아랫변이 토우 끝점(tB)에 못 미치고 **그 차이만큼 옆 판넬과 벌어진다**
                //   (하니스 실측: 코너에서 판넬 한 장 떨어진 자리에 0.40m 틈 — 위는 맞물리는데 아래만 벌어진다).
                //   → 열 폭을 **크레스트와 토우 중 긴 쪽**에 맞춘다. 짧은 쪽은 줄눈이 조금 좁아질 뿐이지만
                //     긴 쪽을 못 덮으면 그 자리가 빈다.
                //   ★[JACK 0806 지문: '오목부에서만 · 한쪽만 · 꼭 한 판넬만 · 그 자리는 모든 단'] 원인 확정.
                //     판넬을 **직사각형**으로 만들고 있었다 — 폭을 크레스트 호길이 하나로 정해 위아래 똑같이 썼다.
                //     그런데 벽이 1:n으로 기울어 있어 **코너에서는 윗변과 아랫변 길이가 다르다**
                //     (오목이면 아랫변이 길고, 볼록이면 짧다 — 코너당 2d). 그래서 코너에 닿는 **딱 한 열**에서
                //     아랫변이 모자라거나(틈) 지나쳐(어긋남) 나간다. 코너 반대쪽은 부호가 반대라 한쪽만 보이고,
                //     단마다 같은 기하가 반복되므로 그 자리는 모든 단에서 똑같이 생긴다 — JACK의 지문 그대로다.
                //     → 판넬을 **사다리꼴**로 만든다: 아랫변은 토우 길이, 윗변은 크레스트 길이.
                //       (v19.45의 '긴 쪽에 맞춰 늘리기'는 이 사다리꼴의 절반짜리 근사였다 —
                //        늘리기만 하니 짧아야 할 쪽에서 코너를 지나쳐 나갔다.)
                double toeSpanU = (tB.X - tA.X) * ux + (tB.Y - tA.Y) * uy;
                double offEndX = tA.X + toeSpanU * ux - tB.X, offEndY = tA.Y + toeSpanU * uy - tB.Y;
                bool toeStraight = System.Math.Sqrt(offEndX * offEndX + offEndY * offEndY) < 0.05;
                // ★[JACK 0806] 판넬을 **사다리꼴**로 만든다 — 아랫변은 토우 길이, 윗변은 크레스트 길이.
                //   실제 옹벽면이 그 모양이다. 직사각형으로 만들면 코너에 닿는 딱 한 열에서
                //   아랫변이 모자라거나(틈) 지나쳐(어긋남) 나간다 — JACK 지문 네 가지가 전부 이것으로 설명됐다.
                //   폭(colW)은 **긴 쪽**으로 잡고, 짧은 쪽은 아래 사다리꼴 변환에서 줄인다.
                //   토우가 이 열 안에서 꺾이면(=코너를 넘고 있으면) 비율이 무의미하므로 손대지 않는다.
                //   → **폭을 토우 길이로** 잡는다. 판넬은 직사각형 그대로고(줄눈이 수직으로 유지된다),
                //     아랫변이 토우를 정확히 타일링하므로 밑에서 벌어지지도 지나치지도 않는다.
                //     윗변은 크레스트보다 조금 짧거나 길어지지만 그 차이는 **벽 꼭대기의 코너 부근**뿐이고
                //     모서리 겹침(0.10m)이 덮는다 — 눈에 띄는 밑동을 정확히 맞추는 쪽이 옳다.
                //   ※사다리꼴(아래=토우·위=크레스트)도 만들어 봤다. 기하는 맞지만 **판넬 옆면이 비스듬해져**
                //     실물 앵커판넬(직사각형·수직 줄눈)과 달라 보인다(JACK 0806 '사선으로 쪼개졌어'). 폐기.
                //   토우가 이 열 안에서 꺾이면(코너를 넘고 있으면) 길이가 무의미하므로 손대지 않는다.
                if (toeStraight && toeSpanU > 1e-6 && !DisableTrapezoidForTest
                    && System.Math.Abs(toeSpanU - colW) > 1e-6)
                { toeLong++; toeLongMax = System.Math.Max(toeLongMax, System.Math.Abs(toeSpanU - colW)); colW = toeSpanU; }

                // ★[0806 JACK '길게 누락됨' — 벽 전체 높이가 통째로 빈 세로줄] 열마다 '판넬이 나왔나·왜 안 나왔나'를
                //   순서대로 적어 둔다. 총계(지반위 5421 · 실오라기 2)로는 **그 구멍이 벽 한가운데인지 끝인지** 알 수 없다.
                //   양옆에 판넬이 있는데 가운데만 빈 열 = 진짜 구멍. 끝쪽이 빈 것 = 데이라잇(정상).
                int logIdx = colLog.Count, tilesBefore = tiles.Count;
                colLog.Add((false, "행 전멸", org.X, org.Y, colW));

                // 이 열의 데이라잇 상한 — 원지반보다 위로는 벽이 없다.
                double CapAt(double fu)
                {
                    if (ground == null) return faceH;
                    double f = fa + (fb - fa) * System.Math.Clamp(fu, 0, 1);
                    var lf = LocOfFrac(cumC, f);
                    var c0 = AtLoc(crest, lf.Lo, lf.T);
                    var t0 = pairByIndex ? AtLoc(toe, lf.Lo, lf.T) : AtFrac(toe, cumT, f);

                    // ★[치명 0805] **성토는 데이라잇으로 자르지 않는다**(JACK 0721 확정 — 보강토와 동일 규칙):
                    //   크레스트가 지반 위면 벽이 꽉 차고, 아니면 벽이 없다.
                    //   아래 절토 규칙("설계면이 원지반보다 아래일 때 벽")과 **부호가 정반대**라, 방향을 안 가르면
                    //   성토 벽은 토우가 지반 위라 전부 '벽 없음'이 되어 **판넬이 한 장도 안 나온다**.
                    //   옛 구현(WallPanels.DayS)에는 있던 분기인데 재작성 때 빠졌다.
                    if (!run.Up)
                    {
                        if (!ground.TryGetElevation(c0.X, c0.Y, out double gc)) return faceH;
                        return c0.Z > gc + 0.02 ? faceH : 0;   // 여유 0.02는 옛 값(eps) — 1e-6은 표본 잡음에 흔들린다
                    }

                    if (!ground.TryGetElevation(t0.X, t0.Y, out double gz0)) return -1;   // 지반 밖
                    if (t0.Z >= gz0 - 1e-6) return 0;            // 토우가 이미 지반 위 — 벽 없음
                    if (!ground.TryGetElevation(c0.X, c0.Y, out double gz1)) return -1;
                    if (c0.Z <= gz1 + 1e-6) return faceH;        // 크레스트도 지반 아래 — 꽉 참
                    // 토우~크레스트 사이에서 지반과 만나는 지점(선형 보간).
                    double d0 = gz0 - t0.Z, d1 = gz1 - c0.Z;     // d0>0, d1<0
                    double r = d0 / (d0 - d1);
                    return System.Math.Clamp(r, 0, 1) * faceH;
                }

                // ★[0805 JACK '데이라잇에 끊긴 객체가 깔끔하지 않고 삐죽 나옴'] 상한을 **촘촘히 표본**해
                //   실루엣을 따라간다. 종전엔 열 양 끝 2점으로 사다리꼴을 만들어, 지반이 열 안에서 휘면
                //   실제 데이라잇선과 어긋나 실오라기가 삐져나왔다.
                double uL = jm - lapA, uR = colW - jm + lapB;
                if (uR - uL < 0.05) { dJoint++; colLog[logIdx] = (false, "줄눈", org.X, org.Y, colW); continue; }
                int NS = System.Math.Max(2, (int)System.Math.Ceiling((uR - uL) / 0.15));
                var capS = new double[NS + 1];
                bool anyGnd = false, anyCap = false;
                for (int t = 0; t <= NS; t++)
                {
                    double c = CapAt((uL + (uR - uL) * t / NS) / colW);
                    if (c >= 0) anyGnd = true;
                    capS[t] = System.Math.Clamp(c, 0, faceH);
                    if (capS[t] > 1e-6) anyCap = true;
                }
                if (!anyGnd) { dGround++; colLog[logIdx] = (false, "지반밖", org.X, org.Y, colW); continue; }
                if (!anyCap)
                {
                    dAbove++;
                    colLog[logIdx] = (false, "지반위", org.X, org.Y, colW);
                    // ★[0806] '지반위라 버림'이 **진짜인지** 잰다 — 토우가 원지반보다 얼마나 높은가.
                    //   수 m~수십 m면 옹벽선이 데이라잇 위까지 뻗은 것(정상, 그 위엔 팔 흙이 없다).
                    //   수 cm면 판정이 표본 잡음에 흔들린 것(버그) — 두 경우는 개수만 봐선 절대 안 갈린다.
                    //   현장 v19.25에서 12줄 중 10줄이 통째로 이 가지로 사라졌는데, 개수(926)만으론
                    //   정상인지 알 수 없어 이 숫자를 만들었다.
                    if (ground != null)
                    {
                        double fm = fa + (fb - fa) * 0.5;
                        var lm = LocOfFrac(cumC, fm);
                        var tm = pairByIndex ? AtLoc(toe, lm.Lo, lm.T) : AtFrac(toe, cumT, fm);
                        if (ground.TryGetElevation(tm.X, tm.Y, out double gm))
                        {
                            double gap = tm.Z - gm;
                            if (aboveN == 0 || gap < aboveMin) aboveMin = gap;
                            if (aboveN == 0 || gap > aboveMax) { aboveMax = gap; aboveX = tm.X; aboveY = tm.Y; }
                            aboveN++;
                        }
                    }
                    continue;
                }

                // ★행 수는 **설계 규칙에서 직접** 정한다(단높이 ≤1m→1행 / ≤3m→2행 / 그 이상→3행).
                //   `ceil(경사길이 ÷ 한변)`으로 구하면 안 된다 — 벽이 1:0.05로 살짝 기울어 **경사길이가
                //   수직높이보다 조금 길기 때문에**(5m 단이면 4.996 vs 4.99) 4.996÷1.663 = 3.004가 올림되어
                //   3행이 **4행**이 되고, 행 높이가 1.67m → 1.25m로 낮아진다.
                //   그러면 판넬이 정사각이 아니게 되고, 정착구 보호구역(0.66m)이 판넬 높이의 절반을 넘어
                //   **가운데 세로줄의 자연석이 통째로 제외된다**(JACK 0805 '돌무늬가 생기다 말았다').
                int nrow = RowsFor(height);
                //   단높이가 설계 상한을 넘는 예외 상황(구간마다 옹벽/사면이 섞여 평균 Z가 튀는 자리)에서는
                //   한 변이 상한을 넘지 않도록 행을 더 쪼갠다 — 거대 판넬 방지(v18.0 교훈).
                //   ※상한 비교도 **수직 높이**로 해야 한다 — 경사길이로 재면 5m 단이 5.006m라
                //     5.006÷1.667 = 3.004가 올림돼 방금 고친 4행이 그대로 되살아난다.
                //   ★[치명 0805] 그리고 **여유 0.5m**가 필요하다. 링 평균 Z는 완화 정점 때문에 설계 단높이보다
                //     수 mm~수 cm 크게 나온다(v18.0 실측 5.0002m). 여유 없이 걸면 5.0002m 부지에서 3행이 4행이 되고,
                //     행 높이가 1.67→1.25m로 낮아져 정착구 보호구역이 판넬 높이의 절반을 넘어
                //     **가운데 자연석이 사라진다**(JACK 0805 '돌무늬가 생기다 말았다'와 같은 증상).
                //     옛 구현도 같은 자리에 0.5m 여유를 뒀다(WallPanels: stepLimit 5.5).
                const double heightSlack = 0.5;
                nrow = System.Math.Max(nrow, (int)System.Math.Ceiling((height - heightSlack) / MaxSide - 1e-9));
                double rowH = faceH / nrow;

                // ★[JACK 0805 '위에 패널이 있는데도 아래패널이 비스듬히 잘려버림'] 맨 위 행 처리.
                //   데이라잇이 행 경계 **바로 위**에 걸리면 그 행의 조각이 몇 cm짜리가 되어 실오라기 필터에
                //   통째로 걸린다. 그러면 그 열의 벽이 **한 행만큼 뚝 낮아져** 옆 열과 어긋나고,
                //   화면에선 삼각형 구멍처럼 보인다. 버리지 말고 **아래 행에 합쳐** 그 행의 윗변이
                //   데이라잇을 그대로 따라가게 한다(실제 옹벽도 맨 윗단을 잘라 맞춘다).
                double capTop = 0;
                foreach (var cs in capS) capTop = System.Math.Max(capTop, cs);
                int topRow = (int)System.Math.Floor((capTop - 1e-9) / rowH);
                if (topRow > nrow - 1) topRow = nrow - 1;
                if (topRow < 0) topRow = 0;
                const double minTopRow = 0.25;                       // 이보다 얇은 맨 윗행은 아래에 합친다
                if (capTop - topRow * rowH < minTopRow && topRow > 0) topRow--;

                // [진단 0805 — JACK '위에 패널이 있는데도 아래패널이 잘림'] 이 열에서 **실제로 만들어진**
                //   판넬이 데이라잇까지 올라왔는지 잰다. 조각이 버려지면 그 열만 주저앉아 옆 열과 어긋나 보인다.
                double colMaxV = 0;
                var colSpans = new List<(double Lo, double Hi)>();   // 이 열이 실제로 덮은 v 구간들

                for (int i = 0; i <= topRow; i++)
                {
                    rowN++;
                    double s0 = i * rowH;
                    //   맨 위 행은 데이라잇까지만(그 위는 흙이 없다). 아래 행들은 설계 높이 그대로.
                    double s1 = i == topRow ? System.Math.Min(faceH, System.Math.Max(capTop, s0 + minTopRow))
                                            : (i + 1) * rowH;
                    double v0 = s0 + jm, v1 = s1 - jm;
                    if (v1 - v0 < 0.05) { dJoint++; continue; }

                    // 표본마다 이 행의 윗변 높이 — 상한에 걸리면 그만큼 낮아진다.
                    var topV = new double[NS + 1];
                    for (int t = 0; t <= NS; t++) topV[t] = System.Math.Min(v1, capS[t] - jm);

                    // 윗변이 아랫변보다 높은 **연속 구간**마다 조각을 하나씩 만든다 —
                    //   상한이 열 한가운데를 가로지르면 조각이 나뉘는 게 맞고, 억지로 한 장으로 만들면 삐죽 나온다.
                    int t0 = 0;
                    while (t0 <= NS)
                    {
                        if (topV[t0] <= v0 + 1e-6) { t0++; continue; }
                        int t1 = t0;
                        while (t1 + 1 <= NS && topV[t1 + 1] > v0 + 1e-6) t1++;

                        // ★[JACK 0805 '여전히 4각형으로 잘리는 게 있다'] 윗변을 오목하게 만드는 건 오직
                        //   **골짜기(국소 최소)** 뿐이다 — 봉우리는 볼록을 유지한다(외적 부호로 확인).
                        //   골짜기에서 조각을 나누면 각 조각의 윗변이 단조로워 **전부 볼록**해지고,
                        //   사다리꼴로 물러날 일이 없어 귀퉁이만 잘린 5각/6각이 그대로 살아난다.
                        //   ※'양옆보다 **엄격히** 낮은 점'만 찾으면 **바닥이 평평한 골짜기**(같은 값 두 개 이상)를
                        //     놓친다 — 현장 실측이 그 경우였다(정점 7개짜리 오목, 골짜기 u 0.641 v 0.186).
                        //     구간 안의 **가장 낮은 점**을 찾아 양 끝보다 낮으면 거기서 나눈다(평평해도 잡힌다).
                        bool dipEnd = false;
                        if (t1 - t0 >= 2)
                        {
                            int tm = t0 + 1; double vm = topV[tm];
                            for (int t = t0 + 1; t < t1; t++) if (topV[t] < vm) { vm = topV[t]; tm = t; }
                            if (vm < topV[t0] - 1e-9 && vm < topV[t1] - 1e-9) { t1 = tm; dipEnd = true; }
                        }

                        double stepU = (uR - uL) / NS;
                        // 조각의 양 끝은 상한이 v0을 지나는 **정확한 위치**까지 늘린다(계단 모양 방지).
                        double ua = uL + t0 * stepU, ub = uL + t1 * stepU;
                        if (t0 > 0)
                        {
                            double a0 = topV[t0 - 1], a1 = topV[t0];
                            double r0 = (v0 - a0) / System.Math.Max(a1 - a0, 1e-9);
                            ua = uL + (t0 - 1 + System.Math.Clamp(r0, 0, 1)) * stepU;
                        }
                        //   ※골짜기에서 끊은 경우(dipEnd)는 윗변이 v0을 지나는 게 아니라 그냥 나눈 것이라
                        //     보간하면 안 된다 — 그 자리 그대로 끝나고, **다음 조각이 같은 점에서 시작**해 이어진다.
                        if (t1 < NS && !dipEnd)
                        {
                            double b0 = topV[t1], b1 = topV[t1 + 1];
                            double r1 = (v0 - b0) / System.Math.Min(b1 - b0, -1e-9);
                            ub = uL + (t1 + System.Math.Clamp(r1, 0, 1)) * stepU;
                        }
                        int tNext = dipEnd ? t1 : t1 + 1;

                        // ★[JACK 0805] 윗변을 **실루엣 그대로** 따라간다 — 데이라잇은 판넬의 **귀퉁이만** 잘라야 하고,
                        //   그러면 5각·6각이 나오는 게 옳다. 종전엔 볼록성을 지키려고 윗변을 양 끝 직선 하나로
                        //   퉁쳐서 **잘리는 지점부터 다음 꼭지점까지 통째로** 날아갔고, 결과가 항상 사각형이 됐다
                        //   (JACK: '귀퉁이만 잘려야 되는데 항상 4각형으로만 만들어지네').
                        //   ※볼록성은 여전히 필요하다(자연석 무늬 클립이 볼록한 창에서만 옳다 — 115094).
                        //     다행히 `min(행 꼭대기, 데이라잇)` 윗변은 데이라잇이 이 열에서 단조로우면 **볼록**하다.
                        //     지반은 삼각망이라 1.6m 폭에서는 사실상 직선이므로 정상 케이스는 전부 볼록.
                        //     혹시 오목해지면(지반이 열 안에서 꺾이는 드문 경우) 옛 사다리꼴로 물러나고 세어 둔다.
                        var local = new List<(double u, double v)>(NS + 4) { (ua, v0), (ub, v0) };
                        for (int t = t1; t >= t0; t--) local.Add((uL + t * stepU, topV[t]));
                        t0 = tNext;
                        local = Simplify(local);          // 공선점 제거 → 곧은 구간은 2점으로 줄어 5각/6각이 된다
                        if (local.Count < 3) { dThin++; continue; }
                        // ※[0806 폐기] 사다리꼴 변환(아래=토우·위=크레스트)은 기하는 맞지만 **판넬 옆면이 비스듬**해져
                        //   실물 앵커판넬(직사각형·수직 줄눈)과 달라 보인다(JACK '사선으로 쪼개졌어'). 폭을 토우로 잡는 쪽으로 대체.
                        // ★[JACK 0805 '딱 이 부분만 사선으로 잘려'] 오목하다고 **사다리꼴로 물러나면 안 된다** —
                        //   물러나는 순간 그 판넬만 '잘리는 지점부터 다음 꼭지점까지' 통째로 날아가 긴 사선이 된다.
                        //   판넬 **모양은 언제나 실루엣 그대로**가 옳다. 오목해도 솔리드 압출은 문제없다.
                        //   볼록성이 필요한 건 **자연석 무늬 클립뿐**이므로(볼록 창에서만 옳음 — 115094),
                        //   그건 무늬를 만드는 쪽에서 건너뛰게 한다(WallPanelDwg). 여기서는 세기만 한다.
                        if (!IsConvex(local))
                        {
                            nonConvex++;
                            // [진단 0805] 골짜기에서 나눴는데도 오목이 12장 그대로였다 — **왜 오목인지**를 남긴다.
                            //   정점 수와 오목한 자리의 (u,v)가 나오면 원인이 윗변인지 다른 데인지 갈린다.
                            if (firstConcave.Length == 0)
                            {
                                int bad = -1;
                                for (int q = 0; q < local.Count; q++)
                                {
                                    var a2 = local[q]; var b2 = local[(q + 1) % local.Count]; var c2 = local[(q + 2) % local.Count];
                                    double cr2 = (b2.u - a2.u) * (c2.v - b2.v) - (b2.v - a2.v) * (c2.u - b2.u);
                                    if (cr2 < -1e-9) { bad = (q + 1) % local.Count; break; }
                                }
                                firstConcave = bad >= 0
                                    ? $"정점 {local.Count}개 중 {bad}번(u {local[bad].u:F3} v {local[bad].v:F3})" +
                                      $" · 열폭 {colW:F2} 행 [{v0:F2}..{v1:F2}] 겹침 {lapA:F2}/{lapB:F2}"
                                    : $"정점 {local.Count}개(부호 판정 실패)";
                            }
                            // ※여기서 **모양을 바꾸지 않는다.** 실루엣 그대로 내보낸다.
                        }

                        double u0 = ua, u1 = ub;

                        // ★실오라기 제거 — 데이라잇에 잘리고 남은 조각이 너무 얇거나 작으면 만들지 않는다.
                        //   이게 없으면 벽이 사면으로 사그라드는 끝단에서 바늘 같은 조각이 삐죽 나온다(JACK 지적).
                        double pArea = 0;
                        for (int q = 0; q < local.Count; q++)
                        {
                            var pA = local[q]; var pB = local[(q + 1) % local.Count];
                            pArea += pA.u * pB.v - pB.u * pA.v;
                        }
                        pArea = System.Math.Abs(pArea) / 2;
                        double mnV = double.MaxValue, mxV = double.MinValue;
                        foreach (var (lu2, lv2) in local) { mnV = System.Math.Min(mnV, lv2); mxV = System.Math.Max(mxV, lv2); }
                        if (pArea < SliverArea || (u1 - u0) < SliverEdge || (mxV - mnV) < SliverEdge)
                        {
                            dSliver++;
                            // [0806 JACK '살짝 누락부가 보인다'] 버린 실오라기는 **그 자리에 구멍**으로 남는다.
                            //   개수만으론 눈에 보이는 구멍인지 안 보이는 티끌인지 알 수 없다 — 크기와 좌표를 남긴다.
                            if (sliverFirst.Length == 0)
                                sliverFirst = $"{u1 - u0:F2}×{mxV - mnV:F2}m {pArea:F4}㎡ @ {org.X:F0},{org.Y:F0}";
                            continue;
                        }

                        // '온전'(=앵커·정착구를 다는 판넬)의 뜻: **데이라잇에 안 잘린 완전한 사각**이고
                        //   가운데 정착구(도넛 0.56m)를 물 만큼 크다는 것.
                        //   ※ 열 폭이 상한(side)과 같아야 한다는 식으로 판정하면 안 된다 — 균등 분배라 열 폭은
                        //     거의 항상 상한보다 조금 작아서(예 1.553 < 1.667) **온전이 하나도 안 나오고 앵커가
                        //     통째로 사라진다**(첫 구현에서 실제로 온전 0장이었다).
                        // ★[JACK 0805 '앵커보호공 데이라잇에 안 잘림'] — **v13.9에서 이미 고쳤던 검사를 되살린다.**
                        //   옛 WallPanels에는 '도넛 네 모서리가 판넬 안에 들어올 때만 온전'이라는 검사가 있었는데
                        //   (v13.9: 판정 반경 0.1 → 0.30, 네 모서리 검사), v19.0에서 옹벽을 새로 짜면서
                        //   그 검사를 가져오지 않았다. 대신 쓴 '위쪽이 꼭대기에 닿으면 온전'은 **한쪽만 닿아도**
                        //   통과해서, 데이라잇에 비스듬히 잘린 판넬에 도넛·앵커가 달려 지반 밖으로 삐져나왔다.
                        //   ※교훈: 새로 짜면 옛 코드에 쌓인 수정이 **자동으로 따라오지 않는다** — 하나씩 옮겨야 한다.
                        const double collarHalf = 0.30;   // 도넛 1단 0.56/2 = 0.28 + 여유 0.02 (v13.9 실측값)
                        double pcu = (u0 + u1) / 2, pcv = (v0 + v1) / 2;
                        bool isFull =
                            (u1 - u0) >= collarHalf * 2 + 0.2 && (v1 - v0) >= collarHalf * 2 + 0.2
                            && PointInPoly(pcu, pcv, local)
                            && PointInPoly(pcu - collarHalf, pcv - collarHalf, local)
                            && PointInPoly(pcu + collarHalf, pcv - collarHalf, local)
                            && PointInPoly(pcu - collarHalf, pcv + collarHalf, local)
                            && PointInPoly(pcu + collarHalf, pcv + collarHalf, local);
                        if (isFull) full++;

                        var poly = new List<Point3>(local.Count);
                        foreach (var (lu, lv) in local)
                            poly.Add(new Point3(org.X + lu * ux + lv * vx,
                                                org.Y + lu * uy + lv * vy,
                                                org.Z + lv * vz));
                        double pu = (u0 + u1) / 2, pv = (v0 + v1) / 2;
                        tiles.Add(new Tile(poly, isFull, org, (ux, uy, 0), (vx, vy, vz), (wx, wy, wz),
                            local, pu, pv, run.Bench, run.Up, i));
                        if (mxV > colMaxV) colMaxV = mxV;
                        colSpans.Add((mnV, mxV));
                        // 이 판넬 아랫변 중점이 옹벽선(토우) 위에 있는지 — 어긋나면 배치가 잘못된 것이다.
                        if (i == 0)
                        {
                            double bu = (u0 + u1) / 2;
                            CheckOnLine(new Point3(org.X + bu * ux + v0 * vx, org.Y + bu * uy + v0 * vy, 0));
                        }
                    }
                }

                if (tiles.Count > tilesBefore) colLog[logIdx] = (true, "", org.X, org.Y, colW);

                // ── 이 열의 결산: 판넬 잘림 증상을 **가설별로 갈라내는 숫자** 세 가지 ──
                //   ① 데이라잇(capTop)까지 못 올라왔나 — 조각이 버려져 주저앉은 경우
                double shortBy = capTop - colMaxV - jm;
                if (shortBy > 0.30)
                {
                    colShort++;
                    if (shortBy > maxShort) { maxShort = shortBy; shortX = org.X; shortY = org.Y; }
                }

                //   ② **상한 자체가 틀렸나** — 데이라잇에 잘린 열인데 벽 꼭대기가 실제 지반에서 멀리 떨어져 있으면
                //      cap 계산이 낮게 나온 것이다(①은 cap 기준이라 cap이 틀리면 통과해 버린다).
                //      벽이 크레스트까지 꽉 찬 열(capTop ≥ faceH)은 지반이 그 위에 있는 게 정상이라 제외.
                if (ground != null && capTop < faceH - 1e-6 && colMaxV > 1e-6)
                {
                    double tx = org.X + (colW / 2) * ux + colMaxV * vx;
                    double ty = org.Y + (colW / 2) * uy + colMaxV * vy;
                    double tz = org.Z + colMaxV * vz;
                    if (ground.TryGetElevation(tx, ty, out double gzTop))
                    {
                        double d = gzTop - tz;
                        if (d > 0.35)
                        {
                            capOff++;
                            if (d > maxCapOff) { maxCapOff = d; capOffX = tx; capOffY = ty; }
                        }
                    }
                }

                //   ③ **열 중간에 구멍이 났나** — ①은 꼭대기만 보므로 중간이 비어도 통과한다.
                if (colSpans.Count > 1)
                {
                    colSpans.Sort((p, q) => p.Lo.CompareTo(q.Lo));
                    for (int s = 0; s + 1 < colSpans.Count; s++)
                    {
                        double hole = colSpans[s + 1].Lo - colSpans[s].Hi;
                        if (hole > 2 * jm + 0.15)
                        {
                            colHole++;
                            if (hole > maxHole) { maxHole = hole; holeX = org.X; holeY = org.Y; }
                            break;
                        }
                    }
                }
            }
        }

        // ★[0806] 벽 **한가운데** 구멍만 골라낸다 — 양옆에 판넬이 있는데 가운데만 빈 열의 연속 구간.
        //   끝쪽이 비는 건 데이라잇이라 정상이므로, 첫 판넬 앞·마지막 판넬 뒤는 보지 않는다.
        int firstMade = colLog.FindIndex(x => x.Made), lastMade = colLog.FindLastIndex(x => x.Made);
        double midHoleW = 0; string midHoleWhy = ""; double midHoleX = 0, midHoleY = 0; int midHoleN = 0;
        if (firstMade >= 0)
            for (int i = firstMade + 1; i < lastMade; i++)
            {
                if (colLog[i].Made) continue;
                int j2 = i; double w = 0;
                while (j2 < lastMade && !colLog[j2].Made) { w += colLog[j2].W; j2++; }
                midHoleN++;
                if (w > midHoleW) { midHoleW = w; midHoleWhy = colLog[i].Why; midHoleX = colLog[i].X; midHoleY = colLog[i].Y; }
                i = j2;
            }

        LastDiag = $"판넬 {tiles.Count}(온전 {full}) · 벽면 {runs.Count} · 열 {colN} · 행 {rowN}" +
                   $" · 한변 {side:F2}m · 높이 {height:F2}m" +
                   $" · 버림(지반밖 {dGround} · 지반위 {dAbove} · 줄눈 {dJoint} · 퇴화 {dThin} · 실오라기 {dSliver})" +
                   (colShort > 0 ? $" · ⚠데이라잇 못 미친 열 {colShort}개(최대 {maxShort:F2}m @ {shortX:F0},{shortY:F0})" : "") +
                   (capOff > 0 ? $" · ⚠상한이 지반보다 낮은 열 {capOff}개(최대 {maxCapOff:F2}m @ {capOffX:F0},{capOffY:F0})" : "") +
                   (colHole > 0 ? $" · ⚠열 중간 구멍 {colHole}개(최대 {maxHole:F2}m @ {holeX:F0},{holeY:F0})" : "") +
                   (colShort + capOff + colHole == 0 ? " · 열 검사 이상 없음" : "") +
                   (nonConvex > 0 ? $" · 오목 윗변 {nonConvex}장(모양 정확 · 무늬는 볼록 분해로 채움)" : "") +
                   // ★[0806 JACK '단높이가 2.5·3m로 바뀌어도 괜찮은지'] 온전 판넬이 0장이면 **앵커도 도넛도 안 달린다**.
                   //   판넬이 0.8m 미만이면 도넛(0.56m)이 안 들어가서 온전 판정이 안 난다(v13.9 규칙).
                   //   단높이 1.0~1.7m 구간이 여기 걸린다(2행 × 0.5~0.85m). 숫자로만 보면 판넬은 멀쩡히 나오므로
                   //   말해주지 않으면 '앵커 없는 앵커판넬 옹벽'이 조용히 나간다.
                   (tiles.Count > 0 && full == 0
                       ? $" · ⚠온전 판넬 0장 — 앵커·정착구가 하나도 안 달린다(판넬 {side:F2}m < 0.80m, 단높이 {height:F2}m)" : "") +
                   $" · 판넬↔옹벽선 최대 이탈 {offLine:F3}m @ {offX:F0},{offY:F0}" +
                   (offN > 0 ? $"(0.35m 초과 {offN}장)" : "") +
                   (faceCnt > 0
                       ? $" · 열폭 {minColW:F2}~{maxColW:F2}m(규격 {side:F2}m · 벽면 {faceCnt}개" +
                         (narrowN > 0 ? $" · 규격 미만 {narrowN}개(끝 자투리+급커브)" : " · 전부 규격") +
                         (chordSplit > 0 ? $" · 급커브 분할 {chordSplit}열(안 쪼갰다면 이탈 {noSplitDev:F3}m · 한도 {ChordTol:F2}m)" : "") +
                         $" · 최소 @ {narrowX:F0},{narrowY:F0})" : "") +
                   (sliverFirst.Length > 0 ? $" · 실오라기 구멍 첫 사례 {sliverFirst}" : "") +
                   (toeLong > 0 ? $" · 토우가 더 긴 열 {toeLong}개(최대 +{toeLongMax:F2}m — 그만큼 판넬을 늘려 덮음)" : "") +
                   (midHoleN > 0 ? $" · ⚠벽 한가운데 구멍 {midHoleN}곳(최대 {midHoleW:F2}m 폭 · 사유 {midHoleWhy} @ {midHoleX:F0},{midHoleY:F0})" : "") +
                   $" · ★이탈 코너별(오목 {offCav:F3}m @ {offCavX:F0},{offCavY:F0} · 볼록 {offCnv:F3}m · 코너밖 {offFar:F3}m)" +
                   (facetCav + facetCnv > 0
                       ? $" · 코너 조각(규격 미만 벽면) 오목 {facetCav}개/볼록 {facetCnv}개 · 최단 {facetMin:F2}m({(facetCav2 ? "오목" : "볼록")}) @ {facetX:F0},{facetY:F0}"
                       : " · 코너 조각 없음") +
                   (firstConcave.Length > 0 ? $" · 오목 첫 사례 {firstConcave}" : "");

        // [0806] 줄마다 '남긴 판넬/지반위로 버린 판넬/토우가 지반 위로 뜬 최소 거리'를 남긴다.
        //   판넬이 0장인 줄은 **그 자체로는 이상이 아니다** — JACK 확인(0806): 이 현장 옹벽은 설계상
        //   맨 아래 두 단에만 있고, 위 단들의 옹벽선은 데이라잇 위(팔 흙이 없는 자리)를 지난다.
        //   그러니 '0장'이 아니라 **뜬 거리가 작은데 0장인 것**만 경고해야 한다 —
        //   정상에서 매번 울리는 경고는 진짜가 울릴 때 같이 무시당한다.
        tPerLine.Add((tiles.Count, dAbove, aboveN > 0 ? aboveMin : double.NaN));
        if (faceCnt > 0)
        {
            if (minColW < tMinColW) { tMinColW = minColW; tNarrowX = narrowX; tNarrowY = narrowY; tNarrowLen = narrowLen; tNarrowDiv = narrowN2; }
            if (maxColW > tMaxColW) tMaxColW = maxColW;
            tNarrowN += narrowN; tFaceCnt += faceCnt; tChordSplit += chordSplit;
            if (noSplitDev > tNoSplitDev) tNoSplitDev = noSplitDev;
        }
        if (sliverFirst.Length > 0 && tSliverFirst.Length == 0) tSliverFirst = sliverFirst;
        tHoleN += midHoleN;
        if (midHoleW > tHoleW) { tHoleW = midHoleW; tHoleWhy = midHoleWhy; tHoleX = midHoleX; tHoleY = midHoleY; }
        if (aboveN > 0 && (tAboveN == 0 || aboveMax > tAboveMax)) { tAboveMax = aboveMax; tAboveX = aboveX; tAboveY = aboveY; }
        if (aboveN > 0 && (tAboveN == 0 || aboveMin < tAboveMin)) tAboveMin = aboveMin;
        tAboveN += aboveN;
        tCall++; tTile += tiles.Count; tFull += full; tNonConvex += nonConvex;
        tGround += dGround; tAbove += dAbove; tJoint += dJoint; tThin += dThin; tSliver += dSliver;
        tShort += colShort; tCap += capOff; tHole += colHole;
        if (offLine > tOff) { tOff = offLine; tOffX = offX; tOffY = offY; }
        if (offCav > tOffCav) { tOffCav = offCav; tOffCavX = offCavX; tOffCavY = offCavY; }
        if (offCnv > tOffCnv) tOffCnv = offCnv;
        if (offFar > tOffFar) tOffFar = offFar;
        tFacetCav += facetCav; tFacetCnv += facetCnv;
        if (facetMin < tFacetMin) { tFacetMin = facetMin; tFacetX = facetX; tFacetY = facetY; }
        return tiles;
    }

    // ── [0806 중간-4] 옹벽선이 12줄이면 Slice()도 12번 불리는데 로그엔 **첫 줄만** 남았다.
    //    나머지 11줄에서 판넬이 몇 장 버려졌는지·경고가 떴는지 볼 수 없어, '무늬없음 25'처럼
    //    전체 규모가 걸린 문제를 첫 줄 숫자로 어림잡게 만들었다. 줄마다 누적해 전체를 찍는다.
    private static int tCall, tTile, tFull, tNonConvex, tGround, tAbove, tJoint, tThin, tSliver, tShort, tCap, tHole;
    private static double tOff, tOffX, tOffY;
    private static readonly List<(int Kept, int Above, double Gap)> tPerLine = new();
    private static int tAboveN; private static double tAboveMin, tAboveMax, tAboveX, tAboveY;
    private static double tMinColW = double.MaxValue, tMaxColW, tNarrowX, tNarrowY, tNarrowLen;
    private static int tNarrowN, tNarrowDiv, tFaceCnt, tChordSplit; private static string tSliverFirst = "";
    private static int tHoleN; private static double tHoleW, tHoleX, tHoleY, tNoSplitDev; private static string tHoleWhy = "";
    /// <summary>[0806] 벽면 경계(코너)의 좌표와 볼록/오목 — 결함이 오목 코너에 몰리는지 세는 데 쓴다.</summary>
    private static readonly List<(double X, double Y, double Z, bool Convex, double Deg)> tCorners = new();
    /// <summary>[0806] 판넬 이탈을 코너 종류별로 모은 전 줄 합계 — 첫 줄만 보면 나머지 44줄을 놓친다(중간-4의 재판).</summary>
    private static double tOffCav, tOffCnv, tOffFar, tOffCavX, tOffCavY;
    private static int tFacetCav, tFacetCnv; private static double tFacetMin = double.MaxValue, tFacetX, tFacetY;

    /// <summary>옹벽선 여러 줄을 자르기 직전에 호출 — 줄별 누적을 초기화한다.</summary>
    public static void ResetTotals()
    {
        tCall = tTile = tFull = tNonConvex = tGround = tAbove = tJoint = tThin = tSliver = tShort = tCap = tHole = 0;
        tOff = tOffX = tOffY = 0; tPerLine.Clear();
        tAboveN = 0; tAboveMin = tAboveMax = tAboveX = tAboveY = 0;
        tMinColW = double.MaxValue; tMaxColW = tNarrowX = tNarrowY = tNarrowLen = 0;
        tNarrowN = tNarrowDiv = tFaceCnt = tChordSplit = 0; tSliverFirst = "";
        tHoleN = 0; tHoleW = tHoleX = tHoleY = tNoSplitDev = 0; tHoleWhy = ""; tCorners.Clear();
        tOffCav = tOffCnv = tOffFar = tOffCavX = tOffCavY = 0;
        tFacetCav = tFacetCnv = 0; tFacetMin = double.MaxValue; tFacetX = tFacetY = 0;
    }

    /// <summary>
    /// [0806 JACK '길게 누락됨' — 계측 3판] 만들어진 판넬만 보고 <b>옆이 뚫린 자리</b>를 찾는다.
    /// <para>
    /// 앞선 두 계측(열 단위 '벽 한가운데 구멍' · 옹벽선 '줄사이 틈')이 모두 '이상 없음'을 냈는데
    /// 현장 구멍은 그대로다 — 즉 <b>구멍이 그 두 틀 어디에도 안 걸린다</b>. 그래서 틀을 버리고
    /// <b>JACK이 보는 것과 같은 방식</b>으로 잰다: 판넬 옆면끼리 맞닿았는가, 안 맞닿았으면 몇 m 벌어졌는가.
    /// 줄·벽면·열 구분 없이 월드 좌표로만 보므로 어떤 경로로 생긴 구멍이든 걸린다.
    /// </para>
    /// 벽이 끝나는 자리도 옆이 뚫려 있으므로, <b>양옆에 판넬이 있는 틈</b>(=마주 보는 짝이 있는 틈)만 센다.
    /// </summary>
    public static string GapReport(IReadOnlyList<Tile> tiles, double minGap = 0.30, double maxGap = 6.0)
    {
        if (tiles == null || tiles.Count == 0) return "";
        // 판넬마다 좌·우 옆면의 월드 중점(행 중앙 높이).
        var L = new List<(double X, double Y, double Z, int I)>(tiles.Count);
        var R = new List<(double X, double Y, double Z, int I)>(tiles.Count);
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var (u, v) in t.Local) { u0 = System.Math.Min(u0, u); u1 = System.Math.Max(u1, u); v0 = System.Math.Min(v0, v); v1 = System.Math.Max(v1, v); }
            double vm = (v0 + v1) / 2;
            L.Add((t.Origin.X + u0 * t.UAxis.x + vm * t.VAxis.x, t.Origin.Y + u0 * t.UAxis.y + vm * t.VAxis.y, t.Origin.Z + vm * t.VAxis.z, i));
            R.Add((t.Origin.X + u1 * t.UAxis.x + vm * t.VAxis.x, t.Origin.Y + u1 * t.UAxis.y + vm * t.VAxis.y, t.Origin.Z + vm * t.VAxis.z, i));
        }
        // 왼쪽 옆면마다 '마주 보는 오른쪽 옆면' 중 가장 가까운 것까지의 거리 = 그 자리 틈.
        //   [0806 v2] 최대값 하나만으로는 **데이라잇에서 벽이 끝나 생긴 정상 틈**과 진짜 구멍이 안 갈린다.
        //   틈마다 양옆 판넬이 **온전(데이라잇에 안 잘린 완전한 사각)**인지 함께 본다 —
        //   양옆이 다 온전한데 벌어져 있으면 데이라잇 탓이 아니라 **빠진 것**이다.
        var found = new List<(double D, double X, double Y, double Z, bool FullBoth)>();
        for (int i = 0; i < L.Count; i++)
        {
            // ★[0806 v6] 자를 다섯 번째로 고친다. v5(방향 필터)가 왜 틀렸는지 —
            //   직각 코너에서 이웃 벽면 판넬은 내 끝점을 **가로질러** 있어서 그 중심이 진행 방향(+U) 쪽에 놓인다.
            //   방향으로 거르면 **바로 그 이웃이 제외**되고, 노치 건너편 판넬을 짝으로 잡아 3.61m를 지어냈다
            //   (실측: 노치 옆면은 Y 10.18~20.32로 코너를 지나 연속인데 '구멍'으로 찍혔다).
            //   → 옳은 방식: 끝점이 **어느 방향으로든** 다른 판넬 몸통에 닿아 있으면 '막힌 끝'이고,
            //     아무 데도 안 닿으면 '열린 끝'이다. 그리고 **열린 끝 둘이 마주 볼 때만** 구멍이다 —
            //     벽이 데이라잇에서 끝나는 자리는 열린 끝이 하나뿐이라 자연히 빠진다.
            var Lt = tiles[L[i].I];
            double best = double.MaxValue; int bestJ = -1;
            for (int j = 0; j < tiles.Count; j++)
            {
                if (j == L[i].I) continue;
                var Rt = tiles[j];
                if (Rt.Bench != Lt.Bench || Rt.Row != Lt.Row || Rt.Up != Lt.Up) continue;   // 같은 단·같은 행끼리만
                double d = PtSegDist2D(L[i].X, L[i].Y, L[j].X, L[j].Y, R[j].X, R[j].Y);
                if (d < best) { best = d; bestJ = j; }
            }
            if (best < minGap) continue;                 // 어딘가에 닿아 있다 = 막힌 끝(정상)
            // 열린 끝 — 마주 보는 열린 끝(다른 판넬의 오른쪽 끝)을 찾는다.
            best = double.MaxValue; bestJ = -1;
            for (int j = 0; j < tiles.Count; j++)
            {
                if (j == L[i].I) continue;
                var Rt = tiles[j];
                if (Rt.Bench != Lt.Bench || Rt.Row != Lt.Row || Rt.Up != Lt.Up) continue;
                double rOpen = double.MaxValue;
                for (int k = 0; k < tiles.Count; k++)
                {
                    if (k == j) continue;
                    var Kt = tiles[k];
                    if (Kt.Bench != Rt.Bench || Kt.Row != Rt.Row || Kt.Up != Rt.Up) continue;
                    double dd = PtSegDist2D(R[j].X, R[j].Y, L[k].X, L[k].Y, R[k].X, R[k].Y);
                    if (dd < rOpen) rOpen = dd;
                }
                if (rOpen < minGap) continue;            // 저쪽 끝은 막혀 있다 = 마주 보는 열린 끝이 아니다
                double d2 = System.Math.Sqrt((R[j].X - L[i].X) * (R[j].X - L[i].X) + (R[j].Y - L[i].Y) * (R[j].Y - L[i].Y));
                if (d2 < best) { best = d2; bestJ = j; }
            }
            if (bestJ < 0 || best < minGap || best > maxGap) continue;   // 붙었거나(정상) 벽 끝(짝 없음)
            found.Add((best, L[i].X, L[i].Y, L[i].Z,
                       tiles[L[i].I].IsFull && tiles[R[bestJ].I].IsFull));
        }
        // ★[0806 JACK '오목부에서 자꾸 오류' — 심증을 숫자로 확정] 틈마다 **가장 가까운 코너와 그 종류**를 붙인다.
        //   오목 코너에 몰리면 원인이 코너 처리(겹침·오프셋)이고, 골고루 흩어져 있으면 다른 원인이다.
        //   이 한 줄이 '오목부가 문제다'를 확정하거나 기각한다.
        int convN = 0, cavN = 0;
        foreach (var c in tCorners) { if (c.Convex) convN++; else cavN++; }
        int nearCav = 0, nearConv = 0, farAll = 0;
        foreach (var g in found)
        {
            double best = double.MaxValue; bool bestConv = false;
            foreach (var c in tCorners)
            {
                if (System.Math.Abs(c.Z - g.Z) > 6.0) continue;             // 같은 단 근처만
                double dx = c.X - g.X, dy = c.Y - g.Y;
                double d = System.Math.Sqrt(dx * dx + dy * dy);
                if (d < best) { best = d; bestConv = c.Convex; }
            }
            if (best > 3.0) farAll++;
            else if (bestConv) nearConv++;
            else nearCav++;
        }

        if (found.Count == 0) return $"판넬 옆면 틈 없음(전부 맞닿음) · 코너 볼록 {convN}/오목 {cavN}";
        found.Sort((p, q) => q.D.CompareTo(p.D));
        int realN = found.FindAll(x => x.FullBoth).Count;
        var top = new System.Text.StringBuilder();
        for (int i = 0; i < found.Count && i < 5; i++)
        {
            // 가장 가까운 코너를 **각도까지** 붙인다 — '볼록/오목'은 노출면 기준 이름이라 위에서 본 것과 반대일 수 있으니,
            //   좌표와 각도로 JACK과 같은 자리를 가리키게 한다.
            double cd = double.MaxValue; string ctag = "코너 없음";
            foreach (var c in tCorners)
            {
                if (System.Math.Abs(c.Z - found[i].Z) > 6.0) continue;
                double dx2 = c.X - found[i].X, dy2 = c.Y - found[i].Y;
                double d2 = System.Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                if (d2 < cd) { cd = d2; ctag = $"{(c.Convex ? "볼록" : "오목")}{c.Deg:F0}° {d2:F1}m"; }
            }
            top.Append($" [{found[i].D:F2}m @ {found[i].X:F0},{found[i].Y:F0} Z{found[i].Z:F1}" +
                       $"{(found[i].FullBoth ? " ★양옆 온전" : " 데이라잇 잘림")} · 가까운 코너 {ctag}]");
        }
        return $"⚠★판넬 옆면 틈 {found.Count}곳(그중 양옆이 온전한 진짜 구멍 {realN}곳)" +
               $" · 코너 볼록 {convN}/오목 {cavN} · 틈 위치: 오목코너 3m내 {nearCav} · 볼록코너 3m내 {nearConv} · 코너와 무관 {farAll}" +
               $" — 큰 것부터:{top}";
    }

    /// <summary>[0806] 점에서 선분까지 거리(2D) — 판넬 끝점이 이웃 판넬 몸통에 닿았는지 재는 자.</summary>
    private static double PtSegDist2D(double px, double py, double sx, double sy, double tx, double ty)
    {
        double vx = tx - sx, vy = ty - sy, L2 = vx * vx + vy * vy;
        double t = L2 < 1e-12 ? 0 : System.Math.Clamp(((px - sx) * vx + (py - sy) * vy) / L2, 0, 1);
        double qx = sx + vx * t, qy = sy + vy * t;
        return System.Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
    }

    /// <summary>[0806] 줄별 요약을 짧게 — 판넬이 나온 줄만 나열하고 0장 줄은 개수+뜬거리 범위로 접는다.</summary>
    private static string PerLineBrief()
    {
        var sb = new System.Text.StringBuilder();
        int zeroN = 0; double gLo = double.MaxValue, gHi = double.MinValue;
        foreach (var x in tPerLine)
        {
            if (x.Kept > 0) { sb.Append($"{x.Kept}/{x.Above} "); continue; }
            zeroN++;
            if (double.IsNaN(x.Gap)) continue;
            if (x.Gap < gLo) gLo = x.Gap;
            if (x.Gap > gHi) gHi = x.Gap;
        }
        if (zeroN > 0)
            sb.Append(gHi >= gLo ? $"+ 0장 {zeroN}줄(뜬거리 {gLo:F1}~{gHi:F1}m)" : $"+ 0장 {zeroN}줄");
        return sb.ToString().TrimEnd();
    }

    /// <summary>옹벽선 <b>전 줄</b> 합계 — <see cref="LastDiag"/>(마지막 한 줄)와 달리 전체 규모를 보여준다.</summary>
    public static string TotalDiag =>
        tCall == 0 ? "" :
        $"전체 {tCall}줄 합계 — 판넬 {tTile}(온전 {tFull})" +
        (tTile > 0 && tFull == 0 ? " · ⚠앵커·정착구가 하나도 안 달렸다(판넬이 0.80m 미만 — 단높이 확인)" : "") +
        $" · 버림(지반밖 {tGround} · 지반위 {tAbove} · 줄눈 {tJoint} · 퇴화 {tThin} · 실오라기 {tSliver})" +
        (tNonConvex > 0 ? $" · 오목 윗변 {tNonConvex}장(볼록 분해로 무늬 채움)" : "") +
        (tShort + tCap + tHole > 0
            ? $" · ⚠열 경고(못 미침 {tShort} · 상한낮음 {tCap} · 중간구멍 {tHole})"
            : " · 열 검사 이상 없음") +
        $" · 판넬↔옹벽선 최대 이탈 {tOff:F3}m @ {tOffX:F0},{tOffY:F0}" +
        $" · ★이탈 코너별 전체(오목 {tOffCav:F3}m @ {tOffCavX:F0},{tOffCavY:F0} · 볼록 {tOffCnv:F3}m · 코너밖 {tOffFar:F3}m)" +
        (tFacetCav + tFacetCnv > 0
            ? $" · 코너 조각 오목 {tFacetCav}/볼록 {tFacetCnv} · 최단 {tFacetMin:F2}m @ {tFacetX:F0},{tFacetY:F0}"
            : " · 코너 조각 없음") +
        (tFaceCnt > 0
            ? $" · 열폭 {tMinColW:F2}~{tMaxColW:F2}m(벽면 {tFaceCnt}개" +
              (tNarrowN > 0 ? $" · 규격 미만 {tNarrowN}열(끝 자투리+급커브)" : " · 전부 규격") +
              (tChordSplit > 0 ? $" · 급커브 분할 {tChordSplit}열(안 쪼갰다면 이탈 최대 {tNoSplitDev:F3}m · 한도 {ChordTol:F2}m)" : "") +
              $" · 최소 @ {tNarrowX:F0},{tNarrowY:F0})" : "") +
        (tSliverFirst.Length > 0 ? $" · ⚠실오라기 구멍 첫 사례 {tSliverFirst}" : "") +
        (tHoleN > 0
            ? $" · ⚠★벽 한가운데 구멍 {tHoleN}곳(최대 {tHoleW:F2}m 폭 · 사유 {tHoleWhy} @ {tHoleX:F0},{tHoleY:F0})"
            : " · 벽 한가운데 구멍 없음") +
        // [0806 JACK '로그가 너무 길다'] 45단이면 이 목록만 45칸이다. **판넬이 나온 줄**만 적고
        //   나머지(데이라잇 위라 0장인 정상 줄)는 개수와 뜬거리 범위로 접는다 — 판정에 필요한 정보는 같다.
        $" · 줄별 남김/지반위버림 {PerLineBrief()}" +
        (tAboveN > 0
            ? $" · 지반위 버림 실측: 토우가 원지반보다 {tAboveMin:F2}~{tAboveMax:F2}m 높음(최대 @ {tAboveX:F0},{tAboveY:F0})"
            : "") +
        // ★판넬 0장인 줄 중 **진짜 이상**만 고른다.
        //   [0806 재교정] 처음엔 '뜬 거리 0.5m 미만'으로 걸었더니 현장에서 `0/64(+0.1m)` 줄이 걸렸는데,
        //   그건 데이라잇 **바로 위**를 지나는 줄이라 정상이다 — 토우가 지반 위면 붙잡을 흙이 없어 벽 높이가 0이다.
        //   기준은 거리가 아니라 **부호**여야 한다: 토우가 지반 **아래**(붙잡을 흙이 있다)인데 벽이 0장이면
        //   그때만 사라진 것이다. 5cm는 지반 표본·링 조밀화 잡음(현장 실측 이탈 0.11m)에 대한 여유.
        //   ※NaN = '지반위'로 버린 열이 하나도 없는데 0장 — 다른 사유로 통째로 사라진 것이라 역시 이상하다.
        (tPerLine.FindAll(x => x.Kept == 0 && (double.IsNaN(x.Gap) || x.Gap < -0.05)).Count is int susp && susp > 0
            ? $" · ⚠토우가 지반 아래인데 판넬 0장인 줄 {susp}개 — 벽이 사라졌을 수 있음"
            : (tPerLine.FindAll(x => x.Kept == 0).Count > 0
                ? $" · 판넬 0장인 줄 {tPerLine.FindAll(x => x.Kept == 0).Count}개는 전부 데이라잇 위(정상 — 붙잡을 흙 없음)" : ""));
}
