namespace DH.Grading.Civil;

/// <summary>진단 로그(DHGRADE_진단.log) 경로 해석 — 배포 대응(JACK 0728).
/// 개발 PC(저장소 폴더 존재)면 기존 경로 그대로(작업 흐름 유지), 배포 PC면
/// %LOCALAPPDATA%\DHGrading\ 에 기록한다(예전엔 하드코딩 경로라 배포 PC에선 조용히 로그가 안 남았음).
/// 모든 기록은 실패해도 조용히 무시(로그가 기능을 깨면 안 됨).</summary>
public static class DiagLog
{
    private static readonly string Resolved = Resolve();

    private static string Resolve()
    {
        const string devDir = @"C:\Users\user\Desktop\AI\civil3d-grading";
        try { if (System.IO.Directory.Exists(devDir)) return System.IO.Path.Combine(devDir, "DHGRADE_진단.log"); }
        catch { }
        try
        {
            string baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(baseDir))
            {
                string dir = System.IO.Path.Combine(baseDir, "DHGrading");
                System.IO.Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "DHGRADE_진단.log");
            }
        }
        catch { }
        return "DHGRADE_진단.log";   // 최후 폴백 — 작업 폴더 상대경로(크래시 방지 우선)
    }

    /// <summary>로그 파일 전체 경로(안내 문구 표기용).</summary>
    public static string FilePath => Resolved;

    /// <summary>덧붙여 기록(기존 File.AppendAllText 대체).</summary>
    public static void Append(string text)
    {
        try { System.IO.File.AppendAllText(Resolved, text); } catch { }
    }

    /// <summary>★★[JACK 0820] <b>다음 Reset을 살아남는</b> 기록.
    /// <para>변환 명령(옹벽·사면)은 자기 판단을 로그에 남긴 <b>직후</b> 재생성(DoGrade)을 부르는데,
    /// DoGrade는 첫 줄에서 <see cref="Reset"/>으로 파일을 새로 쓴다 — 그래서 <b>정작 원인을 가를 줄이
    /// 매번 통째로 지워졌다</b>(0820 실측: '클릭한 선' 줄이 로그에 아예 없었다).
    /// 여기 담아 두면 Reset이 머리말 바로 뒤에 다시 붙여 준다.</para></summary>
    public static void AppendCarry(string text)
    {
        try { Carry.Append(text); } catch { }
        Append(text);
    }

    private static readonly System.Text.StringBuilder Carry = new();

    /// <summary>새로 시작(기존 File.WriteAllText 대체 — DoGrade 시작 시 1회).
    /// <see cref="AppendCarry"/>로 남긴 줄은 머리말 뒤에 이어 붙여 살린다.</summary>
    public static void Reset(string text)
    {
        string carry = "";
        try { if (Carry.Length > 0) { carry = Carry.ToString(); Carry.Clear(); } } catch { }
        Archive();
        try { System.IO.File.WriteAllText(Resolved, text + carry); } catch { }
    }

    /// <summary>★[JACK 0904] <b>덮어쓰기 전에 직전 판을 옆에 남긴다.</b>
    /// <para>고침 전/후를 나란히 놓아야 판정이 되는 국면이 계속 나오는데(§67 A/B/A, §68, 0904 이음매),
    /// 로그가 실행마다 통째로 덮여 <b>비교 자료가 사라졌다</b>(0904 실측: 11:02 판 성토 체인 덤프 복구 불가).
    /// 시각을 붙여 <c>진단이력</c> 폴더로 옮기고 최근 20판만 남긴다 — 용량은 판당 수십 KB다.</para></summary>
    private static void Archive()
    {
        try
        {
            if (!System.IO.File.Exists(Resolved)) return;
            string dir = System.IO.Path.GetDirectoryName(Resolved) ?? ".";
            string hist = System.IO.Path.Combine(dir, "진단이력");
            System.IO.Directory.CreateDirectory(hist);
            string stamp = System.IO.File.GetLastWriteTime(Resolved).ToString("yyyyMMdd_HHmmss");
            // 같은 초에 두 번 돌아도 안 덮이게 — 이미 있으면 그대로 둔다(먼저 것이 원본).
            string dst = System.IO.Path.Combine(hist, $"DHGRADE_진단_{stamp}.log");
            if (!System.IO.File.Exists(dst)) System.IO.File.Copy(Resolved, dst);
            // 곁다리 덤프도 같은 시각으로 함께 — 체인·클립링은 이 로그와 짝이라 따로 두면 못 맞춘다.
            foreach (var side in new[] { "DHXSEC_진단.log", "DHXSEC_진단_절토.log", "DHXSEC_진단_성토.log" })
            {
                string src = System.IO.Path.Combine(dir, side);
                if (!System.IO.File.Exists(src)) continue;
                string d2 = System.IO.Path.Combine(hist, System.IO.Path.GetFileNameWithoutExtension(side) + "_" + stamp + ".log");
                if (!System.IO.File.Exists(d2)) System.IO.File.Copy(src, d2);
            }
            // 오래된 것부터 정리 — 최근 20판(짝 파일 포함이라 파일 수는 그 몇 배)만.
            var files = new System.IO.DirectoryInfo(hist).GetFiles("DHGRADE_진단_*.log");
            if (files.Length > 20)
            {
                System.Array.Sort(files, (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
                for (int i = 0; i < files.Length - 20; i++)
                {
                    string tag = System.IO.Path.GetFileNameWithoutExtension(files[i].Name).Replace("DHGRADE_진단_", "");
                    foreach (var f in new System.IO.DirectoryInfo(hist).GetFiles("*_" + tag + ".log"))
                        try { f.Delete(); } catch { }
                }
            }
        }
        catch { }
    }
}
