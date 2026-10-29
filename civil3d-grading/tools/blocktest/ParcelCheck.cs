// ── S179 ★★[v109.1 · 계획_v109.md 3 · 5 · JACK 1028 «지적 VWorld · 국유지/사유지 구분»] VWorld 지적 · 소유 읽기 · 합치기 · 자르기 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 응답 꼴은 1029 실측(서울시청 둘레)과 같은 짜임
using System.Globalization;
using DH.Grading.Core;

static class ParcelCheck
{
    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>네모 고리 GeoJSON «[[x0,y0],[x1,y0],[x1,y1],[x0,y1],[x0,y0]]».</summary>
    static string Sq(double x0, double y0, double x1, double y1) =>
        $"[[{F(x0)},{F(y0)}],[{F(x1)},{F(y0)}],[{F(x1)},{F(y1)}],[{F(x0)},{F(y1)}],[{F(x0)},{F(y0)}]]";

    static string Feat(string pnu, string jibun, string rings, string extra = "") =>
        "{\"type\":\"Feature\",\"properties\":{\"pnu\":\"" + pnu + "\",\"jibun\":\"" + jibun + "\",\"bonbun\":\"\",\"bubun\":\"\",\"addr\":\"가상시 가상동 " + jibun + "\""
        + extra + "},\"geometry\":{\"type\":\"MultiPolygon\",\"coordinates\":[" + rings + "]},\"id\":\"x\"}";

    static string Page(int total, int pages, int cur, params string[] feats) =>
        "{\"response\" : {\"service\" : {\"name\" : \"data\"}, \"status\" : \"OK\", \"record\" : {\"total\" : \"" + total + "\", \"current\" : \"" + feats.Length + "\"}, "
        + "\"page\" : {\"total\" : \"" + pages + "\", \"current\" : \"" + cur + "\", \"size\" : \"1000\"}, \"result\" : {\"featureCollection\" : {\"type\":\"FeatureCollection\",\"features\":["
        + string.Join(",", feats) + "]}}}}";

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S179 VWorld 지적 · 소유(읽기 · 합치기 · 자르기) ==");

        // ① 상자 나누기 — 한 장 ≤ 1.9 km² · 빈틈 · 겹침 없음(넓이 합 = 전체 · 모서리 공유)
        {
            var b = new VWorldParcels.Box(200000, 500000, 203500, 501200);   // 3.5 × 1.2 km = 4.2 km²
            var bs = VWorldParcels.SplitBox(b);
            double sum = bs.Sum(q => q.Area), mx = bs.Max(q => q.Area);
            bool edges = bs.All(q => q.X0 >= b.X0 && q.X1 <= b.X1 && q.Y0 >= b.Y0 && q.Y1 <= b.Y1);
            // 겹침 없음 — 두 상자 겹친 넓이 0
            double ov = 0;
            for (int i = 0; i < bs.Count; i++)
                for (int j = i + 1; j < bs.Count; j++)
                    ov += Math.Max(0, Math.Min(bs[i].X1, bs[j].X1) - Math.Max(bs[i].X0, bs[j].X0)) * Math.Max(0, Math.Min(bs[i].Y1, bs[j].Y1) - Math.Max(bs[i].Y0, bs[j].Y0));
            check("S179 ① 상자 나누기(≤ 1.9 km² · 합 = 전체 · 겹침 0)", mx <= VWorldParcels.MaxBoxArea && Math.Abs(sum - b.Area) < 1e-3 && ov < 1e-6 && edges && bs.Count >= 3,
                $"{bs.Count}장 · 최대 {mx / 1e6:F3} km² · 합 차 {sum - b.Area:F4} · 겹침 {ov:F4}");
            var small = VWorldParcels.SplitBox(new VWorldParcels.Box(0, 0, 500, 400));
            var bad = VWorldParcels.SplitBox(new VWorldParcels.Box(0, 0, 0, 400));
            var q4 = VWorldParcels.Quarter(new VWorldParcels.Box(0, 0, 100, 60));
            check("S179 ① 작은 상자 = 한 장 · 빈 상자 = 없음 · 넷 나누기", small.Count == 1 && bad.Count == 0 && Math.Abs(q4.Sum(q => q.Area) - 6000) < 1e-9, $"{small.Count} · {bad.Count} · {q4.Sum(q => q.Area)}");
        }

        // ② 지번 · 지목 — 공백 섞임 · 붙음 · 산 · 지목 없음 · 지목 아닌 끝 글자 · PNU
        {
            var cases = new (string Raw, string J, string M)[]
            {
                ("13-2 대", "13-2", "대"), ("25-5대", "25-5", "대"), ("산12-1임", "산12-1", "임"), ("3종", "3", "종"), ("192-41도", "192-41", "도"),
                ("645", "645", ""), ("  7-1 전 ", "7-1", "전"), ("", "", ""), ("12블", "12블", ""),
            };
            var bad = cases.Where(c => VWorldParcels.SplitJibun(c.Raw) != (c.J, c.M)).Select(c => $"«{c.Raw}»→{VWorldParcels.SplitJibun(c.Raw)}").ToList();
            check("S179 ② 지번/지목 가르기(9꼴)", bad.Count == 0, string.Join(" · ", bad));
            check("S179 ② 지목 이름 28종 · 코드 → 부호", VWorldParcels.JimokName("과") == "과수원" && VWorldParcels.JimokName("잡") == "잡종지" && VWorldParcels.JimokName("x") == ""
                && VWorldParcels.JimokSymOfCode("08") == "대" && VWorldParcels.JimokSymOfCode("28") == "잡" && VWorldParcels.JimokSymOfCode("29") == "" && VWorldParcels.JimokSymOfCode(null) == "",
                $"{VWorldParcels.JimokName("과")} · {VWorldParcels.JimokSymOfCode("08")}");
            var pnus = new (string P, string J)[]
            {
                ("1111010100100130002", "13-2"), ("1111010100200120001", "산12-1"), ("1111010100100030000", "3"),
                ("111101010010013000", ""), ("1111010100300130002", ""), ("1111010100100000000", ""), ("11110101001001300ab", ""),
            };
            var badP = pnus.Where(c => VWorldParcels.JibunFromPnu(c.P) != c.J).Select(c => $"{c.P}→«{VWorldParcels.JibunFromPnu(c.P)}»").ToList();
            check("S179 ② PNU → 지번(토지 · 임야 · 부번 0 · 꼴 틀림 넷)", badP.Count == 0, string.Join(" · ", badP));
        }

        // ③ 소유 구분 코드표 · 국공유지 판정 · 섞임
        {
            bool codes = VWorldParcels.KindOfCode("02") == VWorldParcels.OwnKind.Public && VWorldParcels.KindOfCode("04") == VWorldParcels.OwnKind.Public
                && VWorldParcels.KindOfCode("05") == VWorldParcels.OwnKind.Public
                && new[] { "01", "03", "06", "07", "08", "09" }.All(c => VWorldParcels.KindOfCode(c) == VWorldParcels.OwnKind.Private)
                && VWorldParcels.KindOfCode(null) == VWorldParcels.OwnKind.Unknown && VWorldParcels.KindOfCode("") == VWorldParcels.OwnKind.Unknown
                && VWorldParcels.KindOfCode("10") == VWorldParcels.OwnKind.Unknown;
            check("S179 ③ 코드표(국공유 02·04·05 · 사유 01·03·06~09 · 빈칸 · 모르는 코드 = 모름)", codes, "");
            check("S179 ③ 여러 줄 합치기(다 국공유 · 다 사유 · 섞임 · 빈칸만)",
                VWorldParcels.Combine(new[] { "02", "04" }) == VWorldParcels.OwnKind.Public && VWorldParcels.Combine(new[] { "01", "01", null }) == VWorldParcels.OwnKind.Private
                && VWorldParcels.Combine(new[] { "01", "05" }) == VWorldParcels.OwnKind.Mixed && VWorldParcels.Combine(new string?[] { null, "" }) == VWorldParcels.OwnKind.Unknown
                && VWorldParcels.CodeName("04") == "시·도유지" && VWorldParcels.CodeName("") == "", "");
        }

        // ④ 데이터 API 읽기 — 쪽 · 다각형 · 구멍 · 여러 조각 · 공시지가 · NOT_FOUND · ERROR · 꼴 아님
        {
            string hole = Sq(0, 0, 20, 20) + "," + Sq(5, 5, 10, 10);   // 바깥 + 구멍
            string json = Page(3, 2, 1,
                Feat("1111010100100010000", "1대", "[" + Sq(0, 0, 10, 10) + "]", ",\"jiga\":\"1234000\",\"gosi_year\":\"2025\",\"gosi_month\":\"01\""),
                Feat("1111010100100020000", "2 전", "[" + hole + "]"),
                Feat("1111010100100030000", "3도", "[" + Sq(0, 0, 1, 1) + "],[" + Sq(5, 5, 6, 6) + "]"));
            var p = VWorldParcels.ParseDataPage(json);
            var a = p.Items.Count == 3 ? p.Items : null;
            bool ok = a != null && p.Status == "OK" && p.Total == 3 && p.Pages == 2 && p.Page == 1
                && a[0].Jiga == 1234000 && a[0].GosiYm == "2025-01" && a[0].Polys.Count == 1 && a[0].Polys[0].Shell.Count == 5
                && a[1].Polys[0].Holes.Count == 1 && a[1].JibunRaw == "2 전"
                && a[2].Polys.Count == 2 && a[0].Addr.StartsWith("가상시");
            check("S179 ④ 데이터 API 한 쪽(쪽 수 · 구멍 · 두 조각 · 공시지가 · 기준년월)", ok, $"{p.Status} n={p.Items.Count} 쪽 {p.Page}/{p.Pages}");
            var nf = VWorldParcels.ParseDataPage("{\"response\" : {\"status\" : \"NOT_FOUND\", \"record\" : {\"total\" : \"0\", \"current\" : \"0\"}}}");
            var er = VWorldParcels.ParseDataPage("{\"response\" : {\"status\" : \"ERROR\", \"error\" : {\"level\" : \"1\", \"code\" : \"INVALID_KEY\", \"text\" : \"등록되지 않은 인증키입니다.\"}}}");
            var junk = VWorldParcels.ParseDataPage("<html>gateway</html>");
            // ★[설치 전 검토 · 낮음] 맨 바깥이 배열 · 좌표가 글자 — 예외 없이 «꼴 아님»
            bool odd = true;
            try
            {
                var o1 = VWorldParcels.ParseDataPage("[1,2]");
                var o2 = VWorldParcels.ParseDataPage(Page(1, 1, 1, Feat("1111010100100010000", "1대", "[[[\"a\",\"b\"],[1,2],[3,4],[1,2]]]")));
                var o3 = VWorldParcels.ParseOwnershipWfs("[]");
                var o4 = VWorldParcels.ParseOwnerCodes("[]", out _, out _);
                odd = o1.Items.Count == 0 && o1.Status == "" && !o3.Ok && o4 == null && (o2.Status == "" || o2.Items.Count <= 1);
            }
            catch (Exception ex) { odd = false; Console.WriteLine("   예외 " + ex.GetType().Name); }
            check("S179 ④ 꼴이 다른 응답(맨 바깥 배열 · 좌표 글자) — 예외 없음", odd, "");
            check("S179 ④ NOT_FOUND · ERROR(코드 · 글) · 꼴 아님",nf.Status == "NOT_FOUND" && nf.Items.Count == 0 && er.Status == "ERROR" && er.Error.Contains("INVALID_KEY")
                && junk.Status == "" && junk.Error.Contains("gateway"), $"{nf.Status} · {er.Error} · {junk.Error}");
        }

        // ⑤ 소유 WFS · 필지별 조회 읽기 — 빈칸 코드 · 공유인수 · 날짜 · 다 찼나 · 오류 XML · field 하나
        {
            string wfs = "{\"type\":\"FeatureCollection\",\"features\":["
                + "{\"type\":\"Feature\",\"properties\":{\"pnu\":\"A\",\"posesn_se_code\":\"04\",\"cnrs_psn_co\":1,\"ownship_chg_de\":\"19590303\",\"lndcgr_code\":\"08\",\"lndpcl_ar\":12709.4}},"
                + "{\"type\":\"Feature\",\"properties\":{\"pnu\":\"B\",\"posesn_se_code\":null,\"cnrs_psn_co\":14,\"ownship_chg_de\":null,\"lndcgr_code\":\"08\",\"lndpcl_ar\":598.36}},"
                + "{\"type\":\"Feature\",\"properties\":{\"pnu\":\"C\",\"posesn_se_code\":\"01\",\"cnrs_psn_co\":1}},"
                + "{\"type\":\"Feature\",\"properties\":{\"pnu\":\"C\",\"posesn_se_code\":\"02\",\"cnrs_psn_co\":1}}"
                + "],\"totalFeatures\":4,\"numberMatched\":4,\"numberReturned\":4}";
            var w = VWorldParcels.ParseOwnershipWfs(wfs);
            var m = VWorldParcels.MergeOwnership(w.Items);
            bool ok = w.Ok && w.Items.Count == 4 && m.Count == 3 && m["A"].Kind == VWorldParcels.OwnKind.Public && m["A"].ChgDate == "1959-03-03" && m["A"].LedgerArea == 12709.4
                && m["B"].Kind == VWorldParcels.OwnKind.Unknown && m["B"].Cnrs == 14 && VWorldParcels.NeedsLookup(m["B"]) && !VWorldParcels.NeedsLookup(m["A"])
                && m["C"].Kind == VWorldParcels.OwnKind.Mixed && m["C"].CodeText == "개인 외 1" && m["A"].CodeText == "시·도유지";
            check("S179 ⑤ 소유 WFS 읽기 · PNU 합치기(빈칸 = 조회 필요 · 한 PNU 두 줄 = 일부 국공유)", ok, $"{w.Items.Count} · {m.Count}");
            var full = VWorldParcels.ParseOwnershipWfs("{\"type\":\"FeatureCollection\",\"features\":[],\"numberMatched\":1500,\"numberReturned\":1000}");
            full.Returned = 1000;
            var notFull = VWorldParcels.ParseOwnershipWfs("{\"type\":\"FeatureCollection\",\"features\":[],\"numberMatched\":0,\"numberReturned\":0}");
            var x = VWorldParcels.ParseOwnershipWfs("<ServiceExceptionReport><ServiceException code=\"INVALID_KEY\"/></ServiceExceptionReport>");
            check("S179 ⑤ 다 찼나(1,000 · 맞은 수 > 받은 수) · 빈 상자 · 오류 XML", VWorldParcels.WfsFull(full, 1000) && !VWorldParcels.WfsFull(notFull, 1000) && notFull.Ok && !x.Ok && x.Error.Contains("INVALID_KEY"), x.Error);
            var c1 = VWorldParcels.ParseOwnerCodes("{\"possessions\" : {\"field\" : [{\"posesnSeCode\" : \"01\"}, {\"posesnSeCode\" : \"01\"}, {\"posesnSeCode\" : \"05\"}], \"totalCount\" : \"3\"}}", out int t1, out _);
            var c2 = VWorldParcels.ParseOwnerCodes("{\"possessions\" : {\"field\" : {\"posesnSeCode\" : \"06\"}, \"totalCount\" : \"1\"}}", out int t2, out _);
            var c3 = VWorldParcels.ParseOwnerCodes("{\"possessions\" : {\"field\" : [], \"totalCount\" : \"0\"}}", out int t3, out _);
            var c4 = VWorldParcels.ParseOwnerCodes("oops", out _, out string e4);
            var c5 = VWorldParcels.ParseOwnerCodes("{\"response\" : {\"pageNo\" : \"1\", \"resultCode\" : \"\", \"totalCount\" : \"0\", \"numOfRows\" : \"1000\", \"resultMsg\" : \"\"}}", out int t5, out _);
            check("S179 ⑤ 필지별 조회 읽기(여러 줄 · 하나 · 없음 · 자료 없는 필지 «response» · 꼴 아님)", c1 != null && c1.Count == 3 && t1 == 3 && VWorldParcels.Combine(c1) == VWorldParcels.OwnKind.Mixed
                && c2 != null && c2.Count == 1 && t2 == 1 && c3 != null && c3.Count == 0 && t3 == 0 && c4 == null && e4 == "oops" && c5 != null && c5.Count == 0 && t5 == 0, "");
        }

        // ⑥ 합치기 · 자르기 — 상자 경계 중복 · 볼록 자름 · ㄷ자(오목)가 두 조각 · 구멍 유지 · 범위 밖 · 자기교차 고침 · 지번 글자 = 큰 조각 안
        {
            VWorldParcels.Raw R(string pnu, string jibun, params VWorldParcels.Poly[] ps) => new() { Pnu = pnu, JibunRaw = jibun, Polys = ps.ToList() };
            VWorldParcels.Poly P(params (double, double)[] s) => new() { Shell = s.ToList() };
            var clip = new VWorldParcels.Box(0, 0, 100, 100);
            var sq = P((80, 80), (120, 80), (120, 120), (80, 120));                                 // 걸침(볼록) — 20×20만
            // ㄷ자: 바닥 띠 + 두 기둥 — 위쪽(y > 60)을 잘라내면 기둥 둘만 남아 두 조각
            var u = P((10, 40), (50, 40), (50, 90), (40, 90), (40, 50), (20, 50), (20, 90), (10, 90));
            var uClip = new VWorldParcels.Box(0, 55, 100, 100);
            var holed = new VWorldParcels.Poly { Shell = new() { (0, 0), (30, 0), (30, 30), (0, 30) }, Holes = { new() { (10, 10), (20, 10), (20, 20), (10, 20) } } };
            var far = P((500, 500), (510, 500), (510, 510));
            var bow = P((60, 0), (70, 10), (70, 0), (60, 10));                                      // 나비(자기교차)
            var raws = new[]
            {
                R("1111010100100010000", "1대", sq), R("1111010100100010000", "1대", sq),          // 같은 필지 두 상자에서
                R("1111010100100020000", "2전", holed), R("1111010100100090000", "9잡", far), R("1111010100100050000", "5 답", bow),
            };
            var own = new Dictionary<string, VWorldParcels.OwnInfo>
            {
                ["1111010100100010000"] = new() { Codes = { "02" } },
                ["1111010100100020000"] = new() { Codes = { "01", "05" } },
            };
            var res = VWorldParcels.Assemble(raws, own, clip);
            var p1 = res.Parcels.FirstOrDefault(p => p.Pnu.EndsWith("10000"));
            var p2 = res.Parcels.FirstOrDefault(p => p.Pnu.EndsWith("20000"));
            var p5 = res.Parcels.FirstOrDefault(p => p.Pnu.EndsWith("50000"));
            bool ok = res.DupRows == 1 && res.Parcels.Count == 3 && res.Skipped == 1 && res.Fixed == 1
                && p1 != null && p1.Pieces.Count == 1 && Math.Abs(p1.Pieces[0].Area - 400) < 1e-6 && p1.Own.Kind == VWorldParcels.OwnKind.Public && p1.LabelText == "1대"
                && p2 != null && p2.Pieces[0].Holes.Count == 1 && Math.Abs(p2.Pieces[0].Area - 800) < 1e-6 && p2.Own.Kind == VWorldParcels.OwnKind.Mixed && p2.LabelText == "2전"
                && p5 != null && p5.Own.Kind == VWorldParcels.OwnKind.Unknown && p5.Jimok == "답"
                && res.Public == 1 && res.Mixed == 1 && res.NoOwn == 1;
            check("S179 ⑥ PNU 합치기 · 자르기(걸침 400 ㎡ · 구멍 800 ㎡ · 범위 밖 건너뜀 · 나비 고침 · 소유 붙임)", ok,
                $"필지 {res.Parcels.Count} · 중복 {res.DupRows} · 건너뜀 {res.Skipped} · 고침 {res.Fixed} · p1 {p1?.Pieces.FirstOrDefault()?.Area:F1} · p2 {p2?.Pieces.FirstOrDefault()?.Area:F1}");
            var ru = VWorldParcels.Assemble(new[] { R("1111010100100070000", "7대", u) }, new Dictionary<string, VWorldParcels.OwnInfo>(), uClip);
            var pu = ru.Parcels.FirstOrDefault();
            bool two = pu != null && pu.Pieces.Count == 2 && Math.Abs(pu.Pieces.Sum(q => q.Area) - 2 * 10 * 35) < 1e-6;
            bool lblIn = pu != null && pu.Pieces.Any(q => VWorldParcels.Inside(q, pu.Label.X, pu.Label.Y));
            check("S179 ⑥ 오목(ㄷ자) 필지가 범위에 잘려 두 조각 · 지번 글자는 조각 안", two && lblIn, $"조각 {pu?.Pieces.Count} · 넓이 {pu?.Pieces.Sum(q => q.Area):F1} · 글자 ({pu?.Label.X:F1}, {pu?.Label.Y:F1})");
            // 구멍 안 점은 필지 밖
            check("S179 ⑥ 점-필지(구멍 안 = 밖 · 고리 안 = 안)", p2 != null && !VWorldParcels.Inside(p2.Pieces[0], 15, 15) && VWorldParcels.Inside(p2.Pieces[0], 5, 5), "");
            // PNU와 jibun 글자가 다르면 PNU를 쓰고 셈(M5)
            var rm = VWorldParcels.Assemble(new[] { R("1111010100100110000", "12대", P((1, 1), (9, 1), (9, 9))) }, new Dictionary<string, VWorldParcels.OwnInfo>(), clip);
            check("S179 ⑥ 지번 견줌(글자 «12» ≠ PNU «11» → PNU · 셈 1)", rm.JibunMismatch == 1 && rm.Parcels[0].Jibun == "11" && rm.Parcels[0].Jimok == "대", $"{rm.JibunMismatch} · {rm.Parcels[0].Jibun}");
        }

        // ⑦ ★[v110.2.1 · JACK 1029 «국공유지 내용은 없는 것 같아»] 법정동 코드가 바뀐 곳 — 지적 PNU(새 코드)와 소유 PNU(옛 코드)가 다름(본번 · 부번 같음) → 자리로 맞춤.
        //   같은 본번 · 부번이 두 동에 있으면(범위가 다름) 필지 점이 든 쪽 · 맞는 쪽이 없으면 못 맞춤(OwnPnu = 지적 PNU) · PNU 같으면 그대로.
        {
            VWorldParcels.Raw R(string pnu, double x0, double y0, double x1, double y1) => new() { Pnu = pnu, Polys = { new VWorldParcels.Poly { Shell = new() { (x0, y0), (x1, y0), (x1, y1), (x0, y1) } } } };
            var raws = new[]
            {
                R("9911110500105100389", 0, 0, 10, 10),       // 새 코드 · 510-389 — 소유 쪽 옛 코드 두 동 중 (0~10) 범위 쪽
                R("9911110900105090000", 20, 0, 30, 10),      // 새 코드 · 509 — 소유 쪽 없음 → 못 맞춤
                R("1111010100100010000", 40, 0, 50, 10),      // 같은 PNU
            };
            var rows = new[]
            {
                new VWorldParcels.OwnRow { Pnu = "9900010100105100389", Code = "02", Box = new[] { -1.0, -1, 11, 11 } },
                new VWorldParcels.OwnRow { Pnu = "9900010200105100389", Code = "01", Box = new[] { 100.0, 100, 110, 110 } },   // 다른 동 같은 번호 — 범위 밖
                new VWorldParcels.OwnRow { Pnu = "1111010100100010000", Code = "06", Box = new[] { 40.0, 0, 50, 10 } },
            };
            var m = VWorldParcels.MatchOwnership(raws, rows, out int byPlace, out int un);
            bool ok = m["9911110500105100389"].Kind == VWorldParcels.OwnKind.Public && m["9911110500105100389"].OwnPnu == "9900010100105100389"
                && m["9911110900105090000"].Kind == VWorldParcels.OwnKind.Unknown && m["9911110900105090000"].OwnPnu == "9911110900105090000"
                && m["1111010100100010000"].Kind == VWorldParcels.OwnKind.Private && byPlace == 1 && un == 1;
            check("S179 ⑦ 법정동 코드 바뀐 곳 — 본번 · 부번 + 자리로 맞춤 · 다른 동 같은 번호 안 섞임 · 없으면 못 맞춤", ok, $"자리 {byPlace} · 못 맞춤 {un} · {m["9911110500105100389"].OwnPnu}");
            var wb = VWorldParcels.ParseOwnershipWfs("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"geometry\":null,\"properties\":{\"pnu\":\"A\",\"posesn_se_code\":\"06\"},\"bbox\":[100000.5,200000.25,100420.75,200829.5]}]}");
            check("S179 ⑦ WFS 필지 범위(bbox) 읽기 — 도형 없이 받아도", wb.Items.Count == 1 && wb.Items[0].Box != null && Math.Abs(wb.Items[0].Box![2] - 100420.75) < 1e-6, "");
        }
    }
}
