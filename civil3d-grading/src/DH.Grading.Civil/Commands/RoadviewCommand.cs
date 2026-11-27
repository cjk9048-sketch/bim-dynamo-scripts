using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v117.44 · JACK 1126] [기타] → [로드뷰](DHROADVIEW) — 도면에서 위치를 찍으면 그 자리 위 작은 창에 카카오 로드뷰.
/// <para>계속 찍기(JACK 인터뷰 «계속 찍기») — 찍을 때마다 창이 그 자리로 옮겨 새로 찾는다 · ★[v117.44.1] Esc = 끝 + 창 닫기 · Enter = 창 두고 끝(창 [✕]로 닫으면 도면 표시도 걷힘).</para>
/// <para>좌표계는 다른 VWorld 기능(지적도 · 서버 지표면 · 3D)과 같은 길(<see cref="ImportGisCommand.ResolveEpsg"/>) — 도면 좌표계가 없으면 [공통설정] «기타» 원점.</para></summary>
public class RoadviewCommand
{
    [CommandMethod("DHROADVIEW")]
    public static void Roadview()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        if (KakaoKey.Current.Length == 0)
        {
            ed.WriteMessage("\n[로드뷰] " + KakaoKey.NoKeyHelp);
            return;
        }
        int epsg = ImportGisCommand.ResolveEpsg(doc.Database, out string csNote);
        var belt = ShapefileWriter.Belt(epsg);
        if (belt == null)
        {
            ed.WriteMessage($"\n[로드뷰] 좌표계 EPSG:{epsg} 못 씀 → [공통설정] «기타»에서 좌표계 고르기");
            return;
        }
        ed.WriteMessage($"\n[로드뷰] {csNote} · 카카오 {KakaoKey.Source}");
        int cm = belt.Value.cm; double fn = belt.Value.fn;

        PickingDoc = doc;
        try
        {
            while (true)
            {
                var po = new PromptPointOptions("\n로드뷰 위치 찍기 [Esc = 닫기 · Enter = 창 남김]") { AllowNone = true };
                var r = ed.GetPoint(po);
                // ★[v117.44.1 · JACK 1127 «ESC 하면 로드뷰 창 닫히게»] Esc = 창도 닫음 · Enter(빈 답) = 창 두고 찍기만 끝
                if (r.Status == PromptStatus.Cancel) { RoadviewWindow.CloseIfOpen(); break; }
                if (r.Status != PromptStatus.OK) break;
                var w = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var (lon, lat) = KoreaTm.ToLonLat(w.X, w.Y, cm, fn);
                // 우리나라 둘레(제주 · 독도 포함) 밖이면 좌표계가 틀린 것 — 카카오에 묻지 않는다
                if (double.IsNaN(lat) || lat < 32.5 || lat > 39.5 || lon < 124 || lon > 132.5)
                {
                    ed.WriteMessage($"\n[로드뷰] 국내 위치 아님(위도 {lat:F3} · 경도 {lon:F3}) → 도면 좌표계 확인({csNote})");
                    continue;
                }
                RoadviewWindow.ShowAt(doc, w, lon, lat, cm, fn, FanSize(ed));
            }
        }
        finally { PickingDoc = null; }
    }

    /// <summary>지금 로드뷰 찍기 중인 도면(없으면 null) — 창에서 Esc 를 누르면 이 도면의 점 기다림을 끝낸다(<see cref="RoadviewWindow"/>).</summary>
    internal static Document? PickingDoc { get; private set; }

    /// <summary>부채꼴 크기(도면 단위) — 지금 화면 높이의 6 %(작은 부지 · 넓은 노선 어디서나 비슷하게 보이게) · 2~200 m.</summary>
    static double FanSize(Editor ed)
    {
        try
        {
            using var v = ed.GetCurrentView();
            return System.Math.Clamp(v.Height * 0.06, 2, 200);
        }
        catch { return 10; }
    }
}
