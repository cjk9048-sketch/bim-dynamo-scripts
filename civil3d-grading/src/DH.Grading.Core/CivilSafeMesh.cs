using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.OverlayNG;

namespace DH.Grading.Core;

/// <summary>★★★[v100.4 · 0928 화면 없는 Civil 3D 실측] 순수옹벽_DH를 LandXML로 넘기기 전에 <b>Civil이 그대로 받는 삼각형</b>으로 다듬는다.
/// <para>Civil TIN이 LandXML 면을 받으며 하는 일 — accoreconsole /isolate · DHT·NCS 템플릿 · 현장 네 판으로 잰 것:</para>
/// <list type="bullet">
/// <item>좌표·높이는 비트 단위로 그대로 받는다.</item>
/// <item><b>네모 거리 max(|dx|,|dy|) &lt; 1e-4</b>(도면 단위 — 작은·원래·큰 좌표 셋 다 같은 문턱)인 두 점은 하나로 합친다(면 목록에 먼저 나온 점이 남는다 — 검토 0928).
///   v100.3 ClipToBand(1µm 격자)는 초록 선 위에 그런 쌍을 55~100개 만든다 — 17:32: 775점 → 675.</item>
/// <item>세 점이 거의 한 줄인 <b>납작한 면</b>(가장 낮은 높이 ≲ 1e-4)과 옆 면 사이 대각선을 뒤집는다. 볼록·같은 평면이어도 그 자리에서 면 하나가 통째로 빠진 적이 있다
///   (17:32: 0.0108㎡ · 테두리 45mm — 9/18 현장의 «최대 거리 41mm»와 같은 꼴).</item>
/// </list>
/// <para>그래서 ①네모 거리 <see cref="Gap"/> 안의 두 점은 <b>양쪽 방향을 다 재 보고</b> 오차가 작은 쪽으로 합치고 ②<see cref="FoldLen"/> 이하 변을 가진 납작한 면은 접고,
/// 긴 변 가운데 점이 문제인 납작한 면은 같은 평면이면 우리가 먼저 대각선을 뒤집는다 ③결과를 <b>정확히</b> 잰다 —
/// 옹벽 높이는 새 면과 옹벽 삼각형이 겹치는 다각형의 꼭짓점에서(면 가운데·변 가운데 표본은 참값의 절반만 봤다: 17:32 0.423 vs 0.847mm — 계획 검토 0928 · 높음 1),
/// 테두리는 양쪽 꼭짓점을 서로에게 비춘 자리와 5cm 간격 점에서(평면 거리는 이것으로 정확하다 — 높이 차는 모서리 이등분선에서 가장 가까운 초록 선 자리가
/// 건너뛰어 조금 적게 나올 수 있다: 검토 0928 v100.4 · 낮음 3의 반례 1:0.01에서 0.775mm인데 3D 거리는 8µm라 잣대가 꺾이는 것).</para>
/// <para>판정 3단(계획 검토 0928 · 높음 2): <b>1 정확</b>(평면 ≤10µm · 테두리·옹벽 높이 ≤1mm) · <b>2 거의</b>(≤0.2mm · ≤10mm — 자리를 적는다) · <b>3 못 함</b>(null).
/// 0.1mm 안에 진짜 꺾임점 둘이 있거나 옹벽 꺾임점이 초록 선 10~100µm 곁에 있으면 Civil 해상도(0.1mm)로는 10µm를 <b>원리상</b> 못 맞춘다 —
/// 그런 자리를 톱니 길(60mm)로 떨어뜨리지 않고 «거의»로 짓는다.</para></summary>
public static class CivilSafeMesh
{
    /// <summary>실측 Civil 점 합치기 문턱(네모 거리, 도면 단위).</summary>
    public const double CivilMerge = 1e-4;
    /// <summary>우리가 벌리는 간격 — 점이 1µm 격자 위에 있고 문턱이 좌표 크기와 무관(실측)하므로 5µm 여유. 20%로 벌리면 Civil이 안 합칠 쌍까지 억지로 합쳐 판을 깼다(계획 검토 0928 · 높음 2: 134.5µm 모서리).</summary>
    public const double Gap = 1.05e-4;
    /// <summary>납작한 면을 접을 때 접는 변의 최대 길이. 오차는 길이가 아니라 <b>정확한 평가(정확 기준 안)</b>가 묶는다 —
    /// 계획 검토 0928 · 높음 3의 «1m 가까운 테두리 변까지 접는다»는 그 평가에서 걸러진다.
    /// ★[0928 Civil 재시험] 0.24mm로 막았더니 현장 바늘 면(짧은 변 0.27~0.56mm · 긴 변 0.7~3.1m · 높이 5~28µm)이 안 접혀
    /// Civil이 28판 중 26판에서 면을 바꾸고 몇 판은 점까지 버려 모양이 깨졌다(45 · 460mm) — 길이 제한 없던 시험판은 0판.</summary>
    public const double FoldLen = 2e-3;
    const double PlanTol1 = 1e-5, ZTol1 = 1e-3, FlipTol1 = 2e-4;
    const double PlanTol2 = 2e-4, ZTol2 = 1e-2, FlipTol2 = 1e-3;
    const double OnRing = 1.5e-6;          // 초록 선 위로 치는 거리(ClipToBand 1µm 격자 맞춤 ≤0.71µm)
    const double OwnFlipDz = 2e-4;         // 우리가 먼저 뒤집어도 되는 높이 변화(두 대각선이 만나는 점) — 끝의 옹벽 높이 검사가 전체를 다시 지킨다
    /// <summary>★[v101.0 · 0929 화면 없는 Civil 재시험] 구역 모드(합성)에서 이보다 짧은 변은 정확 기준 안일 때 접는다.
    /// <para>합성 10판 중 1판(17:32@61.7°)에서 Civil이 옹벽 앞면의 바늘 면 하나(짧은 변 120µm · 높이 120µm · 긴 변 450mm, 긴 변은 들로네)와
    /// 그 곁 두 면을 뒤집어 0.001㎡ 구멍을 냈다. 88점짜리 작은 판으로 떼어도 똑같이 깨졌고, 짧은 변을 접으면(어느 쪽으로든) 그대로 받았다.
    /// 짧은 쪽 점을 옮겨 가며 잰 문턱: 긴 변 선까지 수직 거리 ≤122.0µm면 깨지고 ≥122.5µm면 받는다(거리·네모·방향을 바꿔 가른 것 — 네모나 점 사이 거리로는 안 맞음).
    /// 다른 판에는 높이 105~130µm · 들로네 아닌 바늘이 수백 개 있어도 Civil이 받았다 — 규칙을 다 아는 것은 <b>아니다</b>.
    /// 그래서 규칙을 흉내 내지 않고 이런 모양의 뿌리(0.25mm 안 짧은 변)를 정확 기준 안에서만 없앤다. 못 접은 것은 세어 적고, Civil 되읽기 관문이 끝을 지킨다.</para></summary>
    public const double ShortFold = 2.5e-4;
    /// <summary>짧은 변(납작하지는 않은 면)을 접을 때 쓰는 오차 한도 — 점수 0.2 = 높이 0.2mm(정확 기준 1mm의 1/5).
    /// 0929 다섯 판·돌린 판: 1로 두나 0.2로 두나 접힌 수(284~400)·남은 짧은 변(0~1)·옹벽 높이 최대(0.48~0.50mm — 이건 Civil 합치기 쌍 몫)가 같아 아껴 쓴다.</summary>
    const double ShortFoldScore = 0.2;

    /// <summary>하네스 전용 계측 — null이 아니면 합치기·접기·먼저 뒤집기와 남은 위험 면을 한 줄씩 적는다(출하 경로는 null).</summary>
    internal static Action<string>? DebugLog;          // ★[코드 검토 낮음 9] 하네스 전용 — 출하 DLL 밖에서 못 건드리게 internal(InternalsVisibleTo blocktest)

    /// <summary>하네스 전용 — 참이면 <b>옛 길</b>(v101: 여유 띠 쌍도 무조건 합침 · 마지막 수단 없음)만 돈다. 켬/끔 대조와
    /// «판정 1 판은 옛 길과 비트 그대로» 검사에 쓴다(계획 검토 r4 중간 2). 출하 경로는 거짓.</summary>
    internal static bool LegacyOnly;

    /// <summary>★★[v102.0 · 계획 검토 r3 C1] <b>반드시 합칠</b> 네모 거리 — Civil 문턱 1e-4(실측: 합친 쌍 ≤ 91µm · 남은 쌍 ≥ 100.00µm) +
    /// 1µm(격자 위 100µm 쌍의 부동소수 몫 — 좌표 차 부동소수 오차는 1e-10 m 대). [MustMerge, <see cref="Gap"/>) 쌍(5µm 여유 띠)은 Civil이 안 합친다 —
    /// <b>정확 기준 안(점수 ≤ 1)일 때만</b> 합치고 아니면 둔다. 0930 S143: 여유 띠 쌍 하나(네모 104µm)를 억지로 합쳐 테두리 11.8mm가 났다 — Civil 한계가 아니라 우리 여유였다.</summary>
    public const double MustMerge = 1.01e-4;

    public sealed class Report
    {
        public int Tier = 3;
        public int PtsIn, PtsOut, FacesIn, FacesOut;
        public int Pairs, Moves, MovesRing, MovesBorder, MovesInner, Unresolved; public double MoveMax; public string UnresolvedAt = "";
        public int Folds, OwnFlips, FlatLeft; public double FlatMinH = double.PositiveInfinity;
        /// <summary>구역 모드: 짧은 변(&lt; <see cref="ShortFold"/>) 접기 수 · 못 접고 남은 짧은 변 수 · 그중 가장 짧은 것.</summary>
        public int ShortFolds, ShortLeft; public double ShortMin = double.PositiveInfinity; public string ShortAt = "";
        /// <summary>구역 모드: 짧은 변은 없어도 가장 낮은 높이가 125µm 아래인 면(긴 변 안쪽) — 0929 실험의 Civil 문턱(122µm) 곁. 판정엔 안 넣고 적는다.</summary>
        public int ThinLeft; public string ThinAt = "";
        public int FlipRiskN; public double FlipRiskDz; public string FlipRiskAt = "";
        /// <summary>★[v102.0] 마지막 수단 — 위험한 납작한 면을 없앤 수 · 그중 꼭짓점 옮기기 · 가장 큰 점수(≤ 2).</summary>
        public int Snaps, LastResort; public double LastResortScore;
        /// <summary>★[v102.0] 판정이 나빠져 되돌린 것 — 마지막 수단(돌리기 전 결과를 씀) · 새 길 전체(옛 길 결과를 씀 — 그때 새 길 판정).</summary>
        public int LastResortReverted, NewPathWorse;
        /// <summary>옛 길로 지은 까닭(열쇠 비교) · 판정 2의 옹벽 높이 한도(합성은 남은 예산 — 기본 10mm).</summary>
        public string NewPathWhy = ""; public double WallTol2 = ZTol2;
        /// <summary>★[v102.0 · C1] 합치지 않고 둔 여유 띠 쌍(네모 [MustMerge, Gap) · 합치면 정확 기준 밖) · 마지막 수단 뒤 다시 본 쌍 수.</summary>
        public int MarginKept, PairsAgain;
        /// <summary>★[v102.0 · 계획 검토 r4 낮음 2] 정확 기준(점수 1)을 넘긴 손질의 자리 — 하네스가 «1mm 넘는 오차는 이 자리 곁에만»을 따로 잰다.</summary>
        public List<(double X, double Y, double Score)> BigOps = new();
        public Report Clone() { var r = (Report)MemberwiseClone(); r.BigOps = new List<(double X, double Y, double Score)>(BigOps); return r; }
        /// <summary>Civil이 고칠 면 — 가장 낮은 높이 &lt; 1e-4이고 가장 긴 변이 안쪽이며 들로네가 아닌 면(계획 검토 0928 · 중간 4: 73면 중 71~73면 일치).
        /// 이런 면 곁에서 Civil은 대각선을 뒤집고, 그러다 면이나 점을 통째로 빠뜨린 적이 있다(17:32 45mm · 돌린 13:33 460mm).</summary>
        public int CivilRisk; public string CivilRiskAt = "";
        public double MinBox = double.PositiveInfinity;
        public double PlanMax; public string PlanAt = "";
        public double BorderDz; public string BorderAt = "";
        public double WallDz; public string WallAt = "";
        public double AreaSum, AreaUnion;
        /// <summary>겹침 계산 예외·NaN 수 — 0이 아니면 그 자리 오차를 못 쟀으니 판정 3.</summary>
        public int OverlayExc;
        /// <summary>판정 2(거의)의 실제 까닭 — 정확 기준을 넘은 값과 자리.</summary>
        public string Tier2Why = "";
        public string Fail = "";
        public string TierText => Tier == 1 ? "정확" : Tier == 2 ? "⚠거의" : "⚠못 함";
        public string Summary
        {
            get
            {
                var ci = CultureInfo.InvariantCulture;
                string minBox = double.IsPositiveInfinity(MinBox) ? $"≥{Gap * 1e6:F0}" : (MinBox * 1e6).ToString("F0", ci);
                return $"판정 {Tier}({TierText}) · 점 {PtsIn}→{PtsOut} · 면 {FacesIn}→{FacesOut}"
                     + $" · 가까운 쌍 {Pairs}(합침 {Moves}: 초록 선 꼭짓점 {MovesRing} · 선 위 {MovesBorder} · 안쪽 {MovesInner} · 최대 이동 {MoveMax * 1e6:F0}µm{(Unresolved > 0 ? $" · ⚠못 푼 쌍 {Unresolved} @{UnresolvedAt}" : "")})"
                     + $" · 납작한 면 접기 {Folds} · 먼저 뒤집기 {OwnFlips}{(LastResort > 0 ? $" · 마지막 수단 {LastResort}(꼭짓점 옮기기 {Snaps} · 가장 큰 점수 {LastResortScore:F2}{(PairsAgain > 0 ? $" · 다시 본 쌍 {PairsAgain}" : "")})" : "")}{(LastResortReverted > 0 ? $" · ⚠마지막 수단 {LastResortReverted}건은 열쇠(판정·위험 면·높이 오차)가 나빠져 돌리기 전 결과를 씀" : "")}{(NewPathWorse > 0 ? $" · ⚠{NewPathWhy}" : "")}{(MarginKept > 0 ? $" · 여유 띠 쌍 둠 {MarginKept}" : "")} · 남은 납작한 면 {FlatLeft}{(FlatLeft > 0 ? $"(가장 낮은 {FlatMinH * 1e6:F0}µm)" : "")}"
                     + (ShortFolds + ShortLeft > 0 ? $" · 짧은 변 접기 {ShortFolds} · 남은 짧은 변 {ShortLeft}{(ShortLeft > 0 ? $"(가장 짧은 {ShortMin * 1e6:F0}µm @{ShortAt})" : "")}" : "")
                     + (ThinLeft > 0 ? $" · 가는 바늘(높이 <125µm) {ThinLeft} @{ThinAt}" : "")
                     + $" · 뒤집기 흉내 {FlipRiskN}변 최대 {FlipRiskDz * 1000:F3}mm{(FlipRiskDz > FlipTol1 ? " @" + FlipRiskAt : "")}"
                     + $" · Civil이 고칠 면 {CivilRisk}{(CivilRisk > 0 ? " @" + CivilRiskAt : "")}"
                     + $" · 남은 점 네모 최소 {minBox}µm"
                     + $" · 초록 선과 평면 {PlanMax * 1e6:F1}µm{(PlanMax > PlanTol1 ? " @" + PlanAt : "")}"
                     + $" · 테두리 높이 {BorderDz * 1000:F3}mm{(BorderDz > ZTol1 ? " @" + BorderAt : "")}"
                     + $" · 옹벽 높이 {WallDz * 1000:F3}mm{(WallDz > ZTol1 ? " @" + WallAt : "")}"
                     + (Math.Abs(AreaSum - AreaUnion) > 1e-9 + 1e-9 * AreaSum ? $" · ⚠면 겹침 {AreaSum - AreaUnion:E2}㎡" : "")
                     + (OverlayExc > 0 ? $" · ⚠겹침 계산 예외 {OverlayExc}" : "")
                     + (Tier2Why.Length > 0 ? $" · 거의인 까닭: {Tier2Why}" : "")
                     + (Fail.Length > 0 ? $" · ⚠{Fail}" : "");
            }
        }
    }

    /// <summary>ClipToBand 삼각형 → Civil이 그대로 받는 삼각형. 판정 3(못 함)이면 null — 까닭은 <paramref name="rep"/>.Fail·Summary.</summary>
    /// <param name="wallTris">그 자리 옹벽 삼각형(ClipToBand에 준 것 그대로) — 높이 대조의 참값.</param>
    public static List<WallDaylight.Tri>? Make(IReadOnlyList<WallDaylight.Tri> tris, IReadOnlyList<Point3> ring,
        IReadOnlyList<List<Point3>> holes, IReadOnlyList<WallDaylight.Tri> wallTris, out Report rep)
    {
        rep = new Report();
        if (tris == null || tris.Count == 0 || ring == null || ring.Count < 3 || wallTris == null || wallTris.Count == 0)
        { rep.Fail = "삼각형·띠·옹벽 중 빈 것이 있다"; return null; }
        var (res, r) = Run(() => new Work(tris, ring, holes, wallTris), ZTol2);
        rep = r;
        return res;
    }

    /// <summary>★★[v101.0 · 합성지표면_DH] <b>구역 모드</b> — 초록 선 없이, 받은 삼각형 자체를 참값으로 다듬는다.
    /// <para><paramref name="fixedPts"/>는 그대로 옮길 정지면 삼각형과 나누는 점이다 — 절대 없애지 않고(좌표도 비트 그대로),
    /// 둘 다 고정인 쌍은 Civil이 이미 받은 자기 점이라 간격 검사에서 뺀다. 높이 대조는 바뀐 면만(안 바뀐 면은 곧 참값).</para></summary>
    /// <param name="wallTol2">★[v102.0 · 코드 검토 중간 2] 판정 2의 옹벽 높이 한도 — 합성은 «높이 오차 합 ≤ 1cm»라 나머지 세 항(미리 맞춤·규칙 대조·한 점 한 높이)을
    /// 뺀 몫을 넘긴다. 그래야 새 길·옛 길·마지막 수단 가운데 <b>합성 예산을 지키는 쪽</b>을 고른다(판정이 같다고 오차 큰 쪽을 골라 합성이 판정 3이 되지 않게).</param>
    public static List<WallDaylight.Tri>? MakeZone(IReadOnlyList<WallDaylight.Tri> tris, IEnumerable<Point3> fixedPts, out Report rep, double wallTol2 = ZTol2)
    {
        rep = new Report();
        if (tris == null || tris.Count == 0) { rep.Fail = "삼각형이 없다"; return null; }
        var keys = new HashSet<(long, long)>(fixedPts.Select(q => ((long)Math.Round(q.X / 1e-7), (long)Math.Round(q.Y / 1e-7))));
        var (res, r) = Run(() => new Work(tris, null, Array.Empty<List<Point3>>(), tris, keys), Math.Min(ZTol2, Math.Max(0, wallTol2)));
        rep = r;
        return res;
    }

    /// <summary>★[v102.0 · 코드 검토 중간 2] 두 결과를 견주는 열쇠 — 작을수록 낫다: ①판정 ②위험한 납작한 면이 남았나(Civil이 고칠 면 · 뒤집기 흉내 &gt; 0.2mm)
    /// ③옹벽·테두리 높이 오차 중 큰 것. 판정이 같을 때 오차가 큰 쪽을 골라 합성 예산을 넘기지 않게 한다.</summary>
    static (int Tier, int Risky, double Err) Key(Report r) => (r.Tier, r.CivilRisk > 0 || r.FlipRiskDz > FlipTol1 ? 1 : 0, Math.Max(r.WallDz, r.BorderDz));
    static bool Better((int Tier, int Risky, double Err) a, (int Tier, int Risky, double Err) b) =>
        a.Tier != b.Tier ? a.Tier < b.Tier : a.Risky != b.Risky ? a.Risky < b.Risky : a.Err < b.Err - 1e-12;

    /// <summary>다듬기 — 새 길(여유 띠 쌍은 정확할 때만 · 위험한 면이 남으면 마지막 수단)을 돌리고, 판정 1이 아니면 <b>옛 길</b>(v101)도 돌려
    /// <see cref="Key"/>가 나은 쪽을 쓴다(같으면 새 길 — 계획 검토 r4 중간 2 · 코드 검토 중간 2). 판정 1인 새 길은 옛 길을 안 돈다.</summary>
    static (List<WallDaylight.Tri>? Res, Report Rep) Run(Func<Work> make, double wallTol2)
    {
        if (LegacyOnly) return RunPath(make(), margin: false, lastResort: false, wallTol2);
        var (resN, repN) = RunPath(make(), margin: true, lastResort: true, wallTol2);
        if (repN.Tier == 1 || repN.FacesIn == 0) return (resN, repN);
        var (resO, repO) = RunPath(make(), margin: false, lastResort: false, wallTol2);
        // ★[재검토 2] 둘 다 판정 3(못 함)이면 아무것도 안 짓는다 — 새 길(출하 길)의 실패 까닭을 그대로 알린다
        if (!(repN.Tier == 3 && repO.Tier == 3) && Better(Key(repO), Key(repN)))
        {
            repO.NewPathWorse = repN.Tier;
            repO.NewPathWhy = string.Format(CultureInfo.InvariantCulture, "새 길 판정 {0}·위험 면 {1}·높이 {2:F2}mm가 옛 길 판정 {3}·위험 면 {4}·높이 {5:F2}mm보다 나빠 옛 길로 지었다",
                repN.Tier, Key(repN).Risky, Key(repN).Err * 1e3, repO.Tier, Key(repO).Risky, Key(repO).Err * 1e3);
            DebugLog?.Invoke(repO.NewPathWhy);
            return (resO, repO);
        }
        return (resN, repN);
    }

    /// <summary>한 길 — 쌍 합치기 → 납작한 면 → 잼 → (마지막 수단이면 · 위험한 면이 남았으면) 마지막 수단 → 다시 잼.
    /// <para>★[v102.0 · 계획 4판 §2] 마지막 수단은 판정 1 조건(Civil이 고칠 면 0 · 뒤집기 흉내 ≤ 0.2mm)을 못 채운 판에서만 돈다.
    /// 돌린 뒤 판정이 더 나쁘면 <b>돌리기 전 결과</b>(스냅숏)를 쓴다 — 판정이 같으면 돌린 것을 쓴다.</para></summary>
    static (List<WallDaylight.Tri>? Res, Report Rep) RunPath(Work w, bool margin, bool lastResort, double wallTol2)
    {
        var rep = new Report { WallTol2 = wallTol2 };
        rep.PtsIn = w.PtsAlive(); rep.FacesIn = w.FacesAlive();
        if (rep.FacesIn == 0) { rep.Fail = "넓이 있는 면이 없다"; return (null, rep); }
        w.Margin = margin;
        w.ResolvePairs(rep);
        w.FoldFlat(rep);
        int e0 = w.OverlayExc;
        w.Measure(rep);
        rep.OverlayExc = w.OverlayExc;
        Judge(rep);
        if (!lastResort || !(rep.CivilRisk > 0 || rep.FlipRiskDz > FlipTol1))
            return (rep.Tier == 3 ? null : w.Output(), rep);
        // ★ 위험한 납작한 면이 남았다 — 돌리기 전 결과를 떠 두고 마지막 수단(한 바퀴 뒤 새로 생긴 쌍·납작한 면은 종전 길로 다시)
        var snapRes = rep.Tier == 3 ? null : w.Output();
        var snapRep = rep.Clone();
        w.OverlayExc = e0;                               // ★[검토 r4 중간 1] 첫 잼의 겹침 예외는 둘째 잼으로 옮기지 않는다
        int tier0 = rep.Tier, ops = 0;
        for (int k = 0; k < 4; k++)
        {
            int d = w.LastResort(rep);
            ops += d;
            if (d == 0) break;
            w.ResolvePairs(rep, again: true);
            w.FoldFlat(rep);
        }
        if (ops == 0) return (snapRes, snapRep);
        w.Measure(rep);
        rep.OverlayExc = w.OverlayExc;
        Judge(rep);
        // ★[코드 검토 중간 2] 돌린 쪽이 열쇠로 나쁘지 않으면(판정 · 위험 면 · 오차 순) 돌린 것 — 위험 면을 없앤 것은 판정이 같으면 낫다
        if (!Better(Key(snapRep), Key(rep))) return (rep.Tier == 3 ? null : w.Output(), rep);
        snapRep.LastResortReverted = ops;
        DebugLog?.Invoke($"마지막 수단 되돌림 — 판정 {tier0} → {rep.Tier}(높이 {Key(rep).Err * 1e3:F2}mm)이라 돌리기 전 결과를 쓴다");
        return (snapRes, snapRep);
    }

    /// <summary>잰 값으로 판정 1·2·3과 까닭을 적는다(여러 번 불러도 같다).</summary>
    static void Judge(Report rep)
    {
        rep.Tier2Why = "";
        if (!rep.Fail.StartsWith("합치기 예외")) rep.Fail = "";
        bool ok0 = rep.Unresolved == 0 && rep.MinBox >= MustMerge && Math.Abs(rep.AreaSum - rep.AreaUnion) <= 1e-9 + 1e-9 * rep.AreaSum && rep.OverlayExc == 0 && rep.Fail.Length == 0;
        bool t1 = ok0 && rep.PlanMax <= PlanTol1 && rep.BorderDz <= ZTol1 && rep.WallDz <= Math.Min(ZTol1, rep.WallTol2) && rep.FlipRiskDz <= FlipTol1 && rep.CivilRisk == 0;
        bool t2 = ok0 && rep.PlanMax <= PlanTol2 && rep.BorderDz <= ZTol2 && rep.WallDz <= rep.WallTol2 && rep.FlipRiskDz <= FlipTol2;
        rep.Tier = t1 ? 1 : t2 ? 2 : 3;
        if (rep.Tier == 2)
        {
            // ★[검토 0928 v100.4 · 낮음 8] «거의»의 까닭을 실제대로 — 해상도 탓만이 아니다(Civil이 고칠 면이 남았을 수도)
            var why2 = new List<string>();
            if (rep.PlanMax > PlanTol1) why2.Add($"초록 선과 평면 {rep.PlanMax * 1e6:F0}µm @{rep.PlanAt}");
            if (rep.BorderDz > ZTol1) why2.Add($"테두리 높이 {rep.BorderDz * 1000:F1}mm @{rep.BorderAt}");
            if (rep.WallDz > ZTol1) why2.Add($"옹벽 높이 {rep.WallDz * 1000:F1}mm @{rep.WallAt}");
            if (rep.FlipRiskDz > FlipTol1) why2.Add($"Civil이 뒤집으면 높이 {rep.FlipRiskDz * 1000:F1}mm @{rep.FlipRiskAt}");
            if (rep.CivilRisk > 0) why2.Add($"Civil이 고칠 면 {rep.CivilRisk} @{rep.CivilRiskAt}");
            rep.Tier2Why = string.Join(" · ", why2);
        }
        if (rep.Tier == 3)
        {
            var why = new List<string>();
            if (rep.Fail.Length > 0) why.Add(rep.Fail);
            if (rep.OverlayExc > 0) why.Add($"겹침 계산 예외 {rep.OverlayExc}");
            if (rep.Unresolved > 0) why.Add($"못 푼 쌍 {rep.Unresolved}");
            if (rep.MinBox < MustMerge) why.Add($"남은 점 네모 {rep.MinBox * 1e6:F0}µm");
            if (Math.Abs(rep.AreaSum - rep.AreaUnion) > 1e-9 + 1e-9 * rep.AreaSum) why.Add("면 겹침");
            if (rep.PlanMax > PlanTol2) why.Add($"평면 {rep.PlanMax * 1e6:F0}µm @{rep.PlanAt}");
            if (rep.BorderDz > ZTol2) why.Add($"테두리 높이 {rep.BorderDz * 1000:F1}mm @{rep.BorderAt}");
            if (rep.WallDz > rep.WallTol2) why.Add($"옹벽 높이 {rep.WallDz * 1000:F1}mm{(rep.WallTol2 < ZTol2 ? $"(한도 {rep.WallTol2 * 1000:F2}mm — 합성 높이 오차 합 1cm에서 남은 몫)" : "")} @{rep.WallAt}");
            if (rep.FlipRiskDz > FlipTol2) why.Add($"뒤집기 흉내 {rep.FlipRiskDz * 1000:F1}mm @{rep.FlipRiskAt}");
            rep.Fail = string.Join(" · ", why);
        }
    }

    // ══════════════════════════════ 안쪽 ══════════════════════════════

    private sealed class Work
    {
        readonly double ox, oy;
        readonly GeometryFactory gf = new();
        // 점(국소 좌표 x·y, 높이 z)
        readonly List<double> X = new(), Y = new(), Z = new();
        readonly List<bool> dead = new(), border = new(), ringV = new(), fixedV = new();
        // ★[v101.0 · 계획 검토 0928 v101 · 중간 5] 내보낼 때는 받은 세계 좌표를 <b>그대로</b> — (x − ox) + ox는 마지막 비트가 달라질 수 있어
        //   고정 점이 옆 정지면 삼각형과 어긋난 두 점이 되면 Civil이 합친다
        readonly List<double> WX = new(), WY = new();
        readonly bool zone;                       // 구역 모드(합성지표면) — 초록 선 없음 · 고정 점 · 참값 = 받은 삼각형
        readonly HashSet<int> modified = new(); bool initDone;
        /// <summary>★[v102.0 · C1] 새 길이면 여유 띠 쌍([MustMerge, Gap))은 정확할 때만 합친다 — 옛 길(거짓)은 v101처럼 무조건.</summary>
        public bool Margin;
        // 면(반시계) · 살았나 · 점 → 면
        readonly List<int[]> F = new();
        readonly List<bool> fAlive = new();
        readonly List<HashSet<int>> vf = new();
        // 초록 선(링·구멍) 변
        readonly List<(double X1, double Y1, double Z1, double X2, double Y2, double Z2)> rs = new();
        readonly STRtree<int> rsTree = new();
        // 옹벽 삼각형 — 참값
        readonly List<(double X1, double Y1, double Z1, double X2, double Y2, double Z2, double X3, double Y3, double Z3)> wt = new();
        readonly STRtree<int> wtTree = new();

        public Work(IReadOnlyList<WallDaylight.Tri> tris, IReadOnlyList<Point3>? ring, IReadOnlyList<List<Point3>> holes, IReadOnlyList<WallDaylight.Tri> wallTris,
                    HashSet<(long, long)>? fixedKeys = null)
        {
            zone = ring == null;
            ox = ring != null ? ring[0].X : tris[0].A.X; oy = ring != null ? ring[0].Y : tris[0].A.Y;
            // 점·면 — WriteLandXmlTin과 같은 1e-7 버킷(ClipToBand는 같은 점을 같은 double로 낸다)
            var id = new Dictionary<(long, long), int>();
            int Id(Point3 q)
            {
                var k = ((long)Math.Round(q.X / 1e-7), (long)Math.Round(q.Y / 1e-7));
                if (id.TryGetValue(k, out int v)) return v;
                X.Add(q.X - ox); Y.Add(q.Y - oy); Z.Add(q.Z); WX.Add(q.X); WY.Add(q.Y);
                dead.Add(false); border.Add(false); ringV.Add(false); fixedV.Add(fixedKeys != null && fixedKeys.Contains(k)); vf.Add(new HashSet<int>());
                id[k] = X.Count - 1; return X.Count - 1;
            }
            foreach (var t in tris)
            {
                int a = Id(t.A), b = Id(t.B), c = Id(t.C);
                if (a == b || b == c || a == c) continue;
                double s = Sa(a, b, c);
                if (s == 0) continue;
                AddFace(s > 0 ? new[] { a, b, c } : new[] { a, c, b });
            }
            // 초록 선(구역 모드엔 없다 — 테두리는 고정 점뿐)
            if (!zone)
            {
                foreach (var r0 in new[] { ring! }.Concat(holes))
                    for (int k = 0; k < r0.Count; k++)
                    {
                        var a = r0[k]; var b = r0[(k + 1) % r0.Count];
                        rsTree.Insert(new Envelope(a.X - ox, b.X - ox, a.Y - oy, b.Y - oy), rs.Count);
                        rs.Add((a.X - ox, a.Y - oy, a.Z, b.X - ox, b.Y - oy, b.Z));
                    }
                rsTree.Build();
            }
            var rvTree = new STRtree<(double, double)>();
            if (!zone) { foreach (var r0 in new[] { ring! }.Concat(holes)) foreach (var p in r0) rvTree.Insert(new Envelope(p.X - ox, p.X - ox, p.Y - oy, p.Y - oy), (p.X - ox, p.Y - oy)); rvTree.Build(); }
            for (int i = 0; i < X.Count; i++)
            {
                if (vf[i].Count == 0) { dead[i] = true; continue; }
                if (zone) continue;
                border[i] = NearRing(X[i], Y[i], OnRing, out _, out _, out _) <= OnRing;
                foreach (var (vx, vy) in rvTree.Query(new Envelope(X[i] - OnRing, X[i] + OnRing, Y[i] - OnRing, Y[i] + OnRing)))
                    if (Math.Max(Math.Abs(vx - X[i]), Math.Abs(vy - Y[i])) <= OnRing) { ringV[i] = true; break; }
            }
            // 옹벽
            foreach (var t in wallTris)
            {
                var e = (t.A.X - ox, t.A.Y - oy, t.A.Z, t.B.X - ox, t.B.Y - oy, t.B.Z, t.C.X - ox, t.C.Y - oy, t.C.Z);
                double s = (e.Item4 - e.Item1) * (e.Item8 - e.Item2) - (e.Item5 - e.Item2) * (e.Item7 - e.Item1);
                if (!(Math.Abs(s) > 1e-18)) continue;
                wtTree.Insert(new Envelope(Math.Min(e.Item1, Math.Min(e.Item4, e.Item7)), Math.Max(e.Item1, Math.Max(e.Item4, e.Item7)),
                                           Math.Min(e.Item2, Math.Min(e.Item5, e.Item8)), Math.Max(e.Item2, Math.Max(e.Item5, e.Item8))), wt.Count);
                wt.Add(e);
            }
            wtTree.Build();
            initDone = true;
        }

        void AddFace(int[] f) { F.Add(f); fAlive.Add(true); foreach (int v in f) vf[v].Add(F.Count - 1); if (initDone) modified.Add(F.Count - 1); }
        void KillFace(int fi) { fAlive[fi] = false; foreach (int v in F[fi]) vf[v].Remove(fi); }
        public int PtsAlive() { int n = 0; for (int i = 0; i < X.Count; i++) if (!dead[i] && vf[i].Count > 0) n++; return n; }
        public int FacesAlive() { int n = 0; for (int i = 0; i < F.Count; i++) if (fAlive[i]) n++; return n; }

        double Sa(int a, int b, int c) => ((X[b] - X[a]) * (Y[c] - Y[a]) - (Y[b] - Y[a]) * (X[c] - X[a])) / 2;
        double Len(int a, int b) => Math.Sqrt((X[a] - X[b]) * (X[a] - X[b]) + (Y[a] - Y[b]) * (Y[a] - Y[b]));
        double Box(int a, int b) => Math.Max(Math.Abs(X[a] - X[b]), Math.Abs(Y[a] - Y[b]));
        /// <summary>가장 낮은 높이 = 2×넓이 / 가장 긴 변.</summary>
        double Low(int[] f) => 2 * Math.Abs(Sa(f[0], f[1], f[2])) / Math.Max(Len(f[0], f[1]), Math.Max(Len(f[1], f[2]), Len(f[2], f[0])));
        string At(double x, double y) => string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4})", x + ox, y + oy);

        /// <summary>초록 선에서 가장 가까운 자리 — 거리·그 자리 초록 선 높이.</summary>
        double NearRing(double x, double y, double win, out double zr, out double px, out double py)
        {
            zr = double.NaN; px = x; py = y; double best = double.PositiveInfinity;
            for (double w = Math.Max(win, 1e-5); ; w *= 10)
            {
                foreach (int i in rsTree.Query(new Envelope(x - w, x + w, y - w, y + w)))
                {
                    var s = rs[i];
                    double ex = s.X2 - s.X1, ey = s.Y2 - s.Y1, L2 = ex * ex + ey * ey;
                    double u = L2 < 1e-24 ? 0 : Math.Clamp(((x - s.X1) * ex + (y - s.Y1) * ey) / L2, 0, 1);
                    double qx = s.X1 + ex * u, qy = s.Y1 + ey * u, d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
                    if (d < best) { best = d; zr = s.Z1 + (s.Z2 - s.Z1) * u; px = qx; py = qy; }
                }
                if (best <= w || w > 1e3) return best;
            }
        }

        /// <summary>면 평면 높이.</summary>
        static double PlaneZ(double x1, double y1, double z1, double x2, double y2, double z2, double x3, double y3, double z3, double x, double y)
        {
            double d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
            double l1 = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / d, l2 = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / d;
            return l1 * z1 + l2 * z2 + (1 - l1 - l2) * z3;
        }

        /// <summary>★[계획 검토 0928 · 높음 1] 면 하나의 옹벽 높이 오차 — 면과 옹벽 삼각형이 <b>겹치는 다각형의 꼭짓점</b>마다(두 평면의 차는 겹침 조각 안에서 1차라 최대는 꼭짓점에 있다).</summary>
        double FaceWallErr(int a, int b, int c, out double atX, out double atY)
        {
            atX = X[a]; atY = Y[a];
            var poly = gf.CreatePolygon(new[] { new Coordinate(X[a], Y[a]), new Coordinate(X[b], Y[b]), new Coordinate(X[c], Y[c]), new Coordinate(X[a], Y[a]) });
            var env = poly.EnvelopeInternal;
            double max = 0;
            foreach (int k in wtTree.Query(env))
            {
                var t = wt[k];
                var wp = gf.CreatePolygon(new[] { new Coordinate(t.X1, t.Y1), new Coordinate(t.X2, t.Y2), new Coordinate(t.X3, t.Y3), new Coordinate(t.X1, t.Y1) });
                Geometry inter;
                // ★[검토 0928 v100.4 · 낮음 7] 예외·NaN을 조용히 건너뛰면 그 자리 오차가 0으로 셈해진다 — 세어서 판정 3으로
                try { inter = OverlayNGRobust.Overlay(poly, wp, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
                catch { OverlayExc++; continue; }
                if (inter.IsEmpty || inter.Area <= 0) continue;
                foreach (var q in inter.Coordinates)
                {
                    double zf = PlaneZ(X[a], Y[a], Z[a], X[b], Y[b], Z[b], X[c], Y[c], Z[c], q.X, q.Y);
                    double zw = PlaneZ(t.X1, t.Y1, t.Z1, t.X2, t.Y2, t.Z2, t.X3, t.Y3, t.Z3, q.X, q.Y);
                    double dz = Math.Abs(zf - zw);
                    if (double.IsNaN(dz)) { OverlayExc++; continue; }
                    if (dz > max) { max = dz; atX = q.X; atY = q.Y; }
                }
            }
            return max;
        }

        /// <summary>겹침 계산 예외·NaN 수(0이 아니면 판정 3).</summary>
        public int OverlayExc;

        /// <summary>점이 이 삼각망의 바깥 테두리 위인가 — 그 점에서 나가는 변 중 면 하나만 쓰는 변이 있나.</summary>
        bool OnMeshBorder(int v)
        {
            var cnt = new Dictionary<int, int>();
            foreach (int fi in vf[v]) foreach (int x in F[fi]) if (x != v) cnt[x] = cnt.TryGetValue(x, out int n) ? n + 1 : 1;
            return cnt.Values.Any(n => n == 1);
        }

        /// <summary>계측용 — 마지막 EvalMove의 옹벽 높이 · 테두리 높이 · 평면 거리.</summary>
        double lastWall, lastBz, lastPlan;
        string Kind(int v) => fixedV[v] ? "고정" : ringV[v] ? "선꼭짓점" : border[v] ? "선위" : "안쪽";
        string Pt(int v) => string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4} {3})", X[v] + ox, Y[v] + oy, Z[v], Kind(v));

        /// <summary>gone을 keep 자리로 합쳐 보면 — 되나(이음 조건 · 뒤집힘 없음) · 점수(1 이하 = 정확 기준 안).</summary>
        bool EvalMove(int gone, int keep, out double score, out List<int[]> newFaces, out List<int> kill)
        {
            score = double.PositiveInfinity; newFaces = new(); kill = new();
            if (fixedV[gone]) return false;                            // 고정 점(그대로 옮길 정지면과 나누는 점)은 안 없앤다
            var fg = vf[gone].ToList(); var fk = vf[keep];
            var both = fg.Where(fk.Contains).ToList();
            if (both.Count == 0) return false;                        // 변으로 이어지지 않은 쌍 — 합치면 겹친다
            // 이음 조건: 두 점의 공통 이웃 = 두 점을 함께 쓰는 면의 맞은편 점
            var ng = new HashSet<int>(); foreach (int fi in fg) foreach (int v in F[fi]) if (v != gone) ng.Add(v);
            var nk = new HashSet<int>(); foreach (int fi in fk) foreach (int v in F[fi]) if (v != keep) nk.Add(v);
            ng.Remove(keep); nk.Remove(gone); ng.IntersectWith(nk);
            var opp = new HashSet<int>(); foreach (int fi in both) foreach (int v in F[fi]) if (v != gone && v != keep) opp.Add(v);
            if (!ng.SetEquals(opp)) return false;
            // ★[검토 0928 v100.4 · 낮음 5] 두 점이 다 바깥 테두리 위인데 둘을 잇는 변은 안쪽 변(면 둘) — 접으면 잘록한 목이
            //   한 점으로 조여 두 삼각형이 꼭짓점 하나로만 닿는다(폭 60µm 모래시계에서 잼)
            if (both.Count > 1 && OnMeshBorder(gone) && OnMeshBorder(keep)) return false;
            double wall = 0;
            foreach (int fi in fg)
            {
                kill.Add(fi);
                if (both.Contains(fi)) continue;
                var nf = F[fi].Select(v => v == gone ? keep : v).ToArray();
                if (!(Sa(nf[0], nf[1], nf[2]) > 1e-15)) return false;  // 뒤집힘·넓이 0
                newFaces.Add(nf);
                wall = Math.Max(wall, FaceWallErr(nf[0], nf[1], nf[2], out _, out _));
            }
            // 테두리: gone이 초록 선 위였으면 새 테두리가 그 자리에서 얼마나 떨어지나 · keep이 안쪽인데 테두리가 되면 keep이 초록 선에서 얼마나 떨어지나
            double plan = 0, bz = 0;
            if (border[gone] || border[keep])
            {
                var around = fk.Where(fi => !both.Contains(fi)).Select(fi => F[fi]).Concat(newFaces).ToList();
                var use = new Dictionary<int, int>();
                foreach (var f in around) foreach (int v in f) if (v != keep) use[v] = use.TryGetValue(v, out int n) ? n + 1 : 1;
                var bnb = use.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();   // keep과 테두리 변으로 이어질 점
                if (border[gone] && bnb.Count > 0)
                {
                    double best = double.PositiveInfinity, zb = Z[keep];
                    foreach (int v in bnb)
                    {
                        double ex = X[v] - X[keep], ey = Y[v] - Y[keep], L2 = ex * ex + ey * ey;
                        double u = L2 < 1e-24 ? 0 : Math.Clamp(((X[gone] - X[keep]) * ex + (Y[gone] - Y[keep]) * ey) / L2, 0, 1);
                        double qx = X[keep] + ex * u, qy = Y[keep] + ey * u, d = Math.Sqrt((X[gone] - qx) * (X[gone] - qx) + (Y[gone] - qy) * (Y[gone] - qy));
                        if (d < best) { best = d; zb = Z[keep] + (Z[v] - Z[keep]) * u; }
                    }
                    plan = best;
                    NearRing(X[gone], Y[gone], OnRing, out double zr, out _, out _);
                    if (!double.IsNaN(zr)) bz = Math.Abs(zb - zr);
                }
                if (!border[keep] && border[gone])
                {
                    double d = NearRing(X[keep], Y[keep], OnRing, out double zr, out _, out _);
                    plan = Math.Max(plan, d);
                    if (!double.IsNaN(zr)) bz = Math.Max(bz, Math.Abs(Z[keep] - zr));
                }
            }
            lastWall = wall; lastBz = bz; lastPlan = plan;
            // 점수: 정확 기준(평면 10µm · 높이 1mm) 안이면 [0,1] — 그 안에서는 <b>높이를 먼저</b> 줄인다(평면은 20µm로 나눈다).
            //   ★[0928 17:32 돌린 판] 옹벽 꺾임점이 초록 선 안쪽 8µm인 자리에서 «꺾임점을 선 쪽으로»(옹벽 높이 0.85mm)와
            //   «선 점을 꺾임점 쪽으로»(평면 8µm · 높이 0.06mm)가 거의 비겨 방향마다 갈렸다 — JACK이 보는 것은 높이다(9/18 톱니도 높이).
            //   기준을 넘는 후보는 언제나 뒤로(1 + 거의 기준으로 나눈 값)
            bool in1 = wall <= ZTol1 && bz <= ZTol1 && plan <= PlanTol1;
            score = in1 ? Math.Max(wall / ZTol1, Math.Max(bz / ZTol1, plan / (2 * PlanTol1)))
                        : 1 + Math.Max(wall / ZTol2, Math.Max(bz / ZTol2, plan / PlanTol2));
            return true;
        }

        void ApplyMove(int gone, int keep, List<int[]> newFaces, List<int> kill)
        {
            var touched = new HashSet<int>();
            foreach (int fi in kill) { foreach (int v in F[fi]) touched.Add(v); KillFace(fi); }
            foreach (var nf in newFaces) AddFace(nf);
            dead[gone] = true;
            border[keep] = border[keep] || border[gone];
            // ★[0928 17:32 재현] 1µm 세모(2·3·4) 한 변을 접으면 세 번째 점은 면이 하나도 안 남는다 — 그 점을 살려 두면
            //   다음 쌍(3-4)을 «이어지지 않은 쌍»으로 세어 판정 3이 났다. 면이 없는 점은 함께 지운다(모양은 1µm 안이라 그대로)
            foreach (int v in touched) if (v != keep && vf[v].Count == 0) dead[v] = true;
        }

        /// <summary>① 네모 거리 Gap 안의 두 점 — 가까운 쌍부터, 양쪽 방향을 다 재 보고 점수가 낮은 쪽으로 합친다.</summary>
        public void ResolvePairs(Report rep, bool again = false)
        {
            var cell = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < X.Count; i++)
            {
                if (dead[i]) continue;
                var k = ((long)Math.Floor(X[i] / Gap), (long)Math.Floor(Y[i] / Gap));
                if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(i);
            }
            var pairs = new List<(double D, int A, int B)>();
            foreach (var kv in cell)
                foreach (int i in kv.Value)
                    for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l))
                            foreach (int j in l) if (j > i && Box(i, j) < Gap && !(fixedV[i] && fixedV[j])) pairs.Add((Box(i, j), i, j));
            pairs.Sort((p, q) => p.D.CompareTo(q.D));
            // ★[v102.0 · 검토 r4 낮음 5] 마지막 수단 뒤 다시 볼 때는 따로 센다 — 못 푼 쌍 · 둔 여유 띠 쌍은 이번 판으로 다시
            if (again) { rep.PairsAgain += pairs.Count; rep.Unresolved = 0; rep.UnresolvedAt = ""; rep.MarginKept = 0; }
            else rep.Pairs = pairs.Count;
            // ★[검토 0928 v100.4 · 중간 2] 한 번 보고 버리지 않는다 — 먼저 본 쌍이 아직 변으로 안 이어졌거나(다른 변이 가로지름)
            //   잘록한 목이라 막혀도, 다른 쌍을 합치고 나면 풀린다. 더 합칠 것이 없을 때까지 되풀이하고 남은 것만 «못 푼 쌍»으로
            var pending = pairs;
            for (int pass = 0; pass < 8 && pending.Count > 0; pass++)
            {
                var next = new List<(double D, int A, int B)>();
                int moved = 0;
                foreach (var (dd, a, b) in pending)
                {
                    if (dead[a] || dead[b]) continue;
                    bool okA = EvalMove(a, b, out double sA, out var nfA, out var kA);     // a를 없앤다
                    var eA = (lastWall, lastBz, lastPlan);
                    bool okB = EvalMove(b, a, out double sB, out var nfB, out var kB);     // b를 없앤다
                    var eB = (lastWall, lastBz, lastPlan);
                    // ★[검토 r4 중간 2] 이어지지 않은 쌍은 여유 띠여도 <b>다음 판에 다시</b> 본다(«한 번 보고 버리지 않는다») — 끝의 못 푼 쌍 셈에서만 뺀다
                    if (!okA && !okB) { next.Add((dd, a, b)); continue; }
                    // ★[v102.0 · C1] 여유 띠 쌍은 정확 기준 안일 때만 — Civil이 안 합치는 쌍을 우리가 비싸게 합치지 않는다
                    if (Margin && dd >= MustMerge && Math.Min(okA ? sA : double.PositiveInfinity, okB ? sB : double.PositiveInfinity) > 1)
                    {
                        DebugLog?.Invoke(string.Format(CultureInfo.InvariantCulture, "여유 띠 쌍 둠 네모 {0:F1}µm · {1} · {2} · 점수 {3:F3}/{4:F3}", dd * 1e6, Pt(a), Pt(b), okA ? sA : double.NaN, okB ? sB : double.NaN));
                        rep.MarginKept++;
                        continue;
                    }
                    // 점수가 같으면 초록 선 꼭짓점 → 선 위 → 안쪽 순으로 남긴다
                    int Rank(int v) => fixedV[v] ? -1 : ringV[v] ? 0 : border[v] ? 1 : 2;
                    bool takeA = okA && (!okB || sA < sB || (sA == sB && Rank(a) >= Rank(b)));
                    int gone = takeA ? a : b, keep = takeA ? b : a;
                    if (DebugLog != null)
                    {
                        string E((double W, double B, double P) e) => string.Format(CultureInfo.InvariantCulture, "옹벽 {0:F3}mm · 테두리 {1:F3}mm · 평면 {2:F1}µm", e.W * 1e3, e.B * 1e3, e.P * 1e6);
                        var ce = takeA ? eA : eB; double cs = takeA ? sA : sB;
                        DebugLog(string.Format(CultureInfo.InvariantCulture, "쌍[{0}] 네모 {1:F1}µm · 없앰 {2} → 남김 {3} · 점수 {4:F3}({5}) · 반대쪽 {6}",
                            pass, dd * 1e6, Pt(gone), Pt(keep), cs, E(ce),
                            (takeA ? okB : okA) ? $"{(takeA ? sB : sA):F3}({E(takeA ? eB : eA)})" : "안 됨"));
                    }
                    rep.Moves++; moved++;
                    if (ringV[gone]) rep.MovesRing++; else if (border[gone]) rep.MovesBorder++; else rep.MovesInner++;
                    rep.MoveMax = Math.Max(rep.MoveMax, Len(gone, keep));
                    { double sc = takeA ? sA : sB; if (sc > 1) rep.BigOps.Add((X[gone] + ox, Y[gone] + oy, sc)); }
                    if (takeA) ApplyMove(a, b, nfA, kA); else ApplyMove(b, a, nfB, kB);
                }
                pending = next;
                if (moved == 0) break;
            }
            foreach (var (dd, a, b) in pending)
            {
                if (dead[a] || dead[b]) continue;
                if (Margin && dd >= MustMerge) { rep.MarginKept++; continue; }     // 이어지지 않은 여유 띠 쌍 — Civil이 안 합친다
                DebugLog?.Invoke($"못 푼 쌍 {Pt(a)} · {Pt(b)} · 네모 {Box(a, b) * 1e6:F1}µm");
                rep.Unresolved++;
                if (rep.UnresolvedAt.Length < 200) rep.UnresolvedAt += (rep.UnresolvedAt.Length > 0 ? " " : "") + At(X[a], Y[a]);
            }
        }

        /// <summary>② 납작한 면 — 짧은 변(≤FoldLen)은 접고(정확 기준 안일 때만), 아니면 같은 평면·볼록일 때 긴 변을 우리가 먼저 뒤집는다.</summary>
        public void FoldFlat(Report rep)
        {
            for (int round = 0; round < 30; round++)
            {
                bool changed = false;
                for (int fi = 0; fi < F.Count; fi++)
                {
                    if (!fAlive[fi]) continue;
                    var f = F[fi];
                    // 가장 짧은 변
                    int su = -1, sv = -1; double sl = double.PositiveInfinity;
                    for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l < sl) { sl = l; su = u; sv = v; } }
                    bool flat = Low(f) < Gap;
                    bool tiny = zone && sl < ShortFold;                  // ★[v101.0] 구역 모드: 0.25mm 안 짧은 변(ShortFold 설명)
                    if (!flat && !tiny) continue;
                    if (sl <= FoldLen)
                    {
                        bool o1 = EvalMove(su, sv, out double s1, out var n1, out var k1);
                        bool o2 = EvalMove(sv, su, out double s2, out var n2, out var k2);
                        double lim = flat ? 1 : ShortFoldScore;          // 짧은 변만인 면은 오차를 더 아껴 쓴다(ShortFoldScore)
                        bool t1 = o1 && s1 <= lim, t2 = o2 && s2 <= lim;
                        if (t1 || t2)
                        {
                            bool take1 = t1 && (!t2 || s1 <= s2);
                            DebugLog?.Invoke(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] 변 {2:F1}µm · 높이 {3:F1}µm · 없앰 {4} → 남김 {5} · 점수 {6:F3}",
                                flat ? "접기" : "짧은 변 접기", round, sl * 1e6, Low(f) * 1e6, Pt(take1 ? su : sv), Pt(take1 ? sv : su), take1 ? s1 : s2));
                            if (take1) ApplyMove(su, sv, n1, k1); else ApplyMove(sv, su, n2, k2);
                            if (flat) rep.Folds++; else rep.ShortFolds++;
                            changed = true; continue;
                        }
                    }
                    if (flat && TryOwnFlip(fi)) { rep.OwnFlips++; changed = true; DebugLog?.Invoke($"먼저 뒤집기[{round}] 면 {Pt(f[0])} {Pt(f[1])} {Pt(f[2])}"); }
                }
                if (!changed) break;
            }
        }

        // ───────── ★★[v102.0 · 계획 4판 §2] 마지막 수단 — 위험한 납작한 면만 ─────────

        /// <summary>★[계획 4판 §2 · 검토 r3 L4] 위험한 납작한 면 — 가장 낮은 높이 &lt; Gap이면서 ①Civil이 고칠 면(&lt; 1e-4 · 긴 변 안쪽 · 들로네 아님)
        /// 또는 ②안쪽 변 중 뒤집기 흉내 &gt; FlipTol1. <see cref="Measure"/>의 Civil이 고칠 면·뒤집기 흉내와 <b>같은 식</b>이다 —
        /// 그래서 판정 1인 판(두 셈 다 0)은 대상이 0이다.</summary>
        bool IsRisky(int fi, out bool civ, out double flipDz)
        {
            var f = F[fi]; civ = false; flipDz = 0;
            double h = Low(f);
            if (h >= Gap) return false;
            civ = h < CivilMerge && LongEdgeNonDelaunay(fi);
            for (int e = 0; e < 3; e++)
            {
                int u = f[e], v = f[(e + 1) % 3], w1 = f[(e + 2) % 3];
                int gi = vf[u].Where(g => g != fi && vf[v].Contains(g)).DefaultIfEmpty(-1).First();
                if (gi < 0) continue;
                int w2 = F[gi].First(x => x != u && x != v);
                if (DiagCross(u, v, w1, w2, out double dz)) flipDz = Math.Max(flipDz, dz);
            }
            return civ || flipDz > FlipTol1;
        }

        /// <summary>★[검토 r3 M1] 마지막 수단이 옮기거나 없애도 되는 점 — 고정 점은 안 되고, 구역 모드(합성)에서는 삼각망 <b>바깥 테두리</b> 점도 안 된다
        /// (옮기면 정지면 테두리 모양이 바뀌어 «합성 넓이 = 손댄 정지면 넓이»가 깨진다).</summary>
        bool Movable(int v) => !fixedV[v] && !(zone && OnMeshBorder(v));

        /// <summary>★[검토 r3 M1] 마지막 수단이 갈라도 되는 변 — 구역 모드에서는 면 하나만 쓰는 변(바깥 테두리)과 고정–고정 변(이음매)은 안 된다(T자 이음).</summary>
        bool CanSplit(int u, int v)
        {
            if (!zone) return true;
            if (fixedV[u] && fixedV[v]) return false;
            return vf[u].Count(g => vf[v].Contains(g)) == 2;
        }

        /// <summary>변 uv 위 매개 t 자리에 점을 넣고 그 변을 쓰는 면(1~2개)을 둘로 가른다 — 높이는 uv를 따라 1차라 <b>모양이 안 바뀐다</b>. 되돌리기 정보를 준다.
        /// <para>★[검토 r3 L8] 새 점의 «초록 선 위»는 실제 거리로 잰다(현 위면 선에서 떨어졌을 수 있다 — 그래야 EvalMove가 선 거리·높이를 잰다).</para></summary>
        (int P, List<int> Killed, List<int> Added) SplitEdge(int u, int v, double t)
        {
            double px = X[u] + t * (X[v] - X[u]), py = Y[u] + t * (Y[v] - Y[u]), pz = Z[u] + t * (Z[v] - Z[u]);
            var fs = vf[u].Where(g => vf[v].Contains(g)).ToList();
            X.Add(px); Y.Add(py); Z.Add(pz); WX.Add(px + ox); WY.Add(py + oy);
            int p = X.Count - 1;
            bool onRing = !zone && NearRing(px, py, OnRing, out _, out _, out _) <= OnRing;
            dead.Add(false); border.Add(onRing); ringV.Add(false); fixedV.Add(false); vf.Add(new HashSet<int>());
            var killed = new List<int>(); var added = new List<int>();
            foreach (int g in fs)
            {
                var f = F[g];
                int x = f.First(q => q != u && q != v);
                int ia = Array.IndexOf(f, u), ib = Array.IndexOf(f, v);
                int a = (ia + 1) % 3 == ib ? u : v, b = a == u ? v : u;     // 반시계로 a → b
                KillFace(g); killed.Add(g);
                AddFace(new[] { a, p, x }); added.Add(F.Count - 1);
                AddFace(new[] { p, b, x }); added.Add(F.Count - 1);
            }
            return (p, killed, added);
        }

        void UndoSplit((int P, List<int> Killed, List<int> Added) s)
        {
            foreach (int g in s.Added) if (fAlive[g]) KillFace(g);
            foreach (int g in s.Killed) { fAlive[g] = true; foreach (int v in F[g]) vf[v].Add(g); }
            dead[s.P] = true;
        }

        /// <summary>꼭짓점 옮기기 자리 — 납작한 면의 꼭짓점(가장 긴 변의 맞은편)이 가장 긴 변 위로 떨어지는 발(매개 t).</summary>
        bool ApexFoot(int fi, out int w, out int lu, out int lv, out double t)
        {
            var f = F[fi]; w = -1; lu = -1; lv = -1; t = double.NaN; double ll = -1;
            for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; w = f[(e + 2) % 3]; } }
            if (!Movable(w) || !CanSplit(lu, lv)) return false;
            double ex = X[lv] - X[lu], ey = Y[lv] - Y[lu], L2 = ex * ex + ey * ey;
            if (L2 < 1e-24) return false;
            t = ((X[w] - X[lu]) * ex + (Y[w] - Y[lu]) * ey) / L2;
            if (!(t > 1e-9 && t < 1 - 1e-9)) return false;
            // 발이 끝점과 네모 Gap 안이면 쌍 문제다(새 점이 또 쌍이 된다) — 이 방법은 안 쓴다
            double fx = X[lu] + t * ex, fy = Y[lu] + t * ey;
            if (Math.Max(Math.Abs(fx - X[lu]), Math.Abs(fy - Y[lu])) < Gap || Math.Max(Math.Abs(fx - X[lv]), Math.Abs(fy - Y[lv])) < Gap) return false;
            return true;
        }

        /// <summary>★★[v102.0 · 계획 4판 §2] 마지막 수단 — 종전 길이 다 못 없앤 <b>위험한</b> 납작한 면(<see cref="IsRisky"/>)을 오차가 가장 작은 방법으로 없앤다
        /// (남겨 두면 Civil이 뒤집거나 면·점을 빠뜨린다 — 17:32 45mm · 13:33 460mm).
        /// <para>후보: 아무 변 접기(양쪽) · 꼭짓점 옮기기(긴 변을 발에서 갈라 모양 그대로 둔 뒤 합침) · 긴 변 뒤집기. <b>점수 ≤ 2</b>(판정 2 한도 안)만 —
        /// 평가 중 겹침 예외가 난 후보는 버리고 그 예외는 셈에서 뺀다(검토 r3 L2·r4 낮음 4).</para>
        /// <para>②만인 면(Civil이 고칠 면은 아니고 뒤집기 흉내만 &gt; 0.2mm): 흉내 &gt; FlipTol2(판정 3)이면 한도 안 가장 싼 고침을,
        /// 아니면 고침의 높이 오차(mm)가 1mm 안이거나 흉내(mm)보다 작을 때만 한다(검토 r3 M2 — 높이 mm끼리 견줌).</para></summary>
        public int LastResort(Report rep)
        {
            int done = 0;
            for (int round = 0; round < 50; round++)
            {
                bool changed = false;
                int nF0 = F.Count;                       // ★[코드 검토 낮음 3] 한 바퀴는 시작할 때의 면까지만 — 새로 생긴 면은 다음 바퀴에
                for (int fi = 0; fi < nF0; fi++)
                {
                    if (!fAlive[fi]) continue;
                    if (!IsRisky(fi, out bool civ, out double flipDz)) continue;
                    var f = F[fi];
                    double h = Low(f);
                    double best = double.PositiveInfinity, bestErr = double.PositiveInfinity; int kind = -1;
                    (int Gone, int Keep, List<int[]> Nf, List<int> K) bm = default;
                    // 평가 한 번 — 겹침 예외가 나면 그 후보는 버리고 예외 수는 되돌린다
                    bool Eval(int gone, int keep, out double sc, out List<int[]> nf, out List<int> k, out double err)
                    {
                        int e0 = OverlayExc;
                        bool ok = EvalMove(gone, keep, out sc, out nf, out k);
                        err = Math.Max(lastWall, lastBz);
                        bool exc = OverlayExc != e0; OverlayExc = e0;
                        return ok && !exc && sc <= 2;
                    }
                    for (int e = 0; e < 3; e++)
                    {
                        int u = f[e], v = f[(e + 1) % 3];
                        if (Movable(u) && Eval(u, v, out double s1, out var n1, out var k1, out double r1) && s1 < best) { best = s1; bestErr = r1; kind = 0; bm = (u, v, n1, k1); }
                        if (Movable(v) && Eval(v, u, out double s2, out var n2, out var k2, out double r2) && s2 < best) { best = s2; bestErr = r2; kind = 0; bm = (v, u, n2, k2); }
                    }
                    if (ApexFoot(fi, out int w, out int lu, out int lv, out double t))
                    {
                        var sp = SplitEdge(lu, lv, t);
                        if (Eval(w, sp.P, out double s3, out _, out _, out double r3) && s3 < best) { best = s3; bestErr = r3; kind = 1; }
                        UndoSplit(sp);
                    }
                    {
                        int e0 = OverlayExc;
                        double s4 = EvalFlip(fi, out int[]? t1, out int[]? t2, out int gi, out double r4);
                        bool exc = OverlayExc != e0; OverlayExc = e0;
                        if (!exc && s4 <= 2 && s4 < best) { best = s4; bestErr = r4; kind = 2; }
                        if (kind == 2) { bm = default; flipT1 = t1; flipT2 = t2; flipG = gi; }
                    }
                    if (kind < 0) continue;
                    // ②만인 면 — 판정 3이 아니면 더 나쁘게 만들지 않는다(높이 mm끼리)
                    if (!civ && flipDz <= FlipTol2 && !(bestErr <= ZTol1 || bestErr < flipDz)) continue;
                    string before = DebugLog != null ? $"{Pt(f[0])} {Pt(f[1])} {Pt(f[2])}" : "";
                    double atX = (X[f[0]] + X[f[1]] + X[f[2]]) / 3 + ox, atY = (Y[f[0]] + Y[f[1]] + Y[f[2]]) / 3 + oy;
                    if (kind == 0) { rep.MoveMax = Math.Max(rep.MoveMax, Len(bm.Gone, bm.Keep)); ApplyMove(bm.Gone, bm.Keep, bm.Nf, bm.K); }
                    else if (kind == 1)
                    {
                        var sp = SplitEdge(lu, lv, t);
                        if (EvalMove(w, sp.P, out _, out var nf, out var kill)) { rep.MoveMax = Math.Max(rep.MoveMax, Len(w, sp.P)); ApplyMove(w, sp.P, nf, kill); }
                        else { UndoSplit(sp); continue; }
                    }
                    else { KillFace(fi); KillFace(flipG); AddFace(flipT1!); AddFace(flipT2!); }
                    rep.LastResort++; if (kind == 1) rep.Snaps++; rep.LastResortScore = Math.Max(rep.LastResortScore, best);
                    if (best > 1) rep.BigOps.Add((atX, atY, best));
                    changed = true; done++;
                    DebugLog?.Invoke(string.Format(CultureInfo.InvariantCulture, "마지막 수단[{0}] {1} · 높이 {2:F1}µm · 점수 {3:F3} · 오차 {4:F3}mm · {5} · 면 {6}",
                        round, kind == 0 ? "변 접기" : kind == 1 ? "꼭짓점 옮기기" : "뒤집기", h * 1e6, best, bestErr * 1e3, civ ? "Civil이 고칠 면" : $"뒤집기 흉내 {flipDz * 1e3:F3}mm", before));
                }
                if (!changed) break;
            }
            return done;
        }
        int[]? flipT1, flipT2; int flipG = -1;

        /// <summary>가장 긴 변 뒤집기의 점수(옹벽 높이 — 겹침 꼭짓점에서 정확히) · 안 되면 +∞. 가장 낮은 높이가 좋아질 때만.</summary>
        double EvalFlip(int fi, out int[]? t1, out int[]? t2, out int gi, out double wall)
        {
            t1 = null; t2 = null; gi = -1; wall = double.PositiveInfinity;
            var f = F[fi];
            int lu = -1, lv = -1, w1 = -1; double ll = -1;
            for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; w1 = f[(e + 2) % 3]; } }
            gi = vf[lu].Where(g => g != fi && vf[lv].Contains(g)).DefaultIfEmpty(-1).First();
            if (gi < 0) return double.PositiveInfinity;
            int w2 = F[gi].First(v => v != lu && v != lv);
            if (!DiagCross(lu, lv, w1, w2, out _)) return double.PositiveInfinity;
            var a = new[] { w1, w2, lv }; if (Sa(a[0], a[1], a[2]) < 0) (a[1], a[2]) = (a[2], a[1]);
            var b = new[] { w2, w1, lu }; if (Sa(b[0], b[1], b[2]) < 0) (b[1], b[2]) = (b[2], b[1]);
            if (!(Sa(a[0], a[1], a[2]) > 1e-15 && Sa(b[0], b[1], b[2]) > 1e-15)) return double.PositiveInfinity;
            if (Math.Min(Low(a), Low(b)) <= Math.Min(Low(f), Low(F[gi]))) return double.PositiveInfinity;
            wall = Math.Max(FaceWallErr(a[0], a[1], a[2], out _, out _), FaceWallErr(b[0], b[1], b[2], out _, out _));
            t1 = a; t2 = b;
            return wall <= ZTol1 ? wall / ZTol1 : 1 + wall / ZTol2;
        }

        /// <summary>납작한 면의 가장 긴 변을 뒤집는다 — 옆 면이 있고 · 볼록 · 두 대각선이 만나는 점에서 높이 차 ≤ OwnFlipDz · 가장 낮은 높이가 좋아질 때만.</summary>
        bool TryOwnFlip(int fi)
        {
            var f = F[fi];
            int lu = -1, lv = -1, w1 = -1; double ll = -1;
            for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; w1 = f[(e + 2) % 3]; } }
            int gi = vf[lu].Where(g => g != fi && vf[lv].Contains(g)).DefaultIfEmpty(-1).First();
            if (gi < 0) return false;                                    // 테두리 변 — Civil도 못 뒤집는다
            int w2 = F[gi].First(v => v != lu && v != lv);
            if (!DiagCross(lu, lv, w1, w2, out double dz)) return false;
            if (dz > OwnFlipDz) return false;
            var t1 = new[] { w1, w2, lv }; if (Sa(t1[0], t1[1], t1[2]) < 0) (t1[1], t1[2]) = (t1[2], t1[1]);
            var t2 = new[] { w2, w1, lu }; if (Sa(t2[0], t2[1], t2[2]) < 0) (t2[1], t2[2]) = (t2[2], t2[1]);
            if (!(Sa(t1[0], t1[1], t1[2]) > 1e-15 && Sa(t2[0], t2[1], t2[2]) > 1e-15)) return false;
            if (Math.Min(Low(t1), Low(t2)) <= Math.Min(Low(f), Low(F[gi]))) return false;
            KillFace(fi); KillFace(gi); AddFace(t1); AddFace(t2);
            return true;
        }

        /// <summary>사각형 u·w1·v·w2가 볼록인가(두 대각선이 서로 속을 지른다) · 뒤집으면 두 대각선이 만나는 점의 높이가 얼마 바뀌나.</summary>
        bool DiagCross(int u, int v, int w1, int w2, out double dz)
        {
            dz = double.PositiveInfinity;
            double d1 = Sa(w1, w2, u), d2 = Sa(w1, w2, v), d3 = Sa(u, v, w1), d4 = Sa(u, v, w2);
            if (!(d1 * d2 < 0 && d3 * d4 < 0)) return false;
            double t = d3 / (d3 - d4), s = d1 / (d1 - d2);
            double zNew = Z[w1] + t * (Z[w2] - Z[w1]), zOld = Z[u] + s * (Z[v] - Z[u]);
            dz = Math.Abs(zNew - zOld);
            return true;
        }

        /// <summary>면의 가장 긴 변이 안쪽 변이고, 옆 면의 맞은편 점이 이 면의 외접원 안에 드는가(들로네가 아님).</summary>
        bool LongEdgeNonDelaunay(int fi)
        {
            var f = F[fi];
            int lu = -1, lv = -1; double ll = -1;
            for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; } }
            int gi = vf[lu].Where(g => g != fi && vf[lv].Contains(g)).DefaultIfEmpty(-1).First();
            if (gi < 0) return false;
            int w2 = F[gi].First(v => v != lu && v != lv);
            // 반시계 면 (a,b,c)에 대해 점 d가 외접원 안이면 행렬식 > 0 — 국소 좌표를 w2 기준으로 옮겨 크기를 줄인다
            int a = f[0], b = f[1], c = f[2];
            double ax = X[a] - X[w2], ay = Y[a] - Y[w2], bx = X[b] - X[w2], by = Y[b] - Y[w2], cx = X[c] - X[w2], cy = Y[c] - Y[w2];
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return det > 0;
        }

        /// <summary>③ 결과를 잰다 — 뒤집기 흉내 · 남은 점 네모 최소 · 겹침 · 초록 선과 평면·테두리 높이 · 옹벽 높이(정확).</summary>
        public void Measure(Report rep)
        {
            // ★[검토 r4 중간 1] 잰 값은 매번 처음부터 — 마지막 수단 앞뒤로 두 번 잰다
            rep.FlatLeft = 0; rep.FlatMinH = double.PositiveInfinity; rep.CivilRisk = 0; rep.CivilRiskAt = "";
            rep.FlipRiskN = 0; rep.FlipRiskDz = 0; rep.FlipRiskAt = "";
            rep.ShortLeft = 0; rep.ShortMin = double.PositiveInfinity; rep.ShortAt = ""; rep.ThinLeft = 0; rep.ThinAt = "";
            rep.MinBox = double.PositiveInfinity; rep.AreaSum = 0; rep.AreaUnion = 0;
            rep.PlanMax = 0; rep.PlanAt = ""; rep.BorderDz = 0; rep.BorderAt = ""; rep.WallDz = 0; rep.WallAt = "";
            if (rep.Fail.StartsWith("합치기 예외")) rep.Fail = "";
            rep.PtsOut = PtsAlive(); rep.FacesOut = FacesAlive();
            // 뒤집기 흉내 — 납작한 면이 낀 볼록 사각형의 대각선을 Civil이 뒤집는다고 치면
            var seenE = new HashSet<(int, int)>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue;
                var f = F[fi]; double h = Low(f);
                if (h >= Gap) continue;
                rep.FlatLeft++; rep.FlatMinH = Math.Min(rep.FlatMinH, h);
                IsRisky(fi, out bool civ, out _);          // ★[검토 r3 L4] 마지막 수단의 대상과 같은 식
                DebugLog?.Invoke(string.Format(CultureInfo.InvariantCulture, "남은 납작한 면 높이 {0:F1}µm · 변 {1:F1}/{2:F1}/{3:F1}mm · {4} {5} {6}{7}",
                    h * 1e6, Len(f[0], f[1]) * 1e3, Len(f[1], f[2]) * 1e3, Len(f[2], f[0]) * 1e3, Pt(f[0]), Pt(f[1]), Pt(f[2]),
                    civ ? " · ⚠Civil이 고칠 면" : ""));
                if (civ)
                {
                    rep.CivilRisk++;
                    if (rep.CivilRiskAt.Length < 160) rep.CivilRiskAt += (rep.CivilRiskAt.Length > 0 ? " " : "") + At((X[f[0]] + X[f[1]] + X[f[2]]) / 3, (Y[f[0]] + Y[f[1]] + Y[f[2]]) / 3);
                }
                for (int e = 0; e < 3; e++)
                {
                    int u = f[e], v = f[(e + 1) % 3], w1 = f[(e + 2) % 3];
                    if (!seenE.Add(u < v ? (u, v) : (v, u))) continue;
                    int gi = vf[u].Where(g => g != fi && vf[v].Contains(g)).DefaultIfEmpty(-1).First();
                    if (gi < 0) continue;
                    int w2 = F[gi].First(x => x != u && x != v);
                    if (!DiagCross(u, v, w1, w2, out double dz)) continue;
                    if (dz > FlipTol1) DebugLog?.Invoke(string.Format(CultureInfo.InvariantCulture, "뒤집기 흉내 {0:F3}mm · 변 {1} {2} · 맞은편 {3} {4}", dz * 1e3, Pt(u), Pt(v), Pt(w1), Pt(w2)));
                    rep.FlipRiskN++;
                    if (dz > rep.FlipRiskDz) { rep.FlipRiskDz = dz; rep.FlipRiskAt = At((X[u] + X[v]) / 2, (Y[u] + Y[v]) / 2); }
                }
            }
            // 못 접고 남은 짧은 변(< 0.25mm) · 가는 바늘(높이 < 125µm) — 판정엔 안 넣고 적는다(0929 Civil이 구멍을 낸 모양 · Civil 되읽기 관문이 지킨다).
            //   ★[v102.0 · 검토 r4 중간 3] 순수옹벽에서도 센다 — 여유 띠 쌍을 두면 그 길이(101~148µm)의 짧은 변이 생긴다
            {
                var seenS = new HashSet<(int, int)>();
                for (int fi = 0; fi < F.Count; fi++)
                {
                    if (!fAlive[fi]) continue; var f = F[fi];
                    for (int e = 0; e < 3; e++)
                    {
                        int u = f[e], v = f[(e + 1) % 3];
                        if (!seenS.Add(u < v ? (u, v) : (v, u))) continue;
                        double l = Len(u, v);
                        if (l >= ShortFold || (fixedV[u] && fixedV[v])) continue;
                        rep.ShortLeft++;
                        if (l < rep.ShortMin) { rep.ShortMin = l; rep.ShortAt = At((X[u] + X[v]) / 2, (Y[u] + Y[v]) / 2); }
                    }
                }
                for (int fi = 0; fi < F.Count; fi++)
                {
                    if (!fAlive[fi]) continue; var f = F[fi];
                    double h = Low(f); if (h >= 1.25e-4) continue;
                    int lu = -1, lv = -1; double ll = -1;
                    for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; double l = Len(u, v); if (l > ll) { ll = l; lu = u; lv = v; } }
                    if (!vf[lu].Any(g => g != fi && vf[lv].Contains(g))) continue;          // 긴 변이 테두리면 Civil도 못 뒤집는다
                    rep.ThinLeft++;
                    if (rep.ThinAt.Length < 60) rep.ThinAt += (rep.ThinAt.Length > 0 ? " " : "") + At((X[f[0]] + X[f[1]] + X[f[2]]) / 3, (Y[f[0]] + Y[f[1]] + Y[f[2]]) / 3);
                }
            }
            // 남은 점 네모 최소
            var alive = Enumerable.Range(0, X.Count).Where(i => !dead[i] && vf[i].Count > 0).ToList();
            var cell = new Dictionary<(long, long), List<int>>();
            foreach (int i in alive) { var k = ((long)Math.Floor(X[i] / Gap), (long)Math.Floor(Y[i] / Gap)); if (!cell.TryGetValue(k, out var l)) cell[k] = l = new(); l.Add(i); }
            foreach (var kv in cell)
                foreach (int i in kv.Value)
                    for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++)
                        if (cell.TryGetValue((kv.Key.Item1 + dx, kv.Key.Item2 + dy), out var l))
                            foreach (int j in l) if (j != i && !(fixedV[i] && fixedV[j])) rep.MinBox = Math.Min(rep.MinBox, Box(i, j));
            // 겹침 — 넓이 합 = 합집합 넓이
            var polys = new List<Geometry>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                rep.AreaSum += Sa(f[0], f[1], f[2]);
                polys.Add(gf.CreatePolygon(new[] { new Coordinate(X[f[0]], Y[f[0]]), new Coordinate(X[f[1]], Y[f[1]]), new Coordinate(X[f[2]], Y[f[2]]), new Coordinate(X[f[0]], Y[f[0]]) }));
            }
            Geometry U;
            try { U = OverlayNGRobust.Union(polys); rep.AreaUnion = U.Area; }
            catch (Exception ex) { rep.Fail = $"합치기 예외 {ex.GetType().Name}"; rep.AreaUnion = -1; return; }
            // 테두리 — 면 하나만 쓰는 변
            var use = new Dictionary<(int, int), int>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                for (int e = 0; e < 3; e++) { int u = f[e], v = f[(e + 1) % 3]; var k = u < v ? (u, v) : (v, u); use[k] = use.TryGetValue(k, out int n) ? n + 1 : 1; }
            }
            var be = use.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            var beTree = new STRtree<int>();
            for (int i = 0; i < be.Count; i++)
            {
                var (u, v) = be[i];
                beTree.Insert(new Envelope(X[u], X[v], Y[u], Y[v]), i);
            }
            if (be.Count > 0) beTree.Build();
            if (zone) be.Clear();                // 구역 모드: 테두리 = 고정 점뿐 — 초록 선 대조가 없다
            void Worse(double d, double dz, double x, double y)
            {
                if (d > rep.PlanMax) { rep.PlanMax = d; rep.PlanAt = At(x, y); }
                if (dz > rep.BorderDz) { rep.BorderDz = dz; rep.BorderAt = At(x, y); }
            }
            // 두 선 사이 <b>평면 거리</b>는 «한쪽 꼭짓점을 다른 쪽에 비춘 자리»에서 최대가 나온다 — 양쪽 꼭짓점을 서로에게 비춰 잰다.
            //   ★[검토 0928 v100.4 · 낮음 3] 높이 차는 그렇지 않다: 모서리 이등분선에서 가장 가까운 초록 선 자리가 다른 변으로 건너뛰어
            //   그 자리는 비춘 자리가 아니다 — 조금 적게 나올 수 있다(3D로는 µm 거리라 잣대가 꺾이는 것). 5cm 간격은 덧댐
            const double Step = 0.05;
            // (가) 테두리 → 초록 선: 꼭짓점과 5cm 간격
            foreach (var (u, v) in be)
            {
                double L = Len(u, v); int n = Math.Max(1, (int)Math.Ceiling(L / Step));
                for (int s = 0; s <= n; s++)
                {
                    double t = (double)s / n, x = X[u] + t * (X[v] - X[u]), y = Y[u] + t * (Y[v] - Y[u]), z = Z[u] + t * (Z[v] - Z[u]);
                    double d = NearRing(x, y, 1e-4, out double zr, out _, out _);
                    Worse(d, double.IsNaN(zr) ? double.PositiveInfinity : Math.Abs(z - zr), x, y);
                }
            }
            // (나) 초록 선 → 테두리: 꼭짓점과 5cm 간격
            double NearBorder(double x, double y, out double zb)
            {
                zb = double.NaN; double best = double.PositiveInfinity;
                for (double wdw = 1e-4; ; wdw *= 10)
                {
                    foreach (int i in beTree.Query(new Envelope(x - wdw, x + wdw, y - wdw, y + wdw)))
                    {
                        var (u, v) = be[i];
                        double ex = X[v] - X[u], ey = Y[v] - Y[u], L2 = ex * ex + ey * ey;
                        double q = L2 < 1e-24 ? 0 : Math.Clamp(((x - X[u]) * ex + (y - Y[u]) * ey) / L2, 0, 1);
                        double qx = X[u] + ex * q, qy = Y[u] + ey * q, d = Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy));
                        if (d < best) { best = d; zb = Z[u] + (Z[v] - Z[u]) * q; }
                    }
                    if (best <= wdw || wdw > 1e3) return best;
                }
            }
            if (be.Count > 0 && !zone)
                foreach (var s0 in rs)
                {
                    double L = Math.Sqrt((s0.X2 - s0.X1) * (s0.X2 - s0.X1) + (s0.Y2 - s0.Y1) * (s0.Y2 - s0.Y1));
                    int n = Math.Max(1, (int)Math.Ceiling(L / Step));
                    for (int s = 0; s <= n; s++)
                    {
                        double t = (double)s / n, x = s0.X1 + t * (s0.X2 - s0.X1), y = s0.Y1 + t * (s0.Y2 - s0.Y1), z = s0.Z1 + t * (s0.Z2 - s0.Z1);
                        double d = NearBorder(x, y, out double zb);
                        Worse(d, double.IsNaN(zb) ? double.PositiveInfinity : Math.Abs(zb - z), x, y);
                    }
                }
            // 옹벽 높이 — 모든 면을 정확히(구역 모드는 참값 = 받은 삼각형이라 <b>바뀐 면만</b> — 삼각형 수만 개에서 시간이 여기서 갔다)
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                if (zone && !modified.Contains(fi)) continue;
                double e = FaceWallErr(f[0], f[1], f[2], out double ax, out double ay);
                if (e > rep.WallDz) { rep.WallDz = e; rep.WallAt = At(ax, ay); }
            }
        }

        public List<WallDaylight.Tri> Output()
        {
            var res = new List<WallDaylight.Tri>();
            for (int fi = 0; fi < F.Count; fi++)
            {
                if (!fAlive[fi]) continue; var f = F[fi];
                Point3 P(int i) => new(WX[i], WY[i], Z[i]);
                res.Add(new WallDaylight.Tri(P(f[0]), P(f[1]), P(f[2])));
            }
            return res;
        }
    }

    // ══════════════════════════════ Civil 되읽기 대조 ══════════════════════════════

    /// <summary>★[검토 0928 · 중간 4·5] Civil이 돌려준 보이는 삼각형(<c>theirs</c>)이 우리가 넘긴 면(<c>ours</c>)과 같은가.
    /// <para>«삼각형 수 = 면 수»로는 다시 이은 것을 못 가린다(뒤집기는 수를 안 바꾼다). 25점 높이 대조는 꺾임을 가르는 뒤집기를 0.3%만 맞힌다.
    /// → 꼭짓점 셋으로 같은 면을 세고, <b>다른 삼각형만</b> 골라 겹침 다각형 꼭짓점에서 높이를 정확히 견준다. 모양은 합집합 대칭차·최대 거리.</para></summary>
    public sealed class Diff
    {
        public int Ours, Theirs, Same, Exceptions;
        public double SymArea, Hausdorff, MaxDz;
        public string DzAt = "", HdAt = "";
        /// <summary>모양 계산을 건너뛰었나(받은 면이 넘긴 면과 전부 같고 높이도 같을 때).</summary>
        public bool ShapeSkipped;
        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "넘긴 면과 같은 것 {0}/{1}(Civil 삼각형 {2}) · 다른 삼각형·같은 점의 높이 차 최대 {3:F3}mm{4} · 모양 {5}{6}",
            Same, Ours, Theirs, MaxDz * 1000, MaxDz > 0 ? " @" + DzAt : "",
            ShapeSkipped ? "전부 같음(계산 건너뜀)" : string.Format(CultureInfo.InvariantCulture, "대칭차 {0:E2}㎡ · 최대 거리 {1:F2}µm{2}", SymArea, Hausdorff * 1e6, Hausdorff > 1e-6 ? " @" + HdAt : ""),
            Exceptions > 0 ? $" · ⚠겹침 계산 예외 {Exceptions}" : "");
    }

    /// <summary>★[v100.4 · 검토 0928 v100.5 중간 1 · 낮음 6] Civil 되읽기 대조.
    /// <para>모양 계산(DiscreteHausdorff DensifyFraction 0.001)이 테두리 점 수의 제곱이라 17:32 한 판 3~4초, 테두리 점 4002개면 149초였다 —
    /// 합성지표면은 삼각형이 수만 개다. → 받은 면이 넘긴 면과 <b>전부 같고 높이도 같으면</b> 모양 계산을 건너뛰고(모양이 같을 수밖에 없다),
    /// 아니면 두 테두리의 꼭짓점·변 가운데만 서로에게 색인 거리로 잰다(n log n).</para>
    /// <para>같은 면은 XY로만 짝지으면 같은 자리에서 높이만 바뀐 것을 못 잡는다 — 짝이 맞은 면도 꼭짓점 높이를 견준다.</para></summary>
    public static Diff Compare(IReadOnlyList<WallDaylight.Tri> ours, IReadOnlyList<WallDaylight.Tri> theirs)
    {
        var d = new Diff { Ours = ours.Count, Theirs = theirs.Count };
        if (ours.Count == 0 || theirs.Count == 0) { d.SymArea = double.PositiveInfinity; d.Hausdorff = double.PositiveInfinity; return d; }
        double ox = ours[0].A.X, oy = ours[0].A.Y;
        var gf = new GeometryFactory();
        static (long, long) K(Point3 q) => ((long)Math.Round(q.X / 1e-7), (long)Math.Round(q.Y / 1e-7));
        ((long, long), (long, long), (long, long)) Key(WallDaylight.Tri t)
        {
            var a = new[] { K(t.A), K(t.B), K(t.C) }; Array.Sort(a); return (a[0], a[1], a[2]);
        }
        string At(double x, double y) => string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4})", x, y);
        // 넘긴 점의 높이(같은 XY 짝) — 짝이 맞은 면도 높이를 견준다
        var oz = new Dictionary<(long, long), double>();
        foreach (var t in ours) { oz[K(t.A)] = t.A.Z; oz[K(t.B)] = t.B.Z; oz[K(t.C)] = t.C.Z; }
        var oKeys = new HashSet<((long, long), (long, long), (long, long))>(ours.Select(Key));
        var tKeys = new HashSet<((long, long), (long, long), (long, long))>(theirs.Select(Key));
        foreach (var t in theirs)
        {
            if (!oKeys.Contains(Key(t))) continue;
            d.Same++;
            foreach (var q in new[] { t.A, t.B, t.C })
            {
                double dz = oz.TryGetValue(K(q), out double z0) ? Math.Abs(q.Z - z0) : double.NaN;
                if (double.IsNaN(dz)) { d.Exceptions++; continue; }
                if (dz > d.MaxDz) { d.MaxDz = dz; d.DzAt = At(q.X, q.Y); }
            }
        }
        Polygon Pg(WallDaylight.Tri t) => gf.CreatePolygon(new[] { new Coordinate(t.A.X - ox, t.A.Y - oy), new Coordinate(t.B.X - ox, t.B.Y - oy), new Coordinate(t.C.X - ox, t.C.Y - oy), new Coordinate(t.A.X - ox, t.A.Y - oy) });
        static double Pz(WallDaylight.Tri t, double ox, double oy, double x, double y)
        {
            double x1 = t.A.X - ox, y1 = t.A.Y - oy, x2 = t.B.X - ox, y2 = t.B.Y - oy, x3 = t.C.X - ox, y3 = t.C.Y - oy;
            double dd = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3);
            double l1 = ((y2 - y3) * (x - x3) + (x3 - x2) * (y - y3)) / dd, l2 = ((y3 - y1) * (x - x3) + (x1 - x3) * (y - y3)) / dd;
            return l1 * t.A.Z + l2 * t.B.Z + (1 - l1 - l2) * t.C.Z;
        }
        // 다른 삼각형만 골라 상대 쪽 면과 겹침 꼭짓점에서
        void Dz(IReadOnlyList<WallDaylight.Tri> src, HashSet<((long, long), (long, long), (long, long))> otherKeys, IReadOnlyList<WallDaylight.Tri> other)
        {
            List<WallDaylight.Tri>? diff = null;
            foreach (var t in src) if (!otherKeys.Contains(Key(t))) (diff ??= new()).Add(t);
            if (diff == null) return;
            var tree = new STRtree<int>();
            for (int i = 0; i < other.Count; i++) tree.Insert(Pg(other[i]).EnvelopeInternal, i);
            tree.Build();
            foreach (var t in diff)
            {
                var p = Pg(t);
                foreach (int i in tree.Query(p.EnvelopeInternal))
                {
                    Geometry inter;
                    try { inter = OverlayNGRobust.Overlay(p, Pg(other[i]), NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
                    catch { d.Exceptions++; continue; }
                    if (inter.IsEmpty || inter.Area <= 0) continue;
                    foreach (var q in inter.Coordinates)
                    {
                        double dz = Math.Abs(Pz(t, ox, oy, q.X, q.Y) - Pz(other[i], ox, oy, q.X, q.Y));
                        if (double.IsNaN(dz)) { d.Exceptions++; continue; }
                        if (dz > d.MaxDz) { d.MaxDz = dz; d.DzAt = At(q.X + ox, q.Y + oy); }
                    }
                }
            }
        }
        Dz(theirs, oKeys, ours);
        Dz(ours, tKeys, theirs);
        // 전부 같고 높이도 같으면 모양은 같을 수밖에 없다 — 건너뛴다(삼각형 수만 개에서 수십 초 걸리던 자리)
        if (d.Same == ours.Count && ours.Count == theirs.Count && oKeys.Count == ours.Count && tKeys.Count == theirs.Count && d.MaxDz == 0 && d.Exceptions == 0)
        {
            d.ShapeSkipped = true; d.SymArea = 0; d.Hausdorff = 0;
            return d;
        }
        try
        {
            var uo = OverlayNGRobust.Union(ours.Select(t => (Geometry)Pg(t)).ToList());
            var ut = OverlayNGRobust.Union(theirs.Select(t => (Geometry)Pg(t)).ToList());
            d.SymArea = OverlayNGRobust.Overlay(uo, ut, NetTopologySuite.Operation.Overlay.SpatialFunction.SymDifference).Area;
            // 두 테두리의 꼭짓점·변 가운데를 서로에게 — 색인 거리(n log n)
            (double M, double X, double Y) Far(Geometry from, Geometry to)
            {
                var idx = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(to);
                double m = 0, mx = 0, my = 0;
                foreach (var ls in NetTopologySuite.Geometries.Utilities.LinearComponentExtracter.GetLines(from))
                {
                    var cs = ls.Coordinates;
                    for (int q = 0; q < cs.Length; q++)
                        foreach (var c in q + 1 < cs.Length ? new[] { cs[q], new Coordinate((cs[q].X + cs[q + 1].X) / 2, (cs[q].Y + cs[q + 1].Y) / 2) } : new[] { cs[q] })
                        {
                            double dist = idx.Distance(gf.CreatePoint(c));
                            if (dist > m) { m = dist; mx = c.X; my = c.Y; }
                        }
                }
                return (m, mx, my);
            }
            var f1 = Far(uo.Boundary, ut.Boundary); var f2 = Far(ut.Boundary, uo.Boundary);
            var f = f1.M >= f2.M ? f1 : f2;
            d.Hausdorff = f.M; d.HdAt = At(f.X + ox, f.Y + oy);
        }
        catch { d.Exceptions++; d.SymArea = double.PositiveInfinity; d.Hausdorff = double.PositiveInfinity; }
        return d;
    }
}
