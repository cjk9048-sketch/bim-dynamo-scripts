using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>
/// "정지 설정"(DHGRADESET) — 단높이/소단폭/구배/격자를 팝업 창에서 입력받아 저장.
/// 구배는 1:n = 수직 1 : 수평 n (예 1:1.5).
/// </summary>
public sealed class GradingSettingsCommand
{
    [CommandMethod("DHGRADESET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 도면이 바뀌었으면 그 도면이 저장한 값을 보여준다

        // [리뷰 0731 D-1] 대화상자를 열기 전에 **도면 좌표계로 옵션 값을 맞춘다**.
        //   그러지 않으면 콤보가 하드코딩 기본값(중부 5186)을 보여주고, 사용자가 좌표계를 건드리지 않고
        //   [저장]만 눌러도 도면 좌표계가 중부로 덮어써져 배경지도가 통째로 딴 곳(최대 180km)으로 간다.
        //   [리뷰 0731 R-2] 단, 현재 선택이 **도면 코드로 표현 불가한 원점**(구 좌표계·UTM-K)이면 덮어쓰지 않는다.
        //   ResolveEpsgFromCode는 신/구를 구분 못 해 항상 신(5185~5188)을 돌려주므로, 덮어쓰면 사용자가 고른
        //   구좌표가 정지옵션을 열 때마다 신좌표로 리셋돼 배경지도가 북쪽으로 100km 어긋난다(ResolveCs와 대칭).
        var detected = KoreaCs.ResolveEpsgFromCode(KoreaCs.Read(doc.Database));
        if (detected.HasValue && KoreaCs.CodeForEpsg(GradingSettings.ExportEpsg) != null)
            GradingSettings.ExportEpsg = detected.Value;
        int prevEpsg = GradingSettings.ExportEpsg;   // 사용자가 콤보를 실제로 바꿨는지 판정용

        var dlg = new GradingDialog("저장");
        AcadApp.ShowModalWindow(dlg); // [저장] 시 GradingSettings에 반영됨
        if (dlg.DialogResult != true) return;

        // [JACK 0731 — 좌표계 연동] 사용자가 좌표계를 **실제로 바꿨을 때만** 도면 좌표계에 반영(MAPCSASSIGN 상당)
        //   + 이미 깔린 배경지도를 새 좌표계로 자동 재생성. 안 바꿨으면 아무것도 건드리지 않는다(리뷰 D-1).
        //   [리뷰 0731 R-1] 조기 return 금지 — 아래 '결과지표면만 표시' 즉시 반영이 통째로 건너뛰어진다.
        if (prevEpsg != GradingSettings.ExportEpsg)
        {
            // [JACK 0731] 가져온 등고선·지적도가 있으면 예/아니오로 확인. 이 자료들은 이전 좌표계 기준이라
            //   좌표계가 바뀌면 더 이상 맞지 않는다(뒤에 원지반→정지면이 줄줄이 물려 있음).
            //   예 = 가져온 것 + 정지 결과 전부 초기화(사용자가 직접 그린 계획폴리곤은 보존)
            //   아니오 = 좌표계 변경 자체를 취소(자료와 좌표계가 항상 맞는 상태 유지)
            if (ResetCommand.HasImportedGis(doc.Database))
            {
                // [JACK 0731] 문구는 짧게 — 줄이 길면 자동 줄바꿈과 겹쳐 지저분해진다.
                var ans = System.Windows.MessageBox.Show(
                    "좌표계를 바꾸면\n" +
                    "가져온 등고선·지적도가 맞지 않게 됩니다.\n\n" +
                    "[예] 가져온 자료와 정지 결과를 지우고 변경\n" +
                    "[아니오] 변경 취소\n\n" +
                    "※ 직접 그린 계획폴리곤은 그대로 둡니다.",
                    "DH 정지 — 좌표계 변경",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (ans != System.Windows.MessageBoxResult.Yes)
                {
                    GradingSettings.ExportEpsg = prevEpsg;   // 좌표계 변경 취소(나머지 설정은 저장됨)
                    doc.Editor.WriteMessage("\n[정지 옵션] 좌표계 변경을 취소했습니다(다른 설정은 저장됨).");
                    goto AfterCs;
                }
                try
                {
                    var (s, e2, _) = ResetCommand.ResetCore(doc, includeImported: true);
                    doc.Editor.WriteMessage($"\n[정지 옵션] 좌표계 변경 — 가져온 자료·정지 결과 초기화(지표면 {s}·객체 {e2})");
                    try { DiagLog.Append($"\n■ 좌표계 변경 초기화 — 지표면 {s} · 객체 {e2}\n"); } catch { }
                }
                catch (System.Exception rex)
                { doc.Editor.WriteMessage("\n[정지 옵션] 초기화 중 오류: " + rex.Message); }
            }

            try
            {
                var (ok, note) = KoreaCs.Assign(doc.Database, GradingSettings.ExportEpsg);
                doc.Editor.WriteMessage("\n[정지 옵션] " + note);
                if (!ok)
                    AcadApp.ShowAlertDialog(
                        "도면 좌표계를 자동으로 바꾸지 못했습니다.\n" + note +
                        "\n\n배경지도는 정지옵션에서 고른 좌표계로 만들어집니다.\n" +
                        "도면 좌표계까지 맞추려면 MAPCSASSIGN 명령으로 직접 지정하세요.");
                try { DiagLog.Append($"\n■ DHGRADESET 좌표계 변경 — EPSG {prevEpsg} → {GradingSettings.ExportEpsg} · {note}\n"); } catch { }

                // 좌표계가 바뀌면 같은 도면 좌표라도 실제 지구상 위치가 달라져 기존 위성사진은 더는 맞지 않는다.
                int refreshed = BasemapCommand.RefreshAll(doc);
                if (refreshed > 0)
                    doc.Editor.WriteMessage($"\n[정지 옵션] 배경지도 {refreshed}개를 새 좌표계로 다시 배치했습니다.");
            }
            catch (System.Exception ex)
            {
                // [리뷰 M-A] 조용히 삼키지 않는다 — 배경지도가 사라진 채 무음이 되는 것을 방지.
                doc.Editor.WriteMessage("\n[정지 옵션] 좌표계 반영 중 오류: " + ex.Message);
                try { DiagLog.Append($"\n■ DHGRADESET 좌표계 반영 오류 — {ex.Message}\n"); } catch { }
            }
        }
    AfterCs:

        // [JACK 0728] '결과지표면만 표시' 저장 즉시 반영 — 해제=숨겼던 지표면 전부 표시 / 체크=정지면_DH만(있을 때).
        try
        {
            using var tr = doc.Database.TransactionManager.StartTransaction();
            if (!GradingSettings.ShowOnlyResultSurface)
                GradingBuilder.IsolateSurfaces(tr, null);
            else if (GradingBuilder.SurfaceExistsByBaseName(tr, "정지면_DH"))
                GradingBuilder.IsolateSurfaces(tr, "정지면_DH");

            // ★★[v32.4 · JACK 0812 '자꾸 스냅샷 재작성 느낌표가 뜬다'] <b>느낌표의 진범이 여기 있었다.</b>
            //
            //   <b>표시를 끄면 지표면이 '구식'이 된다</b> — 그리고 <b>다시 켜도 구식으로 남는다</b>.
            //   (Autodesk 포럼에 결함으로 등록된 동작이다. 표면과 무관한 선을 숨겨도 붙는다.)
            //   <c>IsolateSurfaces</c>는 바로 그 <c>Visible</c>을 건드린다.
            //
            //   그런데 종전 코드는 <b>옵션을 켤 때만</b> 재작성을 따라 붙였다. <b>끄는 쪽엔 없었다.</b>
            //   JACK이 '결과지표면만 표시'를 해제하는 순간 도면의 <b>모든</b> 지표면이 구식이 되고
            //   아무도 되돌리지 않는다 — 정지 생성 쪽 순서를 아무리 고쳐도 이 길로 느낌표가 되살아난다.
            //   → <b>양쪽 다</b> 재작성한다. 끄든 켜든 가시성을 건드렸으면 반드시 뒤따라야 한다.
            GradingBuilder.SetSurfaceVisible(tr, SectionCommand.PurePadSurfaceBase, false);          // 순수면은 늘 숨김
            GradingBuilder.SetSurfaceVisible(tr, SectionCommand.PurePadSurfaceBase + "이전", false);
            // ★★[v32.7] <b>둘만이 아니라 전부.</b> 숨김은 도면의 <b>모든</b> 지표면에 붙으므로
            //   되살리는 것도 전부여야 한다(둘만 챙겼더니 나머지에 느낌표가 남았다).
            GradingBuilder.RebuildAllSurfaces(tr);
            tr.Commit();
            doc.Editor.Regen();
        }
        catch { }
    }
}
