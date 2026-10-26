using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★[v105.1.3 · JACK 1023 «원지반 가져오기 스플릿 단추 안에 맨 마지막에 원지반 삭제를 넣어줘»]
/// <para>[초기화]가 원지반을 남기게 되면서(v105.1.2) 원지반을 버리는 길이 따로 필요해졌다. [원지반 가져오기](서버 지표면 · 수치지도 DXF)가 만든 것만 지운다 —
/// '원지반' 지표면(이름 · 이름_N)과 그 재료(등고선 · 계곡선 · 표고점 · 원지형 자르기가 옮겨 둔 범위 밖 자료). 사람이 따로 고른 다른 지표면은 안 건드린다.</para>
/// <para>★<b>원지반으로 만든 결과가 있으면 같이 지운다([초기화]와 같음).</b> 정지면은 원지반을 붙여넣어 만든다 — 원지반만 지우면 붙여넣기 원본이 없는 면이 남고,
/// 그 면의 스냅샷 재작성이 실패한 채 커밋되면 다음 재작성에서 Civil이 죽는다(1013 실측). 그래서 묻는 창에 그것을 적고 함께 걷는다.</para></summary>
public sealed class GroundOffCommand
{
    [CommandMethod("DHGROUNDOFF")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            int nSurf = 0, nEnt = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                nSurf = GradingBuilder.CountSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                nEnt = OnLayers(db, tr, GroundLayers()).Count;
                tr.Commit();
            }
            if (nSurf == 0 && nEnt == 0)
            {
                ed.WriteMessage("\n[원지반 삭제] 지울 것이 없습니다 — [원지반 가져오기]로 만든 '원지반'이 없습니다.");
                AcadApp.ShowAlertDialog("원지반 삭제\n\n지울 것이 없습니다 — [원지반 가져오기]로 만든 '원지반'이 없습니다.\n(직접 고른 다른 지표면은 지우지 않습니다)");
                return;
            }

            string results = ResetCommand.ResultsOnGround(db);
            string ask = results.Length == 0
                ? "'원지반' 지표면과 그 재료(등고선·계곡선·표고점)를 지웁니다.\n\n계속할까요?"
                : "원지반으로 만든 결과가 있습니다.\n" + results + "\n\n" +
                  "원지반만 지우면 그 결과가 깨지므로 같이 지웁니다([초기화]와 같음 — 지적도·지번도).\n" +
                  "직접 그린 것은 남깁니다.\n\n계속할까요?";
            var ans = System.Windows.MessageBox.Show(ask, "DH 정지 — 원지반 삭제", System.Windows.MessageBoxButton.YesNo,
                                                     System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
            if (ans != System.Windows.MessageBoxResult.Yes) { ed.WriteMessage("\n[원지반 삭제] 취소"); return; }

            var (done, left) = Erase(doc, results);
            ed.Regen();
            ed.WriteMessage($"\n[원지반 삭제] {done}." + (left.Length > 0 ? "\n  " + left : ""));
            AcadApp.ShowAlertDialog("원지반 삭제 완료\n\n" + done + "." + (left.Length > 0 ? "\n\n" + left : "") + "\n\n[원지반 가져오기]로 새 원지반을 만들 수 있습니다.");
            try { DiagLog.Append($"\n■ DHGROUNDOFF — {done}{(results.Length > 0 ? $"(결과: {results})" : "")}{(left.Length > 0 ? " · " + left : " · 남은 것 없음")}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[원지반 삭제 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("원지반 삭제 중 오류:\n" + ex.Message);
        }
    }

    /// <summary>원지반의 재료 레이어 — 등고선 · 계곡선 · 표고점 · 원지형 자르기가 옮겨 둔 범위 밖 자료.</summary>
    static System.Collections.Generic.HashSet<string> GroundLayers()
        => new(ImportGisCommand.GroundImportLayers, System.StringComparer.OrdinalIgnoreCase) { CropGroundCommand.CutAwayLayer };

    /// <summary>지운다(묻기 뒤 · 화면 없는 시험도 이것을 부른다). <paramref name="results"/>가 있으면 [초기화]와 같게 결과까지 · 없으면 원지반과 재료만.
    /// 반환 = (한 일, 되읽어 남은 것 — 없으면 빈 글).</summary>
    internal static (string Done, string Left) Erase(Document doc, string results)
    {
        var db = doc.Database;
        var layers = GroundLayers();
        string done;
        int fail = 0;
        if (results.Length > 0)
        {
            var (s, e, _) = ResetCommand.ResetCore(doc, includeImported: true, keepGround: false);
            done = $"정지 결과까지 지웠습니다 — 지표면 {s}개 · 객체 {e}개";
        }
        else
        {
            int nS = 0, nE = 0;
            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                nS = GradingBuilder.CountSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                GradingBuilder.EraseSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                foreach (var id in OnLayers(db, tr, layers))
                {
                    try { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); nE++; }
                    catch { fail++; }   // 레이어가 잠겼으면 여기로 온다 — 숨기지 않는다
                }
                tr.Commit();
            }
            done = $"원지반 {nS}개 · 재료 {nE:N0}개를 지웠습니다";
        }
        // 되읽기 — 새 트랜잭션에서 원지반이 정말 없어졌나
        int leftSurf, leftEnt;
        using (var tr2 = db.TransactionManager.StartTransaction())
        {
            leftSurf = GradingBuilder.CountSurfacesByBaseName(tr2, ImportGisCommand.GroundSurfaceName);
            leftEnt = OnLayers(db, tr2, layers).Count;
            tr2.Commit();
        }
        string left = leftSurf + leftEnt == 0 ? "" : $"⚠남은 것 — 원지반 {leftSurf}개 · 재료 {leftEnt}개{(fail > 0 ? "(레이어 잠금 확인)" : "")}";
        return (done, left);
    }

    /// <summary>모형 공간에서 그 레이어들에 있는 객체 — 지우는 중에 목록이 흔들리지 않게 먼저 모은다.</summary>
    static System.Collections.Generic.List<ObjectId> OnLayers(Database db, Transaction tr, System.Collections.Generic.HashSet<string> layers)
    {
        var ids = new System.Collections.Generic.List<ObjectId>();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            try { if (tr.GetObject(id, OpenMode.ForRead) is Entity e && e.Layer != null && layers.Contains(e.Layer)) ids.Add(id); } catch { }
        return ids;
    }
}
