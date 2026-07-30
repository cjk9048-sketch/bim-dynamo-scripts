using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil;

/// <summary>[배포 — JACK 0728] 한국 좌표계 9종(KOREA_BESSEL/GRS80 125·127·129·131TM + UTM-K) 자동 설치.
/// 한국어판 Civil3D에도 이 좌표계들은 기본 포함이 아니라 JACK이 직접 정의한 것 — 애드인 시작 시 1회 검사한다.
///  · 사용자 좌표계 사전(%LOCALAPPDATA%\Autodesk\User Geospatial Coordinate Systems)이 없으면
///    → 번들 Contents\coordsys의 Coordsys.CSD/Category.CSD 복사(신규 설치, 가장 흔한 경우) + 1회 안내.
///  · 사전이 있고 한국 좌표계 포함 → 아무것도 안 함(조용).
///  · 사전이 있는데 한국 좌표계 없음 → 남의 사용자 정의를 지킬 수 있게 **자동 병합하지 않고**
///    MAPCSLIBRARY 가져오기(CSLibrary.xml) 안내만 띄운다(설치.ps1과 동일 방침).
/// 사전은 계정별(LOCALAPPDATA)이라 한 PC 여러 계정도 각자 첫 실행 때 자동으로 채워진다.</summary>
public static class CoordSysInstaller
{
    private static bool _done;

    /// <summary>시작(Idle) 시 1회 호출 — 실패해도 조용히 통과(좌표계는 내보내기 편의 기능일 뿐).</summary>
    public static void EnsureInstalled()
    {
        if (_done) return;
        _done = true;
        try
        {
            string? asmDir = System.IO.Path.GetDirectoryName(typeof(CoordSysInstaller).Assembly.Location);
            if (asmDir == null) return;
            string bundleCs = System.IO.Path.Combine(asmDir, "coordsys");
            string srcCoord = System.IO.Path.Combine(bundleCs, "Coordsys.CSD");
            string srcCat = System.IO.Path.Combine(bundleCs, "Category.CSD");
            if (!System.IO.File.Exists(srcCoord)) return;   // 배포물에 사전 없음 — 검사 불가, 조용히 통과

            string userDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Autodesk", "User Geospatial Coordinate Systems");
            string dstCoord = System.IO.Path.Combine(userDir, "Coordsys.CSD");

            if (!System.IO.File.Exists(dstCoord))
            {
                // 신규 설치 — 사용자 사전이 아예 없음(배포 대상 PC 대부분).
                System.IO.Directory.CreateDirectory(userDir);
                System.IO.File.Copy(srcCoord, dstCoord, overwrite: false);
                if (System.IO.File.Exists(srcCat))
                    System.IO.File.Copy(srcCat, System.IO.Path.Combine(userDir, "Category.CSD"), overwrite: false);
                AcadApp.ShowAlertDialog(
                    "DH 정지: 한국 좌표계 9종(KOREA_GRS80/BESSEL 125·127·129·131TM, UTM-K)을 등록했습니다.\n" +
                    "좌표계 목록에 바로 안 보이면 Civil3D를 한 번 재시작하세요.");
                return;
            }

            // 사용자 사전이 이미 있음 — 한국 정의 포함 여부(CSD 안 이름은 ASCII로 저장됨 — 실측 확인).
            if (ContainsAscii(dstCoord, "KOREA_GRS80")) return;   // 이미 있음 — 조용히 통과

            // 남의 사용자 정의가 있는 사전 — 덮어쓰면 그 정의가 날아가므로 수동 가져오기 안내만.
            string xml = System.IO.Path.Combine(bundleCs, "CSLibrary.xml");
            AcadApp.ShowAlertDialog(
                "DH 정지: 이 계정의 사용자 좌표계 사전에 한국 좌표계(KOREA_GRS80_…TM)가 없습니다.\n" +
                "기존 사용자 정의 보호를 위해 자동 병합은 하지 않습니다.\n\n" +
                "MAPCSLIBRARY 명령 → 가져오기에서 아래 파일을 선택해 주세요:\n" + xml);
        }
        catch { }
    }

    /// <summary>바이너리 파일에 ASCII 문자열이 들어 있는지(사전 포함 여부 검사).</summary>
    private static bool ContainsAscii(string path, string needle)
    {
        try
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
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
}
