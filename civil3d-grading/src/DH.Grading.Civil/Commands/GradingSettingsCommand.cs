using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[계획 8단계] <b>DHGRADESET은 이제 도킹창을 연다.</b>
///
/// <para>종전에는 팝업 하나에 다섯 덩어리가 들어 있었다. 그중 정지 제원은
/// <see cref="DH.Grading.Civil.GradingPanel"/>(도킹창)으로, 좌표계·표시는
/// <see cref="MiscSettingsCommand"/>(기타 설정)로 갈라 나갔다.</para>
///
/// <para>★<b>명령 이름은 그대로 둔다.</b> JACK이 손에 익었고, 문서·툴팁·매크로에 흩어져 있다.
/// 이름을 바꾸면 <b>어제 쓰던 것이 오늘 안 되는</b> 종류의 변화다 — 얻는 것 없이 잃기만 한다.</para></summary>
public sealed class GradingSettingsCommand
{
    [CommandMethod("DHGRADESET", CommandFlags.Session)]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        DH.Grading.Civil.GradingPalette.Show();
        try
        {
            doc.Editor.WriteMessage(
                // ★[JACK 0910] [값 저장]은 없어졌다 — 친 값은 바로 반영되고, 만들기는 그 칸 안에 있다.
                "\n[정지 설정] 오른쪽 <계획부지 정지> 창에서 값을 고치면 바로 반영됩니다 — [계획부지생성하기]로 만드세요."
              + "\n  · 좌표계·표시 옵션과 옹벽 형태는 리본 [기타] → [기타 설정](DHMISCSET)으로 옮겼습니다.");
        }
        catch { }
    }
}

/// <summary>★★★[계획 8단계 · JACK 지시 14] <b>기타 설정</b>(DHMISCSET) — 좌표계 · 표시.
///
/// <para>★<b>저장 뒤 사슬을 통째로 물려받았다.</b> 좌표계를 바꾸면 도면 좌표계를 다시 잡고,
/// 가져온 자료가 있으면 묻고, 배경지도를 다시 배치하고, 표시 옵션을 즉시 반영한다.
/// 화면만 옮기고 이 사슬을 <c>DHGRADESET</c>에 두고 왔다면
/// <b>좌표계를 바꿔도 도면이 안 따라오는</b> 상태가 됐을 것이다 —
/// 이 저장소가 "고친 것이 한 곳에만 들어갔다"로 여러 번 값을 치른 자리다(§77).</para></summary>
public sealed class MiscSettingsCommand
{
    [CommandMethod("DHMISCSET")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);   // [도면 전환 0803] 그 도면이 저장한 값을 보여준다

        // [리뷰 0731 D-1] 창을 열기 전에 **도면 좌표계로 옵션 값을 맞춘다**.
        //   그러지 않으면 콤보가 하드코딩 기본값(중부 5186)을 보여주고, 사용자가 좌표계를 건드리지 않고
        //   [저장]만 눌러도 도면 좌표계가 중부로 덮어써져 배경지도가 통째로 딴 곳(최대 180km)으로 간다.
        //   [리뷰 0731 R-2] 단, 현재 선택이 **도면 코드로 표현 불가한 원점**(구 좌표계·UTM-K)이면 덮어쓰지 않는다.
        var detected = KoreaCs.ResolveEpsgFromCode(KoreaCs.Read(doc.Database));
        if (detected.HasValue && KoreaCs.CodeForEpsg(GradingSettings.ExportEpsg) != null)
            GradingSettings.ExportEpsg = detected.Value;
        int prevEpsg = GradingSettings.ExportEpsg;   // 사용자가 콤보를 실제로 바꿨는지 판정용

        var dlg = new MiscSettingsDialog();
        AcadApp.ShowModalWindow(dlg); // [저장] 시 GradingSettings에 반영됨
        // ★이 화면은 정지 <b>제원</b>(단높이·소단·구배)을 안 건드리므로 도킹창 값을 되채울 것이 없다.
        //   ★[JACK 0910] 단, <b>옹벽 형태</b>가 이리로 왔다 — 그것은 값 칸이 아니라
        //     정지 창의 <b>예시 그림</b>에 나타나므로, 맨 아래에서 그림만 다시 그리게 한다.
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
                    doc.Editor.WriteMessage("\n[기타 설정] 좌표계 변경을 취소했습니다(다른 설정은 저장됨).");
                    goto AfterCs;
                }
                try
                {
                    var (s, e2, _) = ResetCommand.ResetCore(doc, includeImported: true);
                    doc.Editor.WriteMessage($"\n[기타 설정] 좌표계 변경 — 가져온 자료·정지 결과 초기화(지표면 {s}·객체 {e2})");
                    try { DiagLog.Append($"\n■ 좌표계 변경 초기화 — 지표면 {s} · 객체 {e2}\n"); } catch { }
                }
                catch (System.Exception rex)
                { doc.Editor.WriteMessage("\n[기타 설정] 초기화 중 오류: " + rex.Message); }
            }

            try
            {
                // ★사용자가 <b>바꿨을 때</b>만 여기 온다 — 덮어써도 되는 자리다.
                var (ok, note) = KoreaCs.Assign(doc.Database, GradingSettings.ExportEpsg);
                doc.Editor.WriteMessage("\n[기타 설정] " + note);
                if (!ok)
                    AcadApp.ShowAlertDialog(
                        "도면 좌표계를 자동으로 바꾸지 못했습니다.\n" + note +
                        "\n\n배경지도는 [기타 설정]에서 고른 좌표계로 만들어집니다.\n" +
                        "도면 좌표계까지 맞추려면 MAPCSASSIGN 명령으로 직접 지정하세요.");
                try { DiagLog.Append($"\n■ DHMISCSET 좌표계 변경 — EPSG {prevEpsg} → {GradingSettings.ExportEpsg} · {note}\n"); } catch { }

                // 좌표계가 바뀌면 같은 도면 좌표라도 실제 지구상 위치가 달라져 기존 위성사진은 더는 맞지 않는다.
                int refreshed = BasemapCommand.RefreshAll(doc);
                if (refreshed > 0)
                    doc.Editor.WriteMessage($"\n[기타 설정] 배경지도 {refreshed}개를 새 좌표계로 다시 배치했습니다.");
            }
            catch (System.Exception ex)
            {
                // [리뷰 M-A] 조용히 삼키지 않는다 — 배경지도가 사라진 채 무음이 되는 것을 방지.
                doc.Editor.WriteMessage("\n[기타 설정] 좌표계 반영 중 오류: " + ex.Message);
                try { DiagLog.Append($"\n■ DHMISCSET 좌표계 반영 오류 — {ex.Message}\n"); } catch { }
            }
        }
        // ★★★[JACK 0901 "정지옵션에서 저장을 누르는 순간 도면에 좌표계가 정의가 안 되어 있다면
        //   정의해 줘야 되는 거 아니야?"] — <b>맞다. 구멍이었다.</b>
        //
        //   위 블록은 <b>콤보를 실제로 바꿨을 때만</b> 돈다. 빈 도면은 콤보가 이미 중부원점으로
        //   떠 있어서, 손대지 않고 [저장]을 누르면 <b>도면 좌표계가 계속 비어 있다</b>.
        //   우리 애드인 안에서는 정지옵션 값으로 대신하니 잘 돌지만, 그 도면이 <b>밖으로 나가면</b>
        //   여기가 어디인지 아무도 모른다(InfraWorks·QGIS·MAPIMPORT).
        //
        //   ★<b>비어 있을 때만</b> 채운다 — 이미 잡아 놓고 쓰던 도면은 절대 안 건드린다.
        try
        {
            var (setIt, csNote2) = KoreaCs.AssignIfMissing(doc.Database, GradingSettings.ExportEpsg);
            if (setIt && csNote2.Contains("지정")) doc.Editor.WriteMessage("\n[기타 설정] " + csNote2);
        }
        catch { }

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

        // ★[JACK 0910] 옹벽 형태가 이 창으로 옮겨 왔다 — 정지 창의 <b>예시 그림</b>이 그 값을 그린다.
        //   여기서 안 알리면 창은 <b>옛 옹벽 모양</b>을 계속 보여 준다(§78 "화면을 옮기면 사슬도 옮겨라").
        //   ★<c>Refresh</c>가 아니라 <c>RedrawExample</c>이다 — 고치다 만 숫자를 지우지 않기 위해서다.
        try { GradingPalette.RedrawExample(); } catch { }
    }
}
