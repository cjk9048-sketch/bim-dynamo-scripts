using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CivilDb = Autodesk.Civil.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[JACK 0901 "기타에 <b>원지형 자르기</b> 기능을 넣어 줘. 단추를 누르면 드래그로 박스를
/// 그리게 하고 그 박스 안에 지형만 남고 나머지는 지우는 거. 수치지도를 불러 버리면 계획지역보다
/// 너무 클 수 있어서 그래"]
///
/// <para>수치지도 도엽 한 장은 <b>2×3km</b>다. 현장이 200m면 지형의 99%가 쓸모없이 무겁다 —
/// 삼각형 수만 개가 종단·횡단·수량 계산을 매번 따라다닌다.</para>
///
/// <para>★<b>지우기 전에 만들고, 만든 것을 확인한 뒤에 지운다.</b> 자르기가 실패했는데 원본을 먼저
/// 지우면 <b>지형이 통째로 사라진다</b> — 되돌릴 방법이 없다. 그래서 ①잘라 만들고 ②삼각형이
/// 있는지 재고 ③그때 비로소 원본을 지운다.</para>
///
/// <para>원본 등고선·표고점 중 <b>박스 밖에 통째로 있는 것</b>도 같이 지운다 — 안 지우면
/// 도면이 계속 무겁고, 다음에 [다시 만들기]를 하면 잘라낸 자리가 되살아난다.</para>
///
/// <para>★★★[v111.9 · JACK 1103 «어느 단계에서나 수행하면 모든 지표면이 잘렸으면»] 종전엔 «원지반» 한 장에만 Outer를 넣었다(정지면_DH 255,075 그대로).
/// 이제 <see cref="GroundCropBuilder"/>가 땅 크기 면을 <b>모두</b> 상자 밖 Hide 네 띠로 숨긴다 — 설계 자리가 상자 밖이면 거절(빨간 표시 · 다시 묻기 · 도면 0줄) ·
/// 확인이 어긋나면 통째로 물림 · 핸들 그대로라 종단 · 횡단 · 붙여넣기 · 도장 참조를 하나도 안 고친다(계획_원지형자르기.md).</para></summary>
public sealed class CropGroundCommand
{
    /// <summary>범위 밖 원본(등고선·표고점)을 <b>옮겨 두는</b> 레이어 — 지우지 않고 끈다.
    /// <para>지표면 정의가 이 선들을 물고 있어서, 지우면 도구공간 <b>등고선에 느낌표</b>가 뜬다.
    /// 옮겨 두면 정의는 그대로 살아 있고 화면·출력에서만 빠진다.</para></summary>
    internal const string CutAwayLayer = "DH-원지반자료(범위밖)";

    /// <summary>화면 없는 Civil 시험 고리 — 드래그 대신 상자를 차례로 꺼내 쓴다(x0 y0 x1 y1 · 다 쓰면 Esc와 같음). 평소엔 null.</summary>
#pragma warning disable CS0649   // 시험(반사)만 씀
    internal static List<double[]>? _cropBoxesForTest;
#pragma warning restore CS0649

    /// <summary>남길 범위 드래그(UCS → WCS · 축 정렬). 거짓 = 취소.</summary>
    static bool AskBox(Editor ed, bool again, out double x0, out double y0, out double x1, out double y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (_cropBoxesForTest != null)
        {
            if (_cropBoxesForTest.Count == 0) return false;
            var b = _cropBoxesForTest[0]; _cropBoxesForTest.RemoveAt(0);
            x0 = Math.Min(b[0], b[2]); x1 = Math.Max(b[0], b[2]); y0 = Math.Min(b[1], b[3]); y1 = Math.Max(b[1], b[3]);
            return true;
        }
        var p1 = ed.GetPoint(again ? "\n[원지형 자르기] 빨간 네모를 덮도록 다시 — 첫 번째 모서리 (Esc=끝): "
                                   : "\n[원지형 자르기] 남길 범위의 첫 번째 모서리 (Esc=취소): ");
        if (p1.Status != PromptStatus.OK) return false;
        var p2 = ed.GetCorner(new PromptCornerOptions("\n반대쪽 모서리: ", p1.Value));
        if (p2.Status != PromptStatus.OK) return false;
        var ucs = ed.CurrentUserCoordinateSystem;
        var w1 = p1.Value.TransformBy(ucs);
        var w2 = p2.Value.TransformBy(ucs);
        x0 = Math.Min(w1.X, w2.X); x1 = Math.Max(w1.X, w2.X);
        y0 = Math.Min(w1.Y, w2.Y); y1 = Math.Max(w1.Y, w2.Y);
        return true;
    }

    /// <summary>★[계획 4장] 빨간 표시(임시 그래픽 — 도면 · 실행 취소 · DBMOD와 무관) — 필요한 가장 작은 네모(D + 5 m) · 상자 밖으로 나간 조각만. 화면 없는 Civil은 안 그림(창 틀 없음).</summary>
    static void PaintRefusal(Document doc, Core.GroundCrop.Cover cov, List<PickMark> marks)
    {
        if (SelfCheckLog.Headless) return;
        foreach (var m in marks) { try { m.Dispose(); } catch { } }
        marks.Clear();
        try
        {
            var n = cov.Need;
            if (!n.IsNone)
                marks.Add(PickMark.PaintPath(doc, new List<Core.Point3> { new(n.X0, n.Y0, 0), new(n.X1, n.Y0, 0), new(n.X1, n.Y1, 0), new(n.X0, n.Y1, 0) }, true));
            int k = 0;
            foreach (var o in cov.Outs)
                foreach (var part in o.Parts)
                {
                    if (part.Count < 2 || ++k > 200) continue;   // 표본선이 수백 개여도 화면이 빨개지지 않게
                    marks.Add(PickMark.PaintPath(doc, part.Select(q => new Core.Point3(q.X, q.Y, 0)).ToList(), false));
                }
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n■ DHCROP 빨간 표시 못 함 — " + ex.Message); } catch { } }
    }

    [CommandMethod("DHCROP")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;
        using var job = new SelfCheckLog.Job("원지형 자르기", doc.Name);
        var marks = new List<PickMark>();

        try
        {
            bool again = false;
            while (true)
            {
                // ── ① 남길 범위를 드래그로 ────────────────────────────────────────
                if (!AskBox(ed, again, out double x0, out double y0, out double x1, out double y1))
                {
                    ed.WriteMessage(again ? "\n[원지형 자르기] 끝 — 아무것도 바꾸지 않았습니다" : "\n[원지형 자르기] 취소");
                    job.DoneInfo(again ? "거절 뒤 끝(도면 그대로)" : "취소");
                    return;
                }
                if (x1 - x0 < 1.0 || y1 - y0 < 1.0)
                {
                    Refuse(ed, $"범위가 너무 작습니다(가로 {x1 - x0:F1}m × 세로 {y1 - y0:F1}m).\n\n" +
                               "두 모서리를 서로 떨어뜨려 다시 잡아 주세요.");
                    job.Done(false, "범위가 너무 작음");
                    return;
                }

                // ── ② 원지형(원지반) — 없거나 상자와 안 겹치면 거절 ─────────────────
                ObjectId gid = FindGround(db, out string gname, out int triBefore);
                if (gid.IsNull)
                {
                    Refuse(ed, "자를 원지형이 없습니다.\n\n[원지반 가져오기]로 먼저 지형을 만들어 주세요.");
                    job.Done(false, "원지형 없음");
                    return;
                }
                Extents3d gext;
                try
                {
                    using var trE = db.TransactionManager.StartTransaction();
                    gext = ((Entity)trE.GetObject(gid, OpenMode.ForRead)).GeometricExtents;
                    trE.Commit();
                }
                catch (System.Exception eex)
                {
                    Refuse(ed, "지형 범위를 못 읽었습니다 — 원본은 그대로 두었습니다.\n\n" + eex.Message);
                    job.Done(false, "지형 범위 못 읽음");
                    return;
                }
                if (x1 <= gext.MinPoint.X || x0 >= gext.MaxPoint.X ||
                    y1 <= gext.MinPoint.Y || y0 >= gext.MaxPoint.Y)
                {
                    Refuse(ed, "그 범위에는 지형이 없습니다 — 원본은 그대로 두었습니다.\n\n"
                             + $"지형은 {gext.MinPoint.X:F0},{gext.MinPoint.Y:F0} ~ {gext.MaxPoint.X:F0},{gext.MaxPoint.Y:F0} 에 있습니다.");
                    job.Done(false, "범위에 지형 없음");
                    return;
                }
                ed.WriteMessage($"\n[원지형 자르기] {x1 - x0:F0}×{y1 - y0:F0}m 밖의 지표면을 모두 숨깁니다…");

                // ── ③ 설계 자리 검사 → 대상 면 모두 네 띠 · 재작성 · 확인(어긋나면 통째로 물림) ─────
                var box = new Core.GroundCrop.Box(x0, y0, x1, y1);
                var res = GroundCropBuilder.Run(doc, box, gid);
                if (res.Refused && res.Cover != null)
                {
                    // ★[Q1 · JACK 1103 «설계된 자리를 자르면 거절»] 아무것도 안 바꿈 · 빨간 표시를 남긴 채 다시 묻기(Esc = 끝)
                    PaintRefusal(doc, res.Cover, marks);
                    string items = string.Join(" · ", res.Red.Take(6)) + (res.Red.Count > 6 ? $" 외 {res.Red.Count - 6}" : "");
                    ed.WriteMessage($"\n[원지형 자르기] ⚠설계 자리가 상자 밖 — {items}");
                    if (!SelfCheckLog.Headless)
                        AcadApp.ShowAlertDialog("원지형 자르기 — 설계된 자리(빨간 선)가 상자 밖으로 나갑니다.\n\n빨간 네모를 덮도록 다시 그려 주세요.\n\n" + items);
                    job.Note("거절 — " + res.Why);
                    again = true;
                    continue;
                }
                foreach (var m in marks) { try { m.Dispose(); } catch { } }
                marks.Clear();
                if (res.RolledBack)
                {
                    ed.WriteMessage("\n[원지형 자르기] ⚠못 잘랐습니다 — 원래대로 두었습니다(" + res.Why + ")");
                    if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("원지형 자르기 — ⚠못 잘랐습니다 — 원래대로 두었습니다.\n\n" + res.Why);
                    job.Done(false, "되돌림 — " + res.Why);
                    return;
                }
                if (!res.Ok)
                {
                    Refuse(ed, "자르지 못했습니다 — " + res.Why);
                    job.Done(false, res.Why);
                    return;
                }

                // ── ④ 박스 <b>밖에 통째로</b> 있는 원본 선·점 정리(원지반 자료만 · 지우지 않고 끈 레이어로) ──────
                int wiped = res.Nothing ? 0 : WipeOutside(doc, db, x0, y0, x1, y1);

                DrawOrderFix.Apply(db);
                ed.Regen();
                ZoomTo(ed, x0, y0, x1, y1);

                string done = res.Done + (wiped > 0 ? $" · 범위 밖 원본 {wiped:N0}개 껐음(지우지 않음)" : "");
                ed.WriteMessage($"\n[원지형 자르기] {done}" + (res.GrowNote.Length > 0 ? "\n[원지형 자르기] " + res.GrowNote : ""));
                if (res.FinalNote.StartsWith("⚠")) ed.WriteMessage("\n[원지형 자르기] " + res.FinalNote);
                job.Done(res.Stale == 0 && !res.FinalNote.StartsWith("⚠"), done + (res.GrowNote.Length > 0 ? " · " + res.GrowNote : "") + $" · {res.Line.Trim()}");
                // ★★★[JACK 0903 "잘되는데 별도 팝업은 안 띄웠으면 좋겠어"] 잘된 일은 화면이 말해 준다 — 명령창 · 로그에만.
                //   ★[Q3 · JACK 1103] 다시 키웠는데 못 커진 DH 면은 한 줄로 알림(위 명령창 줄).

                // ★★★[JACK 0902 "계속 등고선 부분에 느낌표 떠" · "재작성 누르면 없어지긴 해"]
                //   이 명령 <b>안에서</b> 부른 <c>Rebuild()</c>는 원지반 자료를 옮긴(WipeOutside) 여파 앞이다 —
                //   <b>명령을 하나 더 걸어</b> 이 명령이 끝난 뒤에 다시 짓는다(<c>SendStringToExecute</c>).
                //   ★[v111.9 · V87 실측] 자른 면을 붙여넣은 스냅샷 없는 면 · 체적면은 소스를 다시 지은 <b>그 명령 안에서</b> 다시 지어도 명령 끝에 «구식»이 붙는다 → 한 명령 더 뒤(DHCROPDEPS)에서.
                if (!res.Nothing) try { doc.SendStringToExecute(res.Chain > 0 ? "DHGNDREBUILD DHCROPDEPS " : "DHGNDREBUILD ", true, false, true); } catch { }
                return;
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[원지형 자르기 오류] " + ex.Message);
            try { DiagLog.AppendCarry("\n■ DHCROP 오류 — " + ex + "\n"); } catch { }
            if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("원지형 자르기 중 오류:\n" + ex.Message);
        }
        finally
        {
            foreach (var m in marks) { try { m.Dispose(); } catch { } }
        }
    }

    /// <summary>★★★[JACK 0902] <b>자르기가 끝난 뒤에 다시 짓는다.</b>
    /// <para>도구공간 <b>정의 → 등고선</b>에 남는 느낌표를 지우기 위해서다 —
    /// 자르기 <b>안에서</b> 짓면 등고선을 지운 여파가 아직 반영되기 전이라 소용이 없다.</para>
    /// <para>사람이 따로 부를 일도 있다 — 지표면에 느낌표가 떴 때 <c>DHGNDREBUILD</c>.</para></summary>
    [CommandMethod("DHGNDREBUILD")]
    public void RebuildGround()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Editor ed = doc.Editor;
        Database db = doc.Database;
        try
        {
            ObjectId gid = FindGround(db, out string gname, out _);
            if (gid.IsNull) { ed.WriteMessage("\n[지표면 재작성] 원지형을 못 찾았습니다."); return; }
            string how = "—"; string stale = "?"; int tri = 0;
            using (var dl = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool okR = true;
                if (tr.GetObject(gid, OpenMode.ForWrite) is CivilDb.TinSurface gs)
                {
                    try { gs.Rebuild(); how = "OK"; } catch (System.Exception rex) { how = "⚠" + rex.Message + "(물림 — 그대로)"; okR = false; }
                    try { tri = gs.Triangles.Count; } catch { }
                }
                // ★[v111.9 · 설치 전 검토 L4] 재작성이 던졌으면 커밋하지 않는다(실패한 재작성을 남기지 않음 — 1013 메모와 같은 법)
                if (okR) tr.Commit(); else tr.Abort();
            }
            // ★되읽기 — 느낌표를 정하는 값을 <b>새 트랜잭션</b>에서 재다.
            try
            {
                using var tr2 = db.TransactionManager.StartTransaction();
                if (tr2.GetObject(gid, OpenMode.ForRead) is CivilDb.Surface gs2)
                    stale = gs2.IsOutOfDate ? "⚠아직 최신 아님" : "최신";
                tr2.Commit();
            }
            catch { }
            // ★[v111.9 · V87 final 실측] 원지반 자료(등고선 · 표고점)를 옮긴 여파는 «원지반으로 고른 면»(세모가 가장 많은 사람 면)이 아닌 면에도 간다 —
            //   사람 면이 원지반보다 세모가 많으면 그 면이 골라져 진짜 원지반이 구식으로 남았다. 자르기 기록에 든 스냅샷 없는 면 중 구식인 것도 다시 짓는다(자르지 않은 도면은 기록이 없어 그대로).
            var more = new List<string>();
            foreach (var rid in GroundCropBuilder.RecordedIds(db))
            {
                if (rid == gid) continue;
                try
                {
                    using var dlR = doc.LockDocument();
                    using var trR = db.TransactionManager.StartTransaction();
                    if (trR.GetObject(rid, OpenMode.ForRead) is CivilDb.TinSurface rs && !rs.HasSnapshot && rs.IsOutOfDate)
                    {
                        rs.UpgradeOpen(); rs.Rebuild(); more.Add(rs.Name);
                    }
                    trR.Commit();
                }
                catch (System.Exception rx) { more.Add("⚠" + rx.Message); }
            }
            string extra = more.Count > 0 ? " · 자른 면도 다시 지음 " + string.Join(" · ", more) : "";
            ed.WriteMessage($"\n[지표면 재작성] '{gname}' — {how} · 상태 {stale} · 삼각형 {tri:N0}개" + extra);
            try { DiagLog.Append($"\n■ DHGNDREBUILD — '{gname}' {how} · 상태 {stale} · 삼각형 {tri}" + extra + "\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지표면 재작성 오류] " + ex.Message);
        }
    }

    /// <summary>★[v111.9 · V86 «(옛)» · V87] <b>자른 면을 붙여넣은 스냅샷 없는 면 · 체적면을 다시 짓는다</b> — DHCROP · DHGNDREBUILD <b>뒤</b> 명령으로 걸린다.
    /// <para>소스(원지반 · 자른 면)를 다시 지은 명령 안에서 그 딸린 면을 다시 지어도 명령 끝에 «구식»이 붙는다(화면 없는 Civil 실측 — 소스를 다시 짓지 않는 다음 명령에서 지어야 남는다).</para></summary>
    [CommandMethod("DHCROPDEPS")]
    public void RebuildCropDependents()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        Database db = doc.Database;
        try
        {
            var seeds = GroundCropBuilder.RecordedIds(db);
            ObjectId gid = FindGround(db, out _, out _);
            if (!gid.IsNull) seeds.Add(gid);
            string deps; List<ObjectId> built;
            using (doc.LockDocument()) deps = GroundCropBuilder.RebuildDependents(db, seeds, out built);
            // ★[v111.9 · 설치 전 검토 M1] 끝 확인 — 자른 면 · 다시 지은 면이 아직 구식이면 ⚠(사슬이 끊겼거나 재작성 실패 — 말없이 남기지 않음)
            var left = GroundCropBuilder.StaleOf(db, seeds.Concat(built));
            string warn = left.Count > 0 ? " · ⚠아직 구식 " + string.Join(" · ", left) + " — 도구공간에서 [재작성]" : "";
            if (deps.Length + warn.Length > 0) doc.Editor.WriteMessage("\n[원지형 자르기] " + deps + warn);
            try { DiagLog.Append("\n■ DHCROPDEPS — " + (deps.Length > 0 ? deps : "딸린 면 없음") + warn + "\n"); } catch { }
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\n[원지형 자르기] 딸린 면 다시 짓기 오류 " + ex.Message); }
    }

    /// <summary>자를 지형을 고른다 — 규칙은 <see cref="ImportGisCommand.FindGroundSurface"/> 하나뿐이다(§50).</summary>
    private static ObjectId FindGround(Database db, out string name, out int tris)
        => ImportGisCommand.FindGroundSurface(db, out name, out tris);

    /// <summary>박스 <b>밖에 통째로</b> 있는 원본 등고선·표고점을 지운다.
    /// <para>걸쳐 있는 것은 <b>남긴다</b> — 자른 경계에서 지형이 어떻게 이어졌는지 남겨 두는 편이 낫다.</para></summary>
    private static int WipeOutside(Document doc, Database db, double x0, double y0, double x1, double y1)
    {
        int n = 0;
        try
        {
            using var dl = doc.LockDocument();
            using var tr = db.TransactionManager.StartTransaction();
            var want = new HashSet<string>(ImportGisCommand.GroundImportLayers, StringComparer.OrdinalIgnoreCase);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            // ★훑으면서 손대지 않는다 — 모아 두고 나서 한다(이 저장소가 데인 자리).
            var doomed = new List<ObjectId>();
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                    if (!want.Contains(e.Layer)) continue;
                    var ext = e.GeometricExtents;
                    if (ext.MaxPoint.X < x0 || ext.MinPoint.X > x1 ||
                        ext.MaxPoint.Y < y0 || ext.MinPoint.Y > y1) doomed.Add(id);
                }
                catch { }
            }
            // ★★★[JACK 0903 "추천방향대로 할게 — 중요한 건 도면 수행 시 무거우면 안 돼"]
            //   <b>지우지 않고 옮겨서 끈다.</b> 이 저장소가 <see cref="ImportGisCommand.HideLayer"/>에
            //   못 박아 둔 규칙이다 — <i>"지표면은 이 선들을 자료로 물고 있어서 지우면 정의가 끊긴다."</i>
            //   지웠기 때문에 도구공간 <b>등고선에 느낌표</b>가 떴고, 나는 그것을 재작성으로 <b>덮고</b> 있었다.
            //
            //   ★<b>동결(Freeze)이 아니라 끄기(Off)</b>다 — 같은 주석이 <i>"동결된 레이어는 지표면 자료로
            //   못 쓰는 판이 있다"</i>고 적어 두었다. 규칙을 두 곳에 나눠 적지 않는다.
            //
            //   <b>무거워지지 않는가.</b> 무게는 <b>삼각형</b>이지 폴리선이 아니다 —
            //   지표면은 바깥 경계로 그대로 잘려 있고(실측 248,283 → 50,460), 남는 것은
            //   꺼진 레이어의 폴리선 몇백 개다. 로그의 <c>삼각형 N→M</c>이 그대로면 무게도 그대로다.
            var offLayer = ImportGisCommand.EnsureLayer(db, tr, CutAwayLayer, 8);   // 8 = 어두운 회색
            foreach (var id in doomed)
            {
                try { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).LayerId = offLayer; n++; }
                catch { }
            }
            if (n > 0) ImportGisCommand.HideLayer(db, tr, CutAwayLayer);
            tr.Commit();
        }
        catch { }
        return n;
    }

    private static void ZoomTo(Editor ed, double x0, double y0, double x1, double y1)
    {
        double w = Math.Max(1.0, x1 - x0), h = Math.Max(1.0, y1 - y0);
        try
        {
            ed.Command("_.ZOOM", "_W",
                new Point3d(x0 - w * 0.08, y0 - h * 0.08, 0),
                new Point3d(x1 + w * 0.08, y1 + h * 0.08, 0));
        }
        catch { try { ed.Command("_.ZOOM", "_E"); } catch { } }
    }

    /// <summary>★★★[JACK 0902 "원지형 자르기가 여전히 안돼"] <b>실패 이유를 로그에 남긴다.</b>
    /// <para>종전엔 <b>성공했을 때만</b> <c>DiagLog</c>에 적었다. 그래서 실패하면 화면 안내만 뜨고
    /// <b>기록이 하나도 안 남아</b>, 다음에 원인을 물으면 답할 것이 없었다 —
    /// 이 저장소의 규칙은 <i>"기하 버그는 계측부터"</i>인데 정작 여기에 계측이 없었다.</para></summary>
    private static void Refuse(Editor ed, string msg)
    {
        string one = msg.Replace("\n\n", " — ").Replace("\n", " ");
        ed.WriteMessage("\n[원지형 자르기] " + one);
        try { DiagLog.AppendCarry("\n■ DHCROP 실패 — " + one + "\n"); } catch { }
        if (!SelfCheckLog.Headless) AcadApp.ShowAlertDialog("원지형 자르기 — 하지 못했습니다\n\n" + msg);   // 화면 없는 Civil은 창 틀이 없어 알림 상자에서 죽는다(v103.1.9)
    }
}
