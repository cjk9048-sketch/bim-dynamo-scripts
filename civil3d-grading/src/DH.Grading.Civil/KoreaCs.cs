using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Gis.Map.CoordinateSystem;
using DH.Grading.Core;

namespace DH.Grading.Civil;

/// <summary>도면 좌표계(MAPCSASSIGN) 자동 처리 — JACK 0723. 배포 대상 PC마다 좌표계가 안 잡혀 있을 수 있어,
/// DHINFRA 실행 시 ① 도면에 좌표계가 있으면 그 원점을 읽어 내보내기 EPSG에 자동 반영, ② 없으면 설정(드롭박스)
/// 원점으로 도면에 좌표계를 지정한다.
///
/// 실측(DHCS): 도면 좌표계 코드는 한국 Civil3D 사용자사전의 <c>KOREA_GRS80_{125|127|129|131}TM</c>(GRS80=Korea2000)
///   또는 <c>KOREA_BESSEL_*</c>(구 1985). EPSG:5186 등 표준코드·CS-Map코드는 SetCoordinateSystem이 거부.
///   API: <c>AcMapCoordsysCore.Get/SetCoordinateSystem(db)</c>(AcMapCoordsysCoreMgd.dll).</summary>
public static class KoreaCs
{
    /// <summary>도면에 지정된 좌표계 코드(없으면 빈 문자열).</summary>
    public static string Read(Database db)
    {
        try { return AcMapCoordsysCore.GetCoordinateSystem(db) ?? ""; }
        catch { return ""; }
    }

    /// <summary>KOREA_GRS80_###TM 코드 → 신(2010) 벨트 EPSG. 인식 못하면 null(Bessel/기타는 미지원 → 드롭박스 사용).
    /// 신/구(원점가산 500000/600000)는 코드에 없어 현대 표준 신(5185~5188)으로 본다. 구 좌표는 드롭박스로 오버라이드.</summary>
    public static int? ResolveEpsgFromCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var m = Regex.Match(code, @"KOREA_GRS80_(\d{3})TM", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Value switch
        {
            "125" => 5185,   // 서부
            "127" => 5186,   // 중부
            "129" => 5187,   // 동부
            "131" => 5188,   // 동해
            _ => (int?)null,
        };
    }

    /// <summary>도면에 좌표계가 없으면 epsg 원점에 해당하는 KOREA_GRS80_{cm}TM 을 지정. 반환=(적용여부, 안내).
    /// 대상 PC 사용자사전에 그 코드가 없으면 실패(예외)하지만 내보내기는 계속(드롭박스가 좌표를 정함).</summary>
    public static (bool assigned, string note) AssignIfMissing(Database db, int epsg)
    {
        string cur = Read(db);
        if (!string.IsNullOrEmpty(cur)) return (false, $"이미 지정됨({cur})");
        var belt = ShapefileWriter.Belt(epsg);
        if (belt == null) return (false, "미지원 EPSG — 지정 생략");
        string code = $"KOREA_GRS80_{belt.Value.cm}TM";
        try
        {
            AcMapCoordsysCore.SetCoordinateSystem(code, db);
            return (true, $"도면에 좌표계 자동 지정: {code}");
        }
        catch (System.Exception ex)
        {
            return (false, $"자동 지정 실패({code}) — 이 PC 사용자사전에 없을 수 있음: {ex.Message}");
        }
    }
}
