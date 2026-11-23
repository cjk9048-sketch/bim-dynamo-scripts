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
/// <para>★[JACK 1116 «두 개 이상이면 선택해서»] 원지반이 둘 이상이면 창(<see cref="GroundOffDialog"/>)에서 지울 것을 고른다. 결과는 고른 원지반을 쓰는 것이 있을 때만 같이 지운다.</para>
/// <para>★[v117.19 · JACK 1116] 재료(등고선 · 표고점 · 브레이크라인)마다 <b>주인 표시</b>(<see cref="GroundSet"/>)가 있어 <b>고른 원지반의 재료만</b> 지운다. 주인 표시 없는 옛 재료는 원지반을 <b>전부</b> 지울 때만 같이 지운다.
/// 면이 «물러남»(남이 씀 — 지워지지 않음)이면 그 재료는 남긴다(정의가 물고 있음).</para></summary>
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
            var ext = new System.Collections.Generic.Dictionary<string, Extents3d>();   // ★[JACK 1117] 이름 → 범위(창에서 누르면 그 자리로 화면 이동)
            int nEnt = 0, nLegacy = 0;
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
                        try { ext[nm] = s.GeometricExtents; } catch { }
                    }
                    catch { }
                nEnt = OnLayers(db, tr, GroundLayers()).Count;
                nLegacy = OnLayers(db, tr, GroundLayers(), untaggedOnly: true).Count;
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
                var dlg = new GroundOffDialog(grounds.Select(g => new GroundOffDialog.Item(g.Name, g.Pts, used.TryGetValue(g.Id, out var u) ? u : "")).ToList(), nLegacy > 0);
                dlg.ZoomTo = it => { if (ext.TryGetValue(it.Name, out var e)) ZoomToExt(ed, e); };
                AcadApp.ShowModalWindow(dlg);
                var names = dlg.Chosen;
                if (dlg.DialogResult != true || names.Count == 0) { ed.WriteMessage("\n[원지반 삭제] 취소"); return; }
                only = grounds.Where(g => names.Contains(g.Name)).Select(g => g.Id).ToList();
            }
            if (only != null && only.Count == 0) { ed.WriteMessage("\n[원지반 삭제] 고른 것 없음"); return; }
            if (only != null && only.Count == grounds.Count) only = null;   // 다 골랐으면 전부와 같음(재료도 같이)

            // ★[JACK 1116] 결과는 지울 원지반에 기대는 것만 가려 지운다(GroundDeps) — 가릴 수 없으면 종전대로 전체 지움(이유를 묻는 글에 적음)
            var gIds = only != null ? only : grounds.Select(g => g.Id).ToList();
            var plan = GroundDeps.Resolve(doc, gIds);
            bool partial = plan.Fallback.Length == 0;
            string gNames = string.Join(" · ", grounds.Where(g => gIds.Contains(g.Id)).Select(g => g.Name).Take(4));
            string results = "";
            if (!partial)
            {
                results = ResetCommand.ResultsOnGround(db);
                if (only != null)
                {
                    var used = UsedBy(db, only);
                    if (used.Count == 0) results = "";
                    else if (results.Length == 0) results = string.Join(" · ", used.Values.Distinct());
                }
            }
            string ask = "";
            if (partial && (plan.Any || only == null || plan.Human.Length > 0))
            {
                ask = "원지반 지움: " + gNames + " (재료 등고선·계곡선·표고점 포함)\n" +
                      (plan.Any ? "\n함께 지움: " + plan.Gone() : "") +
                      (plan.Stay().Length > 0 ? "\n남김: " + plan.Stay() : "") +
                      (plan.Human.Length > 0 ? "\n남이 씀 → 원지반은 이름만 바꿔 숨김: " + plan.Human : "") +
                      "\n직접 그린 것은 남김 · 지운 뒤 U(되돌리기) 안 됨.\n\n계속?";
            }
            else if (!partial && (only == null || results.Length > 0))
            {
                ask = results.Length == 0
                    ? "원지반과 그 재료(등고선·계곡선·표고점)를 지움 · 지운 뒤 U(되돌리기) 안 됨.\n\n계속?"
                    : "원지반으로 만든 결과가 있음.\n" + results + "\n\n" +
                      "함께 지움: " + GoneSummary(db) + "\n" +
                      "쓰는 결과만 가려 지우지 못함(" + plan.Fallback + ") → 결과 전체를 지움([전체 초기화]와 같음).\n" +
                      "직접 그린 것은 남김 · 지운 뒤 U(되돌리기) 안 됨.\n\n계속?";
            }
            LastAskForTest = ask;
            if (!SelfCheckLog.Headless && ask.Length > 0)
            {
                var ans = System.Windows.MessageBox.Show(ask, "DH 정지 — 원지반 삭제", System.Windows.MessageBoxButton.YesNo,
                                                         System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
                if (ans != System.Windows.MessageBoxResult.Yes) { ed.WriteMessage("\n[원지반 삭제] 취소"); return; }
            }

            string done, left;
            if (partial) (done, left) = GroundDeps.Execute(doc, plan, gIds, only);
            else (done, left) = Erase(doc, results, only, plan.Fallback);
            ed.Regen();
            ed.WriteMessage($"\n[원지반 삭제] {done}." + (left.Length > 0 ? "\n  " + left : ""));
            if (!SelfCheckLog.Headless)
                AcadApp.ShowAlertDialog("원지반 삭제 끝\n\n" + done + "." + (left.Length > 0 ? "\n\n" + left : ""));
            try { DiagLog.Append($"\n■ DHGROUNDOFF — {done}{(partial ? " (일부만 — 쓰는 결과만)" : $"(결과: {results} · 전체 지움 이유: {plan.Fallback})")}{(left.Length > 0 ? " · " + left : " · 남은 것 없음")}{(only != null ? $" · 일부({only.Count}/{grounds.Count})" : "")}\n"); } catch { }
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
    /// <summary>시험 이음매 — 마지막 확인 글(화면 없는 Civil에서 «함께 지움 / 남김» 줄을 잼).</summary>
    internal static string LastAskForTest = "";

    /// <summary>원지반마다 «그 면을 쓰는 것»(붙여넣기 소스 · 사람 표본선 · <b>그 면 위에 놓인 진입도로</b> …). 안 쓰이면 목록에 없음. 읽기만.
    /// 도로 = 기록의 원지반 핸들이 그 면이거나, 바탕면 이름이 그 면이고 정지면 위가 아닐 때(«-바탕» 종단이 그 면을 따라 그림).</summary>
    internal static System.Collections.Generic.Dictionary<ObjectId, string> UsedBy(Database db, System.Collections.Generic.List<ObjectId> ids)
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
            var nameOf = new System.Collections.Generic.Dictionary<ObjectId, string>();
            foreach (var id in ids) try { nameOf[id] = (tr.GetObject(id, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Surface)?.Name ?? ""; } catch { nameOf[id] = ""; }
            foreach (var aid in RoadRanges.Roads(tr, db))
                try
                {
                    var d = RoadDesign.Read(tr, aid);
                    if (d == null) continue;
                    bool onPad = (d.BaseSurface ?? "").Length > 0 && GroundDeps.IsSitePad(d.BaseSurface ?? "");   // GroundDeps.Resolve와 같은 판정(정지면 가족)
                    string road = "진입도로 «" + ((tr.GetObject(aid, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Alignment)?.Name ?? "") + "»";
                    foreach (var id in ids)
                    {
                        bool hit = (!string.IsNullOrEmpty(d.GroundHandle) && d.GroundHandle.Equals(id.Handle.ToString(), System.StringComparison.OrdinalIgnoreCase))
                                || (!onPad && nameOf[id].Length > 0 && nameOf[id] == d.BaseSurface);
                        if (!hit) continue;
                        r[id] = r.TryGetValue(id, out var o) && o.Length > 0 ? o + " · " + road : road;
                    }
                }
                catch { }
            tr.Commit();
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n■ DHGROUNDOFF 쓰임 읽기 실패 — " + ex.Message + "\n"); } catch { } }
        return r;
    }

    /// <summary>결과를 같이 지울 때 실제로 사라지는 것 — «정지면 · 진입도로 2 · 터파기 1». 읽기만.</summary>
    static string GoneSummary(Database db)
    {
        var l = new System.Collections.Generic.List<string>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try { if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s && SectionCommand.IsBase(s.Name ?? "", SectionCommand.PadSurfaceBase)) { l.Add("정지면"); break; } } catch { }
            int nr = RoadRanges.Roads(tr, db).Count;
            if (nr > 0) l.Add("진입도로 " + nr);
            var recs = ExcavBundleStore.TryLoadAll(db, tr, out _, out bool tooNew);
            if (!tooNew && recs != null && recs.Count > 0) l.Add("터파기 " + recs.Count);
            tr.Commit();
        }
        catch { }
        return l.Count == 0 ? "결과 면" : string.Join(" · ", l);
    }

    /// <summary>원지반의 재료 레이어 — 등고선 · 계곡선 · 표고점 · 원지형 자르기가 옮겨 둔 범위 밖 자료.</summary>
    static System.Collections.Generic.HashSet<string> GroundLayers()
        => new(ImportGisCommand.GroundImportLayers, System.StringComparer.OrdinalIgnoreCase) { CropGroundCommand.CutAwayLayer };

    /// <summary>지운다(묻기 뒤). <paramref name="results"/>가 있으면 [초기화]와 같게 결과까지 · 없으면 원지반과 재료만.
    /// <paramref name="only"/> = 고른 원지반만(null = 전부).
    /// ★재료(등고선 · 계곡선 · 표고점 · 브레이크라인)는 <b>레이어째가 아니라 주인으로</b> 지운다(<see cref="EraseMaterial"/>) — 주인 표시가 있는 재료는 그 원지반이 정말 지워졌거나 없을 때만 ·
    /// 표시 없는 옛 재료는 살아 있는 원지반이 하나도 안 남을 때만. 못 지운 · 물러난 원지반의 재료는 남는다(정의가 물고 있음).
    /// 반환 = (한 일, 되읽어 남은 것 · 알림 — 없으면 빈 글).</summary>
    internal static (string Done, string Left) Erase(Document doc, string results, System.Collections.Generic.ICollection<ObjectId>? only = null, string why = "")
    {
        var db = doc.Database;
        string done;
        int fail = 0;
        if (results.Length > 0)
        {
            var (s, e, _) = ResetCommand.ResetCore(doc, includeImported: true, keepGround: false, onlyGround: only, keepGroundMaterial: true);
            int nT = EraseMaterial(doc, ref fail);   // 결과를 걷은 뒤 — 물러난 원지반은 살아 있어 그 재료가 남음
            done = $"정지 결과까지 지움(결과 전체" + (why.Length > 0 ? " — " + why : "") + $") — 지표면 {s}개 · 객체 {e + nT}개";
        }
        else
        {
            int nS = 0;
            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (only == null)
                {
                    nS = GradingBuilder.CountSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
                    GradingBuilder.EraseSurfacesByBaseName(tr, ImportGisCommand.GroundSurfaceName);
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
            int nE = EraseMaterial(doc, ref fail);
            done = $"원지반 {nS}개 · 재료 {nE:N0}개 지움";
        }
        // 되읽기 — 새 트랜잭션에서 원지반이 정말 없어졌나
        int leftSurf, leftEnt; bool anyGround;
        using (var tr2 = db.TransactionManager.StartTransaction())
        {
            leftSurf = GradingBuilder.CountSurfacesByBaseName(tr2, ImportGisCommand.GroundSurfaceName);
            anyGround = GroundAlive(db, tr2);
            leftEnt = OnLayers(db, tr2, GroundLayers(), untaggedOnly: true).Count;
            tr2.Commit();
        }
        var parts = new System.Collections.Generic.List<string>();
        if (leftSurf > 0) parts.Add(only == null ? $"⚠남은 원지반 {leftSurf}개" : $"남은 원지반 {leftSurf}개");
        if (fail > 0) parts.Add($"⚠못 지움 {fail}개" + (only == null ? " (레이어 잠금 확인)" : ""));
        if (leftEnt > 0) parts.Add(anyGround ? $"옛 도면 등고선 · 표고점 {leftEnt:N0}개는 남김(주인 표시 없음)" : $"⚠남은 재료 {leftEnt:N0}개");
        return (done, string.Join(" · ", parts));
    }

    /// <summary>살아 있는 원지반이 있나 — 원지반 이름의 면 · 또는 물러났어도 원래 이름이 원지반인 면(그 정의가 옛 재료를 물고 있을 수 있음).</summary>
    static bool GroundAlive(Database db, Transaction tr)
    {
        try
        {
            var civ = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            foreach (ObjectId sid in civ.GetSurfaceIds())
                try
                {
                    if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface sf) continue;
                    string nm = sf.Name ?? "";
                    if (SectionCommand.IsBase(nm, ImportGisCommand.GroundSurfaceName)) return true;
                    if (SurfaceRetire.IsRetired(sf) && SectionCommand.IsBase(SurfaceRetire.OriginalName(tr, sf), ImportGisCommand.GroundSurfaceName)) return true;
                }
                catch { }
        }
        catch { return true; }   // 못 읽으면 «있다» — 옛 재료를 지우는 쪽이 되돌릴 수 없어 더 나쁜 실수
        return false;
    }

    /// <summary>재료를 <b>주인으로</b> 지운다 — 주인 표시가 있으면 그 원지반이 이제 없을 때(지워짐 · 처음부터 없음) · 표시가 없으면(옛 도면) 살아 있는 원지반이 하나도 없을 때.
    /// 레이어만 보고 지우지 않는다. 반환 = 지운 수 · <paramref name="fail"/>에 못 지운 수를 더함.</summary>
    internal static int EraseMaterial(Document doc, ref int fail)
    {
        var db = doc.Database;
        int n = 0;
        using var dl = doc.LockDocument();
        using var tr = db.TransactionManager.StartTransaction();
        bool anyGround = GroundAlive(db, tr);
        var ownerAlive = new System.Collections.Generic.Dictionary<string, bool>();
        foreach (var id in OnLayers(db, tr, GroundLayers()))
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                string o = GroundSet.OwnerOf(ent);
                bool kill;
                if (o.Length == 0) kill = !anyGround;
                else
                {
                    if (!ownerAlive.TryGetValue(o, out bool al)) ownerAlive[o] = al = !NoriCommand.FindByHandle(db, o).IsNull;
                    kill = !al;
                }
                if (!kill) continue;
                ent.UpgradeOpen();
                ent.Erase(); n++;
            }
            catch { fail++; }   // 레이어가 잠겼으면 여기로 온다 — 숨기지 않는다
        }
        tr.Commit();
        return n;
    }

    /// <summary>모형 공간에서 그 레이어들에 있는 객체 — 지우는 중에 목록이 흔들리지 않게 먼저 모은다.
    /// <paramref name="untaggedOnly"/> = 주인 표시 없는 옛 재료만.</summary>
    static System.Collections.Generic.List<ObjectId> OnLayers(Database db, Transaction tr, System.Collections.Generic.HashSet<string> layers, bool untaggedOnly = false)
    {
        var ids = new System.Collections.Generic.List<ObjectId>();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || e.Layer == null || !layers.Contains(e.Layer)) continue;
                if (untaggedOnly && GroundSet.OwnerOf(e).Length != 0) continue;
                ids.Add(id);
            }
            catch { }
        return ids;
    }

    /// <summary>★[JACK 1117 «누르면 해당 원지반으로 줌인»] 원지반 범위(가장자리 8 % 여유)로 화면 이동 — 창이 떠 있는 채(명령 안).
    /// 먼저 SetCurrentView, 화면 가운데가 안 바뀌었으면(조용히 안 먹는 자리 — MapPalette 0901 메모) ZOOM 창(W)으로 한 번 더.</summary>
    internal static void ZoomToExt(Editor ed, Extents3d e)
    {
        double w = System.Math.Max(1.0, e.MaxPoint.X - e.MinPoint.X), h = System.Math.Max(1.0, e.MaxPoint.Y - e.MinPoint.Y);
        double cx = (e.MinPoint.X + e.MaxPoint.X) / 2, cy = (e.MinPoint.Y + e.MaxPoint.Y) / 2;
        try
        {
            using var v = ed.GetCurrentView();
            // 화면 좌표(DCS) 가운데 — 평면 보기(위에서)만 정확 · 다른 보기면 ZOOM으로
            bool plan = v.ViewDirection.IsParallelTo(Autodesk.AutoCAD.Geometry.Vector3d.ZAxis) && System.Math.Abs(v.ViewTwist) < 1e-9;
            if (plan)
            {
                double asp = v.Width / System.Math.Max(v.Height, 1e-9);
                double vh = System.Math.Max(h, w / System.Math.Max(asp, 1e-9)) * 1.16;
                v.CenterPoint = new Autodesk.AutoCAD.Geometry.Point2d(cx - v.Target.X, cy - v.Target.Y);
                v.Height = vh; v.Width = vh * asp;
                ed.SetCurrentView(v);
                Autodesk.AutoCAD.ApplicationServices.Application.UpdateScreen();
                using var r = ed.GetCurrentView();
                if (System.Math.Abs(r.CenterPoint.X + r.Target.X - cx) < w * 0.01 + 0.01 && System.Math.Abs(r.CenterPoint.Y + r.Target.Y - cy) < h * 0.01 + 0.01) return;
            }
        }
        catch { }
        try
        {
            ed.Command("_.ZOOM", "_W",
                new Autodesk.AutoCAD.Geometry.Point3d(e.MinPoint.X - w * 0.08, e.MinPoint.Y - h * 0.08, 0),
                new Autodesk.AutoCAD.Geometry.Point3d(e.MaxPoint.X + w * 0.08, e.MaxPoint.Y + h * 0.08, 0));
        }
        catch (System.Exception ex) { ed.WriteMessage("\n[원지반 삭제] 화면 이동 못 함 — " + ex.Message); }
    }

}
