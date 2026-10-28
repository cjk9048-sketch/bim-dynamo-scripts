// ── S177 ★★[v109 · 계획_v109.md · 검토 M6 · H3] 비밀값 검사 — 공개 저장소에 키 · 사내 DB 접속 정보가 없는가 ──
//   알려진 값 = 설치 전용 파일(dh.keys.json · .gitignore)에서 읽어 git 추적 파일 전부를 뒤진다 — 값은 찍지 않고 file:line만.
//   우리 프로젝트(civil3d-grading/) 안에서 걸리면 실패 · 저장소의 다른 자리(참고자료 등)는 알림만(JACK 결정 대기).
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

static class SecretScanCheck
{
    static readonly HashSet<string> Binary = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico", ".dwg", ".dxf", ".dwt", ".xlsx", ".xls", ".xlsm", ".pdf", ".zip", ".7z",
        ".hwp", ".hwpx", ".dll", ".exe", ".pdb", ".rvt", ".rfa", ".dyn", ".mp4", ".webm", ".pptx", ".docx", ".shp", ".shx", ".dbf", ".ttf", ".otf", ".woff", ".woff2",
    };

    static string? Git(string root, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            psi.Environment["GIT_CONFIG_PARAMETERS"] = "'core.quotepath=false'";
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(60000);
            return p.ExitCode == 0 ? o : null;
        }
        catch { return null; }
    }

    public static void Run(Action<string, bool, string> check0)
    {
        void check(string name, bool ok, string detail = "") => check0(name, ok, detail);
        Console.WriteLine("\n== S177 비밀값 검사(공개 저장소) ==");
        // 저장소 뿌리 · 우리 프로젝트 폴더
        string? dir = AppContext.BaseDirectory;
        string? proj = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "작업과정.md")) && Directory.Exists(Path.Combine(d.FullName, "src", "DH.Grading.Civil"))) { proj = d.FullName; break; }
        if (proj == null) { check("S177 프로젝트 폴더 찾기", false, AppContext.BaseDirectory); return; }
        string? top = Git(proj, "rev-parse --show-toplevel")?.Trim();
        if (string.IsNullOrEmpty(top)) { check("S177 git 저장소 찾기", false, proj); return; }
        top = Path.GetFullPath(top);
        string projRel = Path.GetRelativePath(top, proj).Replace('\\', '/') + "/";

        // 알려진 값 — 설치 전용 파일에서(없으면 검사는 꼴 검사만)
        var secrets = new List<string>();
        string keysPath = Path.Combine(proj, "src", "DH.Grading.Civil", "DH.Grading.bundle", "Contents", "dh.keys.json");
        if (File.Exists(keysPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(keysPath));
                void Add(JsonElement e, string k) { if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: >= 6 } s) secrets.Add(s); }
                Add(doc.RootElement, "vworld");
                if (doc.RootElement.TryGetProperty("gisdb", out var g)) { Add(g, "host"); Add(g, "password"); }
            }
            catch (Exception ex) { check("S177 설치 키 파일 읽기", false, "형식 오류 — " + ex.GetType().Name); return; }   // ★[검토 L12] 값은 안 찍음
        }

        // ★[설치 전 검토 중간 ①] 추적 파일 + 아직 안 올린 새 파일(무시 목록 빼고) — 안 올린 파일에 값이 붙어 있으면 다음 git add에 그대로 공개된다
        string? lsAll = Git(top, "-c core.quotepath=false ls-files --cached --others --exclude-standard");
        if (lsAll == null) { check("S177 git ls-files", false, top); return; }
        var files = lsAll.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

        // ① 설치 전용 파일이 추적되고 있지 않다
        bool keysTracked = files.Any(f => Path.GetFileName(f).StartsWith("dh.keys", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith("share3d", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        check("S177 ① 설치 전용 키 파일(dh.keys · share3d .json)이 git에 없음", !keysTracked, keysTracked ? "추적됨 — git rm --cached" : "없음");

        // ② 알려진 값이 추적 파일에 없다(우리 프로젝트 = 실패 · 그 밖 = 알림) · 작업 폴더의 지금 내용을 본다(커밋 전 고친 것도)
        var hitsOurs = new List<string>(); var hitsOther = new List<string>();
        int scanned = 0;
        foreach (var f in files)
        {
            if (Binary.Contains(Path.GetExtension(f))) continue;
            string full = Path.Combine(top, f.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;
            var fi = new FileInfo(full);
            if (fi.Length > 20_000_000) continue;
            string text;
            try { text = File.ReadAllText(full); } catch { continue; }
            scanned++;
            foreach (var s in secrets)
            {
                int at = text.IndexOf(s, StringComparison.Ordinal);
                if (at < 0) continue;
                int line = 1; for (int i = 0; i < at; i++) if (text[i] == '\n') line++;
                (f.StartsWith(projRel, StringComparison.Ordinal) ? hitsOurs : hitsOther).Add($"{f}:{line}");
                break;
            }
        }
        check("S177 ② 알려진 비밀값(내장 VWorld 키 · 사내 DB 주소 · 비밀번호)이 우리 프로젝트 추적 파일에 0곳",
            secrets.Count > 0 && hitsOurs.Count == 0,
            secrets.Count == 0 ? "설치 키 파일이 없어 값 검사를 못 함" : $"검사 {scanned}개 파일 · 값 {secrets.Count}개 · 걸림 {hitsOurs.Count}곳{(hitsOurs.Count > 0 ? " — " + string.Join(", ", hitsOurs.Take(8)) : "")}");
        if (hitsOther.Count > 0)
            Console.WriteLine($"  ⚠ 알림 — 저장소의 다른 자리(우리 프로젝트 밖)에 같은 값 {hitsOther.Count}곳: {string.Join(", ", hitsOther.Take(8))}");

        // ③ 꼴 검사 — 소스에 키 · 비밀번호 상수가 다시 들어오지 않게(값을 몰라도)
        var bad = new List<string>();
        var rxKey = new Regex(@"const\s+string\s+\w*(ApiKey|Password|Secret|Token)\w*\s*=\s*""[^""]+""", RegexOptions.IgnoreCase);
        var rxUuidKey = new Regex(@"(key|apikey)\s*[=:]\s*[""'][0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}[""']", RegexOptions.IgnoreCase);
        foreach (var f in files.Where(f => f.StartsWith(projRel, StringComparison.Ordinal) && (f.EndsWith(".cs") || f.EndsWith(".js") || f.EndsWith(".mjs") || f.EndsWith(".html") || f.EndsWith(".py") || f.EndsWith(".ps1"))))
        {
            string full = Path.Combine(top, f.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;
            var lines = File.ReadAllLines(full);
            for (int i = 0; i < lines.Length; i++)
                if (rxKey.IsMatch(lines[i]) || rxUuidKey.IsMatch(lines[i])) bad.Add($"{f}:{i + 1}");
        }
        check("S177 ③ 소스에 키 · 비밀번호 상수 꼴 0곳(const …ApiKey/Password = \"…\" · key = \"UUID\")", bad.Count == 0, bad.Count == 0 ? "없음" : string.Join(", ", bad.Take(8)));
    }
}
