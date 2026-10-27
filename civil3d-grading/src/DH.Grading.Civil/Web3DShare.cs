using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DH.Grading.Civil;

/// <summary>★★[v107.1 · JACK 1027 «만들어진 3d뷰는 다른 pc에서 볼수있게 웹주소로 호스팅» → «내 개인 nas» (시놀로지 · https 주소 있음)] <b>3D 웹 공유</b> — 시놀로지 NAS에 올리고 웹 주소를 돌려준다.
/// <list type="bullet">
/// <item>올리기 = DSM 웹 API(File Station · 이미 열려 있는 https DSM 주소 — 포트를 더 안 연다) · 보이기 = Web Station이 <c>web</c> 공유 폴더를 웹으로.</item>
/// <item>이름 = 무작위 32자(주소를 아는 사람만) · 도면 이름은 주소에 안 넣는다.</item>
/// <item>설정 · 올린 목록은 이 PC 사용자 폴더(<c>%APPDATA%\DH.Grading</c>) · 비밀번호는 Windows 암호화(DPAPI · 이 사용자만 풀 수 있음) — 저장소 · 도면에는 안 들어간다.</item>
/// </list></summary>
internal static class Web3DShare
{
    internal sealed class Config
    {
        /// <summary>DSM 주소(로그인 · 올리기) — 예 https://이름.synology.me:5001</summary>
        public string Dsm { get; set; } = "";
        /// <summary>올릴 NAS 폴더(공유 폴더 web 아래) — 예 /web/3d</summary>
        public string Folder { get; set; } = "/web/3d";
        /// <summary>그 폴더의 웹 주소(보는 사람) — 예 https://이름.synology.me/3d</summary>
        public string Web { get; set; } = "";
        public string User { get; set; } = "";
        /// <summary>DPAPI로 감싼 비밀번호(base64).</summary>
        public string Pass { get; set; } = "";
        public bool Ready => Dsm.Length > 0 && Web.Length > 0 && User.Length > 0 && Pass.Length > 0 && Folder.StartsWith("/");
    }

    internal sealed class Item
    {
        public string Url { get; set; } = "";
        public string Remote { get; set; } = "";
        public string Dwg { get; set; } = "";
        public string Time { get; set; } = "";
        public double Mb { get; set; }
    }

    static string Dir => System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "DH.Grading");
    static string CfgPath => System.IO.Path.Combine(Dir, "share3d.json");
    static string ListPath => System.IO.Path.Combine(Dir, "share3d_list.json");
    static readonly JsonSerializerOptions Js = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static Config Load()
    {
        try { if (System.IO.File.Exists(CfgPath)) return JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(CfgPath)) ?? new Config(); }
        catch { }
        return ImportDefault() ?? new Config();
    }

    /// <summary>★[JACK 1027 «설치할때부터 이걸로 설정되게»] 설치 폴더의 기본 설정(<c>share3d.default.json</c> — DLL 옆)을 처음 한 번 가져와 이 사용자 설정으로(비밀번호는 DPAPI로 바꿔 둔다).
    /// <para>★공개 저장소 — 주소 · 계정 · 비밀번호는 소스에 없다. 기본 파일은 설치 폴더에만 놓이고 저장소에는 막아 둔다(.gitignore).
    /// 그 파일의 비밀번호는 base64(감춤일 뿐 암호화 아님 — 설치 파일을 가진 사람은 읽을 수 있다 · 그래서 계정은 web 폴더 쓰기만).</para></summary>
    static Config? ImportDefault()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(Web3DShare).Assembly.Location) ?? "";
            string p = System.IO.Path.Combine(dir, "share3d.default.json");
            if (!System.IO.File.Exists(p)) return null;
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(p));
            var r = doc.RootElement;
            string S(string k) => r.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
            string pass = S("PassB64").Length > 0 ? Encoding.UTF8.GetString(System.Convert.FromBase64String(S("PassB64"))) : "";
            var c = new Config { Dsm = S("Dsm"), Folder = S("Folder").Length > 0 ? S("Folder") : "/web/3d", Web = S("Web"), User = S("User"), Pass = Protect(pass) };
            if (!c.Ready) return null;
            Save(c);
            try { DiagLog.Append("\n[3D 공유] 설치 기본 설정을 가져왔습니다"); } catch { }
            return c;
        }
        catch { return null; }
    }

    internal static void Save(Config c)
    {
        System.IO.Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(CfgPath, JsonSerializer.Serialize(c, Js), new UTF8Encoding(false));
    }

    internal static string Protect(string plain) => plain.Length == 0 ? "" : System.Convert.ToBase64String(
        System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    static string Unprotect(string b64) => b64.Length == 0 ? "" : Encoding.UTF8.GetString(
        System.Security.Cryptography.ProtectedData.Unprotect(System.Convert.FromBase64String(b64), Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser));

    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DH.Grading.Web3DShare.v1");

    internal static List<Item> List()
    {
        try { if (System.IO.File.Exists(ListPath)) return JsonSerializer.Deserialize<List<Item>>(System.IO.File.ReadAllText(ListPath)) ?? new(); }
        catch { }
        return new();
    }

    static void SaveList(List<Item> l)
    {
        System.IO.Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(ListPath, JsonSerializer.Serialize(l, Js), new UTF8Encoding(false));
    }

    static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromMinutes(15) };

    /// <summary>NAS 오류 — 시놀로지 코드를 같이 든다(지우기에서 «이미 없음»을 가르려고).</summary>
    internal sealed class NasError : System.InvalidOperationException
    {
        public readonly int Code;
        public NasError(string msg, int code) : base(msg) { Code = code; }
    }

    /// <summary>DSM 웹 API 한 번 — 성공이면 data, 아니면 쉬운 말 예외. <paramref name="auth"/>면 로그인 오류표(같은 번호가 로그인과 파일에서 뜻이 다르다 — 보안 검토 F1).</summary>
    static async Task<JsonElement> Call(HttpResponseMessage resp, string what, bool auth = false)
    {
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new System.InvalidOperationException($"{what} — NAS 응답 {(int)resp.StatusCode}");
        JsonElement root;
        try { root = JsonDocument.Parse(body).RootElement; } catch { throw new System.InvalidOperationException($"{what} — NAS가 알 수 없는 답을 했습니다(DSM 주소 확인)"); }
        if (root.TryGetProperty("success", out var ok) && ok.GetBoolean()) return root.TryGetProperty("data", out var d) ? d : default;
        int code = root.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
        throw new NasError($"{what} — {(auth ? WhyAuth(code) : WhyFile(code))}(코드 {code})", code);
    }

    /// <summary>로그인 오류(SYNO.API.Auth) → 쉬운 말.</summary>
    static string WhyAuth(int code) => code switch
    {
        400 => "계정 또는 비밀번호가 틀립니다",
        401 => "계정이 꺼져 있습니다",
        402 => "이 계정은 쓸 수 없습니다(권한)",
        403 or 404 or 406 => "2단계 인증 계정입니다 — 3D 전용 계정(2단계 인증 없음)을 쓰세요",
        407 => "NAS가 이 PC 주소를 막았습니다(로그인 여러 번 실패 — DSM 보안에서 풀기)",
        408 or 409 or 410 => "비밀번호가 만료됐거나 바꿔야 합니다 — DSM에서 그 계정으로 한 번 들어가 바꾸세요",
        _ => Common(code),
    };

    /// <summary>파일 오류(SYNO.FileStation) → 쉬운 말.</summary>
    static string WhyFile(int code) => code switch
    {
        408 => "파일 · 폴더가 없습니다",
        409 or 410 or 411 => "그 폴더에 쓸 권한이 없습니다",
        414 => "같은 이름이 있습니다",
        416 => "NAS 저장 공간이 모자랍니다",
        _ => Common(code),
    };

    static string Common(int code) => code switch
    {
        105 => "이 계정에 권한이 없습니다(File Station 허용 · 폴더 권한)",
        106 or 107 or 119 => "로그인이 끊겼습니다 — 다시 해 보세요",
        101 or 102 or 103 or 104 => "NAS가 요청을 이해하지 못했습니다(DSM 판 확인)",
        _ => "NAS가 거절했습니다",
    };

    sealed class Session : System.IAsyncDisposable
    {
        public string Base = "", Sid = "", AuthPath = "entry.cgi", UploadPath = "entry.cgi", DeletePath = "entry.cgi";
        public int UploadVer = 2, DeleteVer = 2;
        public async ValueTask DisposeAsync()
        {
            try { await Http.GetAsync($"{Base}/webapi/{AuthPath}?api=SYNO.API.Auth&version=6&method=logout&session=FileStation&_sid={Sid}"); } catch { }
        }
    }

    static async Task<Session> Login(Config c)
    {
        // ★[보안 검토 낮음 2] 설정 파일을 손으로 고쳐도 https가 아니면 비밀번호를 안 보낸다
        if (!System.Uri.TryCreate(c.Dsm, System.UriKind.Absolute, out var du) || du.Scheme != System.Uri.UriSchemeHttps)
            throw new System.InvalidOperationException("DSM 주소가 https가 아닙니다 — 웹 공유 설정에서 고치세요");
        var s = new Session { Base = c.Dsm.TrimEnd('/') };
        // API 자리 · 판 묻기(DSM 6 · 7 둘 다)
        try
        {
            var q = await Call(await Http.GetAsync($"{s.Base}/webapi/query.cgi?api=SYNO.API.Info&version=1&method=query&query=SYNO.API.Auth,SYNO.FileStation.Upload,SYNO.FileStation.Delete"), "NAS 연결");
            if (q.TryGetProperty("SYNO.API.Auth", out var a)) s.AuthPath = a.GetProperty("path").GetString() ?? s.AuthPath;
            if (q.TryGetProperty("SYNO.FileStation.Upload", out var u)) { s.UploadPath = u.GetProperty("path").GetString() ?? s.UploadPath; s.UploadVer = System.Math.Min(2, u.GetProperty("maxVersion").GetInt32()); }
            if (q.TryGetProperty("SYNO.FileStation.Delete", out var d)) { s.DeletePath = d.GetProperty("path").GetString() ?? s.DeletePath; s.DeleteVer = System.Math.Min(2, d.GetProperty("maxVersion").GetInt32()); }
        }
        catch (HttpRequestException ex) { throw new System.InvalidOperationException("NAS에 연결하지 못했습니다(DSM 주소 · 인터넷 확인) — " + ex.Message); }
        // 비밀번호는 주소가 아니라 본문으로(로그에 안 남게)
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api"] = "SYNO.API.Auth", ["version"] = "6", ["method"] = "login", ["session"] = "FileStation", ["format"] = "sid",
            ["account"] = c.User, ["passwd"] = Unprotect(c.Pass),
        });
        var data = await Call(await Http.PostAsync($"{s.Base}/webapi/{s.AuthPath}", form), "NAS 로그인", auth: true);
        s.Sid = data.GetProperty("sid").GetString() ?? "";
        return s;
    }

    /// <summary>파일 하나 올리기 — 여러 부분(multipart)을 직접 만든다(경계 따옴표 · 필드 차례 · 파일은 맨 끝 — DSM이 까다롭다).</summary>
    static async Task Upload(Session s, string folder, string name, byte[] bytes)
    {
        string b = "----dh3d" + System.Guid.NewGuid().ToString("N");
        using var ms = new System.IO.MemoryStream(bytes.Length + 1024);
        void W(string t) { var x = Encoding.UTF8.GetBytes(t); ms.Write(x, 0, x.Length); }
        foreach (var (k, v) in new[] { ("path", folder), ("create_parents", "true"), ("overwrite", "true") })
            W($"--{b}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n");
        W($"--{b}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{name}\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        ms.Write(bytes, 0, bytes.Length);
        W($"\r\n--{b}--\r\n");
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=" + b);
        await Call(await Http.PostAsync($"{s.Base}/webapi/{s.UploadPath}?api=SYNO.FileStation.Upload&version={s.UploadVer}&method=upload&_sid={s.Sid}", content), "올리기");
    }

    static async Task Delete(Session s, string remote)
    {
        string path = System.Uri.EscapeDataString(JsonSerializer.Serialize(new[] { remote }));
        await Call(await Http.GetAsync($"{s.Base}/webapi/{s.DeletePath}?api=SYNO.FileStation.Delete&version={s.DeleteVer}&method=delete&path={path}&_sid={s.Sid}"), "지우기");
    }

    /// <summary>보는 주소가 웹페이지로 열리나 — 200 · text/html이어야(Web Station이 그 폴더를 웹으로 내놓는지).</summary>
    static async Task<string?> CheckWeb(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1023);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if ((int)resp.StatusCode == 404) return "웹 주소에서 파일이 안 보입니다(404) — Web Station이 그 폴더를 웹으로 내놓는지 · 웹 주소가 맞는지 확인";
            if (!resp.IsSuccessStatusCode) return $"웹 주소가 {(int)resp.StatusCode}로 답합니다";
            string ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!ct.Contains("html", System.StringComparison.OrdinalIgnoreCase)) return $"웹 주소가 웹페이지가 아니라 «{ct}»로 내보냅니다(내려받기로 열림)";
            return null;
        }
        catch (System.Exception ex) { return "웹 주소에 연결하지 못했습니다 — " + ex.Message; }
    }

    static string WebUrl(Config c, string name) => c.Web.TrimEnd('/') + "/" + System.Uri.EscapeDataString(name);

    /// <summary>3D 파일을 올리고 주소를 돌려준다(올린 뒤 그 주소가 웹페이지로 열리는지까지 본다).</summary>
    internal static async Task<(string Url, string Note)> Share(string htmlPath, string dwg)
    {
        var c = Load();
        if (!c.Ready) throw new System.InvalidOperationException("웹 공유 설정이 비어 있습니다");
        var bytes = await System.IO.File.ReadAllBytesAsync(htmlPath);
        string name = System.Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + ".html";
        await using (var s = await Login(c))
        {
            // 빈 index.html — 폴더 주소로 들어와도 목록이 안 보이게(시험을 안 했어도)
            await Upload(s, c.Folder, "index.html", Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><meta name=robots content=noindex><title>-</title>"));
            await Upload(s, c.Folder, name, bytes);
        }
        string url = WebUrl(c, name);
        string? bad = await CheckWeb(url);
        var l = List();
        l.Insert(0, new Item { Url = url, Remote = c.Folder.TrimEnd('/') + "/" + name, Dwg = dwg, Time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Mb = System.Math.Round(bytes.Length / 1048576.0, 1) });
        SaveList(l);
        try { DiagLog.Append($"\n[3D 공유] 올림 {bytes.Length / 1048576.0:F1} MB · {(bad == null ? "웹 확인" : "⚠ " + bad)}"); } catch { }
        return (url, bad == null ? $"{bytes.Length / 1048576.0:F0} MB · 주소를 아는 사람은 누구나 볼 수 있습니다" : "⚠ " + bad);
    }

    /// <summary>올린 것 지우기(NAS에서 · 목록에서).</summary>
    internal static async Task Remove(Item it)
    {
        var c = Load();
        await using (var s = await Login(c))
            try { await Delete(s, it.Remote); }
            catch (NasError ne) when (ne.Code == 408) { }   // 이미 NAS에 없음 — 목록에서만 지운다(보안 검토 F2)
        var l = List();
        l.RemoveAll(x => x.Remote == it.Remote);
        SaveList(l);
    }

    /// <summary>연결 시험 — 로그인 · 빈 index.html(폴더 목록 가림) · 작은 시험 페이지 올리기 · 웹 주소로 열리나 · 폴더 목록이 보이나 · 지우기.
    /// <para>★[보안 검토 중간 2] 무작위 이름은 폴더 목록이 안 보여야 지켜진다 — 목록에 시험 파일 이름이 보이면 실패로 알린다.</para></summary>
    internal static async Task<string> Test(Config c)
    {
        string name = "dh3d-test-" + System.Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant() + ".html";
        var page = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><meta name=robots content=noindex><title>DH 3D 시험</title><p>DH 3D 공유 시험 페이지");
        await using var s = await Login(c);
        await Upload(s, c.Folder, "index.html", Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><meta name=robots content=noindex><title>-</title>"));
        await Upload(s, c.Folder, name, page);
        string? bad = await CheckWeb(WebUrl(c, name));
        if (bad == null)
        {
            try
            {
                string listing = await Http.GetStringAsync(c.Web.TrimEnd('/') + "/");
                if (listing.Contains(name, System.StringComparison.OrdinalIgnoreCase)) bad = "폴더 목록이 보입니다 — Web Station에서 목록 보기를 끄세요(다른 사람이 올린 3D를 다 볼 수 있음)";
            }
            catch { }   // 403 · 404 = 목록 안 보임(좋음)
        }
        try { await Delete(s, c.Folder.TrimEnd('/') + "/" + name); } catch { }
        return bad ?? "연결 · 올리기 · 웹 주소 모두 됩니다";
    }
}
