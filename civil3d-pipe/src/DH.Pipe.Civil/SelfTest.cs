using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Quantity;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Pipe.Civil;

/// <summary>
/// 자가 시험 명령 DHPIPESELFTEST — 지금 도면에 가짜 지형과 노선을 만들어 ① ~ ⑥ 을 실제 기능으로 끝까지 돌리고 결과를 적는다.
/// 화면 없는 Civil(accoreconsole)에서 설치 전 확인에 쓴다. 결과 = 환경변수 DH_PIPE_TEST_OUT 폴더의 selftest.txt.
/// ※ 빈 도면에서만 쓸 것(객체를 만든다).
/// </summary>
public sealed class SelfTest
{
    private readonly StringBuilder _out = new();
    private int _pass, _fail;

    private void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        string line = $"{(ok ? "PASS" : "FAIL")}  {name}{(detail == "" ? "" : "  — " + detail)}";
        _out.AppendLine(line);
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + line); } catch { }
    }

    private void Note(string text)
    {
        _out.AppendLine("      " + text);
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n      " + text); } catch { }
    }

    private void Step(string name, Action body)
    {
        try { body(); }
        catch (System.Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); PipeLog.Line($"[자가 시험] {name} — {ex}"); }
    }

    [CommandMethod("DH_PIPE", "DHPIPESELFTEST", CommandFlags.Modal)]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var db = doc.Database;
        // 시험 지형 · 노선 · 도면을 만든다 → 화면 없는 Civil(시험 실행기)에서만. 작업 도면에서 실수로 치지 않게(검토 보통)
        if (!PipeLog.Headless && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DH_PIPE_TEST_OUT")))
        {
            try { doc.Editor.WriteMessage("\n[DH 관로] 자가 시험은 시험 실행기(tools/headless/run_selftest.py) 전용\n"); } catch { }
            return;
        }
        string dir = Environment.GetEnvironmentVariable("DH_PIPE_TEST_OUT") ?? "";
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
        _out.AppendLine($"# {PipeSettings.ProductName} {PipeSettings.Version} 자가 시험 · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {(PipeLog.Headless ? "화면 없음" : "화면")}");

        ObjectId surfId = ObjectId.Null, plId = ObjectId.Null;
        RouteData? route = null;

        Step("① 지형 만들기", () =>
        {
            surfId = CivilDb.TinSurface.Create(db, "DHP시험지형");
            using var tr = db.TransactionManager.StartTransaction();
            var tin = (CivilDb.TinSurface)tr.GetObject(surfId, OpenMode.ForWrite);
            var pts = new Point3dCollection();
            for (double x = DemoTerrain.X0; x <= DemoTerrain.X1 + 1e-9; x += 5)
                for (double y = DemoTerrain.Y0; y <= DemoTerrain.Y1 + 1e-9; y += 5)
                    pts.Add(new Point3d(x, y, DemoTerrain.Z(x, y)));
            tin.AddVertices(pts);
            tr.Commit();
            var idx = RouteBuilder.ReadTin(db, surfId);
            Check("① 지형 만들기", idx.Count > 1000, $"점 {pts.Count} · 세모 {idx.Count}");
        });

        Step("② 노선 만들기", () =>
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline();
                for (int i = 0; i < DemoTerrain.Plan.Length; i++) pl.AddVertexAt(i, new Point2d(DemoTerrain.Plan[i].X, DemoTerrain.Plan[i].Y), 0, 0, 0);
                plId = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                // 지표 구역(첫 구간 아스콘 도로 · 보도) · 연석
                void Zone(string layer, short aci, bool closed, params (double X, double Y)[] p)
                {
                    var z = new Polyline();
                    for (int i = 0; i < p.Length; i++) z.AddVertexAt(i, new Point2d(p[i].X, p[i].Y), 0, 0, 0);
                    z.Closed = closed;
                    z.LayerId = CivilUtil.EnsureLayer(db, tr, layer, aci);
                    ms.AppendEntity(z);
                    tr.AddNewlyCreatedDBObject(z, true);
                }
                Zone(PipeLayers.ZoneAsp, 8, true, (-10, -5), (205, -5), (205, 4), (-10, 4));
                Zone(PipeLayers.ZoneBlk, 30, true, (-10, 4), (205, 4), (205, 7), (-10, 7));
                Zone(PipeLayers.Curb, 3, false, (-10, 4), (205, 4));
                tr.Commit();
            }
            var log = new StringBuilder();
            route = Ops.CreateRoute(doc, plId, surfId, "시험관로", log);
            bool origKept;
            using (var tr = db.TransactionManager.StartTransaction()) { origKept = !plId.IsErased && tr.GetObject(plId, OpenMode.ForRead) is Polyline; tr.Commit(); }
            var b = RouteBuilder.Build(db, route, needProfile: false);
            Check("② 노선 만들기", Math.Abs(b.Ctx.Plan.Length - 629.24) < 0.05 && b.Ctx.Plan.Deflections().Count == 3,
                $"{log.ToString().Trim()} · 길이 {b.Ctx.Plan.Length:0.00} · 꺾인 점 {b.Ctx.Plan.Deflections().Count}");
            Check("② 원본 폴리선은 그대로", origKept);
            Check("② 지표 구역 · 연석 읽기", b.Ctx.Zones.Count == 2 && b.Ctx.Curbs.Count == 1, $"구역 {b.Ctx.Zones.Count} · 연석 {b.Ctx.Curbs.Count}");
            Check("② 지반 종단", Math.Abs(b.Ground.X1 - b.Ctx.Plan.Length) < 0.05 && b.Ground.Count > 100, $"지반 점 {b.Ground.Count} · 알림 {b.Notes.Count}");
        });
        if (route == null) { Finish(dir); return; }

        Step("관 · 공법 · 땅속 · 지장물 넣기", () =>
        {
            double len = RouteBuilder.Build(db, route, needProfile: false).Ctx.Plan.Length;
            Ops.SetRun(route, 284.85, len, PipeCatalog.Make("SP", 600), len);
            Ops.SetMethod(route, 120, 200, new MethodSpan { Kind = MethodKind.ShoreSk });
            Ops.SetMethod(route, 320, 370, new MethodSpan { Kind = MethodKind.RiverOpen, Encase = true });
            Ops.SetMethod(route, 470, 520, new MethodSpan { Kind = MethodKind.Jacking });
            Ops.SetMethod(route, 560, len, new MethodSpan { Kind = MethodKind.HPile });
            route.Strata.Add(new StrataPoint { Sta = 0, SoilThk = 2.2, WrThk = 1.5, WaterDepth = 2.6 });
            route.Strata.Add(new StrataPoint { Sta = 300, SoilThk = 1.6, WrThk = 2.0, WaterDepth = 1.2 });
            route.Strata.Add(new StrataPoint { Sta = len, SoilThk = 3.0, WrThk = 2.0, WaterDepth = 3.0 });
            route.Obstacles.Add(new Obstacle { Sta = 60, Kind = "하수", Width = 0.6, Height = 0.6, Depth = 1.4 });
            route.Obstacles.Add(new Obstacle { Sta = 150, Kind = "가스", Width = 0.2, Height = 0.2, Depth = 0.9 });
            route.Std.Use = PipeUse.Distribution;
            PipeStore.Save(db, route);
            var back = PipeStore.Current(db);
            Check("도면 안 저장 · 다시 읽기", back != null && back.Methods.Count == 4 && back.Runs.Count == 2 && back.Obstacles.Count == 2 && back.AlignHandle == route.AlignHandle,
                back == null ? "못 읽음" : $"구간 {back.Methods.Count} · 관 {back.Runs.Count} · 지장물 {back.Obstacles.Count}");
        });

        Step("③ 처음 종단", () =>
        {
            var log = new StringBuilder();
            string msg = Ops.MakeProfile(doc, route, false, new Point3d(0, -400, 0), log);
            var b = RouteBuilder.Build(db, route);
            int n = b.Ctx.Profile.Pvi.Count;
            Check("③ 처음 종단(Civil 종단에 PVI)", n >= 4 && n <= 40 && !CivilUtil.FromHandle(db, route.PipeProfileHandle).IsNull, $"{msg} · 지반 점 {b.Ground.Count} → PVI {n}");
            Check("③ 지반 종단(Civil)", !CivilUtil.FromHandle(db, route.GroundProfileHandle).IsNull, log.ToString().Trim());
            bool view = !CivilUtil.FromHandle(db, route.ProfileViewHandle).IsNull;
            if (view) Check("③ 종단뷰", true);
            else Note("종단뷰 못 만듦(이 도면에 종단뷰 스타일이 없을 수 있음) — " + log.ToString().Trim());
            // 지장물 회피로 다시(있는 종단의 PVI 바꾸기 길)
            string msg2 = Ops.MakeProfile(doc, route, true, null, log);
            var b2 = RouteBuilder.Build(db, route);
            var chk = DH.Pipe.Core.Profile.ProfileCheck.Obstacles(b2.Ctx.Profile, b2.Ground, route);
            Check("③ 지장물 회피(PVI 바꾸기)", chk.Count == 2 && chk.All(c => c.Gap >= route.Std.ClearanceToOthers - 0.011),
                msg2 + " · 이격 " + string.Join(" / ", chk.Select(c => c.Gap.ToString("0.00"))));
        });

        Step("③ 가이드안", () =>
        {
            var log = new StringBuilder();
            string msg = Ops.MakeGuide(doc, route, log);
            Check("③ 가이드안", route.Appts.Any(a => a.Kind == ApptKind.AirValve) && route.Appts.Any(a => a.Kind == ApptKind.Drain)
                && route.Appts.Count(a => a.Kind == ApptKind.GateValve) >= 6 && route.Appts.Any(a => a.Kind == ApptKind.Hydrant), msg);
            int marks = Ops.DrawMarks(doc, route);
            Check("③ 평면 표시", marks > 0, $"객체 {marks}");
        });

        Ops.Calc? calc = null;
        Step("④ 단면 계산", () =>
        {
            calc = Ops.Compute(db, route);
            var secs = calc.Sections();
            int bad = secs.Count(s => !s.Ok);
            Check("④ 단면 계산", bad == 0 && secs.Count >= 40, $"측점 {calc.Rows.Count} · 단면 {secs.Count} · 못 만든 것 {bad}");
            var kinds = secs.Select(s => s.Method).Distinct().ToList();
            Check("④ 공법 다섯 가지가 단면에 나옴", kinds.Count == 5, string.Join(" · ", kinds.Select(MethodSpan.NameOf)));
            Check("④ 경계 측점은 전 · 후 두 장", calc.Rows.Count(r => r.IsBreak) >= 7, $"경계 {calc.Rows.Count(r => r.IsBreak)}곳");
            Check("④ 포장 · 지층 · 용수가 수량에 나옴", calc.Earth.T(DH.Pipe.Core.Section.Q.PavAsp) > 0 && calc.Earth.AreaKeys.Any(k => k.Contains("WET")) && calc.Earth.AreaKeys.Any(k => k.EndsWith("WR")),
                string.Join(" ", calc.Earth.AreaKeys));
            Check("④ 토공 합계", calc.Earth.ExcTotal > 3000 && calc.Earth.ExcTotal < 8000 && calc.Earth.MissingIntervals == 0,
                $"터파기 {calc.Earth.ExcTotal:0.00} · 되메우기 {calc.Earth.BackfillTotal:0.00} · 잔토 {calc.Earth.Surplus:0.00}");
            foreach (var q in calc.Sum.Spans) Note($"구간 {q.StaFrom:0.0} ~ {q.StaTo:0.0} {MethodSpan.NameOf(q.Span.Kind)} 평균 깊이 {q.AvgDepth:0.00} {q.SpecText}");
            foreach (var i in calc.Issues) Note($"검사 [{i.Kind}] {i.StaFrom:0.0} ~ {i.StaTo:0.0} {i.Text}");
        });

        Step("⑤ 횡단도", () =>
        {
            string msg = Ops.DrawXsec(doc, route, new Point3d(0, -1500, 0));
            string g = CivilUtil.GroupName("XSEC", route.AlignHandle);
            int n1 = CivilUtil.GroupCount(db, g);
            int all1 = Tagged(db, g);
            string msg2 = Ops.DrawXsec(doc, route, new Point3d(0, -1500, 0));
            int n2 = CivilUtil.GroupCount(db, g);
            Check("⑤ 횡단도 그리기", n1 > 1000, $"{msg} · 객체 {n1}");
            Check("⑤ 다시 그리면 옛 그림을 지움", n1 == n2 && Tagged(db, g) == all1, $"{n1} → {n2} · 표식 달린 것 {all1} → {Tagged(db, g)}");
            Check("⑤ 해치", CivilUtil.GroupCount(db, g, "HATCH") > 50, $"해치 {CivilUtil.GroupCount(db, g, "HATCH")}");
            // 사용자가 묶음을 풀어도(묶음만 지움) 다시 그릴 때 옛 그림이 남지 않아야 한다
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                if (gd.Contains(g)) tr.GetObject(gd.GetAt(g), OpenMode.ForWrite).Erase();
                tr.Commit();
            }
            Ops.DrawXsec(doc, route, new Point3d(0, -1500, 0));
            Check("⑤ 묶음을 풀어도 옛 그림을 지움", Tagged(db, g) == all1, $"표식 달린 것 {Tagged(db, g)} (한 벌 = {all1})");
        });

        Step("③ 종단도면", () =>
        {
            string msg = Ops.DrawProfileSheet(doc, route, new Point3d(0, -900, 0));
            int n = CivilUtil.GroupCount(db, CivilUtil.GroupName("PROF", route.AlignHandle));
            Check("③ 종단도면 그리기", n > 300, $"{msg} · 객체 {n}");
        });

        Step("⑥ 엑셀", () =>
        {
            string path = Path.Combine(dir, "selftest_수량산출서.xlsx");
            try { File.Delete(path); } catch { }
            string msg = Ops.ExportXlsx(doc, route, path);
            var fi = new FileInfo(path);
            Check("⑥ 토적표 엑셀", fi.Exists && fi.Length > 8000, $"{msg} · {(fi.Exists ? fi.Length : 0)} 바이트");
        });

        Step("⑥ 3D", () =>
        {
            string msg = Ops.Make3D(doc, route);
            int n = CivilUtil.GroupCount(db, CivilUtil.GroupName("3D", route.AlignHandle));
            Check("⑥ 3D 모형", n >= 5, $"{msg} · 객체 {n}");
        });

        Finish(dir);
    }

    /// <summary>모형 공간에서 그 표식이 달린 객체 수(묶음에 들었든 아니든).</summary>
    private static int Tagged(Database db, string name)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        int n = 0;
        foreach (ObjectId id in ms)
        {
            using var xd = tr.GetObject(id, OpenMode.ForRead).GetXDataForApplication(CivilUtil.TagApp);
            if (xd == null) continue;
            foreach (TypedValue tv in xd)
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString && tv.Value is string v && v == name) { n++; break; }
        }
        tr.Commit();
        return n;
    }

    private void Finish(string dir)
    {
        string sum = $"# 자가 시험 pass {_pass} · fail {_fail}";
        _out.AppendLine(sum);
        try { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + sum + "\n"); } catch { }
        try { File.WriteAllText(Path.Combine(dir, "selftest.txt"), _out.ToString(), new UTF8Encoding(false)); } catch { }
        PipeLog.Line("[자가 시험] " + sum);
    }
}
