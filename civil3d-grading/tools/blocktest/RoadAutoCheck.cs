// ── S185 ★★[v111.7.1 · JACK 1102 «2초 쉬면 저절로 · 클릭 수를 최대한 적게»] 진입도로 저절로 다시 만들기 판정(보낼까 · 무엇을) · [PVI 넣기] 확대할 종단도 · 확대 창 ──
//   시계 · 입력 거르개(PreTranslateMessage) · 화면은 화면 없는 Civil에서 못 돈다 — 그 판정만 떼어 낸 Core 함수(RoadAutoRules · ProfileSheetRules)를 본다(만든 값만)
using DH.Grading.Core;

static class RoadAutoCheck
{
    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S185 진입도로 저절로 다시 만들기(판정 · 무엇을 · PVI 확대) ==");
        RoadAutoRules.Facts Ok() => new() { Stale = true, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 };
        string D(RoadAutoRules.Facts f) { var (a, w) = RoadAutoRules.Decide(f); return a + (w.Length > 0 ? ":" + w : ""); }

        // ① 보낼까 — 막을 것이 없으면 보냄 · 막는 까닭은 앞에서부터 하나
        {
            var cases = new (string Name, RoadAutoRules.Facts F, string Want)[]
            {
                ("바뀐 것 없음", new() { Stale = false, Command = true }, "Fresh:최신"),
                ("모두 됨", Ok(), "Send"),
                ("해 봄(실행 취소)", new() { Stale = true, Tried = "실행 취소로 온 상태", SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Block:해 봄(실행 취소로 온 상태)"),
                ("계산 오류", new() { Stale = true, CalcError = true, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Block:계산 오류"),
                ("명령 중", new() { Stale = true, Command = true, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Wait:명령 중"),
                ("고르는 중", new() { Stale = true, Picking = true, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Wait:고르는 중"),
                ("방금 바뀜(1.9초)", new() { Stale = true, SinceChange = 1.9, SinceInput = 5, SinceUserSend = 5 }, "Wait:방금 바뀜"),
                ("단추 누름(0.5초)", new() { Stale = true, SinceChange = 5, SinceInput = 5, SinceUserSend = 0.5 }, "Wait:단추 누름"),
                ("칸 입력 중", new() { Stale = true, Typing = true, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Wait:칸 입력 중"),
                ("입력 1.5초 전", new() { Stale = true, SinceChange = 5, SinceInput = 1.5, SinceUserSend = 5 }, "Wait:입력 중"),
                ("단추 누른 채(끌기)", new() { Stale = true, ButtonDown = true, SinceChange = 5, SinceInput = 9, SinceUserSend = 5 }, "Wait:입력 중"),
                ("창 뒤", new() { Stale = true, Front = false, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Wait:창 뒤"),
                ("다른 도면 설정", new() { Stale = true, Synced = false, SinceChange = 5, SinceInput = 5, SinceUserSend = 5 }, "Wait:다른 도면 설정"),
                ("해 봄이 명령 중보다 앞", new() { Stale = true, Tried = "보냄", Command = true }, "Block:해 봄(보냄)"),
                ("명령 중이 입력 중보다 앞", new() { Stale = true, Command = true, SinceChange = 0, SinceInput = 0, Typing = true, Front = false }, "Wait:명령 중"),
                ("2.0초 딱 = 보냄", new() { Stale = true, SinceChange = 2.0, SinceInput = 2.0, SinceUserSend = 1.0 }, "Send"),
            };
            var bad = cases.Where(c => D(c.F) != c.Want).Select(c => $"{c.Name}: {D(c.F)} ≠ {c.Want}").ToList();
            check("S185 ① 보낼까(최신 · 보냄 · 해 봄 · 계산 오류 · 명령 · 고르기 · 방금 바뀜 · 단추 · 칸 입력 · 입력 · 끌기 · 창 뒤 · 다른 도면 — 앞에서부터 하나)", bad.Count == 0, string.Join(" | ", bad));
            // 마우스를 움직이기만 하는 것은 입력이 아님 — 시계는 SinceInput(키 · 단추 · 휠 시각)만 넘긴다: 움직임은 이 함수에 안 들어온다(= 2초 지나면 보냄)
            check("S185 ① 마우스 움직임만(진짜 입력 9초 전) = 보냄", D(new() { Stale = true, SinceChange = 3, SinceInput = 9, SinceUserSend = 9 }) == "Send", "");
        }

        // ② 무엇을 — 지표면(옛것 · 없음) · 종단도(옛것 + 자리 + 안 겹침) · 횡단도(그린 적 + 자리 + 그릴 수 있음 + (옛것 · 새 지표면))
        {
            RoadAutoRules.Plan C(bool sS = false, bool sM = false, bool shS = false, bool shP = true, bool shO = false, bool xH = true, bool xS = false, bool xP = true, bool xR = false)
                => RoadAutoRules.Choose(sS, sM, shS, shP, shO, xH, xS, xP, xR);
            string P(RoadAutoRules.Plan p) => $"{(p.Surface ? "S" : "-")}{(p.Sheet ? "P" : "-")}{(p.Xsec ? "X" : "-")}" + (p.WhyNot.Length > 0 ? ":" + p.WhyNot : "");
            var cases = new (string Name, RoadAutoRules.Plan Got, string Want)[]
            {
                ("PVI 옮김(지표면 · 종단도 옛것 · 횡단도 있음)", C(sS: true, shS: true), "SPX"),
                ("지표면 없음(처음 뒤 지움)", C(sM: true, xH: false), "S--"),
                ("종단도만 옛것", C(shS: true), "-P-"),
                ("횡단도만 옛것", C(xS: true), "--X"),
                ("종단도 자리 모름 · 나머지 없음", C(shS: true, shP: false), "---:" + RoadAutoRules.WhySheetPlace),
                ("종단도 겹침 · 나머지 없음", C(shS: true, shO: true), "---:" + RoadAutoRules.WhySheetOverlap),
                ("종단도 자리 모름 + 지표면 옛것 = 지표면 · 횡단도는 함(까닭은 남김)", C(sS: true, shS: true, shP: false), "S-X:" + RoadAutoRules.WhySheetPlace),
                ("횡단도 자리 모름", C(xS: true, xP: false), "---:" + RoadAutoRules.WhyXsecPlace),
                ("횡단도 못 그림", C(xS: true, xR: true), "---:" + RoadAutoRules.WhyXsecRefused),
                ("횡단도 그린 적 없음 = 지표면만", C(sS: true, xH: false), "S--"),
                ("할 것 없음", C(), "---:" + RoadAutoRules.WhyNothing),
            };
            var bad = cases.Where(c => P(c.Got) != c.Want).Select(c => $"{c.Name}: {P(c.Got)} ≠ {c.Want}").ToList();
            check("S185 ② 무엇을(지표면 → 종단도 → 횡단도 · 자리 모름 · 겹침 · 못 그림 · 그릴 것 없음)", bad.Count == 0, string.Join(" | ", bad));
            check("S185 ② 글(로그 · 명령창)", C(sS: true, shS: true).What == "지표면 · 종단도 · 횡단도" && C(shS: true).What == "종단도" && !C().Any && C(sM: true).Any, C(sS: true, shS: true).What);
        }

        // ③ [PVI 넣기] 확대할 종단도 — 화면 가운데가 든 장 · 없으면 가장 가까운 장 · 깨진 상자 건너뜀 · 없으면 −1 / 확대 창(여유 · 가로세로 비)
        {
            var boxes = new List<(double X0, double Y0, double X1, double Y1)> { (0, 0, 400, 150), (500, 0, 900, 150), (double.NaN, 0, 1, 1), (1000, 0, 900, 50) };
            int a = ProfileSheetRules.NearestBox(boxes, 600, 70), b = ProfileSheetRules.NearestBox(boxes, 460, 70), c = ProfileSheetRules.NearestBox(boxes, 440, 70),
                d = ProfileSheetRules.NearestBox(boxes, -5000, 0), e = ProfileSheetRules.NearestBox(new List<(double, double, double, double)>(), 0, 0), f = ProfileSheetRules.NearestBox(boxes, 450, 70);
            check("S185 ③ 확대할 종단도(든 장 · 가까운 장 · 같으면 앞 장 · 깨진 상자 건너뜀 · 없으면 −1)", a == 1 && b == 1 && c == 0 && d == 0 && e == -1 && f == 0, $"{a} {b} {c} {d} {e} {f}");
            var (cx, cy, w, h) = ProfileSheetRules.ZoomWindow(500, 0, 900, 150, 16.0 / 9.0, 0.08);   // 넓은 종단도 — 너비가 정함
            var (cx2, cy2, w2, h2) = ProfileSheetRules.ZoomWindow(0, 0, 100, 400, 16.0 / 9.0, 0.08);  // 높은 상자 — 높이가 정함
            bool zOk = Math.Abs(cx - 700) < 1e-9 && Math.Abs(cy - 75) < 1e-9 && Math.Abs(w - 400 * 1.16) < 1e-6 && Math.Abs(w / h - 16.0 / 9.0) < 1e-9
                && Math.Abs(h2 - 400 * 1.16) < 1e-6 && Math.Abs(w2 / h2 - 16.0 / 9.0) < 1e-9 && Math.Abs(cx2 - 50) < 1e-9 && Math.Abs(cy2 - 200) < 1e-9;
            check("S185 ③ 확대 창(가운데 · 둘레 8 % · 화면 가로세로 비 유지)", zOk, $"{cx:F1},{cy:F1} {w:F1}×{h:F1} · {cx2:F1},{cy2:F1} {w2:F1}×{h2:F1}");
        }
    }
}
