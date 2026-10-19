using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.1.7 · JACK 1014 «최대한 현장확인말고 로그로 확인할수있게 해줘»] <b>자가점검 기록</b> — 화면을 안 봐도 로그만으로 «됐다 · 안 됐다»를 가린다.
/// <para>파일 셋(진단 로그와 같은 폴더 — 개발 PC는 저장소 폴더):
/// <c>DH_자가점검.log</c> 한 줄 = 한 판정(시각 · 판 · 도면 · 무엇 · ✔ 됨 / ⚠ 문제 / – 알림 · 내용 — 2MB가 넘으면 <c>DH_자가점검_이전.log</c>로 넘긴다) ·
/// <c>DH_진행중.txt</c> 지금 도는 긴 작업(시작할 때 적고 끝나면 지운다 — Civil이 그 도중에 죽으면 남아 다음 시작 때 «⚠ 끝나지 않았다») ·
/// <c>DH_세션.txt</c> Civil 세션(시작할 때 적고 정상 종료 때 끝을 적는다 — 끝이 없으면 다음 시작 때 «⚠ 정상 종료되지 않았다»).</para>
/// <para>화면 없는 Civil(accoreconsole — 시험)은 이름 끝에 «_화면없음»이 붙은 따로 파일을 쓴다 — 시험이 일부러 죽인 세션이 JACK의 기록에 섞이지 않게.</para>
/// <para>이 클래스는 Autodesk 형을 하나도 안 쓴다(Civil 없이 시험할 수 있다). 모든 기록은 실패해도 조용히 넘긴다 — 로그가 기능을 깨면 안 된다(<see cref="DiagLog"/>와 같은 규칙).</para></summary>
internal static class SelfCheckLog
{
#pragma warning disable CS0649   // 시험에서 반사로 넣는다
    /// <summary>시험용 이음매 — 기록 폴더를 딴 곳으로 돌린다(평소엔 null = 진단 로그 폴더).</summary>
    static string? _dirForTest;
#pragma warning restore CS0649
    static readonly object Gate = new();
    static readonly string Host = DetectHost();
    static string DetectHost()
    {
        try
        {
            string pn = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            return pn.Equals("accoreconsole", System.StringComparison.OrdinalIgnoreCase) ? "_화면없음" : "";
        }
        catch { return ""; }
    }

    /// <summary>★[v103.1.9] 화면 없는 Civil(accoreconsole — 시험)인가 — 창(도킹창 · 알림창)을 건드리면 Civil이 죽는다(창 틀이 없다 · DHP2G1이 정지면 생성 끝에서 잡음). 화면 있는 Civil은 거짓.</summary>
    internal static bool Headless => Host.Length > 0;

    static string Dir
    {
        get
        {
            string d = _dirForTest ?? System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? "";
            return d.Length == 0 ? "." : d;
        }
    }
    /// <summary>판정 기록 파일(안내 문구 표기용).</summary>
    public static string JournalPath => System.IO.Path.Combine(Dir, $"DH_자가점검{Host}.log");
    static string OldJournalPath => System.IO.Path.Combine(Dir, $"DH_자가점검{Host}_이전.log");
    static string BusyPath => System.IO.Path.Combine(Dir, $"DH_진행중{Host}.txt");
    static string SessionPath => System.IO.Path.Combine(Dir, $"DH_세션{Host}.txt");
    /// <summary>점검 보고(<c>DHSELFCHECK</c>가 덮어쓴다).</summary>
    public static string ReportPath => System.IO.Path.Combine(Dir, $"DH_자가점검_보고{Host}.txt");
    const long RollBytes = 2L * 1024 * 1024;
    const int SessionKeep = 30;

    /// <summary>판 번호만(«v103.1.7 (2026-10-14)» → «v103.1.7»).</summary>
    internal static string Ver
    {
        get { string v = GradingSettings.Version; int sp = v.IndexOf(' '); return sp > 0 ? v.Substring(0, sp) : v; }
    }
    static int Pid { get { try { return System.Environment.ProcessId; } catch { return 0; } } }

    /// <summary>한 줄 적는다. <paramref name="mark"/>: '✔' 됨 · '⚠' 문제 · '–' 알림(판정이 아님).</summary>
    public static void Line(string what, char mark, string text, string doc = "")
    {
        try
        {
            string t = Clean(text);
            lock (Gate)
            {
                Roll();
                System.IO.File.AppendAllText(JournalPath, $"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} · {Ver} · {ShortDoc(doc)} · {Clean(what)} · {mark} · {t}\n");
            }
        }
        catch { }
    }

    /// <summary>판정 한 줄 — 참이면 ✔ · 거짓이면 ⚠.</summary>
    public static void Line(string what, bool ok, string text, string doc = "") => Line(what, ok ? '✔' : '⚠', text, doc);

    /// <summary>한 줄로 — 굵게 표시를 떼고 줄바꿈을 « · »로 · 너무 길면 자른다(한 판정이 한 줄이어야 grep으로 읽힌다).</summary>
    internal static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string t = s.Replace("<b>", "").Replace("</b>", "").Replace("<i>", "").Replace("</i>", "")
                    .Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
        var parts = t.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0);
        t = string.Join(" · ", parts);
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t.Length > 6000 ? t.Substring(0, 6000) + " …(잘림)" : t;
    }

    static string ShortDoc(string doc)
    {
        if (string.IsNullOrEmpty(doc)) return "-";
        try { return System.IO.Path.GetFileName(doc); } catch { return doc; }
    }

    static void Roll()
    {
        try
        {
            var fi = new System.IO.FileInfo(JournalPath);
            if (!fi.Exists || fi.Length < RollBytes) return;
            System.IO.File.Move(JournalPath, OldJournalPath, true);
        }
        catch { }
    }

    static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, text);
        System.IO.File.Move(tmp, path, true);
    }

    /// <summary>그 번호의 프로세스가 아직 도는 Civil(acad · accoreconsole)인가 — 못 읽으면 «아니다».</summary>
    static bool Alive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            if (p.HasExited) return false;
            string pn = p.ProcessName;
            return pn.Equals("acad", System.StringComparison.OrdinalIgnoreCase) || pn.Equals("accoreconsole", System.StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ── 진행 표지 ─────────────────────────────────────────────

    /// <summary>긴 작업을 시작한다 — 진행 표지에 한 줄(번호를 돌려준다 · <see cref="End"/>로 지운다).</summary>
    public static string Begin(string what, string doc)
    {
        string id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
        try
        {
            lock (Gate)
                System.IO.File.AppendAllText(BusyPath, $"{id}\t{Pid}\t{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{Ver}\t{ShortDoc(doc)}\t{Clean(what)}\n");
        }
        catch { }
        return id;
    }

    /// <summary>그 작업의 진행 표지를 지운다.</summary>
    public static void End(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            lock (Gate)
            {
                if (!System.IO.File.Exists(BusyPath)) return;
                var keep = System.IO.File.ReadAllLines(BusyPath).Where(l => l.Length > 0 && !l.StartsWith(id + "\t", System.StringComparison.Ordinal)).ToList();
                if (keep.Count == 0) System.IO.File.Delete(BusyPath);
                else WriteAtomic(BusyPath, string.Join("\n", keep) + "\n");
            }
        }
        catch { }
    }

    /// <summary>지금 진행 표지에 적힌 작업들(점검 보고용) — 없으면 «없음».</summary>
    public static string BusyNow()
    {
        try
        {
            lock (Gate)
            {
                if (!System.IO.File.Exists(BusyPath)) return "없음";
                var l = System.IO.File.ReadAllLines(BusyPath).Where(x => x.Length > 0).Select(x => x.Split('\t')).Where(a => a.Length >= 6)
                    .Select(a => $"«{a[5]}»(시작 {a[2]} · 프로세스 {a[1]}{(int.TryParse(a[1], out int p) && p == Pid ? " — 이 Civil" : "")})").ToList();
                return l.Count == 0 ? "없음" : string.Join(" · ", l);
            }
        }
        catch (System.Exception ex) { return "못 읽음(" + ex.GetType().Name + ")"; }
    }

    /// <summary>★긴 작업 하나 — 만들 때 진행 표지를 적고, <see cref="Done"/>으로 판정 한 줄을 남기고, 버릴 때 표지를 지운다.
    /// 판정 없이 버려지면(취소 · 중간에 멈춤 · 판정 줄을 안 거친 길) «판정 없이 끝남»을 적는다 — 어느 작업도 기록 없이 사라지지 않게.</summary>
    internal sealed class Job : System.IDisposable
    {
        readonly string _id, _what, _doc;
        readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        bool _done, _disposed;
        internal Job(string what, string doc) { _what = what; _doc = doc ?? ""; _id = Begin(what, _doc); }
        string Secs => (_sw.ElapsedMilliseconds / 1000.0).ToString("F1");
        /// <summary>판정 한 줄(한 번만 — 두 번째부터는 알림 줄).</summary>
        public void Done(bool ok, string text)
        {
            if (_done) { Line(_what, '–', "(덧붙임) " + text, _doc); return; }
            _done = true;
            Line(_what, ok, $"{text} · {Secs}초", _doc);
        }
        /// <summary>판정이 아닌 알림 한 줄.</summary>
        public void Note(string text) => Line(_what, '–', text, _doc);
        /// <summary>★[v103.1.7 · 확인 검토 낮음 4] 판정 대신 알림(–)으로 끝낸다 — 정상 끝이지만 ✔/⚠로 가를 것이 없는 길(옛 판 폴리곤만 길 등).</summary>
        public void DoneInfo(string text)
        {
            if (_done) { Line(_what, '–', "(덧붙임) " + text, _doc); return; }
            _done = true;
            Line(_what, '–', $"{text} · {Secs}초", _doc);
        }
        public bool HasVerdict => _done;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_done) { _done = true; Line(_what, '–', $"판정 없이 끝남(취소 · 중간에 멈춤 · 판정 줄을 안 거친 길 — 진단 로그 참고) · {Secs}초", _doc); }
            End(_id);
        }
    }

    // ── 세션 ─────────────────────────────────────────────

    /// <summary>Civil 시작 뒤 한 번 — 지난 세션 · 끝나지 않은 작업을 살피고 이번 세션을 적는다.
    /// 반환 = (문제 없나, 자가점검 줄 내용). 다른 Civil이 아직 돌고 있으면(프로세스가 살아 있다) 그 세션 · 작업은 건드리지 않는다.</summary>
    public static (bool Ok, string Text) SessionStart(string extra)
    {
        var warn = new List<string>();
        try
        {
            lock (Gate)
            {
                var lines = System.IO.File.Exists(SessionPath)
                    ? System.IO.File.ReadAllLines(SessionPath).Where(l => l.Length > 0).ToList()
                    : new List<string>();
                for (int i = 0; i < lines.Count; i++)
                {
                    var a = lines[i].Split('\t');
                    if (a.Length < 4 || a[3].Length > 0) continue;              // 끝을 적은 세션
                    if (!int.TryParse(a[0], out int pid) || pid == Pid) continue;
                    if (Alive(pid)) continue;                                     // 다른 Civil이 아직 돈다
                    warn.Add($"지난 Civil 세션(시작 {a[1]} · 판 {a[2]})이 정상 종료되지 않았다(멈췄거나 강제로 껐다)");
                    lines[i] = string.Join("\t", a[0], a[1], a[2], "비정상");
                }
                lines.Add($"{Pid}\t{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{Ver}\t");
                if (lines.Count > SessionKeep) lines = lines.Skip(lines.Count - SessionKeep).ToList();
                WriteAtomic(SessionPath, string.Join("\n", lines) + "\n");

                if (System.IO.File.Exists(BusyPath))
                {
                    var keep = new List<string>();
                    foreach (string l in System.IO.File.ReadAllLines(BusyPath))
                    {
                        var b = l.Split('\t');
                        if (b.Length < 6) continue;
                        if (int.TryParse(b[1], out int bp) && bp != Pid && Alive(bp)) { keep.Add(l); continue; }
                        warn.Add($"지난번 «{b[5]}»(시작 {b[2]} · 판 {b[3]} · 도면 {b[4]})이 끝나지 않았다 — Civil이 그 도중에 멈춘 것으로 보인다");
                    }
                    if (keep.Count == 0) System.IO.File.Delete(BusyPath);
                    else WriteAtomic(BusyPath, string.Join("\n", keep) + "\n");
                }
            }
        }
        catch (System.Exception ex) { warn.Add("세션 기록을 못 다뤘다(" + ex.GetType().Name + ")"); }
        string text = (warn.Count == 0 ? "지난 세션은 정상 종료 · 끝나지 않은 작업 없음" : string.Join(" · ", warn))
                    + (string.IsNullOrEmpty(extra) ? "" : " · " + extra);
        return (warn.Count == 0, text);
    }

    /// <summary>끝내기를 물렸다(QuitAborted) — 이번 세션 줄의 끝을 지운다(그 뒤에 Civil이 죽으면 다음 시작이 알게).</summary>
    public static void SessionReopen()
    {
        try
        {
            lock (Gate)
            {
                if (!System.IO.File.Exists(SessionPath)) return;
                var lines = System.IO.File.ReadAllLines(SessionPath).Where(l => l.Length > 0).ToList();
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    var a = lines[i].Split('\t');
                    if (a.Length < 4 || !int.TryParse(a[0], out int pid) || pid != Pid) continue;
                    if (a[3].Length > 0 && a[3] != "비정상") { lines[i] = string.Join("\t", a[0], a[1], a[2], ""); WriteAtomic(SessionPath, string.Join("\n", lines) + "\n"); }
                    break;
                }
            }
        }
        catch { }
    }

    /// <summary>정상 종료 — 이번 세션 줄에 끝을 적는다.</summary>
    public static void SessionEnd()
    {
        try
        {
            lock (Gate)
            {
                if (!System.IO.File.Exists(SessionPath)) return;
                var lines = System.IO.File.ReadAllLines(SessionPath).Where(l => l.Length > 0).ToList();
                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    var a = lines[i].Split('\t');
                    if (a.Length < 4 || a[3].Length > 0) continue;
                    if (!int.TryParse(a[0], out int pid) || pid != Pid) continue;
                    lines[i] = string.Join("\t", a[0], a[1], a[2], System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    break;
                }
                WriteAtomic(SessionPath, string.Join("\n", lines) + "\n");
            }
        }
        catch { }
    }
}

/// <summary>★[v103.1.7] 우리 긴 명령마다 진행 표지 + 끝 한 줄(걸린 시간 · 끝/취소/실패) — 명령 안의 판정은 그 명령의 <see cref="SelfCheckLog.Job"/>이 따로 적는다.
/// 도면 이벤트(<c>CommandWillStart</c> · <c>CommandEnded</c> · <c>CommandCancelled</c> · <c>CommandFailed</c>)가 부른다(<see cref="RibbonApp"/>).</summary>
internal static class SelfCheckCmd
{
    /// <summary>표지를 세우는 명령 — 도면을 바꾸거나 오래 걸리는 것만(찍기 · 창 열기 같은 잔명령은 뺀다 — 기록이 잡음으로 덮이지 않게).</summary>
    static readonly HashSet<string> Tracked = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "DHGRADE", "DHWALL", "DHSLOPE", "DHWALLCOMP", "DHRESET", "DHMISCSET", "DHNORI", "DHINFRA",
        "DHPROFILE", "DHSECTION", "DHXVIEW", "DHSAMPLE", "DHSHEET", "DHSHEETSET",
        "DHSTATION", "DHSTATIONFB", "DHSTATIONDEL",
        "DHEXCAV", "DHEXCAVWALL", "DHEXCAVSLOPE", "DHEXCAVWALLCLEAR",
        "DHNGII", "DHCROP", "DHGNDREBUILD", "DHCONTOUR", "DHCONTOURWEB", "DHCONTOURBOX", "DHPARCEL", "DHPARCELOFF",
        "DHSTRATAPICK", "DHSELFCHECK",
    };
    static readonly Dictionary<Document, (string Id, string Name, System.Diagnostics.Stopwatch Sw)> _now = new();

    internal static void Start(Document d, string? name)
    {
        if (name == null || !Tracked.Contains(name)) return;
        try
        {
            if (_now.TryGetValue(d, out var old)) SelfCheckLog.End(old.Id);   // 끝 이벤트를 못 받은 앞 명령(겹친 명령) — 표지만 걷는다
            _now[d] = (SelfCheckLog.Begin("명령 " + name.ToUpperInvariant(), d.Name), name, System.Diagnostics.Stopwatch.StartNew());
        }
        catch { }
    }

    internal static void Stop(Document d, string? name, char mark, string how)
    {
        if (name == null || !Tracked.Contains(name)) return;
        try
        {
            if (!_now.TryGetValue(d, out var cur) || !string.Equals(cur.Name, name, System.StringComparison.OrdinalIgnoreCase)) return;
            _now.Remove(d);
            SelfCheckLog.End(cur.Id);
            SelfCheckLog.Line("명령 " + name.ToUpperInvariant(), mark, $"{how} · {cur.Sw.ElapsedMilliseconds / 1000.0:F1}초", d.Name);
        }
        catch { }
    }
}

/// <summary>★★★[v103.1.7 · JACK 1014] 자가점검의 Civil 쪽 — 우리 지표면의 상태 · 지문을 읽는다(읽기만 · 아무것도 안 바꾼다).
/// <para>★화면의 느낌표는 두 가지다. <b>지표면 한 장 단위</b>(<c>IsOutOfDate</c> · <c>IsSnapshotOutOfDate</c> — 여기서 읽는 값)와
/// <b>정의 탭의 줄(작업) 단위</b> ⚠(Civil 3D 2026 API에 읽을 속성이 없다 — 0812 조사). 이 점검은 앞의 것만 본다 — «⚠ 0»이 정의 탭까지 깨끗하다는 뜻은 아니다.</para></summary>
internal static class SelfCheck
{
    static readonly System.Text.RegularExpressions.Regex OurName =
        new(@"_DH(이전|합성전)?(_\d+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>우리가 만든 지표면 이름인가 — 끝이 «_DH» · «_DH이전» · «_DH합성전»(뒤에 «_숫자»가 붙어도). «현황_DHT» · «DH_현황» 같은 남의 면은 아니다.</summary>
    internal static bool IsOurSurfaceName(string? nm) => !string.IsNullOrEmpty(nm) && OurName.IsMatch(nm);

    /// <summary>지표면 지문 — 삼각형 수 · 점 수 · 최저 · 최고 · 평균 표고(재작성 앞뒤를 견줄 값 · Civil이 들고 있는 통계라 싸다). 못 읽으면 삼각형 −1.</summary>
    internal readonly record struct Print(int Tris, long Pts, double MinZ, double MaxZ, double MeanZ)
    {
        public override string ToString()
            => Tris < 0 ? "못 읽음" : $"삼각형 {Tris:N0} · 점 {Pts:N0} · 표고 {MinZ:F3}~{MaxZ:F3}(평균 {MeanZ:F3})";
    }

    internal static Print PrintOf(Autodesk.Civil.DatabaseServices.Surface s)
    {
        int t = -1; long p = -1; double lo = double.NaN, hi = double.NaN, mean = double.NaN;
        try { if (s is Autodesk.Civil.DatabaseServices.TinSurface ts) t = ts.GetTinProperties().NumberOfTriangles; } catch { }
        try
        {
            var g = s.GetGeneralProperties();
            p = g.NumberOfPoints; lo = g.MinimumElevation; hi = g.MaximumElevation; mean = g.MeanElevation;
        }
        catch { }
        return new Print(t, p, lo, hi, mean);
    }

    /// <summary>우리 지표면의 이름 → (지문, 굳은 면인가). 굳은 면 = 스냅샷이 있고 XML로 만들었거나 붙여넣기 소스가 지워졌거나 «…이전» — 재작성으로 모양이 바뀌면 안 되는 면.</summary>
    internal static Dictionary<string, (Print P, bool Frozen)> Prints(Transaction tr, Database db)
    {
        var d = new Dictionary<string, (Print, bool)>(System.StringComparer.Ordinal);
        try
        {
            var cd = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in cd.GetSurfaceIds())
            {
                try
                {
                    if (sid.IsNull || sid.IsErased) continue;
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    string nm = s.Name ?? "";
                    if (!IsOurSurfaceName(nm) || d.ContainsKey(nm)) continue;
                    bool frozen = false;
                    try
                    {
                        frozen = s.HasSnapshot && (nm.Contains("이전") || GradingBuilder.IsXmlBacked(s) || !GradingBuilder.CanRebuildSnapshot(s, out _));
                    }
                    catch { }
                    d[nm] = (PrintOf(s), frozen);
                }
                catch { }
            }
        }
        catch { }
        return d;
    }

    /// <summary>앞뒤 지문을 견준다 — (굳은 면이 바뀜 = 문제, 그 밖의 면이 바뀜 = 알림(소스가 바뀌었으면 맞다), 같은 면 수).</summary>
    internal static (List<string> FrozenChanged, List<string> OtherChanged, int Same) Compare(
        Dictionary<string, (Print P, bool Frozen)> before, Dictionary<string, (Print P, bool Frozen)> after)
    {
        var fz = new List<string>(); var other = new List<string>(); int same = 0;
        foreach (var kv in before)
        {
            if (!after.TryGetValue(kv.Key, out var a)) { (kv.Value.Frozen ? fz : other).Add($"{kv.Key}(없어졌다)"); continue; }
            if (a.P == kv.Value.P) { same++; continue; }
            (kv.Value.Frozen ? fz : other).Add($"{kv.Key}({kv.Value.P} → {a.P})");
        }
        foreach (var kv in after) if (!before.ContainsKey(kv.Key)) other.Add($"{kv.Key}(새로 생겼다)");
        return (fz, other, same);
    }

    /// <summary>★우리 지표면의 상태 한 줄 — 지표면 한 장 단위의 구식 표시(구식 · 스냅샷 구식) · XML로 만든 면의 원본 파일 · 굳음.
    /// 반환 = (글, 문제 있는 면 수, 우리 면 수). <paramref name="withPrints"/>면 면마다 지문을 덧붙인다(점검 보고용).</summary>
    internal static (string Text, int Bad, int N) SurfaceState(Transaction tr, Database db, bool withPrints = false)
    {
        var (iss, n, all) = Scan(tr, db, withPrints);
        var bad = iss.Select(kv => $"{NameOf(kv.Key)}({kv.Value})").ToList();
        string text = n == 0 && bad.Count == 0 ? "우리 지표면 없음"
                    : bad.Count == 0 ? $"우리 지표면 {n}개 — 표면 단위 구식 표시 0"
                    : $"우리 지표면 {n}개 중 ⚠{bad.Count}: {string.Join(" · ", bad)}";
        if (withPrints && all.Count > 0) text += " || " + string.Join(" · ", all);
        return (text, bad.Count, n);
    }

    /// <summary>★★[v103.1.7 · 검토 낮음 6] 작업 <b>앞</b>의 문제(<see cref="IssuesNow"/>)와 견준다 — <b>새로 생긴 것만 ⚠</b>로 센다.
    /// 전부터 있던 것(옛 판이 만든 면의 원본 없음 · 다른 PC에서 만든 도면 등)은 «이번 작업 탓 아님»으로 적기만 한다 — 안 그러면 그런 도면은 무엇을 해도 늘 ⚠다.
    /// 앞 상태를 못 읽었으면(<paramref name="before"/> = null) 전부 새로 센다(종전과 같다).</summary>
    internal static (string Text, int NewBad, int N) StateVs(Transaction tr, Database db, Dictionary<string, string>? before)
    {
        var (iss, n, _) = Scan(tr, db, false);
        // ★[v103.1.7 · 확인 검토 중간 1] «전부터 있던 것» = <b>같은 면(이름 + 핸들)</b>에 같은 까닭 — 다시 지은 같은 이름의 면(새 핸들)이 같은 문제를 가지면 새 문제다
        bool Old(KeyValuePair<string, string> kv) => before != null && before.TryGetValue(kv.Key, out var b) && b == kv.Value;
        var fresh = iss.Where(kv => !Old(kv)).Select(kv => $"{NameOf(kv.Key)}({kv.Value})").ToList();
        var old = iss.Where(Old).Select(kv => $"{NameOf(kv.Key)}({kv.Value})").ToList();
        string text = n == 0 && iss.Count == 0 ? "우리 지표면 없음"
                    : iss.Count == 0 ? $"우리 지표면 {n}개 — 표면 단위 구식 표시 0"
                    : $"우리 지표면 {n}개" + (fresh.Count > 0 ? $" 중 ⚠새로 생긴 문제 {fresh.Count}: {string.Join(" · ", fresh)}" : " — 새로 생긴 문제 0")
                      + (old.Count > 0 ? $" · 전부터 있던 것 {old.Count}(이번 작업 탓 아님): {string.Join(" / ", old)}" : "");
        return (text, fresh.Count, n);
    }

    /// <summary>작업 앞 상태 — 우리 지표면의 문제(이름 → 까닭 · 자기 트랜잭션 · 읽기만). 못 읽으면 null.</summary>
    internal static Dictionary<string, string>? IssuesNow(Database? db)
    {
        if (db == null) return null;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var (iss, _, _) = Scan(tr, db, false);
            tr.Commit();
            return iss;
        }
        catch { return null; }
    }

    /// <summary>문제 표의 열쇠(«이름 ⇥ 핸들»)에서 이름만.</summary>
    static string NameOf(string key) { int k = key.IndexOf('\t'); return k < 0 ? key : key.Substring(0, k); }

    /// <summary>우리 지표면을 훑는다 — (문제 있는 면: «이름 ⇥ 핸들» → 까닭, 우리 면 수, 지문 글).</summary>
    static (Dictionary<string, string> Issues, int N, List<string> Prints) Scan(Transaction tr, Database db, bool withPrints)
    {
        var iss = new Dictionary<string, string>(System.StringComparer.Ordinal); var all = new List<string>(); int n = 0;
        string curHandle = "";
        void Bad(string nm, string why) { string k = nm + "\t" + curHandle; iss[iss.ContainsKey(k) ? k + "#" + (iss.Count + 1) : k] = why; }
        try
        {
            var cd = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in cd.GetSurfaceIds())
            {
                string nm = "?";
                try
                {
                    if (sid.IsNull || sid.IsErased) continue;
                    curHandle = sid.Handle.ToString();
                    if (tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                    nm = s.Name ?? "?";
                    if (!IsOurSurfaceName(nm)) continue;
                    n++;
                    var why = new List<string>();
                    try { if (s.IsOutOfDate) why.Add("구식"); } catch { why.Add("구식 여부 못 읽음"); }
                    try { if (s.HasSnapshot && s.IsSnapshotOutOfDate) why.Add("스냅샷 구식"); } catch { }
                    try
                    {
                        if (GradingBuilder.IsXmlBacked(s))
                        {
                            var files = GradingBuilder.XmlFilesOf(s);
                            if (files == null) why.Add("XML 원본 경로 못 읽음");
                            else foreach (string f in files) if (!System.IO.File.Exists(f)) { why.Add("XML 원본 파일 없음"); break; }
                            if (!s.HasSnapshot) why.Add("안 굳음(재작성 때 원본을 다시 읽는다)");
                        }
                    }
                    catch { }
                    if (why.Count > 0) Bad(nm, string.Join("·", why.Distinct()));
                    if (withPrints) all.Add($"{nm}[{PrintOf(s)}{(s.HasSnapshot ? " · 스냅샷 있음" : "")}]");
                }
                catch (System.Exception ex) { Bad(nm, $"못 읽음 {ex.GetType().Name}"); }
            }
        }
        catch (System.Exception ex) { Bad("면 목록", "못 읽음 " + ex.GetType().Name); }
        return (iss, n, all);
    }

    /// <summary>줄마다 «※» 뒤(설명 꼬리 — «※정의 탭 ⚠는 …»처럼 ⚠ 글자가 들어 있어 판정을 속인다)를 뗀다.</summary>
    internal static string NoFootnote(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var lines = s.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++) { int k = lines[i].IndexOf('※'); if (k >= 0) lines[i] = lines[i].Substring(0, k).TrimEnd(); }
        return string.Join("\n", lines);
    }

    /// <summary>글에서 ⚠ 조각만 뽑는다(« · »로 나뉜 조각 중 ⚠가 든 것 · «※» 설명 꼬리는 뺀다) — 없으면 빈 문자열.</summary>
    internal static string WarnBits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var l = SelfCheckLog.Clean(NoFootnote(s)).Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries).Where(x => x.Contains('⚠')).ToList();
        return string.Join(" · ", l);
    }

    /// <summary>명령줄에 한 줄 — «[자가점검] ⚠ …»(문제가 있을 때만 부른다).</summary>
    internal static void Warn(Document? doc, string text)
    {
        try { doc?.Editor.WriteMessage("\n[자가점검] ⚠ " + SelfCheckLog.Clean(text) + $"  (기록: {SelfCheckLog.JournalPath})"); } catch { }
    }

    /// <summary>설치본 지문 — Civil · Core DLL의 SHA-256 앞 16자(어느 빌드가 돌았는지 로그로 가리려고).</summary>
    internal static string BuildStamp()
    {
        static string H(System.Reflection.Assembly a)
        {
            try
            {
                using var f = System.IO.File.OpenRead(a.Location);
                using var sha = System.Security.Cryptography.SHA256.Create();
                return System.BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
            catch { return "?"; }
        }
        return $"Civil {H(typeof(SelfCheck).Assembly)} · Core {H(typeof(DH.Grading.Core.GradingParams).Assembly)}";
    }

    // ── 명령 뒤(Idle) 다시 보기 ─────────────────────────────

    static readonly List<(Document Doc, string What, int BadNow, Dictionary<string, string>? Before, int Tries)> _pending = new();
    static bool _hooked;

    /// <summary>★명령이 끝난 뒤(다음 Idle) 우리 지표면 상태를 <b>한 번 더</b> 읽어 적는다 — 커밋 뒤에 Civil이 붙이는 표시까지 보려고(«화면이 쓰는 값을 재라» — 0812 실측: 트랜잭션 안에서 깨끗해도 커밋 뒤 더러워졌다).
    /// <paramref name="badNow"/> = 명령 안에서 센 «새로 생긴 문제» 수 · <paramref name="before"/> = 작업 앞 상태(<see cref="IssuesNow"/> — 새로 생긴 것만 ⚠로 센다).</summary>
    internal static void RecheckAtIdle(Document? doc, string what, int badNow, Dictionary<string, string>? before = null)
    {
        if (doc == null) return;
        try
        {
            _pending.Add((doc, what, badNow, before, 0));
            if (!_hooked) { AcadApp.Idle += OnIdle; _hooked = true; }
        }
        catch { }
    }

    static void OnIdle(object? sender, System.EventArgs e)
    {
        try { AcadApp.Idle -= OnIdle; } catch { }
        _hooked = false;
        var items = _pending.ToList(); _pending.Clear();
        foreach (var (doc, what, badNow, before, tries) in items)
        {
            try
            {
                bool open = false;
                foreach (Document d in AcadApp.DocumentManager) if (d == doc) { open = true; break; }
                if (!open) continue;
                // 아직 명령이 도는 중이면(알림창을 띄운 채 · 이어 태운 합성) 다음 Idle로 미룬다 — 명령이 끝난 뒤의 값을 재려는 것이다(몇 번까지만)
                string busy = ""; try { busy = doc.CommandInProgress ?? ""; } catch { }
                if (busy.Length > 0)
                {
                    // ★[v103.1.7 · 검토 낮음 14] 200번을 미뤄도 명령이 이어지면 잠그고 읽지 않는다 — 한 줄만 남기고 끝낸다
                    if (tries >= 200) { SelfCheckLog.Line(what + " — 명령 뒤", '–', $"다시 보지 못했다(명령 «{busy}»이 이어져 기다리다 그만둠)", doc.Name); continue; }
                    _pending.Add((doc, what, badNow, before, tries + 1));
                    if (!_hooked) { AcadApp.Idle += OnIdle; _hooked = true; }
                    continue;
                }
                string text; int bad;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    (text, bad, _) = StateVs(tr, doc.Database, before);
                    tr.Commit();
                }
                SelfCheckLog.Line(what + " — 명령 뒤", bad == 0, (bad == badNow ? "다시 봄: " : $"다시 봄(명령 안 새 ⚠{badNow} → 뒤 새 ⚠{bad}): ") + text, doc.Name);
                if (bad > badNow) Warn(doc, $"{what} 뒤에 지표면 구식 표시가 붙었습니다 — {text}");
            }
            catch { }
        }
    }
}
