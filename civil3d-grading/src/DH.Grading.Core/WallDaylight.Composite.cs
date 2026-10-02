using System.Diagnostics;
using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Polygonize;

namespace DH.Grading.Core;

/// <summary>★★★[v101.0 · JACK 0928 «커밋하고 원지반 뚜껑만들어서 합성하는걸로»] <b>합성지표면_DH</b> — 정지면 + 순수옹벽 + 원지반 뚜껑.
/// <para>폴리곤 안 모든 자리의 높이 = <b>max(정지면, min(옹벽, 원지반))</b> — 옹벽이 원지반을 잘라 내리고(min) 정지면이 받친다(max).
/// 옹벽 앞 → 정지면 · 띠 → 옹벽 · 옹벽 위쪽 → 원지반(뚜껑)이 저절로 나오고, 포개진 소단(옹벽 = 원지반)은 높이가 같아 따로 규칙이 없다.
/// median(옹벽, 원지반, 정지면)과는 «옹벽 앞 성토» 두 경우만 다르다(median은 원지반·옹벽을 내줘 틀림).
/// 폴리곤 테두리(측선 · 앞 옹벽선 · 먼 쪽)에서 이 값은 정지면과 같다(계획 검토 0928 v101: 다섯 판 max(합성 − 정지면) = 0.0000mm).</para>
/// <para>만드는 법(계획 검토 v101 반영): ①정지면 삼각형 중 폴리곤에 <b>닿기만 해도</b>(변·꼭짓점 공유) 손댄다 — 가로지르는 것만 자르면 앞 옹벽선 54m에서
/// 손 안 댄 삼각형 변 한가운데에 합성 점 136~161개가 놓였다(T자 이음). Civil이 고칠 면(바늘)과 그 이웃도 손댄다.
/// ②잇기 <b>전에</b> 옹벽·원지반·폴리곤 꼭짓점이 정지면 꼭짓점에서 네모 1.05e-4 안이고 높이가 맞으면 그 꼭짓점으로 맞춘다(17:32: 앞 옹벽선 위 53.8µm 쌍이 판정 3을 냈다).
/// ③세 면의 변 + 교선 셋(±ZeroD) + 폴리곤 테두리 + 손댄 정지면 변을 1µm 격자로 한 번에 잇고, 조각마다 규칙으로 고른 면의 평면으로 삼각분할한다.
/// ④조각마다 세 면을 <b>따로</b> 읽어 규칙과 견준다 — 참값을 참값 자신과만 견주면 교선이 하나 빠져도 통과한다(계획 검토 v101 · 높음 3).
/// ⑤고정 점(손 안 댄 삼각형과 나누는 점)은 받은 좌표·높이 <b>그대로</b>(1µm 격자 맞춤이 0.5µm 옮기면 Civil이 두 점을 합친다 — 중간 5).
/// ⑥<see cref="CivilSafeMesh.MakeZone"/>로 다듬는다.</para></summary>
public static partial class WallDaylight
{
    public sealed class CompositeResult
    {
        /// <summary>다듬은 손댄 구역 삼각형(세계 좌표). 판정 3이면 null.</summary>
        public List<Tri>? Zone;
        /// <summary>그대로 옮길 정지면 삼각형(받은 그대로).</summary>
        public List<Tri> Untouched = new();
        public CivilSafeMesh.Report? Clean;
        public int PlanTotal, Touched, TouchRisk, FixedPts, PreSnapPlan, LineCount, Pieces, PiecesPoly, PiecesPlan, UnknownPieces, TriFail, AreaMis;
        public double UnknownArea;
        public int RuleSamples, RuleOver1, RuleOver10; public double RuleMax; public string RuleAt = "";
        public int OverlayFail; public string OverlayAt = "";
        /// <summary>폴리곤 테두리에서 |규칙 − 정지면| 최대 — 0이어야 바깥 정지면과 턱 없이 이어진다(폴리곤이 데이라잇 밖까지 안 나가면 턱).</summary>
        public double EdgeMax; public string EdgeAt = ""; public int EdgeSamples;
        /// <summary>한 점 = 한 높이로 맞추며 바꾼 양 — 조각 평면 값과 고른 높이의 차 최대(1µm 넘게 바뀐 꼭짓점 수 · 규칙을 못 읽은 점 수).</summary>
        public int ZOneN, ZOneMiss; public double ZOneMax; public string ZOneAt = "", ZOneMissAt = "";
        public int ZoneTrisRaw;
        /// <summary>★[검토 0929 v101 · 중간 3] 미리 맞춤(정지면 꼭짓점 → 폴리곤·옹벽·원지반 꼭짓점)이 정지면을 비튼 양 — 옮긴 부채꼴과 원래 정지면의
        /// 겹침 꼭짓점마다 잰 최대(받는 조건 ≤0.1mm) · 거절 수 · 최대 이동.</summary>
        public double PreSnapDz, PreSnapMove; public int PreSnapRefused;
        /// <summary>★[검토 0929 v101 · 낮음 7] 이음매 — 손댄·안 댄 삼각형이 나누는 변이 합성에 <b>그대로</b>(비트) 있는가 · 합성 넓이 = 손댄 정지면 넓이.</summary>
        public int SeamEdges, SeamMissing; public string SeamAt = ""; public double ZoneArea, TouchedArea;
        /// <summary>높이 오차 합(규칙 대조 + 한 점 한 높이 + 다듬기) — 합성이 규칙에서 벗어날 수 있는 위 끝(판정에 넣는다 · 검토 0929 v101 · 낮음 8).</summary>
        public double HeightBudget;
        /// <summary>★[v102.1 · JACK 0930 ③] 높이 오차 합의 한도 — 보통 1cm · 다듬기가 «Civil이 반드시 합칠 쌍» 허용을 썼으면 <see cref="CivilSafeMesh.ForcedCap"/>.
        /// Civil 되읽기 관문도 이 한도를 쓴다(허용 판은 Civil이 넘긴 면을 하나도 안 바꿨을 때만 받는다 — 명령 쪽).</summary>
        public double HeightLimit = 1e-2;
        /// <summary>손댄 정지면 삼각형이 차지하는 상자(세계 좌표) — 입력 파일을 이 상자로 떨궈야 재생이 출하와 같다(검토 0929 v101 · 낮음 11).</summary>
        public double TouchMinX = double.MaxValue, TouchMinY = double.MaxValue, TouchMaxX = double.MinValue, TouchMaxY = double.MinValue;
        public long MsTouch, MsZero, MsNode, MsPieces, MsClean;
        public int Tier = 3;
        public string Fail = "";
        /// <summary>★[v102.0 · 계획 검토 M3] 폴리곤 안에서 규칙이 정지면이 <b>아닌</b> 조각(옹벽·원지반을 고른 조각)의 넓이 —
        /// 0이면 합성이 정지면과 같다(옹벽이 안 드러난다 — 방향을 거꾸로 쓰면 절토·성토 두 규칙 모두 = 정지면이 된다).</summary>
        public double NonPlanArea;
        /// <summary>그 조각들에서 고른 면 − 정지면의 최대(조각 안 점 · 뒤집은 셈에선 부호도 거울) — 1cm 안이면 옹벽이 사실상 안 드러난다(넓이만 보면 정지면과 원지반이
        /// mm로 어긋난 부스러기가 넓이를 채워 관문을 빠져나간다 — 하네스 S142 음성).</summary>
        public double NonPlanMaxDz;
        public string TierText => Tier == 1 ? "정확" : Tier == 2 ? "⚠거의" : "⚠못 함";

        /// <summary>★[v102.0 · 성토] 뒤집은 셈의 결과를 되뒤집는다 — <b>z를 가진 필드는 여기 전부</b>(Zone · Untouched).
        /// 나머지는 수·넓이·XY 상자·|차|(높이가 든 글 EdgeAt·ZOneAt은 셈 안에서 되뒤집어 적었다).</summary>
        internal void NegateZ()
        {
            if (Zone != null) Zone = NegZ(Zone, "zone");
            Untouched = NegZ(Untouched, "untouched");
        }
        public string Summary => string.Format(CultureInfo.InvariantCulture,
            "판정 {0}({1}) · 정지면 {2}면 중 손댐 {3}(Civil이 고칠 면 곁 {4}) · 고정 점 {5} · 미리 맞춤(정지면 꼭짓점) {6}(최대 {7:F1}µm · 비틈 {8:F3}mm · 거절 {37})"
            + " · 선 {9} · 조각 {10}(폴리곤 안 {11} · 정지면 {12}{13}) · 규칙 대조 {14}점 최대 {15:F3}mm{16}{17} · 테두리 턱 {29}점 최대 {30:F3}mm{31}"
            + " · 한 점 한 높이 {32}점 최대 {33:F3}mm{34}{35} · 이음매 변 {39}(빠짐 {40}){41} · 높이 오차 합 {42:F3}mm"
            + " · 합성 삼각형 {18} → {19}{20}{21} · {22}ms(손댐 {23} · 교선 {24} · 잇기 {25} · 조각 {26} · 다듬기 {27}){28}",
            Tier, TierText, PlanTotal, Touched, TouchRisk, FixedPts, PreSnapPlan, PreSnapMove * 1e6, PreSnapDz * 1000,
            LineCount, Pieces, PiecesPoly, PiecesPlan, UnknownPieces > 0 ? $" · ⚠면을 모름 {UnknownPieces}({UnknownArea:F3}㎡)" : "",
            RuleSamples, RuleMax * 1000, RuleMax > 1e-3 ? " @" + RuleAt : "", RuleOver1 > 0 ? $"(1mm 넘음 {RuleOver1} · 1cm 넘음 {RuleOver10})" : "",
            ZoneTrisRaw, Zone?.Count ?? 0, OverlayFail > 0 ? $" · ⚠교선 겹침 계산 실패 {OverlayFail} @{OverlayAt}" : "",
            TriFail + AreaMis > 0 ? $" · ⚠삼각분할 실패 {TriFail} · 넓이 안 맞음 {AreaMis}" : "",
            MsTouch + MsZero + MsNode + MsPieces + MsClean, MsTouch, MsZero, MsNode, MsPieces, MsClean,
            (Clean != null ? " · 다듬기: " + Clean.Summary : "") + (Fail.Length > 0 ? " · ⚠" + Fail : ""),
            EdgeSamples, EdgeMax * 1000, EdgeMax > 1e-3 ? " @" + EdgeAt : "",
            ZOneN, ZOneMax * 1000, ZOneMax > 1e-3 ? " @" + ZOneAt : "", ZOneMiss > 0 ? $" · ⚠규칙을 못 읽은 꼭짓점 {ZOneMiss} @{ZOneMissAt}" : "",
            0, PreSnapRefused, 0, SeamEdges, SeamMissing, SeamMissing > 0 ? " @" + SeamAt : "",
            HeightBudget * 1000)
            + (HeightLimit > 1e-2 ? string.Format(CultureInfo.InvariantCulture, " · ⚠높이 오차 합 한도 {0:F0}mm(Civil이 반드시 합칠 쌍 허용)", HeightLimit * 1000) : "");
    }

    /// <param name="poly">옹벽 폴리곤(세계 좌표 · 첫점 반복 없음).</param>
    /// <param name="wallTris">가상옹벽_DH 삼각형(폴리곤 둘레).</param>
    /// <param name="groundTris">원지반 삼각형(폴리곤 둘레).</param>
    /// <param name="planAll">정지면_DH <b>전체</b> 삼각형(보이는 것) — 손 안 댄 것은 그대로 옮긴다.</param>
    /// <summary>★[검토 0929 v101 · 낮음 7] 폴리곤에 이만큼 가까운 정지면 삼각형까지 손댄다 — 폴리곤 곁 네모 1.05e-4(대각 1.49e-4) 안 정지면 꼭짓점은
    /// 부채꼴이 전부 손댄 삼각형이라 <b>고정 점이 아니다</b> — 미리 맞춤·다듬기가 그 점을 옮길 수 있다(고정이면 가파른 옹벽 쪽을 옮겨야 했다).</summary>
    const double TouchD = 2e-4;

    /// <summary>★[v102.1] 하네스 전용 — 나머지(미리 맞춤 + 규칙 대조 + 한 점 한 높이)에 더해 다듬기 예산을 줄인다(음성: 예산 ≤ 0이면 허용 없음). 출하 경로는 0.</summary>
    internal static double DebugRestAdd;

    /// <param name="down">★★[v102.0 · JACK 0929 «성토 — 앞 = 원지반 · 뒤 = 계획면»] 성토 — 폴리곤 안 = <b>min(정지면, max(옹벽, 원지반))</b>
    /// (절토 규칙의 −z 거울: 옹벽이 땅 위면 옹벽 · 땅에 묻히면 원지반(성토 사면이 없어진다) · 원지반이 계획면 위면 계획면).
    /// 입력을 전부 −z로 뒤집어 같은 셈을 하고 Zone·Untouched를 되뒤집는다 — 손 안 댄 삼각형은 −(−z) = z라 비트 그대로다.</param>
    public static CompositeResult BuildComposite(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris, IReadOnlyList<Tri> groundTris, IReadOnlyList<Tri> planAll,
        bool down = false)
    {
        if (!down) return BuildCompositeCore(poly, wallTris, groundTris, planAll, false);
        var r = BuildCompositeCore(NegZ(poly, "poly"), NegZ(wallTris, "wall"), NegZ(groundTris, "ground"), NegZ(planAll, "plan"), true);
        r.NegateZ();
        return r;
    }

    /// <param name="down">사람에게 보이는 높이(EdgeAt·ZOneAt)만 되뒤집어 적는다 — 입력은 이미 뒤집혀 왔다.</param>
    static CompositeResult BuildCompositeCore(IReadOnlyList<Point3> poly, IReadOnlyList<Tri> wallTris, IReadOnlyList<Tri> groundTris, IReadOnlyList<Tri> planAll,
        bool down)
    {
        double Hz(double z) => down ? -z : z;
        var R = new CompositeResult();
        var sw = Stopwatch.StartNew();
        if (poly == null || poly.Count < 3 || wallTris == null || wallTris.Count == 0 || groundTris == null || groundTris.Count == 0 || planAll == null || planAll.Count == 0)
        { R.Fail = "폴리곤·옹벽·원지반·정지면 중 빈 것이 있다"; return R; }
        double ox = poly[0].X, oy = poly[0].Y;
        var gf = new GeometryFactory();
        var pm = new PrecisionModel(1e6);
        R.PlanTotal = planAll.Count;
        Polygon TriPoly(Tri t) => gf.CreatePolygon(new[] { new Coordinate(t.A.X - ox, t.A.Y - oy), new Coordinate(t.B.X - ox, t.B.Y - oy), new Coordinate(t.C.X - ox, t.C.Y - oy), new Coordinate(t.A.X - ox, t.A.Y - oy) });

        // ① 손댈 정지면 삼각형 — 폴리곤에 닿는 것 + Civil이 고칠 면과 그 이웃
        var polyRing = poly.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(poly[0].X - ox, poly[0].Y - oy)).ToArray();
        var polyG0 = gf.CreatePolygon(polyRing);
        if (!polyG0.IsValid) { R.Fail = "폴리곤이 제 몸을 지른다"; return R; }
        var penv = polyG0.EnvelopeInternal.Copy(); penv.ExpandBy(1e-3 + TouchD);
        var touched = new bool[planAll.Count];
        for (int i = 0; i < planAll.Count; i++)
        {
            var t = planAll[i];
            var e = new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox,
                                 Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy);
            if (!e.Intersects(penv)) continue;
            if (polyG0.IsWithinDistance(TriPoly(t), TouchD)) touched[i] = true;        // 닿기만 해도(변·꼭짓점 공유 · TouchD 안)
        }
        // Civil이 고칠 면(가장 낮은 높이 < 1e-4 · 긴 변 안쪽 · 들로네 아님) — 그 면과 이웃을 손댄다(붙여넣기 이음매 바늘 대비 — 계획 검토 v101 · 중간 4)
        var eMap = new Dictionary<((double, double), (double, double)), List<int>>();
        static ((double, double), (double, double)) EK(Point3 a, Point3 b)
        { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
        for (int i = 0; i < planAll.Count; i++)
        {
            var t = planAll[i];
            foreach (var k in new[] { EK(t.A, t.B), EK(t.B, t.C), EK(t.C, t.A) })
            { if (!eMap.TryGetValue(k, out var l)) eMap[k] = l = new List<int>(2); l.Add(i); }
        }
        {
            static double L2(Point3 a, Point3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
            // ★[검토 0929 v101 · 중간 4] 정지면 <b>전체</b>를 훑으면 옹벽과 무관한 먼 이음매 바늘 하나로 판정 3이 나거나, 폴리곤이 안 닿은
            //   삼각형이 바뀐다(«받은 그대로» 약속이 깨짐). Civil은 제 삼각형을 다시 받을 때 안 바꾼다(원지반 248,283면 실측) —
            //   우리가 곁을 바꾸는 자리, 곧 폴리곤에 닿은 삼각형에서 변으로 두 고리 안만 본다
            var near = (bool[])touched.Clone();
            for (int ringN = 0; ringN < 2; ringN++)
            {
                var add = new List<int>();
                for (int i = 0; i < planAll.Count; i++)
                {
                    if (!near[i]) continue;
                    var t = planAll[i];
                    foreach (var k in new[] { EK(t.A, t.B), EK(t.B, t.C), EK(t.C, t.A) })
                        if (eMap.TryGetValue(k, out var nb)) foreach (int j in nb) if (!near[j]) add.Add(j);
                }
                foreach (int j in add) near[j] = true;
            }
            for (int i = 0; i < planAll.Count; i++)
            {
                if (!near[i]) continue;
                var t = planAll[i];
                double ar = Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
                double lab = L2(t.A, t.B), lbc = L2(t.B, t.C), lca = L2(t.C, t.A), lmax = Math.Sqrt(Math.Max(lab, Math.Max(lbc, lca)));
                if (lmax <= 0 || 2 * ar / lmax >= CivilSafeMesh.CivilMerge) continue;
                var ke = lab >= lbc && lab >= lca ? EK(t.A, t.B) : lbc >= lca ? EK(t.B, t.C) : EK(t.C, t.A);
                if (!touched[i]) { touched[i] = true; R.TouchRisk++; }
                if (eMap.TryGetValue(ke, out var nb)) foreach (int j in nb) if (!touched[j]) { touched[j] = true; R.TouchRisk++; }
            }
        }
        var touchedTris = new List<Tri>();
        for (int i = 0; i < planAll.Count; i++) if (touched[i]) touchedTris.Add(planAll[i]); else R.Untouched.Add(planAll[i]);
        foreach (var t in touchedTris)
            foreach (var q in new[] { t.A, t.B, t.C })
            { R.TouchMinX = Math.Min(R.TouchMinX, q.X); R.TouchMinY = Math.Min(R.TouchMinY, q.Y); R.TouchMaxX = Math.Max(R.TouchMaxX, q.X); R.TouchMaxY = Math.Max(R.TouchMaxY, q.Y); }
        R.Touched = touchedTris.Count;
        // 손댄 삼각형 변 중 정지면 <b>바깥 테두리</b>(면 하나만 쓰는 변) 길이 — 거기 꼭짓점은 고정이 아니라 1µm 격자로 옮겨질 수 있다(넓이 허용에 쓴다)
        double planEdgeLen = 0;
        for (int i = 0; i < planAll.Count; i++)
        {
            if (!touched[i]) continue;
            var t = planAll[i];
            foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
                if (eMap.TryGetValue(EK(a, b), out var l) && l.Count == 1) planEdgeLen += Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
        // 고정 점 — 손댄 삼각형 꼭짓점 중 손 안 댄 삼각형도 쓰는 점(세계 좌표·높이 그대로)
        var untouchedV = new HashSet<(double, double)>();
        foreach (var t in R.Untouched) { untouchedV.Add((t.A.X, t.A.Y)); untouchedV.Add((t.B.X, t.B.Y)); untouchedV.Add((t.C.X, t.C.Y)); }
        var fixedW = new Dictionary<(double, double), Point3>();
        foreach (var t in touchedTris) foreach (var q in new[] { t.A, t.B, t.C }) if (untouchedV.Contains((q.X, q.Y))) fixedW[(q.X, q.Y)] = q;
        R.FixedPts = fixedW.Count;
        R.MsTouch = sw.ElapsedMilliseconds; sw.Restart();

        // ② ★[v101.0 · 0929 하네스 테두리 띠가 잡음] 미리 맞추기 — <b>정지면 꼭짓점</b>을 곁(네모 1.05e-4)의 폴리곤·옹벽·원지반 꼭짓점 자리로 옮긴다.
        //   Civil은 그런 두 점을 합친다. 종전엔 거꾸로 옹벽·폴리곤 꼭짓점을 정지면 꼭짓점으로 옮겼는데(17:32 앞 옹벽선 모서리 53.8µm),
        //   그 이동에 앞면 직각 성분 17.7µm가 섞여 1:0.01 앞면이 기울고 합성이 규칙에서 1.787mm 벗어났다 — 격자 0.2m는 못 짚고
        //   테두리 띠(2cm 간격)가 잡았다. 꼭짓점 높이만 보는 검사는 0.000mm로 통과했다(삼각형 안쪽이 비틀린 것).
        //   안 맞추면 모서리에 높이 0 납작한 면이 남아 판정 3(같은 판) — 그래서 <b>완만한 정지면 쪽</b>을 옮기고, 옮긴 부채꼴이
        //   원래 정지면과 겹치는 다각형의 꼭짓점마다 0.1mm 안일 때만 받는다(정확 — 두 평면의 차는 겹침 조각 안에서 1차).
        //   높이는 원래 정지면의 새 자리 높이. 고정 점은 안 옮긴다(TouchD가 폴리곤 곁 꼭짓점을 고정에서 뺀다).
        var wallS = wallTris as List<Tri> ?? wallTris.ToList();
        var groundS = groundTris as List<Tri> ?? groundTris.ToList();
        var polyS = poly.ToList();
        var polyG = polyG0;
        {
            double cellA = CivilSafeMesh.Gap;
            var prep0 = PreparedGeometryFactory.Prepare(polyG0);
            var targets = new Dictionary<(long, long), List<(double X, double Y)>>();
            void AddT(double x, double y)
            {
                var k = ((long)Math.Floor((x - ox) / cellA), (long)Math.Floor((y - oy) / cellA));
                if (!targets.TryGetValue(k, out var l)) targets[k] = l = new();
                if (!l.Contains((x, y))) l.Add((x, y));
            }
            foreach (var q in poly) AddT(q.X, q.Y);
            // 옹벽·원지반 꼭짓점은 폴리곤 안(테두리 포함)만 — 밖 변은 폴리곤으로 잘려 합성 꼭짓점이 안 된다
            foreach (var src in new[] { wallTris, groundTris })
                foreach (var t in src)
                    foreach (var q in new[] { t.A, t.B, t.C })
                    {
                        var pt = gf.CreatePoint(new Coordinate(q.X - ox, q.Y - oy));
                        if (prep0.Covers(pt) || polyG0.IsWithinDistance(pt, 1e-6)) AddT(q.X, q.Y);
                    }
            var planV = new Dictionary<(double, double), Point3>();
            var fan = new Dictionary<(double, double), List<int>>();
            for (int i = 0; i < touchedTris.Count; i++)
                foreach (var q in new[] { touchedTris[i].A, touchedTris[i].B, touchedTris[i].C })
                {
                    planV.TryAdd((q.X, q.Y), q);
                    if (!fan.TryGetValue((q.X, q.Y), out var l)) fan[(q.X, q.Y)] = l = new();
                    l.Add(i);
                }
            var cands = new List<(double D, (double, double) Pk, (double X, double Y) T)>();
            foreach (var (pk, pq) in planV)
            {
                if (fixedW.ContainsKey(pk)) continue;
                long kx = (long)Math.Floor((pq.X - ox) / cellA), ky = (long)Math.Floor((pq.Y - oy) / cellA);
                (double X, double Y)? best = null; double bd = double.MaxValue; bool same = false;
                for (long dx = -1; dx <= 1 && !same; dx++)
                    for (long dy = -1; dy <= 1 && !same; dy++)
                        if (targets.TryGetValue((kx + dx, ky + dy), out var l))
                            foreach (var a in l)
                            {
                                double b = Math.Max(Math.Abs(a.X - pq.X), Math.Abs(a.Y - pq.Y));
                                if (b == 0) { same = true; break; }
                                if (b < CivilSafeMesh.Gap && b < bd) { bd = b; best = a; }
                            }
                if (!same && best != null) cands.Add((bd, pk, best.Value));
            }
            cands.Sort((u, v) => u.D.CompareTo(v.D));
            var orig = new TriSet(touchedTris, ox, oy);             // 참값 = 받은 정지면
            static double Sa(Tri t) => (t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X);
            double Dist(Tri nt)
            {
                double x1 = nt.A.X - ox, y1 = nt.A.Y - oy, x2 = nt.B.X - ox, y2 = nt.B.Y - oy, x3 = nt.C.X - ox, y3 = nt.C.Y - oy;
                var npoly = gf.CreatePolygon(new[] { new Coordinate(x1, y1), new Coordinate(x2, y2), new Coordinate(x3, y3), new Coordinate(x1, y1) });
                double d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3), max = 0;
                foreach (int j in orig.Tree.Query(npoly.EnvelopeInternal))
                {
                    if (!orig.Ok[j]) continue;
                    Geometry inter;
                    try { inter = OverlayNGRobust.Overlay(npoly, orig.Poly(j, gf), NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
                    catch { return double.PositiveInfinity; }
                    if (inter.IsEmpty || inter.Area <= 0) continue;
                    foreach (var c in inter.Coordinates)
                    {
                        double l1 = ((y2 - y3) * (c.X - x3) + (x3 - x2) * (c.Y - y3)) / d, l2 = ((y3 - y1) * (c.X - x3) + (x1 - x3) * (c.Y - y3)) / d;
                        double zn = l1 * nt.A.Z + l2 * nt.B.Z + (1 - l1 - l2) * nt.C.Z;
                        double dz = Math.Abs(zn - orig.Z(j, c.X, c.Y));
                        if (!(dz <= max)) max = double.IsNaN(dz) ? double.PositiveInfinity : Math.Max(max, dz);
                    }
                }
                return max;
            }
            var usedT = new HashSet<(double, double)>();
            foreach (var (dd, pk, tq) in cands)
            {
                if (usedT.Contains(tq) || planV.ContainsKey(tq) || !fan.TryGetValue(pk, out var fi)) { R.PreSnapRefused++; continue; }
                if (!orig.TryZNear(tq.X - ox, tq.Y - oy, 1e-6, out double zt)) { R.PreSnapRefused++; continue; }
                var np = new Point3(tq.X, tq.Y, zt);
                var newT = new List<(int I, Tri T)>();
                double dist = 0; bool ok = true;
                foreach (int i in fi)
                {
                    var t = touchedTris[i];
                    Point3 Rp(Point3 q) => q.X == pk.Item1 && q.Y == pk.Item2 ? np : q;
                    var nt = new Tri(Rp(t.A), Rp(t.B), Rp(t.C));
                    double s0 = Sa(t), s1 = Sa(nt);
                    if (!(s1 * Math.Sign(s0) > 1e-14)) { ok = false; break; }   // 뒤집힘·넓이 0
                    dist = Math.Max(dist, Dist(nt));
                    newT.Add((i, nt));
                }
                if (!ok || !(dist <= 1e-4)) { R.PreSnapRefused++; continue; }
                foreach (var (i, nt) in newT) touchedTris[i] = nt;
                fan.Remove(pk); fan[tq] = fi; planV.Remove(pk); planV[tq] = np;
                usedT.Add(tq);
                R.PreSnapPlan++; R.PreSnapDz = Math.Max(R.PreSnapDz, dist);
                R.PreSnapMove = Math.Max(R.PreSnapMove, Math.Sqrt((np.X - pk.Item1) * (np.X - pk.Item1) + (np.Y - pk.Item2) * (np.Y - pk.Item2)));
            }
        }
        var prepPoly = PreparedGeometryFactory.Prepare(polyG);

        // ③ 면 셋 · 교선 셋(±ZeroD)
        var W = new TriSet(wallS, ox, oy); var G = new TriSet(groundS, ox, oy); var P = new TriSet(touchedTris, ox, oy);
        // ★[v101.0 · 하네스를 짜다 찾음] 폴리곤 테두리에서 규칙값 = 정지면이어야 바깥과 턱 없이 이어진다 — 옹벽변환 폴리곤은 구조상 그렇다
        //   (측선: 옹벽 맨 아래 줄 ≤ 정지면 · 먼 쪽: 데이라잇 밖이라 정지면 = 원지반 — 계획 검토 v101 다섯 판 0.0000mm).
        //   폴리곤이 데이라잇까지 안 나가면 절벽이 생긴다 — 재서 1cm 넘으면 멈춘다
        {
            var ring = polyS;
            for (int k = 0; k < ring.Count; k++)
            {
                var a = ring[k]; var b = ring[(k + 1) % ring.Count];
                double L = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                int n = Math.Max(1, (int)Math.Ceiling(L / 0.1));
                for (int q = 0; q < n; q++)
                {
                    double t = (double)q / n, x = a.X + t * (b.X - a.X) - ox, y = a.Y + t * (b.Y - a.Y) - oy;
                    if (!W.TryZNear(x, y, 1e-5, out double zw) || !G.TryZNear(x, y, 1e-5, out double zg) || !P.TryZNear(x, y, 1e-5, out double zp)) continue;
                    R.EdgeSamples++;
                    double dz = Math.Abs(Math.Max(zp, Math.Min(zw, zg)) - zp);
                    if (dz > R.EdgeMax) { R.EdgeMax = dz; R.EdgeAt = $"({x + ox:F3},{y + oy:F3}) 규칙 {Hz(Math.Max(zp, Math.Min(zw, zg))):F3} · 정지면 {Hz(zp):F3}"; }
                }
            }
        }
        var zs = new List<ZeroSet>();
        foreach (var (a, b) in new[] { (W, G), (W, P), (G, P) })
            foreach (double lv in new[] { +ZeroD, -ZeroD })
                zs.Add(ZeroLines(a, b, gf, ox, oy, null, lv));
        foreach (var z in zs) { R.OverlayFail += z.OverlayFail; if (R.OverlayAt.Length == 0 && z.FailAt.Length > 0) R.OverlayAt = z.FailAt; }
        R.MsZero = sw.ElapsedMilliseconds; sw.Restart();

        // ④ 한 번에 잇기 — 폴리곤 테두리 + 손댄 정지면 삼각형 변 전부 + (폴리곤으로 자른) 옹벽·원지반 변 + (폴리곤으로 자른) 교선
        var lines = new List<Geometry> { polyG.ExteriorRing };
        var seen = new HashSet<((double, double), (double, double))>();
        void AddEdges(TriSet S0, bool clip)
        {
            var ls = new List<LineString>();
            for (int i = 0; i < S0.N; i++)
            {
                if (!S0.Ok[i]) continue;
                var p3 = new[] { (S0.X1[i], S0.Y1[i]), (S0.X2[i], S0.Y2[i]), (S0.X3[i], S0.Y3[i]) };
                for (int e = 0; e < 3; e++)
                {
                    var p = p3[e]; var q = p3[(e + 1) % 3];
                    var key = p.CompareTo(q) <= 0 ? (p, q) : (q, p);
                    if (!seen.Add(key)) continue;
                    ls.Add(gf.CreateLineString(new[] { new Coordinate(p.Item1, p.Item2), new Coordinate(q.Item1, q.Item2) }));
                }
            }
            if (ls.Count == 0) return;
            Geometry g = gf.CreateMultiLineString(ls.ToArray());
            if (clip) { try { g = OverlayNGRobust.Overlay(g, polyG, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); } catch { R.Fail = "변을 폴리곤으로 못 잘랐다"; } }
            lines.Add(g);
        }
        AddEdges(P, false);
        AddEdges(W, true);
        AddEdges(G, true);
        foreach (var z in zs)
        {
            if (z.Lines.Count == 0) continue;
            Geometry g = gf.CreateGeometryCollection(z.Lines.ToArray());
            try { g = OverlayNGRobust.Overlay(gf.BuildGeometry(z.Lines), polyG, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection); }
            catch { R.Fail = "교선을 폴리곤으로 못 잘랐다"; }
            lines.Add(g);
        }
        R.LineCount = lines.Sum(g => g.NumGeometries);
        Geometry noded;
        try { noded = NetTopologySuite.Operation.OverlayNG.UnaryUnionNG.Union((Geometry)gf.CreateGeometryCollection(lines.ToArray()), pm); }
        catch (Exception ex) { R.Fail = $"선 잇기가 터졌다 {ex.GetType().Name}"; return R; }
        R.MsNode = sw.ElapsedMilliseconds; sw.Restart();

        // ⑤ 조각마다 — 손댄 구역 안인가 · 폴리곤 안이면 규칙, 밖이면 정지면 → 평면 → 삼각분할 · 세 면을 따로 읽어 규칙 대조
        var uPolys = touchedTris.Select(t => (Geometry)TriPoly(t)).ToList();
        Geometry U;
        try { U = OverlayNGRobust.Union(uPolys); } catch (Exception ex) { R.Fail = $"손댄 구역을 못 합쳤다 {ex.GetType().Name}"; return R; }
        var prepU = PreparedGeometryFactory.Prepare(U);
        var pz = new Polygonizer(); pz.Add(noded);
        var raw = new List<Tri>();
        var outsideZ = new Dictionary<(double, double), double>();       // 폴리곤 밖 조각(= 정지면)에 닿는 꼭짓점 → 정지면 높이
        double Rule(double x, double y, out bool ok)
        {
            ok = W.TryZ(x, y, out double zw) & G.TryZ(x, y, out double zg) & P.TryZ(x, y, out double zp);
            if (!ok) return double.NaN;
            return Math.Max(zp, Math.Min(zw, zg));
        }
        foreach (var gpz in pz.GetPolygons())
        {
            if (gpz is not Polygon f || f.IsEmpty || f.Area <= 0) continue;
            var ip = f.InteriorPoint;
            if (ip == null || !prepU.Contains(ip)) continue;
            R.Pieces++;
            bool inPoly = prepPoly.Contains(ip);
            // 고를 면
            TriSet src; int tIdx;
            int tp = P.At(ip.X, ip.Y);
            if (tp < 0) { R.UnknownPieces++; R.UnknownArea += f.Area; continue; }
            if (!inPoly) { src = P; tIdx = tp; R.PiecesPlan++; }
            else
            {
                R.PiecesPoly++;
                int tw = W.At(ip.X, ip.Y), tg = G.At(ip.X, ip.Y);
                if (tw < 0 || tg < 0) { R.UnknownPieces++; R.UnknownArea += f.Area; continue; }
                double zw = W.Z(tw, ip.X, ip.Y), zg = G.Z(tg, ip.X, ip.Y), zp = P.Z(tp, ip.X, ip.Y);
                double m = Math.Min(zw, zg);
                if (zp >= m) { src = P; tIdx = tp; }
                else if (zw <= zg) { src = W; tIdx = tw; R.NonPlanArea += f.Area; R.NonPlanMaxDz = Math.Max(R.NonPlanMaxDz, m - zp); }
                else { src = G; tIdx = tg; R.NonPlanArea += f.Area; R.NonPlanMaxDz = Math.Max(R.NonPlanMaxDz, m - zp); }   // m − 정지면 > 0(고른 면이 정지면 위)
            }
            Geometry trs;
            try { trs = NetTopologySuite.Triangulate.Polygon.ConstrainedDelaunayTriangulator.Triangulate(f); }
            catch { R.TriFail++; continue; }
            double fa = 0; var local = new List<Tri>();
            for (int q = 0; q < trs.NumGeometries; q++)
            {
                if (trs.GetGeometryN(q) is not Polygon tq || tq.Area <= 0) continue;
                var c = tq.ExteriorRing.Coordinates;
                Point3 Pt(Coordinate k) => new(k.X + ox, k.Y + oy, src.Z(tIdx, k.X, k.Y));
                local.Add(new Tri(Pt(c[0]), Pt(c[1]), Pt(c[2])));
                fa += tq.Area;
            }
            if (Math.Abs(fa - f.Area) > 1e-9 + 1e-9 * f.Area) { R.AreaMis++; continue; }
            raw.AddRange(local);
            if (!inPoly) foreach (var t in local) foreach (var q in new[] { t.A, t.B, t.C }) outsideZ.TryAdd((q.X, q.Y), q.Z);
            // 규칙 대조 — 안 점 + 꼭짓점마다 안쪽으로 25% 들어간 점(조각 안인 것만)
            if (inPoly)
            {
                var samples = new List<Coordinate> { ip.Coordinate };
                var ec = f.ExteriorRing.Coordinates;
                for (int k = 0; k + 1 < ec.Length; k++)
                {
                    var s = new Coordinate(ec[k].X + 0.25 * (ip.X - ec[k].X), ec[k].Y + 0.25 * (ip.Y - ec[k].Y));
                    if (f.Contains(gf.CreatePoint(s))) samples.Add(s);
                }
                foreach (var s in samples)
                {
                    double zr = Rule(s.X, s.Y, out bool ok);
                    if (!ok) continue;
                    R.RuleSamples++;
                    double dz = Math.Abs(src.Z(tIdx, s.X, s.Y) - zr);
                    if (dz > 1e-3) R.RuleOver1++;
                    if (dz > 1e-2) R.RuleOver10++;
                    if (dz > R.RuleMax) { R.RuleMax = dz; R.RuleAt = $"({s.X + ox:F4},{s.Y + oy:F4})"; }
                }
            }
        }
        R.MsPieces = sw.ElapsedMilliseconds; sw.Restart();
        // ⑥ 고정 점 — 받은 좌표·높이 그대로(1.5µm 안 꼭짓점을 되돌린다)
        var fx = new Dictionary<(long, long), List<Point3>>();
        foreach (var q in fixedW.Values)
        {
            var k = ((long)Math.Floor((q.X - ox) / 1e-5), (long)Math.Floor((q.Y - oy) / 1e-5));
            if (!fx.TryGetValue(k, out var l)) fx[k] = l = new List<Point3>(); l.Add(q);
        }
        Point3 Fix(Point3 q)
        {
            long kx = (long)Math.Floor((q.X - ox) / 1e-5), ky = (long)Math.Floor((q.Y - oy) / 1e-5);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (fx.TryGetValue((kx + dx, ky + dy), out var l))
                        foreach (var a in l)
                            if ((a.X - q.X) * (a.X - q.X) + (a.Y - q.Y) * (a.Y - q.Y) <= 1.5e-6 * 1.5e-6) return a;
            return q;
        }
        for (int i = 0; i < raw.Count; i++) raw[i] = new Tri(Fix(raw[i].A), Fix(raw[i].B), Fix(raw[i].C));
        // ⑥-b ★[v101.0 · 하네스 S140 돌린 판이 잡음] <b>한 점 = 한 높이</b> — 조각마다 제 면 평면으로 꼭짓점을 읽으면, 1µm 격자로 옮겨진
        //   교선·테두리 점에서 면끼리 높이가 갈린다(옹벽 앞면 1:0.01 × 0.5µm = 0.05mm). 0928@2.9°: 테두리 점이 옹벽 높이를 받아
        //   폴리곤 <b>밖</b> 정지면 삼각형이 51µm 내려앉았다(원래 방향 판은 점이 격자 위라 0.04µm — 드러나지 않았을 뿐).
        //   고정 점 = 받은 그대로 · 폴리곤 밖 조각에 닿는 점(테두리 포함) = 정지면 · 안에만 있는 점 = 규칙 max(정지면, min(옹벽, 원지반)).
        //   테두리에서 규칙 = 정지면(EdgeMax)이라 바깥은 정지면 그대로, 안쪽은 규칙 값 하나로 이어진다.
        {
            var zOne = new Dictionary<(double, double), double>();
            Point3 One(Point3 q)
            {
                var k = (q.X, q.Y);
                if (!zOne.TryGetValue(k, out double z))
                {
                    if (fixedW.TryGetValue(k, out var f)) z = f.Z;
                    else if (outsideZ.TryGetValue(k, out double zo)) z = zo;
                    else
                    {
                        double x = q.X - ox, y = q.Y - oy;
                        if (W.TryZNear(x, y, 1e-5, out double zw) && G.TryZNear(x, y, 1e-5, out double zg) && P.TryZNear(x, y, 1e-5, out double zp))
                            z = Math.Max(zp, Math.Min(zw, zg));
                        else { z = q.Z; R.ZOneMiss++; if (R.ZOneMissAt.Length == 0) R.ZOneMissAt = $"({q.X:F3},{q.Y:F3})"; }
                    }
                    zOne[k] = z;
                }
                double dz = Math.Abs(z - q.Z);
                if (dz > 1e-6) R.ZOneN++;
                if (dz > R.ZOneMax) { R.ZOneMax = dz; R.ZOneAt = $"({q.X:F4},{q.Y:F4}) 조각 평면 {Hz(q.Z):F4} → {Hz(z):F4}"; }
                return dz == 0 ? q : new Point3(q.X, q.Y, z);
            }
            for (int i = 0; i < raw.Count; i++) raw[i] = new Tri(One(raw[i].A), One(raw[i].B), One(raw[i].C));
        }
        R.ZoneTrisRaw = raw.Count;

        // ⑦ 다듬기 — 고정 점은 안 없애고, 참값 = 조각 삼각형
        // ★[v102.0 · 코드 검토 중간 2] 다듬기에 합성 높이 예산(1cm − 미리 맞춤 − 규칙 대조 − 한 점 한 높이)을 넘긴다 — 판정이 같은 길 중 예산을 지키는 쪽을 고른다
        // ★[v102.1 · JACK 0930 ③] 허용 위 끝 = 1.5cm − 나머지(넘은 오차가 전부 Civil이 반드시 합칠 쌍 곁일 때만 — CivilSafeMesh.Judge)
        double rest = R.PreSnapDz + R.RuleMax + R.ZOneMax + DebugRestAdd;
        R.Zone = CivilSafeMesh.MakeZone(raw, fixedW.Values, out var cr, wallTol2: 1e-2 - rest, wallCap: CivilSafeMesh.ForcedCap - rest);
        R.Clean = cr;
        R.HeightLimit = cr.OverForced ? CivilSafeMesh.ForcedCap : 1e-2;
        R.MsClean = sw.ElapsedMilliseconds;
        // ⑦-b ★[검토 0929 v101 · 낮음 7] 이음매 — 손댄 삼각형과 안 댄 삼각형이 나누는 변은 합성에 <b>비트 그대로</b> 한 번 있어야 한다
        //   (없으면 T자 이음·틈·겹침). 합성 넓이 = 손댄 정지면 넓이(구멍·덧붙음이 없다)
        if (R.Zone != null)
        {
            static ((double, double), (double, double)) EK2(Point3 a, Point3 b)
            { var ka = (a.X, a.Y); var kb = (b.X, b.Y); return ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka); }
            var untE = new HashSet<((double, double), (double, double))>();
            foreach (var t in R.Untouched) { untE.Add(EK2(t.A, t.B)); untE.Add(EK2(t.B, t.C)); untE.Add(EK2(t.C, t.A)); }
            var zoneE = new Dictionary<((double, double), (double, double)), int>();
            foreach (var t in R.Zone) foreach (var k in new[] { EK2(t.A, t.B), EK2(t.B, t.C), EK2(t.C, t.A) }) zoneE[k] = zoneE.TryGetValue(k, out int c) ? c + 1 : 1;
            var seam = new HashSet<((double, double), (double, double))>();
            foreach (var t in touchedTris) foreach (var k in new[] { EK2(t.A, t.B), EK2(t.B, t.C), EK2(t.C, t.A) }) if (untE.Contains(k)) seam.Add(k);
            R.SeamEdges = seam.Count;
            foreach (var k in seam)
                if (!zoneE.TryGetValue(k, out int c) || c != 1)
                { R.SeamMissing++; if (R.SeamAt.Length < 120) R.SeamAt += $" ({k.Item1.Item1:F3},{k.Item1.Item2:F3})"; }
            static double TriA(Tri t) => Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) - (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2;
            foreach (var t in R.Zone) R.ZoneArea += TriA(t);
            foreach (var t in touchedTris) R.TouchedArea += TriA(t);
        }
        R.HeightBudget = R.PreSnapDz + R.RuleMax + R.ZOneMax + cr.WallDz;

        // ⑧ 판정 — 규칙 대조 · 교선 · 모르는 면 · 삼각분할 · 다듬기 중 가장 나쁜 것
        int tier = cr.Tier;
        var why = new List<string>();
        if (R.OverlayFail > 0) { tier = 3; why.Add($"교선 겹침 계산 실패 {R.OverlayFail}"); }
        if (R.UnknownPieces > 0) { tier = 3; why.Add($"면을 모르는 조각 {R.UnknownPieces}({R.UnknownArea:F3}㎡)"); }
        if (R.TriFail + R.AreaMis > 0) { tier = 3; why.Add($"삼각분할 실패 {R.TriFail} · 넓이 안 맞음 {R.AreaMis}"); }
        if (R.EdgeSamples == 0) { tier = 3; why.Add("폴리곤 테두리에서 세 면을 못 읽었다"); }
        else if (R.EdgeMax > 1e-2) { tier = 3; why.Add($"폴리곤 테두리에서 합성이 정지면과 {R.EdgeMax * 1000:F1}mm 어긋난다(턱 — 폴리곤이 데이라잇 밖까지 안 나감?) @{R.EdgeAt}"); }
        else if (R.EdgeMax > 1e-3) { tier = Math.Max(tier, 2); why.Add($"폴리곤 테두리에서 정지면과 {R.EdgeMax * 1000:F2}mm @{R.EdgeAt}"); }
        // 한 점 한 높이로 바꾼 양 = 그 꼭짓점 곁에서 조각 평면과 어긋난 양(1µm 격자 × 면 기울기 — 옹벽 앞면이면 ≤0.07mm)
        if (R.ZOneMiss > 0) { tier = Math.Max(tier, 2); why.Add($"규칙을 못 읽은 꼭짓점 {R.ZOneMiss}(조각 평면 값을 씀) @{R.ZOneMissAt}"); }
        if (R.ZOneMax > 1e-2) { tier = 3; why.Add($"한 점 한 높이로 {R.ZOneMax * 1000:F1}mm 바꿈 @{R.ZOneAt}"); }
        else if (R.ZOneMax > 1e-3) { tier = Math.Max(tier, 2); why.Add($"한 점 한 높이로 {R.ZOneMax * 1000:F2}mm 바꿈 @{R.ZOneAt}"); }
        if (R.RuleMax > 1e-2) { tier = 3; why.Add($"규칙과 {R.RuleMax * 1000:F1}mm @{R.RuleAt}"); }
        else if (R.RuleMax > 1e-3) { tier = Math.Max(tier, 2); why.Add($"규칙과 {R.RuleMax * 1000:F2}mm @{R.RuleAt}"); }
        if (R.Zone != null && R.SeamMissing > 0) { tier = 3; why.Add($"이음매 변 {R.SeamMissing}/{R.SeamEdges}개가 합성에 그대로 없다(T자 이음·틈) @{R.SeamAt}"); }
        // 넓이 허용: 이음매(고정 점끼리)는 비트 그대로라 0 — 정지면 바깥 테두리에 걸린 손댄 변만 1µm 격자 몫(길이 × 1µm) + 1e-8㎡ + 1e-9 상대(2차 검토 N9)
        if (R.Zone != null && Math.Abs(R.ZoneArea - R.TouchedArea) > planEdgeLen * 1e-6 + 1e-8 + 1e-9 * R.TouchedArea)
        { tier = 3; why.Add($"합성 넓이 {R.ZoneArea:F6}㎡ ≠ 손댄 정지면 넓이 {R.TouchedArea:F6}㎡(구멍·덧붙음)"); }
        if (R.HeightBudget > R.HeightLimit) { tier = 3; why.Add($"높이 오차 합 {R.HeightBudget * 1000:F1}mm(한도 {R.HeightLimit * 1000:F0}mm · 규칙 대조 + 한 점 한 높이 + 다듬기)"); }
        else if (R.HeightBudget > 1e-2)
        {
            // ★[v102.1 · JACK 0930 ③ «짓고 ⚠»] 1cm를 넘었지만 넘은 몫이 전부 Civil이 반드시 합칠 쌍 곁 — 짓고 자리·크기를 알린다(잰 값만)
            tier = Math.Max(tier, 2);
            why.Add($"높이 오차 합 {R.HeightBudget * 1000:F2}mm — 1cm 넘음(한도 {R.HeightLimit * 1000:F0}mm) = 미리 맞춤 {R.PreSnapDz * 1000:F2} + 규칙 대조 {R.RuleMax * 1000:F2} + 한 점 한 높이 {R.ZOneMax * 1000:F2} + 다듬기 {cr.WallDz * 1000:F2} @{cr.WallAt}");
        }
        else if (R.HeightBudget > 1e-3) { tier = Math.Max(tier, 2); why.Add($"높이 오차 합 {R.HeightBudget * 1000:F2}mm(미리 맞춤 {R.PreSnapDz * 1000:F2} + 규칙 대조 {R.RuleMax * 1000:F2} + 한 점 한 높이 {R.ZOneMax * 1000:F2} + 다듬기 {cr.WallDz * 1000:F2})"); }
        if (R.Zone == null) { tier = 3; why.Add("다듬기 못 함 — " + cr.Fail); }
        // ★[v102.0 · 계획 검토 M3] 옹벽이 한 곳도 안 드러난다 — 합성이 곧 정지면이다. 방향을 거꾸로 쓰면(절토 자료에 성토 규칙 · 그 반대)
        //   두 규칙 모두 = 정지면이라 테두리 턱도 0으로 재여 «판정 1, 옹벽 없음»으로 조용히 끝난다 — 여기서 멈춘다
        if (R.NonPlanArea <= 1e-4 || R.NonPlanMaxDz <= 0.01)
        { tier = 3; why.Add($"합성이 정지면과 같다(옹벽·원지반을 고른 조각 넓이 {R.NonPlanArea:G3}㎡ · 정지면과 최대 {R.NonPlanMaxDz * 1000:F1}mm — 옹벽이 안 드러난다 · 옹벽 방향(절토/성토)·폴리곤을 보세요)"); }
        if (R.Fail.Length > 0) { tier = 3; why.Add(R.Fail); }
        R.Tier = tier;
        if (why.Count > 0) R.Fail = string.Join(" · ", why);
        if (tier == 3) R.Zone = null;
        return R;
    }

    /// <summary>★★★[v103.0] 옹벽 하나 — <see cref="ComposeWalls"/> 입력(번호 · 폴리곤 · 가상옹벽 · 원지반 · 성토인가).</summary>
    public sealed class WallInput
    {
        public int No;
        public IReadOnlyList<Point3> Poly = Array.Empty<Point3>();
        public IReadOnlyList<Tri> WallTris = Array.Empty<Tri>();
        public IReadOnlyList<Tri> GroundTris = Array.Empty<Tri>();
        public bool Down;
    }

    /// <summary>한 옹벽의 합성 단계 — 결과 · 앞 옹벽 폴리곤 안을 건드린 삼각형 수(0이어야 한다) · 앞 단계가 다시 지은 삼각형을 건드린 수.</summary>
    public sealed class ComposeStep
    {
        public int No;
        public CompositeResult R = new();
        public int IntoPrev, IntoPrevNo;
        public string IntoPrevAt = "";
        public int TouchedPrevZone;
        public long Ms;
    }

    public sealed class ComposeResult
    {
        /// <summary>마지막 합성의 삼각형 전부(다듬은 구역 + 그대로 옮긴 것) — 못 했으면 null.</summary>
        public List<Tri>? All;
        public List<ComposeStep> Steps = new();
        /// <summary>못 한 단계(옹벽 차례 · 0부터) · 까닭.</summary>
        public int FailIndex = -1;
        public string Fail = "";
        public int Tier = 3;
        /// <summary>높이 오차 합 — 옹벽마다의 예산(폴리곤 안은 늘 합성 전 면 위에서 짓는다) 중 최대와, 손댄 구역이 겹친 바깥에서 합성 전 면과 <b>잰</b> 차이 중 큰 것.</summary>
        public double HeightBudget;
        /// <summary>손댄 구역이 겹친 바깥(모든 폴리곤 밖 · 다시 지은 삼각형 꼭짓점)에서 합성 전 면과 잰 높이 차 최대 — 겹치지 않으면 0.</summary>
        public double SharedDev;
        public double HeightLimit = 1e-2;
        /// <summary>«Civil이 반드시 합칠 쌍» 허용을 쓴 옹벽이 하나라도 있다 — 되읽기 관문이 Civil이 넘긴 면을 그대로 받았을 때만 받는다.</summary>
        public bool OverForced;
        public bool ZonesOverlap;
        public string TierText => Tier == 1 ? "정확" : Tier == 2 ? "⚠거의" : "⚠못 함";
        public string Summary => string.Format(CultureInfo.InvariantCulture, "옹벽 {0}개 차례 합성 · 판정 {1}({2}) · 높이 오차 합 {3:F3}mm(옹벽마다의 최대{4}) · 한도 {5:F0}mm{6}",
            Steps.Count, Tier, TierText, HeightBudget * 1000,
            ZonesOverlap ? string.Format(CultureInfo.InvariantCulture, " · 손댄 구역이 겹친 바깥에서 합성 전 면과 잰 차 {0:F3}mm", SharedDev * 1000) : "",
            HeightLimit * 1000, Fail.Length > 0 ? " · ⚠" + Fail : "");
    }

    /// <summary>★★★[v103.0 · JACK 0930 «옹벽이 남아야» · 계획 검토 v103 H2] <b>여러 옹벽을 합성 전 면 위에 차례로</b> — P_k = BuildComposite(P_{k−1}, 옹벽 k).
    /// <para>더하기(새 옹벽 하나)와 다시 짓기(목록 전부)가 <b>같은 함수</b>다 — 더하기는 목록 끝에 새 옹벽을 붙여 처음부터 차례로 짓는다.
    /// 옹벽이 하나면 <see cref="BuildComposite"/> 한 번과 비트 같다(삼각형 차례도 Zone 다음 Untouched).</para>
    /// <para>★지키는 것: 뒤 옹벽이 앞 옹벽 <b>폴리곤 안</b> 삼각형을 건드리면 멈춘다(앞 옹벽 모양이 바뀐다) — 꼭짓점이 테두리에서 10µm 넘게 안쪽이거나
    /// 무게중심이 안쪽인 삼각형. 폴리곤 테두리를 나누는 바깥 삼각형(평탄부를 가로지르는 큰 삼각형을 두 옹벽이 같이 건드리는 자리)은 된다 —
    /// 그 테두리 꼭짓점은 손 안 댄 안쪽 삼각형과 나누므로 고정 점이라 안 움직인다.</para></summary>
    public static ComposeResult ComposeWalls(IReadOnlyList<Tri> planAll, IReadOnlyList<WallInput> walls)
    {
        var res = new ComposeResult();
        if (walls == null || walls.Count == 0) { res.Fail = "옹벽이 없다"; return res; }
        if (planAll == null || planAll.Count == 0) { res.Fail = "정지면 삼각형이 없다"; return res; }
        var original = new HashSet<Tri>(planAll);
        IReadOnlyList<Tri> cur = planAll;
        var gf = new GeometryFactory();
        double ox = walls[0].Poly.Count > 0 ? walls[0].Poly[0].X : 0, oy = walls[0].Poly.Count > 0 ? walls[0].Poly[0].Y : 0;
        var polys = new List<(int No, Polygon G, IPreparedGeometry Pg)>();
        foreach (var w in walls)
        {
            if (w.Poly == null || w.Poly.Count < 3) { res.Fail = $"옹벽 {w.No}의 폴리곤이 없다"; return res; }
            var ring = w.Poly.Select(q => new Coordinate(q.X - ox, q.Y - oy)).Append(new Coordinate(w.Poly[0].X - ox, w.Poly[0].Y - oy)).ToArray();
            var pgon = gf.CreatePolygon(ring);
            polys.Add((w.No, pgon, PreparedGeometryFactory.Prepare(pgon)));
        }
        // 삼각형이 폴리곤 <b>안</b>인가 — 무게중심이나 꼭짓점이 테두리에서 10µm 넘게 안쪽(1µm 격자로 맞춘 테두리 점은 안 걸린다)
        const double InsideTol = 1e-5;
        bool InsideOf(Tri t, Polygon g, IPreparedGeometry pg, out string at)
        {
            double cx = (t.A.X + t.B.X + t.C.X) / 3 - ox, cy = (t.A.Y + t.B.Y + t.C.Y) / 3 - oy;
            at = string.Format(CultureInfo.InvariantCulture, "({0:F3},{1:F3})", cx + ox, cy + oy);
            bool In(double x, double y)
            {
                var pt = gf.CreatePoint(new Coordinate(x, y));
                return pg.Contains(pt) && g.ExteriorRing.Distance(pt) > InsideTol;
            }
            return In(cx, cy) || In(t.A.X - ox, t.A.Y - oy) || In(t.B.X - ox, t.B.Y - oy) || In(t.C.X - ox, t.C.Y - oy);
        }
        int worst = 1; double maxB = 0, lim = 1e-2; bool overF = false, overlap = false;
        for (int k = 0; k < walls.Count; k++)
        {
            var w = walls[k];
            var (_, gK, pgK) = polys[k];
            // ①' 앞 옹벽들이 다시 지은 삼각형이 이 옹벽 폴리곤 <b>안</b>에 있으면 멈춘다 — 이 옹벽은 폴리곤 안을 언제나 합성 전 면 위에서 짓는다
            //    (그래야 옹벽마다의 높이 예산이 합성 전 면 기준이고, 더하기 차례와 상관없이 폴리곤 안 모양이 같다)
            if (k > 0)
            {
                int into = 0; string at0 = "";
                foreach (var t in cur)
                {
                    if (original.Contains(t)) continue;
                    if (InsideOf(t, gK, pgK, out string a1)) { if (into++ == 0) at0 = a1; }
                }
                if (into > 0)
                {
                    res.Steps.Add(new ComposeStep { No = w.No, IntoPrev = into, IntoPrevAt = at0 });
                    res.FailIndex = k;
                    res.Fail = string.Format(CultureInfo.InvariantCulture, "앞 옹벽이 다시 지은 삼각형 {0}개가 옹벽 {1}의 폴리곤 안에 있다 @{2} — 두 옹벽을 더 떨어뜨려 지정하세요", into, w.No, at0);
                    return res;
                }
            }
            var sw = Stopwatch.StartNew();
            var r = BuildComposite(w.Poly, w.WallTris, w.GroundTris, cur, w.Down);
            var st = new ComposeStep { No = w.No, R = r, Ms = sw.ElapsedMilliseconds };
            res.Steps.Add(st);
            if (r.Zone == null) { res.FailIndex = k; res.Fail = $"옹벽 {w.No} — {r.Fail}"; return res; }
            // ② 손댄 삼각형(이번 입력 − 그대로 옮긴 것)이 앞 옹벽 폴리곤 <b>안</b>이면 멈춘다 — 앞 옹벽 모양이 바뀐다.
            //    폴리곤 테두리를 나누는 바깥 삼각형(평탄부를 가로지르는 큰 삼각형을 두 옹벽이 같이 건드리는 자리 — 0930 덤프 973면)은 된다
            var unt = new HashSet<Tri>(r.Untouched);
            foreach (var t in cur)
            {
                if (unt.Contains(t)) continue;
                if (!original.Contains(t)) st.TouchedPrevZone++;
                for (int j = 0; j < k; j++)
                    if (InsideOf(t, polys[j].G, polys[j].Pg, out string a2))
                    {
                        if (st.IntoPrev++ == 0) { st.IntoPrevNo = polys[j].No; st.IntoPrevAt = a2; }
                        break;
                    }
            }
            if (st.IntoPrev > 0)
            {
                res.FailIndex = k;
                res.Fail = string.Format(CultureInfo.InvariantCulture, "옹벽 {0}가 앞 옹벽 {1}의 폴리곤 안 삼각형 {2}개를 건드린다 @{3} — 앞 옹벽 모양이 바뀌므로 짓지 않는다(두 옹벽을 더 떨어뜨려 지정하세요)",
                    w.No, st.IntoPrevNo, st.IntoPrev, st.IntoPrevAt);
                return res;
            }
            if (st.TouchedPrevZone > 0) overlap = true;
            var next = new List<Tri>(r.Zone.Count + r.Untouched.Count);
            next.AddRange(r.Zone); next.AddRange(r.Untouched);
            cur = next;
            worst = Math.Max(worst, r.Tier);
            maxB = Math.Max(maxB, r.HeightBudget); lim = Math.Max(lim, r.HeightLimit);
            if (r.Clean?.OverForced == true) overF = true;
        }
        res.ZonesOverlap = overlap;
        res.HeightLimit = lim;
        res.OverForced = overF;
        // ③ 높이 오차 합 — 폴리곤 안은 옹벽마다 합성 전 면 기준 예산(①' 덕분). 손댄 구역이 겹친 바깥(평탄부 등)은 앞·뒤 구역 오차가 얹힐 수 있으므로
        //    합성 전 면과 <b>잰다</b>: 다시 지은 삼각형 중 모든 폴리곤 밖의 꼭짓점 높이 − 합성 전 면 높이(더해서 짐작하지 않는다)
        double dev = 0;
        if (overlap)
        {
            var idx = new STRtree<Tri>();
            foreach (var t in planAll)
            {
                var e = new Envelope(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) - ox, Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) - ox,
                                     Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y)) - oy, Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)) - oy);
                idx.Insert(e, t);
            }
            idx.Build();
            double? P0At(double x, double y)
            {
                foreach (var t in idx.Query(new Envelope(x - ox, x - ox, y - oy, y - oy)))
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
            foreach (var t in cur)
            {
                if (original.Contains(t)) continue;
                bool inAny = false;
                foreach (var (_, g, pg) in polys) { if (InsideOf(t, g, pg, out _)) { inAny = true; break; } }
                if (inAny) continue;
                foreach (var q in new[] { t.A, t.B, t.C })
                {
                    var z0 = P0At(q.X, q.Y);
                    if (z0 != null) dev = Math.Max(dev, Math.Abs(q.Z - z0.Value));
                }
            }
        }
        res.SharedDev = dev;
        res.HeightBudget = Math.Max(maxB, dev);
        int tier = worst;
        if (res.HeightBudget > lim)
        {
            res.Tier = 3; res.FailIndex = walls.Count - 1;
            res.Fail = string.Format(CultureInfo.InvariantCulture, "손댄 구역이 겹친 바깥에서 합성 전 면과 {0:F1}mm 어긋난다(한도 {1:F0}mm) — 마지막 옹벽 {2}를 빼고 다시 지정하세요",
                dev * 1000, lim * 1000, walls[^1].No);
            return res;
        }
        if (res.HeightBudget > 1e-3) tier = Math.Max(tier, 2);
        res.Tier = tier;
        res.All = cur as List<Tri> ?? new List<Tri>(cur);
        return res;
    }
}
