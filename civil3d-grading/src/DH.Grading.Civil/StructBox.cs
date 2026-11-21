using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[v117.19 · JACK 1121 «초기 설계엔 Revit 모델이 없다 — 박스 구조물을 직접 배치해 개략 되메우기 · 토공»] 박스 구조물.
/// <para><b>새 소비 경로를 만들지 않는다</b> — Revit 구조물과 같은 기록(NOD DH_GRADING/STRUCTS · <see cref="StructRec"/>)에 «박스» 한 칸으로 들어간다.
/// 형상 = 도면 안 블록 «DH구조물_번호»(xref 대신) 안의 3D 솔리드 하나(외곽 폴리곤 × [바닥고, 바닥고 + 높이] · 속 빈 곳 없음) · 삽입은 xref와 같은 레이어(<see cref="StructImport.LayXref"/>).
/// 그래서 평면 외곽 · 종단 · 횡단 그림 · Civil 투영용 솔리드 · 수량(성토 · 되메우기 공제) · 3D 보기는 지금 있는 길(<see cref="StructImport.LoadAll"/> → <see cref="StructImport.ModelOf"/>)을 그대로 탄다.</para>
/// <para>표식 = <see cref="StructRec.IsBox"/>(Sha16 «box:» + 제원 해시 — 고치면 바뀌어 캐시 · 솔리드 열쇠가 새로 됨) · 제원(외곽 · 높이 · 바닥 · 깊이 방식)은 블록 정의 확장 사전 «DH_BOX» Xrecord — 도면 저장 · 다시 열기에 그대로.</para></summary>
internal static class StructBox
{
    const string XrecKey = "DH_BOX", XrecSign = "DH_BOX2", XrecSign1 = "DH_BOX1";   // 판 2 = 두께(벽 · 상판 · 바닥판) 더함 · 판 1(두께 없음) = 기본 두께로 읽음
    internal const double DefWall = 0.4, DefTop = 0.4, DefBot = 0.5;
    /// <summary>안 빈 칸이 이보다 좁거나(평면) 낮으면(높이) 두께를 줄임 — 그래도 안 되면 꽉 찬 상자.</summary>
    const double MinClear = 0.2;
    static readonly System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>박스 제원 — 외곽(정점 X · Y · 볼록 · 닫힘) · 높이 · 바닥고(EL) · 깊이 방식이면 «땅(원지반)에서 깊이».</summary>
    internal sealed class Spec
    {
        public string Name = "";
        public double Height = 3, Bottom = double.NaN;
        public bool ByDepth;
        public double Depth = 3;
        /// <summary>★[v117.19 · JACK 1121 «횡단에서 꽉 찬 네모 말고 속 빈 박스»] 벽체 · 상부 슬래브 · 바닥 슬래브 두께(m · 넣은 값 — 실제 쓴 값은 <see cref="Eff"/>).</summary>
        public double Wall = DefWall, Top = DefTop, Bot = DefBot;
        public List<(double X, double Y, double B)> Ring = new();
        public Spec Copy() => new() { Name = Name, Height = Height, Bottom = Bottom, ByDepth = ByDepth, Depth = Depth, Wall = Wall, Top = Top, Bot = Bot, Ring = new(Ring) };
    }

    /// <summary>실제 쓴 두께 · 안 빈 칸(평면 고리 · 넓이) · 알림(줄였으면).</summary>
    internal sealed class Hollow
    {
        public double Wall, Top, Bot;
        public List<(double X, double Y, double B)>? Inner;   // null = 꽉 찬 상자
        public double OuterArea, InnerArea, InnerH;
        public string Note = "";
        public double Concrete(double h) => OuterArea * h - (Inner != null ? InnerArea * InnerH : 0);
    }

    /// <summary>목록 한 줄(창).</summary>
    internal sealed class Row
    {
        public int Id;
        public Spec S = new();
        public double Area, Volume;
        /// <summary>콘크리트(벽 · 슬래브) 부피 ㎥ — 바깥 부피 − 안 빈 칸.</summary>
        public double Concrete;
        public Hollow H = new();
        public Extents3d Ext;
    }

    // ── 외곽 ─────────────────────────────

    /// <summary>닫힌 폴리선 → 외곽(끝 = 처음이면 뺌 · 볼록 그대로).</summary>
    internal static List<(double X, double Y, double B)> RingOf(Polyline pl)
    {
        var r = new List<(double, double, double)>();
        for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); r.Add((p.X, p.Y, pl.GetBulgeAt(i))); }
        if (r.Count > 2 && Near(r[0], r[^1])) r.RemoveAt(r.Count - 1);
        return r;
    }

    static bool Near((double X, double Y, double B) a, (double X, double Y, double B) b) => System.Math.Abs(a.X - b.X) < 1e-6 && System.Math.Abs(a.Y - b.Y) < 1e-6;

    static Polyline Poly(List<(double X, double Y, double B)> ring, double ox = 0, double oy = 0)
    {
        var pl = new Polyline();
        for (int i = 0; i < ring.Count; i++) pl.AddVertexAt(i, new Point2d(ring[i].X - ox, ring[i].Y - oy), ring[i].B, 0, 0);
        pl.Closed = true;
        return pl;
    }

    /// <summary>외곽 넓이(㎡ · 볼록 포함).</summary>
    internal static double Area(List<(double X, double Y, double B)> ring)
    {
        if (ring.Count < 3) return 0;
        try { using var pl = Poly(ring, ring[0].X, ring[0].Y); return System.Math.Abs(pl.Area); } catch { return 0; }
    }

    /// <summary>안쪽 고리 — 바깥 외곽을 안으로 <paramref name="t"/>만큼(두 방향 중 넓이가 작은 닫힌 것) · 못 하면 null.</summary>
    static List<(double X, double Y, double B)>? Inset(List<(double X, double Y, double B)> ring, double t, out double area)
    {
        area = 0;
        double ox = ring[0].X, oy = ring[0].Y;
        using var pl = Poly(ring, ox, oy);
        double a0 = System.Math.Abs(pl.Area);
        List<(double, double, double)>? best = null; double bestA = double.MaxValue;
        foreach (double d in new[] { t, -t })
        {
            DBObjectCollection? got = null;
            try { got = pl.GetOffsetCurves(d); } catch { }
            if (got == null) continue;
            try
            {
                if (got.Count != 1 || got[0] is not Polyline q || !q.Closed) continue;
                double a = System.Math.Abs(q.Area);
                if (!(a < a0 - 1e-6) || a < 1e-4 || a >= bestA) continue;
                var r = new List<(double, double, double)>();
                for (int i = 0; i < q.NumberOfVertices; i++) { var p = q.GetPoint2dAt(i); r.Add((p.X + ox, p.Y + oy, q.GetBulgeAt(i))); }
                if (r.Count > 2 && Near(r[0], r[^1])) r.RemoveAt(r.Count - 1);
                if (r.Count < 3) continue;
                best = r; bestA = a;
            }
            finally { foreach (DBObject o in got) o.Dispose(); }
        }
        // 안 빈 칸이 너무 좁은지 — 안쪽 고리를 다시 MinClear/2만큼 줄여 살아남아야 함
        if (best != null)
        {
            using var qi = Poly(best, ox, oy);
            bool ok = false;
            foreach (double d in new[] { MinClear / 2, -MinClear / 2 })
            {
                try
                {
                    var g = qi.GetOffsetCurves(d);
                    foreach (DBObject o in g) { if (o is Polyline z && z.Closed && System.Math.Abs(z.Area) < bestA - 1e-9 && System.Math.Abs(z.Area) > 1e-4) ok = true; o.Dispose(); }
                }
                catch { }
            }
            if (!ok) best = null;
        }
        if (best != null) area = bestA;
        return best;
    }

    /// <summary>★[v117.19 · JACK 1121] 실제 쓸 두께 — 박스에 비해 크면 줄임(벽: 반씩 4번 · 높이: 상판 + 바닥판 ≤ 높이 − 0.2 m가 되게 비율로) · 그래도 안 되면 꽉 찬 상자 + 알림.</summary>
    internal static Hollow Eff(Spec s)
    {
        var e = new Hollow { OuterArea = Area(s.Ring) };
        double w = System.Math.Max(0, s.Wall), t = System.Math.Max(0, s.Top), b = System.Math.Max(0, s.Bot);
        var notes = new List<string>();
        // 위아래
        double room = s.Height - MinClear;
        if (t + b > room)
        {
            double k = room > 0 ? room / (t + b) : 0;
            t = System.Math.Floor(t * k * 1000) / 1000; b = System.Math.Floor(b * k * 1000) / 1000;
            notes.Add(room > 0 ? $"상판 · 바닥판을 {t:0.###} · {b:0.###} m로 줄임(높이 {s.Height:0.##} m)" : "높이가 낮아 상판 · 바닥판 없이 꽉 찬 상자");
        }
        // 벽
        List<(double X, double Y, double B)>? inner = null; double ia = 0;
        if (w > 0 && room > 0 && t + b <= room + 1e-9)
        {
            double w0 = w;
            for (int i = 0; i < 5 && inner == null; i++)
            {
                try { inner = Inset(s.Ring, w, out ia); } catch { inner = null; }
                if (inner == null) w = System.Math.Floor(w * 0.5 * 1000) / 1000;
                if (w < 0.05) break;
            }
            if (inner == null) { notes.Add("외곽이 좁아 벽 두께를 못 넣음 — 꽉 찬 상자"); w = 0; }
            else if (w < w0 - 1e-9) notes.Add($"벽 두께를 {w:0.###} m로 줄임");
        }
        if (inner == null || room <= 0) { e.Wall = 0; e.Top = 0; e.Bot = 0; e.Inner = null; }
        else { e.Wall = w; e.Top = t; e.Bot = b; e.Inner = inner; e.InnerArea = ia; e.InnerH = s.Height - t - b; }
        e.Note = string.Join(" · ", notes);
        return e;
    }

    /// <summary>값 검사 — 틀리면 까닭(짧게) · 맞으면 null.</summary>
    internal static string? Check(Spec s, bool needRing = true)
    {
        if (!(s.Height > 0.05) || s.Height > 200) return "높이는 0.05 ~ 200 m";
        if (s.ByDepth) { if (!double.IsFinite(s.Depth) || s.Depth < -100 || s.Depth > 200) return "깊이는 −100 ~ 200 m"; }
        else if (!double.IsFinite(s.Bottom) || System.Math.Abs(s.Bottom) > 9000) return "바닥고(EL)를 넣으세요";
        if (!double.IsFinite(s.Wall) || !double.IsFinite(s.Top) || !double.IsFinite(s.Bot) || s.Wall < 0 || s.Top < 0 || s.Bot < 0 || s.Wall > 20 || s.Top > 20 || s.Bot > 20)
            return "두께는 0 ~ 20 m(0 = 그 부분 없음 · 벽 0 = 꽉 찬 상자)";
        if (needRing)
        {
            if (s.Ring.Count < 3) return "외곽 점이 셋 이상이어야 합니다";
            if (Area(s.Ring) < 0.01) return "외곽 넓이가 0입니다";
        }
        return null;
    }

    /// <summary>원지반 표고 — 외곽 범위 가운데 → 안 되면 정점들 평균(원지반 밖이면 NaN).</summary>
    internal static double GroundAt(Database db, List<(double X, double Y, double B)> ring, out string why)
    {
        why = "";
        var gid = Commands.ImportGisCommand.FindGroundSurface(db, out _, out int tris);
        if (gid.IsNull || tris <= 0) { why = "원지반이 없습니다 — 깊이 대신 바닥고(EL)를 넣으세요"; return double.NaN; }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var ts = (CivilDb.TinSurface)tr.GetObject(gid, OpenMode.ForRead);
            double x0 = ring.Min(p => p.X), x1 = ring.Max(p => p.X), y0 = ring.Min(p => p.Y), y1 = ring.Max(p => p.Y);
            var tries = new List<(double, double)> { ((x0 + x1) / 2, (y0 + y1) / 2), (ring.Average(p => p.X), ring.Average(p => p.Y)) };
            tries.AddRange(ring.Select(p => (p.X, p.Y)));
            foreach (var (x, y) in tries)
                try { double z = ts.FindElevationAtXY(x, y); if (double.IsFinite(z)) { tr.Commit(); return z; } } catch { }
            tr.Commit();
        }
        catch { }
        why = "외곽이 원지반 밖입니다 — 바닥고(EL)를 넣으세요";
        return double.NaN;
    }

    /// <summary>깊이 방식이면 바닥고를 원지반에서 셈 — 못 세면 까닭.</summary>
    internal static string? Resolve(Database db, Spec s)
    {
        if (!s.ByDepth) return null;
        double g = GroundAt(db, s.Ring, out string why);
        if (double.IsNaN(g)) return why;
        s.Bottom = System.Math.Round(g - s.Depth, 3);
        return null;
    }

    static string Sig(Spec s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(s.Height.ToString("R", IC)).Append('|').Append(s.Bottom.ToString("R", IC))
          .Append('|').Append(s.Wall.ToString("R", IC)).Append('|').Append(s.Top.ToString("R", IC)).Append('|').Append(s.Bot.ToString("R", IC));
        foreach (var p in s.Ring) sb.Append('|').Append(p.X.ToString("R", IC)).Append(',').Append(p.Y.ToString("R", IC)).Append(',').Append(p.B.ToString("R", IC));
        var h = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return StructRec.BoxPrefix + System.Convert.ToHexString(h).Substring(0, 12).ToLowerInvariant();
    }

    // ── 블록 ─────────────────────────────

    /// <summary>블록 안을 비우고 솔리드 하나 + 제원 Xrecord — 돌려줌 = 삽입점(첫 정점 · 바닥고). 국소 좌표로 지음(큰 좌표 ACIS 정밀 · StructSolids와 같은 까닭).</summary>
    static Point3d Build(Database db, Transaction tr, BlockTableRecord btr, Spec s, out Hollow hol)
    {
        hol = Eff(s);
        foreach (ObjectId id in btr) try { tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch { }
        double ox = s.Ring[0].X, oy = s.Ring[0].Y;
        using var pl = Poly(s.Ring, ox, oy);
        var regs = Region.CreateFromCurves(new DBObjectCollection { pl });
        Region? reg = null;
        try
        {
            foreach (DBObject o in regs) if (o is Region r && (reg == null || r.Area > reg.Area)) reg = r;
            if (reg == null || reg.Area < 0.01) throw new System.InvalidOperationException("외곽으로 면을 못 만듦(꼬인 선?)");
            var sol = new Solid3d();
            sol.Extrude(reg, s.Height, 0);
            // ★[v117.19] 속 빈 박스 — 안쪽 고리 × [바닥판, 높이 − 상판]을 뺌(닫힌 빈칸 — StructModel이 빈칸 껍질을 바깥 부재에 붙여 횡단 = 테두리 + 구멍 · 수량은 StructQty.Shells가 구멍을 메워 바깥 껍질)
            if (hol.Inner != null)
            {
                using var ipl = Poly(hol.Inner, ox, oy);
                var iregs = Region.CreateFromCurves(new DBObjectCollection { ipl });
                try
                {
                    Region? ir = null;
                    foreach (DBObject o in iregs) if (o is Region r && (ir == null || r.Area > ir.Area)) ir = r;
                    if (ir != null)
                    {
                        using var cav = new Solid3d();
                        cav.Extrude(ir, hol.InnerH, 0);
                        cav.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, hol.Bot)));
                        sol.BooleanOperation(BooleanOperationType.BoolSubtract, cav);
                    }
                }
                catch (System.Exception ex) { hol.Note = (hol.Note.Length > 0 ? hol.Note + " · " : "") + "속을 못 비움(꽉 찬 상자) — " + ex.Message; hol.Inner = null; }
                finally { foreach (DBObject o in iregs) o.Dispose(); }
            }
            double vol = 0; try { vol = sol.MassProperties.Volume; } catch { }
            if (!(vol > 1e-6)) { sol.Dispose(); throw new System.InvalidOperationException("솔리드를 못 만듦"); }
            sol.SetDatabaseDefaults(db);
            sol.Layer = "0";   // 삽입 레이어를 따름(StructImport.ReadSoup — 블록 안 0 레이어 = 삽입 레이어)
            btr.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
        }
        finally { foreach (DBObject o in regs) o.Dispose(); }

        if (btr.ExtensionDictionary.IsNull) btr.CreateExtensionDictionary();
        var dict = (DBDictionary)tr.GetObject(btr.ExtensionDictionary, OpenMode.ForWrite);
        var vals = new List<TypedValue>
        {
            new((int)DxfCode.Text, XrecSign), new((int)DxfCode.Real, s.Height), new((int)DxfCode.Real, s.Bottom),
            new((int)DxfCode.Int32, s.ByDepth ? 1 : 0), new((int)DxfCode.Real, s.Depth),
            new((int)DxfCode.Real, s.Wall), new((int)DxfCode.Real, s.Top), new((int)DxfCode.Real, s.Bot), new((int)DxfCode.Int32, s.Ring.Count),
        };
        foreach (var p in s.Ring) { vals.Add(new((int)DxfCode.Real, p.X)); vals.Add(new((int)DxfCode.Real, p.Y)); vals.Add(new((int)DxfCode.Real, p.B)); }
        if (dict.Contains(XrecKey)) { var old = (Xrecord)tr.GetObject(dict.GetAt(XrecKey), OpenMode.ForWrite); old.Data = new ResultBuffer(vals.ToArray()); }
        else { var xr = new Xrecord { Data = new ResultBuffer(vals.ToArray()) }; dict.SetAt(XrecKey, xr); tr.AddNewlyCreatedDBObject(xr, true); }
        return new Point3d(ox, oy, s.Bottom);
    }

    /// <summary>블록 정의의 제원(없으면 null).</summary>
    static Spec? ReadSpec(Transaction tr, BlockTableRecord btr)
    {
        try
        {
            if (btr.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(btr.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(XrecKey)) return null;
            var a = ((Xrecord)tr.GetObject(dict.GetAt(XrecKey), OpenMode.ForRead)).Data?.AsArray();
            string sign = a?.Length > 0 ? a[0].Value as string ?? "" : "";
            if (a == null || a.Length < 6 || (sign != XrecSign && sign != XrecSign1)) return null;
            var s = new Spec
            {
                Height = System.Convert.ToDouble(a[1].Value), Bottom = System.Convert.ToDouble(a[2].Value),
                ByDepth = System.Convert.ToInt32(a[3].Value) != 0, Depth = System.Convert.ToDouble(a[4].Value),
            };
            int k = 5;
            if (sign == XrecSign) { s.Wall = System.Convert.ToDouble(a[5].Value); s.Top = System.Convert.ToDouble(a[6].Value); s.Bot = System.Convert.ToDouble(a[7].Value); k = 8; }   // 판 1 = 기본 두께
            int n = System.Convert.ToInt32(a[k].Value);
            for (int i = 0; i < n && k + 1 + 3 * i + 2 < a.Length; i++)
                s.Ring.Add((System.Convert.ToDouble(a[k + 1 + 3 * i].Value), System.Convert.ToDouble(a[k + 2 + 3 * i].Value), System.Convert.ToDouble(a[k + 3 + 3 * i].Value)));
            return s;
        }
        catch { return null; }
    }

    static BlockReference? InsertOf(Database db, Transaction tr, StructRec r, OpenMode mode)
    {
        try
        {
            if (r.XrefHandle.Length > 0 && db.TryGetObjectId(new Handle(System.Convert.ToInt64(r.XrefHandle, 16)), out var iid) && !iid.IsErased)
                return tr.GetObject(iid, mode) as BlockReference;
        }
        catch { }
        return null;
    }

    // ── 배치 · 고치기 · 목록 ─────────────────────────────

    /// <summary>새 박스 — 블록 · 삽입 · 기록 → 형상 읽기 · 계측 → 평면 · 종단 · 횡단 · Civil 솔리드(<see cref="StructDraw.Reflect"/>). 돌려줌 = 번호(실패 −1) · <paramref name="msg"/> = 알림 한 줄.</summary>
    internal static int Place(Database db, Spec s, System.Text.StringBuilder log, out string msg)
    {
        msg = ""; Hollow hol;
        string? bad = Check(s) ?? Resolve(db, s) ?? Check(s);
        if (bad != null) { msg = "⚠" + bad; return -1; }
        int id;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!StructImport.TryLoad(db, tr, out var head, out var recs, out string lw)) { tr.Commit(); msg = "⚠" + lw; return -1; }
            if (s.Name.Trim().Length == 0) s.Name = NextName(recs);
            if (recs.Any(r => r.Name == s.Name)) { tr.Commit(); msg = $"⚠같은 이름 «{s.Name}»이 있습니다"; return -1; }
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            id = head.NextId;
            while (bt.Has(StructImport.BlockBase + id) || recs.Any(r => r.Id == id)) id++;
            var btr = new BlockTableRecord { Name = StructImport.BlockBase + id, Origin = Point3d.Origin };
            bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
            var bp = Build(db, tr, btr, s, out hol);
            bool layNew = !((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead)).Has(StructImport.LayXref);
            var lay = Commands.SectionCommand.EnsureLayer(db, tr, StructImport.LayXref, 8);
            // Revit 원본과 같게 — 원본 레이어는 처음 만들 때 꺼 둠(보이는 것 = 평면 외곽 · Civil 투영용 솔리드 · 종단 · 횡단)
            if (layNew) { try { ((LayerTableRecord)tr.GetObject(lay, OpenMode.ForWrite)).IsOff = true; } catch { } }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var br = new BlockReference(bp, btr.ObjectId);
            br.SetDatabaseDefaults(db); br.LayerId = lay;
            ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
            recs.Add(new StructRec { Id = id, Name = s.Name, Sha16 = Sig(s), Factor = 1, XrefHandle = br.Handle.ToString() });
            head.NextId = id + 1;
            StructImport.Save(db, tr, head, recs);
            tr.Commit();
        }
        catch (System.Exception ex) { msg = "⚠배치 실패 — " + ex.Message; return -1; }
        log.AppendLine($"  박스 «{s.Name}» 번호 {id} · 높이 {s.Height:F2} · 바닥고 {s.Bottom:F3}" + (s.ByDepth ? $"(원지반에서 {s.Depth:F2} m)" : "") + $" · 넓이 {Area(s.Ring):F2} · 정점 {s.Ring.Count} · {HollowText(hol, s.Height)}");
        msg = AfterChange(db, id, log) + (hol.Note.Length > 0 ? " · ⚠" + hol.Note : "");
        return id;
    }

    /// <summary>제원 고치기 — 같은 블록 · 같은 삽입(핸들 유지) · 기록 이름 · 표식 고침 → 다시 그림.</summary>
    internal static bool Update(Database db, int id, Spec s, System.Text.StringBuilder log, out string msg)
    {
        msg = ""; Hollow hol;
        string? bad = Check(s) ?? Resolve(db, s) ?? Check(s);
        if (bad != null) { msg = "⚠" + bad; return false; }
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!StructImport.TryLoad(db, tr, out var head, out var recs, out string lw)) { tr.Commit(); msg = "⚠" + lw; return false; }
            var r = recs.FirstOrDefault(x => x.Id == id && x.IsBox);
            if (r == null) { tr.Commit(); msg = "⚠그 박스가 없습니다"; return false; }
            if (s.Name.Trim().Length == 0) s.Name = r.Name;
            if (recs.Any(x => x.Id != id && x.Name == s.Name)) { tr.Commit(); msg = $"⚠같은 이름 «{s.Name}»이 있습니다"; return false; }
            var br = InsertOf(db, tr, r, OpenMode.ForWrite);
            if (br == null) { tr.Commit(); msg = "⚠박스 삽입이 없습니다(지워짐?)"; return false; }
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            var bp = Build(db, tr, btr, s, out hol);
            br.Position = bp;
            br.RecordGraphicsModified(true);
            r.Name = s.Name; r.Sha16 = Sig(s);
            StructImport.Save(db, tr, head, recs);
            tr.Commit();
        }
        catch (System.Exception ex) { msg = "⚠고치기 실패 — " + ex.Message; return false; }
        StructImport.Forget(db, id);
        log.AppendLine($"  박스 «{s.Name}» 고침 · 높이 {s.Height:F2} · 바닥고 {s.Bottom:F3} · 넓이 {Area(s.Ring):F2} · {HollowText(hol, s.Height)}");
        msg = AfterChange(db, id, log) + (hol.Note.Length > 0 ? " · ⚠" + hol.Note : "");
        return true;
    }

    /// <summary>배치 · 고친 뒤 — 계측(부재 · 세모)을 기록에 · 그리기(평면 · 종단 · 횡단 · 솔리드).</summary>
    static string AfterChange(Database db, int id, System.Text.StringBuilder log)
    {
        var all = StructImport.LoadAll(db, log, out var lwarn);
        var me = all.FirstOrDefault(x => x.Rec.Id == id);
        if (me == null || me.Model.Empty) return "⚠형상을 못 읽었습니다" + (me != null ? " — " + me.Note : "");
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (StructImport.TryLoad(db, tr, out var head, out var recs, out _))
            {
                var r = recs.First(x => x.Id == id);
                r.Parts = me.Model.Parts.Count(p => !p.Excluded); r.Tris = me.Model.TriCount; r.OpenParts = me.Model.OpenParts;
                StructImport.Save(db, tr, head, recs);
            }
            tr.Commit();
        }
        catch { }
        string s = StructDraw.Reflect(db, log);
        return s + (lwarn.Count > 0 ? " · " + string.Join(" · ", lwarn.Distinct()) : "");
    }

    /// <summary>«벽 0.4 · 상판 0.4 · 바닥판 0.5 · 콘크리트 98.78 ㎥» 또는 «꽉 찬 상자».</summary>
    internal static string HollowText(Hollow h, double height) =>
        h.Inner == null ? "꽉 찬 상자" : string.Format(IC, "벽 {0:0.###} · 상판 {1:0.###} · 바닥판 {2:0.###} m · 안 빈 칸 {3:F2} ㎡ × {4:F2} m · 콘크리트 {5:F2} ㎥", h.Wall, h.Top, h.Bot, h.InnerArea, h.InnerH, h.Concrete(height));

    internal static string NextName(IEnumerable<StructRec> recs)
    {
        var names = recs.Select(r => r.Name).ToHashSet();
        for (int i = 1; ; i++) if (!names.Contains("박스" + i)) return "박스" + i;
    }

    /// <summary>도면의 박스 목록(기록 차례).</summary>
    internal static List<Row> List(Database db)
    {
        var res = new List<Row>();
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (StructImport.TryLoad(db, tr, out _, out var recs, out _))
                foreach (var r in recs.Where(x => x.IsBox))
                {
                    var br = InsertOf(db, tr, r, OpenMode.ForRead);
                    if (br == null || tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is not BlockTableRecord btr) continue;
                    var s = ReadSpec(tr, btr);
                    if (s == null || s.Ring.Count < 3) continue;
                    s.Name = r.Name;
                    double a = Area(s.Ring);
                    Hollow ho; try { ho = Eff(s); } catch { ho = new Hollow { OuterArea = a }; }
                    var row = new Row { Id = r.Id, S = s, Area = a, Volume = a * s.Height, H = ho, Concrete = ho.Concrete(s.Height) };
                    row.Ext = new Extents3d(new Point3d(s.Ring.Min(p => p.X), s.Ring.Min(p => p.Y), s.Bottom), new Point3d(s.Ring.Max(p => p.X), s.Ring.Max(p => p.Y), s.Bottom + s.Height));
                    res.Add(row);
                }
            tr.Commit();
        }
        catch { }
        return res;
    }

    /// <summary>«이름,높이,바닥고,넓이,부피» 한 줄(명령줄 · 시험).</summary>
    internal static string Describe(Row r) =>
        string.Format(IC, "{0} · 높이 {1:F2} m · 바닥고 {2:F3}{3} · 넓이 {4:F2} ㎡ · 부피 {5:F2} ㎥ · {6}",
            r.S.Name, r.S.Height, r.S.Bottom, r.S.ByDepth ? string.Format(IC, "(원지반에서 {0:F2} m)", r.S.Depth) : "", r.Area, r.Volume, HollowText(r.H, r.S.Height));

    /// <summary>★[v117.19] 평면 — 안쪽 벽 선(점선 · 박스만) · <see cref="StructDraw.DrawPlan"/>이 부름(같은 꼬리표라 같이 지워짐). 그린 수.</summary>
    internal static int PlanInner(Database db, Transaction tr, StructImport.Loaded L, System.Action<Entity> add)
    {
        if (!L.Rec.IsBox) return 0;
        if (tr.GetObject(L.Insert, OpenMode.ForRead) is not BlockReference br || tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is not BlockTableRecord btr) return 0;
        var s = ReadSpec(tr, btr);
        if (s == null || s.Ring.Count < 3) return 0;
        var h = Eff(s);
        if (h.Inner == null) return 0;
        var pl = Poly(h.Inner);
        pl.Elevation = 0;
        add(pl);
        try
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!lt.Has("DASHED")) try { db.LoadLineTypeFile("DASHED", "acad.lin"); } catch { }
            if (lt.Has("DASHED")) { pl.Linetype = "DASHED"; pl.LinetypeScale = 0.5; }
        }
        catch { }
        return 1;
    }
}
