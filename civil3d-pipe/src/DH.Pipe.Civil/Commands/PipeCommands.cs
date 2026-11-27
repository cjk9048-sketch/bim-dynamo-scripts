using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Quantity;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Pipe.Civil.Commands;

/// <summary>
/// 명령(묻는 쪽). 실제 일은 <see cref="Ops"/> 가 한다.
/// 알림 글은 단답형: «~ 없음 → 할 일».
/// </summary>
public sealed class PipeCommands
{
    private static Document Doc => AcadApp.DocumentManager.MdiActiveDocument;

    internal static void Say(string text)
    {
        try { Doc?.Editor.WriteMessage("\n[DH 관로] " + text + "\n"); } catch { }
    }

    /// <summary>명령 하나를 감싼다: 오류를 한 줄로 알리고 기록에 남긴다.</summary>
    private static void Run(string name, Action<Document> body)
    {
        var doc = Doc;
        if (doc == null) return;
        try { body(doc); }
        catch (Cancelled) { }
        catch (InvalidOperationException ex) { Say(ex.Message); PipeLog.Line($"[{name}] 못 함 — {ex.Message}"); }
        catch (System.Exception ex) { Say("원인: " + ex.Message); PipeLog.Line($"[{name}] 오류 — {ex}"); }
        finally { try { PipePalette.RefreshSoon(); } catch { } }
    }

    private static RouteData NeedRoute(Document doc)
        => PipeStore.Current(doc.Database) ?? throw new InvalidOperationException("노선 없음 → [노선 만들기]");

    private static string Sta(RouteData r, double s) => "No." + StationPlan.Name(s, r.Std.StationInterval);

    /// <summary>명령을 끊고 나간다(ESC) — <see cref="Run"/> 이 조용히 받는다.</summary>
    private sealed class Cancelled : System.Exception { }

    /// <summary>평면에서 한 점을 찍어 노선의 측점으로. Enter = null(기본값을 쓰라는 뜻) · ESC = 명령을 끊는다.</summary>
    private static double? PickStation(Document doc, PlanLine plan, RouteData r, string prompt, double s0, double s1)
    {
        var ppo = new PromptPointOptions("\n" + prompt) { AllowNone = true };
        var ppr = doc.Editor.GetPoint(ppo);
        if (ppr.Status == PromptStatus.Cancel || ppr.Status == PromptStatus.Error) throw new Cancelled();
        if (ppr.Status != PromptStatus.OK) return null;
        var w = ppr.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
        var (s, off) = plan.Project(new P2(w.X, w.Y));
        s = Math.Clamp(s, s0, s1);
        Say($"{Sta(r, s)} (노선에서 {Math.Abs(off):0.0} m)");
        return s;
    }

    private static string? Keyword(Document doc, string message, string def, params (string Key, string Text)[] items)
    {
        var pko = new PromptKeywordOptions("\n" + message) { AllowNone = true };
        foreach (var (k, t) in items) pko.Keywords.Add(k, k, $"{t}({k})");
        pko.Keywords.Default = def;
        var r = doc.Editor.GetKeywords(pko);
        if (r.Status == PromptStatus.None) return def;
        return r.Status == PromptStatus.OK ? r.StringResult : null;
    }

    private static double? Number(Document doc, string message, double def, bool allowZero = true)
    {
        var o = new PromptDoubleOptions("\n" + message) { DefaultValue = def, UseDefaultValue = true, AllowNegative = false, AllowZero = allowZero, AllowNone = true };
        var r = doc.Editor.GetDouble(o);
        if (r.Status == PromptStatus.None) return def;
        return r.Status == PromptStatus.OK ? r.Value : null;
    }

    private static readonly (string Key, string Text)[] MethodKeys =
    {
        ("O", "자연터파기"), ("S", "조립식"), ("T", "조절식"), ("H", "H파일"), ("P", "시트파일"), ("R", "하천횡단"), ("J", "추진"),
    };

    private static MethodKind KindOf(string key) => key switch
    {
        "S" => MethodKind.ShoreSk, "T" => MethodKind.ShoreTs, "H" => MethodKind.HPile, "P" => MethodKind.SheetPile,
        "R" => MethodKind.RiverOpen, "J" => MethodKind.Jacking, _ => MethodKind.Open,
    };

    // ================================================================ 창 · 정보
    [CommandMethod("DH_PIPE", "DHPIPE", CommandFlags.Session)]
    public void OpenPalette() { try { PipePalette.Show(); } catch (System.Exception ex) { Say("원인: " + ex.Message); PipeLog.Line("[창] " + ex); } }

    [CommandMethod("DH_PIPE", "DHPIPEABOUT", CommandFlags.Modal)]
    public void About()
    {
        Say($"{PipeSettings.ProductName} {PipeSettings.Version} · 기록 {PipeLog.FilePath}");
        try { if (!PipeLog.Headless) AboutWindow.ShowIt(); } catch (System.Exception ex) { PipeLog.Line("[정보] " + ex.Message); }
    }

    // ================================================================ ① 원지반
    [CommandMethod("DH_PIPE", "DHPIPESURF", CommandFlags.Modal)]
    public void PickSurface() => Run("원지반", doc =>
    {
        var db = doc.Database;
        var list = RouteBuilder.Surfaces(db);
        if (list.Count == 0) throw new InvalidOperationException("원지반(TIN 지표면) 없음 → 먼저 만들기");
        ObjectId pick = list[0].Id;
        if (list.Count > 1)
        {
            var sb = new StringBuilder("지표면:");
            for (int i = 0; i < list.Count; i++) sb.Append($"\n  {i + 1}. {list[i].Name}");
            Say(sb.ToString());
            var o = new PromptIntegerOptions("\n원지반 번호") { LowerLimit = 1, UpperLimit = list.Count, DefaultValue = 1, UseDefaultValue = true, AllowNone = true };
            var r = doc.Editor.GetInteger(o);
            if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return;
            pick = list[(r.Status == PromptStatus.OK ? r.Value : 1) - 1].Id;
        }
        string handle = CivilUtil.Handle(pick);
        PipeStore.SetText(db, "SURFACE", handle);       // 도면 안에(레지스트리에 두면 다른 도면의 같은 손잡이와 섞인다 — 검토 보통)
        var route = PipeStore.Current(db);
        if (route != null) { route.SurfaceHandle = handle; PipeStore.Save(db, route); }
        Say("원지반 = " + list.First(s => s.Id == pick).Name);
    });

    // ================================================================ ② 노선
    [CommandMethod("DH_PIPE", "DHPIPEROUTE", CommandFlags.Modal)]
    public void MakeRoute() => Run("노선", doc =>
    {
        var db = doc.Database;
        var ed = doc.Editor;
        var surfs = RouteBuilder.Surfaces(db);
        if (surfs.Count == 0) throw new InvalidOperationException("원지반(TIN 지표면) 없음 → 먼저 만들기");
        var peo = new PromptEntityOptions("\n노선으로 쓸 폴리선 선택: ");
        peo.SetRejectMessage("\n폴리선 · 선만.");
        peo.AddAllowedClass(typeof(Polyline), false);
        peo.AddAllowedClass(typeof(Polyline2d), false);
        peo.AddAllowedClass(typeof(Polyline3d), false);
        peo.AddAllowedClass(typeof(Line), false);
        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return;
        var pso = new PromptStringOptions("\n노선 이름") { DefaultValue = "관로", UseDefaultValue = true, AllowSpaces = true };
        var psr = ed.GetString(pso);
        if (psr.Status != PromptStatus.OK && psr.Status != PromptStatus.None) return;
        string? mat = Keyword(doc, "관종", "D", ("D", "주철관"), ("S", "강관"));
        if (mat == null) return;
        var dn = new PromptIntegerOptions("\n관경(mm)") { DefaultValue = 300, UseDefaultValue = true, LowerLimit = 50, UpperLimit = 3000, AllowNone = true };
        var dr = ed.GetInteger(dn);
        if (dr.Status != PromptStatus.OK && dr.Status != PromptStatus.None) return;
        // 원지반: 지금 노선이 쓰던 것 · 마지막에 고른 것 · 하나뿐이면 그것
        ObjectId surf = CivilUtil.FromHandle(db, PipeStore.GetText(db, "SURFACE"));
        if (surf.IsNull || surfs.All(s => s.Id != surf)) surf = surfs.Count == 1 ? surfs[0].Id : ObjectId.Null;
        var log = new StringBuilder();
        var route = Ops.CreateRoute(doc, per.ObjectId, surf, psr.Status == PromptStatus.OK ? psr.StringResult : "관로", log);
        route.Runs[0].Pipe = PipeCatalog.Make(mat == "S" ? "SP" : "DCIP", dr.Status == PromptStatus.OK ? dr.Value : 300);
        PipeStore.Save(db, route);
        Say(log.ToString().TrimEnd() + $" · {route.Runs[0].Pipe.Name}" + (surf.IsNull ? " · 원지반 여럿 → [지표면 고르기]" : ""));
    });

    [CommandMethod("DH_PIPE", "DHPIPERUN", CommandFlags.Modal)]
    public void SetRun() => Run("관 구간", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        var b = RouteBuilder.Build(db, route, needProfile: false);
        double len = b.Ctx.Plan.Length;
        double? a = PickStation(doc, b.Ctx.Plan, route, "관을 바꿀 구간 시작점 <처음부터>: ", 0, len);
        double? e = a == null ? null : PickStation(doc, b.Ctx.Plan, route, "끝점 <끝까지>: ", 0, len);
        string? mat = Keyword(doc, "관종", "D", ("D", "주철관"), ("S", "강관"));
        if (mat == null) return;
        var dn = new PromptIntegerOptions("\n관경(mm)") { DefaultValue = 300, UseDefaultValue = true, LowerLimit = 50, UpperLimit = 3000, AllowNone = true };
        var dr = doc.Editor.GetInteger(dn);
        if (dr.Status != PromptStatus.OK && dr.Status != PromptStatus.None) return;
        var pipe = PipeCatalog.Make(mat == "S" ? "SP" : "DCIP", dr.Status == PromptStatus.OK ? dr.Value : 300);
        Ops.SetRun(route, a ?? 0, e ?? len, pipe, len);
        PipeStore.Save(db, route);
        Say($"{Sta(route, a ?? 0)} ~ {Sta(route, e ?? len)} = {pipe.Name} (외경 {pipe.Od * 1000:0} mm)");
    });

    [CommandMethod("DH_PIPE", "DHPIPEZONE", CommandFlags.Modal)]
    public void TagZones() => Run("지표 구역", doc =>
    {
        var db = doc.Database;
        string? k = Keyword(doc, "지표 종류", "A", ("A", "아스콘"), ("C", "콘크리트"), ("B", "보도블록"), ("K", "연석선"));
        if (k == null) return;
        bool curb = k == "K";
        var pso = new PromptSelectionOptions { MessageForAdding = curb ? "\n연석 선(폴리선 · 선) 선택: " : "\n구역 테두리(닫힌 폴리선) 선택: " };
        var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, curb ? "LWPOLYLINE,LINE,POLYLINE" : "LWPOLYLINE,POLYLINE") });
        var psr = doc.Editor.GetSelection(pso, filter);
        if (psr.Status != PromptStatus.OK) return;
        var kind = k == "A" ? SurfaceKind.Asphalt : k == "C" ? SurfaceKind.Concrete : SurfaceKind.Block;
        int n = 0, open = 0, locked = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ObjectId lay = curb ? CivilUtil.EnsureLayer(db, tr, PipeLayers.Curb, 3) : CivilUtil.EnsureLayer(db, tr, PipeLayers.ZoneLayer(kind), PipeLayers.ZoneColor(kind));
            foreach (ObjectId id in psr.Value.GetObjectIds())
            {
                try
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (!curb && ent is Polyline pl && !pl.Closed) { open++; continue; }
                    ent.UpgradeOpen();                  // 잠긴 레이어의 객체는 여기서 막힌다 → 그것만 건너뜀
                    ent.LayerId = lay;
                    n++;
                }
                catch { locked++; }
            }
            tr.Commit();
        }
        Say($"{(curb ? "연석 선" : "구역")} {n}개(레이어를 «{(curb ? PipeLayers.Curb : PipeLayers.ZoneLayer(kind))}» 로 옮김)"
            + (open > 0 ? $" · 안 닫힌 폴리선 {open}개 뺌 → 닫기" : "") + (locked > 0 ? $" · 잠긴 레이어 {locked}개 뺌" : ""));
    });

    // ================================================================ ③ 종단
    [CommandMethod("DH_PIPE", "DHPIPEPROFILE", CommandFlags.Modal)]
    public void MakeProfile() => Run("처음 종단", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        if (!CivilUtil.FromHandle(db, route.PipeProfileHandle).IsNull)
        {
            string? yn = Keyword(doc, "종단이 이미 있음 — PVI 를 새로 만들까요", "N", ("Y", "새로"), ("N", "그대로"));
            if (yn != "Y") return;
        }
        double? band = Number(doc, $"여유(토피 띠 두께 m — 크면 PVI 적음)", route.Std.SmoothBand, false);
        if (band == null) return;
        route.Std.SmoothBand = band.Value;
        Point3d? at = null;
        if (CivilUtil.FromHandle(db, route.ProfileViewHandle).IsNull)
        {
            var ppr = doc.Editor.GetPoint(new PromptPointOptions("\n종단뷰 놓을 자리 <안 만듦>: ") { AllowNone = true });
            if (ppr.Status == PromptStatus.OK) at = ppr.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
            else if (ppr.Status != PromptStatus.None) return;
        }
        var log = new StringBuilder();
        string msg = Ops.MakeProfile(doc, route, false, at, log);
        Say(msg + (log.Length > 0 ? "\n" + log.ToString().TrimEnd() : ""));
    });

    [CommandMethod("DH_PIPE", "DHPIPECHECK", CommandFlags.Modal)]
    public void Check() => Run("종단 검사", doc =>
    {
        var route = NeedRoute(doc);
        var c = Ops.Compute(doc.Database, route);
        var sb = new StringBuilder($"PVI {c.B.Ctx.Profile.Pvi.Count} · 측점 {c.Rows.Count} · 경고 {c.Issues.Count}");
        foreach (string n in c.Notes) sb.Append("\n  [알림] " + n);
        foreach (var i in c.Issues) sb.Append($"\n  [{i.Kind}] {Sta(route, i.StaFrom)}" + (i.StaTo - i.StaFrom > 0.01 ? $" ~ {Sta(route, i.StaTo)}" : "") + " " + i.Text);
        foreach (var r in c.Rows.Where(r => !r.After.Ok || !r.Before.Ok)) sb.Append($"\n  [단면] No.{r.Name} " + string.Join(" · ", r.After.Messages.Concat(r.Before.Messages).Distinct()));
        Say(sb.ToString());
    });

    [CommandMethod("DH_PIPE", "DHPIPEOBST", CommandFlags.Modal)]
    public void AddObstacle() => Run("지장물", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        var b = RouteBuilder.Build(db, route, needProfile: false);
        double? s = PickStation(doc, b.Ctx.Plan, route, "지장물 자리(평면에서 찍기): ", b.S0, b.S1);
        if (s == null) return;
        var o = AskObstacle(doc);
        if (o == null) return;
        o.Sta = s.Value;
        route.Obstacles.Add(o);
        route.Obstacles = route.Obstacles.OrderBy(x => x.Sta).ToList();
        PipeStore.Save(db, route);
        try { Ops.DrawMarks(doc, route); } catch { }
        Say($"{Sta(route, o.Sta)} {o.Kind} " + (o.Round ? $"D{o.Width * 1000:0}" : $"{o.Width:0.00}×{o.Height:0.00}") + $" · 윗면 깊이 {o.Depth:0.00} · 지장물 {route.Obstacles.Count}개");
    });

    private static readonly (string Key, string Text)[] ObstKeys =
    {
        ("W", "상수"), ("S", "하수"), ("R", "우수"), ("G", "가스"), ("T", "통신"), ("E", "전력"), ("B", "구조물"), ("X", "기타"),
    };

    /// <summary>지장물 종류 · 크기 · 깊이를 묻는다.</summary>
    private static Obstacle? AskObstacle(Document doc)
    {
        string? k = Keyword(doc, "종류", "S", ObstKeys);
        if (k == null) return null;
        string kind = ObstKeys.First(x => x.Key == k).Text;
        var o = new Obstacle { Kind = kind };
        if (k == "B")
        {
            double? w = Number(doc, "폭(m)", 1.0, false);
            double? h = w == null ? null : Number(doc, "높이(m)", 1.0, false);
            if (h == null) return null;
            o.Round = false; o.Width = w!.Value; o.Height = h.Value;
        }
        else
        {
            double? d = Number(doc, "관경(mm)", 300, false);
            if (d == null) return null;
            o.Width = o.Height = d.Value / 1000.0;
        }
        double? dep = Number(doc, "윗면까지 깊이(지반 아래 m)", 1.0);
        if (dep == null) return null;
        o.Depth = dep.Value;
        return o;
    }

    [CommandMethod("DH_PIPE", "DHPIPEOBSTSEL", CommandFlags.Modal)]
    public void ObstaclesFromLines() => Run("지장물 선", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        var b = RouteBuilder.Build(db, route, needProfile: false);
        var pso = new PromptSelectionOptions { MessageForAdding = "\n지하시설물 선(노선과 만나는 폴리선 · 선) 선택: " };
        var psr = doc.Editor.GetSelection(pso, new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,LINE,POLYLINE") }));
        if (psr.Status != PromptStatus.OK) return;
        var proto = AskObstacle(doc);
        if (proto == null) return;
        int n = 0, withZ = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in psr.Value.GetObjectIds())
            {
                if (id == b.AlignId) continue;
                var pts = CivilUtil.PolyPoints(tr, id, out bool closed);
                if (closed && pts.Count > 0) pts.Add(pts[0]);
                // 선에 높이(Z)가 있으면 그 높이를 윗면 표고로
                double? z = null;
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is Polyline pl && Math.Abs(pl.Elevation) > 1e-6) z = pl.Elevation;
                else if (ent is Line ln && Math.Abs(ln.StartPoint.Z) > 1e-6) z = 0.5 * (ln.StartPoint.Z + ln.EndPoint.Z);
                foreach (double s in b.Ctx.Plan.Crossings(pts))
                {
                    if (s < b.S0 || s > b.S1) continue;
                    if (route.Obstacles.Any(o => Math.Abs(o.Sta - s) < 0.05 && o.Kind == proto.Kind)) continue;
                    route.Obstacles.Add(new Obstacle { Sta = s, Kind = proto.Kind, Width = proto.Width, Height = proto.Height, Round = proto.Round, Depth = proto.Depth, TopElev = z });
                    n++;
                    if (z.HasValue) withZ++;
                }
            }
            tr.Commit();
        }
        route.Obstacles = route.Obstacles.OrderBy(x => x.Sta).ToList();
        PipeStore.Save(db, route);
        try { Ops.DrawMarks(doc, route); } catch { }
        Say($"지장물 {n}개 더함(선의 높이를 윗면 표고로 쓴 것 {withZ}) · 모두 {route.Obstacles.Count}개");
    });

    [CommandMethod("DH_PIPE", "DHPIPEOBSTDEL", CommandFlags.Modal)]
    public void DelObstacle() => Run("지장물 지우기", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        if (route.Obstacles.Count == 0) throw new InvalidOperationException("지장물 없음");
        var b = RouteBuilder.Build(db, route, needProfile: false);
        string? k = Keyword(doc, "지울 것", "P", ("P", "찍은곳"), ("A", "모두"));
        if (k == null) return;
        int before = route.Obstacles.Count;
        if (k == "A") route.Obstacles.Clear();
        else
        {
            double? s = PickStation(doc, b.Ctx.Plan, route, "지울 지장물 가까이 찍기: ", b.S0, b.S1);
            if (s == null) return;
            var near = route.Obstacles.OrderBy(o => Math.Abs(o.Sta - s.Value)).First();
            if (Math.Abs(near.Sta - s.Value) > 10) throw new InvalidOperationException("10 m 안에 지장물 없음");
            route.Obstacles.Remove(near);
        }
        PipeStore.Save(db, route);
        try { Ops.DrawMarks(doc, route); } catch { }
        Say($"지장물 {before - route.Obstacles.Count}개 지움 · 남은 것 {route.Obstacles.Count}");
    });

    [CommandMethod("DH_PIPE", "DHPIPEGUIDE", CommandFlags.Modal)]
    public void MakeGuide() => Run("가이드안", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        var log = new StringBuilder();
        string pre = "";
        if (route.Obstacles.Count > 0)
        {
            string? k = Keyword(doc, "지장물을 피하게 종단을 다시 만들까요(지금 PVI 는 지워짐)", "N", ("Y", "회피"), ("N", "자리제안만"));
            if (k == null) return;
            if (k == "Y") pre = Ops.MakeProfile(doc, route, true, null, log) + "\n";
        }
        string msg = Ops.MakeGuide(doc, route, log);
        try { Ops.DrawMarks(doc, route); } catch { }
        Say(pre + msg + (log.Length > 0 ? "\n" + log.ToString().TrimEnd() : ""));
    });

    [CommandMethod("DH_PIPE", "DHPIPEPROFSHEET", CommandFlags.Modal)]
    public void ProfileSheet() => Run("종단도면", doc =>
    {
        var route = NeedRoute(doc);
        double? hs = Number(doc, "가로 축척 1:", route.Std.ProfileHScale, false);
        double? vs = hs == null ? null : Number(doc, "세로 축척 1:", route.Std.ProfileVScale, false);
        if (vs == null) return;
        route.Std.ProfileHScale = hs!.Value;
        route.Std.ProfileVScale = vs.Value;
        var ppr = doc.Editor.GetPoint("\n종단도면 놓을 자리(왼쪽 아래): ");
        if (ppr.Status != PromptStatus.OK) return;
        PipeStore.Save(doc.Database, route);
        Say(Ops.DrawProfileSheet(doc, route, ppr.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem)));
    });

    // ================================================================ ④ 구간 공법 · 땅속
    [CommandMethod("DH_PIPE", "DHPIPEMETHOD", CommandFlags.Modal)]
    public void AddMethod() => Run("구간 공법", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        var b = RouteBuilder.Build(db, route, needProfile: false);
        double? a = PickStation(doc, b.Ctx.Plan, route, "구간 시작점(평면에서 찍기): ", b.S0, b.S1);
        if (a == null) return;
        double? e = PickStation(doc, b.Ctx.Plan, route, "구간 끝점: ", b.S0, b.S1);
        if (e == null) return;
        string? k = Keyword(doc, "터파기 방식", "O", MethodKeys);
        if (k == null) return;
        var m = new MethodSpan { Kind = KindOf(k) };
        switch (m.Kind)
        {
            case MethodKind.Open:
            {
                double? n = Number(doc, "토사 경사 1:", route.Std.SlopeSoil);
                if (n == null) return;
                if (Math.Abs(n.Value - route.Std.SlopeSoil) > 1e-9) m.SlopeSoil = n.Value;
                break;
            }
            case MethodKind.RiverOpen:
            {
                double? cv = Number(doc, "하상 아래 토피(m)", route.Std.RiverCover, false);
                if (cv == null) return;
                m.Cover = cv.Value;
                string? enc = Keyword(doc, "콘크리트 보호공", "Y", ("Y", "있음"), ("N", "없음"));
                if (enc == null) return;
                m.Encase = enc == "Y";
                string? dam = Keyword(doc, "가물막이", "B", ("B", "톤마대"), ("S", "시트파일"));
                if (dam == null) return;
                m.Dam = dam == "S" ? DamKind.Sheet : DamKind.Bag;
                break;
            }
            case MethodKind.Jacking:
            {
                double? cv = Number(doc, "하상(가장 낮은 땅) 아래 토피(m · 케이싱 윗면)", route.Std.JackCover, false);
                if (cv == null) return;
                m.Cover = cv.Value;
                string? sh = Keyword(doc, "수직구", "R", ("R", "사각"), ("C", "원형"));
                if (sh == null) return;
                m.Shaft = sh == "C" ? ShaftShape.Circle : ShaftShape.Rect;
                if (m.Shaft == ShaftShape.Rect)
                {
                    string? w = Keyword(doc, "수직구 흙막이", "H", ("H", "H파일토류판"), ("S", "시트파일"));
                    if (w == null) return;
                    m.Wall = w == "S" ? ShaftWall.Sheet : ShaftWall.HPile;
                }
                break;
            }
            default:
            {
                double? w = Number(doc, "바닥폭(m) <0 = 규칙대로>", 0);
                if (w == null) return;
                if (w.Value > 0) m.Width = w.Value;
                break;
            }
        }
        Ops.SetMethod(route, a.Value, e.Value, m);
        PipeStore.Save(db, route);
        bool level = m.IsLevel && !CivilUtil.FromHandle(db, route.PipeProfileHandle).IsNull;
        Say($"{Sta(route, Math.Min(a.Value, e.Value))} ~ {Sta(route, Math.Max(a.Value, e.Value))} = {MethodSpan.NameOf(m.Kind)} · 구간 {route.Methods.Count}개"
            + (level ? " · 종단을 이 구간 높이에 맞추려면 [처음 종단]을 다시" : ""));
    });

    [CommandMethod("DH_PIPE", "DHPIPEMETHODDEL", CommandFlags.Modal)]
    public void DelMethod() => Run("구간 지우기", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        if (route.Methods.Count == 0) throw new InvalidOperationException("공법 구간 없음(전부 자연 터파기)");
        var b = RouteBuilder.Build(db, route, needProfile: false);
        double? s = PickStation(doc, b.Ctx.Plan, route, "지울 구간 안을 찍기: ", b.S0, b.S1);
        if (s == null) return;
        var hit = route.Methods.FirstOrDefault(m => s.Value >= m.StaFrom && s.Value <= m.StaTo) ?? throw new InvalidOperationException("그 자리에 구간 없음");
        route.Methods.Remove(hit);
        PipeStore.Save(db, route);
        Say($"{MethodSpan.NameOf(hit.Kind)} 구간 지움 → 자연 터파기 · 남은 구간 {route.Methods.Count}");
    });

    [CommandMethod("DH_PIPE", "DHPIPESTRATA", CommandFlags.Modal)]
    public void AddStrata() => Run("땅속 값", doc =>
    {
        var db = doc.Database;
        var route = NeedRoute(doc);
        var b = RouteBuilder.Build(db, route, needProfile: false);
        string? k = Keyword(doc, "땅속 값", "A", ("A", "더하기"), ("C", "모두지우기"));
        if (k == null) return;
        if (k == "C") { route.Strata.Clear(); PipeStore.Save(db, route); Say("땅속 값 지움 → 전부 토사 · 육상"); return; }
        double? s = PickStation(doc, b.Ctx.Plan, route, "시추 자리(평면에서 찍기) <노선 전체>: ", b.S0, b.S1);
        double? soil = Number(doc, "토사 두께(m) <0 = 끝없이 토사>", 0);
        if (soil == null) return;
        double? wr = soil.Value > 0 ? Number(doc, "풍화암 두께(m) <0 = 그 아래 전부 풍화암>", 0) : 0;
        if (wr == null) return;
        double? wd = Number(doc, "지하수위 깊이(지반 아래 m) <0 = 없음>", 0);
        if (wd == null) return;
        var p = new StrataPoint
        {
            Sta = s ?? 0,
            SoilThk = soil.Value > 0 ? soil.Value : null,
            WrThk = wr.Value > 0 ? wr.Value : null,
            WaterDepth = wd.Value > 0 ? wd.Value : null,
        };
        route.Strata.RemoveAll(x => Math.Abs(x.Sta - p.Sta) < 0.05);
        route.Strata.Add(p);
        route.Strata = route.Strata.OrderBy(x => x.Sta).ToList();
        PipeStore.Save(db, route);
        Say($"{Sta(route, p.Sta)} 토사 {(p.SoilThk.HasValue ? p.SoilThk.Value.ToString("0.0") : "끝없이")} · 풍화암 {(p.WrThk.HasValue ? p.WrThk.Value.ToString("0.0") : "-")} · 지하수위 {(p.WaterDepth.HasValue ? p.WaterDepth.Value.ToString("0.0") : "없음")} · 시추 {route.Strata.Count}곳");
    });

    // ================================================================ ⑤ 횡단도
    [CommandMethod("DH_PIPE", "DHPIPEXSEC", CommandFlags.Modal)]
    public void XsecSheets() => Run("횡단도", doc =>
    {
        var route = NeedRoute(doc);
        double? sc = Number(doc, "축척 1:", route.Std.XsecScale, false);
        if (sc == null) return;
        route.Std.XsecScale = sc.Value;
        var ppr = doc.Editor.GetPoint("\n횡단도 놓을 자리(첫 장 왼쪽 아래): ");
        if (ppr.Status != PromptStatus.OK) return;
        PipeStore.Save(doc.Database, route);
        Say(Ops.DrawXsec(doc, route, ppr.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem)));
    });

    // ================================================================ ⑥ 수량 · 3D
    [CommandMethod("DH_PIPE", "DHPIPEQTY", CommandFlags.Modal)]
    public void Qty() => Run("수량", doc =>
    {
        var route = NeedRoute(doc);
        string dir = "";
        try { dir = Path.GetDirectoryName(doc.Name) ?? ""; } catch { }
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string path = Path.Combine(dir, $"{Safe(route.Name)}_수량산출서.xlsx");
        if (!PipeLog.Headless)
        {
            var o = new PromptSaveFileOptions("\n엑셀 저장") { Filter = "엑셀 (*.xlsx)|*.xlsx", InitialDirectory = dir, InitialFileName = Path.GetFileName(path) };
            var r = doc.Editor.GetFileNameForSave(o);
            if (r.Status != PromptStatus.OK) return;
            path = r.StringResult;
        }
        string msg;
        try { msg = Ops.ExportXlsx(doc, route, path); }
        catch (IOException) { throw new InvalidOperationException("엑셀 파일이 열려 있음 → 닫고 다시"); }
        Say(msg + "\n  " + path);
    });

    private static string Safe(string name)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return name;
    }

    [CommandMethod("DH_PIPE", "DHPIPE3D", CommandFlags.Modal)]
    public void Model3D() => Run("3D", doc => Say(Ops.Make3D(doc, NeedRoute(doc))));

    [CommandMethod("DH_PIPE", "DHPIPEPICK", CommandFlags.Modal)]
    public void PickRoute() => Run("노선 고르기", doc =>
    {
        var db = doc.Database;
        var all = PipeStore.All(db);
        if (all.Count == 0) throw new InvalidOperationException("노선 없음 → [노선 만들기]");
        var peo = new PromptEntityOptions("\n다룰 노선(선형) 선택: ");
        peo.SetRejectMessage("\n선형만.");
        peo.AddAllowedClass(typeof(CivilDb.Alignment), false);
        var per = doc.Editor.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return;
        string h = CivilUtil.Handle(per.ObjectId);
        var r = all.FirstOrDefault(x => x.AlignHandle == h) ?? throw new InvalidOperationException("관로 노선이 아님 → [노선 만들기]");
        PipeStore.SetCurrent(db, h);
        Say("지금 노선 = " + r.Name);
    });

    /// <summary>부지정지 애드인의 명령을 부른다(원지반 만들기 · 지층 · 로드뷰).</summary>
    [CommandMethod("DH_PIPE", "DHPIPEGRADING", CommandFlags.Modal)]
    public void CallGrading() => Run("부지정지 부르기", doc =>
    {
        if (!GradingLoaded()) throw new InvalidOperationException("부지정지 애드인 없음 → 설치 · 이미 있는 지표면은 [지표면 고르기]");
        string? k = Keyword(doc, "원지반", "G", ("G", "원지반창"), ("N", "수치지도"), ("C", "자르기"), ("S", "지층"), ("V", "로드뷰"));
        if (k == null) return;
        string cmd = k switch { "N" => "DHNGII", "C" => "DHCROP", "S" => "DHSTRATA", "V" => "DHROADVIEW", _ => "DHGROUNDPANEL" };
        doc.SendStringToExecute(cmd + " ", true, false, true);
    });

    internal static bool GradingLoaded()
    {
        try { return AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "DH.Grading.Civil"); }
        catch { return false; }
    }
}
