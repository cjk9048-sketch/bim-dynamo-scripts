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

        // ★★★[검토 0908 · 심각] <b>안내문은 재생성 <u>뒤에</u> 찍는다.</b>
        //
        //   종전엔 여기서 먼저 찍고 아래에서 <c>Rebuild</c>를 불렀다. 그런데 폭이 <b>자동</b>이면
        //   <c>Rebuild</c> 안에서 <see cref="XsecWidth"/>가 정지 결과를 재어 폭을 <b>덮어쓴다</b> —
        //   그래서 <b>[저장] 한 번 누르는 사이에</b> 사용자가 친 30이 50으로 바뀌는데
        //   화면에는 30이라고 찍혀 있었다. 도면설정을 다시 열면 50이 들어 있다.
        //   <b>자기가 방금 한 말을 스스로 거짓말로 만드는</b> 순서였다.
        //   → 다시 그린 <b>뒤에</b> 최종 값을 찍는다.

        // ★★[v32.29 · JACK 0813] <b>저장하면 이미 만든 종단도가 그 자리에서 갱신된다.</b>
        //   JACK: <i>"도면설정에서 원지반 표현을 바꾸고 저장해도 업데이트가 되지 않아."</i>
        //   정밀도를 바꾸면 측점이 바뀌고, 측점이 바뀌면 단면검토선·밴드·종단뷰·도곽이 전부 딸려 가므로
        //   <b>다시 그리는 것이 곧 갱신</b>이다. 노선과 놓은 자리를 재사용하므로 다시 찍을 것이 없다.
        //   종단도가 없으면 <see cref="ProfileCommand.Rebuild"/>가 조용히 안내만 하고 돌아선다.
        ProfileCommand.Rebuild(doc, out bool profileTouched);

        // ★★★[JACK 0908 "종단은 다시 그려지는데 횡단은 그냥 지워져버려"]
        //   <b>횡단도 같이 다시 그린다.</b>
        //
        //   <c>Rebuild</c>가 검토선을 새로 만들면 <b>거기 매달린 횡단면도를 Civil이 지운다</b>.
        //   <see cref="StationCommand"/>는 그 뒤에 <c>Refresh</c>를 불러 같은 자리에 다시 그리는데,
        //   여기만 <b>그 줄이 빠져 있었다</b> — 그래서 축척을 바꾸면 종단은 갱신되고
        //   횡단은 <b>사라지기만</b> 했다.
        //   ★<c>Refresh</c>는 한 번도 안 그렸으면(<c>LastAt</c>이 비었으면) 아무 일도 안 한다.
        //   ★★[검토 0908 · 보통] <b>오래 걸린다고 먼저 말한다.</b>
        //     실측(진단이력 20260908_152050): 검토선 43개 → 뷰 43장 · 표본 지표면 10장 · 도곽 22장.
        //     여기에 위 <c>Rebuild</c>까지 더하면 <b>[저장] 한 번이 종단·횡단 통째 재작성</b>이다.
        //     종단은 시작 전에 예고를 하는데 횡단만 말이 없으면, 사람은 창이 닫힌 뒤
        //     <b>왜 멈춰 있는지 모른 채</b> 수십 초를 기다린다.
        //   ★★[검토 0908 · 낮음] <b>부서진 것이 없으면 다시 그리지 않는다.</b>
        //     종단도가 아예 없어 <c>Rebuild</c>가 그냥 돌아섰다면 횡단도 딸려 지워지지 않았다 —
        //     그때 굳이 뷰 수십 장을 새로 구우면 <b>시간만 버리고</b>, 종단은 옛 설정인데
        //     횡단만 새 설정이 되어 <b>둘이 어긋난다</b>.
        //     반대로 <c>Body</c>가 중간에 실패한 경우에는 <b>이미 부순 뒤</b>라 꼭 다시 그려야 한다 —
        //     그래서 성공/실패가 아니라 <b>손을 댔는가</b>로 가른다.
        bool xsecBack = false;
        bool xsecTried = profileTouched && XsecViewCommand.LastAt != null;
        if (xsecTried)
            doc.Editor.WriteMessage("\n[도면 설정] 횡단도도 같은 자리에 다시 그립니다 — 장수가 많으면 시간이 걸립니다...");
        try { if (xsecTried) xsecBack = XsecViewCommand.Refresh(doc); }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\n[도면 설정] 횡단도 갱신 실패 — " + ex.Message); }

        // ★재생성이 폭을 고쳤을 수 있으므로 <b>지금</b> 읽어 찍는다.
        doc.Editor.WriteMessage(
            "\n[도면 설정] 저장했습니다 — 횡단 간격 " + GradingSettings.XsecInterval.ToString("0.#") + "m"
            + " · 폭 좌" + GradingSettings.XsecLeft.ToString("0.#") + "/우" + GradingSettings.XsecRight.ToString("0.#") + "m"
            + (GradingSettings.XsecWidthAuto ? "(자동 — 정지 결과에서 쟀습니다)" : "(수동 — 치신 값)")
            + " · 원지반 굴곡 " + GradingSettings.GroundBreakLabels[GradingSettings.GroundBreakStep()]
            + "(" + GradingSettings.GroundBreakTolZ.ToString("0.###") + "m)"
            + " · 종단뷰 축척 " + (GradingSettings.ProfileScale > 0
                                   ? "1:" + GradingSettings.ProfileScale.ToString("F0") + "(고정)"
                                   : "자동")
            + (!xsecTried
                   ? ""                                            // 그린 적이 없거나 손댄 것이 없으면 할 말이 없다
                   : xsecBack
                       ? " · 횡단도도 같은 자리에 다시 그렸습니다"
                       // ★[검토 0908 · 높음] <b>못 그렸으면 못 그렸다고 한다.</b>
                       //   종전엔 이 경우 아무 말이 없어, 지워지기만 한 것을 사람이 알 길이 없었다.
                       : " · 횡단도는 다시 그리지 못했습니다 — 위 [횡단도] 메시지를 보세요"));
    }
}
