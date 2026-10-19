namespace DH.Grading.Civil;

/// <summary>★★★[v103.1.5 · JACK 1012 «지금 바로 고침»] LandXML로 만드는 면(옹벽 합성 정지면_DH · 순수옹벽{n}_DH)의 <b>원본 XML 파일</b>을 두고 치우는 곳.
/// <para>Civil 3D는 그런 면을 재작성할 때 원본 파일을 <b>다시 읽는다</b>(1012 화면 없는 Civil 실측 — <see cref="GradingBuilder.IsXmlBacked"/> 위 설명).
/// 그래서 규칙은 하나다 — <b>한 번 쓴 파일은 다시 쓰지 않는다</b>. 만들 때마다 새 이름으로 쓰고(<see cref="NewPath"/>), 아무 면도 안 물게 된 파일만 치운다(<see cref="Tidy"/>).</para>
/// <para>치울 때 조심할 것은 <b>저장된 도면</b>이다. 저장 안 하고 닫거나 Civil이 죽으면, 다시 연 도면은 <b>마지막 저장 때의 면</b>을 갖고 있다 —
/// 그 면의 원본을 지웠으면 «스냅샷 구식» 느낌표가 붙는다(모양은 굳어 있어 그대로이고 «스냅샷 재작성»은 «파일 없음»으로 끝난다 — 실측).
/// 그래서 <see cref="Deletable"/>이 «저장된 도면이 아직 물고 있을 수 있는 파일»을 남긴다. 합성 원본 하나가 현장 크기로 19MB라(1007) 안 치울 수는 없다.</para>
/// <para>«저장했나»는 <b>도면 파일의 마지막 쓴 시각(저장 도장)이 달라졌나</b>로 가린다 — 같은 시계끼리만 견주므로(도면이 네트워크 드라이브에 있어도) 시계 차이에 안 속는다.
/// 파일마다 «만든 때의 도장» · «안 물림을 처음 본 때와 그때의 도장»을 폴더의 목록(<c>_원본목록.tsv</c>)에 적는다.</para>
/// <para>남의 도면 폴더는 건드리지 않는다 — 그 도면이 무엇을 물고 있는지 여기서는 알 수 없다.
/// <b>폴더(DH_XML)를 통째로 지워도 면의 모양은 안 바뀐다</b>(굳어 있다) — 느낌표만 붙는다.</para>
/// <para>이 클래스는 Autodesk 형을 하나도 쓰지 않는다 — 시작 때(<see cref="RibbonApp"/>) 불러도 되고, Civil 없이 시험할 수 있다.</para></summary>
internal static class XmlStore
{
#pragma warning disable CS0649   // 시험에서 반사로 넣는다
    /// <summary>시험용 이음매 — 뿌리 폴더를 딴 곳으로 돌린다(평소엔 null = 진단 로그 폴더).</summary>
    static string? _baseForTest;
#pragma warning restore CS0649
    /// <summary>뿌리 폴더 — <b>늘 전체 경로</b>(면의 정의에 박히는 경로다 · 상대 경로면 작업 폴더가 바뀔 때 «없는 파일»이 된다 — 검토 1012 낮음 4).</summary>
    static string BaseDir
    {
        get
        {
            string d = _baseForTest ?? System.IO.Path.GetDirectoryName(DiagLog.FilePath) ?? "";
            return System.IO.Path.GetFullPath(d.Length == 0 ? "." : d);
        }
    }

    const string RootName = "DH_XML";
    const string UnnamedName = "_이름없는도면";
    const string ListName = "_원본목록.tsv";
    /// <summary>옛 판(v100.3~v103.1.4)이 실행마다 덮어쓰던 고정 이름 파일.</summary>
    static readonly string[] Legacy = { "DHWALLCOMP_합성.xml", "DHWALLDL_순수옹벽.xml" };

    /// <summary>되돌리기(Ctrl+Z) 여유 — 안 물리게 된 지 이만큼은 둔다(되돌려 옛 면이 살아나도 원본이 있게).</summary>
    static readonly System.TimeSpan UndoGrace = System.TimeSpan.FromMinutes(20);
    /// <summary>이름 없는 도면이 함께 쓰는 폴더의 파일을 치우는 한도(만든 지) — ★[v103.1.7 · JACK 1014 «1일»](종전 30일 · 합성 한 번에 19MB).
    /// 지금 열린 도면이 무는 것 · 나중에 저장한 도면이 문다고 적힌 것(<see cref="ClaimSaved"/>)은 기한과 상관없이 둔다.</summary>
    static readonly System.TimeSpan UnnamedKeep = System.TimeSpan.FromDays(1);
    /// <summary>★[v103.1.7] 도면 파일이 없어진 뒤 그 폴더 · 그 도면의 «물림» 기록을 두는 한도(네트워크 드라이브가 잠깐 안 보이는 것 · 옮긴 도면을 다시 여는 것을 기다린다).</summary>
    static readonly System.TimeSpan MissingKeep = System.TimeSpan.FromDays(30);
    /// <summary>★[v103.1.7] 이 폴더의 파일을 무는 <b>다른</b> 저장된 도면의 목록 — 줄: 파일 이름 · 도면 전체 경로 · 적은 때의 도면 도장 · 안 물림을 본 때 · 그때의 도면 도장 · 도면이 없어진 것을 처음 본 때.</summary>
    const string ClaimName = "_물린도면.tsv";
    /// <summary>★[v103.1.7] 이 폴더의 도면 전체 경로(첫 줄) · 도면이 없어진 것을 처음 본 때(둘째 줄 · 틱) — 도면이 없어진 폴더를 치우는 근거(<see cref="SweepOrphans"/>).</summary>
    const string DwgNoteName = "_도면.txt";
    /// <summary>★★[v103.1.7 · 검토 중간 1] 이름 없는 도면 폴더에서 <b>1일 규칙을 처음 쓴 때</b>(UTC 틱) — 그보다 먼저 쓴 파일(v103.1.6 이하가 만든 것)은 종전대로 30일을 둔다.
    /// 그때 «이름 없이 만들고 저장한» 도면에는 «물림» 기록(<see cref="ClaimSaved"/>)이 없어, 1일로 지우면 설치 첫날 그 도면들의 원본이 지워진다.</summary>
    const string RuleStartName = "_1일규칙_시작.txt";
    static readonly System.TimeSpan LegacyUnnamedKeep = System.TimeSpan.FromDays(30);
    /// <summary>★[v103.1.7 · 검토 낮음 1] 이름 없는 도면 폴더의 «안 물림을 처음 본 때»(전체 경로 → UTC 틱 · 이 프로세스 기억) — 되돌리기(Ctrl+Z) 여유 20분을 이름 있는 도면과 같게 둔다.</summary>
    static readonly Dictionary<string, long> _unnamedSeen = new(System.StringComparer.OrdinalIgnoreCase);
#pragma warning disable CS0649   // 시험에서 반사로 넣는다
    /// <summary>시험용 이음매 — 이름 없는 도면 폴더의 되돌리기 여유를 바꾼다(평소엔 null = <see cref="UndoGrace"/>).</summary>
    static System.TimeSpan? _unnamedGraceForTest;
#pragma warning restore CS0649
    /// <summary>★[v103.1.7 · 검토 중간 4] 경로 뿌리(드라이브 · \\서버\공유)가 지금 보이나 — 한 프로세스에서 뿌리마다 한 번만 묻고(2초 안에 답이 없으면 «모름»),
    /// 안 보이는 뿌리의 도면은 «없어졌다»로 세지 않는다(네트워크 드라이브가 끊긴 동안 30일이 쌓여 폴더를 지우는 일 · 시작 때 SMB 시간초과로 멈추는 일을 막는다).</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _rootSeen = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>이 경로의 뿌리가 지금 보이면 true · 안 보이거나 2초 안에 모르면 false(= 판단 보류).</summary>
    internal static bool RootReachable(string path)
    {
        try
        {
            string? root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return true;
            if (_rootSeen.TryGetValue(root, out bool known)) return known;
            var t = System.Threading.Tasks.Task.Run(() => System.IO.Directory.Exists(root));
            bool ok = t.Wait(2000) && t.Result;
            _rootSeen[root] = ok;
            return ok;
        }
        catch { return false; }
    }

    /// <summary>그 폴더의 1일 규칙 시작 때 — 없으면 지금으로 적는다(못 적으면 지금 = 모든 파일을 옛 규칙으로 — 늦게 지우는 쪽).</summary>
    static long RuleStart(string dir)
    {
        string p = System.IO.Path.Combine(dir, RuleStartName);
        try
        {
            if (System.IO.File.Exists(p) && long.TryParse(System.IO.File.ReadAllText(p).Trim(), out long t) && t > 0 && t <= System.DateTime.MaxValue.Ticks) return t;
        }
        catch { }
        long now = System.DateTime.UtcNow.Ticks;
        try { System.IO.File.WriteAllText(p, now.ToString()); } catch { }
        return now;
    }

    /// <summary>목록의 한 줄 — 틱. 0 = 모름/없음.</summary>
    sealed class Row
    {
        public long Made;        // 만든 때의 저장 도장
        public long Seen;        // «안 물림»을 처음 본 때(이 PC의 UTC)
        public long SeenStamp;   // 그때의 저장 도장
    }

    /// <summary>도면의 <b>저장 도장</b> — 도면 파일을 마지막으로 쓴 시각(틱). 이름 없는 도면 · 못 읽음 = 0.</summary>
    static long SaveStamp(string dwgPath)
    {
        try { if (!string.IsNullOrEmpty(dwgPath) && System.IO.File.Exists(dwgPath)) return System.IO.File.GetLastWriteTimeUtc(dwgPath).Ticks; }
        catch { }
        return 0;
    }

    /// <summary>XML 원본을 두는 폴더 — <b>도면마다 따로</b>(&lt;로그 폴더&gt;\DH_XML\&lt;도면 이름&gt;_&lt;경로 지문&gt;). 도면끼리 서로의 파일을 치우지 않게.
    /// <paramref name="dwgPath"/>가 비었으면(이름 없는 새 도면) 함께 쓰는 폴더.</summary>
    public static string DirFor(string dwgPath)
    {
        string dir = FolderPath(dwgPath);
        System.IO.Directory.CreateDirectory(dir);
        // ★[v103.1.7] 이 폴더가 누구 것인지 적어 둔다 — 도면이 없어진 폴더를 치울 근거(SweepOrphans · 검토 1012 낮음 5)
        if (!string.IsNullOrEmpty(dwgPath))
            try
            {
                string note = System.IO.Path.Combine(dir, DwgNoteName);
                if (!System.IO.File.Exists(note)) System.IO.File.WriteAllText(note, System.IO.Path.GetFullPath(dwgPath) + "\n");
            }
            catch { }
        return dir;
    }

    /// <summary>그 도면의 원본 폴더 경로 — 만들지는 않는다(<see cref="DirFor"/>와 같은 이름 규칙).</summary>
    static string FolderPath(string dwgPath)
    {
        string name = UnnamedName;
        if (!string.IsNullOrEmpty(dwgPath))
        {
            string stem = ""; try { stem = System.IO.Path.GetFileNameWithoutExtension(dwgPath) ?? ""; } catch { }
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
            if (stem.Length > 40) stem = stem.Substring(0, 40);
            stem = stem.Trim().TrimEnd('.', ' ');                   // 폴더 이름 끝의 점 · 공백은 Windows가 떼어 낸다
            if (stem.Length == 0) stem = "도면";
            uint h = 2166136261;                                    // FNV-1a — string.GetHashCode는 실행마다 달라져 폴더 이름으로 못 쓴다
            unchecked { foreach (char c in dwgPath.ToUpperInvariant()) { h ^= c; h *= 16777619; } }
            name = $"{stem}_{h:x8}";
        }
        return System.IO.Path.Combine(BaseDir, RootName, name);
    }

    /// <summary>새 XML 원본의 경로 — <b>부를 때마다 다른 이름</b>. 만든 때의 저장 도장을 목록에 적어 둔다(«만든 뒤로 저장이 없었다»를 가리는 값).</summary>
    public static string NewPath(string dwgPath, string kind)
    {
        string dir = DirFor(dwgPath);
        string name = $"{kind}_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}_{System.Guid.NewGuid().ToString("N").Substring(0, 6)}.xml";
        if (!string.IsNullOrEmpty(dwgPath))
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(dir, ListName), $"{name}\t{SaveStamp(dwgPath)}\t0\t0\n"); }
            catch { }   // 못 적으면 «만든 때 모름» — 늦게 지우는 쪽이라 해가 없다
        }
        return System.IO.Path.Combine(dir, name);
    }

    /// <summary>안 물린 XML 원본을 지워도 되나 — <b>저장된 도면이 아직 물고 있을 수 있으면 안 지운다</b>.
    /// <list type="bullet">
    /// <item>«안 물림»을 <b>처음 본 때부터</b> 20분이 안 됐으면 둔다(되돌리기 여유).</item>
    /// <item>지금의 저장 도장을 못 읽었으면 둔다.</item>
    /// <item><b>만든 뒤로 저장이 없었으면</b>(만든 때의 도장 = 지금 도장) 지운다 — 저장된 도면은 이 파일이 생기기 전 것이라 물 수 없다.</item>
    /// <item><b>«안 물림»을 본 뒤에 저장했으면</b>(지금 도장 &gt; 그때 도장) 지운다 — 저장된 도면도 안 문다. 도장이 뒤로 갔으면(백업을 되살림) 둔다.</item>
    /// </list>
    /// <para>★둘째 판단은 «안 물림을 본 때부터 저장 때까지 계속 안 물려 있었다»가 참이어야 맞다. 저장 안 하고 닫으면 다시 연 도면이 그 파일을 다시 문다 —
    /// 그래서 <b>면을 지우는 우리 명령은 들머리에서(지우기 전에) <see cref="Tidy"/>를 한 번 부른다</b>: 물린 것으로 보이면 «안 물림» 기록이 지워져 처음부터 다시 센다(검토 1012 중간 1).
    /// 기한만으로 지우는 길(«30일 지나면»)은 두지 않는다 — 그 길은 저장된 도면이 무는 파일을 지울 수 있다.</para>
    /// <para>못 막는 길(느낌표만 붙는다 · 모양은 그대로): 되돌리기(Ctrl+Z)로 20분 넘은 옛 면을 되살림 · 손으로 면을 지운 뒤 저장 없이 닫음 · 탐색기로 복사하거나 이름을 바꾸거나 옮긴 도면 ·
    /// 백업(.bak) 되살리기 · 다른 PC에서 열기 · 같은 도면을 한 번 더 열어 거기서 합성.</para></summary>
    internal static bool Deletable(System.DateTime nowUtc, long seenUtc, long savedNow, long madeStamp, long seenStamp)
    {
        if (seenUtc <= 0) return false;
        if (nowUtc - new System.DateTime(seenUtc, System.DateTimeKind.Utc) < UndoGrace) return false;
        if (savedNow == 0) return false;
        if (madeStamp != 0 && madeStamp == savedNow) return true;
        if (seenStamp != 0 && savedNow > seenStamp) return true;
        return false;
    }

    /// <summary>이 경로가 <b>우리가 쓴</b> XML 원본인가 — 폴더 줄기에 <c>DH_XML</c>이 있거나(새 판) 파일 이름이 옛 판의 고정 이름이다. 다른 PC에서 만든 도면도 같은 꼴이다.</summary>
    internal static bool IsOurs(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;
            string fn = System.IO.Path.GetFileName(path);
            foreach (string l in Legacy) if (string.Equals(fn, l, System.StringComparison.OrdinalIgnoreCase)) return true;
            string? d = System.IO.Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(d))
            {
                if (string.Equals(System.IO.Path.GetFileName(d), RootName, System.StringComparison.OrdinalIgnoreCase)) return true;
                d = System.IO.Path.GetDirectoryName(d);
            }
        }
        catch { }
        return false;
    }

    /// <summary>이 도면 폴더에서 안 물린 XML 원본을 치운다(<see cref="Deletable"/>) · 옛 판의 고정 이름 파일을 물린다(<see cref="RetireLegacy"/>).
    /// <paramref name="used"/> = 지금 도면의 면이 물고 있는 파일들(전체 경로) — <b>물린 파일은 절대 안 지운다</b>(물린 것으로 보이면 «안 물림» 기록도 지운다).
    /// 한 줄 요약 — 폴더에 원본이 있으면 늘 낸다(물린 수가 맞는지 로그로 보이게 — 검토 1012 중간 3) · 아무것도 없으면 빈 문자열.
    /// <para>이름 없는 새 도면은 폴더를 함께 써 «남이 물고 있나»를 모른다 — 지금 열린 도면이 무는 것 · 저장한 도면이 문다고 적힌 것(<see cref="ClaimSaved"/>)을 빼고
    /// 만든 지 1일 넘은 것만 지운다(★v103.1.7 — 종전 30일 · 저장한 도면도 30일 뒤 원본을 잃을 수 있었다 · 검토 1012 중간 2).</para>
    /// <para>★[v103.1.7] <b>다른 도면이 문다고 적힌 파일</b>(그 폴더의 <c>_물린도면.tsv</c>)은 그 도면이 있는 한(없어진 지 30일까지) 안 지운다 —
    /// 이름 없이 만들고 저장한 도면 · «다른 이름으로 저장»한 도면이 옛 폴더의 원본을 문다.</para></summary>
    public static string Tidy(string dwgPath, IEnumerable<string> used) => TidyAll(dwgPath, used, false);

    /// <summary>★[v103.1.7] <see cref="Tidy"/> — <paramref name="used"/>는 <b>지금 열린 모든 도면</b>이 무는 파일이어야 한다(이름 없는 도면끼리 폴더를 함께 쓴다).
    /// <paramref name="othersUnknown"/> = 다른 열린 도면의 면을 못 읽었다 — 그러면 아무것도 지우지 않는다(무엇이 물렸는지 모른다 — 늦게 지우는 쪽).</summary>
    public static string TidyAll(string dwgPath, IEnumerable<string> used, bool othersUnknown)
    {
        int del = 0, kept = 0, wait = 0, claimed = 0; long bytes = 0; var bad = new List<string>();
        try
        {
            var usedSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string u in used) { try { usedSet.Add(System.IO.Path.GetFullPath(u)); } catch { usedSet.Add(u); } }
            bool named = !string.IsNullOrEmpty(dwgPath);
            string own = DirFor(dwgPath);
            var now = System.DateTime.UtcNow;
            long saved = SaveStamp(dwgPath);
            var rows = named ? Load(own) : new Dictionary<string, Row>(System.StringComparer.OrdinalIgnoreCase);
            bool dirty = false;
            // ★[v103.1.7] 다른 저장된 도면이 문다고 적힌 파일 — 도면이 없어졌으면 없어진 때를 적고 30일 뒤 놓는다
            var claims = LoadClaims(own); bool dirtyC = false;
            foreach (var c in claims.ToList())
            {
                // ★[v103.1.7 · 검토 중간 4] 뿌리가 안 보이면(끊긴 네트워크 드라이브) «있다»로 친다 — 없어진 때를 적지도 늘리지도 않는다
                bool exists = true; try { exists = !RootReachable(c.Dwg) || System.IO.File.Exists(c.Dwg); } catch { }
                if (exists) { if (c.Missing != 0 && RootReachable(c.Dwg)) { c.Missing = 0; dirtyC = true; } continue; }
                if (c.Missing <= 0 || c.Missing > System.DateTime.MaxValue.Ticks) { c.Missing = now.Ticks; dirtyC = true; continue; }
                if (now - new System.DateTime(c.Missing, System.DateTimeKind.Utc) >= MissingKeep) { claims.Remove(c); dirtyC = true; }
            }
            var claimedSet = new HashSet<string>(claims.Select(c => c.File), System.StringComparer.OrdinalIgnoreCase);
            var here = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            long ruleStart = named ? 0 : RuleStart(own);
            var grace = _unnamedGraceForTest ?? UndoGrace;
            foreach (string f in System.IO.Directory.GetFiles(own, "*.xml"))
            {
                string nm = System.IO.Path.GetFileName(f);
                here.Add(nm);
                try
                {
                    string full = System.IO.Path.GetFullPath(f);
                    bool isUsed = usedSet.Contains(full);
                    bool ok;
                    if (!isUsed && claimedSet.Contains(nm)) { claimed++; continue; }   // 다른 저장된 도면이 문다
                    if (!named)
                    {
                        if (isUsed) { kept++; lock (_unnamedSeen) _unnamedSeen.Remove(full); continue; }
                        if (othersUnknown) { wait++; continue; }
                        // ★[v103.1.7 · 검토 낮음 1] 되돌리기 여유 — «안 물림»을 처음 본 지 20분이 안 됐으면 둔다(이 프로세스 기억 · Civil을 다시 켜면 처음부터 센다 — 늦게 지우는 쪽)
                        long seen;
                        lock (_unnamedSeen) { if (!_unnamedSeen.TryGetValue(full, out seen)) { seen = now.Ticks; _unnamedSeen[full] = seen; } }
                        if (now - new System.DateTime(seen, System.DateTimeKind.Utc) < grace) { wait++; continue; }
                        // ★[v103.1.7 · 검토 중간 1] 1일 규칙을 처음 쓴 때보다 먼저 쓴 파일은 종전 30일(그때 저장한 도면에는 «물림» 기록이 없다)
                        var mt = System.IO.File.GetLastWriteTimeUtc(f);
                        ok = now - mt >= (mt.Ticks < ruleStart ? LegacyUnnamedKeep : UnnamedKeep);
                        if (ok) lock (_unnamedSeen) _unnamedSeen.Remove(full);
                    }
                    else
                    {
                        rows.TryGetValue(nm, out Row? r);
                        // 물렸다 — 되돌리기로 다시 물린 것이면 «안 물림» 기록을 지운다(다음에 안 물리면 그때부터 다시 센다)
                        if (isUsed) { kept++; if (r != null && r.Seen != 0) { r.Seen = 0; r.SeenStamp = 0; dirty = true; } continue; }
                        if (r == null) { r = new Row(); rows[nm] = r; dirty = true; }      // 목록에 없는 파일 — 만든 때 모름
                        if (r.Seen == 0) { r.Seen = now.Ticks; r.SeenStamp = saved; dirty = true; }
                        ok = !othersUnknown && Deletable(now, r.Seen, saved, r.Made, r.SeenStamp);
                    }
                    if (!ok) { wait++; continue; }
                    long len = new System.IO.FileInfo(f).Length;
                    System.IO.File.Delete(f);
                    del++; bytes += len;
                    if (rows.Remove(nm)) dirty = true;
                    here.Remove(nm);
                }
                catch (System.Exception ex) { bad.Add($"{nm}({ex.GetType().Name})"); }
            }
            foreach (string k in new List<string>(rows.Keys)) if (!here.Contains(k)) { rows.Remove(k); dirty = true; }
            if (named && dirty) Save(own, rows);
            if (claims.RemoveAll(c => !here.Contains(c.File)) > 0) dirtyC = true;   // 없어진 파일의 «물림»은 걷는다
            if (dirtyC) SaveClaims(own, claims);
        }
        catch (System.Exception ex) { bad.Add(ex.GetType().Name + ": " + ex.Message); }
        string legacy = RetireLegacy();
        if (del + kept + wait + claimed == 0 && bad.Count == 0 && legacy.Length == 0) return "";
        return $"XML 원본 — 물린 것 {kept} · 기다리는 것 {wait}(되돌리기 여유 · 저장 전{(othersUnknown ? " · 다른 열린 도면을 못 읽어 안 지움" : "")}) · 지운 것 {del}({bytes / 1048576.0:F1}MB)"
             + (claimed > 0 ? $" · 다른 저장된 도면이 무는 것 {claimed}(둔다)" : "")
             + (legacy.Length > 0 ? " · " + legacy : "")
             + (bad.Count > 0 ? " · ⚠못 다룸: " + string.Join(" · ", bad) : "");
    }

    // ── ★★[v103.1.7 · 검토 1012 중간 2 · JACK 1014 «나중에 저장한 도면이 쓰는 원본은 지키게»] «다른 도면이 문다» 기록 ─────────

    /// <summary>«다른 도면이 문다» 한 줄.</summary>
    sealed class ClaimRow
    {
        public string File = "", Dwg = "";
        public long Stamp;       // 적은 때(저장 직후)의 그 도면 도장
        public long Seen;        // 그 도면이 «안 문다»를 처음 본 때(이 PC의 UTC)
        public long SeenStamp;   // 그때의 그 도면 도장
        public long Missing;     // 그 도면 파일이 없어진 것을 처음 본 때(UTC)
    }

    static List<ClaimRow> LoadClaims(string dir)
    {
        var l = new List<ClaimRow>();
        try
        {
            string p = System.IO.Path.Combine(dir, ClaimName);
            if (!System.IO.File.Exists(p)) return l;
            foreach (string line in System.IO.File.ReadAllLines(p))
            {
                string[] a = line.Split('\t');
                if (a.Length < 6 || a[0].Length == 0 || a[1].Length == 0) continue;
                if (!long.TryParse(a[2], out long st) || !long.TryParse(a[3], out long seen) || !long.TryParse(a[4], out long seenSt) || !long.TryParse(a[5], out long miss)) continue;
                if (st < 0 || seen < 0 || seenSt < 0 || miss < 0 || seen > System.DateTime.MaxValue.Ticks || miss > System.DateTime.MaxValue.Ticks) continue;
                l.RemoveAll(c => c.File.Equals(a[0], System.StringComparison.OrdinalIgnoreCase) && c.Dwg.Equals(a[1], System.StringComparison.OrdinalIgnoreCase));
                l.Add(new ClaimRow { File = a[0], Dwg = a[1], Stamp = st, Seen = seen, SeenStamp = seenSt, Missing = miss });
            }
        }
        catch { }
        return l;
    }

    /// <summary>목록을 쓴다 — 됐으면 true(★[v103.1.7 · 검토 낮음 7] 못 쓰면 부른 쪽이 «적었다»고 하지 않게).</summary>
    static bool SaveClaims(string dir, List<ClaimRow> l)
    {
        try
        {
            string p = System.IO.Path.Combine(dir, ClaimName);
            if (l.Count == 0) { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); return true; }
            var sb = new System.Text.StringBuilder();
            foreach (var c in l) sb.Append(c.File).Append('\t').Append(c.Dwg).Append('\t').Append(c.Stamp).Append('\t').Append(c.Seen).Append('\t').Append(c.SeenStamp).Append('\t').Append(c.Missing).Append('\n');
            string tmp = p + ".tmp";
            System.IO.File.WriteAllText(tmp, sb.ToString());
            System.IO.File.Move(tmp, p, true);
            return true;
        }
        catch { return false; }   // 못 적으면 다음 저장 때 다시 적는다 — 그 사이 지워질 수 있는 것은 이름 없는 도면 폴더의 1일 넘은 파일뿐이다
    }

    /// <summary>★도면을 <b>저장한 직후</b> — 그 도면의 면이 무는 XML 원본 가운데 <b>제 폴더 밖</b>의 것(이름 없이 만들고 저장한 도면 · «다른 이름으로 저장»한 도면이 옛 폴더의 원본을 문다)을
    /// 그 파일이 있는 폴더의 <c>_물린도면.tsv</c>에 «이 도면이 문다»로 적는다 — 그 폴더의 정리가 기한과 상관없이 지킨다(<see cref="TidyAll"/>).
    /// 놓는 것은 그 도면 자신의 정리(<see cref="ReleaseClaims"/>) · 그 도면 파일이 30일 넘게 없을 때. 한 줄 요약(한 일이 없으면 빈 문자열).</summary>
    public static string ClaimSaved(string dwgPath, IEnumerable<string> files)
    {
        if (string.IsNullOrEmpty(dwgPath) || !dwgPath.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase)) return "";   // 자동 저장(.sv$) 등은 안 센다
        int n = 0; var bad = new List<string>();
        try
        {
            string full = System.IO.Path.GetFullPath(dwgPath);
            string own = FolderPath(full);
            string rootDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseDir, RootName));
            long stamp = SaveStamp(full);
            var byDir = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string f0 in files)
            {
                string f; try { f = System.IO.Path.GetFullPath(f0); } catch { continue; }
                string? d = System.IO.Path.GetDirectoryName(f);
                if (string.IsNullOrEmpty(d)) continue;
                if (!string.Equals(System.IO.Path.GetDirectoryName(d), rootDir, System.StringComparison.OrdinalIgnoreCase)) continue;   // DH_XML 바로 밑 폴더의 파일만(우리가 쓴 원본)
                if (string.Equals(d, own, System.StringComparison.OrdinalIgnoreCase)) continue;                                          // 제 폴더 — 제 목록(_원본목록)이 다룬다
                if (!System.IO.Directory.Exists(d)) continue;
                if (!byDir.TryGetValue(d, out var l)) byDir[d] = l = new List<string>();
                l.Add(System.IO.Path.GetFileName(f));
            }
            foreach (var kv in byDir)
            {
                var claims = LoadClaims(kv.Key);
                int k = 0;
                foreach (string fn in kv.Value.Distinct(System.StringComparer.OrdinalIgnoreCase))
                {
                    claims.RemoveAll(c => c.File.Equals(fn, System.StringComparison.OrdinalIgnoreCase) && c.Dwg.Equals(full, System.StringComparison.OrdinalIgnoreCase));
                    claims.Add(new ClaimRow { File = fn, Dwg = full, Stamp = stamp });
                    k++;
                }
                if (SaveClaims(kv.Key, claims)) n += k;
                else bad.Add($"{System.IO.Path.GetFileName(kv.Key)}의 물림 목록을 못 썼다({k}개 — 1일 뒤 지워질 수 있다)");
            }
        }
        catch (System.Exception ex) { bad.Add(ex.GetType().Name + ": " + ex.Message); }
        if (n == 0 && bad.Count == 0) return "";
        return (n > 0 ? $"저장한 도면이 제 폴더 밖의 XML 원본 {n}개를 문다 — 그 폴더에 «이 도면이 문다»로 적었다(기한과 상관없이 지킨다)" : "")
             + (bad.Count > 0 ? " · ⚠못 적음: " + string.Join(" · ", bad) : "");
    }

    /// <summary>★★[v103.1.7 · 검토 낮음 8] 도면을 <b>저장한 직후</b> — 그 도면이 지금 무는 <b>제 폴더</b>의 원본은 «안 물림» 기록을 지운다(저장된 도면이 문다).
    /// <para>종전 틈: 다시 변환(«안 물림» 기록) → Ctrl+Z로 옛 면을 되살림 → 저장하면, 저장된 도면은 그 원본을 무는데 기록은 «안 물림을 본 뒤 저장했다»라 다음 정리가 지웠다(느낌표).</para>
    /// 한 줄 요약(한 일이 없으면 빈 문자열).</summary>
    public static string MarkSaved(string dwgPath, IEnumerable<string> files)
    {
        if (string.IsNullOrEmpty(dwgPath) || !dwgPath.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase)) return "";
        int reset = 0;
        try
        {
            string own = FolderPath(System.IO.Path.GetFullPath(dwgPath));
            if (!System.IO.Directory.Exists(own)) return "";
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string f in files) { try { set.Add(System.IO.Path.GetFullPath(f)); } catch { set.Add(f); } }
            var rows = Load(own); bool dirty = false;
            foreach (var kv in rows)
                if (kv.Value.Seen != 0 && set.Contains(System.IO.Path.Combine(own, kv.Key))) { kv.Value.Seen = 0; kv.Value.SeenStamp = 0; dirty = true; reset++; }
            if (dirty) Save(own, rows);
        }
        catch { }
        return reset > 0 ? $"저장한 도면이 무는 제 폴더 원본 {reset}개의 «안 물림» 기록을 지웠다(저장된 도면이 문다 — 되돌리기 뒤 저장)" : "";
    }

    /// <summary>★이 도면이 <b>남의 폴더에 적어 둔 «물림»</b>을 손본다 — 지금 그 파일을 무는가(<paramref name="used"/> = 이 도면의 면이 무는 파일) ·
    /// «안 문다»를 본 뒤 이 도면을 저장했나(<see cref="Deletable"/>와 같은 잣대 — 만든 때 도장은 안 쓴다: 적은 때 그 도면은 그 파일을 물었다). 한 줄 요약(놓은 것이 없으면 빈 문자열).</summary>
    public static string ReleaseClaims(string dwgPath, IEnumerable<string> used)
    {
        if (string.IsNullOrEmpty(dwgPath)) return "";
        int rel = 0;
        try
        {
            string full = System.IO.Path.GetFullPath(dwgPath);
            var usedSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string u in used) { try { usedSet.Add(System.IO.Path.GetFullPath(u)); } catch { usedSet.Add(u); } }
            string rootDir = System.IO.Path.Combine(BaseDir, RootName);
            if (!System.IO.Directory.Exists(rootDir)) return "";
            long saved = SaveStamp(full);
            var now = System.DateTime.UtcNow;
            foreach (string d in System.IO.Directory.GetDirectories(rootDir))
            {
                if (!System.IO.File.Exists(System.IO.Path.Combine(d, ClaimName))) continue;
                var claims = LoadClaims(d); bool dirty = false; int relHere = 0;
                foreach (var c in claims.ToList())
                {
                    if (!c.Dwg.Equals(full, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (usedSet.Contains(System.IO.Path.Combine(d, c.File))) { if (c.Seen != 0) { c.Seen = 0; c.SeenStamp = 0; dirty = true; } continue; }
                    if (c.Seen == 0) { c.Seen = now.Ticks; c.SeenStamp = saved; dirty = true; continue; }
                    if (Deletable(now, c.Seen, saved, 0, c.SeenStamp)) { claims.Remove(c); relHere++; dirty = true; }
                }
                if (dirty && SaveClaims(d, claims)) rel += relHere;
            }
        }
        catch { }
        return rel > 0 ? $"이 도면이 남의 폴더에 적어 둔 «물림» {rel}개를 놓았다(안 문 채로 저장됨)" : "";
    }

    /// <summary>★[v103.1.7 · 검토 1012 낮음 5 — 미뤘던 것] <b>도면이 없어진 폴더</b>를 치운다 — 폴더의 <c>_도면.txt</c>가 가리키는 도면 파일이 30일 넘게 없고,
    /// 그 폴더의 파일을 문다고 적힌 다른 도면이 하나도 없을 때만(<c>_도면.txt</c>가 없는 폴더 — v103.1.5 · v103.1.6이 만든 것 — 는 누구 것인지 몰라 안 건드린다).
    /// <para>못 막는 길(느낌표만 · 모양은 그대로): 탐색기로 옮기거나 이름을 바꾼 도면을 30일 넘게 다시 저장하지 않음 · 30일 넘게 연결이 끊긴 네트워크 드라이브.</para>
    /// 한 줄 요약(한 일이 없으면 빈 문자열).</summary>
    public static string SweepOrphans() => SweepOrphansSkipping(null);

    /// <summary>★[v103.1.7 · 검토 중간 4] <see cref="SweepOrphans"/> — <paramref name="skipDirs"/>(지금 열린 도면의 면이 무는 원본 폴더들)는 건드리지 않는다
    /// (.dwg를 두 번 눌러 Civil을 켜 시작 때 이미 열린 도면 · 옮긴 뒤 연 도면). 뿌리가 안 보이는 도면(끊긴 네트워크 드라이브)은 «없어졌다»로 세지 않는다.
    /// Autodesk 형을 안 써서 시작 때 <b>뒤 실에서</b> 돌린다(네트워크 경로를 묻다 Civil이 멈춘 듯 보이지 않게).</summary>
    public static string SweepOrphansSkipping(ICollection<string>? skipDirs)
    {
        int del = 0; long bytes = 0; var bad = new List<string>();
        try
        {
            string rootDir = System.IO.Path.Combine(BaseDir, RootName);
            if (!System.IO.Directory.Exists(rootDir)) return "";
            var now = System.DateTime.UtcNow;
            var skip = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (skipDirs != null) foreach (string s in skipDirs) { try { skip.Add(System.IO.Path.GetFullPath(s).TrimEnd('\\')); } catch { } }
            foreach (string d in System.IO.Directory.GetDirectories(rootDir))
            {
                try
                {
                    if (System.IO.Path.GetFileName(d).Equals(UnnamedName, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (skip.Contains(System.IO.Path.GetFullPath(d).TrimEnd('\\'))) continue;
                    string note = System.IO.Path.Combine(d, DwgNoteName);
                    if (!System.IO.File.Exists(note)) continue;
                    var ln = System.IO.File.ReadAllLines(note);
                    string dwg = ln.Length > 0 ? ln[0].Trim() : "";
                    if (dwg.Length == 0) continue;
                    if (!RootReachable(dwg)) continue;                      // 뿌리가 안 보인다 — 판단 보류(없어진 때를 적지도 늘리지도 않는다)
                    if (System.IO.File.Exists(dwg))
                    {
                        if (ln.Length > 1 && ln[1].Trim().Length > 0) System.IO.File.WriteAllText(note, dwg + "\n");   // 돌아왔다 — 없어진 때를 지운다
                        continue;
                    }
                    long missing = ln.Length > 1 && long.TryParse(ln[1].Trim(), out long m) ? m : 0;
                    if (missing <= 0 || missing > System.DateTime.MaxValue.Ticks) { System.IO.File.WriteAllText(note, dwg + "\n" + now.Ticks + "\n"); continue; }
                    if (now - new System.DateTime(missing, System.DateTimeKind.Utc) < MissingKeep) continue;
                    bool claimedByLive = false;
                    foreach (var c in LoadClaims(d)) { try { if (!RootReachable(c.Dwg) || System.IO.File.Exists(c.Dwg)) { claimedByLive = true; break; } } catch { claimedByLive = true; break; } }
                    if (claimedByLive) continue;
                    foreach (var f in new System.IO.DirectoryInfo(d).GetFiles()) bytes += f.Length;
                    System.IO.Directory.Delete(d, true);
                    del++;
                }
                catch (System.Exception ex) { bad.Add($"{System.IO.Path.GetFileName(d)}({ex.GetType().Name})"); }
            }
        }
        catch (System.Exception ex) { bad.Add(ex.GetType().Name); }
        if (del == 0 && bad.Count == 0) return "";
        return $"도면이 30일 넘게 없는 XML 원본 폴더 {del}개를 지웠다({bytes / 1048576.0:F1}MB)" + (bad.Count > 0 ? " · ⚠못 다룸: " + string.Join(" · ", bad) : "");
    }

    /// <summary>★[v103.1.7] 점검 보고용 — 이 도면 폴더 · 이름 없는 도면 폴더 · 전체의 파일 수와 크기.</summary>
    public static string Usage(string dwgPath)
    {
        static (int N, long B) Of(string d)
        {
            try
            {
                if (!System.IO.Directory.Exists(d)) return (0, 0);
                var fs = new System.IO.DirectoryInfo(d).GetFiles("*.xml", System.IO.SearchOption.AllDirectories);
                return (fs.Length, fs.Sum(f => f.Length));
            }
            catch { return (-1, 0); }
        }
        try
        {
            var a = string.IsNullOrEmpty(dwgPath) ? (0, 0L) : Of(FolderPath(dwgPath));
            var u = Of(FolderPath(""));
            var t = Of(System.IO.Path.Combine(BaseDir, RootName));
            return $"이 도면 폴더 {a.Item1}개 {a.Item2 / 1048576.0:F1}MB · 이름 없는 도면 폴더 {u.N}개 {u.B / 1048576.0:F1}MB · 전체 {t.N}개 {t.B / 1048576.0:F1}MB({System.IO.Path.Combine(BaseDir, RootName)})";
        }
        catch (System.Exception ex) { return "못 셈(" + ex.GetType().Name + ")"; }
    }

    /// <summary>이 도면 폴더의 «안 물림» 기록을 전부 버린다 — <b>지금 무엇이 물려 있는지 볼 수 없을 때</b>(정의를 못 읽은 면이 있다) 부른다.
    /// 못 본 채로 두면 같은 명령의 뒤 정리가 낡은 기록으로 지울 수 있다(검토 1012 확인 낮음) — 버리면 다음 정리가 처음부터 다시 센다(늦게 지우는 쪽).</summary>
    public static void ForgetSeen(string dwgPath)
    {
        try
        {
            if (string.IsNullOrEmpty(dwgPath)) return;
            string own = DirFor(dwgPath);
            var rows = Load(own);
            bool dirty = false;
            foreach (var r in rows.Values) if (r.Seen != 0) { r.Seen = 0; r.SeenStamp = 0; dirty = true; }
            if (dirty) Save(own, rows);
        }
        catch { }
    }

    /// <summary>옛 판의 고정 이름 파일을 «.옛판.xml»로 <b>이름을 바꾼다</b> — 옛 판이 지은 면이 그 파일을 «없는 파일»로 보게.
    /// <para>그 파일은 실행마다 덮어써서 대부분의 옛 면에게는 이미 «남의 내용»이다 — 그대로 두면 그 면을 재작성(굳힌 면은 스냅샷 재작성)할 때 말없이 그 내용으로 바뀐다(1012 실측).
    /// 없는 파일이면 재작성은 «파일 없음»으로 끝나고 삼각형은 남는다(실측) — 느낌표는 붙지만 틀린 모양이 되는 길이 없다. 그 면은 다시 지을 때(계획부지 생성 · 옹벽 합성) 새 방식이 된다.</para>
    /// 한 줄 요약(한 일이 없으면 빈 문자열).</summary>
    public static string RetireLegacy()
    {
        int n = 0; var bad = new List<string>();
        try
        {
            string dir = BaseDir;
            foreach (string legacy in Legacy)
            {
                try
                {
                    string p = System.IO.Path.Combine(dir, legacy);
                    if (!System.IO.File.Exists(p)) continue;
                    System.IO.File.Move(p, System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(legacy) + ".옛판.xml"), true);
                    n++;
                }
                catch (System.Exception ex) { bad.Add($"{legacy}({ex.GetType().Name})"); }
            }
        }
        catch (System.Exception ex) { bad.Add(ex.GetType().Name); }
        if (n == 0 && bad.Count == 0) return "";
        return (n > 0 ? $"옛 판 고정 이름 파일 {n}개를 «.옛판.xml»로 이름 바꿈" : "옛 판 고정 이름 파일")
             + (bad.Count > 0 ? " · ⚠이름 못 바꿈: " + string.Join(" · ", bad) : "");
    }

    /// <summary>폴더의 목록(파일 이름 → 만든 때의 도장 · 안 물림을 본 때 · 그때의 도장). 같은 이름이 여러 줄이면 뒤 줄이 이긴다.
    /// 못 읽은 줄은 버린다(= 모름 — 늦게 지우는 쪽).</summary>
    static Dictionary<string, Row> Load(string dir)
    {
        var d = new Dictionary<string, Row>(System.StringComparer.OrdinalIgnoreCase);
        try
        {
            string p = System.IO.Path.Combine(dir, ListName);
            if (!System.IO.File.Exists(p)) return d;
            foreach (string line in System.IO.File.ReadAllLines(p))
            {
                string[] a = line.Split('\t');
                if (a.Length < 4 || a[0].Length == 0) continue;
                if (!long.TryParse(a[1], out long made) || !long.TryParse(a[2], out long seen) || !long.TryParse(a[3], out long seenStamp)) continue;
                if (made < 0 || seen < 0 || seenStamp < 0 || seen > System.DateTime.MaxValue.Ticks) continue;
                d[a[0]] = new Row { Made = made, Seen = seen, SeenStamp = seenStamp };
            }
        }
        catch { }
        return d;
    }

    static void Save(string dir, Dictionary<string, Row> d)
    {
        try
        {
            string p = System.IO.Path.Combine(dir, ListName);
            if (d.Count == 0) { if (System.IO.File.Exists(p)) System.IO.File.Delete(p); return; }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('\t').Append(kv.Value.Made).Append('\t').Append(kv.Value.Seen).Append('\t').Append(kv.Value.SeenStamp).Append('\n');
            // 임시 파일에 쓰고 바꿔 끼운다 — 쓰다 죽어 숫자가 잘린 줄이 «작은 도장»으로 읽히지 않게(검토 1012 낮음 8)
            string tmp = p + ".tmp";
            System.IO.File.WriteAllText(tmp, sb.ToString());
            System.IO.File.Move(tmp, p, true);
        }
        catch { }   // 못 적으면 다음에도 «지금 처음 본 것» — 늦게 지우는 쪽이라 해가 없다
    }
}
