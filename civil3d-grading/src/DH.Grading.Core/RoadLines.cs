using System.Globalization;
using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 1119 «선 먼저» · 계획 .omc/plans/2026-11-19_진입도로_선먼저_엔진.md 1단계] 진입도로 사면 엔진 — 단 선을 먼저 긋고 그 선으로 면을 짓는다(부지 방식).
/// <para>판(Sheet) = 도로 둘레 원천마다 바깥 법선 광선 위 단면 F(d) — 광선 사이 세모(평면) 묶음 · 절토 판 = 아래 감싸기(낮은 쪽) · 성토 판 = 위 감싸기(높은 쪽) ·
/// 최종 z = min(HI, max(LO, 땅)) — 옛 고리 엔진과 같은 높이 규칙(JACK D1).</para>
/// <para>단 선 = «이긴 판 조각의 단면 거리 &lt; D_i» 자리의 경계(판 꺾임선 j의 보이는 토막 + 다른 원천 판과 만나는 골 토막) — 같은 단은 골을 따라 처음부터 한 줄.
/// 데이라잇 = 이긴 판 조각 ∩ 땅 세모의 정확한 영선 · 섬은 메움. 세모 = 이 선들만으로(칸 점 없음).</para>
/// <para>1단계: 옹벽 기록이 없는 도로만(<see cref="Eligible"/>) · 못 지으면 부르는 쪽(<see cref="RoadRing.Build"/>)이 옛 고리 엔진으로 물러섬 + 까닭 한 줄.</para></summary>
public static partial class RoadLines
{
    /// <summary>엔진 스위치 — 기본 켬 · 환경 변수 DH_ROAD_ENGINE=ring 이면 옛 고리 엔진(시험 · 문제 시 되돌림).</summary>
    public static bool On = !string.Equals(Environment.GetEnvironmentVariable("DH_ROAD_ENGINE"), "ring", StringComparison.OrdinalIgnoreCase);
    /// <summary>★★[2단계] 가장자리 옹벽 기록(<see cref="RoadGrade.Input.Walls"/>)이 없는 토막 — [옹벽 변환] 기록(0단부터 포함)은 띠마다 단면으로 짓고 ·
    /// [옹벽 추가] 선 옹벽은 이 면(P0) 위에 부르는 쪽이 얹음(<see cref="RoadGrade.ApplyWalls"/>).</summary>
    public static bool Eligible(RoadGrade.Input inp) => inp.Walls.Count == 0;
    /// <summary>★[2단계] 새 엔진이 못 지으면 옛 고리 엔진으로 물러서도 되는가 — 옛 고리 엔진은 1단(0단부터) [옹벽 변환] 기록을 못 받음(말없이 사면이 됨) → 그런 토막은 물러서지 않고 못 지음.</summary>
    public static bool RingCanTake(RoadGrade.Input inp) => inp.TierWalls.All(t => t.Tier >= 1);
    /// <summary>노트 머리 — «고리 면»으로 시작해 Civil 길 판정(RoadSession.PathOf · 키트)은 그대로 · «선 먼저»로 새 엔진임을 밝힘.</summary>
    public const string Head = "고리 면 — 선 먼저 엔진";
    /// <summary>[하네스] 단계 로그.</summary>
    internal static Action<string>? Debug;
    /// <summary>[하네스] 단계 도형 덤프(이름, 도형).</summary>
    internal static Action<string, List<Geometry>>? DebugGeo;

    /// <summary>새 엔진 결과 묶음(<see cref="RoadRing.Built.Lf"/>) — 소비처(노리선 · 소단 자리 · 결과 격자 · 계측)가 읽음.</summary>
    public sealed class Data
    {
        internal Ctx C = null!;
        /// <summary>골 · 등성이 끊김선(다른 원천 판이 만나는 곳 · 꺾임 큰 것) — 면에 넣은 그대로.</summary>
        public readonly List<List<Point3>> Creases = new();
        /// <summary>★[2b] 오목 굽이 C 곁이라 면에는 안 넣은 골 — 노리선 멈춤 선으로만 씀.</summary>
        public readonly List<List<Point3>> CoreCreases = new();
        /// <summary>★[2b · 검토 M3] 오래 둘 꾸러미(도로 면 캐시)용 — 다시 셀 수 있는 보이는 조각 캐시를 버림(노리선 · 소단 자리 · 단 표는 그대로 씀).</summary>
        public void Trim() => C?.DropVisCache();
        /// <summary>단 선 꼭짓점마다 원천 광선 방향(바깥 · 단위) — 골 따라 이은 점은 NaN(노리선 안 쏨).</summary>
        public readonly Dictionary<RoadGrade.TierLine, (double UX, double UY)[]> Dir = new(ReferenceEqualityComparer.Instance);
        /// <summary>지은 자리(도로 면 뺌) · 절토(부딪힘 · 절토 섬 포함) · 성토(성토 섬 포함).</summary>
        public Geometry? Region, RegionCut, RegionFill;
        /// <summary>절토 · 성토 영선이 섞인 구멍(땅 그대로 · 높이 = 땅).</summary>
        public Geometry? RegionGround;
        /// <summary>메운 섬 수 · 넓이 · 성토/절토 섞인 섬 수.</summary>
        public int Isles, IslesMixed; public double IsleArea;
        /// <summary>단 선 이음 못 한 끝(면 속에서 끝난 끝) 수 · 첫 자리.</summary>
        public int OpenEnds; public string OpenAt = "";
        /// <summary>판 세모 · 광선 · 골 토막 · 데이라잇 토막 수.</summary>
        public int Facets, Rays, CreaseSegs, ZeroSegs, Bends;
        /// <summary>★[2단계] 땅과 못 만나 뺀 판 세모 수.</summary>
        public int FacetsDropped;
        /// <summary>★[2단계] 절토 1단 시작선(도로 가장자리 중 절토 자리와 맞닿은 줄 · 묶음 번호) — [옹벽 변환] 고를 선만(<see cref="RoadGrade.EdgeStartLines"/>) · 면 · 그림 선 아님.</summary>
        public readonly List<RoadGrade.TierLine> EdgeStart = new();
        /// <summary>단계 ms.</summary>
        public long MsSheet, MsEnv, MsZero, MsRegion, MsLines, MsTin, MsFix, MsAll;
        public string Stats => string.Format(CultureInfo.InvariantCulture,
            "오목 굽이(한 점으로 모은 광선 무리) {17} · 광선 {0:N0} · 판 세모 {1:N0}(땅과 못 만나 뺌 {18:N0}) · 골 토막 {2:N0} · 영선 토막 {3:N0} · 섬 메움 {4}({5:F1} ㎡ · 섞임 {6}) · 이음 못 한 끝 {7}{8} · ms 판 {9} · 덮음 {10} · 영선 {11} · 자리 {12} · 선 {13} · 세모 {14} · 맞춤 {15} · 전체 {16}",
            Rays, Facets, CreaseSegs, ZeroSegs, Isles, IsleArea, IslesMixed, OpenEnds, OpenAt.Length > 0 ? " @" + OpenAt : "", MsSheet, MsEnv, MsZero, MsRegion, MsLines, MsTin, MsFix, MsAll, Bends, FacetsDropped);
    }

    /// <summary>한 조각 짓기 — <paramref name="tin"/> 거짓 = 미리보기(선 · 데이라잇 · 토량까지 · Civil 맞춤 안 함). 못 지으면 Ok = false · Note에 까닭.</summary>
    public static RoadRing.Built Build(RoadGrade.Input inp, bool tin = true)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var b = new RoadRing.Built();
        var deck = RoadRing.BuildDeck(inp, out string why, true);
        if (deck == null) { b.Note = "선 면 못 지음 — " + why; return b; }
        b.Deck = deck;
        if (inp.Base is not IGroundTris gt) { b.Note = "선 면 못 지음 — 바탕면 세모를 꺼낼 수 없음"; return b; }
        var data = new Data();
        b.Lf = data;
        try
        {
            var c = new Ctx(inp, deck, gt, data);
            data.C = c;
            c.Run(b, tin, sw);
        }
        catch (Exception ex)
        {
            b.Ok = false; b.Note = "선 면 못 지음 — 예외 " + ex.GetType().Name + ": " + ex.Message;
            Debug?.Invoke(ex.ToString());
        }
        data.MsAll = sw.ElapsedMilliseconds;
        return b;
    }
}
