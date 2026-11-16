// ── S236 FA ★[v117.19 · JACK 1116 «회사 임상도 API»] 회사 GIS API → 임상도 칸 — 줄 읽기 · WKB · 5186→5179 · 속성 코드 · 칸 만들기 · 쪽 이어받기 · 못 쓸 때 다음 창고 ──
//   가짜 자료만(공개 저장소): 5179 X≈1,000,000 · Y≈1,800,000 부근 · 가짜 주소(127.0.0.1) · 진짜 주소 · 진짜 좌표 없음
using System.Globalization;
using System.Net;
using System.Text;
using DH.Grading.Core;
using DH.Grading.Core.Forest;

static class ForestApiCheck
{
    static double[] Cw(double x0, double y0, double x1, double y1) => new[] { x0, y0, x0, y1, x1, y1, x1, y0, x0, y0 };
    static double[] Ccw(double x0, double y0, double x1, double y1) => new[] { x0, y0, x1, y0, x1, y1, x0, y1, x0, y0 };

    // 가짜 레코드 — 5179 고리(SHP 꼴: 바깥 시계 · 구멍 반시계) + 속성(API 이름)
    sealed record Rec(List<double[]> Rings, Dictionary<string, string> At);
    static Dictionary<string, string> At(int gid, string frtp, string kof, string dn, string h, string dm = "2", string ag = "4", string fr = "2", string yr = "2020")
        => new() { ["gid"] = gid.ToString(CultureInfo.InvariantCulture), ["frtp_cd"] = frtp, ["koftr_grou"] = kof, ["dnst_cd"] = dn, ["height"] = h, ["dmcls_cd"] = dm, ["agcls_cd"] = ag, ["fror_cd"] = fr, ["갱신년도"] = yr,
                  ["frtp_nm"] = frtp == "0" ? "비산림" : "침엽수림", ["koftr_nm"] = "소나무", ["dmcls_nm"] = "중경목", ["agcls_nm"] = "4영급", ["dnst_nm"] = "밀", ["height_nm"] = "임분고" };

    static List<Rec> Recs() => new()
    {
        new(new() { Cw(1000123.37, 1800210.41, 1000263.37, 1800340.41) }, At(11, "1", "11", "C", "12")),
        new(new() { Cw(1001933.37, 1800410.41, 1002063.37, 1800540.41) }, At(12, "2", "31", "B", "10")),                       // 칸 경계(x = 1002000)를 건넘
        new(new() { Cw(1000713.37, 1801210.41, 1000963.37, 1801460.41), Ccw(1000793.37, 1801290.41, 1000873.37, 1801370.41) }, At(13, "3", "77", "A", "14")),   // 구멍
        new(new() { Cw(1000413.37, 1800810.41, 1000493.37, 1800890.41), Cw(1000613.37, 1800810.41, 1000693.37, 1800890.41) }, At(14, "1", "15", "C", "8")),   // 두 부분
        new(new() { Cw(1000113.37, 1801610.41, 1000213.37, 1801710.41) }, At(15, "0", "99", "", "")),                                 // 비산림
    };

    static string Wkb(List<double[]> rings5186, bool multi, bool bigEndian)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        void U32(uint v) { var b = BitConverter.GetBytes(v); if (bigEndian) Array.Reverse(b); w.Write(b); }
        void F64(double v) { var b = BitConverter.GetBytes(v); if (bigEndian) Array.Reverse(b); w.Write(b); }
        void Poly(IEnumerable<double[]> rs)
        {
            w.Write((byte)(bigEndian ? 0 : 1)); U32(3);
            var l = rs.ToList(); U32((uint)l.Count);
            foreach (var r in l) { U32((uint)(r.Length / 2)); foreach (var v in r) F64(v); }
        }
        // WKB는 바깥 반시계(OGC) — SHP와 반대로 적어 방향 바로잡기도 시험
        double[] Rev(double[] r) { var o = new double[r.Length]; int n = r.Length / 2; for (int i = 0; i < n; i++) { o[2 * i] = r[2 * (n - 1 - i)]; o[2 * i + 1] = r[2 * (n - 1 - i) + 1]; } return o; }
        var outers = rings5186.Where(r => Area2(r) < 0).Select(Rev).ToList();   // 시계 → 반시계
        var holes = rings5186.Where(r => Area2(r) > 0).Select(Rev).ToList();
        if (!multi && outers.Count == 1) Poly(new[] { outers[0] }.Concat(holes));
        else
        {
            w.Write((byte)(bigEndian ? 0 : 1)); U32(6); U32((uint)outers.Count);
            for (int i = 0; i < outers.Count; i++) Poly(new[] { outers[i] }.Concat(i == 0 ? holes : new List<double[]>()));
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    static double Area2(double[] r) { double a = 0; int n = r.Length / 2; for (int i = 0, j = n - 1; i < n; j = i++) a += r[2 * j] * r[2 * i + 1] - r[2 * i] * r[2 * j + 1]; return a; }

    static double[] To5186(double[] r5179) { var o = new double[r5179.Length]; for (int i = 0; i < r5179.Length / 2; i++) { var (e, n) = ForestApi.To5186(r5179[2 * i], r5179[2 * i + 1]); o[2 * i] = e; o[2 * i + 1] = n; } return o; }

    static readonly string[] Names = { "gid", "frtp_cd", "koftr_grou", "dnst_cd", "height", "dmcls_cd", "agcls_cd", "fror_cd", "갱신년도", "frtp_nm", "koftr_nm", "dmcls_nm", "agcls_nm", "dnst_nm", "height_nm" };

    static string Schema() => "{\"type\":\"schema\",\"format\":\"wkb-ndjson-v1\",\"layer\":\"tb_fgdi_im5000\",\"srid\":5186,\"fields\":["
        + string.Join(",", Names.Select(n => $"{{\"name\":\"{n}\",\"type\":\"character varying\"}}")) + "],\"pk\":\"gid\"}";

    static string Feature(Rec r, bool multi, bool be)
    {
        string wkb = Wkb(r.Rings.Select(To5186).ToList(), multi, be);
        var sb = new StringBuilder("{\"type\":\"feature\",\"wkb\":\"" + wkb + "\",\"attrs\":[");
        sb.Append(string.Join(",", Names.Select(n => n == "gid" ? r.At[n] : r.At[n].Length == 0 ? "null" : "\"" + r.At[n] + "\"")));
        return sb.Append("]}").ToString();
    }

    /// <summary>가짜 서버 — 쪽마다 <paramref name="pageSize"/>건 · mode: ok / 500 / cut(end 줄 없음).</summary>
    sealed class Fake : IDisposable
    {
        readonly HttpListener l = new();
        public readonly string Base;
        public int Wkb, Count, Health;
        public int Peak, Cur;
        public Fake(List<Rec> recs, int pageSize, string mode)
        {
            int port;
            while (true)
            {
                port = Random.Shared.Next(20000, 40000);
                try { l.Prefixes.Clear(); l.Prefixes.Add($"http://127.0.0.1:{port}/"); l.Start(); break; } catch (HttpListenerException) { }
            }
            Base = $"http://127.0.0.1:{port}";
            Task.Run(() =>
            {
                while (l.IsListening)
                {
                    HttpListenerContext c;
                    try { c = l.GetContext(); } catch { return; }
                    int cur = Interlocked.Increment(ref Cur); Peak = Math.Max(Peak, cur);
                    try
                    {
                        string p = c.Request.Url!.AbsolutePath;
                        var q = c.Request.QueryString;
                        byte[] body; int code = 200;
                        if (p == "/health") { Health++; body = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"); }
                        else if (p == "/v1/features/count") { Count++; body = Encoding.UTF8.GetBytes($"{{\"layer\":\"tb_fgdi_im5000\",\"count\":{recs.Count}}}"); }
                        else if (p == "/v1/features/wkb")
                        {
                            Wkb++;
                            if (mode == "500") { code = 500; body = Encoding.UTF8.GetBytes("{\"error\":\"서버 오류\",\"rid\":\"x\"}"); }
                            else
                            {
                                long after = long.TryParse(q["after"], out long a) ? a : 0;
                                var rest = recs.Where(r => long.Parse(r.At["gid"], CultureInfo.InvariantCulture) > after).ToList();
                                var pg = rest.Take(pageSize).ToList();
                                var sb = new StringBuilder(Schema() + "\n");
                                foreach (var r in pg) sb.Append(Feature(r, r.Rings.Count(x => Area2(x) < 0) > 1, recs.IndexOf(r) % 2 == 1) + "\n");
                                // 쪽 사이 겹침(앞 쪽 마지막 건을 한 번 더) — 받는 쪽이 gid로 거른다
                                if (after > 0 && mode == "ok") { var prev = recs.Last(r => long.Parse(r.At["gid"], CultureInfo.InvariantCulture) <= after); sb.Append(Feature(prev, false, false) + "\n"); }
                                if (mode != "cut") sb.Append($"{{\"type\":\"end\",\"count\":{pg.Count},\"next_after\":{(rest.Count > pageSize ? pg[^1].At["gid"] : "null")}}}\n");
                                body = Encoding.UTF8.GetBytes(sb.ToString());
                            }
                        }
                        else { code = 404; body = Encoding.UTF8.GetBytes("{}"); }
                        c.Response.StatusCode = code; c.Response.ContentLength64 = body.Length; c.Response.OutputStream.Write(body); c.Response.Close();
                    }
                    catch { try { c.Response.Abort(); } catch { } }
                    finally { Interlocked.Decrement(ref Cur); }
                }
            });
        }
        public void Dispose() { try { l.Stop(); l.Close(); } catch { } }
    }

    // 폴더 길(변환기와 같은 ProcessRecord · 5179 원본 고리) — 칸 바이트
    static Dictionary<(int, int), byte[]> FolderTiles(List<Rec> recs, HashSet<(int, int)> want)
    {
        var items = new Dictionary<(int, int), List<(long, int, byte[])>>();
        uint n = 0;
        foreach (var r in recs)
        {
            var at = ForestApi.Attrs(k => r.At.TryGetValue(k, out var v) ? v : "", ((uint)ForestApi.SourceId << 24) | ++n);
            var ro = new ForestConvert.RecOut();
            ForestConvert.ProcessRecord(r.Rings.Select(x => (double[])x.Clone()).ToList(), at, 1.0, 25.0, ro);
            if (ro.Status > 1) continue;
            foreach (var pc in ro.Pieces)
            {
                if (!want.Contains((pc.Tx, pc.Ty))) continue;
                if (!items.TryGetValue((pc.Tx, pc.Ty), out var l)) items[(pc.Tx, pc.Ty)] = l = new();
                l.Add((pc.P.Order, pc.Piece, ForestTileFormat.EncodePoly(pc.P)));
            }
        }
        return items.ToDictionary(k => k.Key, k => ForestTileFormat.EncodeTile(k.Key.Item1, k.Key.Item2, k.Value.OrderBy(q => q.Item1).ThenBy(q => q.Item2).Select(q => q.Item3).ToList()));
    }

    static string Tmp()
    {
        string d = Path.Combine(Path.GetTempPath(), "dh_forestapi_" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    public static void Run(Action<string, bool, string> check)
    {
        Console.WriteLine("\n== S236 FA 회사 임상도 API(가짜 서버 · 가짜 좌표) ==");
        var recs = Recs();

        // ① 좌표 5186 ↔ 5179
        {
            var (x, y) = ForestApi.To5179(200000, 600000);   // 중부원점(127° · 38°)
            var ll = KoreaTm.UtmkToLonLat(x, y);
            bool origin = Math.Abs(ll.Lon - 127.0) < 1e-9 && Math.Abs(ll.Lat - 38.0) < 1e-9;
            double worst = 0;
            foreach (var (e, n) in new[] { (150000.0, 520000.0), (233333.3, 577777.7), (310000.0, 480000.0) })
            {
                var (x2, y2) = ForestApi.To5179(e, n); var (e2, n2) = ForestApi.To5186(x2, y2);
                worst = Math.Max(worst, Math.Max(Math.Abs(e2 - e), Math.Abs(n2 - n)));
                var l2 = KoreaTm.ToLonLat(e, n, 127.0, 600000.0); var u = KoreaTm.UtmkFromLonLat(l2.Lon, l2.Lat);   // KoreaTm 직접
                worst = Math.Max(worst, Math.Max(Math.Abs(u.X - x2), Math.Abs(u.Y - y2)));
            }
            check("S236 ① 5186 원점 = 경도 127 · 위도 38 · 왕복 · KoreaTm 직접과 같음", origin && worst < 1e-4, $"왕복 최대 {worst:E2} m");
        }

        // ② WKB — 다각형 · 다중다각형 · 빅엔디안 · 방향 바로잡기
        {
            var r5 = recs[2].Rings;   // 바깥 + 구멍
            var rr = ForestApi.ParseWkbRings(Convert.FromBase64String(Wkb(r5.Select(To5186).ToList(), false, false)));
            bool okPoly = rr.Count == 2 && Area2(rr[0]) < 0 && Area2(rr[1]) > 0 && rr[0].Length == 10;
            var m5 = recs[3].Rings;
            var mr = ForestApi.ParseWkbRings(Convert.FromBase64String(Wkb(m5.Select(To5186).ToList(), true, true)));
            bool okMulti = mr.Count == 2 && mr.All(r => Area2(r) < 0);
            bool bad = false;
            try { ForestApi.ParseWkbRings(new byte[] { 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }); } catch (InvalidDataException) { bad = true; }   // 점(형 1)
            bool cut = false;
            try { var b = Convert.FromBase64String(Wkb(r5.Select(To5186).ToList(), false, false)); ForestApi.ParseWkbRings(b[..(b.Length - 5)]); } catch (InvalidDataException) { cut = true; }
            check("S236 ② WKB 다각형 · 다중다각형(빅엔디안) · 방향 · 형 틀림 · 잘림", okPoly && okMulti && bad && cut, $"다각형 {rr.Count}고리 · 다중 {mr.Count}고리");
        }

        // ③ 속성 코드 = 변환기와 같음(같은 값 → 같은 바이트) · 숫자로 온 값 · 빈 값
        {
            var g = Recs()[0].At; var a = ForestApi.Attrs(k => g[k], 1);
            var exp = new ForestPoly { Frtp = ForestCodes.Frtp("1"), Koftr = ForestCodes.Koftr("11"), Dnst = ForestCodes.Dnst("C"), Hgt = ForestCodes.Height("12"), Dm = ForestCodes.Dm("2"), Ag = ForestCodes.Small("4"), Fror = ForestCodes.Small("2") };
            bool same = (a.Frtp, a.Koftr, a.Dnst, a.Hgt, a.Dm, a.Ag, a.Fror) == (exp.Frtp, exp.Koftr, exp.Dnst, exp.Hgt, exp.Dm, exp.Ag, exp.Fror) && (a.Frtp, a.Koftr, a.Dnst, a.Hgt) == ((byte)1, (byte)11, (byte)3, (byte)12);
            var e0 = ForestApi.Attrs(_ => "", 1);   // 빈 값 — FRTP 모름 → 0 · 높이 255 · 밀도 0
            var js = "{\"type\":\"schema\",\"fields\":[{\"name\":\"FRTP_CD\"},{\"name\":\"height\"}]}\n{\"type\":\"feature\",\"wkb\":\"AA==\",\"attrs\":[2,null]}\n{\"type\":\"end\",\"count\":1,\"next_after\":null}\n";
            string got = "";
            ForestApi.ReadPage(new StringReader(js), (w, get) => got = get("frtp_cd") + "|" + get("height") + "|" + get("없는칸"));
            check("S236 ③ 속성 코드 = 변환기 함수 · 빈 값 · 숫자 값 · 대소문자 무시", same && e0.Frtp == 0 && e0.Hgt == 255 && e0.Dnst == 0 && got == "2||", $"{got}");
        }

        // ④ 한 번에 만들기 = 폴더 길(같은 다각형 → 같은 칸 바이트)
        var want = new HashSet<(int, int)>();
        foreach (var r in recs) { var e = r.Rings[0]; for (int i = 0; i < e.Length / 2; i++) want.Add((ForestTileFormat.TileOf(e[2 * i]), ForestTileFormat.TileOf(e[2 * i + 1]))); }
        want.Add((499, 900));   // 달라고 했지만 숲 없는 칸
        var folder = FolderTiles(recs, want);
        ForestApiSource? src;
        {
            var b = new ForestApiBuilder(want);
            foreach (var r in recs) b.Add(ForestApi.ParseWkbRings(Convert.FromBase64String(Wkb(r.Rings.Select(To5186).ToList(), r.Rings.Count(x => Area2(x) < 0) > 1, false))), k => r.At.TryGetValue(k, out var v) ? v : "");
            src = b.Finish();
            bool keys = src.Index.Tiles.Keys.OrderBy(k => k).SequenceEqual(folder.Keys.Select(k => ForestTileFormat.Key(k.Item1, k.Item2)).OrderBy(k => k));
            bool bytes = folder.All(kv => src.Tile(kv.Key.Item1, kv.Key.Item2) is { } t && t.AsSpan().SequenceEqual(kv.Value));
            int polys = src.Index.Tiles.Values.Sum(v => (int)v[0]);
            check("S236 ④ API 칸 = 폴더 길 칸(칸 수 · 바이트 · 경계 조각 · 구멍 · 두 부분 · 비산림)", keys && bytes && folder.Count >= 2, $"칸 {folder.Count} · 조각 {polys} · 바이트 같음 {bytes}");
            check("S236 ④b 색인 — 원본 1개 · 갱신년도 · 이름표 · 숲 없는 칸은 칸에 없음", src.Index.Sources.Count == 1 && src.Index.Sources[0].YearMin == 2020 && src.Index.Codes.ContainsKey("frtp") && src.Tile(499, 900) == null && !src.Index.Tiles.ContainsKey("499_900"), $"records {src.Index.Sources[0].Records} kept {src.Index.Sources[0].Kept} frtp0 {src.Index.Sources[0].Frtp0}");
        }

        // ⑤ 가짜 서버 — 쪽 이어받기(쪽마다 2건 · 겹침 · gid 거르기) = ④와 같음 · 요청은 하나씩
        var keyList = want.ToList();
        ForestApiClient.ResetCache();
        using (var f = new Fake(recs, 2, "ok"))
        {
            var r = ForestApiClient.TryFetch(f.Base, keyList);
            bool same = r.Source != null && r.Source.Index.Tiles.Count == src.Index.Tiles.Count && folder.All(kv => r.Source.Tile(kv.Key.Item1, kv.Key.Item2) is { } t && t.AsSpan().SequenceEqual(kv.Value));
            check("S236 ⑤ 쪽 이어받기(2건씩 · 겹침 거름) → ④와 같은 칸 · 건강 확인 1 · 건수 1 · 쪽 ≥ 3 · 동시 요청 1", same && f.Health == 1 && f.Count == 1 && f.Wkb >= 3 && f.Peak <= 1 && r.Polys == recs.Count, $"쪽 {f.Wkb} · 폴리곤 {r.Polys} · 놓침 {r.Note}");
            // 같은 세션 두 번째 — 건강 확인을 다시 안 함
            ForestApiClient.TryFetch(f.Base, keyList);
            check("S236 ⑤b 건강 확인은 세션 동안 기억(두 번째 호출에도 1회)", f.Health == 1, $"건강 확인 {f.Health}");
        }

        // ⑥ 못 쓸 때 — 닫힌 주소 · 500 · 끊긴 응답 · 주소 없음 → 창고 → 사본 → 없음 (까닭 한 줄 · 주소는 안 적음)
        string root = Tmp();
        string tilesDir = Path.Combine(root, "창고");
        foreach (var kv in folder) { string p = Path.Combine(tilesDir, ForestTileFormat.RelPath(kv.Key.Item1, kv.Key.Item2)); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, kv.Value); }
        src.Index.Write(Path.Combine(tilesDir, "index.json"));
        string dwg = Path.Combine(root, "도면"); Directory.CreateDirectory(dwg);
        ForestApiClient.HealthTimeout = TimeSpan.FromSeconds(2);
        void Case(string name, string? apiBase, Func<ForestSource.Pick, bool> ok, string tiles, string? dwgDir, Func<string, bool>? noteOk)
        {
            ForestApiClient.ResetCache();
            var pk = ForestSource.OpenWithApi(apiBase, keyList, tiles, false, true, dwgDir, null, out string note, out int polys);
            bool leak = apiBase != null && apiBase.Length > 8 && (note.Contains(apiBase) || pk.Log.Contains(apiBase));
            check($"S236 ⑥ 못 쓸 때 — {name}", ok(pk) && !leak && (noteOk?.Invoke(note) ?? true), $"창고={pk.Source?.Describe ?? "없음"} · 글 «{note}» «{pk.Log}»");
        }
        Case("닫힌 주소 → 창고", "http://127.0.0.1:1", p => p.Source is FolderForestSource && !p.FromApi, tilesDir, dwg, n => n.StartsWith("회사 API 못 씀"));
        using (var f500 = new Fake(recs, 2, "500")) Case("서버 500 → 창고", f500.Base, p => p.Source is FolderForestSource, tilesDir, dwg, n => n.StartsWith("회사 API 못 씀"));
        using (var fcut = new Fake(recs, 2, "cut")) Case("끊긴 응답(end 줄 없음) → 창고", fcut.Base, p => p.Source is FolderForestSource, tilesDir, dwg, n => n.StartsWith("회사 API 못 씀"));
        Case("주소 없음(키 파일에 gisapi 없음) → 창고, 글 없음", "", p => p.Source is FolderForestSource, tilesDir, dwg, n => n.Length == 0);
        {   // API 못 씀 + 창고 없음 → 도면 폴더 사본
            int w = ForestSource.CopyUsed(src, keyList, src.Index.Tiles.Keys.Select(k => (int.Parse(k.Split('_')[0], CultureInfo.InvariantCulture), int.Parse(k.Split('_')[1], CultureInfo.InvariantCulture))).ToList(), dwg);
            Case("닫힌 주소 + 창고 없음 → 도면 폴더 사본", "http://127.0.0.1:1", p => p.FromCopy && p.Source != null, Path.Combine(root, "없는폴더"), dwg, n => n.StartsWith("회사 API 못 씀"));
            Case("닫힌 주소 + 창고 없음 + 사본 없음 → 없음", "http://127.0.0.1:1", p => p.Source == null, Path.Combine(root, "없는폴더"), null, null);
            var cp = ForestSource.OpenCopy(dwg, out var copy, out _) ? copy! : null;
            bool same = cp != null && folder.All(kv => cp.Tile(kv.Key.Item1, kv.Key.Item2) is { } t && t.AsSpan().SequenceEqual(kv.Value));
            check("S236 ⑦ API 칸 → 도면 폴더 사본(CopyUsed) → 다시 읽으면 같은 바이트 · 새로 쓴 칸 수", same && w == folder.Count, $"쓴 칸 {w}/{folder.Count}");
        }
        // 정말 끝까지 — API가 되면 API가 먼저(창고가 있어도)
        ForestApiClient.ResetCache();
        using (var f = new Fake(recs, 100, "ok"))
        {
            var pk = ForestSource.OpenWithApi(f.Base, keyList, tilesDir, false, true, dwg, null, out string note, out int polys);
            check("S236 ⑧ API가 되면 창고가 있어도 API가 먼저", pk.FromApi && pk.Source is ForestApiSource && polys == recs.Count && note.Length == 0 && pk.Log == ForestApi.Label, $"폴리곤 {polys} · {pk.Log}");
        }
        try { Directory.Delete(root, true); } catch { }
        Clearing(check);
        Live(check);
    }

    /// <summary>벌개제근 = 공사 범위 ∩ 입목지 — 풀이가 손으로 되는 가짜 네모(5179 → 도면 = 평행이동).</summary>
    static void Clearing(Action<string, bool, string> check)
    {
        double X = 1000000, Y = 1800000;
        var recs = new List<Rec>
        {
            new(new() { Cw(X + 100, Y + 100, X + 300, Y + 300) }, At(1, "1", "11", "C", "12", "2")),
            new(new() { Cw(X + 400, Y + 100, X + 600, Y + 300), Ccw(X + 450, Y + 150, X + 550, Y + 250) }, At(2, "2", "31", "B", "10", "1")),
            new(new() { Cw(X + 700, Y + 100, X + 900, Y + 300) }, At(3, "0", "99", "", "", "")),   // 무립목지 · 비산림 — 뺌
            new(new() { Cw(X + 100, Y + 400, X + 300, Y + 600) }, At(4, "4", "78", "A", "8", "3")),
        };
        var want = new HashSet<(int, int)> { (500, 900) };
        var tiles = FolderTiles(recs, want).Select(kv => ForestTileFormat.DecodeTile(kv.Value)).ToList();
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        NetTopologySuite.Geometries.Polygon Rect(double x0, double y0, double x1, double y1) => gf.CreatePolygon(new[] { new NetTopologySuite.Geometries.Coordinate(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1), new(x0, y0) });
        var fp = Rect(200, 200, 500, 450).Union(Rect(700, 200, 800, 300));   // 도면 좌표(= 5179 − (X, Y))
        var r = ForestClearing.Compute(tiles, (x, y) => (x - X, y - Y), fp);
        bool ok = r.Known && Math.Abs(r.Total - 22500) < 1 && Math.Abs(r.ByFrtp[1] - 10000) < 1 && Math.Abs(r.ByFrtp[2] - 7500) < 1 && r.ByFrtp[3] == 0 && Math.Abs(r.ByFrtp[4] - 5000) < 1
                  && Math.Abs(r.ByDm[2] - 10000) < 1 && Math.Abs(r.ByDm[1] - 7500) < 1 && Math.Abs(r.ByDm[3] - 5000) < 1 && r.Polys == 3;
        check("S236 ⑨ 벌개제근 = 범위 ∩ 입목지 · 구멍 · 무립목지 뺌 · 임상 · 경급별 값(손셈 22,500 = 10,000 + 7,500 + 5,000)", ok, r.Detail());
        var r0 = ForestClearing.Compute(tiles, (x, y) => (x - X, y - Y), Rect(5000, 5000, 5100, 5100));
        var rn = ClearingResult.None("임상도 없음 → 벌개제근 못 셈");
        check("S236 ⑨b 범위 밖 = 잰 0 · 임상도 없음 = «못 잼»(0이 아님)", r0.Known && r0.Total == 0 && !rn.Known, $"{r0.Total} · {rn.Note}");
    }

    /// <summary>실서버 비교(회사망 · 선택) — DH_FOREST_API(주소) · DH_FOREST_TILES(창고 폴더) · DH_FOREST_KEY("tx_ty") 셋이 있을 때만. 건수 · 임상 넓이 비율만 적는다(좌표 · 주소 안 적음).</summary>
    static void Live(Action<string, bool, string> check)
    {
        string? api = Environment.GetEnvironmentVariable("DH_FOREST_API"), wh = Environment.GetEnvironmentVariable("DH_FOREST_TILES"), kk = Environment.GetEnvironmentVariable("DH_FOREST_KEY");
        if (string.IsNullOrEmpty(api) || string.IsNullOrEmpty(wh) || string.IsNullOrEmpty(kk)) return;
        var k = kk.Split('_'); var key = (int.Parse(k[0], CultureInfo.InvariantCulture), int.Parse(k[1], CultureInfo.InvariantCulture));
        ForestApiClient.ResetCache();
        var r = ForestApiClient.TryFetch(api, new[] { key });
        var fs = new FolderForestSource(wh);
        if (r.Source == null) { check("S236 실서버 받기", false, r.Note); return; }
        double[] Area(IForestTileSource s) { var a = new double[5]; var b = s.Tile(key.Item1, key.Item2); if (b == null) return a; foreach (var p in ForestTileFormat.DecodeTile(b).Polys) a[Math.Min(4, (int)p.Frtp)] += ForestTileFormat.PolyArea(p); return a; }
        int Cnt(IForestTileSource s) => s.Index.Tiles.TryGetValue(ForestTileFormat.Key(key.Item1, key.Item2), out var v) ? (int)v[0] : 0;
        var A = Area(r.Source); var B = Area(fs);
        string d = string.Join(" ", Enumerable.Range(0, 5).Select(i => $"임상{i} API {A[i]:F0} 창고 {B[i]:F0}"));
        double ta = A.Sum(), tb = B.Sum();
        Console.WriteLine($"  [실서버] 받은 폴리곤 {r.Polys} · 쪽 {r.Requests} · 조각 API {Cnt(r.Source)} / 창고 {Cnt(fs)} · {d}");
        check("S236 실서버 받기 · 같은 칸 창고와 비교(2025판 ≠ 창고판이라 «참고»)", r.Polys > 0, $"숲 넓이 API/창고 = {(tb > 0 ? ta / tb : double.NaN):F3}");
    }
}
