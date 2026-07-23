using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Gis.Map.CoordinateSystem;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>[진단 DHCS — JACK 0723] 도면 좌표계 읽기/지정 API 실측용. 배포 자동설정 구현 전에
/// AcMapCoordsysCore.SetCoordinateSystem 이 어떤 코드 문자열(EPSG:5186? CS-Map 코드?)을 받아주는지 확인.
/// 현재 좌표계를 읽어 출력하고, 후보 코드들을 시험 지정 → 성패를 리포트하고 원래대로 되돌린다.</summary>
public sealed class CoordSysProbeCommand
{
    [CommandMethod("DHCS")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        var db = doc.Database;
        var sb = new System.Text.StringBuilder();

        string original;
        try { original = AcMapCoordsysCore.GetCoordinateSystem(db) ?? ""; }
        catch (System.Exception ex) { ed.WriteMessage($"\n[DHCS] 좌표계 읽기 실패: {ex.Message}"); return; }

        sb.AppendLine($"현재 도면 좌표계 코드: '{original}'{(string.IsNullOrEmpty(original) ? "  (지정 안 됨)" : "")}");
        sb.AppendLine("── 후보 코드 시험 지정(성공하면 그 형식이 유효) ──");

        // 5186(중부 2010) 후보 + 검증용으로 사전에 존재하는 경위도 코드도 하나.
        string[] candidates =
        {
            "EPSG:5186", "5186",
            "Korea2000.CentralBelt2010", "Korea2000.Central-Belt2010", "Korea2000.CentralBelt.2010",
            "Korea2000/Central-Belt.2010", "KoreaCentralBelt2010", "Korean2000.CentralBelt",
            "Korean2000.LL",   // 사전에 존재(경위도) — API 자체 동작 확인용
        };

        using (var dl = doc.LockDocument())
        {
            foreach (var code in candidates)
            {
                string result;
                try
                {
                    AcMapCoordsysCore.SetCoordinateSystem(code, db);
                    string now = AcMapCoordsysCore.GetCoordinateSystem(db) ?? "";
                    result = string.Equals(now, code, System.StringComparison.OrdinalIgnoreCase)
                        ? "✔ 성공(그대로 저장)"
                        : $"△ 지정됨 → 실제 저장코드 '{now}'";
                }
                catch (System.Exception ex) { result = "X 실패: " + ex.Message; }
                sb.AppendLine($"  '{code}'  →  {result}");
            }

            // 원래대로 복원(빈 문자열이면 지정 해제 시도).
            try { AcMapCoordsysCore.SetCoordinateSystem(original, db); }
            catch { }
        }

        string report = sb.ToString().TrimEnd();
        ed.WriteMessage("\n" + report + "\n");
        AcadApp.ShowAlertDialog("DHCS 좌표계 API 진단\n\n" + report);
    }
}
