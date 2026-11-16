using System.Globalization;
using System.Text.Json;

namespace DH.Grading.Core.Forest;

/// <summary>★[v117.19 · JACK 1116 «회사 임상도 API»] 회사 GIS 데이터 API(<c>tb_fgdi_im5000</c> · 2025판 · EPSG 5186) → 같은 DHF1 칸.
/// 줄 읽기(NDJSON) · WKB 읽기 · 5186 → 5179 · 칸 만들기는 순수 계산(하네스 «forestapi»). 주소는 소스에 없다(설치 키 파일 «gisapi»).</summary>
public static class ForestApi
{
    public const string Layer = "tb_fgdi_im5000";
    public const string Label = "회사 API(2025)";
    public const int SourceId = 1;

    // ── 좌표: 5186(중부원점 · 600000) ↔ 5179(UTM-K) — 경위도를 거친다 ──
    public static (double X, double Y) To5179(double e, double n)
    {
        var ll = KoreaTm.ToLonLat(e, n, 127.0, 600000.0);
        return KoreaTm.UtmkFromLonLat(ll.Lon, ll.Lat);
    }

    public static (double E, double N) To5186(double x, double y)
    {
        var ll = KoreaTm.UtmkToLonLat(x, y);
        return KoreaTm.FromLonLat(ll.Lon, ll.Lat, 127.0, 600000.0);
    }

    /// <summary>달라고 한 칸들(5179) 테두리를 5186으로 옮겨 감싼 상자 + 여유(m) — [minx, miny, maxx, maxy].</summary>
    public static double[] Bbox5186(IEnumerable<(int Tx, int Ty)> keys, double margin = 200)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        const int n = 8;
        double s = ForestTileFormat.TileSize;
        foreach (var (tx, ty) in keys)
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    if (i != 0 && i != n && j != 0 && j != n) continue;
                    var (e, nn) = To5186(tx * s + s * i / n, ty * s + s * j / n);
                    x0 = Math.Min(x0, e); y0 = Math.Min(y0, nn); x1 = Math.Max(x1, e); y1 = Math.Max(y1, nn);
                }
        if (x0 > x1) throw new ArgumentException("칸 없음");
        return new[] { Math.Floor(x0 - margin), Math.Floor(y0 - margin), Math.Ceiling(x1 + margin), Math.Ceiling(y1 + margin) };
    }

    // ── WKB(2차원 · Polygon 3 / MultiPolygon 6 · EWKB · Z/M는 건너뜀) → 고리들(바깥 = 시계 · 구멍 = 반시계 · SHP 꼴) ──
    public static List<double[]> ParseWkbRings(byte[] b)
    {
        var rings = new List<double[]>();
        int o = 0;
        ReadGeom(b, ref o, rings);
        return rings;
    }

    static void ReadGeom(byte[] b, ref int o, List<double[]> rings)
    {
        if (o + 5 > b.Length) throw new InvalidDataException("WKB 잘림");
        bool le = b[o++] == 1;
        uint t = U32(b, ref o, le);
        int dims = 2;
        if ((t & 0x80000000) != 0) dims++;
        if ((t & 0x40000000) != 0) dims++;
        if ((t & 0x20000000) != 0) U32(b, ref o, le);   // SRID
        uint bt = t & 0xFFFF;
        int iso = (int)(bt / 1000);   // ISO 1000 = Z · 2000 = M · 3000 = ZM
        dims += iso == 3 ? 2 : iso > 0 ? 1 : 0;
        bt %= 1000;
        if (bt == 3)
        {
            uint nr = U32(b, ref o, le);
            if (nr > (uint)b.Length) throw new InvalidDataException("WKB 고리 수");
            for (uint k = 0; k < nr; k++)
            {
                uint np = U32(b, ref o, le);
                if ((long)np * dims * 8 > b.Length - o) throw new InvalidDataException("WKB 점 수");
                var r = new double[np * 2];
                for (uint i = 0; i < np; i++)
                {
                    r[2 * i] = F64(b, ref o, le); r[2 * i + 1] = F64(b, ref o, le);
                    o += 8 * (dims - 2);
                }
                bool cw = Area2(r) < 0;
                if (cw != (k == 0)) r = Flip(r);
                rings.Add(r);
            }
        }
        else if (bt == 6)
        {
            uint n = U32(b, ref o, le);
            if (n > (uint)b.Length) throw new InvalidDataException("WKB 부분 수");
            for (uint i = 0; i < n; i++) ReadGeom(b, ref o, rings);
        }
        else throw new InvalidDataException("WKB 형 " + bt + " — 다각형 아님");
    }

    static double[] Flip(double[] r)
    {
        int n = r.Length / 2;
        var o = new double[r.Length];
        for (int i = 0; i < n; i++) { o[2 * i] = r[2 * (n - 1 - i)]; o[2 * i + 1] = r[2 * (n - 1 - i) + 1]; }
        return o;
    }

    static double Area2(double[] r)
    {
        double a = 0;
        int n = r.Length / 2;
        for (int i = 0, j = n - 1; i < n; j = i++) a += r[2 * j] * r[2 * i + 1] - r[2 * i] * r[2 * j + 1];
        return a;   // 반시계 양수
    }

    static uint U32(byte[] b, ref int o, bool le)
    {
        if (o + 4 > b.Length) throw new InvalidDataException("WKB 잘림");
        uint v = le ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
        o += 4; return v;
    }

    static double F64(byte[] b, ref int o, bool le)
    {
        if (o + 8 > b.Length) throw new InvalidDataException("WKB 잘림");
        double v = le ? System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(b.AsSpan(o)) : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(b.AsSpan(o));
        o += 8; return v;
    }

    // ── NDJSON 한 쪽 ──
    public sealed class Page
    {
        public int Features;
        public long? NextAfter;
        public bool Ended;
    }

    /// <summary>한 쪽을 줄마다 읽는다 — <paramref name="onFeature"/>(wkb, 속성 이름 → 글). end 줄이 없으면(끊김) 예외 · error 줄도 예외.</summary>
    public static Page ReadPage(TextReader r, Action<byte[], Func<string, string>> onFeature)
    {
        var pg = new Page();
        string[] names = Array.Empty<string>();
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var e = doc.RootElement;
            if (e.TryGetProperty("error", out _)) throw new InvalidDataException("API 오류 줄");
            string ty = e.TryGetProperty("type", out var tv) ? tv.GetString() ?? "" : "";
            if (ty == "schema")
                names = e.GetProperty("fields").EnumerateArray().Select(f => (f.GetProperty("name").GetString() ?? "").ToLowerInvariant()).ToArray();
            else if (ty == "feature")
            {
                var wkb = Convert.FromBase64String(e.GetProperty("wkb").GetString() ?? "");
                var vals = e.GetProperty("attrs").EnumerateArray().Select(Str).ToArray();
                string Get(string k) { int i = Array.IndexOf(names, k.ToLowerInvariant()); return i >= 0 && i < vals.Length ? vals[i] : ""; }
                onFeature(wkb, Get);
                pg.Features++;
            }
            else if (ty == "end")
            {
                pg.Ended = true;
                pg.NextAfter = e.TryGetProperty("next_after", out var na) && na.ValueKind == JsonValueKind.Number && na.TryGetInt64(out long v) ? v : null;
            }
        }
        if (!pg.Ended) throw new InvalidDataException("응답이 중간에 끊김");
        return pg;
    }

    static string Str(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => (v.GetString() ?? "").Trim(),
        JsonValueKind.Number => v.TryGetInt64(out long l) ? l.ToString(CultureInfo.InvariantCulture) : v.GetRawText(),
        _ => "",
    };

    /// <summary>속성 → 타일 바이트 — <see cref="ForestConvert"/>와 같은 코드 함수 · 같은 칸(코드 칸).</summary>
    public static ForestPoly Attrs(Func<string, string> get, uint src)
    {
        var p = new ForestPoly
        {
            Src = src,
            Frtp = ForestCodes.Frtp(get("frtp_cd")), Koftr = ForestCodes.Koftr(get("koftr_grou")), Dnst = ForestCodes.Dnst(get("dnst_cd")),
            Hgt = ForestCodes.Height(get("height")), Dm = ForestCodes.Dm(get("dmcls_cd")), Ag = ForestCodes.Small(get("agcls_cd")), Fror = ForestCodes.Small(get("fror_cd")),
        };
        if (p.Frtp == 255) p.Frtp = 0;   // 모르는 FRTP = 나무 안 심는 자리
        return p;
    }
}

/// <summary>API에서 받은 폴리곤 → 칸(메모리) — 원본 SHP 변환(<see cref="ForestConvert.ProcessRecord"/>)과 같은 단순화 · 자르기 · 정수화.</summary>
public sealed class ForestApiBuilder
{
    readonly HashSet<(int, int)> want;
    readonly Dictionary<(int, int), List<(long Order, int Piece, byte[] Bytes)>> tiles = new();
    readonly ForestIndex.Source st = new() { Name = ForestApi.Layer + "(API)", Id = ForestApi.SourceId };
    readonly Dictionary<string, Dictionary<string, string>> codes = new();
    uint next;
    public int Failed => st.Failed;
    public int Records => st.Records;

    public ForestApiBuilder(IEnumerable<(int Tx, int Ty)> wantKeys) { want = wantKeys.ToHashSet(); st.YearMin = int.MaxValue; st.YearMax = 0; }

    /// <summary>레코드 하나 — 고리는 5186 · 속성 = API 이름(소문자) → 글.</summary>
    public void Add(List<double[]> rings5186, Func<string, string> get)
    {
        st.Records++;
        var rings = rings5186.Select(r =>
        {
            var o = new double[r.Length];
            for (int i = 0; i < r.Length / 2; i++) { var (x, y) = ForestApi.To5179(r[2 * i], r[2 * i + 1]); o[2 * i] = x; o[2 * i + 1] = y; }
            return o;
        }).ToList();
        var attrs = ForestApi.Attrs(get, ((uint)ForestApi.SourceId << 24) | (++next & 0xFFFFFF));
        var ro = new ForestConvert.RecOut();
        if (rings.Count == 0) { ro.Status = 3; ro.Why = "고리 없음"; }
        else ForestConvert.ProcessRecord(rings, attrs, 1.0, 25.0, ro);
        foreach (var (key, cf, nf) in new[] { ("frtp", "frtp_cd", "frtp_nm"), ("koftr", "koftr_grou", "koftr_nm"), ("dmcls", "dmcls_cd", "dmcls_nm"), ("agcls", "agcls_cd", "agcls_nm"), ("dnst", "dnst_cd", "dnst_nm"), ("height", "height", "height_nm") })
        {
            string c = get(cf), n = get(nf);
            if (c.Length == 0) continue;
            if (!codes.TryGetValue(key, out var m)) codes[key] = m = new();
            m.TryAdd(c, n);
        }
        if (int.TryParse(get("갱신년도"), out int yr) && yr >= 1900 && yr <= 2100) { st.YearMin = Math.Min(st.YearMin, yr); st.YearMax = Math.Max(st.YearMax, yr); }
        switch (ro.Status) { case 0: st.Kept++; break; case 1: st.Frtp0++; break; case 2: st.Small++; break; default: st.Failed++; break; }
        if (ro.Status > 1) return;
        foreach (var pc in ro.Pieces)
        {
            if (!want.Contains((pc.Tx, pc.Ty))) continue;
            if (!tiles.TryGetValue((pc.Tx, pc.Ty), out var l)) tiles[(pc.Tx, pc.Ty)] = l = new();
            l.Add((pc.P.Order, pc.Piece, ForestTileFormat.EncodePoly(pc.P)));
            st.AreaOut += pc.Area;
        }
        st.AreaIn += ro.AreaIn;
    }

    public ForestApiSource Finish()
    {
        var ix = new ForestIndex { Made = "회사 API 임상도 2025", Simplify = 1.0, Codes = codes };
        if (st.YearMax == 0) st.YearMin = 0;
        ix.Sources.Add(st);
        var bytes = new Dictionary<(int, int), byte[]>();
        foreach (var (k, l) in tiles.OrderBy(q => q.Key))
        {
            var ord = l.OrderBy(q => q.Order).ThenBy(q => q.Piece).Select(q => q.Bytes).ToList();
            var gz = ForestTileFormat.EncodeTile(k.Item1, k.Item2, ord);
            bytes[k] = gz;
            ix.Tiles[ForestTileFormat.Key(k.Item1, k.Item2)] = new long[] { ord.Count, gz.Length };
        }
        return new ForestApiSource(ix, bytes);
    }
}

/// <summary>메모리 칸 창고 — API에서 받아 만든 칸(디스크에 안 씀 · 사본은 <see cref="ForestSource.CopyUsed"/>가 만든다).</summary>
public sealed class ForestApiSource : IForestTileSource
{
    readonly Dictionary<(int, int), byte[]> bytes;
    public ForestIndex Index { get; }
    public string Describe => ForestApi.Label;
    public ForestApiSource(ForestIndex ix, Dictionary<(int, int), byte[]> b) { Index = ix; bytes = b; }
    public byte[]? Tile(int tx, int ty) => bytes.TryGetValue((tx, ty), out var b) ? b : null;
}

/// <summary>회사 API 쪽 HTTP — 건강 확인(3초) → 건수 → 쪽 나눠 받기(next_after). 한 번에 요청 하나(한도 2 이하). 주소는 부르는 쪽이 준다(로그 · 화면에 안 적음).</summary>
public sealed class ForestApiClient : IDisposable
{
    public const int MaxFeatures = 200_000;
    public static TimeSpan HealthTimeout = TimeSpan.FromSeconds(3);
    public static TimeSpan CountTimeout = TimeSpan.FromSeconds(30), PageTimeout = TimeSpan.FromSeconds(180);

    readonly string baseUrl;
    readonly HttpClient http;

    public ForestApiClient(string baseUrl, HttpMessageHandler? handler = null)
    {
        this.baseUrl = baseUrl.TrimEnd('/');
        http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public void Dispose() => http.Dispose();

    HttpResponseMessage Get(string path, TimeSpan timeout, out CancellationTokenSource cts)
    {
        cts = new CancellationTokenSource(timeout);
        var resp = http.Send(new HttpRequestMessage(HttpMethod.Get, baseUrl + path), HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var c = cts;
        c.Token.Register(() => { try { resp.Dispose(); } catch { } });   // 본문 읽는 중 시간 초과도 끊는다
        if (!resp.IsSuccessStatusCode) { int code = (int)resp.StatusCode; resp.Dispose(); throw new HttpRequestException("HTTP " + code); }
        return resp;
    }

    public bool Health()
    {
        try { using var r = Get("/health", HealthTimeout, out var c); using (c) return true; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ObjectDisposedException) { return false; }
    }

    static string Bb(double[] b) => string.Join(",", b.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)));

    public long Count(double[] bbox)
    {
        using var r = Get($"/v1/features/count?layer={ForestApi.Layer}&bbox={Bb(bbox)}", CountTimeout, out var c);
        using (c)
        {
            using var doc = JsonDocument.Parse(r.Content.ReadAsStream());
            return doc.RootElement.GetProperty("count").GetInt64();
        }
    }

    /// <summary>범위 안 폴리곤을 쪽 나눠 모두 받는다 — <paramref name="onFeature"/>(WKB, 속성). 돌려준 값 = 받은 건수 · 요청 수.</summary>
    public (int Features, int Requests) Fetch(double[] bbox, Action<byte[], Func<string, string>> onFeature, Action<string>? progress = null)
    {
        long? after = null; int n = 0, req = 0;
        do
        {
            string q = $"/v1/features/wkb?layer={ForestApi.Layer}&bbox={Bb(bbox)}&count=10000" + (after != null ? "&after=" + after.Value.ToString(CultureInfo.InvariantCulture) : "");
            using var r = Get(q, PageTimeout, out var c);
            using (c)
            {
                req++;
                using var sr = new StreamReader(r.Content.ReadAsStream());
                var pg = ForestApi.ReadPage(sr, onFeature);
                n += pg.Features; after = pg.NextAfter;
                progress?.Invoke($"임상도 받는 중 {n:N0}");
            }
            if (req > 1000) throw new InvalidDataException("쪽이 너무 많음");
        } while (after != null);
        return (n, req);
    }

    // ── 한 세션 안 도달 확인 기억(성공 · 실패 5분) ──
    static string? cacheBase; static bool cacheOk; static DateTime cacheAt;
    public static void ResetCache() { cacheBase = null; }

    /// <summary>칸들을 덮는 범위를 받아 메모리 창고로 — 못 하면 (null, 까닭 한 줄). 예외는 밖으로 안 나간다.</summary>
    public static (ForestApiSource? Source, string Note, int Polys, int Requests) TryFetch(string baseUrl, IReadOnlyList<(int Tx, int Ty)> keys, Action<string>? progress = null, HttpMessageHandler? handler = null)
    {
        try
        {
            using var cl = new ForestApiClient(baseUrl, handler);
            bool known = cacheBase == baseUrl && DateTime.UtcNow - cacheAt < TimeSpan.FromMinutes(5);
            if (known && !cacheOk) return (null, "회사 API 못 씀 — 회사망 밖 · 응답 없음", 0, 0);
            if (!known)
            {
                bool ok = cl.Health();
                cacheBase = baseUrl; cacheOk = ok; cacheAt = DateTime.UtcNow;
                if (!ok) return (null, "회사 API 못 씀 — 회사망 밖 · 응답 없음", 0, 0);
            }
            progress?.Invoke("임상도 받는 중(회사 API)");
            var bbox = ForestApi.Bbox5186(keys);
            long cnt = cl.Count(bbox);
            if (cnt > MaxFeatures) return (null, $"회사 API 못 씀 — 폴리곤 {cnt:N0}개로 너무 많음 → 범위를 줄일 것", 0, 0);
            var b = new ForestApiBuilder(keys);
            var seen = new HashSet<string>();
            var (_, req) = cl.Fetch(bbox, (wkb, get) =>
            {
                if (!seen.Add(get("gid"))) return;   // 쪽 사이 겹침 방지
                b.Add(ForestApi.ParseWkbRings(wkb), get);
            }, progress);
            return (b.Finish(), "", seen.Count, req);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or InvalidDataException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ObjectDisposedException or UriFormatException)
        {
            return (null, "회사 API 못 씀 — " + ex.GetType().Name + " → 도면 폴더 사본으로", 0, 0);
        }
    }
}
