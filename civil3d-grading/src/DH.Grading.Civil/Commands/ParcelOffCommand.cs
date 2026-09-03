using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0902 <i>"지적도도 위성지도 삽입처럼 스플릿버튼으로 지적도라고 만들고 안에
/// 지적도 삽입버튼하고 지적도삭제 버튼 만들어줘. 삭제버튼 누르면 지적도 관련해서 부른 자료들
/// 삭제시켜 — 레이어를 통으로"</i>]
///
/// <para><b>지적도 지우기 — 레이어 통째로.</b> [지적도 가져오기]가 만든 것은 두 레이어에만 올라간다:
/// 필지 경계(<see cref="ImportGisCommand.LayerParcel"/>)와 지번 글씨(<see cref="ImportGisCommand.LayerJibun"/>).
/// 그 두 레이어의 객체를 모형공간에서 통째로 지운다 — 레이어 자체는 남겨 다음에 다시 쓴다.</para>
///
/// <para>★<b>[초기화]로는 안 지워진다.</b> 가져온 자료는 <see cref="ImportGisCommand.ImportLayers"/>에 들어
/// 있어 초기화가 <b>일부러 보존</b>한다(등고선·표고점과 함께). 지적도만 따로 버리고 싶을 때 쓰는 단추다.</para></summary>
public sealed class ParcelOffCommand
{
    [CommandMethod("DHPARCELOFF")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            int n = 0, fail = 0;
            var want = new System.Collections.Generic.HashSet<string>(
                new[] { ImportGisCommand.LayerParcel, ImportGisCommand.LayerJibun },
                System.StringComparer.OrdinalIgnoreCase);

            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                // 지우는 중에 목록이 흔들리지 않게 <b>먼저 모으고</b> 나서 지운다.
                var kill = new System.Collections.Generic.List<ObjectId>();
                foreach (ObjectId id in ms)
                {
                    try
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                        if (e.Layer != null && want.Contains(e.Layer)) kill.Add(id);
                    }
                    catch { }
                }
                foreach (ObjectId id in kill)
                {
                    try { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); n++; }
                    catch { fail++; }   // ★[검토 0902] 레이어가 잠겼으면 여기로 온다 — 숨기지 않는다
                }
                tr.Commit();
            }

            ed.Regen();
            if (fail > 0)
            {
                // ★[검토 0902] 후보는 있었는데 못 지웠다 — "없습니다"로 덮지 않는다.
                ed.WriteMessage($"\n[지적도 삭제] {n:N0}개 지웠고 {fail:N0}개는 못 지웠습니다(레이어 잠금 확인).");
                AcadApp.ShowAlertDialog($"지적도 삭제\n\n{n:N0}개를 지웠고 {fail:N0}개는 못 지웠습니다.\n레이어가 잠겨 있는지 확인해 주세요.");
                return;
            }
            if (n == 0)
            {
                ed.WriteMessage("\n[지적도 삭제] 지울 것이 없습니다 — 가져온 지적도가 없습니다.");
                AcadApp.ShowAlertDialog("지적도 삭제\n\n지울 것이 없습니다 — 가져온 지적도가 없습니다.");
                return;
            }
            ed.WriteMessage($"\n[지적도 삭제] {n:N0}개 지웠습니다(필지 경계·지번).");
            AcadApp.ShowAlertDialog($"지적도 삭제 완료\n\n{n:N0}개를 지웠습니다(필지 경계·지번).");
            try { DiagLog.Append($"\n■ DHPARCELOFF — {n}개 삭제(레이어 {ImportGisCommand.LayerParcel}·{ImportGisCommand.LayerJibun})\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지적도 삭제 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("지적도 삭제 중 오류:\n" + ex.Message);
        }
    }
}
