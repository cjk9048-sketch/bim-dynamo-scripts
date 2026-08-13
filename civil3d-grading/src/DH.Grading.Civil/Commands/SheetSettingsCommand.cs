using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v32.28 · JACK 0813] <b>"도면 설정"(DHSHEETSET)</b> — 도면화에 관한 값만 모은 창.
///
/// <para>정지옵션(<c>DHGRADESET</c>)에서 <b>도면 쪽 값들을 떼어 왔다</b>: 횡단 간격·폭·배치 수,
/// 원지반 굴곡, 종단도 표 종류, 배경지도 화질. 가른 기준은 하나다 —
/// <b>정지면(흙)의 모양을 바꾸는가, 도면의 모양을 바꾸는가.</b></para>
///
/// <para>그래서 이 창의 값은 <b>정지면을 다시 만들 필요가 없다.</b> 바꾼 뒤 도면만 다시 그리면 된다
/// (종단도 버튼 → '지우고 새로'). 정지옵션과 달리 좌표계·배경지도 재생성 같은
/// <b>뒷일이 하나도 없어서</b> 이 명령은 창을 띄우고 저장하는 것이 전부다.</para></summary>
public sealed class SheetSettingsCommand
{
    [CommandMethod("DHSHEETSET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        // [도면 전환 0803] 도면이 바뀌었으면 그 도면이 저장한 값을 보여준다(정지옵션과 같은 규칙).
        GradingSettings.SyncToDocument(doc);

        var dlg = new SheetDialog("저장");
        AcadApp.ShowModalWindow(dlg);
        if (dlg.DialogResult != true) return;

        doc.Editor.WriteMessage(
            "\n[도면 설정] 저장했습니다 — 횡단 간격 " + GradingSettings.XsecInterval.ToString("0.#") + "m"
            + " · 폭 좌" + GradingSettings.XsecLeft.ToString("0.#") + "/우" + GradingSettings.XsecRight.ToString("0.#") + "m"
            + " · 원지반 굴곡 " + GradingSettings.GroundBreakLabels[GradingSettings.GroundBreakStep()]
            + "(" + GradingSettings.GroundBreakTolZ.ToString("0.###") + "m)");

        // ★★[v32.29 · JACK 0813] <b>저장하면 이미 만든 종단도가 그 자리에서 갱신된다.</b>
        //   JACK: <i>"도면설정에서 원지반 표현을 바꾸고 저장해도 업데이트가 되지 않아."</i>
        //   정밀도를 바꾸면 측점이 바뀌고, 측점이 바뀌면 단면검토선·밴드·종단뷰·도곽이 전부 딸려 가므로
        //   <b>다시 그리는 것이 곧 갱신</b>이다. 노선과 놓은 자리를 재사용하므로 다시 찍을 것이 없다.
        //   종단도가 없으면 <see cref="ProfileCommand.Rebuild"/>가 조용히 안내만 하고 돌아선다.
        ProfileCommand.Rebuild(doc);
    }
}
