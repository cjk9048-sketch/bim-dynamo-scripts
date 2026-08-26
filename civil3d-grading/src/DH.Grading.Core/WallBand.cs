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

    /// <summary>[JACK 0806] 직전 <see cref="Slice"/>가 만든 코너 필러 — 볼록 코너에서 두 벽면이 벌어져
    /// 남은 쐐기 틈을 메우는 기둥. 렌더러는 <c>WallPanelDwg.BuildQuoin</c>(이미 있음).
    /// <para>호출자가 여러 줄을 자르는 동안 쌓이므로, 한 내보내기 단위로 <see cref="ResetTotals"/>가 비운다.</para></summary>
    public static List<WallPanels.Quoin> LastQuoins { get; } = new();

    private static double Dist2(Point3 a, Point3 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return System.Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>★★★[JACK 0819 확정] <b>판넬 한 변 상한(m) — 1.5m.</b>
    ///
    /// <para>JACK: <i>"보통 단이 1M 2M처럼 너무 낮으면 그냥 큰 거 한 판 패널로 들어가.
    /// 5M처럼 단이 높으면 3장 정도는 써서 들어가고, 더 단이 높아진다고 패널도 계속 커지는 건 아니야.
    /// 그냥 쉽게 이야기해서 <b>최대 1.5M로 하고 그보다 작으면 무조건 한 장 사이즈</b>로."</i></para>
    ///
    /// <para><b>제작 규격이지 계산값이 아니다.</b> 종전 값 <c>5.0/3.0</c>은 '단높이 5m ÷ 3행'에서 나온 숫자라,
    /// 단높이를 바꾸면 같이 바뀌어야 하는 값처럼 읽혔다 — 실제로는 <b>운반·제작이 정하는 고정 치수</b>다.
    /// 단높이가 20m가 되어도 판넬은 1.5m를 넘지 않는다.</para></summary>
    public const double MaxSide = 1.5;

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

    /// <summary>[하니스 전용] '열 폭을 토우 길이로' 잡는 것을 끄고 크레스트 길이로 되돌린다.
    /// 다른 방어(코너 분할·현 제한)의 자체검증은 이걸 같이 꺼야 성립한다 — 사다리꼴이 그 결함까지
    /// 덮어 주면 '방어를 껐는데도 멀쩡한' 결과가 나와 검사가 무력해진다.</summary>
    public static bool DisableToeWidthForTest;



    /// <summary>[0806] 벽면 끝에 남는 자투리 판넬의 하한(m) — 이보다 짧으면 앞 판넬에 붙인다.
    /// 수 cm짜리 자투리는 줄눈 인셋에 통째로 죽어 그 자리가 구멍이 된다(v17.8 '줄눈 1690'의 정체).</summary>
    public const double MinTailLen = 0.40;

    /// <summary>★[JACK 0807] 성토 벽면은 <b>원지반선에서 아래가 잘린다</b>(하한). 상한(데이라잇)의 거울.
    /// <para>
    /// 0806~0807에 '한 단을 통째로 살릴까 버릴까'로 두 번 실패했다 — 버리면 전이 구간에 13m 구멍(0806),
    /// 살리면 지반이 내려앉은 자리마다 한 단이 통째로 매달린다(0807 스샷 '2단인데 3단까지'). 여유값을
    /// 어디에 두든 둘 중 하나는 반드시 나온다. <b>값이 아니라 규칙이 틀렸다.</b>
    /// </para>
    /// 성토는 흙을 쌓아 올리므로 원지반 아래엔 벽면이 없다(그 아래는 기초지, 판넬이 아니다).
    /// 지반선을 따라 아래를 자르면 전이 구간에서 벽이 <b>가늘어지다 사라지고</b>, 이미 잠긴 아래 단은
    /// <b>애초에 안 생긴다</b> — 두 결함이 한 규칙에서 같이 죽는다. 구현은 <c>FloorAt</c>.
    /// </summary>
    public const string FillFloorRule = "성토 하한 = 원지반선(FloorAt)";

    /// <summary>지반선 아래라 만들지 않은 열의 사유 문자열 — 진단에서 '진짜 구멍'과 갈라내는 열쇠라 상수로 둔다.</summary>
    internal const string WhyBuried = "지반선아래";

    /// <summary>★[JACK 0807] 코너 전용 판넬의 <b>다리 길이</b>(m) — 코너에서 양옆 규격 판넬이 물러나는 거리.
    /// 실제 프리캐스트 옹벽도 코너는 현장에서 비스듬히 자르지 않고 <b>코너 전용 유닛</b>을 쓴다.
    /// 0.35m면 판넬 두께(0.20)와 전면 돌출(0.10)을 합친 것보다 넉넉해 어떤 각도에서도 단면이 성립한다.</summary>
    public const double CornerLeg = 0.35;

    /// <summary>★[JACK 0807] 코너 전용 판넬 하나 — 아래·위 두 단면(월드 3D). 압출이 아니라 <b>두 단면 사이 로프트</b>다.
    /// <para>벽이 1:0.05로 기울어 두 벽면이 <b>서로 다른 방향으로</b> 물러나므로, 위 단면은 아래 단면의
    /// 단순 평행이동이 아니다(90° 코너·단높이 5m면 0.18m 어긋난다). 두 단면을 각자 제 높이에서 구해
    /// 그 사이를 이으면 위·아래가 정확하고 중간은 선형이라 기울기와 정확히 맞는다.</para></summary>
    /// <param name="Swept">★[JACK 0819] 옹벽선을 그대로 훑어 만든 덩어리(각도를 안 재는 경로).
    /// 정점마다 그 자리 데이라잇으로 <b>이미</b> 잘려 있으므로 <see cref="ClampQuoinsToPanels"/>의
    /// 높이 보정(정점 0의 Z를 단면 전체의 대표로 쓴다)을 태우면 오히려 윗변이 평평하게 뭉개진다 —
    /// 허공 검사는 태우고 높이 보정만 건너뛴다.</param>
    public readonly record struct CornerUnit(IReadOnlyList<Point3> Bot, IReadOnlyList<Point3> Top, bool Swept = false);

    /// <summary>직전 <see cref="Slice"/>가 만든 코너 전용 판넬들 — <see cref="ResetTotals"/>가 비운다.</summary>
    public static List<CornerUnit> LastCornerUnits { get; } = new();

    /// <summary>★★★[JACK 0819 '지금 여전히 하나하나 블록으로 나오는데'] <b>옹벽 한 줄 = 솔리드 하나.</b>
    /// <para>
    /// 종전엔 정점 구간마다 조각을 만들어 <b>쌓았다</b>(실측 131개). 맞붙어 있어도 도면에서는 131덩어리라
    /// JACK이 본 대로 "하나하나 블록"이다. 로프트를 두 단면 사이에만 걸었기 때문이다.
    /// </para>
    /// 로프트는 <b>단면을 여러 장</b> 받을 수 있다 — 정점마다 마름모 단면을 만들어 <b>한 번에</b> 통과시키면
    /// 솔리드 하나가 나오고, 그러면서도 단면마다 데이라잇 높이가 달라 벽 윗선이 지형을 따라간다.
    /// <para>단면은 4점 평행사변형(아래=토우 · 위=크레스트 · 두께 = 판넬 두께)이고 <b>반드시 평면</b>이다 —
    /// 비평면 단면은 로프트가 예외 없이 빈 솔리드를 돌려준다(0819 실측 26개 중 23개).</para>
    /// 데이라잇에 벽이 끊기면 거기서 덩어리를 나눈다(한 덩어리로 이으면 허공을 가로지른다).</summary>
    public readonly record struct WallMass(IReadOnlyList<IReadOnlyList<Point3>> Sections);

    /// <summary>직전 <see cref="Slice"/>들이 만든 옹벽 매스 — 줄마다 하나(데이라잇에 끊기면 여럿).</summary>
    public static List<WallMass> LastMasses { get; } = new();

    /// <summary>★★★[JACK 0820 '백판 덩어리를 데이라잇으로 지우기 전에 무늬 작업을 하고,
    /// 무늬까지 포함해서 데이라잇으로 지워야 해'] <b>자르기 전의 온전한 벽면</b>과 <b>자르는 위치</b>.
    /// <para>무늬 격자를 <b>잘린 벽</b>에 깔면 데이라잇이 끊은 자리마다 격자가 다시 시작해 세로줄이 어긋나고
    /// (JACK 0820 '세로 방향을 유지할 것 · 지금은 들쑥날쑥함'), 구간 경계가 통째로 빈다
    /// ('직선구간 중간에 비는 공간이 없게 할 것').</para>
    /// 그래서 격자는 <b>온전한 벽</b>(<paramref name="Full"/>)에 깔고, 판마다 <paramref name="Lo"/>·<paramref name="Hi"/>로
    /// <b>나중에 자른다</b> — 벽과 무늬가 같은 선에서 잘린다.
    /// <para><paramref name="Lo"/>·<paramref name="Hi"/>는 토우(0)에서 크레스트(1)로 가는 비율이다.
    /// <c>Hi ≤ Lo</c>면 그 자리엔 벽이 없다.</para></summary>
    public readonly record struct WallFace(IReadOnlyList<IReadOnlyList<Point3>> Full,
                                           IReadOnlyList<double> Lo, IReadOnlyList<double> Hi);

    /// <summary>직전 <see cref="Slice"/>들이 만든 자르기 전 벽면 — 무늬 격자가 이것 위에 깔린다.</summary>
    public static List<WallFace> LastFaces { get; } = new();

    /// <summary>★[JACK 0820 '무늬랑 앵커부랑 앵커는 전에 만들었던 것 활용해'] 직전 <see cref="BuildFacePanels"/>가
    /// 만든 표면 마감 판 — <b>기존 판넬과 같은 자료형</b>이라 무늬·도넛·앵커·정착판이 그대로 붙는다.
    /// <para>새 렌더러를 만들지 않는다. 이 저장소에서 이미 다듬어 온 <c>WallPanelDwg</c> 경로를 그대로 타는 것이
    /// 요점이다 — 같은 그림을 두 번 그리기 시작하면 둘이 어긋나는 것은 시간 문제다.</para></summary>
    public static List<WallPanels.Panel> LastFacePanels { get; } = new();

    /// <summary>표면 판 한 변(m) — JACK 0820 "한 무늬가 가로세로 1.5로 규정해서 채우면 될 것 같아".</summary>
    public const double FaceTile = 1.5;

    /// <summary>★[JACK 0820] 마감이 매스 바깥면에서 띄우는 양(m) — <b>0</b>이다.
    /// 몸통(바탕 판)을 안 만들고 무늬만 얹으므로, 무늬는 매스 표면에 <b>딱 붙어</b> 있어야 한다.
    /// 띄우면 무늬만 허공에 뜬다(종전 0.03은 판을 겹쳐 얹던 시절 값이다).</summary>
    public const double FaceProud = 0.0;

    /// <summary>[진단] 직전 <see cref="BuildFacePanels"/>의 성적.</summary>
    public static string LastFaceDiag { get; private set; } = "";

    /// <summary>★★[JACK 0820] <see cref="LastMasses"/>의 바깥면에 1.5m 격자 판을 얹는다.
    /// <para>★★★[JACK 0820 스샷 3장] 격자를 <b>벽 전체에 한 번만</b> 깐다. 종전엔 직선 구간마다 따로 깔았는데,
    /// 그 하나가 세 증상을 동시에 만들었다 —
    /// ① <b>세로가 들쑥날쑥</b>(행마다 시작점이 달라 열이 어긋남) ② <b>직선구간 중간에 빈 공간</b>
    /// (구간 경계가 통째로 빔) ③ <b>잘린 부분이 안 채워짐</b>(위 자투리 행을 아예 안 만듦).</para>
    /// 이제 토우를 따라 잰 <b>누적 거리</b> 하나로 칸을 나눈다. 칸마다
    /// <b>굽었으면 건너뛰고</b>(JACK: 굴곡부까지 억지로 채우지 말고),
    /// 곧으면 아래에서 위로 채우되 <b>맨 윗행은 데이라잇 선에 맞춰 잘라</b> 끝까지 마감한다
    /// (JACK: 데이라잇으로 잘려지는 부분까지도 끝까지 마감할 것).</summary>
    /// <param name="flatTol">직선 판정 한도(m) — 그 칸을 곧은 판으로 덮었을 때 벽선에서 벗어나는 양.
    /// 각도가 아니라 거리다(§39: 각도 문턱은 이 저장소가 여덟 번 고쳐 온 자다).</param>
    public static int BuildFacePanels(double flatTol = 0.05)
    {
        LastFacePanels.Clear();
        int tiles = 0, curvedCol = 0, clipped = 0, dropped = 0; double wallLen = 0;

        // ★★★[JACK 0820 '백판 덩어리를 데이라잇으로 지우기 전에 무늬 작업을 하고, 무늬까지 포함해서 지워야 해']
        //   격자를 **자르기 전의 온전한 벽**에 깔고, 판마다 나중에 자른다.
        //   잘린 벽에 깔면 데이라잇이 끊은 자리마다 격자가 다시 시작해 세로줄이 어긋나고 구간 경계가 빈다.
        foreach (var wf in LastFaces)
        {
            var sec = wf.Full;
            if (sec == null || sec.Count < 2) continue;

            // 토우(바깥아래)를 따라 잰 누적 거리 — 이 하나가 벽 전체의 가로 좌표다.
            var cum = new double[sec.Count];
            for (int k = 1; k < sec.Count; k++) cum[k] = cum[k - 1] + Dist3(sec[k - 1][1], sec[k][1]);
            double total = cum[sec.Count - 1];
            wallLen += total;
            if (total < FaceTile) continue;

            // ★★★[검토 심각4] **끝 자투리를 버리지 않는다.** Floor로 세면 마지막 칸 뒤의 0~1.49m가
            //   어떤 칸에도 안 들어가 줄마다 맨살이 남는다("구간 경계가 통째로 빔"이 자리만 옮긴 것).
            //   Ceiling으로 세고 마지막 칸을 벽 끝에서 자른다 — TileColumn은 임의 폭을 이미 받는다.
            int nCol = (int)System.Math.Ceiling(total / FaceTile - 1e-9);
            for (int c = 0; c < nCol; c++)
            {
                double s0 = c * FaceTile, s1 = System.Math.Min((c + 1) * FaceTile, total);
                if (s1 - s0 < MinPieceLen) continue;                   // 실오라기 칸은 줄눈에 죽는다
                double curv = ChordOffS(sec, cum, s0, s1);
                if (curv > flatTol)
                {
                    curvedCol++;
                    // ★[JACK 0820] **어디가 굽었는지 좌표로 남긴다** — 개수만으로는 어디를 볼지 모른다.
                    if (curv > tCurvMax)
                    {
                        var cs = SecAt(sec, cum, (s0 + s1) / 2);
                        tCurvMax = curv; tCurvX = cs[1].X; tCurvY = cs[1].Y;
                    }
                    continue;
                }
                var L = SecAt(sec, cum, s0);
                var R = SecAt(sec, cum, s1);
                // 이 칸에서 잘리는 위치(토우 0 ~ 크레스트 1) — 양 끝에서 각각.
                double loL = LerpAt(cum, wf.Lo, s0), hiL = LerpAt(cum, wf.Hi, s0);
                double loR = LerpAt(cum, wf.Lo, s1), hiR = LerpAt(cum, wf.Hi, s1);
                if (hiL - loL <= 0 && hiR - loR <= 0) { dropped++; tPfNoWall++; continue; }   // 이 칸엔 벽이 없다
                tiles += TileColumn(sec, cum, wf.Lo, wf.Hi, s0, s1, ref clipped, ref dropped);
            }
        }
        // ★★[JACK 0820 '문제점을 로그로도 확인할 수 있게 해 · 계속 스샷으로 하기 힘들어']
        //   **스샷 없이도 갈리게 잰다.** 눈으로만 보이던 것을 숫자로 바꾼다 —
        //   ① 덮개율: 벽 겉면 중 판이 덮은 비율. 낮으면 '빈 공간이 많다'는 그 증상이다.
        //   ② 뒤집힌 판: 판 앞면이 벽 바깥을 등진 개수. 0이 아니면 '반대로 됨'이다.
        //   ③ 가장 큰 빈 자리의 좌표 — 어디를 봐야 하는지까지 알려 준다.
        // ★★[JACK 0820 '문제점을 로그로도 확인할 수 있게 해'] 스샷 없이 갈리게 잰다.
        // ★[검토 중간10] 종전엔 판마다 모든 매스 단면을 훑어 <b>O(판 × 단면)</b>이었다
        //   (판 1만 × 단면 2만 = 2억 회) — "내보내기가 너무 오래 걸린다"에 정면으로 역행한다.
        //   게다가 [검토 심각1]대로 그 비교는 **원하는 결함을 못 잡는다**(둘이 같이 뒤집히면 내적이 양수).
        //   → 뒤집힘은 위 <c>LastSideFlip</c>(매스를 안 보고 재는 값)이 맡고, 여기서는 **면적만** 센다.
        double wallArea = 0, panelArea = 0;
        foreach (var mm in LastMasses)
            for (int k = 0; k + 1 < mm.Sections.Count; k++)
            {
                var A = mm.Sections[k]; var B = mm.Sections[k + 1];
                if (A.Count != 4 || B.Count != 4) continue;
                wallArea += (Dist3(A[1], A[0]) + Dist3(B[1], B[0])) / 2 * Dist3(A[1], B[1]);
            }
        foreach (var f in LastFacePanels)
        {
            double a2 = 0;
            for (int i = 0, j = f.Local.Count - 1; i < f.Local.Count; j = i++)
                a2 += f.Local[j].u * f.Local[i].v - f.Local[i].u * f.Local[j].v;
            panelArea += System.Math.Abs(a2) / 2;
        }
        double cover = wallArea > 1e-9 ? panelArea / wallArea * 100 : 0;
        double gapMax = System.Math.Max(0, wallArea - panelArea);
        // 줄눈 때문에 100%는 구조적으로 불가능하다 — 기준선을 같이 찍어야 숫자가 판단이 된다(검토 낮음15).
        double coverMax = FaceTile > 1e-9 ? (FaceTile - JointW) * (FaceTile - JointW) / (FaceTile * FaceTile) * 100 : 0;
        LastFaceDiag = $"표면 판 {tiles}장(한 변 {FaceTile:F1}m · 잘린 판 {clipped}장 · 못 놓은 자리 {dropped}곳)" +
                       $" · 굽어서 건너뛴 칸 {curvedCol}개" +
                       (tCurvMax > 0 ? $"(최대 이탈 {tCurvMax:F2}m @ {tCurvX:F0},{tCurvY:F0} · 한도 {flatTol:F2}m)" : "") +
                       (tPfNoWall + tPfThin + tPfFrame > 0
                           ? $" · 못 놓은 사유(벽 없음 {tPfNoWall} · 행이 얇음 {tPfThin} · 프레임 실패 {tPfFrame})" : "") +
                       $" · 벽 길이 {wallLen:F0}m" +
                       $" · ★덮개율 {cover:F0}%(줄눈 빼고 최대 {coverMax:F0}% · 벽 {wallArea:F0}㎡ · 판 {panelArea:F0}㎡ · 안 덮인 {gapMax:F0}㎡)";
        return tiles;
    }

    /// <summary>누적 거리 s 자리의 값 — 이웃 두 station 사이를 선형 보간한다(자르는 비율에 쓴다).</summary>
    private static double LerpAt(double[] cum, IReadOnlyList<double> val, double s)
    {
        int lo = 0, hi = cum.Length - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (cum[mid] <= s) lo = mid; else hi = mid;
        }
        double seg = cum[hi] - cum[lo];
        double t = seg > 1e-12 ? System.Math.Clamp((s - cum[lo]) / seg, 0, 1) : 0;
        return val[lo] + (val[hi] - val[lo]) * t;
    }

    /// <summary>[s0..s1] 칸을 곧은 판으로 덮었을 때 그 사이 단면들이 벗어나는 최대 거리(m) — 위·아래 둘 다 본다.</summary>
    private static double ChordOffS(IReadOnlyList<IReadOnlyList<Point3>> sec, double[] cum, double s0, double s1)
    {
        double worst = 0;
        for (int idx = 0; idx <= 1; idx++)                              // 0=바깥위(크레스트) · 1=바깥아래(토우)
        {
            var A = SecAt(sec, cum, s0)[idx];
            var B = SecAt(sec, cum, s1)[idx];
            double dx = B.X - A.X, dy = B.Y - A.Y, L2 = dx * dx + dy * dy;
            if (L2 < 1e-12) continue;
            for (int k = 0; k < sec.Count; k++)
            {
                if (cum[k] <= s0 + 1e-9 || cum[k] >= s1 - 1e-9) continue;
                var q = sec[k][idx];
                double t = System.Math.Clamp(((q.X - A.X) * dx + (q.Y - A.Y) * dy) / L2, 0, 1);
                double ex = q.X - (A.X + dx * t), ey = q.Y - (A.Y + dy * t);
                double d = System.Math.Sqrt(ex * ex + ey * ey);
                if (d > worst) worst = d;
            }
        }
        return worst;
    }

    /// <summary>누적 거리 s 자리의 단면(네 점) — 이웃 두 단면 사이를 선형 보간한다.</summary>
    private static IReadOnlyList<Point3> SecAt(IReadOnlyList<IReadOnlyList<Point3>> sec, double[] cum, double s)
    {
        int lo = 0, hi = sec.Count - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (cum[mid] <= s) lo = mid; else hi = mid;
        }
        double seg = cum[hi] - cum[lo];
        double t = seg > 1e-12 ? System.Math.Clamp((s - cum[lo]) / seg, 0, 1) : 0;
        var r = new List<Point3>(4);
        for (int i = 0; i < 4; i++) r.Add(Lerp3(sec[lo][i], sec[hi][i], t));
        return r;
    }

    /// <summary>칸 하나를 아래에서 위로 채운다 — 격자는 <b>온전한 벽</b> 기준이고, 판은 <b>나중에</b> 잘린다.
    /// <para>★★★[JACK 0820 '무늬 부분의 잘림이 데이라잇과 맞지 않는다'] <b>윗변을 데이라잇 정점마다 꺾는다.</b>
    /// 종전엔 판을 사각형으로 고집해 윗변이 1.5m짜리 <b>직선</b>이었다 — 데이라잇은 그 안에서 꺾이므로
    /// 무늬 잘린 선과 벽 윗선이 어긋난다. 판을 사각형으로 둘 이유가 없다(이 저장소는 이미 5·6각 판을 만든다).
    /// 칸 안의 옹벽선 정점마다 잘리는 높이를 찍어 <b>여러 점으로 된 윗변</b>을 만들면 벽과 정확히 같은 선에서 잘린다.</para></summary>
    private static int TileColumn(IReadOnlyList<IReadOnlyList<Point3>> sec, double[] cum,
                                  IReadOnlyList<double> loArr, IReadOnlyList<double> hiArr,
                                  double s0, double s1, ref int clipped, ref int dropped)
    {
        double half = JointW / 2;
        const double MinRow = 0.15;                                    // 이보다 얇은 행은 줄눈에 죽는다

        // 좌우 줄눈 — 칸 폭에서 양쪽을 half씩 들인다. 윗변을 꺾을 s 지점들도 그 안에서만 잡는다.
        double sa = s0 + half, sb = s1 - half;
        if (sb - sa < MinRow) return 0;

        // 이 칸 안의 옹벽선 정점 — 윗변이 꺾일 자리다.
        var ss = new List<double> { sa };
        for (int k = 0; k < cum.Length; k++)
            if (cum[k] > sa + 1e-6 && cum[k] < sb - 1e-6) ss.Add(cum[k]);
        ss.Add(sb);

        // 각 s에서 온전한 벽면(토우·크레스트)과 자르는 비율
        int n = ss.Count;
        var bot = new Point3[n]; var top = new Point3[n];
        var hgt = new double[n]; var lo = new double[n]; var hi = new double[n];
        // ★★★[JACK 0820 '반대로 됨'] **바깥 방향은 자리마다 구한다.**
        //   직전 판에서 <c>sec[0]</c>(벽 <b>전체의 첫 단면</b>)의 방향을 가져다 썼다 — 벽이 돌면 뒤쪽 판이
        //   반대편에 붙는다. 오늘만 다섯 번째로 같은 실수다: <b>먼 데서 값을 빌려오는 것</b>
        //   (벽 끝 방향 · 로프트 실패 시 절반 버리기 · 지반 밖 높이 · 코너 단면 법선 · 그리고 이것).
        //   단면 자체가 그 자리 방향을 이미 담고 있다(바깥위 − 안쪽위) — 빌릴 이유가 없다.
        var nx = new double[n]; var ny = new double[n]; var nz = new double[n];
        for (int k = 0; k < n; k++)
        {
            var S = SecAt(sec, cum, ss[k]);
            bot[k] = S[1]; top[k] = S[0];
            hgt[k] = Dist3(bot[k], top[k]);
            lo[k] = LerpAt(cum, loArr, ss[k]);
            hi[k] = LerpAt(cum, hiArr, ss[k]);
            var nk = Norm3(S[0].X - S[3].X, S[0].Y - S[3].Y, S[0].Z - S[3].Z);
            if (!nk.ok) return 0;
            nx[k] = nk.x; ny[k] = nk.y; nz[k] = nk.z;
        }
        var nrm = (ok: true, x: nx[n / 2], y: ny[n / 2], z: nz[n / 2]);   // 프레임 부호용 대표값(칸 안)

        Point3 P(int k, double v)
        {
            double t = hgt[k] > 1e-9 ? System.Math.Clamp(v / hgt[k], 0, 1) : 0;
            return new Point3(bot[k].X + (top[k].X - bot[k].X) * t + nx[k] * FaceProud,
                              bot[k].Y + (top[k].Y - bot[k].Y) * t + ny[k] * FaceProud,
                              bot[k].Z + (top[k].Z - bot[k].Z) * t + nz[k] * FaceProud);
        }

        // 임의의 s 자리 값 — 데이라잇이 행을 가로지르는 지점을 찾는 데 쓴다.
        (Point3 bot, Point3 top, double hgt, double lo, double hi) At(double sv)
        {
            var S = SecAt(sec, cum, sv);
            return (S[1], S[0], Dist3(S[1], S[0]), LerpAt(cum, loArr, sv), LerpAt(cum, hiArr, sv));
        }
        int made = 0;
        for (int j = 0; j < 400; j++)
        {
            double v0 = j * FaceTile + half, v1 = (j + 1) * FaceTile - half;
            bool anyLive = false;
            for (int k = 0; k < n; k++) if (v0 < hgt[k]) { anyLive = true; break; }
            if (!anyLive) break;                                       // 온전한 벽 위로는 더 없다

            // ★★★[JACK 0820 '안 잘렸으면 안 잘렸어야지 왜 아예 없는거지'] **데이라잇이 비스듬히 지나면 가로로도 자른다.**
            //   종전엔 세로로만 잘랐다 — 한쪽 끝의 데이라잇이 그 행보다 아래면 두께를 0으로 만들 뿐
            //   <b>자리는 그대로 둬서 그 뾰족한 끝이 벽 위 허공에 떴다</b>(실측 2.26m).
            //   → 데이라잇이 행의 아랫변을 <b>가로지르는 s를 찾아</b> 거기서 판을 끊는다.
            //     한 행이 두 조각으로 갈릴 수도 있다(가운데가 잘리는 경우) — 그때는 조각마다 판을 놓는다.
            var vb = new double[n]; var vt = new double[n];
            bool cut = false;
            for (int k = 0; k < n; k++)
            {
                vb[k] = System.Math.Max(v0, lo[k] * hgt[k]);
                vt[k] = System.Math.Min(v1, hi[k] * hgt[k]);
                if (vb[k] > v0 + 1e-9 || vt[k] < v1 - 1e-9) cut = true;
            }
            const double Tiny = 0.01;
            // 데이라잇이 행의 아랫변을 가로지르는 자리를 좁혀 찾는다 — 거기서 판이 끝나야 허공에 안 뜬다.
            double CrossS(double sDead, double sLive)
            {
                for (int it = 0; it < 14; it++)
                {
                    double sm = (sDead + sLive) / 2;
                    var r = At(sm);
                    double bV = System.Math.Max(v0, r.lo * r.hgt);
                    double tV = System.Math.Min(v1, r.hi * r.hgt);
                    if (tV - bV >= Tiny) sLive = sm; else sDead = sm;
                }
                return sLive;
            }
            int p0 = 0;
            while (p0 < n)
            {
                while (p0 < n && vt[p0] - vb[p0] < Tiny) p0++;
                if (p0 >= n) break;
                int p1 = p0;
                while (p1 + 1 < n && vt[p1 + 1] - vb[p1 + 1] >= Tiny) p1++;

                // 조각의 표본 s 목록 — 양 끝에는 데이라잇이 아랫변을 가로지르는 자리를 끼워 넣는다.
                var sv = new List<double>();
                if (p0 > 0) sv.Add(CrossS(ss[p0 - 1], ss[p0]));
                for (int k = p0; k <= p1; k++) sv.Add(ss[k]);
                if (p1 + 1 < n) sv.Add(CrossS(ss[p1 + 1], ss[p1]));

                int m = sv.Count;
                var qb = new Point3[m]; var qt = new Point3[m];
                double thickest = 0;
                for (int i = 0; i < m; i++)
                {
                    var r = At(sv[i]);
                    var nk = Norm3(0, 0, 0);
                    // 그 자리 바깥 방향 — 단면에서 직접 얻는다(먼 데서 빌려오지 않는다).
                    var SS = SecAt(sec, cum, sv[i]);
                    nk = Norm3(SS[0].X - SS[3].X, SS[0].Y - SS[3].Y, SS[0].Z - SS[3].Z);
                    if (!nk.ok) { thickest = 0; break; }
                    double bV = System.Math.Max(v0, r.lo * r.hgt);
                    double tV = System.Math.Max(bV, System.Math.Min(v1, r.hi * r.hgt));
                    thickest = System.Math.Max(thickest, tV - bV);
                    Point3 On(double v)
                    {
                        double t = r.hgt > 1e-9 ? System.Math.Clamp(v / r.hgt, 0, 1) : 0;
                        return new Point3(r.bot.X + (r.top.X - r.bot.X) * t + nk.x * FaceProud,
                                          r.bot.Y + (r.top.Y - r.bot.Y) * t + nk.y * FaceProud,
                                          r.bot.Z + (r.top.Z - r.bot.Z) * t + nk.z * FaceProud);
                    }
                    qb[i] = On(bV); qt[i] = On(tV);
                }
                if (thickest < MinRow) { dropped++; tPfThin++; p0 = p1 + 1; continue; }
                if (cut) clipped++;

                var poly = new List<Point3>(2 * m);
                for (int i = 0; i < m; i++) poly.Add(qb[i]);
                for (int i = m - 1; i >= 0; i--) poly.Add(qt[i]);

                // 앵커는 격자 자리를 지킨다 — 칸 한가운데, 행 한가운데. 조각이 그 자리를 품을 때만 놓는다.
                double vAnchor = j * FaceTile + FaceTile / 2;
                double sMid = (sa + sb) / 2;
                bool room = sMid >= sv[0] - 1e-9 && sMid <= sv[m - 1] + 1e-9;
                // ★★★[검토 심각5] **프레임은 그 조각 안에서 뽑는다.** 한 행이 두 조각으로 갈리면
                //   sMid는 <b>죽은 가운데</b>라, 거기서 W·V를 받으면 조각이 없는 자리의 방향을 쓰는 셈이다
                //   (오늘 반복된 그 패턴). 앵커 자리만 격자를 지키면 되고, 방향은 조각 자신에게 물어야 한다.
                double sFrame = room ? sMid : (sv[0] + sv[m - 1]) / 2;
                var rm = At(sMid);
                if (room)
                    room = vAnchor - PocketHalf >= System.Math.Max(v0, rm.lo * rm.hgt) - 1e-9
                        && vAnchor + PocketHalf <= System.Math.Min(v1, rm.hi * rm.hgt) + 1e-9;
                var SM2 = SecAt(sec, cum, sFrame);
                var nM = Norm3(SM2[0].X - SM2[3].X, SM2[0].Y - SM2[3].Y, SM2[0].Z - SM2[3].Z);
                // ★★★[JACK 0820 '무늬가 불특정하게 누락'] 기준점은 **언제나 격자 자리(sMid)** 다.
                //   앵커가 안 붙는 판에서도 이 점이 무늬 칸의 중심이 된다 — 조각 가운데로 옮기면
                //   잘린 판마다 무늬 격자가 제자리를 벗어나 이어지지 않는다.
                //   (프레임 방향만 조각 안에서 뽑는다 — 그건 죽은 자리의 방향을 쓰면 안 되기 때문이다.)
                double tA = rm.hgt > 1e-9 ? System.Math.Clamp(vAnchor / rm.hgt, 0, 1) : 0;
                var aCtr = new Point3(rm.bot.X + (rm.top.X - rm.bot.X) * tA + (nM.ok ? nM.x : 0) * FaceProud,
                                      rm.bot.Y + (rm.top.Y - rm.bot.Y) * tA + (nM.ok ? nM.y : 0) * FaceProud,
                                      rm.bot.Z + (rm.top.Z - rm.bot.Z) * tA + (nM.ok ? nM.z : 0) * FaceProud);

                // 사면 방향(토우→크레스트)은 판이 어떻게 잘리든 성립한다 — 프레임의 세로축을 여기서 얻는다.
                var rf = At(sFrame);
                var slope = Norm3(rf.top.X - rf.bot.X, rf.top.Y - rf.bot.Y, rf.top.Z - rf.bot.Z);
                if (AddFace(poly, nM, room, aCtr, slope)) made++;
                else { dropped++; tPfFrame++; }                        // 조용히 버리지 않는다(검토 심각5)
                p0 = p1 + 1;
            }
        }
        return made;
    }



    /// <summary>네 점을 기존 판넬 자료형으로 만들어 담는다 — 무늬·도넛·앵커·정착판이 그대로 붙는다.</summary>
    /// <summary>정착구 보호공 반폭(m) — 이만큼 위아래로 자리가 나야 앵커를 놓는다(JACK 0820).</summary>
    public const double PocketHalf = 0.40;

    /// <summary>도넛 1단 반폭(m) — <c>WallPanelDwg.Collar1Size</c>(0.56)의 절반. 앵커가 판 안에 드는지 검사하는 자.
    /// <para>두 곳에 같은 숫자가 살면 언젠가 갈라진다 — 여기서 한 번만 정하고 검사는 이 값을 쓴다.</para></summary>
    public const double Collar1Half = 0.28;

    private static bool AddFace(IReadOnlyList<Point3> poly,
                                (bool ok, double x, double y, double z) nrm,
                                bool withAnchor, Point3 anchorAt,
                                (bool ok, double x, double y, double z) slope)
    {
        if (poly.Count < 4) return false;
        Point3 r00 = poly[0], r10 = poly[1];
        // ★★[0820 실측 'eCannotScaleNonUniformly'] 프레임은 **직교 단위축**이어야 렌더러가 판을 만든다.
        // ★★★[JACK 0820 '객체별로 돌아가게 생성됨'] **프레임을 판이 아니라 벽에서 가져온다.**
        //   종전엔 세로축을 <b>판의 왼쪽 변</b>(r00→마지막 점)에서 뽑았다. 데이라잇이 판을 비스듬히 가로지르면
        //   그 변이 거의 0이 되거나 가로축과 나란해져 <b>방향이 좌표 잡음</b>이 된다 — 그 판만 제멋대로 돈다.
        //   오늘 반복된 그 실수다: <b>퇴화한 것에서 방향을 묻는 것</b>
        //   (벽 끝 두께방향 · 코너 단면 법선 · 벽 전체 첫 단면 · 그리고 이것).
        //   → 벽면의 <b>바깥 법선(W)</b>과 <b>사면 방향(토우→크레스트)</b>은 판이 어떻게 잘리든 언제나 성립한다.
        //     거기서 프레임을 만들고, 판의 모양은 <b>로컬 좌표</b>가 받는다(그게 로컬 좌표의 일이다).
        if (!nrm.ok || !slope.ok) return false;
        var ww = nrm;
        double sw = slope.x * ww.x + slope.y * ww.y + slope.z * ww.z;
        var vv = Norm3(slope.x - sw * ww.x, slope.y - sw * ww.y, slope.z - sw * ww.z);
        if (!vv.ok) return false;
        // U = V × W — 면 안에 있고 V와 직각. 판의 진행 방향(r00→r10)과 맞도록 부호를 고른다.
        var uu = Norm3(vv.y * ww.z - vv.z * ww.y, vv.z * ww.x - vv.x * ww.z, vv.x * ww.y - vv.y * ww.x);
        if (!uu.ok) return false;
        if ((r10.X - r00.X) * uu.x + (r10.Y - r00.Y) * uu.y + (r10.Z - r00.Z) * uu.z < 0)
        {
            uu = (true, -uu.x, -uu.y, -uu.z);
            vv = (true, -vv.x, -vv.y, -vv.z);       // 오른손 좌표계 유지(W는 바깥 그대로)
        }
        var uvs = new List<(double u, double v)>(poly.Count);
        foreach (var q in poly)
        {
            double dx = q.X - r00.X, dy = q.Y - r00.Y, dz = q.Z - r00.Z;
            uvs.Add((dx * uu.x + dy * uu.y + dz * uu.z, dx * vv.x + dy * vv.y + dz * vv.z));
        }
        double uMin = double.MaxValue, vMin = double.MaxValue;
        foreach (var t in uvs) { uMin = System.Math.Min(uMin, t.u); vMin = System.Math.Min(vMin, t.v); }
        var local = new List<(double u, double v)>(uvs.Count);
        foreach (var t in uvs) local.Add((t.u - uMin, t.v - vMin));

        // ★★★[JACK 0820 '코너부 무늬 누락' — 로그: 무늬없음 2[분해실패 2]]
        //   **중복·공선 정점을 없앤다.** 데이라잇이 벽을 죽이는 자리에서 판이 쐐기 모양이 되면
        //   아랫점과 윗점이 <b>같은 자리</b>가 된다(두께 0). 그런 다각형은 귀 자르기(ConvexPieces)가
        //   못 쪼개고 빈 목록을 돌려주며, 호출부는 종전대로 <b>무늬를 통째로 건너뛴다</b> — 그게 코너부 민판이다.
        //   옛 판넬 경로는 이미 <c>Simplify</c>를 부르고 있었는데(이 파일의 판넬 조립부) 새 경로에 안 따라왔다.
        //   ※ 정리는 <b>로컬 좌표에서</b> 하고 3D 점을 거기서 다시 만든다 — 렌더러가 실제로 쓰는 것이
        //     <c>Local</c>이므로, 둘을 따로 두면 개수가 어긋나 진단이 거짓말을 시작한다.
        var kept = Simplify(local);
        if (kept.Count < 3) return false;
        // ★ 남은 점의 **원래 3D 좌표를 그대로** 가져온다. 평면으로 다시 만들면 안 된다 —
        //   판의 네 점은 (조금 휜) 벽면 위에 있어 프레임의 W 성분을 갖는데, 그걸 버리고 재구성하면
        //   판이 벽에서 통째로 밀려난다(실측 0.7m). Simplify는 점을 **지우기만** 하므로 값으로 되찾을 수 있다.
        var quad = new List<Point3>(kept.Count);
        int scan = 0;
        foreach (var t in kept)
        {
            while (scan < local.Count &&
                   (System.Math.Abs(local[scan].u - t.u) > 1e-12 || System.Math.Abs(local[scan].v - t.v) > 1e-12)) scan++;
            if (scan >= local.Count) return false;                 // 못 되찾음 — 조용히 어긋난 판을 내보내지 않는다
            quad.Add(poly[scan]);
            scan++;
        }
        local = kept;
        // ★★★[검토 심각3] **Origin은 판 앞면이 아니라 두께 한가운데다.**
        //   렌더러(WallPanelDwg)는 Origin에서 <c>+W·FrontOut</c>(0.10)만큼 나간 자리를 <b>앞면</b>으로 잡는다
        //   (기존 판넬은 org가 옹벽선 위의 토우점이라 슬래브가 옹벽선 기준 [−0.10,+0.10]으로 딱 맞았다).
        //   내 판 네 점은 <b>매스 바깥면</b>(옹벽선 +0.10)에 있으므로, 그걸 그대로 Origin으로 주면
        //   렌더러가 +0.10을 또 얹어 무늬가 <b>매스에서 10cm 떠 버린다</b>(몸통을 없앤 지금은 허공에 뜬다).
        //   → 옹벽선 자리까지 물려서 준다. 그러면 앞면이 정확히 매스 바깥면과 같아진다.
        var org = new Point3(r00.X + uu.x * uMin + vv.x * vMin - ww.x * PanelFrontOut,
                             r00.Y + uu.y * uMin + vv.y * vMin - ww.y * PanelFrontOut,
                             r00.Z + uu.z * uMin + vv.z * vMin - ww.z * PanelFrontOut);
        // 앵커 자리 — **격자가 정한 점**을 이 판의 로컬 좌표로 옮긴다(판 한가운데가 아니다).
        double cu, cv;
        {
            double dx = anchorAt.X - r00.X, dy = anchorAt.Y - r00.Y, dz = anchorAt.Z - r00.Z;
            cu = dx * uu.x + dy * uu.y + dz * uu.z - uMin;
            cv = dx * vv.x + dy * vv.y + dz * vv.z - vMin;
        }
        // ★★★[검토 심각2] **도넛·앵커가 판 안에 들어가는지 확인한다** — v13.9에서 이미 고쳤던 검사인데
        //   새 경로에 안 따라왔다(이 파일 §"새로 짜면 옛 코드의 수정이 자동으로 따라오지 않는다").
        //   데이라잇이 행을 두 조각으로 가르고 그 경계가 앵커 자리 바로 옆이면, 폭이 몇 mm인 조각도
        //   'sMid를 품는다'는 이유로 통과해 도넛 0.28m와 앵커봉이 <b>허공에 뜬다</b>.
        if (withAnchor)
        {
            const double collarHalf = Collar1Half;
            withAnchor = PointInPoly(cu, cv, local)
                      && PointInPoly(cu - collarHalf, cv - collarHalf, local)
                      && PointInPoly(cu + collarHalf, cv - collarHalf, local)
                      && PointInPoly(cu + collarHalf, cv + collarHalf, local)
                      && PointInPoly(cu - collarHalf, cv + collarHalf, local);
        }
        // 무늬는 잘린 판도 받는다(Detail). 앵커·도넛만 자리가 날 때 붙는다(IsFull).
        // ★[JACK 0820] Overlay — **몸통은 안 만든다.** 매스가 이미 판넬이고 여기서는 마감만 얹는다.
        LastFacePanels.Add(ToPanel(new Tile(quad, withAnchor, org,
            (uu.x, uu.y, uu.z), (vv.x, vv.y, vv.z), (ww.x, ww.y, ww.z), local, cu, cv, 0, true,
            Row: 0, Filler: false, Detail: true))
            with { Overlay = true, CellU = FaceTile - JointW, CellV = FaceTile - JointW });
        return true;
    }

    /// <summary>토우(lo)에서 크레스트(hi)로 가는 벽면 위에서 높이 v인 점 — 바깥으로 <see cref="FaceProud"/>만큼 내민다.</summary>
    private static Point3 OnFace(Point3 lo, Point3 hi, double h, double v,
                                 (bool ok, double x, double y, double z) n)
    {
        double t = h > 1e-9 ? v / h : 0;
        return new Point3(lo.X + (hi.X - lo.X) * t + n.x * FaceProud,
                          lo.Y + (hi.Y - lo.Y) * t + n.y * FaceProud,
                          lo.Z + (hi.Z - lo.Z) * t + n.z * FaceProud);
    }

    private static Point3 Toward(Point3 p, Point3 to, double d)
    {
        double L = Dist3(p, to);
        if (L < 1e-9) return p;
        double t = d / L;
        return new Point3(p.X + (to.X - p.X) * t, p.Y + (to.Y - p.Y) * t, p.Z + (to.Z - p.Z) * t);
    }

    private static Point3 Lerp3(Point3 a, Point3 b, double t)
        => new Point3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    private static double Dist3(Point3 a, Point3 b)
        => System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    private static (bool ok, double x, double y, double z) Norm3(double x, double y, double z)
    {
        double l = System.Math.Sqrt(x * x + y * y + z * z);
        return l < 1e-9 ? (false, 0, 0, 0) : (true, x / l, y / l, z / l);
    }

    /// <summary>그 자리를 <b>코너 전용 판넬이 이미 메우고 있는가</b> — 틈 판정·틈 메우기 둘 다 이걸 봐야 한다.
    /// <para>안 보면 유닛이 채운 자리를 '구멍'으로 오독하고, 그 위에 필러까지 세워 또 뭉친다.
    /// 0806에 코너 필러를 안 보고 같은 오독을 했다 — 같은 실수를 두 번 하지 않으려고 함수로 뺐다.</para></summary>
    public static bool CornerUnitCovers(double x, double y, double z, double radius)
    {
        foreach (var cu in LastCornerUnits)
        {
            if (cu.Bot.Count == 0 || cu.Top.Count == 0) continue;
            double lo = cu.Bot[0].Z, hi = cu.Top[0].Z;
            if (z < lo - 1.0 || z > hi + 1.0) continue;                 // 높이가 겹치지 않으면 남
            foreach (var p in cu.Bot)
            {
                double dx = p.X - x, dy = p.Y - y;
                if (dx * dx + dy * dy <= radius * radius) return true;
            }
        }
        return false;
    }

    /// <summary>★★[JACK 0807 '각진부 마감을 깔끔하게 할 수 없나'] <b>코너 전용 판넬의 평면 단면(ㄱ자)</b>.
    /// <para>
    /// 지금은 양쪽 판넬이 각자 자기 평면으로 코너를 지나쳐 나가 서로 파고들고, 그 위에 필러까지 얹혀
    /// 세 덩어리가 뭉친다("막았다"이지 "마감했다"가 아니다 — JACK 스샷).
    /// 코너에서 양옆 판넬을 <see cref="CornerLeg"/>만큼 물러나게 하고, 그 자리를 감싸는 유닛 하나를 세운다.
    /// 유닛의 두 노출면은 각각 이웃 판넬의 전면과 <b>같은 평면</b>이라 이어 붙으면 한 면처럼 보인다.
    /// </para>
    /// 불리언 연산이 <b>0회</b>다 — 단면을 만들어 압출만 한다. 이 저장소의 모델링 오류(115094)는 전부
    /// 불리언에서 났으므로, 마이터 컷(코너당 불리언 2회 × 행)보다 이 길이 훨씬 안전하다.
    /// <para>반환은 평면 다각형(바깥면 → 안쪽면 순). 코너가 거의 직선이면 빈 목록(유닛 불필요).</para>
    /// </summary>
    /// <param name="corner">벽선 위 코너 점.</param>
    /// <param name="dirA">코너로 <b>들어오는</b> 벽면 A의 진행 방향(단위, 수평).</param>
    /// <param name="dirB">코너에서 <b>나가는</b> 벽면 B의 진행 방향(단위, 수평).</param>
    /// <param name="nA">A의 노출면 바깥 법선(단위, 수평).</param>
    /// <param name="nB">B의 노출면 바깥 법선(단위, 수평).</param>
    /// <param name="leg">양옆 판넬이 물러난 거리.</param>
    /// <param name="thick">판넬 두께.</param>
    /// <param name="front">판넬 전면 돌출(부지쪽).</param>
    public static List<(double x, double y)> CornerUnitProfile(
        (double x, double y) legEndA, (double x, double y) legEndB,
        (double x, double y) dirA, (double x, double y) dirB,
        (double x, double y) nA, (double x, double y) nB,
        double thick, double front)
    {
        var outp = new List<(double x, double y)>();
        double cross = dirA.x * dirB.y - dirA.y * dirB.x;
        double dot = dirA.x * dirB.x + dirA.y * dirB.y;
        if (System.Math.Abs(cross) < 0.05 && dot > 0) return outp;     // 3도 미만 꺾임 — 코너가 아니다

        static bool Meet((double x, double y) p, (double x, double y) d,
                         (double x, double y) q, (double x, double y) e, out (double x, double y) r)
        {
            double den = d.x * e.y - d.y * e.x;
            r = default;
            if (System.Math.Abs(den) < 1e-9) return false;
            double t = ((q.x - p.x) * e.y - (q.y - p.y) * e.x) / den;
            r = (p.x + d.x * t, p.y + d.y * t);
            return true;
        }

        // 다리 끝 — 이웃 판넬 전면/뒷면과 **같은 평면** 위의 점.
        var aOut = (x: legEndA.x + nA.x * front, y: legEndA.y + nA.y * front);
        var aIn = (x: legEndA.x + nA.x * (front - thick), y: legEndA.y + nA.y * (front - thick));
        var bOut = (x: legEndB.x + nB.x * front, y: legEndB.y + nB.y * front);
        var bIn = (x: legEndB.x + nB.x * (front - thick), y: legEndB.y + nB.y * (front - thick));

        if (!Meet(aOut, dirA, bOut, dirB, out var pOut)) return outp;
        if (!Meet(aIn, dirA, bIn, dirB, out var pIn)) return outp;

        // 교점이 다리 끝에서 지나치게 멀면(아주 예각) 단면이 뒤집힌다 — 그런 자리는 유닛을 안 만든다.
        double la = System.Math.Sqrt((pOut.x - aOut.x) * (pOut.x - aOut.x) + (pOut.y - aOut.y) * (pOut.y - aOut.y));
        double lb = System.Math.Sqrt((pOut.x - bOut.x) * (pOut.x - bOut.x) + (pOut.y - bOut.y) * (pOut.y - bOut.y));
        if (la > 3.0 || lb > 3.0) return outp;

        outp.Add(aOut); outp.Add(pOut); outp.Add(bOut);      // 노출면 쪽
        outp.Add(bIn); outp.Add(pIn); outp.Add(aIn);         // 뒷면 쪽
        var chk = new List<(double u, double v)>(outp.Count);
        foreach (var q2 in outp) chk.Add((q2.x, q2.y));
        if (PolyArea(chk) < 1e-4 || !IsSimple(chk)) outp.Clear();
        return outp;
    }

    /// <summary>수평 벡터 정규화 — 코너 유닛 단면에 쓰는 작은 도우미.</summary>
    private static (bool ok, double x, double y) Norm2(double x, double y)
    {
        double l = System.Math.Sqrt(x * x + y * y);
        return l < 1e-9 ? (false, 0, 0) : (true, x / l, y / l);
    }

    /// <summary>판넬 두께·전면 돌출 — <c>WallPanelDwg</c>의 같은 값과 <b>반드시 일치해야</b> 코너 유닛이 이웃 판넬과 맞물린다.</summary>
    public const double PanelThick = 0.20;
    public const double PanelFrontOut = 0.10;

    /// <summary>★★★[JACK 0819 '각도로 접근하는 방법은 버려 · 각도를 기준으로 하면 언젠간 경우에 따라 오류가 날 것 같은데']
    /// <b>판넬이 옹벽선에서 자기 두께만큼 벗어나면 그 자리엔 판넬을 깔지 않는다.</b>
    /// <para>
    /// 종전엔 코너를 <b>각도로</b> 찾았다(<c>SplitAtCorners</c> 12°). 그래서 직각은 되고 라운드는 안 됐다 —
    /// 원호는 사분면당 8조각이라 <b>조각당 11.25°</b>로 문턱에 안 걸리고, 벽면이 하나면 쐐기가 설 자리가 없다.
    /// 문턱을 낮추면 완만한 곡선까지 잘리고, 올리면 진짜 코너를 놓친다 — 이 저장소가 고쳐 온 '자'가
    /// 12°·45°·8°·3°·0.30m이고 <b>각도를 하나 더 얹으면 여덟 번째</b>다.
    /// </para>
    /// 그래서 각도를 안 쓴다. 기준은 <b>판넬 자신에게서 나온 길이</b>다 —
    /// 판넬이 자기 두께만큼 벽선에서 벗어났다면 그 판넬은 이미 벽 밖이고, 각도가 몇 도인지는 알 필요가 없다.
    /// 직각 90°든 라운드 11.25°씩이든 <b>같은 자에 걸리므로 모드 분기가 코드에서 사라진다.</b></summary>
    public const double WedgeDev = PanelThick;

    /// <summary>★[JACK 0819 '직선부분만 인식해서 그 부분만 패널 배열하고 나머지는 한 덩어리 스윕'] 이탈 분포 눈금(m).
    /// <b>문턱을 어디에 둘지는 숫자를 보고 정한다</b> — 0.05/0.10/0.20 세 칸만 있을 땐 '무엇이 진짜 직선인지'가 안 보였다.
    /// 첫 칸(1mm)은 설계값이 아니라 <b>수치 잡음 한계</b>다: 여기 안 걸리는 열이 곧 '완전한 직선'이다.</summary>
    public static readonly double[] DevBuckets = { 0.001, 0.01, 0.02, 0.05, 0.10, 0.20 };

    /// <summary>★★[JACK 0807 '또 중간에 틈이 있어 — 자꾸 발생하는 걸 보니 근본 원인이 있다'] <b>그 근본 원인.</b>
    /// <para>
    /// 판넬 사이 틈을 재는 자의 최소 눈금이 <b>0.30m</b>였다. 그래서 <b>29cm까지의 틈은 자에 안 잡혔고</b>,
    /// 틈을 메우는 쪽(<see cref="AddGapFillers"/>)도 같은 눈금을 써서 <b>메우지도 않았다.</b>
    /// 설계 줄눈이 0.05m이니 0.29m는 줄눈의 <b>여섯 배</b> — 도면에서는 대놓고 보인다.
    /// 로그가 매번 '진짜 구멍 0곳'이라고 하는데 JACK 스샷에는 틈이 있던 이유가 이것이다.
    /// </para>
    /// 0.30은 '설계 줄눈(0.05)을 틈으로 세지 않으려고' 넉넉히 잡은 값이었는데, 넉넉함이 여섯 배였다.
    /// 기준은 임의의 숫자가 아니라 <b>줄눈에서 나와야 한다</b>: 줄눈의 두 배를 넘으면 틈이다.
    /// <para>이 저장소에서 자를 고친 것이 벌써 일곱 번째다 — 그리고 이번에도 자가 먼저였다.</para>
    /// </summary>
    public const double GapTol = 0.12;   // 설계 줄눈 0.05의 2.4배 — 이보다 벌어지면 눈에 보인다

    /// <summary>★[JACK 0807] 설계 줄눈(m) — "패널 사이의 간격은 <b>어떠한 경우에도 5cm</b>를 유지하게 해."
    /// 판넬 사이·판넬과 필러 사이 모두 이 값이다. 조각을 만들 수 있는 최소 크기도 여기서 나온다.</summary>
    public const double JointW = 0.05;

    /// <summary>조각 하나가 성립하는 최소 벽면 길이 = 판넬 최소폭(0.05) + 줄눈(0.05).
    /// 이보다 짧은 자투리는 <b>따로 세우지 않고 앞 조각에 합친다</b> — 세우면 줄눈 인셋에 죽어
    /// 그 자리가 통째로 구멍이 되고(현장 실측 '줄눈 39개 버림 · 벽 한가운데 구멍 0.38m'),
    /// 구멍은 곧 5cm가 아닌 간격이 된다.</summary>
    public const double MinPieceLen = 0.10;

    /// <summary>★[JACK 0807] 전용 얇은 객체라도 <b>규격 폭의 이 비율 이상</b>이면 LOD를 규격 판넬과 같게 올린다
    /// (앵커·도넛·무늬를 그대로 붙인다). "만약 폭이 70% 이상 되는 판넬은 LOD를 옹벽패널과 같이 올려."
    /// <para>전용 객체가 LOD를 포기하는 건 폭이 좁아 앵커보호공(0.56m)도 못 물기 때문이다.
    /// 규격의 70%(≈1.17m)면 충분히 물리므로 민판으로 둘 이유가 없고, 오히려 벽 한가운데
    /// 넓은 민판이 섞여 더 눈에 띈다.</para></summary>
    /// <remarks>[JACK 0807] 0.70 → <b>0.60</b>. 규격의 60%면 ≈1.00m로, 앵커보호공(0.56m)에 양옆 여유까지
    /// 충분히 들어간다. 문턱을 내릴수록 민판으로 남는 조각이 줄어 벽이 고르게 보인다.</remarks>
    public const double FullLodRatio = 0.60;

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

    /// <summary>★[JACK 0819] 한 단을 몇 행으로 나눌지 — <b>한 변이 <see cref="MaxSide"/>를 넘지 않는 최소 행 수</b>.
    ///
    /// <para>종전은 계단식(<c>≤1m→1 / ≤3m→2 / 그 이상→3</c>)이었는데 <b>낮은 단에서 어긋났다</b> —
    /// (예: 1.6m 단이 0.8m 두 장). 나눗셈 하나로 바꾸면 <b>1.5m 이하는 저절로 한 장</b>이 되고,
    /// 큰 단도 상한이 저절로 지켜진다.</para>
    ///
    /// <para>★★[치명 0805 · 검토 0819] <b>여유가 필요하고, 그 크기가 자리를 정한다.</b>
    /// <c>height</c>는 설계값이 아니라 <b>이웃 두 링의 평균 Z 차이</b>(측정값)라 설계 단높이보다 조금 크게 나온다
    /// (실측 5.0002m · 하니스는 5cm 편차까지 모델링한다). 여유가 없으면 <c>3.0m</c> 설계 부지가
    /// 측정 <c>3.02m</c>로 잡히는 순간 2행이 <b>3행</b>이 되어 판넬이 33% 작아진다 — 같은 부지에서 단마다 크기가 달라진다.</para>
    ///
    /// <para><b>여유는 5cm.</b> 이 값이 '행이 하나 늘어나는 절벽'을 <c>1.5n + 0.05</c>에 놓는다 —
    /// 사람이 고르는 라운드 단높이(1.5·3.0·4.5·6.0·7.5·15)에서 <b>5cm 떨어진 자리</b>다.
    /// 1cm로 잡으면 절벽이 그 값들 <b>바로 위</b>에 붙어, 측정이 조금만 흔들려도 판넬 크기가 튄다
    /// (종전 0.5m는 상한이 5m이던 시절 값이라 우연히 라운드값을 피해 있었다).</para></summary>
    public static int RowsForBench(double height)
    {
        double h = System.Math.Abs(height);
        const double heightSlack = 0.05;
        return System.Math.Max(1, (int)System.Math.Ceiling((h - heightSlack) / MaxSide - 1e-9));
    }

    /// <summary>판넬 한 변 — <b>단높이 ÷ 행 수</b>, <see cref="MaxSide"/>로 자른다.
    ///
    /// <para>자르는 이유: 위 여유(5cm) 때문에 나눗셈 결과가 상한을 <b>조금 넘을 수 있다</b>
    /// (예 <c>1.55m → 1행 → 1.55m</c>). <see cref="MaxSide"/>는 <b>제작 규격</b>이라 넘으면 안 되므로 여기서 막는다 —
    /// 세로 행 높이는 그대로 두고 <b>가로 폭만</b> 규격에 맞춘다.</para>
    ///
    /// <para>검산: <c>1m→1장(1.00)</c> · <c>1.5m→1장(1.50)</c> · <c>3m→2장(1.50)</c> ·
    /// <c>5m→4장(1.25)</c> · <c>10m→7장(1.43)</c> · <c>15m→10장(1.50)</c></para></summary>
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
        int Row = 0,
        /// <summary>★[JACK 0807] 이 조각이 <b>규격 판넬</b>이 아니라 <b>자투리 전용 얇은 객체</b>인가.
        /// <para>JACK 원칙: "단에 따라 패널의 높이와 폭은 정해진다. 그 원칙은 지키되, 배열할 때 패널 폭이
        /// 제각각 달라지는 건 절대 하지 말고, 부족하면 얇은 거 전용객체 하나 만들어서 넣는다
        /// (이때 LOD는 포기, 재질만 통일)."</para>
        /// 그래서 규격 판넬은 <b>언제나 정확히 한 변</b>이고, 벽면 끝에 남는 자투리와 규격보다 짧은
        /// 벽면(라운드 코너 조각 등)만 이 표시를 달고 나온다 — 앵커·도넛·무늬 없이 재질만 같게.
        /// 데이라잇 자르기는 규격 판넬과 <b>똑같이</b> 적용된다(따로 두면 그 자리만 안 잘려 삐져나온다).</summary>
        bool Filler = false,
        /// <summary>★[JACK 0807] 앵커·도넛·무늬를 붙이는가(LOD). <see cref="Filler"/>와 <b>분리해서</b> 둔다 —
        /// Filler는 '규격 폭이 아니다'라는 **분류**이고, 이건 '어떻게 그리느냐'는 **표현**이다.
        /// 둘을 한 값으로 묶었더니 'LOD 70% 승격'이 분류까지 바꿔 버려, 규격보다 넓은 조각이
        /// '규격 판넬'로 세어지면서 JACK 원칙(규격 판넬 폭은 언제나 같다)이 깨졌다(하니스 S35가 잡았다).</summary>
        bool Detail = true);

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
            t.Origin, t.UAxis, t.VAxis, t.WAxis, t.Local, t.PocketU, t.PocketV, t.Filler, t.Detail);
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

    /// <summary>★★[JACK 0807 '잘린 걸 최종 단계에서 무늬에 한해서만 서로 붙은 객체는 합친다'] <b>무늬 사각형 ∩ 판넬</b>
    /// — 가능하면 <b>한 조각</b>으로 돌려준다.
    /// <para>
    /// 무늬 클립(Sutherland–Hodgman)은 <b>창</b>이 볼록해야 옳다. 종전엔 그래서 <b>판넬을 볼록 조각으로 쪼개
    /// 창으로 삼았고</b>, 무늬 사각형 하나가 조각 수만큼 잘려 한 점에서 부챗살처럼 퍼졌다(JACK 스샷).
    /// </para>
    /// 그런데 <b>역할을 바꾸면 쪼갤 필요가 없다</b>: 무늬 사각형은 언제나 축에 나란한 <b>직사각형=볼록</b>이므로
    /// 그것을 창으로 삼고 <b>판넬을 잘라도</b> 결과는 같은 교집합이다. 창이 볼록하니 클립도 옳고,
    /// 조각이 하나로 나와 이음매가 <b>아예 안 생긴다</b>.
    /// <para>
    /// 다만 교집합이 <b>끊어질</b> 수 있다(판넬 노치가 사각형을 가로지르는 드문 경우). 그때 SH는 한 고리에
    /// 실오라기 다리를 만들어 자기교차가 되므로, 결과를 검사해 이상하면 <b>종전 방식(볼록 조각별 클립)으로
    /// 물러난다</b>. 이 저장소가 세 번 쓴 처방과 같다 — 빠른 길 먼저, 실패하면 낱개로.
    /// 물러난 결과는 <b>종전과 완전히 같으므로</b> 이 수정이 지금보다 나빠질 수는 없다.
    /// </para></summary>
    /// <param name="merged">true면 한 조각으로 합쳐진 것(이음매 없음), false면 종전 방식으로 물러난 것.</param>
    public static List<List<(double u, double v)>> RectClip(
        IReadOnlyList<(double u, double v)> face,
        double a, double b, double c, double d, out bool merged)
    {
        merged = false;
        var outp = new List<List<(double u, double v)>>();
        if (face == null || face.Count < 3 || b - a <= 1e-9 || d - c <= 1e-9) return outp;

        // ── 빠른 길: 판넬을 '사각형'이라는 볼록 창으로 자른다(반평면 4번) ──
        var cur = new List<(double u, double v)>(face);
        cur = ClipHalf(cur, 1, 0, a);      //  u ≥ a
        cur = ClipHalf(cur, -1, 0, -b);    //  u ≤ b
        cur = ClipHalf(cur, 0, 1, c);      //  v ≥ c
        cur = ClipHalf(cur, 0, -1, -d);    //  v ≤ d
        cur = Simplify(cur);
        if (cur.Count >= 3 && PolyArea(cur) > 1e-12 && IsSimple(cur))
        {
            merged = true;
            outp.Add(cur);
            return outp;
        }

        // ── 물러나기: 종전대로 판넬을 볼록 조각으로 쪼개고 조각마다 사각형을 자른다 ──
        foreach (var win in ConvexPieces(face))
        {
            var r = new List<(double u, double v)> { (a, c), (b, c), (b, d), (a, d) };
            var q = ClipToConvex(r, win);
            q = Simplify(q);
            if (q.Count >= 3 && PolyArea(q) > 1e-12) outp.Add(q);
        }
        return outp;
    }

    /// <summary>반평면 nx·u + ny·v ≥ off 로 자르기(Sutherland–Hodgman 한 단계).</summary>
    private static List<(double u, double v)> ClipHalf(
        IReadOnlyList<(double u, double v)> poly, double nx, double ny, double off)
    {
        var res = new List<(double u, double v)>(poly.Count + 4);
        int n = poly.Count;
        if (n == 0) return res;
        for (int i = 0; i < n; i++)
        {
            var P = poly[i]; var Q = poly[(i + 1) % n];
            double dp = nx * P.u + ny * P.v - off, dq = nx * Q.u + ny * Q.v - off;
            if (dp >= -1e-12) res.Add(P);
            if ((dp > 1e-12 && dq < -1e-12) || (dp < -1e-12 && dq > 1e-12))
            {
                double t = dp / (dp - dq);
                res.Add((P.u + (Q.u - P.u) * t, P.v + (Q.v - P.v) * t));
            }
        }
        return res;
    }

    /// <summary>볼록 창으로 자르기 — 창의 각 변을 반평면으로 본다(창은 반드시 볼록이어야 옳다).</summary>
    private static List<(double u, double v)> ClipToConvex(
        IReadOnlyList<(double u, double v)> subj, IReadOnlyList<(double u, double v)> win)
    {
        if (win == null || win.Count < 3) return new List<(double u, double v)>();
        bool ccw = Area2(win) > 0;
        var cur = new List<(double u, double v)>(subj);
        for (int i = 0; i < win.Count && cur.Count > 0; i++)
        {
            var A = win[i]; var B = win[(i + 1) % win.Count];
            double ex = B.u - A.u, ey = B.v - A.v;
            double nx = ccw ? -ey : ey, ny = ccw ? ex : -ex;      // 창 안쪽을 향하는 법선
            cur = ClipHalf(cur, nx, ny, nx * A.u + ny * A.v);
        }
        return cur;
    }

    /// <summary>단순 다각형인가(자기교차·중복 정점 없음) — 무늬 클립 결과의 안전 검사.
    /// 정점이 20개 안팎이라 O(n²)이어도 무시할 비용이고, 여기서 걸러야 압출이 115094로 터지지 않는다.</summary>
    public static bool IsSimple(IReadOnlyList<(double u, double v)> p)
    {
        int n = p?.Count ?? 0;
        if (n < 3) return false;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (System.Math.Abs(p[i].u - p[j].u) < 1e-9 && System.Math.Abs(p[i].v - p[j].v) < 1e-9)
                    return false;                                  // 같은 점이 두 번 = 실오라기 다리
        for (int i = 0; i < n; i++)
        {
            var a1 = p[i]; var a2 = p[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                if (j == i || (j + 1) % n == i || j == (i + 1) % n) continue;   // 이웃 변은 끝점을 공유한다
                var b1 = p[j]; var b2 = p[(j + 1) % n];
                if (SegCross(a1, a2, b1, b2)) return false;
            }
        }
        return true;
    }

    private static bool SegCross((double u, double v) a1, (double u, double v) a2,
                                 (double u, double v) b1, (double u, double v) b2)
    {
        double D(( double u, double v) o, (double u, double v) x, (double u, double v) y)
            => (x.u - o.u) * (y.v - o.v) - (x.v - o.v) * (y.u - o.u);
        double d1 = D(b1, b2, a1), d2 = D(b1, b2, a2), d3 = D(a1, a2, b1), d4 = D(a1, a2, b2);
        return ((d1 > 1e-12 && d2 < -1e-12) || (d1 < -1e-12 && d2 > 1e-12))
            && ((d3 > 1e-12 && d4 < -1e-12) || (d3 < -1e-12 && d4 > 1e-12));
    }

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

    /// <summary>[하네스 전용] 코너 전용 유닛을 꺼서 종전 동작(코너 필러만)으로 되돌린다 — 자가검증용.</summary>
    public static bool DisableCornerUnitForTest;
    /// <summary>★[JACK 0819] 쐐기 규칙(<see cref="WedgeDev"/>)을 끈다 — <b>하니스 자체검증 전용</b>.
    /// 이것이 세 번째 방어라, 켜 둔 채로는 앞의 두 방어를 꺼도 판넬이 멀쩡해 보여
    /// '고장을 일부러 내는' 시험이 무력해진다(S24가 실제로 그렇게 죽었다).</summary>
    public static bool DisableWedgeForTest;

    /// <summary>★★★[JACK 0819 '그냥 옹벽 자체를 하나의 매스로 스윕해서 만들고' · '먼저 아무 무늬 없는
    /// 덩어리 스윕만 되게'] <b>판넬을 깔지 않고 벽 전체를 옹벽선 스윕 덩어리로 만든다.</b>
    /// <para>
    /// 목적이 <i>"인프라웍스에서 패널식 옹벽이라는 게 느껴지면 된다"</i>(기본설계 · 수량 불필요)라면,
    /// 판넬 배치가 붙들고 있던 문제 — 코너에서 어디서 멈출까 · 쐐기를 어떻게 채울까 · 폭/자투리/줄눈/이탈 —
    /// 가 <b>전부 사라진다</b>. 스윕 경로가 옹벽선 자체라 원호든 직각이든 그냥 따라가고,
    /// 데이라잇 절단도 '조각에 높이가 있냐 없냐' 한 가지로 줄어든다.
    /// </para>
    /// 단면은 JACK이 올린 스샷 그대로 <b>기울어진 사각형</b>(평행사변형)이다 — 아래는 토우, 위는 크레스트,
    /// 두 선의 수평 차이가 곧 구배(1:0.05)×단높이다. 그래서 <b>높이를 따로 계산하지 않는다</b>.
    /// <para>기본값 <c>false</c> — 하니스는 판넬 경로를 계속 검사해야 하므로, 켜는 것은 내보내기 쪽이다.</para></summary>
    public static bool MassOnly;

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
            // ★[감사 0807 — 성능] 종전엔 열마다 **선 전체 정점**을 훑으며 범위 필터만 했다.
            //   cum은 단조증가 배열이므로 이진탐색(LocOfFrac)으로 범위를 바로 잡을 수 있다 —
            //   현장 규모(크레스트 정점 2만 × 열 1.2만)에서 수억 회 반복이던 것이 열당 1~2회로 줄고,
            //   필터 조건을 그대로 두었으므로 **결과(worst)는 비트 단위로 같다**.
            int iLo = LocOfFrac(cum, fa).Lo + 1;
            int iHi = System.Math.Min(LocOfFrac(cum, fb).Lo, line.Count - 1);
            for (int i = iLo; i <= iHi; i++)
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
                // ★★★[JACK 0819 '코너부면 무조건 ㄱ자 쐐기'] <b>지우는 경계의 꺾임을 남는 이웃에 넘긴다.</b>
                //
                //   종전엔 경계를 지우면 그 <b>꺾임이 그냥 사라졌다</b>. 코너가 한 정점에서 딱 꺾이지 않고
                //   <b>완만하게 여러 정점으로</b> 돌아가면(실측: 급커브 분할 36열) 조각마다 12°를 넘어 끊기지만,
                //   각 조각이 짧고 꺾임이 45° 이내라 <b>전부 다시 합쳐진다</b> —
                //   20°씩 네 번이면 결과는 <b>80° 코너인데 벽면 1개</b>다.
                //   벽면이 하나면 <c>cornerAtStart/End</c>가 둘 다 거짓이라 <b>ㄱ자 쐐기가 설 자리가 없다</b>
                //   (JACK 스샷: 코너부만 위로 갈수록 벌어진다).
                //
                //   → 누적하면 <b>20°씩 세 번째에서 60°</b>가 되어 45°를 넘고, 거기서 합치기가 멈춘다.
                //     한 번에 45°든 나눠서 45°든 <b>판넬이 가로지르면 평면이 깨지는 것은 같다</b> —
                //     그것이 이 한계값의 본래 뜻이다.
                int keep = (drop == k) ? k + 1 : k;
                if (keep >= 0 && keep < c.Count && !double.IsNaN(c[drop]) && !double.IsNaN(c[keep]))
                {
                    double dDrop = System.Math.Acos(System.Math.Clamp(c[drop], -1.0, 1.0));
                    double dKeep = System.Math.Acos(System.Math.Clamp(c[keep], -1.0, 1.0));
                    c[keep] = System.Math.Cos(System.Math.Min(System.Math.PI, dKeep + dDrop));
                }
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
        // ★[0807] 성토에서 '너무 깊이 묻혀' 버린 열 — 이 숫자가 0이면 이번 수정은 아무 일도 안 한 것이고,
        //   크면 그만큼이 종전에 보이지도 않는 채로 만들어지던 판넬이다. 고친 효과를 로그로 확인하는 장치.
        int deepBuried = 0; double deepMax = 0, deepX = 0, deepY = 0;
        // [0806 JACK '판넬 가로 넓이가 달라졌다' / '살짝 누락부'] 열 폭 분포와 실오라기 구멍의 실측.
        double minColW = double.MaxValue, maxColW = 0, narrowX = 0, narrowY = 0, narrowLen = 0;
        int narrowN = 0, narrowN2 = 0, faceCnt = 0, chordSplit = 0; string sliverFirst = ""; double noSplitDev = 0;
        // [0806] 토우가 크레스트보다 길어 열 폭을 늘린 횟수와 최대 증가량 — 오목 코너에서만 나와야 정상.
        int toeLong = 0; double toeLongMax = 0;
        // ★[JACK 0807] 자투리 전용 객체가 몇 개인가 — 이 설계의 건강 지표.
        //   규격 판넬보다 자투리가 많으면 벽면이 너무 잘게 쪼개진 것이고, 그건 판넬이 아니라 옹벽선 쪽 문제다.
        int fillerN = 0;
        int quoinN = 0; double quoinMax = 0;   // 이 줄에서 만든 코너 필러 수와 최대 폭
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

        // ★★[JACK 0807] **짧은 벽면 합치기 폐지.** 종전엔 한 변의 절반보다 짧은 벽면을 덜 꺾인 이웃에
        //   합쳤다(MinFaceLenFor·MergeMaxDeg). 그래야 자투리가 안 생긴다는 논리였는데, 합치는 순간
        //   그 벽면 안에 **코너가 들어가고** 규격 판넬이 코너를 가로질러 아랫변이 벽선에서 0.235m 벗어났다
        //   (하니스 S36/S38 실측 — 판넬 두께 0.20m보다 크다. 0806에 JACK이 지적한 '어긋남'이 이것이다).
        //   JACK 0807 원칙: "직각부나 라운드부는 그 부분만을 위한 얇은 옹벽객체를 별도로 만들어서 array."
        //   → 합치지 않는다. 짧은 벽면은 규격 판넬이 0장이라 통째로 **전용 얇은 객체 하나**가 된다.
        //   판넬은 어떤 코너도 가로지르지 않고, 코너는 전부 전용객체가 맡는다.
        //   ※다만 **티끌 벽면**(MinPieceLen 미만, 실측 0.06m짜리가 나온다)은 합친다. 그건 진짜 코너가 아니라
        //     옹벽선 조밀화가 남긴 부스러기이고, 따로 두면 그 열이 줄눈 인셋에 죽어 **그 자리가 구멍**이 된다
        //     (현장 '줄눈 39개 버림'). 6cm를 합쳐 봐야 판넬이 가로지르는 각도는 무시할 수준이다 —
        //     0806에 문제였던 건 **0.83m(한 변 절반)까지** 합치던 것이었다.
        var runs = SplitAtCorners(crest, cornerDeg, MinPieceLen,
                                  toe.Count == crest.Count ? toe : null);   // 토우 코너에서도 끊는다(0806)

        // ★★★[JACK 0819 '왜 조각으로 하냐는거야'] **매스 모드에서는 벽면을 나누지 않는다 — 한 줄이 통째로 하나다.**
        //   벽면을 나누는 것은 판넬을 깔기 위한 준비였다(코너를 가로지르는 판넬을 막으려고).
        //   매스는 옹벽선을 그대로 따라가므로 코너를 가로지를 일이 없고, 나누면 오히려
        //   코너에서 양옆이 <see cref="CornerLeg"/>만큼 물러나(아래 물러나기) **그 자리가 비고 매스가 둘로 갈린다**.
        //   판넬을 위해 만든 구조를 매스에 그대로 씌우면 매번 이런 식으로 샌다 — 그래서 통째로 건너뛴다.
        if (MassOnly) { runs.Clear(); runs.Add((0.0, 1.0)); }

        // ★[0806 JACK '판넬부는 오목부에서 자꾸 오류가 나는 것 같다 — 누더기 수리 말고 정확히 확인해봐']
        //   벽면 경계(코너)마다 **볼록/오목**을 판정해 좌표와 함께 모아 둔다.
        //   판정: 진행 방향이 도는 쪽과 노출면이 있는 쪽이 **같으면 볼록**(벽이 밖으로 돌출),
        //         **다르면 오목**(벽이 안으로 꺾임 — 이웃 벽면끼리 서로를 향해 다가온다).
        //   오목 코너에서는 두 벽면이 서로를 향하므로 모서리 겹침(cornerLap)이 **겹침이 아니라 관통**이 되고,
        //   토우/크레스트의 오프셋 길이 차이도 볼록과 부호가 반대다. 이 목록으로 결함이 정말
        //   오목 코너에 몰리는지 **세어서** 확인한다(스샷 심증 → 숫자로 확정).
        var cornerConcave = new bool[System.Math.Max(1, runs.Count)];
        var cornerDegAt = new double[System.Math.Max(1, runs.Count)];
        // [0806] 벽면 양 끝의 실제 모서리(아래·위 월드점 + 노출면 방향) — 코너 필러를 만드는 데 쓴다.
        // ★★[JACK 0807 '길이를 재는 로직을 다시 생각해봐야 할 듯'] **벽면 상한·하한을 한 군데로 모은다.**
        //   판넬은 자기 자리 원지반을 찍어 높이를 정하는데, 코너 필러·코너 유닛은 **옆에 있는 무언가**에서
        //   높이를 빌려 왔다(설계 높이 → 이웃 끝 열 → 이웃 A 벽면). 세 번 다 같은 종류로 터졌다.
        //   데이라잇이 코너 쪽으로 내려오면 코너는 **국소 최저점**이라 양옆 어느 쪽을 봐도 항상 더 높다 —
        //   min(A,B)로도 부족하고, 그 자리 지반을 직접 찍는 수밖에 없다.
        //   반환은 토우에서 잰 높이(m). hi < 0 = 지반 밖(판단 불가), lo ≥ hi = 벽면 없음.
        (double lo, double hi) WallSpanAtPt(Point3 c0, Point3 t0, double fh)
        {
            if (ground == null) return (0, fh);
            if (run.Up)
            {
                if (!ground.TryGetElevation(t0.X, t0.Y, out double g0)) return (0, -1);
                if (t0.Z >= g0 - 1e-6) return (0, 0);
                if (!ground.TryGetElevation(c0.X, c0.Y, out double g1)) return (0, -1);
                if (c0.Z <= g1 + 1e-6) return (0, fh);
                double d0 = g0 - t0.Z, d1 = g1 - c0.Z;
                return (0, System.Math.Clamp(d0 / (d0 - d1), 0, 1) * fh);
            }
            if (!ground.TryGetElevation(t0.X, t0.Y, out double h0)) return (0, fh);
            if (t0.Z >= h0 - 1e-6) return (0, fh);
            if (!ground.TryGetElevation(c0.X, c0.Y, out double h1)) return (0, fh);
            if (c0.Z <= h1 + 1e-6) return (fh, fh);
            double e0 = t0.Z - h0, e1 = c0.Z - h1;
            return (System.Math.Clamp(-e0 / (e1 - e0), 0, 1) * fh, fh);
        }

        // 크레스트 호길이 비율 f에서의 벽면 상한·하한 + 그 자리 크레스트·토우 점(코너 유닛이 쓴다).
        (double lo, double hi, Point3 c0, Point3 t0, double fh) WallSpanAtFrac(double f)
        {
            var lf2 = LocOfFrac(cumC, f);
            var cc = AtLoc(crest, lf2.Lo, lf2.T);
            var tt2 = (toe.Count == crest.Count && !DisableIndexPairingForTest) ? AtLoc(toe, lf2.Lo, lf2.T) : AtFrac(toe, cumT, f);
            double fh = System.Math.Sqrt((cc.X - tt2.X) * (cc.X - tt2.X) + (cc.Y - tt2.Y) * (cc.Y - tt2.Y)
                                       + (cc.Z - tt2.Z) * (cc.Z - tt2.Z));
            if (fh < 1e-9) return (0, -1, cc, tt2, 0);
            var sp = WallSpanAtPt(cc, tt2, fh);
            return (sp.lo, sp.hi, cc, tt2, fh);
        }

        var faceStart = new (bool Ok, Point3 Bot, Point3 Top, (double x, double y, double z) W, (double x, double y) U)[System.Math.Max(1, runs.Count)];
        var faceEnd = new (bool Ok, Point3 Bot, Point3 Top, (double x, double y, double z) W, (double x, double y) U)[System.Math.Max(1, runs.Count)];

        // ★★[JACK 0807 '각진부 마감을 깔끔하게'] **코너에서 양옆 벽면을 물러나게 한다.**
        //   물러난 자리는 코너 전용 유닛(ㄱ자)이 감싼다 — 두 노출면이 이웃 판넬 전면과 같은 평면이라
        //   이어 붙으면 한 면처럼 보인다. 지금은 양쪽 판넬이 코너를 지나쳐 서로 파고들고 그 위에
        //   필러까지 얹혀 세 덩어리가 뭉친다("막았다"이지 "마감했다"가 아니다 — JACK 스샷).
        //   ※물러나기는 **진짜 코너에서만**. 줄의 양 끝(열린 줄의 시작·끝)은 코너가 아니므로 건드리지 않는다.
        //   ※물러난 뒤 벽면이 규격 판넬 한 장도 못 담을 만큼 짧아지면 물러나지 않는다 —
        //     그 자리는 종전대로 코너 필러가 맡는다. 마감을 예쁘게 하려다 벽을 없애면 안 된다.
        double totC0 = cumC[cumC.Length - 1];
        bool closedRun0 = Dist2D(crest[0], crest[crest.Count - 1]) < 1e-6;
        double legFrac = totC0 > 1e-9 ? CornerLeg / totC0 : 0;
        var legAtStart = new double[System.Math.Max(1, runs.Count)];
        var legAtEnd = new double[System.Math.Max(1, runs.Count)];
        if (!DisableCornerUnitForTest && !MassOnly && legFrac > 0)
            for (int rIdx = 0; rIdx < runs.Count; rIdx++)
            {
                bool cornerAtStart = closedRun0 || rIdx > 0;
                bool cornerAtEnd = closedRun0 || rIdx < runs.Count - 1;
                double f0 = runs[rIdx].F0, f1 = runs[rIdx].F1;
                double want = (cornerAtStart ? legFrac : 0) + (cornerAtEnd ? legFrac : 0);
                //   남는 길이가 한 변 + 줄눈보다 짧아지면 그 벽면은 물러나지 않는다.
                if ((f1 - f0 - want) * totC0 < side + JointW) continue;
                if (cornerAtStart) { legAtStart[rIdx] = CornerLeg; f0 += legFrac; }
                if (cornerAtEnd) { legAtEnd[rIdx] = CornerLeg; f1 -= legFrac; }
                runs[rIdx] = (f0, f1);
            }
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

        // ★★★[JACK 0819 '옹벽선을 이용해서 그 라운드 모양으로 버퍼줘서' ·
        //   '꺾인점 기준으로 양쪽 패널 시점의 위치를 이용해서 그냥 덩어리로 하나짜리를 만드는 거지']
        //   **쐐기를 옹벽선 스윕으로 만든다 — 모양을 판정하지 않는다.**
        //
        //   판넬이 비켜 준 구간의 옹벽선(크레스트·토우)을 **그대로 훑어** 판넬 두께만큼의 단면으로 스윕한다.
        //   옹벽선이 원호면 원호가, 직각이면 직각이 나온다. 코드는 그게 무슨 모양인지 알 필요가 없다 —
        //   재는 게 아니라 **따라가는** 것이라서, 직각/라운드 분기 자체가 없다(<see cref="WedgeDev"/> 참조).
        //
        //   높이도 구하지 않는다. JACK 0807: *"코너 필렛도 그냥 옹벽 단 설정 높이만큼 만들고 판넬 자를 때
        //   같이 데이라잇으로 자르면 될 것 같은데."* → 정점마다 판넬과 **같은 자**(WallSpanAtFrac)로 자른다.
        //   '높이를 어디서 가져오나'로 네 번 헤맨 질문이 여기서는 아예 생기지 않는다.
        var wedgeSpans = new List<(double F0, double F1)>();
        int wedgeMade = 0, wedgeDrop = 0, wedgePiece = 0; double wedgeLenMax = 0;
        // [계측] 문턱이 안 걸려도 **실제 이탈이 얼마인지**는 보여야 한다 — 안 그러면 '0개'가 성공인지 실패인지 모른다.
        double devMax = 0, devMaxX = 0, devMaxY = 0; int devTot = 0;
        var devHist = new int[DevBuckets.Length];
        void BuildSweptWedge(double fa, double fb)
        {
            if (fb - fa < 1e-9) { wedgeDrop++; return; }
            // 스윕 경로 = 구간 안의 **옹벽선 정점 그대로** + 양 끝. 정점을 건너뛰는 순간 모양이 뭉개진다.
            var fs = new List<double> { fa };
            for (int i = 1; i + 1 < crest.Count; i++)
            {
                double f = cumC[i] / totalC;
                if (f > fa + 1e-9 && f < fb - 1e-9) fs.Add(f);
            }
            fs.Add(fb);

            // 단면: 옹벽선 기준 바깥 +PanelFrontOut, 안쪽 −(PanelThick−PanelFrontOut).
            //   판넬과 **같은 값**이라 옆에 붙으면 한 면으로 이어진다(코너 유닛이 쓰는 그 규칙).
            //   바깥 방향은 **크레스트→토우의 수평 성분** — 절토·성토 둘 다 그쪽이 노출면이다(분기 불필요).
            // ★★[JACK 0819 '스윕이 위아래 길이가 다른데도 잘되나?'] **좋은 지적이라 조각으로 쌓는다.**
            //   위(크레스트)와 아래(토우)는 **길이가 다르다** — 벽이 1:n으로 기울어 코너를 도는 동안
            //   크레스트가 토우보다 (기울기×높이)×회전각 만큼 길다(10m·1:0.05·90°면 0.79m).
            //   짝은 호길이가 아니라 **정점 번호**로 지으니 대응 자체는 확실하지만(판넬과 같은 규칙),
            //   AutoCAD 로프트는 내가 준 정점 순서를 쓰는 게 아니라 두 단면을 **자기 매개변수로 다시 맞춘다** —
            //   위아래 간격이 많이 다르면 엇갈려 이어져 **비틀린다**. ㄱ자 유닛이 멀쩡한 건 6정점·0.7m로 짧아서다.
            //
            //   → 옹벽선 **정점 구간마다**(≈1m) 네 점짜리 조각을 만들어 쌓는다. 한 조각 안에서는 위아래
            //     길이 차가 몇 cm라 비틀 여지가 없고, 조각끼리는 **정점을 공유해 딱 맞붙어** 한 덩어리로 보인다.
            // ★★★[JACK 0819] 정점마다 **마름모 단면**을 만들어 모은다 — 쌓는 게 아니라 한 번에 통과시킨다.
            //   단면 = 아래 토우, 위 크레스트, 두께는 판넬 두께. 두 선의 수평 차이가 곧 구배 × 단높이라
            //   **높이를 따로 계산하지 않는다**(JACK 0807·0819 같은 원칙).
            //   네 점은 모두 '노출면 법선 N과 수직선 Z가 만드는 평면' 위에 있어 **평면이 보장된다**.
            var secs = new List<IReadOnlyList<Point3>>();
            bool anyPiece = false;
            void FlushMass()
            {
                // 단면이 2장 미만이면 통과시킬 것이 없다 — 데이라잇에 끊긴 자리다.
                if (secs.Count >= 2) { LastMasses.Add(new WallMass(new List<IReadOnlyList<Point3>>(secs))); wedgePiece++; anyPiece = true; }
                secs.Clear();
            }

            // ★★★[JACK 0820 '벽이 휘었어' — 스샷: 벽 끝에서 단면이 부채꼴로 비틀림] **방향을 잡음에서 가져오지 않는다.**
            //   단면의 두께 방향(노출면 법선)을 <b>크레스트→토우</b> 벡터로 정했는데, 그 수평 길이는
            //   <b>구배 × 높이</b>다 — 데이라잇이 벽을 깎아 높이가 0으로 가는 <b>벽 끝에서 0에 수렴</b>한다.
            //   0에 가까운 벡터의 방향은 좌표 잡음이라, 그 자리 단면들이 제멋대로 돌아가 부채꼴로 비틀렸다.
            //   → ① 너무 짧으면 <b>이웃의 방향을 물려받고</b> ② 이웃과 반대로 뒤집힌 방향은 되돌린다.
            //     방향은 벽을 따라 <b>연속</b>이어야 한다 — 한 자리에서 뒤집히면 그 구간이 통째로 꼬인다.
            // ★★[JACK 0825 구배 하한 0.05→0.01] <b>2cm는 구배에 매인 값이었다.</b>
            //   이 벡터의 길이가 곧 <c>구배 × 단높이</c>라, 구배를 1/5로 낮추면 같은 벽이 1/5로 짧아진다.
            //   2cm를 그대로 두면 방향을 못 재는 문턱이 <b>단높이 0.4m → 2.0m</b>로 다섯 배 올라가고,
            //   그 단은 <b>매스도 마감판도 0개</b>가 된다(잔여단·자투리 사면이 흔히 여기 걸린다).
            //   문턱의 본뜻은 "좌표 잡음보다 큰가"이다. 링 좌표는 1mm 격자를 지나므로 잡음은 최대 ~1.4mm —
            //   <b>5mm면 그 3.5배</b>로 충분하고, 벽 끝(높이→0)에서는 여전히 걸려 물려받기가 작동한다.
            const double NormMin = 0.005;                      // 수평 5mm — 1mm 격자 잡음의 3.5배
            const double MinLive = 0.02;                       // 이보다 낮으면 벽이 없다(종전 0.15 — 끝이 뭉툭했다)

            // ★★★[JACK 0820 '왜 끝단이 뭉퉁그려졌지?'] **벽이 0으로 줄어드는 자리까지 따라간다.**
            //   종전엔 높이가 0.15m 미만이면 단면을 안 만들었다. 그러면 데이라잇과 만나 뾰족하게 끝나야 할
            //   자리가 <b>15cm짜리 마구리로 막힌다</b> — 그게 JACK이 본 뭉툭한 끝이다.
            //   문턱을 낮추는 것만으론 부족하다(2cm 마구리가 될 뿐이다). 마지막 살아 있는 정점과 그 다음
            //   정점 사이에서 <b>높이가 0이 되는 자리를 이분법으로 찾아</b> 단면을 하나 더 놓는다 — 칼끝처럼 만난다.
            // ★★[검토 중간7] **지반 밖에서 꽉 채우면 칼끝을 못 찾는다.**
            //   여기(SpanAt)는 <c>AddTip</c>/<c>ZeroBetween</c>이 "높이가 0이 되는 자리"를 좁히는 데 쓴다.
            //   지반 밖에서 원래 높이로 꽉 채우면 모든 표본이 살아 있다고 나와 이분법이 죽은 쪽까지 밀리고,
            //   칼끝 대신 <b>온전한 높이의 마구리</b>가 서서 절벽이 생긴다("끝단이 뭉퉁그려졌다"의 재발).
            //   → 지반 밖이면 **벽이 없다**고 답한다. 끝을 찾는 일에서는 그것이 옳은 답이다.
            (bool ok, Point3 bot, Point3 top, double h) SpanAt(double f)
            {
                var sp3 = WallSpanAtFrac(f);
                if (sp3.fh < 1e-9) return (false, default, default, 0);
                if (sp3.hi < 0) return (false, default, default, 0);   // 판단 불가 = 여기선 벽이 없다고 본다
                double hh = System.Math.Min(sp3.hi, sp3.fh);
                double ll = System.Math.Clamp(sp3.lo, 0, sp3.fh);
                Point3 On(double t) => new Point3(sp3.t0.X + (sp3.c0.X - sp3.t0.X) * t,
                                                  sp3.t0.Y + (sp3.c0.Y - sp3.t0.Y) * t,
                                                  sp3.t0.Z + (sp3.c0.Z - sp3.t0.Z) * t);
                return (hh - ll >= 0, On(ll / sp3.fh), On(hh / sp3.fh), hh - ll);
            }
            // 살아 있는 fLive와 죽은 fDead 사이에서 벽이 사라지는 자리를 좁혀 간다.
            double ZeroBetween(double fLive, double fDead)
            {
                for (int it = 0; it < 14; it++)
                {
                    double fm = (fLive + fDead) / 2;
                    var r = SpanAt(fm);
                    if (r.ok && r.h > 1e-4) fLive = fm; else fDead = fm;
                }
                return fLive;
            }
            int nSt = fs.Count;
            // ★★★[JACK 0820 '왜 비틀렸지? 지표면 그대로 나온 거 아니야?'] **자를 땐 X·Y도 같이 잘라야 한다.**
            //   벽면은 토우에서 크레스트로 가는 <b>비스듬한 선</b>이다. 데이라잇이 위를 자르면 그 선의
            //   중간에서 끊기므로 <b>가로 위치도 그만큼 안쪽</b>이어야 하는데, 종전엔 Z만 줄이고
            //   X·Y는 크레스트를 그대로 썼다 — 10m 벽이 2m로 잘려도 가로로는 여전히 0.5m 벌어져
            //   그 자리만 구배가 1:0.05가 아니라 1:0.25가 됐다. 자리마다 다르게 누우니 벽이 비틀린다.
            //   평평한 구간은 안 잘려서 멀쩡했고 <b>잘린 끝에서만</b> 부채꼴이 된 이유가 이것이다.
            var stTop = new Point3[nSt]; var stBot = new Point3[nSt];
            var stNx = new double[nSt]; var stNy = new double[nSt];
            var stOk = new bool[nSt]; var stNOk = new bool[nSt];
            // ★[JACK 0820] 자르기 **전**의 토우·크레스트와, 자르는 비율(0=토우, 1=크레스트).
            var stFullB = new Point3[nSt]; var stFullT = new Point3[nSt];
            var stLo = new double[nSt]; var stHi = new double[nSt];
            var stHas = new bool[nSt];
            // ★★★[JACK 0820 '데이라잇이 이상하게 잘림'] **지반 밖에서 높이를 꽉 채우지 않는다.**
            //   WallSpanAtPt는 그 자리가 지반 밖이면 hi < 0(판단 불가)을 준다. 종전엔 그때 <b>원래 높이로 꽉</b>
            //   채웠는데, 그러면 이웃은 데이라잇에 잘려 낮은데 그 자리만 갑자기 솟아 <b>계단</b>이 생긴다.
            //   오늘 세 번 나온 것과 같은 종류다 — <b>판단 불가인 자리에서 극단값을 집는 것.</b>
            //   → 아는 이웃 사이를 보간해 물려받는다. 양쪽 다 모르면 그때만 꽉 채운다(벽 전체가 지반 밖).
            var stHiKnown = new bool[nSt];
            // ★★[검토 중간6] **성토에는 '판단 불가' 개념이 아예 없었다.**
            //   절토는 지반 밖이면 <c>hi &lt; 0</c>으로 말해 주는데, 성토는 데이라잇이 <c>lo</c>(묻힌 비율)로
            //   표현되고 지반 조회에 실패하면 <c>lo = 0</c>(전부 노출)이라는 <b>극단값</b>을 집는다.
            //   <c>hi = fh ≥ 0</c>이라 위쪽 보간 장치에도 안 걸린다 — 원지반 TIN 구멍에서 그 자리만
            //   토우까지 내려가 <b>성토 데이라잇에 계단</b>이 생긴다. 오늘 반복된 그 패턴의 마지막 사례다.
            //   → 지반이 그 자리를 덮는지 직접 물어 <c>stLoKnown</c>으로 표시하고, 위쪽과 같은 방식으로 보간한다.
            var stLoKnown = new bool[nSt];
            for (int k = 0; k < nSt; k++)
            {
                var sp2 = WallSpanAtFrac(fs[k]);
                if (sp2.fh < 1e-9) continue;
                stHiKnown[k] = sp2.hi >= 0;
                stLoKnown[k] = ground == null || run.Up
                            || (ground.TryGetElevation(sp2.t0.X, sp2.t0.Y, out _)
                             && ground.TryGetElevation(sp2.c0.X, sp2.c0.Y, out _));
                double hi2 = sp2.hi < 0 ? sp2.fh : System.Math.Min(sp2.hi, sp2.fh);
                double lo2 = System.Math.Clamp(sp2.lo, 0, sp2.fh);
                // 토우→크레스트 선 위의 두 점. **X·Y·Z를 같은 비율로** 잡아야 구배가 유지된다.
                double sLo = lo2 / sp2.fh, sHi = System.Math.Max(lo2, hi2) / sp2.fh;
                Point3 On(double t) => new Point3(sp2.t0.X + (sp2.c0.X - sp2.t0.X) * t,
                                                  sp2.t0.Y + (sp2.c0.Y - sp2.t0.Y) * t,
                                                  sp2.t0.Z + (sp2.c0.Z - sp2.t0.Z) * t);
                // ★★★[JACK 0820 '아예 누락된 게 있다는 건 아직도 잘린 부위에 맞춰서 배치하고 있는 거 아니야?']
                //   **맞다.** 종전엔 데이라잇이 죽인 자리에서 여기를 그냥 빠져나가, 온전한 벽면 목록에
                //   그 자리가 <b>통째로 빠졌다</b>. 그러면 누적 거리가 건너뛰어 격자가 <b>잘린 모양에 맞춰진다</b> —
                //   "온전한 벽에 깔고 나중에 자른다"고 해 놓고 실제로는 잘린 벽에 깔고 있었다(덮개율 59%).
                //   → 기하는 <b>언제나</b> 남기고, 살았는지는 lo/hi로만 말한다(hi ≤ lo면 그 자리엔 벽이 없다).
                stFullB[k] = sp2.t0; stFullT[k] = sp2.c0;
                stLo[k] = sLo; stHi[k] = sHi; stHas[k] = true;
                if (hi2 - lo2 < MinLive) continue;             // 데이라잇에 다 잘렸다 — 매스는 여기서 끊긴다
                stBot[k] = On(sLo); stTop[k] = On(sHi);
                stOk[k] = true;
                // ★★★[JACK 0820 '성토부쪽 모양이 뒤집힘' → '성토라고 다 뒤집어진 건 아니고 어느 면은 맞고
                //   어느 면은 안 맞고 그래'] **절토/성토로 가를 문제가 아니다. 기하로 정한다.**
                //   종전엔 노출면을 언제나 <c>크레스트→토우</c>로 잡았다. 그런데 옹벽선을 만드는 쪽에서
                //   줄에 따라 <b>크레스트와 토우가 뒤바뀌어</b> 나오면(성토 일부) 그 줄만 통째로 뒤집힌다 —
                //   "어느 면은 맞고 어느 면은 안 맞고"가 정확히 그 모습이다.
                //   ⇒ 이름을 믿지 말고 <b>높이</b>를 본다. 옹벽 면은 뒤로 누우므로 <b>아래가 언제나 바깥</b>이다.
                //     즉 노출면 = <b>위쪽 선에서 아래쪽 선으로</b>. 이름이 뒤바뀐 줄이 섞여도 안 깨진다.
                //   ※오늘 반복된 그 패턴의 또 다른 얼굴이다 — <b>이름(규약)을 믿고 기하를 안 본 것.</b>
                bool crestUp = sp2.c0.Z >= sp2.t0.Z;
                double hx = crestUp ? sp2.t0.X - sp2.c0.X : sp2.c0.X - sp2.t0.X;
                double hy = crestUp ? sp2.t0.Y - sp2.c0.Y : sp2.c0.Y - sp2.t0.Y;
                if (System.Math.Sqrt(hx * hx + hy * hy) >= NormMin)
                {
                    var nn = Norm2(hx, hy);
                    if (nn.ok) { stNx[k] = nn.x; stNy[k] = nn.y; stNOk[k] = true; }
                }
                else tLowWall++;      // ★[0820] 벽이 낮아 방향을 못 잰다 — 물려받기가 여기서 시작된다
            }
            // ★[JACK 0820] 지반 밖이라 높이를 모르는 자리는 **아는 이웃 사이를 보간**해 물려받는다.
            //   그래야 데이라잇 선이 이어진다 — 꽉 채우면 그 자리만 솟아 계단이 된다.
            for (int k = 0; k < nSt; k++)
            {
                if (stHiKnown[k] || !stHas[k]) continue;
                int lo = -1, hi = -1;
                for (int q = k - 1; q >= 0; q--) if (stHiKnown[q] && stHas[q]) { lo = q; break; }
                for (int q = k + 1; q < nSt; q++) if (stHiKnown[q] && stHas[q]) { hi = q; break; }
                double v;
                if (lo >= 0 && hi >= 0)
                {
                    // ★[검토 중간11] 정점 번호가 아니라 **호길이**로 가중한다 — fs[]는 등간격이 아니다
                    //   (옹벽선 정점 + 구간 양 끝이라 코너 부근이 촘촘하다). 번호로 나누면 보간된
                    //   데이라잇 선이 실제와 어긋난다.
                    double span = fs[hi] - fs[lo];
                    double t = span > 1e-12 ? (fs[k] - fs[lo]) / span : 0;
                    v = stHi[lo] + (stHi[hi] - stHi[lo]) * t;
                }
                else if (lo >= 0) v = stHi[lo];
                else if (hi >= 0) v = stHi[hi];
                else continue;                                  // 벽 전체가 지반 밖 — 그때만 꽉 채운 값을 그대로 둔다
                stHi[k] = System.Math.Clamp(v, stLo[k], 1.0);
                // 잘라 둔 위 점도 같이 옮긴다 — 매스와 무늬가 같은 선에서 잘려야 한다.
                stTop[k] = new Point3(stFullB[k].X + (stFullT[k].X - stFullB[k].X) * stHi[k],
                                      stFullB[k].Y + (stFullT[k].Y - stFullB[k].Y) * stHi[k],
                                      stFullB[k].Z + (stFullT[k].Z - stFullB[k].Z) * stHi[k]);
                if (stHi[k] - stLo[k] < 0.001) stOk[k] = false;  // 물려받고 보니 벽이 없다
            }
            // ★[검토 중간6] 성토 쪽 '판단 불가'도 같은 방식으로 이웃에서 물려받는다(위 주석 참조).
            for (int k = 0; k < nSt; k++)
            {
                if (stLoKnown[k] || !stHas[k]) continue;
                int lo = -1, hi = -1;
                for (int q = k - 1; q >= 0; q--) if (stLoKnown[q] && stHas[q]) { lo = q; break; }
                for (int q = k + 1; q < nSt; q++) if (stLoKnown[q] && stHas[q]) { hi = q; break; }
                double v;
                if (lo >= 0 && hi >= 0)
                {
                    // ★[검토 중간11] 정점 번호가 아니라 **호길이**로 가중한다 — fs[]는 등간격이 아니다.
                    double span = fs[hi] - fs[lo];
                    double t = span > 1e-12 ? (fs[k] - fs[lo]) / span : 0;
                    v = stLo[lo] + (stLo[hi] - stLo[lo]) * t;
                }
                else if (lo >= 0) v = stLo[lo];
                else if (hi >= 0) v = stLo[hi];
                else continue;
                stLo[k] = System.Math.Clamp(v, 0.0, stHi[k]);
                stBot[k] = new Point3(stFullB[k].X + (stFullT[k].X - stFullB[k].X) * stLo[k],
                                      stFullB[k].Y + (stFullT[k].Y - stFullB[k].Y) * stLo[k],
                                      stFullB[k].Z + (stFullT[k].Z - stFullB[k].Z) * stLo[k]);
                if (stHi[k] - stLo[k] < 0.001) stOk[k] = false;
            }

            // ① 못 믿을 자리는 이웃에서 물려받는다(앞으로 한 번, 뒤로 한 번 — 줄 끝도 채워진다).
            // ★[JACK 0820] **몇 자리를 물려받았는지 센다.** 물려받기는 굽은 자리를 건너면 방향을 반대편에
            //   붙일 수 있어 '한쪽이 반대'의 유력 용의자다 — 세어 두지 않으면 그게 원인인지 못 가른다.
            // ★[JACK 0820 실측 '물려받은 자리 609/651인데 벽 낮아 못 잼 0점'] **살아 있는 자리에서만 센다.**
            //   데이라잇에 죽은 자리는 방향을 잴 일이 없어 전부 '물려받음'으로 잡혔다 — 90%가 나오니
            //   그 숫자로는 아무 판단도 못 한다(정상인데 위험해 보인다).
            //   진짜 위험한 것은 <b>벽이 살아 있는데 방향을 못 잰</b> 자리다. 그것만 센다.
            for (int k = 0; k < nSt; k++) if (stOk[k]) { tNormTot++; if (!stNOk[k]) tNormInherit++; }
            for (int k = 1; k < nSt; k++)
                if (!stNOk[k] && stNOk[k - 1]) { stNx[k] = stNx[k - 1]; stNy[k] = stNy[k - 1]; stNOk[k] = true; }
            for (int k = nSt - 2; k >= 0; k--)
                if (!stNOk[k] && stNOk[k + 1]) { stNx[k] = stNx[k + 1]; stNy[k] = stNy[k + 1]; stNOk[k] = true; }
            // ② 이웃과 반대로 뒤집힌 방향을 되돌린다 — 방향은 벽을 따라 연속이어야 한다.
            // ★★★[검토 심각1] **90°를 넘게 꺾였다고 되돌리면 진짜 코너를 뒤집는다.**
            //   이 방향은 벽 접선이 아니라 크레스트→토우(노출면 법선)라, 벽선이 θ만큼 꺾이면 같이 θ만큼 돈다.
            //   <c>dot &lt; 0</c>으로 되돌리면 120° 코너 하나에서 뒤집히고, 제자리 순차 루프라
            //   <b>그 뒤로 줄 끝까지 전염된다</b> — 벽이 통째로 0.20m 옆으로 밀리고 판이 흙 쪽에 붙는다.
            //   → 되돌리는 것은 <b>거의 정반대</b>일 때만(155° 초과). 그건 코너가 아니라 수치 잡음이다.
            for (int k = 1; k < nSt; k++)
                if (stNOk[k] && stNOk[k - 1] && stNx[k] * stNx[k - 1] + stNy[k] * stNy[k - 1] < -0.9)
                { stNx[k] = -stNx[k]; stNy[k] = -stNy[k]; }

            // ★★★[검토 심각1] **뒤집힘은 매스와 비교해서는 못 잡는다** — 둘이 같이 오염되면 내적이 양수라
            //   진단이 언제나 "뒤집힌 판 없음"이라고 답한다(실제로 그랬다).
            //   → 매스를 보지 말고 <b>불변량</b>을 본다: 노출면은 진행 방향의 <b>한쪽</b>에만 있어야 한다.
            //     줄을 따라가며 <c>cross(접선, 법선)</c>의 부호를 세고, 다수와 다른 자리를 센다.
            //     이 값이 0이 아니면 그 줄 어딘가에서 방향이 실제로 뒤집힌 것이다.
            {
                int plus = 0, minus = 0;
                var sgn = new int[nSt];
                for (int k = 0; k + 1 < nSt; k++)
                {
                    if (!stNOk[k] || !stHas[k] || !stHas[k + 1]) continue;
                    double tx = stFullB[k + 1].X - stFullB[k].X, ty = stFullB[k + 1].Y - stFullB[k].Y;
                    double tl = System.Math.Sqrt(tx * tx + ty * ty);
                    if (tl < 0.01) continue;                       // 1cm 미만 — 조밀화 잡음이라 방향을 못 믿는다
                    // 정규화한 뒤 본다. 진행 방향과 노출면이 **거의 나란하면**(sin < 0.2 ≈ 12°) 부호가
                    //   좌표 잡음으로 뒤집히므로 세지 않는다 — 그건 뒤집힘이 아니라 못 재는 자리다.
                    double sin = (tx * stNy[k] - ty * stNx[k]) / tl;
                    if (System.Math.Abs(sin) < 0.2) continue;
                    sgn[k] = sin > 0 ? 1 : -1;
                    if (sgn[k] > 0) plus++; else minus++;
                }
                // ★[JACK 0820 실측 113/228점] **'다수와 다른 자리'로 세면 안 된다.**
                //   벽선이 좁은 성토를 <b>감싸고 돌아오면</b> 노출면은 바깥을 향한 채로 진행 방향 기준
                //   왼쪽↔오른쪽이 바뀐다 — 정상인데 절반이 '다르다'고 나온다(실측 113/228 ≈ 딱 절반).
                //   진짜 결함은 <b>이웃끼리 급격히 뒤집히는 것</b>이다. 벽선이 크게 안 꺾였는데 부호가
                //   바뀌면 그건 기하가 아니라 방향 계산이 튄 것이다.
                int odd = 0;
                for (int k = 0; k + 1 < nSt; k++)
                {
                    if (sgn[k] == 0 || sgn[k + 1] == 0 || sgn[k] == sgn[k + 1]) continue;
                    // 이웃 두 구간의 진행 방향이 크게 안 꺾였는데 부호가 바뀌었나?
                    double t0x = stFullB[k + 1].X - stFullB[k].X, t0y = stFullB[k + 1].Y - stFullB[k].Y;
                    double t1x = stFullB[k + 2].X - stFullB[k + 1].X, t1y = stFullB[k + 2].Y - stFullB[k + 1].Y;
                    double l0 = System.Math.Sqrt(t0x * t0x + t0y * t0y), l1 = System.Math.Sqrt(t1x * t1x + t1y * t1y);
                    if (l0 < 1e-9 || l1 < 1e-9) continue;
                    if ((t0x * t1x + t0y * t1y) / (l0 * l1) < 0.0) continue;   // 90° 넘게 꺾였다 — 감아 도는 자리다
                    odd++;
                    if (tSideFirst.Length == 0)
                        tSideFirst = $"{stFullB[k].X:F0},{stFullB[k].Y:F0}";
                }
                tSideOdd += odd; tSideTot += plus + minus;
            }

            void Add(Point3 top, Point3 bot, double nx, double ny)
            {
                double oX = nx * PanelFrontOut, oY = ny * PanelFrontOut;
                double iX = nx * (PanelFrontOut - PanelThick), iY = ny * (PanelFrontOut - PanelThick);
                // 바깥위 → 바깥아래 → 안쪽아래 → 안쪽위 (닫힌 마름모)
                secs.Add(new List<Point3> {
                    new Point3(top.X + oX, top.Y + oY, top.Z),
                    new Point3(bot.X + oX, bot.Y + oY, bot.Z),
                    new Point3(bot.X + iX, bot.Y + iY, bot.Z),
                    new Point3(top.X + iX, top.Y + iY, top.Z) });
            }
            // 벽이 시작·끝나는 자리에 높이 0인 단면을 하나 더 놓아 칼끝처럼 만나게 한다.
            void AddTip(double fFrom, double fTo, double nx, double ny)
            {
                var r = SpanAt(ZeroBetween(fFrom, fTo));
                if (r.ok) Add(r.top, r.bot, nx, ny);
            }
            // ★★★[JACK 0820] 온전한 벽면은 **끊지 않고 통째로** 담는다 — 무늬 격자가 벽 전체에 한 번만 깔리도록.
            {
                var full = new List<IReadOnlyList<Point3>>();
                var los = new List<double>(); var his = new List<double>();
                for (int k = 0; k < nSt; k++)
                {
                    if (!stHas[k] || !stNOk[k]) continue;
                    double oX2 = stNx[k] * PanelFrontOut, oY2 = stNy[k] * PanelFrontOut;
                    double iX2 = stNx[k] * (PanelFrontOut - PanelThick), iY2 = stNy[k] * (PanelFrontOut - PanelThick);
                    full.Add(new List<Point3> {
                        new Point3(stFullT[k].X + oX2, stFullT[k].Y + oY2, stFullT[k].Z),
                        new Point3(stFullB[k].X + oX2, stFullB[k].Y + oY2, stFullB[k].Z),
                        new Point3(stFullB[k].X + iX2, stFullB[k].Y + iY2, stFullB[k].Z),
                        new Point3(stFullT[k].X + iX2, stFullT[k].Y + iY2, stFullT[k].Z) });
                    los.Add(stLo[k]); his.Add(stHi[k]);
                }
                if (full.Count >= 2) LastFaces.Add(new WallFace(full, los, his));
            }

            for (int k = 0; k < nSt; k++)
            {
                bool live = stOk[k] && stNOk[k];
                bool prevLive = k > 0 && stOk[k - 1] && stNOk[k - 1];
                if (live && !prevLive && k > 0) AddTip(fs[k], fs[k - 1], stNx[k], stNy[k]);   // 벽이 시작하는 끝
                if (!live)
                {
                    if (prevLive) AddTip(fs[k - 1], fs[k], stNx[k - 1], stNy[k - 1]);         // 벽이 끝나는 끝
                    FlushMass(); continue;
                }
                Add(stTop[k], stBot[k], stNx[k], stNy[k]);
            }
            FlushMass();
            if (!anyPiece)
            {
                // ★[JACK 0820] **사유를 갈라 센다.** '못 세운 구간 N개'만으로는 원래 벽이 없는 자리인지
                //   만들다 실패한 자리인지 못 가른다 — 그러면 그 숫자로 아무 판단도 못 한다.
                int live2 = 0, dir2 = 0;
                for (int k = 0; k < nSt; k++) { if (stOk[k]) live2++; if (stNOk[k]) dir2++; }
                if (live2 == 0) tWdCut++;            // 데이라잇에 통째로 잘렸다(정상일 수 있다)
                else if (dir2 == 0) tWdNoDir++;      // 방향을 어디서도 못 구했다(벽이 아주 낮다)
                else tWdShort++;                     // 살아 있는데 단면이 2장을 못 채웠다
                wedgeDrop++; return;
            }
            wedgeMade++;
            double wl2 = (fb - fa) * totalC; if (wl2 > wedgeLenMax) wedgeLenMax = wl2;
        }

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
            // ★★[JACK 0807 확정 원칙] **규격 판넬은 언제나 정확히 한 변. 남는 자투리는 전용 얇은 객체.**
            //   "단에 따라 패널의 높이와 폭은 정해진다. 그 원칙은 지키되, 배열할 때 패널 폭이 제각각
            //    달라지는 건 절대 하지 말고, 부족하면 얇은 거 전용객체 하나 만들어서 넣는다."
            //   종전엔 마지막 열이 나머지 길이를 그대로 먹고(0.06~1.67m 아무 값), 너무 짧으면 앞 장과
            //   반씩 나눠 **두 장 다 비규격**이 됐다. 그게 JACK이 두 번 지적한 '들쭉날쭉'의 나머지 절반이다.
            //   이제 나누지 않는다 — 규격 판넬을 깔 수 있는 만큼 깔고, 남은 조각 하나만 전용 객체로 세운다.
            var edge = new List<double> { 0.0 };                       // 벽면 안 누적 길이(m)
            int nFull = (int)System.Math.Floor(segLen / side + 1e-9);
            double tailLen = segLen - nFull * side;
            // ★★[JACK 0807 '패널 사이의 간격은 어떠한 경우에도 5cm'] 자투리가 <see cref="MinPieceLen"/>보다
            //   짧으면 **따로 세우지 않고 앞 조각과 하나로 합친다.** 종전엔 2cm만 넘으면 자기 열을 받았는데,
            //   그 열은 줄눈 인셋(양쪽 2.5cm)에 통째로 죽어(현장 '줄눈 39개 버림') 그 자리가 구멍이 됐다 —
            //   구멍은 곧 5cm가 아닌 간격이다. 합치면 그 조각 하나만 규격보다 넓어지고(≤1.77m)
            //   **규격 판넬은 여전히 전부 정확히 한 변**이다(합친 조각은 전용객체로 표시).
            bool mergeTail = tailLen > 1e-9 && tailLen < MinPieceLen && nFull >= 1;
            int nSpec = mergeTail ? nFull - 1 : nFull;
            for (int k = 1; k <= nSpec; k++) edge.Add(k * side);
            bool hasTail = segLen - nSpec * side > 1e-9;
            if (hasTail) edge.Add(segLen);
            //   벽면 자체가 한 변보다 짧으면(라운드 코너 조각 등) 규격 판넬이 0장 —
            //   그 벽면은 통째로 전용 객체 하나가 된다. JACK 0807: "직각부나 라운드부는 단순 array 하면
            //   블록 크기 때문에 떡지거나 겹쳐지니 그 부분만을 위한 얇은 옹벽객체를 별도로 만든다."
            //   열마다 '규격 판넬인가 / 전용 얇은 객체인가'. 규격 판넬은 **폭이 언제나 한 변**이고,
            //   전용 객체만 폭이 자유롭다 — JACK 0807 원칙을 자료구조로 못 박은 것이다.
            var isFill = new List<bool>(edge.Count);
            for (int k = 0; k + 1 < edge.Count; k++) isFill.Add(false);
            if (hasTail) isFill[isFill.Count - 1] = true;              // 끝 자투리는 전용 객체
            // 급커브에서 쪼갠 조각은 따로 표시해 둔다 — 아래 'LOD 70%' 승격에서 **제외**하기 위해서다.
            //   곡선 조각을 규격 판넬로 되돌리면 쪼갠 이유(현 이탈)가 그대로 되살아난다(실측 0.235m).
            var isCurve = new List<bool>(isFill);
            for (int k = 0; k < isCurve.Count; k++) isCurve[k] = false;

            // ★★★[JACK 0819] **판넬이 자기 두께만큼 벽선에서 벗어나는 열은 안 깐다.**
            //   판정은 **쪼개기 전**, 규격 폭 그대로의 이탈로 한다 — 쪼갠 뒤에 재면 이미 작아져 있어
            //   '판넬이 이 자리를 감당할 수 있나'라는 질문에 답이 안 된다.
            //   이탈은 크레스트·토우 **둘 다** 재고 나쁜 쪽을 쓴다(오목에서는 아래가 더 벗어난다).
            var isWedge = new List<bool>(System.Math.Max(0, edge.Count - 1));
            for (int k = 0; k + 1 < edge.Count; k++)
            {
                double fw0 = f0 + (f1 - f0) * edge[k] / segLen;
                double fw1 = f0 + (f1 - f0) * edge[k + 1] / segLen;
                double devW = System.Math.Max(MaxChordDev(crest, cumC, fw0, fw1, 1),
                                              pairByIndex ? MaxToeChordDev(toe, cumC, fw0, fw1) : 0);
                devTot++;
                for (int bi = 0; bi < DevBuckets.Length; bi++) if (devW > DevBuckets[bi]) devHist[bi]++;
                if (devW > devMax)
                {
                    devMax = devW;
                    var lw = LocOfFrac(cumC, fw0); var pw = AtLoc(crest, lw.Lo, lw.T);
                    devMaxX = pw.X; devMaxY = pw.Y;
                }
                isWedge.Add(!DisableWedgeForTest && (MassOnly || devW > WedgeDev));
            }

            // ★★[JACK 0807] **급커브도 '전용 얇은 객체'로 간다.**
            //   판넬은 평면이고 벽선은 곡선이라, 규격 폭 그대로 두면 현(弦)이 되어 안쪽으로 파고든다
            //   (하니스 실측 0.405m — 판넬 두께 0.20m의 두 배라 지표면에 통째로 묻힌다).
            //   종전엔 그 열을 반으로 쪼개 **좁은 판넬**을 만들었고, 그게 JACK이 금지한 '제각각 폭'이었다.
            //   이제는 쪼개되 **규격 판넬이라고 부르지 않는다** — 앵커·무늬 없는 전용 객체로 표시한다.
            //   그래서 규격 판넬은 언제나 정확히 한 변이고, 곡선부는 얇은 조각들이 곡선을 따라간다.
            //   ("직각부나 라운드부는 그 부분만을 위한 얇은 옹벽객체를 별도로 만들어서 array 시킨다" — JACK 0807)
            if (!DisableChordLimitForTest)
                for (int guard = 0; guard < 6; guard++)
                {
                    bool anySplit = false;
                    for (int i = 0; i + 1 < edge.Count; i++)
                    {
                        if (edge[i + 1] - edge[i] < 2 * MinTailLen) continue;   // 더 쪼개면 실오라기
                        if (i < isWedge.Count && isWedge[i]) continue;          // [0819] 쐐기 구간 — 판넬을 안 까니 쪼갤 것도 없다
                        double fa2 = f0 + (f1 - f0) * edge[i] / segLen;
                        double fb2 = f0 + (f1 - f0) * edge[i + 1] / segLen;
                        // 현 이탈은 **크레스트와 토우 둘 다** 재고 나쁜 쪽으로 판단한다 —
                        //   오목 코너에서는 토우가 크레스트보다 더 꺾여, 위는 통과해도 아래가 0.405m 벗어난다.
                        double dev2 = System.Math.Max(MaxChordDev(crest, cumC, fa2, fb2, 1),
                                                      pairByIndex ? MaxToeChordDev(toe, cumC, fa2, fb2) : 0);
                        if (dev2 <= ChordTol) continue;
                        edge.Insert(i + 1, (edge[i] + edge[i + 1]) / 2);
                        isFill[i] = true; isFill.Insert(i + 1, true);   // 쪼갠 두 조각은 **둘 다 전용 객체**
                        isCurve[i] = true; isCurve.Insert(i + 1, true);
                        if (i < isWedge.Count) isWedge.Insert(i + 1, false);    // [0819] 표시를 열과 같이 늘린다
                        chordSplit++; anySplit = true; i++;
                    }
                    if (!anySplit) break;
                }

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
            // ★★[JACK 0807 '최대한 심플하게 · 왜 어딘 폭이 좁고 어딘 넓냐'] **급커브 분할 폐지.**
            //   종전엔 곡선에서 현 이탈이 5cm를 넘으면 그 열을 반으로 쪼갰다. 이탈은 줄지만 **폭이 깨진다** —
            //   JACK이 두 번(0806·0807) 지적한 '들쭉날쭉'의 절반이 이 규칙이었다(현장 396열).
            //   JACK 0807 원칙: "직각부나 라운드부는 단순 array 하면 겹치거나 떡지니 **그 부분만을 위한
            //   얇은 옹벽객체를 별도로** array 한다." → 규격 판넬은 **언제나 규격 폭 그대로** 깔고,
            //   곡선·코너가 남긴 자리는 판넬을 변형해서가 아니라 **코너 필러**로 메운다.
            //   (이탈 실측 noSplitDev는 계속 잰다 — 얼마나 벌어지는지는 알아야 필러 폭이 옳은지 확인된다.)
            int ncol = edge.Count - 1;
            faceCnt++;

            for (int j = 0; j < ncol; j++)
            {
                // ★★★[JACK 0819] 이 열은 판넬이 감당 못 한다 — 판넬을 안 깔고 옹벽선 스윕 덩어리가 맡는다.
                //   이웃한 쐐기 열끼리는 **하나로 이어 붙인다**(JACK: "그냥 덩어리로 하나짜리를 만드는 거지").
                //   벽면이 갈려도 호길이가 이어지면 계속 이어 붙으므로, 코너를 낀 두 벽면도 한 덩어리가 된다.
                if (j < isWedge.Count && isWedge[j])
                {
                    double wa = f0 + (f1 - f0) * edge[j] / segLen;
                    double wb = f0 + (f1 - f0) * edge[j + 1] / segLen;
                    if (wa > wb) { double sw3 = wa; wa = wb; wb = sw3; }
                    int lastW = wedgeSpans.Count - 1;
                    if (lastW >= 0 && System.Math.Abs(wedgeSpans[lastW].F1 - wa) < 1e-9)
                        wedgeSpans[lastW] = (wedgeSpans[lastW].F0, wb);
                    else wedgeSpans.Add((wa, wb));
                    continue;
                }
                colN++;
                double colW = edge[j + 1] - edge[j];
                bool isFiller = j < isFill.Count && isFill[j];   // [JACK 0807] 전용 얇은 객체(앵커·무늬 없음)
                bool isTail = isFiller;    // 승격(LOD 70%) 전의 '자투리인가' — 폭을 토우에 맞출지는 이걸로 판단한다
                // ★[JACK 0807 추가] "만약 폭이 70% 이상 되는 판넬은 LOD를 옹벽패널과 같이 올려."
                //   전용 객체는 LOD를 포기하는 대신 폭이 자유로운 것인데, 규격에 가까운 조각까지 민판으로 두면
                //   벽 한가운데 앵커·무늬 없는 넓은 판이 섞여 오히려 눈에 띈다. 규격의 70%를 넘으면
                //   규격 판넬과 똑같이 앵커·도넛·무늬를 붙인다(폭만 다를 뿐 '판넬'로 대접).
                //   단, **급커브에서 쪼갠 조각은 승격하지 않는다** — 되돌리면 쪼갠 이유(현 이탈)가 살아난다.
                //   그리고 **벽선을 실제로 따라가는 조각만** 승격한다. 어긋난 판에 앵커·무늬를 달면
                //   눈에 덜 띄기는커녕 '앵커 달린 판이 벽에서 삐져나온' 모양이 된다(실측 0.235m ≈ 토우↔크레스트 간격).
                double fa = f0 + (f1 - f0) * edge[j] / segLen;
                double fb = f0 + (f1 - f0) * edge[j + 1] / segLen;
                bool curveCol = j < isCurve.Count && isCurve[j];
                bool detail = !isFiller;                 // 규격 판넬은 언제나 풀 LOD
                if (isFiller && !curveCol && colW >= side * FullLodRatio)
                {
                    double devLod = System.Math.Max(MaxChordDev(crest, cumC, fa, fb, 1),
                                                    pairByIndex ? MaxToeChordDev(toe, cumC, fa, fb) : 0);
                    if (devLod <= ChordTol) detail = true;   // ★분류(Filler)는 그대로 두고 **표현만** 올린다
                }
                if (isFiller) fillerN++;

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
                // ★★[JACK 0807 원칙] **모서리 겹침 폐지.** 위 주석 전체가 이 한 규칙을 놓고
                //   0806에 **네 번 고쳐 네 번 실패한** 기록이다(오목 확대 3회 · 볼록 확대 1회).
                //   결론은 매번 같았다: *판넬을 늘려서는 코너를 못 메운다.* 볼록에서 두 벽면은 책처럼
                //   벌어지므로 각자 길어져 봐야 사이는 그대로고, 오목에서는 이미 물려 있어 이웃을 뚫는다.
                //   JACK 0807이 같은 결론을 원칙으로 정리했다 — 코너는 **전용 얇은 객체**가 맡는다.
                //   그러니 판넬은 자기 벽면 안에만 있고(겹침 0), 코너는 아래 필러가 통째로 책임진다.
                double lapA = 0, lapB = 0;

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
                // ★★[JACK 0807 원칙] **폭을 토우 길이로 바꾸는 보정 폐지.**
                //   이 보정은 코너에 닿는 열의 폭을 ±0.4m까지 바꿔 놓는다 — 옳은 기하였지만
                //   JACK이 금지한 바로 그것("배열할 때 패널 폭이 제각각 달라지는 건 절대 하지 말고")이다.
                //   코너에서 위·아래 길이가 다른 건 사실이고, 그 차이는 이제 **코너 필러**가 먹는다.
                //   (실측만 남긴다 — 필러가 덮어야 할 폭이 얼마인지 알아야 필러가 옳은지 확인된다.)
                if (toeStraight && toeSpanU > 1e-6 && System.Math.Abs(toeSpanU - colW) > 1e-6)
                { toeLong++; toeLongMax = System.Math.Max(toeLongMax, System.Math.Abs(toeSpanU - colW)); }

                // ★★[JACK 0807 '또 중간에 틈이 있어' — 두 번째 뿌리] **자투리는 토우 길이에 맞춘다.**
                //   벽면은 **크레스트 호길이**로 잘라 놓는데, 벽이 1:n으로 기울어 코너에서는 위·아래 길이가 다르다
                //   (볼록이면 토우가 짧고, 오목이면 길다 — 코너당 n×단높이 = 0.25m).
                //   그래서 벽면 **끝** 조각의 아랫변이 토우 꼭짓점을 지나쳐 나가거나(볼록) 못 미친다(오목).
                //   실측 0.235m ≈ 0.25m — '한 선만큼 밀림'의 정체가 이것이었다.
                //   JACK 0807 원칙과 충돌하지 않는다: 금지된 것은 **규격 판넬**의 폭이 제각각인 것이고,
                //   자투리 전용 객체는 애초에 "부족하면 얇은 거 하나 넣는" 자리라 폭이 자유롭다.
                //   → 규격 판넬은 절대 안 건드리고, **자투리만** 토우에 맞춘다. 못 미치는 쪽은 필러가 메운다.
                //   ★★[JACK 0807 스샷 멘트 — 폐기] 위 처방(자투리를 토우 길이로 **줄이기**)이 바로
                //   JACK이 찍어 보낸 '중간 빈공간'을 만들었다. 자투리를 줄이면 그 자리가 다음 벽면과 안 맞물려
                //   벽 한가운데가 벌어지고, 그걸 얇은 띠로 막는 악순환이 된다.
                //   JACK 확정: "노란색 부분 전체가 **애초에 빈공간 없이 붙고**, 빨간 네모의 LOD 낮은 객체가
                //   **커져서** 공백을 없애야 됨. LOD 낮은 객체를 만들라고 한 이유 자체가 중간에 공백이 생기면
                //   그 폭을 조절하려는 것임."
                //   → 자투리는 **줄이지 않는다. 남은 만큼 그대로 다 먹는다**(아래 edge가 이미 그렇게 깔린다).
                //     볼록 코너에서 아랫변이 토우 꼭짓점을 조금 지나치는 건 코너 필러가 맡는 자리이고,
                //     자투리는 Filler로 분류돼 '판넬이 선을 따라가나' 검사에서도 제외된다.
                //
                // ★★★[JACK 0807 스샷 '애초에 다음 패널을 댕겨서 작성'] **판넬 사이가 벌어지던 진짜 뿌리.**
                //   판넬 **폭**은 윗선(크레스트) 호길이를 n등분해 정하는데, 판넬 **위치**는 아랫선(토우)에서 잡는다.
                //   코너 부근에서는 두 선의 길이가 다르므로(1:n 기울기 × 단높이 = 코너당 0.25m),
                //   한 열의 아랫변이 다음 열의 시작점에 **못 미치거나 지나친다** — 그 차이가 곧 빈공간이다.
                //   하니스 실측: 같은 벽면의 이웃 규격 판넬 두 장이 0.05m가 아니라 **0.344m** 떨어져 있었다
                //   (10.01,12.87 → 9.95,13.21). 오목 100° 코너에서 2·d·tan(θ/2) ≈ 0.3m — 정확히 그 값이다.
                //   → **폭을 아랫선 길이로 잡는다.** 그러면 아랫변이 정확히 다음 판넬 시작점에서 끝나 빈공간이 없다.
                //     JACK 원칙(규격 판넬 폭은 언제나 같다)과 충돌하지 않는다: 곧은 벽면에서는 두 선 길이가
                //     **정확히 같아** 폭이 한 치도 안 변하고, 달라지는 건 코너에 닿는 열뿐이다.
                //     그 열은 이미 규격이 아니므로 **전용 얇은 객체로 분류**한다(폭이 자유로운 쪽으로 보낸다).
                if (!DisableToeWidthForTest && toeStraight && toeSpanU > 1e-6
                    && System.Math.Abs(toeSpanU - colW) > 1e-9)
                {
                    colW = toeSpanU;
                    //   재분류 문턱은 **줄눈 폭(0.05m)** 이다. 곧은 벽면에서도 옹벽선 조밀화·정점 끼워넣기 때문에
                    //   두 선 길이가 수 mm~수 cm 어긋난다 — 그것까지 '규격 아님'으로 세면 벽 전체가 전용객체가 된다
                    //   (문턱 0.01로 뒀다가 하니스에서 규격 판넬 0장이 나왔다). 줄눈보다 작은 차이는 눈에 안 보인다.
                    if (!isFiller && System.Math.Abs(colW - side) > JointW)
                    {
                        isFiller = true; fillerN++;
                        // LOD는 종전 규칙 그대로 — 규격의 70% 이상이면 앵커·무늬를 그대로 붙인다.
                        detail = colW >= side * FullLodRatio;
                    }
                }

                // ★[0806 JACK '길게 누락됨' — 벽 전체 높이가 통째로 빈 세로줄] 열마다 '판넬이 나왔나·왜 안 나왔나'를
                //   순서대로 적어 둔다. 총계(지반위 5421 · 실오라기 2)로는 **그 구멍이 벽 한가운데인지 끝인지** 알 수 없다.
                //   양옆에 판넬이 있는데 가운데만 빈 열 = 진짜 구멍. 끝쪽이 빈 것 = 데이라잇(정상).
                int logIdx = colLog.Count, tilesBefore = tiles.Count;
                colLog.Add((false, "행 전멸", org.X, org.Y, colW));

                // ★★[JACK 0807 스샷 '성토부는 2단인데 3단까지 생긴다'] **성토 지반선 하한(FloorAt).**
                // <para>
                // 0806까지의 규칙은 성토 한 단을 **통째로 살리거나 통째로 버리거나**였다. 그래서:
                //  · 버리는 쪽으로 기울이면 전이 구간에서 벽이 뚝 끊겨 13m가 통째로 빈다(JACK 0806).
                //  · 살리는 쪽으로 기울이면 지반이 조금 내려앉은 자리마다 **한 단이 통째로 매달린다**(JACK 0807).
                // 여유값(BuryDepth)을 어디에 두든 둘 중 하나는 반드시 나온다 — 값이 아니라 **규칙이 틀렸다.**
                // </para>
                // 실제 옹벽은 지반선을 따라 아래가 잘린다. 성토는 흙을 **쌓아** 올리므로 원지반 아래로는 벽면이 없다
                // (그 아래는 기초지, 판넬이 아니다). 그래서 상한(CapAt)의 거울로 **하한**을 둔다:
                // 벽면이 지반과 만나는 높이 아래는 만들지 않는다. 그러면 전이 구간에서 벽이 **가늘어지다 사라지고**
                // (구멍 안 생김), 이미 잠긴 아래 단은 **애초에 안 생긴다**(3단 안 매달림). 두 결함이 한 규칙에서 같이 죽는다.
                //
                // 반환 = 토우에서 잰 높이(m). 0 = 통째로 노출(자를 것 없음) · faceH 이상 = 통째로 매몰(열 자체가 없음).
                double FloorAt(double fu)
                {
                    if (ground == null || run.Up) return 0;          // 절토는 하한 없음(위에서 자른다)
                    double f = fa + (fb - fa) * System.Math.Clamp(fu, 0, 1);
                    var lf = LocOfFrac(cumC, f);
                    var c0 = AtLoc(crest, lf.Lo, lf.T);
                    var t0 = pairByIndex ? AtLoc(toe, lf.Lo, lf.T) : AtFrac(toe, cumT, f);

                    // 지반 자료가 없는 자리(측량 범위 밖)는 **자르지 않는다** — 묻혔는지 알 근거가 없는데 자르면
                    //   측량이 좁은 현장에서 멀쩡한 벽이 사라진다. 판단할 수 있을 때만 판단한다.
                    // ★[JACK 0807] 규칙은 **공용 함수 하나**(WallSpanAtPt)에 있다 — 판넬·코너가 같은 자를 쓴다.
                    double lo0 = WallSpanAtPt(c0, t0, faceH).lo;
                    if (lo0 >= faceH - 1e-9 && ground.TryGetElevation(c0.X, c0.Y, out double gzc))
                    {
                        double d = gzc - c0.Z;                       // 매몰 깊이 실측 — 규모를 로그로 본다
                        if (d > deepMax) { deepMax = d; deepX = c0.X; deepY = c0.Y; }
                    }
                    return lo0;
                }

                // 이 열의 데이라잇 상한 — 원지반보다 위로는 벽이 없다.
                double CapAt(double fu)
                {
                    if (ground == null) return faceH;
                    double f = fa + (fb - fa) * System.Math.Clamp(fu, 0, 1);
                    var lf = LocOfFrac(cumC, f);
                    var c0 = AtLoc(crest, lf.Lo, lf.T);
                    var t0 = pairByIndex ? AtLoc(toe, lf.Lo, lf.T) : AtFrac(toe, cumT, f);

                    // ★[JACK 0806 확정] **성토는 데이라잇으로 자르지 않는다 — 아예.**
                    //   "성토는 윗선을 기준으로 아래로 옹벽을 치는 게 맞긴 한데, 절토처럼 원지반과 맞닿는
                    //    데이라잇까지 끊을 필요는 없다. 어차피 인프라웍스에서 지표면 아래로 들어갈 거니깐 괜찮다."
                    //   종전엔 '크레스트가 지반 위면 꽉, 아니면 0'이라는 **전부 아니면 전무** 규칙이었다(0721).
                    //   그래서 원지반이 올라와 계획면과 만나는 전이 구간에서 벽이 **점점 낮아지지 않고 뚝 끊겨**
                    //   한 구간이 통째로 비었다(JACK 0806 스샷 '옹벽누락부' — 13.48m). 조건 자체를 없앤다.
                    //   묻히는 아랫부분은 InfraWorks에서 지표면에 가려지므로 그대로 두는 편이 옳다.
                    //   ※절토는 다르다 — 데이라잇 위로는 팔 흙이 자체가 없으므로 반드시 잘라야 한다(아래 규칙 유지).
                    //
                    //   ※성토의 **윗변**은 언제나 크레스트까지다(위로 자를 것이 없다). 지반은 아래에서 올라오므로
                    //     성토의 지반 처리는 상한이 아니라 **하한**(FloorAt)이 맡는다 — 바로 아래에 있다.
                    if (!run.Up) return faceH;

                    // ★[JACK 0807] 규칙은 **공용 함수 하나**에 있다 — 판넬과 코너 유닛이 같은 자를 쓰게 하려고
                    //   여기로 모았다. 자가 갈리면 코너만 벽 위로 솟는다(0807에 세 번 겪었다).
                    return WallSpanAtPt(c0, t0, faceH).hi;
                }

                // ★[0805 JACK '데이라잇에 끊긴 객체가 깔끔하지 않고 삐죽 나옴'] 상한을 **촘촘히 표본**해
                //   실루엣을 따라간다. 종전엔 열 양 끝 2점으로 사다리꼴을 만들어, 지반이 열 안에서 휘면
                //   실제 데이라잇선과 어긋나 실오라기가 삐져나왔다.
                double uL = jm - lapA, uR = colW - jm + lapB;
                if (uR - uL < 0.05) { dJoint++; colLog[logIdx] = (false, "줄눈", org.X, org.Y, colW); continue; }
                int NS = System.Math.Max(2, (int)System.Math.Ceiling((uR - uL) / 0.15));
                var capS = new double[NS + 1];
                var floorS = new double[NS + 1];      // [0807] 성토 지반선 하한 — 절토는 전부 0
                bool anyGnd = false, anyCap = false, anyOpen = false;
                for (int t = 0; t <= NS; t++)
                {
                    double c = CapAt((uL + (uR - uL) * t / NS) / colW);
                    if (c >= 0) anyGnd = true;
                    capS[t] = System.Math.Clamp(c, 0, faceH);
                    floorS[t] = System.Math.Clamp(FloorAt((uL + (uR - uL) * t / NS) / colW), 0, faceH);
                    if (capS[t] > 1e-6) anyCap = true;
                    if (capS[t] - floorS[t] > 1e-6) anyOpen = true;   // 이 표본에 '드러난 벽면'이 있는가
                }
                if (!anyGnd) { dGround++; colLog[logIdx] = (false, "지반밖", org.X, org.Y, colW); continue; }
                // ★[0807] 성토에서 지반선 아래로 통째로 잠긴 열 — '지반위'와 **다른 사유**다. 같이 세면
                //   아래 실측(토우가 지반보다 얼마나 높은가)이 성토에서 통째로 거짓말이 된다. 벽 한가운데
                //   구멍 판정에서도 빼야 한다 — 설계대로 안 만든 것이지 사라진 게 아니다.
                // 지반선 위로 남은 벽면이 **판넬 한 장도 못 될 만큼 얇으면** 그 열도 여기서 끝낸다.
                //   이걸 아래 행 루프까지 흘려보내면 조각이 전부 줄눈·실오라기에 죽어 사유가 '행 전멸'로 찍히고,
                //   그러면 '벽 한가운데 구멍'으로 오탐된다(하니스 S38에서 43m짜리 가짜 구멍으로 확인).
                //   정상적으로 사그라든 끝단과 진짜 결함은 **반드시 다른 사유**로 남아야 한다 — 0806의 교훈.
                double openMax = 0;
                for (int t = 0; t <= NS; t++) openMax = System.Math.Max(openMax, capS[t] - floorS[t]);
                if (anyCap && (!anyOpen || openMax < 2 * jm + 0.05))
                {
                    deepBuried++;
                    colLog[logIdx] = (false, WhyBuried, org.X, org.Y, colW);
                    continue;
                }
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
                //   3행이 **4행**이 되고 행 높이가 낮아진다.
                //   ※[JACK 0819 규격 변경 뒤] 이제 5m 단은 <b>본래 4행 1.25m</b>다(판넬 상한 1.5m).
                //     옛 경고("4행이 되면 가운데 자연석이 사라진다")는 <b>격자 무늬 시절</b>의 것이고,
                //     0806 십자 4분할에서는 1.25m 판넬의 상하 조각이 0.265m로 하한 0.08m를 크게 넘는다.
                //   ★[JACK 0819] 행 수 계산은 <see cref="RowsForBench"/> 한 곳에 모았다 —
                //   종전엔 여기서 계단식 규칙과 상한 검사를 <b>또 한 번</b> 적어, 규칙이 바뀌면 두 곳을 고쳐야 했다.
                //   (같은 규칙이 두 군데 있으면 한 군데만 고쳐진다 — 이 저장소가 되풀이해 배운 그것이다.)
                int nrow = RowsForBench(height);
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
                // ★[JACK 0807 '코너부 보강에서 삐죽삐죽 나온 객체'] 이 열이 **실제로** 덮은 v의 아래끝.
                //   코너 필러 높이를 벽면 설계 높이(faceH)로 잡으면, 데이라잇·지반선에 잘려 1m만 남은
                //   자리에도 5m짜리 기둥이 서서 허공으로 삐죽 솟는다. 실제 덮은 범위를 써야 한다.
                double colMinV = double.MaxValue;
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
                    // ★[0807] 표본마다 이 행의 **아랫변** 높이 — 성토 지반선(하한)에 걸리면 그만큼 올라간다.
                    //   절토는 floorS가 전부 0이라 botV ≡ v0 → 종전 동작과 **비트 단위로 같다**.
                    var botV = new double[NS + 1];
                    bool anyFloor = false;
                    for (int t = 0; t <= NS; t++)
                    {
                        botV[t] = System.Math.Max(v0, floorS[t] + jm);
                        if (botV[t] > v0 + 1e-9) anyFloor = true;
                    }

                    // 윗변이 아랫변보다 높은 **연속 구간**마다 조각을 하나씩 만든다 —
                    //   상한이 열 한가운데를 가로지르면 조각이 나뉘는 게 맞고, 억지로 한 장으로 만들면 삐죽 나온다.
                    //   [0807] 기준을 상수 v0이 아니라 **botV[t]**로 바꾼다 — 아랫변이 지반선을 따라 올라가므로
                    //   '살아 있는 구간'은 (윗변−아랫변)이 양수인 구간이다. 절토에선 botV=v0이라 식이 종전과 같다.
                    int t0 = 0;
                    while (t0 <= NS)
                    {
                        if (topV[t0] <= botV[t0] + 1e-6) { t0++; continue; }
                        int t1 = t0;
                        while (t1 + 1 <= NS && topV[t1 + 1] > botV[t1 + 1] + 1e-6) t1++;

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
                        // 조각의 양 끝은 윗변과 아랫변이 **만나는 정확한 위치**까지 늘린다(계단 모양 방지).
                        //   [0807] 종전엔 '윗변이 v0을 지나는 위치'였다. 아랫변이 지반선을 따라 움직이므로
                        //   두 선의 **차(gap)**가 0이 되는 자리로 일반화한다 — 절토(botV=v0)에선 완전히 같은 식이다.
                        double Gap(int t) => topV[t] - botV[t];
                        double ua = uL + t0 * stepU, ub = uL + t1 * stepU;
                        if (t0 > 0)
                        {
                            double a0 = Gap(t0 - 1), a1 = Gap(t0);
                            double r0 = (0 - a0) / System.Math.Max(a1 - a0, 1e-9);
                            ua = uL + (t0 - 1 + System.Math.Clamp(r0, 0, 1)) * stepU;
                        }
                        //   ※골짜기에서 끊은 경우(dipEnd)는 윗변이 아랫변을 지나는 게 아니라 그냥 나눈 것이라
                        //     보간하면 안 된다 — 그 자리 그대로 끝나고, **다음 조각이 같은 점에서 시작**해 이어진다.
                        if (t1 < NS && !dipEnd)
                        {
                            double b0 = Gap(t1), b1 = Gap(t1 + 1);
                            double r1 = (0 - b0) / System.Math.Min(b1 - b0, -1e-9);
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
                        // 아랫변 — 절토(및 완전 노출 성토)는 종전 그대로 2점 직선.
                        //   성토에서 지반선이 이 열을 가로지를 때만 실루엣을 따라간다(윗변과 대칭).
                        var local = new List<(double u, double v)>(2 * NS + 6);
                        if (anyFloor)
                        {
                            local.Add((ua, System.Math.Max(v0, botV[t0])));
                            for (int t = t0; t <= t1; t++) local.Add((uL + t * stepU, botV[t]));
                            local.Add((ub, System.Math.Max(v0, botV[t1])));
                        }
                        else { local.Add((ua, v0)); local.Add((ub, v0)); }
                        for (int t = t1; t >= t0; t--) local.Add((uL + t * stepU, topV[t]));
                        t0 = tNext;
                        local = Simplify(local);   // 공선점 제거 → 곧은 구간은 2점으로 줄어 5각/6각이 된다
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
                        //   [JACK 0807] 자투리 전용 객체는 **언제나 '온전' 아님** — 앵커·도넛을 달지 않는다
                        //   ("이때 LOD는 포기함, 재질만 통일"). 폭이 우연히 넉넉해도 규격 판넬이 아니다.
                        bool isFull = detail
                            && (u1 - u0) >= collarHalf * 2 + 0.2 && (v1 - v0) >= collarHalf * 2 + 0.2
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
                            local, pu, pv, run.Bench, run.Up, i, isFiller, detail));
                        if (mxV > colMaxV) colMaxV = mxV;
                        if (mnV < colMinV) colMinV = mnV;
                        colSpans.Add((mnV, mxV));
                        // 이 판넬 아랫변 중점이 옹벽선(토우) 위에 있는지 — 어긋나면 배치가 잘못된 것이다.
                        //   [0807] 지반선에 아래가 잘린 열은 제외 — 아랫변이 토우에서 **일부러** 떠 있으므로
                        //   재면 '이탈'로 잡힌다. 설계대로 올린 것을 오차로 세면 이 자가 또 거짓말을 한다.
                        if (i == 0 && !anyFloor)
                        {
                            double bu = (u0 + u1) / 2;
                            CheckOnLine(new Point3(org.X + bu * ux + v0 * vx, org.Y + bu * uy + v0 * vy, 0));
                        }
                    }
                }

                if (tiles.Count > tilesBefore) colLog[logIdx] = (true, "", org.X, org.Y, colW);

                // ★[JACK 0806 '코너필러까지 하고 나서 설치해줘'] 벽면 **양 끝**의 실제 모서리 위치를 기록해 둔다.
                //   볼록 코너에서는 두 벽면이 책처럼 벌어져 그 사이에 쐐기 틈이 남는데(100°면 0.60m),
                //   판넬을 늘려서는 못 메운다(4번 시도 4번 실패 — 각자 길어져 봐야 사이는 그대로다).
                //   그 자리를 채우는 건 **코너 필러**다. 렌더러(WallPanelDwg.BuildQuoin)는 멀쩡한데
                //   생성이 옛 경로(WallPanels.LastQuoins)에만 있어 새 경로에서는 **항상 0개**였다(0805 감사).
                //   여기서 만든다 — 판넬이 실제로 나온 열의 끝만 기록해, 벽이 없는 자리엔 필러도 안 생긴다.
                if (tiles.Count > tilesBefore)
                {
                    Point3 WorldAt(double uu, double vv) => new Point3(
                        org.X + uu * ux + vv * vx, org.Y + uu * uy + vv * vy, org.Z + vv * vz);
                    // ★[JACK 0807] '첫 열/마지막 열'이 아니라 **판넬이 실제로 나온 첫 열/마지막 열**을 쓴다.
                    //   종전엔 j==0 / j==ncol-1로 못 박아, 그 열이 데이라잇·지반선에 잘려 판넬이 안 나오면
                    //   벽면 끝 기록이 통째로 비었고 → 그 코너에는 **필러가 아예 안 섰다**
                    //   (하니스 실측: 남은 진짜 구멍 2곳 중 하나는 반경 2m에 필러 0개).
                    // 높이는 **실제로 판넬이 덮은 범위**(colMinV~colMaxV)로 — 설계 높이(faceH)로 잡으면
                    //   데이라잇에 잘린 자리에서 필러만 5m로 솟는다(JACK 0807 '삐죽삐죽 나온 객체').
                    double qLo = colMinV < double.MaxValue ? colMinV : 0;
                    double qHi = colMaxV > qLo ? colMaxV : faceH;
                    if (!faceStart[rIdx].Ok)
                        faceStart[rIdx] = (true, WorldAt(uL, qLo), WorldAt(uL, qHi), (wx, wy, wz), (ux, uy));
                    faceEnd[rIdx] = (true, WorldAt(uR, qLo), WorldAt(uR, qHi), (wx, wy, wz), (ux, uy));   // 마지막으로 나온 열로 갱신
                }

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

        // ★★★[JACK 0819] 판넬이 비켜 준 구간을 옹벽선 스윕 덩어리로 채운다(위 BuildSweptWedge 참조).
        foreach (var ws in wedgeSpans) BuildSweptWedge(ws.F0, ws.F1);

        // ★[JACK 0806] 코너 필러 — 이웃 벽면의 끝과 시작 사이에 남은 쐐기 틈을 기둥 하나로 메운다.
        //   볼록 코너에서만 틈이 생기고(오목은 이미 물려 있다) 그 폭은 2·d·tan(θ/2)라 각도가 클수록 커진다.
        //   분류에 기대지 않고 **실제로 벌어진 거리를 재서** 필요할 때만 만든다 — 분류가 틀려도 안전하다.
        //   ※짝은 **이웃한 벽면끼리만** 본다. 가장 가까운 시작점을 찾게 해 봤더니 멀리 떨어진 벽면끼리
        //     묶여 **2.68m짜리 판**이 엉뚱한 자리에 섰다 — 코너 쐐기가 아니라 벽 한 장 크기다. 되돌린다.
        //   폭 상한도 조인다: 코너 쐐기는 2·d·tan(θ/2)이고 d=0.25·단높이/5 정도라 140°에서도 1.4m 안쪽이다.
        for (int rIdx = 0; rIdx + (closedRun ? 0 : 1) < runs.Count; rIdx++)
        {
            int nxt = (rIdx + 1) % runs.Count;
            if (nxt == rIdx) break;
            // ★★[JACK 0807 원칙] **오목/볼록을 가리지 않고 모든 벽면 경계에 세운다.**
            //   0806에 볼록만으로 좁힌 이유는 '오목은 판넬이 이미 겹쳐 있어 필러가 겹침 위에 얹힌다'였다.
            //   그런데 그 겹침은 **모서리 겹침(lap)과 토우 폭 보정**이 만든 것이고, JACK 0807 원칙에 따라
            //   둘 다 방금 지웠다 — 이제 판넬은 자기 벽면 안에만 있으므로 오목 코너에도 **진짜 틈**이 남는다.
            //   그 틈을 메우는 것이 필러의 일이다. 아래 폭 조건(0.03~1.5m)이 '이미 붙은 자리'를 걸러 준다.
            var A = faceEnd[rIdx]; var B = faceStart[nxt];
            if (!A.Ok || !B.Ok) continue;                       // 한쪽 벽이 없으면 메울 틈도 없다

            // ★★[JACK 0807] **코너 전용 유닛을 먼저 시도한다.** 양옆이 물러나 있으면(legAtEnd/legAtStart)
            //   그 자리를 ㄱ자 유닛 하나가 감싼다 — 두 노출면이 이웃 판넬 전면과 같은 평면이라 마감이 이어진다.
            //   유닛이 서면 **필러는 세우지 않는다**(둘 다 서면 또 뭉친다 — 지금 도면이 그 모양이다).
            if (legAtEnd[rIdx] > 0 && legAtStart[nxt] > 0)
            {
                var uA = A.U; var uB = B.U;
                var nAh = Norm2(A.W.x, A.W.y); var nBh = Norm2(B.W.x, B.W.y);
                if (nAh.ok && nBh.ok)
                {
                    //   A.Bot/B.Bot이 곧 **물러난 다리 끝**이다(범위를 CornerLeg만큼 줄여 놨다).
                    var bot = CornerUnitProfile((A.Bot.X, A.Bot.Y), (B.Bot.X, B.Bot.Y),
                                                uA, uB, (nAh.x, nAh.y), (nBh.x, nBh.y), PanelThick, PanelFrontOut);
                    var top = CornerUnitProfile((A.Top.X, A.Top.Y), (B.Top.X, B.Top.Y),
                                                uA, uB, (nAh.x, nAh.y), (nBh.x, nBh.y), PanelThick, PanelFrontOut);
                    if (bot.Count == 6 && top.Count == 6)
                    {
                        // ★[JACK 0807 '삐죽 나오는 객체' — 명백한 버그] 높이를 **A 벽면에서만** 가져오고 있었다.
                        //   B가 데이라잇에 더 낮게 잘려 있으면 유닛이 B 위로 그대로 솟는다.
                        //   두 벽면이 **겹치는 높이**만 써야 양쪽 어디에도 안 튀어나온다(필러엔 이미 같은 규칙이 있다).
                        //   ★★[JACK 0807 '길이를 재는 로직'] 높이를 **코너 그 자리 지반**에서 구한다.
                        //   양옆에서 빌려 오면(A만·min(A,B) 둘 다) 데이라잇이 코너로 내려올 때 코너는
                        //   국소 최저점이라 **어느 쪽을 봐도 항상 더 높다** — 그래서 유닛만 벽 위로 솟았다.
                        //   판넬이 쓰는 그 규칙(WallSpanAtPt)을 코너 위치에서 한 번 더 부른다.
                        // ★★★[JACK 0807 '코너 필렛도 그냥 옹벽 단 설정 높이만큼 만들고 판넬 자를 때 같이
                        //   데이라잇으로 자르면 될 것 같은데'] **그 말이 맞다. 높이를 구하지 않는다.**
                        //   판넬은 처음부터 그렇게 한다 — 크레스트까지 꽉 채워 만들고 데이라잇으로 자른다.
                        //   코너 유닛만 '높이를 구해서 그만큼 세우는' 방식이라, 그 높이를 어디서 가져오냐가
                        //   매번 문제가 됐다(설계 높이 → 이웃 끝 열 → 이웃 A 벽면 → 코너 지반. 네 번 헤맸다).
                        //   게다가 한 높이로 세우면 윗면이 **평평**한데 양옆 판넬 윗면은 지형을 따라 **비스듬**하다 —
                        //   높이를 정확히 맞춰도 코너에 턱이 남는다.
                        //   → 단면 정점마다 **그 자리 데이라잇**으로 자른다. 판넬과 같은 자(WallSpanAtPt)를 쓰므로
                        //     윗변이 양옆 판넬과 이어지고, '높이를 어디서 가져오나'라는 질문 자체가 사라진다.
                        double fC = System.Math.Clamp(runs[rIdx].F1 + legFrac, 0, 1);
                        var stA = WallSpanAtFrac(System.Math.Clamp(runs[rIdx].F1, 0, 1));
                        var stC = WallSpanAtFrac(fC);
                        var stB = WallSpanAtFrac(System.Math.Clamp(runs[nxt].F0, 0, 1));
                        //   단면 정점 차례: [aOut, pOut, bOut, bIn, pIn, aIn] → 각각 A·코너·B 자리에서 잰다.
                        var st = new[] { stA, stC, stB, stB, stC, stA };

                        (bool ok, double zLo, double zHi) ZAt(
                            (double lo, double hi, Point3 c0, Point3 t0, double fh) s)
                        {
                            if (s.fh < 1e-9) return (false, 0, 0);
                            double hi2 = s.hi < 0 ? s.fh : System.Math.Min(s.hi, s.fh);   // 지반 밖이면 꽉 채운다
                            double lo2 = System.Math.Clamp(s.lo, 0, s.fh);
                            if (hi2 - lo2 < 1e-6) return (false, 0, 0);
                            double zt = s.t0.Z, dz = s.c0.Z - s.t0.Z;
                            return (true, zt + dz * (lo2 / s.fh), zt + dz * (hi2 / s.fh));
                        }

                        var b3 = new List<Point3>(6); var t3 = new List<Point3>(6);
                        bool okAll = true; double hMax = 0;
                        for (int k = 0; k < 6 && okAll; k++)
                        {
                            var z = ZAt(st[k]);
                            if (!z.ok) { okAll = false; break; }
                            var pb = k < 3 ? bot[k] : bot[k];
                            b3.Add(new Point3(pb.x, pb.y, z.zLo));
                            t3.Add(new Point3(top[k].x, top[k].y, z.zHi));
                            hMax = System.Math.Max(hMax, z.zHi - z.zLo);
                        }
                        if (!okAll || hMax < 0.15) continue;         // 잘리고 남은 게 없으면 유닛을 안 세운다
                        LastCornerUnits.Add(new CornerUnit(b3, t3));
                        continue;                               // 유닛이 섰으니 필러는 세우지 않는다
                    }
                }
            }
            double gBot = Dist2(A.Bot, B.Bot), gTop = Dist2(A.Top, B.Top);
            double gw = System.Math.Max(gBot, gTop);
            if (gw < 0.03 || gw > 1.5) continue;                // 이미 붙었거나(정상) 코너 쐐기가 아닌 먼 자리
            // [JACK 0807] 필러 좌우에도 줄눈 5cm — 종전엔 gw+0.02로 이웃을 파고들어 줄눈이 아예 없었다.
            double gwFit = gw - 2 * JointW;
            if (gwFit < 0.03) continue;                        // 남는 폭이 실오라기면 그 자리는 이미 줄눈에 가깝다
            // ★[JACK 0807 '삐죽삐죽'] 양옆 벽 높이가 다르면(한쪽만 데이라잇에 잘림) **낮은 쪽에 맞춘다.**
            //   평균으로 잡으면 낮은 쪽 위로 그 차이의 절반만큼 기둥이 솟아 허공에 날이 선다.
            double qBotZ = System.Math.Max(A.Bot.Z, B.Bot.Z);
            double qTopZ = System.Math.Min(A.Top.Z, B.Top.Z);
            if (qTopZ - qBotZ < 0.15) continue;                // 겹치는 높이가 없으면 메울 것도 없다
            // ★★[JACK 0807 '각진부 근처에 간간히 가로로 긴 이상한 객체'] **코너 쐐기는 세로로 긴 기둥이다.**
            //   폭이 높이에 육박하면 그건 쐐기가 아니라 **누운 판**이고, 도면에서 벽 위에 널빤지처럼 얹혀 보인다
            //   (JACK이 선택해 보여준 그 객체). 두 벽면이 겹치는 높이가 얕은 자리에서 폭만 넓게 잡히면 생긴다.
            //   0805에 '필러 짝을 넓혔다가 2.68m짜리 판이 엉뚱한 자리에 선' 것과 같은 부류다 — 모양으로 거른다.
            if (gwFit > (qTopZ - qBotZ) * 0.9) continue;
            var mid0 = new Point3((A.Bot.X + B.Bot.X) / 2, (A.Bot.Y + B.Bot.Y) / 2, qBotZ);
            var mid1 = new Point3((A.Top.X + B.Top.X) / 2, (A.Top.Y + B.Top.Y) / 2, qTopZ);
            double axX = B.Bot.X - A.Bot.X, axY = B.Bot.Y - A.Bot.Y;
            double axL = System.Math.Sqrt(axX * axX + axY * axY);
            if (axL < 1e-9) { axX = B.Top.X - A.Top.X; axY = B.Top.Y - A.Top.Y; axL = System.Math.Sqrt(axX * axX + axY * axY); }
            if (axL < 1e-9) continue;
            double nwx = A.W.x + B.W.x, nwy = A.W.y + B.W.y, nwz = A.W.z + B.W.z;
            double nwl = System.Math.Sqrt(nwx * nwx + nwy * nwy + nwz * nwz);
            if (nwl < 1e-9) continue;
            LastQuoins.Add(new WallPanels.Quoin(mid0, mid1, (axX / axL, axY / axL, 0),
                                                (nwx / nwl, nwy / nwl, nwz / nwl), gwFit));
            quoinN++; if (gw > quoinMax) quoinMax = gw;
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
                // [0807] 지반선 아래로 빠진 자리는 구멍이 아니다 — 그 자리엔 애초에 벽면이 없다.
                //   설계대로 뺀 것을 결함으로 세면, 성토를 돌릴 때마다 ⚠가 떠서 진짜 구멍이 그 속에 묻힌다.
                if (colLog[i].Why == WhyBuried) { i = j2; continue; }
                midHoleN++;
                if (w > midHoleW) { midHoleW = w; midHoleWhy = colLog[i].Why; midHoleX = colLog[i].X; midHoleY = colLog[i].Y; }
                i = j2;
            }

        LastDiag = $"판넬 {tiles.Count}(온전 {full}) · 벽면 {runs.Count} · 열 {colN} · 행 {rowN}" +
                   $" · 한변 {side:F2}m · 높이 {height:F2}m" +
                   $" · 버림(지반밖 {dGround} · 지반위 {dAbove} · 줄눈 {dJoint} · 퇴화 {dThin} · 실오라기 {dSliver}" +
                   (deepBuried > 0 ? $" · 지반선아래 {deepBuried}" : "") + ")" +
                   // ★[JACK 0807] 규격 판넬 : 자투리 전용객체 — 폭이 통일됐는지 한눈에 보는 숫자.
                   $" · 규격 {colN - fillerN}열 · 자투리 전용객체 {fillerN}개" +
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
                   (faceCnt > 0 && minColW <= maxColW
                       ? $" · 열폭 {minColW:F2}~{maxColW:F2}m(규격 {side:F2}m · 벽면 {faceCnt}개" +
                         (narrowN > 0 ? $" · 규격 미만 {narrowN}개(끝 자투리+급커브)" : " · 전부 규격") +
                         (chordSplit > 0 ? $" · 급커브 분할 {chordSplit}열(안 쪼갰다면 이탈 {noSplitDev:F3}m · 한도 {ChordTol:F2}m)" : "") +
                         $" · 최소 @ {narrowX:F0},{narrowY:F0})" : "") +
                   // ★[JACK 0819 계측] 문턱이 안 걸려도 실제 이탈을 보여 준다 — '쐐기 0개'가 성공인지 실패인지 갈리는 자리다.
                   $" · ★옹벽 매스 {wedgePiece}개" + (wedgeMade > 0 ? $"(구간 {wedgeMade} · 최장 {wedgeLenMax:F2}m)" : "") +
                   (wedgeDrop > 0 ? $" · 못 세운 구간 {wedgeDrop}개(데이라잇에 다 잘림)" : "") +
                   (MassOnly ? "" :
                     $" · 규격폭 이탈 최대 {devMax:F3}m @ {devMaxX:F0},{devMaxY:F0}" +
                     $"(전체 {devTot}열 · 직선 {devTot - (devHist.Length > 0 ? devHist[0] : 0)}열" +
                     DevHist(devHist) + $" · 쐐기 한도 {WedgeDev:F2}m)") +
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
        tPerLine.Add((tiles.Count, dAbove, aboveN > 0 ? aboveMin : double.NaN, deepBuried));
        if (faceCnt > 0)
        {
            if (minColW < tMinColW) { tMinColW = minColW; tNarrowX = narrowX; tNarrowY = narrowY; }
            if (maxColW > tMaxColW) tMaxColW = maxColW;
            tNarrowN += narrowN; tFaceCnt += faceCnt; tChordSplit += chordSplit;
            if (noSplitDev > tNoSplitDev) tNoSplitDev = noSplitDev;
            tWedgeN += wedgeMade; tWedgeDrop += wedgeDrop; tWedgePiece += wedgePiece;
            if (wedgeLenMax > tWedgeLenMax) tWedgeLenMax = wedgeLenMax;
            tDevTot += devTot; for (int bi = 0; bi < devHist.Length; bi++) tDevHist[bi] += devHist[bi];
            if (devMax > tDevMax) { tDevMax = devMax; tDevMaxX = devMaxX; tDevMaxY = devMaxY; }
        }
        if (sliverFirst.Length > 0 && tSliverFirst.Length == 0) tSliverFirst = sliverFirst;
        tQuoinN += quoinN; if (quoinMax > tQuoinMax) tQuoinMax = quoinMax;
        tHoleN += midHoleN;
        if (midHoleW > tHoleW) { tHoleW = midHoleW; tHoleWhy = midHoleWhy; tHoleX = midHoleX; tHoleY = midHoleY; }
        if (aboveN > 0 && (tAboveN == 0 || aboveMax > tAboveMax)) { tAboveMax = aboveMax; tAboveX = aboveX; tAboveY = aboveY; }
        if (aboveN > 0 && (tAboveN == 0 || aboveMin < tAboveMin)) tAboveMin = aboveMin;
        tAboveN += aboveN;
        tCall++; tTile += tiles.Count; tFull += full; tNonConvex += nonConvex;
        tGround += dGround; tAbove += dAbove; tJoint += dJoint; tThin += dThin; tSliver += dSliver;
        tDeep += deepBuried;
        if (deepMax > tDeepMax) { tDeepMax = deepMax; tDeepX = deepX; tDeepY = deepY; }
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
    /// <summary>줄별 (남긴 판넬 · 지반위 버림 · 토우가 뜬 거리 · [0807]깊이묻힘 버림).
    /// Deep을 따로 들고 있어야 '판넬 0장'이 결함인지 정상인지 갈린다 — 성토 아래 단들은 <b>전부</b>
    /// 깊이묻힘으로 0장이 되는데, 그걸 '벽이 사라졌다'고 경고하면 진짜 경고가 그 속에 묻힌다.</summary>
    private static readonly List<(int Kept, int Above, double Gap, int Deep)> tPerLine = new();
    private static int tAboveN; private static double tAboveMin, tAboveMax, tAboveX, tAboveY;
    private static double tMinColW = double.MaxValue, tMaxColW, tNarrowX, tNarrowY;
    private static int tNarrowN, tFaceCnt, tChordSplit; private static string tSliverFirst = "";
    private static int tHoleN; private static double tHoleW, tHoleX, tHoleY, tNoSplitDev; private static string tHoleWhy = "";
    private static int tQuoinN; private static double tQuoinMax;
    /// <summary>★[JACK 0819] 옹벽선 스윕 쐐기 — 전 줄 합계. <c>tDev*</c>는 <b>규격 폭 그대로일 때</b>의 이탈 분포로,
    /// 쐐기가 0개일 때 그것이 '벗어난 데가 없어서'인지 '문턱이 높아서'인지를 가른다(숫자 없이 판정하지 말 것).</summary>
    private static int tWedgeN, tWedgeDrop, tWedgePiece, tDevTot;
    /// <summary>★[검토 심각1] 노출면이 진행 방향의 반대쪽에 있는 자리 수 / 전체 — <b>매스를 안 보고</b> 재는 값.
    /// 매스와 비교하면 둘이 같이 뒤집혔을 때 못 잡는다(그래서 진단이 언제나 '없음'이라고 답했다).</summary>
    private static int tSideOdd, tSideTot;
    private static string tSideFirst = "";
    private static int tNormInherit, tNormTot;
    /// <summary>★[JACK 0820 '추가로 확인할 것들이 있으면 이참에 로그를 더 붙여'] 매스를 못 세운 **사유별** 수.
    /// <para>종전엔 '못 세운 구간 12개'라고만 했다 — 12개가 <b>원래 벽이 없는 자리</b>인지
    /// <b>만들다 실패한 자리</b>인지 갈리지 않으면 그 숫자로는 아무 판단도 못 한다.</para></summary>
    private static int tWdShort, tWdNoDir, tWdCut;
    /// <summary>★[0820] 벽이 낮아 방향을 못 재는 자리(수평차 &lt; NormMin) — 물려받기의 원천이다.</summary>
    private static int tLowWall;
    /// <summary>★[0820] 굽어서 건너뛴 칸의 최대 이탈과 그 자리 — 어디가 굽었는지 좌표로 남긴다.</summary>
    private static double tCurvMax, tCurvX, tCurvY;
    /// <summary>★[0820] 표면 판을 못 놓은 **사유별** 수 — 행이 얇아서인지 프레임을 못 만들어서인지.</summary>
    private static int tPfThin, tPfFrame, tPfNoWall;
    /// <summary>[검토 심각1] 직전 <see cref="Slice"/> 묶음의 방향 뒤집힘 — 0이 아니면 벽이 어딘가에서 뒤집혔다.</summary>
    public static (int Odd, int Total) LastSideFlip => (tSideOdd, tSideTot);
    private static readonly int[] tDevHist = new int[DevBuckets.Length];
    /// <summary>이탈 분포를 사람이 읽는 한 줄로 — 눈금이 바뀌어도 표기가 따라오도록 한 군데서 만든다.</summary>
    private static string DevHist(int[] h)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < h.Length && i < DevBuckets.Length; i++)
            sb.Append($" · {DevBuckets[i] * 100:F1}cm↑ {h[i]}");
        return sb.ToString();
    }
    private static double tWedgeLenMax, tDevMax, tDevMaxX, tDevMaxY;
    /// <summary>★[0807] 성토에서 지반 아래로 <see cref="BuryDepth"/>보다 깊이 잠겨 만들지 않은 열 — 전 줄 합계.
    /// 이 숫자가 곧 '종전에 보이지도 않는 채로 만들던 판넬'의 규모다(내보내기 지연의 몸통).</summary>
    private static int tDeep; private static double tDeepMax, tDeepX, tDeepY;
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
        tMinColW = double.MaxValue; tMaxColW = tNarrowX = tNarrowY = 0;
        tNarrowN = tFaceCnt = tChordSplit = 0; tSliverFirst = "";
        tHoleN = 0; tHoleW = tHoleX = tHoleY = tNoSplitDev = 0; tHoleWhy = ""; tCorners.Clear();
        LastQuoins.Clear(); LastCornerUnits.Clear(); LastMasses.Clear(); LastFaces.Clear(); LastFacePanels.Clear();
        tQuoinN = 0; tQuoinMax = 0;
        tWedgeN = tWedgeDrop = tWedgePiece = tDevTot = 0;
        tSideOdd = tSideTot = 0; tSideFirst = ""; tNormInherit = tNormTot = 0;
        tWdShort = tWdNoDir = tWdCut = 0; tLowWall = 0;
        tCurvMax = tCurvX = tCurvY = 0; tPfThin = tPfFrame = tPfNoWall = 0;
        System.Array.Clear(tDevHist, 0, tDevHist.Length);
        tWedgeLenMax = tDevMax = tDevMaxX = tDevMaxY = 0;
        tDeep = 0; tDeepMax = tDeepX = tDeepY = 0;
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
    /// <summary>★★[JACK 0807] 판넬 사이에 실제로 남은 틈을 <b>재서 그 자리에 전용 얇은 객체를 세운다.</b>
    /// <para>
    /// JACK 원칙: "부족하면 얇은 거 전용객체 하나 만들어서 넣고(LOD는 포기, 재질만 통일)."
    /// 종전 필러는 <b>벽면 끝점끼리</b> 짝지어 만들었는데, 실제 틈은 코너에서 판넬 한 장쯤 떨어진 자리에
    /// 생겨(현장·하니스 실측 0.35m @ 코너로부터 1.7m) 필러가 엉뚱한 데 서고 틈은 그대로 남았다.
    /// </para>
    /// 그래서 <b>틈을 찾는 자(GapReport)를 그대로 써서 틈을 메운다</b> — 같은 정의, 같은 자리.
    /// 이러면 '어디에 세울지'를 따로 추측할 필요가 없다(추측이 결함의 근원이었다).
    /// 전 줄을 다 자른 <b>뒤에</b> 한 번 부른다 — 틈은 줄과 줄 사이에도 생기므로 줄마다 부르면 못 찾는다.
    /// </summary>
    /// <returns>세운 필러 수.</returns>
    /// <summary>★★[JACK 0807 '여전히 각진부에 삐져나와 · 길이를 참조하는 로직이 잘못된 듯한데'] <b>그 지적이 맞다.</b>
    /// <para>
    /// 코너 필러의 높이를 <b>이웃 벽면 끝 열</b>에서 가져오고 있었다. 그런데 데이라잇이 코너 옆 열을
    /// 통째로 지우면 그 '끝 열'은 <b>한참 뒤의 높은 열</b>이 되고, 필러가 그 높이를 그대로 받아
    /// 실제 벽보다 몇 m씩 솟는다. 기준을 옆 열이 아니라 <b>그 자리에 실제로 있는 판넬</b>로 바꾼다.
    /// </para>
    /// 모든 줄을 자른 <b>뒤에</b> 한 번 부른다 — 필러 주변 판넬이 다 모여 있어야 그 자리 높이를 알 수 있다.
    /// 축 방향은 그대로 두고 <b>길이만</b> 줄인다(기울기 유지). 주변에 판넬이 아예 없으면 허공 필러이므로 지운다.
    /// </summary>
    /// <returns>(줄인 개수, 지운 개수).</returns>
    /// <param name="near">필러 발치에서 이 거리 안의 <b>정점만</b> 본다(m). 판넬은 1:0.05로 기울어
    /// 5m 단이면 위 모서리가 발치에서 0.25m 물러나므로 0.7m면 <b>바로 옆 판넬의 옆면</b>이 다 들어오고,
    /// 한 칸 건너 판넬(1.6m 이상)은 확실히 빠진다 — 그 판넬의 먼 쪽 위 모서리를 기준으로 삼는 순간
    /// 오목 코너에서 필러가 0.3~0.5m 솟는다(JACK 0807 두 번째 스샷).</param>
    public static (int Trimmed, int Dropped) ClampQuoinsToPanels(IReadOnlyList<Tile> tiles, double near = 0.7)
    {
        int trimmed = 0, dropped = 0;
        if (tiles == null) return (0, 0);

        // ★★[JACK 0807 '저번처럼 삐죽 나오는 객체가 있어'] **코너 전용 판넬도 같은 안전망을 씌운다.**
        //   유닛 높이는 '코너에서 0.35m 물러난 열'의 높이에서 오는데, 데이라잇이 코너 쪽으로 내려오면
        //   그 열이 코너 자리보다 높다 — 유닛만 벽 위로 솟는다(JACK 인프라웍스 스샷).
        //   필러에 넣은 정리를 유닛에 안 씌운 것이 원인이다. 같은 종류의 객체는 같은 안전망을 지나야 한다.
        //   ※위·아래 단면은 정점 대응이 같으므로, 높이를 줄일 때 **두 단면을 보간**하면 기울기가 유지된다.
        for (int i = LastCornerUnits.Count - 1; i >= 0; i--)
        {
            var cu = LastCornerUnits[i];
            if (cu.Bot.Count == 0 || cu.Bot.Count != cu.Top.Count) { LastCornerUnits.RemoveAt(i); dropped++; continue; }
            // ★[JACK 0819] 스윕 덩어리는 이 보정을 **통째로** 건너뛴다.
            //   ① 정점마다 그 자리 데이라잇으로 이미 잘려 있다 — 아래 보정은 정점 0의 Z를 단면 전체의
            //      대표로 쓰므로(Lerp) 오히려 윗변을 뭉갠다.
            //   ② 매스 모드에서는 **이웃 판넬이 아예 없다** — '가까운 판넬이 없으면 허공'이라는 판정이
            //      벽 전체를 지워 버린다. 기준으로 삼을 것이 없으면 그 검사는 검사가 아니다.
            if (cu.Swept) continue;
            double zLo0 = cu.Bot[0].Z, zHi0 = cu.Top[0].Z;
            if (zHi0 - zLo0 < 1e-6) { LastCornerUnits.RemoveAt(i); dropped++; continue; }

            double topZ = double.MinValue, botZ = double.MaxValue;
            foreach (var t in tiles)
                foreach (var p in t.Poly)
                {
                    bool near2 = false;
                    foreach (var b in cu.Bot)
                    {
                        double dx = p.X - b.X, dy = p.Y - b.Y;
                        if (dx * dx + dy * dy <= near * near) { near2 = true; break; }
                    }
                    if (!near2) continue;
                    if (p.Z > topZ) topZ = p.Z;
                    if (p.Z < botZ) botZ = p.Z;
                }
            if (topZ == double.MinValue) { LastCornerUnits.RemoveAt(i); dropped++; continue; }   // 허공 유닛

            double lo2 = System.Math.Max(zLo0, botZ), hi2 = System.Math.Min(zHi0, topZ);
            if (hi2 - lo2 < 0.15) { LastCornerUnits.RemoveAt(i); dropped++; continue; }
            if (System.Math.Abs(lo2 - zLo0) < 1e-6 && System.Math.Abs(hi2 - zHi0) < 1e-6) continue;

            List<Point3> Lerp(double z)
            {
                double s = (z - zLo0) / (zHi0 - zLo0);
                var r = new List<Point3>(cu.Bot.Count);
                for (int k = 0; k < cu.Bot.Count; k++)
                    r.Add(new Point3(cu.Bot[k].X + (cu.Top[k].X - cu.Bot[k].X) * s,
                                     cu.Bot[k].Y + (cu.Top[k].Y - cu.Bot[k].Y) * s, z));
                return r;
            }
            LastCornerUnits[i] = new CornerUnit(Lerp(lo2), Lerp(hi2));
            trimmed++;
        }
        for (int i = LastQuoins.Count - 1; i >= 0; i--)
        {
            var q = LastQuoins[i];
            // ★★[JACK 0807 '여전히 삐죽 튀어나와' — 안전망 자체의 결함] **가까운 정점만** 센다.
            //   종전엔 '반경 안에 정점이 하나라도 있으면 **그 판넬의 최고점**'을 기준으로 삼았다.
            //   오목 코너에서는 벽 윗선이 코너 쪽으로 내려오는데, 코너 옆 판넬의 **먼 쪽 위 모서리**는
            //   훨씬 높다 — 그 높이까지 필러를 허용하니 코너에서 그만큼(실측 0.3~0.5m) 솟았다.
            //   필러가 메우는 건 **바로 옆 판넬의 옆면**이므로, 그 옆면 정점만 봐야 옳다.
            //   (한 칸 건너 판넬은 1.6m 이상 떨어져 있어 이 반경 밖이다.)
            double topZ = double.MinValue, botZ = double.MaxValue;
            foreach (var t in tiles)
                foreach (var p in t.Poly)
                {
                    double dx = p.X - q.Toe.X, dy = p.Y - q.Toe.Y;
                    if (dx * dx + dy * dy > near * near) continue;
                    if (p.Z > topZ) topZ = p.Z;
                    if (p.Z < botZ) botZ = p.Z;
                }
            if (topZ == double.MinValue) { LastQuoins.RemoveAt(i); dropped++; continue; }   // 허공 필러

            double dzx = q.Top.X - q.Toe.X, dzy = q.Top.Y - q.Toe.Y, dzz = q.Top.Z - q.Toe.Z;
            double dl = System.Math.Sqrt(dzx * dzx + dzy * dzy + dzz * dzz);
            if (dl < 1e-9 || System.Math.Abs(dzz) < 1e-6) continue;                          // 수평 필러는 없다
            double lo = System.Math.Max(q.Toe.Z, botZ), hi = System.Math.Min(q.Top.Z, topZ);
            if (hi - lo < 0.15) { LastQuoins.RemoveAt(i); dropped++; continue; }
            // ★[JACK 0807 '가로로 긴 이상한 객체'] **높이를 잘라낸 뒤에** 폭이 높이보다 커지면 그건 누운 판이다.
            //   만들 때는 세로로 길었어도, 여기서 벽 높이에 맞춰 자르면 납작해질 수 있다 — 그때 지운다.
            //   코너 쐐기는 세로 기둥이라야 한다. 납작한 조각이 벽 위에 얹히면 그게 곧 JACK이 집어낸 그 객체다.
            if (q.Width > (hi - lo) * 0.9) { LastQuoins.RemoveAt(i); dropped++; continue; }
            if (System.Math.Abs(lo - q.Toe.Z) < 1e-6 && System.Math.Abs(hi - q.Top.Z) < 1e-6) continue;

            Point3 At(double z)                                    // 축 위에서 표고 z인 점 — 기울기를 유지한다
            {
                double s = (z - q.Toe.Z) / dzz;
                return new Point3(q.Toe.X + dzx * s, q.Toe.Y + dzy * s, z);
            }
            LastQuoins[i] = q with { Toe = At(lo), Top = At(hi) };
            trimmed++;
        }
        return (trimmed, dropped);
    }

    /// <param name="cornerOnly">★[JACK 0807] <b>코너에서만</b> 세운다(기본).
    /// JACK: "중간에 빈공간을 얇은 띠형 객체로 막았는데 <b>이렇게 해결하면 안 됨</b> — 애초에 다음 패널을
    /// 댕겨서 작성하고 직선 양단 끝에서 LOD 낮은 객체 폭을 조절해 빈공간이 없게 작성해."
    /// 반면 코너 쐐기는 JACK이 같은 날 오전에 <b>전용 얇은 객체로 채우라</b>고 지시한 자리다.
    /// 둘을 가르는 기준은 '틈이 코너 옆인가'뿐이므로, 알려진 코너에서 <paramref name="cornerNear"/> 안쪽만 채운다.
    /// 벽 한가운데 틈은 <b>채우지 않고 그대로 남겨</b> GapReport가 반드시 드러내게 한다 — 거긴 배치를 고쳐야 할 자리다.</param>
    public static int AddGapFillers(IReadOnlyList<Tile> tiles, double minGap = GapTol, double maxGap = 1.5,
                                    bool cornerOnly = true, double cornerNear = 1.2)
    {
        // ※문턱(minGap)은 <see cref="GapReport"/>와 **반드시 같아야 한다.** 0.03으로 뒀다가
        //   '메웠는데 여전히 구멍 18곳'이 나왔다 — 줄눈(0.05)까지 '열린 끝'으로 세는 바람에
        //   자와 메우개가 서로 다른 자리를 가리켰다. 정의가 갈리면 계측이 거짓말을 한다(0806의 반복).
        var (sides, openL, openR) = ScanSides(tiles, minGap);
        int made = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (!openL[i]) continue;
            var Ti = tiles[i];
            double best = double.MaxValue; int bj = -1;
            for (int j = 0; j < tiles.Count; j++)
            {
                if (j == i || !openR[j]) continue;
                var Tj = tiles[j];
                if (Tj.Bench != Ti.Bench || Tj.Row != Ti.Row || Tj.Up != Ti.Up) continue;
                double d = Dist2(sides[j].RBot, sides[i].LBot);
                if (d < best) { best = d; bj = j; }
            }
            if (bj < 0 || best < minGap || best > maxGap) continue;
            // 이미 필러가 선 자리면 또 세우지 않는다(양쪽 끝에서 각각 한 번씩 찾으므로 짝당 2번 걸린다).
            var a0 = sides[i].LBot; var b0 = sides[bj].RBot;
            var a1v = sides[i].LTop; var b1v = sides[bj].RTop;
            // [JACK 0807 '삐죽삐죽'] 양옆 높이가 다르면 낮은 쪽에 맞춘다 — 안 그러면 낮은 벽 위로 날이 솟는다.
            double zLo = System.Math.Max(a0.Z, b0.Z), zHi = System.Math.Min(a1v.Z, b1v.Z);
            if (zHi - zLo < 0.15) continue;
            var mid0 = new Point3((a0.X + b0.X) / 2, (a0.Y + b0.Y) / 2, zLo);
            // 코너 전용 판넬이 이미 그 자리를 감쌌으면 필러를 또 세우지 않는다(둘 다 서면 뭉친다).
            if (CornerUnitCovers(mid0.X, mid0.Y, mid0.Z, best / 2 + 0.6)) continue;
            bool dup = false;
            foreach (var q in LastQuoins)
                if (System.Math.Abs(q.Toe.Z - mid0.Z) < 0.5 && Dist2(q.Toe, mid0) < 0.10) { dup = true; break; }
            if (dup) continue;
            var a1 = a1v; var b1 = b1v;
            var mid1 = new Point3((a1.X + b1.X) / 2, (a1.Y + b1.Y) / 2, zHi);
            double axX = b0.X - a0.X, axY = b0.Y - a0.Y;
            double axL = System.Math.Sqrt(axX * axX + axY * axY);
            if (axL < 1e-9) { axX = b1.X - a1.X; axY = b1.Y - a1.Y; axL = System.Math.Sqrt(axX * axX + axY * axY); }
            if (axL < 1e-9) continue;
            var wi = Ti.WAxis; var wj = tiles[bj].WAxis;
            double nwx = wi.x + wj.x, nwy = wi.y + wj.y, nwz = wi.z + wj.z;
            double nwl = System.Math.Sqrt(nwx * nwx + nwy * nwy + nwz * nwz);
            if (nwl < 1e-9) continue;
            // ★[JACK 0807] "패널 사이의 간격은 **어떠한 경우에도 5cm**를 유지하게 해."
            //   필러를 끼우면 줄눈이 둘 생긴다(판넬│필러│판넬) — 그러니 필러 폭 = 틈 − 5cm×2.
            //   종전엔 틈+0.02(겹치게)라 필러가 이웃을 파고들어 줄눈이 아예 없었다.
            double fw = best - 2 * JointW;
            if (fw < 0.03) continue;                 // 남는 폭이 실오라기면 안 만든다(그 자리는 이미 줄눈에 가깝다)
            // [JACK 0807] 코너 쐐기는 **세로로 긴 기둥** — 폭이 높이에 육박하면 누운 판이 된다.
            if (fw > (zHi - zLo) * 0.9) continue;
            // ★코너 옆인가 — 아니면 손대지 않는다(JACK 0807: 벽 한가운데는 메우는 게 아니라 안 생기게 한다).
            if (cornerOnly)
            {
                bool nearCorner = false;
                foreach (var c in tCorners)
                {
                    if (System.Math.Abs(c.Z - mid0.Z) > 6.0) continue;      // 같은 단 근처만
                    double dx = c.X - mid0.X, dy = c.Y - mid0.Y;
                    if (dx * dx + dy * dy <= cornerNear * cornerNear) { nearCorner = true; break; }
                }
                if (!nearCorner) continue;
            }
            LastQuoins.Add(new WallPanels.Quoin(mid0, mid1, (axX / axL, axY / axL, 0),
                                                (nwx / nwl, nwy / nwl, nwz / nwl), fw));
            tQuoinN++; if (best > tQuoinMax) tQuoinMax = best;
            made++;
        }
        return made;
    }

    /// <summary>판넬마다 좌·우 옆면(아래·위 끝점)과 그 끝이 '열려 있는지'를 한 번에 구한다 —
    /// <see cref="GapReport"/>(틈 찾기)와 <see cref="AddGapFillers"/>(틈 메우기)가 <b>같은 정의</b>를 쓰도록
    /// 한 군데로 모았다. 정의가 갈리면 '메웠는데 여전히 구멍 있음'이 나온다(0806에 자를 여섯 번 고친 이유).</summary>
    private static ((Point3 LBot, Point3 LTop, Point3 RBot, Point3 RTop)[] Sides, bool[] OpenL, bool[] OpenR)
        ScanSides(IReadOnlyList<Tile> tiles, double minGap)
    {
        int n = tiles.Count;
        var sides = new (Point3 LBot, Point3 LTop, Point3 RBot, Point3 RTop)[n];
        var Lm = new Point3[n]; var Rm = new Point3[n];
        for (int i = 0; i < n; i++)
        {
            var t = tiles[i];
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var (u, v) in t.Local) { u0 = System.Math.Min(u0, u); u1 = System.Math.Max(u1, u); v0 = System.Math.Min(v0, v); v1 = System.Math.Max(v1, v); }
            Point3 W(double u, double v) => new Point3(
                t.Origin.X + u * t.UAxis.x + v * t.VAxis.x,
                t.Origin.Y + u * t.UAxis.y + v * t.VAxis.y,
                t.Origin.Z + u * t.UAxis.z + v * t.VAxis.z);
            sides[i] = (W(u0, v0), W(u0, v1), W(u1, v0), W(u1, v1));
            double vm = (v0 + v1) / 2;
            Lm[i] = W(u0, vm); Rm[i] = W(u1, vm);
        }
        var openL = new bool[n]; var openR = new bool[n];
        for (int i = 0; i < n; i++)
        {
            double bl = double.MaxValue, br = double.MaxValue;
            var Ti = tiles[i];
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                var Tj = tiles[j];
                if (Tj.Bench != Ti.Bench || Tj.Row != Ti.Row || Tj.Up != Ti.Up) continue;
                double d1 = PtSegDist2D(Lm[i].X, Lm[i].Y, Lm[j].X, Lm[j].Y, Rm[j].X, Rm[j].Y);
                if (d1 < bl) bl = d1;
                double d2 = PtSegDist2D(Rm[i].X, Rm[i].Y, Lm[j].X, Lm[j].Y, Rm[j].X, Rm[j].Y);
                if (d2 < br) br = d2;
                if (bl < minGap && br < minGap) break;
            }
            openL[i] = bl >= minGap; openR[i] = br >= minGap;
        }
        return (sides, openL, openR);
    }

    /// <param name="runs">★[JACK 0807 스샷 '절토부 옹벽에 또 공백'] 판넬을 만든 옹벽선.
    /// 세로로 한 열이 통째로 빈 자리가 <b>줄 안</b>에서 생긴 건지 <b>줄과 줄 사이 이음매</b>에서 생긴 건지는
    /// 눈으로도 로그로도 안 갈렸다 — 두 원인은 고치는 자리가 완전히 다르다(판넬 배치 vs 옹벽선 분할).
    /// 틈마다 <b>가장 가까운 줄 끝점까지의 거리</b>를 붙여 가설을 반으로 자른다: 0에 가까우면 이음매다.</param>
    public static string GapReport(IReadOnlyList<Tile> tiles, double minGap = GapTol, double maxGap = 6.0,
        IReadOnlyList<WallRun>? runs = null)
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

        // ★[0806 성능] 끝점이 '열렸는지'를 **한 번만** 계산한다. 종전엔 열린 끝마다 상대편이 열렸는지를
        //   그 자리에서 다시 전수 조사해 **O(판넬수³)** 이 됐다 — 현장 594장이면 2억 번이라
        //   내보내기가 멈춘 것처럼 보였다(JACK 0806 '내보내기를 눌러도 반응이 없어, 엄청 오래 걸려').
        //   진단이 결과보다 오래 걸리면 그건 진단이 아니라 장애다. 미리 한 번 계산해 O(판넬수²)로 낮춘다.
        var openL = new bool[tiles.Count];
        var openR = new bool[tiles.Count];
        for (int i = 0; i < tiles.Count; i++)
        {
            double bl = double.MaxValue, br = double.MaxValue;
            var Ti = tiles[i];
            for (int j = 0; j < tiles.Count; j++)
            {
                if (j == i) continue;
                var Tj = tiles[j];
                if (Tj.Bench != Ti.Bench || Tj.Row != Ti.Row || Tj.Up != Ti.Up) continue;
                double d1 = PtSegDist2D(L[i].X, L[i].Y, L[j].X, L[j].Y, R[j].X, R[j].Y);
                if (d1 < bl) bl = d1;
                double d2 = PtSegDist2D(R[i].X, R[i].Y, L[j].X, L[j].Y, R[j].X, R[j].Y);
                if (d2 < br) br = d2;
                if (bl < minGap && br < minGap) break;      // 양쪽 다 막혔으면 더 볼 것 없다
            }
            openL[i] = bl >= minGap; openR[i] = br >= minGap;
        }

        for (int i = 0; i < L.Count; i++)
        {
            // ★[0806 v6] 자를 다섯 번째로 고친다. v5(방향 필터)가 왜 틀렸는지 —
            //   직각 코너에서 이웃 벽면 판넬은 내 끝점을 **가로질러** 있어서 그 중심이 진행 방향(+U) 쪽에 놓인다.
            //   방향으로 거르면 **바로 그 이웃이 제외**되고, 노치 건너편 판넬을 짝으로 잡아 3.61m를 지어냈다
            //   (실측: 노치 옆면은 Y 10.18~20.32로 코너를 지나 연속인데 '구멍'으로 찍혔다).
            //   → 옳은 방식: 끝점이 **어느 방향으로든** 다른 판넬 몸통에 닿아 있으면 '막힌 끝'이고,
            //     아무 데도 안 닿으면 '열린 끝'이다. 그리고 **열린 끝 둘이 마주 볼 때만** 구멍이다 —
            //     벽이 데이라잇에서 끝나는 자리는 열린 끝이 하나뿐이라 자연히 빠진다.
            if (!openL[i]) continue;                     // 어딘가에 닿아 있다 = 막힌 끝(정상)
            var Lt = tiles[L[i].I];
            // 열린 끝 — 마주 보는 열린 끝(다른 판넬의 오른쪽 끝)을 찾는다(위에서 미리 계산해 둔 openR 사용).
            double best = double.MaxValue; int bestJ = -1;
            for (int j = 0; j < tiles.Count; j++)
            {
                if (j == L[i].I || !openR[j]) continue;
                var Rt = tiles[j];
                if (Rt.Bench != Lt.Bench || Rt.Row != Lt.Row || Rt.Up != Lt.Up) continue;
                double d2 = System.Math.Sqrt((R[j].X - L[i].X) * (R[j].X - L[i].X) + (R[j].Y - L[i].Y) * (R[j].Y - L[i].Y));
                if (d2 < best) { best = d2; bestJ = j; }
            }
            if (bestJ < 0 || best < minGap || best > maxGap) continue;   // 붙었거나(정상) 벽 끝(짝 없음)
            // ★[JACK 0806] 그 자리에 **코너 필러**가 서 있으면 구멍이 아니다 — 판넬 사이가 벌어진 건 맞지만
            //   볼록 코너의 쐐기는 원래 판넬이 아니라 필러가 메우는 자리다. 필러를 안 보면
            //   메워 놓고도 '구멍 있음'으로 찍혀, 판넬을 늘리는 잘못된 처방으로 다시 끌려간다(4번 겪었다).
            double gmx = (L[i].X + R[bestJ].X) / 2, gmy = (L[i].Y + R[bestJ].Y) / 2;
            bool filled = false;
            foreach (var q in LastQuoins)
            {
                if (System.Math.Abs(q.Toe.Z - L[i].Z) > 6.0 && System.Math.Abs(q.Top.Z - L[i].Z) > 6.0) continue;
                double dq = System.Math.Sqrt((q.Toe.X - gmx) * (q.Toe.X - gmx) + (q.Toe.Y - gmy) * (q.Toe.Y - gmy));
                if (dq <= best / 2 + 0.35) { filled = true; break; }
            }
            // ★[JACK 0807] **코너 전용 판넬**이 그 자리를 메우고 있으면 구멍이 아니다 — 필러와 같은 이치다.
            if (!filled) filled = CornerUnitCovers(gmx, gmy, L[i].Z, best / 2 + 0.6);
            if (filled) continue;
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
            // ★[0807] 이 틈이 **옹벽선 이음매**에 있는가 — 가설을 반으로 자르는 한 숫자.
            //   줄 끝점 바로 위(≲0.5m)면 원인은 판넬 배치가 아니라 **옹벽선이 거기서 끊긴 것**이고,
            //   멀면 줄은 이어져 있는데 판넬이 빠진 것이다. 두 경우는 고칠 자리가 서로 다르다.
            string seam = "";
            if (runs != null)
            {
                double sd = double.MaxValue; int sRun = -1; bool sHead = false;
                for (int r = 0; r < runs.Count; r++)
                {
                    var cr = runs[r].Crest;
                    if (cr == null || cr.Count == 0) continue;
                    for (int e = 0; e < 2; e++)
                    {
                        var p = e == 0 ? cr[0] : cr[cr.Count - 1];
                        if (System.Math.Abs(p.Z - found[i].Z) > 6.0) continue;      // 같은 단 근처만
                        double d3 = System.Math.Sqrt((p.X - found[i].X) * (p.X - found[i].X)
                                                   + (p.Y - found[i].Y) * (p.Y - found[i].Y));
                        if (d3 < sd) { sd = d3; sRun = r; sHead = e == 0; }
                    }
                }
                seam = sd == double.MaxValue ? " · 줄끝 없음"
                     : sd <= 0.5 ? $" · ★줄 이음매(줄{sRun} {(sHead ? "시작" : "끝")} {sd:F2}m)"
                     : $" · 줄끝과 {sd:F1}m 떨어짐(줄 안의 구멍)";
            }
            top.Append($" [{found[i].D:F2}m @ {found[i].X:F0},{found[i].Y:F0} Z{found[i].Z:F1}" +
                       $"{(found[i].FullBoth ? " ★양옆 온전" : " 데이라잇 잘림")} · 가까운 코너 {ctag}{seam}]");
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
        int zeroN = 0, deepN = 0; double gLo = double.MaxValue, gHi = double.MinValue;
        foreach (var x in tPerLine)
        {
            if (x.Kept > 0) { sb.Append($"{x.Kept}/{x.Above} "); continue; }
            if (x.Deep > 0 && x.Above == 0) { deepN++; continue; }   // [0807] 성토 깊이묻힘 줄 — 따로 센다
            zeroN++;
            if (double.IsNaN(x.Gap)) continue;
            if (x.Gap < gLo) gLo = x.Gap;
            if (x.Gap > gHi) gHi = x.Gap;
        }
        if (zeroN > 0)
            sb.Append(gHi >= gLo ? $"+ 0장 {zeroN}줄(뜬거리 {gLo:F1}~{gHi:F1}m)" : $"+ 0장 {zeroN}줄");
        if (deepN > 0) sb.Append($" + 지반선아래 {deepN}줄");
        return sb.ToString().TrimEnd();
    }

    /// <summary>옹벽선 <b>전 줄</b> 합계 — <see cref="LastDiag"/>(마지막 한 줄)와 달리 전체 규모를 보여준다.</summary>
    public static string TotalDiag =>
        tCall == 0 ? "" :
        $"전체 {tCall}줄 합계 — 판넬 {tTile}(온전 {tFull})" +
        (tTile > 0 && tFull == 0 ? " · ⚠앵커·정착구가 하나도 안 달렸다(판넬이 0.80m 미만 — 단높이 확인)" : "") +
        $" · 버림(지반밖 {tGround} · 지반위 {tAbove} · 줄눈 {tJoint} · 퇴화 {tThin} · 실오라기 {tSliver})" +
        // ★[0807] 성토 전용 — 이 숫자만큼이 종전에 지표면 수십~수백 m 아래에 만들어지던(보이지도 않던) 열이다.
        (tDeep > 0 ? $" · 지반선아래 생략 {tDeep}열(최대 {tDeepMax:F0}m 아래 @ {tDeepX:F0},{tDeepY:F0} — InfraWorks에서 안 보이는 자리)" : "") +
        (tNonConvex > 0 ? $" · 오목 윗변 {tNonConvex}장(볼록 분해로 무늬 채움)" : "") +
        (tShort + tCap + tHole > 0
            ? $" · ⚠열 경고(못 미침 {tShort} · 상한낮음 {tCap} · 중간구멍 {tHole})"
            : " · 열 검사 이상 없음") +
        $" · 판넬↔옹벽선 최대 이탈 {tOff:F3}m @ {tOffX:F0},{tOffY:F0}" +
        $" · ★이탈 코너별 전체(오목 {tOffCav:F3}m @ {tOffCavX:F0},{tOffCavY:F0} · 볼록 {tOffCnv:F3}m · 코너밖 {tOffFar:F3}m)" +
        (tFacetCav + tFacetCnv > 0
            ? $" · 코너 조각 오목 {tFacetCav}/볼록 {tFacetCnv} · 최단 {tFacetMin:F2}m @ {tFacetX:F0},{tFacetY:F0}"
            : " · 코너 조각 없음") +
        (tFaceCnt > 0 && tMinColW <= tMaxColW
            ? $" · 열폭 {tMinColW:F2}~{tMaxColW:F2}m(벽면 {tFaceCnt}개" +
              (tNarrowN > 0 ? $" · 규격 미만 {tNarrowN}열(끝 자투리+급커브)" : " · 전부 규격") +
              (tChordSplit > 0 ? $" · 급커브 분할 {tChordSplit}열(안 쪼갰다면 이탈 최대 {tNoSplitDev:F3}m · 한도 {ChordTol:F2}m)" : "") +
              $" · 최소 @ {tNarrowX:F0},{tNarrowY:F0})" : "") +
        (tSliverFirst.Length > 0 ? $" · ⚠실오라기 구멍 첫 사례 {tSliverFirst}" : "") +
        (tQuoinN > 0 ? $" · 코너 필러 {tQuoinN}개(최대 폭 {tQuoinMax:F2}m — 볼록 코너 쐐기 메움)" : " · 코너 필러 0개(메울 쐐기 없음)") +
        // ★[JACK 0819] 각도를 안 쓰는 경로의 성적표. 쐐기 0개면 오른쪽 이탈 분포가 그 이유를 말해 준다.
        $" · ★★옹벽 매스 {tWedgePiece}개" + (tWedgeN > 0 ? $"(구간 {tWedgeN} · 최장 {tWedgeLenMax:F2}m)" : "") +
        (tNormTot > 0 ? $" · 방향 물려받은 자리 {tNormInherit}/{tNormTot}점(벽 낮아 못 잼 {tLowWall}점)" : "") +
        (tWdCut + tWdNoDir + tWdShort > 0
            ? $" · 못 세운 사유(데이라잇에 다 잘림 {tWdCut} · 방향 못 구함 {tWdNoDir} · 단면 부족 {tWdShort})" : "") +
        (tSideOdd > 0 ? $" · ⚠★★노출면 방향 뒤집힌 자리 {tSideOdd}/{tSideTot}점(벽이 뒤집혔다 · 첫 자리 @ {tSideFirst})"
                      : (tSideTot > 0 ? $" · 노출면 방향 일관({tSideTot}점)" : "")) +
        (tWedgeDrop > 0 ? $" · 못 세운 구간 {tWedgeDrop}개" : "") +
        $" · ★규격폭 이탈 최대 {tDevMax:F3}m @ {tDevMaxX:F0},{tDevMaxY:F0}" +
        $"(전체 {tDevTot}열 · ★직선 {tDevTot - tDevHist[0]}열" + DevHist(tDevHist) + $" · 쐐기 한도 {WedgeDev:F2}m)" +
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
        //   [0807] 깊이묻힘 줄(성토 아래 단)은 여기서 제외한다 — 설계대로 뺀 것이지 사라진 게 아니다.
        //   빼지 않으면 성토 옹벽을 돌릴 때마다 40줄 넘게 가짜 경고가 떠서 진짜 경고를 덮는다.
        // ★[JACK 0820] **매스 모드에서는 판넬 0장이 정상이다** — 벽은 매스가 만들고 판넬은 안 깐다.
        //   빼지 않으면 옹벽을 돌릴 때마다 '벽이 사라졌을 수 있음'이 매번 떠서 진짜 경고를 덮는다
        //   (오늘 '생성 ≠ 저장'·'앵커 > 판넬'과 같은 종류의 가짜 경고다).
        (!MassOnly && tPerLine.FindAll(x => x.Kept == 0 && x.Deep == 0 && (double.IsNaN(x.Gap) || x.Gap < -0.05)).Count is int susp && susp > 0
            ? $" · ⚠토우가 지반 아래인데 판넬 0장인 줄 {susp}개 — 벽이 사라졌을 수 있음"
            : (!MassOnly && tPerLine.FindAll(x => x.Kept == 0 && x.Deep == 0).Count is int zn && zn > 0
                ? $" · 판넬 0장인 줄 {zn}개는 전부 데이라잇 위(정상 — 붙잡을 흙 없음)" : ""));
}
