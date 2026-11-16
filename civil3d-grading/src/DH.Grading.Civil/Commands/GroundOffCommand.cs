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
/// 그 면의 스냅샷 재작성이 실패한 채 커밋되면 다음 재작성에서 Civil이 죽는다(1013 실측). 그래서 묻는 창에 그것을 적고 함께 걷는다.</para>
/// <para>★[JACK 1116 «두 개 이상이면 선택해서»] 원지반이 둘 이상이면 창(<see cref="GroundOffDialog"/>)에서 지울 것을 고른다. 재료(등고선 · 표고점)는 공용 레이어라
/// 어느 원지반 것인지 구분이 안 됨 → <b>다 지울 때만</b> 같이 지우고 일부만이면 남긴다. 결과는 고른 원지반을 쓰는 것이 있을 때만 같이 지운다.</para></summary>
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
            var grounds = new System.Collections.Generic.List<(ObjectId Id, string Name, int Pts)>();
            int nEnt = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
                foreach (ObjectId sid in civ.GetSurfaceIds())
                    try
                    {
                        if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface s) continue;
                        string nm = s.Name ?? "";
                        if (!SectionCommand.IsBase(nm, ImportGisCommand.GroundSurfaceName)) continue;   // 원지반 이름만 — 남의 면은 안 건드림
                        int pts = -1; try { pts = s.GetGeneralProperties().NumberOfPoints; } catch { }
                        grounds.Add((sid, nm, pts));
                    }
                    catch { }
                nEnt = OnLayers(db, tr, GroundLayers()).Count;
                tr.Commit();
            }
            if (grounds.Count == 0 && nEnt == 0)
            {
                ed.WriteMessage("\n[원지반 삭제] 지울 것 없음 — [원지반 가져오기]로 만든 원지반 없음.");
                if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("원지반 삭제\n\n지울 것 없음 — [원지반 가져오기]로 만든 원지반이 없음.\n(직접 고른 다른 지표면은 안 지움)");
                return;
            }

            // 둘 이상이면 고르기 — 화면 없는 시험은 묻지 않고 전부(종전과 같음) · 시험 이음매가 있으면 그 이름만
            System.Collections.Generic.List<ObjectId>? only = null;   // null = 전부
            if (_pickForTest != null)
                only = grounds.Where(g => _pickForTest.Contains(g.Name)).Select(g => g.Id).ToList();
            else if (grounds.Count >= 2 && !SelfCheckLog.Headless)
            {
                var used = UsedBy(db, grounds.Select(g => g.Id).ToList());
                var dlg = new GroundOffDialog(grounds.Select(g => new GroundOffDialog.Item(g.Name, g.Pts, used.TryGetValue(g.Id, out var u) ? u : "")).ToList());
                AcadApp.ShowModalWindow(dlg);
                var names = dlg.Chosen;
                if (dlg.DialogResult != true || names.Count == 0) { ed.WriteMessage("\n[원지반 삭제] 취소"); return; }
                only = grounds.Where(g => names.Contains(g.Name)).Select(g => g.Id).ToList();
            }
            if (only != null && only.Count == 0) { ed.WriteMessage("\n[원지반 삭제] 고른 것 없음"); return; }
            if (only != null && only.Count == grounds.Count) only = null;   // 다 골랐으면 전부와 같음(재료도 같이)

            // 결과(정지면 · 진입도로 …) — 전부 지울 때는 종전 규칙 · 일부만이면 고른 것을 쓰는 결과가 있을 때만
            string results = ResetCommand.ResultsOnGround(db);
            if (only != null)
            {
                var used = UsedBy(db, only);
                if (used.Count == 0) results = "";
                else if (results.Length == 0) results = string.Join(" · ", used.Values.Distinct());
            }
            if (!SelfCheckLog.Headless && (only == null || results.Length > 0))
            {
                string ask = results.Length == 0
                    ? "원지반과 그 재료(등고선·계곡선·표고점)를 지움.\n\n계속?"
                    : "원지반으로 만든 결과가 있음.\n" + results + "\n\n" +
                      "원지반만 지우면 결과가 깨짐 → 같이 지움([전체 초기화]와 같음).\n" +
                      "직접 그린 것은 남김.\n\n계속?";
                var ans = System.Windows.MessageBox.Show(ask, "DH 정지 — 원지반 삭제", System.Windows.MessageBoxButton.YesNo,
                                                         System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
                if (ans != System.Windows.MessageBoxResult.Yes) { ed.WriteMessage("\n[원지반 삭제] 취소"); return; }
            }

            var (done, left) = Erase(doc, results, only);
            ed.Regen();
            ed.WriteMessage($"\n[원지반 삭제] {done}." + (left.Length > 0 ? "\n  " + left : ""));
            if (!SelfCheckLog.Headless)
                AcadApp.ShowAlertDialog("원지반 삭제 끝\n\n" + done + "." + (left.Length > 0 ? "\n\n" + left : ""));
            try { DiagLog.Append($"\n■ DHGROUNDOFF — {done}{(results.Length > 0 ? $"(결과: {results})" : "")}{(left.Length > 0 ? " · " + left : " · 남은 것 없음")}{(only != null ? $" · 일부({only.Count}/{grounds.Count})" : "")}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[원지반 삭제 오류] " + ex.Message);
            if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("원지반 삭제 오류:\n" + ex.Message);
        }
    }

    /// <summary>시험 이음매 — 값이 있으면 그 이름의 원지반만 지운다(창 없이 고르기 갈래를 재려고, 반사로 넣음). 평소엔 null.</summary>
#pragma warning disable CS0649
    static string[]? _pickForTest;
#pragma warning restore CS0649

    /// <summary>원지반마다 «그 면을 쓰는 것»(붙여넣기 소스 · 사람 표본선 …). 안 쓰이면 목록에 없음. 읽기만.</summary>
    static System.Collections.Generic.Dictionary<ObjectId, string> UsedBy(Database db, System.Collections.Generic.List<ObjectId> ids)
    {
        var r = new System.Collections.Generic.Dictionary<ObjectId, string>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var id in ids)
            {
                var m = SurfaceRetire.MustStay(db, tr, new[] { id });
                if (m.TryGetValue(id, out var who) && who.Length > 0) r[id] = who;
            }
            tr.Commit();
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n■ DHGROUNDOFF 쓰임 읽기 실패 — " + ex.Message + "\n"); } catch { } }
        return r;
    }

    /// <summary>원지반의 재료 레이어 — 등고선 · 계곡선 · 표고점 · 원지형 자르기가 옮겨 둔 범위 밖 자료.</summary>
    static System.Collections.Generic.HashSet<string> GroundLayers()
        => new(ImportGisCommand.GroundImportLayers, System.StringComparer.OrdinalIgnoreCase) { CropGroundCommand.CutAwayLayer };

    /// <summary>지운다(묻기 뒤). <paramref name="results"/>가 있으면 [초기화]와 같게 결과까지 · 없으면 원지반과 재료만.
    /// <paramref name="only"/> = 고른 원지반만(null = 전부). 일부만이면 재료(등고선 · 표고점)는 남긴다 — 레이어가 하나라 어느 원지반 것인지 구분이 안 됨.
    /// 반환 = (한 일, 되읽어 남은 것 · 알림 — 없으면 빈 글).</summary>
    internal static (string Done, string Left) Erase(Document doc, string results, System.Collections.Generic.ICollection<ObjectId>? only = null)
    {
        var db = doc.Database;
        var layers = GroundLayers();
        string done;
        int fail = 0;
        if (results.Length > 0)
        {
            var (s, e, _) = ResetCommand.ResetCore(doc, includeImported: true, keepGround: false, onlyGround: only);
            done = $"정지 결과까지 지움 — 지표면 {s}개 · 객체 {e}개";
        }
        else
        {
            int nS = 0, nE = 0;
            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (only == null)
                {
                    nS = GradingBuilder.CountSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                    GradingBuilder.EraseSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                    foreach (var id in OnLayers(db, tr, layers))
                    {
                        try { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); nE++; }
                        catch { fail++; }   // 레이어가 잠겼으면 여기로 온다 — 숨기지 않는다
                    }
                }
                else
                    foreach (var id in only)
                        try
                        {
                            // 원지반 이름만 — 지우기 직전에 이름을 다시 확인
                            if (id.IsErased || tr.GetObject(id, OpenMode.ForWrite) is not Autodesk.Civil.DatabaseServices.Surface sf
                                || !SectionCommand.IsBase(sf.Name ?? "", ImportGisCommand.GroundSurfaceName)) continue;
                            sf.Erase(); nS++;
                        }
                        catch { fail++; }
                tr.Commit();
            }
            done = only == null ? $"원지반 {nS}개 · 재료 {nE:N0}개 지움" : $"원지반 {nS}개 지움";
        }
        // 되읽기 — 새 트랜잭션에서 원지반이 정말 없어졌나
        int leftSurf, leftEnt;
        using (var tr2 = db.TransactionManager.StartTransaction())
        {
            leftSurf = GradingBuilder.CountSurfacesByBaseName(tr2, ImportGisCommand.GroundSurfaceName);
            leftEnt = OnLayers(db, tr2, layers).Count;
            tr2.Commit();
        }
        string left;
        if (only == null)
            left = leftSurf + leftEnt == 0 ? "" : $"⚠남은 것 — 원지반 {leftSurf}개 · 재료 {leftEnt}개{(fail > 0 ? " (레이어 잠금 확인)" : "")}";
        else
            left = $"남은 원지반 {leftSurf}개" + (fail > 0 ? $" · ⚠못 지움 {fail}개" : "") + (leftEnt > 0 ? " · 등고선 · 표고점은 남김 — 어느 원지반 것인지 구분 안 됨" : "");
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
