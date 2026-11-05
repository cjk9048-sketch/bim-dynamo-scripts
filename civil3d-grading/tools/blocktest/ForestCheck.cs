// ── S216 FT ★★[v115 · JACK 1105 임상도] [3D 보기] 수목 — 타일 변환(SHP · DBF → DHF1) · 좌표(5179 ↔ TM) · 심기 · 빼기 · 한도 · 장면 ──
//   가짜 좌표만(공개 저장소): 5179 X≈1,000,000 · Y≈1,800,000 / TM X≈200,000 · Y≈550,000 · 경위도는 정수 도 근처 일반 점
//   PROJ 기준값 = pyproj 3.7.2(PROJ 9.5.1)로 1105에 잰 값(아래 표 — 일반 점)
using System.Globalization;
using DH.Grading.Core;
using DH.Grading.Core.Forest;
using NetTopologySuite.Geometries;

static class ForestCheck
{
    static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);
    static string Tmp()
    {
        string d = Path.Combine(Path.GetTempPath(), "dh_forestcheck_" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    // 시계 방향 네모(SHP 바깥) · 반시계(구멍)
    static double[] Cw(double x0, double y0, double x1, double y1) => new[] { x0, y0, x0, y1, x1, y1, x1, y0, x0, y0 };
    static double[] Ccw(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1, x0, y0 };

    static readonly (string, int)[] Fields =
    {
        ("FRTP_CD", 4), ("KOFTR_GROU", 4), ("DNST_CD", 2), ("HEIGHT", 2), ("DMCLS_CD", 2), ("AGCLS_CD", 2), ("FROR_CD", 2), ("갱신년도", 8),
        ("FRTP_NM", 20), ("KOFTR_NM", 20), ("Shape_Area", 19),
    };
    static string[] Attr(string frtp, string kof, string dn, string h, string area = "") => new[] { frtp, kof, dn, h, "2", "4", "2", "2020", frtp == "0" ? "무립목지/비산림" : "침엽수림", "소나무", area };

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S216 FT [3D 보기] 수목(임상도 타일 · 좌표 · 심기 · 빼기 · 한도) ==");
        string root = Tmp();
        try
        {
            Convert(check, root);
            Coords(check);
            Plant(check);
            Exclude(check);
            CapTests(check);
            SceneTests(check);
            Synth(check);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    // ── ① 변환 ──
    static void Convert(Action<string, bool, string> check, string root)
    {
        const double X = 1_000_000, Y = 1_800_000;   // 칸 경계(2 km 배수)
        string src = Path.Combine(root, "src"), outD = Path.Combine(root, "out");
        Directory.CreateDirectory(Path.Combine(src, "90")); Directory.CreateDirectory(Path.Combine(src, "91"));
        // 가는 띠(폭 0.8 m × 400 m = 320 ㎡ — 1 m 솎기로 사라짐 → M1 넓이 목록에 오를 것)
        var sawCw = Cw(X + 1200, Y + 1200, X + 1600, Y + 1200.8).ToList();
        var rings = new List<IReadOnlyList<double[]>>
        {
            new[] { Cw(X - 100, Y - 100, X + 100, Y + 100) },                                                          // r0 네 칸에 걸침 · 4 ha
            new[] { Cw(X + 300, Y + 300, X + 500, Y + 500), Ccw(X + 350, Y + 350, X + 400, Y + 400) },                    // r1 구멍
            new[] { Cw(X + 600, Y + 300, X + 700, Y + 400), Cw(X + 800, Y + 300, X + 900, Y + 400),                       // r2 바깥 둘 · 각자 구멍(구멍 차례를 뒤바꿔 적음)
                    Ccw(X + 820, Y + 320, X + 830, Y + 330), Ccw(X + 620, Y + 320, X + 640, Y + 340) },
            new[] { Cw(X + 1000, Y + 300, X + 1100, Y + 400), Ccw(X + 1200, Y + 300, X + 1210, Y + 310) },               // r3 고아 구멍
            new[] { new[] { X + 300, Y + 700, X + 400, Y + 800, X + 400, Y + 700, X + 300, Y + 820, X + 300, Y + 700 } },   // r4 나비넥타이(두 귀 2,272.7 + 3,272.7 ㎡)
            new[] { Cw(X + 600, Y + 700, X + 700, Y + 800) },                                                          // r5 FRTP 0
            new[] { Cw(X + 900, Y + 900, X + 902, Y + 905) },                                                          // r6 10 ㎡ 부분만
            new[] { Cw(X - 0.5, Y + 1000, X + 100, Y + 1100) },                                                        // r7 칸 경계 0.5 m 조각(50 ㎡ — 남음)
            new[] { sawCw.ToArray() },                                                                                 // r8 가는 띠
            new[] { new[] { X + 50, Y + 1500, X + 60, Y + 1500, X + 50, Y + 1500, X + 50, Y + 1500 } },                  // r9 넓이 0 고리(원자료에 있는 꼴)
        };
        var vals = new List<string[]>
        {
            Attr("1", "11", "C", "14"), Attr("2", "31", "B", "16"), Attr("3", "77", "C", "12"), Attr("1", "12", "A", "10"), Attr("2", "30", "C", "08"),
            Attr("0", "82", "", ""), Attr("1", "11", "C", "14"), Attr("2", "34", "C", "18"), Attr("2", "30", "C", "14"), Attr("1", "11", "C", "14"),
        };
        ShpDbfReader.WriteTest(Path.Combine(src, "90", "90.shp"), rings, Fields, vals);
        ShpDbfReader.WriteTest(Path.Combine(src, "91", "91.shp"), new List<IReadOnlyList<double[]>> { new[] { Cw(X + 5000, Y + 5000, X + 5200, Y + 5200) } }, Fields, new List<string[]> { Attr("4", "78", "", "") });
        // 읽기 — cp949 칸 이름 · 코드
        using (var rd = new ShpDbfReader(Path.Combine(src, "90", "90.shp")))
        {
            var r = rd.ReadBatch(0, 3);
            check("S216 FT ① SHP · DBF 읽기(레코드 · 고리 · cp949 칸 이름)", rd.Count == 10 && rd.DbfCount == 10 && rd.FieldOf("갱신년도") != null && r[1].Rings.Count == 2 && ShpDbfReader.Code(r[0].Dbf, rd.FieldOf("KOFTR_GROU")) == "11" && ShpDbfReader.Text(r[0].Dbf, rd.FieldOf("KOFTR_NM")) == "소나무",
                $"레코드 {rd.Count} · 칸 {rd.Fields.Count}");
        }
        var logs = new List<string>();
        var o = new ForestConvert.Options { Src = src, Out = outD, Threads = 4, Log = s => logs.Add(s) };
        var ix = ForestConvert.Build(o);
        check("S216 FT ① 변환 끝 · index.json", ix != null && File.Exists(Path.Combine(outD, "index.json")), ix == null ? string.Join(" | ", logs.TakeLast(5)) : $"칸 {ix.Tiles.Count}");
        if (ix == null) return;
        var st = ForestConvert.Done(outD, ForestConvert.FindSources(src)[0], o)!;
        check("S216 FT ① 셈 — 10 = 남김 7 + FRTP0 1 + 작은 부분만 2 + 실패 0(넓이 0 고리 = 작은 부분)", st.Records == 10 && st.Kept == 7 && st.Frtp0 == 1 && st.Small == 2 && st.Failed == 0,
            $"남김 {st.Kept} · FRTP0 {st.Frtp0} · 작은 {st.Small} · 실패 {st.Failed}");
        check("S216 FT ① 고아 구멍 1 · 나비넥타이 고침", st.OrphanHoles == 1 && st.Fixed >= 1, $"고아 {st.OrphanHoles} · 고침 {st.Fixed}");
        // 칸 배정 · 넓이
        var tiles = ix.Tiles.Keys.Select(k => { var p = k.Split('_'); return ForestTileFormat.DecodeTile(File.ReadAllBytes(Path.Combine(outD, ForestTileFormat.RelPath(int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture))))); }).ToList();
        double AreaOf(int rec) => tiles.SelectMany(t => t.Polys).Where(q => q.Source == 0 && q.Record == rec).Sum(ForestTileFormat.PolyArea);
        int TilesOf(int rec) => tiles.Count(t => t.Polys.Any(q => q.Source == 0 && q.Record == rec));
        check("S216 FT ① 네 칸에 걸친 네모 = 칸 넷 · 넓이 40,000 ㎡", TilesOf(0) == 4 && Math.Abs(AreaOf(0) - 40000) < 1e-6, $"칸 {TilesOf(0)} · {AreaOf(0):F3}");
        check("S216 FT ① 구멍 = 40,000 − 2,500", Math.Abs(AreaOf(1) - 37500) < 1e-6, $"{AreaOf(1):F3}");
        var r2 = tiles.SelectMany(t => t.Polys).Where(q => q.Source == 0 && q.Record == 2).OrderBy(q => q.Part).ToList();
        check("S216 FT ① 바깥 둘에 각자 구멍(M1 — 담는 가장 작은 바깥)", r2.Count == 2 && r2.All(q => q.Rings.Count == 2) && Math.Abs(ForestTileFormat.PolyArea(r2[0]) - 9600) < 1e-6 && Math.Abs(ForestTileFormat.PolyArea(r2[1]) - 9900) < 1e-6,
            string.Join(" · ", r2.Select(q => $"부분 {q.Part} 고리 {q.Rings.Count} {ForestTileFormat.PolyArea(q):F1}")));
        check("S216 FT ① 나비넥타이 = 고친 넓이(두 귀 5,545.5 ㎡ · 건넘 마디 0.1 m 맞춤)", Math.Abs(AreaOf(4) - 5545.45) < 2, $"{AreaOf(4):F3}");
        check("S216 FT ① FRTP 0도 넣음(frtp = 0)", tiles.SelectMany(t => t.Polys).Any(q => q.Record == 5 && q.Source == 0 && q.Frtp == 0), "");
        check("S216 FT ① 10 ㎡ 부분 · 넓이 0 고리 = 안 넣음", TilesOf(6) == 0 && TilesOf(9) == 0, $"{TilesOf(6)} · {TilesOf(9)}");
        double sliver = tiles.Where(t => t.Tx == 499).SelectMany(t => t.Polys).Where(q => q.Source == 0 && q.Record == 7).Sum(ForestTileFormat.PolyArea);
        check("S216 FT ① 칸 경계 0.5 m 조각(50 ㎡)은 남음(25 ㎡ 규칙은 자르기 전 부분에만)", Math.Abs(sliver - 50) < 1e-6 && Math.Abs(AreaOf(7) - 10050) < 1e-6, $"조각 {sliver:F3} · 합 {AreaOf(7):F3}");
        check("S216 FT ① 1 m 솎기로 사라진 가는 띠(320 ㎡) = M1 넓이 목록에 오름", st.Flags.Any(f => (int)f[0] == 8) && st.FlagCount >= 1, $"목록 {st.FlagCount} · 띠 칸 넓이 {AreaOf(8):F1}");
        check("S216 FT ① 고리 방향(바깥 반시계 · 구멍 시계) · 끝점 되풀이 없음", tiles.SelectMany(t => t.Polys).All(q => ForestTileFormat.RingArea(q.Rings[0]) > 0 && q.Rings.Skip(1).All(h => ForestTileFormat.RingArea(h) < 0)
            && q.Rings.All(r => !(r[0] == r[^2] && r[1] == r[^1]))), "");
        // 코덱 비트 왕복
        bool codec = tiles.All(t => ForestTileFormat.EncodeTile(ForestTileFormat.DecodeTile(ForestTileFormat.EncodeTile(t))).AsSpan().SequenceEqual(ForestTileFormat.EncodeTile(t)));
        var raw0 = ix.Tiles.Keys.ToDictionary(k => k, k => File.ReadAllBytes(Path.Combine(outD, ForestTileFormat.RelPath(int.Parse(k.Split('_')[0], CultureInfo.InvariantCulture), int.Parse(k.Split('_')[1], CultureInfo.InvariantCulture)))));
        bool fileSame = tiles.All(t => ForestTileFormat.EncodeTile(t).AsSpan().SequenceEqual(raw0[ForestTileFormat.Key(t.Tx, t.Ty)]));
        check("S216 FT ① 코덱 비트 왕복(쓰기 → 읽기 → 쓰기 = 같은 바이트 · 파일 = 다시 쓴 것)", codec && fileSame, "");
        // 두 번 = 같은 바이트(--force)
        logs.Clear();
        var o2 = new ForestConvert.Options { Src = src, Out = outD, Threads = 2, Force = true, Log = s => logs.Add(s) };
        var ix2 = ForestConvert.Build(o2)!;
        bool same2 = ix2.Tiles.Keys.SequenceEqual(ix.Tiles.Keys) && ix2.Tiles.Keys.All(k => File.ReadAllBytes(Path.Combine(outD, ForestTileFormat.RelPath(int.Parse(k.Split('_')[0], CultureInfo.InvariantCulture), int.Parse(k.Split('_')[1], CultureInfo.InvariantCulture)))).AsSpan().SequenceEqual(raw0[k]));
        check("S216 FT ① 두 번(스레드 4 · 2) = 같은 바이트", same2, $"칸 {ix2.Tiles.Count}");
        // 이어 하기 — done 없음 → 그 원본만 지우고 다시 · 다른 원본은 건너뜀
        string w90 = Path.Combine(outD, "_work", "90");
        File.Delete(Path.Combine(w90, "done.json"));
        File.WriteAllText(Path.Combine(w90, "junk.txt"), "x");
        logs.Clear();
        ForestConvert.Build(new ForestConvert.Options { Src = src, Out = outD, Threads = 2, Log = s => logs.Add(s) });
        check("S216 FT ① 이어 하기 — done 없는 원본만 처음부터(옛 조각 지움) · 끝난 원본은 건너뜀",
            logs.Any(l => l.StartsWith("[90] 시작", StringComparison.Ordinal)) && logs.Any(l => l.StartsWith("[91] 이미 끝남", StringComparison.Ordinal)) && !File.Exists(Path.Combine(w90, "junk.txt")), "");
        // --only 91 → 합치기는 done 전부
        var ix3 = ForestConvert.Build(new ForestConvert.Options { Src = src, Out = outD, Threads = 2, Only = new HashSet<string> { "91" }, Log = _ => { } })!;
        check("S216 FT ① --only 91 뒤 합치기에 done 원본 전부(90 칸 포함)", ix3.Sources.Count == 2 && ix.Tiles.Keys.All(ix3.Tiles.ContainsKey), $"원본 {ix3.Sources.Count} · 칸 {ix3.Tiles.Count}");
        var v = ForestConvert.Verify(outD, 2, _ => { });
        check("S216 FT ① 검증 보고서(report.txt · report.json) · ③ 칸 경계 통과", File.Exists(Path.Combine(outD, "report.txt")) && File.Exists(Path.Combine(outD, "report.json")) && v.Text.Contains("0.01 % 넘음 0", StringComparison.Ordinal), $"② 실패 {v.Fails}(가는 띠가 1 m 솎기로 사라진 영향 — 일부러 넣은 꼴)");
        // 창고 고르기(5절 표) — 폴더 · 도면 사본 · 기본 경로 처음 · 지정 폴더 없음 · http
        string dwgDir = Path.Combine(root, "dwg");
        Directory.CreateDirectory(dwgDir);
        var p1 = ForestSource.Open(outD, true, false, dwgDir);
        var src1 = p1.Source!;
        var keys = ForestScatter.TilesFor(new Envelope(X - 100, X + 100, Y - 100, Y + 100)).ToList();
        var loaded = ForestSource.Load(src1, keys, out var used);
        int wrote = ForestSource.CopyUsed(src1, used, dwgDir);
        int wrote2 = ForestSource.CopyUsed(src1, used, dwgDir);
        var p2 = ForestSource.Open(Path.Combine(root, "없는폴더"), false, true, dwgDir);
        var p3 = ForestSource.Open(Path.Combine(root, "없는폴더"), true, false, null);
        var p4 = ForestSource.Open(Path.Combine(root, "없는폴더"), false, true, null);
        var p5 = ForestSource.Open("https://nas.example/forest", false, true, null);
        check("S216 FT ① 창고 고르기 — 폴더 → 사본(쓴 칸만 · 두 번째는 안 씀) → 처음(quiet) · 지정 없음(⚠) · http(아직)",
            p1.Source != null && loaded.Count == 4 && wrote == 4 && wrote2 == 0 && Directory.GetFiles(Path.Combine(dwgDir, ForestSource.CopyDir), "*.dhf", SearchOption.AllDirectories).Length == 4
            && p2.Source != null && p2.FromCopy && p3.Source == null && p3.Quiet && !p3.Warn && p4.Source == null && !p4.Quiet && p4.Warn && p4.Missing == "임상도 타일 폴더가 없습니다" && p5.Source == null && p5.Missing.StartsWith("웹 주소", StringComparison.Ordinal),
            $"사본 {wrote}/{wrote2} · {p2.Log} · {p3.Missing} · {p4.Missing} · {p5.Missing}");
    }

    // ── ② 좌표 ──
    static readonly (int Epsg, double Lon, double Lat, double X, double Y)[] Proj =
    {
        (5179, 127.0, 37.5, 955804.8163, 1944643.7158), (5179, 126.0, 35.0, 863117.1241, 1668255.9134), (5179, 129.0, 36.0, 1135196.2493, 1779173.7840),
        (5179, 128.5, 38.0, 1087798.4178, 2000471.7381), (5179, 125.0, 34.0, 769102.7537, 1559158.9057), (5179, 130.9, 37.5, 1300572.3065, 1949958.9847),
        (5179, 124.7, 37.8, 753476.7124, 1981503.5975), (5179, 131.8, 37.2, 1381689.7703, 1919913.2068),
        (5186, 127.0, 37.5, 200000.0000, 544504.1235), (5186, 127.5, 36.0, 245082.0224, 378160.2747), (5186, 126.5, 35.5, 154635.1151, 322682.4106), (5186, 128.0, 37.0, 289012.9296, 489480.4634),
        (5181, 127.2, 37.3, 217732.1593, 422325.8470), (5181, 126.8, 36.2, 182012.8935, 300255.3636),
        (5182, 126.5, 33.4, 153485.1424, 39724.9601), (5182, 126.9, 33.3, 190686.4316, 28526.6253),
        (5185, 125.5, 37.0, 244505.9931, 489129.8263), (5187, 129.2, 36.0, 218032.7490, 378063.1507), (5188, 131.0, 37.4, 200000.0000, 533405.5140),
    };

    static (double Cm, double Fn) Belt(int epsg) => epsg switch
    {
        5181 => (127, 500000), 5182 => (127, 550000), 5185 => (125, 600000), 5186 => (127, 600000), 5187 => (129, 600000), 5188 => (131, 600000), _ => (127, 600000),
    };

    static (double X, double Y) To(int epsg, double lon, double lat)
    {
        if (epsg == 5179) return KoreaTm.UtmkFromLonLat(lon, lat);
        var b = Belt(epsg);
        return KoreaTm.FromLonLat(lon, lat, b.Cm, b.Fn);
    }

    static void Coords(Action<string, bool, string> check)
    {
        // 옛 KoreaTm(겹함수 전 v114.2.1) — 비트까지 같아야
        int diff = 0, n = 0;
        for (double lon = 124.0; lon <= 132.0; lon += 0.0731)
            for (double lat = 33.0; lat <= 38.9; lat += 0.0537)
                foreach (var (cm, fn) in new[] { (125.0, 600000.0), (127.0, 600000.0), (129.0, 600000.0), (131.0, 600000.0), (127.0, 500000.0), (127.0, 550000.0) })
                {
                    var a = KoreaTm.FromLonLat(lon, lat, cm, fn); var b = OldTm.FromLonLat(lon, lat, cm, fn);
                    var c = KoreaTm.ToLonLat(a.E, a.N, cm, fn); var d = OldTm.ToLonLat(a.E, a.N, cm, fn);
                    n++;
                    if (BitConverter.DoubleToInt64Bits(a.E) != BitConverter.DoubleToInt64Bits(b.E) || BitConverter.DoubleToInt64Bits(a.N) != BitConverter.DoubleToInt64Bits(b.N)
                        || BitConverter.DoubleToInt64Bits(c.Lon) != BitConverter.DoubleToInt64Bits(d.Lon) || BitConverter.DoubleToInt64Bits(c.Lat) != BitConverter.DoubleToInt64Bits(d.Lat)) diff++;
                }
        check("S216 FT ② 옛 KoreaTm(k0 1 · FE 200000) 비트 같음", diff == 0, $"점 {n:N0} · 다름 {diff}");
        // 왕복 < 1 mm — 5179 ↔ 경위도 · 5179 → 경위도 → 5186 → 경위도 → 5179
        double worst = 0, worst2 = 0;
        for (double x = 760000; x <= 1380000; x += 15500)
            for (double y = 1460000; y <= 2040000; y += 14500)
            {
                var ll = KoreaTm.UtmkToLonLat(x, y);
                var back = KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat);
                worst = Math.Max(worst, Hyp(back.X - x, back.Y - y));
                var tm = KoreaTm.FromLonLat(ll.Lon, ll.Lat, 127, 600000);
                var ll2 = KoreaTm.ToLonLat(tm.E, tm.N, 127, 600000);
                var b2 = KoreaTm.UtmkFromLonLat(ll2.Lon, ll2.Lat);
                if (Math.Abs(ll.Lon - 127) < 2.5) worst2 = Math.Max(worst2, Hyp(b2.X - x, b2.Y - y));
            }
        check("S216 FT ② 5179 ↔ 경위도 왕복 < 1 mm · 5179 → 5186 → 5179(중부 ±2.5°) < 1 mm", worst < 1e-3 && worst2 < 1e-3, $"{worst * 1000:F4} mm · {worst2 * 1000:F4} mm");
        // PROJ 기준값 < 1 cm
        var bad = new List<string>(); double pw = 0; string pwAt = "";
        foreach (var (e, lon, lat, px, py) in Proj)
        {
            var q = To(e, lon, lat);
            double d = Hyp(q.X - px, q.Y - py);
            if (d > pw) { pw = d; pwAt = $"{e} {lon},{lat}"; }
            if (d >= 0.01) bad.Add($"{e} ({lon},{lat}) {d * 100:F2} cm");
        }
        check("S216 FT ② PROJ 기준값 < 1 cm(5179 · 5181 · 5182 · 5185~5188 · 19점)", bad.Count == 0, bad.Count > 0 ? string.Join(" · ", bad) : $"최대 {pw * 1000:F2} mm({pwAt})");
        // 네 모서리 봉투가 돌린 구역을 덮음 — 5186 네모(가짜 TM 200,000 · 550,000 근처) 둘레 촘촘히 → 5179 안에 다 듦 · 대각 두 모서리 봉투로는 모자람
        Func<double, double, (double X, double Y)> from = (x, y) => { var ll = KoreaTm.ToLonLat(x, y, 127, 600000); return KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat); };
        double x0 = 199000, y0 = 549000, x1 = 202000, y1 = 551500;
        var env = ForestScatter.Region5179(x0, y0, x1, y1, 0, from);
        var two = new Envelope(); var a0 = from(x0, y0); var a1 = from(x1, y1); two.ExpandToInclude(a0.X, a0.Y); two.ExpandToInclude(a1.X, a1.Y);
        int outN = 0, outTwo = 0;
        for (int i = 0; i <= 400; i++)
        {
            double t = i / 400.0;
            foreach (var (x, y) in new[] { (x0 + (x1 - x0) * t, y0), (x0 + (x1 - x0) * t, y1), (x0, y0 + (y1 - y0) * t), (x1, y0 + (y1 - y0) * t) })
            {
                var p = from(x, y);
                if (!new Envelope(env.MinX - 1e-6, env.MaxX + 1e-6, env.MinY - 1e-6, env.MaxY + 1e-6).Contains(p.X, p.Y)) outN++;
                if (!two.Contains(p.X, p.Y)) outTwo++;
            }
        }
        check("S216 FT ② 네 모서리 봉투가 돌린 구역 둘레를 다 덮음(두 모서리로는 모자람)", outN == 0 && outTwo > 0, $"밖 {outN} · 두 모서리면 밖 {outTwo}");
    }

    // ── 심기 공통 ──
    static ForestTile Tile(int tx, int ty, params ForestPoly[] ps) => new() { Tx = tx, Ty = ty, Polys = ps.ToList() };

    /// <summary>칸 안 네모 폴리곤(칸 남서부터 m).</summary>
    static ForestPoly Sq(uint src, ushort part, byte frtp, byte kof, byte dn, byte h, double x0, double y0, double x1, double y1, byte fror = 2, byte dm = 2)
    {
        ushort Q(double v) => (ushort)Math.Round(v / 0.1);
        return new ForestPoly { Src = src, Part = part, Frtp = frtp, Koftr = kof, Dnst = dn, Hgt = h, Dm = dm, Ag = 4, Fror = fror, Rings = new() { new[] { Q(x0), Q(y0), Q(x1), Q(y0), Q(x1), Q(y1), Q(x0), Q(y1) } } };
    }

    static ForestScatter.Input Flat(IReadOnlyList<ForestTile> tiles, double x0, double y0, double x1, double y1, int max = 1_000_000)
        => new() { Tiles = tiles, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, SurfZ = (_, _) => 10.0, Max = max };

    static string Sig(ForestScatter.Result r) => string.Join(";", r.Trees.OrderBy(t => t.Hash).Select(t => $"{t.X:R},{t.Y:R},{t.H:R},{t.W:R},{t.Kind},{t.Rot},{t.Tint}"));

    static void Plant(Action<string, bool, string> check)
    {
        // 칸 (500, 900) = 5179 X 1,000,000 ~ 1,002,000 · Y 1,800,000 ~ 1,802,000 — 좌표 변환 없음(같은 판)
        const double X = 1_000_000, Y = 1_800_000;
        var t = Tile(500, 900, Sq(1, 0, 2, 31, 3, 16, 0, 0, 600, 600), Sq(2, 0, 1, 11, 3, 14, 700, 0, 1300, 600), Sq(3, 0, 3, 77, 3, 12, 1400, 0, 2000, 600));
        var r1 = ForestScatter.Run(Flat(new[] { t }, X, Y, X + 2000, Y + 2000));
        var r2 = ForestScatter.Run(Flat(new[] { t }, X, Y, X + 2000, Y + 2000));
        check("S216 FT ③ 같은 입력 = 같은 배열(장면 바이트)", Pack(r1).SequenceEqual(Pack(r2)) && r1.Trees.Count > 1000, $"{r1.Trees.Count:N0}그루");
        check("S216 FT ③ 모양 — 활엽 31 = 참나무형 · 소나무 = 소나무형 · 혼효 = 0 : 3 반반",
            r1.Trees.Where(q => q.X < X + 600).All(q => q.Kind == 3) && r1.Trees.Where(q => q.X > X + 700 && q.X < X + 1300).All(q => q.Kind == 0)
            && Math.Abs(r1.Trees.Count(q => q.X > X + 1400 && q.Kind == 0) / (double)Math.Max(1, r1.Trees.Count(q => q.X > X + 1400)) - 0.5) < 0.05, "");
        // 한도 아래 · 구역 100 m 옮김 → 겹친 자리 나무 같음
        var r3 = ForestScatter.Run(Flat(new[] { t }, X + 100, Y + 100, X + 2000, Y + 2000));
        var inner = (Func<ForestScatter.Tree, bool>)(q => q.X >= X + 100 && q.Y >= Y + 100);
        var s1 = r1.Trees.Where(inner).Select(q => (q.X, q.Y, q.H)).ToHashSet(); var s3 = r3.Trees.Select(q => (q.X, q.Y, q.H)).ToHashSet();
        check("S216 FT ③ 한도 아래 — 구역 100 m 옮겨도 겹친 자리 나무 같음", s1.SetEquals(s3) && s1.Count > 0, $"{s1.Count:N0} · {s3.Count:N0}");
        // 쪼갠 폴리곤 = 통(같은 src · part) — 칸 경계에서 둘로
        var whole = Tile(500, 900, Sq(5, 0, 2, 31, 3, 16, 1000, 1000, 2000, 1500));
        var left = Tile(500, 900, Sq(5, 0, 2, 31, 3, 16, 1000, 1000, 2000, 1500));
        var halfA = Tile(500, 900, Sq(5, 0, 2, 31, 3, 16, 1000, 1000, 1500, 1500));
        var halfB = Tile(500, 900, Sq(5, 0, 2, 31, 3, 16, 1500, 1000, 2000, 1500));
        var rw = ForestScatter.Run(Flat(new[] { whole }, X, Y, X + 2000, Y + 2000));
        var rs = ForestScatter.Run(Flat(new[] { Tile(500, 900, halfA.Polys[0], halfB.Polys[0]) }, X, Y, X + 2000, Y + 2000));
        check("S216 FT ③ 쪼갠 폴리곤 = 통", Sig(rw) == Sig(rs) && rw.Trees.Count > 100 && left.Polys.Count == 1, $"{rw.Trees.Count} · {rs.Trees.Count}");
        // 겹친 폴리곤 — 같은 격자 → 한 그루(작은 src 속성)
        var ov = Tile(500, 900, Sq(9, 0, 2, 31, 3, 16, 0, 1000, 500, 1500), Sq(7, 0, 2, 31, 3, 16, 0, 1000, 500, 1500));
        var ro = ForestScatter.Run(Flat(new[] { ov }, X, Y, X + 2000, Y + 2000));
        var one = ForestScatter.Run(Flat(new[] { Tile(500, 900, Sq(7, 0, 2, 31, 3, 16, 0, 1000, 500, 1500)) }, X, Y, X + 2000, Y + 2000));
        check("S216 FT ③ 겹친 폴리곤 = 한 그루(작은 src 쪽)", Sig(ro) == Sig(one) && ro.DropOwner == one.Trees.Count, $"{ro.Trees.Count} · 겹침 {ro.DropOwner}");
        // 덮임별 그루/ha ±10 %(간격 식 그대로) — 500 m 네모 · 활엽 31 · 키 16(15~17) · 경급 2
        var dens = new List<string>(); bool okD = true;
        foreach (byte dn in new byte[] { 1, 2, 3 })
        {
            var td = Tile(500, 900, Sq(11, 0, 2, 31, dn, 16, 500, 500, 1500, 1500));
            var rd = ForestScatter.Run(Flat(new[] { td }, X, Y, X + 2000, Y + 2000));
            double s = ForestCodes.GridNo(ForestCodes.MeanCrown(3, (15, 17), 2), dn) * 0.5;
            double want = 1e6 / (s * s), got = rd.Trees.Count;
            if (Math.Abs(got / want - 1) > 0.10) okD = false;
            dens.Add($"{(dn == 1 ? "소" : dn == 2 ? "중" : "밀")} {got / 100:N0}/ha(식 {want / 100:N0})");
        }
        check("S216 FT ③ 덮임별 그루/ha ±10 %", okD, string.Join(" · ", dens));
        // 키 · 수관 — 구간 안 · 최대 25 m
        check("S216 FT ③ 키 = HEIGHT 구간 안 · 수관 ≤ 25 m", r1.Trees.Where(q => q.X < X + 600).All(q => q.H >= 15 && q.H < 17) && r1.Trees.All(q => q.W > 0 && q.W <= 25), "");
    }

    static byte[] Pack(ForestScatter.Result r)
    {
        var sc = new Web3DScene();
        sc.Head["forest"] = ForestScatter.Head(sc, r, 1_000_000, 1_800_000, (2020, 2020));
        return sc.Pack();
    }

    // ── ④ 빼기 ──
    static void Exclude(Action<string, bool, string> check)
    {
        const double X = 1_000_000, Y = 1_800_000;
        var t = Tile(500, 900, Sq(1, 0, 2, 31, 3, 16, 0, 0, 1000, 1000));
        // 지형 세모 — 100 m 격자 · 가운데 칸 둘 중 아래 세모 하나만 «계획면»(원지반 아님)
        var xs = new List<double>(); var ys = new List<double>(); var fs = new List<int>();
        int nG = 11;
        for (int j = 0; j < nG; j++) for (int i = 0; i < nG; i++) { xs.Add(X + i * 100); ys.Add(Y + j * 100); }
        var cls = new List<TerrainMesh.Cls>();
        for (int j = 0; j < nG - 1; j++)
            for (int i = 0; i < nG - 1; i++)
            {
                int a = j * nG + i, b = a + 1, c = a + nG + 1, d = a + nG;
                fs.AddRange(new[] { a, b, c }); cls.Add(i == 5 && j == 5 ? TerrainMesh.Cls.Plan : TerrainMesh.Cls.Ground);
                fs.AddRange(new[] { a, c, d }); cls.Add(TerrainMesh.Cls.Ground);
            }
        var gf = new GeometryFactory();
        var design = gf.CreatePolygon(new[] { new Coordinate(X + 100, Y + 100), new Coordinate(X + 300, Y + 100), new Coordinate(X + 300, Y + 300), new Coordinate(X + 100, Y + 300), new Coordinate(X + 100, Y + 100) });
        var mask = new ForestMask(xs.ToArray(), ys.ToArray(), fs.ToArray(), cls.ToArray(), new Geometry[] { design });
        var bld = gf.CreatePolygon(new[] { new Coordinate(X + 700, Y + 700), new Coordinate(X + 750, Y + 700), new Coordinate(X + 750, Y + 750), new Coordinate(X + 700, Y + 750), new Coordinate(X + 700, Y + 700) });
        var bldB = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(bld.Buffer(1.0));
        var inp = Flat(new[] { t }, X, Y, X + 1000, Y + 1000);
        inp.Design = mask.IsDesign;
        inp.Building = (x, y) => bldB.Covers(gf.CreatePoint(new Coordinate(x, y)));
        var r = ForestScatter.Run(inp);
        bool InTri(double x, double y) => x >= X + 500 && x <= X + 600 && y >= Y + 500 && y <= Y + 600 && (x - (X + 500)) >= (y - (Y + 500));
        var pd = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(design);
        int inDesign = r.Trees.Count(q => pd.Covers(gf.CreatePoint(new Coordinate(q.X, q.Y))) || pd.Covers(gf.CreatePoint(new Coordinate(q.X + 0.7 * q.W, q.Y))) || pd.Covers(gf.CreatePoint(new Coordinate(q.X - 0.7 * q.W, q.Y))));
        int inTri = r.Trees.Count(q => InTri(q.X, q.Y));
        int otherHalf = r.Trees.Count(q => q.X >= X + 500 && q.X <= X + 600 && q.Y >= Y + 500 && q.Y <= Y + 600 && (q.X - (X + 500)) < (q.Y - (Y + 500)) - 10);
        int inBld = r.Trees.Count(q => bldB.Covers(gf.CreatePoint(new Coordinate(q.X, q.Y))));
        check("S216 FT ④ 설계 다각형 · 원지반 아닌 세모 안 0 · 같은 칸 다른 반쪽(원지반 세모)엔 나무 있음(네모 민둥 자리 없음)", inDesign == 0 && inTri == 0 && otherHalf > 10 && r.DropDesign > 0, $"설계 {inDesign} · 세모 {inTri} · 반쪽 {otherHalf} · 뺌 {r.DropDesign}");
        check("S216 FT ④ 건물(+1 m) 안 0", inBld == 0 && r.DropBldg > 0, $"뺌 {r.DropBldg}");
        // 경사 — 60° 0 · 40° 남김 1 − 0.7 × 5/15 ±5 %
        var r60 = ForestScatter.Run(new ForestScatter.Input { Tiles = new[] { t }, X0 = X, Y0 = Y, X1 = X + 1000, Y1 = Y + 1000, SurfZ = (x, y) => (x - X) * Math.Tan(60 * Math.PI / 180) });
        var r40 = ForestScatter.Run(new ForestScatter.Input { Tiles = new[] { t }, X0 = X, Y0 = Y, X1 = X + 1000, Y1 = Y + 1000, SurfZ = (x, y) => (x - X) * Math.Tan(40 * Math.PI / 180) });
        var r0 = ForestScatter.Run(Flat(new[] { t }, X, Y, X + 1000, Y + 1000));
        double k40 = r40.Trees.Count / (double)r0.Trees.Count, want40 = 1 - 0.7 * 5 / 15.0;
        check("S216 FT ④ 급경사 60° = 0 · 40° 남김 ±5 %", r60.Trees.Count == 0 && Math.Abs(k40 - want40) < 0.05, $"60° {r60.Trees.Count} · 40° {k40:P1}(식 {want40:P1})");
        // FRTP 0 겹침 · 키 00 · 면 없음
        var tv = Tile(500, 900, Sq(1, 0, 2, 31, 3, 16, 0, 0, 1000, 1000), Sq(2, 0, 0, 82, 0, 255, 200, 200, 400, 400), Sq(3, 0, 1, 11, 3, 0, 1200, 0, 1800, 600));
        var rv = ForestScatter.Run(Flat(new[] { tv }, X, Y, X + 2000, Y + 2000));
        int inVoid = rv.Trees.Count(q => q.X >= X + 200 && q.X <= X + 400 && q.Y >= Y + 200 && q.Y <= Y + 400), inH00 = rv.Trees.Count(q => q.X >= X + 1200 && q.X <= X + 1800);
        var rz = ForestScatter.Run(new ForestScatter.Input { Tiles = new[] { t }, X0 = X, Y0 = Y, X1 = X + 1000, Y1 = Y + 1000, SurfZ = (x, y) => x < X + 500 ? null : 5.0 });
        check("S216 FT ④ FRTP 0 안 · 키 00 · 면 없음 = 0", inVoid == 0 && rv.DropFrtp0 > 0 && inH00 == 0 && rv.PolyH00 == 1 && rz.Trees.All(q => q.X >= X + 500) && rz.DropNoZ > 0, $"제지 뺌 {rv.DropFrtp0} · 키 00 폴리곤 {rv.PolyH00} · 면 없음 {rz.DropNoZ}");
        var hill = new ForestScatter.Input { Tiles = new[] { t }, X0 = X, Y0 = Y, X1 = X + 1000, Y1 = Y + 1000, SurfZ = (x, y) => 50 + 20 * Math.Sin((x - X) / 90) * Math.Cos((y - Y) / 70) };
        var rh = ForestScatter.Run(hill);
        double dz = rh.Trees.Max(q => Math.Abs(q.Z + 0.3 - hill.SurfZ(q.X, q.Y)!.Value));
        check("S216 FT ④ 밑동 + 0.3 = SurfZ(언덕 · 전수) · 자가검증 줄", dz < 1e-9 && rh.SelfN > 0 && rh.SelfDz < 1e-9 && rh.Text.StartsWith("수목: 칸", StringComparison.Ordinal), $"{dz:E1} · 표본 {rh.SelfN}");
    }

    // ── ⑤ 한도 ──
    static void CapTests(Action<string, bool, string> check)
    {
        const double X = 1_000_000, Y = 1_800_000;
        var t = Tile(500, 900, Sq(1, 0, 1, 11, 3, 14, 0, 0, 2000, 2000));
        ForestScatter.Result Run(int max, double cx0 = double.NaN)
        {
            var i = Flat(new[] { t }, X, Y, X + 2000, Y + 2000, max);
            if (double.IsFinite(cx0)) { i.CX0 = X + 900; i.CY0 = Y + 900; i.CX1 = X + 1100; i.CY1 = Y + 1100; }
            return ForestScatter.Run(i);
        }
        var all = Run(10_000_000, 1);
        var big = Run(60_000, 1); var small = Run(30_000, 1);
        var hs = small.Trees.Select(q => q.Hash).ToHashSet(); var hb = big.Trees.Select(q => q.Hash).ToHashSet();
        bool Near(ForestScatter.Tree q)
        {
            double dx = Math.Max(0, Math.Max(X + 900 - q.X, q.X - (X + 1100))), dy = Math.Max(0, Math.Max(Y + 900 - q.Y, q.Y - (Y + 1100)));
            return Math.Sqrt(dx * dx + dy * dy) <= 300;
        }
        int nearAll = all.Trees.Count(Near), nearBig = big.Trees.Count(Near);
        check("S216 FT ⑤ Σ ≤ 한도 · 작은 한도 ⊂ 큰 한도 · 가까운 자리 다 남음",
            all.Trees.Count > 60_000 && big.Trees.Count <= 60_000 && small.Trees.Count <= 30_000 && hs.IsSubsetOf(hb) && nearBig == nearAll && big.DropCap > 0,
            $"전부 {all.Trees.Count:N0} · 6만 → {big.Trees.Count:N0}(k {big.K:G3}) · 3만 → {small.Trees.Count:N0} · 가까운 {nearAll:N0}/{nearBig:N0}");
        // N1 — 가까운 자리만으로 한도를 넘음 → 가까운 자리 안에서 해시 순위로 한도만큼 · 먼 자리 0 · 작은 ⊂ 큰
        int cap1 = nearAll / 2, cap2 = nearAll / 3;
        var n1 = Run(cap1, 1); var n2 = Run(cap2, 1);
        check("S216 FT ⑤ N1 — 한도 < 가까운 자리 수: 수 = 한도 · 다 가까운 자리 · 작은 ⊂ 큰 · 그보다 큰 한도에도 들어감",
            n1.Trees.Count == cap1 && n2.Trees.Count == cap2 && n1.Trees.All(Near) && n2.Trees.Select(q => q.Hash).ToHashSet().IsSubsetOf(n1.Trees.Select(q => q.Hash).ToHashSet())
            && n1.Trees.Select(q => q.Hash).ToHashSet().IsSubsetOf(hb),
            $"가까운 {nearAll:N0} · 한도 {cap1:N0} → {n1.Trees.Count:N0} · {cap2:N0} → {n2.Trees.Count:N0}");
        var under = Run(10_000_000);
        check("S216 FT ⑤ 한도 아래 = 다 남김(k 없음)", under.DropCap == 0 && !under.Capped && double.IsNaN(under.K), $"{under.Trees.Count:N0}");
    }

    // ── ⑥ 장면 ──
    static void SceneTests(Action<string, bool, string> check)
    {
        var empty = new ForestScatter.Result();
        var sc = new Web3DScene();
        check("S216 FT ⑥ 나무 없으면 머리 null(열쇠를 안 넣음 → 옛 장면 비트 같음)", ForestScatter.Head(sc, empty, 0, 0, (0, 0)) == null && sc.BlobBytes == 0, "");
        // 덩이 · 모양 · 순위 차례 · grp 덮음
        var trees = new List<ForestScatter.Tree>();
        for (int i = 0; i < 2000; i++)
        {
            ulong h = ForestScatter.Mix((ulong)i);
            trees.Add(new ForestScatter.Tree { X = 200_000 + (h % 900), Y = 550_000 + ((h >> 20) % 700), Z = 40, H = 12, W = 5, Kind = (byte)((h >> 40) % 9), Hash = h, U = (h >> 11) * (1.0 / (1UL << 53)) });
        }
        var pk = ForestScatter.Pack(trees, 200_000, 550_000);
        bool cover = true; int sum = 0;
        for (int g = 0; g < pk.Grp.Length / 3; g++)
        {
            int s = pk.Grp[3 * g], n = pk.Grp[3 * g + 1], c = pk.Grp[3 * g + 2];
            if (s != sum) cover = false;
            sum += n;
            for (int i = s; i < s + n; i++)
            {
                if (pk.K[i] != pk.K[s]) cover = false;
                float x = pk.Xy[2 * i], y = pk.Xy[2 * i + 1];
                if (x < pk.CBox[4 * c] || x >= pk.CBox[4 * c + 2] || y < pk.CBox[4 * c + 1] || y >= pk.CBox[4 * c + 3]) cover = false;
            }
        }
        check("S216 FT ⑥ 장면 배열 — grp가 전부를 차례로 덮음 · 무리 = 한 모양 · 덩이 상자 안 · 17 B/그루", cover && sum == trees.Count && pk.Xy.Length == 2 * trees.Count && pk.Hw.Length == 2 * trees.Count,
            $"무리 {pk.Grp.Length / 3} · 덩이 {pk.CBox.Length / 4}");
    }

    // ── 화면 없는 그림 시험 자료(진짜 C# 흐름 · DH_FOREST_SYNTH=폴더일 때만) — 가짜 언덕 + 숲 셋 + 설계 자리 + 건물 ──
    static void Synth(Action<string, bool, string> check)
    {
        string? dir = Environment.GetEnvironmentVariable("DH_FOREST_SYNTH");
        if (string.IsNullOrEmpty(dir)) return;
        string? web = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "web3d", "page.html"))) { web = Path.Combine(d.FullName, "web3d"); break; }
        if (web == null || !File.Exists(Path.Combine(web, "dist", "viewer.js"))) { check("S216 FT 그림 자료 — web3d 폴더 찾기", false, AppContext.BaseDirectory); return; }
        Directory.CreateDirectory(dir);
        // 도면 = 가짜 TM(5186) · 지형 3 km 네모 · 4 m 격자 언덕
        const double ox = 200_000, oy = 550_000, half = 1500, cell = 6;
        int n = (int)(2 * half / cell) + 1;
        double Hill(double x, double y) { double u = (x - ox) / 400, v = (y - oy) / 330; return 120 + 60 * Math.Exp(-(u * u + v * v) / 2) + 25 * Math.Sin((x - ox) / 210) * Math.Cos((y - oy) / 260); }
        var X = new double[n * n]; var Yv = new double[n * n]; var Z = new double[n * n];
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int k = j * n + i; X[k] = ox - half + i * cell; Yv[k] = oy - half + j * cell; Z[k] = Hill(X[k], Yv[k]); }
        // 설계 자리 — 가운데 120 × 90 m 평평한 계획면(지형 높이를 바꿈)
        var gf = NtsSupport.Factory();
        double px0 = ox + 300, py0 = oy - 200, px1 = px0 + 120, py1 = py0 + 90, padZ = Hill(px0 + 60, py0 + 45);
        var Zd = (double[])Z.Clone();
        for (int k = 0; k < Zd.Length; k++)
            if (X[k] >= px0 - 30 && X[k] <= px1 + 30 && Yv[k] >= py0 - 30 && Yv[k] <= py1 + 30)
            {
                double dx = Math.Max(0, Math.Max(px0 - X[k], X[k] - px1)), dy = Math.Max(0, Math.Max(py0 - Yv[k], Yv[k] - py1)), d = Math.Sqrt(dx * dx + dy * dy);
                Zd[k] = d <= 0 ? padZ : d >= 30 ? Z[k] : padZ + (Z[k] - padZ) * d / 30;
            }
        var F = new List<int>();
        for (int j = 0; j + 1 < n; j++) for (int i = 0; i + 1 < n; i++) { int a = j * n + i; F.AddRange(new[] { a, a + 1, a + n + 1, a, a + n + 1, a + n }); }
        var plan = gf.CreatePolygon(new[] { new Coordinate(px0, py0), new Coordinate(px1, py0), new Coordinate(px1, py1), new Coordinate(px0, py1), new Coordinate(px0, py0) });
        var inp = new TerrainMesh.Input { X = X, Y = Yv, Z = Zd, F = F.ToArray(), GroundZ = (x, y) => Hill(x, y), Plan = plan };
        var tm = TerrainMesh.Build(inp, ox, oy);
        double? SurfZ(double x, double y)
        {
            double fi = (x - (ox - half)) / cell, fj = (y - (oy - half)) / cell;
            int i = (int)Math.Floor(fi), j = (int)Math.Floor(fj);
            if (i < 0 || j < 0 || i >= n - 1 || j >= n - 1) return null;
            double u = fi - i, v = fj - j; int a = j * n + i;
            return u >= v ? Zd[a] + u * (Zd[a + 1] - Zd[a]) + v * (Zd[a + n + 1] - Zd[a + 1]) : Zd[a] + v * (Zd[a + n] - Zd[a]) + u * (Zd[a + n + 1] - Zd[a + n]);
        }
        // 숲 셋(5179로 만들어 진짜 변환 흐름) — 침엽 · 활엽 · 혼효 + 대나무 조각 · 사이에 제지
        Func<double, double, (double X, double Y)> to5179 = (x, y) => { var ll = KoreaTm.ToLonLat(x, y, 127, 600000); return KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat); };
        Func<double, double, (double X, double Y)> toDwg = (x, y) => { var ll = KoreaTm.UtmkToLonLat(x, y); var q = KoreaTm.FromLonLat(ll.Lon, ll.Lat, 127, 600000); return (q.E, q.N); };
        var polys = new List<(double[] Ring, byte Frtp, byte Kof, byte Dn, byte H)>
        {
            (Rect(ox - 1400, oy - 1400, ox - 100, oy + 1400), 1, 11, 3, 16),
            (Rect(ox - 100, oy - 1400, ox + 1400, oy + 200), 2, 31, 3, 18),
            (Rect(ox - 100, oy + 200, ox + 1400, oy + 1400), 3, 77, 2, 14),
            (Rect(ox + 900, oy - 900, ox + 1100, oy - 700), 4, 78, 0, 255),
            (Rect(ox - 140, oy - 1400, ox - 100, oy + 1400), 0, 82, 0, 255),
        };
        static double[] Rect(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1 };
        var tilesD = new Dictionary<(int, int), ForestTile>();
        uint src = 1;
        foreach (var (ring, frtp, kof, dn, h) in polys)
        {
            // 도면 네모 → 5179 고리(변 따라 촘촘히) → 칸으로 자름(변환기와 같은 꼴)
            var cs = new List<Coordinate>();
            for (int e = 0; e < 4; e++)
            {
                double ax = ring[2 * e], ay = ring[2 * e + 1], bx = ring[(2 * e + 2) % 8], by = ring[(2 * e + 3) % 8];
                for (int s = 0; s < 20; s++) { var p = to5179(ax + (bx - ax) * s / 20, ay + (by - ay) * s / 20); cs.Add(new Coordinate(p.X, p.Y)); }
            }
            cs.Add(cs[0].Copy());
            var pg = new GeometryFactory(new PrecisionModel(10)).CreatePolygon(cs.ToArray());
            var env = pg.EnvelopeInternal;
            foreach (var (tx, ty) in ForestScatter.TilesFor(env))
            {
                double x0 = tx * 2000.0, y0 = ty * 2000.0;
                var rect = new GeometryFactory(new PrecisionModel(10)).ToGeometry(new Envelope(x0, x0 + 2000, y0, y0 + 2000));
                var cut = NetTopologySuite.Operation.OverlayNG.OverlayNG.Overlay(pg, rect, NetTopologySuite.Operation.Overlay.SpatialFunction.Intersection, new PrecisionModel(10));
                for (int gi = 0; gi < cut.NumGeometries; gi++)
                {
                    if (cut.GetGeometryN(gi) is not Polygon q || q.IsEmpty) continue;
                    var sh = q.ExteriorRing.Coordinates;
                    var rr = new List<ushort>();
                    for (int c = 0; c + 1 < sh.Length; c++) { rr.Add((ushort)Math.Round((sh[c].X - x0) * 10)); rr.Add((ushort)Math.Round((sh[c].Y - y0) * 10)); }
                    var arr = rr.ToArray();
                    if (ForestTileFormat.RingArea(arr) < 0) { var rev = new ushort[arr.Length]; for (int c = 0; c < arr.Length / 2; c++) { rev[2 * c] = arr[arr.Length - 2 - 2 * c]; rev[2 * c + 1] = arr[arr.Length - 1 - 2 * c]; } arr = rev; }
                    if (!tilesD.TryGetValue((tx, ty), out var tile)) tilesD[(tx, ty)] = tile = new ForestTile { Tx = tx, Ty = ty };
                    tile.Polys.Add(new ForestPoly { Src = src, Part = 0, Frtp = frtp, Koftr = kof, Dnst = dn, Hgt = h, Dm = 2, Ag = 4, Fror = 2, Rings = new() { arr } });
                }
            }
            src++;
        }
        // 건물 — 원지반 위 둘(그 둘레에 나무 없음)
        var raws = new List<Web3DBuildings.Raw>();
        foreach (var (bx, by, fl) in new[] { (ox - 600.0, oy + 300.0, "3"), (ox + 600.0, oy + 700.0, "5") })
            raws.Add(new Web3DBuildings.Raw { Id = $"b{bx}", Floors = fl, Polys = new() { new VWorldParcels.Poly { Shell = new() { (bx, by), (bx + 40, by), (bx + 40, by + 25), (bx, by + 25) } } } });
        var pr = Web3DBuildings.Place(raws, (x, y) => Hill(x, y), tm.InChanged, SurfZ);
        var bU = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(pr.Items.SelectMany(b => b.Polys).Select(p => (Geometry)gf.CreatePolygon(p.Shell.Append(p.Shell[0]).Select(q => new Coordinate(q.X, q.Y)).ToArray())).ToList()).Buffer(1.0);
        var bP = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(bU);
        var mask = new ForestMask(X, Yv, inp.F, tm.TriCls, new Geometry?[] { plan });
        var fi = new ForestScatter.Input
        {
            Tiles = tilesD.Values.ToList(), ToDrawing = toDwg, FromDrawing = to5179, X0 = ox - half, Y0 = oy - half, X1 = ox + half, Y1 = oy + half,
            CX0 = tm.CMinX, CY0 = tm.CMinY, CX1 = tm.CMaxX, CY1 = tm.CMaxY, SurfZ = SurfZ, Design = mask.IsDesign, Building = (x, y) => bP.Covers(new Point(x, y)),
            Max = int.TryParse(Environment.GetEnvironmentVariable("DH_FOREST_SYNTH_MAX"), out int mx) ? mx : 150_000,
        };
        var fr = ForestScatter.Run(fi);
        Console.WriteLine("  " + fr.Text);
        var sc = new Web3DScene();
        var H = sc.Head;
        H["app"] = "DH.Grading 수목 시험"; H["made"] = "시험"; H["dwg"] = "수목 시험"; H["dwgKey"] = "forest-synth"; H["epsg"] = 5186; H["origin"] = new[] { ox, oy };
        H["bbox"] = new[] { -half, -half, Zd.Min(), half, half, Zd.Max() };
        H["focus"] = new[] { px0 - ox - 20, py0 - oy - 20, padZ - 5, px1 - ox + 20, py1 - oy + 20, padZ + 5 };
        H["terrain"] = new Dictionary<string, object?>
        {
            ["pos"] = sc.F32(tm.Pos), ["idx"] = sc.U32(tm.Idx), ["cf"] = sc.F32(tm.Cf), ["ranges"] = tm.Ranges.Select(r => new[] { (int)r.Cls, r.Start, r.Count }).ToList(),
            ["classes"] = TerrainMesh.Keys.Select((k, i) => new Dictionary<string, object> { ["key"] = k, ["name"] = TerrainMesh.Names[i] }).ToList(),
        };
        H["walls"] = new List<object>(); H["patterns"] = new List<object>();
        var ar = Web3DBuildings.Pack(pr.Items, ox, oy);
        H["bldg"] = new Dictionary<string, object> { ["n"] = pr.Items.Count, ["xy"] = sc.F32(ar.Xy), ["ring"] = sc.U32(ar.Ring), ["poly"] = sc.U32(ar.Poly), ["bldg"] = sc.U32(ar.Bldg), ["z0"] = sc.F32(ar.Z0), ["h"] = sc.F32(ar.H), ["hit"] = sc.U8(ar.Hit), ["note"] = "시험 건물" };
        H["forest"] = ForestScatter.Head(sc, fr, ox, oy, (2020, 2024));
        H["geo"] = new Dictionary<string, object> { ["lat"] = 37.5, ["lon"] = 127.0 };
        H["warn"] = new List<string>();
        string path = Path.Combine(dir, "forest_synth.html");
        using (var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            Web3DScene.WritePage(w, File.ReadAllText(Path.Combine(web, "page.html")), File.ReadAllText(Path.Combine(web, "dist", "viewer.js")), "수목 시험", sc.PackGzip());
        // 시험 보기 자리(도면 좌표) — 숲 안 · 설계 자리 · 건물
        File.WriteAllText(Path.Combine(dir, "forest_synth_at.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            pad = new[] { (px0 + px1) / 2, (py0 + py1) / 2, padZ },
            inside = new[] { ox + 500, oy - 600, SurfZ(ox + 500, oy - 600) ?? 0 },
            bldg = new[] { ox - 580, oy + 312, SurfZ(ox - 580, oy + 312) ?? 0 },
            trees = fr.Trees.Count,
        }));
        bool padClear = fr.Trees.All(q => !(q.X >= px0 && q.X <= px1 && q.Y >= py0 && q.Y <= py1)) && fr.Trees.All(q => !bP.Covers(new Point(q.X, q.Y)));
        check("S216 FT 그림 자료 — forest_synth.html(진짜 C# 흐름 · 설계 · 건물 위 0)", File.Exists(path) && fr.Trees.Count > 1000 && padClear, $"{fr.Trees.Count:N0}그루 · {new FileInfo(path).Length / 1048576.0:F1} MB · {fr.Ms} ms");
    }
}

/// <summary>v114.2.1 KoreaTm 그대로(겹함수 전) — 비트 같음 비교용.</summary>
static class OldTm
{
    private const double A = 6378137.0, F = 1.0 / 298.257222101, K0 = 1.0, FE = 200000.0;
    private static readonly double Lat0 = 38.0 * Math.PI / 180.0;
    private const double E2 = 2 * F - F * F, Ep2 = E2 / (1 - E2);
    public static (double Lon, double Lat) ToLonLat(double e, double n, double lon0Deg, double fn)
    {
        double lon0 = lon0Deg * Math.PI / 180.0;
        double m0 = MeridArc(Lat0);
        double m = m0 + (n - fn) / K0;
        double mu = m / (A * (1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256));
        double e1 = (1 - Math.Sqrt(1 - E2)) / (1 + Math.Sqrt(1 - E2));
        double phi1 = mu
            + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
            + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu)
            + (1097 * Math.Pow(e1, 4) / 512) * Math.Sin(8 * mu);
        double sinp = Math.Sin(phi1), cosp = Math.Cos(phi1), tanp = Math.Tan(phi1);
        double c1 = Ep2 * cosp * cosp;
        double t1 = tanp * tanp;
        double n1 = A / Math.Sqrt(1 - E2 * sinp * sinp);
        double r1 = A * (1 - E2) / Math.Pow(1 - E2 * sinp * sinp, 1.5);
        double d = (e - FE) / (n1 * K0);
        double lat = phi1 - (n1 * tanp / r1) * (d * d / 2
            - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * Ep2) * Math.Pow(d, 4) / 24
            + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * Ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lon = lon0 + (d
            - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6
            + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * Ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / cosp;
        return (lon * 180.0 / Math.PI, lat * 180.0 / Math.PI);
    }
    public static (double E, double N) FromLonLat(double lonDeg, double latDeg, double lon0Deg, double fn)
    {
        double lon = lonDeg * Math.PI / 180.0;
        double lat = latDeg * Math.PI / 180.0;
        double lon0 = lon0Deg * Math.PI / 180.0;
        double sinp = Math.Sin(lat), cosp = Math.Cos(lat), tanp = Math.Tan(lat);
        double n1 = A / Math.Sqrt(1 - E2 * sinp * sinp);
        double t = tanp * tanp;
        double c = Ep2 * cosp * cosp;
        double a1 = (lon - lon0) * cosp;
        double m = MeridArc(lat);
        double m0 = MeridArc(Lat0);
        double e = FE + K0 * n1 * (a1
            + (1 - t + c) * Math.Pow(a1, 3) / 6
            + (5 - 18 * t + t * t + 72 * c - 58 * Ep2) * Math.Pow(a1, 5) / 120);
        double n = fn + K0 * (m - m0 + n1 * tanp * (a1 * a1 / 2
            + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a1, 4) / 24
            + (61 - 58 * t + t * t + 600 * c - 330 * Ep2) * Math.Pow(a1, 6) / 720));
        return (e, n);
    }
    private static double MeridArc(double phi) =>
        A * ((1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256) * phi
           - (3 * E2 / 8 + 3 * E2 * E2 / 32 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(2 * phi)
           + (15 * E2 * E2 / 256 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(4 * phi)
           - (35 * E2 * E2 * E2 / 3072) * Math.Sin(6 * phi));
}
