using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v115 · JACK 1105 Revit 구조물 · 계획 5.2 · D3 · D4 · D11 ~ D13] 구조물 그리기 — 평면 외곽 · 종단뷰 · 횡단뷰 자른 면(옹벽 단면 꼴 · <see cref="WallSectionDraw"/>).
/// <list type="bullet">
/// <item>종단 □R CR-STRU-NEWC · CR-PATT · CR-LEAL · CR-LEAT / 횡단 □C CC-GSTR-NEWC · CC-PATT · CC-LEAL · CC-LEAT(있는 레이어는 안 바꿈) · 평면 <see cref="StructImport.LayPlan"/>.</item>
/// <item>윤곽 = 원래 변만(뷰 네모에 붙은 자른 변은 안 그림) · ANSI31 해치 = H = V 뷰 + 해치 칠 부재(빼는 이름 — 계단 · 난간 · 문 · 창 …) · 이름 인출 = 덩어리마다.</item>
/// <item>꼬리표 XData <see cref="App"/> [종류 · 뷰 핸들 · 구조물 번호] — 손으로 그린 CALS 구조물은 안 지운다. 평면(PLAN)은 [다시 반영] · [구조물 지우기]만 지운다(검토 M1).</item>
/// </list></summary>
internal static class StructDraw
{
    internal const string App = "DH_STRUCTDRAW";
    internal const string KindPlan = "PLAN", KindProf = "PROF", KindXsec = "XSEC";
    /// <summary>★[v116.5 · JACK 1110] Civil 기본 투영용 도면 안 3D 솔리드(<see cref="StructSolids"/>) — 평면 · 종단 · 횡단 장 내용 아님(모형 객체).</summary>
    internal const string KindSolid = "SOLID";
    /// <summary>★[D13 스위치] 구조물 바닥까지 뷰 표고 범위를 넓힌다(끄면 «잘림 n»만 남김 — JACK이 싫다 하면).</summary>
    internal static bool Widen = true;
    internal const double HatchGapMm = 2.0, JoinTol = 0.002, CloseEps = 0.005, BottomPad = 0.5;
    /// <summary>축척 · 넓힘 알림 — 그리는 쪽이 명령줄에 한 번 내고 비운다.</summary>
    internal static readonly List<string> Notes = new();
    /// <summary>[다시 반영] 동안 건드리지 않을 구조물(원본 없음).</summary>
    static readonly HashSet<int> Skip = new();
    /// <summary>[다시 반영] 알림의 «잘림» 셈(측점 · 표고).</summary>
    static int ClipU, ClipZ;

    // ── 꼬리표 ─────────────────────────────

    static void Tag(Database db, Transaction tr, Entity e, string kind, string view, int id)
    {
        GradingBuilder.EnsureRegApp(db, tr, App);
        e.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                                   new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind),
                                   new TypedValue((int)DxfCode.ExtendedDataAsciiString, view),
                                   new TypedValue((int)DxfCode.ExtendedDataInteger32, id));
    }

    internal static void TagSolid(Database db, Transaction tr, Entity e, int id, string key) => Tag(db, tr, e, KindSolid, key, id);

    /// <summary>꼬리표가 맞는 우리 그림 — 자리 · 뷰 칸(<paramref name="pred"/>(종류, 뷰, 번호)).</summary>
    internal static List<(ObjectId Id, string View)> FindTagged(Database db, Transaction tr, System.Func<string, string, int, bool> pred)
    {
        var o = new List<(ObjectId, string)>();
        if (!((RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead)).Has(App)) return o;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            try { if (tr.GetObject(id, OpenMode.ForRead) is Entity e && TagOf(e, out string k, out string v, out int sid) && pred(k, v, sid)) o.Add((id, v)); } catch { }
        return o;
    }
    internal static ObjectId EnsureLayer(Database db, Transaction tr, string name, short aci) => Ensure(db, tr, name, aci);

    internal static bool TagOf(Entity e, out string kind, out string view, out int id)
    {
        kind = view = ""; id = 0;
        try
        {
            using var rb = e.GetXDataForApplication(App);
            if (rb == null) return false;
            var a = rb.AsArray();
            if (a.Length > 1) kind = a[1].Value as string ?? "";
            if (a.Length > 2) view = a[2].Value as string ?? "";
            if (a.Length > 3) id = System.Convert.ToInt32(a[3].Value);
            return true;
        }
        catch { return false; }
    }

    /// <summary>우리 구조물 그림을 지운다(모아 두고 밖에서 · 잠긴 레이어여도). <paramref name="pred"/>(종류, 뷰, 번호, 도로 표식).</summary>
    internal static int EraseTagged(Database db, Transaction tr, System.Func<string, string, int, bool, bool> pred)
    {
        if (!((RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead)).Has(App)) return 0;   // 그린 적이 없는 도면 — 모형을 훑지 않음
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var kill = new List<ObjectId>();
        foreach (ObjectId id in ms)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity e && TagOf(e, out string k, out string v, out int sid) && pred(k, v, sid, Commands.SheetTag.IsRoad(e))) kill.Add(id);
            }
            catch { }
        }
        int n = 0;
        foreach (var id in kill) try { tr.GetObject(id, OpenMode.ForWrite, false, true).Erase(); n++; } catch { }
        return n;
    }

    /// <summary>부지 [종단도] · [횡단도] · 초기화가 지울 때 — 종단 · 횡단 그림만(평면은 안 지움 · 검토 M1) · 진입도로 표식은 건너뜀. <paramref name="kind"/> null = 둘 다.</summary>
    internal static int EraseSiteSheets(Database db, Transaction tr, string? kind)
        => EraseTagged(db, tr, (k, _, _, road) => !road && k != KindPlan && k != KindSolid && (kind == null || k == kind));

    // ── 커튼 ─────────────────────────────

    /// <summary>노선 [a, b] → 커튼(현 오차 ≤ 2 mm · 곧은 곳은 끝점만 — 5 m 토막을 반씩 나누며 사분점 · 가운데 어긋남을 잰다). u = 측점.</summary>
    internal static StructSlice.Curtain? AlignCurtain(CivilDb.Alignment al, double a, double b)
    {
        if (!(b > a)) return null;
        bool At(double s, out double x, out double y) { x = y = 0; try { al.PointLocation(s, 0, ref x, ref y); return true; } catch { return false; } }
        var S = new List<double>(); var X = new List<double>(); var Y = new List<double>();
        void Rec(double s0, double x0, double y0, double s1, double x1, double y1, int depth)
        {
            double dx = x1 - x0, dy = y1 - y0, l = System.Math.Sqrt(dx * dx + dy * dy);
            double dev = 0;
            foreach (double f in new[] { 0.25, 0.5, 0.75 })
            {
                if (!At(s0 + (s1 - s0) * f, out double xm, out double ym)) continue;
                double d = l < 1e-9 ? System.Math.Sqrt((xm - x0) * (xm - x0) + (ym - y0) * (ym - y0)) : System.Math.Abs((xm - x0) * dy - (ym - y0) * dx) / l;
                dev = System.Math.Max(dev, d);
            }
            if (dev > 0.002 && depth < 24 && s1 - s0 > 1e-3)
            {
                double sm = (s0 + s1) / 2;
                if (At(sm, out double xm, out double ym)) { Rec(s0, x0, y0, sm, xm, ym, depth + 1); Rec(sm, xm, ym, s1, x1, y1, depth + 1); return; }
            }
            S.Add(s1); X.Add(x1); Y.Add(y1);
        }
        if (!At(a, out double xa, out double ya)) return null;
        S.Add(a); X.Add(xa); Y.Add(ya);
        int n = System.Math.Max(1, (int)System.Math.Ceiling((b - a) / 5.0));
        double sp = a, xp = xa, yp = ya;
        for (int i = 1; i <= n; i++)
        {
            double s = i == n ? b : a + (b - a) * i / n;
            if (!At(s, out double x, out double y)) continue;
            Rec(sp, xp, yp, s, x, y, 0);
            sp = s; xp = x; yp = y;
        }
        // 길이 0 토막 빼기
        var c = new StructSlice.Curtain();
        var xs = new List<double>(); var ys = new List<double>(); var us = new List<double>();
        for (int i = 0; i < S.Count; i++)
        {
            if (xs.Count > 0 && (System.Math.Abs(X[i] - xs[^1]) + System.Math.Abs(Y[i] - ys[^1]) < 1e-9 || S[i] <= us[^1])) continue;
            xs.Add(X[i]); ys.Add(Y[i]); us.Add(S[i]);
        }
        if (xs.Count < 2) return null;
        c.X = xs.ToArray(); c.Y = ys.ToArray(); c.U = us.ToArray();
        return c;
    }

    /// <summary>표본선 꼭짓점(왼 → 오른) → 커튼(u = 이격 · 끝없이 늘임). 꼭짓점이 한 줄이 아니면 null(건너뛰고 셈).</summary>
    internal static StructSlice.Curtain? SampleCurtain(CivilDb.Alignment al, CivilDb.SampleLine sl)
    {
        var pts = new List<(double X, double Y)>();
        foreach (CivilDb.SampleLineVertex v in sl.Vertices) pts.Add((v.Location.X, v.Location.Y));
        if (pts.Count < 2) return null;
        double st = 0, o0 = 0, o1 = 0;
        al.StationOffset(pts[0].X, pts[0].Y, ref st, ref o0);
        al.StationOffset(pts[^1].X, pts[^1].Y, ref st, ref o1);
        if (o0 > o1) { pts.Reverse(); (o0, o1) = (o1, o0); }
        var a = pts[0]; var b = pts[^1];
        double l = System.Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        if (l < 1e-6) return null;
        foreach (var p in pts) if (System.Math.Abs((p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X)) / l > 0.01) return null;
        return StructSlice.Curtain.Of(new List<(double, double)> { a, b }, o0, extend: true);
    }

    /// <summary>횡단뷰 격자 범위(이격 · 표고) — 뷰 상자 모서리를 되돌려 읽는다(ElevationMin/Max는 자료 범위라 안 씀 — XsecViewCommand 함정).</summary>
    internal static bool XsecGrid(CivilDb.SectionView sv, out double o0, out double o1, out double e0, out double e1)
    {
        o0 = o1 = e0 = e1 = 0;
        try
        {
            var ex = ((Entity)sv).GeometricExtents;
            if (!sv.FindOffsetAndElevationAtXY(ex.MinPoint.X, ex.MinPoint.Y, ref o0, ref e0) || !sv.FindOffsetAndElevationAtXY(ex.MaxPoint.X, ex.MaxPoint.Y, ref o1, ref e1)) return false;
            if (o0 > o1) (o0, o1) = (o1, o0);
            if (e0 > e1) (e0, e1) = (e1, e0);
            return o1 > o0 && e1 > e0;
        }
        catch { return false; }
    }

    // ── 레이어 ─────────────────────────────

    sealed class Lays { public ObjectId Line, Patt, Leal, Leat; }

    static ObjectId Ensure(Database db, Transaction tr, string name, short aci)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        return lt.Has(name) ? lt[name] : Commands.SectionCommand.EnsureLayer(db, tr, name, aci);
    }

    static Lays LaysOf(Database db, Transaction tr, bool prof) => prof
        ? new Lays { Line = Ensure(db, tr, "CR-STRU-NEWC", 4), Patt = Ensure(db, tr, "CR-PATT", 1), Leal = Ensure(db, tr, "CR-LEAL", 1), Leat = Ensure(db, tr, "CR-LEAT", 3) }
        : new Lays { Line = Ensure(db, tr, "CC-GSTR-NEWC", 4), Patt = Ensure(db, tr, "CC-PATT", 1), Leal = Ensure(db, tr, "CC-LEAL", 1), Leat = Ensure(db, tr, "CC-LEAT", 3) };

    // ── 한 뷰에 한 단면 ─────────────────────────────

    sealed class Ctx
    {
        public Database Db = null!; public Transaction Tr = null!; public BlockTableRecord Ms = null!;
        public Lays L = null!; public ObjectId Kst; public double N, TxtH; public string Kind = "", View = "", Road = ""; public int Id;
        public int Ents, Hatches, HatchFail, Labels;
        public string HatchWhy = "";
    }

    static void Put(Ctx c, Entity e, ObjectId lay)
    {
        e.SetDatabaseDefaults(c.Db);
        e.LayerId = lay;
        Tag(c.Db, c.Tr, e, c.Kind, c.View, c.Id);
        c.Ms.AppendEntity(e); c.Tr.AddNewlyCreatedDBObject(e, true);
        if (c.Road.Length > 0) Commands.SheetTag.Put(c.Db, c.Tr, e, c.Road, c.View, c.Kind == KindXsec ? Commands.SheetTag.KindXsec : e.Layer);
        c.Ents++;
    }

    static void Pl(Ctx c, IReadOnlyList<(double U, double Z)> pts, bool closed, System.Func<double, double, Point2d> map, ObjectId lay)
    {
        var pl = new Polyline();
        int k = 0;
        foreach (var p in pts) { var q = map(p.U, p.Z); if (k > 0 && (q - pl.GetPoint2dAt(k - 1)).Length < 1e-9) continue; pl.AddVertexAt(k++, q, 0, 0, 0); }
        if (k < 2) { pl.Dispose(); return; }
        pl.Closed = closed && k >= 3;
        Put(c, pl, lay);
    }

    /// <summary>해치 패턴 간격(ANSI31 — acadiso 3.175 · acad 0.125).</summary>
    static double PatSpacing(Database db) => db.Measurement == MeasurementValue.Metric ? 3.175 : 0.125;

    static void DrawHatch(Ctx c, NetTopologySuite.Geometries.Polygon p, System.Func<double, double, Point2d> map)
    {
        Hatch? h = null;
        string step = "new";
        try
        {
            // ★ 지적도 해치(ImportGisCommand — 화면 없는 Civil에서 됨)와 같은 차례: 붙이고 → 무늬 → 축척(무늬를 다시 걸어야 먹음) → 고리(겹친 끝점 없이 닫음) → 계산 → 꼬리표
            h = new Hatch { LayerId = c.L.Patt, Associative = false };
            // ★[v117.3 · JACK 1111 «구조물 내부 해치가 빨간색이라 너무 헷갈려 · 주황색 같은 거»] 구조물 단면 해치만 주황(ACI 30) — 레이어(CR/CC-PATT · 다른 해치와 같이 씀) 색은 안 바꿈
            h.ColorIndex = 30;
            c.Ms.AppendEntity(h); c.Tr.AddNewlyCreatedDBObject(h, true);
            step = "pattern";
            h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
            h.PatternAngle = 0;
            h.PatternScale = HatchGapMm * c.N / 1000.0 / PatSpacing(c.Db);
            h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
            void Loop(NetTopologySuite.Geometries.Coordinate[] co, HatchLoopTypes t)
            {
                var pc = new Point2dCollection();
                for (int i = 0; i + 1 < co.Length; i++) { var q = map(co[i].X, co[i].Y); if (pc.Count == 0 || (q - pc[pc.Count - 1]).Length > 1e-9) pc.Add(q); }
                if (pc.Count < 3) return;
                pc.Add(pc[0]);
                var bc = new DoubleCollection(); for (int i = 0; i < pc.Count; i++) bc.Add(0);
                h!.AppendLoop(t, pc, bc);
            }
            step = "loop";
            Loop(p.Shell.Coordinates, HatchLoopTypes.External | HatchLoopTypes.Polyline);
            foreach (var hr in p.Holes) Loop(hr.Coordinates, HatchLoopTypes.Default | HatchLoopTypes.Polyline);
            step = "evaluate";
            h.EvaluateHatch(true);
            step = "tag";
            Tag(c.Db, c.Tr, h, c.Kind, c.View, c.Id);
            if (c.Road.Length > 0) Commands.SheetTag.Put(c.Db, c.Tr, h, c.Road, c.View, c.Kind == KindXsec ? Commands.SheetTag.KindXsec : h.Layer);
            c.Hatches++; c.Ents++;
        }
        catch (System.Exception ex)
        {
            c.HatchFail++;
            if (c.HatchWhy.Length == 0) c.HatchWhy = $"{step} {ex.GetType().Name}: {ex.Message} · 축척 {HatchGapMm * c.N / 1000.0 / PatSpacing(c.Db):0.####}";
            try { if (h != null && !h.IsErased && h.ObjectId.IsValid) h.Erase(); } catch { }
        }
    }

    static void Label(Ctx c, double u, double z, string name, System.Func<double, double, Point2d> map)
    {
        double t = c.TxtH;
        var A = map(u, z);
        var P1 = new Point2d(A.X + 1.2 * t, A.Y + 2.2 * t);
        double w = name.Length * t * 0.95 + 0.6 * t;
        var P2 = new Point2d(P1.X + w, P1.Y);
        var ld = new Polyline(); ld.AddVertexAt(0, A, 0, 0, 0); ld.AddVertexAt(1, P1, 0, 0, 0); ld.AddVertexAt(2, P2, 0, 0, 0);
        Put(c, ld, c.L.Leal);
        var dv = A - P1; double dl = dv.Length;
        if (dl > 1e-9)
        {
            var uu = dv / dl; var nn = new Vector2d(-uu.Y, uu.X); var b = A - uu * (0.9 * t);
            Put(c, new Solid(new Point3d(A.X, A.Y, 0), new Point3d(b.X + nn.X * 0.3 * t, b.Y + nn.Y * 0.3 * t, 0), new Point3d(b.X - nn.X * 0.3 * t, b.Y - nn.Y * 0.3 * t, 0)), c.L.Leal);
        }
        var tx = new DBText { TextString = name, Height = t, HorizontalMode = TextHorizontalMode.TextLeft, VerticalMode = TextVerticalMode.TextBottom };
        tx.SetDatabaseDefaults(c.Db);
        if (!c.Kst.IsNull) tx.TextStyleId = c.Kst;
        tx.AlignmentPoint = new Point3d(P1.X + 0.3 * t, P1.Y + 0.25 * t, 0);
        Put(c, tx, c.L.Leat);
        c.Labels++;
    }

    /// <summary>자른 결과 하나를 뷰에 — 윤곽 · 열린 선 · 해치(H = V일 때) · 이름.</summary>
    static void DrawCut(Ctx c, StructSlice.Cutout cut, System.Func<double, double, Point2d> map, bool hv, string name)
    {
        foreach (var (pts, closed) in cut.Lines) Pl(c, pts, closed, map, c.L.Line);
        foreach (var ln in cut.OpenLines) Pl(c, ln, false, map, c.L.Line);
        if (hv) foreach (var p in cut.Hatch) DrawHatch(c, p, map);
        for (int i = 0; i < cut.All.Count && i < cut.LabelAt.Count; i++)
            if (cut.All[i].Area >= 0.05) Label(c, cut.LabelAt[i].U, cut.LabelAt[i].Z, name, map);
    }

    static bool IsHV(System.Func<double, double, Point2d> map, double a, double z)
    {
        var p0 = map(a, z); var p1 = map(a + 1, z); var p2 = map(a, z + 1);
        double h = (p1 - p0).Length, v = (p2 - p0).Length;
        return h > 1e-9 && System.Math.Abs(v / h - 1) < 1e-3;
    }

    static void Flush(Autodesk.AutoCAD.EditorInput.Editor? ed)
    {
        if (Notes.Count == 0) return;
        foreach (var n in Notes.Distinct()) try { ed?.WriteMessage("\n  · " + n); } catch { }
        Notes.Clear();
    }

    static Autodesk.AutoCAD.EditorInput.Editor? Ed() { try { return Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument?.Editor; } catch { return null; } }

    // ── 평면 ─────────────────────────────

    /// <summary>★[D3] 평면 외곽 + 이름(덩어리가 여럿이면 «이름-1 …») — 이 구조물 평면 것을 지우고 다시.</summary>
    internal static int DrawPlan(Database db, Transaction tr, StructImport.Loaded L, System.Text.StringBuilder? log)
    {
        EraseTagged(db, tr, (k, _, id, _) => k == KindPlan && id == L.Rec.Id);
        var pcs = StructPoly.PlanOutline(L.Model, out string note);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        var lay = Ensure(db, tr, StructImport.LayPlan, 4);
        var kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr);
        double sc = Commands.SheetCommand.CurrentDrawingScale(db);
        double th = (sc > 0 ? sc : 500) * 2.5 / 1000.0;
        int n = 0;
        void Add(Entity e) { e.SetDatabaseDefaults(db); e.LayerId = lay; Tag(db, tr, e, KindPlan, "", L.Rec.Id); ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); n++; }
        void Ring(List<(double X, double Y)> r)
        {
            var pl = new Polyline();
            for (int i = 0; i < r.Count; i++) pl.AddVertexAt(i, new Point2d(r[i].X, r[i].Y), 0, 0, 0);
            pl.Closed = true; Add(pl);
        }
        for (int i = 0; i < pcs.Count; i++)
        {
            Ring(pcs[i].Shell);
            foreach (var h in pcs[i].Holes) Ring(h);
            if (pcs[i].Area < 1.0 && pcs.Count > 1) continue;   // 작은 조각엔 이름을 안 붙임
            var t = new DBText { TextString = pcs.Count > 1 ? $"{L.Rec.Name}-{i + 1}" : L.Rec.Name, Height = th, HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid };
            t.SetDatabaseDefaults(db);
            if (!kst.IsNull) t.TextStyleId = kst;
            t.AlignmentPoint = new Point3d(pcs[i].Lx, pcs[i].Ly, 0);
            Add(t);
        }
        log?.AppendLine($"  [구조물] {L.Rec.Name} 평면 — {note} · 객체 {n}");
        return pcs.Count;
    }

    // ── 종단 ─────────────────────────────

    /// <summary>★[계획 5.3] 종단뷰 하나 — 이 뷰의 구조물 그림을 지우고 다시(불러 둔 xref 그대로 · 다시 싣지 않음). <paramref name="n"/> = 해치 간격 · 글자 축척(1:N) ·
    /// <paramref name="road"/> = 진입도로 노선 핸들(빈 값 = 표식 안 붙임 — 도로 장 짓는 동안은 RoadSheets가 붙인다). 그린 단면 수.</summary>
    internal static int IntoProfile(Database db, ObjectId pvId, double n, System.Text.StringBuilder? log, string road = "")
    {
        if (!StructImport.Any(db)) return 0;
        var all = StructImport.LoadAll(db, log, out var warn).Where(x => !Skip.Contains(x.Rec.Id)).ToList();
        foreach (var w in warn) log?.AppendLine("  [구조물] " + w);
        if (all.Count == 0) return 0;
        int drawn = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(pvId, OpenMode.ForRead) is not CivilDb.ProfileView pv || tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is not CivilDb.Alignment al) { tr.Commit(); return 0; }
            string vh = pvId.Handle.ToString();
            var ids = all.Select(x => x.Rec.Id).ToHashSet();
            EraseTagged(db, tr, (k, v, id, _) => k == KindProf && v == vh && ids.Contains(id));
            double a = pv.StationStart, b = pv.StationEnd, z0 = pv.ElevationMin, z1 = pv.ElevationMax;
            var cur = AlignCurtain(al, a, b);
            var map = WallSectionDraw.Affine(a, b, z0, z1, (s, z) => { double x = 0, y = 0; return pv.FindXYAtStationAndElevation(s, z, ref x, ref y) ? new Point2d(x, y) : (Point2d?)null; });
            if (cur == null || map == null) { tr.Commit(); log?.AppendLine("  [구조물] 종단뷰 좌표를 못 잼"); return 0; }
            bool hv = IsHV(map, (a + b) / 2, (z0 + z1) / 2);
            var c = new Ctx { Db = db, Tr = tr, Ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite), L = LaysOf(db, tr, true), Kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr), N = n > 0 ? n : 100, Kind = KindProf, View = vh, Road = road };
            c.TxtH = WallSectionDraw.LabelMm / 1000.0 * c.N;
            foreach (var L in all)
            {
                if (StructImport.LooksChanged(db, tr, L.Rec, L.Btr)) Notes.Add($"⚠원본이 바뀜: {L.Rec.Name} — [구조물 불러오기]로 같은 파일을 다시 고르세요");
                c.Id = L.Rec.Id;
                var cut = StructSlice.Cut(L.Model, cur, new StructSlice.Window(a, b, z0, z1), JoinTol, CloseEps);
                ClipU += cut.ClipU; ClipZ += cut.ClipZ;
                log?.AppendLine($"  [구조물] {L.Rec.Name} 종단 {pv.Name} — {cut.Line()}{(hv ? "" : " · 과장 뷰(해치 없음)")}");
                if (cut.All.Count == 0 && cut.OpenLines.Count == 0) continue;
                DrawCut(c, cut, map, hv, L.Rec.Name);
                drawn++;
            }
            if (c.HatchFail > 0) log?.AppendLine($"  [구조물] ⚠해치 실패 {c.HatchFail}(윤곽만) — {c.HatchWhy}");
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  [구조물] 종단 그리기 실패 — " + ex.Message); }
        Flush(Ed());
        return drawn;
    }

    // ── 횡단 ─────────────────────────────

    /// <summary>★[계획 5.3] 횡단뷰들 — 뷰마다 이 뷰 구조물 그림을 지우고 다시. <paramref name="n"/> = 고른 횡단 축척. 그린 뷰 수.</summary>
    internal static int IntoSections(Database db, IEnumerable<ObjectId> views, double n, System.Text.StringBuilder? log, string road = "")
    {
        if (!StructImport.Any(db)) return 0;
        var all = StructImport.LoadAll(db, log, out var warn).Where(x => !Skip.Contains(x.Rec.Id)).ToList();
        foreach (var w in warn) log?.AppendLine("  [구조물] " + w);
        if (all.Count == 0) return 0;
        int drawnViews = 0, skipLine = 0, clipU = 0, clipZ = 0, cuts = 0;
        long ms = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ids = all.Select(x => x.Rec.Id).ToHashSet();
            var c = new Ctx { Db = db, Tr = tr, Ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite), L = LaysOf(db, tr, false), Kst = Commands.ImportGisCommand.EnsureKoreanTextStyle(db, tr), N = n > 0 ? n : 200, Kind = KindXsec, Road = road };
            c.TxtH = WallSectionDraw.LabelMm / 1000.0 * c.N;
            // ★[검토 중간] 이 뷰들의 옛 그림을 한 번에(뷰마다 모형을 훑지 않게)
            var vlist = views.ToList();
            var vhs = vlist.Select(v => v.Handle.ToString()).ToHashSet();
            EraseTagged(db, tr, (k, v, id, _) => k == KindXsec && vhs.Contains(v) && ids.Contains(id));
            foreach (var vid in vlist)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    if (tr.GetObject(sv.SampleLineId, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                    if (tr.GetObject(((CivilDb.SampleLineGroup)tr.GetObject(sl.GroupId, OpenMode.ForRead)).ParentAlignmentId, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    string vh = vid.Handle.ToString();
                    var cur = SampleCurtain(al, sl);
                    if (cur == null) { skipLine++; continue; }
                    if (!XsecGrid(sv, out double o0, out double o1, out double e0, out double e1)) continue;
                    var map = WallSectionDraw.Affine(o0, o1, e0, e1, (o, z) => { double x = 0, y = 0; return sv.FindXYAtOffsetAndElevation(o, z, ref x, ref y) ? new Point2d(x, y) : (Point2d?)null; });
                    if (map == null) continue;
                    bool hv = IsHV(map, (o0 + o1) / 2, (e0 + e1) / 2);
                    c.View = vh;
                    bool any = false;
                    foreach (var L in all)
                    {
                        c.Id = L.Rec.Id;
                        var cut = StructSlice.Cut(L.Model, cur, new StructSlice.Window(o0, o1, e0, e1), JoinTol, CloseEps);
                        ms += cut.Ms; clipU += cut.ClipU; clipZ += cut.ClipZ; ClipU += cut.ClipU; ClipZ += cut.ClipZ;
                        if (cut.All.Count == 0 && cut.OpenLines.Count == 0) continue;
                        DrawCut(c, cut, map, hv, L.Rec.Name);
                        cuts++; any = true;
                        if (cut.OpenParts > 0 || !cut.AreaOk) log?.AppendLine($"  [구조물] {L.Rec.Name} 횡단 {sl.Name} — {cut.Line()}");
                    }
                    if (any) drawnViews++;
                }
                catch (System.Exception exV) { log?.AppendLine("  [구조물] 횡단뷰 하나 못 그림 — " + exV.Message); }
            }
            tr.Commit();
            log?.AppendLine($"  [구조물] 횡단 {drawnViews}장 · 단면 {cuts} · 해치 {c.Hatches}{(c.HatchFail > 0 ? $"(⚠실패 {c.HatchFail} — {c.HatchWhy})" : "")} · 이름 {c.Labels}"
                          + (clipU + clipZ > 0 ? $" · 잘림 이격 {clipU} · 표고 {clipZ}" : "") + (skipLine > 0 ? $" · 꺾인 표본선 건너뜀 {skipLine}" : "") + $" · {ms} ms");
        }
        catch (System.Exception ex) { log?.AppendLine("  [구조물] 횡단 그리기 실패 — " + ex.Message); }
        Flush(Ed());
        return drawnViews;
    }

    // ── D13 범위 ─────────────────────────────

    /// <summary>뷰마다 구조물 뺀 데이터 범위(축척 알림용).</summary>
    static readonly Dictionary<ObjectId, (double Lo, double Hi)> Bare = new();

    /// <summary>★[D13] 종단 표고 범위 — 구조물 바닥 − 0.5 · 윗면 + 0.5까지(부지 = 노선 전체 · 도로 = 그 장 측점). <see cref="Commands.SheetCommand"/> ElevDataRange 안에서만.</summary>
    internal static void ExtendProfileRange(Database db, ObjectId pvId, bool road, ref double lo, ref double hi)
    {
        Bare.Remove(pvId);
        if (!Widen || lo > hi || !StructImport.Any(db)) return;
        var all = StructImport.LoadAll(db, null, out _);
        if (all.Count == 0) return;
        double l0 = lo, h0 = hi;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(pvId, OpenMode.ForRead) is CivilDb.ProfileView pv && tr.GetObject(pv.AlignmentId, OpenMode.ForRead) is CivilDb.Alignment al)
            {
                double a = road ? pv.StationStart : al.StartingStation, b = road ? pv.StationEnd : al.EndingStation;
                var cur = AlignCurtain(al, a, b);
                if (cur != null)
                    foreach (var L in all)
                    {
                        var (bot, top) = StructSlice.BottomAlong(L.Model, cur, a, b);
                        if (double.IsNaN(bot)) continue;
                        lo = System.Math.Min(lo, bot - BottomPad); hi = System.Math.Max(hi, top + BottomPad);
                    }
            }
            tr.Commit();
        }
        catch { }
        if (lo < l0 - 1e-9 || hi > h0 + 1e-9) Bare[pvId] = (l0, h0);
    }

    /// <summary>이 뷰가 구조물 때문에 넓어졌으면 구조물 뺀 범위.</summary>
    internal static bool BareRange(ObjectId pvId, out double lo, out double hi)
    {
        lo = hi = 0;
        if (!Bare.TryGetValue(pvId, out var b)) return false;
        lo = b.Lo; hi = b.Hi; return true;
    }

    /// <summary>★[D13 · 검토 낮음] 구조물 때문에 축척이 내려갔으면 한 줄(구조물 뺀 축척과 견줌) · 옛 도면 규칙이면 넘칠 수 있다고 한 줄.</summary>
    internal static void ScaleNote(string what, double h0, double v0, double h1, double v1, bool legacy, System.Text.StringBuilder? log)
    {
        string s;
        if (legacy) s = $"⚠옛 {what}도 규칙 — 구조물 바닥까지 넓힌 표고가 도곽을 넘칠 수 있음";
        else if (h0 > 0 && v0 > 0 && (System.Math.Abs(h0 - h1) > 1e-6 || System.Math.Abs(v0 - v1) > 1e-6))
            s = System.Math.Abs(h0 - v0) < 1e-6 && System.Math.Abs(h1 - v1) < 1e-6
                ? $"구조물 바닥 때문에 {what} 축척 1:{h0:0} → 1:{h1:0}"
                : $"구조물 바닥 때문에 {what} 축척 H 1:{h0:0} V 1:{v0:0} → H 1:{h1:0} V 1:{v1:0}";
        else return;
        log?.AppendLine("  " + s);
        if (!Notes.Contains(s)) Notes.Add(s);
    }

    /// <summary>★[D13] 횡단 — 뷰마다 구조물 바닥 − 0.5(윗면 + 0.5)까지 넓힌다. 넓힌 뷰만 «사용자 지정 범위»(자동 값을 먼저 읽어 위 여유를 지킴). <see cref="Commands.XsecViewCommand"/> MeasureViews 앞. 넓힌 뷰 수.</summary>
    internal static int WidenSections(Database db, IEnumerable<ObjectId> views, System.Text.StringBuilder? log)
    {
        if (!Widen || !StructImport.Any(db)) return 0;
        var all = StructImport.LoadAll(db, null, out _);
        if (all.Count == 0) return 0;
        int nW = 0; string first = "";
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var vid in views)
            {
                try
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is not CivilDb.SectionView sv) continue;
                    if (tr.GetObject(sv.SampleLineId, OpenMode.ForRead) is not CivilDb.SampleLine sl) continue;
                    if (tr.GetObject(((CivilDb.SampleLineGroup)tr.GetObject(sl.GroupId, OpenMode.ForRead)).ParentAlignmentId, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    var cur = SampleCurtain(al, sl);
                    if (cur == null || !XsecGrid(sv, out double o0, out double o1, out double g0, out double g1)) continue;
                    double bot = double.PositiveInfinity, top = double.NegativeInfinity;
                    foreach (var L in all)
                    {
                        var (b, t) = StructSlice.BottomAlong(L.Model, cur, o0, o1);
                        if (double.IsNaN(b)) continue;
                        bot = System.Math.Min(bot, b); top = System.Math.Max(top, t);
                    }
                    if (!double.IsFinite(bot) || (bot - BottomPad >= g0 && top + BottomPad <= g1)) continue;
                    double dMin = sv.ElevationMin, dMax = sv.ElevationMax;   // 자동(자료) 범위 — 바꾸기 전에 읽는다
                    double nMin = System.Math.Min(dMin, bot - BottomPad), nMax = System.Math.Max(dMax, top + BottomPad);
                    sv.UpgradeOpen();
                    sv.IsElevationRangeAutomatic = false;
                    sv.ElevationMin = nMin; sv.ElevationMax = nMax;
                    nW++;
                    if (first.Length == 0) first = $"{sl.Name}: 격자 {g0:F2}~{g1:F2} · 자료 {dMin:F2}~{dMax:F2} · 구조물 {bot:F2}~{top:F2} → {nMin:F2}~{nMax:F2}";
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { log?.AppendLine("  [구조물] 횡단 범위 넓히기 실패 — " + ex.Message); }
        if (nW > 0) log?.AppendLine($"  [구조물] 횡단 표고 범위를 구조물 바닥까지 넓힘 {nW}장(첫 {first})");
        return nW;
    }

    // ── 다시 반영 ─────────────────────────────

    /// <summary>★[계획 5.2 다시 반영] 바뀜 검사 · 다시 싣기(D10 — 명령 머리) → 다시 읽기에 성공한 구조물 것만 지움 → 평면 → 우리 종단뷰(부지 · 도로 장) · 횡단뷰(부지 · 도로) 모두. 알림 한 줄.</summary>
    internal static string Reflect(Database db, System.Text.StringBuilder log)
    {
        // ★[v116.9 · JACK 1110] [구조물 다시 반영] 없앰 — 지금은 [구조물 불러오기]만 부름(다시 싣기 없음 · 같은 파일 다시 고르기 = 바꿔치기)
        var missing = new HashSet<int>();
        var msgs = new List<string>();
        var all = StructImport.LoadAll(db, log, out var warn).Where(x => !missing.Contains(x.Rec.Id)).ToList();   // 원본 없음 = 지우지도 그리지도 않음
        msgs.AddRange(warn);
        if (all.Count == 0) return string.Join(" · ", msgs.DefaultIfEmpty("구조물 없음"));
        var ids = all.Select(x => x.Rec.Id).ToHashSet();
        var profs = new List<(ObjectId Id, double N, string Road)>(); var xsecs = new List<(ObjectId Id, double N, string Road)>();
        int nPlan = 0, nSolid = 0, nSolidFail = 0;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            EraseTagged(db, tr, (k, _, id, _) => ids.Contains(id) && k != KindSolid);   // ★[v116.5 · 검토 H1] Civil 투영용 솔리드는 바뀐 것만 다시(핸들 유지 — Civil 투영이 물고 있음)
            foreach (var L in all) nPlan += DrawPlan(db, tr, L, log);
            // ★[v116.5 · JACK 1110 «Civil 투영 기능에 맞추기»] Civil [객체를 횡단/종단 뷰에 투영]용 도면 안 3D 솔리드
            foreach (var L in all) { try { var (mk, fl) = StructSolids.Draw(db, tr, L, log); nSolid += mk; nSolidFail += fl; } catch (System.Exception exS) { log.AppendLine("  [구조물] 솔리드 못 만듦 — " + exS.Message); } }
            TryLoadHead(db, tr, out var head);
            double site = Commands.SheetCommand.CurrentDrawingScale(db);
            var cdoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
            ObjectId H(string h) { try { return h.Length > 0 && db.TryGetObjectId(new Handle(System.Convert.ToInt64(h, 16)), out var id) && !id.IsErased ? id : ObjectId.Null; } catch { return ObjectId.Null; } }
            foreach (ObjectId aid in cdoc.GetAlignmentIds())
            {
                try
                {
                    if (aid.IsErased || tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) continue;
                    var rd = RoadDesign.Read(tr, aid);
                    if (rd != null)
                    {
                        // ★[D11] 진입도로 = 종단도 장 · 횡단도만(실시간 설계 종단뷰는 안 그림)
                        foreach (var h in rd.SheetViews) { var id = H(h); if (!id.IsNull) profs.Add((id, rd.SheetScale, aid.Handle.ToString())); }
                        foreach (var h in rd.XsecViews) { var id = H(h); if (!id.IsNull) xsecs.Add((id, rd.XsecScale, aid.Handle.ToString())); }
                        continue;
                    }
                    if (!(al.Name ?? "").StartsWith(Commands.SectionCommand.AlignBase, System.StringComparison.Ordinal)) continue;
                    foreach (ObjectId pid in al.GetProfileViewIds()) profs.Add((pid, site, ""));
                    foreach (ObjectId gid in al.GetSampleLineGroupIds())
                    {
                        if (tr.GetObject(gid, OpenMode.ForRead) is not CivilDb.SampleLineGroup g || !Commands.XsecViewCommand.IsXsecGroupName(g.Name ?? "")) continue;
                        foreach (ObjectId sid in g.GetSampleLineIds())
                            if (tr.GetObject(sid, OpenMode.ForRead) is CivilDb.SampleLine sl) foreach (ObjectId vid in sl.GetSectionViewIds()) xsecs.Add((vid, head.SiteXsecScale > 0 ? head.SiteXsecScale : site, ""));
                    }
                }
                catch { }
            }
            tr.Commit();
        }
        catch (System.Exception ex) { msgs.Add("⚠다시 반영 실패 — " + ex.Message); }
        int nP = 0, nX = 0;
        Skip.Clear(); Skip.UnionWith(missing); ClipU = ClipZ = 0;
        try
        {
            foreach (var p in profs) if (IntoProfile(db, p.Id, p.N, log, p.Road) > 0) nP++;
            foreach (var grp in xsecs.GroupBy(x => (x.N, x.Road))) nX += IntoSections(db, grp.Select(g => g.Id), grp.Key.N, log, grp.Key.Road);
        }
        finally { Skip.Clear(); }
        int parts = all.Sum(x => x.Model.Parts.Count(p => !p.Excluded)), open = all.Sum(x => x.Model.OpenParts);
        string names = string.Join(", ", all.Select(x => x.Rec.Name));
        return $"{names} · 부재 {parts} · 평면 {nPlan} · 종단 {nP}장 · 횡단 {nX}장"
            + (nSolid > 0 ? $" · Civil 투영용 솔리드 {nSolid}(«{StructSolids.Layer}» — Civil [객체를 횡단/종단 뷰에 투영]에서 고르세요)" : "") + (nSolidFail > 0 ? $" · ⚠솔리드로 못 바꾼 메시 {nSolidFail}" : "") + (ClipU + ClipZ > 0 ? $" · 잘림 측점 {ClipU} · 표고 {ClipZ}" : "") + (open > 0 ? $" · ⚠열린 {open}" : "") + (msgs.Count > 0 ? " · " + string.Join(" · ", msgs) : "");
    }

    static void TryLoadHead(Database db, Transaction tr, out StructHead head)
    {
        if (!StructImport.TryLoad(db, tr, out head, out _, out _)) head = new StructHead();
    }

    /// <summary>부지 [횡단도]가 고른 축척을 적어 둔다([다시 반영]이 해치 간격 · 글자에 쓴다).</summary>
    internal static void RememberSiteXsecScale(Database db, double scale)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (StructImport.TryLoad(db, tr, out var head, out var recs, out _) && recs.Count > 0 && System.Math.Abs(head.SiteXsecScale - scale) > 1e-9)
            { head.SiteXsecScale = scale; StructImport.Save(db, tr, head, recs); }
            tr.Commit();
        }
        catch { }
    }
}
