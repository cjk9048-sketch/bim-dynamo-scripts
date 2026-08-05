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

    /// <summary>판넬 한 변 — 단높이 ≤1m→높이 / ≤3m→½ / 그 이상→⅓, 상한 <see cref="MaxSide"/>.</summary>
    public static double SideFor(double height)
    {
        double h = System.Math.Abs(height);
        double s = h <= 1.0 + 1e-9 ? h : h <= 3.0 + 1e-9 ? h / 2 : h / 3;
        return System.Math.Min(System.Math.Max(s, 1e-3), MaxSide);
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
        int Bench, bool Up);

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

    /// <summary>폴리선의 누적 2D 호길이.</summary>
    private static double[] Cum(IReadOnlyList<Point3> p)
    {
        var c = new double[p.Count];
        for (int i = 1; i < p.Count; i++) c[i] = c[i - 1] + Dist2D(p[i - 1], p[i]);
        return c;
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
    public static List<(double F0, double F1)> SplitAtCorners(IReadOnlyList<Point3> crest, double cornerDeg = 12.0)
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
        double start = 0;
        for (int i = 1; i < crest.Count - 1; i++)
        {
            double ax = crest[i].X - crest[i - 1].X, ay = crest[i].Y - crest[i - 1].Y;
            double bx = crest[i + 1].X - crest[i].X, by = crest[i + 1].Y - crest[i].Y;
            double la = System.Math.Sqrt(ax * ax + ay * ay), lb = System.Math.Sqrt(bx * bx + by * by);
            if (la < 1e-9 || lb < 1e-9) continue;
            double cos = (ax * bx + ay * by) / (la * lb);
            if (cos >= cosLim) continue;                       // 꺾임이 작다 — 같은 벽면으로 이어간다
            double f = cum[i] / total;
            if (f - start > 1e-6) outp.Add((start, f));
            start = f;
        }
        if (1.0 - start > 1e-6) outp.Add((start, 1.0));
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
        int colN = 0, rowN = 0, dGround = 0, dAbove = 0, dJoint = 0, dThin = 0;
        int full = 0;

        var runs = SplitAtCorners(crest, cornerDeg);
        double totalC = cumC[cumC.Length - 1];

        foreach (var (f0, f1) in runs)
        {
            double segLen = (f1 - f0) * totalC;
            if (segLen < 1e-3) continue;
            // 열 폭을 **균등 분배** — ceil로 개수를 정하고 길이를 n등분한다.
            //   종전처럼 side로 자르고 나머지를 자투리 열로 두면 수 mm짜리 실오라기가 생겨
            //   줄눈 인셋에 통째로 죽었다(v17.8에서 '줄눈 1690'의 정체).
            int ncol = System.Math.Max(1, (int)System.Math.Ceiling(segLen / side - 1e-9));
            double colW = segLen / ncol;

            for (int j = 0; j < ncol; j++)
            {
                colN++;
                double fa = f0 + (f1 - f0) * j / ncol;
                double fb = f0 + (f1 - f0) * (j + 1) / ncol;
                // ★모서리 겹침 마감 — 벽면 끝 열은 모서리 쪽으로 두께 절반만큼 더 나간다.
                //   두 벽면이 코너에서 정확히 만나면 볼록 모서리에 쐐기 틈이 남는다(JACK '각진부 마감 이상').
                //   판넬은 자기 평면을 따라 조금 더 나가므로 이웃 벽 뒤로 물려 코너가 꽉 찬다.
                //   ※ 옛 방식의 '이웃 평면으로 잘라내기'와 달리 **자르지 않는다** — 그게 버그의 온상이었다.
                double lapA = j == 0 ? cornerLap : 0, lapB = j == ncol - 1 ? cornerLap : 0;

                var cA = AtFrac(crest, cumC, fa); var cB = AtFrac(crest, cumC, fb);
                var tA = AtFrac(toe, cumT, fa); var tB = AtFrac(toe, cumT, fb);

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

                // 이 열의 데이라잇 상한 — 원지반보다 위로는 벽이 없다.
                double CapAt(double fu)
                {
                    if (ground == null) return faceH;
                    double f = fa + (fb - fa) * System.Math.Clamp(fu, 0, 1);
                    var t0 = AtFrac(toe, cumT, f); var c0 = AtFrac(crest, cumC, f);
                    if (!ground.TryGetElevation(t0.X, t0.Y, out double gz0)) return -1;   // 지반 밖
                    if (t0.Z >= gz0 - 1e-6) return 0;            // 토우가 이미 지반 위 — 벽 없음
                    if (!ground.TryGetElevation(c0.X, c0.Y, out double gz1)) return -1;
                    if (c0.Z <= gz1 + 1e-6) return faceH;        // 크레스트도 지반 아래 — 꽉 참
                    // 토우~크레스트 사이에서 지반과 만나는 지점(선형 보간).
                    double d0 = gz0 - t0.Z, d1 = gz1 - c0.Z;     // d0>0, d1<0
                    double r = d0 / (d0 - d1);
                    return System.Math.Clamp(r, 0, 1) * faceH;
                }

                double capA = CapAt(0.02), capM = CapAt(0.5), capB = CapAt(0.98);
                if (capA < 0 && capM < 0 && capB < 0) { dGround++; continue; }
                double capMax = System.Math.Max(0, System.Math.Max(capA, System.Math.Max(capM, capB)));
                if (capMax <= 1e-6) { dAbove++; continue; }

                int nrow = System.Math.Max(1, (int)System.Math.Ceiling(faceH / side - 1e-9));
                double rowH = faceH / nrow;
                for (int i = 0; i < nrow; i++)
                {
                    rowN++;
                    double s0 = i * rowH, s1 = (i + 1) * rowH;
                    // 데이라잇 사선 클립 — 열 좌/중/우 상한으로 사다리꼴을 만든다.
                    double c0 = System.Math.Clamp(capA, 0, faceH), c1 = System.Math.Clamp(capB, 0, faceH);
                    if (c0 <= s0 + 1e-6 && c1 <= s0 + 1e-6) { dAbove++; continue; }

                    // 모서리 겹침은 줄눈을 넘어 바깥으로 나간다 — 그래야 코너가 꽉 찬다.
                    double u0 = jm - lapA, u1 = colW - jm + lapB, v0 = s0 + jm, v1 = s1 - jm;
                    if (u1 - u0 < 0.03 || v1 - v0 < 0.03) { dJoint++; continue; }

                    // 상한이 이 행을 가로지르면 사다리꼴로 자른다(양끝 상한을 선형 보간).
                    double capL = System.Math.Clamp(c0, 0, faceH), capR = System.Math.Clamp(c1, 0, faceH);
                    var local = new List<(double u, double v)>(4);
                    double vAtL = System.Math.Min(v1, capL - jm), vAtR = System.Math.Min(v1, capR - jm);
                    if (vAtL <= v0 + 1e-6 && vAtR <= v0 + 1e-6) { dAbove++; continue; }
                    local.Add((u0, v0));
                    local.Add((u1, v0));
                    if (vAtR > v0 + 1e-6) local.Add((u1, vAtR));
                    if (vAtL > v0 + 1e-6) local.Add((u0, vAtL));
                    if (local.Count < 3) { dThin++; continue; }

                    // '온전'(=앵커·정착구를 다는 판넬)의 뜻: **데이라잇에 안 잘린 완전한 사각**이고
                    //   가운데 정착구(도넛 0.56m)를 물 만큼 크다는 것.
                    //   ※ 열 폭이 상한(side)과 같아야 한다는 식으로 판정하면 안 된다 — 균등 분배라 열 폭은
                    //     거의 항상 상한보다 조금 작아서(예 1.553 < 1.667) **온전이 하나도 안 나오고 앵커가
                    //     통째로 사라진다**(첫 구현에서 실제로 온전 0장이었다).
                    const double anchorMin = 0.80;               // 도넛 0.56 + 여유
                    bool uncut = vAtL >= v1 - 1e-6 && vAtR >= v1 - 1e-6;
                    bool isFull = uncut && (u1 - u0) >= anchorMin && (v1 - v0) >= anchorMin;
                    if (isFull) full++;

                    var poly = new List<Point3>(local.Count);
                    foreach (var (lu, lv) in local)
                        poly.Add(new Point3(org.X + lu * ux + lv * vx,
                                            org.Y + lu * uy + lv * vy,
                                            org.Z + lv * vz));
                    double pu = (u0 + u1) / 2, pv = (v0 + v1) / 2;
                    tiles.Add(new Tile(poly, isFull, org, (ux, uy, 0), (vx, vy, vz), (wx, wy, wz),
                        local, pu, pv, run.Bench, run.Up));
                }
            }
        }

        LastDiag = $"판넬 {tiles.Count}(온전 {full}) · 벽면 {runs.Count} · 열 {colN} · 행 {rowN}" +
                   $" · 한변 {side:F2}m · 높이 {height:F2}m" +
                   $" · 버림(지반밖 {dGround} · 지반위 {dAbove} · 줄눈 {dJoint} · 퇴화 {dThin})";
        return tiles;
    }
}
