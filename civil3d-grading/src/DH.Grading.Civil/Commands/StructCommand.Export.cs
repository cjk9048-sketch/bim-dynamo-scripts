using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Grading.Core;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Civil.Commands;

// ── ★★[v116.9 · JACK 1110 «구조물 좌표계 내보내기»] Civil → Revit 애드인 기준점 파일(계획_Civil구조물기준점_내보내기.md) ──
//   인터뷰 1110: 도면 표시 안 남김 · 이름 기본값 = 이 도면에서 마지막에 쓴 이름(처음엔 도면 이름) · 원지반 + 최종 지표면 표고 둘 다
// ── ★★[v117.1 · JACK 1111 «박스로 영역 · 팝업 · 레빗에서 3D로» · 계획 2026-11-11_구조물내보내기_팝업_3D.md] 팝업 + 3D 영역 파일 ──
//   창 = 이름 · [두 점 찍기] · [네모 찍기] · 넣을 것 · 저장 · [내보내기] — 단추를 누르면 창이 닫히고 명령이 찍은 뒤 다시 띄운다(AltDialog 방식).
//   기억 = 도면마다 · 프로그램이 켜 있는 동안(도면 안에는 안 씀 — 읽기만 하는 명령 · DBMOD 0 → 0). 화면 없는 Civil = 명령줄(같은 셈 함수).
public sealed partial class StructCommand
{
    /// <summary>도면마다 기억 — 이름 · 두 점(WCS · 도면 단위) · 네모(m · 1 mm) · 넣을 것 · 저장 경로.</summary>
    sealed class PtsMemo
    {
        public string Name = "";
        public Point3d? W1, W2;
        public GroundCrop.Box? Box;
        public HashSet<string> Include = new(StructRegion3D.Keys);
        public string SavePath = "";
        public bool PathConfirmed;
    }
    static readonly Dictionary<string, PtsMemo> Memos = new(System.StringComparer.OrdinalIgnoreCase);
    static string LastPtsDir = "";
    static double? WinLeft, WinTop;
    /// <summary>화면 없는 Civil 시험 고리(HLST3DCAP) — 0보다 크면 세모 한도를 이 값으로(<see cref="FinalSurfaceBuilder.FailGateForTest"/>와 같은 꼴).</summary>
#pragma warning disable CS0649   // 시험(반사)만 씀
    internal static int TrisCapForTest;
#pragma warning restore CS0649
    static int Cap => TrisCapForTest > 0 ? TrisCapForTest : StructRegion3D.MaxTris;

    /// <summary>내보낼 지표면(명령 한 번에 한 번 고름) — 세모(m) · 이름 · 레이어 · 출처 · 진입도로가 들었나.</summary>
    sealed class Terrain
    {
        public List<Tri> Tris = new();
        public string Name = "", Layer = "", Source = "";
        public bool InTerrain;
    }

    /// <summary>모은 선 하나(m · WCS) — <see cref="Has3D"/> 거짓 = Z 없음(진입도로 윤곽 · 2D 계획 경계 → 메시에 입힘).</summary>
    sealed class Raw
    {
        public string Kind = "", Layer = "";
        public bool Closed, Has3D = true;
        public List<Point3> Pts = new();
    }

    /// <summary>네모 하나의 결과 — 메시(자른 세모) · 선(다듬은 3D · 종류 전부) · 종류별 수 · 미리보기 선(네모로 자른 XY · struct 포함).</summary>
    sealed class Region
    {
        public GroundCrop.Box Box;
        public Terrain? Terr;
        public StructRegion3D.Mesh? Mesh;
        public int Tris;
        public List<StructRegion3D.Line3> Lines = new();
        public Dictionary<string, int> Counts = new();
        public List<StructPoints.SketchLine> Sketch = new();
        public List<string> Warn = new();
        public string Note = "";
    }

    /// <summary>명령 한 번의 문맥.</summary>
    sealed class Ctx
    {
        public Document Doc = null!; public Database Db = null!; public Editor Ed = null!;
        public double F; public int Ins; public bool Assumed;
        public string DocName = ""; public bool Named;
        public System.Text.StringBuilder Log = new();
        public readonly List<string> ModTrail = new();
        public bool TerrTried; public Terrain? Terr;
        public void Mod(string at) { try { ModTrail.Add($"{at} {AcadApp.GetSystemVariable("DBMOD")}"); } catch { } }
    }

    static double? ElevAt(Transaction tr, ObjectId sid, double x, double y)
    {
        if (sid.IsNull || sid.IsErased) return null;
        try { return tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface ts ? ts.FindElevationAtXY(x, y) : null; }
        catch { return null; }   // 면 밖
    }

    /// <summary>배치(종이) 공간에서 찍으면 종이 좌표가 그대로 적힌다(뷰포트 너머 스냅이어도) — 진입도로 명령과 같은 문.</summary>
    static bool InPaper()
    {
        try { return System.Convert.ToInt32(AcadApp.GetSystemVariable("TILEMODE")) == 0 && System.Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) == 1; }
        catch { return false; }
    }
    const string PaperWhy = "원인: 배치(종이) 공간에서는 도면 좌표를 못 찍음 — 모형 공간(또는 배치의 모형 뷰포트 안)에서 다시";

    [CommandMethod("DHSTRUCTPTS")]
    public void ExportPoints()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
        var c = new Ctx { Doc = doc, Db = doc.Database, Ed = doc.Editor, Log = new System.Text.StringBuilder("\n■ DHSTRUCTPTS(구조물 좌표계 내보내기)\n") };
        c.Mod("시작");
        GradingSettings.SyncToDocument(doc);
        c.Mod("설정");
        if (InPaper()) { Done(c.Ed, c.Log, "⚠" + PaperWhy); return; }
        c.Ins = (int)c.Db.Insunits;
        if (!StructPoints.TryToMeter(c.Ins, out c.F, out c.Assumed))
        { Done(c.Ed, c.Log, $"⚠원인: 도면 단위({StructPoints.UnitName(c.Ins)})를 m로 바꾸는 표가 없음 — UNITS로 m · mm · cm 중 하나로 정한 뒤 다시"); return; }
        c.DocName = doc.Name ?? "";
        try { c.Named = doc.IsNamedDrawing; } catch { }
        if (!Memos.TryGetValue(c.DocName, out var memo)) Memos[c.DocName] = memo = new PtsMemo();
        if (memo.Name.Length == 0) { memo.Name = Path.GetFileNameWithoutExtension(c.DocName); if (string.IsNullOrWhiteSpace(memo.Name)) memo.Name = "구조물"; }
        if (SelfCheckLog.Headless) RunLine(c, memo);
        else RunPopup(c, memo);
    }

    // ══════════════════════════════ 화면 없는 Civil — 명령줄(같은 셈 함수) ══════════════════════════════

    static void RunLine(Ctx c, PtsMemo memo)
    {
        var ed = c.Ed;
        var nr = ed.GetString(new PromptStringOptions("\n[구조물] 이름") { AllowSpaces = true, DefaultValue = memo.Name, UseDefaultValue = true });
        if (nr.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
        string name = (nr.StringResult ?? "").Trim(); if (name.Length == 0) name = memo.Name;
        if (!PickTwo(c, out var w1, out var w2)) { ed.WriteMessage("\n[구조물] 취소"); return; }
        if (!PickBox(c, allowNone: true, out var box)) { ed.WriteMessage("\n[구조물] 취소"); return; }
        var include = new HashSet<string>(StructRegion3D.Keys);
        Region? reg = null;
        if (box is GroundCrop.Box bx)
        {
            while (true)
            {
                var ir = ed.GetString(new PromptStringOptions("\n[구조물] 넣을 것 F(최종지표면) W(옹벽선) B(소단선) S(사면선) L(정지 경계) R(진입도로 윤곽)") { DefaultValue = "전부", UseDefaultValue = true });
                if (ir.Status != PromptStatus.OK) { ed.WriteMessage("\n[구조물] 취소"); return; }
                if (StructRegion3D.ParseInclude(ir.StringResult, out include)) break;
                ed.WriteMessage("\n[구조물] 글자 F W B S L R 조합 · 또는 Enter(전부)");
            }
            reg = BuildRegion(c, bx);
        }
        // 저장(기본 = 도면 폴더 · 없으면 마지막 폴더) — 폴더를 치면 그 폴더에 기본 이름
        string defDir = DefaultDir(c), defFile = StructPoints.SafeFileName(name) + "_Civil기준점.json";
        var pr = ed.GetString(new PromptStringOptions("\n[구조물] 저장할 파일") { AllowSpaces = true, DefaultValue = Path.Combine(defDir, defFile), UseDefaultValue = true });
        string? path = pr.Status == PromptStatus.OK ? (pr.StringResult ?? "").Trim().Trim('"') : null;
        if (path != null && path.Length == 0) path = Path.Combine(defDir, defFile);
        if (string.IsNullOrEmpty(path)) { ed.WriteMessage("\n[구조물] 취소"); return; }
        try { if (Directory.Exists(path)) path = Path.Combine(path, defFile); } catch { }
        if (!Export(c, name, path, w1, w2, reg, include, out string msg)) { Done(ed, c.Log, "⚠" + msg); return; }
        memo.Name = name;
    }

    // ══════════════════════════════ 팝업 ══════════════════════════════

    static void RunPopup(Ctx c, PtsMemo memo)
    {
        var ed = c.Ed;
        if (memo.SavePath.Length == 0) memo.SavePath = Path.Combine(DefaultDir(c), StructPoints.SafeFileName(memo.Name) + "_Civil기준점.json");
        PickMark? markPts = null, markBox = null;
        void Paint()
        {
            try { markPts?.Dispose(); } catch { }
            try { markBox?.Dispose(); } catch { }
            markPts = markBox = null;
            if (memo.W1 is Point3d a && memo.W2 is Point3d b)
                markPts = PickMark.PaintPath(c.Doc, new[] { new Point3(a.X, a.Y, a.Z), new Point3(b.X, b.Y, b.Z) }, false);
            if (memo.Box is GroundCrop.Box q)
            {
                double f = c.F, z = memo.W1?.Z ?? 0;
                markBox = PickMark.PaintPath(c.Doc, new[] { new Point3(q.X0 / f, q.Y0 / f, z), new Point3(q.X1 / f, q.Y0 / f, z), new Point3(q.X1 / f, q.Y1 / f, z), new Point3(q.X0 / f, q.Y1 / f, z) }, true);
            }
        }
        Region? reg = null;
        string error = "";
        try
        {
            if (memo.Box is GroundCrop.Box b0) reg = BuildRegion(c, b0);   // 지난번 네모(이 도면 · 이 세션)
            string hint = SurfaceHint(c.Db);
            Paint();
            for (int guard = 0; guard < 200; guard++)
            {
                var v = new StructExportDialog.View
                {
                    Name = memo.Name, Path = memo.SavePath, PathConfirmed = memo.PathConfirmed, Include = new HashSet<string>(memo.Include),
                    DefaultDir = DefaultDir(c), DefaultFile = StructPoints.SafeFileName(memo.Name) + "_Civil기준점.json",
                    Error = error, Cap = Cap, Foot = FootText(c),
                };
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                if (memo.W1 is Point3d a && memo.W2 is Point3d b)
                {
                    double d = System.Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) * c.F;
                    v.PtsText = string.Format(ic, "점1 ({0:F3}, {1:F3}) · 점2 ({2:F3}, {3:F3}) · {4:F3} m", a.X * c.F, a.Y * c.F, b.X * c.F, b.Y * c.F, d);
                }
                var warns = new List<string>();
                if (reg != null)
                {
                    v.BoxText = string.Format(ic, "{0:F1} × {1:F1} m", reg.Box.W, reg.Box.H);
                    v.SurfAvail = reg.Terr != null; v.SurfName = reg.Terr?.Name ?? ""; v.Tris = reg.Tris; v.Counts = new Dictionary<string, int>(reg.Counts);
                    warns.AddRange(reg.Warn);
                    if (memo.W1 is Point3d pa && memo.W2 is Point3d pb && !(reg.Box.Contains(pa.X * c.F, pa.Y * c.F) && reg.Box.Contains(pb.X * c.F, pb.Y * c.F)))
                        warns.Insert(0, "기준점이 네모 밖에 있습니다");
                }
                else { v.SurfAvail = hint.Length > 0; v.SurfName = hint; }
                v.Warn = string.Join(" · ", warns);
                var dlg = new StructExportDialog(v);
                if (WinLeft is double wl && WinTop is double wt) { dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; dlg.Left = wl; dlg.Top = wt; }
                AcadApp.ShowModalWindow(dlg);
                try { WinLeft = dlg.Left; WinTop = dlg.Top; } catch { }
                if (dlg.NameText.Length > 0) memo.Name = dlg.NameText;
                memo.SavePath = dlg.PathText; memo.PathConfirmed = dlg.PathConfirmed;
                if (reg != null)
                {
                    var inc = dlg.Include;
                    if (!v.SurfAvail && memo.Include.Contains("final")) inc.Add("final");   // 지표면이 없어 잠근 칸은 고른 것 그대로 둠
                    memo.Include = inc;
                }
                error = "";
                if (dlg.DialogResult != true || dlg.Action == StructExportDialog.Act.None) { ed.WriteMessage("\n[구조물] 닫음"); return; }
                switch (dlg.Action)
                {
                    case StructExportDialog.Act.PickPts:
                        if (InPaper()) { error = PaperWhy; continue; }
                        if (PickTwo(c, out var w1, out var w2)) { memo.W1 = w1; memo.W2 = w2; Paint(); }
                        continue;   // Esc = 이전 값 그대로
                    case StructExportDialog.Act.PickBox:
                        if (InPaper()) { error = PaperWhy; continue; }
                        if (PickBox(c, allowNone: false, out var nb) && nb is GroundCrop.Box bx)
                        {
                            memo.Box = bx;
                            reg = BuildRegion(c, bx);
                            Paint();   // 돌린 UCS에서도 WCS 네모를 다시 보여 줌
                        }
                        continue;
                    case StructExportDialog.Act.ClearBox:
                        memo.Box = null; reg = null; Paint();
                        continue;
                    case StructExportDialog.Act.Export:
                        if (memo.W1 is not Point3d e1 || memo.W2 is not Point3d e2) { error = "기준점 두 점을 찍으세요"; continue; }
                        // ★[설치 전 검토 중간] 한 번 [...]로 고른 경로는 이번 내보내기에만 «확인됨» — 다음 내보내기(다른 이름 등)는 있는 파일이면 다시 묻는다
                        if (Export(c, memo.Name, memo.SavePath, e1, e2, reg, memo.Include, out string msg)) { memo.PathConfirmed = false; return; }
                        error = msg;   // 창으로 돌아가 값 지킴(계획 9절 L7)
                        ed.WriteMessage("\n[구조물] ⚠" + msg);
                        try { DiagLog.Append(c.Log.ToString() + "  → ⚠" + msg + "(창으로 돌아감)\n"); } catch { }
                        c.Log.Clear().Append("\n■ DHSTRUCTPTS(구조물 좌표계 내보내기 · 다시)\n");
                        continue;
                }
            }
        }
        finally
        {
            // 내보내기 · 닫기 · Esc · 오류 — 어느 끝에서도 임시 표시를 걷는다
            try { markPts?.Dispose(); } catch { }
            try { markBox?.Dispose(); } catch { }
        }
    }

    static string DefaultDir(Ctx c)
    {
        string d = c.Named ? (Path.GetDirectoryName(c.DocName) ?? "") : LastPtsDir;
        if (d.Length == 0 || !Directory.Exists(d)) d = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        return d;
    }

    static string FootText(Ctx c)
    {
        int epsg = GradingSettings.ExportEpsg;
        return (epsg > 0 ? "EPSG " + epsg : "좌표계 모름") + " · 단위 m" + (c.Ins != 6 ? $"(도면 {StructPoints.UnitName(c.Ins)} → m)" : "");
    }

    /// <summary>창 열 때 싼 확인(계획 9절 M6) — 최종지표면_DH · 정지면_DH 이름만 본다(세모는 안 읽음). 없으면 빈 글.</summary>
    static string SurfaceHint(Database db)
    {
        string fin = "", plan = "";
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db).GetSurfaceIds())
                try
                {
                    if (sid.IsErased || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface s) continue;
                    if (s.Name == FinalSurfaceBuilder.Name) fin = s.Name;
                    else if (SectionCommand.IsBase(s.Name, WallCompositeCommand.PlanName)) plan = s.Name;
                }
                catch { }
            tr.Commit();
        }
        catch { }
        return fin.Length > 0 ? fin : plan;
    }

    // ══════════════════════════════ 찍기 ══════════════════════════════

    /// <summary>점 1 → 점 2(UCS로 받은 값을 WCS로 · 1 m 미만이면 다시). Esc = 거짓.</summary>
    static bool PickTwo(Ctx c, out Point3d w1, out Point3d w2)
    {
        var ed = c.Ed; w1 = w2 = default;
        var ucs = ed.CurrentUserCoordinateSystem;
        var r1 = ed.GetPoint(new PromptPointOptions("\n[구조물] 점 1 — 구조물 모서리(끝점 · 교차점 스냅): "));
        if (r1.Status != PromptStatus.OK) return false;
        w1 = r1.Value.TransformBy(ucs);
        while (true)
        {
            var r2 = ed.GetPoint(new PromptPointOptions("\n[구조물] 점 2 — 다른 모서리(Revit에서도 이 순서로 찍습니다): ") { UseBasePoint = true, BasePoint = r1.Value, UseDashedLine = true });
            if (r2.Status != PromptStatus.OK) return false;
            w2 = r2.Value.TransformBy(ucs);
            double dm = System.Math.Sqrt((w2.X - w1.X) * (w2.X - w1.X) + (w2.Y - w1.Y) * (w2.Y - w1.Y)) * c.F;
            if (dm >= StructPoints.MinDist) return true;
            ed.WriteMessage($"\n[구조물] 두 점 거리 {dm:F3} m — {StructPoints.MinDist:F0} m 넘게 떨어진 모서리를 고르세요");
        }
    }

    /// <summary>네모 — 첫 모서리(<paramref name="allowNone"/>면 Enter = 없음) → 다른 모서리(고무줄) → 두 모서리를 WCS로 → WCS 축 네모(m · 1 mm) · 한 변 1 m 미만이면 다시.
    /// 돌려줌 = 거짓이면 Esc(<paramref name="box"/> 그대로 없음).</summary>
    static bool PickBox(Ctx c, bool allowNone, out GroundCrop.Box? box)
    {
        var ed = c.Ed; box = null;
        var ucs = ed.CurrentUserCoordinateSystem;
        while (true)
        {
            var r1 = ed.GetPoint(new PromptPointOptions(allowNone ? "\n[구조물] 영역 첫 모서리 <없음>: " : "\n[구조물] 영역 첫 모서리: ") { AllowNone = allowNone });
            if (allowNone && r1.Status == PromptStatus.None) return true;
            if (r1.Status != PromptStatus.OK) return false;
            var r2 = ed.GetCorner(new PromptCornerOptions("\n[구조물] 영역 다른 모서리: ", r1.Value));
            if (r2.Status != PromptStatus.OK) return false;
            Point3d a = r1.Value.TransformBy(ucs), b = r2.Value.TransformBy(ucs);
            if (StructRegion3D.TryBox(a.X * c.F, a.Y * c.F, b.X * c.F, b.Y * c.F, out var bx)) { box = bx; return true; }
            ed.WriteMessage($"\n[구조물] 네모 한 변이 {StructRegion3D.MinSide:F0} m 미만 — 다시 찍으세요");
        }
    }

    // ══════════════════════════════ 지표면 · 선 모으기 ══════════════════════════════

    /// <summary>★[계획 9절 M6] 지표면 — 명령 한 번에 한 번(창이 다시 떠도 재사용 · 창이 떠 있는 동안 도면은 안 바뀜).
    /// 네 갈래: 도로 없음 → 정지면_DH · 최종면 맞음 → 그 면 · 옛것/없음 → 메모리 합침 · 합치기 실패 → 정지면_DH(진입도로 빠짐) · 아무것도 없으면 null(점과 선만).</summary>
    static Terrain? EnsureTerrain(Ctx c, List<string> warn)
    {
        if (c.TerrTried) return c.Terr;
        c.TerrTried = true;
        var db = c.Db; var log = c.Log;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var busy = BusyPopup.Scope("3D 영역 — 지표면 읽는 중");
        try
        {
            var plog = new System.Text.StringBuilder(); var pw = new List<string>();
            var P = FinalSurfaceBuilder.PickTerrain(db, plog, pw, meshOnly: true);
            long tPick = sw.ElapsedMilliseconds;
            var t = new Terrain();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var (pids, pnames) = WallCompositeCommand.Candidates(cdoc, tr, WallCompositeCommand.PlanName, SectionCommand.GroundOf(db));
                string planLayer = "";
                try { if (pids.Count >= 1) planLayer = ((CivilDb.Surface)tr.GetObject(pids[0], OpenMode.ForRead)).Layer ?? ""; } catch { }
                if (P.Tris != null)
                {
                    t.Tris = P.Tris; t.Name = FinalSurfaceBuilder.Name + "(메모리 합침)"; t.Layer = planLayer; t.Source = "메모리 합침"; t.InTerrain = true;
                }
                else if (!P.Id.IsNull)
                {
                    var ts = (CivilDb.TinSurface)tr.GetObject(P.Id, OpenMode.ForRead);
                    t.Tris = FinalSurfaceBuilder.ReadAll(ts, ts.Name); t.Name = ts.Name; t.Layer = ts.Layer ?? ""; t.InTerrain = P.InTerrain;
                    t.Source = P.InTerrain ? "최종면 맞음" : "정지면_DH(진입도로 빠짐)";
                }
                else if (!P.HasRoads && pids.Count == 1)
                {
                    var ts = (CivilDb.TinSurface)tr.GetObject(pids[0], OpenMode.ForRead);
                    t.Tris = FinalSurfaceBuilder.ReadAll(ts, ts.Name); t.Name = ts.Name; t.Layer = ts.Layer ?? ""; t.Source = "정지면_DH(진입도로 없음)";
                }
                else
                {
                    string why = pids.Count == 0 ? "정지면_DH 없음" : $"정지면_DH가 여럿({string.Join(",", pnames)})";
                    log.AppendLine("  3D 영역 지표면: 없음 — " + why + " → 점과 선만");
                    warn.Add("내보낼 지표면 없음(" + why + ")");
                    tr.Commit();
                    return c.Terr = null;
                }
                tr.Commit();
            }
            if (t.Tris.Count == 0) { warn.Add("지표면 세모 0"); return c.Terr = null; }
            if (c.F != 1.0)
            {
                double f = c.F;
                t.Tris = t.Tris.Select(q => new Tri(new Point3(q.A.X * f, q.A.Y * f, q.A.Z * f), new Point3(q.B.X * f, q.B.Y * f, q.B.Z * f), new Point3(q.C.X * f, q.C.Y * f, q.C.Z * f))).ToList();
            }
            foreach (var w in pw) warn.Add(w.TrimStart('⚠'));
            log.AppendLine($"  3D 영역 지표면: «{t.Name}»({t.Source}) 세모 {t.Tris.Count:N0} · 고르기 {tPick} ms · 읽기 {sw.ElapsedMilliseconds - tPick} ms");
            if (plog.Length > 0) log.AppendLine("    " + plog.ToString().Trim().Replace("\n", "\n    "));
            return c.Terr = t;
        }
        catch (System.Exception ex)
        {
            log.AppendLine("  3D 영역 지표면: 못 읽음 — " + ex.Message + " → 점과 선만");
            warn.Add("지표면 못 읽음 → 점과 선만");
            return c.Terr = null;
        }
    }

    static string? KindOf(string layer)
    {
        if (layer == "DH-옹벽선" || layer == RoadSession.LayWall) return "wall";
        if (layer.StartsWith("DH-소단선", System.StringComparison.Ordinal) || layer == RoadSession.LayBench) return "bench";   // ★[v117.1] 부지 소단선 = DH-소단선-절토/성토/전환
        if (layer == "DH-정지경계") return "site";
        if (layer == StructImport.LayPlan) return "struct";
        if (layer.StartsWith("DH-사면선", System.StringComparison.Ordinal) || layer == RoadSession.LaySlope) return "slope";   // ★[v117.1 Revit 확인 1111] 사면선 = slope(옛 other)
        return null;
    }

    /// <summary>★[v116.13 · 약속 §2-6 · §3 «sketch»] → ★[v117.1] 선 모으기(XYZ · m) — 모형 공간의 우리 평면 선(옹벽선 · 소단선 · 사면선 · 정지 경계 · 구조물 평면) +
    /// 계획 부지 경계(정지 기록의 계획 폴리선) + 진입도로 포장 윤곽(3D 보기와 같은 셈 — <see cref="RoadLanes.Pavement"/> · Z 없음).
    /// 범위 = 도면 단위 상자(<paramref name="x0"/> ~ <paramref name="y1"/>)에 걸치는 선을 통째로 · 곡선은 0.5 m 꺾은선. 도면은 안 바꾼다(읽기만).</summary>
    static List<Raw> CollectLines(Database db, double f, double x0, double y0, double x1, double y1, System.Text.StringBuilder log)
    {
        double step = 0.5 / f;                                              // 곡선 꺾은선 간격 0.5 m(도면 단위로)
        bool Near(Extents3d e) => e.MaxPoint.X >= x0 && e.MinPoint.X <= x1 && e.MaxPoint.Y >= y0 && e.MinPoint.Y <= y1;
        List<Point3> Meters(IEnumerable<Point3d> pts) => pts.Select(p => new Point3(p.X * f, p.Y * f, p.Z * f)).ToList();
        var res = new List<Raw>();
        var seen = new HashSet<ObjectId>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            // ① 모형 공간 우리 레이어 선
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Curve c || c.IsErased) continue;
                    string? k = KindOf(c.Layer ?? ""); if (k == null) continue;
                    Extents3d ex; try { ex = c.GeometricExtents; } catch { continue; }
                    if (!Near(ex)) continue;
                    var pts = Sample(c, step); if (pts.Count < 2) continue;
                    bool closed = c.Closed || (pts.Count > 2 && pts[0] == pts[^1]);
                    res.Add(new Raw { Kind = k, Layer = c.Layer ?? "", Closed = closed, Pts = Meters(pts) }); seen.Add(id);
                }
                catch { }
            }
            // ② 계획 부지 경계 — 정지 기록 구역마다 계획 폴리선(어느 레이어든) · 2D이거나 Z가 모두 0이면 메시에 입힘(계획 3절 · 재야 할 것 3은 로그)
            try
            {
                var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
                if (regs != null)
                    foreach (var rg in regs)
                    {
                        try
                        {
                            string ph = rg.PlanHandle ?? ""; if (ph.Length == 0) continue;
                            if (!db.TryGetObjectId(new Handle(System.Convert.ToInt64(ph, 16)), out var pid) || pid.IsErased || seen.Contains(pid)) continue;
                            if (tr.GetObject(pid, OpenMode.ForRead) is not Curve pc) continue;
                            if (!Near(pc.GeometricExtents)) continue;
                            var pts = Sample(pc, step);
                            bool flat = pc is Polyline || pc is Polyline2d || pts.All(p => p.Z == 0);
                            res.Add(new Raw { Kind = "site", Layer = pc.Layer ?? "", Closed = true, Pts = Meters(pts), Has3D = !flat }); seen.Add(pid);
                            log.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  계획 경계: {0} · 점 {1} · Z {2:F3}~{3:F3} → {4}", pc.GetType().Name, pts.Count,
                                pts.Count > 0 ? pts.Min(p => p.Z) : 0, pts.Count > 0 ? pts.Max(p => p.Z) : 0, flat ? "메시에 입힘" : "Z 그대로"));
                        }
                        catch { }
                    }
            }
            catch (System.Exception ex) { log.AppendLine("  선 모으기 — 계획 경계 못 읽음: " + ex.Message); }
            // ③ 진입도로 포장 윤곽(설계에서 — 도면에 선으로 없다 · XY만)
            try
            {
                var gf = new NetTopologySuite.Geometries.GeometryFactory();
                foreach (var aid in RoadRanges.Roads(tr, db))
                {
                    try
                    {
                        var d = RoadDesign.Read(tr, aid); if (d == null) continue;
                        string nm = ((Autodesk.Civil.DatabaseServices.Alignment)tr.GetObject(aid, OpenMode.ForRead)).Name ?? "";
                        var rl = RoadLanes.Of(tr, aid, d, nm, null); if (rl == null) continue;
                        var (pave, _, _) = RoadLanes.Pavement(tr, rl, gf);
                        if (pave == null || pave.IsEmpty) continue;
                        var env = pave.EnvelopeInternal;
                        if (env.MaxX < x0 || env.MinX > x1 || env.MaxY < y0 || env.MinY > y1) continue;
                        void Ring(NetTopologySuite.Geometries.LineString ls)
                            => res.Add(new Raw { Kind = "road", Layer = "진입도로 포장(" + nm + ")", Closed = true, Has3D = false, Pts = ls.Coordinates.Select(q => new Point3(q.X * f, q.Y * f, 0)).ToList() });
                        for (int gi = 0; gi < pave.NumGeometries; gi++)
                            if (pave.GetGeometryN(gi) is NetTopologySuite.Geometries.Polygon pg)
                            {
                                Ring(pg.ExteriorRing);
                                for (int h = 0; h < pg.NumInteriorRings; h++) Ring(pg.GetInteriorRingN(h));
                            }
                    }
                    catch { }
                }
            }
            catch (System.Exception ex) { log.AppendLine("  선 모으기 — 진입도로 못 읽음: " + ex.Message); }
            tr.Commit();
        }
        return res;
    }

    /// <summary>네모 없음 — v116.13 그대로: 두 점 가운데 반지름(<see cref="StructPoints.SketchRadius"/>) 상자에 걸치는 선을 통째로(Revit이 잘라 그림) · XY만 · 5만 꼭짓점 한도.</summary>
    static StructPoints.Sketch BuildSketch(Ctx c, Point3d w1, Point3d w2, out string note)
    {
        double f = c.F;
        double cx = (w1.X + w2.X) / 2, cy = (w1.Y + w2.Y) / 2;
        double distM = System.Math.Sqrt((w2.X - w1.X) * (w2.X - w1.X) + (w2.Y - w1.Y) * (w2.Y - w1.Y)) * f;
        double rM = StructPoints.SketchRadius(distM), rU = rM / f;           // m · 도면 단위
        var raws = CollectLines(c.Db, f, cx - rU, cy - rU, cx + rU, cy + rU, c.Log);
        var lines = raws.Select(r => new StructPoints.SketchLine(r.Kind, r.Layer, r.Closed, XY(r.Pts))).Where(l => l.Pts.Count >= 2).ToList();
        var (kept, dropped) = StructPoints.Trim(lines);
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        note = string.Format(ic, "반지름 {0:F1} m · 선 {1}({2}) · 꼭짓점 {3}{4}", rM, kept.Count,
            string.Join(" · ", kept.GroupBy(l => l.Kind).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())), kept.Sum(l => l.Pts.Count),
            dropped > 0 ? $" · 한도({StructPoints.SketchMaxPts})로 뺀 선 {dropped}" : "");
        c.Log.AppendLine("  미리보기 선: " + note);
        return new StructPoints.Sketch(cx * f, cy * f, rM, kept);
    }

    /// <summary>★[v117.1] 네모 하나의 결과 — 지표면(한 번) → 자른 메시 → 선 모으기(네모 상자) → 3D 선(Z 그대로 자르기 · Z 없으면 «2D 자르기 → 1 m 채우기 → 입히기») → 다듬기 · 종류별 수 · 미리보기 선(네모로 자름).</summary>
    static Region BuildRegion(Ctx c, GroundCrop.Box box)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var R = new Region { Box = box };
        R.Terr = EnsureTerrain(c, R.Warn);
        c.Mod("지표면");
        long tTerr = sw.ElapsedMilliseconds;
        if (R.Terr != null)
        {
            R.Mesh = StructRegion3D.Clip(R.Terr.Tris, box, out R.Tris);
            R.Mesh!.Name = R.Terr.Name; R.Mesh.Layer = R.Terr.Layer;
        }
        long tClip = sw.ElapsedMilliseconds - tTerr;
        double f = c.F;
        var raws = CollectLines(c.Db, f, box.X0 / f, box.Y0 / f, box.X1 / f, box.Y1 / f, c.Log);
        StructRegion3D.MeshZ? mz = R.Mesh != null && R.Mesh.T.Count > 0 ? new StructRegion3D.MeshZ(R.Mesh) : null;
        var lines = new List<StructRegion3D.Line3>();
        int cutPieces = 0, draped = 0, noDrape = 0, roadOut = 0;
        foreach (var r in raws)
        {
            var pieces = StructRegion3D.ClipLine(r.Pts, r.Closed, box);
            if (pieces.Count > 1 || (pieces.Count == 1 && pieces[0].Closed != r.Closed)) cutPieces += pieces.Count;
            foreach (var (pts, cl) in pieces) { var xy = XY(pts); if (xy.Count >= 2) R.Sketch.Add(new StructPoints.SketchLine(r.Kind, r.Layer, cl, xy)); }
            if (r.Kind == "struct") continue;
            if (r.Kind == "road") { roadOut++; continue; }   // ★[v117.1 · 1111 범위 줄임] 진입도로 윤곽 입히기는 v117.1.1(Core Drape · S247 ⑧은 준비됨) — 이번 판은 3D 선에서 뺌(미리보기 sketch엔 그대로)
            if (r.Has3D) { foreach (var (pts, cl) in pieces) lines.Add(new StructRegion3D.Line3(r.Kind, r.Layer, cl, pts)); continue; }
            if (pieces.Count == 0) continue;
            if (mz == null) { noDrape++; continue; }
            if (r.Kind == "road" && R.Terr?.InTerrain != true) { roadOut++; continue; }   // ★[계획 9절 L5] 지형에 도로가 들었을 때만 입힘
            foreach (var (pts, cl) in pieces)
                foreach (var (dp, dcl) in StructRegion3D.Drape(pts, cl, mz)) { lines.Add(new StructRegion3D.Line3(r.Kind, r.Layer, dcl, dp)); draped++; }
        }
        R.Lines = StructRegion3D.Tidy(lines);
        foreach (var k in StructRegion3D.Keys) R.Counts[k] = R.Lines.Count(l => l.Kind == k);
        if (noDrape > 0) R.Warn.Add($"지표면이 없어 높이 없는 선 {noDrape}개 뺌");
        if (roadOut > 0) c.Log.AppendLine($"  3D 영역: 진입도로 윤곽 {roadOut}개는 3D 선에서 뺌(v117.1.1 · 미리보기 선엔 있음)");
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        R.Note = string.Format(ic, "3D 영역: 네모 ({0:F3}, {1:F3}) ~ ({2:F3}, {3:F3}) · {4:F1}×{5:F1} m · 지표면 {6} 세모 {7:N0} / 전체 {8:N0} · 자른 세모 {9:N0} · 뺀 납작 세모 {10:N0} · 선 {11}(잘린 조각 {12} · 입힌 선 {13}) · 지표면 {14} ms · 자르기 {15} ms · 모으기 {16} ms",
            box.X0, box.Y0, box.X1, box.Y1, box.W, box.H,
            R.Terr != null ? $"«{R.Terr.Name}»({R.Terr.Source})" : "없음", R.Tris, R.Terr?.Tris.Count ?? 0, R.Mesh?.Cut ?? 0, R.Mesh?.Flat ?? 0,
            string.Join(" · ", StructRegion3D.Keys.Skip(1).Select(k => k + " " + R.Counts[k])), cutPieces, draped, tTerr, tClip, sw.ElapsedMilliseconds - tTerr - tClip);
        c.Log.AppendLine("  " + R.Note + (R.Warn.Count > 0 ? " · ⚠" + string.Join(" · ", R.Warn) : ""));
        return R;
    }

    // ══════════════════════════════ 쓰기 ══════════════════════════════

    /// <summary>표고 · 좌표계 · 미리보기 → ① 3D 영역 파일(네모가 있을 때) ② 점 파일 → 되읽기 → 끝 알림. 실패 = 거짓 + 까닭(«원인: …» 한 줄).</summary>
    static bool Export(Ctx c, string name, string path, Point3d w1, Point3d w2, Region? reg, HashSet<string> include, out string fail)
    {
        fail = "";
        var db = c.Db; var ed = c.Ed; var log = c.Log; double f = c.F;
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        bool ucsWorld = ed.CurrentUserCoordinateSystem.IsEqualTo(Matrix3d.Identity);
        // ③ 표고(원지반 · 최종 지표면) · 좌표계
        StructPoints.Pt p1, p2;
        string gName = "", fName = "";
        {
            var gid = ImportGisCommand.FindGroundSurface(db, out gName, out int gTris);
            if (gTris <= 0) gid = ObjectId.Null;
            using var tr = db.TransactionManager.StartTransaction();
            ObjectId fid = ObjectId.Null;
            try
            {
                foreach (ObjectId sid in Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db).GetSurfaceIds())
                    if (!sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface s && s.Name == FinalSurfaceBuilder.Name) { fid = sid; fName = s.Name; break; }
            }
            catch { }
            StructPoints.Pt Mk(Point3d w)
            {
                double? g = ElevAt(tr, gid, w.X, w.Y), fz = ElevAt(tr, fid, w.X, w.Y);
                return new StructPoints.Pt(w.X * f, w.Y * f, w.Z * f, gName ?? "", g * f, fName, fz * f);
            }
            p1 = Mk(w1); p2 = Mk(w2);
            tr.Commit();
        }
        string code = KoreaCs.Read(db);
        int? det = KoreaCs.ResolveEpsgFromCode(code);
        // ★[설치 전 검토 낮음] SyncToDocument가 이미 도면 값으로 맞춘 설정 값을 쓴다 — 도면 코드로 못 나타내는 원점(구 좌표계 · UTM-K)을 고른 판은 그 고름을 지킨다
        //   (ResolveEpsgFromCode는 신/구를 못 가려 늘 신을 준다). 출처는 실제로 도면 값을 따랐을 때만 «drawing».
        int epsg = GradingSettings.ExportEpsg;
        bool fromDrawing = det.HasValue && det.Value == epsg && KoreaCs.CodeForEpsg(epsg) != null;
        var info = new StructPoints.Info(name, epsg, code, fromDrawing ? "drawing" : "settings", Path.GetFileName(c.DocName), c.Named ? c.DocName : "",
            c.Ins, ucsWorld, "Civil 3D · AutoCAD " + AcadApp.Version, "DH.Grading " + GradingSettings.Version, System.DateTimeOffset.Now);

        // ③-b 미리보기 선 — 네모 없음 = v116.13 그대로(두 점 반지름) · 네모 있음 = 같은 모은 선을 네모로 자른 것(체크와 상관없이 전부 · struct 포함)
        StructPoints.Sketch? sketch = null; string sketchNote = "";
        StructRegion3D.Mesh? mesh = null;
        List<StructRegion3D.Line3> lines = new();
        try
        {
            if (reg == null) sketch = BuildSketch(c, w1, w2, out sketchNote);
            else
            {
                var (kept, dropped) = StructPoints.Trim(reg.Sketch);
                sketch = StructRegion3D.SketchOf(reg.Box, kept);
                sketchNote = string.Format(ic, "네모 {0:F1}×{1:F1} m · 선 {2}({3}) · 꼭짓점 {4}{5}", reg.Box.W, reg.Box.H, kept.Count,
                    string.Join(" · ", kept.GroupBy(l => l.Kind).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())), kept.Sum(l => l.Pts.Count), dropped > 0 ? $" · 한도로 뺀 선 {dropped}" : "");
                log.AppendLine("  미리보기 선: " + sketchNote);
            }
        }
        catch (System.Exception sx) { sketchNote = "미리보기 선 못 모음 — " + sx.Message; log.AppendLine("  ⚠" + sketchNote); }
        if (reg != null)
        {
            bool surfOn = include.Contains("final") && reg.Mesh != null;
            lines = reg.Lines.Where(l => include.Contains(l.Kind)).ToList();
            int chosen = (surfOn ? reg.Tris : 0) + lines.Count;
            string why = StructRegion3D.WhyNot(name.Length > 0, true, path.Length > 0, true, surfOn, reg.Tris, chosen, Cap);
            if (why.Length > 0)
            {
                fail = why + (why.Contains("줄이세요") ? " — 파일 안 씀" : "");
                log.AppendLine("  3D 영역: " + fail);
                return false;
            }
            mesh = surfOn ? reg.Mesh : null;
        }

        // ④ 쓰기 — ① 3D 영역 파일 ② 점 파일 → 되읽기
        string path3 = "";
        long size3 = 0;
        int tris3 = mesh?.T.Count ?? 0;
        var swW = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            path = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(path) ?? "";
            path3 = Path.Combine(dir, StructRegion3D.FileFor(Path.GetFileName(path)));
            if (reg != null)
            {
                using (var fs = new FileStream(path3, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                    StructRegion3D.Write(fs, info, Path.GetFileName(path), reg.Box, mesh, lines);
                size3 = new FileInfo(path3).Length;
            }
            else if (File.Exists(path3)) log.AppendLine("  옆에 옛 3D 영역 파일이 있음(지우지 않음 · 점 파일이 안 가리킴): " + path3);
            var rref = reg != null ? new StructPoints.Region3DRef(Path.GetFileName(path3), reg.Box, tris3, lines.Count) : null;
            string json = StructPoints.Json(info, p1, p2, sketch, rref);
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
            // 되읽기 — 쓴 x · y가 그대로 읽히나(비트 그대로) · region3d.tris = 3D 파일 세모 수 · 머리 같음(값)
            using var jd = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
            var root = jd.RootElement;
            var pa = root.GetProperty("points");
            bool same = pa.GetArrayLength() == 2 && pa[0].GetProperty("x").GetDouble() == p1.X && pa[0].GetProperty("y").GetDouble() == p1.Y && pa[1].GetProperty("x").GetDouble() == p2.X && pa[1].GetProperty("y").GetDouble() == p2.Y;
            if (!same) { fail = "원인: 저장한 파일을 다시 읽으니 좌표가 다름 — " + path; return false; }
            if (reg != null)
            {
                using var j3 = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path3));
                var r3 = j3.RootElement;
                var ms = r3.GetProperty("meshes");
                int nt = ms.GetArrayLength() > 0 ? ms[0].GetProperty("tris").GetArrayLength() : 0, nv = ms.GetArrayLength() > 0 ? ms[0].GetProperty("verts").GetArrayLength() : 0;
                bool head = StructRegion3D.HeadKeys.All(k => StructRegion3D.SameValue(r3.GetProperty(k), root.GetProperty(k)));
                bool boxSame = StructRegion3D.SameValue(r3.GetProperty("box"), root.GetProperty("region3d").GetProperty("box"));
                int rt = root.GetProperty("region3d").GetProperty("tris").GetInt32();
                bool ok3 = r3.GetProperty("format").GetString() == StructRegion3D.Format && nt == tris3 && nv == (mesh?.V.Count ?? 0) && rt == nt && head && boxSame
                           && r3.GetProperty("lines").GetArrayLength() == lines.Count;
                log.AppendLine($"  자가검증: 되읽기 세모 {nt:N0} = {tris3:N0} · 점 {nv:N0} = {mesh?.V.Count ?? 0:N0} · region3d.tris {rt:N0} · 선 {r3.GetProperty("lines").GetArrayLength()} = {lines.Count} · 머리 {(head ? "같음" : "다름")} · box {(boxSame ? "같음" : "다름")} → {(ok3 ? "OK" : "어긋남")}");
                if (!ok3) { fail = "원인: 3D 영역 파일을 다시 읽으니 쓴 값과 다름(세모 · 점 · 머리 · box) — " + path3; return false; }
            }
        }
        catch (System.Exception ex) { fail = "원인: 저장 실패 — " + ex.Message; return false; }
        long tWrite = swW.ElapsedMilliseconds;
        if (Memos.TryGetValue(c.DocName, out var mm)) mm.Name = name;
        try { LastPtsDir = Path.GetDirectoryName(path) ?? ""; } catch { }
        var (dist, brg) = StructPoints.Check(p1, p2);
        string El(StructPoints.Pt p) => (p.Ground is double g ? string.Format(ic, " 원지반 {0:F3}", g) : "") + (p.Final is double q ? string.Format(ic, " 최종 {0:F3}", q) : "");
        var notes = new List<string>();
        if (c.Ins != 6) notes.Add(c.Assumed ? "도면 단위 없음(INSUNITS 0) → m로 봄" : $"도면 단위 {StructPoints.UnitName(c.Ins)} → m로 바꿔 적음");
        if (!ucsWorld) notes.Add("UCS가 WCS가 아니어서 WCS 값으로 바꿔 적음");
        if (!det.HasValue) notes.Add($"도면 좌표계 {(code.Length > 0 ? "«" + code + "» 자동인식 밖" : "없음")} — 설정값 EPSG {epsg}");
        else if (!fromDrawing) notes.Add($"좌표계 = 설정에서 고른 EPSG {epsg}(도면 코드 «{code}»로는 원점을 못 가림)");
        if (reg != null) notes.AddRange(reg.Warn.Select(w => "⚠" + w));
        log.AppendLine(sketch == null ? StructPoints.Json(info, p1, p2) : StructPoints.Json(info, p1, p2, null,
            reg != null ? new StructPoints.Region3DRef(Path.GetFileName(path3), reg.Box, tris3, lines.Count) : null));   // 진단 로그엔 미리보기 선 빼고(수만 점) — 선 수는 아래 한 줄
        if (sketchNote.Length > 0) log.AppendLine("  미리보기 선: " + sketchNote);
        if (reg != null)
            log.AppendLine(string.Format(ic, "  3D 영역 파일: {0} · {1:N0} 바이트 · 세모 {2:N0} · 선 {3}({4}) · 쓰기 · 되읽기 {5} ms", path3, size3, tris3, lines.Count,
                string.Join(" · ", lines.GroupBy(l => l.Kind).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count())), tWrite));
        c.Mod("끝");
        log.AppendLine("  도면 바뀜(DBMOD) 단계별: " + string.Join(" · ", c.ModTrail));
        Done(ed, log, string.Format(ic, "기준점 저장 — {0} · 점1 ({1:F3}, {2:F3}){3} · 점2 ({4:F3}, {5:F3}){6} · 거리 {7:F3} m · 방향 {8:F2}° · 단위 m · EPSG {9}",
            path, p1.X, p1.Y, El(p1), p2.X, p2.Y, El(p2), dist, brg, epsg)
            + (sketch != null ? $" · 미리보기 선 {sketch.Lines.Count}개" : " · ⚠미리보기 선 없음")
            + (reg != null ? string.Format(ic, " · 3D 영역 세모 {0:N0} · 선 {1}", tris3, lines.Count) : "")
            + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : ""));
        return true;
    }

    /// <summary>미리보기(평면) 점 — XY만 · 이어진 같은 XY는 하나로(3D 선의 수직 토막이 길이 0 토막이 되지 않게 — v116.13 SampleXY와 같은 결과).</summary>
    static List<(double X, double Y)> XY(IEnumerable<Point3> pts)
    {
        var r = new List<(double X, double Y)>();
        foreach (var p in pts) if (r.Count == 0 || System.Math.Abs(r[^1].X - p.X) > 1e-9 || System.Math.Abs(r[^1].Y - p.Y) > 1e-9) r.Add((p.X, p.Y));
        return r;
    }

    /// <summary>곡선 → 점열(도면 단위 · WCS · Z 포함 — LW 폴리선은 높이(Elevation) 든 점) — 꼭짓점 그대로 · 굽은 토막(호 · 스플라인 · 원)은 <paramref name="step"/> 간격.</summary>
    static List<Point3d> Sample(Curve c, double step)
    {
        var r = new List<Point3d>();
        void Push(Point3d q) { if (r.Count == 0 || System.Math.Abs(r[^1].X - q.X) > 1e-9 || System.Math.Abs(r[^1].Y - q.Y) > 1e-9 || System.Math.Abs(r[^1].Z - q.Z) > 1e-9) r.Add(q); }
        if (c is Line ln) { Push(ln.StartPoint); Push(ln.EndPoint); return r; }   // 선의 매개변수는 길이라 꼭짓점 셈에 안 맞음
        double p0 = c.StartParam, p1 = c.EndParam;
        bool vertexed = c is Polyline || c is Polyline2d || c is Polyline3d;
        if (vertexed)
        {
            int nSeg = System.Math.Max(1, (int)System.Math.Round(p1 - p0));
            for (int i = 0; i < nSeg; i++)
            {
                var a = c.GetPointAtParameter(p0 + i); var b = c.GetPointAtParameter(System.Math.Min(p1, p0 + i + 1)); var m = c.GetPointAtParameter(System.Math.Min(p1, p0 + i + 0.5));
                Push(a);
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
                if ((m.X - mx) * (m.X - mx) + (m.Y - my) * (m.Y - my) > step * step * 1e-4)   // 굽은 토막(호 bulge)
                {
                    double d0 = c.GetDistanceAtParameter(p0 + i), d1 = c.GetDistanceAtParameter(System.Math.Min(p1, p0 + i + 1));
                    int k = System.Math.Max(2, (int)System.Math.Ceiling((d1 - d0) / step));
                    for (int q = 1; q < k; q++) Push(c.GetPointAtDist(d0 + (d1 - d0) * q / k));
                }
            }
            Push(c.GetPointAtParameter(p1));
        }
        else
        {
            double L = c.GetDistanceAtParameter(p1);
            int k = System.Math.Max(2, (int)System.Math.Ceiling(L / step));
            for (int q = 0; q <= k; q++) Push(c.GetPointAtDist(L * q / k));
        }
        return r;
    }
}
