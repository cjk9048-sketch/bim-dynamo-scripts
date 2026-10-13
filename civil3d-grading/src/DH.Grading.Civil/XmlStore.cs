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
    /// <summary>이름 없는 도면이 함께 쓰는 폴더의 파일을 치우는 한도(만든 지).</summary>
    static readonly System.TimeSpan UnnamedKeep = System.TimeSpan.FromDays(30);

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
        string dir = System.IO.Path.Combine(BaseDir, RootName, name);
        System.IO.Directory.CreateDirectory(dir);
        return dir;
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
    /// <para>이름 없는 새 도면은 폴더를 함께 써 «남이 물고 있나»를 모른다 — 만든 지 30일 넘은 것만 지운다
    /// (그래서 이름 없는 도면에서 합성한 뒤 저장하고 30일 넘게 다시 생성하지 않은 도면은 원본을 잃을 수 있다 — 느낌표만 · 검토 1012 중간 2 · 다음 판).</para></summary>
    public static string Tidy(string dwgPath, IEnumerable<string> used)
    {
        int del = 0, kept = 0, wait = 0; long bytes = 0; var bad = new List<string>();
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
            var here = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string f in System.IO.Directory.GetFiles(own, "*.xml"))
            {
                string nm = System.IO.Path.GetFileName(f);
                here.Add(nm);
                try
                {
                    bool isUsed = usedSet.Contains(System.IO.Path.GetFullPath(f));
                    bool ok;
                    if (!named)
                    {
                        if (isUsed) { kept++; continue; }
                        ok = now - System.IO.File.GetLastWriteTimeUtc(f) >= UnnamedKeep;
                    }
                    else
                    {
                        rows.TryGetValue(nm, out Row? r);
                        // 물렸다 — 되돌리기로 다시 물린 것이면 «안 물림» 기록을 지운다(다음에 안 물리면 그때부터 다시 센다)
                        if (isUsed) { kept++; if (r != null && r.Seen != 0) { r.Seen = 0; r.SeenStamp = 0; dirty = true; } continue; }
                        if (r == null) { r = new Row(); rows[nm] = r; dirty = true; }      // 목록에 없는 파일 — 만든 때 모름
                        if (r.Seen == 0) { r.Seen = now.Ticks; r.SeenStamp = saved; dirty = true; }
                        ok = Deletable(now, r.Seen, saved, r.Made, r.SeenStamp);
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
        }
        catch (System.Exception ex) { bad.Add(ex.GetType().Name + ": " + ex.Message); }
        string legacy = RetireLegacy();
        if (del + kept + wait == 0 && bad.Count == 0 && legacy.Length == 0) return "";
        return $"XML 원본 — 물린 것 {kept} · 기다리는 것 {wait}(되돌리기 여유 · 저장 전) · 지운 것 {del}({bytes / 1048576.0:F1}MB)"
             + (legacy.Length > 0 ? " · " + legacy : "")
             + (bad.Count > 0 ? " · ⚠못 다룸: " + string.Join(" · ", bad) : "");
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
