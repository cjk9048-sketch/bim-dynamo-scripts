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
                    ppo.UseBasePoint = true; ppo.BasePoint = pts[^1].TransformBy(ucs.Inverse()); ppo.UseDashedLine = false;
                    ppo.Keywords.Add("U", "U", "되돌리기(U)");
                    ppo.AppendKeywordsToMessage = false;   // 글에 이미 [되돌리기(U)] — 두 번 붙지 않게
                    ppo.AllowNone = pts.Count >= 2;
                }
                var pr = ed.GetPoint(ppo);
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
        try
        {
            ObjectId aid = srcId;
            RoadDesign? d = null;
            if (isAlign)
            {
                using var tr = db.TransactionManager.StartTransaction();
                d = RoadDesign.Read(tr, aid);
                tr.Commit();
                if (d != null) { Open(doc, aid); return; }        // 이미 진입도로 — 다시 연다
            }
            // ★[재검토 낮음 1] 바탕면을 노선 만들기 «앞»에서 — 없으면 아무것도 안 만들고 멈춤(주인 없는 DH도로_N이 남지 않게)
            var (bs, bsName) = RoadBuilder.PickBase(db, cdoc);
            if (bs.IsNull) { SectionCommand.Refuse(ed, "바탕면(정지면_DH 또는 원지반)이 없습니다 — 원지반부터 만들어 주세요."); return; }
            if (!isAlign)
            {
                double rad = RoadDesign.LastCurveR;
                aid = RoadBuilder.AlignmentFromPolyline(doc, srcId, rad, log);
                created = aid;
                d = new RoadDesign { CurveR = rad };
            }
            d ??= new RoadDesign { OwnCurves = false };   // 기존 Civil 노선(사람이 설계한 곡선) — 곡선 반경 칸이 안 바꿈
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
            // 종단뷰 — v110.2b에서 부지 [종단도]와 같은 꼴(도곽 · 밴드)로 바꿈(JACK 1029 결정 · 부지 종단 코드를 대상 인자로 고치는 일이 먼저)
            var ppr = ed.GetPoint("\n[진입도로] 종단뷰를 놓을 자리(Esc = 안 그림): ");
            if (ppr.Status == PromptStatus.OK)
            {
                try { RoadBuilder.MakeView(doc, aid, ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem)); log.AppendLine("  종단뷰 그림 — 계획 종단(PVI)을 끌어 옮기면 바로 다시 계산합니다"); }
                catch (System.Exception ex) { log.AppendLine("  ⚠종단뷰를 못 그림 — " + ex.Message); }
            }
            ed.WriteMessage("\n" + log.ToString().TrimEnd());
            try { DiagLog.Append("\n" + log); } catch { }
            Open(doc, aid);
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
        }
        finally
        {
            if (tempPolyline) SectionCommand.EraseQuiet(db, srcId);
        }
    }

    static void Open(Document doc, ObjectId aid)
    {
        var s = RoadSession.Start(doc, aid);
        RoadPalette.Show();
        doc.Editor.WriteMessage($"\n[진입도로] {s.Name} — {(s.Last?.Text ?? s.Note)}");
    }

    static RoadSession? Need(Editor ed)
    {
        var s = RoadSession.Of(AcadApp.DocumentManager.MdiActiveDocument);
        if (s == null) ed.WriteMessage("\n[진입도로] 열린 진입도로가 없습니다 — [진입도로]로 노선을 고르세요.");
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
            if (Math.Abs(b - a) < 0.5) { ed.WriteMessage("\n[진입도로 옹벽] 구간이 0.5 m보다 짧습니다 — 노선을 따라 두 점을 찍으세요."); return; }
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

    const string PickLayer = "DH-도로계단선_선택";

    static void Convert(bool wallMode)
    {
        string label = wallMode ? "옹벽 변환" : "사면 변환", noun = wallMode ? "계단선" : "옹벽선";
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ed = doc.Editor;
        RoadSession? s = null;
        var made = new Dictionary<ObjectId, int>();
        try
        {
            s = Need(ed); if (s == null) return;
            if (s.Last == null || s.Last.Error.Length > 0) { ed.WriteMessage($"\n[{label}] 계산된 도로가 없습니다 — [다시 계산] 뒤 하세요."); return; }
            // ★[설치 전 검토 L8] 배치 공간(모형 뷰포트 밖)에서는 잠깐 그린 선을 못 고른다
            try
            {
                if (System.Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0 && System.Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) == 1)
                { ed.WriteMessage($"\n[{label}] 모형 공간(또는 배치의 모형 뷰포트 안)에서 하세요."); return; }
            }
            catch { }
            // 고를 선 — 옹벽 변환 = 사면선 · 소단선 · 사면 변환 = 옹벽선(도로 가장자리 0단 옹벽선 포함 — 계단선이 표본에서 냄). 끝 마구리 둘레(쪽 없음)는 측점 구간으로 못 바꿔 뺀다
            var cands = s.Lines.Where(l => l.Side != 0 && (l.Role == RoadGrade.LineRole.Wall) != wallMode).ToList();
            if (cands.Count == 0)
            {
                ed.WriteMessage(wallMode ? $"\n[{label}] 사면선 · 소단선이 없습니다 — 사면이 생기는 도로에서 하세요." : $"\n[{label}] 옹벽이 없습니다 — 먼저 [옹벽 변환]을 하세요.");
                return;
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
            if (made.Count == 0) { ed.WriteMessage($"\n[{label}] 고를 선을 그리지 못했습니다."); return; }
            s.SetPickMode(true);
            ed.WriteMessage($"\n[{label}] ① {noun}(시안)을 클릭 → ② 전체구간/구간지정 → ③ " + (wallMode ? "소단 길이 정하고 Enter." : "Enter.") + " (전체해제=C · Esc=취소)");
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
                peo.Keywords.Add("C", "C", "전체해제(C)");
                var per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.Cancel) return;
                // 키워드는 Keyword로도 OK로도 온다 — 둘 다 받음(부지와 같음)
                if (per.Status == PromptStatus.Keyword || (per.Status == PromptStatus.OK && per.ObjectId.IsNull))
                {
                    if ((per.StringResult ?? "").Trim().ToUpperInvariant() != "C") continue;
                    var dc = s.Fresh();
                    int n0 = dc.TierWalls.Count;
                    if (n0 == 0) { ed.WriteMessage("\n → 옹벽 구간이 없습니다."); continue; }
                    // ★[설치 전 검토 L5] 두 쪽 옹벽을 다 지우는 일이라 한 번 묻는다(되돌리기는 됨)
                    var yn = new PromptKeywordOptions($"\n옹벽 {n0}구간을 모두 지우고 사면으로 되돌릴까요? [예(Y)/아니오(N)] <아니오(N)>");
                    yn.Keywords.Add("Y", "Y", "예(Y)"); yn.Keywords.Add("N", "N", "아니오(N)"); yn.Keywords.Default = "N"; yn.AllowNone = true;
                    var ynr = ed.GetKeywords(yn);
                    if (!((ynr.Status == PromptStatus.OK || ynr.Status == PromptStatus.Keyword) && (ynr.StringResult ?? "").Trim().ToUpperInvariant() == "Y")) { ed.WriteMessage("\n → 전체 해제 안 함."); continue; }
                    dc.TierWalls.Clear();
                    s.SaveDesign(); s.Recompute();
                    ed.WriteMessage($"\n → 전체 해제 — 옹벽 {n0}구간을 지우고 사면으로 다시 계산했습니다.");
                    return;
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
                var (w0, w1) = RoadGrade.RangeOnLine(line, null, null);
                double span = w1 - w0;
                string what = $"{(line.Side < 0 ? "좌" : "우")} · {(line.Cut ? "절토" : "성토")} {line.K + 1}단 {(line.Role == RoadGrade.LineRole.Crest ? "사면선" : line.Role == RoadGrade.LineRole.Toe ? "소단선" : "옹벽선")}";
                if (!double.IsFinite(span) || span < 0.5) { ed.WriteMessage($"\n → 이 선은 {span:0.##}m로 너무 짧습니다 — 다른 선을 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                // ② 전체구간 / 구간지정 — 부지와 같은 글
                var scope = new PromptKeywordOptions($"\n무엇을 바꿀까요? 〈{what} · {span:0.#}m〉 [전체구간(A)/구간지정(P)] <전체구간(A)>");
                scope.Keywords.Add("A", "A", "전체구간(A)");
                scope.Keywords.Add("P", "P", "구간지정(P)");
                scope.Keywords.Default = "A";
                scope.AllowNone = true;
                var scr = ed.GetKeywords(scope);
                if (scr.Status == PromptStatus.Cancel) { Paint(selId, false); selId = ObjectId.Null; continue; }
                string kw = scr.Status == PromptStatus.Keyword || scr.Status == PromptStatus.OK ? (scr.StringResult ?? "A").Trim().ToUpperInvariant() : "A";
                double c0 = w0, c1 = w1;
                bool part = kw == "P";
                if (part)
                {
                    var ruler = new RoadGrade.LineRuler(line);
                    var r1 = PickOnLine(ed, "\n시작점 선택: ", ruler, null, out double ta);
                    if (r1.Status != PromptStatus.OK) { ed.WriteMessage("\n → 구간지정 취소."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    var r2 = PickOnLine(ed, "\n끝점 선택: ", ruler, ta, out double tb);
                    if (r2.Status != PromptStatus.OK) { ed.WriteMessage("\n → 구간지정 취소."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    double sa = ruler.At(ta).S, sb = ruler.At(tb).S;
                    c0 = Math.Min(sa, sb); c1 = Math.Max(sa, sb);
                    if (!double.IsFinite(c0) || !double.IsFinite(c1) || c1 - c0 < 0.5) { ed.WriteMessage("\n → 구간이 0.5 m보다 짧습니다 — 선을 다시 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    ed.WriteMessage($"\n → 구간지정 {c1 - c0:0.#}m (전체 {span:0.#}m 중)");
                }
                else ed.WriteMessage($"\n → 전체구간({span:0.#}m)을 바꿉니다.");
                double a0 = c0 + s.S0, a1 = c1 + s.S0;                         // Core 측점 → 노선 측점
                var d = s.Fresh();
                if (wallMode)
                {
                    // ③ 소단 길이(기본 = 그쪽 사면 소단폭) — Enter = 기본값으로 적용
                    double defL = line.Cut ? d.CutBenchW : d.FillBenchW;
                    var pdo = new PromptDoubleOptions($"\n옹벽 사이 소단 길이(m) 정하고 Enter") { DefaultValue = defL, AllowNegative = false, AllowZero = true, UseDefaultValue = true };
                    var pdr = ed.GetDouble(pdo);
                    if (pdr.Status != PromptStatus.OK) { Paint(selId, false); selId = ObjectId.Null; continue; }
                    d.TierWalls.Add(new[] { a0, a1, (double)line.Side, line.K, pdr.Value });
                    s.SaveDesign(); s.Recompute();
                    ed.WriteMessage($"\n[{label}] {what} — {a0:F1} ~ {a1:F1} m({a1 - a0:F1} m) · {line.K + 1}단부터 옹벽 · 소단 {pdr.Value:0.##} m");
                }
                else
                {
                    // ★[설치 전 검토 M2] 전체구간 = 고른 옹벽선을 낸 기록만 통째로(옆 단 · 옆 구간 기록은 그대로) · 구간지정 = 그 구간의 옹벽을 모두 잘라 냄
                    int n = RoadGrade.CutWallRecords(d.TierWalls, line.Side, a0, a1, whole: !part, match: part ? null : RoadGrade.WallLineMatch(s.Last, line));
                    if (n == 0) { ed.WriteMessage("\n → 이 자리에는 옹벽 기록이 없습니다 — 다른 옹벽선을 클릭하세요."); Paint(selId, false); selId = ObjectId.Null; continue; }
                    s.SaveDesign(); s.Recompute();
                    ed.WriteMessage($"\n[{label}] {(line.Side < 0 ? "좌" : "우")} {a0:F1} ~ {a1:F1} m — 옹벽 {n}구간을 사면으로" + (part ? "(고른 구간만)" : ""));
                }
                return;
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

    /// <summary>계획 종단 초안 다시(지금 PVI는 지워짐) — 노선이 바뀌어 끝 측점이 달라졌을 때.</summary>
    [CommandMethod("DHROADREDRAFT")]
    public void Redraft()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
        var s = Need(ed); if (s == null) return;
        try
        {
            var dr = s.RedraftNow();
            ed.WriteMessage("\n[진입도로] 종단 초안 — " + dr.Text);
        }
        catch (System.Exception ex) { ed.WriteMessage("\n[진입도로] 종단 초안 실패 — " + ex.Message); }
        try { s.Recompute(); } catch { }
    }

    /// <summary>★[v110.1 · JACK 1029] [도로 면 만들기] — 도로 + 사면을 따로 지표면(«진입도로_DH»)으로. 정지면_DH는 안 건드림 · 다시 누르면 새로.</summary>
    [CommandMethod("DHROADSURF")]
    public void Surf()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor;
        var s = Need(ed); if (s == null) return;
        try
        {
            string msg;
            using (BusyPopup.Scope("도로 면 만드는 중입니다.")) msg = s.MakeSurface();
            ed.WriteMessage("\n[진입도로] " + msg);
            try { ed.Regen(); } catch { }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage("\n[진입도로] 도로 면 실패 — " + ex.Message);
            try { DiagLog.Append("\n[진입도로] 도로 면 실패 — " + ex); } catch { }
        }
        try { s.Recompute(); } catch { }
    }

    [CommandMethod("DHROADOFF")]
    public void Off()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        try { RoadSession.Stop(doc); } catch { }
        doc.Editor.WriteMessage("\n[진입도로] 미리보기를 껐습니다(노선 · 종단은 그대로 — [진입도로]로 노선을 고르면 다시 열림).");
    }
}
