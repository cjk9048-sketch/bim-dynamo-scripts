using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DH.Pipe.Core.Geom;
using DH.Pipe.Core.Model;
using DH.Pipe.Core.Profile;
using DH.Pipe.Core.Quantity;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Pipe.Civil;

/// <summary>레이어 이름(부지정지 «DH-» 와 안 겹치게 «DHP-»).</summary>
internal static class PipeLayers
{
    internal const string Align = "DHP-노선";
    internal const string Profile = "DHP-종단";
    internal const string View = "DHP-종단뷰";
    internal const string ZoneAsp = "DHP-구역-아스콘";
    internal const string ZoneCon = "DHP-구역-콘크리트";
    internal const string ZoneBlk = "DHP-구역-보도블록";
    internal const string Curb = "DHP-연석";
    internal const string Obst = "DHP-지장물";
    internal const string Mark = "DHP-표시";
    internal const string Mesh = "DHP-3D-";

    internal static string ZoneLayer(SurfaceKind k) => k switch
    {
        SurfaceKind.Asphalt => ZoneAsp,
        SurfaceKind.Concrete => ZoneCon,
        SurfaceKind.Block => ZoneBlk,
        _ => "",
    };

    internal static short ZoneColor(SurfaceKind k) => k switch { SurfaceKind.Asphalt => 8, SurfaceKind.Concrete => 9, SurfaceKind.Block => 30, _ => 7 };
}

/// <summary>도면에서 읽어 만든 계산 자료 한 벌.</summary>
internal sealed class Built
{
    public RouteContext Ctx { get; set; } = new();
    public Pl Ground { get; set; } = Pl.Const(0, 1, 0);
    /// <summary>선형의 시작 측점(Civil 측점 = 이 값 + 우리 측점).</summary>
    public double StaBase { get; set; }
    /// <summary>선형의 끝 측점(Civil 측점).</summary>
    public double StaEnd { get; set; }
    public ObjectId AlignId { get; set; }
    public ObjectId SurfId { get; set; }
    public string AlignName { get; set; } = "";
    public List<string> Notes { get; } = new();
    public double S0 => Ground.X0;
    public double S1 => Ground.X1;
}

/// <summary>도면(Civil 선형 · 지표면 · 종단 · 구역 레이어) → 엔진 입력.</summary>
internal static class RouteBuilder
{
    // ---------------------------------------------------------------- 원지반 사본(지표면마다 한 번 읽어 둔다)
    private sealed record TinKey(int Tris, double MinZ, double MaxZ, TinIndex Tin);
    private static readonly Dictionary<ObjectId, TinKey> TinCache = new();
    /// <summary>처리기를 단 도면들(포인터 값으로 — 같은 도면의 포장 객체가 여러 개여도 한 번만 달리게).</summary>
    private static readonly HashSet<IntPtr> Hooked = new();
    private static bool _docHooked;

    /// <summary>
    /// 지표면이 고쳐지거나 지워지면 읽어 둔 사본을 버린다.
    /// (세모 수 · 높이 범위만 견주면 변 뒤집기 · 안쪽 점 고치기 뒤에도 옛 지형으로 셈한다 — 1127 검토 높음)
    /// </summary>
    private static void Hook(Database db)
    {
        if (Hooked.Add(db.UnmanagedObject))
        {
            db.ObjectModified += (_, e) => { try { if (TinCache.Count > 0) TinCache.Remove(e.DBObject.ObjectId); } catch { } };
            db.ObjectErased += (_, e) => { try { if (TinCache.Count > 0) TinCache.Remove(e.DBObject.ObjectId); } catch { } };
        }
        if (_docHooked) return;
        _docHooked = true;
        try
        {
            AcadApp.DocumentManager.DocumentToBeDestroyed += (_, e) =>
            {
                try
                {
                    IntPtr gone = e.Document.Database.UnmanagedObject;
                    foreach (var id in TinCache.Keys.Where(k => k.Database == null || k.Database.UnmanagedObject == gone).ToList()) TinCache.Remove(id);
                    Hooked.Remove(gone);
                }
                catch { }
            };
        }
        catch { }
    }

    /// <summary>도면의 TIN 지표면들(체적 면은 TinSurface 가 아니라서 들어오지 않는다).</summary>
    internal static List<(ObjectId Id, string Name)> Surfaces(Database db)
    {
        var res = new List<(ObjectId, string)>();
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        using var tr = db.TransactionManager.StartTransaction();
        foreach (ObjectId sid in cdoc.GetSurfaceIds())
        {
            try
            {
                if (tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface t) continue;
                // 부지정지의 지층면 · 지하수위 면은 원지반 후보가 아니다(원지반이 하나인데 «여럿 → 고르기»가 뜨지 않게)
                string nm = t.Name ?? "";
                if (nm.StartsWith(StrataPrefix, StringComparison.Ordinal) || nm == WaterSurface) continue;
                res.Add((sid, nm));
            }
            catch { }
        }
        tr.Commit();
        return res;
    }

    /// <summary>노선이 쓸 원지반: 저장된 것 → 없으면 하나뿐인 것. 여럿인데 안 골랐으면 Null(count 로 알림).</summary>
    internal static ObjectId FindSurface(Database db, RouteData route, out int count)
    {
        var all = Surfaces(db);
        count = all.Count;
        var id = CivilUtil.FromHandle(db, route.SurfaceHandle);
        if (!id.IsNull && all.Any(s => s.Id == id)) return id;
        return all.Count == 1 ? all[0].Id : ObjectId.Null;
    }

    /// <summary>지표면의 세모를 읽어 사본을 만든다. 같은 면은 다시 안 읽는다(고쳐지면 버림).</summary>
    internal static TinIndex ReadTin(Database db, ObjectId surfId)
    {
        Hook(db);
        using var tr = db.TransactionManager.StartTransaction();
        var tin = (CivilDb.TinSurface)tr.GetObject(surfId, OpenMode.ForRead);
        int n = 0;
        double minZ = 0, maxZ = 0;
        try
        {
            n = tin.GetTinProperties().NumberOfTriangles;
            var gp = tin.GetGeneralProperties();
            minZ = gp.MinimumElevation;
            maxZ = gp.MaximumElevation;
        }
        catch { }
        if (TinCache.TryGetValue(surfId, out var hit) && hit.Tris == n && n > 0 && Math.Abs(hit.MinZ - minZ) < 1e-9 && Math.Abs(hit.MaxZ - maxZ) < 1e-9)
        {
            tr.Commit();
            return hit.Tin;
        }
        var list = new List<(P3, P3, P3)>(Math.Max(16, n));
        using (var tris = tin.GetTriangles(false))
        {
            foreach (CivilDb.TinSurfaceTriangle t in tris)
            {
                var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                list.Add((new P3(a.X, a.Y, a.Z), new P3(b.X, b.Y, b.Z), new P3(c.X, c.Y, c.Z)));
            }
        }
        tr.Commit();
        if (list.Count == 0) throw new InvalidOperationException("원지반에 세모가 없음");
        var idx = new TinIndex(list);
        TinCache[surfId] = new TinKey(n, minZ, maxZ, idx);
        return idx;
    }

    /// <summary>
    /// 선형 → 평면 꺾은선. 직선은 양 끝만(꺾인 점이 정확히 들어감) · 곡선 · 완화곡선은 1 m 마다 나누되
    /// «곡선 안의 점»으로 표시해 꺾인 점 · 곡관 · 측점으로 세어지지 않게 한다(1127 검토 높음).
    /// </summary>
    internal static PlanLine ReadPlan(CivilDb.Alignment al)
    {
        var stas = new SortedSet<double> { al.StartingStation, al.EndingStation };
        var curves = new List<(double A, double B)>();
        int n = al.Entities.Count;
        for (int i = 0; i < n; i++)
        {
            var e = al.Entities.GetEntityByOrder(i);
            for (int j = 0; j < e.SubEntityCount; j++)
            {
                var sub = e[j];
                double a = sub.StartStation, b = sub.EndStation;
                stas.Add(a);
                stas.Add(b);
                if (sub.SubEntityType == CivilDb.AlignmentSubEntityType.Line) continue;
                curves.Add((Math.Min(a, b), Math.Max(a, b)));
                int parts = Math.Max(2, (int)Math.Ceiling(Math.Abs(b - a) / 1.0));
                for (int k = 1; k < parts; k++) stas.Add(a + (b - a) * k / parts);
            }
        }
        var pts = new List<P2>();
        var kinds = new List<int>();
        foreach (double s in stas)
        {
            if (s < al.StartingStation - 1e-9 || s > al.EndingStation + 1e-9) continue;
            double x = 0, y = 0;
            al.PointLocation(Math.Clamp(s, al.StartingStation, al.EndingStation), 0, ref x, ref y);
            pts.Add(new P2(x, y));
            // 0 = 꺾인 점(직선 끝) · 1 = 곡선 안 · 2 = 곡선의 시작/끝
            kinds.Add(curves.Any(c => s > c.A + 1e-6 && s < c.B - 1e-6) ? 1 : curves.Any(c => s >= c.A - 1e-6 && s <= c.B + 1e-6) ? 2 : 0);
        }
        return new PlanLine(pts, kinds);
    }

    /// <summary>Civil 종단(레이아웃)의 PVI → 종단선. 측점은 우리 것(선형 시작 = 0)으로. curved = 종단 곡선이 들어 있는지.</summary>
    internal static ProfileLine ReadProfile(Database db, string handle, double staBase, out bool curved)
    {
        curved = false;
        var id = CivilUtil.FromHandle(db, handle);
        if (id.IsNull) return new ProfileLine();
        using var tr = db.TransactionManager.StartTransaction();
        var list = new List<(double, double)>();
        if (tr.GetObject(id, OpenMode.ForRead) is CivilDb.Profile pr)
        {
            for (int i = 0; i < pr.PVIs.Count; i++)
            {
                var p = pr.PVIs[i];
                list.Add((p.RawStation - staBase, p.Elevation));
            }
            try
            {
                foreach (CivilDb.ProfileEntity e in pr.Entities)
                    if (e.EntityType != CivilDb.ProfileEntityType.Tangent) { curved = true; break; }
            }
            catch { }
        }
        tr.Commit();
        return list.Count >= 2 ? new ProfileLine(list) : new ProfileLine();
    }

    /// <summary>지표 구역(레이어의 닫힌 폴리선) · 연석 선을 읽는다. 구역 레이어가 하나도 없으면 모형 공간을 훑지 않는다.</summary>
    internal static void ReadZones(Database db, RouteContext c)
    {
        c.Zones.Clear();
        c.Curbs.Clear();
        using var tr = db.TransactionManager.StartTransaction();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var kinds = new Dictionary<ObjectId, SurfaceKind?>();
        var names = new List<string>();
        void Want(string name, SurfaceKind? k) { if (lt.Has(name)) { kinds[lt[name]] = k; names.Add(name); } }
        Want(PipeLayers.ZoneAsp, SurfaceKind.Asphalt);
        Want(PipeLayers.ZoneCon, SurfaceKind.Concrete);
        Want(PipeLayers.ZoneBlk, SurfaceKind.Block);
        Want(PipeLayers.Curb, null);
        if (kinds.Count == 0) { tr.Commit(); return; }
        foreach (ObjectId id in ZoneCandidates(db, tr, names))
        {
            if (id.IsNull || id.IsErased) continue;
            string dxf = id.ObjectClass.DxfName;
            if (dxf != "LWPOLYLINE" && dxf != "POLYLINE" && dxf != "LINE") continue;
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
            if (!kinds.TryGetValue(ent.LayerId, out var kind)) continue;
            var pts = CivilUtil.PolyPoints(tr, id, out bool closed);
            if (kind != null)
            {
                if (pts.Count >= 3) c.Zones.Add(new ZonePoly { Kind = kind.Value, Pts = pts });
            }
            else if (pts.Count >= 2)
            {
                if (closed) pts.Add(pts[0]);
                c.Curbs.Add(pts);
            }
        }
        tr.Commit();
    }

    internal const string StrataPrefix = "DH_지층_", WaterSurface = "DH_지하수위";

    /// <summary>
    /// 부지정지의 지층 기능이 만든 지층 지표면(«DH_지층_n_이름» = n번 층의 윗면)과 지하수위 면(«DH_지하수위»)을 읽는다.
    /// 암 분류는 지표면 설명란에 적힌 것(«DH_ROCK=Soft» …)을 쓰고, 없으면 층 이름으로 어림한다.
    /// 면은 원지반처럼 사본을 만들어 두고(고쳐지면 버림) 단면 선 · 노선을 따라 뜬다.
    /// </summary>
    internal static void ReadStrata(Database db, RouteContext c, ObjectId groundId, List<string> notes)
    {
        c.Strata.Clear();
        c.WaterTin = null;
        var found = new List<(int Order, string Name, int Rock, bool Show, ObjectId Id)>();
        bool oldBase = false, newBase = false;   // 옛 지층면(«DH_SVER» 가 2 미만 — n번 면 = n번 층의 아랫면) / 지금 방식(윗면)
        ObjectId water = ObjectId.Null;
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId sid in cdoc.GetSurfaceIds())
            {
                try
                {
                    if (sid == groundId || tr.GetObject(sid, OpenMode.ForRead) is not CivilDb.TinSurface t) continue;
                    string nm = t.Name ?? "";
                    if (nm == WaterSurface) { water = sid; continue; }
                    if (!nm.StartsWith(StrataPrefix, StringComparison.Ordinal)) continue;
                    string rest = nm.Substring(StrataPrefix.Length);
                    int us = rest.IndexOf('_');
                    if (us <= 0 || !int.TryParse(rest.Substring(0, us), out int ord)) continue;
                    string name = rest.Substring(us + 1);
                    int rock = -1;
                    string desc = "";
                    try { desc = t.Description ?? ""; } catch { }
                    int at = desc.IndexOf("DH_ROCK=", StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        string v = desc.Substring(at + "DH_ROCK=".Length);
                        int semi = v.IndexOf(';');
                        rock = StrataSurface.RockOfClass(semi >= 0 ? v.Substring(0, semi) : v);
                    }
                    if (rock < 0) rock = StrataSurface.RockOfName(name);
                    // 선을 그릴지: 부지정지 [지층]에서 고른 값(«DH_SHOW=1/0») · 없으면 맨 위 층(지반과 같은 면)만 안 그림
                    bool show = ord > 1;
                    int sh = desc.IndexOf("DH_SHOW=", StringComparison.Ordinal);
                    if (sh >= 0 && sh + 8 < desc.Length) show = desc[sh + 8] == '1' && ord > 1;
                    // 만든 방식: «DH_SVER=2» = 층의 윗면(지금) · 없거나 1 = 옛 아랫면 기준(부지정지 0831 이전)
                    int ver = 1;
                    int vt = desc.IndexOf("DH_SVER=", StringComparison.Ordinal);
                    if (vt >= 0) { string v = desc.Substring(vt + 8); int semi = v.IndexOf(';'); int.TryParse(semi >= 0 ? v.Substring(0, semi) : v, out ver); }
                    if (ver < 2) oldBase = true; else newBase = true;
                    found.Add((ord, name, rock, show, sid));
                }
                catch { }
            }
            tr.Commit();
        }
        foreach (var f in found.OrderBy(x => x.Order))
        {
            try { c.Strata.Add(new StrataSurface { Order = f.Order, Name = f.Name, Rock = f.Rock, Show = f.Show, Tin = ReadTin(db, f.Id) }); }
            catch (System.Exception ex) { notes.Add($"지층면 «{f.Name}» 못 읽음 — {ex.Message}"); }
        }
        if (oldBase && newBase)
        {
            // 만든 방식 표식이 있는 면과 없는 면이 섞여 있다(손으로 만든 면 · 지우다 만 옛 면) — 밀지 않고 지금 방식으로 읽는다(재검토 낮음)
            notes.Add("지층면 일부에 만든 방식 표식 없음 — 위 기준으로 읽음 → 부지정지 [지층]에서 [확인] 다시");
        }
        else if (oldBase && c.Strata.Count > 0)
        {
            // 옛 면은 층의 아랫면이다 → 윗면 기준으로 바꿔 읽는다(그대로 읽으면 암 분류가 한 층씩 밀린다)
            var top = StrataSurface.FromBottomBased(c.Strata, c.Tin);
            c.Strata.Clear();
            c.Strata.AddRange(top);
            notes.Add("옛 지층면(층 아래 기준)을 위 기준으로 바꿔 읽음 → 부지정지 [지층]에서 [확인]을 다시 누르면 새 방식");
        }
        if (!water.IsNull)
        {
            try { c.WaterTin = ReadTin(db, water); }
            catch (System.Exception ex) { notes.Add("지하수위 면 못 읽음 — " + ex.Message); }
        }
    }

    /// <summary>
    /// 구역 · 연석 레이어에 있는 선 · 폴리선 후보.
    /// 빠른 길 = 레이어로 걸러 고르기(도면의 선을 하나하나 열지 않는다 — 수치지도처럼 선이 수십만 개인 도면에서 명령마다 느려지지 않게).
    /// 못 쓰면(문서 없음 · 고르기 실패 · 고른 것 없음) 모형 공간을 훑는다 — 결과는 같다.
    /// </summary>
    private static IEnumerable<ObjectId> ZoneCandidates(Database db, Transaction tr, List<string> layerNames)
    {
        try
        {
            var doc = AcadApp.DocumentManager.GetDocument(db);
            if (doc != null)
            {
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE,LINE"),
                    new TypedValue((int)DxfCode.LayerName, string.Join(",", layerNames)),
                    new TypedValue((int)DxfCode.LayoutName, "Model"),
                });
                var r = doc.Editor.SelectAll(filter);
                if (r.Status == PromptStatus.OK && r.Value != null)
                {
                    SayZonePath("레이어로 고르기");
                    return r.Value.GetObjectIds();
                }
                SayZonePath("모형 공간 훑기(고르기 결과 " + r.Status + ")");
            }
            else SayZonePath("모형 공간 훑기(문서 없음)");
        }
        catch (System.Exception ex) { SayZonePath("모형 공간 훑기(" + ex.GetType().Name + ")"); }
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        return ms.Cast<ObjectId>().ToList();
    }

    private static string _zonePath = "";
    /// <summary>구역을 어느 길로 읽는지 — 바뀔 때만 한 줄 적는다(진단용).</summary>
    private static void SayZonePath(string how)
    {
        if (how == _zonePath) return;
        _zonePath = how;
        PipeLog.Line("[구역] 읽는 길 = " + how);
    }

    /// <summary>계산 자료를 만든다. 못 만들면 까닭을 적어 던진다.</summary>
    internal static Built Build(Database db, RouteData route, bool needProfile = true)
    {
        var b = new Built();
        b.AlignId = CivilUtil.FromHandle(db, route.AlignHandle);
        if (b.AlignId.IsNull) throw new InvalidOperationException("노선 없음 → [노선 만들기]");
        b.SurfId = FindSurface(db, route, out int count);
        if (b.SurfId.IsNull) throw new InvalidOperationException(count == 0 ? "원지반 없음 → 원지반 먼저" : "원지반 여럿 → [지표면 고르기]");
        PlanLine plan;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var al = (CivilDb.Alignment)tr.GetObject(b.AlignId, OpenMode.ForRead);
            b.StaBase = al.StartingStation;
            b.StaEnd = al.EndingStation;
            b.AlignName = al.Name;
            plan = ReadPlan(al);
            tr.Commit();
        }
        var tin = ReadTin(db, b.SurfId);
        var ground = RouteEngine.GroundAlong(plan, tin, b.Notes) ?? throw new InvalidOperationException("노선이 원지반 밖");
        b.Ground = ground;
        b.Ctx = new RouteContext { Route = route, Plan = plan, Tin = tin };
        b.Ctx.Profile = ReadProfile(db, route.PipeProfileHandle, b.StaBase, out bool curved);
        if (needProfile && b.Ctx.Profile.IsEmpty) throw new InvalidOperationException("종단 없음 → [처음 종단]");
        if (curved) b.Notes.Add("종단 곡선은 꺾은선(PVI)으로 봄");
        if (Math.Abs(b.StaBase) > 1e-6) b.Notes.Add($"선형 시작 측점 {b.StaBase:0.00} — 관로 측점은 시작 = 0 기준");
        if (!b.Ctx.Profile.IsEmpty && (b.Ctx.Profile.S0 > b.S0 + 0.5 || b.Ctx.Profile.S1 < b.S1 - 0.5))
            b.Notes.Add($"종단이 노선보다 짧음({b.Ctx.Profile.S0:0.0} ~ {b.Ctx.Profile.S1:0.0}) — 그 범위만 셈");
        ReadZones(db, b.Ctx);
        ReadStrata(db, b.Ctx, b.SurfId, b.Notes);
        // 관 구간이 비었으면 기본 관으로 전체
        if (route.Runs.Count == 0) route.Runs.Add(new PipeRun { StaFrom = 0, StaTo = plan.Length, Pipe = PipeCatalog.Make("DCIP", 300) });
        return b;
    }
}
