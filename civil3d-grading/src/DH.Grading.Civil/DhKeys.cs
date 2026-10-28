using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DH.Grading.Civil;

/// <summary>★★[v109 · 계획_v109.md 1 · JACK 1028 «사용자가 자신의 API를 발급받아서 넣는 형태» · 검토 H3 · L3]
/// <b>비밀값은 소스 밖</b> — 내장 VWorld 키 · 사내 GIS DB 접속 정보는 설치 폴더의 <c>dh.keys.json</c>(DLL 옆 · 저장소는 .gitignore)에만.
/// <para>저장소가 공개라 소스에 적으면 누구나 본다(0731~1028 실제로 그랬다 — 옛 값은 git 기록에 남아 바꿔야 한다).
/// 파일이 없거나 깨지면 빈 값 — 위성 · 지도는 «키 없음», 등고선은 «사내 DB 접속 정보 없음»으로 알린다(세션을 죽이지 않음).</para>
/// <para>★시험(화면 없는 Civil)은 환경 변수 <c>DH_GRADING_KEYS_DIR</c>로 다른 폴더를 가리킨다 — 설치 폴더 · 사용자 설정을 안 건드림(검토 M10).</para></summary>
internal static class DhKeys
{
    internal sealed class Db
    {
        public string Host = "", Database = "", User = "", Password = "";
        public int Port = 6432;
        public bool Ready => Host.Length > 0 && Database.Length > 0 && User.Length > 0;
    }

    internal sealed class Bundle
    {
        public string VWorld = "";
        public Db GisDb = new();
        public string Note = "";   // 파일을 못 읽은 까닭(값은 안 적음)
    }

    internal static string? TestDir =>
        System.Environment.GetEnvironmentVariable("DH_GRADING_KEYS_DIR") is { Length: > 0 } d ? d : null;

    static string InstallDir => TestDir ?? System.IO.Path.GetDirectoryName(typeof(DhKeys).Assembly.Location) ?? "";

    static readonly System.Lazy<Bundle> _b = new(Load);
    internal static Bundle Installed => _b.Value;

    static Bundle Load()
    {
        var b = new Bundle();
        try
        {
            string p = System.IO.Path.Combine(InstallDir, "dh.keys.json");
            if (!System.IO.File.Exists(p)) { b.Note = "설치 키 파일(dh.keys.json)이 없습니다"; return b; }
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(p));
            var r = doc.RootElement;
            static string S(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
            b.VWorld = S(r, "vworld");
            if (r.TryGetProperty("gisdb", out var g) && g.ValueKind == JsonValueKind.Object)
            {
                b.GisDb.Host = S(g, "host"); b.GisDb.Database = S(g, "database"); b.GisDb.User = S(g, "user"); b.GisDb.Password = S(g, "password");
                // ★[설치 전 검토 L1] 숫자가 아니면 기본 포트(TryGetInt32는 문자열에서 예외 → 키까지 통째로 비던 것)
                if (g.TryGetProperty("port", out var pt) && pt.ValueKind == JsonValueKind.Number && pt.TryGetInt32(out int port) && port > 0) b.GisDb.Port = port;
            }
            // ★[검토 L9] 내장 키 꼴 검사 — 틀린 꼴이면 쓰지 않고 알림(주소 경로에 그대로 들어가므로)
            if (b.VWorld.Length > 0 && !VWorldKey.LooksValid(b.VWorld)) { b.VWorld = ""; b.Note = "설치 키 파일의 VWorld 키 꼴이 틀렸습니다(8-4-4-4-12)"; }
        }
        catch (System.Exception ex) { b = new Bundle { Note = "설치 키 파일을 못 읽었습니다 — " + ex.GetType().Name }; }
        try { if (b.Note.Length > 0) DiagLog.Append("\n[키] " + b.Note); } catch { }
        return b;
    }
}

/// <summary>★★[v109 · 계획_v109.md 2] VWorld 인증키 — <b>내 키</b>([기타 설정] · %APPDATA% · DPAPI)가 있으면 그것, 없으면 <b>내장 키</b>(JACK 1028 «처음 써 볼 때 진입장벽을 낮추고 싶어»).
/// <para>VWorld 약관: 인증키는 «회원 본인에 한하여» · «양도 · 증여 불가» — 많이 쓰는 사람은 자기 키를 넣는 것이 맞다.</para></summary>
internal static class VWorldKey
{
    static string Dir => DhKeys.TestDir ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "DH.Grading");
    static string UserPath => System.IO.Path.Combine(Dir, "vworld.json");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DH.Grading.VWorldKey.v1");
    static string? _user;

    /// <summary>내 키(없으면 "") — 한 번 읽어 두고 저장 · 지우기 때 바꾼다.</summary>
    internal static string User
    {
        get
        {
            if (_user != null) return _user;
            string u = "";   // ★[검토 L13] 다 읽은 뒤 한 번에 넣는다(먼저 ""를 넣으면 동시에 처음 읽는 쪽이 내장 키를 쓴다)
            try
            {
                if (System.IO.File.Exists(UserPath))
                {
                    using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(UserPath));
                    if (doc.RootElement.TryGetProperty("key", out var v) && v.GetString() is { Length: > 0 } enc)
                        u = Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                            System.Convert.FromBase64String(enc), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser)).Trim();
                }
            }
            catch (System.Exception ex) { try { DiagLog.Append("\n[키] 내 VWorld 키를 못 읽었습니다(내장 키로) — " + ex.GetType().Name); } catch { } }
            _user = u;
            return u;
        }
    }

    internal static bool UsingUser => User.Length > 0;
    internal static string Current => UsingUser ? User : DhKeys.Installed.VWorld;
    internal static string Source => UsingUser ? "내 키" : DhKeys.Installed.VWorld.Length > 0 ? "내장 키" : "키 없음";

    internal static bool LooksValid(string k) =>
        System.Text.RegularExpressions.Regex.IsMatch((k ?? "").Trim(), "^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$");

    internal static void SaveUser(string key)
    {
        key = key.Trim();
        System.IO.Directory.CreateDirectory(Dir);
        string enc = System.Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser));
        System.IO.File.WriteAllText(UserPath, JsonSerializer.Serialize(new { key = enc }), new UTF8Encoding(false));
        _user = key;
    }

    /// <summary>내 키 지우기 — 못 지우면 예외(★[검토 L2] 조용히 넘기면 화면은 «내장 키»인데 다음 실행에 옛 키가 되살아난다).</summary>
    internal static void ClearUser()
    {
        if (System.IO.File.Exists(UserPath)) System.IO.File.Delete(UserPath);
        _user = "";
    }

    /// <summary>VWorld 응답이 키 오류인가 — WMTS는 200에 XML(locator="key") · WMS/WFS는 ServiceException INVALID_KEY · 데이터 API는 JSON INVALID_KEY(조사 실측 1028).</summary>
    internal static bool IsKeyError(string body) =>
        body.Contains("INVALID_KEY", System.StringComparison.OrdinalIgnoreCase)
        || body.Contains("locator=\"key\"", System.StringComparison.OrdinalIgnoreCase)
        || body.Contains("인증키", System.StringComparison.Ordinal);

    internal const string KeyHelp = "VWorld 키가 맞지 않습니다(등록 안 됐거나 만료) — [기타 설정]에서 키를 확인하세요";
    internal const string NoKeyHelp = "VWorld 키가 없습니다 — [기타 설정]에서 내 키를 넣으세요";

    // ── [키 시험] — 공개 장소(서울시청 둘레) 고정 좌표로 네 가지를 한 번씩(조사 1028에서 성공한 꼴 그대로) ──
    // 15초 — VWorld 지적 그림(WMS)은 때때로 10초를 넘긴다(1028 화면 없는 시험: 같은 키로 한 번은 ✓ · 한 번은 10초 넘김)
    static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromSeconds(15) };
    const double Lat = 37.5663, Lon = 126.9779;                 // 서울시청(공공장소)
    const double X0 = 198050, Y0 = 551870, X1 = 198100, Y1 = 551920;   // 같은 곳 50 m 상자(EPSG:5186)

    internal sealed record Probe(string Name, bool Ok, string Why);

    /// <summary>★기다림은 모두 ConfigureAwait(false) — 명령 스레드에서 막고 기다려도(화면 없는 시험) 서로 묶이지 않게(1028 실측: 묶여 10분 멈춤).
    /// 네 가지 — 위성 지도(WMTS) · 지도 도킹바 지적 그림(WMS · domain=https://dh.map — 도킹바가 실제로 보내는 값) · 지적 자료(2D 데이터 API) · 소유 정보(국가중점 WFS). 아무 스레드에서나.</summary>
    internal static async System.Threading.Tasks.Task<List<Probe>> Test(string key)
    {
        key = (key ?? "").Trim();
        var res = new List<Probe>();
        int z = 17, n = 1 << z;
        int tx = (int)System.Math.Floor((Lon + 180) / 360 * n);
        double lr = Lat * System.Math.PI / 180;
        int ty = (int)System.Math.Floor((1 - System.Math.Log(System.Math.Tan(lr) + 1 / System.Math.Cos(lr)) / System.Math.PI) / 2 * n);
        double mx = Lon * 20037508.34 / 180, my = System.Math.Log(System.Math.Tan((90 + Lat) * System.Math.PI / 360)) / (System.Math.PI / 180) * 20037508.34 / 180;
        string k = System.Uri.EscapeDataString(key);
        // ★[설치 전 검토 L3] 넷을 한꺼번에(차례로면 최대 40초)
        var all = await System.Threading.Tasks.Task.WhenAll(
            One("위성 지도", $"https://api.vworld.kr/req/wmts/1.0.0/{k}/Satellite/{z}/{ty}/{tx}.jpeg", image: true),
            One("지도 도킹바 지적", "https://api.vworld.kr/req/wms?service=WMS&request=GetMap&version=1.3.0&layers=lp_pa_cbnd_bubun&styles=lp_pa_cbnd_bubun"
                + $"&crs=EPSG:3857&bbox={mx - 100:F0},{my - 100:F0},{mx + 100:F0},{my + 100:F0}&width=256&height=256&format=image/png&transparent=true"
                + $"&key={k}&domain={System.Uri.EscapeDataString("https://dh.map")}", image: true),
            One("지적 자료", "https://api.vworld.kr/req/data?service=data&version=2.0&request=GetFeature&format=json&size=1&geometry=false"
                + $"&data=LP_PA_CBND_BUBUN&crs=EPSG:5186&geomFilter=BOX({X0},{Y0},{X1},{Y1})&key={k}", image: false),
            One("소유 정보", $"https://api.vworld.kr/ned/wfs/getPossessionWFS?typename=dt_d160&bbox={X0},{Y0},{X1},{Y1},EPSG:5186&srsName=EPSG:5186&maxFeatures=1&output=application/json&key={k}", image: false)
        ).ConfigureAwait(false);
        res.AddRange(all);
        return res;
    }

    static async System.Threading.Tasks.Task<Probe> One(string name, string url, bool image)
    {
        try
        {
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            byte[] b = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            string ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (image && ct.StartsWith("image", System.StringComparison.OrdinalIgnoreCase) && b.Length > 100) return new(name, true, "");
            string body = Encoding.UTF8.GetString(b, 0, System.Math.Min(b.Length, 2000));
            if (IsKeyError(body)) return new(name, false, "키가 등록되지 않았거나 만료 · 또는 이 API를 신청하지 않음");
            if (!resp.IsSuccessStatusCode) return new(name, false, $"HTTP {(int)resp.StatusCode}");
            if (!image)
            {
                // 데이터 API: status OK / NOT_FOUND = 키는 맞음 · WFS: FeatureCollection = 맞음
                if (body.Contains("\"status\":\"OK\"") || body.Contains("\"status\":\"NOT_FOUND\"") || body.Contains("FeatureCollection")) return new(name, true, "");
            }
            string head = System.Text.RegularExpressions.Regex.Replace(body, @"\s+", " ");
            return new(name, false, head.Length > 80 ? head.Substring(0, 80) + "…" : head);
        }
        catch (System.Threading.Tasks.TaskCanceledException) { return new(name, false, "응답 없음(15초) — VWorld가 느리거나 인터넷 · 방화벽 문제 · 잠시 뒤 다시 시험"); }
        catch (System.Exception ex) { return new(name, false, ex.GetType().Name + " — 인터넷 확인"); }
    }
}
