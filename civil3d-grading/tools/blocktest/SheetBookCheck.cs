// ── S190 SB ★★★[v112.0 · 계획_도면화.md Step 1] 도면화 — 모형 장 알아보기 · 표제란 글 · 페이지 설정 이름 · 이름 고리 · 평면 축척 · 글자 맞춤 · 도곽 검출 · 토공량 영역 거름 ──
//   만든 값만(현장 값 없음 — 공개 저장소) · 가짜 좌표 X≈200,000 · Y≈550,000
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using static DH.Grading.Core.SheetBookRules;

static class SheetBookCheck
{
    static readonly double[] Ladder =
        { 20, 25, 50, 75, 80, 100, 120, 150, 200, 250, 300, 500, 600, 750, 1000, 1200, 1250, 2000, 2500, 3000, 5000 };

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S190 SB 도면화(장 알아보기 · 표제란 · 페이지 설정 · 평면 축척 · 글자 맞춤 · 도곽 검출 · 영역 거름) ==");
        const double X = 200_000, Y = 550_000;

        // ① 장 알아보기
        {
            double s = 0.2;   // 1:200
            RectIn Outer(double x, double y, double sc, bool xs = false, string fk = "", double tagSc = 0, double tagAnno = 0, string road = "", string rk = "", double hMul = 1)
                => new(x, y, x + PaperW * sc / 1000, y + PaperH * sc / 1000 * hMul, xs, fk, tagSc, tagAnno, road, rk);
            var rects = new List<RectIn>
            {
                Outer(X + 168.2, Y, 200),                                     // 부지 종단 2장째(먼저 넣어도 x 차례)
                Outer(X, Y, 200),                                             // 부지 종단 1장째(표식 없음)
                new(X + 25 * s, Y + 50 * s, X + 821 * s, Y + 454 * s, false, "", 0, 0, "", ""),       // 안쪽(비 다름 — 무시)
                Outer(X, Y, 200),                                             // 같은 자리 두 번 — 하나로
                Outer(X, Y - 500, 200, true, "XSEC", 200, 500),               // 부지 횡단(표식 · 주석 1:500)
                Outer(X + 168.2, Y - 500, 200, true, "XSEC", 200, 500),
                Outer(X, Y + 500, 500, false, "PROF", 500, 500, "1FD12", "DH-도곽범위(모형)", hMul: 1.3),   // 진입도로 종단 — 그래프가 높아 비가 달라도 표식으로
                Outer(X - 1000, Y + 500, 500, false, "PROF", 500, 500, "2AB01", "DH-도곽범위(모형)"),     // 다른 노선(이름 차례가 앞)
                Outer(X, Y + 900, 200, true, "XSEC", 200, 200, "1FD12", "XSEC"),                        // 진입도로 횡단
                Outer(X + 5000, Y + 1500, 213),                               // 사다리 밖(1:213) — 그대로 + ⚠ (x가 가장 커 끝)
            };
            var warns = new List<string>();
            var names = new Dictionary<string, string> { ["1FD12"] = "DH도로_2", ["2AB01"] = "DH도로_1" };
            var sh = Recognize(rects, Ladder, 300, h => names.TryGetValue(h, out var n) ? n : h, warns);
            var prof = sh.Where(q => q.Kind == Kind.Prof).ToList();
            check("S190 ① 부지 종단 — 2장 · x 차례 · 1:200 · 안쪽 사각형 · 겹친 사각형 무시", prof.Count == 3 && prof[0].X0 == X && Math.Abs(prof[1].X0 - (X + 168.2)) < 1e-9 && prof[0].Scale == 200,
                  string.Join(" · ", prof.Select(q => $"{q.X0 - X:F1}/{q.Scale}")));
            check("S190 ① 표식 없는 장 = 지금 주석 축척(1:300) + ⚠ 옛 장", prof[0].Anno == 300 && !prof[0].Tagged && warns.Any(w => w.Contains("옛 장")), string.Join(" | ", warns));
            check("S190 ① 사다리 밖(1:213) — 그대로 + ⚠", prof.Any(q => Math.Abs(q.Scale - 213.0) < 0.01) && warns.Any(w => w.Contains("사다리")), "");
            var xs = sh.Where(q => q.Kind == Kind.Xsec).ToList();
            check("S190 ① 부지 횡단 — 표식 · 그린 때 주석 1:500", xs.Count == 2 && xs.All(q => q.Tagged && q.Anno == 500 && q.Scale == 200), "");
            var rp = sh.Where(q => q.Kind == Kind.RProf).ToList();
            check("S190 ① 진입도로 종단 — 노선 이름 차례(DH도로_1 → _2) · 비가 달라도 표식으로", rp.Count == 2 && rp[0].Road == "2AB01" && rp[1].Road == "1FD12", string.Join(",", rp.Select(q => q.Road)));
            check("S190 ① 진입도로 횡단 — 도로 표식 «XSEC»", sh.Count(q => q.Kind == Kind.RXsec) == 1, "");
            check("S190 ① 탭 차례 = 종류 차례(부지종단 → 부지횡단 → 도로종단 → 도로횡단)", sh.Select(q => (int)q.Kind).SequenceEqual(sh.Select(q => (int)q.Kind).OrderBy(v => v)), "");
            var d = prof[0].Draw;
            check("S190 ① 그림 자리 = 바깥 + (25, 50)·s · 796 × 524 ·s", Math.Abs(d.X0 - (X + 5)) < 1e-9 && Math.Abs(d.Y0 - (Y + 10)) < 1e-9 && Math.Abs(d.X1 - d.X0 - 159.2) < 1e-9 && Math.Abs(d.Y1 - d.Y0 - 104.8) < 1e-9, $"{d}");
            check("S190 ① 장 원점 — 왼 → 오 맞붙임(841 mm씩)", SheetOrigin(2) == (1682.0, 0.0), "");
        }

        // ② 표제란 글
        {
            check("S190 ② 도면명 — 한 장이면 번호 없음 · 둘 이상 « (i/n)»", DrawingTitle("배수지", "토공 횡단면도", 1, 5) == "배수지 토공 횡단면도 (1/5)" && DrawingTitle("", "토공 계획평면도", 1, 1) == "토공 계획평면도"
                  && DrawingTitle(null, " 토공 종단면도 ", 2, 2) == "토공 종단면도 (2/2)", "");
            check("S190 ② 도면번호 — 접두 + 세 자리 0 채움 · 이어 매김", DrawingNo("C1-", 2, 0) == "C1-002" && DrawingNo("C1-", 2, 6) == "C1-008" && DrawingNo("C-", 1, 999) == "C-1000" && DrawingNo(null, 0, 0) == "000", "");
            check("S190 ② 축척 — 평면 · 횡단 «1:600» · 종단 수직과장 5 → «H=1:1000, V=1:200» · 과장 없음 «1:200»",
                  ScaleText(Kind.Plan, 600) == "1:600" && ScaleText(Kind.Xsec, 200, 5) == "1:200" && ScaleText(Kind.Prof, 1000, 5) == "H=1:1000, V=1:200"
                  && ScaleText(Kind.RProf, 200, 1) == "1:200" && ScaleText(Kind.Prof, 300, 1.5) == "H=1:300, V=1:200", ScaleText(Kind.Prof, 1000, 5));
            check("S190 ② 날짜 — 비면 이번 달 «yyyy.MM» · 넣으면 그대로", DateText("", new DateTime(2026, 11, 3)) == "2026.11" && DateText(" 2026.07 ", DateTime.Now) == "2026.07", "");
            var ps = new[] { PageSetupName(Kind.Xsec, 1, 5, "A1"), PageSetupName(Kind.Xsec, 5, 5, "A3"), PageSetupName(Kind.Plan, 1, 1, "A1") };
            check("S190 ② 페이지 설정 이름 — «/» 없음 · 한 장이면 번호 없음", ps[0] == "DH 부지횡단 1-5 A1" && ps[1] == "DH 부지횡단 5-5 A3" && ps[2] == "DH 평면 A1" && ps.All(p => !p.Contains('/')), string.Join(" · ", ps));
            var taken = new HashSet<string> { "부지 종단면도", "부지 종단면도(DH)" };
            string u1 = UniqueName("부지 종단면도", taken.Contains);
            taken.Add(u1);
            string u2 = UniqueName("부지 종단면도", taken.Contains);
            check("S190 ② 이름 겹침 고리 — 그대로 → (DH) → (DH2) → (DH3)", u1 == "부지 종단면도(DH2)" && u2 == "부지 종단면도(DH3)" && UniqueName("전체 계획평면도", taken.Contains) == "전체 계획평면도", $"{u1} · {u2}");
            check("S190 ② 종류 코드 왕복", Kinds.All(k => FromCode(Code(k)) == k) && FromCode("X") == null, "");
        }

        // ③ 평면 축척 · 자리
        {
            var lay = PlanLayout(new double[] { 25, 50, 821, 574 });
            check("S190 ③ 평면 자리 — 뷰포트 25–681 × 50–534 · 띠 681–821 · 제목 40", lay.Viewport.SequenceEqual(new double[] { 25, 50, 681, 534 }) && lay.Band[0] == 681 && lay.Title[1] == 534, string.Join(",", lay.Viewport));
            var a = PlanScale(250, 150, 656, 484);          // 250 m → 1:500이면 500 mm · 1:300이면 833 mm
            var b = PlanScale(130, 300, 656, 484);          // 세로가 정함 300 m → 1:1000이면 300 mm · 1:600이면 500 mm
            var c = PlanScale(5000, 100, 656, 484);
            var dd = PlanScale(100, 100, 656, 484, 1200);
            check("S190 ③ 평면 축척 자동 — 들어가는 가장 작은 사다리 값 · 한 단계 작으면 안 들어감", a == (500, true) && b == (1000, true) && !(300.0 * 1000 / 600 <= 484), $"{a} · {b}");
            check("S190 ③ 1:5000에도 안 들면 5000 + 넘침 · 고정 축척은 그대로", c == (5000, false) && dd.Scale == 1200 && dd.Fits, $"{c} · {dd}");
        }

        // ④ 글자 칸 맞춤
        {
            var f1 = FitText(60, 4, 115, 11.98);
            var f2 = FitText(140, 4, 115, 11.98);     // 폭 0.81
            var f3 = FitText(240, 4, 115, 11.98);     // 0.7로도 넘침 → 높이 줄임
            check("S190 ④ 들어가면 그대로", f1 == (4, 1.0), $"{f1}");
            check("S190 ④ 넘치면 폭 배율 0.7까지", Math.Abs(f2.WidthFactor - 113.0 / 140) < 1e-9 && f2.H == 4, $"{f2}");
            check("S190 ④ 0.7로도 넘치면 높이 줄임(칸 폭 − 2 mm 안)", f3.WidthFactor == 0.7 && f3.H < 4 && 240 * f3.H / 4 * 0.7 <= 113 + 1e-9, $"{f3}");
            check("S190 ④ 높이는 칸 높이 − 1 mm를 안 넘음", FitText(10, 4, 45, 4).H == 3, "");
            // 정렬 글 — 삽입점은 칸 밖 · 경계상자 가운데는 안
            check("S190 ④ 글 지우기 판정 — 경계상자 가운데(오른쪽 정렬 글 삽입점이 칸 밖이어도)", CenterIn(500, 27, 525, 30, new[] { 490, 26.02, 535, 32.02 }) && !CenterIn(400, 27, 470, 30, new[] { 490, 26.02, 535, 32.02 }), "");
        }

        // ⑤ 도곽 검출
        {
            var simple = SimpleFrame();
            List<Seg> Rect(double x0, double y0, double x1, double y1) => new() { new(x0, y0, x1, y0), new(x1, y0, x1, y1), new(x1, y1, x0, y1), new(x0, y1, x0, y0) };
            List<Seg> FrameSegs(double ox, double oy, double k, bool split)
            {
                var l = new List<Seg>();
                l.AddRange(Rect(0, 0, 841, 594)); l.AddRange(Rect(25, 20, 821, 574));
                var (lines, _) = SimpleFrameDrawing();
                foreach (var q in lines) l.Add(new(q.X0, q.Y0, q.X1, q.Y1));
                if (split)
                {
                    // 조각 난 선(바깥 아래 변을 셋 · 테두리 왼 변을 둘로) + 끝점 0.3 mm 어긋남
                    l.RemoveAll(s => s.Y0 == 0 && s.Y1 == 0 || s.X0 == 25 && s.X1 == 25 && s.Y0 != s.Y1 && Math.Abs(s.Y1 - s.Y0) > 500);
                    l.Add(new(0, 0, 300, 0)); l.Add(new(300.2, 0, 600, 0)); l.Add(new(600, 0, 841, 0));
                    l.Add(new(25, 20, 25, 300)); l.Add(new(25, 300.3, 25, 574));
                }
                return l.Select(s => new Seg(ox + s.X0 / k, oy + s.Y0 / k, ox + s.X1 / k, oy + s.Y1 / k)).ToList();
            }
            List<Txt> T(double ox, double oy, double k, params (string T, double X, double Y, double W)[] t)
                => t.Select(q => new Txt(q.T, ox + (q.X - q.W / 2) / k, oy + (q.Y - 1.5) / k, ox + (q.X + q.W / 2) / k, oy + (q.Y + 1.5) / k)).ToList();
            var labels = new (string, double, double, double)[]
            {
                ("프로젝트명", 37.5, 27.5, 12), ("도면명", 407.5, 44, 9), ("도면번호", 477.5, 35, 12), ("도면축척", 407.5, 29, 12), ("구도면번호", 407.5, 35, 15),
                ("편철번호", 477.5, 29, 12), ("날짜", 572.5, 23, 6), ("내  용", 620, 23, 10), ("2026.01", 572.5, 29, 14), ("가상 사업 이름 기본 및 실시설계 용역", 117.5, 25, 90),
            };
            bool Near(double[] a, double[] b, double tol) => a.Length == 4 && b.Length == 4 && Enumerable.Range(0, 4).All(i => Math.Abs(a[i] - b[i]) <= tol);
            string Cells(FrameDef f) => string.Join(" · ", f.Cells.Select(kv => $"{kv.Key}[{string.Join(",", kv.Value.Rect.Select(v => v.ToString("0.#")))}]"));
            // K-water 꼴(도곽 단위 = mm · 원점 0)
            var d1 = DetectFrame(FrameSegs(0, 0, 1, false), T(0, 0, 1, labels), Array.Empty<AttDef>(), "시험", out string e1);
            bool okK = d1 != null && CellNames.All(c => d1.Cells.ContainsKey(c) && Near(d1.Cells[c].Rect, simple.Cells[c].Rect, 1.0));
            check("S190 ⑤ K-water 꼴 — 다섯 칸이 손으로 쓴 정의와 1 mm 안 · 테두리 · 그림 자리", okK && Near(d1!.Border, simple.Border, 1) && Near(d1.DrawArea, simple.DrawArea, 1) && d1.Notes.Count == 0,
                  e1 + (d1 == null ? "" : Cells(d1) + " · da " + string.Join(",", d1.DrawArea) + " · " + string.Join("|", d1.Notes)));
            // 같은 도곽을 미터(1/1000)로 · 다른 원점 · 조각 난 선
            var d2 = DetectFrame(FrameSegs(X, Y, 1000, true), T(X, Y, 1000, labels), Array.Empty<AttDef>(), "m", out string e2);
            bool okM = d2 != null && Math.Abs(d2.K - 1000) < 1e-6 && Math.Abs(d2.Origin[0] - X) < 1e-9 && CellNames.All(c => d2.Cells.ContainsKey(c) && Near(d2.Cells[c].Rect, simple.Cells[c].Rect, 1.0));
            check("S190 ⑤ 미터 단위 도곽(k = 1000) · 원점 · 선 넷 · 조각 난 선 → 같은 칸", okM && Near(d2!.Border, simple.Border, 1), e2 + (d2 == null ? "" : $"k {d2.K:0.###} · " + Cells(d2)));
            // 이름표 오른쪽이 또 이름표 → 위(«날짜» → 위 칸 «2026.01»)
            check("S190 ⑤ 오른쪽이 이름표(«내용»)면 위 칸", d1 != null && Near(d1.Cells["날짜"].Rect, new[] { 560, 26.02, 585, 32.02 }, 0.1), d1 == null ? "" : string.Join(",", d1.Cells["날짜"].Rect));
            // 비가 다른 도곽 거절
            var bad = DetectFrame(new List<Seg> { new(0, 0, 594, 0), new(594, 0, 594, 841), new(594, 841, 0, 841), new(0, 841, 0, 0) }, new List<Txt>(), Array.Empty<AttDef>(), "세로", out string e3);
            check("S190 ⑤ A 계열 가로가 아니면 거절", bad == null && e3.Contains("A 계열"), e3);
            // 표제란 띠 없는 도곽 → 그림 자리 = 테두리 · 칸 못 찾음 다섯
            var d4 = DetectFrame(Rect(0, 0, 841, 594).Concat(Rect(25, 20, 821, 574)).ToList(), new List<Txt>(), Array.Empty<AttDef>(), "빈", out _);
            check("S190 ⑤ 표제란 띠 없음 → 그림 자리 = 테두리 · «칸 못 찾음» 딱지", d4 != null && Near(d4.DrawArea, new double[] { 25, 20, 821, 574 }, 0.01) && d4.Found.Count == 0 && d4.Notes.Any(n => n.StartsWith("칸 못 찾음")),
                  d4 == null ? "" : string.Join("|", d4.Notes));
            // 세로 표제란(오른쪽 띠 120 mm) · «TITLE» 머리글(같은 칸) · «SCALE» 오른쪽 값
            var v = Rect(0, 0, 841, 594).Concat(Rect(20, 20, 821, 574)).ToList();
            v.Add(new(701, 20, 701, 574));                                   // 띠 왼선
            v.Add(new(701, 200, 821, 200)); v.Add(new(701, 120, 821, 120)); v.Add(new(701, 60, 821, 60));
            v.Add(new(741, 20, 741, 60));
            var vt = new List<Txt> { new("TITLE", 704, 192, 716, 196), new("SCALE", 705, 38, 735, 42), new("DWG. NO.", 705, 88, 735, 92) };
            var d5 = DetectFrame(v, vt, Array.Empty<AttDef>(), "세로띠", out _);
            check("S190 ⑤ 세로 표제란 — 그림 자리 오른쪽 = 띠 왼선 · TITLE 머리글(위) = 같은 칸의 그 아래 · SCALE 오른쪽 값 칸",
                  d5 != null && Math.Abs(d5.DrawArea[2] - 701) < 0.01 && Near(d5.Cells["도면명"].Rect, new double[] { 701, 120, 821, 191.5 }, 0.01) && Near(d5.Cells["축척"].Rect, new double[] { 741, 20, 821, 60 }, 0.01),
                  d5 == null ? "" : Cells(d5) + " · da " + string.Join(",", d5.DrawArea));
            // 넓은 칸 왼쪽 이름표(«DRAWING NO.» — 칸 가르는 세로선이 그 줄엔 없음) → 이름표 오른쪽 · 같은 칸 이름 둘(개정 표 머리 «DATE» · 표제 «DATE 2026. 05») → 값이 든 쪽
            var h = Rect(0, 0, 841, 594).Concat(Rect(35, 20, 816, 574)).ToList();
            h.Add(new(35, 55, 816, 55));
            h.Add(new(170, 20, 170, 55)); h.Add(new(272, 20, 272, 55)); h.Add(new(170, 48, 272, 48)); h.Add(new(200, 41, 200, 48)); h.Add(new(170, 41, 272, 41));
            h.Add(new(744, 20, 744, 55)); h.Add(new(744, 34, 816, 34)); h.Add(new(744, 41, 816, 41)); h.Add(new(768.5, 41, 768.5, 55)); h.Add(new(744, 48, 816, 48));
            var ht = new List<Txt> { new("DATE", 186, 49, 196, 54), new("REVISED", 210, 49, 230, 54), new("DRAWING NO.", 746, 25, 764, 29), new("DATE", 746, 49.5, 756, 53.5), new("2026. 05", 775, 49.5, 795, 53.5), new("SCALE", 746, 42.5, 758, 46.5) };
            var d7 = DetectFrame(h, ht, Array.Empty<AttDef>(), "표제칸꼴", out _);
            check("S190 ⑤ 넓은 칸 왼쪽 이름표 → 그 오른쪽 · 같은 이름표 둘이면 값(숫자)이 든 칸 · SCALE 오른쪽",
                  d7 != null && d7.Cells.TryGetValue("도면번호", out var dn) && Math.Abs(dn.Rect[0] - 765) < 0.01 && Math.Abs(dn.Rect[2] - 816) < 0.01
                  && d7.Cells.TryGetValue("날짜", out var dd7) && Math.Abs(dd7.Rect[0] - 768.5) < 0.01 && d7.Cells.TryGetValue("축척", out var ds7) && Math.Abs(ds7.Rect[0] - 768.5) < 0.01,
                  d7 == null ? "" : Cells(d7));
            // ATTDEF 표제란 — 태그 다섯 → 칸 다섯 · 낱말 표 밖 태그는 안 이음
            var atts = new[] { new AttDef("DWG_TITLE", "도면명?", 500, 40), new AttDef("DWG_NO", "", 500, 33), new AttDef("SCALE", "", 430, 28), new AttDef("DATE", "", 570, 28), new AttDef("PROJECT", "", 100, 25), new AttDef("CHECKED_BY", "검토자", 700, 25) };
            var d6 = DetectFrame(Rect(0, 0, 841, 594).Concat(Rect(25, 20, 821, 574)).ToList(), new List<Txt>(), atts, "속성", out _);
            check("S190 ⑤ 속성 정의 표제란 — 태그 다섯 → 칸 다섯 · 모르는 태그는 안 이음", d6 != null && d6.Attrs.Count == 5 && !d6.Attrs.ContainsKey("CHECKED_BY") && d6.Attrs["DWG_TITLE"] == "도면명" && d6.Found.Count == 5,
                  d6 == null ? "" : string.Join(",", d6.Attrs.Select(kv => kv.Key + "=" + kv.Value)));
            // 정의 json 왕복 · 해시가 바뀜
            var js = simple.ToJson(); var back = FrameDef.FromJson(js);
            var changed = FrameDef.FromJson(js)!; changed.Cells["도면명"].H = 5;
            check("S190 ⑤ 정의 json 왕복 · 칸이 바뀌면 해시가 다름", back != null && back.ToJson() == js && back.JsonHash4() == simple.JsonHash4() && changed.JsonHash4() != simple.JsonHash4(), simple.JsonHash4());
            check("S190 ⑤ 그림 자리 작음 판정", !simple.DrawAreaSmall && d5!.DrawAreaSmall, "");
        }

        // ⑥ 토공량 영역 거름
        {
            // 원지반 z = 100(평면) · 계획 z = 100 + 0.01·(x − X) − 0.5(절토 · 성토 섞임) · 40 × 40 m 칸 세모
            var gf = GeometryFactory.Default;
            List<(Point3, Point3, Point3)> Grid(Func<double, double, double> z, double step)
            {
                var l = new List<(Point3, Point3, Point3)>();
                for (double x = 0; x < 100 - 1e-9; x += step)
                    for (double y = 0; y < 60 - 1e-9; y += step)
                    {
                        Point3 P(double a, double b) => new(X + a, Y + b, z(a, b));
                        l.Add((P(x, y), P(x + step, y), P(x + step, y + step)));
                        l.Add((P(x, y), P(x + step, y + step), P(x, y + step)));
                    }
                return l;
            }
            var bas = Grid((a, b) => 100, 20);
            var cmp = Grid((a, b) => 100 + 0.02 * a - 1.0, 10);
            var all = TinVolume.Compute(cmp, bas);
            // 도로 자리 — 기운 띠(세모를 비스듬히 가름)
            var road = gf.CreatePolygon(new[] { new Coordinate(X + 13.3, Y - 5), new Coordinate(X + 37.7, Y - 5), new Coordinate(X + 61.1, Y + 65), new Coordinate(X + 41.9, Y + 65), new Coordinate(X + 13.3, Y - 5) });
            var inR = TinVolume.Compute(cmp, bas, road, true);
            var outR = TinVolume.Compute(cmp, bas, road, false);
            double dC = inR.Cut + outR.Cut - all.Cut, dF = inR.Fill + outR.Fill - all.Fill, dA = inR.Area + outR.Area - all.Area;
            check("S190 ⑥ 안 + 밖 = 전체(절토 · 성토 · 넓이 1e-6 ㎥ 안) · 칸마다 0 이상", Math.Abs(dC) < 1e-6 && Math.Abs(dF) < 1e-6 && Math.Abs(dA) < 1e-6 && inR.Cut >= 0 && inR.Fill >= 0 && outR.Cut >= 0 && outR.Fill >= 0,
                  $"전체 {all.Cut:F4}/{all.Fill:F4} · 안 {inR.Cut:F4}/{inR.Fill:F4} · 밖 {outR.Cut:F4}/{outR.Fill:F4}");
            // 해석값 — 직사각 거름(x 30~70 · 전체 y) 안: d = 0.02x − 1 → 0선 x = 50 → 성토 ∫(50~70) = 0.01·20²·60 = 240 · 절토 ∫(30~50) = 240
            var box = gf.CreatePolygon(new[] { new Coordinate(X + 30, Y - 1), new Coordinate(X + 70, Y - 1), new Coordinate(X + 70, Y + 61), new Coordinate(X + 30, Y + 61), new Coordinate(X + 30, Y - 1) });
            var bx = TinVolume.Compute(cmp, bas, box, true);
            check("S190 ⑥ 걸친 세모 정확(해석값 240 · 240 ㎥ · 넓이 2400 ㎡)", Math.Abs(bx.Fill - 240) < 1e-6 && Math.Abs(bx.Cut - 240) < 1e-6 && Math.Abs(bx.Area - 2400) < 1e-6, $"{bx.Cut:F6}/{bx.Fill:F6}/{bx.Area:F6}");
            var none = TinVolume.Compute(cmp, bas, null, true);
            check("S190 ⑥ 거름 없음 = 옛 셈 그대로(같은 값)", none == all, "");
            var far = gf.CreatePolygon(new[] { new Coordinate(X + 500, Y), new Coordinate(X + 510, Y), new Coordinate(X + 510, Y + 10), new Coordinate(X + 500, Y) });
            var fIn = TinVolume.Compute(cmp, bas, far, true); var fOut = TinVolume.Compute(cmp, bas, far, false);
            check("S190 ⑥ 안 겹치는 다각형 — 안 0 · 밖 = 전체", fIn.Cut == 0 && fIn.Fill == 0 && Math.Abs(fOut.Cut - all.Cut) < 1e-9 && Math.Abs(fOut.Fill - all.Fill) < 1e-9, "");
        }
    }
}
