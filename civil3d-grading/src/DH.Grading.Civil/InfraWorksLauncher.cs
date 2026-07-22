using System.Diagnostics;
using System.Reflection;

namespace DH.Grading.Civil;

/// <summary>InfraWorks 원스톱 실행(JACK 0722) — 애드인 번들에 동봉된 템플릿 모델을 **새 이름으로 복사**하고 InfraWorks로 연다.
/// 템플릿 데이터소스는 <c>C:\DHInfra\지형.xml·옹벽3D.dwg</c> 고정경로를 참조하므로, DHINFRA가 그 폴더에 파일을 내보낸 뒤
/// 복사·열기만 하면 어느 PC든 동작(portable). 복사본은 매 실행마다 새 모델(타임스탬프) = "새 프로젝트 매번"(JACK).
/// 열린 뒤 사용자가 지형 Refresh + 옹벽 Reimport(InfraWorks가 밖에서의 자동 임포트를 지원 안 함 — 실측 확정).</summary>
public static class InfraWorksLauncher
{
    private const string InfraWorksExe = @"C:\Program Files\Autodesk\InfraWorks\InfraWorks.exe";
    private const string TemplateName = "DHInfra_Template";

    /// <summary>번들 템플릿(Contents\template)을 InfraWorks 모델 폴더에 새 이름으로 복사, **소스 경로를 실행 폴더로
    /// 재작성**(DHInfraAuto retarget — 실행별 격리, JACK 0722)한 뒤 InfraWorks 실행. 반환=안내 메시지.</summary>
    public static string CopyTemplateAndOpen(string runFolder)
    {
        string bundleDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        string tmplSqlite = System.IO.Path.Combine(bundleDir, "template", TemplateName + ".sqlite");
        string tmplFiles = System.IO.Path.Combine(bundleDir, "template", TemplateName + ".files");
        if (!System.IO.File.Exists(tmplSqlite))
            return $"InfraWorks 열기 생략 — 번들에 템플릿 없음({tmplSqlite}).";

        string modelsDir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            "Autodesk InfraWorks Models");
        try { System.IO.Directory.CreateDirectory(modelsDir); } catch { }
        string name = "DH_" + System.IO.Path.GetFileName(runFolder);   // 모델명=실행폴더명(짝 맞춤)
        string destSqlite = System.IO.Path.Combine(modelsDir, name + ".sqlite");
        string destFiles = System.IO.Path.Combine(modelsDir, name + ".files");

        try
        {
            System.IO.File.Copy(tmplSqlite, destSqlite, true);
            if (System.IO.Directory.Exists(tmplFiles)) CopyDir(tmplFiles, destFiles);
        }
        catch (System.Exception ex) { return $"InfraWorks 모델 복사 실패: {ex.Message}"; }

        // 소스 경로 재작성 — 번들의 DHInfraAuto.exe(헬퍼, SQLite 편집)를 동기 실행. 실패해도 열기는 계속(경로만 템플릿 기본).
        string retargetNote = "";
        string helper = System.IO.Path.Combine(bundleDir, "tools", "DHInfraAuto.exe");
        if (System.IO.File.Exists(helper))
        {
            try
            {
                var psi = new ProcessStartInfo(helper, $"retarget \"{destSqlite}\" \"{runFolder}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                p?.WaitForExit(15000);
                retargetNote = p != null && p.ExitCode == 0 ? "" : " (경로 재작성 확인 필요)";
            }
            catch { retargetNote = " (경로 재작성 실패)"; }
        }
        else retargetNote = " (헬퍼 없음 — 경로 재작성 생략)";

        if (!System.IO.File.Exists(InfraWorksExe))
            return $"InfraWorks 모델 '{name}' 생성됨{retargetNote} — InfraWorks.exe를 못 찾아 자동 실행 생략. 직접 여세요.";
        try { Process.Start(new ProcessStartInfo(InfraWorksExe, "\"" + destSqlite + "\"") { UseShellExecute = true }); }
        catch (System.Exception ex) { return $"InfraWorks 모델 '{name}' 생성됨{retargetNote} — 실행 실패({ex.Message}). 목록에서 직접 여세요."; }
        return $"InfraWorks 모델 '{name}' 생성 + 실행{retargetNote} — 열리면 [소스 Refresh/Reimport].";
    }

    private static void CopyDir(string src, string dst)
    {
        System.IO.Directory.CreateDirectory(dst);
        foreach (var f in System.IO.Directory.GetFiles(src))
            try { System.IO.File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), true); } catch { }
        foreach (var d in System.IO.Directory.GetDirectories(src))
            CopyDir(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
    }
}
