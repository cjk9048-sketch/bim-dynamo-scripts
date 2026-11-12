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
public static partial class WallByLine
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
        /// <summary>★[v114.3 · JACK 1105 소단 높이 · 계획 2판 2절 «방식 칸»] 높이 방식 — 기본 <see cref="LevelKind.Line"/>(선기준 · 옛 규칙 · 도로 대기 · 옛 하네스).
        /// 부지 흐름은 <see cref="LevelKind.OldBench"/>를 넘기고 그 쪽 단 표(<see cref="CutBench"/> · <see cref="FillBench"/>)가 없으면 거절한다.</summary>
        public LevelKind LevelMode = LevelKind.Line;
        /// <summary>★[v114.3] 옛 소단 방식의 단 표(절토 · 성토 — 부지 = GradingGeometry 링 · ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 도로 = 그 시도 면의 광선 묶음).</summary>
        public BenchLevels? CutBench, FillBench;
        /// <summary>★[v115 · 설치 전 검토 S1] 단 표를 못 만든 까닭(절토 · 성토 — 부지 링 <see cref="OldBench.Of"/>의 why) — 단 표가 없어 거절할 때 화면 까닭에 넣는다.</summary>
        public string CutBenchWhy = "", FillBenchWhy = "";
        /// <summary>★[v114.3 · 재검토 M2] 옛 소단 방식에서 줄 · 끝 선에 높이를 주기 전에 채우는 점 간격(사면 코드와 같은 clamp(VertexSpacing, 0.3, 1.0)).</summary>
        public double RowDens = 1.0;
        /// <summary>★[v116.3 · JACK 1109 옹벽 천단 선택] 계단 — 단 높이(절토 = 벽 윗선 · 뒤 소단 / 성토 = 벽 아랫선)를 블록(<see cref="BlockLen"/>)마다 수평(절토 꼴 블록 안 최대 —
        /// 절토 = 가장 높은 곳 · 성토 = 가장 낮은 곳) · 블록 경계에 가로 마구리. 끔(기본) = 옛 경사(비트 같음). 옛 소단 방식에서만 뜻이 있다(선기준 높이는 이미 수평).</summary>
        public bool TopStep;
        /// <summary>계단 블록 길이(m · 선 위 호 길이) — 0 이하면 9.</summary>
        public double BlockLen = 9;
        /// <summary>★[v117.0 1단계 · 계획 `2026-11-10_옹벽추가_안쪽소단.md` · 검토 H2] 모양 — 기본 <see cref="WallShape.Out"/>(옛 바깥 계단 · 비트 같음).
        /// <see cref="WallShape.In"/>(선 안쪽 계단)은 부르는 쪽이 명시해야 <see cref="BuildIn"/>로 간다.</summary>
        public WallShape Shape = WallShape.Out;
        /// <summary>★[v117.0 1단계] 새 모양의 첫 소단 · 단 수 규칙 — 기본 <see cref="InwardRule.Strict"/>(JACK 1110 18시 결정).</summary>
        public InwardRule InRule = InwardRule.Strict;
        /// <summary>★[v117.0 1단계] <see cref="InwardRule.Ledge"/> 규칙의 턱 한도(m · 첫 소단이 선 위 원지반보다 높은(절토) · 낮은(성토) 양) — 계획 «1단계 결과» 제안값 1.0 m(JACK이 정함).</summary>
        public double LedgeMax = 1.0;
        /// <summary>★[v117.3 · 계획 `2026-11-11_부지_다단옹벽.md` 2절] 다단(<see cref="WallShape.Tiered"/>) — 단높이 H · 소단 폭 b(NaN = 방향의 Hc/Hf · Tc/Tf) · 첫 소단 Z1(실제 z · NaN = 자동).</summary>
        public double TierH = double.NaN, TierB = double.NaN, TierZ1 = double.NaN;
        /// <summary>★[v117.5 · 키트 wtfill] 다단 2차 수직만 — 세계 좌표 (x, y)에서 P0′ = 원지반이 짓기상 정확한가(소단이 땅 높이에서 잘린 자리 · 바깥 벽 높이 0).
        /// 그 자리 P0 읽기 = 원지반 그대로(합성 삼각망의 근사값 대신) · 두 L′ 끝이 다 그 자리인 칸 = 높이 0 칸(부호 대신 |W − G| ≤ 1 mm). null = v117 그대로(비트 같음).</summary>
        public Func<double, double, bool>? ZeroAt;
        /// <summary>★[v117.x A · 로드맵 D2 ③] 다단 구간(선 위 호 길이 S0~S1 · 첫 소단 Z1 실제 z · NaN = 그 구간 자동 · NoTier = 그 구간 단 없음).
        /// null 또는 1개 이하 = 선 전체 한 구간(옛 길 비트 같음).</summary>
        public List<TierSec>? TierSecs;
        /// <summary>★[v117.x B · 로드맵 D3 ②] 끝 비탈 — 선 양 끝(벽 끝 꼭대기)에서 원뿔 비탈(1:<see cref="EndRatioC"/> · 성토 <see cref="EndRatioF"/>)로 옛 면까지 · 검토 높이를 넘으면 단 높이마다 소단. 거짓 = 단별 자연 종료(옛 길 비트 같음).</summary>
        public bool TierEndSlope;
        /// <summary>★[v117.9 · 설치 전 검토 MED] 참 = 다시 짓기 — 소단 자리 ↔ 막을 다각형 겹침을 거절 대신 ⚠만(이미 지은 옹벽이 새 규칙으로 최종면에서 빠지지 않게). 새로 그을 때 · 옮기기는 거짓(거절).</summary>
        public bool BlockersWarnOnly;
        public double EndRatioC = 1.0, EndRatioF = 1.5;
        /// <summary>★[로드맵 2-2 · D4 ①] 검토 높이(m · 절토 · 성토) — 도면의 소단 생성 기준 높이(Civil BuildGeom이 구역 제원 <c>BenchHeightOf</c>로 채움 · 기록의 Hc/Hf 아님 — 수직 기록은 0).
        /// 벽이 넘으면 ⚠만(거절 아님 · 구조 안전 판정 아님). NaN = 안 잼.</summary>
        public double ReviewHc = double.NaN, ReviewHf = double.NaN;
        /// <summary>★[로드맵 2-3 · 재기만] 기초 재기에 쓸 형식 단면 표(절토 · 성토)와 공통 칸 — null이면 안 잼.</summary>
        public WallSection.Table? FootCut, FootFill;
        public WallSection.Common? FootCommon;
        /// <summary>★[v117.3] 얕은 복사(다단 2차 수직 짓기가 P0만 바꿔 부른다).</summary>
        internal Input Copy() => (Input)MemberwiseClone();
    }

    /// <summary>★[v117.0 1단계] 선 옹벽 모양 — 0 = 옛 바깥 계단(v114~v116) · 1 = 선 안쪽 계단(새).</summary>
    /// <summary>★[v117.0 · JACK 1111 · 계획 `2026-11-11_옹벽추가_수직옹벽.md`] 2 = 수직 옹벽(그은 선에서 옛 사면을 1:s로 두부 자르듯 · <see cref="BuildVertical"/>).</summary>
    /// <summary>★[v117.3 · JACK 1111 «경계 중심 방향으로 소단»] 3 = 부지 다단(수직 옹벽 + 선 안쪽 소단 · <see cref="BuildTiered"/>).</summary>
    public enum WallShape { Out = 0, In = 1, Vertical = 2, Tiered = 3 }

    /// <summary>★[v117.x A] 다단 구간 하나 — 선 위 호 길이 [S0, S1] · 첫 소단 Z1(실제 z · NaN = 자동) · 단 없음(수직만) · 단 수(지은 값 · 제안은 어림).</summary>
    public sealed class TierSec
    {
        public double S0, S1, Z1 = double.NaN; public bool NoTier; public int N;
        /// <summary>★[조정자 결정 1111] 손으로 친 Z1인가 — 아니면(자동) 다시 짓기마다 지금 면으로 다시 셈(기록엔 쓴 값을 남기되 다시 지을 때 NaN으로 넘김).</summary>
        public bool Hand;
        public TierSec Copy() => (TierSec)MemberwiseClone();
    }

    /// <summary>★[v117.0 1단계 · 0단계 M1 뒤] 새 모양의 단 수 n 규칙.
    /// <para><see cref="Strict"/> — 선 어디서도 첫 소단이 원지반 위로 뜨지 않는 가장 높은 기준(절토 · 성토 거울) · 바깥 벽이 어디서든 단높이를 넘으면 거절(JACK 1110 18시).</para>
    /// <para><see cref="Ledge"/> — 바깥 벽이 어디서도 단높이를 넘지 않는 가장 낮은 n(엄격 n부터 올림) · 땅이 낮은 자리는 첫 소단이 원지반보다 높게 선다(선 위 작은 턱 = Z_n − G) · 턱이 <see cref="Input.LedgeMax"/>를 넘으면 거절.</para></summary>
    public enum InwardRule { Strict = 0, Ledge = 1 }

    /// <summary>★[v114.3 · JACK 1105 소단 높이] 벽 소단 높이 방식 — 선기준(부분마다 선 위 P0 최저 · 최고에서 H씩 수평) · 옛소단(옛 사면 소단 높이 — 바닥이 기울면 따라 기욺).</summary>
    public enum LevelKind { Line = 0, OldBench = 1 }

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
        /// <summary>★[v114.1 · 검토 M3] 좁힌 마구리(짧은 줄기 · 부분 — 옛 폭 FaceRun/2까지) 수 · 그 가장 가파른 기울기(H / 폭 : 1) · 마구리를 빼고 남은 선 토막이 면 폭보다 짧은 줄기 수.</summary>
        public int CapNarrow, PieceShort; public double CapSteep;
        /// <summary>★[v115 · S218 (f)] 마구리 관문 — 끝 선(E · 자리 안 몫) 표본 수 · 벽 면이 그 선 높이를 안 따른 수(1 cm 넘음) · 최대 차 · 자리.</summary>
        public int EndN, EndMiss; public double EndMax; public (double X, double Y) EndAt;
        /// <summary>★[v114.0 · JACK 1105 소단 높이 관문] 표본 — Kind 0 = 소단(단마다 소단 폭 가운데 ± 0.5 m · 0.5 m 간격 · Z = Z_k) · 1 = F(0.5 m 간격 · Z = G).
        /// 제 바꿀 자리 밖 · 땅이 이기는 자리(절토 꼴 G′ − Z′_k ≤ 1 cm)는 안 넣는다. 합성 뒤 관문(<see cref="BenchGate"/>)도 이것을 쓴다.</summary>
        public List<(double X, double Y, double Z, int Kind)> BenchPts = new();
        /// <summary>★[v116.3 · 계단 천단] 블록 경계로 가른 줄기 수 · 가장 높은 자리 단면을 못 써 그 자리 옛 높이로 둔 수(오목 꼭짓점 이등분선 너머).</summary>
        public int StepCuts, StepFallback, StepFaceBad, StepRowBad;
        public double StepRowMax;
        /// <summary>★[v114.0 · 소단 높이 관문] 계수(까닭별) — 소단 표본 · 소단 높이 틀림(1 cm 넘음) · 소단에 벽 면 없음 · F 표본 · F 턱(1 cm 넘음) · F 못 읽음 · 가장 큰 차와 그 자리.</summary>
        public int BenchN, BenchSag, BenchMiss, FootN, FootBad, FootMiss; public double BenchMax; public (double X, double Y) BenchAt;
        /// <summary>★[v114.3 · JACK 1105 소단 높이 · 계획 2판 1절 불변식 · 재검토 M-B] 옛 소단 방식 계수 — 줄 안 이웃 점 뜀(|ΔZ| &gt; maxGrad·Δs + 1 cm) ·
        /// 줄 사이 면 높이(ε &lt; h ≤ H_k + maxGrad·f_k + 1 cm — 첫 면 L → 1줄 포함) · 규칙(L 위 소단 띠 안 |P0 − 옛 소단| ≤ 1 cm) · 옛 소단 이음(⚠만) · 벽 소단 기울기(⚠ 5 %).</summary>
        public int JumpN, JumpBad, FaceHN, FaceHBad, RuleN, RuleMiss, JoinN, JoinMiss, JoinNone;
        public double JumpMax, FaceHMin = double.MaxValue, FaceHMax, RuleMax, JoinMax, TiltAlong, TiltOut;
        public (double X, double Y) JumpAt, FaceHAt, RuleAt, JoinAt;
        /// <summary>★[v114.3 · JACK 1105 진입도로 옹벽 추가] 단 표가 «이어지지 않음»(NaN)이라 한 이웃 점 쌍 수 · 첫 자리 · 까닭(도로: 다른 다리 · 광선 안 건넘).</summary>
        public int JumpLeg; public (double X, double Y) JumpLegAt; public string JumpLegWhy = "";
        public List<FaceSeg> Faces = new();
        public bool HasCut => ZoneC.Count > 0; public bool HasFill => ZoneF.Count > 0;
        public List<string> Warn = new();
        public string Log = "";
        /// <summary>표시용 최댓값 — 기하를 정하지 않는다.</summary>
        public double HMax => Faces.Count == 0 ? 0 : Faces.Max(f => f.HMax);

        // ══ ★[v117.0 1단계 · 계획 1-11 · 검토 M3 · M6] 새 모양(선 안쪽 계단 · <see cref="BuildIn"/>)만 채운다 — 옛 Build는 손대지 않음(Shape 0) ══
        /// <summary>0 = 옛 바깥 계단 · 1 = 선 안쪽 계단.</summary>
        public int Shape;
        /// <summary>방향 +1 절토 · −1 성토 · 0 섞임(2 = 같음 · 거절) · 단 규칙(0 엄격 · 1 턱) · 단 수 n · 엄격 규칙의 n(턱 규칙 비교용).</summary>
        public int Dir = 2, Rule, Tiers = -1, TiersStrict = -1;
        /// <summary>안쪽 깊이 D_tot(가장 안쪽 벽 발 줄의 선에서 거리).</summary>
        public double Depth;
        /// <summary>바깥 벽 h = G′ − Z′_n(P_n) — 표본 · 넘음 · 음수(−1 mm 아래) · 범위 · 가장 높은 자리 · 한도 H_{n+1}.</summary>
        public int OuterN, OuterOver, OuterNeg; public double OuterMin = double.NaN, OuterMax = double.NaN, OuterLim = double.NaN; public (double X, double Y) OuterAt;
        /// <summary>턱(첫 소단이 선 위 원지반보다 높은(절토) · 낮은(성토) 양 = −h · 0 아래는 0) 최대 · 자리 · 턱 표본 수(1 mm 넘음) · 한도(턱 규칙).</summary>
        public double LedgeMax, LedgeLim; public int LedgeN; public (double X, double Y) LedgeAt;
        /// <summary>자리 — 벽 줄(가장 안쪽 포함) ↔ 바닥 가장 짧은 거리 · 자리.</summary>
        public double RoomMin = double.NaN; public (double X, double Y) RoomAt;
        /// <summary>줄 — 수 · 한 줄이 아닌 줄 · d ± 1 µm 어긋난 꼭짓점 · 0.25 mm 안이라 합친 점.</summary>
        public int RowN, RowPieceBad, RowDistBad, RowMerged;
        /// <summary>둥근 이음 부채(가장 가까운 늘인 L 점 = 꼭짓점)의 8분할 현 위 줄 꼭짓점 수 — 거리 d·cos(π/32) ~ d(평면 모양만 · 계획 1-5) · RowDistBad에 안 셈.</summary>
        public int RowChord;
        /// <summary>끝 — 끝 각 θ(°) · 날개벽(가로대마다 |W − P0|) 최대 · 자리 · 가로대 수 · 날개 길이(E′) · 날개 넓이(∫|W − P0| · E 따라) · 끝 A · B에서 G − P0(경계가 데이라잇이면 0).</summary>
        public double EndAngleA = double.NaN, EndAngleB = double.NaN, EndMaxA, EndMaxB, WingLenA, WingLenB, WingAreaA, WingAreaB, EndStepA, EndStepB;
        public (double X, double Y) EndAtA, EndAtB; public int EndRungs;
        /// <summary>바닥 호 |Z0 − P0| 최대(정의상 0) · 줄 · 띠 Z0 끊김 수 · 벽 안쪽 메움(벽 세모를 0.5 m 안 작은 세모로 나눠 무게중심 W′ − G′ &gt; 1 cm인 넓이 · 최대 — 절토 꼴) · 바닥 넓힘(평평한 띠 넓이 · 최대 폭).</summary>
        public double FloorStep, InnerFillArea, InnerFillMax, BandArea, BandMaxW; public int Z0Jump; public (double X, double Y) Z0JumpAt;
        /// <summary>M2 — 가장 안쪽 면(Z_1 − Z0 · 절토 꼴) 범위.</summary>
        public double Z1Min = double.NaN, Z1Max = double.NaN;
        /// <summary>★[검토 M3] 벽 세모 변 가운데 0.25 mm 미만 수 · 끝 쐐기(A · B를 꼭짓점으로 가진 벽 세모) 최소 높이.</summary>
        public int ShortEdges; public double WedgeMin = double.NaN;
        /// <summary>표본 · 띠 표본 수(소단 관문 Kind 0 가운데 띠 몫).</summary>
        public int BandN, BandBad;
        /// <summary>미리 재기 한 줄(<see cref="Survey"/> 같은 셈 · 같은 규칙).</summary>
        public string SurveyText = "";
        /// <summary>★[계획 1-7 · 2단계 합성용] R_out 고리(첫 점 반복 없음 · z = 0) — L 몫을 바꿀 자리 고리와 같은 L 점 배열로(두 고리가 L을 같은 꼭짓점으로 나눠 가짐).</summary>
        public List<Point3> OutRing = new();
        /// <summary>자가검증 한 줄(계획 4절).</summary>
        public string SelfCheck = "";

        // ══ ★[v117.0 · 계획 `2026-11-11_옹벽추가_수직옹벽.md` 1-5] 수직 옹벽(<see cref="BuildVertical"/> · Shape 2)만 채운다 ══
        /// <summary>벽 기울기 s(1:s) · 띠 폭 범위(L′까지 거리) · 벽 면적(Σ 가로대 높이 × L 길이) · 절토 · 성토 L 길이 · L 위 |W − G| · L′ 위 |W − P0| 최대 · 가로대 사이 폭 차 최대 · 부호 최대(m) · 꼬리 길이 · R_out 호 |G − P0| 최대 · 뺀 꼭짓점 차 최대.</summary>
        public double Batter, WMin = double.NaN, WMax = double.NaN, Area, LenC, LenF, EdgeGMax, EdgeP0Max, BatterDevMax, SignMax, TailLen, ArcStepMax, GoneMax;
        /// <summary>가로대 수 · G 변 · P0 변(L′ 위 포함) · 꼬리 곳 · 띠 안 꼭짓점 · 선 바깥 · 부호 틀림 · 부호 잰 점 · 풀이 되풀이 최대 · 못 푼 기운 가로대 · 뺀 꼭짓점.</summary>
        public int RungN, RungG, RungP0, TailN, InBandVerts, OutsideN, SignBad, SignN, SolveIterMax, Unsolved, GoneN;
        /// <summary>뺀 가로대(까닭별).</summary>
        public Dictionary<string, int> RungDrop = new();

        // ══ ★[v117.3 · 계획 `2026-11-11_부지_다단옹벽.md`] 다단(<see cref="BuildTiered"/> · Shape 3)만 채운다 ══
        /// <summary>단 수 n · 바깥 단이 끊긴 곳(양 끝 꼬리 말고) · 땅 위(절토 F &gt; max(G, P0) + 1 µm) 표본 수 · L′ 자른 점이 L′ 꼭짓점이 아닌 수 · 성토(절토) 주머니 고리 수.</summary>
        public int TierN, BreakN, AboveN, ClipMiss, PocketN;
        /// <summary>단높이 H · 소단 폭 b · 첫 소단 Z1(실제 z) · 자동 Z1 · 바깥 최고 · 안쪽 최고 · 구역 깊이 D_T · 땅 위 최대(m).</summary>
        public double TierH = double.NaN, TierB = double.NaN, TierZ1 = double.NaN, TierZ1Auto = double.NaN, OuterHMax, InnerHMax, TierDepth, AboveMax;
        /// <summary>참 = 다단이 필요 없어 v117 수직 결과 그대로(비트 같음 · Shape 2).</summary>
        public bool TierNone;
        /// <summary>바깥 단이 끊긴 자리(미리보기 빨간 X).</summary>
        public List<(double X, double Y)> BreakAt = new();
        /// <summary>첫 합성(P0′) 한 줄 · 단 머리 줄(미리보기 초록).</summary>
        public string TierCompText = "";
        public List<List<Point3>> TierRows = new();
        /// <summary>★[v117.9.3 · JACK 1112 «선을 그렸을 때 부지 경계 안으로 침범하는 걸 직관적으로 — 침범했을 경우만 소단선 미리보기»] 다단 «자리» 거절 때만 채움 —
        /// 선 안쪽 소단선(L에서 k·(소단 + 벽 기울기 폭), k = 1..n−1 · 새 경계 안 · 선 측점 안 · z = 0) 중 부지 바닥(계획선) <b>밖</b> 몫(<see cref="TierBenchOk"/>) · <b>안</b> 몫(<see cref="TierBenchHit"/> = 침범).</summary>
        public List<List<Point3>> TierBenchOk = new(), TierBenchHit = new();
        /// <summary>★[v117.4 설치 전 검토 중간 1] 테라스 T 세모(합성 전 · 하네스가 소단 높이를 바로 잰다).</summary>
        internal List<Tri> TierT = new();
        /// <summary>★[v117.x A] 지은 구간(2개 이상일 때만 · Z1 실제 z) · 구간 경계 자리(L 위 · 미리보기) · 옆벽(구간 경계 1:s 면) 최고.</summary>
        public List<TierSec> TierSecsUsed = new();
        public List<(double X, double Y)> TierSecMarks = new();
        public double TierSideHMax;
        /// <summary>★[v117.x A] 한 구간이 안 맞을 때(바깥 단 끊김) 구간 나눔 제안(2개 이상 · Z1 실제 z · N = 어림) · 한 줄씩 글 · 경계 자리 · 제안 뒤 남는 끊김(어림).</summary>
        public List<TierSec>? TierPropose;
        public List<string> TierProposeText = new();
        public List<(double X, double Y)> TierProposeMarks = new();
        public int TierProposeBreaks;
        /// <summary>★[v117.x B] 끝 비탈 — 썼는가 · 판정(GAP/CONTACT) · 한 줄 · 끝마다 비탈 높이 · 소단 수.</summary>
        public bool TierEnd; public string TierEndText = "";

        // ══ ★[로드맵 2-2 · 2-3 — 보고만] 수직(Shape 2) · 다단(Shape 3 = 2차 수직의 것) — 측점 높이표 · 분류 · 검토 높이 · 기초 재기(기하 · 기록 · 자가검증 줄은 안 바뀜) ══
        public WallProfile.Result? Profile;
    }

    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>★★[v114.3 · JACK 1105 진입도로 옹벽 추가 · 계획 `2026-11-05_진입도로_옹벽추가_보완.md` 2판 2절] 옛 소단 높이 단 표의 바탕 —
    /// <see cref="Build"/>가 옛 소단 방식에서 읽는 것은 이것뿐이다. 부지 = <see cref="OldBench"/>(사면 링) · 도로 = <see cref="RoadGrade.RoadBench"/>(그 시도 면의 광선 묶음).</summary>
    public abstract class BenchLevels
    {
        /// <summary>절토(위로)인가.</summary>
        public abstract bool Up { get; }
        /// <summary>줄 안 이웃 점 높이 차 한도의 기울기(부지 = 링 Z 완화 최대 경사 · 도로 = 그 자리 종단 경사) · 줄 사이 면 높이 여유(H_k + MaxGrad·f_k + 1 cm).</summary>
        public abstract double MaxGrad { get; }
        /// <summary>옛 소단이 실제로 있는 단 수.</summary>
        public abstract int K { get; }
        public abstract double TerraceW { get; }
        public abstract double BenchW { get; }
        public abstract int TierCount { get; }
        /// <summary>단 k(1부터)의 누적 rise · 단 간격 · 대소단.</summary>
        public abstract (double Rise, double H, bool Terrace) Tier(int k);
        /// <summary>단 k의 옛 소단 높이(실제 z) — 못 읽으면 NaN.</summary>
        public abstract double Level(int k, double x, double y);
        /// <summary>단 k 옛 소단 띠 안인가(규칙 검사 범위).</summary>
        public abstract bool InBand(int k, double x, double y);
        /// <summary>줄 안 이웃 두 점(실제 좌표 · 단 k 높이 za · zb)의 높이 차가 한도를 넘은 양(m · ≤ 0.01이면 이어짐).
        /// 부지 = |Δz| − MaxGrad·거리(옛 판 그대로). NaN = 이어지지 않음(도로: 다른 다리 · 광선을 안 건넘 — 까닭은 부르는 쪽이 <see cref="JumpWhy"/>로).</summary>
        public virtual double JumpOver(int k, double x0, double y0, double za, double x1, double y1, double zb, double dist)
            => Math.Abs(zb - za) - MaxGrad * dist;
        /// <summary>마지막 NaN 까닭(«다른 다리» · «광선 안 건넘» 등) — 기본 빈 값.</summary>
        public virtual string JumpWhy => "";
        /// <summary>로그 이름.</summary>
        public virtual string Name => "링";
        /// <summary>★[v114.3 · S218 (b)] 소단 가운데에도 줄을 넣는가(높이가 비틀린 단 표 — 도로). 부지 링은 거짓(옛 판 비트 같음).</summary>
        public virtual bool MidBench => false;
        /// <summary>★[v116.6] 로그 한 줄(단 표 · 셈) — 기본 = 이름.</summary>
        public virtual string Text => Name;
    }

    /// <summary>★★[v114.3 · JACK 1105 «옛 사면 소단 높이에 맞춤» · 계획 `2026-11-05_옹벽소단_사면소단높이.md` 2판 1절] 옛 사면 소단 높이 —
    /// 사면 만드는 코드(<see cref="GradingGeometry.Build"/>)가 실제로 만든 링(<see cref="VirtualSlope.RingRise"/>)에서 읽는다(그 셈을 따로 다시 하면 RelaxRingZ · 이등분선 뜀이 말없이 어긋난다).
    /// <para>단 k(1부터) = rise가 rise_k인 링들 — 안쪽(사면 끝) · 바깥(소단 끝 · 대소단이면 그 끝). 점마다 링 위 2D 가장 가까운 점의 Z(변 두 끝 1차 · TIN에 없는 2.5 m 넘는 이음 변은 건너뜀 — 재검토 LOW-E) ·
    /// 두 링 사이(소단 띠)면 거리 비로(폭 0이면 안쪽 링 — LOW-F) · 밖이면 가까운 링. 링이 없는 단(예산 위)은 마지막 링 Z ± (rise_k − rise_K)(LOW-G).</para></summary>
    public sealed class OldBench : BenchLevels
    {
        sealed class RingG
        {
            public double[] X = Array.Empty<double>(), Y = Array.Empty<double>(), Z = Array.Empty<double>();
            public bool[] Seg = Array.Empty<bool>();   // i → i+1 변이 TIN에 들어가는가(≤ 2.5 m)
            public List<Point3> Src = new();
            public double X0, Y0, X1, Y1;
        }
        readonly List<RingG> _rings = new();
        readonly List<(double Rise, double H, bool Terrace, int In, int Out)> _tiers = new();
        bool _up; double _maxGrad, _terraceW, _benchW; int _k;
        /// <summary>절토(위로)인가.</summary>
        public override bool Up => _up;
        /// <summary>링 Z 완화 최대 경사(평평한 계획선 = 0).</summary>
        public override double MaxGrad => _maxGrad;
        /// <summary>링이 있는 단 수(그 위는 예산 밖 — 마지막 링 ± rise 차).</summary>
        public override int K => _k;
        /// <summary>대소단 폭(대소단 단의 소단 폭).</summary>
        public override double TerraceW => _terraceW;
        /// <summary>그 쪽 사면 소단 폭(전역 · 옛 소단 이음 잴 때).</summary>
        public override double BenchW => _benchW;
        /// <summary>단 k(1부터)의 누적 rise · 높이 · 대소단.</summary>
        public override (double Rise, double H, bool Terrace) Tier(int k)
        {
            var t = _tiers[Math.Clamp(k, 1, _tiers.Count) - 1];
            return (t.Rise, t.H, t.Terrace);
        }
        public override int TierCount => _tiers.Count;
        /// <summary>단 k 링 두 줄(안쪽 · 바깥 — 없으면 null) — 하네스 · 로그.</summary>
        public (IReadOnlyList<Point3>? In, IReadOnlyList<Point3>? Out) RingsOf(int k)
        {
            if (k < 1 || k > K) return (null, null);
            var t = _tiers[k - 1];
            return (t.In >= 0 ? _rings[t.In].Src : null, t.Out >= 0 ? _rings[t.Out].Src : null);
        }

        /// <summary>★[v114.3 · 재검토 M-C] 링 캐시 열쇠 — 링을 정하는 입력(경계 · 제원 전부 · 링 셈 구간)을 비트 그대로 적는다(같으면 같은 링).</summary>
        public static string Fingerprint(IReadOnlyList<Point3> boundary, GradingParams p, IEnumerable<SlopeZone>? cutZones, IEnumerable<SlopeZone>? fillZones)
        {
            var sb = new System.Text.StringBuilder();
            void D(double v) => sb.Append(BitConverter.DoubleToInt64Bits(v)).Append(',');
            foreach (var q in boundary) { D(q.X); D(q.Y); D(q.Z); }
            sb.Append('|');
            foreach (var v in new[] { p.CutBenchHeight, p.FillBenchHeight, p.CutBenchWidth, p.FillBenchWidth, p.CutSlope, p.FillSlope, p.CellSize, p.MaxRise, p.MaxRiseCut, p.MaxRiseFill,
                                      p.VertexSpacing, p.MinSlope, p.WallGateSlope, p.MinFaceRun, p.MiterLimit, p.TerraceInterval, p.TerraceWidth, p.ZoneBlendLength }) D(v);
            sb.Append(p.MaxBenches).Append(',').Append(p.MiterConvex).Append(',').Append(p.NoPlatform).Append(',').Append(p.SeamLines).Append(',').Append(p.TransitionSteps).Append(',').Append(p.MountainTerrace).Append('|');
            foreach (var st in new[] { p.CutBenchSteps, p.FillBenchSteps }) { foreach (var (fb, h) in st) { sb.Append(fb).Append(':'); D(h); } sb.Append('|'); }
            foreach (var zs in new[] { cutZones, fillZones })
            {
                if (zs != null)
                    foreach (var z in zs)
                    {
                        D(z.T0); D(z.T1); sb.Append(z.Partial).Append(';');
                        foreach (var (fb, s, w) in z.Rules) { sb.Append(fb).Append(':'); D(s); D(w); }
                        if (z.Ref != null) foreach (var q in z.Ref) { D(q.X); D(q.Y); }
                        sb.Append('/');
                    }
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>링 묶음 → 단 표. 못 만들면 null · 까닭(링 rise가 단 목록과 다름 · 빠진 단 · 링 없음).</summary>
        public static OldBench? Of(VirtualSlope vs, GradingParams p, bool up, out string why)
        {
            why = "";
            var ob = new OldBench { _up = up, _maxGrad = Math.Max(0, vs.MaxGrad), _terraceW = Math.Max(0, p.TerraceWidth), _benchW = Math.Max(0, p.BenchWidthOf(up)) };
            if (vs.RingRise.Count != vs.Rings.Count) { why = "링 rise 목록이 링과 나란하지 않다"; return null; }
            var groups = new List<(double Rise, int In, int Out)>();
            for (int i = 0; i < vs.Rings.Count; i++)
            {
                double r = vs.RingRise[i];
                if (double.IsNaN(r) || vs.Rings[i].Count < 3) continue;
                int gi = ob._rings.Count;
                ob._rings.Add(RingOf(vs.Rings[i]));
                if (groups.Count > 0 && Math.Abs(groups[^1].Rise - r) <= 1e-9) groups[^1] = (groups[^1].Rise, groups[^1].In, gi);
                else groups.Add((r, gi, gi));
            }
            if (groups.Count == 0) { why = (up ? "절토" : "성토") + " 사면 링이 없다(옛 사면이 없는 쪽)"; return null; }
            var tl = StepProfile.TierList(p, up, groups.Count + 400);
            int gk = 0;
            for (int k = 0; k < tl.Count; k++)
            {
                int iIn = -1, iOut = -1;
                if (gk < groups.Count && Math.Abs(groups[gk].Rise - tl[k].Rise) <= 1e-6) { iIn = groups[gk].In; iOut = groups[gk].Out; gk++; ob._k = k + 1; }
                else if (gk < groups.Count) { why = string.Format(Ci, "{0} 링 rise {1:F3}가 단 목록({2}단 {3:F3})과 다르다 — 링을 다시 못 만든다", up ? "절토" : "성토", groups[gk].Rise, k + 1, tl[k].Rise); return null; }
                ob._tiers.Add((tl[k].Rise, tl[k].H, tl[k].Terrace, iIn, iOut));
            }
            if (gk < groups.Count) { why = string.Format(Ci, "{0} 링 {1}묶음 가운데 {2}묶음만 단 목록에 맞았다", up ? "절토" : "성토", groups.Count, gk); return null; }
            return ob;
        }

        static RingG RingOf(List<Point3> r)
        {
            int n = r.Count;
            bool closed = n >= 2 && Math.Abs(r[0].X - r[^1].X) < 1e-9 && Math.Abs(r[0].Y - r[^1].Y) < 1e-9;
            var pts = closed ? r.Take(n - 1).ToList() : r.ToList();
            var g = new RingG { Src = r, X = pts.Select(q => q.X).ToArray(), Y = pts.Select(q => q.Y).ToArray(), Z = pts.Select(q => q.Z).ToArray(), Seg = new bool[pts.Count] };
            for (int i = 0; i < pts.Count; i++)
            {
                int j = (i + 1) % pts.Count;
                double dx = g.X[j] - g.X[i], dy = g.Y[j] - g.Y[i];
                g.Seg[i] = dx * dx + dy * dy <= BreaklinePrep.RingSegMaxM * BreaklinePrep.RingSegMaxM;
            }
            g.X0 = g.X.Min(); g.X1 = g.X.Max(); g.Y0 = g.Y.Min(); g.Y1 = g.Y.Max();
            return g;
        }

        /// <summary>링 위(TIN에 있는 변만) 2D 가장 가까운 점 — 거리 · Z.</summary>
        static (double D, double Z) Near(RingG g, double x, double y)
        {
            double bd = double.MaxValue, bz = double.NaN;
            int n = g.X.Length;
            for (int i = 0; i < n; i++)
            {
                if (!g.Seg[i]) continue;
                int j = (i + 1) % n;
                double ex = g.X[j] - g.X[i], ey = g.Y[j] - g.Y[i], l2 = ex * ex + ey * ey;
                double t = l2 < 1e-18 ? 0 : Math.Clamp(((x - g.X[i]) * ex + (y - g.Y[i]) * ey) / l2, 0, 1);
                double dx = x - g.X[i] - ex * t, dy = y - g.Y[i] - ey * t, d2 = dx * dx + dy * dy;
                if (d2 < bd) { bd = d2; bz = g.Z[i] + (g.Z[j] - g.Z[i]) * t; }
            }
            return (Math.Sqrt(bd), bz);
        }

        static bool Inside(RingG g, double x, double y)
        {
            if (x < g.X0 || x > g.X1 || y < g.Y0 || y > g.Y1) return false;
            bool c = false; int n = g.X.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if ((g.Y[i] > y) != (g.Y[j] > y) && x < (g.X[j] - g.X[i]) * (y - g.Y[i]) / (g.Y[j] - g.Y[i]) + g.X[i]) c = !c;
            return c;
        }

        /// <summary>★[v115 · 설치 전 검토 S2] <see cref="Level"/> 부른 수 · 걸린 틱(Stopwatch) — Build가 ④-c 로그에 이번 짓기 몫을 적는다(링 점 전부를 훑는 셈이라 재 봄).</summary>
        internal long Calls, Ticks;
        /// <summary>단 k(1부터)의 옛 소단 높이(실제 z) — 못 읽으면 NaN.</summary>
        public override double Level(int k, double x, double y)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return LevelCore(k, x, y); }
            finally { Calls++; Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; }
        }

        double LevelCore(int k, double x, double y)
        {
            if (k < 1 || k > _tiers.Count || K < 1) return double.NaN;
            if (k > K) return LevelCore(K, x, y) + (Up ? 1 : -1) * (_tiers[k - 1].Rise - _tiers[K - 1].Rise);
            var t = _tiers[k - 1];
            var gi = _rings[t.In]; var go = _rings[t.Out];
            var (di, zi) = Near(gi, x, y);
            if (t.In == t.Out) return zi;
            var (dO, zO) = Near(go, x, y);
            if (double.IsNaN(zi)) return zO;
            if (double.IsNaN(zO)) return zi;
            if (!Inside(gi, x, y) && Inside(go, x, y)) return di + dO < 1e-9 ? zi : (zi * dO + zO * di) / (di + dO);
            return di <= dO ? zi : zO;
        }

        /// <summary>★[v115 · 설치 전 검토 S3] 높이를 못 읽은(NaN) 이웃 점 — 까닭을 남긴다(옛 판은 빈 값이라 «이어지지 않는 자리»만 보였다).</summary>
        public override double JumpOver(int k, double x0, double y0, double za, double x1, double y1, double zb, double dist)
        {
            if (double.IsNaN(za) || double.IsNaN(zb))
            {
                _jumpWhy = k < 1 || k > _tiers.Count || K < 1
                    ? string.Format(Ci, "단 {0}이 옛 사면 단 목록({1}단) 밖", k, _tiers.Count)
                    : string.Format(Ci, "단 {0} 옛 소단 링에서 높이를 못 읽음(그 단 링 변이 모두 TIN 이음 한도 {1:0.#} m를 넘음)", k, BreaklinePrep.RingSegMaxM);
                return double.NaN;
            }
            return base.JumpOver(k, x0, y0, za, x1, y1, zb, dist);
        }
        string _jumpWhy = "";
        public override string JumpWhy => _jumpWhy;

        /// <summary>단 k 소단 띠 안(안쪽 링 밖 · 바깥 링 안 — 대소단 폭 포함)인가.</summary>
        public override bool InBand(int k, double x, double y)
        {
            if (k < 1 || k > K) return false;
            var t = _tiers[k - 1];
            if (t.In == t.Out) return false;
            return !Inside(_rings[t.In], x, y) && Inside(_rings[t.Out], x, y);
        }

        /// <summary>★[재검토 M-A] 링 꼭짓점이 합성 전 면 꼭짓점과 0.1 mm(평면) · 1 mm(높이) 안에서 겹치는가 — 상자 안 · 그 쪽 바꿀 자리(데이라잇 안 · 계획선 밖) 안 꼭짓점만 센다.
        /// 돌려주는 값: 센 꼭짓점 · 겹친 것 · 안 겹친 첫 자리.</summary>
        public (int N, int Hit, (double X, double Y)? Miss) VertexMatch(IReadOnlyList<Tri> p0Tris, Func<double, double, bool> counted, double x0, double y0, double x1, double y1)
        {
            var cell = new Dictionary<(long, long), List<Point3>>();
            const double C = 1e-4;
            foreach (var t in p0Tris)
                foreach (var q in new[] { t.A, t.B, t.C })
                {
                    if (q.X < x0 - 1 || q.X > x1 + 1 || q.Y < y0 - 1 || q.Y > y1 + 1) continue;
                    var key = ((long)Math.Floor(q.X / C), (long)Math.Floor(q.Y / C));
                    if (!cell.TryGetValue(key, out var l)) cell[key] = l = new();
                    l.Add(q);
                }
            int n = 0, hit = 0; (double, double)? miss = null;
            for (int k = 1; k <= K; k++)
            {
                var t = _tiers[k - 1];
                foreach (int ri in t.In == t.Out ? new[] { t.In } : new[] { t.In, t.Out })
                {
                    var g = _rings[ri];
                    for (int i = 0; i < g.X.Length; i++)
                    {
                        double x = g.X[i], y = g.Y[i];
                        if (x < x0 || x > x1 || y < y0 || y > y1 || !counted(x, y)) continue;
                        n++;
                        long kx = (long)Math.Floor(x / C), ky = (long)Math.Floor(y / C);
                        bool ok = false;
                        for (long a = -1; a <= 1 && !ok; a++) for (long b = -1; b <= 1 && !ok; b++)
                                if (cell.TryGetValue((kx + a, ky + b), out var l))
                                    foreach (var q in l) if (Math.Abs(q.X - x) <= 1e-4 && Math.Abs(q.Y - y) <= 1e-4 && Math.Abs(q.Z - g.Z[i]) <= 1e-3) { ok = true; break; }
                        if (ok) hit++; else miss ??= (x, y);
                    }
                }
            }
            return (n, hit, miss);
        }
    }

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
        // ★[v117.0 1단계 · 검토 H2] 새 모양은 부르는 쪽이 명시할 때만 — 기본(Out)은 아래 옛 몸통 그대로(비트 같음)
        if (inp.Shape == WallShape.In) return BuildIn(inp);
        if (inp.Shape == WallShape.Vertical) return BuildVertical(inp);   // ★[v117.0 · JACK 1111] 수직 옹벽 — 옛 몸통은 그대로
        if (inp.Shape == WallShape.Tiered) return BuildTiered(inp);   // ★[v117.3] 다단 — 수직 옹벽 + 선 안쪽 소단
        var R = new Built();
        var log = new System.Text.StringBuilder();
        // ★[v115 · 설치 전 검토 S2] 옛 소단 높이 읽기 수 · 시간(이번 짓기 몫 — 링 단 표는 여러 짓기가 함께 씀)
        var obC0 = inp.CutBench as OldBench; var obF0 = inp.FillBench as OldBench;
        long lvCalls0 = (obC0?.Calls ?? 0) + (obF0?.Calls ?? 0), lvTicks0 = (obC0?.Ticks ?? 0) + (obF0?.Ticks ?? 0);
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
        var eCut = new List<List<Point3>>();   // ★[v115 · S218 (f)] 자리로 자른 끝 선 E(마구리 관문이 잰다)
        int partNo = 0;
        foreach (var part in parts)
        {
            partNo++;
            double sg = part.Up ? 1.0 : -1.0;
            // ★[v114.3 · JACK 1105 «옛 사면 소단 높이에 맞춤» · 계획 2판 2절] 옛 소단 방식이면 단 k의 높이 · 단 간격 H_k · 소단 폭 T_k(대소단 = 그 단 폭) · 면 폭 f_k를 옛 사면 링에서 —
            //   선기준(옛 규칙)은 아래 함수들이 옛 값(zr + k·H · H · T · f)을 그대로 돌려 비트 같다
            BenchLevels? ob = inp.LevelMode == LevelKind.OldBench ? (part.Up ? inp.CutBench : inp.FillBench) : null;
            if (inp.LevelMode == LevelKind.OldBench && ob == null)
            {
                // ★[v115 · 설치 전 검토 S1] 진짜 까닭(링 rise · 빠진 단 · 링 없음 · 예외)을 화면에도 — 옛 판은 로그에만 남기고 «정지면을 다시 만든 뒤»만 보였다
                string bw = part.Up ? inp.CutBenchWhy : inp.FillBenchWhy;
                return No("옛 소단", $"{(part.Up ? "절토" : "성토")} 쪽 옛 사면 링이 없어 옛 소단 높이를 못 읽습니다{(bw.Length > 0 ? "(" + bw + ")" : "")} — 정지면을 다시 만든 뒤 하세요", Wd(pl.At((part.S0 + part.S1) / 2)));
            }
            double T = part.T;
            double Hk(int k) => ob == null ? part.H : ob.Tier(k).H;
            double Tk(int k) => ob == null ? T : ob.Tier(k).Terrace ? ob.TerraceW : T;
            double Fk(int k) => Math.Max(Hk(k) * Math.Max(0, inp.SlopeW), Math.Max(1e-4, inp.FaceRun));
            // 마구리 폭 · 표시용 면 폭은 그 부분이 쓸 수 있는 가장 큰 단 간격으로(옛 소단 방식 — 계획 2판 2절 «마구리»)
            double H = ob == null ? part.H : Enumerable.Range(1, ob.TierCount).Max(k => ob.Tier(k).H);
            double f = Math.Max(H * Math.Max(0, inp.SlopeW), Math.Max(1e-4, inp.FaceRun));
            R.Face = Math.Max(R.Face, f);
            // ★[v114.1 · 현장 합성 거절 계측] 이 부분의 마구리 폭 vE ≥ 면 폭(H · SlopeW) — 줄기 갈림 마구리는 이웃 줄기와 한 단(H) 높이가 달라 폭 vE에서 H를 내린다.
            //   vEps = FaceRun(출하 MinFaceRun 0.005)이면 5 m를 5 mm에 내려 1000 : 1(설계 면 100 : 1의 열 배 — 현장 성토 · 절토 벽 세모 44 · 21개 1005 : 1)이 되고,
            //   합성이 그 면을 정지면 변으로 가른 두 점(네모 52 µm)을 Civil이 합치며 51.7 mm가 틀어져 거절했다(하네스는 FaceRun 0.05라 못 봤다). 면과 같은 기울기로 —
            //   부분마다(검토 M1: 절 · 성 H가 다르면 낮은 부분의 f가 작다). SlopeW = 0(곧은 벽)이면 vE = FaceRun 그대로 — 앞면도 FaceRun 폭이라 합성 쌍 위험은 남는다(알려진 한계)
            double vE = Math.Max(vEps, H * Math.Max(0, inp.SlopeW));
            R.VEps = Math.Max(R.VEps, vE);
            // ★ 가름점 C 쪽 부분 끝도 줄기 갈림처럼 마구리 반 폭을 물린다 — 두 부분 끝 띠가 C 법선에서 만나고(z = G) 서로 겹치지 않게(A · B 쪽은 끝 너머 vE 띠 · 부분마다 제 반 폭)
            bool cA = part.S0 > 1e-9, cB = part.S1 < pl.Len - 1e-9;
            // 기준 = 절토 꼴 P0′ 최소(성토는 P0 최대)
            var pS = smp.Where(q => q.S >= part.S0 - 1e-12 && q.S <= part.S1 + 1e-12).Select(q => (q.S, P: sg * q.P)).ToList();
            pS.Insert(0, (part.S0, sg * P0At(part.S0))); pS.Add((part.S1, sg * P0At(part.S1)));
            pS = pS.OrderBy(q => q.S).ToList();
            double zr = pS.Min(q => q.P);
            part.Zref = sg * zr;
            double Lv(int k) => zr + k * H;   // 절토 꼴 높이(선기준)
            // ★[v114.3] 단 k의 높이(절토 꼴 · 지역 좌표 점마다) — 옛 소단 = 링에서 · 선기준 = Lv(k)
            double ZL0(int k, double lx, double ly) => ob == null ? Lv(k) : sg * ob.Level(k, lx + ox, ly + oy);
            // ★[v116.3 · 계단 천단 · 계획 2판 H1] 계단 높이는 줄기를 정하고 블록으로 가른 뒤에만 켠다(줄기 판정 · 합치기 · 입력 관문은 ZL0)
            Func<int, double, double, double>? zlStep = null;
            double ZL(int k, double lx, double ly) => zlStep != null ? zlStep(k, lx, ly) : ZL0(k, lx, ly);
            // 선 위 s에서 바깥으로 d 민 점(지역 좌표)
            (double X, double Y) RowPt(double s, double d) { var c = pl.At(s); var (tx, ty) = pl.Tan(s); return (c.X - ty * side * d, c.Y + tx * side * d); }
            // ★[v114.3 · 계획 2판 2절 «줄기» · 재검토 M-B] P0′ ≥ Thr(k)면 단 k를 넘어 다음 단 — 옛 소단은 «그 단 ±1 cm 안(옛 소단 위)이면 다음 단»(첫 면 h = H_k)이고
            //   견주는 높이는 L 점이 아니라 그 면이 오를 줄(바깥 f_k)의 제 높이다(바닥이 기울면 L과 줄의 높이가 다르다)
            double Thr(int k, double s) { if (ob == null) return Lv(k); var (x, y) = RowPt(s, Fk(k)); return ZL(k, x, y) - 0.01; }
            int Jof(double p, double s)
            {
                if (ob == null) return (int)Math.Floor((p - zr) / H) + 1;
                int k = 1;
                while (k < ob.TierCount && p >= Thr(k, s)) k++;
                return k;
            }
            // ★[v114.3 · 계획 2판 1절 «검사 범위» M4] 규칙 — 이 부분 L 표본 가운데 단 k 소단 띠(두 끝 링 사이 · 대소단 폭 포함) 안의 모든 점에서 |P0 − 옛 소단| ≤ 1 cm(앞 구역 사면이 덮은 자리 등)
            if (ob != null)
            {
                foreach (var q in smp)
                {
                    if (q.S < part.S0 - 1e-12 || q.S > part.S1 + 1e-12) continue;
                    var c = pl.At(q.S);
                    for (int k = 1; k <= ob.K; k++)
                    {
                        if (!ob.InBand(k, c.X + ox, c.Y + oy)) continue;
                        double lv = ob.Level(k, c.X + ox, c.Y + oy), d = Math.Abs(q.P - lv);
                        R.RuleN++;
                        if (d > R.RuleMax) { R.RuleMax = d; R.RuleAt = Wd(c); }
                        if (d > 0.01) R.RuleMiss++;
                    }
                }
                if (R.RuleMiss > 0)
                {
                    var c = new Coordinate(R.RuleAt.X - ox, R.RuleAt.Y - oy);
                    var q = smp.OrderBy(u => pl.At(u.S).Distance(c)).First();
                    double lvA = double.NaN; for (int k = 1; k <= ob.K; k++) if (ob.InBand(k, R.RuleAt.X, R.RuleAt.Y)) { lvA = ob.Level(k, R.RuleAt.X, R.RuleAt.Y); break; }
                    return No("옛 소단", string.Format(Ci, "옛 사면 소단 높이와 다름 ({0:F1}, {1:F1}) 링 {2:F2} · 면 {3:F2}(L 위 띠 안 {4}점 중 {5}점 · 1 cm 넘음) — 앞 구역 사면 · 다른 옹벽이 덮은 자리인지 보세요",
                        R.RuleAt.X, R.RuleAt.Y, lvA, q.P, R.RuleN, R.RuleMiss), R.RuleAt);
                }
            }
            // 줄기 — j가 바뀌는 자리(P0′ = Z′_k)는 표본 사이 1차(표본이 P0 꺾임을 다 품는다 → 정확)
            var stems = new List<Stem>();
            {
                double s0 = pS[0].S; int j0 = Jof(pS[0].P, pS[0].S);
                for (int i = 1; i < pS.Count; i++)
                {
                    int j1 = Jof(pS[i].P, pS[i].S);
                    if (j1 == j0) continue;
                    double pa = pS[i - 1].P, pb = pS[i].P, sa = pS[i - 1].S, sb = pS[i].S;
                    int step = j1 > j0 ? 1 : -1;
                    for (int jj = j0; jj != j1; jj += step)
                    {
                        double t;
                        if (ob == null)
                        {
                            double lev = step > 0 ? Lv(jj) : Lv(jj - 1);
                            t = Math.Abs(pb - pa) < 1e-15 ? 0.5 : Math.Clamp((lev - pa) / (pb - pa), 0, 1);
                        }
                        else
                        {
                            // 옛 소단 — 문턱이 자리마다 다르다: g(s) = P0′(s) − Thr(k, s)가 0을 지나는 자리(표본 사이 1차)
                            int kt = step > 0 ? jj : jj - 1;
                            double ga = pa - Thr(kt, sa), gb = pb - Thr(kt, sb);
                            t = Math.Abs(gb - ga) < 1e-15 ? 0.5 : Math.Clamp(-ga / (gb - ga), 0, 1);
                        }
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
                // ★[v114.3] 옛 소단은 그 줄기의 단 간격 H_j · 줄(바깥 f_j)의 제 높이로 잰다 · 첫 면 h > 0.1 mm(접힌 면 0 — 재검토 M-B ε와 같은 문턱)
                double HFit(Stem st, int jn, double s, double p)
                {
                    if (ob == null) return Lv(jn) - p;
                    var (x, y) = RowPt(s, Fk(jn)); return ZL(jn, x, y) - p;
                }
                double hLo = ob == null ? -1e-9 : 1e-4;
                bool Fits(Stem st, int jn)
                {
                    foreach (var q in pS) if (q.S >= st.S0 - 1e-12 && q.S <= st.S1 + 1e-12) { double h = HFit(st, jn, q.S, q.P); if (!(h > hLo && h <= Hk(jn) + 0.01)) return false; }
                    foreach (double s in new[] { st.S0, st.S1 }) { double h = HFit(st, jn, s, sg * P0At(s)); if (!(h > hLo && h <= Hk(jn) + 0.01)) return false; }
                    return true;
                }
                var stuck = new HashSet<Stem>();
                for (int guard = 0; guard < 10000; guard++)
                {
                    Stem? sh = null;
                    // ★ 짧은 줄기(< f) + 길어도 «P0이 높이 경계 1 cm 안에만 있는» 줄기(이웃 높이로 합쳐도 첫 면 ≤ H + 1 cm) — 삼각망 정지면이 Z_k ± 몇 mm로 흔들려 생기는 줄기 갈림을 마구리 하나로
                    // ★[v114.1 · 검토 M2] 잣대는 마구리 반 폭을 뺀 L 위 토막 — vE = f이면 두 끝 마구리가 줄기를 거의 다 먹어(5.005 cm 줄기 → 50 µm 토막) 다시 Civil이 합칠 쌍이 된다
                    double Piece(int si) => stems[si].S1 - stems[si].S0 - (si > 0 || cA ? vE / 2 : 0) - (si + 1 < stems.Count || cB ? vE / 2 : 0);
                    for (int si = 0; si < stems.Count; si++)
                    {
                        var st = stems[si];
                        if (stuck.Contains(st)) continue;
                        bool cand = Piece(si) < (ob == null ? f : Fk(st.J))
                            ||(si > 0 && stems[si - 1].J != st.J && Fits(st, stems[si - 1].J))
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
            // ★[v116.3 · 계단 천단 · 계획 2판 H3 · M5] 블록 경계에서 줄기를 가름(옛 소단만 — 선기준 높이는 이미 수평) · 줄기 경계 · 부분 끝 곁(max(1 m, 2·max(f, vE)) 안)은 안 가름 · 마지막 토막 < B/3이면 앞 블록에 붙임
            bool stepOn = inp.TopStep && ob != null;
            if (stepOn)
            {
                double B = inp.BlockLen > 0 ? Math.Max(1.0, inp.BlockLen) : 9.0;
                double tolB = Math.Max(1.0, 2 * Math.Max(f, vE));
                var cutsB = new List<double>();
                for (double sc = part.S0 + B; sc < part.S1 - 1e-9; sc += B) cutsB.Add(sc);
                if (cutsB.Count > 0 && part.S1 - cutsB[^1] < B / 3) cutsB.RemoveAt(cutsB.Count - 1);
                var bnds = stems.Skip(1).Select(st => st.S0).Concat(new[] { part.S0, part.S1 }).ToList();
                foreach (double sc in cutsB)
                {
                    if (bnds.Any(bd => Math.Abs(bd - sc) < tolB)) continue;
                    int si = stems.FindIndex(st => st.S0 < sc && st.S1 > sc);
                    if (si < 0) continue;
                    var st0 = stems[si];
                    stems.Insert(si + 1, new Stem { S0 = sc, S1 = st0.S1, J = st0.J });
                    st0.S1 = sc; bnds.Add(sc); R.StepCuts++;
                }
            }
            part.Stems = stems;
            if (ob != null) part.H = Hk(stems.Min(s => s.J));   // 표시용(첫 단 간격)
            for (int k = 1; k < stems.Count; k++) { var c = pl.At(stems[k].S0); R.Points.Add((c.X + ox, c.Y + oy, 1)); }

            // 줄 — 부분 선(양 끝 곧게 늘임)에서 거리 d의 버퍼 테두리 ∩ 줄기 칸(바깥 쪽 한쪽 버퍼 · 끝 = 법선)
            double dBig = reachLim + 5;
            var subPart = pl.Sub(part.S0, part.S1);
            var lpExt = WallInPoly.ExtendEnds(subPart.Select(c => new Point3(c.X, c.Y, 0)).ToList(), dBig + 5);
            var lpG = gf.CreateLineString(lpExt.Select(q => new Coordinate(q.X, q.Y)).ToArray());
            var bufCache = new Dictionary<double, Geometry>();
            Geometry BufB(double d) { if (!bufCache.TryGetValue(d, out var g)) bufCache[d] = g = lpG.Buffer(d, rnd).Boundary; return g; }
            int nSt = stems.Count;
            // ★[v114.1 · 검토 M3] 마구리 반 폭은 줄기마다 — 보통 vE/2. 못 합친 짧은 줄기 · 짧은 부분(2~4 cm)은 L 위 토막이 면 폭 f 이상 남게 좁히되 옛 폭(FaceRun/2)까지만 —
            //   좁힌 마구리는 가파르다(H / 폭 : 1 — 세고 ⚠). 옛 폭에서도 토막이 없으면(1 µm 안) 예전처럼 거절. 옛 판이 짓던 꼴은 모두 짓는다
            var half = new double[nSt];
            int capNarrow = 0, pieceShort = 0; double capSteep = 0;
            for (int k = 0; k < nSt; k++)
            {
                int nc = (k > 0 || cA ? 1 : 0) + (k + 1 < nSt || cB ? 1 : 0);
                double len = stems[k].S1 - stems[k].S0, fS = ob == null ? f : Fk(stems[k].J);   // ★[v114.3] 그 줄기 첫 단의 면 폭
                half[k] = vE / 2;
                if (nc == 0 || len - nc * half[k] >= fS) continue;
                half[k] = Math.Max(vEps / 2, Math.Min(vE / 2, (len - fS) / nc));
                if (half[k] < vE / 2 - 1e-12) capNarrow++;
                if (len - nc * half[k] < fS - 1e-12) pieceShort++;
            }
            double shS(int k) => k > 0 || cA ? half[k] : 0;
            double shE(int k) => k + 1 < nSt || cB ? half[k] : 0;
            // 좁힌 마구리의 기울기(줄기 갈림 = 두 반 폭의 합에서 H · C 쪽 끝 = 제 반 폭에서 최대 H로 잰다) — ★[v114.3] 옛 소단은 갈림 자리의 실제 단 간격(높은 줄기의 첫 단)
            for (int k = 0; k < nSt; k++)
            {
                if (k + 1 < nSt) capSteep = Math.Max(capSteep, (ob == null ? H : Hk(Math.Max(stems[k].J, stems[k + 1].J))) / Math.Max(1e-9, shE(k) + shS(k + 1)));
                if (k == 0 && cA) capSteep = Math.Max(capSteep, (ob == null ? H : Hk(stems[0].J)) / Math.Max(1e-9, 2 * shS(0)));
                if (k == nSt - 1 && cB) capSteep = Math.Max(capSteep, (ob == null ? H : Hk(stems[k].J)) / Math.Max(1e-9, 2 * shE(k)));
            }
            if (capNarrow > 0) R.Warn.Add(string.Format(Ci, "부분 {0}: 짧은 줄기 · 부분 {1}곳의 마구리를 좁혔다(가장 가파른 마구리 {2:F0} : 1 · 설계 면 {3:F0} : 1 — 합성이 거절할 수 있다)", partNo, capNarrow, capSteep, H / f));
            if (pieceShort > 0) R.Warn.Add(string.Format(Ci, "부분 {0}: 마구리를 빼고 남은 선 토막이 면 폭({1:F3} m)보다 짧은 줄기 {2}개(0.1 mm 안 점 쌍이 생길 수 있다)", partNo, f, pieceShort));
            R.CapNarrow += capNarrow; R.CapSteep = Math.Max(R.CapSteep, capNarrow > 0 ? capSteep : 0); R.PieceShort += pieceShort;
            (double A, double B) Shr(int k) => (stems[k].S0 + shS(k), stems[k].S1 - shE(k));
            (double X, double Y) TanAt(double s, bool atStart) => pl.Tan(atStart ? Math.Min(s + 1e-9, pl.Len) : Math.Max(s - 1e-9, 0));
            var (pt0x, pt0y) = TanAt(part.S0, true); var (pt1x, pt1y) = TanAt(part.S1, false);
            var cP0 = pl.At(part.S0); var cP1 = pl.At(part.S1);
            var ext0 = cA ? cP0 : new Coordinate(cP0.X - pt0x * vE, cP0.Y - pt0y * vE);
            var ext1 = cB ? cP1 : new Coordinate(cP1.X + pt1x * vE, cP1.Y + pt1y * vE);
            for (int k = 0; k < nSt; k++)
            {
                var (a, b) = Shr(k);
                if (b - a < 1e-6) return No("줄", string.Format(Ci, "부분 {0}의 줄기 {1}({2:F4} m)이 옛 마구리 폭({3:F3} m)보다도 짧아 선 토막이 남지 않습니다 — 선을 조금 옮기세요", partNo, k + 1, stems[k].S1 - stems[k].S0, vEps), Wd(pl.At(stems[k].S0)));
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
            // ★[v116.3 · 계단 천단 · 계획 2판 M4] 계단 높이 = 그 점이 든 줄기의 «가장 높은 자리» s*의 옛 단면을 줄기 따라 수평으로 민 것 —
            //   s* = 그 단 면 끝 거리(DF)에서 0.25 m 표본 중 ZL0(절토 꼴) 최대 · 높이 = ZL0(그 단, RowPt(s*, 그 점 거리 d)).
            //   거리마다 따로 최대를 잡으면 소단 띠 안에서 최대 자리가 바뀌어 줄 사이 세모와 1 cm 어긋났다(S216 (k) 4 m) — 한 자리 단면이면 줄 사이가 곧다.
            //   표본의 SOn이 줄기 밖(오목 꼭짓점 이등분선 너머 = 남의 다리)이거나 NaN이면 버림 · 못 쓰면 그 자리 ZL0.
            //   줄기를 아는 곳(줄 · 끝 선 · 소단 표본 · 면 — 줄기마다 도는 고리)은 curStem으로 바로 넘김(오목 꼭짓점 곁 끝 선 점의 SOn이 이웃 줄기로 넘어갔다)
            int curStem = -1;
            if (stepOn)
            {
                var sStar = new Dictionary<(int, int), double>();
                zlStep = (kq, x, y) =>
                {
                    int q = curStem;
                    if (q < 0 || q >= stems.Count)
                    {
                        double sq = Math.Clamp(SOn(x, y).S, part.S0, part.S1);
                        q = stems.Count - 1; for (int i = 0; i < stems.Count; i++) if (sq <= stems[i].S1 + 1e-9) { q = i; break; }
                    }
                    var st = stems[q];
                    if (!sStar.TryGetValue((q, kq), out double sb))
                    {
                        double dRef = DF(st.J, Math.Max(1, kq - st.J + 1)), dChk = dRef + Tk(kq) + 0.5;
                        // s* 후보 = 그 단 소단 끝(+0.5 m)까지 민 점도 줄기 안(오목 꼭짓점 이등분선 안)인 자리 — 거기서 단면을 밀면 그 단의 모든 거리에서 같은 단면(S216 (k) 4 m: 꼭짓점 곁 s*의 소단 끝 줄이 이등분선을 넘어 옛 높이로 떨어졌다)
                        double bestA = double.NegativeInfinity, bestB = double.NegativeInfinity, sA = double.NaN, sB2 = double.NaN;
                        int n = Math.Max(2, (int)Math.Ceiling((st.S1 - st.S0) / 0.25));
                        bool In(double s1, double d0) { var (px, py) = RowPt(s1, d0); double ss = SOn(px, py).S; return ss >= st.S0 - 0.05 && ss <= st.S1 + 0.05; }
                        for (int i = 0; i <= n; i++)
                        {
                            double s1 = st.S0 + (st.S1 - st.S0) * i / n;
                            if (!In(s1, dRef)) continue;
                            var (px, py) = RowPt(s1, dRef);
                            double z = ZL0(kq, px, py); if (double.IsNaN(z)) continue;
                            if (z > bestB) { bestB = z; sB2 = s1; }
                            if (In(s1, dChk) && z > bestA) { bestA = z; sA = s1; }
                        }
                        sb = !double.IsNaN(sA) ? sA : sB2;
                        sStar[(q, kq)] = sb;
                    }
                    if (!double.IsNaN(sb))
                    {
                        var (px, py) = RowPt(sb, lpx.Near(x, y).D);
                        double ss = SOn(px, py).S;
                        if (ss >= st.S0 - 0.05 && ss <= st.S1 + 0.05) { double z = ZL0(kq, px, py); if (!double.IsNaN(z)) return z; }
                    }
                    R.StepFallback++;
                    return ZL0(kq, x, y);
                };
            }
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
            // ★[v114.3 · 계획 2판 2절 «줄 촘촘히» M2] 옛 소단 방식이면 줄에 높이를 주기 전에 사면 코드와 같은 간격으로 점을 채운다(버퍼 변은 끝점만 — 점마다 높이가 다르다).
            //   끝점(Shorten · Bis가 만든 점)은 그대로 · 채운 점은 끝점에서 네모 0.1 mm(Civil 합침 잣대) 안이면 버린다 · 같은 목록을 고쳐 쓴다(끝 선 꼬리표가 목록을 가리킨다)
            //   ★[v114.3 · 0단계 실측 — 임의 선 20개 중 3개 관문 거절(소단 차 7~16 cm)] 옛 소단 높이는 링 Z가 RelaxRingZ 쐐기 끝 · 경계 Z 꺾임에서 꺾여(0 → 0.69)
            //   1 m 점 사이 1차로는 못 따라간다 — 토막 가운데 높이가 두 끝 1차와 1 mm 넘게 다르면 반씩 나눈다(2 cm까지 · 꺾임을 2 cm 안에 가둠)
            List<List<Coordinate>> Dn(List<List<Coordinate>> pcs, int kl)
            {
                if (ob == null) return pcs;
                double dd = Math.Clamp(inp.RowDens, 0.3, 1.0);
                bool Far(Coordinate q, Coordinate a, Coordinate b) => Math.Max(Math.Abs(q.X - a.X), Math.Abs(q.Y - a.Y)) >= CivilSafeMesh.Gap && Math.Max(Math.Abs(q.X - b.X), Math.Abs(q.Y - b.Y)) >= CivilSafeMesh.Gap;
                void Refine(Coordinate a, double za, Coordinate b, double zb, List<Coordinate> o, int depth)
                {
                    if (depth >= 8 || a.Distance(b) < 0.02) return;
                    var m = new Coordinate((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    double zm = ZL(kl, m.X, m.Y);
                    if (Math.Abs(zm - (za + zb) / 2) <= 1e-3 || !Far(m, a, b)) return;
                    Refine(a, za, m, zm, o, depth + 1); o.Add(m); Refine(m, zm, b, zb, o, depth + 1);
                }
                foreach (var pc in pcs)
                {
                    var o = new List<Coordinate> { pc[0] };
                    for (int i = 1; i < pc.Count; i++)
                    {
                        var a = pc[i - 1]; var b = pc[i];
                        int nn = (int)Math.Ceiling(a.Distance(b) / dd);
                        var seg = new List<Coordinate> { a };
                        for (int u = 1; u < nn; u++)
                        {
                            var q = new Coordinate(a.X + (b.X - a.X) * u / nn, a.Y + (b.Y - a.Y) * u / nn);
                            if (Far(q, a, b)) seg.Add(q);
                        }
                        seg.Add(b);
                        for (int j = 1; j < seg.Count; j++)
                        {
                            Refine(seg[j - 1], ZL(kl, seg[j - 1].X, seg[j - 1].Y), seg[j], ZL(kl, seg[j].X, seg[j].Y), o, 0);
                            o.Add(seg[j]);
                        }
                    }
                    // ★[v114.3 · S216 (a) (f) 계측 — 0.1 mm 안 점 쌍 1~2(선기준 0)] 줄 끝에 0.02 µm 겹친 두 점(Bis 끝 + 버퍼 꼭짓점)이 남으면 자리 자르기가 0.1 µm 안 이웃 점 가운데
                    //   앞 점을 남겨, 끝점을 쓰는 끝 선 점과 갈렸다 — 그 잣대(0.1 µm) 안 이웃 점은 여기서 하나로 · 끝점은 끝점 그대로(끝 선 꼬리표가 그 점을 쓴다)
                    for (int j = o.Count - 1; j >= 1; j--)
                        if (Math.Abs(o[j].X - o[j - 1].X) < 1e-7 && Math.Abs(o[j].Y - o[j - 1].Y) < 1e-7) { if (j == o.Count - 1) o[j - 1] = o[j]; o.RemoveAt(j); }
                    pc.Clear(); pc.AddRange(o);
                }
                return pcs;
            }
            // 줄 높이 kl(그 단 · 점마다)보다 땅이 높은 자리(절토 꼴)
            bool Buried(List<List<Coordinate>> pcs, int kl)
            {
                foreach (var pc in pcs)
                    for (int q = 0; q < pc.Count; q++)
                    {
                        var c = pc[q];
                        double gn = Gn(c.X, c.Y, sg); if (!double.IsNaN(gn) && gn > ZL(kl, c.X, c.Y) + 1e-6) return true;
                        if (q + 1 >= pc.Count) continue;
                        double sl = c.Distance(pc[q + 1]); int nn = (int)Math.Ceiling(sl / 0.5);
                        for (int u = 1; u < nn; u++)
                        {
                            double x = c.X + (pc[q + 1].X - c.X) * u / nn, y = c.Y + (pc[q + 1].Y - c.Y) * u / nn;
                            double g2 = Gn(x, y, sg); if (!double.IsNaN(g2) && g2 > ZL(kl, x, y) + 1e-6) return true;
                        }
                    }
                return false;
            }
            // ★[v114.3 · 계획 2판 2절 «단마다 면 폭»] 줄기 첫 단 J의 t번 단 면 머리 거리 = Σ(f_k + T_k) + f — 선기준은 옛 식 그대로((t−1)(f+T) + f)
            double DF(int J, int t)
            {
                if (ob == null) return (t - 1) * (f + T) + f;
                double d = 0;
                for (int i = 1; i < t; i++) d += Fk(J + i - 1) + Tk(J + i - 1);
                return d + Fk(J + t - 1);
            }
            // 단 수(줄 멈춤 · 계획 L10) — 그 단 소단(소단 0이면 면 끝 + 0.5 m)이 땅 속(절토 꼴 G′ > 높이)인 자리가 있으면 한 단 더
            //   ★[v114.3] 줄마다 높이 하나(Z)가 아니라 단 번호(Kl) — 높이는 점마다 ZL(Kl, 점)
            var tierRows = new List<List<(double D, int Kl, List<List<Coordinate>> Pc)>>();
            var dEnd = new double[nSt];
            bool reachHit = false;
            for (int k = 0; k < nSt; k++)
            {
                curStem = k;
                var st = stems[k];
                var rows = new List<(double D, int Kl, List<List<Coordinate>> Pc)>();
                // ★[v115 · S218 (f) 계측] 소단 가운데 줄을 0.5 m 간격으로 — 가운데 한 줄(T/2)만이면 넓은 소단(T 3 m)에서 줄 사이가 1.5 m라 비틀린 옛 소단(도로 · 굽은 곳 · 오목 꼭짓점 곁)을
                //   세모가 못 따라가 소단 관문이 1~2 cm로 거절했다(곧은 다리 셋 · 한 줄로 1.1~2 cm). 소단 1 m 이하는 나눔 2(가운데 한 줄 — 옛 판과 비트 같음)
                void MidRows(List<(double D, int Kl, List<List<Coordinate>> Pc)> rr, double d0, double d1, int kq)
                {
                    int m = Math.Max(2, (int)Math.Ceiling((d1 - d0) / 0.5 - 1e-9));
                    for (int q = 1; q < m; q++) { double dq = m == 2 ? (d0 + d1) / 2 : d0 + (d1 - d0) * q / m; rr.Add((dq, kq, Dn(RowAt(k, dq), kq))); }
                }
                int n = 0; double lastF = DF(st.J, 1); int lastK = st.J;
                for (int t = 1; t <= 200; t++)
                {
                    int kl = st.J + t - 1;
                    double dF = DF(st.J, t), Tt = Tk(kl);
                    if (dF > reachLim) { reachHit = true; break; }
                    rows.Add((dF, kl, Dn(RowAt(k, dF), kl)));
                    n = t; lastF = dF; lastK = kl;
                    double dS = dF + Math.Max(Tt, 0.5);
                    bool more = Buried(RowAt(k, dS), kl) || (Tt > 1e-6 && (Buried(RowAt(k, dF + Tt / 2), kl) || Buried(RowAt(k, dF + Tt), kl)));
                    if (!more) break;
                    if (t == 200) reachHit = true;
                    // ★[v114.3 · JACK 1105 진입도로 옹벽 추가 · S218 (b) 실측] 소단 가운데 줄 — 도로 옛 소단은 종단 · 굽은 곳을 따라 비틀린 면이라 소단 폭(1 m) 세모 한 장이 가운데에서 1 cm 넘게 벗어났다(소단 관문 거절) · 부지 링(평면에 가까움)은 안 넣음(비트 같음)
                    if (Tt >= 0.2 && ob != null && ob.MidBench) MidRows(rows, dF, dF + Tt, kl);
                    if (Tt > 1e-6) rows.Add((dF + Tt, kl, Dn(RowAt(k, dF + Tt), kl)));
                }
                st.Tiers = Math.Max(1, n);
                dEnd[k] = lastF + Math.Max(Tk(lastK), 0.5);
                if (ob != null && ob.MidBench && dEnd[k] - lastF >= 0.2) MidRows(rows, lastF, dEnd[k], lastK);
                rows.Add((dEnd[k], lastK, Dn(RowAt(k, dEnd[k]), lastK)));
                tierRows.Add(rows);
                part.Dmax = Math.Max(part.Dmax, dEnd[k]);
            }
            double dMax = part.Dmax;
            if (reachHit) return No("도달", string.Format(Ci, "부분 {0}의 벽이 선에서 {1:F1} m(한도) 안에서 원지반에 못 닿습니다 — 선을 옮기거나 단높이 · 소단을 바꾸세요", partNo, reachLim), Wd(pl.At((part.S0 + part.S1) / 2)));
            // ★★[v114.3 · 계획 2판 1절 «불변식» · 재검토 M-B] 옛 소단 방식 — 짓기 전에 높이가 이어지는가(못 이어지면 거절 · 까닭 한 줄):
            //   ① 줄 안 이웃 점: |ΔZ| ≤ maxGrad·Δs + 1 cm(평평한 바닥이면 maxGrad = 0) — 링 보간이 접힘 · 이등분선에서 뛰면 여기서 막는다
            //   ② 줄 사이(면): 위 줄 점마다 아래 줄(앞 단 소단 끝)의 가장 가까운 점과 ε < h ≤ H_k + maxGrad·f_k + 1 cm · 첫 면(L → 1줄)도 같은 잣대(아래 = L 위 P0)
            //   ③ 기울기 기록(⚠ 5 % · 막지 않음) — 줄 방향 · 바깥(같은 단 면 머리 → 소단 끝)
            if (ob != null)
            {
                const double Eps = 1e-4;
                double mg = ob.MaxGrad;
                static (double Z, double D) NearZ(List<Coordinate> pc, Func<double, double, double> z, double x, double y)
                {
                    double bd = double.MaxValue, bt = 0; int bi = -1;
                    for (int i = 0; i + 1 < pc.Count; i++)
                    {
                        double ex = pc[i + 1].X - pc[i].X, ey = pc[i + 1].Y - pc[i].Y, l2 = ex * ex + ey * ey;
                        double t = l2 < 1e-24 ? 0 : Math.Clamp(((x - pc[i].X) * ex + (y - pc[i].Y) * ey) / l2, 0, 1);
                        double dx = x - pc[i].X - ex * t, dy = y - pc[i].Y - ey * t, d2 = dx * dx + dy * dy;
                        if (d2 < bd) { bd = d2; bi = i; bt = t; }
                    }
                    if (bi < 0) return (double.NaN, double.MaxValue);
                    double za = z(pc[bi].X, pc[bi].Y), zb = z(pc[bi + 1].X, pc[bi + 1].Y);
                    return (za + (zb - za) * bt, Math.Sqrt(bd));
                }
                for (int k = 0; k < nSt; k++)
                {
                    curStem = k;
                    var rr = tierRows[k];
                    // ★[v116.3 설치 전 검토 M1] 계단이면 지은 높이(ZL)도 잰다 — 줄마다 수평(1 cm) · 첫 면 h > 0(오목 꼭짓점 곁 옛 높이로 떨어진 점 · 이등분선 안 최고 단면이 옛 높이보다 낮은 자리)
                    if (stepOn)
                        foreach (var (_, kl, pcs) in rr)
                            foreach (var pc in pcs)
                            {
                                if (pc.Count < 2) continue;
                                double zmin = double.MaxValue, zmax = double.MinValue;
                                foreach (var c in pc) { double z = ZL(kl, c.X, c.Y); zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z); }
                                R.StepRowMax = Math.Max(R.StepRowMax, zmax - zmin);
                                if (zmax - zmin > 0.01) { if (R.StepRowBad == 0) R.FaceHAt = Wd(pc[0]); R.StepRowBad++; }
                            }
                    foreach (var (_, kl, pcs) in rr)
                        foreach (var pc in pcs)
                            for (int i = 0; i + 1 < pc.Count; i++)
                            {
                                double dsl = pc[i].Distance(pc[i + 1]), za = ZL0(kl, pc[i].X, pc[i].Y), zb = ZL0(kl, pc[i + 1].X, pc[i + 1].Y), dz = Math.Abs(zb - za);   // ★[v116.3 계획 2판 H2] 입력 관문 = 옛 높이
                                R.JumpN++;
                                // ★[v114.3 · JACK 1105 진입도로 옹벽 추가] 한도는 단 표가 정한다(부지 = |Δz| − maxGrad·거리 옛 그대로 · 도로 = 다른 다리 · 광선 안 건넘이면 NaN)
                                double over = ob.JumpOver(kl, pc[i].X + ox, pc[i].Y + oy, sg * za, pc[i + 1].X + ox, pc[i + 1].Y + oy, sg * zb, dsl);
                                if (double.IsNaN(over))
                                {
                                    if (R.JumpLeg++ == 0) { R.JumpLegAt = Wd(pc[i]); R.JumpLegWhy = ob.JumpWhy; }
                                    R.JumpBad++;
                                    continue;
                                }
                                if (over > R.JumpMax) { R.JumpMax = over; R.JumpAt = Wd(pc[i]); }
                                if (over > 0.01) R.JumpBad++;
                                if (dsl >= 0.1) R.TiltAlong = Math.Max(R.TiltAlong, dz / dsl);
                            }
                    // 첫 면 — 1줄 점마다 이 줄기 L 토막의 가장 가까운 점(P0)
                    var lSub = pl.Sub(stems[k].S0, stems[k].S1).ToList();
                    var lPl = new PL(lSub.ToArray());
                    double p0L(double x, double y) { var nr = lPl.Near(x, y); return sg * P0At(stems[k].S0 + nr.S); }
                    void Face(List<List<Coordinate>> hiP, int kHi, Func<double, double, (double Z, double D)> lo)
                    {
                        double lim = Hk(kHi) + mg * Fk(kHi) + 0.01;
                        foreach (var pc in hiP)
                            foreach (var c in pc)
                            {
                                var (zl, _) = lo(c.X, c.Y);
                                if (double.IsNaN(zl)) continue;
                                double h = ZL0(kHi, c.X, c.Y) - zl;   // ★[v116.3 계획 2판 H2] 입력 관문 = 옛 높이(계단 면은 h + Δ ≥ h)
                                R.FaceHN++;
                                R.FaceHMin = Math.Min(R.FaceHMin, h); R.FaceHMax = Math.Max(R.FaceHMax, h);
                                if (!(h > Eps && h <= lim)) { if (R.FaceHBad == 0) R.FaceHAt = Wd(c); R.FaceHBad++; }
                            }
                    }
                    if (rr.Count > 0) Face(rr[0].Pc, rr[0].Kl, (x, y) => (p0L(x, y), 0));
                    if (stepOn && rr.Count > 0)
                        foreach (var pc in rr[0].Pc)
                            foreach (var c in pc)
                            {
                                double hs = ZL(rr[0].Kl, c.X, c.Y) - p0L(c.X, c.Y);
                                if (!(hs > Eps)) { if (R.StepFaceBad == 0) R.FaceHAt = Wd(c); R.StepFaceBad++; }
                            }
                    for (int i = 0; i + 1 < rr.Count; i++)
                    {
                        var lo = rr[i]; var hi = rr[i + 1];
                        if (hi.Kl == lo.Kl)
                        {
                            // 같은 단 — 면 머리 → 소단 끝(바깥 기울기 · 막지 않음)
                            double w = hi.D - lo.D;
                            if (w < 0.1) continue;
                            foreach (var pc in hi.Pc)
                                foreach (var c in pc)
                                {
                                    (double Z, double D) nb = (double.NaN, double.MaxValue);
                                    foreach (var lp in lo.Pc) { var r0 = NearZ(lp, (x, y) => ZL0(lo.Kl, x, y), c.X, c.Y); if (r0.D < nb.D) nb = r0; }
                                    if (!double.IsNaN(nb.Z) && nb.D < w * 1.5) R.TiltOut = Math.Max(R.TiltOut, Math.Abs(ZL0(hi.Kl, c.X, c.Y) - nb.Z) / nb.D);
                                }
                            continue;
                        }
                        Face(hi.Pc, hi.Kl, (x, y) =>
                        {
                            (double Z, double D) best = (double.NaN, double.MaxValue);
                            foreach (var lp in lo.Pc) { var r = NearZ(lp, (u, v) => ZL0(lo.Kl, u, v), x, y); if (r.D < best.D) best = r; }
                            return best;
                        });
                    }
                }
                if (R.JumpLeg > 0)
                    return No("옛 소단", string.Format(Ci, "옛 소단 높이를 이어 읽지 못함 ({0:F1}, {1:F1}) — {2}(이웃 점 {3}쌍 중 {4}쌍) — 선을 옮기세요",
                        R.JumpLegAt.X, R.JumpLegAt.Y, R.JumpLegWhy.Length > 0 ? R.JumpLegWhy : "이어지지 않는 자리", R.JumpN, R.JumpLeg), R.JumpLegAt);
                if (R.JumpBad > 0)
                    return No("옛 소단", string.Format(Ci, "옛 소단 높이가 이 자리에서 끊김 ({0:F1}, {1:F1}) Δ {2:F2} m(한도 maxGrad·거리 + 0.01 · 이웃 점 {3}쌍 중 {4}쌍) — 선을 옮기세요(링 이등분선 · 접힘 곁)",
                        R.JumpAt.X, R.JumpAt.Y, R.JumpMax + 0.01, R.JumpN, R.JumpBad), R.JumpAt);
                if (R.FaceHBad > 0)
                    return No("옛 소단", string.Format(Ci, "옛 소단 사이 벽 면 높이가 맞지 않음 ({0:F1}, {1:F1}) h {2:F3}~{3:F3} m(0 < h ≤ 단 간격 + maxGrad·면 폭 + 1 cm · {4}점 중 {5}점) — 선을 옮기세요",
                        R.FaceHAt.X, R.FaceHAt.Y, R.FaceHMin, R.FaceHMax, R.FaceHN, R.FaceHBad), R.FaceHAt);
                if (R.StepFaceBad > 0 || R.StepRowBad > 0)
                    return No("계단", string.Format(Ci, "계단 천단을 이 선에 못 맞춤 ({0:F1}, {1:F1}) — {2}{3} — 블록 길이를 바꾸거나 천단 경사로 하세요",
                        R.FaceHAt.X, R.FaceHAt.Y, R.StepFaceBad > 0 ? $"첫 벽 면 높이가 0 이하인 점 {R.StepFaceBad}" : "",
                        R.StepRowBad > 0 ? string.Format(Ci, "{0}수평이 아닌 줄 {1}(최대 {2:F2} m · 오목 꼭짓점 곁)", R.StepFaceBad > 0 ? " · " : "", R.StepRowBad, R.StepRowMax) : ""), R.FaceHAt);
            }
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
                curStem = k;
                foreach (var (dd, kl, pcs) in tierRows[k])
                    foreach (var pc in pcs) { var l = pc.Select(c => new Point3(c.X + ox, c.Y + oy, sg * ZL(kl, c.X, c.Y))).ToList(); raw.Add(l); rowSet.Add(l); R.RawRows.Add(l); }
                var (a, b) = Shr(k);
                foreach (var (se, atStart) in new[] { (a, true), (b, false) })
                {
                    // 끝 선 E — 그쪽 경계에서 끝난 줄 조각 끝들(법선 위 · 오목 꼭짓점 곁은 이등분선 곁) · L에서 먼 차례
                    int want = atStart ? -1 : 1;
                    var ends = new List<(double D, Point3 P)>();
                    foreach (var (dd, kl, pcs) in tierRows[k])
                        foreach (var pc in pcs)
                        {
                            if (!tags.TryGetValue(pc, out var tg)) continue;
                            if (tg.A == want) ends.Add((dd, new Point3(pc[0].X + ox, pc[0].Y + oy, sg * ZL(kl, pc[0].X, pc[0].Y))));
                            if (tg.B == want) ends.Add((dd, new Point3(pc[^1].X + ox, pc[^1].Y + oy, sg * ZL(kl, pc[^1].X, pc[^1].Y))));
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
                            double cx = p0e.X + nx0 * dc, cy = p0e.Y + ny0 * dc;
                            double RZ(int q) => ZL(rr[q].Kl, cx, cy);   // ★[v114.3] 줄 높이는 그 자리에서
                            if (dc <= rr[0].D) zN = p0N + (RZ(0) - p0N) * dc / rr[0].D;
                            else { zN = RZ(rr.Count - 1); for (int q = 0; q + 1 < rr.Count; q++) if (dc >= rr[q].D && dc <= rr[q + 1].D) { zN = RZ(q) + (RZ(q + 1) - RZ(q)) * (dc - rr[q].D) / Math.Max(1e-12, rr[q + 1].D - rr[q].D); break; } }
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
                var oP = RowIn(dMax + vE, part.S0 - (cA ? 0 : vE), part.S1 + (cB ? 0 : vE), 0, 0);
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
                var gb = NtsSupport.LargestPolygon(gf.CreateGeometryCollection(gl).Buffer(vE * 1.5, new BufferParameters { QuadrantSegments = 4 }));
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
            // ★[v115 · S218 (g) 계측] 조각을 먼저 1 nm 격자로 내린 뒤 합친다 — 날 조각(격자 밖)을 차례 합치기(캐스케이드)에 그대로 넣으면 중간 결과만 격자에 올라
            //   다음 짝 합치기의 스냅 반올림이 L 위 한 꼭짓점을 12 nm 떨어진 두 점으로 갈랐다(입력 6조각은 그 점을 비트 같게 나눔 · 결과 고리 L3a → X → L3b).
            //   그 틈(나비 12 nm · 길이 28 cm)이 자리 고리에 «L → 원지반 높이 점 → L» 가시로 남아 벽 면 2 cm 안에 G 높이 점을 꽂았다(발 관문 184 cm 거절).
            //   먼저 내리면 모든 입력이 한 격자 위라 반올림이 꼭짓점을 새로 만들지 않는다(같은 1e9 격자 · 잣대는 그대로)
            var pm9 = new PrecisionModel(1e9);
            var pieces9 = new List<Geometry>(pieces.Count);
            try { foreach (var pc in pieces) { var r9 = NetTopologySuite.Precision.GeometryPrecisionReducer.Reduce(pc, pm9); if (!r9.IsEmpty) pieces9.Add(r9); } }
            catch (Exception ex) { return No("자리", $"부분 {partNo}의 바꿀 자리 조각을 격자에 못 올렸습니다({ex.GetType().Name})"); }
            try { zu = pieces9.Count == 0 ? gf.CreatePolygon() : NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union(gf.BuildGeometry(pieces9), pm9); }
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
                    curStem = -1;
                    foreach (var (dd, kl, pcs) in oRows)
                        foreach (var pc in pcs)
                            foreach (var c in pc)
                                if (prep.Contains(gf.CreatePoint(c)) && Gn(c.X, c.Y, sg) - ZL(kl, c.X, c.Y) > 1e-2) { R.EdgeHit++; if (R.Marks.Count == 0) R.Marks.Add(Wd(c)); }
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
                void Take(double x, double y, int kl)
                {
                    double gn = Gn(x, y, sg), lev = ZL(kl, x, y);   // ★[v114.3] 소단 높이는 그 자리에서(옛 소단 — 바닥이 기울면 따라 기욺)
                    if (double.IsNaN(gn) || gn - lev <= 1e-2) return;
                    var pt = gf.CreatePoint(new Coordinate(x, y));
                    if (!zShell.Any(z => z.Contains(pt))) return;
                    R.BenchPts.Add((x + ox, y + oy, sg * lev, 0)); benchSrc.Add(rawByPart.Count - 1);
                }
                for (int k = 0; k < nSt; k++)
                    for (int t = 1; t <= stems[k].Tiers; t++)
                    {
                        curStem = k;
                        int lev = stems[k].J + t - 1;
                        double dF = DF(stems[k].J, t);
                        double w0 = dF + vE, w1 = (t == stems[k].Tiers ? dEnd[k] : dF + Tk(lev)) - vE;
                        if (w1 - w0 < 1e-6) continue;
                        double mid = (w0 + w1) / 2; int nh = (int)Math.Floor((w1 - w0) / 2 / 0.5 + 1e-9);
                        for (int h = -nh; h <= nh; h++)
                            foreach (var pc in RowIn(mid + h * 0.5, stems[k].S0, stems[k].S1, shS(k) + vE, shE(k) + vE))
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
                    // ★[v114.1 · 검토 낮음 (b)] 건넘은 이미 있는 모든 고리 점(칸 해시)과 네모 Gap 넘게 떨어질 때만 — 앞 · 뒤 점만 보면 곁 토막의 점과 쌍이 될 수 있다
                    var cellD = new Dictionary<(long, long), List<Point3>>();
                    (long, long) CK(Point3 u) => ((long)Math.Floor((u.X - ox) / CivilSafeMesh.Gap), (long)Math.Floor((u.Y - oy) / CivilSafeMesh.Gap));
                    void PutD(Point3 u) { var k = CK(u); if (!cellD.TryGetValue(k, out var l)) cellD[k] = l = new(); l.Add(u); }
                    bool FarAll(Point3 u)
                    {
                        var (kx, ky) = CK(u);
                        for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++)
                                if (cellD.TryGetValue((kx + a, ky + b), out var l) && l.Any(v => Math.Max(Math.Abs(u.X - v.X), Math.Abs(u.Y - v.Y)) < CivilSafeMesh.Gap)) return false;
                        return true;
                    }
                    foreach (var u in clean) PutD(u);
                    for (int i = 0; i < clean.Count; i++)
                    {
                        var p = clean[i]; var q = clean[(i + 1) % clean.Count];
                        dn.Add(p);
                        bool pL = pl.Near(p.X - ox, p.Y - oy).D < 1e-9, qL = pl.Near(q.X - ox, q.Y - oy).D < 1e-9;
                        if (pL && qL) continue;
                        var pr = ProfileOnTris(p, q, inp.GroundTris, inp.Ground, 1e9);
                        // ★[v114.1 · 현장 0.1 mm 안 점 쌍 계측] 고리 꼭짓점이 이미 원지반 변 위(ZonePieces가 변에서 자름 · 1 nm 격자 합침)면 같은 건넘을 0.1~0.5 nm 곁에 또 넣었고
                        //   (현장 성토 8쌍 · 절토 2쌍), 1 mm로 올린 발 점 71 µm 곁 건넘은 쌍 + 바늘(높이 73 µm)이 됐다 — Civil과 같은 네모 잣대로 앞 · 뒤 점 곁 건넘은 안 넣는다(높이 차 ≤ 0.1 mm × 땅 기울기 꺾임)
                        if (pr != null) for (int k = 1; k + 1 < pr.Count; k++) if (FarAll(pr[k])) { dn.Add(pr[k]); PutD(pr[k]); }
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
                            double wx = c.X + ox, wy = c.Y + oy, z = isRow && ob == null ? l[0].Z : ZAlong(l, wx, wy);   // ★[v114.3] 옛 소단 줄은 점마다 높이 — 자른 끝은 그 토막에서 1차
                            // 자른 끝이 고리 꼭짓점과 1µm 안이면 그 꼭짓점으로(교차 계산 부스러기 — 0.1 mm 안 점 쌍 · 바늘이 되지 않게)
                            var kk = ((long)Math.Floor(c.X / 1e-6), (long)Math.Floor(c.Y / 1e-6));
                            for (long ia = -1; ia <= 1; ia++) for (long ib = -1; ib <= 1; ib++)
                                    if (ringHash.TryGetValue((kk.Item1 + ia, kk.Item2 + ib), out var rv) && Math.Abs(rv.X - wx) <= 1e-6 && Math.Abs(rv.Y - wy) <= 1e-6) { wx = rv.X; wy = rv.Y; }
                            if (pts.Count > 0 && Math.Abs(pts[^1].X - wx) < 1e-7 && Math.Abs(pts[^1].Y - wy) < 1e-7) continue;
                            pts.Add(new Point3(wx, wy, z));
                        }
                        if (pts.Count < 2) continue;
                        if (isRow) R.Rows.Add(pts); else if (inner) R.EndFaces.Add(pts);
                        if (isE) eCut.Add(pts);
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
            if (ob != null)
            {
                // ★[v114.3 · 계획 2판 2절 «옛 소단 이음» M3 · 재검토 LOW-D] 줄기 갈림 S마다 — L 안쪽(남는 사면 쪽)에서 S에 가장 가까운 옛 소단(아래 줄기 첫 단) 띠 점의 P0를
                //   그 점 제 높이의 옛 소단과 견준다(벽 소단은 짓는 법으로 그 단 높이 — 둘 다 제 자리 Level과 견줌). 1 cm 넘으면 JoinMiss · S 곁 1.5 m 안에 띠가 없으면 JoinNone(둘 다 ⚠만 · 기본값).
                //   ★[S216 계측] 처음 판(법선으로 소단 폭 반만큼 한 점)은 L이 옛 소단을 비스듬히 건너면 늘 띠 밖이라 JoinNone만 났다 — S 둘레(0.1~1.5 m · 16 방향)에서 찾는다
                for (int k = 0; k + 1 < nSt; k++)
                {
                    int kLo = Math.Min(stems[k].J, stems[k + 1].J);
                    var cS = pl.At(stems[k + 1].S0);
                    double x = 0, y = 0; bool found = false;
                    foreach (double rr0 in new[] { 0.1, 0.25, 0.5, 1.0, 1.5 })
                    {
                        for (int a = 0; a < 16 && !found; a++)
                        {
                            double ang = a * Math.PI / 8, qx = cS.X + rr0 * Math.Cos(ang), qy = cS.Y + rr0 * Math.Sin(ang);
                            if (cutP.Contains(gf.CreatePoint(new Coordinate(qx, qy))) || kLo > ob.K || !ob.InBand(kLo, qx + ox, qy + oy)) continue;
                            x = qx; y = qy; found = true;
                        }
                        if (found) break;
                    }
                    R.JoinN++;
                    if (!found) { R.JoinNone++; continue; }
                    double? pz = inp.P0(x + ox, y + oy) ?? (inp.P0Tris != null ? ZOnTris(inp.P0Tris, x + ox, y + oy) : null);
                    if (pz == null) { R.JoinNone++; continue; }
                    double d = Math.Abs(pz.Value - ob.Level(kLo, x + ox, y + oy));
                    if (d > R.JoinMax) { R.JoinMax = d; R.JoinAt = Wd(new Coordinate(x, y)); }
                    if (d > 0.01) R.JoinMiss++;
                }
                // 로그 ②-e — 쓴 단마다 벽 소단 높이 폭(줄 위 · 실제 z)
                var lvTxt = new List<string>();
                curStem = -1;
                foreach (int kl in tierRows.SelectMany(r => r.Select(q => q.Kl)).Distinct().OrderBy(q => q))
                {
                    var zs = tierRows.SelectMany(r => r.Where(q => q.Kl == kl)).SelectMany(q => q.Pc).SelectMany(pc => pc).Select(c => sg * ZL(kl, c.X, c.Y)).ToList();
                    if (zs.Count > 0) lvTxt.Add(string.Format(Ci, "단 {0} {1:F2}{2}", kl, zs.Min(), zs.Max() - zs.Min() > 5e-3 ? string.Format(Ci, "~{0:F2}", zs.Max()) : ""));
                }
                log.Append(string.Format(Ci, "  ②-e 부분 {0} 옛 소단(링 · {1}단까지 · maxGrad {2:F3}) {3}\n", partNo, ob.K, ob.MaxGrad, string.Join(" · ", lvTxt)));
            }
            // 면 토막(Faces)
            curStem = -1;
            BuildFaces(R, part, partNo, tierRows, eLines, pl, ox, oy, sg, side, ZL, DF, Fk, Tk, P0At, Gw, gf, (part.Up ? lfC : lfW), k2 => curStem = k2, stepOn);
            if (stepOn && R.StepFallback > 0) R.Warn.Add($"부분 {partNo}: 계단 천단 — 오목 꼭짓점 곁 {R.StepFallback}점은 그 자리 옛 높이(가장 높은 단면이 이등분선을 넘음)");
            curStem = -1;
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
        // ★[v114.1 · 현장 0.1 mm 안 점 쌍 계측] 합치기가 L 위에서 거의 겹친 두 변(자리 고리의 L 토막 · R_out의 L 변)을 서로 끊어 L 위 새 꼭짓점을 지어냈다(현장 절토 38개 ·
        //   그중 하나가 자리 고리 L 점과 네모 95 µm — Civil이 합칠 쌍). L 위 꼭짓점은 L 꼭짓점 · 자리 고리 점만 남긴다(앞 · 뒤도 L 위면 L 위 곧은 토막 안이라 모양 그대로)
        {
            var keepL = new List<Coordinate>(pl.C);
            foreach (var r in lfW.Concat(lfC)) foreach (var q in r) keepL.Add(new Coordinate(q.X - ox, q.Y - oy));
            int m = pr0.Length - 1;
            bool OnL(Coordinate c) => pl.Near(c.X, c.Y).D < 1e-9;
            static Coordinate Mid(Coordinate u, Coordinate v) => new((u.X + v.X) / 2, (u.Y + v.Y) / 2);
            for (int i = 0; i < m; i++)
            {
                var c = pr0[i]; var pv = pr0[(i - 1 + m) % m]; var nx = pr0[(i + 1) % m];
                // ★[v114.1 · 검토 낮음 (c)] 앞 · 뒤 변 가운데도 L 위여야(두 변이 L을 따라갈 때만 — 이웃 점만 L 위이고 변이 L을 벗어나는 꼴은 남긴다)
                if (OnL(c) && OnL(pv) && OnL(nx) && OnL(Mid(pv, c)) && OnL(Mid(c, nx)) && keepL.All(k => Math.Abs(k.X - c.X) > 1e-8 || Math.Abs(k.Y - c.Y) > 1e-8)) continue;
                R.Poly.Add(new Point3(c.X + ox, c.Y + oy, 0));
            }
        }
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
            // ★[v115 · S218 (f) 계측] 마구리 관문 — 끝 선 E(자리로 자른 몫)가 벽 면에 들어갔나(선 위 0.25 m마다 · 벽 면 높이 = 선 높이 1 cm).
            //   U자 선 안쪽에서 양쪽 다리의 줄기 갈림 법선이 가운데 이등분선에 서로 다른 자리(S218 (f) 꼴 21 cm 떨어짐)로 닿으면, 그 사이 이등분선에서는
            //   단이 다른 두 줄기(j1 · j2 — 5 m 차)가 마주 보는데 줄 끝이 하나도 없어 두 끝 선 E가 모퉁이 점(dc)에서 아무 선과도 안 이어진 채 끝난다.
            //   벽 세모 잇기(MeshLines — 고리만 제약)는 이어지지 않은 선(꼬리)을 버리므로 마구리가 사라지고 세모가 두 단 사이 5 m를 비스듬히 건넜다(소단 관문 490 cm).
            //   그 자리에 세울 «세 줄기가 만나는 마구리» 꼴은 지금 짓는 법에 없다(새 기하 — 모양을 정한 뒤 짓기) → 까닭을 밝혀 거절한다(소단 관문보다 먼저 · 같은 1 cm 잣대).
            //   잰다 = 끝 선 표본이 벽 세모의 <b>변 위</b>(선이 제약으로 들어감 — 그 끝이 자리 고리에 닿아 고리 높이가 이기는 몫도 여기)면 통과 · 세모 <b>안</b>을 지나면(제약이 빠짐) 선 높이와 면 높이를 견준다
            {
                var wtAll = R.WallTrisF.Concat(R.WallTrisC).ToList();
                var wtTree = new STRtree<int>();
                for (int i = 0; i < wtAll.Count; i++) { var t = wtAll[i]; wtTree.Insert(new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)), Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))), i); }
                if (wtAll.Count > 0) wtTree.Build();
                static double SegD(double px, double py, Point3 a, Point3 b)
                {
                    double ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                    double t = l2 < 1e-24 ? 0 : Math.Clamp(((px - a.X) * ex + (py - a.Y) * ey) / l2, 0, 1);
                    double dx = px - a.X - ex * t, dy = py - a.Y - ey * t; return Math.Sqrt(dx * dx + dy * dy);
                }
                foreach (var el in eCut)
                    for (int i = 0; i + 1 < el.Count && wtAll.Count > 0; i++)
                    {
                        var a = el[i]; var e = el[i + 1];
                        int nn = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((e.X - a.X) * (e.X - a.X) + (e.Y - a.Y) * (e.Y - a.Y)) / 0.25));
                        for (int u = i == 0 ? 0 : 1; u <= nn; u++)
                        {
                            double t = (double)u / nn, x = a.X + (e.X - a.X) * t, y = a.Y + (e.Y - a.Y) * t, zl = a.Z + (e.Z - a.Z) * t;
                            double? zw = null; bool onEdge = false;
                            foreach (int ti in wtTree.Query(new Envelope(x, x, y, y)))
                            {
                                var tr = wtAll[ti];
                                double d = (tr.B.Y - tr.C.Y) * (tr.A.X - tr.C.X) + (tr.C.X - tr.B.X) * (tr.A.Y - tr.C.Y);
                                if (Math.Abs(d) < 1e-18) continue;
                                double l1 = ((tr.B.Y - tr.C.Y) * (x - tr.C.X) + (tr.C.X - tr.B.X) * (y - tr.C.Y)) / d, l2 = ((tr.C.Y - tr.A.Y) * (x - tr.C.X) + (tr.A.X - tr.C.X) * (y - tr.C.Y)) / d, l3 = 1 - l1 - l2;
                                if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
                                if (Math.Min(SegD(x, y, tr.A, tr.B), Math.Min(SegD(x, y, tr.B, tr.C), SegD(x, y, tr.C, tr.A))) < 1e-6) { onEdge = true; break; }
                                zw ??= l1 * tr.A.Z + l2 * tr.B.Z + l3 * tr.C.Z;
                            }
                            if (onEdge || zw == null) continue;   // 변 위 = 제약으로 들어감 · 면 없음 = 자리 테두리 위 수치 부스러기(소단 관문의 F 못 읽음과 같은 뜻)
                            R.EndN++;
                            double dz = Math.Abs(zw.Value - zl);
                            if (dz > 1e-2) R.EndMiss++;
                            if (dz > R.EndMax) { R.EndMax = dz; R.EndAt = (x, y); }
                        }
                    }
                log.Append(string.Format(Ci, "  ④-d 마구리 관문 — 끝 선 {0}개 · 표본 {1}점(벽 면이 선을 안 따름 {2}) · 최대 차 {3:F1} mm @({4:F2},{5:F2})\n",
                    eCut.Count, R.EndN, R.EndMiss, R.EndMax * 1000, R.EndAt.X, R.EndAt.Y));
                if (R.EndMiss > 0 && !BenchGateOff)
                    return No("마구리", string.Format(Ci, "벽 면이 끝 선(마구리)을 안 따름({0:F1}, {1:F1} · 차 {2:F0} cm) — 대개 오목한(U자) 선 안쪽에서 단이 다른 두 벽이 만나는 자리 · 소단 폭을 줄이거나 선 안쪽을 넓히세요",
                        R.EndAt.X, R.EndAt.Y, R.EndMax * 100), R.EndAt);
            }
            if (R.BenchSag + R.BenchMiss + R.FootBad > 0 && !BenchGateOff)
                return No("소단 높이", R.BenchSag + R.FootBad == 0
                    ? string.Format(Ci, "옹벽 소단에 벽 면이 없음({0:F1}, {1:F1} · {2}점) — 소단 폭을 줄이거나 선을 펴세요", R.BenchAt.X, R.BenchAt.Y, R.BenchMiss)
                    : string.Format(Ci, "옹벽 {0} 높이가 맞지 않음({1:F1}, {2:F1} · 차 {3:F0} cm) — 소단 폭을 줄이거나 선을 펴세요",
                        atKind == 0 ? "소단" : "발(F)", R.BenchAt.X, R.BenchAt.Y, R.BenchMax * 100), R.BenchAt);
        }
        R.Ok = true; R.Code = "통과";
        log.Append(string.Format(Ci, "  ③ 바꿀 자리 성토 {0} · 절토 {1} · Poly {2}점 · 선 {3} · 벽 세모 성토 {4} · 절토 {5} · 마구리 {6} · 부분 끝 마구리 {7} · 선 바깥 최대 {8:F3} m @({9:F1},{10:F1})\n",
            lfW.Count, lfC.Count, R.Poly.Count, R.Lines.Count, R.WallTrisF.Count, R.WallTrisC.Count, R.EndFaceN, R.EndCliff, R.FootMax, R.FootAt.X, R.FootAt.Y));
        log.Append($"  ④ 자가검증 — 발 폭 1 mm로 올림 {R.WMinBumped} · L에 합친 점 {R.MergedPts} · 0.1 mm 안 점 쌍 {R.PairsUnder01}(0이어야) · 바늘 세모 {R.Needles}(0이어야)"
            + $" · 마구리 폭 {R.VEps:0.###} m · 좁힌 마구리 {R.CapNarrow}{(R.CapNarrow > 0 ? $"(가장 가파름 {R.CapSteep:F0} : 1)" : "")} · 면 폭보다 짧은 선 토막 {R.PieceShort}\n");
        if (inp.LevelMode == LevelKind.OldBench)
        {
            // ★[v114.3 · 계획 2판 2절 «로그 ②» · «기울기 기록» M6] 옛 소단 — 규칙 · 끊김 · 면 높이 · 이음 · 기울기(5 % 넘으면 ⚠만 — JACK 후속 확인)
            log.Append(string.Format(Ci, "  ④-c 옛 소단 — 띠 안 L 표본 {0}(차 최대 {1:F1} mm) · 줄 끊김 {2}/{3}(maxGrad 넘음 최대 {4:F1} mm) · 면 높이 {5:F3}~{6:F3} m({7}점 · 어긋남 {8})"
                + " · 이음 S {9}(어긋남 {10} · 옛 소단 없음 {11} · 최대 {12:F1} mm) · 벽 소단 기울기 줄 방향 {13:F1} % · 바깥 {14:F1} %\n",
                R.RuleN, R.RuleMax * 1000, R.JumpBad, R.JumpN, Math.Max(0, R.JumpMax) * 1000, R.FaceHN > 0 ? R.FaceHMin : 0, R.FaceHMax, R.FaceHN, R.FaceHBad,
                R.JoinN, R.JoinMiss, R.JoinNone, R.JoinMax * 1000, R.TiltAlong * 100, R.TiltOut * 100));
            if (obC0 != null || obF0 != null)
            {
                long dc = (obC0?.Calls ?? 0) + (obF0?.Calls ?? 0) - lvCalls0, dt = (obC0?.Ticks ?? 0) + (obF0?.Ticks ?? 0) - lvTicks0;
                log.Append(string.Format(Ci, "  ④-e 옛 소단 높이 읽기(링) {0}번 · {1:F0} ms\n", dc, dt * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
            }
            if (R.TiltAlong > 0.05 || R.TiltOut > 0.05) R.Warn.Add(string.Format(Ci, "벽 소단이 옛 소단을 따라 기욺(줄 방향 {0:F1} % · 바깥 {1:F1} % — 5 % 넘음)", R.TiltAlong * 100, R.TiltOut * 100));
            if (R.JoinMiss > 0) R.Warn.Add(string.Format(Ci, "옛 소단 이음 어긋남 {0}곳(1 cm 넘음 · 최대 {1:F1} mm @({2:F1}, {3:F1}))", R.JoinMiss, R.JoinMax * 1000, R.JoinAt.X, R.JoinAt.Y));
            if (R.JoinNone > 0) R.Warn.Add($"줄기 갈림 {R.JoinNone}곳 곁에 옛 소단이 없음(이음을 못 잼)");
        }
        if (R.PairsUnder01 > 0) R.Warn.Add($"0.1 mm 안 점 쌍 {R.PairsUnder01}개(Civil이 합친다)");
        if (R.Needles > 0) R.Warn.Add($"바늘 세모 {R.Needles}개(Civil이 뒤집을 수 있다)");
        if (R.EndCliff > 0) R.Warn.Add($"부분 끝에서 벽이 원지반과 떨어진 채 끝나 마구리(폭 {R.VEps:0.###} m)로 내렸다 {R.EndCliff}곳");
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
    /// <param name="zlv">★[v114.3] 단 k 높이(절토 꼴 · 지역 좌표 점마다 — 선기준은 zr + k·H 그대로).</param>
    /// <param name="dfOf">줄기 첫 단 J의 t번 단 면 머리 거리.</param>
    /// <param name="fOf">단 k 면 폭.</param>
    /// <param name="tOf">단 k 소단 폭.</param>
    static void BuildFaces(Built R, Part part, int partNo, List<List<(double D, int Kl, List<List<Coordinate>> Pc)>> tierRows,
        List<(int Stem, bool AtStart, bool Inner, List<Point3> E)> eLines, PL pl, double ox, double oy, double sg, double side,
        Func<int, double, double, double> zlv, Func<int, int, double> dfOf, Func<int, double> fOf, Func<int, double> tOf,
        Func<double, double> p0At, Func<double, double, double?> gw, GeometryFactory gf, List<List<Point3>> zones, Action<int>? setStem = null, bool step = false)
    {
        var zp = zones.Select(r => BoundaryCutPoly(gf, r, ox, oy)).Where(p => p != null).Cast<Geometry>().ToList();
        if (zp.Count == 0) return;
        Geometry zu = zp.Count == 1 ? zp[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(zp);
        var zb = zu.Buffer(1e-6);
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
            setStem?.Invoke(k);   // ★[v116.3 · 계단 천단] 계단 높이는 이 줄기 블록에서
            var st = part.Stems[k];
            for (int t = 1; t <= st.Tiers; t++)
            {
                int kl = st.J + t - 1;
                double f = fOf(kl);
                List<Coordinate[]> tops;
                if (t == 1) tops = new() { pl.Sub(st.S0, st.S1) };
                else
                {
                    double dTop = dfOf(st.J, t - 1) + tOf(kl - 1);   // 앞 단 소단 끝(선기준 = (t − 1)(f + T))
                    var near = tierRows[k].FirstOrDefault(r => Math.Abs(r.D - dTop) < 1e-9);
                    if (near.Pc == null) continue;
                    tops = near.Pc.Select(p => p.ToArray()).ToList();
                }
                foreach (var tc in tops)
                    foreach (var ls in Clip(tc))
                    {
                        var mid = ls.GetCoordinateN(ls.NumPoints / 2);
                        var fs = new FaceSeg { Up = part.Up, Tier = t, Level = sg * zlv(kl, mid.X, mid.Y), Kind = 0, Part = partNo, Len = ls.Length };   // 표시용 대표값(토막 가운데)
                        foreach (var c in ls.Coordinates)
                        {
                            var nr = pl.Near(c.X, c.Y);
                            var (tx, ty) = pl.Tan(nr.S);
                            double nx = -ty * side, ny = tx * side;
                            double zt = t == 1 ? p0At(nr.S) : sg * zlv(kl - 1, c.X, c.Y);
                            double fx = c.X + nx * f, fy = c.Y + ny * f;
                            double levN = zlv(kl, fx, fy);
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
        setStem?.Invoke(-1);
        // 가로 마구리(줄기 갈림 S마다) — 절토 꼴로 높은 쪽(J가 큰 쪽) 끝 선이 머리 · 발 = 낮은 쪽 끝 선과 땅 중 (절토 꼴) 낮은 것
        for (int k = 0; k + 1 < part.Stems.Count; k++)
        {
            var ea = eLines.First(e => e.Stem == k && !e.AtStart).E; var eb = eLines.First(e => e.Stem == k + 1 && e.AtStart).E;
            // ★[v116.3 · 계단 천단 · 계획 2판 L8] J가 같으면(블록 경계) 끝 선 높이(절토 꼴 평균) 큰 쪽이 머리
            bool aHi = !step || part.Stems[k].J != part.Stems[k + 1].J ? part.Stems[k].J > part.Stems[k + 1].J : ea.Average(q => sg * q.Z) > eb.Average(q => sg * q.Z);   // 끔 = 옛 규칙 그대로(검토 L1)
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
        Func<double, double, double?> ground, Func<double, double, double?> p0, IReadOnlyList<Tri>? p0Tris, IReadOnlyList<Tri>? groundTris = null, WallShape shape = WallShape.Out,
        double tierZ1 = double.NaN, List<TierSec>? tierSecs = null, bool tierEnd = false, double endRatio = 1.5)
    {
        var bl = blockers?.ToList();
        var cut = BoundaryCut.Classify(src, bd, plan, bl);
        if (!cut.Ok) return (cut, null);
        var b = Build(new Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = plan, Hc = hc, Tc = tc, Hf = hf, Tf = tf, SlopeW = slopeW, FaceRun = faceRun, Grid = grid, Reach = reach,
            Ground = ground, P0 = p0, P0Tris = p0Tris, GroundTris = groundTris, Blockers = bl, Shape = shape, TierZ1 = tierZ1,
            TierSecs = tierSecs, TierEndSlope = tierEnd, EndRatioC = endRatio, EndRatioF = endRatio,
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
