// ── S157 ★★★[v103.1.9 · JACK 1002 Q1 «옹벽이 서 있던 단이 움직이면 재생성 전에 묻고 진행 — 아니오면 아무것도 안 바뀜»] 옹벽마다 자리 재기를 한 함수로(WallPlace.CheckAll) ──
//   재생성 전 묻기(CreateGradingCommand 들머리)와 재생성 뒤 끝 확인(WallRerunEnd)이 <b>같은 함수</b>를 쓴다 —
//   종전 WallRerunEnd 고리(아래에 그대로 옮긴 사본)와 답이 비트까지 같아야 한다. 판(경계 · 제원 · 구간)은 S151과 같은 네모 부지.
using DH.Grading.Core;

static class WallPlaceCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S157 옹벽 자리 재기 한 함수(CheckAll) — 미리 묻기 = 끝 확인 ==");
        var bd = new List<Point3> { new(0, 0, 100), new(60, 0, 100), new(60, 40, 100), new(0, 40, 100) };
        GradingParams P(bool steps)
        {
            var g = new GradingParams
            {
                CutBenchHeight = 5, FillBenchHeight = 5, CutBenchWidth = 1, FillBenchWidth = 1,
                CutSlope = 1.5, FillSlope = 1.5, CellSize = 1.0, MaxBenches = 8, MaxRise = 30,
                VertexSpacing = 2.0, MinSlope = 0.05, MinFaceRun = 0.005, MiterConvex = true, MiterLimit = 2.0,
            };
            if (steps) g.CutBenchSteps.Add((0, 4.0));
            return g;
        }
        var pp = P(false); var pH = P(true);
        SlopeZone Z(double t0, double t1, int from) { var z = new SlopeZone { T0 = t0, T1 = t1 }; z.Rules.Add((from, 3.0, -1)); z.Normalize(); return z; }
        var zS = Z(5, 55, 0);        // 남쪽 변 · 0단부터 1:3.0
        var zN0 = Z(105, 155, 0);    // 북쪽 변(옹벽 자리) · 0단부터
        var zN2 = Z(105, 155, 2);    // 북쪽 변 · 바깥 단(2단부터)만
        var zSF = Z(5, 55, 0);       // 성토 남쪽 · 0단부터(절토 목록과 다른 객체)

        // 절토 1단(아랫선 z 105) 북쪽 변 가운데 20m — S151과 같은 옹벽
        var R0 = WallPlace.BenchRings(bd, pp, true, null);
        bool have1 = R0.TryGetValue(1, out var ring1) && ring1!.Count >= 3;
        // 성토 자 = 윗선(crest) — 0단 윗선은 계획선 자리라 0단 구배를 바꿔도 안 움직인다 · 1단 윗선(0단 사면 + 소단 밖)에 옹벽을 둔다
        var RF = WallPlace.BenchRings(bd, pp, false, null);
        bool haveF1 = RF.TryGetValue(1, out var ringF1) && ringF1!.Count >= 3;
        check("S157 준비 — 절토 1단 · 성토 1단 자 링", have1 && haveF1, $"절토 단 {R0.Count}개 · 성토 단 {RF.Count}개");
        if (!have1 || !haveF1) return;
        var c1 = GradingGeometry.CumLen2D(ring1!); double tot1 = c1[^1];
        double yN = ring1!.Max(q => q.Y);
        double tA = GradingGeometry.ParamAt(ring1, c1, 40, yN), tB = GradingGeometry.ParamAt(ring1, c1, 20, yN);
        double Sp(double a, double b) => b >= a ? b - a : tot1 - a + b;
        var (T0, T1) = Sp(tA, tB) <= Sp(tB, tA) ? (tA, tB) : (tB, tA);
        var seg = WallInPoly.SegmentPoints(ring1, c1, T0, T1);
        double head = WallInPoly.HeadZ(ring1, c1, T0, Sp(T0, T1), true);

        var cF = GradingGeometry.CumLen2D(ringF1!);
        double fA = GradingGeometry.ParamAt(ringF1!, cF, 30, ringF1!.Min(q => q.Y));
        var segF = WallInPoly.SegmentPoints(ringF1, cF, fA, fA + 12);
        var cB = GradingGeometry.CumLen2D(bd);
        var segB = WallInPoly.SegmentPoints(bd, cB, GradingGeometry.ParamAt(bd, cB, 52, 40), GradingGeometry.ParamAt(bd, cB, 45, 40));   // 북쪽 변 7m(계획선은 반시계 — 북쪽은 x가 주는 쪽)

        var wA = new WallRec { No = 1, State = 1, Up = true, Bench = 1, Seg = seg, HeadZ = head, SlopeW = 0.01, RulerKind = 0 };                                          // 절토 1단 북쪽 20m
        var wB = new WallRec { No = 2, State = 1, Up = true, Bench = 1, Seg = segB, HeadZ = 100, SlopeW = -1, RulerKind = 0 };                                             // 판 1 옹벽 — 사실은 계획선 위(자 종류 칸 없음)
        var wC = new WallRec { No = 3, State = 1, Up = false, Bench = 1, Seg = segF, HeadZ = WallInPoly.HeadZ(ringF1, cF, fA, 12, false), SlopeW = 0.01, RulerKind = 0 };  // 성토 1단 남쪽 12m
        var wD = new WallRec { No = 4, State = 1, Up = true, Bench = 40, Seg = seg, HeadZ = head, SlopeW = 0.01, RulerKind = 0 };                                         // 없는 단
        var ws = new List<WallRec> { wA, wB, wC, wD };

        // 종전 WallRerunEnd(CreateGradingCommand.cs v103.1.8 :587-606)의 고리를 그대로 옮긴 사본
        List<(bool Moved, double DXY, double DZ, double T0, double T1, string Why, int Rk)> OldLoop(List<WallRec> wl, GradingParams pX, List<SlopeZone> cz, List<SlopeZone> fz)
        {
            var rings = new Dictionary<bool, Dictionary<int, List<Point3>>>();
            Dictionary<int, List<Point3>> R(bool up) { if (!rings.TryGetValue(up, out var r)) { r = WallPlace.BenchRings(bd, pX, up, up ? cz : fz); rings[up] = r; } return r; }
            var res = new List<(bool, double, double, double, double, string, int)>();
            foreach (var w in wl)
            {
                int rk = w.RulerKind;
                var ruler = rk == 1 ? bd : (R(w.Up).TryGetValue(w.Bench, out var rr) ? rr : null);
                var chk = WallPlace.Check(w.Seg, w.HeadZ, w.Up, ruler);
                if (chk.Moved && w.SlopeW < 0 && rk == 0) { var chk2 = WallPlace.Check(w.Seg, w.HeadZ, w.Up, bd); if (!chk2.Moved) { chk = chk2; rk = 1; } }
                res.Add((chk.Moved, chk.DXY, chk.DZ, chk.T0, chk.T1, chk.Why, rk));
            }
            return res;
        }
        static bool Same((bool Moved, double DXY, double DZ, double T0, double T1, string Why, int Rk) a, WallPlace.WallCheck b)
            => a.Moved == b.Place.Moved && a.DXY.Equals(b.Place.DXY) && a.DZ.Equals(b.Place.DZ) && a.T0.Equals(b.Place.T0) && a.T1.Equals(b.Place.T1) && a.Why == b.Place.Why && a.Rk == b.RulerKind;

        var empty = new List<SlopeZone>();
        var cases = new (string Name, GradingParams P, List<SlopeZone> Cut, List<SlopeZone> Fill)[]
        {
            ("구간 없음", pp, empty, empty), ("남쪽만 바꿈", pp, new() { zS }, empty), ("아랫단 구배(옹벽 자리)", pp, new() { zN0 }, empty),
            ("바깥 단만", pp, new() { zN2 }, empty), ("단높이 4m", pH, empty, empty), ("성토 남쪽 0단 1:3.0", pp, empty, new() { zSF }),
        };
        int eq = 0; var diff = new List<string>(); var got = new Dictionary<string, List<WallPlace.WallCheck>>();
        foreach (var cs in cases)
        {
            var a = OldLoop(ws, cs.P, cs.Cut, cs.Fill); var b = WallPlace.CheckAll(ws, bd, cs.P, cs.Cut, cs.Fill);
            got[cs.Name] = b;
            bool all = a.Count == b.Count && b.Select((r, i) => ReferenceEquals(r.Wall, ws[i])).All(x => x) && a.Zip(b).All(t => Same(t.First, t.Second));
            if (all) eq++; else diff.Add(cs.Name);
        }
        check("S157 CheckAll = 종전 WallRerunEnd 고리 — 옹벽 넷 · 경우 여섯에서 움직임 · 밑선 · 머리 · 새 T · 까닭 · 자 종류가 비트까지 같다 · 차례 그대로",
              eq == cases.Length, diff.Count == 0 ? $"{eq}/{cases.Length}" : "다름: " + string.Join(", ", diff));
        string M(string k) => string.Join(" ", got[k].Select(r => $"{r.Wall.No}{(r.Place.Moved ? "움직임" : "그대로")}({r.Place.DXY * 1000:F1}mm·{r.Place.DZ * 1000:F1}mm)"));
        var g0 = got["구간 없음"];
        check("S157 구간 없음 — 옹벽 1 · 3 그대로 · 판 1 옹벽 2는 계획선으로 물러나 그대로(자 종류 1) · 없는 단의 옹벽 4는 움직임(까닭: 링 없음)",
              !g0[0].Place.Moved && !g0[1].Place.Moved && g0[1].RulerKind == 1 && !g0[2].Place.Moved && g0[3].Place.Moved && g0[3].Place.Why.Contains("없다"), M("구간 없음"));
        check("S157 다른 자리만 바꿈(남쪽 절토) — 북쪽 옹벽 1 그대로 · 성토 옹벽 3 그대로", !got["남쪽만 바꿈"][0].Place.Moved && !got["남쪽만 바꿈"][2].Place.Moved, M("남쪽만 바꿈"));
        check("S157 옹벽 자리 아랫단 구배 바꿈 — 옹벽 1 움직임(밑선 1m 넘게) · 성토 옹벽 3 그대로",
              got["아랫단 구배(옹벽 자리)"][0].Place.Moved && got["아랫단 구배(옹벽 자리)"][0].Place.DXY > 1.0 && !got["아랫단 구배(옹벽 자리)"][2].Place.Moved, M("아랫단 구배(옹벽 자리)"));
        check("S157 바깥 단만 바꿈 — 옹벽 1 그대로", !got["바깥 단만"][0].Place.Moved, M("바깥 단만"));
        check("S157 단높이 바꿈(절토 0단부터 4m) — 옹벽 1 움직임(머리 높이) · 성토 옹벽 3 그대로",
              got["단높이 4m"][0].Place.Moved && got["단높이 4m"][0].Place.DZ > 0.5 && !got["단높이 4m"][2].Place.Moved, M("단높이 4m"));
        check("S157 성토 옹벽 자리 아랫단(0단) 구배 바꿈 — 성토 1단 옹벽 3만 움직임(절토 옹벽 1 그대로)",
              got["성토 남쪽 0단 1:3.0"][2].Place.Moved && !got["성토 남쪽 0단 1:3.0"][0].Place.Moved, M("성토 남쪽 0단 1:3.0"));
        check("S157 CheckAll은 옹벽을 안 고친다(판 1 옹벽의 자 종류 칸 그대로 0 · 목록에 쓰는 것은 부르는 쪽)", wB.RulerKind == 0 && wA.RulerKind == 0, $"옹벽 2 자 종류 {wB.RulerKind}");
        var gE = WallPlace.CheckAll(new List<WallRec>(), bd, pp, empty, empty);
        check("S157 옹벽이 없으면 빈 목록(단 링도 안 짓는다)", gE.Count == 0);

        // ── ★[v103.1.9 · 계획 검토 중간 1 · 2] 대상 고르기 · 끝 가르기 — 미리 잰 옹벽과 끝에서 잰 옹벽이 같은 집합이어야 한다(전부터 보류인 옹벽을 세면 첫 «예» 다음부터 매번 헛 ⚠) ──
        const string FailTag = "다시 짓기 실패";
        WallRec Wr(int no, int state = 1, string hold = "") => new() { No = no, State = state, Up = true, Bench = 1, Seg = seg, HeadZ = head, SlopeW = 0.01, Hold = hold };
        var l1 = Wr(1); var l2 = Wr(2, hold: "밑선이 840.0mm 움직였다"); var l3 = Wr(3, hold: FailTag + " — 터짐"); var l4 = Wr(4); var l5 = Wr(5); var l6 = Wr(6, hold: "밑선이 12.0mm 움직였다"); var l7 = Wr(7, state: 0);
        var lst = new List<WallRec> { l1, l2, l3, l4, l5, l6, l7 };
        var hdA = new WallListHead { PlanHandle = "2A3F" };
        var cnd = WallPlace.Candidates(hdA, lst, "2A3F", new[] { 4 }, FailTag);
        check("S157 대상 — 넣은 옹벽 · 보류 아님(다시 짓기 실패 보류는 대상) · 덮인 옹벽 아님 → 1 · 3 · 5", string.Join(",", cnd.Select(w => w.No)) == "1,3,5", string.Join(",", cnd.Select(w => w.No)));
        check("S157 대상 — 다른 계획선의 목록이면 없음 · 머리 계획선이 빈 값이면 이 구역", WallPlace.Candidates(hdA, lst, "9Z", null, FailTag).Count == 0
              && WallPlace.Candidates(new WallListHead { PlanHandle = "" }, lst, "9Z", null, FailTag).Count == 4);
        WallPlace.WallCheck Ck(WallRec w, bool moved) => new(w, new WallPlace.Place(moved, moved ? 0.84 : 0, 0, 0, 0, moved ? "밑선이 840.0mm 움직였다" : ""), 0);
        string S(List<(WallPlace.WallCheck C, WallPlace.EndKind Kind)> r) => string.Join(" ", r.Select(x => $"{x.C.Wall.No}:{x.Kind}"));
        var preC = new HashSet<int>(cnd.Select(w => w.No));
        // (가) 묻고 «예»(옹벽 1이 움직인다) — 끝: 1 움직임 · 2(전부터 보류) 아직 움직임 · 3 · 5 그대로 · 6(보류) 제자리로
        var endA = new List<WallPlace.WallCheck> { Ck(l1, true), Ck(l2, true), Ck(l3, false), Ck(l5, false), Ck(l6, false) };
        var sA = WallPlace.SortEnd(endA, preC, new HashSet<int> { 1 }, FailTag);
        check("S157 끝 가르기(묻고 예) — 1 지움 · 2 보류 그대로(⚠ 아님) · 3 · 5 그대로 · 6 돌아옴", S(sA) == "1:Remove 2:StillHeld 3:Keep 5:Keep 6:Back", S(sA));
        // (나) 미리 잰 것과 다르다 — 묻지 않았는데(움직일 것 없음) 끝에서 5가 움직임 → 보류 + ⚠ · 전부터 보류인 2는 여전히 ⚠ 아님
        var sB = WallPlace.SortEnd(new List<WallPlace.WallCheck> { Ck(l1, false), Ck(l2, true), Ck(l5, true) }, preC, new HashSet<int>(), FailTag);
        check("S157 끝 가르기(묻지 않았는데 움직임) — 5만 예상 밖(⚠) · 2는 보류 그대로", S(sB) == "1:Keep 2:StillHeld 5:Unexpected", S(sB));
        // (다) «예»를 받았는데 끝에서는 그대로 → 안 지움 · ⚠
        var sC = WallPlace.SortEnd(new List<WallPlace.WallCheck> { Ck(l1, false) }, preC, new HashSet<int> { 1 }, FailTag);
        check("S157 끝 가르기(예를 받았는데 그대로) — 안 지운다 · 미리와 다름", S(sC) == "1:AskedButStayed", S(sC));
        // (라) 미리 재지 않았다(되돌리기만 · 옛 길) — 움직인 넣은 옹벽은 예상 밖 · 전부터 보류인 것은 그대로 보류
        var sD = WallPlace.SortEnd(new List<WallPlace.WallCheck> { Ck(l1, true), Ck(l2, true), Ck(l3, true) }, null, null, FailTag);
        check("S157 끝 가르기(미리 안 잼) — 1 · 3(다시 짓기 실패 보류) 예상 밖 · 2 보류 그대로", S(sD) == "1:Unexpected 2:StillHeld 3:Unexpected", S(sD));
        // (마) 다시 짓기 실패로 보류된 옹벽 3이 미리 «움직인다» → 묻고 예 → 지움
        var sE = WallPlace.SortEnd(new List<WallPlace.WallCheck> { Ck(l3, true) }, preC, new HashSet<int> { 3 }, FailTag);
        check("S157 끝 가르기(다시 짓기 실패 보류도 대상) — 묻고 예 → 지움", S(sE) == "3:Remove", S(sE));
    }
}
