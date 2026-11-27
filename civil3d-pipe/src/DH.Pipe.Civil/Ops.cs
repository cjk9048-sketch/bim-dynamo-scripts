using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Pipe.Core.Draw;
using DH.Pipe.Core.Export;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Model3D;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using DH.Pipe.Core.Section;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;
using CivilStyles = Autodesk.Civil.DatabaseServices.Styles;

namespace DH.Pipe.Civil;

/// <summary>
/// 묻지 않고 하는 일들(노선 만들기 · 종단 · 공법 · 그리기 · 엑셀 · 3D).
/// 명령(묻는 쪽)과 자가 시험이 같은 함수를 부른다. 못 하면 까닭을 적어 던진다.
/// </summary>
internal static class Ops
{
    /// <summary>한 노선의 계산 결과 한 벌.</summary>
    internal sealed class Calc
    {
        public Built B { get; set; } = new();
        public List<StationRow> Rows { get; set; } = new();
        public EarthTable Earth { get; set; } = new();
        public Summary Sum { get; set; } = new();
        public List<ProfileIssue> Issues { get; set; } = new();
        public List<ObstacleCheck> Obst { get; set; } = new();
        /// <summary>알림(원지반 범위 · 종단이 짧음 · 종단 곡선 …).</summary>
        public List<string> Notes => B.Notes;

        /// <summary>그릴 단면들(경계 측점은 전 · 후 두 장).</summary>
        public List<SectionResult> Sections()
        {
            var secs = new List<SectionResult>();
            foreach (var r in Rows)
            {
                secs.Add(r.Before);
                if (r.IsBreak) secs.Add(r.After);
            }
            return secs;
        }
    }

    /// <param name="built">이미 읽어 둔 자료(있으면 도면을 다시 읽지 않는다).</param>
    internal static Calc Compute(Database db, RouteData route, Built? built = null)
    {
        var b = built ?? RouteBuilder.Build(db, route);
        if (b.Ctx.Profile.IsEmpty) throw new InvalidOperationException("종단 없음 → [처음 종단]");
        var c = new Calc { B = b };
        double s0 = Math.Max(b.S0, b.Ctx.Profile.S0), s1 = Math.Min(b.S1, b.Ctx.Profile.S1);
        if (s1 - s0 < 0.5) throw new InvalidOperationException("종단이 노선과 안 겹침 → [처음 종단]");
        c.Rows = RouteEngine.BuildRows(b.Ctx, s0, s1);
        int outside = c.Rows.Count(r => r.After.Messages.Contains(RouteEngine.OutsideStrata) || r.Before.Messages.Contains(RouteEngine.OutsideStrata));
        if (outside > 0) b.Notes.Add($"지층면 밖 측점 {outside}곳 — " + (route.Strata.Count > 0 ? "땅속 값으로 셈" : "토사로 셈"));
        c.Earth = EarthTable.Build(c.Rows, route.Std.DepthLimit);
        c.Sum = Summary.Build(b.Ctx, c.Rows, b.Ground);
        c.Issues = ProfileCheck.Run(b.Ctx.Profile, b.Ground, route);
        c.Obst = ProfileCheck.Obstacles(b.Ctx.Profile, b.Ground, route);
        return c;
    }

    private static ObjectId StyleOrNew(Database db, CivilStyles.StyleCollectionBase coll, string newName, params string[] prefer)
    {
        var id = CivilUtil.PickStyle(db, coll, prefer);
        if (!id.IsNull) return id;
        try { return coll.Add(newName); }
        catch (System.Exception ex)
        {
            PipeLog.Line($"[스타일] {coll.GetType().Name} 에 «{newName}» 못 만듦 — {ex.GetType().Name}: {ex.Message} · 개수 {coll.Count}");
            return ObjectId.Null;
        }
    }

    // ================================================================ ② 노선
    /// <summary>폴리선 → Civil 선형 + 노선 자료. 원본 폴리선은 그대로 둔다.</summary>
    internal static RouteData CreateRoute(Document doc, ObjectId plineId, ObjectId surfId, string? name, StringBuilder log)
    {
        var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        ObjectId lay = CivilUtil.EnsureLayer(db, PipeLayers.Align, 2);
        ObjectId flat = CivilUtil.FlatCopy(db, plineId, lay, out int nv, out double len);
        if (flat.IsNull) throw new InvalidOperationException("폴리선 점 부족");
        string nm = CivilUtil.UniqueAlignName(db, cdoc, string.IsNullOrWhiteSpace(name) ? "관로" : name!.Trim());
        ObjectId aid;
        try
        {
            aid = CivilDb.Alignment.Create(cdoc,
                new CivilDb.PolylineOptions { PlineId = flat, EraseExistingEntities = true, AddCurvesBetweenTangents = false },
                nm, ObjectId.Null, lay,
                StyleOrNew(db, cdoc.Styles.AlignmentStyles, "DHP", "기본", "Standard", "Basic"),
                StyleOrNew(db, cdoc.Styles.LabelSetStyles.AlignmentLabelSetStyles, "DHP", "_없음", "None", "표준", "Standard"));
        }
        catch
        {
            try { using var trE = db.TransactionManager.StartTransaction(); if (!flat.IsErased) trE.GetObject(flat, OpenMode.ForWrite).Erase(); trE.Commit(); } catch { }
            throw;
        }
        var route = new RouteData { Name = nm, AlignHandle = CivilUtil.Handle(aid), SurfaceHandle = CivilUtil.Handle(surfId) };
        route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = len, Pipe = PipeCatalog.Make("DCIP", 300) });
        PipeStore.Save(db, route);
        log.AppendLine($"노선 «{nm}» · 점 {nv} · {len:0.0} m");
        PipeLog.Line($"[노선] «{nm}» 만듦 · 점 {nv} · {len:0.0} m · 손잡이 {route.AlignHandle}");
        return route;
    }

    // ================================================================ ③ 종단
    /// <summary>
    /// 처음 종단(토피 띠 안 최소 꺾임)을 만들어 Civil 종단(레이아웃)으로 넣는다. 이미 있으면 PVI 를 바꾼다.
    /// viewAt 을 주면 종단뷰도(없을 때만).
    /// </summary>
    internal static string MakeProfile(Document doc, RouteData route, bool avoid, Point3d? viewAt, StringBuilder log)
    {
        var db = doc.Database;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        var b = RouteBuilder.Build(db, route, needProfile: false);
        var res = InitialProfile.Make(b.Ground, route, avoid);
        if (res.Line.Pvi.Count < 2) throw new InvalidOperationException("종단 못 만듦(지반 범위)");
        ObjectId lay = CivilUtil.EnsureLayer(db, PipeLayers.Profile, 6);
        var labels = StyleOrNew(db, cdoc.Styles.LabelSetStyles.ProfileLabelSetStyles, "DHP", "_없음", "None", "표준", "Standard");
        var gStyle = StyleOrNew(db, cdoc.Styles.ProfileStyles, "DHP", "기존", "Existing", "지반", "기본", "Standard");
        var pStyle = StyleOrNew(db, cdoc.Styles.ProfileStyles, "DHP", "설계", "Design", "계획", "기본", "Standard");
        // Civil 템플릿으로 만든 도면이 아니면 종단 라벨 묶음 뿌리가 없어 스타일을 만들 수도 없다(1127 화면 없는 Civil 실측 «Root Oid is not valid»)
        if (labels.IsNull || gStyle.IsNull || pStyle.IsNull) throw new InvalidOperationException("종단 스타일 없음 → Civil 3D 템플릿으로 만든 도면에서 시작");

        // 지반 종단(Civil) — 종단뷰에 보이라고 만든다. 못 만들어도 계속.
        // Civil 의 종단 만들기는 부른 그 자리에서 도면에 남는다 → 만든 직후 손잡이를 저장한다(뒤에서 실패해도 임자 없는 종단이 안 남게 — 검토 보통)
        if (CivilUtil.FromHandle(db, route.GroundProfileHandle).IsNull)
        {
            try
            {
                var gid = CivilDb.Profile.CreateFromSurface(CivilUtil.UniqueProfileName(db, b.AlignId, b.AlignName + "-지반"), b.AlignId, b.SurfId, lay, gStyle, labels);
                route.GroundProfileHandle = CivilUtil.Handle(gid);
                PipeStore.Save(db, route);
            }
            catch (System.Exception ex) { log.AppendLine("지반 종단 못 만듦 — " + ex.Message); PipeLog.Line("[종단] 지반 종단 실패 — " + ex); }
        }

        // Civil 은 선형 밖 측점의 PVI 를 거절한다 → 선형 범위 안으로(끝은 0.1 mm 안쪽)
        double staMax = Math.Max(b.StaBase, b.StaEnd - 1e-4);
        var pvi = res.Line.Pvi.Select(p => (S: Math.Clamp(p.S + b.StaBase, b.StaBase, staMax), p.Z)).ToList();
        for (int i = pvi.Count - 1; i >= 1; i--) if (pvi[i].S - pvi[i - 1].S < 1e-6) pvi.RemoveAt(i - 1 == 0 ? i : i - 1);     // 잘라서 겹친 점
        if (pvi.Count < 2) throw new InvalidOperationException("종단 못 만듦(노선이 너무 짧음)");
        ObjectId pid = CivilUtil.FromHandle(db, route.PipeProfileHandle);
        if (pid.IsNull)
        {
            pid = CivilDb.Profile.CreateByLayout(CivilUtil.UniqueProfileName(db, b.AlignId, b.AlignName + "-관저고"), b.AlignId, lay, pStyle, labels);
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var pr = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForWrite);
                foreach (var (s, z) in pvi) pr.PVIs.AddPVI(s, z);
                tr.Commit();
            }
            catch
            {
                // PVI 를 못 넣었으면 빈 종단을 지운다
                try { using var trE = db.TransactionManager.StartTransaction(); trE.GetObject(pid, OpenMode.ForWrite).Erase(); trE.Commit(); } catch { }
                throw;
            }
            route.PipeProfileHandle = CivilUtil.Handle(pid);
            PipeStore.Save(db, route);
        }
        else ReplacePvis(db, pid, pvi);

        if (viewAt.HasValue && CivilUtil.FromHandle(db, route.ProfileViewHandle).IsNull)
        {
            try
            {
                ObjectId vid = CivilDb.ProfileView.Create(b.AlignId, viewAt.Value);
                using var tr = db.TransactionManager.StartTransaction();
                ((Entity)tr.GetObject(vid, OpenMode.ForWrite)).LayerId = CivilUtil.EnsureLayer(db, tr, PipeLayers.View, 7);
                tr.Commit();
                route.ProfileViewHandle = CivilUtil.Handle(vid);
            }
            catch (System.Exception ex) { log.AppendLine("종단뷰 못 만듦 — " + ex.Message); PipeLog.Line("[종단] 종단뷰 실패 — " + ex); }
        }
        PipeStore.Save(db, route);
        foreach (string n in res.Notes) log.AppendLine(n);
        foreach (string n in b.Notes) log.AppendLine(n);
        string msg = $"PVI {res.Line.Pvi.Count}개 · 토피 평균 {res.AvgCover:0.00} 최소 {res.MinCover:0.00} 최대 {res.MaxCover:0.00}" + (avoid ? " · 지장물 회피" : "");
        PipeLog.Line($"[종단] «{route.Name}» {msg} · 지반 점 {b.Ground.Count}");
        return msg;
    }

    /// <summary>
    /// 종단의 PVI 를 새 목록으로 바꾼다.
    /// Civil 은 처음 · 끝 PVI 를 못 지우고 다 비운 종단에는 못 넣으므로(부지정지 1028 실측) 가운데만 지우고 양 끝을 옮긴 뒤 안쪽을 더한다.
    /// </summary>
    private static void ReplacePvis(Database db, ObjectId pid, List<(double S, double Z)> pvi)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var pr = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForWrite);
        if (pr.PVIs.Count < 2)
        {
            foreach (var (s, z) in pvi) pr.PVIs.AddPVI(s, z);
            tr.Commit();
            return;
        }
        for (int i = pr.PVIs.Count - 2; i >= 1; i--) pr.PVIs.RemoveAt(i);
        var first = pr.PVIs[0]; var last = pr.PVIs[pr.PVIs.Count - 1];
        var (ns0, nz0) = pvi[0]; var (ns1, nz1) = pvi[^1];
        if (ns0 >= last.RawStation || (ns1 < last.RawStation && ns1 > first.RawStation)) { last.RawStation = ns1; first.RawStation = ns0; }
        else { first.RawStation = ns0; last.RawStation = ns1; }
        first.Elevation = nz0;
        last.Elevation = nz1;
        for (int k = 1; k + 1 < pvi.Count; k++) pr.PVIs.AddPVI(pvi[k].S, pvi[k].Z);
        if (pr.PVIs.Count != pvi.Count) throw new InvalidOperationException($"종단 PVI 수 다름({pr.PVIs.Count}/{pvi.Count}) — 안 바꿈");
        tr.Commit();
    }

    /// <summary>가이드안(공기밸브 · 이토 · 제수밸브 · 소화전 자리). 손으로 넣은 부속은 남긴다.</summary>
    internal static string MakeGuide(Document doc, RouteData route, StringBuilder log)
    {
        var db = doc.Database;
        var b = RouteBuilder.Build(db, route);
        var manual = route.Appts.Where(a => !a.Auto).ToList();
        var auto = Guide.Make(b.Ctx.Profile, route);
        route.Appts = manual.Concat(auto).OrderBy(a => a.Sta).ToList();
        PipeStore.Save(db, route);
        var issues = ProfileCheck.Run(b.Ctx.Profile, b.Ground, route);
        foreach (var i in issues) log.AppendLine($"[{i.Kind}] No.{StationPlan.Name(i.StaFrom, route.Std.StationInterval)} {i.Text}");
        string msg = string.Join(" · ", Enum.GetValues<ApptKind>().Select(k => $"{Appurtenance.NameOf(k)} {auto.Count(a => a.Kind == k)}"));
        PipeLog.Line($"[가이드안] «{route.Name}» {msg} · 경고 {issues.Count}");
        return msg + (issues.Count > 0 ? $" · 경고 {issues.Count}" : "");
    }

    // ================================================================ ④ 구간
    /// <summary>[a, b] 를 새 공법 구간으로. 겹치는 옛 구간은 잘라 낸다.</summary>
    /// <summary>
    /// 새 경계가 이미 있는 경계(공법 · 관)에서 10 cm 안이면 그 자리에 맞춘다.
    /// 눈으로 «같은 점»을 찍어도 몇 mm ~ 몇 cm 어긋나는데, 그러면 같은 이름의 단면이 두 줄 나온다(2차 검토 낮음).
    /// </summary>
    internal static double SnapToBreak(RouteData route, double s)
    {
        double best = s, bestD = RouteEngine.NearBreak;
        void Try(double x) { double d = Math.Abs(x - s); if (d < bestD) { bestD = d; best = x; } }
        foreach (var m in route.Methods) { Try(m.StaFrom); Try(m.StaTo); }
        foreach (var r in route.Runs) { Try(r.StaFrom); Try(r.StaTo); }
        return best;
    }

    internal static void SetMethod(RouteData route, double a, double b, MethodSpan proto)
    {
        if (b < a) (a, b) = (b, a);
        a = SnapToBreak(route, a);
        b = SnapToBreak(route, b);
        if (b - a < 0.01) throw new InvalidOperationException("구간 길이 0");
        var keep = new List<MethodSpan>();
        foreach (var m in route.Methods)
        {
            if (m.StaTo <= a + 1e-6 || m.StaFrom >= b - 1e-6) { keep.Add(m); continue; }     // 안 겹침
            if (m.StaFrom < a - 1e-6) { var left = m.Clone(); left.StaTo = a; keep.Add(left); }
            if (m.StaTo > b + 1e-6) { var right = m.Clone(); right.StaFrom = b; keep.Add(right); }
        }
        var span = proto.Clone();
        span.StaFrom = a;
        span.StaTo = b;
        keep.Add(span);
        route.Methods = keep.OrderBy(m => m.StaFrom).ToList();
    }

    /// <summary>[a, b] 의 관을 바꾼다. 관 구간은 빈틈없이 이어지게 유지한다.</summary>
    internal static void SetRun(RouteData route, double a, double b, PipeSpec pipe, double length)
    {
        if (b < a) (a, b) = (b, a);
        if (route.Runs.Count == 0) route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = length, Pipe = PipeCatalog.Make("DCIP", 300) });
        a = Math.Max(0, SnapToBreak(route, a));
        b = Math.Min(length, SnapToBreak(route, b));
        if (b - a < 0.01) throw new InvalidOperationException("구간 길이 0");
        var keep = new List<PipeRun>();
        foreach (var r in route.Runs)
        {
            if (r.StaTo <= a + 1e-6 || r.StaFrom >= b - 1e-6) { keep.Add(r); continue; }
            if (r.StaFrom < a - 1e-6) keep.Add(new PipeRun { StaFrom = r.StaFrom, StaTo = a, Pipe = r.Pipe.Clone() });
            if (r.StaTo > b + 1e-6) keep.Add(new PipeRun { StaFrom = b, StaTo = r.StaTo, Pipe = r.Pipe.Clone() });
        }
        keep.Add(new PipeRun { StaFrom = a, StaTo = b, Pipe = pipe.Clone() });
        route.Runs = keep.OrderBy(r => r.StaFrom).ToList();
    }

    // ================================================================ ⑤ 도면
    internal static string DrawXsec(Document doc, RouteData route, Point3d at)
    {
        var db = doc.Database;
        var c = Compute(db, route);
        var secs = c.Sections();
        var keys = SectionDrawing.TableKeys(secs);
        var set = SectionDrawing.Sheets(secs, keys, route.Std.DepthLimit, route.Std.XsecScale, route.Name);
        using var tr = db.TransactionManager.StartTransaction();
        string g = CivilUtil.GroupName("XSEC", route.AlignHandle);
        int old = CivilUtil.EraseGroup(db, tr, g);
        var ids = DrawWriter.Write(db, tr, set.Draw, "횡단", at, route.Std.XsecScale);
        CivilUtil.MakeGroup(db, tr, g, ids, route.Name + " 횡단면도");
        tr.Commit();
        int bad = secs.Count(s => !s.Ok);
        string msg = $"횡단도 {set.Sheets}장 · 단면 {set.Cells}개" + (bad > 0 ? $" · 못 만든 단면 {bad}" : "") + (set.Notes.Count > 0 ? " · " + string.Join(" · ", set.Notes) : "")
            + (c.Notes.Count > 0 ? " · " + string.Join(" · ", c.Notes) : "");
        PipeLog.Line($"[횡단도] «{route.Name}» {msg} · 객체 {ids.Count}(옛 것 {old} 지움) · 축척 1:{route.Std.XsecScale:0}");
        return msg;
    }

    internal static string DrawProfileSheet(Document doc, RouteData route, Point3d at)
    {
        var db = doc.Database;
        var c = Compute(db, route);
        var input = new ProfileDrawing.Input
        {
            Title = route.Name, Ground = c.B.Ground, Line = c.B.Ctx.Profile, Route = route,
            Stations = c.Rows.Select(r => r.Sta).ToList(), Spans = c.Sum.Spans, Pipes = c.Sum.Pipes,
        };
        // 지층면 · 지하수위 면이 있으면 노선을 따라 떠서 지층선 · 수위선으로(없으면 «땅속 값»으로 그린다)
        foreach (var ly in c.B.Ctx.Strata.Where(x => x.Show)) input.Strata.Add((ly.Name, RouteEngine.SurfaceAlong(c.B.Ctx.Plan, ly.Tin)));
        if (c.B.Ctx.WaterTin != null) input.Water = RouteEngine.SurfaceAlong(c.B.Ctx.Plan, c.B.Ctx.WaterTin);
        // 장마다 측점 구간 하나(A1 그림 자리에 맞춤 — 도면화가 이 장을 그대로 본다)
        var set = ProfileDrawing.Sheets(input);
        using var tr = db.TransactionManager.StartTransaction();
        string g = CivilUtil.GroupName("PROF", route.AlignHandle);
        int old = CivilUtil.EraseGroup(db, tr, g);
        var ids = DrawWriter.Write(db, tr, set.Draw, "종단", at, route.Std.ProfileHScale);
        CivilUtil.MakeGroup(db, tr, g, ids, route.Name + " 종단면도");
        tr.Commit();
        string msg = $"종단도면 {set.Sheets}장 · 측점 {c.Rows.Count} · PVI {c.B.Ctx.Profile.Pvi.Count}"
            + (c.B.Ctx.Strata.Count > 0 ? $" · 지층면 {c.B.Ctx.Strata.Count}" : "") + (set.Notes.Count > 0 ? " · " + string.Join(" · ", set.Notes) : "");
        PipeLog.Line($"[종단도면] «{route.Name}» {msg} · 객체 {ids.Count}(옛 것 {old} 지움)");
        return msg;
    }

    // ================================================================ ⑥ 수량 · 3D
    internal static string ExportXlsx(Document doc, RouteData route, string path)
    {
        var c = Compute(doc.Database, route);
        WriteXlsx(route, c, path);
        string msg = $"터파기 {c.Earth.ExcTotal:#,##0.00} ㎥ · 되메우기 {c.Earth.BackfillTotal:#,##0.00} ㎥ · 잔토 {c.Earth.Surplus:#,##0.00} ㎥"
            + (c.Earth.MissingIntervals > 0 ? $" · 못 센 구간 {c.Earth.MissingIntervals}" : "")
            + (c.Notes.Count > 0 ? " · " + string.Join(" · ", c.Notes) : "");
        PipeLog.Line($"[수량] «{route.Name}» {msg} → {path}");
        return msg;
    }

    /// <summary>엑셀 라이브러리는 이 함수가 불릴 때만 올라온다(시작 때 안 올림 — 부지정지와 같은 방식).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WriteXlsx(RouteData route, Calc c, string path)
    {
        QtyWorkbook.Write(new QtyWorkbook.Input
        {
            Title = route.Name, Version = PipeSettings.ProductName + " " + PipeSettings.Version, Route = route,
            Earth = c.Earth, Sum = c.Sum, Obstacles = c.Obst, Issues = c.Issues, Rows = c.Rows,
        }, path);
    }

    internal static string Make3D(Document doc, RouteData route)
    {
        var db = doc.Database;
        var c = Compute(db, route);
        // 수량 측점은 곡선의 시작 · 끝과 20 m 마다뿐이라 3D 가 곡선을 가로질러 간다 → 3D 에만 꺾임 10° 마다 고리를 더 넣는다
        var rows3 = c.Rows;
        var extra = c.B.Ctx.Plan.CurveStations(10.0);
        if (extra.Count > 0 && c.Rows.Count >= 2) rows3 = RouteEngine.BuildRows(c.B.Ctx, c.Rows[0].Sta, c.Rows[^1].Sta, extra);
        var meshes = Mesh3D.Build(c.B.Ctx, rows3, c.Sum, c.B.Ground);
        using var tr = db.TransactionManager.StartTransaction();
        string g = CivilUtil.GroupName("3D", route.AlignHandle);
        int old = CivilUtil.EraseGroup(db, tr, g);
        var ids = DrawWriter.WriteMeshes(db, tr, meshes);
        CivilUtil.MakeGroup(db, tr, g, ids, route.Name + " 3D");
        tr.Commit();
        string msg = $"3D 객체 {ids.Count}개 · 면 {meshes.Sum(m => m.F.Count)}개";
        PipeLog.Line($"[3D] «{route.Name}» {msg}(옛 것 {old} 지움) · " + string.Join(" ", meshes.GroupBy(m => m.Key).Select(x => $"{x.Key} {x.Count()}")));
        return msg;
    }

    // ================================================================ 평면 표시(지장물 · 부속)
    /// <summary>평면에 지장물 · 부속 자리를 표시한다(다시 부르면 옛 표시를 지우고).</summary>
    internal static int DrawMarks(Document doc, RouteData route)
    {
        var db = doc.Database;
        var b = RouteBuilder.Build(db, route, needProfile: false);
        using var tr = db.TransactionManager.StartTransaction();
        string g = CivilUtil.GroupName("MARK", route.AlignHandle);
        CivilUtil.EraseGroup(db, tr, g);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        ObjectId lay = CivilUtil.EnsureLayer(db, tr, PipeLayers.Mark, 6);
        ObjectId style = CivilUtil.EnsureTextStyle(db, tr);
        var ids = new ObjectIdCollection();
        void Put(Entity e) { e.LayerId = lay; ids.Add(ms.AppendEntity(e)); tr.AddNewlyCreatedDBObject(e, true); }
        // 크기는 평면도 축척(종단 가로 축척과 같게 쓴다)의 종이 mm 로 — 1:1000 이면 글자 2.2 mm = 2.2 m
        double f = Math.Max(1, route.Std.ProfileHScale) / 1000.0;
        var plan = b.Ctx.Plan;
        // 노선 방향으로 눕힌 글(거꾸로 서지 않게 뒤집음). side: 왼쪽(+) · 오른쪽(−)으로 띄울 거리
        void Along(double sta, double side, double h, string text)
        {
            var p = plan.PointAt(sta);
            var r = plan.RightAt(sta);
            double ang = Math.Atan2(r.X, -r.Y);          // 진행 방향 = 오른쪽 직각을 왼쪽으로 90° 돌린 것
            var q = new Point3d(p.X - r.X * side, p.Y - r.Y * side, 0);
            if (ang > Math.PI / 2 + 1e-9 || ang < -Math.PI / 2 - 1e-9) ang += Math.PI;
            var t = new DBText { TextString = text, Height = h, TextStyleId = style, Rotation = ang, Position = q, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = q };
            Put(t);
            try { t.AdjustAlignment(db); } catch { }
        }
        void Tick(double sta, double half)
        {
            var p = plan.PointAt(sta);
            var r = plan.RightAt(sta);
            Put(new Line(new Point3d(p.X - r.X * half, p.Y - r.Y * half, 0), new Point3d(p.X + r.X * half, p.Y + r.Y * half, 0)));
        }
        // 공법 구간(왼쪽) · 관(오른쪽) 이름표
        foreach (var m in route.Methods)
        {
            double a0 = Math.Max(b.S0, m.StaFrom), a1 = Math.Min(b.S1, m.StaTo);
            if (a1 - a0 < 1e-6) continue;
            Tick(a0, 4 * f);
            Tick(a1, 4 * f);
            Along(0.5 * (a0 + a1), 6 * f, 2.2 * f, $"{MethodSpan.NameOf(m.Kind)} L={a1 - a0:0.0}m");
        }
        foreach (var run in route.Runs)
        {
            double a0 = Math.Max(b.S0, run.StaFrom), a1 = Math.Min(b.S1, run.StaTo);
            if (a1 - a0 < 1e-6) continue;
            Along(a0 + 0.25 * (a1 - a0), -6 * f, 2.2 * f, $"{run.Pipe.Name} L={a1 - a0:0.0}m");
        }
        foreach (var o in route.Obstacles)
        {
            var p = plan.PointAt(o.Sta);
            Put(new Circle(new Point3d(p.X, p.Y, 0), Vector3d.ZAxis, Math.Max(1.0 * f, o.Width * 0.5)));
            Put(new DBText { Position = new Point3d(p.X + 1.6 * f, p.Y + 1.6 * f, 0), Height = 2.0 * f, TextString = o.Kind, TextStyleId = style });
        }
        foreach (var a in route.Appts)
        {
            var p = plan.PointAt(a.Sta);
            var r = plan.RightAt(a.Sta);
            string tag = a.Kind switch { ApptKind.AirValve => "AV", ApptKind.Drain => "DV", ApptKind.GateValve => "GV", _ => "FH" };
            var q = new Point3d(p.X - r.X * 12 * f, p.Y - r.Y * 12 * f, 0);
            Put(new Line(new Point3d(p.X, p.Y, 0), q));
            Put(new Circle(q, Vector3d.ZAxis, 2.2 * f));
            var t = new DBText { TextString = tag, Height = 1.8 * f, TextStyleId = style, Position = q, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = q };
            Put(t);
            try { t.AdjustAlignment(db); } catch { }
        }
        CivilUtil.MakeGroup(db, tr, g, ids, route.Name + " 표시");
        tr.Commit();
        return ids.Count;
    }
}
