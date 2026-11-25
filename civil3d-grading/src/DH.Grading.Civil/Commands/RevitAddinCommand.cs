using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[JACK 1123 «레빗 애드인 파일 설치 버튼도 추가»] [구조물] 창 → [Revit 애드인 설치](DHREVITADDIN).
/// <para>번들 <c>Contents\RevitAddin\</c>(DH.CivilCoord.Revit.dll · DH.CivilCoord.Core.dll · DH.CivilCoord.addin)를 Revit 2026 애드인 폴더로 복사한다 —
/// [좌표계 내보내기] 파일을 읽어 Revit 모델을 Civil 좌표에 맞추는 애드인(Revit 리본 «DH 좌표»).</para>
/// <para>차례: 설치 파일 있음 → Revit 2026 있음 → Revit 꺼져 있음 → 같은 AddInId 매니페스트가 있는 자리(ProgramData · %APPDATA%)를 덮어 갱신
/// (두 군데 다 있으면 둘 다 갱신 + 알림 — Revit 중복 오류 자리) · 없으면 ProgramData에 쓰기 시험 → 안 되면 %APPDATA%.
/// 덮기 전 백업(%APPDATA%\DH.CivilCoord\backup\시각) · 끝에 해시 대조 · 셋 다 같으면 «이미 최신»(안 씀).</para>
/// <para>★시험 고리(<c>*ForTest</c>) — 키트가 설치 위치 · Revit 있음/켜짐 · 쓰기 거절을 scratch 폴더로 바꾼다. 이 PC의 실제 애드인 폴더는 시험에서 안 씀.</para></summary>
public sealed class RevitAddinCommand
{
    internal static string? SourceDirForTest, ProgramDataRootForTest, AppDataRootForTest, BackupRootForTest, RevitExeForTest;
    internal static bool? RevitRunningForTest;
    internal static bool ProgramDataDenyForTest;
    /// <summary>시험용 — 마지막 결과 한 줄.</summary>
    internal static string LastForTest = "";

    const string Tag = "[Revit 애드인] ";
    static readonly string[] Dlls = { "DH.CivilCoord.Revit.dll", "DH.CivilCoord.Core.dll" };
    const string Manifest = "DH.CivilCoord.addin";

    static string SourceDir => SourceDirForTest ?? Path.Combine(BundleContents.Root, "RevitAddin");
    static string ProgramDataRoot => ProgramDataRootForTest ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData), @"Autodesk\Revit\Addins\2026");
    static string AppDataRoot => AppDataRootForTest ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), @"Autodesk\Revit\Addins\2026");
    static string BackupRoot => BackupRootForTest ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), @"DH.CivilCoord\backup");
    static string RevitExe => RevitExeForTest ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), @"Autodesk\Revit 2026\Revit.exe");

    static bool RevitRunning()
    {
        if (RevitRunningForTest is bool b) return b;
        var ps = System.Diagnostics.Process.GetProcessesByName("Revit");   // 읽기만 — 건드리지 않음
        foreach (var p in ps) p.Dispose();
        return ps.Length > 0;
    }

    [CommandMethod("DHREVITADDIN")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var log = new System.Text.StringBuilder("\n■ DHREVITADDIN(Revit 애드인 설치)\n");
        string msg;
        try { Install(log, out msg); }
        catch (System.Exception ex) { msg = "⚠설치 실패 — " + ex.Message; }
        LastForTest = msg;
        try { doc?.Editor.WriteMessage("\n" + Tag + msg); } catch { }
        try { DiagLog.Append(log.ToString() + "  → " + msg + "\n"); } catch { }
        // 창 단추로 부르므로 명령줄만으로는 놓치기 쉬움 — 화면 있으면 알림 창도
        if (!SelfCheckLog.Headless) try { AcadApp.ShowAlertDialog("Revit 애드인 설치\n\n" + msg.Replace(" · ", "\n")); } catch { }
    }

    static string Sha(string path)
    {
        using var s = File.OpenRead(path);
        return System.Convert.ToHexString(SHA256.HashData(s));
    }

    /// <summary>판 «1.2.0» — Revit DLL 파일 판(못 읽으면 빈 글).</summary>
    static string VersionOf(string dll)
    {
        try { var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll); return v.FileMajorPart + v.FileMinorPart + v.FileBuildPart > 0 ? $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}" : ""; }
        catch { return ""; }
    }

    /// <summary>이 루트(Addins\2026)에 같은 AddInId 매니페스트가 있나 — 있으면 그 파일 경로(이름이 달라도 찾음).</summary>
    static string? FindManifest(string root, string addInId)
    {
        try
        {
            if (!Directory.Exists(root)) return null;
            foreach (var f in Directory.EnumerateFiles(root, "*.addin"))
                try { if (File.ReadAllText(f).Contains(addInId, System.StringComparison.OrdinalIgnoreCase)) return f; } catch { }
        }
        catch { }
        return null;
    }

    /// <summary>쓸 수 있나 — 폴더를 만들고 빈 파일 하나 써 보고 지움.</summary>
    static bool CanWrite(string root, out string why)
    {
        why = "";
        try
        {
            if (ProgramDataDenyForTest && root == ProgramDataRoot) throw new System.UnauthorizedAccessException("시험 — 쓰기 거절");
            Directory.CreateDirectory(root);
            string t = Path.Combine(root, ".dh_write_test_" + System.Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(t, "");
            File.Delete(t);
            return true;
        }
        catch (System.Exception ex) { why = ex.Message; return false; }
    }

    /// <summary>설치 한 번 — <paramref name="msg"/> = 끝 알림 한 줄(초보용 단답형).</summary>
    internal static bool Install(System.Text.StringBuilder log, out string msg)
    {
        string src = SourceDir, srcManifest = Path.Combine(src, Manifest);
        foreach (var f in Dlls.Append(Manifest))
            if (!File.Exists(Path.Combine(src, f))) { msg = $"⚠설치 파일 없음({f}) → DH 부지 애드인을 다시 설치"; log.AppendLine("  원본 " + src); return false; }
        if (!File.Exists(RevitExe)) { msg = "Revit 2026 없음 → 이 PC에 Revit 2026을 설치한 뒤 다시"; log.AppendLine("  찾은 곳 " + RevitExe); return false; }
        if (RevitRunning()) { msg = "Revit 켜져 있음 → Revit을 닫고 다시"; return false; }

        string man = File.ReadAllText(srcManifest);
        string id = Regex.Match(man, @"<AddInId>\s*([^<\s]+)\s*</AddInId>").Groups[1].Value;
        string asmRel = Regex.Match(man, @"<Assembly>\s*([^<]+?)\s*</Assembly>").Groups[1].Value;
        if (id.Length == 0 || asmRel.Length == 0) { msg = "⚠설치 파일이 이상함(매니페스트) → DH 부지 애드인을 다시 설치"; return false; }
        string sub = Path.GetDirectoryName(asmRel) ?? "";   // «DH.CivilCoord»
        string ver = VersionOf(Path.Combine(src, Dlls[0]));
        string verTxt = ver.Length > 0 ? " · 판 " + ver : "";

        // 설치 자리 — 같은 AddInId가 이미 있는 자리(들) · 없으면 ProgramData(쓰기 시험) → %APPDATA%
        var places = new List<(string Root, string ManifestPath)>();
        foreach (var root in new[] { ProgramDataRoot, AppDataRoot })
            if (FindManifest(root, id) is { } mp) places.Add((root, mp));
        string note = "";
        if (places.Count == 0)
        {
            if (CanWrite(ProgramDataRoot, out string why)) places.Add((ProgramDataRoot, Path.Combine(ProgramDataRoot, Manifest)));
            else
            {
                log.AppendLine($"  ProgramData 쓰기 안 됨 — {why} → %APPDATA%");
                places.Add((AppDataRoot, Path.Combine(AppDataRoot, Manifest)));
            }
        }
        else if (places.Count > 1) note = " · ⚠두 군데 설치됨(Revit 중복 알림) → 하나는 지울 것";
        log.AppendLine($"  원본 {src}{verTxt} · AddInId {id} · 자리 " + string.Join(" | ", places.Select(p => p.ManifestPath)));

        var srcHash = Dlls.ToDictionary(f => f, f => Sha(Path.Combine(src, f)));
        string srcManHash = Sha(srcManifest);
        int wrote = 0;
        string backups = "";
        foreach (var (root, manPath) in places)
        {
            string dir = Path.Combine(root, sub);
            var pairs = Dlls.Select(f => (Src: Path.Combine(src, f), Dst: Path.Combine(dir, f), Hash: srcHash[f])).Append((Src: srcManifest, Dst: manPath, Hash: srcManHash)).ToList();
            if (pairs.All(p => File.Exists(p.Dst) && Sha(p.Dst) == p.Hash)) { log.AppendLine("  " + root + " — 같음"); continue; }
            // 백업 — 있는 것만
            var olds = pairs.Where(p => File.Exists(p.Dst)).ToList();
            string bk = "";
            if (olds.Count > 0)
            {
                string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                bk = Path.Combine(BackupRoot, stamp);
                for (int k = 2; Directory.Exists(bk); k++) bk = Path.Combine(BackupRoot, stamp + "_" + k);
                Directory.CreateDirectory(bk);
                foreach (var p in olds) File.Copy(p.Dst, Path.Combine(bk, Path.GetFileName(p.Dst)), true);
                backups += (backups.Length > 0 ? " · " : "") + bk;
            }
            try
            {
                Directory.CreateDirectory(dir);
                foreach (var p in pairs) File.Copy(p.Src, p.Dst, true);
            }
            catch (System.Exception ex) when (ex is IOException or System.UnauthorizedAccessException)
            {
                // 되살리기 — 백업한 것을 제자리로(새로 생긴 것은 그대로 둠 · 덮다 만 것만 되돌림)
                foreach (var p in olds) try { File.Copy(Path.Combine(bk, Path.GetFileName(p.Dst)), p.Dst, true); } catch { }
                log.AppendLine("  복사 실패 — " + ex.Message);
                msg = ex is System.UnauthorizedAccessException
                    ? $"⚠쓰기 권한 없음({root}) → Civil 3D를 관리자 권한으로 켜고 다시"
                    : "⚠파일이 쓰이는 중 → Revit을 닫고 다시";
                return false;
            }
            foreach (var p in pairs)
                if (Sha(p.Dst) != p.Hash) { msg = $"⚠해시 다름({Path.GetFileName(p.Dst)}) → 다시 눌러 보고 · 또 나면 알릴 것"; return false; }
            wrote++;
            log.AppendLine($"  {root} — 씀 · 해시 같음" + (bk.Length > 0 ? " · 백업 " + bk : ""));
        }
        if (wrote == 0) { msg = $"이미 최신{verTxt} · {places[0].Root}{note}"; return true; }
        msg = $"설치 끝 → Revit을 켜면 'DH 좌표' 탭{verTxt} · {string.Join(" · ", places.Select(p => p.Root))}" + (backups.Length > 0 ? " · 옛 판 백업 " + backups : "") + note;
        return true;
    }
}
