// ── S158 ★★★[v103.1.12 · JACK 1014 «합성 미루기» · 1015 «그 옹벽만 보류 · 최종면이 필요한 일 직전엔 묻기»] 합성 셈(WallList.CompositeState) ──
//   정지 창 «대기 N» · [옹벽 합성] · 내보내기 · 터파기 · «이어서» 관문 · 자가점검이 같은 함수로 센다 — 표지 없음 · v102 표지 · 보류 셋 · 바꿈 · 낡은 표지 · 옹벽 없는 도면.
//   함께: 보류 종류(움직임 / 다시 해 볼) · 겹침 판정(미룬 옹벽이 바꿀 옛 옹벽 · 보류 종류별) · 미리 묻기 대상 · 끝 가르기(합성 실패 보류) · 바꿈 따라가기 · 지문.
using DH.Grading.Core;

static class CompStateCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S158 합성 셈(CompositeState) · 보류 종류 · 미룬 옹벽 겹침 판정 ==");
        static string L(IEnumerable<int> l) => string.Join(",", l);
        const string Moved = "밑선이 840.0mm 움직였다";
        string Rb = WallList.RebuildFailTag + " — 데이라잇까지 못 지었다";
        string Cf = WallList.CompFailTag + "(P0 2A3) — 앞 옹벽 구역과 겹침";
        WallRec W(int no, int state = 1, string note = "판정 1(깨끗)", string hold = "", string block = "", int rep = 0) =>
            new() { No = no, State = state, LastNote = note, Hold = hold, Block = block, ReplaceNo = rep, Up = true, Bench = 1 };

        // ① 옹벽 없는 도면(재검토 중간 1) — 표지가 없어도 · v102 표지여도 할 일 없음
        var e0 = WallList.CompositeState(new List<WallRec>(), null);
        var e1 = WallList.CompositeState(new List<WallRec>(), Array.Empty<int>());
        check("S158 옹벽 없는 도면 — 표지 없음 · v102 표지 모두 «합성 할 일 없음» · 빠짐 없음", !e0.Need && e0.Missing.Count == 0 && !e1.Need && e1.Missing.Count == 0, e0.Text);

        // ② 섞인 목록 — 넣음(합성에 듦) · 미룸 · 움직인 보류 · 다시 짓기 실패 · 합성 실패(미룸) · 막히지 않은 대기 · 막힌 대기 · 표지의 낡은 번호
        var ws = new List<WallRec> { W(1), W(2, note: ""), W(3, hold: Moved), W(4, hold: Rb), W(5, note: "", hold: Cf), W(6, state: 0, note: ""), W(7, state: 0, note: "", block: "줄 버림") };
        var s = WallList.CompositeState(ws, new[] { 1, 9 });
        check("S158 섞인 목록 — 대상 1,2,4,5,6 · 표지에 없음 2,4,5,6 · 낡은 표지 9 · 미룸 2,5 · 빠짐 2,3,4,5,6,9 · 움직인 보류 3 · 다시 해 볼 4,5 · 합성 필요",
            L(s.Target) == "1,2,4,5,6" && L(s.Pending) == "2,4,5,6" && L(s.Stale) == "9" && L(s.Deferred) == "2,5" && L(s.Missing) == "2,3,4,5,6,9"
            && L(s.HeldMoved) == "3" && L(s.HeldRetry) == "4,5" && s.Need, s.Text);

        // ③ 표지 없음(정지면_DH가 합성이 아니다 — 재생성 · 되돌리기만 뒤) · v102 표지 — 대상 전부가 빠짐
        var s3 = WallList.CompositeState(new List<WallRec> { W(1), W(2) }, null);
        var s3b = WallList.CompositeState(new List<WallRec> { W(1), W(2) }, Array.Empty<int>());
        check("S158 표지 없음 · v102 표지 — 넣은 옹벽 1,2가 모두 «표지에 없음» · 합성 필요", L(s3.Pending) == "1,2" && s3.Need && L(s3b.Pending) == "1,2" && s3b.Need, s3.Text);

        // ④ 끔의 보통 흐름 — 넣은 옹벽이 다 표지에 있다 → 할 일 없음(관문이 아무것도 안 한다)
        var s4 = WallList.CompositeState(new List<WallRec> { W(1), W(2) }, new[] { 2, 1 });
        check("S158 넣은 옹벽이 다 표지에 — 합성 할 일 없음 · 빠짐 없음(끔의 보통 흐름에서 관문은 조용하다)", !s4.Need && s4.Missing.Count == 0 && s4.Deferred.Count == 0, s4.Text);

        // ⑤ 움직인 보류만 빠짐 — 합성은 안 해 본다(대상 아님) · 빠짐에는 든다(관문이 묻는다)
        var s5 = WallList.CompositeState(new List<WallRec> { W(1), W(3, hold: Moved) }, new[] { 1 });
        check("S158 움직인 보류만 표지에 없다 — 합성 필요 없음 · 빠짐 3(관문은 합성 없이 묻는다)", !s5.Need && L(s5.Missing) == "3", s5.Text);

        // ⑥ 보류 종류
        check("S158 보류 종류 — 다시 짓기 실패 · 합성 실패 = 다시 해 볼 보류 · 그 밖 = 움직인 보류 · 빈 값은 보류 아님",
            WallList.IsRetryHold(Rb) && WallList.IsRetryHold(Cf) && !WallList.IsRetryHold(Moved) && WallList.IsMovedHold(Moved) && !WallList.IsMovedHold(Cf) && !WallList.IsMovedHold("") && !WallList.IsRetryHold(""));
        check("S158 미룬 옹벽 = 넣음 ∧ 합성 글 빔 — 대기(상태 0)는 미룬 옹벽이 아니다",
            WallList.IsDeferred(W(2, note: "")) && !WallList.IsDeferred(W(1)) && !WallList.IsDeferred(W(6, state: 0, note: "")));

        // ⑦ 미리 묻기 대상 · 끝 가르기 — 합성 실패 보류는 «다시 짓기 실패»와 같은 부류(자리는 그대로)
        var hd = new WallListHead { PlanHandle = "" };
        var c5 = W(5, note: "", hold: Cf);
        var cand = WallPlace.Candidates(hd, new List<WallRec> { c5, W(3, hold: Moved), W(1) }, "X", null, WallList.RebuildFailTag);
        check("S158 미리 묻기 대상 — 합성 실패 보류 5 · 넣음 1(움직인 보류 3은 아님)", L(cand.Select(w => w.No)) == "5,1", L(cand.Select(w => w.No)));
        var still = new WallPlace.Place(false, 0, 0, 0, 0, "");
        var gone = new WallPlace.Place(true, 0.84, 0, 0, 0, Moved);
        var se = WallPlace.SortEnd(new List<WallPlace.WallCheck> { new(c5, still, 0), new(W(3, hold: Moved), still, 0) }, new HashSet<int> { 5, 1 }, new HashSet<int>(), WallList.RebuildFailTag);
        check("S158 끝 가르기 — 합성 실패 보류가 그대로면 «그대로»(돌아옴 아님 — 다음 합성이 다시 해 본다) · 움직인 보류가 그대로면 «돌아옴»",
            se[0].Kind == WallPlace.EndKind.Keep && se[1].Kind == WallPlace.EndKind.Back, string.Join(" ", se.Select(x => $"{x.C.Wall.No}:{x.Kind}")));
        var se2 = WallPlace.SortEnd(new List<WallPlace.WallCheck> { new(c5, gone, 0) }, new HashSet<int> { 5 }, new HashSet<int>(), WallList.RebuildFailTag);
        check("S158 끝 가르기 — 합성 실패 보류가 묻지 않았는데 움직이면 ⚠(예상 밖)", se2[0].Kind == WallPlace.EndKind.Unexpected, se2[0].Kind.ToString());

        // ⑧ 겹침 판정(Conflict) — 보류 종류별 · 미룬 옹벽이 바꿀 옛 옹벽
        Point3 P(double x, double y) => new(x, y, 100);
        var sq = new List<Point3> { P(0, 0), P(10, 0), P(10, 5), P(0, 5) };
        var segL = new List<Point3> { P(0, 0), P(10, 0) };
        WallRec Wp(int no, string note = "판정 1", string hold = "", int rep = 0, int bench = 1) =>
            new() { No = no, State = 1, LastNote = note, Hold = hold, ReplaceNo = rep, Up = true, Bench = bench, Poly = sq, Seg = segL };
        var hitPoly = new List<Point3> { P(5, 2), P(15, 2), P(15, 8), P(5, 8) };
        var segH = new List<Point3> { P(5, 2), P(15, 2) };
        var ca = WallList.Conflict(hitPoly, segH, true, 1, new List<WallRec> { Wp(5, note: "", hold: Cf, bench: 2) });
        check("S158 겹침 — 합성 실패 보류(다른 단)와 겹치면 멈춤(자리는 그대로라 보통 옹벽처럼)", ca.Stop.Contains("겹친다"), ca.Stop);
        var cr = WallList.Conflict(hitPoly, segH, true, 1, new List<WallRec> { Wp(4, hold: Rb, bench: 2) });
        check("S158 겹침 — 다시 짓기 실패 보류(다른 단)와 겹쳐도 멈춤", cr.Stop.Contains("겹친다"), cr.Stop);
        var cb = WallList.Conflict(hitPoly, segH, true, 1, new List<WallRec> { Wp(3, hold: Moved, bench: 2) });
        check("S158 겹침 — 움직인 보류는 판정에서 뺀다(종전 그대로)", cb.Stop.Length == 0 && cb.Note.Contains("보류 옹벽 3"), cb.Note);
        var cc = WallList.Conflict(sq, segL, true, 1, new List<WallRec> { Wp(1), Wp(2, note: "", rep: 1) });
        check("S158 같은 선 — 옛 옹벽 1 + 그것을 바꿀 미룬 옹벽 2 → 2만 바꾼다(«함께 걸친다»로 안 멈춤)", cc.ReplaceNo == 2 && cc.Stop.Length == 0, $"{cc.ReplaceNo} · {cc.Stop} · {cc.Note}");
        var cd = WallList.Conflict(sq, segL, true, 1, new List<WallRec> { Wp(1), Wp(2, note: "", rep: 1, hold: Cf) });
        check("S158 같은 선 — 바꿀 미룬 옹벽 2가 합성 실패 보류여도 2를 바꾼다(옛 옹벽 1은 합성이 바꿈을 따라가 뺀다)", cd.ReplaceNo == 2 && cd.Stop.Length == 0, $"{cd.ReplaceNo} · {cd.Stop}");
        // ★[코드 검토 낮음 7] 바뀔 옛 옹벽 1(미룬 옹벽 2가 바꿈)과 <b>다른 단</b>에서 겹치는 새 옹벽은 막는다(2가 보류로 남으면 1은 합성에 그대로)
        var cx = WallList.Conflict(hitPoly, segH, true, 2, new List<WallRec> { Wp(1), Wp(2, note: "", rep: 1, hold: Cf, bench: 3) });
        check("S158 겹침 — 바뀔 옛 옹벽 1과 다른 단에서 겹치는 새 옹벽은 멈춤(같은 선 바꿈 대상에서만 뺀다)", cx.Stop.Contains("옹벽 1(") && cx.Stop.Contains("겹친다"), cx.Stop);
        var cv = WallList.Conflict(sq, segL, true, 1, new List<WallRec> { Wp(1), Wp(2, rep: 1) });
        check("S158 같은 선 — 바꿈 칸이 남은 옹벽이 이미 합성에 들었으면(미룸 아님) 둘 다 같은 선 → 멈춤(종전 그대로)", cv.Stop.Contains("함께 걸친다"), cv.Stop);

        // ⑨ 바꿈 따라가기 — 새 옹벽 8 → 미룬 옹벽 2 → 옛 옹벽 1 · 미룬 옹벽이 없으면 종전(대기의 ReplaceNo)과 같다
        var chain = new List<WallRec> { W(1), W(2, note: "", rep: 1), W(8, state: 0, note: "", rep: 2) };
        check("S158 바꿈 따라가기 — 8이 2를 · 2가 1을 바꾼다 → 1,2", L(WallList.ReplacedBy(chain, new[] { 8 }).OrderBy(x => x)) == "1,2", L(WallList.ReplacedBy(chain, new[] { 8 })));
        var chain2 = new List<WallRec> { W(1), W(2, rep: 1), W(8, state: 0, note: "", rep: 2) };
        check("S158 바꿈 따라가기 — 바꾼 옹벽이 이미 합성에 들었으면 더 안 따라간다(종전 = 2만)", L(WallList.ReplacedBy(chain2, new[] { 8 })) == "2", L(WallList.ReplacedBy(chain2, new[] { 8 })));
        check("S158 바꿈 따라가기 — 새 옹벽이 없으면 빈 집합", WallList.ReplacedBy(chain, Array.Empty<int>()).Count == 0);

        // ⑨-b 4판 중간 2 — 옹벽이 모두 움직인 보류이고 표지가 없다(재생성 뒤) → 합성은 안 해 보지만 빠짐에는 든다(관문이 묻는다)
        var s9 = WallList.CompositeState(new List<WallRec> { W(3, hold: Moved) }, null);
        check("S158 모두 움직인 보류 · 표지 없음 — 합성 필요 없음 · 빠짐 3(옹벽이 있는데 안 묻고 나가지 않는다)", !s9.Need && L(s9.Missing) == "3", s9.Text);

        // ⑨-c 4판 중간 6 — «합성 실패» 보류의 열쇠: 정지면 · 이웃이 그대로면 다시 안 해 본다 · 바뀌면 다시
        var k1 = new List<WallRec> { W(1), W(2, note: "") };
        string key = WallList.CompFailKey(k1, 2, "2A3");
        k1[1].Hold = WallList.CompFailHold("2A3", key, "앞 옹벽 구역과 겹침");
        check("S158 열쇠 — 같은 정지면 · 같은 이웃이면 다시 안 해 본다(셈의 대상 아님 · 빠짐에는 든다 · 합성 필요 없음)",
            !WallList.CompFailFresh(k1[1], k1, "2A3") && WallList.CompositeState(k1, new[] { 1 }, "2A3") is var sk && L(sk.Stuck) == "2" && !sk.Need && L(sk.Missing) == "2" && L(sk.HeldRetry) == "2", WallList.CompositeState(k1, new[] { 1 }, "2A3").Text);
        check("S158 열쇠 — 정지면이 바뀌면 다시 해 본다(대상 · 합성 필요)", WallList.CompFailFresh(k1[1], k1, "2B0") && WallList.CompositeState(k1, new[] { 1 }, "2B0").Need);
        var k2 = new List<WallRec> { W(1), k1[1], W(4, note: "") };
        check("S158 열쇠 — 이웃 옹벽이 늘면 다시 해 본다", WallList.CompFailFresh(k1[1], k2, "2A3"));
        var k3 = new List<WallRec> { W(1, note: "판정 2(⚠거의)"), k1[1] };
        check("S158 열쇠 — 이웃의 합성 글이 바뀌어도(다시 합성됨) 다시 해 본다", WallList.CompFailFresh(k1[1], k3, "2A3"));
        check("S158 열쇠 — 열쇠를 못 읽는 옛 글이면 다시 해 본다 · 합성 실패 보류가 아니면 거짓",
            WallList.CompFailFresh(W(2, note: "", hold: WallList.CompFailTag + " — x"), k1, "2A3") && !WallList.CompFailFresh(W(2, note: "", hold: Moved), k1, "2A3") && !WallList.CompFailFresh(W(2, note: ""), k1, "2A3"));
        check("S158 열쇠 — 보류 글의 까닭(« — » 뒤)을 그대로 꺼낸다", WallList.HoldWhy(k1[1].Hold) == "앞 옹벽 구역과 겹침", WallList.HoldWhy(k1[1].Hold));
        var gw = WallList.GoingAway(new List<WallRec> { W(1), W(2, note: "", rep: 1), W(3, note: "", rep: 5, hold: Moved), W(4, note: "", rep: 6, hold: Cf), W(7, rep: 8) });
        check("S158 바뀔 옛 옹벽 — 미룬 옹벽 2 → 1 · 합성 실패 보류 4 → 6(움직인 보류 3 · 이미 합성된 7은 아님)", L(gw.OrderBy(x => x)) == "1,6", L(gw));

        // ⑩ 지문 — 보류가 바뀌면 다르다 · 같은 목록이면 같다(차례는 상관없음)
        var sigA = WallList.StateSig(new List<WallRec> { W(1), W(2, note: "") }, "2A3", new[] { 1 });
        var sigB = WallList.StateSig(new List<WallRec> { W(2, note: ""), W(1) }, "2A3", new[] { 1 });
        var sigC = WallList.StateSig(new List<WallRec> { W(1), W(2, note: "", hold: Cf) }, "2A3", new[] { 1 });
        var sigD = WallList.StateSig(new List<WallRec> { W(1), W(2, note: "") }, "2B0", new[] { 1 });
        check("S158 지문 — 같은 목록은 같고(차례 무관) · 보류 · 정지면이 바뀌면 다르다", sigA == sigB && sigA != sigC && sigA != sigD);
    }
}
