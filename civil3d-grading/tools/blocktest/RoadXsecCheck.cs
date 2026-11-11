// ── S183 ★★[v111.5 · 계획_v111_4.md 3 · 4] 진입도로 횡단 — 합친 계획면(FirstOf) · 스타일 복사본 이름(축척 짝) · 검토선 이름 꼬리 측점 · 측점 구간 글 · 그린 횡단도 «바뀜» 판정 ──
//   만든 값만(현장 값 없음 — 공개 저장소)
using DH.Grading.Core;

static class RoadXsecCheck
{
    /// <summary>시험용 면 — 네모 [x0, x1] × [y0, y1] 안에서만 z = a + b·x.</summary>
    sealed class Box : IGroundSurface
    {
        readonly double _x0, _x1, _y0, _y1, _a, _b;
        public Box(double x0, double x1, double y0, double y1, double a, double b = 0) { _x0 = x0; _x1 = x1; _y0 = y0; _y1 = y1; _a = a; _b = b; }
        public bool TryGetElevation(double x, double y, out double z)
        {
            if (x < _x0 || x > _x1 || y < _y0 || y > _y1) { z = 0; return false; }
            z = _a + _b * x; return true;
        }
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S183 진입도로 횡단(합친 계획면 · 복사본 이름 · 측점 이름 · 구간 · 바뀜) ==");

        // ① FirstOfSurfaces — 앞 면에 표고가 있으면 그것 · 없으면 뒤 면 · 둘 다 없으면 false · null 건너뜀
        {
            var road = new Box(-5, 5, 0, 100, 110);           // 도로 자리(옆거리 ±5)
            var pad = new Box(-50, 50, 0, 100, 100, 0.1);     // 바탕면(기울어짐)
            var f = new FirstOfSurfaces(road, pad);
            bool a = f.TryGetElevation(0, 10, out double za);
            bool b = f.TryGetElevation(20, 10, out double zb);
            bool c = f.TryGetElevation(80, 10, out _);
            bool e = f.TryGetElevation(5, 10, out double ze);  // 경계 위 = 도로
            check("S183 ① FirstOf — 도로 자리 = 도로(110) · 밖 = 바탕(102) · 둘 다 밖 = 없음 · 경계 = 앞 면", a && za == 110 && b && Math.Abs(zb - 102) < 1e-12 && !c && e && ze == 110,
                $"{za} · {zb} · {c} · {ze}");
            var g = new FirstOfSurfaces(null, pad);
            var h = new FirstOfSurfaces();
            bool g1 = g.TryGetElevation(0, 10, out double zg);
            check("S183 ① FirstOf — null 건너뜀 · 빈 목록 = 없음", g1 && Math.Abs(zg - 100) < 1e-12 && !h.TryGetElevation(0, 0, out _), $"{zg}");
            check("S183 ① FirstOf.SourceAt — 0 · 1 · −1", f.SourceAt(0, 10) == 0 && f.SourceAt(20, 10) == 1 && f.SourceAt(80, 10) == -1, "");
            // 부지가 쓰는 LowerOfSurfaces와 다름 — 도로가 바탕보다 높아도(성토) 도로
            var lo = new LowerOfSurfaces(road, pad);
            lo.TryGetElevation(0, 10, out double zl);
            check("S183 ① FirstOf ≠ Lower — 성토 도로(110 > 100)도 도로 높이", zl == 100 && za == 110, $"lower {zl} · first {za}");
        }

        // ② 복사본 이름 — 축척 짝 · 주석 축척 없음 · 표지 · 원본 이름 되찾기
        {
            string s1 = RoadXsecRules.CloneSuffix(200, 500), s2 = RoadXsecRules.CloneSuffix(200, 0), s3 = RoadXsecRules.CloneSuffix(199.6, 500.4), s4 = RoadXsecRules.CloneSuffix(double.NaN, double.NaN);
            check("S183 ② 꼬리 «(진입도로 1:200@500)» · 주석 없음 «(진입도로 1:200)» · 반올림 · 숫자 아님", s1 == "(진입도로 1:200@500)" && s2 == "(진입도로 1:200)" && s3 == s1 && s4 == "(진입도로 1:0)", $"{s1} · {s2} · {s3} · {s4}");
            check("S183 ② 복사본 판정 — 횡단 복사본 · 측정 · 종단 구배 «(진입도로)» = 참 · 원본 = 거짓",
                RoadXsecRules.IsRoadClone("DH_횡단 뷰 스타일" + s1) && RoadXsecRules.IsRoadClone("DH_횡단 뷰 스타일" + RoadXsecRules.MeasureSuffix)
                && RoadXsecRules.IsRoadClone("DH_종단 뷰_수직 지오메트리_구배(진입도로)") && !RoadXsecRules.IsRoadClone("DH_횡단 뷰 스타일") && !RoadXsecRules.IsRoadClone(null), "");
            string k = RoadXsecRules.DictKey("DH-수량표" + s1);
            check("S183 ② 표 스타일 이름 — «:» 등 사전 이름에 못 쓰는 글자 → «-» · 나머지 그대로 · 복사본 판정 · 원본 이름은 그대로",
                k == "DH-수량표(진입도로 1-200@500)" && RoadXsecRules.DictKey("a<b>c/d\\e\"f;g?h*i|j,k=l`m") == "a-b-c-d-e-f-g-h-i-j-k-l-m"
                && RoadXsecRules.IsRoadClone(k) && RoadXsecRules.BaseName(k) == "DH-수량표" && RoadXsecRules.DictKey(null) == "", k);
            check("S183 ② 원본 이름 — 복사본 꼬리 떼기 · 원본은 그대로",
                RoadXsecRules.BaseName("DH-수량표" + s1) == "DH-수량표" && RoadXsecRules.BaseName("DH_횡단 뷰 스타일" + RoadXsecRules.MeasureSuffix) == "DH_횡단 뷰 스타일"
                && RoadXsecRules.BaseName("DH_횡단 뷰 스타일") == "DH_횡단 뷰 스타일" && RoadXsecRules.BaseName(null) == "", "");
        }

        // ③ 검토선 이름 꼬리 — 그룹 이름 · 모르는 그룹(«_No.» 꼬리) · 못 읽음 = 대신 이름
        {
            string a = RoadXsecRules.LabelOf("DH도로_1-검토_1_No.3+12.50", "DH도로_1-검토_1", "x");
            string b = RoadXsecRules.LabelOf("DH도로_1-검토_2_No.0", "다른그룹", "x");
            string c = RoadXsecRules.LabelOf("이상한이름", "DH도로_1-검토_1", "No.7");
            string d = RoadXsecRules.LabelOf(null, null, "No.1");
            string e = RoadXsecRules.LabelOf("DH도로_1-검토_1_", "DH도로_1-검토_1", "No.9");   // 꼬리 빔
            check("S183 ③ 이름 꼬리 — 그룹 앞머리 떼기 · «_No.» 꼬리 · 못 읽으면 대신 이름", a == "No.3+12.50" && b == "No.0" && c == "No.7" && d == "No.1" && e == "No.9", $"{a} · {b} · {c} · {d} · {e}");
        }

        // ④ 측점 구간 글 — 이어진 것은 «~» · 떨어진 것은 «·» · 하나 · 없음
        {
            var order = new List<string> { "No.0", "No.1", "No.2", "No.3", "No.4", "No.5" };
            string r1 = RoadXsecRules.Ranges(order, new HashSet<string> { "No.0", "No.1", "No.2", "No.5" });
            string r2 = RoadXsecRules.Ranges(order, new HashSet<string> { "No.3" });
            string r3 = RoadXsecRules.Ranges(order, new HashSet<string>());
            string r4 = RoadXsecRules.Ranges(order, new HashSet<string> { "No.9" });   // 목록에 없는 이름은 무시
            check("S183 ④ 구간 — «No.0 ~ No.2 · No.5» · «No.3» · 빈 글 · 없는 이름 무시", r1 == "No.0 ~ No.2 · No.5" && r2 == "No.3" && r3 == "" && r4 == "", $"{r1} | {r2} | {r3} | {r4}");
        }

        // ⑤ 측점 지문 · 그린 횡단도 «바뀜» 판정(측점 → 지표면 → 축척 차례)
        {
            var st = new List<(double, string)> { (0, "No.0"), (20, "No.1"), (31.25, "No.1+11.25") };
            string k0 = RoadXsecRules.StationsKey(st);
            string kSame = RoadXsecRules.StationsKey(new List<(double, string)>(st));
            string kTiny = RoadXsecRules.StationsKey(new List<(double, string)> { (0, "No.0"), (20.0004, "No.1"), (31.25, "No.1+11.25") });
            string kMove = RoadXsecRules.StationsKey(new List<(double, string)> { (0, "No.0"), (20.002, "No.1"), (31.25, "No.1+11.25") });
            string kName = RoadXsecRules.StationsKey(new List<(double, string)> { (0, "No.0"), (20, "No.1(전)"), (31.25, "No.1+11.25") });
            string kLess = RoadXsecRules.StationsKey(st.Take(2));
            check("S183 ⑤ 측점 지문 — 같으면 같음 · 0.4 mm 같음 · 2 mm · 이름 · 개수 바뀌면 다름",
                k0 == kSame && k0 == kTiny && k0 != kMove && k0 != kName && k0 != kLess && RoadXsecRules.StationsKey(null) == RoadXsecRules.StationsKey(Array.Empty<(double, string)>()), "");
            string stamp = RoadXsecRules.Stamp(k0, "1A2B", "SIG");
            string none = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 200, 200, 500, 500);
            string stc = RoadXsecRules.Stale(stamp, kMove, "1A2B", "SIG", 200, 200, 500, 500);
            string srf = RoadXsecRules.Stale(stamp, k0, "3C4D", "SIG", 200, 200, 500, 500);
            string sig = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG2", 200, 200, 500, 500);
            string gone = RoadXsecRules.Stale(stamp, k0, "", "SIG", 200, 200, 500, 500);
            string pin = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 0, 200, 500, 500);
            string ann = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 200, 200, 500, 1000);
            string both = RoadXsecRules.Stale(stamp, kMove, "3C4D", "SIG", 0, 200, 500, 1000);
            string old = RoadXsecRules.Stale("", "아무거나", "", "", 0, 0, 0, 0);
            string nan = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", double.NaN, 0, 500, 500);
            check("S183 ⑤ 바뀜 — 같음 = 빈 글 · 측점 → «종단이 수정» · 핸들 · 지문 · 지움 → «지표면이 다시» · 칸 · 주석 → «축척» · 여럿이면 측점 먼저 · 옛 기록 · NaN = 0",
                none == "" && stc == "종단이 수정되었습니다" && srf == "지표면이 다시 만들어졌습니다" && sig == srf && gone == srf
                && pin == "축척이 바뀌었습니다" && ann == pin && both == stc && old == "" && nan == "",
                $"«{none}» «{stc}» «{srf}» «{pin}» «{ann}» «{both}» «{old}» «{nan}»");
            // ★[v111.7.2 · JACK 1102] 횡단도 배치 — 그릴 때 배치 ≠ 지금 배치 → «횡단도 배치가 바뀌었습니다» · 옛 기록(−1) = 1×2로 그렸다고 봄 · 다른 까닭이 먼저
            string lSame = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 200, 200, 500, 500, 4, 4);
            string lMove = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 200, 200, 500, 500, 1, 4);
            string lOld1 = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 200, 200, 500, 500, -1, 1);
            string lOld4 = RoadXsecRules.Stale(stamp, k0, "1A2B", "SIG", 200, 200, 500, 500, -1, 4);
            string lFirst = RoadXsecRules.Stale(stamp, kMove, "1A2B", "SIG", 200, 200, 500, 500, 1, 4);
            check("S183 ⑤ 배치 — 같음 = 빈 글 · 1×2 → 2×3 = «횡단도 배치» · 옛 기록 = 1×2로 봄(1 같음 · 4 바뀜) · 측점이 먼저",
                lSame == "" && lMove == "횡단도 배치가 바뀌었습니다" && lOld1 == "" && lOld4 == lMove && lFirst == stc,
                $"«{lSame}» «{lMove}» «{lOld1}» «{lOld4}» «{lFirst}»");
        }

        // ⑥ ★★[v117.7.1 · JACK 1104 · 1111 · 화면 없는 시험 pvx1] 자동 축척이라도 한 번 그린 횡단도는 그린 축척 그대로 — 이 도로 설정(축척 칸 · 배치)이 바뀌었을 때만 다시 고름
        {
            double keep = RoadXsecRules.KeptScale(0, 0, 150, 73, 1, 1);          // 계획부지 다시 생성 · 부지 주석 축척 바뀜 — 자동 · 그린 1:150
            double fixNow = RoadXsecRules.KeptScale(200, 0, 150, 73, 1, 1);      // 이제 고정 칸 → 그 칸(지킴 없음)
            double wasFix = RoadXsecRules.KeptScale(0, 200, 200, 73, 1, 1);      // 고정 → 자동으로 되돌림 = 다시 고름
            double first = RoadXsecRules.KeptScale(0, 0, 0, 0, -1, 1);           // 처음 그림
            double noView = RoadXsecRules.KeptScale(0, 0, 150, 0, 1, 1);         // 기록에 뷰 없음
            double lay = RoadXsecRules.KeptScale(0, 0, 150, 73, 1, 4);           // 진입도로 배치 바뀜 = 다시 고름
            double layOld = RoadXsecRules.KeptScale(0, 0, 150, 73, -1, 1);       // 옛 기록(배치 안 적음) = 1×2로 봄
            double layOld4 = RoadXsecRules.KeptScale(0, 0, 150, 73, -1, 4);
            double nan = RoadXsecRules.KeptScale(double.NaN, double.NaN, 150, 73, 1, 1);
            double bad = RoadXsecRules.KeptScale(0, 0, double.NaN, 73, 1, 1);
            check("S183 ⑥ 그린 축척 지킴 — 자동 · 그린 1:150 = 150 · 고정 칸 = 0 · 고정에서 자동 = 0 · 처음 · 뷰 없음 = 0 · 배치 바뀜 = 0 · 옛 기록 = 1×2로 봄 · NaN 칸 = 자동 · NaN 축척 = 0",
                keep == 150 && fixNow == 0 && wasFix == 0 && first == 0 && noView == 0 && lay == 0 && layOld == 150 && layOld4 == 0 && nan == 150 && bad == 0,
                $"{keep} · {fixNow} · {wasFix} · {first} · {noView} · {lay} · {layOld} · {layOld4} · {nan} · {bad}");
        }
    }
}
