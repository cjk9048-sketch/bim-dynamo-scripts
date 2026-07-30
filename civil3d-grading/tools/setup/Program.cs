using System.IO.Compression;
using System.Reflection;

// DH.Grading 단일 exe 설치 프로그램(JACK 0728) — 설치.ps1과 동일한 일을 한다:
//   ① 내장 번들 zip → %APPDATA%\Autodesk\ApplicationPlugins\DH.Grading.bundle 압축 해제(덮어씀)
//   ② 한국 좌표계 9종: 사용자 사전 없으면 신규 설치 / 있으면 포함 검사 / 남의 사전이면 안 덮고 안내
// 숨김 테스트 인자: --target <번들대상폴더> --usercs <좌표계폴더> (자동 검증용 — 사용자 안내에는 없음)

Console.OutputEncoding = System.Text.Encoding.UTF8;
string? argTarget = null, argUserCs = null;
for (int i = 0; i + 1 < args.Length; i++)
{
    if (args[i] == "--target") argTarget = args[i + 1];
    if (args[i] == "--usercs") argUserCs = args[i + 1];
}
bool testMode = argTarget != null || argUserCs != null;

Console.WriteLine("==== DH 정지(부지정지) 플러그인 설치 ====");
Console.WriteLine();
try
{
    // Civil3D 실행 중이면 DLL이 잠겨 복사 실패 → 안내.
    if (!testMode && System.Diagnostics.Process.GetProcessesByName("acad").Length > 0)
    {
        Console.WriteLine("! Civil3D(acad.exe)가 실행 중입니다. 완전히 닫은 뒤 다시 실행하세요.");
        Console.Write("  그래도 계속하려면 Y, 중단하려면 다른 키: ");
        ConsoleKey key;
        try { key = Console.ReadKey().Key; Console.WriteLine(); }
        catch { key = ConsoleKey.Escape; }   // 입력 불가 환경(리다이렉트)이면 안전하게 중단
        if (key != ConsoleKey.Y) { Pause("중단했습니다. Civil3D를 닫고 다시 실행해 주세요."); return; }
    }

    // ── ① 번들 설치 — 부분 설치 방지: 임시폴더 전량 추출 → 잠김 사전검사 → 일괄 반영 ──
    //    (잠긴 파일이 하나라도 있으면 아무것도 바꾸지 않고 중단 — 구DLL+신메타 혼재 방지, 리뷰 0728)
    string pluginDir = argTarget ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "ApplicationPlugins", "DH.Grading.bundle");
    string pluginRoot = Path.GetFullPath(pluginDir) + Path.DirectorySeparatorChar;
    Console.WriteLine("① 애드인 설치 → " + pluginDir);

    string staging = Path.Combine(Path.GetTempPath(), "DHGradingSetup_" + Guid.NewGuid().ToString("N"));
    try
    {
        // 1) 임시폴더로 전량 추출(대상은 아직 안 건드림).
        var files = new List<string>();   // pluginDir 기준 상대경로
        var asm = Assembly.GetExecutingAssembly();
        string res = asm.GetManifestResourceNames().First(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        using (var zs = asm.GetManifestResourceStream(res)!)
        using (var za = new ZipArchive(zs, ZipArchiveMode.Read))
        {
            foreach (var e in za.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;   // 폴더 엔트리
                string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                string dst = Path.GetFullPath(Path.Combine(pluginDir, rel));
                if (!dst.StartsWith(pluginRoot, StringComparison.OrdinalIgnoreCase)) continue; // 경로 방어
                string tmp = Path.Combine(staging, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
                e.ExtractToFile(tmp, overwrite: true);
                files.Add(rel);
            }
        }

        // 2) 잠김 사전검사 — 덮어쓸 기존 파일을 전부 열어본다. 하나라도 잠겨 있으면 변경 0으로 중단.
        var locked = new List<string>();
        foreach (var rel in files)
        {
            string dst = Path.Combine(pluginDir, rel);
            if (!File.Exists(dst)) continue;
            try { using var fs = new FileStream(dst, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch { locked.Add(rel); }
        }
        if (locked.Count > 0)
        {
            Pause("설치 중단(아무것도 변경하지 않았습니다) — 아래 파일이 사용 중입니다:\n   " +
                  string.Join("\n   ", locked) +
                  "\nCivil3D를 완전히 닫고 다시 실행해 주세요.");
            return;
        }

        // 3) 일괄 반영.
        foreach (var rel in files)
        {
            string dst = Path.Combine(pluginDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(Path.Combine(staging, rel), dst, overwrite: true);
        }
    }
    finally
    {
        try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
    }
    Console.WriteLine("   완료 — Civil3D 시작 시 자동 로드됩니다.");
    Console.WriteLine();

    // ── ② 한국 좌표계 9종(KOREA_GRS80/BESSEL 125·127·129·131TM + UTM-K) ──
    Console.WriteLine("② 한국 좌표계 정의 확인");
    string coordSrc = Path.Combine(pluginDir, "Contents", "coordsys");
    string srcCoord = Path.Combine(coordSrc, "Coordsys.CSD");
    string srcCat = Path.Combine(coordSrc, "Category.CSD");
    string userCs = argUserCs ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Autodesk", "User Geospatial Coordinate Systems");
    string dstCoord = Path.Combine(userCs, "Coordsys.CSD");

    if (!File.Exists(srcCoord))
        Console.WriteLine("   좌표계 정의 파일이 번들에 없음 — 생략");
    else if (!File.Exists(dstCoord))
    {
        Directory.CreateDirectory(userCs);
        File.Copy(srcCoord, dstCoord, true);
        if (File.Exists(srcCat)) File.Copy(srcCat, Path.Combine(userCs, "Category.CSD"), true);
        Console.WriteLine("   신규 설치 완료: KOREA_GRS80/BESSEL 125·127·129·131TM + UTM-K (9종)");
    }
    else if (ContainsAscii(dstCoord, "KOREA_GRS80"))
        Console.WriteLine("   이미 설치됨 — 생략");
    else
    {
        File.Copy(dstCoord, dstCoord + ".dhbak", true);   // 남의 사용자 정의 보호 — 덮지 않음
        Console.WriteLine("   주의: 기존 사용자 좌표계 사전이 있어 자동 병합하지 않았습니다.");
        Console.WriteLine("   백업: " + dstCoord + ".dhbak");
        Console.WriteLine("   한국 좌표계가 필요하면 Civil3D에서 MAPCSLIBRARY 명령 → 가져오기로 아래 파일을 선택:");
        Console.WriteLine("   " + Path.Combine(coordSrc, "CSLibrary.xml"));
    }

    Console.WriteLine();
    Pause("==== 설치 완료 ==== Civil3D를 실행하면 'DH 정지' 리본이 나타납니다.");
}
catch (Exception ex)
{
    Pause("설치 실패: " + ex.Message + Environment.NewLine +
          "(Civil3D가 켜져 있으면 완전히 닫고 다시 실행해 주세요.)");
}

static void Pause(string msg)
{
    Console.WriteLine(msg);
    Console.Write("아무 키나 누르면 창이 닫힙니다...");
    try { Console.ReadKey(); } catch { }   // 리다이렉트/자동실행 환경 대비
    Console.WriteLine();
}

static bool ContainsAscii(string path, string needle)
{
    try
    {
        byte[] bytes = File.ReadAllBytes(path);
        byte[] pat = System.Text.Encoding.ASCII.GetBytes(needle);
        for (int i = 0; i + pat.Length <= bytes.Length; i++)
        {
            int j = 0;
            while (j < pat.Length && bytes[i + j] == pat[j]) j++;
            if (j == pat.Length) return true;
        }
        return false;
    }
    catch { return true; }   // 못 읽으면 '있음' 취급 — 괜히 덮지 않게 보수적으로
}
