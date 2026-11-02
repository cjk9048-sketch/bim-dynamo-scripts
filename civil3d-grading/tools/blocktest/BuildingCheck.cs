// ── S184 ★★[v111.7 · JACK 1102 «3D 보기에 건물 · 층수로 높이 · 켜고 끄기»] [3D 보기] 건물(VWorld 건물정보) 읽기 · 높이 · 바닥 · 닿음 · 장면 배열 ──
//   만든 꼴(현장 값 없음 — 공개 저장소) · 응답 꼴은 1102 실측(공개 장소)과 같은 짜임(피처 id · MultiPolygon · grnd_flr 글)
using System.Globalization;
using DH.Grading.Core;

static class BuildingCheck
{
    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>고리 GeoJSON — 점들을 차례대로 + 닫는 점.</summary>
    static string Ring(params (double X, double Y)[] p) =>
        "[" + string.Join(",", p.Append(p[0]).Select(q => $"[{F(q.X)},{F(q.Y)}]")) + "]";

    static string Sq(double x0, double y0, double x1, double y1) => Ring((x0, y0), (x1, y0), (x1, y1), (x0, y1));

    static string Feat(string id, string floors, string polys) =>
        "{\"type\":\"Feature\",\"geometry\":{\"type\":\"MultiPolygon\",\"coordinates\":[" + polys + "]},"
        + "\"properties\":{\"bld_nm\":\"가상 건물\",\"dong_nm\":\"\",\"usability\":\"02000\",\"grnd_flr\":" + floors + ",\"ugrnd_flr\":\"1\",\"height\":\"12.3\"},\"id\":\"" + id + "\"}";

    static string Page(int total, params string[] feats) =>
        "{\"response\" : {\"service\" : {\"name\" : \"data\"}, \"status\" : \"OK\", \"record\" : {\"total\" : \"" + total + "\", \"current\" : \"" + feats.Length + "\"}, "
        + "\"page\" : {\"total\" : \"1\", \"current\" : \"1\", \"size\" : \"1000\"}, \"result\" : {\"featureCollection\" : {\"type\":\"FeatureCollection\",\"features\":["
        + string.Join(",", feats) + "]}}}}";

    static double Area2(List<(double X, double Y)> r)
    {
        double a = 0;
        for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) a += (r[j].X - r[i].X) * (r[j].Y + r[i].Y);
        return a;
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S184 [3D 보기] 건물(읽기 · 높이 · 바닥 · 닿음 · 장면 배열) ==");

        // ① 지상층수 글 — 1 이상 정수만(0 · 빈칸 · 음수 · 소수 · 글자 = 안 그림)
        {
            var cases = new (string? S, int? N)[] { ("3", 3), (" 12 ", 12), ("1", 1), ("4.0", 4), ("0", null), ("", null), (null, null), ("  ", null), ("-2", null), ("2.5", null), ("abc", null), ("NaN", null) };
            var bad = cases.Where(c => Web3DBuildings.Floors(c.S) != c.N).Select(c => $"«{c.S}»→{Web3DBuildings.Floors(c.S)}").ToList();
            check("S184 ① 지상층수 읽기(12꼴 — 0 · 빈칸 · 못 읽음 = 없음)", bad.Count == 0, string.Join(" · ", bad));
        }

        // ② 응답 읽기 — 피처 id · 층수(글 · 숫자 · 빈칸) · 여러 조각 · 구멍 · NOT_FOUND · ERROR · 꼴 아님 · 도형 깨진 건물 하나
        Web3DBuildings.DataPage page;
        {
            string withHole = "[" + Sq(100, 0, 120, 20) + "," + Ring((105, 5), (115, 5), (115, 15), (105, 15)) + "]";   // 구멍도 반시계로 옴(뒤집어야 함)
            string json = Page(6,
                Feat("LT_C_BLDGINFO.1", "\"3\"", "[" + Sq(0, 0, 10, 10) + "]"),                 // 경사 땅 · 3층
                Feat("LT_C_BLDGINFO.2", "\"0\"", "[" + Sq(20, 0, 30, 10) + "]"),                 // 0층 — 안 그림
                Feat("LT_C_BLDGINFO.3", "\"\"", "[" + Sq(40, 0, 50, 10) + "]"),                  // 빈칸 — 안 그림
                Feat("LT_C_BLDGINFO.4", "5", "[" + Sq(60, 0, 70, 10) + "],[" + Sq(80, 0, 85, 5) + "]"),   // 숫자 5 · 두 조각
                Feat("LT_C_BLDGINFO.5", "\"2\"", withHole),                                      // 구멍(가운데 뜰)
                Feat("LT_C_BLDGINFO.1", "\"3\"", "[" + Sq(0, 0, 10, 10) + "]"));                 // 같은 id(상자 경계) — 한 번만
            page = Web3DBuildings.ParseDataPage(json);
            var it = page.Items;
            bool ok = page.Status == "OK" && page.Total == 6 && it.Count == 6
                && it[0].Id == "LT_C_BLDGINFO.1" && it[0].Floors == "3" && it[0].Polys.Count == 1 && it[0].Polys[0].Shell.Count == 5
                && it[1].Floors == "0" && it[2].Floors == "" && it[3].Floors == "5" && it[3].Polys.Count == 2
                && it[4].Polys.Count == 1 && it[4].Polys[0].Holes.Count == 1;
            check("S184 ② 응답 한 쪽(id · 층수 글/숫자/빈칸 · 두 조각 · 구멍)", ok, $"{page.Status} n={it.Count} total={page.Total}");
            var nf = Web3DBuildings.ParseDataPage("{\"response\" : {\"status\" : \"NOT_FOUND\", \"record\" : {\"total\" : \"0\", \"current\" : \"0\"}}}");
            var er = Web3DBuildings.ParseDataPage("{\"response\" : {\"status\" : \"ERROR\", \"error\" : {\"level\" : \"1\", \"code\" : \"INVALID_KEY\", \"text\" : \"등록되지 않은 인증키입니다.\"}}}");
            var junk = Web3DBuildings.ParseDataPage("<html>gateway</html>");
            var arr = Web3DBuildings.ParseDataPage("[1,2]");
            var badGeo = Web3DBuildings.ParseDataPage(Page(1, "{\"type\":\"Feature\",\"geometry\":{\"type\":\"MultiPolygon\",\"coordinates\":[[[[\"a\",\"b\"],[1,2],[3,4]]]]},\"properties\":{\"grnd_flr\":\"2\"},\"id\":\"x\"}"));
            check("S184 ② NOT_FOUND · ERROR(키) · 꼴 아님 둘 · 좌표 글자 = 그 건물만 도형 없음",
                nf.Status == "NOT_FOUND" && nf.Items.Count == 0 && er.Status == "ERROR" && er.Error.Contains("INVALID_KEY")
                && junk.Status == "" && junk.Error.Length > 0 && arr.Status == "" && badGeo.Status == "OK" && badGeo.Items.Count == 1 && badGeo.Items[0].Polys.Count == 0,
                $"{nf.Status} · {er.Status}/{er.Error} · «{junk.Status}» · «{arr.Status}» · {badGeo.Status}/{badGeo.Items.Count}");
            var pb = Web3DBuildings.Place(badGeo.Items, (x, y) => 0, null);
            check("S184 ② 도형 없는 건물 = «모양 나쁨»으로 셈(안 그림)", pb.Items.Count == 0 && pb.BadShape == 1, pb.Text);
        }

        // ③ 놓기 — 경사 땅 z = 100 + 0.1·x(바닥 = 가장 낮은 곳 = 서쪽 끝) · 바뀌는 자리 x ∈ [65, 67] · 뜰 안 땅만 낮음(바닥에 안 들어감) · 같은 id 한 번
        {
            double? Ground(double x, double y)
            {
                if (x > 106 && x < 114 && y > 6 && y < 14) return 50;   // 구멍(뜰) 안쪽만 깊은 땅 — 건물 밑이 아니다
                return 100 + 0.1 * x;
            }
            bool Changed(double x, double y) => (x >= 65 && x <= 67 && y >= 4 && y <= 6) || (x > 107 && x < 113 && y > 7 && y < 13);   // 3번 건물 안쪽 · 5번 뜰 가운데
            var r = Web3DBuildings.Place(page.Items, Ground, Changed);
            var b1 = r.Items.FirstOrDefault(b => b.Id == "LT_C_BLDGINFO.1");
            var b4 = r.Items.FirstOrDefault(b => b.Id == "LT_C_BLDGINFO.4");
            var b5 = r.Items.FirstOrDefault(b => b.Id == "LT_C_BLDGINFO.5");
            check("S184 ③ 그릴 것 3동 · 층수 없음 2 · 같은 id 1 · 받음 6", r.Items.Count == 3 && r.NoFloor == 2 && r.Dup == 1 && r.Raws == 6 && r.NoGround == 0, r.Text);
            check("S184 ③ 높이 = 층 × 3.5 m(3층 10.5 · 5층 17.5 · 2층 7.0)",
                b1 != null && b4 != null && b5 != null && Math.Abs(b1.Height - 10.5) < 1e-9 && Math.Abs(b4.Height - 17.5) < 1e-9 && Math.Abs(b5.Height - 7.0) < 1e-9 && b1.Floors == 3,
                $"{b1?.Height} · {b4?.Height} · {b5?.Height}");
            check("S184 ③ 바닥 = 발자국 아래 가장 낮은 땅(서쪽 끝 · 두 조각이면 둘 중 낮은 것 · 뜰 안 깊은 땅은 안 셈)",
                b1 != null && b4 != null && b5 != null && Math.Abs(b1.Base - 100.0) < 1e-9 && Math.Abs(b4.Base - 106.0) < 1e-9 && Math.Abs(b5.Base - 110.0) < 1e-9,
                $"{b1?.Base} · {b4?.Base} · {b5?.Base}");
            check("S184 ③ 닿음 — 안쪽 격자로 잡음(두 조각 건물) · 뜰 안 바뀜은 안 닿음 · 먼 건물 안 닿음",
                b4 != null && b4.Touch && b5 != null && !b5.Touch && b1 != null && !b1.Touch && r.Touch == 1, $"{b1?.Touch} · {b4?.Touch} · {b5?.Touch}");
            bool orient = r.Items.All(b => b.Polys.All(p => Area2(p.Shell) > 0 && p.Holes.All(h => Area2(h) < 0)
                && p.Holes.Prepend(p.Shell).All(q => q.Count >= 3 && (q[0].X != q[^1].X || q[0].Y != q[^1].Y))));
            check("S184 ③ 고리 방향(바깥 반시계 · 구멍 시계 — 받은 구멍은 반시계였음) · 닫는 점 없음", orient, "");
            var none = Web3DBuildings.Place(page.Items, (x, y) => x < 15 ? 10 : null, null);
            check("S184 ③ 땅 높이 없는 건물은 안 그림(한 점이라도 있으면 그림) · 바뀜 함수 없으면 닿음 없음",
                none.Items.Count == 1 && none.NoGround == 2 && none.Items[0].Base == 10 && none.Touch == 0, none.Text);
            // ★[검토 M] 원지반이 그 건물 밑에 없으면 보이는 지형(fallback)으로 다시 — 원지반이 있는 건물은 원지반 그대로
            var fb = Web3DBuildings.Place(page.Items, (x, y) => x < 15 ? 10 : null, null, (x, y) => 5);
            var fb1 = fb.Items.FirstOrDefault(b => b.Id == "LT_C_BLDGINFO.1");
            check("S184 ③ 원지반 없는 건물만 보이는 지형으로 바닥(2동) · 원지반 있는 건물은 그대로 · 땅 높이 없음 0",
                fb.Items.Count == 3 && fb.Fallback == 2 && fb.NoGround == 0 && fb1 != null && fb1.Base == 10 && fb.Items.Where(b => b != fb1).All(b => b.Base == 5), fb.Text);
            check("S184 ③ 뺀 까닭 글(숫자만)", fb.DropText == "층수 없음 2 · 땅 높이 없음 0 · 모양 이상 0", fb.DropText);
            // 경계 위 한 점만 바뀜 — 둘레 표본(2 m마다)이 잡는다
            var edgeHit = Web3DBuildings.Place(page.Items.Take(1), (x, y) => 0, (x, y) => Math.Abs(x - 10) < 1e-9 && Math.Abs(y - 4) < 1e-9);
            check("S184 ③ 둘레 2 m 표본이 변 위 한 점의 바뀜을 잡음", edgeHit.Items.Count == 1 && edgeHit.Items[0].Touch, edgeHit.Text);
        }

        // ④ 닫는 점 · 겹친 점 · 넓이 0 · 큰 건물 표본 한도
        {
            var ring = new List<(double X, double Y)> { (0, 0), (0, 0.0004), (10, 0), (10, 10), (0, 10), (0, 0) };
            var c = Web3DBuildings.Clean(ring, ccw: true);
            var flat = Web3DBuildings.Clean(new List<(double X, double Y)> { (0, 0), (10, 0), (20, 0) }, ccw: true);
            check("S184 ④ 고리 다듬기(겹친 점 · 닫는 점 뺌 · 일직선 = 버림)", c != null && c.Count == 4 && flat == null, $"{c?.Count} · {(flat == null ? "null" : flat.Count.ToString())}");
            var big = new VWorldParcels.Poly { Shell = new() { (0, 0), (400, 0), (400, 300), (0, 300) } };
            int n = Web3DBuildings.Samples(new List<VWorldParcels.Poly> { big }, Web3DBuildings.Step).Count();
            check("S184 ④ 큰 건물(400×300 m) 표본 한도(둘레 700 + 안쪽 ≤ 2,500 안팎)", n > 700 && n <= 700 + 2600, $"{n}");
        }

        // ④b ★[검토 열린 물음] 같은 건물 가르기 — id(1102 실측으로 안정) · id가 비면 도형 전체(점 수 + 첫 점만 같은 다른 건물은 둘 다 남김)
        {
            VWorldParcels.Poly P(params (double X, double Y)[] p) => new() { Shell = p.ToList() };
            var a1 = new Web3DBuildings.Raw { Floors = "2", Polys = { P((0, 0), (10, 0), (10, 10), (0, 10)) } };
            var a2 = new Web3DBuildings.Raw { Floors = "2", Polys = { P((0, 0), (10, 0), (10, 10), (0, 10)) } };      // a1과 같은 도형 · id 없음 → 겹침
            var a3 = new Web3DBuildings.Raw { Floors = "2", Polys = { P((0, 0), (10, 0), (10, 12), (0, 12)) } };      // 점 수 · 첫 점이 같은 다른 건물 → 남김
            var a4 = new Web3DBuildings.Raw { Id = "x.1", Floors = "2", Polys = { P((0, 0), (10, 0), (10, 10), (0, 10)) } };   // 도형은 a1과 같아도 id가 있으면 id로
            var a5 = new Web3DBuildings.Raw { Id = "x.1", Floors = "3", Polys = { P((50, 0), (60, 0), (60, 10)) } };          // 같은 id → 겹침
            var d = Web3DBuildings.Place(new[] { a1, a2, a3, a4, a5 }, (x, y) => 0, null);
            check("S184 ④b 같은 건물 가르기(id 없으면 도형 전체 · 점 수 + 첫 점만 같으면 다른 건물 · id 같으면 겹침)", d.Items.Count == 3 && d.Dup == 2, d.Text);
        }

        // ④c ★[검토 M] 범위 줄이기 — ★[v111.7.1 · JACK 1102 «주변 아파트가 빠짐»] 지형 전체 → 바뀐 자리 + 500 → + 200 → + 50 → + 0 m(지형 안으로 자름 · 안 줄면 뺌) · 그다음 가운데(넓이 ≤ 0.81 × 한도/수 · 늘 줄어듦)
        {
            var terrain = new VWorldParcels.Box(0, 0, 3000, 2500);
            var site = new VWorldParcels.Box(1000, 2000, 1300, 2200);
            var rg = Web3DBuildings.Ranges(terrain, site);
            string Rs(List<(VWorldParcels.Box Box, string Label)> l) => string.Join(" → ", l.Select(q => $"{q.Label}[{q.Box.X0:0},{q.Box.Y0:0},{q.Box.X1:0},{q.Box.Y1:0}]"));
            // + 500은 위(2700)가 지형 밖이라 2500에서 잘림 · + 200 · + 50 · + 0(바뀐 자리)은 지형 안
            bool rOk = rg.Count == 5 && rg[0].Box == terrain && rg[0].Label == "지형 전체"
                && rg[1].Box == new VWorldParcels.Box(500, 1500, 1800, 2500) && rg[1].Label == "바뀐 자리 + 500 m"
                && rg[2].Box == new VWorldParcels.Box(800, 1800, 1500, 2400) && rg[2].Label == "바뀐 자리 + 200 m"
                && rg[3].Box == new VWorldParcels.Box(950, 1950, 1350, 2250) && rg[3].Label == "바뀐 자리 + 50 m"
                && rg[4].Box == site && rg[4].Label == "바뀐 자리"
                && rg.Zip(rg.Skip(1)).All(z => z.Second.Box.Area < z.First.Box.Area);
            var none = Web3DBuildings.Ranges(terrain, null);
            var small = Web3DBuildings.Ranges(new VWorldParcels.Box(900, 1900, 1400, 2300), site);   // 지형이 작아 + 500 · + 200은 지형 전체와 같음 → 뺌 · + 50 · + 0만 남음
            bool eOk = none.Count == 1 && none[0].Label == "지형 전체" && small.Count == 3 && small[1].Label == "바뀐 자리 + 50 m" && small[2].Box == site && small[2].Label == "바뀐 자리";
            check("S184 ④c 받을 범위 차례(지형 전체 → 바뀐 자리 + 500 → + 200 → + 50 → + 0 · 지형 안으로 자름 · 안 줄면 뺌 · 바뀐 자리 없으면 지형 전체만)", rOk && eOk,
                $"{Rs(rg)} | 없음 {Rs(none)} | 작은 지형 {Rs(small)}");
            var e = Web3DBuildings.Expand(site, 50);
            var s1 = Web3DBuildings.ShrinkAround(site, 20000, 5000);
            var s2 = Web3DBuildings.ShrinkAround(site, 5001, 5000);
            bool sOk = e.X0 == 950 && e.Y1 == 2250 && Math.Abs(s1.Area / site.Area - 0.81 * 5000 / 20000) < 1e-9
                && Math.Abs((s1.X0 + s1.X1) / 2 - 1150) < 1e-9 && Math.Abs((s1.Y0 + s1.Y1) / 2 - 2100) < 1e-9 && s2.Area < site.Area * 0.82;
            check("S184 ④c 범위 줄이기(가운데로 · 넓이 비율 · 늘 줄어듦)", sOk,
                $"줄인 넓이 비 {s1.Area / site.Area:F4} · 겨우 넘음 {s2.Area / site.Area:F3}");
        }

        // ⑤ 장면 배열 — 원점 빼기 · 첫 순번 표 셋(끝에 전체 수) · 바닥 · 높이 · 닿음
        {
            var items = new List<Web3DBuildings.Bldg>
            {
                new() { Id = "a", Floors = 2, Base = 10, Height = 7, Touch = false,
                        Polys = new() { new VWorldParcels.Poly { Shell = new() { (1000, 2000), (1010, 2000), (1010, 2010) } } } },
                new() { Id = "b", Floors = 1, Base = 20, Height = 3.5, Touch = true,
                        Polys = new()
                        {
                            new VWorldParcels.Poly { Shell = new() { (1020, 2000), (1030, 2000), (1030, 2010), (1020, 2010) },
                                                     Holes = new() { new() { (1022, 2002), (1022, 2004), (1024, 2004), (1024, 2002) } } },
                            new VWorldParcels.Poly { Shell = new() { (1040, 2000), (1041, 2000), (1041, 2001) } },
                        } },
            };
            var a = Web3DBuildings.Pack(items, 1000, 2000);
            bool ok = a.Xy.Length == (3 + 4 + 4 + 3) * 2 && a.Xy[0] == 0 && a.Xy[1] == 0 && a.Xy[2] == 10
                && a.Ring.SequenceEqual(new[] { 0, 3, 7, 11, 14 }) && a.Poly.SequenceEqual(new[] { 0, 1, 3, 4 }) && a.Bldg.SequenceEqual(new[] { 0, 1, 3 })
                && a.Z0.SequenceEqual(new[] { 10f, 20f }) && a.H.SequenceEqual(new[] { 7f, 3.5f }) && a.Hit.SequenceEqual(new byte[] { 0, 1 });
            check("S184 ⑤ 장면 배열(점 14 · 고리 4 · 다각형 3 · 건물 2 · 끝에 전체 수)", ok,
                $"ring [{string.Join(",", a.Ring)}] poly [{string.Join(",", a.Poly)}] bldg [{string.Join(",", a.Bldg)}]");
            var e = Web3DBuildings.Pack(new List<Web3DBuildings.Bldg>(), 0, 0);
            check("S184 ⑤ 빈 배열(표마다 0 하나)", e.Xy.Length == 0 && e.Ring.SequenceEqual(new[] { 0 }) && e.Poly.SequenceEqual(new[] { 0 }) && e.Bldg.SequenceEqual(new[] { 0 }) && e.Hit.Length == 0, "");
            // 장면 묶음에 넣어 되읽기 — 머리 JSON이 깨지지 않는다
            var sc = new Web3DScene();
            sc.Head["bldg"] = new Dictionary<string, object> { ["n"] = 2, ["xy"] = sc.F32(a.Xy), ["ring"] = sc.U32(a.Ring), ["hit"] = sc.U8(a.Hit), ["z0"] = sc.F32(a.Z0) };
            var (doc, buf, bo) = Web3DScene.Unpack(sc.PackGzip());
            var back = Web3DScene.ReadF32(doc.RootElement.GetProperty("bldg").GetProperty("z0"), buf, bo);
            check("S184 ⑤ 장면 묶음 되읽기(z0)", back.SequenceEqual(new[] { 10f, 20f }) && doc.RootElement.GetProperty("bldg").GetProperty("n").GetInt32() == 2, "");
        }
    }
}
