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
            // 사용자가 그림 일부를 복사해 둔 것(표식은 따라가지만 묶음에는 안 듦)은 다시 그려도 남아야 한다(2차 검토 권장)
            ObjectId copyId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                var grp = (Group)tr.GetObject(gd.GetAt(g), OpenMode.ForRead);
                var src = (Entity)tr.GetObject(grp.GetAllEntityIds().First(id => id.ObjectClass.DxfName == "LINE"), OpenMode.ForRead);
                var cp = (Entity)src.Clone();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                copyId = ms.AppendEntity(cp);
                tr.AddNewlyCreatedDBObject(cp, true);
                using (var xd = src.GetXDataForApplication(CivilUtil.TagApp)) if (xd != null) cp.XData = xd;     // COPY 명령처럼 표식이 따라가게
                tr.Commit();
            }
            int withCopy = Tagged(db, g);
            Ops.DrawXsec(doc, route, new Point3d(0, -1500, 0));
            Check("⑤ 복사해 둔 그림은 남김", withCopy == all1 + 1 && !copyId.IsErased && Tagged(db, g) == all1 + 1,
                $"복사 뒤 표식 {withCopy} · 다시 그린 뒤 {Tagged(db, g)} (한 벌 {all1} + 복사 1) · 복사본 {(copyId.IsErased ? "지워짐" : "남음")}");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!copyId.IsErased) tr.GetObject(copyId, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
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

        // ---------------------------------------------------------------- 지층면(부지정지의 지층 지표면을 그대로 읽는 길)
        Step("⑦ 지층면", () =>
        {
            // 부지정지 [지층]이 만드는 것과 같은 꼴: «DH_지층_n_이름» = n번 층의 윗면 · 설명란에 암 분류 · «DH_지하수위»
            void Surf(string name, double below, string? desc)
            {
                var id = CivilDb.TinSurface.Create(db, name);
                using var tr = db.TransactionManager.StartTransaction();
                var tin = (CivilDb.TinSurface)tr.GetObject(id, OpenMode.ForWrite);
                var pts = new Point3dCollection();
                for (double x = DemoTerrain.X0; x <= DemoTerrain.X1 + 1e-9; x += 10)
                    for (double y = DemoTerrain.Y0; y <= DemoTerrain.Y1 + 1e-9; y += 10)
                        pts.Add(new Point3d(x, y, DemoTerrain.Z(x, y) - below));
                tin.AddVertices(pts);
                if (desc != null) tin.Description = desc;
                tr.Commit();
            }
            Surf("DH_지층_1_매립토", 0.0, "DH_ROCK=Soil;DH_SHOW=0;DH_SVER=2");
            Surf("DH_지층_2_풍화암", 2.0, "DH_ROCK=Weathered;DH_SHOW=1;DH_SVER=2");
            Surf("DH_지층_3_연암", 3.5, "DH_SHOW=1;DH_SVER=2");                  // 분류 없음 → 이름으로 어림
            Surf("DH_지층_4_경암", 4.5, "DH_ROCK=Hard;DH_SHOW=1;DH_SVER=2");
            Surf("DH_지하수위", 2.5, null);

            Check("⑦ 지층면은 원지반 후보가 아님", RouteBuilder.Surfaces(db).Count == 1, $"원지반 후보 {RouteBuilder.Surfaces(db).Count}");
            var c = Ops.Compute(db, route);
            var st = c.B.Ctx.Strata;
            Check("⑦ 지층면 읽기", st.Count == 4 && st.Select(x => x.Rock).SequenceEqual(new[] { 0, 1, 2, 4 }) && c.B.Ctx.WaterTin != null,
                string.Join(" · ", st.Select(x => $"{x.Order} {x.Name}={DH.Pipe.Core.Section.Q.RockName[x.Rock]}")) + (c.B.Ctx.WaterTin != null ? " · 지하수위 면" : ""));
            // 자연 터파기 한 곳: 토사 2.0 m · 그 아래는 풍화암(깊이 2.2 m 안팎) · 하천 횡단(깊이 4.8 m)에는 경암까지
            var open = c.Rows.First(r => r.After.Ok && r.After.Method == MethodKind.Open && r.Sta > 20 && r.Sta < 100).After;
            bool layerLine = open.Lines.Any(l => l.Key == "LAYER" && l.Label == "풍화암");
            bool hard = c.Earth.AreaKeys.Any(k => k.EndsWith(".HR", StringComparison.Ordinal));
            Check("⑦ 단면이 지층면을 따름", layerLine && hard && !open.Lines.Any(l => l.Key is "STRATA1" or "STRATA2"),
                $"No.{open.Label} 층 선 {string.Join("/", open.Lines.Where(l => l.Key == "LAYER").Select(l => l.Label))} · 터파기 {open.ExcTotal:0.00} · 암 분류 {string.Join(" ", c.Earth.AreaKeys.Where(k => k.StartsWith("EXC")).Select(k => k.Split('.')[3]).Distinct())}");
            // 옛 지층면(«DH_SVER» 없음 = 층의 아랫면 기준): 한 칸 밀어 읽고 알린다 → 풍화암의 윗면 = 1번 면(지반) → 토사 0
            void Describe(Func<string, string> edit)
            {
                var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                using var tr = db.TransactionManager.StartTransaction();
                foreach (ObjectId sid in cdoc.GetSurfaceIds())
                    if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface t && t.Name.StartsWith(RouteBuilder.StrataPrefix, StringComparison.Ordinal))
                    { t.UpgradeOpen(); t.Description = edit(t.Description ?? ""); }
                tr.Commit();
            }
            Describe(d => d.Replace("DH_SVER=2", "DH_SVER=1"));
            var cOld = Ops.Compute(db, route);
            var openOld = cOld.Rows.First(r => Math.Abs(r.Sta - open.Station) < 1e-6).After;
            double soilOld = openOld.A(DH.Pipe.Core.Section.Q.Exc(false, false, 0)) + openOld.A(DH.Pipe.Core.Section.Q.Exc(true, false, 0));
            double soilNew = open.A(DH.Pipe.Core.Section.Q.Exc(false, false, 0)) + open.A(DH.Pipe.Core.Section.Q.Exc(true, false, 0));
            Describe(d => d.Replace("DH_SVER=1", "DH_SVER=2"));
            Check("⑦ 옛 지층면(아래 기준)은 한 칸 밀어 읽음", cOld.Notes.Any(n => n.StartsWith("옛 지층면")) && soilOld < 0.01 && soilNew > 1.0 && !Ops.Compute(db, route).Notes.Any(n => n.StartsWith("옛 지층면")),
                $"No.{open.Label} 토사 — 새 방식 {soilNew:0.00} · 옛 방식으로 읽으면 {soilOld:0.00} · 알림 {(cOld.Notes.Any(n => n.StartsWith("옛 지층면")) ? "있음" : "없음")}");
            // 도면도 다시: 횡단도에 층 선 · 종단도면에 지층선 · 수위선
            Ops.DrawXsec(doc, route, new Point3d(0, -1500, 0));
            string pm = Ops.DrawProfileSheet(doc, route, new Point3d(0, -900, 0));
            int nStrata = CountOnLayer(db, CivilUtil.GroupName("PROF", route.AlignHandle), DH.Pipe.Core.Draw.Lay.Name("종단", DH.Pipe.Core.Draw.Lay.Strata));
            int nWater = CountOnLayer(db, CivilUtil.GroupName("PROF", route.AlignHandle), DH.Pipe.Core.Draw.Lay.Name("종단", DH.Pipe.Core.Draw.Lay.Water));
            Check("⑦ 종단도면에 지층선 · 수위선", nStrata >= 3 && nWater >= 1 && pm.Contains("지층면 4"), $"{pm} · 지층선 객체 {nStrata} · 수위선 객체 {nWater}");
        });

        // ---------------------------------------------------------------- 도면화(배치탭)
        Step("⑧ 도면화", () =>
        {
            new SheetInput { Project = "시험 공사", NoPrefix = "C-", NoStart = 101 }.Save(db);
            var r = PipeSheets.Make(doc, route, SheetInput.Load(db));
            Check("⑧ 도면화 작성", r.Ok && r.Tabs == 3, r.Line);
            if (!r.Ok) return;
            int xs = PipeSheets.ModelSheets(db, CivilUtil.GroupName("XSEC", route.AlignHandle), DH.Pipe.Core.Draw.Lay.Name("횡단", DH.Pipe.Core.Draw.Lay.Frame)).Count;
            int pf = PipeSheets.ModelSheets(db, CivilUtil.GroupName("PROF", route.AlignHandle), DH.Pipe.Core.Draw.Lay.Name("종단", DH.Pipe.Core.Draw.Lay.Frame)).Count;
            var plan = PipeSheets.Inspect(db, r.TabNames[0]);
            var prof = PipeSheets.Inspect(db, r.TabNames[1]);
            var xsec = PipeSheets.Inspect(db, r.TabNames[2]);
            Check("⑧ 장마다 도곽 · 뷰포트", plan.Frames == 1 && plan.Viewports == 1 && prof.Frames == pf && prof.Viewports == pf && xsec.Frames == xs && xsec.Viewports == xs && pf >= 1 && xs >= 1,
                $"평면 {plan.Frames}/{plan.Viewports} · 종단 {prof.Frames}/{prof.Viewports}(모형 {pf}) · 횡단 {xsec.Frames}/{xsec.Viewports}(모형 {xs})");
            Check("⑧ 뷰포트 축척", plan.Scales.All(v => Math.Abs(v - 1.0) < 1e-9) && prof.Scales.All(v => Math.Abs(v - 1.0) < 1e-9) && xsec.Scales.All(v => Math.Abs(v - 10.0) < 1e-9),
                $"평면 {string.Join(",", plan.Scales.Select(v => v.ToString("0.###")))} · 종단 {string.Join(",", prof.Scales.Select(v => v.ToString("0.###")))} · 횡단 {xsec.Scales.FirstOrDefault():0.###}");
            // 평면: 시작 → 끝 현이 가로가 되게 돌림(노선 (0,0) → (600,20))
            double want = -Math.Atan2(20, 600);
            Check("⑧ 평면 돌림", plan.Twists.Count == 1 && Math.Abs(plan.Twists[0] - want) < 1e-6, $"돌림 {plan.Twists.FirstOrDefault() * 180 / Math.PI:0.###}° (바라는 값 {want * 180 / Math.PI:0.###}°)");
            int setups = CountSetups(db);
            Check("⑧ 페이지 설정(A1 · A3)", setups == 2 * (1 + pf + xs), $"페이지 설정 {setups} (장 {1 + pf + xs} × 2)");
            // 다시 만들면 바꿔치기(탭이 늘지 않음) · 지우면 0
            var r2 = PipeSheets.Make(doc, route, SheetInput.Load(db));
            int tabs2 = CountTabs(db);
            Check("⑧ 다시 만들면 바꿔치기", r2.Ok && tabs2 == 3 && CountSetups(db) == setups, $"탭 {tabs2} · 페이지 설정 {CountSetups(db)}");
            // 평면 축척을 1:500 으로 — 한 장 380 m → 노선 629 m 가 두 장 · 꺾인 노선이라 장마다 돌림이 다르다
            var rp = PipeSheets.Make(doc, route, new SheetInput { Plan = true, Prof = false, Xsec = false, PlanScale = 500 });
            var plan2 = rp.Ok ? PipeSheets.Inspect(db, rp.TabNames[0]) : default;
            var bb = RouteBuilder.Build(db, route);
            var wins = DH.Pipe.Core.Draw.PlanSheets.Windows(bb.Ctx.Plan, DH.Pipe.Core.Draw.PlanSheets.Ranges(Math.Max(bb.S0, bb.Ctx.Profile.S0), Math.Min(bb.S1, bb.Ctx.Profile.S1), 380));
            Check("⑧ 평면 여러 장(1:500)", rp.Ok && plan2.Frames == 2 && plan2.Viewports == 2 && plan2.Scales.All(v => Math.Abs(v - 2.0) < 1e-9) && wins.Count == 2
                && Math.Abs(plan2.Twists[0] - wins[0].RotRad) < 1e-6 && Math.Abs(plan2.Twists[1] - wins[1].RotRad) < 1e-6 && Math.Abs(wins[0].RotRad - wins[1].RotRad) > 0.05 && CountTabs(db) == 3,
                rp.Ok ? $"장 {plan2.Frames} · 축척 {string.Join(",", plan2.Scales.Select(v => v.ToString("0.#")))} · 돌림 {string.Join(" / ", plan2.Twists.Select(v => (v * 180 / Math.PI).ToString("0.##") + "°"))} · 탭 {CountTabs(db)}" : rp.Line);
            int erased = PipeSheets.Erase(db, route, new StringBuilder());
            Check("⑧ 지우기", erased == 3 && CountTabs(db) == 0 && CountSetups(db) == 0, $"지운 탭 {erased} · 남은 탭 {CountTabs(db)} · 페이지 설정 {CountSetups(db)}");
            var r3 = PipeSheets.Make(doc, route, SheetInput.Load(db));
            // 눈으로 볼 PDF(첫 장씩) — 못 뽑아도 시험은 안 떨어뜨림(화면 없는 Civil 의 출력 장치 사정)
            foreach (var tab in r3.TabNames)
            {
                try
                {
                    string pdf = Path.Combine(dir, "sheet_" + (r3.TabNames.IndexOf(tab) + 1) + ".pdf");
                    string how = PlotFirstSheet(doc, tab, pdf);
                    _out.AppendLine($"      PDF «{tab}» — {how}");
                }
                catch (System.Exception ex) { _out.AppendLine($"      PDF «{tab}» 못 뽑음 — {ex.GetType().Name}: {ex.Message}"); }
            }
        });

        Finish(dir);
    }

    private static int CountOnLayer(Database db, string group, string layer)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
        int n = 0;
        if (gd.Contains(group))
            foreach (ObjectId id in ((Group)tr.GetObject(gd.GetAt(group), OpenMode.ForRead)).GetAllEntityIds())
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity e && e is not DBText && string.Equals(e.Layer, layer, StringComparison.OrdinalIgnoreCase)) n++;
        tr.Commit();
        return n;
    }

    private static string[] TagOf(Transaction tr, DBObject o)
    {
        try
        {
            if (o.ExtensionDictionary.IsNull) return Array.Empty<string>();
            var xd = (DBDictionary)tr.GetObject(o.ExtensionDictionary, OpenMode.ForRead);
            if (!xd.Contains(PipeSheets.TabTag)) return Array.Empty<string>();
            return ((Xrecord)tr.GetObject(xd.GetAt(PipeSheets.TabTag), OpenMode.ForRead)).Data?.AsArray().Select(v => v.Value?.ToString() ?? "").ToArray() ?? Array.Empty<string>();
        }
        catch { return Array.Empty<string>(); }
    }

    private static int CountTabs(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        int n = 0;
        foreach (DBDictionaryEntry de in (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead))
            if (TagOf(tr, tr.GetObject(de.Value, OpenMode.ForRead)).Length >= 4) n++;
        tr.Commit();
        return n;
    }

    private static int CountSetups(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        int n = 0;
        foreach (DBDictionaryEntry de in (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForRead))
            if (TagOf(tr, tr.GetObject(de.Value, OpenMode.ForRead)).Length >= 4) n++;
        tr.Commit();
        return n;
    }

    /// <summary>그 탭의 첫 장을 A3 페이지 설정으로 PDF 로 뽑는다(눈 확인용).</summary>
    private static string PlotFirstSheet(Document doc, string tab, string pdf)
    {
        var db = doc.Database;
        try { File.Delete(pdf); } catch { }
        var lm = LayoutManager.Current;
        lm.CurrentLayout = tab;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lo = (Layout)tr.GetObject(lm.GetLayoutId(tab), OpenMode.ForRead);
            var kind = TagOf(tr, lo);
            PlotSettings? named = null;
            foreach (DBDictionaryEntry de in (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForRead))
            {
                var ps0 = tr.GetObject(de.Value, OpenMode.ForRead);
                var t = TagOf(tr, ps0);
                if (t.Length >= 4 && t[0] == kind[0] && t[2] == "1" && t[3] == "A3") { named = (PlotSettings)ps0; break; }
            }
            if (named == null) return "A3 페이지 설정 없음";
            using var ps = new PlotSettings(false);
            ps.CopyFrom(named);
            var pi = new Autodesk.AutoCAD.PlottingServices.PlotInfo { Layout = lo.ObjectId, OverrideSettings = ps };
            var piv = new Autodesk.AutoCAD.PlottingServices.PlotInfoValidator { MediaMatchingPolicy = Autodesk.AutoCAD.PlottingServices.MatchingPolicy.MatchEnabled };
            piv.Validate(pi);
            using var pe = Autodesk.AutoCAD.PlottingServices.PlotFactory.CreatePublishEngine();
            pe.BeginPlot(null, null);
            pe.BeginDocument(pi, doc.Name, null, 1, true, pdf);
            using var ppi = new Autodesk.AutoCAD.PlottingServices.PlotPageInfo();
            pe.BeginPage(ppi, pi, true, null);
            pe.BeginGenerateGraphics(null);
            pe.EndGenerateGraphics(null);
            pe.EndPage(null);
            pe.EndDocument(null);
            pe.EndPlot(null);
            tr.Commit();
        }
        lm.CurrentLayout = "Model";
        var fi = new FileInfo(pdf);
        return fi.Exists ? $"{fi.Length} 바이트" : "파일 안 생김";
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
