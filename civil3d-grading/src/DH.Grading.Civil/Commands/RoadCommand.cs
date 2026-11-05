using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v110 · 계획_v110.md 3.4 · JACK 1028 «진입도로 — 인프라웍스처럼»] 진입도로 명령.
/// <para>DHROAD = 폴리선(또는 Civil 노선) → `DH도로_N`(곡선) · 바탕 종단 · 계획 종단 자동 초안 · 종단뷰 → 실시간 미리보기 + 도킹창.
/// 계획 종단의 PVI를 끌어 옮기면(또는 노선 IP · 창 값) 바로 다시 계산한다. 도면에는 노선 · 종단 · 종단뷰만 생긴다(정지면은 [확정] — 다음 판).</para></summary>
public class RoadCommand
{
    /// <summary>★★[v113.3 · JACK 1105 «진입도로 생성을 누르면 진입도로 창이 뜨고, 창에 도로 그리기랑 폴리선 선택 — 별도 팝업은 만들지 말고 · 기존 진입도로 창 버튼은 없애»]
    /// [진입도로 생성] = 진입도로 도킹창을 연다(창 맨 위 [도로 경로 그리기] · [폴리선 선택] — 창에 보이는 값으로 새 도로).</summary>
    [CommandMethod("DHROADNEW")]
    public void RoadNew()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        // 화면 없는 Civil은 도킹창을 건드리면 죽음(1105 화면 없는 시험 0xC0000005 — Build 440줄과 같은 법)
        if (SelfCheckLog.Headless) { doc.Editor.WriteMessage("\n[진입도로] 화면 없음 — 진입도로 창을 띄우지 않습니다."); return; }
        RoadPalette.Show();
    }

    [CommandMethod("DHROAD")]
    public void Road()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        try
        {
            var peo = new PromptEntityOptions("\n[진입도로] 노선이 될 폴리선 또는 Civil 노선을 고르세요: ");
            peo.SetRejectMessage("\n폴리선 또는 노선만 됩니다.");
            peo.AddAllowedClass(typeof(Polyline), true); peo.AddAllowedClass(typeof(Polyline2d), true); peo.AddAllowedClass(typeof(Polyline3d), true);
            peo.AddAllowedClass(typeof(CivilDb.Alignment), true);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;
            bool isAlign;
            using (var tr = db.TransactionManager.StartTransaction()) { isAlign = tr.GetObject(per.ObjectId, OpenMode.ForRead) is CivilDb.Alignment; tr.Commit(); }
            Build(doc, per.ObjectId, isAlign, tempPolyline: false);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>★★[v110.2 · JACK 1029 «경로그리기 기능으로 직접 눌러서 그리면 선형되게» — 인터뷰: 점 찍기 + Enter · 고무줄 선 · 꺾인 곳 자동 곡선(반경 = 도킹창 값)]
    /// 점을 차례로 찍는다(고무줄 선 · 찍은 토막은 임시 선) · U = 마지막 점 빼기 · Enter = 끝 · Esc = 그만 → 임시 폴리선 → [폴리선 선택]과 같은 길(<see cref="Build"/>) → 임시 폴리선은 지움.</summary>
    [CommandMethod("DHROADDRAW")]
    public void RoadDraw()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor; var db = doc.Database;
        var pts = new List<Point3d>();   // 세계 좌표
        try
        {
            // ★[설치 전 검토 · 낮음] 바탕면부터 — 수십 점을 찍은 뒤 «바탕면이 없습니다»로 그린 것을 잃지 않게
            if (RoadBuilder.PickBase(db, CivilApp.CivilDocument.GetCivilDocument(db)).Id.IsNull)
            { SectionCommand.Refuse(ed, "바탕면(정지면_DH 또는 원지반)이 없습니다 — 원지반부터 만들어 주세요."); return; }
            var ucs = ed.CurrentUserCoordinateSystem;
            void Redraw()
            {
                ed.Regen();   // 지난 임시 선 걷기(DrawVector는 Regen에서 사라진다)
                for (int i = 0; i + 1 < pts.Count; i++)
                    ed.DrawVector(pts[i].TransformBy(ucs.Inverse()), pts[i + 1].TransformBy(ucs.Inverse()), 4, true);
            }
            while (true)
            {
                var ppo = new PromptPointOptions(pts.Count == 0 ? "\n[경로 그리기] 시작점: "
                    : pts.Count == 1 ? "\n[경로 그리기] 다음 점 [되돌리기(U)]: " : $"\n[경로 그리기] 다음 점 (점 {pts.Count}개) [되돌리기(U)] <Enter = 끝>: ");
                if (pts.Count > 0)
                {
                    // ★[v112.4 · JACK 1104 «클릭하고 휠로 줌하면 전에 찍은 게 없어져서 작성이 안 돼»] 화면 없는 Civil(지그 없음)만 기준점 고무줄 — 화면에서는 아래 지그가 찍은 선 + 고무줄을 그림
                    if (SelfCheckLog.Headless) { ppo.UseBasePoint = true; ppo.BasePoint = pts[^1].TransformBy(ucs.Inverse()); ppo.UseDashedLine = false; }
                    ppo.Keywords.Add("U", "U", "되돌리기(U)");
                    ppo.AppendKeywordsToMessage = false;   // 글에 이미 [되돌리기(U)] — 두 번 붙지 않게
                    ppo.AllowNone = pts.Count >= 2;
                }
                // 찍은 토막(청록) + 마지막 점 → 커서 고무줄 — 지그가 화면마다 다시 그림(확대 · 축소 · 화면 이동에도 남음)
                var drawn = pts.ToList();
                void Path(Autodesk.AutoCAD.GraphicsInterface.WorldDraw wd, Point3d cur, bool has)
                {
                    var t = wd.SubEntityTraits;
                    t.Color = 4;
                    for (int i = 0; i + 1 < drawn.Count; i++) wd.Geometry.WorldLine(drawn[i], drawn[i + 1]);
                    if (has && drawn.Count > 0) { t.Color = 2; wd.Geometry.WorldLine(drawn[^1], new Point3d(cur.X, cur.Y, drawn[^1].Z)); }
                }
                var lr = CursorLabel.GetPoint(ed, ppo, pts.Count == 0 ? "시작점 클릭" : pts.Count == 1 ? "다음 점 클릭" : "다음 점 클릭 · Enter 끝", "", Path);
                var pr = (Status: lr.Status, Value: lr.Value);
                if (pr.Status == PromptStatus.Keyword) { if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); Redraw(); continue; }
                if (pr.Status == PromptStatus.None) break;
                if (pr.Status != PromptStatus.OK) { ed.Regen(); ed.WriteMessage("\n[경로 그리기] 그만두었습니다."); return; }
                var w = pr.Value.TransformBy(ucs);
                if (pts.Count > 0 && Math.Sqrt(Math.Pow(w.X - pts[^1].X, 2) + Math.Pow(w.Y - pts[^1].Y, 2)) < 0.5) { ed.WriteMessage("\n  (앞 점과 0.5 m 안 — 건너뜀)"); continue; }
                pts.Add(new Point3d(w.X, w.Y, 0));
                if (pts.Count >= 2) ed.DrawVector(pts[^2].TransformBy(ucs.Inverse()), pts[^1].TransformBy(ucs.Inverse()), 4, true);
            }
            ed.Regen();
            if (pts.Count < 2) return;
            // 임시 폴리선(모형 공간) → 노선 → 지움
            ObjectId plId;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline(pts.Count) { LayerId = SectionCommand.EnsureLayerStandalone(db, RoadBuilder.LayAlign, 1) };
                for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                plId = pl.ObjectId;
                tr.Commit();
            }
            Build(doc, plId, isAlign: false, tempPolyline: true);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[경로 그리기] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[경로 그리기] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>노선(또는 폴리선) → 진입도로 하나 — [폴리선 선택] · [경로 그리기]가 함께 쓴다. 곡선 반경 = 도킹창 값(<see cref="RoadDesign.LastCurveR"/>).
    /// <para>중간에 실패하면 이번에 만든 노선 · 종단을 지운다(주인 없는 DH도로_N이 남지 않게 — 설치 전 검토 L6). <paramref name="tempPolyline"/>이면 끝에 그 폴리선을 지운다.</para></summary>
    static void Build(Document doc, ObjectId srcId, bool isAlign, bool tempPolyline)
    {
        var ed = doc.Editor; var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        var log = new System.Text.StringBuilder();
        ObjectId created = ObjectId.Null; bool written = false;
        var profBefore = new HashSet<ObjectId>();   // ★[전체 검토 M3] 사람 노선에서 실패하면 이번에 만든 종단만 지우려고(처음부터 있던 것은 그대로)
        try
        {
            if (isAlign)
            {
                using var trP = db.TransactionManager.StartTransaction();
                if (trP.GetObject(srcId, OpenMode.ForRead) is CivilDb.Alignment alP) foreach (ObjectId p in alP.GetProfileIds()) profBefore.Add(p);
                trP.Commit();
            }
            ObjectId aid = srcId;
            RoadDesign? d = null;
            if (isAlign)
            {
                // ★[v110.4 · 계획 검토 M4] 부지 종단 노선(«DH선형»)은 진입도로로 안 받는다 — 부지 «지우고 새로»가 지우고 · 도곽 · 측점 찍기가 섞인다
                string refuse = SiteAlignRefusal(db, aid);
                if (refuse.Length > 0) { SectionCommand.Refuse(ed, refuse); return; }
                bool foreign;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    d = RoadDesign.Read(tr, aid);
                    foreign = d != null && RoadSession.ForeignRecord(tr, aid, d);
                    // ★[v111.2 · 전체 검토 H2] 복사한 노선 — 따라온 기록(원래 노선의 종단 · 도로 면을 가리킴)을 지우고 이 노선으로 새로
                    if (foreign) { RoadDesign.Remove(tr, aid); d = null; }
                    tr.Commit();
                }
                if (foreign) ed.WriteMessage("\n[진입도로] 복사된 노선이라 따라온 진입도로 기록(원래 노선 것)을 지우고 이 노선으로 새로 만듭니다.");
                if (d != null)
                {
                    // ★[v111.3 · JACK 1030 «노선을 선택하거나 그리고 나서 무조건 종단은 생성» · 설치 전 검토 열린 질문] 종단도(이 노선 종단뷰)가 하나도 없으면 지금 만든다(Esc여도 빈 자리)
                    //   ★[v111.10 · 계획_초기화.md 3.4] 종단뷰를 여는 «앞»에서 센다 — 처음 만드는 길이면 열 때부터 «처음 만드는 중»(첫 계산이 «고친 것이 있습니다»를 띄우지 않게)
                    int nPv = 1;
                    try { using var trV = db.TransactionManager.StartTransaction(); nPv = ((CivilDb.Alignment)trV.GetObject(aid, OpenMode.ForRead)).GetProfileViewIds().Count; trV.Commit(); } catch { }
                    try
                    {
                        Open(doc, aid, fresh: nPv == 0);   // 이미 진입도로 — 다시 연다
                        var so = nPv == 0 ? RoadSession.Of(doc) : null;
                        if (so != null) { AskAndPlaceSheets(doc, so, again: false); MakeFirstSurface(doc, so); }   // ★[v111.7.1] 종단도를 처음 놓았으면 지표면도 이 명령 안에서
                    }
                    finally { RoadSession.Of(doc)?.EndMaking(); }
                    return;
                }
            }
            // ★[재검토 낮음 1] 바탕면을 노선 만들기 «앞»에서 — 없으면 아무것도 안 만들고 멈춤(주인 없는 DH도로_N이 남지 않게)
            var (bs, bsName) = RoadBuilder.PickBase(db, cdoc);
            if (bs.IsNull) { SectionCommand.Refuse(ed, "바탕면(정지면_DH 또는 원지반)이 없습니다 — 원지반부터 만들어 주세요."); return; }
            // ★★[v113.3 · JACK 1105 «도로 설정을 반영한 도로를 만들 수 있게»] 새 도로의 모양 값 = 진입도로 창에 보이는 값(열린 도로가 있으면 그 도로 · 없으면 창의 새 도로 값)
            var tpl = RoadPanel.NewRoadTemplate(doc);
            if (!isAlign)
            {
                double rad = tpl.CurveR;
                aid = RoadBuilder.AlignmentFromPolyline(doc, srcId, rad, log);
                created = aid;
                d = new RoadDesign { CurveR = rad, EndsOn = true };   // ★[도로 마무리 ① · 계획 5.3] 새 도로 = 끝 맞춤 켬
                d.CopyShapeFrom(tpl); d.CurveR = rad;
            }
            if (d == null)
            {
                d = new RoadDesign { OwnCurves = false, EndsOn = true };   // 기존 Civil 노선(사람이 설계한 곡선) — 곡선 반경 칸이 안 바꿈 · 끝은 가상 자르기만
                double keepR = d.CurveR; d.CopyShapeFrom(tpl); d.CurveR = keepR;   // ★[v113.3] 창 값 — 곡선 반경은 사람 노선이라 안 씀
            }
            d.BaseSurface = bsName;
            var (bp, pp, draft) = RoadBuilder.MakeProfiles(doc, aid, bs, d, log);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                d.PlanProfile = pp.Handle.ToString(); d.BaseProfile = bp.Handle.ToString();
                d.Write(tr, aid);
                tr.Commit();
            }
            written = true;
            log.AppendLine($"  바탕면 '{bsName}'");
            ed.WriteMessage("\n" + log.ToString().TrimEnd());
            try { DiagLog.Append("\n" + log); } catch { }
            // ★[v111.10 · 계획_초기화.md 3.4] 막 만든 도로 — 명령 끝까지 «처음 만드는 중»(끝에 EndMaking · 실패해도)
            try
            {
                Open(doc, aid, fresh: true);
                // ★★[v110.4 · 계획_v110_4.md 3.5 · JACK 1029] 종단도 = 부지 [종단도]와 같은 꼴(A1 도곽 · 밴드 6칸) — 노선 만들 때 바로 · 실패해도 도로는 열림
                var s = RoadSession.Of(doc);
                if (s != null) AskAndPlaceSheets(doc, s, again: false);
                // ★★[v111.7.1 · JACK 1102 «[진입도로 지표면 생성]을 누르지 않으면 횡단도가 안 되고 — 클릭 수를 최대한 적게»] 첫 지표면 = 종단도를 놓은 바로 뒤 · 같은 명령(단추 없음)
                if (s != null) MakeFirstSurface(doc, s);
            }
            finally { RoadSession.Of(doc)?.EndMaking(); }
            if (draft.EndUnreachable) ed.WriteMessage("\n[진입도로] ⚠종점 높이를 최대 종단경사로 못 맞춥니다 — 노선을 늘리거나 최대 경사를 올리세요.");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 실패 — " + ex); } catch { }
            if (!written && !created.IsNull)
            {
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var al = (CivilDb.Alignment)tr.GetObject(created, OpenMode.ForWrite);
                    foreach (ObjectId pid in al.GetProfileIds()) { try { tr.GetObject(pid, OpenMode.ForWrite).Erase(); } catch { } }
                    al.Erase();
                    tr.Commit();
                    ed.WriteMessage("\n[진입도로] 이번에 만든 노선 · 종단을 지웠습니다" + (tempPolyline ? "." : "(폴리선은 그대로)."));
                }
                catch { }
            }
            else if (!written && isAlign)
            {
                // ★[전체 검토 M3] 사람 노선 — 이번에 만든 종단(«-바탕» · «-계획»)만 지움(다시 하면 겹치지 않게)
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    int nk = 0;
                    if (tr.GetObject(srcId, OpenMode.ForRead) is CivilDb.Alignment alK)
                        foreach (ObjectId pid in alK.GetProfileIds()) if (!profBefore.Contains(pid)) { try { tr.GetObject(pid, OpenMode.ForWrite, false, true).Erase(); nk++; } catch { } }
                    tr.Commit();
                    if (nk > 0) ed.WriteMessage($"\n[진입도로] 이번에 만든 종단 {nk}개를 지웠습니다(노선은 그대로).");
                }
                catch { }
            }
        }
        finally
        {
            if (tempPolyline) SectionCommand.EraseQuiet(db, srcId);
        }
    }

    /// <summary>★★[v111.7.1 · JACK 1102 «[진입도로 지표면 생성]을 누르지 않아도»] 첫 진입도로 지표면 — DHROAD · DHROADDRAW 끝(종단도를 놓은 바로 뒤 · 같은 명령 = 실행 취소 한 번).
    /// 이미 있거나 계산이 깨졌으면 안 함. 못 만들면 명령창 한 줄 + 도킹창 상태 줄에 까닭(진입도로 창 [업데이트]로 다시).</summary>
    static void MakeFirstSurface(Document doc, RoadSession s)
    {
        var ed = doc.Editor;
        try { s.RecomputeIfDirty(); } catch { }
        if (s.HasSurface()) return;
        // ★[v111.7.1 · 검토 낮음] 계산이 깨져 못 만들면 말없이 넘기지 않음(도킹창 상태 줄 = 계산 오류)
        if (s.Last == null || s.Last.Error.Length > 0) { ed.WriteMessage("\n[진입도로] ⚠계산 오류라 진입도로 지표면을 아직 안 만들었습니다 — " + (s.Last?.Error is { Length: > 0 } er ? er : s.Note) + " (값을 고친 뒤 진입도로 창 [업데이트])"); return; }
        try
        {
            string msg;
            using (BusyPopup.Scope("진입도로 지표면 만드는 중입니다.")) msg = s.MakeSurface();
            ed.WriteMessage("\n[진입도로] " + msg);
            try { ed.Regen(); } catch { }
            try { s.Recompute(); } catch { }
            // ★[v111.8 · L13 · M2] 첫 면의 최종지표면이 실패했으면 상태 줄에도 까닭([업데이트]로 다시)
            if (s.FinalNote.StartsWith("⚠")) s.NoteFail("실패 — " + s.FinalNote.TrimStart('⚠'));
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] ⚠진입도로 지표면을 못 만들었습니다 — " + ex.Message + " (진입도로 창 [업데이트])");
            try { DiagLog.Append("\n[진입도로] 첫 지표면 실패 — " + ex); } catch { }
            try { s.Recompute(); } catch { }
            s.NoteFail("실패 — 지표면 " + ex.Message);
        }
    }

    /// <summary>★[v110.4 · 계획 검토 M4] 부지 종단 노선(«DH선형»으로 시작 · 진입도로 설계 없음)이면 거절 글 — 아니면 빈 글.</summary>
    internal static string SiteAlignRefusal(Database db, ObjectId aid)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            string nm = tr.GetObject(aid, OpenMode.ForRead) is CivilDb.Alignment al ? al.Name ?? "" : "";
            bool road = RoadDesign.Has(tr, aid);
            tr.Commit();
            return !road && nm.StartsWith(SectionCommand.AlignBase, StringComparison.Ordinal)
                ? $"«{nm}»은 계획지표면 [종단도] 노선입니다 — 진입도로로 쓸 수 없습니다(부지 «지우고 새로»가 지우고 · 도곽 · 측점이 섞임).\n진입도로는 폴리선 · [경로 그리기] · 다른 Civil 노선으로 만드세요."
                : "";
        }
        catch { return ""; }
    }

    /// <summary>★★[v110.4 · 계획 3.5 · 검토 H1] 종단도 놓을 자리 묻기 → 다른 종단도와 겹치면 다시(★[JACK 1103] 평면 겹침만 — 같은 X라도 위 · 아래로 떨어지면 됨 · <see cref="RoadSheets.Overlap"/>) →<see cref="RoadSession.MakeSheets"/>.
    /// <paramref name="again"/>(= [종단도 다시] · DHROADSHEET · [업데이트]가 자리를 모를 때)이면 Enter = 지난 자리. Esc = 안 그림(도로는 그대로).
    /// ★[v111.7.2] 돌려줌 = 그렸나(실패 · 그만 = 거짓).
    /// <para>★[설치 전 검토 · 중간] 그린 뒤 실제 도곽 · 뷰로 한 번 더 겹침을 본다(<see cref="RoadSheets.PlacedOverlap"/> — 그래프가 짐작보다 높음) — 겹치면 다른 자리를 묻고
    /// 그 자리에 다시 그린다(겹친 것은 새로 그릴 때 이 도로 것으로 지워짐). 그동안은 Esc · Enter로 그만둘 수 없다(<paramref name="mustMove"/> — 겹친 채 두면 장식 · 밴드 정리가 섞임).</para></summary>
    static bool AskAndPlaceSheets(Document doc, RoadSession s, bool again, bool mustMove = false)
    {
        var ed = doc.Editor; var db = doc.Database;
        try
        {
            GradingSettings.SyncToDocument(doc);   // ★[설치 전 검토 · 낮음] 겹침 검사(축척 · 장 수)도 이 도면 설정으로
            double[] last = s.Fresh().SheetAt;
            bool hasLast = again && !mustMove && last.Length >= 2 && double.IsFinite(last[0]) && double.IsFinite(last[1]);
            // ★[v113.1] 도면설정 «2. 진입도로» H · V(판 2 — 자동 H는 1:1000으로 넓게 짐작 · 실제는 그릴 때 정함) · 옛 설정 = 옛 규칙
            var (scale, veP) = RoadSheets.PlannedScale(db);
            string scNote = RoadSheets.Setting(db, "").Note;
            int n = RoadSheets.SheetCount(db, s.AlignId, scale);
            ed.WriteMessage($"\n[진입도로] 종단도 1:{scale:0} · {n}장 — {scNote}");
            while (true)
            {
                // ★★[v111.3 · JACK 1030 «노선을 선택하거나 그리고 나서 무조건 종단은 생성 — 실수로 Esc로 꺼도»] + ★[v111.3.1 · JACK 1030 «진입도로 종단은 사용자가 찍어서 그려지게»]
                //   노선을 만들 때(again = false)는 사람이 찍은 자리에만 그린다 — Esc · Enter는 그만이 아니라 다시 묻기(종단 없이는 설계 안 됨) · 빈 자리 저절로(v111.3)는 거둠
                //   [종단도 새로 그리기]의 Esc는 지금처럼 그만(옛 종단도 그대로)
                var ppo = new PromptPointOptions(hasLast
                    ? "\n[진입도로] 종단도(도곽 · 밴드)를 놓을 자리 <Enter = 지난 자리 · Esc = 그만>: "
                    : mustMove ? "\n[진입도로] 종단도를 옮길 빈 자리를 찍으세요: "
                    : "\n[진입도로] 종단도(도곽 · 밴드)를 놓을 자리를 찍으세요: ") { AllowNone = true };
                // ★[v111.7.2 · JACK 1102 «마우스에 메모로»] 커서를 따라다니는 이름표(우리가 그림 · AutoCAD 풍선은 안 떴음) · ★[JACK 1103] 둘째 줄 = 이 질문에서 듣는 키만(처음 놓기 · 겹쳐 옮기기는 Enter · Esc 둘 다 다시 묻기라 없음)
                var pr = CursorLabel.GetPoint(ed, ppo, mustMove ? "종단도 옮길 위치 클릭" : "종단도 작성 위치 클릭", hasLast ? "Enter 지난 자리 · Esc 그만" : again && !mustMove ? "Esc 그만" : "");
                Point3d at;
                if (pr.Status == PromptStatus.None && hasLast) at = new Point3d(last[0], last[1], 0);
                else if (pr.Status == PromptStatus.OK) at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                else if (mustMove) { ed.WriteMessage("\n[진입도로] 겹친 종단도는 그대로 둘 수 없습니다 — 빈 자리를 찍으세요."); continue; }
                else if (!again) { ed.WriteMessage("\n[진입도로] 종단도는 꼭 있어야 합니다(종단 없이 설계 안 됨) — 놓을 자리를 찍으세요."); continue; }
                else if (pr.Status == PromptStatus.None) { hasLast = false; continue; }   // [종단도 새로 그리기] — 지난 자리가 없으면 Enter는 다시 묻기
                else { ed.WriteMessage("\n[진입도로] 종단도는 그대로 둡니다(그만)."); return false; }
                string hit = RoadSheets.Overlap(db, s.AlignId, at, scale, n, veP);
                if (hit.Length > 0)
                {
                    ed.WriteMessage($"\n[진입도로] ⚠{hit}와(과) 겹칩니다(새 도곽 {n}장 + 여유 {RoadSheets.PlaceMarginMm:0} mm) — 빈 자리(위 · 아래 · 옆)에 찍으세요.");
                    hasLast = false;
                    continue;
                }
                var log = new System.Text.StringBuilder();
                string msg;
                using (BusyPopup.Scope("종단도 그리는 중입니다.")) msg = s.MakeSheets(at, log);
                ed.WriteMessage("\n[진입도로] " + msg);
                try { DiagLog.Append("\n" + log); } catch { }
                // ★[설치 전 검토 · 중간] 그린 것으로 다시 봄 — 짐작보다 그래프가 높아 남의 것과 겹쳤으면 다른 자리를 묻는다(다음 짐작은 방금 그린 실제 높이를 씀)
                string after = RoadSheets.PlacedOverlap(db, s.AlignId, s.Design.SheetScale > 0 ? s.Design.SheetScale : scale);   // ★[v113.1] 그린 축척
                if (after.Length > 0)
                {
                    ed.WriteMessage($"\n[진입도로] ⚠그린 종단도가 {after}와(과) 겹칩니다(그래프가 짐작보다 높음) — 다른 빈 자리를 찍으세요(그 자리에 다시 그림).");
                    try { DiagLog.Append($"\n[진입도로] 그린 뒤 겹침 — {after} · 자리 ({at.X:F1}, {at.Y:F1})"); } catch { }
                    mustMove = true; hasLast = false;
                    continue;
                }
                try { s.Recompute(); } catch { }
                return true;
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 종단도 실패 — " + ex.Message + "(도로는 그대로 — 진입도로 [종단도]로 다시)");
            try { DiagLog.Append("\n[진입도로] 종단도 실패 — " + ex); } catch { }
        }
        return false;
    }

    /// <summary>★★[v111.6 · JACK 1102 «진입도로도 리본에서 창을 열 수 있게»] 리본 [진입도로 창] — 진입도로 도킹창을 연다. 열린 도로가 없으면 한 줄 알리고 창은 «열린 도로 없음»으로 뜬다(곡선 반경은 그리기 전에도 정함).</summary>
    [CommandMethod("DHROADPANEL")]
    public void OpenPanel()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        if (RoadSession.Of(doc) == null) doc.Editor.WriteMessage("\n[진입도로] 열린 진입도로가 없습니다 — [진입도로 생성]으로 도로를 만드세요.");
        RoadPalette.Show();
    }

    /// <summary>★★[v110.4 · 계획 3.5 · 3.6] [종단도 다시] — 지금 종단 · 축척으로 이 도로 종단도를 새로(옛것은 이 도로 표식 · 핸들로만 지움). Enter = 지난 자리.</summary>
    [CommandMethod("DHROADSHEET")]
    public void Sheet()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var s = Need(doc.Editor); if (s == null) return;
        AskAndPlaceSheets(doc, s, again: true);
    }

    /// <summary>★★[v111.5 · 계획_v111_4.md 3 · JACK 1030 «진입도로 횡단도 계획지표면 횡단도처럼»] [횡단도 그리기] — 놓을 자리(왼쪽 아래)를 찍음 · Enter = 지난 자리 · Esc = 그만(옛 횡단도 그대로).
    /// 진입도로 종단도(측점) · 진입도로 지표면이 없으면 거절.</summary>
    [CommandMethod("DHROADXSEC")]
    public void Xsec()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var s = Need(doc.Editor); if (s == null) return;
        AskAndPlaceXsec(doc, s);
    }

    /// <summary>★[v111.7.2] 횡단도 놓을 자리 묻기(Enter = 지난 자리 · Esc = 그만) → <see cref="RoadSession.MakeXsec"/> — [횡단도] · [업데이트](자리를 모를 때) 공용. 돌려줌 = 그렸나.</summary>
    static bool AskAndPlaceXsec(Document doc, RoadSession s)
    {
        var ed = doc.Editor;
        bool drawn = false;
        try
        {
            GradingSettings.SyncToDocument(doc);
            string why = RoadXsec.Refusal(s);
            if (why.Length > 0) { ed.WriteMessage("\n[진입도로] " + why); return false; }
            double[] last = s.Fresh().XsecAt;
            bool hasLast = last.Length >= 2 && double.IsFinite(last[0]) && double.IsFinite(last[1]);
            var ppo = new PromptPointOptions(hasLast
                ? "\n[진입도로] 횡단도를 놓을 왼쪽 아래 자리 <Enter = 지난 자리 · Esc = 그만>: "
                : "\n[진입도로] 횡단도를 놓을 왼쪽 아래 자리를 찍으세요 (Esc = 그만): ") { AllowNone = hasLast };
            var pr = CursorLabel.GetPoint(ed, ppo, "횡단도 작성 위치 클릭", hasLast ? "Enter 지난 자리 · Esc 그만" : "Esc 그만");   // ★[v111.7.2 · JACK 1102 «마우스에 메모로»] 커서를 따라다니는 이름표(우리가 그림 · AutoCAD 풍선은 안 떴음)
            Point3d at;
            if (pr.Status == PromptStatus.None && hasLast) at = new Point3d(last[0], last[1], 0);
            else if (pr.Status == PromptStatus.OK) at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            else { ed.WriteMessage("\n[진입도로] 횡단도는 그대로 둡니다(그만)."); return false; }
            var log = new System.Text.StringBuilder();
            string msg;
            using (BusyPopup.Scope("횡단도 그리는 중입니다.")) msg = s.MakeXsec(at, log);
            ed.WriteMessage("\n[진입도로] " + msg);
            try { DiagLog.Append("\n" + log); } catch { }
            drawn = !msg.StartsWith("⚠");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 횡단도 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 횡단도 실패 — " + ex); } catch { }
        }
        try { s.Recompute(); } catch { }
        return drawn;
    }

    static void Open(Document doc, ObjectId aid, bool fresh = false)
    {
        var s = RoadSession.Start(doc, aid, fresh);
        // ★[v111.7.1] 창을 못 띄워도 도로 만들기는 이어 감(여기서 터지면 Build가 이번에 만든 노선을 지운다) · 화면 없는 Civil은 창을 건드리면 죽음(SelfCheckLog.Headless — 정지 창과 같은 법)
        if (!SelfCheckLog.Headless) try { RoadPalette.Show(); } catch (System.Exception exP) { try { DiagLog.Append("\n[진입도로] 도킹창을 못 띄움 — " + exP.Message); } catch { } }
        doc.Editor.WriteMessage($"\n[진입도로] {s.Name} — {(s.Last?.Text ?? s.Note)}");
    }

    static RoadSession? Need(Editor ed)
    {
        var s = RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument);
        if (s == null) ed.WriteMessage("\n[진입도로] 열린 진입도로가 없습니다 — [진입도로 생성]으로 도로를 만드세요.");
        // ★[v111.7 · 재검토 낮음] 남은 바뀜(실행 취소 표시 포함)을 먼저 셈 — 이 명령이 고친 결과에 실행 취소 표시가 묻어 저절로 안 그려지는 일이 없게
        else try { s.RecomputeIfDirty(); } catch { }
        return s;
    }

    /// <summary>옹벽 구간 — 도로 가장자리 근처 두 점(시작 · 끝) · 쪽은 첫 점이 노선 왼쪽/오른쪽.</summary>
    [CommandMethod("DHROADWALL")]
    public void Wall()
    {
        var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            var s = Need(ed); if (s == null) return;
            var p1 = ed.GetPoint("\n[진입도로 옹벽] 구간 시작(옹벽을 세울 쪽 가장자리 근처): ");
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetPoint(new PromptPointOptions("\n[진입도로 옹벽] 구간 끝: ") { BasePoint = p1.Value, UseBasePoint = true });
            if (p2.Status != PromptStatus.OK) return;
            var db = s.Doc.Database;
            double a = 0, oa = 0, b = 0, ob = 0, st0, st1;
            bool outA = false, outB = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(s.AlignId, OpenMode.ForRead);
                var w1 = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem); var w2 = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                // ★[설치 전 검토 · 높음] 노선 시점 앞 · 종점 뒤를 찍으면 StationOffset이 예외 — 범위 밖도 받는 쪽으로 받고 측점은 노선 안으로 자른다
                al.StationOffsetAcceptOutOfRange(w1.X, w1.Y, ref a, ref oa, ref outA);
                al.StationOffsetAcceptOutOfRange(w2.X, w2.Y, ref b, ref ob, ref outB);
                st0 = al.StartingStation; st1 = al.EndingStation;
                tr.Commit();
            }
            a = Math.Clamp(a, st0, st1); b = Math.Clamp(b, st0, st1);
            // ★[도로 마무리 ① · 계획 5.8] 도로 구간(잘린 끝 · 빈 구간 밖)으로 자름 — 밖을 찍으면 그 구간 끝으로
            var rg = s.Ranges.Count > 0 ? s.Ranges : new List<(double A, double B)> { (st0, st1) };
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            var parts = RoadRanges.Clip(rg, lo, hi);
            if (parts.Count > 0) { a = parts[0].A; b = parts[^1].B; }
            if (Math.Abs(b - a) < 0.5) { ed.WriteMessage("\n[진입도로 옹벽] 구간이 0.5 m보다 짧습니다 — 노선을 따라 두 점을 찍으세요."); return; }
            if (parts.Count > 1) ed.WriteMessage($"\n[진입도로 옹벽] 빈 구간(부지 바닥)을 걸쳐 찍었습니다 — 도로가 있는 {parts.Count}구간 몫만 쓰입니다.");
            int side = oa < 0 ? -1 : 1;                                   // Civil 오프셋: 오른쪽 +
            s.Fresh().TierWalls.Add(new double[] { Math.Min(a, b), Math.Max(a, b), side, 0, 0 });   // ★[v110.3 · 검토 M4] 가장자리 옹벽 = 0단 · 소단 0(옛 길)
            s.SaveDesign(); s.Recompute();
            ed.WriteMessage($"\n[진입도로 옹벽] {(side < 0 ? "좌" : "우")} {Math.Min(a, b):F1} ~ {Math.Max(a, b):F1} m" + (outA || outB ? " (노선 끝 밖을 찍어 끝 측점으로 자름)" : ""));
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로 옹벽] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로 옹벽] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>★★[도로 마무리 ① · 계획 5.3 · 비평 M6] [끝 맞추기] — 이 판 앞에 만든 도로(끝 맞춤 꺼짐)의 끝을 부지 바닥 · 본 도로 포장에 한 번 맞추고 끝 맞춤을 켬(그 뒤 [업데이트]가 늘 맞춤).
    /// 노선 · 종단을 바꾸는 일이라 말없이 하지 않고 이 단추로만. 이어서 [업데이트]와 같은 차례(지표면 → 종단도 → 횡단도 — 실행 취소 한 번).</summary>
    [CommandMethod("DHROADENDS")]
    public void Ends()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
        try
        {
            var s = Need(ed); if (s == null) return;
            string fx;
            using (BusyPopup.Scope("도로 끝을 맞추는 중입니다.")) fx = s.FixEndsNow(true);
            ed.WriteMessage($"\n[진입도로 끝] {s.Name} — {fx}");
            try { s.RecomputeCore(); } catch { }
            RedrawStale(doc, s);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로 끝] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로 끝] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>★★[도로 마무리 ② · 계획 6.3] [구간 지정] 측구 — 도로 곁 두 점(시작 · 끝) · 쪽 = 첫 점이 노선 왼쪽/오른쪽 · ■ 측구 창의 형식 · 치수로 기록(«없음»이면 그 구간을 지움 · 나중 기록이 이김).
    /// 도로 구간(잘린 끝 · 빈 구간 밖)으로 자름. 실행 취소 = 이 명령 한 줄.</summary>
    [CommandMethod("DHROADDITCH")]
    public void Ditch()
    {
        var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            var s = Need(ed); if (s == null) return;
            var d0 = s.Fresh();
            string kindNm = d0.DitchKind == 1 ? "U형" : d0.DitchKind == 2 ? "L형" : "없음(지우기)";
            if (d0.DitchKind == 1 && DitchSection.Check(RoadDesign.UOf(d0.DitchU)) is { Length: > 0 } eu) { ed.WriteMessage("\n[진입도로 측구] ⚠" + eu); return; }
            if (d0.DitchKind == 2 && DitchSection.Check(RoadDesign.LOf(d0.DitchL)) is { Length: > 0 } el) { ed.WriteMessage("\n[진입도로 측구] ⚠" + el); return; }
            var p1 = ed.GetPoint($"\n[진입도로 측구 · {kindNm}] 구간 시작(측구를 둘 쪽 도로 가장자리 근처): ");
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetPoint(new PromptPointOptions($"\n[진입도로 측구 · {kindNm}] 구간 끝: ") { BasePoint = p1.Value, UseBasePoint = true });
            if (p2.Status != PromptStatus.OK) return;
            var db = s.Doc.Database;
            double a = 0, oa = 0, b = 0, ob = 0, st0, st1;
            bool outA = false, outB = false;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var al = (CivilDb.Alignment)tr.GetObject(s.AlignId, OpenMode.ForRead);
                var w1 = p1.Value.TransformBy(ed.CurrentUserCoordinateSystem); var w2 = p2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                al.StationOffsetAcceptOutOfRange(w1.X, w1.Y, ref a, ref oa, ref outA);
                al.StationOffsetAcceptOutOfRange(w2.X, w2.Y, ref b, ref ob, ref outB);
                st0 = al.StartingStation; st1 = al.EndingStation;
                tr.Commit();
            }
            a = Math.Clamp(a, st0, st1); b = Math.Clamp(b, st0, st1);
            var rg = s.Ranges.Count > 0 ? s.Ranges : new List<(double A, double B)> { (st0, st1) };
            var parts = RoadRanges.Clip(rg, Math.Min(a, b), Math.Max(a, b));
            if (parts.Count > 0) { a = parts[0].A; b = parts[^1].B; }
            if (Math.Abs(b - a) < 0.5) { ed.WriteMessage("\n[진입도로 측구] 구간이 0.5 m보다 짧습니다 — 도로를 따라 두 점을 찍으세요."); return; }
            int side = oa < 0 ? -1 : 1;
            var dims = d0.DitchKind == 1 ? d0.DitchU : d0.DitchKind == 2 ? d0.DitchL : Array.Empty<double>();
            var rec = new double[] { Math.Min(a, b), Math.Max(a, b), side, d0.DitchKind }.Concat(dims).ToArray();
            s.Fresh().Ditches.Add(rec);
            s.SaveDesign(); s.Recompute();
            var sh = RoadDesign.ShapeOf(rec);
            ed.WriteMessage($"\n[진입도로 측구] {(side < 0 ? "좌" : "우")} {Math.Min(a, b):F1} ~ {Math.Max(a, b):F1} m — " + (sh == null ? "측구 지움" : $"{sh.Label}(윗폭 {sh.TopWidth:0.###} m)")
                            + (outA || outB ? " (노선 끝 밖을 찍어 끝 측점으로 자름)" : "") + (parts.Count > 1 ? $" · 빈 구간을 걸쳐 {parts.Count}구간 몫만 쓰임" : "") + " — [업데이트]로 지표면 · 도면에");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로 측구] 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로 측구] 실패 — " + ex); } catch { }
        }
    }

    /// <summary>★★[v110.3 · 계획_v110_3.md 4 · JACK 1029 «계획부지처럼 먼저 사면으로 생성한 후 소단선을 변환»] [옹벽 변환] — 누른 단부터 바깥이 수직 옹벽 · 사이 소단 길이.
    /// <para>★★★[v110.3.1 · JACK 1029 «옹벽변환을 위해 선택하는 마우스 커서모양도 좀 다르고 … 사용법을 간결하게 하면서 계획지표면때 기능을 답습해줘»]
    /// 계획부지 [옹벽 변환](ZoneEditCommon)과 같은 차례 · 같은 꼴: 고를 선을 도면에 잠깐 실제 선(시안)으로 그려 <b>네모 커서로 클릭</b>(GetEntity) → 고른 선 노랑 →
    /// ② [전체구간(A)/구간지정(P)] — 구간지정은 선을 따라 빨간 띠 + 화살촉 → ③ 소단 길이 정하고 Enter. 전체해제 = C · Esc = 취소. 끝나면 잠깐 그린 선은 지운다.</para></summary>
    [CommandMethod("DHROADWALLCONV")]
    public void WallConvert() => Convert(true);

    /// <summary>★★[v110.3.1 · JACK 1029 «계획지표면때 기능을 답습»] [사면 변환] — 옹벽선(빨강)을 클릭해 그 자리 옹벽을 다시 사면으로(계획부지 [사면 변환]과 같은 차례).
    /// 전체구간 = 그 선에 걸린 옹벽 기록을 통째로 · 구간지정 = 고른 구간만 잘라 냄(진입도로 옹벽은 «그 단부터 바깥이 모두 옹벽»이라 위 단만 따로 되돌리지는 않는다).</summary>
    [CommandMethod("DHROADSLOPECONV")]
    public void SlopeConvert() => Convert(false);

    /// <summary>★★[v113.0 · JACK 1104 «폴리선으로 옹벽을 추가하는 건 별도 단추»] [옹벽 추가] — 도로 면 경계 위에서 시작해 사면 안을 지나 다시 경계 위에서 끝나는 폴리선(또는 S로 고르기)
    /// → 2점 판정 → 단높이 · 소단 → 기록 · 다시 계산(<see cref="RoadWallLine.Run"/> — 옛 [옹벽 변환]의 L 길 그대로) → 바꿨으면 지표면 → 종단도 → 횡단도(<see cref="RedrawStale"/>).</summary>
    [CommandMethod("DHROADWALLADD")]
    public void WallAdd()
    {
        const string label = "옹벽 추가";
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;
        RoadSession? s = null;
        bool changed = false;
        try
        {
            s = Need(ed); if (s == null) return;
            if (s.Last == null || s.Last.Error.Length > 0) { ed.WriteMessage($"\n[{label}] 계산된 도로가 없습니다 — 진입도로 창 ■ 결과에 숫자가 나온 뒤 하세요."); return; }
            try
            {
                if (System.Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0 && System.Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) == 1)
                { ed.WriteMessage($"\n[{label}] 모형 공간(또는 배치의 모형 뷰포트 안)에서 하세요."); return; }
            }
            catch { }
            changed = RoadWallLine.Run(doc, s);
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n[{label}] 실패 — " + ex); } catch { }
        }
        if (changed && s != null) RedrawStale(doc, s);
    }

    const string PickLayer = "DH-도로계단선_선택";

    /// <summary>★[v113.0 · 설치 전 검토 M4] 사면 변환 W — 폴리선 옹벽을 번호로 통째로 지움(쉬는 기록도 · 다른 옹벽 기록은 그대로) · 지웠으면 참.</summary>
    static bool RemoveBoundByNumber(Editor ed, RoadSession s, string label)
    {
        var d = s.Fresh();
        var nos = (d.BoundWalls ?? new()).Select(b => b.No).OrderBy(n => n).ToList();
        if (nos.Count == 0) { ed.WriteMessage("\n → 폴리선 옹벽이 없습니다."); return false; }
        var pio = new PromptIntegerOptions($"\n지울 폴리선 옹벽 번호 〈{string.Join(", ", nos)}〉") { AllowNone = false, LowerLimit = 1, UpperLimit = 100000 };
        if (nos.Count == 1) { pio.DefaultValue = nos[0]; pio.UseDefaultValue = true; }
        var ir = ed.GetInteger(pio);
        if (ir.Status != PromptStatus.OK) return false;
        int rm = d.BoundWalls!.RemoveAll(b => b.No == ir.Value);
        if (rm == 0) { ed.WriteMessage($"\n → 폴리선 옹벽 {ir.Value}이 없습니다."); return false; }
        if (d.BoundWalls.Count == 0) d.BoundWalls = null;
        s.SaveDesign(); s.Recompute();
        ed.WriteMessage($"\n[{label}] 폴리선 옹벽 {ir.Value}를 지우고 사면으로 되돌렸습니다.");
        try { DiagLog.Append($"\n■ [{label}] 폴리선 옹벽 {ir.Value} 번호로 지움(W)"); } catch { }
        return true;
    }

    static (bool Changed, RoadSession? S) ConvertCore(bool wallMode)
    {
        string label = wallMode ? "사면→옹벽" : "옹벽→사면", noun = wallMode ? "계단선" : "옹벽선";   // ★[v113.0 · JACK 1104] 단추 이름과 같게
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        RoadSession? s = null;
        bool changed = false;   // 기록을 바꿨나(끝에서 도로 면 다시 — Convert)
        var made = new Dictionary<ObjectId, int>();
        try
        {
            s = Need(ed); if (s == null) return (changed, s);
            s.RecomputeIfDirty();   // ★[전체 검토 M2] PVI를 끌거나 실행 취소한 직후라도 지금 모양으로 고르게
            if (s.Last == null || s.Last.Error.Length > 0) { ed.WriteMessage($"\n[{label}] 계산된 도로가 없습니다 — 진입도로 창 ■ 결과에 숫자가 나온 뒤 하세요."); return (changed, s); }
            // ★[전체 검토 M1] 고르는 동안 창 값으로 다시 계산돼도 이 명령은 처음 모양(측점 0 · 결과)으로 — 선 · 측점 · 기록 판정이 섞이지 않게
            double s0Snap = s.S0; var lastSnap = s.Last;
            // ★[도로 마무리 ① · 계획 5.6] 빈 구간이면 토막마다 선 · 결과 · Core 측점 0(고른 선의 토막으로 셈)
            var pieceSnap = s.Pieces.Count > 1 ? s.Pieces.ToList() : null;
            // ★[설치 전 검토 L8] 배치 공간(모형 뷰포트 밖)에서는 잠깐 그린 선을 못 고른다
            try
            {
                if (System.Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0 && System.Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) == 1)
                { ed.WriteMessage($"\n[{label}] 모형 공간(또는 배치의 모형 뷰포트 안)에서 하세요."); return (changed, s); }
            }
            catch { }
            // 고를 선 — 옹벽 변환 = 사면선 · 소단선 · 사면 변환 = 옹벽선(도로 가장자리 0단 옹벽선 포함 — 계단선이 표본에서 냄). 끝 마구리 둘레(쪽 없음)는 측점 구간으로 못 바꿔 뺀다
            //   ★[v111.3] 끝면 선(Cross — 노선을 가로지르는 수직면 옹벽선 · 측점 폭 0)도 뺀다
            var candP = new List<int>();
            var cands = new List<RoadGrade.TierLine>();
            if (pieceSnap == null) { cands = s.Lines.Where(l => l.Side != 0 && !l.Cross && (l.Role == RoadGrade.LineRole.Wall) != wallMode).ToList(); candP.AddRange(cands.Select(_ => 0)); }
            else
                for (int pk = 0; pk < pieceSnap.Count; pk++)
                    foreach (var l in pieceSnap[pk].Lines.Where(l => l.Side != 0 && !l.Cross && (l.Role == RoadGrade.LineRole.Wall) != wallMode)) { cands.Add(l); candP.Add(pk); }
            if (cands.Count == 0)
            {
                // ★[v113.0 · 설치 전 검토 M4] 옹벽선이 없어도(폴리선 옹벽이 모두 쉬는 중) 사면 변환은 폴리선 옹벽을 번호로 지울 수 있게
                if (!wallMode && (s.Fresh().BoundWalls?.Count ?? 0) > 0) { changed = RemoveBoundByNumber(ed, s, label); return (changed, s); }
                ed.WriteMessage(wallMode ? $"\n[{label}] 사면선 · 소단선이 없습니다 — 사면이 생기는 도로에서 하세요." : $"\n[{label}] 옹벽이 없습니다 — 먼저 [사면→옹벽]이나 [옹벽 추가]를 하세요.");
                return (changed, s);
            }
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ObjectId layId;
                if (lt.Has(PickLayer)) layId = lt[PickLayer];
                else
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = PickLayer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, GradingBuilder.EdgePickAci) };
                    layId = lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var lay = (LayerTableRecord)tr.GetObject(layId, OpenMode.ForWrite);
                lay.IsOff = false; lay.IsFrozen = false; lay.IsLocked = false;
                // ★[설치 전 검토 L7] 지난번에 Civil이 죽어 남은 고를 선이 있으면 먼저 지움
                try
                {
                    var old = ed.SelectAll(new SelectionFilter(new[] { new TypedValue((int)DxfCode.LayerName, PickLayer) }));
                    if (old.Status == PromptStatus.OK) foreach (var oid in old.Value.GetObjectIds()) try { tr.GetObject(oid, OpenMode.ForWrite).Erase(); } catch { }
                }
                catch { }
                lay.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, GradingBuilder.EdgePickAci);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                for (int i = 0; i < cands.Count; i++)
                {
                    var pts = new Point3dCollection();
                    Point3d? last = null;
                    double zPrev = double.NaN;
                    foreach (var q in cands[i].Pts)
                    {
                        if (!double.IsFinite(q.X) || !double.IsFinite(q.Y)) continue;
                        double z = double.IsFinite(q.Z) ? q.Z : zPrev; if (!double.IsFinite(z)) z = 0; zPrev = z;
                        var p = new Point3d(q.X, q.Y, z);
                        if (last is Point3d lp && lp.DistanceTo(p) < 1e-4) continue;   // 같은 자리 점은 Polyline3d가 못 받음
                        pts.Add(p); last = p;
                    }
                    if (pts.Count < 2) continue;
                    var pl = new Polyline3d(Poly3dType.SimplePoly, pts, false) { Layer = PickLayer };
                    var id = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                    made[id] = i;
                }
                tr.Commit();
            }
            if (made.Count == 0) { ed.WriteMessage($"\n[{label}] 고를 선을 그리지 못했습니다."); return (changed, s); }
            s.SetPickMode(true);
            // ★[v113.0 · JACK 1104 «옹벽 폴리선»] 옹벽 변환 = 클릭한 선 전체(구간지정 없음) · 일부는 폴리선(L)
            ed.WriteMessage(wallMode ? $"\n[{label}] ① {noun}(시안)을 클릭(그 선 전체) → ② 소단 길이 정하고 Enter. (전체해제=C · Esc=취소)"
                                     : $"\n[{label}] ① {noun}(시안)을 클릭 → ② 전체구간/구간지정 → ③ Enter. 폴리선 옹벽 선은 그 옹벽을 통째로 되돌립니다. (전체해제=C · Esc=취소)");
            ObjectId selId = ObjectId.Null;
            void Paint(ObjectId id, bool on)
            {
                if (id.IsNull) return;
                try
                {
                    using var trC = db.TransactionManager.StartTransaction();
                    var en = (Entity)trC.GetObject(id, OpenMode.ForWrite);
                    if (on) en.ColorIndex = ZoneEditCommon.SelAci; else en.ColorIndex = 256;   // 256 = 도면층 색
                    trC.Commit();
                }
                catch { }
            }
            double tol = 3.0;
            try { var vh = (double)AcadApp.GetSystemVariable("VIEWSIZE"); tol = Math.Clamp(vh * 0.02, 0.5, 20); } catch { }
            while (true)
            {
                var peo = new PromptEntityOptions($"\n{noun}을 클릭하세요 (Esc=취소)") { AllowNone = false };
                if (!wallMode && (s.Design.BoundWalls?.Count ?? 0) > 0) peo.Keywords.Add("W", "W", "폴리선옹벽(W)");   // ★[v113.0 · 설치 전 검토 M4] 번호로 지우기(쉬는 옹벽도)
                peo.Keywords.Add("C", "C", "전체해제(C)");
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.Cancel) return (changed, s);
                // 키워드는 Keyword로도 OK로도 온다 — 둘 다 받음(부지와 같음)
                if (per.Status == PromptStatus.Keyword || (per.Status == PromptStatus.OK && per.ObjectId.IsNull))
                {
                    string kw0 = (per.StringResult ?? "").Trim().ToUpperInvariant();
                    if (!wallMode && kw0 == "W")
                    {
                        Paint(selId, false);
                        changed = RemoveBoundByNumber(ed, s, label);
                        if (changed) return (changed, s);
                        continue;
                    }
                    if (kw0 != "C") continue;
                    var dc = s.Fresh();
                    int n0 = dc.TierWalls.Count, nb = dc.BoundWalls?.Count ?? 0;   // ★[v113.0] 폴리선 옹벽도 함께(묻는 글에 둘 다 셈)
                    if (n0 + nb == 0) { ed.WriteMessage("\n → 옹벽 구간이 없습니다."); continue; }
                    // ★[설치 전 검토 L5] 두 쪽 옹벽을 다 지우는 일이라 한 번 묻는다(되돌리기는 됨)
                    var yn = new PromptKeywordOptions($"\n옹벽 {n0}구간{(nb > 0 ? $" · 폴리선 옹벽 {nb}개" : "")}를 모두 지우고 사면으로 되돌릴까요? [예(Y)/아니오(N)] <아니오(N)>");
                    yn.Keywords.Add("Y", "Y", "예(Y)"); yn.Keywords.Add("N", "N", "아니오(N)"); yn.Keywords.Default = "N"; yn.AllowNone = true;
                    var ynr = ed.GetKeywords(yn);
                    if (!((ynr.Status == PromptStatus.OK || ynr.Status == PromptStatus.Keyword) && (ynr.StringResult ?? "").Trim().ToUpperInvariant() == "Y")) { ed.WriteMessage("\n → 전체 해제 안 함."); continue; }
                    dc.TierWalls.Clear();
                    if (nb > 0) dc.BoundWalls = null;
                    s.SaveDesign(); changed = true; s.Recompute();   // ★[검토 L6] 기록이 저장되면 바뀐 것(다시 계산이 실패해도 도로 면은 다시)
                    ed.WriteMessage($"\n → 전체 해제 — 옹벽 {n0}구간{(nb > 0 ? $" · 폴리선 옹벽 {nb}개" : "")}를 지우고 사면으로 다시 계산했습니다.");
                    return (changed, s);
                }
                if (per.Status != PromptStatus.OK) continue;
                int li;
                ObjectId hitId = per.ObjectId;
                if (!made.TryGetValue(hitId, out li))
                {
                    // 다른 객체(도로 면 선 등)를 잡았으면 찍은 자리에서 가장 가까운 고를 선
                    var wp = per.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
                    var hit = RoadGrade.NearestLine(cands, wp.X, wp.Y, tol);
                    if (hit == null) { ed.WriteMessage("\n → 근처에 대상 선이 없습니다 — 시안색 선 근처를 클릭하세요."); continue; }
                    li = hit.Value.Line;
                    hitId = made.FirstOrDefault(kv => kv.Value == li).Key;
                }
                if (selId != hitId) { Paint(selId, false); selId = hitId; Paint(selId, true); }
                var line = cands[li];
                if (pieceSnap != null) { lastSnap = pieceSnap[candP[li]].R; s0Snap = pieceSnap[candP[li]].S0; }
                // ★★[v113.0 → ★v114.3 · JACK 1105 진입도로 옹벽 추가] 사면 변환 — [옹벽 추가]로 지은 옹벽선(TierLine.WallNo)이면 그 기록을 통째로 지운다(구간지정 안 받음 · 측점 구간 셈보다 먼저)
                int boundNo = wallMode ? -1 : line.WallNo > 0 ? line.WallNo : -1;
                if (boundNo >= 0)
                {
                    var db2 = s.Fresh();
                    int rm = db2.BoundWalls?.RemoveAll(b => b.No == boundNo) ?? 0;
                    if (rm == 0) { ed.WriteMessage($"\n → 추가한 옹벽 {boundNo}의 기록을 못 찾았습니다."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    if (db2.BoundWalls != null && db2.BoundWalls.Count == 0) db2.BoundWalls = null;
                    s.SaveDesign(); changed = true; s.Recompute();
                    ed.WriteMessage($"\n[{label}] 추가한 옹벽 {boundNo}를 지우고 사면으로 되돌렸습니다.");
                    try { DiagLog.Append($"\n■ [{label}] 추가한 옹벽(선 옹벽) {boundNo} 통째로 지움"); } catch { }
                    return (changed, s);
                }
                var (w0, w1) = RoadGrade.RangeOnLine(line, null, null);
                // ★[v111.3 · 계획 검토 M3] 이웃 사면선이 끝면 띠로 옹벽 구간 안까지 이어 들어간 몫은 뺌(그 선을 낸 단면이 주인인 자리만 — 안 빼면 변환할 때마다 옆 옹벽 구간이 띠만큼 먹힘)
                (w0, w1) = RoadGrade.OwnerRange(lastSnap, line, w0, w1);
                double span = w1 - w0;
                string what = $"{(line.Side < 0 ? "좌" : "우")} · {(line.Cut ? "절토" : "성토")} {line.K + 1}단 {(line.Role == RoadGrade.LineRole.Crest ? "사면선" : line.Role == RoadGrade.LineRole.Toe ? "소단선" : "옹벽선")}";
                // ★[v111.3 · 설치 전 검토 L3] 고른 선이 모두 옹벽 구간 끝면 띠 안(그 선의 주인 구간이 없음)이면 «NaN m» 대신 까닭
                if (!double.IsFinite(span)) { ed.WriteMessage("\n → 이 선은 옹벽 구간 끝면 띠 안이라 고를 수 없습니다 — 띠 밖(사면 구간)의 선을 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                if (!double.IsFinite(span) || span < 0.5) { ed.WriteMessage($"\n → 이 선은 {span:0.##}m로 너무 짧습니다 — 다른 선을 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                // ② 전체구간 / 구간지정 — 부지와 같은 글 · ★[v113.0 · JACK 1104] 옹벽 변환은 묻지 않고 그 선 전체(일부는 폴리선 L)
                string kw = "A";
                if (!wallMode)
                {
                    var scope = new PromptKeywordOptions($"\n무엇을 바꿀까요? 〈{what} · {span:0.#}m〉 [전체구간(A)/구간지정(P)] <전체구간(A)>");
                    scope.Keywords.Add("A", "A", "전체구간(A)");
                    scope.Keywords.Add("P", "P", "구간지정(P)");
                    scope.Keywords.Default = "A";
                    scope.AllowNone = true;
                    var scr = ed.GetKeywords(scope);
                    if (scr.Status == PromptStatus.Cancel) { Paint(selId, false); selId = ObjectId.Null; continue; }
                    kw = scr.Status == PromptStatus.Keyword || scr.Status == PromptStatus.OK ? (scr.StringResult ?? "A").Trim().ToUpperInvariant() : "A";
                }
                double c0 = w0, c1 = w1;
                bool part = kw == "P";
                if (part)
                {
                    var ruler = new RoadGrade.LineRuler(line);
                    var r1 = PickOnLine(ed, "\n시작점 선택: ", ruler, null, out double ta);
                    if (r1.Status != PromptStatus.OK) { ed.WriteMessage("\n → 구간지정 취소."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    var r2 = PickOnLine(ed, "\n끝점 선택: ", ruler, ta, out double tb);
                    if (r2.Status != PromptStatus.OK) { ed.WriteMessage("\n → 구간지정 취소."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    (c0, c1) = ruler.SRange(ta, tb);   // ★[전체 검토 M4] 두 찍은 점만이 아니라 그 사이 선의 측점 모두(빨간 띠와 같은 구간)
                    if (double.IsFinite(c0) && double.IsFinite(c1)) (c0, c1) = RoadGrade.OwnerRange(lastSnap, line, c0, c1);   // ★[v111.3 · M3]
                    if (!double.IsFinite(c0) || !double.IsFinite(c1) || c1 - c0 < 0.5) { ed.WriteMessage("\n → 구간이 0.5 m보다 짧습니다 — 선을 다시 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    ed.WriteMessage($"\n → 구간지정 {c1 - c0:0.#}m (전체 {span:0.#}m 중)");
                }
                else ed.WriteMessage($"\n → 전체구간({span:0.#}m)을 바꿉니다.");
                double a0 = c0 + s0Snap, a1 = c1 + s0Snap;                     // Core 측점 → 노선 측점(고를 때 모양 기준)
                var d = s.Fresh();
                if (wallMode)
                {
                    // ③ 소단 길이(기본 = 그쪽 사면 소단폭) — Enter = 기본값으로 적용
                    double defL = line.Cut ? d.CutBenchW : d.FillBenchW;
                    var pdo = new PromptDoubleOptions($"\n옹벽 사이 소단 길이(m) 정하고 Enter") { DefaultValue = defL, AllowNegative = false, AllowZero = true, UseDefaultValue = true };
                    var pdr = ed.GetDouble(pdo);
                    if (pdr.Status != PromptStatus.OK) { Paint(selId, false); selId = ObjectId.Null; continue; }
                    d.TierWalls.Add(new[] { a0, a1, (double)line.Side, line.K, pdr.Value });
                    s.SaveDesign(); changed = true; s.Recompute();   // ★[검토 L6] 기록이 저장되면 바뀐 것(다시 계산이 실패해도 도로 면은 다시)
                    ed.WriteMessage($"\n[{label}] {what} — {a0:F1} ~ {a1:F1} m({a1 - a0:F1} m) · {line.K + 1}단부터 옹벽 · 소단 {pdr.Value:0.##} m");
                }
                else
                {
                    // ★[설치 전 검토 M2] 전체구간 = 고른 옹벽선을 낸 기록만 통째로(옆 단 · 옆 구간 기록은 그대로) · 구간지정 = 그 구간의 옹벽을 모두 잘라 냄
                    int n = RoadGrade.CutWallRecords(d.TierWalls, line.Side, a0, a1, whole: !part, match: part ? null : RoadGrade.WallLineMatch(lastSnap, line));
                    if (n == 0) { ed.WriteMessage("\n → 이 자리에는 옹벽 기록이 없습니다 — 다른 옹벽선을 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    s.SaveDesign(); changed = true; s.Recompute();   // ★[검토 L6] 기록이 저장되면 바뀐 것(다시 계산이 실패해도 도로 면은 다시)
                    ed.WriteMessage($"\n[{label}] {(line.Side < 0 ? "좌" : "우")} {a0:F1} ~ {a1:F1} m — 옹벽 {n}구간을 사면으로" + (part ? "(고른 구간만)" : ""));
                }
                return (changed, s);
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[{label}] 실패 — " + ex.Message);
            try { DiagLog.Append($"\n[{label}] 실패 — " + ex); } catch { }
        }
        finally
        {
            // 잠깐 그린 고를 선을 지우고 미리보기 계단선을 다시
            if (made.Count > 0)
                try
                {
                    using var trE = doc.Database.TransactionManager.StartTransaction();
                    foreach (var id in made.Keys)
                        if (!id.IsErased) try { trE.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
                    trE.Commit();
                }
                catch (System.Exception exE) { try { DiagLog.Append($"\n[{label}] 고를 선 지우기 실패 — " + exE.Message); } catch { } }
            try { s?.SetPickMode(false); } catch { }
        }
        return (changed, s);
    }

    /// <summary>★★[v111.2 · JACK 1030 «옹벽변환하면 바로 도로면 만들기도 갱신되어야 해»] [옹벽 변환] · [사면 변환] · 전체 해제 — 기록을 바꿨고 도로 면이 이미 있으면 그 자리에서 다시 만든다
    /// (도로 선 · 노리선 · 부지 노리선 합치기도 같이 — <see cref="RoadSession.MakeSurface"/>). 도로 면을 아직 안 만든 도로는 그대로(미리보기만).</summary>
    static void Convert(bool wallMode)
    {
        var (changed, s) = ConvertCore(wallMode);
        if (!changed || s == null) return;
        // ★★★[v111.7.1] 기록이 바뀌었으면 이 명령 안에서 바로 [업데이트]와 같은 차례(지표면 → 종단도 → 횡단도 — 변환과 함께 실행 취소 한 번 · 자리는 안 물음)
        //   지표면이 아직 없던 도로도 만든다(옛: 있을 때만) · 못 만들면 도킹창 상태 줄에 까닭 + [업데이트]
        RedrawStale(AcadApp.DocumentManager.MdiActiveDocument, s);
    }

    /// <summary>★[v110.3.1] 계단선 위에서 한 점 — 부지 구간지정(ZoneEditCommon.PickOnRing)과 같은 꼴: 시작점은 선을 가로지르는 짧고 굵은 빨간 표시,
    /// 끝점은 시작점부터 커서까지 선을 따라 굵은 빨간 띠 + 화살촉 · 툴팁에 측점 · 구간 길이. 고무줄 점선은 안 씀(찍은 자리 ≠ 잡히는 자리라 헷갈림 — JACK 0910).</summary>
    static PromptPointResult PickOnLine(Editor ed, string msg, RoadGrade.LineRuler ruler, double? fromT, out double t)
    {
        double total = ruler.Total;
        double half = ZoneEditCommon.BandHalf(total), arrow = Math.Max(1.5, Math.Min(total * 0.02, 8.0));
        bool logged = false;
        void OnMove(object? o, PointMonitorEventArgs e)
        {
            try
            {
                if (!e.Context.PointComputed) return;
                var p = e.Context.ComputedPoint;
                double tt = ruler.ParamAt(p.X, p.Y);
                var at = ruler.At(tt);
                var dc = e.Context.DrawContext;
                if (dc != null) { try { dc.SubEntityTraits.Color = ZoneEditCommon.GlyphAci; } catch { } }
                if (fromT == null)
                {
                    e.AppendToolTipText($"시작점 · 측점 {at.S:0.00}m");
                    if (dc == null) return;
                    // 선을 가로지르는 짧은 표시 — 선 방향의 직각
                    var b0 = ruler.At(Math.Max(0, tt - 0.5)); var b1 = ruler.At(Math.Min(total, tt + 0.5));
                    double dx = b1.X - b0.X, dy = b1.Y - b0.Y, L = Math.Sqrt(dx * dx + dy * dy);
                    if (L < 1e-9) return;
                    double nx = -dy / L * arrow * 0.5, ny = dx / L * arrow * 0.5;
                    ZoneEditCommon.DrawBand(dc, new List<Point3> { new(at.X - nx, at.Y - ny, at.Z), new(at.X + nx, at.Y + ny, at.Z) }, half);
                    return;
                }
                double a0 = fromT.Value, span = Math.Abs(tt - a0);
                e.AppendToolTipText($"구간 {span:0.00}m · 측점 {at.S:0.00}m");
                if (dc == null || span < 1e-6) return;
                var path = ruler.Sub(a0, tt).Select(q => new Point3(q.X, q.Y, q.Z)).ToList();
                if (tt < a0) path.Reverse();
                if (path.Count < 2) return;
                ZoneEditCommon.DrawBand(dc, path, half);
                var near = ruler.At(tt + (tt >= a0 ? -1.0 : 1.0));
                ZoneEditCommon.DrawArrow(dc, new Point3(at.X, at.Y, at.Z), at.X - near.X, at.Y - near.Y, arrow);
            }
            catch (System.Exception gex)
            {
                if (!logged) { logged = true; try { DiagLog.Append("\n[진입도로] 커서 표식 실패 — " + gex.Message); } catch { } }
            }
        }
        var opt = new PromptPointOptions(msg) { AllowNone = false };
        bool hooked = false;
        try { ed.PointMonitor += OnMove; hooked = true; } catch { }
        PromptPointResult r;
        try { r = ed.GetPoint(opt); }
        finally { if (hooked) { try { ed.PointMonitor -= OnMove; } catch { } } }
        t = 0;
        if (r.Status == PromptStatus.OK) { var w = r.Value.TransformBy(ed.CurrentUserCoordinateSystem); t = ruler.ParamAt(w.X, w.Y); }
        return r;
    }

    /// <summary>사업 경계(닫힌 폴리선) — 사면이 밖으로 나가는 구간을 옹벽으로 제안.</summary>
    [CommandMethod("DHROADLIMIT")]
    public void Limit()
    {
        var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            var s = Need(ed); if (s == null) return;
            var peo = new PromptEntityOptions("\n[진입도로] 사업 경계 폴리선(Enter = 지우기): ") { AllowNone = true };
            peo.SetRejectMessage("\n폴리선만 됩니다."); peo.AddAllowedClass(typeof(Polyline), true);
            var per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.None) s.Fresh().LimitHandle = "";
            else if (per.Status == PromptStatus.OK) s.Fresh().LimitHandle = per.ObjectId.Handle.ToString();
            else return;
            s.SaveDesign(); s.Recompute();
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 사업 경계 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 사업 경계 실패 — " + ex); } catch { }
        }
    }

    /// <summary>[종단 자동으로 다시](옛 [종단 초안 다시]) — 계획 종단 PVI를 모두 지우고 바탕면을 따라 처음부터(노선이 바뀌어 끝 측점이 달라졌을 때 등).
    /// <para>★[v111.7 · JACK 1102] 손으로 옮긴 PVI가 사라지므로 한 번 묻는다(기본 = 아니오).</para></summary>
    [CommandMethod("DHROADREDRAFT")]
    public void Redraft()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
        var s = Need(ed); if (s == null) return;
        var yn = new PromptKeywordOptions("\n[종단 자동으로 다시] 계획 종단 PVI를 모두 지우고 처음부터 다시 잡습니다 — 손으로 옮긴 PVI는 사라집니다. 할까요? [예(Y)/아니오(N)] <아니오(N)>");
        yn.Keywords.Add("Y", "Y", "예(Y)"); yn.Keywords.Add("N", "N", "아니오(N)"); yn.Keywords.Default = "N"; yn.AllowNone = true;
        var ynr = ed.GetKeywords(yn);
        if (!((ynr.Status == PromptStatus.OK || ynr.Status == PromptStatus.Keyword) && (ynr.StringResult ?? "").Trim().ToUpperInvariant() == "Y"))
        { ed.WriteMessage("\n[종단 자동으로 다시] 안 함 — 계획 종단은 그대로입니다."); return; }
        try
        {
            var dr = s.RedraftNow();
            ed.WriteMessage("\n[진입도로] 종단 자동으로 다시 — " + dr.Text);
        }
        catch (System.Exception ex) { ed.WriteMessage("\n[진입도로] 종단 자동으로 다시 실패 — " + ex.Message); }
        try { s.Recompute(); } catch { }
    }

    /// <summary>★★[v111.2 · JACK 1030 «팝업창에 PVI 수정 기능 — 넣기 · 삭제» · 인터뷰: 도킹창 단추 · 넣는 높이 = 지금 계획선 위] 종단도를 찍은 측점에 PVI 넣기(모양 그대로 — 넣은 뒤 잡아끌어 높이) · Enter까지 여러 번.
    /// <para>★[v111.7.1] Redraw — 끝에 골라 둔 계획 종단(그립)이 명령이 끝나도 남게.</para></summary>
    [CommandMethod("DHROADPVIADD", CommandFlags.Modal | CommandFlags.Redraw)]
    public void PviAdd() => PviEdit(true);

    /// <summary>★★[v111.2] 종단도에서 찍은 자리 가까운 PVI 지우기(처음 · 끝은 Civil이 못 지움) · Enter까지 여러 번.</summary>
    [CommandMethod("DHROADPVIDEL", CommandFlags.Modal | CommandFlags.Redraw)]
    public void PviDel() => PviEdit(false);

    /// <summary>★★[v111.7.1 · JACK 1102 «모르는 사람은 그걸 어디에 찍는지 모른다»] 먼저 이 도로 종단도로 확대(지금 화면 가운데에서 가장 가까운 장) + 계획 종단 강조 → 찍기(지금과 같음) →
    /// 끝나면 계획 종단을 골라 둔다(PVI 그립이 보여 바로 끌어 높이를 바꿈). 종단도가 없으면 한 줄 알리고 안 묻는다.</summary>
    static void PviEdit(bool add)
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor; var db = doc.Database;
        var s = Need(ed); if (s == null) return;
        string tag = add ? "[PVI 넣기]" : "[PVI 지우기]";
        try
        {
            (ObjectId View, Extents3d Ext)? tgt;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                tgt = PviView(tr, s.AlignId, ViewCenterWcs(ed));
                tr.Commit();
            }
            if (tgt == null) { ed.WriteMessage($"\n{tag} 이 도로의 종단도가 없습니다 — 진입도로 [종단도]부터"); return; }
            ZoomTo(ed, tgt.Value.Ext);
            var planId = s.PlanId;
            Hilite(db, planId, true);
            int done = 0;
            bool was = s.Suppress; s.Suppress = true;   // 우리가 쓰는 동안 바뀜 표시 막음 — 끝에서 한 번 다시 계산
            try
            {
                while (true)
                {
                    var pr = ed.GetPoint(new PromptPointOptions(add
                        ? "\n[PVI 넣기] 넣을 자리 — 종단도 안을 찍으세요(지금 계획선 위에 넣음 · Enter 끝): "
                        : "\n[PVI 지우기] 지울 PVI 근처를 찍으세요(종단도 안 · Enter 끝): ") { AllowNone = true });
                    if (pr.Status != PromptStatus.OK) break;
                    var p = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    string msg;
                    // ★[설치 전 검토 M1] 한 번 찍기가 실패해도 다음 찍기 · 끝의 다시 계산은 그대로(앞에서 넣은 PVI가 미리보기에 안 보이는 일 없게)
                    try
                    {
                        using var tr = db.TransactionManager.StartTransaction();
                        msg = add ? PviAddAt(tr, s, p) : PviDelAt(tr, s, p);
                        tr.Commit();
                    }
                    catch (System.Exception exP) { msg = "⚠못 함 — " + exP.Message; }
                    ed.WriteMessage($"\n{tag} {msg}");
                    if (!msg.StartsWith("⚠")) done++;
                }
            }
            finally
            {
                s.Suppress = was;
                Hilite(db, planId, false);
                if (done > 0) { try { s.Recompute(); } catch { } }
            }
            // ★[v111.7.2 · JACK 1102 «마지막에 팝업창 단추를 누르면 한꺼번에»] 미리보기 선만 바로 — 지표면 · 종단도 · 횡단도는 [업데이트]
            if (done > 0) ed.WriteMessage($"\n{tag} {done}개 — 미리보기 선만 바뀌었습니다 · 진입도로 창 [업데이트]를 누르면 지표면 · 종단도 · 횡단도를 다시 만듭니다 · 실행 취소(U)는 이번에 한 것을 한꺼번에 되돌림");
            // 끝 — 계획 종단을 골라 둠(그립 → 바로 끌어 PVI 높이)
            try { if (!planId.IsNull && !planId.IsErased) ed.SetImpliedSelection(new[] { planId }); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n{tag} 실패 — " + ex.Message);
            try { DiagLog.Append($"\n{tag} 실패 — " + ex); } catch { }
        }
    }

    /// <summary>★[v111.7.1 · JACK 1102] [PVI 넣기] · [PVI 지우기]가 확대할 이 도로 종단뷰 — 지금 화면 가운데 <paramref name="near"/>가 든 장 · 없으면 가장 가까운 장(<see cref="ProfileSheetRules.NearestBox"/>).
    /// 확대 범위 = 그 종단뷰 범위(밴드 포함). 종단뷰가 없으면 null.</summary>
    internal static (ObjectId View, Extents3d Ext)? PviView(Transaction tr, ObjectId aid, Point3d near)
    {
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        var ids = new List<ObjectId>(); var exts = new List<Extents3d>(); var boxes = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (ObjectId vid in al.GetProfileViewIds())
        {
            try
            {
                if (vid.IsErased || tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.ProfileView pv) continue;
                var e = pv.GeometricExtents;
                ids.Add(vid); exts.Add(e); boxes.Add((e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y));
            }
            catch { }
        }
        int i = ProfileSheetRules.NearestBox(boxes, near.X, near.Y);
        return i < 0 ? null : (ids[i], exts[i]);
    }

    /// <summary>화면(DCS) ↔ 세계(WCS) — 지금 뷰의 방향 · 비틀림 · 목표점.</summary>
    static Matrix3d DcsToWcs(ViewTableRecord v) =>
        Matrix3d.Rotation(-v.ViewTwist, v.ViewDirection, v.Target) * Matrix3d.Displacement(v.Target - Point3d.Origin) * Matrix3d.PlaneToWorld(v.ViewDirection);

    /// <summary>지금 화면 가운데(세계 좌표) — 못 재면 원점.</summary>
    static Point3d ViewCenterWcs(Editor ed)
    {
        try
        {
            using var v = ed.GetCurrentView();
            return new Point3d(v.CenterPoint.X, v.CenterPoint.Y, 0).TransformBy(DcsToWcs(v));
        }
        catch { return Point3d.Origin; }
    }

    /// <summary>★[v111.7.1] 그 범위로 확대(둘레 8 % 여유 · 화면 가로세로 비 유지 — <see cref="ProfileSheetRules.ZoomWindow"/>). 못 하면 말없이 그대로(확대는 돕는 일).</summary>
    static void ZoomTo(Editor ed, Extents3d ext)
    {
        try
        {
            using var v = ed.GetCurrentView();
            var e = ext; e.TransformBy(DcsToWcs(v).Inverse());
            var (cx, cy, w, h) = ProfileSheetRules.ZoomWindow(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y, v.Height > 0 ? v.Width / v.Height : 1.0);
            v.CenterPoint = new Point2d(cx, cy); v.Width = w; v.Height = h;
            ed.SetCurrentView(v);
        }
        catch (System.Exception ex) { try { DiagLog.Append("\n[진입도로] 종단도로 확대 못 함 — " + ex.Message); } catch { } }
    }

    /// <summary>계획 종단 강조(찍는 동안) · 끄기.</summary>
    static void Hilite(Database db, ObjectId id, bool on)
    {
        try
        {
            if (id.IsNull || id.IsErased) return;
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(id, OpenMode.ForRead) is Entity e) { if (on) e.Highlight(); else e.Unhighlight(); }
            tr.Commit();
        }
        catch { }
    }

    /// <summary>찍은 점이 든 이 도로의 종단뷰 → (뷰, 측점, 표고) · 없으면 null(종단도가 여러 장이면 그 점이 든 장).</summary>
    static (CivilDb.ProfileView View, double St, double El)? PickInView(Transaction tr, ObjectId aid, Point3d p)
    {
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        foreach (ObjectId vid in al.GetProfileViewIds())
        {
            try
            {
                if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.ProfileView pv) continue;
                double st = 0, el = 0;
                if (!pv.FindStationAndElevationAtXY(p.X, p.Y, ref st, ref el)) continue;
                if (st < pv.StationStart - 1e-6 || st > pv.StationEnd + 1e-6 || el < pv.ElevationMin - 1e-6 || el > pv.ElevationMax + 1e-6) continue;
                return (pv, st, el);
            }
            catch { }
        }
        return null;
    }

    const double PviGap = 0.5;   // 이웃 PVI와 이보다 가까우면 안 넣음(m)

    static string PviAddAt(Transaction tr, RoadSession s, Point3d p)
    {
        var hit = PickInView(tr, s.AlignId, p);
        if (hit == null) return "⚠종단도 밖입니다 — 이 도로 종단도 안을 찍으세요";
        var prof = (CivilDb.Profile)tr.GetObject(s.PlanId, OpenMode.ForRead);   // ★[검토 L4] 거절이면 안 씀(쓰기는 넣기 바로 앞)
        double st = Math.Round(hit.Value.St, 2);
        if (prof.PVIs.Count < 2) return "⚠계획 종단이 비었습니다";
        double a = prof.PVIs[0].RawStation, b = prof.PVIs[prof.PVIs.Count - 1].RawStation;
        if (st <= a + PviGap || st >= b - PviGap) return $"⚠측점 {st:F2} m — 처음 · 끝 PVI 사이({a:F1} ~ {b:F1} m)만 넣습니다";
        // ★[도로 마무리 ① · 계획 5.4] 도로 구간 밖(가상으로 자른 끝 · 빈 구간 안)은 거절
        if (s.Ranges.Count > 0 && !RoadRanges.Inside(s.Ranges, st)) return $"⚠측점 {st:F2} m — 도로가 없는 자리(부지 바닥 · 잘린 끝)입니다";
        for (int i = 0; i < prof.PVIs.Count; i++)
            if (Math.Abs(prof.PVIs[i].RawStation - st) < PviGap) return $"⚠측점 {st:F2} m 곁 {PviGap} m 안에 PVI가 있습니다(측점 {prof.PVIs[i].RawStation:F2} m)";
        double z = prof.ElevationAt(st);
        prof.UpgradeOpen();
        prof.PVIs.AddPVI(st, z);
        return $"측점 {st:F2} m · 표고 {z:F2} m 넣음(PVI {prof.PVIs.Count}개) — 잡아끌어 높이를 바꾸세요";
    }

    static string PviDelAt(Transaction tr, RoadSession s, Point3d p)
    {
        var hit = PickInView(tr, s.AlignId, p);
        if (hit == null) return "⚠종단도 밖입니다 — 이 도로 종단도 안을 찍으세요";
        var pv = hit.Value.View;
        var prof = (CivilDb.Profile)tr.GetObject(s.PlanId, OpenMode.ForRead);
        int best = -1; double bd = double.MaxValue;
        for (int i = 0; i < prof.PVIs.Count; i++)
        {
            var q = prof.PVIs[i];
            if (q.RawStation < pv.StationStart - 1e-6 || q.RawStation > pv.StationEnd + 1e-6) continue;
            double x = 0, y = 0;
            try { if (!pv.FindXYAtStationAndElevation(q.RawStation, q.Elevation, ref x, ref y)) continue; } catch { continue; }
            double d = Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d < bd) { bd = d; best = i; }
        }
        // 찍기 허용 = 화면 15 px(지금 확대 기준)
        double tol = 1.0;
        try
        {
            double vs = System.Convert.ToDouble(AcadApp.GetSystemVariable("VIEWSIZE"));
            var sc = (Point2d)AcadApp.GetSystemVariable("SCREENSIZE");
            if (sc.Y > 0) tol = vs / sc.Y * 15;
        }
        catch { }
        if (best < 0 || bd > tol) return "⚠찍은 자리 가까이에 PVI가 없습니다 — PVI(꺾인 점) 바로 위를 찍으세요";
        if (best == 0 || best == prof.PVIs.Count - 1) return "⚠처음 · 끝 PVI는 지울 수 없습니다(노선 시점 · 종점 높이)";
        double st = prof.PVIs[best].RawStation, z = prof.PVIs[best].Elevation;
        prof.UpgradeOpen();
        prof.PVIs.RemoveAt(best);
        return $"측점 {st:F2} m · 표고 {z:F2} m 지움(PVI {prof.PVIs.Count}개)";
    }

    /// <summary>★★★[v111.7.2 · JACK 1102 «측점(PVI) 움직일 때마다 모든 게 재작성되니 오래 걸린다 · 마지막에 팝업창 단추를 누르면 한꺼번에 업데이트»] 진입도로 창 ■ 결과 [업데이트] —
    /// 지표면 → 종단도 → 횡단도를 한 명령(실행 취소 한 번)으로. 종단도 · 횡단도 자리를 모르면(처음 · 지웠음 · 일부만 옮김 · 겹침) 커서 이름표로 자리를 묻는다(종단도 먼저 · Esc = 그것만 건너뜀).
    /// 바뀐 것이 없으면 지표면만 새로(옛 [진입도로 지표면 생성]처럼). 옛 저절로 다시 만들기(DHROADAUTOREDRAW) · [다시 시도](DHROADSURF)를 이것 하나로.</summary>
    [CommandMethod("DHROADUPDATE")]
    public void Update()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var s = Need(doc.Editor); if (s == null) return;
        RedrawStale(doc, s, ask: true, surfaceIfFresh: true);
    }

    /// <summary>★★[v111.7 · JACK 1102] 지금과 다른 진입도로 지표면 · 종단도 · 횡단도를 다시 만든다 — 차례 = ① 지표면(옛것이거나 없으면 — <see cref="RoadSession.MakeSurface"/>) →
    /// ② 종단도(알림 + 지금 놓인 자리 — <see cref="RoadSession.PlaceNow"/> · 다른 종단도와 겹치면 안 그림) → ③ 횡단도(지표면 · 종단도를 다시 만든 뒤 다시 잰 알림 + 지금 놓인 자리 + 그릴 수 있음).
    /// 모두 이 한 명령 안 = 실행 취소 한 번.
    /// <para>★[v111.7.2] <paramref name="ask"/>(= [업데이트])이면 자리를 모르거나 겹치는 종단도 · 한 번도 안 그린 종단도 · 횡단도는 커서 이름표로 자리를 묻는다 —
    /// 아니면(도면설정 저장 · [옹벽 변환]/[사면 변환] · [노리선]) 그것만 안 하고 상태 줄에 까닭. 기록한 뷰가 모두 지워졌으면 처음과 같이 물음(횡단도는 세션에 한 번).</para>
    /// <para>옛것을 지운 뒤 못 그리면 도킹창에 늘 남는 «다시 그리지 못했습니다 — U로 되돌리면 …»(<see cref="RoadSession.SheetLost"/>).
    /// 끝에 다시 계산 · 남은 바뀜이 있으면 그 까닭(실패 · 자리 모름 · 겹침)을 적음(상태 줄). 명령창은 요약 한 줄(짓는 줄은 진단 로그로).</para></summary>
    internal static void RedrawStale(Document doc, RoadSession s, bool ask = false, bool surfaceIfFresh = false)
    {
        var ed = doc.Editor; var db = doc.Database;
        string pre = "";
        bool force = false, run = false;
        var log = new System.Text.StringBuilder($"[진입도로] {(ask ? "업데이트" : "다시 만들기")} — {s.Name}\n");
        string why = "", bad = "", surfMsg = "", left = "";
        bool surfDone = false, sheetDone = false, xsecDone = false, finalDone = false;
        static string Add(string a, string b) => a.Length > 0 ? a + " · " + b : b;
        try
        {
            try { s.RecomputeCore(); } catch { }
            pre = s.StaleKey;
            var d0 = s.Design;
            // [업데이트] — 한 번도 안 놓은 종단도 · 횡단도도 할 일(자리를 물음) · ★[v111.7.2 검토 낮음] 기록한 뷰가 하나도 안 남았으면(모두 지움) 처음과 같이
            //   횡단도는 이 세션에 한 번만 물음(Esc로 안 그리기로 했으면 [업데이트]마다 다시 안 물음 — 리본 [횡단도]는 그대로) · 지표면만 새로(force)는 횡단도와 따로
            bool sheetGone = !RoadSession.AnyAlive(db, d0.SheetViews), xsecGone = !RoadSession.AnyAlive(db, d0.XsecViews);
            // ★[v113.3 · JACK 1105 «설정 바꿔 [업데이트]를 누르면 횡단을 선택해서 배치하라고 나와 — 이게 맞어?» → «안 묻기»] 한 번도 안 그린 횡단도는 [업데이트]가 묻지 않는다(리본 · 창 [횡단도]로만) —
            //   그린 적 있는 횡단도의 자리를 잃었을 때(지웠거나 일부만 옮김)만 묻는다
            bool mayAskXsec = ask && !xsecGone;
            bool firstSheet = ask && sheetGone, firstXsec = false;
            force = surfaceIfFresh && pre.Length == 0 && !firstSheet;
            run = pre.Length > 0 || force || firstSheet || firstXsec;
            if (!run) return;
            GradingSettings.SyncToDocument(doc);
            // ⓪ ★★[도로 마무리 ① · 계획 5.3] 끝 맞추기(끝 맞춤을 켠 도로 · 맞춤 필요) — 노선 · 종단을 고친 뒤 아래 지표면 → 종단도 → 횡단도가 이어서(같은 명령 = 실행 취소 한 번)
            if (s.Design.EndsOn && s.EndsStale.Length > 0)
            {
                string fx;
                using (BusyPopup.Scope("도로 끝을 맞추는 중입니다.")) fx = s.FixEndsNow(false);
                log.AppendLine("  끝 맞추기 — " + fx);
                if (fx.StartsWith("⚠")) bad = Add(bad, fx.TrimStart('⚠'));
                else if (why.Length == 0) why = "도로 끝 맞춤";
                try { s.RecomputeCore(); } catch { }
            }
            // ★[v111.7.1 · 검토 열린 물음] [PVI 넣기]가 골라 둔 계획 종단(그립)을 먼저 풂 — 아래에서 그 종단뷰를 지우고 새로 그림(그립이 지운 뷰 위에 남지 않게)
            try { ed.SetImpliedSelection(System.Array.Empty<ObjectId>()); } catch { }
            // ① 지표면 — 옛것 · 없음(· [업데이트]에서 바뀐 것이 없으면 새로)
            if (s.SurfaceStale.Length > 0 || s.SurfaceMissing || force)
            {
                if (s.Last == null || s.Last.Error.Length > 0) bad = Add(bad, "지표면 — 계산 오류");
                else
                {
                    string w = s.SurfaceStale.Length > 0 ? s.SurfaceStale : s.SurfaceMissing ? "지표면이 없어" : "";
                    try
                    {
                        using (BusyPopup.Scope(s.SurfaceMissing ? "진입도로 지표면 만드는 중입니다." : "진입도로 지표면 다시 만드는 중입니다.")) surfMsg = s.MakeSurface();
                        surfDone = true; if (why.Length == 0) why = w;
                        log.AppendLine("  지표면 — " + surfMsg);
                        // ★[v111.8 · M2] 최종지표면 실패는 조용히 넘기지 않음 — 실패 글(상태 줄 까닭 · 깜박 유지 · 명령창)
                        if (s.FinalNote.StartsWith("⚠")) bad = Add(bad, s.FinalNote.TrimStart('⚠'));
                    }
                    catch (System.Exception exF)
                    {
                        bad = Add(bad, "지표면을 못 만들었습니다 — " + exF.Message);
                        log.AppendLine("  지표면 실패 — " + exF);
                    }
                    try { s.RecomputeCore(); } catch { }   // 새 면 → 횡단도 알림 · 열쇠를 지금으로
                }
            }
            // ①b ★[v111.8 · 계획 4장] 도로 면은 맞는데 최종지표면만 옛것 · 없음(앞 실패 포함) — 최종면만 다시
            else if (s.FinalStale)
            {
                string w = s.FinalState == FinalSurfaceBuilder.State.Missing ? "최종지표면이 없어" : "최종지표면이 옛것입니다";
                surfMsg = s.MakeFinal();
                log.AppendLine("  최종지표면 — " + surfMsg);
                if (surfMsg.StartsWith("⚠")) bad = Add(bad, surfMsg.TrimStart('⚠'));
                else { finalDone = true; if (why.Length == 0) why = w; }
                try { s.RecomputeCore(); } catch { }
            }
            // ② 종단도
            var d = s.Design;
            Point3d? sheetAt, xsecAt;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                sheetAt = s.SheetStale.Length > 0 ? RoadSession.PlaceNow(tr, db, d.SheetAt, d.SheetViews, d.SheetViewAt) : null;
                xsecAt = RoadSession.PlaceNow(tr, db, d.XsecAt, d.XsecViews, d.XsecViewAt);   // 종단도를 다시 그려도 횡단뷰는 그대로 — 지금 잼
                tr.Commit();
            }
            bool needSheet = s.SheetStale.Length > 0 || firstSheet;
            string sheetHit = needSheet && sheetAt is Point3d at0 ? s.SheetOverlap(at0) : "";
            if (needSheet && sheetAt is Point3d at && sheetHit.Length == 0)
            {
                string w = s.SheetStale;
                try
                {
                    string msg;
                    ProfileCommand.Quiet = true;
                    using (BusyPopup.Scope("종단도를 같은 자리에 다시 그리는 중입니다.")) msg = s.MakeSheets(at, log);
                    sheetDone = true; if (why.Length == 0) why = w;
                    log.AppendLine("  종단도 — " + msg);
                    // ★[설치 전 검토 · 중간] 다시 그린 종단도(그래프가 높아졌을 수 있음)가 남의 것과 겹치면 — 물을 수 있으면 다른 자리를 물어 옮겨 그림 · 아니면 알림
                    string after = RoadSheets.PlacedOverlap(db, s.AlignId, s.Design.SheetScale > 0 ? s.Design.SheetScale : RoadSheets.ScaleFor(db).Scale);   // ★[v113.1] 그린 축척
                    if (after.Length > 0)
                    {
                        log.AppendLine($"  종단도 — 다시 그린 뒤 {after}와(과) 겹침");
                        if (ask)
                        {
                            ProfileCommand.Quiet = false;
                            ed.WriteMessage($"\n[진입도로] ⚠다시 그린 종단도가 {after}와(과) 겹칩니다(그래프가 높아짐) — 다른 빈 자리를 찍으세요.");
                            AskAndPlaceSheets(doc, s, again: true, mustMove: true);
                        }
                        else bad = Add(bad, $"종단도가 {after}와(과) 겹칩니다(그래프가 높아짐) — [업데이트] 또는 리본 [종단도]로 자리를 찍으세요");
                    }
                }
                catch (System.Exception exS)
                {
                    bad = Add(bad, s.SheetLost.Length > 0 ? s.SheetLost : "종단도를 못 그렸습니다 — " + exS.Message);
                    log.AppendLine("  종단도 실패 — " + exS);
                }
                finally { ProfileCommand.Quiet = false; }
            }
            else if (needSheet && ask)
            {
                // ★[v111.7.2] 자리를 모름(처음 · 지웠음 · 일부만 옮김) · 겹침 — 커서 이름표로 자리를 물음(Enter = 지난 자리 · Esc = 종단도만 건너뜀)
                if (sheetHit.Length > 0) ed.WriteMessage($"\n[진입도로] ⚠지난 자리는 {sheetHit}와(과) 겹칩니다(장 수가 늘어남) — 빈 자리(위 · 아래 · 옆)에 찍으세요.");
                else if (!firstSheet) ed.WriteMessage("\n[진입도로] 종단도 자리를 모릅니다(지웠거나 일부만 옮김) — 놓을 자리를 찍으세요.");
                if (AskAndPlaceSheets(doc, s, again: true)) { sheetDone = true; if (why.Length == 0) why = s.SheetStale.Length > 0 ? s.SheetStale : "종단도가 없어"; }
                log.AppendLine($"  종단도 — 자리를 물음 → {(sheetDone ? "그림" : "안 그림")}");
            }
            else if (needSheet && sheetHit.Length > 0) bad = Add(bad, $"종단도 자리가 {sheetHit}와(과) 겹칩니다(장 수가 늘어남) — [업데이트] 또는 리본 [종단도]로 자리를 찍으세요");
            // ③ 횡단도 — 지표면 · 종단도를 다시 만들었으면 지표면 · 측점이 바뀌었을 수 있다 → 지금 바뀜을 다시 잼
            string xs = s.XsecStaleNow();
            bool needXsec = xs.Length > 0 || firstXsec;
            string refuse = needXsec ? RoadXsec.Refusal(s) : "";
            if (needXsec && refuse.Length > 0)
            {
                if (ask) bad = Add(bad, "횡단도 — " + refuse);
                log.AppendLine("  횡단도 — 못 그림: " + refuse);
            }
            else if (needXsec && xsecAt is Point3d xat)
            {
                try
                {
                    string msg;
                    using (BusyPopup.Scope("횡단도를 같은 자리에 다시 그리는 중입니다.")) msg = s.MakeXsec(xat, log);
                    log.AppendLine("  횡단도 — " + msg);
                    if (msg.StartsWith("⚠")) bad = Add(bad, s.XsecLost.Length > 0 ? s.XsecLost : "횡단도를 못 그렸습니다 — [업데이트] 또는 리본 [횡단도]로 다시");
                    else { xsecDone = true; if (why.Length == 0) why = xs.Length > 0 ? xs : "횡단도가 없어"; }
                }
                catch (System.Exception exX)
                {
                    bad = Add(bad, s.XsecLost.Length > 0 ? s.XsecLost : "횡단도를 못 그렸습니다 — " + exX.Message);
                    log.AppendLine("  횡단도 실패 — " + exX);
                }
            }
            else if (needXsec && mayAskXsec)
            {
                if (xsecGone) s.XsecAsked = true;
                if (!firstXsec) ed.WriteMessage("\n[진입도로] 횡단도 자리를 모릅니다(지웠거나 일부만 옮김) — 놓을 자리를 찍으세요.");
                if (AskAndPlaceXsec(doc, s)) { xsecDone = true; if (why.Length == 0) why = xs.Length > 0 ? xs : "횡단도가 없어"; }
                log.AppendLine($"  횡단도 — 자리를 물음 → {(xsecDone ? "그림" : "안 그림")}");
            }
        }
        catch (System.Exception ex)
        {
            bad = Add(bad, ex.Message);
            log.AppendLine("  실패 — " + ex);
        }
        finally
        {
            if (run)
            {
                try { s.RecomputeCore(); } catch { }
                // ★[v111.7.2] 남은 바뀜이 있으면 그 까닭(실패 · 자리 모름 · 겹침) — 도킹창 상태 줄
                string post = s.StaleKey;
                if (post.Length > 0 && bad.Length == 0) { try { using var dl = doc.LockDocument(); left = s.UpdatePlan().WhyNot; } catch { } }
                s.NoteUpdate(post, bad.Length > 0 ? "실패 — " + bad : left);
                log.AppendLine($"  끝 — 지표면 {surfDone} · 최종지표면만 {finalDone} · 종단도 {sheetDone} · 횡단도 {xsecDone}" + (post.Length > 0 ? " · 남음(" + (bad.Length > 0 ? "실패" : left.Length > 0 ? left : "다시 해도 남음") + ")" : " · 모두 최신"));
            }
        }
        if (surfDone || finalDone) { try { ed.Regen(); } catch { } }
        try
        {
            var parts = new List<string>();
            if (surfDone) parts.Add("지표면"); else if (finalDone) parts.Add("최종지표면"); if (sheetDone) parts.Add("종단도"); if (xsecDone) parts.Add("횡단도");
            string what = string.Join(" · ", parts);
            string tick = surfMsg.Contains("노리선") ? " · 노리선은 [노리선]으로 다시" : "";
            // ★[v111.8 · 7.7] 최종지표면은 만들었는데 경고(가파른 띠 — 사면이 땅에 못 닿음 등)가 있으면 그 글
            if ((surfDone || finalDone) && !s.FinalNote.StartsWith("⚠") && s.FinalNote.Contains('⚠')) tick += " · " + s.FinalNote[s.FinalNote.IndexOf('⚠')..];
            if (force && surfDone && !sheetDone && !xsecDone) ed.WriteMessage("\n[진입도로] " + surfMsg);
            else if (what.Length > 0) ed.WriteMessage($"\n[진입도로] {Because(why)} 다시 만들었습니다 — {what}{tick}" + (bad.Length > 0 ? " · ⚠" + bad : ""));
            else if (bad.Length > 0) ed.WriteMessage("\n[진입도로] ⚠" + bad);
            // ★[v111.7.1] 할 수 있는 것이 없음(자리 모름 · 겹침) — 말없이 끝내지 않음
            else if (left.Length > 0 && !ask) ed.WriteMessage("\n[진입도로] " + left + " — 진입도로 창 [업데이트]를 누르면 자리를 묻습니다");
            if (run) DiagLog.Append("\n" + log);
        }
        catch { }
    }

    /// <summary>★[v111.7 · v111.7.1 지표면 까닭 붙임] 알림 글(SurfaceStale · SheetStale · XsecStale) → 다시 만든 까닭 한 마디.</summary>
    static string Because(string stale) => stale switch
    {
        "종단이 수정되었습니다" => "종단이 바뀌어",
        "노선이 수정되었습니다" => "노선이 바뀌어",
        "횡단 · 사면 · 옹벽 값이 바뀌었습니다" => "횡단 · 사면 · 옹벽 값이 바뀌어",
        "바탕면이 바뀌었습니다" => "바탕면이 바뀌어",
        "축척이 바뀌었습니다" => "축척이 바뀌어",
        "지표면이 다시 만들어졌습니다" => "진입도로 지표면이 다시 만들어져",
        "지표면이 없어" => "지표면이 없어",
        "옛 판에서 만든 지표면입니다" => "옛 판에서 만든 지표면이라",
        "횡단도 배치가 바뀌었습니다" => "횡단도 배치가 바뀌어",   // ★[v111.7.2]
        "종단도가 없어" => "종단도가 없어",                       // ★[v111.7.2] [업데이트]가 처음 놓음
        "횡단도가 없어" => "횡단도가 없어",
        "최종지표면이 없어" => "최종지표면이 없어",            // ★[v111.8]
        "최종지표면이 옛것입니다" => "최종지표면이 옛것이라",
        _ => "설계가 바뀌어",
    };

    [CommandMethod("DHROADOFF")]
    public void Off()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        try { RoadSession.Stop(doc); } catch { }
        doc.Editor.WriteMessage("\n[진입도로] 미리보기를 껐습니다(노선 · 종단은 그대로 — [진입도로]로 노선을 고르면 다시 열림).");
    }
}
