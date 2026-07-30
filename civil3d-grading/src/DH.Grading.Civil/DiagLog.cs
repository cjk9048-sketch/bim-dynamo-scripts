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

    /// <summary>새로 시작(기존 File.WriteAllText 대체 — DoGrade 시작 시 1회).</summary>
    public static void Reset(string text)
    {
        try { System.IO.File.WriteAllText(Resolved, text); } catch { }
    }
}
