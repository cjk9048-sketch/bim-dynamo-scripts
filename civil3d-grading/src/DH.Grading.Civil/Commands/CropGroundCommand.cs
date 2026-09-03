using System;
using System.Collections.Generic;
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
/// 도면이 계속 무겁고, 다음에 [다시 만들기]를 하면 잘라낸 자리가 되살아난다.</para></summary>
public sealed class CropGroundCommand
{
    /// <summary>범위 밖 원본(등고선·표고점)을 <b>옮겨 두는</b> 레이어 — 지우지 않고 끈다.
    /// <para>지표면 정의가 이 선들을 물고 있어서, 지우면 도구공간 <b>등고선에 느낌표</b>가 뜬다.
    /// 옮겨 두면 정의는 그대로 살아 있고 화면·출력에서만 빠진다.</para></summary>
    private const string CutAwayLayer = "DH-원지반자료(범위밖)";

    [CommandMethod("DHCROP")]
    public void Run()
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        GradingSettings.SyncToDocument(doc);
        Editor ed = doc.Editor;
        Database db = doc.Database;

        try
        {
            // ── ① 남길 범위를 드래그로 ────────────────────────────────────────
            var p1 = ed.GetPoint("\n[원지형 자르기] 남길 범위의 첫 번째 모서리 (Esc=취소): ");
            if (p1.Status != PromptStatus.OK) { ed.WriteMessage("\n[원지형 자르기] 취소"); return; }
            var p2 = ed.GetCorner(new PromptCornerOptions("\n반대쪽 모서리: ", p1.Value));
            if (p2.Status != PromptStatus.OK) { ed.WriteMessage("\n[원지형 자르기] 취소"); return; }

            var ucs = ed.CurrentUserCoordinateSystem;
            var w1 = p1.Value.TransformBy(ucs);
            var w2 = p2.Value.TransformBy(ucs);
            double x0 = Math.Min(w1.X, w2.X), x1 = Math.Max(w1.X, w2.X);
            double y0 = Math.Min(w1.Y, w2.Y), y1 = Math.Max(w1.Y, w2.Y);
            if (x1 - x0 < 1.0 || y1 - y0 < 1.0)
            {
                Refuse(ed, $"범위가 너무 작습니다(가로 {x1 - x0:F1}m × 세로 {y1 - y0:F1}m).\n\n" +
                           "두 모서리를 서로 떨어뜨려 다시 잡아 주세요.");
                return;
            }

            // ── ② 자를 지형 찾기 ─────────────────────────────────────────────
            ObjectId gid = FindGround(db, out string gname, out int triBefore);
            if (gid.IsNull)
            {
                Refuse(ed, "자를 원지형이 없습니다.\n\n[원지반 가져오기]로 먼저 지형을 만들어 주세요.");
                return;
            }
            ed.WriteMessage($"\n[원지형 자르기] '{gname}' 삼각형 {triBefore:N0}개 → "
                          + $"{x1 - x0:F0}×{y1 - y0:F0}m로 자릅니다…");

            // ── ③ 범위가 지형과 <b>겹치는지 먼저</b> ──────────────────
            //   경계를 붙이는 것은 원본을 <b>직접</b> 고치는 일이라, 붙인 뒤에 "지형이 없었다"를 알면 늦다.
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
                return;
            }
            if (x1 <= gext.MinPoint.X || x0 >= gext.MaxPoint.X ||
                y1 <= gext.MinPoint.Y || y0 >= gext.MaxPoint.Y)
            {
                Refuse(ed, "그 범위에는 지형이 없습니다 — 원본은 그대로 두었습니다.\n\n"
                         + $"지형은 {gext.MinPoint.X:F0},{gext.MinPoint.Y:F0} ~ {gext.MaxPoint.X:F0},{gext.MaxPoint.Y:F0} 에 있습니다.");
                return;
            }

            // ── ④ <b>바깥 경계</b>를 붙여 자른다 ─────────────────────
            int triAfter = 0;
            string finalName = gname;
            int bndB = -1, bndA = -1;
            bool cropOk = false;
            try
            {
                using var dl = doc.LockDocument();
                using var tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(gid, OpenMode.ForWrite) is not CivilDb.TinSurface gs)
                {
                    tr.Abort();
                    Refuse(ed, "지형을 열지 못했습니다 — 원본은 그대로 두었습니다.");
                    return;
                }
                var pts = new Point3dCollection
                {
                    new Point3d(x0, y0, 0), new Point3d(x1, y0, 0),
                    new Point3d(x1, y1, 0), new Point3d(x0, y1, 0),
                };
                int bndBefore = -1;
                try { bndBefore = gs.BoundariesDefinition.Count; } catch { }
                // ★★★[검토 0902 HIGH] <b>지난 경계를 먼저 뺀다.</b> 안 그러면 자를 때마다 Outer가 쌓인다
                //   (JACK 로그 실측: <c>경계 1→2개</c>). 두 개가 되면 Civil이 교집합으로 보든 합집합으로 보든
                //   좋을 것이 없다 — 교집합이면 정의가 무한히 불어나고, 합집합이면 <b>자르기가 안 먹는다</b>.
                //   <see cref="GradingBuilder.ReplaceOuterBoundary"/>가 쓰는 그 방식이다.
                try { var bdz = gs.BoundariesDefinition; while (bdz.Count > 0) bdz.RemoveAt(0); } catch { }
                // ★★★[JACK 0902 로그 실측] <c>midOrdinateDistance</c>는 <b>0보다 커야 한다</b> —
                //   사각형이라 실제로 쓰이지도 않는 값인데(호를 선분으로 쪼갬 때만 쓴다)
                //   API가 검사한다. <c>0.0</c>을 넘겨 이러했다:
                //   <c>The value of midOrdinateDistance should greater than zero.</c>
                //
                // ★★★[JACK 0902 "깔끔하게 네모반듯히 안 잘리고 경계가 톱니처럼 잘려"]
                //   마지막 인자 <c>useNonDestructiveBreakline</c>을 <b>거꾸로 알고 있었다</b>.
                //     · <c>true</c>  → 경계선을 <b>비파괴 브레이크라인</b>으로 심어, 경계를 가로지르는
                //       삼각형을 <b>그 자리에서 정확히 잘라</b> 모서리가 <b>네모 반듯해진다</b>.
                //     · <c>false</c> → 경계를 물려높은 삼각형을 <b>통째로 버려</b> 모서리가 <b>톱니</b>가 된다.
                //   처음엔 <c>false</c>를 "실제로 자른다"로 읽고 넣었다 — 반대였다.
                gs.BoundariesDefinition.AddBoundaries(pts, 0.1, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
                int bndAfter = -1;
                try { bndAfter = gs.BoundariesDefinition.Count; } catch { }
                try { finalName = gs.Name; } catch { }
                tr.Commit();
                cropOk = true;
                bndB = bndBefore; bndA = bndAfter;
            }
            catch (System.Exception cex)
            {
                Refuse(ed, "자르지 못했습니다 — 원본은 그대로 두었습니다.\n\n" + cex.Message);
                return;
            }

            // ★★★[JACK 0902 "서버지표면 가져오기 후 원지형 자르기 했는데 여전히 등고선에 느낌표 떠"]
            //   <b>순서가 거꾸로 있었다.</b> 느낌표는 도구공간의 <b>정의 → 등고선</b>에 붙고
            //   거기서 위로(정의·원지반·지표면) 번졌다 — 지표면이 최신이 아니어서가 아니라
            //   (로그: <c>재작성 OK · 상태 최신</c>) <b>정의가 가리키는 등고선이 사라졌기</b> 때문이다.
            //   <see cref="WipeOutside"/>가 범위 밖 등고선을 지우는데, 그것을 <b>재작성 뒤에</b> 했다.
            //   → <b>지우기를 먼저, 재작성을 나중에.</b> 그러면 Civil이 <b>남은 등고선만으로</b> 다시 짓고
            //     정의에 깨진 참조가 안 남는다. 이 저장소 규칙과 같다 — <i>"되읽기는 마지막 쓰기 뒤에."</i>

            // ── ⑥ 박스 <b>밖에 통째로</b> 있는 원본 선·점 정리 ────────────────
            // ── ⑤ 재작성은 <b>커밋한 뒤 새 트랜잭션에서</b> ──────────────
            //   ★★★[JACK 0902 "자르기하면 잘 잘리긴하는데 도구공간에 느낌표가 떠"]
            //   느낌표 = <b>지표면이 최신이 아니다(재작성 필요)</b>는 표시다.
            //   종전엔 <c>Rebuild()</c>를 <b>경계를 넣은 그 트랜잭션 안</b>에서 불렀다.
            //   정의 변경은 <b>커밋할 때</b> 확정되므로, 그 안에서 재작성하면
            //   <b>바뀌기 전 상태</b>를 다시 만들고 커밋 순간 다시 '최신 아님'이 된다.
            //   ★이 저장소 규칙 그대로다: <i>"되읽기는 마지막 쓰기 뒤, 새 트랜잭션에서."</i>
            string rebuilt = "—";
            try
            {
                using var dl2 = doc.LockDocument();
                using var tr2 = db.TransactionManager.StartTransaction();
                if (tr2.GetObject(gid, OpenMode.ForWrite) is CivilDb.TinSurface gs2)
                {
                    try { gs2.Rebuild(); rebuilt = "OK"; } catch (System.Exception rex) { rebuilt = rex.Message; }
                    try { triAfter = gs2.Triangles.Count; } catch { }
                }
                tr2.Commit();
            }
            catch (System.Exception r2) { rebuilt = r2.Message; }

            // ★★★[검토 0902 CRITICAL] <b>빈 결과를 "완료"라고 하지 않는다.</b>
            //   종전 판에 있던 <c>triAfter &lt;= 0</c> 관문이 이번 개편에서 통째로 사라졌다.
            //   앞의 겹침 검사(③)는 <b>경계상자끼리만</b> 보므로, L자 지형의 오목한 쪽을 찍으면
            //   통과하는데 삼각형은 0이 된다 — 그런데 화면엔 "원지형 자르기 완료"가 떴다.
            //   ★<b>지우기는 이 검사 뒤로</b> 옮긴다 — 앞에 두면 되돌릴 것이 없다.
            if (triAfter <= 0)
            {
                try
                {
                    using var dlZ = doc.LockDocument();
                    using var trZ = db.TransactionManager.StartTransaction();
                    if (trZ.GetObject(gid, OpenMode.ForWrite) is CivilDb.TinSurface gsZ)
                    {
                        var bdZ = gsZ.BoundariesDefinition;
                        while (bdZ.Count > 0) bdZ.RemoveAt(0);       // 방금 넣은 경계를 뺀다
                        try { gsZ.Rebuild(); } catch { }
                    }
                    trZ.Commit();
                }
                catch { }
                Refuse(ed, "그 범위에는 지형이 없습니다 — 원본은 그대로 두었습니다.\n\n"
                         + "지형이 있는 자리를 다시 잡아 주세요(경계상자는 겹쳐도 삼각형이 없을 수 있습니다).");
                return;
            }



            int wiped = WipeOutside(doc, db, x0, y0, x1, y1);

            // ★되읽기 — 정말 최신인가를 <b>또 다른</b> 트랜잭션에서 재다(느낌표를 결정하는 값).
            string stale = "?";
            try
            {
                using var tr3 = db.TransactionManager.StartTransaction();
                if (tr3.GetObject(gid, OpenMode.ForRead) is CivilDb.Surface gs3)
                    stale = gs3.IsOutOfDate ? "⚠아직 최신 아님" : "최신";
                tr3.Commit();
            }
            catch { }

            try
            {
                DiagLog.AppendCarry($"\n■ DHCROP — 경계 {bndB}→{bndA}개 · 재작성 {rebuilt} · 상태 {stale}"
                             + $" · 삼각형 {triBefore}→{triAfter} · 범위 {x0:F1},{y0:F1}~{x1:F1},{y1:F1}"
                             + $" · 지형범위 {gext.MinPoint.X:F1},{gext.MinPoint.Y:F1}~{gext.MaxPoint.X:F1},{gext.MaxPoint.Y:F1}\n");
            }
            catch { }

            // ── ⑥ 박스 <b>밖에 통째로</b> 있는 원본 선·점 정리 ────────────────

            DrawOrderFix.Apply(db);
            ed.Regen();
            ZoomTo(ed, x0, y0, x1, y1);

            string done = $"삼각형 {triBefore:N0} → {triAfter:N0}개"
                        + (wiped > 0 ? $" · 범위 밖 원본 {wiped:N0}개 껐음(지우지 않음)" : "");
            ed.WriteMessage($"\n[원지형 자르기] {done} · '{finalName}'");
            // ★★★[JACK 0903 "잘되는데 별도 팝업은 안 띄웠으면 좋겠어" · "DXF 가져오기도"]
            //   잘된 일은 <b>화면이 이미 말해 준다</b> — 결과가 그려지고 그 범위로 확대된다.
            //   거기에 확인 단추를 더하면 클릭만 하나 늘 뿐이다.
            //   명령창과 로그에는 그대로 남는다 — 나중에 숫자를 짚을 수 있게.
            //   ※못 한 때와 오류는 그대로 띄운다 — 아무 일도 안 일어난 것을 명령창만으로 알리면 모르고 지나간다.
            // ★[검토 0902] 한 실행에 <c>■ DHCROP</c>가 두 줄 찍히던 것 — 단계 로그(위)만 남긴다.

            // ★★★[JACK 0902 "계속 등고선 부분에 느낌표 떠" · "재작성 누르면 없어지긴 해"]
            //   그 한마디가 답이었다. 이 명령 <b>안에서</b> 부른 <c>Rebuild()</c>는 <b>너무 이르다</b> —
            //   범위 밖 등고선을 지운 여파를 Civil이 <b>명령이 끝난 뒤</b>에 정의에 반영하기 때문이다.
            //   그래서 우리 로그는 <c>재작성 OK · 상태 최신</c>이라 찍는데도 도구공간엔 느낌표가 남고,
            //   사람이 [재작성]을 누르면 사라졌다.
            //   → <b>명령을 하나 더 걸어</b> 이 명령이 끝난 뒤에 다시 짓는다
            //     (이 저장소가 이미 쓰는 방식 — <c>SendStringToExecute</c>).
            try { doc.SendStringToExecute("DHGNDREBUILD ", true, false, true); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[원지형 자르기 오류] " + ex.Message);
            AcadApp.ShowAlertDialog("원지형 자르기 중 오류:\n" + ex.Message);
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
                if (tr.GetObject(gid, OpenMode.ForWrite) is CivilDb.TinSurface gs)
                {
                    try { gs.Rebuild(); how = "OK"; } catch (System.Exception rex) { how = rex.Message; }
                    try { tri = gs.Triangles.Count; } catch { }
                }
                tr.Commit();
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
            ed.WriteMessage($"\n[지표면 재작성] '{gname}' — {how} · 상태 {stale} · 삼각형 {tri:N0}개");
            try { DiagLog.Append($"\n■ DHGNDREBUILD — '{gname}' {how} · 상태 {stale} · 삼각형 {tri}\n"); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[지표면 재작성 오류] " + ex.Message);
        }
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
        AcadApp.ShowAlertDialog("원지형 자르기 — 하지 못했습니다\n\n" + msg);
    }
}
