using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DH.Grading.Civil;

/// <summary>★[v117.19 · 사내 배포 1123 · JACK 1121 «새 판을 올리면 설치한 사람에게 알림»] <b>새 판 확인</b> — 판 비교 · <c>latest.json</c> 읽기 · 확인 기록.
/// <para>이 파일은 Autodesk 형을 하나도 안 쓴다(Civil 없이 시험 — <c>.omc/tools/update_1121/test</c>가 이 파일을 그대로 엮어 돌린다).
/// 화면 · 리본 · 명령줄은 <see cref="UpdateUi"/>.</para>
/// <para>매니페스트 위치는 <b>설정값</b>(설치 폴더 <c>dh.keys.json</c>의 <c>"update"</c> — 문자열 하나 또는 목록 · 차례로 시도)이다 — 소스에 사내 주소를 쓰지 않는다.
/// HTTPS 주소와 UNC/로컬 경로(<c>\\server\share\latest.json</c>) 둘 다.</para>
/// <para><c>latest.json</c> 꼴(배포/README.md «새 판 올리는 법»):
/// <c>{ "version": "117.19.0", "date": "2026-11-24", "mandatory": false, "notes": ["…"], "installer": "DH정지플러그인_설치_v117.19.0.exe", "sha256": "64자 16진" }</c>
/// — installer가 상대 경로면 latest.json이 있는 자리 기준 · URL이면 https만 · sha256 필수.</para></summary>
internal static class UpdateCheck
{
    internal enum Status { NoSource, Unreachable, BadManifest, Same, Older, Newer }

    internal sealed record Latest(string Version, string Date, bool Mandatory, IReadOnlyList<string> Notes, string Installer, string Sha256)
    {
        /// <summary>★[위치 이전] <c>"moved"</c> — 새 확인 위치 목록(절대 경로 · UNC · http(s) 주소만). 받으면 사용자 기록에 저장하고 다음부터 거기를 먼저 본다.</summary>
        public IReadOnlyList<string> Moved { get; init; } = System.Array.Empty<string>();
    }

    /// <summary>확인 한 번의 결과. <see cref="SourceIndex"/>는 설정 목록의 몇 번째(1부터 · 0 = 없음) — 주소 자체는 화면 · 로그에 안 적는다.</summary>
    internal sealed record Result(Status Status, Latest? Latest, string Message, int SourceIndex);

    internal const int TimeoutMs = 3000;
    const int MaxManifestBytes = 256 * 1024;

    // ── 판 번호 ─────────────────────────────────────────

    /// <summary>«v117.18.26 (2026-11-21)» · «117.18.26» · «117.18» → 숫자 3~4자리(빈자리는 0). 숫자 마디가 1개 · 5개 이상 · 숫자가 아니면 null.
    /// <para>문자열 비교 금지(«117.9» &lt; «117.10»이 문자열로는 거꾸로 나온다).</para></summary>
    internal static int[]? ParseVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^\s*[vV]?(\d+(?:\.\d+){1,3})(?![\d.])");
        if (!m.Success) return null;
        var parts = m.Groups[1].Value.Split('.');
        var r = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out r[i])) return null;
        return r;
    }

    /// <summary>a &lt; b → 음수 · 같으면 0 · a &gt; b → 양수(117.18 == 117.18.0).</summary>
    internal static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < 4; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    internal static string Show(int[] v) => v[3] != 0 ? $"{v[0]}.{v[1]}.{v[2]}.{v[3]}" : $"{v[0]}.{v[1]}.{v[2]}";

    /// <summary>지금 판(<see cref="GradingSettings.Version"/>)의 날짜 «2026-11-21» — 없으면 "".</summary>
    internal static string DateOf(string versionLine)
    {
        var m = System.Text.RegularExpressions.Regex.Match(versionLine ?? "", @"\((\d{4}-\d{2}-\d{2})\)");
        return m.Success ? m.Groups[1].Value : "";
    }

    // ── latest.json ─────────────────────────────────────

    /// <summary>매니페스트 글 → <see cref="Latest"/>. 틀리면 null + 까닭.
    /// <paramref name="baseDir"/>(파일) 또는 <paramref name="baseUri"/>(주소)는 installer 상대 경로를 풀 자리.</summary>
    internal static Latest? Parse(string text, string? baseDir, System.Uri? baseUri, out string why)
    {
        why = "";
        try
        {
            using var doc = JsonDocument.Parse((text ?? "").TrimStart('﻿'),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { why = "JSON 맨 위가 { } 가 아님"; return null; }
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

            string ver = S("version");
            var pv = ParseVersion(ver);
            if (pv == null) { why = "version 이 없거나 꼴이 틀림(예: 117.19.0)"; return null; }

            bool mandatory = r.TryGetProperty("mandatory", out var mv) && mv.ValueKind == JsonValueKind.True;

            var notes = new List<string>();
            if (r.TryGetProperty("notes", out var nv))
            {
                if (nv.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in nv.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String && (e.GetString() ?? "").Trim() is { Length: > 0 } t) notes.Add(t);
                }
                else if (nv.ValueKind == JsonValueKind.String)
                    notes.AddRange((nv.GetString() ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0));
            }

            string sha = S("sha256").ToLowerInvariant();
            if (sha.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(sha, "^[0-9a-f]{64}$")) { why = "sha256 꼴이 틀림(16진 64자)"; return null; }

            string inst = S("installer");
            if (inst.Length == 0) { why = "installer 가 없음"; return null; }
            if (System.Uri.TryCreate(inst, System.UriKind.Absolute, out var iu) && (iu.Scheme == "http" || iu.Scheme == "https"))
            {
                if (iu.Scheme != "https") { why = "installer 주소는 https 만"; return null; }
            }
            else if (!System.IO.Path.IsPathRooted(inst))
            {
                if (baseUri != null)
                {
                    var abs = new System.Uri(baseUri, inst);
                    if (abs.Scheme != "https") { why = "installer 주소는 https 만"; return null; }
                    inst = abs.ToString();
                }
                else if (!string.IsNullOrEmpty(baseDir)) inst = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, inst));
            }
            // 내려받는 것(https)은 sha256이 없으면 받지 않는다 — 받은 것이 그 파일인지 가를 길이 없다
            if (IsUrl(inst) && sha.Length == 0) { why = "installer 가 주소인데 sha256 이 없음"; return null; }

            var moved = new List<string>();
            if (r.TryGetProperty("moved", out var mvd))
            {
                IEnumerable<JsonElement> items = mvd.ValueKind == JsonValueKind.Array ? mvd.EnumerateArray() : new[] { mvd };
                foreach (var e in items)
                    if (e.ValueKind == JsonValueKind.String && (e.GetString() ?? "").Trim() is { Length: > 0 } m && ValidSource(m)
                        && !moved.Contains(m, System.StringComparer.OrdinalIgnoreCase)) moved.Add(m);
            }

            return new Latest(Show(pv), S("date"), mandatory, notes, inst, sha) { Moved = moved };
        }
        catch (JsonException ex) { why = "JSON 이 깨짐 — " + ex.Message.Split('\n')[0]; return null; }
        catch (System.Exception ex) { why = "읽기 실패 — " + ex.GetType().Name; return null; }
    }

    internal static bool IsUrl(string s) =>
        System.Uri.TryCreate(s, System.UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http");

    /// <summary>확인 위치로 쓸 수 있는 값인가 — http(s) 주소 또는 드라이브/UNC로 시작하는 절대 경로(상대 경로 · 다른 scheme은 안 됨).</summary>
    internal static bool ValidSource(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return false;
        if (System.Uri.TryCreate(s, System.UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")) return true;
        return s.StartsWith(@"\\", System.StringComparison.Ordinal)
            || (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/'));
    }

    /// <summary>실제로 볼 위치 — 사용자 기록(위치 이전 · [정보] 창에서 바꾼 것)을 먼저, 그다음 설치 기본값(dh.keys.json). 같은 것은 한 번만.</summary>
    internal static List<string> EffectiveSources(State s, IReadOnlyList<string>? installed)
    {
        var res = new List<string>();
        foreach (var x in s.Sources.Concat(installed ?? System.Array.Empty<string>()))
        {
            string t = (x ?? "").Trim();
            if (ValidSource(t) && !res.Contains(t, System.StringComparer.OrdinalIgnoreCase)) res.Add(t);
        }
        return res;
    }

    /// <summary>읽힌 매니페스트에 <c>moved</c>가 있고 지금 사용자 위치와 다르면 사용자 위치로 저장할 것으로 바꾼다 — 바뀌면 참.</summary>
    internal static bool ApplyMoved(State s, Latest? lt)
    {
        if (lt == null || lt.Moved.Count == 0) return false;
        if (s.Sources.Count == lt.Moved.Count && s.Sources.Zip(lt.Moved).All(p => string.Equals(p.First, p.Second, System.StringComparison.OrdinalIgnoreCase))) return false;
        s.Sources = lt.Moved.ToList();
        s.SourcesOrigin = "moved";
        return true;
    }

    // ── 확인 ───────────────────────────────────────────

    static readonly HttpClient Http = new(new HttpClientHandler { UseDefaultCredentials = true }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = MaxManifestBytes };

    /// <summary>설정된 곳을 <b>한꺼번에</b> 읽기 시작해 목록 차례대로 본다 — 처음 읽힌 매니페스트로 지금 판과 견준다.
    /// 전체가 <paramref name="timeoutMs"/>(기본 3초) 안 — 위치가 여럿이어도 3초를 넘지 않는다(JACK PC가 꺼져 공유가 매달려도 3초 뒤 조용히 포기).
    /// <para>뒤 실(Task)에서 부른다 — 파일 · UNC 읽기는 따로 만든 실에서(스레드 풀을 붙잡지 않음) · 시간이 넘으면 기다리지 않고 버린다(그 실은 혼자 끝난다).</para></summary>
    internal static async System.Threading.Tasks.Task<Result> CheckAsync(IReadOnlyList<string> sources, string currentVersion, int timeoutMs = TimeoutMs)
    {
        var cur = ParseVersion(currentVersion);
        if (sources == null || sources.Count == 0) return new(Status.NoSource, null, "업데이트 위치 미설정", 0);
        if (cur == null) return new(Status.BadManifest, null, "지금 판 번호를 못 읽음: " + currentVersion, 0);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var reads = sources.Select(s => (s ?? "").Trim()).Select(s => s.Length == 0 ? null : ReadAsync(s, timeoutMs)).ToList();
        Result? last = null;
        for (int i = 0; i < sources.Count; i++)
        {
            string src = (sources[i] ?? "").Trim();
            if (reads[i] == null) continue;
            int left = (int)System.Math.Max(0, timeoutMs - deadline.ElapsedMilliseconds);
            var rd = reads[i]!;
            if (await System.Threading.Tasks.Task.WhenAny(rd, System.Threading.Tasks.Task.Delay(left)).ConfigureAwait(false) != rd)
            {
                last = new(Status.Unreachable, null, $"응답 없음({timeoutMs / 1000.0:0.#}초) — 회사망 밖 · 공유 PC 꺼짐이면 정상", i + 1);
                continue;
            }
            var (text, err) = await rd.ConfigureAwait(false);
            if (text == null) { last = new(Status.Unreachable, null, err, i + 1); continue; }
            System.Uri? bu = IsUrl(src) ? new System.Uri(src) : null;
            string? bd = bu == null ? SafeDir(src) : null;
            var lt = Parse(text, bd, bu, out string why);
            if (lt == null) { last = new(Status.BadManifest, null, why, i + 1); continue; }
            int c = Compare(ParseVersion(lt.Version)!, cur);
            var st = c > 0 ? Status.Newer : c == 0 ? Status.Same : Status.Older;
            string msg = st switch
            {
                Status.Newer => $"새 판 {lt.Version} 이 있음(지금 {Show(cur)})",
                Status.Same => $"최신 판입니다({Show(cur)})",
                _ => $"올라온 판 {lt.Version} 이 지금 판 {Show(cur)} 보다 낮음 — 알리지 않음",
            };
            return new(st, lt, msg, i + 1);
        }
        return last ?? new(Status.NoSource, null, "확인 위치가 비어 있음", 0);
    }

    static string? SafeDir(string p) { try { return System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(p)); } catch { return null; } }

    /// <summary>한 곳 읽기 — 시간이 넘으면 (null, «응답 없음»).</summary>
    static async System.Threading.Tasks.Task<(string? Text, string Err)> ReadAsync(string src, int timeoutMs)
    {
        try
        {
            if (IsUrl(src))
            {
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                try
                {
                    using var resp = await Http.GetAsync(src, cts.Token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return (null, $"HTTP {(int)resp.StatusCode}");
                    return (await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false), "");
                }
                catch (System.OperationCanceledException) { return (null, $"응답 없음({timeoutMs / 1000.0:0.#}초) — 회사망 밖이면 정상"); }
                catch (HttpRequestException ex) { return (null, "연결 안 됨 — " + (ex.InnerException?.GetType().Name ?? ex.GetType().Name)); }
            }
            // 파일 · UNC — File.Exists도 공유 PC가 꺼져 있으면 수십 초 매달린다 → 따로 만든 실(배경)에 두고 시간만 잰다
            var tcs = new System.Threading.Tasks.TaskCompletionSource<(string?, string)>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            var th = new System.Threading.Thread(() =>
            {
                try
                {
                    var fi = new System.IO.FileInfo(src);
                    if (!fi.Exists) tcs.TrySetResult((null, "파일 없음(경로 · 공유 폴더 권한 · 회사망 확인)"));
                    else if (fi.Length > MaxManifestBytes) tcs.TrySetResult((null, "파일이 너무 큼(latest.json 이 맞나)"));
                    else tcs.TrySetResult((System.IO.File.ReadAllText(src, Encoding.UTF8), ""));
                }
                catch (System.Exception ex) { tcs.TrySetResult((null, "읽기 실패 — " + ex.GetType().Name)); }
            }) { IsBackground = true, Name = "DH 새 판 확인" };
            th.Start();
            var read = tcs.Task;
            var done = await System.Threading.Tasks.Task.WhenAny(read, System.Threading.Tasks.Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (done != read) return (null, $"응답 없음({timeoutMs / 1000.0:0.#}초) — 회사망 밖 · 공유 PC 꺼짐이면 정상");
            return await read.ConfigureAwait(false);
        }
        catch (System.Exception ex) { return (null, "읽기 실패 — " + ex.GetType().Name); }
    }

    // ── 확인 기록(%APPDATA%\DH.Grading\update.json) ─────

    internal sealed class State
    {
        public System.DateTime LastCheck;     // 지역 시각 · MinValue = 한 번도 안 함
        public string LastStatus = "";
        public string LastMessage = "";
        public string SkipVersion = "";       // «이 판은 다시 안 알림»
        public Latest? Latest;                // 마지막으로 읽힌 매니페스트(없으면 null)
        public List<string> Sources = new();  // 사용자 확인 위치(위치 이전 «moved» · [정보] 창) — 설치 기본값보다 먼저 본다
        public string SourcesOrigin = "";     // "moved" · "user" · ""(없음)
    }

    /// <summary>시험용 이음매 — 기록 폴더(평소엔 null = <c>DH_GRADING_KEYS_DIR</c> 또는 %APPDATA%\DH.Grading).</summary>
#pragma warning disable CS0649   // 시험이 반사로 넣는다
    internal static string? StateDirForTest;
#pragma warning restore CS0649
    static string StateDir => StateDirForTest ?? DhKeysTestDir() ?? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "DH.Grading");
    static string? DhKeysTestDir() => System.Environment.GetEnvironmentVariable("DH_GRADING_KEYS_DIR") is { Length: > 0 } d ? d : null;
    internal static string StatePath => System.IO.Path.Combine(StateDir, "update.json");
    static readonly object Gate = new();

    internal static State LoadState()
    {
        var s = new State();
        try
        {
            lock (Gate)
            {
                if (!System.IO.File.Exists(StatePath)) return s;
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(StatePath));
                var r = doc.RootElement;
                string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                if (System.DateTime.TryParse(S("lastCheck"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var lc)) s.LastCheck = lc;
                s.LastStatus = S("lastStatus"); s.LastMessage = S("lastMessage"); s.SkipVersion = S("skipVersion");
                if (r.TryGetProperty("latest", out var l) && l.ValueKind == JsonValueKind.Object)
                    s.Latest = Parse(l.GetRawText(), null, null, out _);
                if (r.TryGetProperty("sources", out var sv) && sv.ValueKind == JsonValueKind.Array)
                    foreach (var e in sv.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String && ValidSource(e.GetString() ?? "")) s.Sources.Add(e.GetString()!.Trim());
                s.SourcesOrigin = S("sourcesOrigin");
            }
        }
        catch { }
        return s;
    }

    internal static void SaveState(State s)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(StateDir);
                var o = new Dictionary<string, object?>
                {
                    ["lastCheck"] = s.LastCheck == System.DateTime.MinValue ? "" : s.LastCheck.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    ["lastStatus"] = s.LastStatus,
                    ["lastMessage"] = s.LastMessage,
                    ["skipVersion"] = s.SkipVersion,
                    ["sources"] = s.Sources,
                    ["sourcesOrigin"] = s.SourcesOrigin,
                    ["latest"] = s.Latest == null ? null : new Dictionary<string, object>
                    {
                        ["version"] = s.Latest.Version, ["date"] = s.Latest.Date, ["mandatory"] = s.Latest.Mandatory,
                        ["notes"] = s.Latest.Notes, ["installer"] = s.Latest.Installer, ["sha256"] = s.Latest.Sha256,
                        ["moved"] = s.Latest.Moved,
                    },
                };
                string tmp = StatePath + ".tmp";
                System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
                System.IO.File.Move(tmp, StatePath, true);
            }
        }
        catch { }
    }

    /// <summary>결과를 기록에 반영한다 — 못 읽은 확인(망 밖)은 «마지막 매니페스트»를 지우지 않는다.</summary>
    internal static void Record(State s, Result r, System.DateTime now)
    {
        s.LastCheck = now;
        s.LastStatus = r.Status.ToString();
        s.LastMessage = r.Message;
        if (r.Latest != null) s.Latest = r.Latest;
    }

    /// <summary>시작 때 자동 확인을 할 때인가 — 하루 한 번(20시간 넘게 지났거나 시계가 거꾸로 갔으면).
    /// 지난번이 «닿지 않음»(회사망 밖)이었으면 2시간 뒤 다시(아침에 망 밖에서 켰다가 회사망에 붙은 날을 하루 통째로 놓치지 않게).</summary>
    internal static bool Due(State s, System.DateTime now) =>
        s.LastCheck == System.DateTime.MinValue || now < s.LastCheck
        || now - s.LastCheck >= System.TimeSpan.FromHours(s.LastStatus == nameof(Status.Unreachable) ? 2 : 20);

    /// <summary>풍선으로 알릴 것인가 — 새 판이고 · «이 판 안 알림»이 아닐 때(꼭 깔 판은 늘 알림).</summary>
    internal static bool ShouldNotify(Result r, State s) =>
        r.Status == Status.Newer && r.Latest != null && (r.Latest.Mandatory || !SameVersion(s.SkipVersion, r.Latest.Version));

    internal static bool SameVersion(string a, string b)
    {
        var pa = ParseVersion(a); var pb = ParseVersion(b);
        return pa != null && pb != null && Compare(pa, pb) == 0;
    }

    /// <summary>기록의 마지막 매니페스트가 지금 판보다 높은가(창 강조 · 리본 표시용 — 망 밖에서도).</summary>
    internal static bool CachedNewer(State s, string currentVersion)
    {
        var cur = ParseVersion(currentVersion); var lv = s.Latest == null ? null : ParseVersion(s.Latest.Version);
        return cur != null && lv != null && Compare(lv, cur) > 0;
    }

    // ── 설치 파일 ───────────────────────────────────────

    /// <summary>파일의 sha256(소문자 16진).</summary>
    internal static string Sha256Of(string path)
    {
        using var f = System.IO.File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return System.Convert.ToHexString(sha.ComputeHash(f)).ToLowerInvariant();
    }

    /// <summary>https 설치 파일을 <paramref name="dir"/>에 내려받고 sha256을 맞춘다. 틀리면 지우고 예외.</summary>
    internal static async System.Threading.Tasks.Task<string> DownloadAsync(Latest lt, string dir, System.Threading.CancellationToken ct)
    {
        var u = new System.Uri(lt.Installer);
        if (u.Scheme != "https") throw new System.InvalidOperationException("https 주소만 내려받습니다");
        if (lt.Sha256.Length != 64) throw new System.InvalidOperationException("sha256 이 없어 내려받지 않습니다");
        string name = System.IO.Path.GetFileName(System.Uri.UnescapeDataString(u.AbsolutePath));
        foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        if (!name.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase)) name += ".exe";
        System.IO.Directory.CreateDirectory(dir);
        string dst = System.IO.Path.Combine(dir, name), part = dst + ".part";
        using (var big = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true }) { Timeout = System.TimeSpan.FromMinutes(10) })
        using (var resp = await big.GetAsync(u, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var fs = new System.IO.FileStream(part, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
            await src.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        string got = Sha256Of(part);
        if (got != lt.Sha256)
        {
            try { System.IO.File.Delete(part); } catch { }
            throw new System.IO.InvalidDataException($"받은 파일의 지문이 다릅니다(sha256 {got[..12]}… ≠ {lt.Sha256[..12]}…) — 지웠습니다");
        }
        System.IO.File.Move(part, dst, true);
        return dst;
    }

    // ── 이번 판 바뀐 점(DLL 안 ReleaseNotes.txt) ───────

    /// <summary>릴리스 노트 글에서 그 판의 줄만 — 머리 «# v117.18.26 (2026-11-21)» 아래 «- …» 줄들. 없으면 빈 목록.</summary>
    internal static List<string> NotesFor(string text, string version)
    {
        var res = new List<string>();
        var want = ParseVersion(version);
        if (want == null || string.IsNullOrEmpty(text)) return res;
        bool on = false;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string ln = raw.Trim();
            if (ln.StartsWith("#"))
            {
                if (on) break;
                var hv = ParseVersion(ln.TrimStart('#').Trim());
                on = hv != null && Compare(hv, want) == 0;
                continue;
            }
            if (on && ln.Length > 0) res.Add(ln.StartsWith("- ") ? ln[2..] : ln);
        }
        return res;
    }
}
