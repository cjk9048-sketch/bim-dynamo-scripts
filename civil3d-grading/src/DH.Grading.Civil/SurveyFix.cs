using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Civil.Commands;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[JACK 1121 «측량도 보정하기»] 백판(수치지도 · 서버 · 전산 등고선으로 만든 원지반) 위에 측량도를 얹어 <b>측량 범위만 측량 자료로 바꿔</b> 원지반을 새로 짓는다.
/// <para><b>왜.</b> 측량은 정해진 자리만 해 와서 가장 정확하지만 범위가 좁고 네모반듯하지 않다 — 넓은 백판을 깔고 그 위에 측량도를 얹은 뒤,
/// 측량도 자리의 백판 등고선 · 표고점을 지우고 측량 자료로 치환해 합성한다(손으로 하던 일).</para>
/// <list type="number">
/// <item><b>읽기</b>(<see cref="Read"/>): 측량도 DWG · DXF를 <b>옆 DB</b>로 읽는다(도면을 열지 않음 · 원본 그대로). 선은 <see cref="GroundContourCommand.Sample"/> 한 벌로 펴고
/// 한 표고면 등고선 · 제각각이면 3D 선(브레이크라인) · 점(POINT)과 표고 든 블록은 표고점. 표고 0은 버린다(다른 세 길과 같음).</item>
/// <item><b>범위</b>: 고른 측량 선 · 점을 틈 메움/2만큼 부풀렸다 줄이고 여유(기본 2 m)를 남긴 것(<see cref="SurveyMerge.Region"/>) · 구멍은 메움.</item>
/// <item><b>합성</b>(<see cref="Merge"/>): 고른 원지반의 재료(주인 표시 · 옛 도면이고 원지반이 하나면 표시 없는 재료)를 읽어 등고선 · 3D 선은 범위 밖 몫만 남기고(경계에서 자름) 표고점은 범위 안 것을 뺀다.
/// 남은 백판 + 측량 자료를 원지반 재료 레이어에 <b>새 사본</b>으로 그리고 <see cref="ImportGisCommand.BuildGroundSurfaceFrom"/>(세 길과 같은 한 벌)로 짓는다 → 새 원지반(원지반_N).</item>
/// <item>옛 원지반 · 그 재료는 <b>지우지 않고</b> 숨기기만(지표면 Visible 끔) — 정지 · 터파기 · 도로가 옛 원지반을 물고 있어도 안 깨진다. 한 명령이라 U 한 번에 다 돌아간다.</item>
/// </list></summary>
internal static class SurveyFix
{
    internal sealed class Data
    {
        public string Path = "";
        public readonly List<SurveyItem> Items = new();
        public readonly HashSet<string> LayersOff = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>★[JACK 1122 «캐드처럼»] 레이어 색(ARGB) — 창 목록 · 그림.</summary>
        public readonly Dictionary<string, int> LayerArgb = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>★[JACK 1122 «측량도의 블록들이 점으로만 나와 구분이 안 됨»] 블록 모양(그림용 — 자료 아님 · 표고 0 블록 포함).</summary>
        public readonly List<SurveyDecor> Decor = new();
        int _decorPts;
        internal void AddDecor(SurveyDecor dc) { if (_decorPts > 400_000) return; _decorPts += dc.Pts.Count; Decor.Add(dc); }
        public int Read, Zero, Other, Fail, Short, BlockZero;
        public string Skipped()
        {
            var s = new List<string>();
            if (Zero > 0) s.Add($"표고 0 {Zero}개");
            if (BlockZero > 0) s.Add($"표고 0 블록 {BlockZero}개");
            if (Short > 0) s.Add($"길이 0 {Short}개");
            if (Other > 0) s.Add($"선 · 점 아님(글자 · 해치 등) {Other}개");
            if (Fail > 0) s.Add($"읽기 실패 {Fail}개");
            return s.Count == 0 ? "" : "뺌: " + string.Join(" · ", s);
        }
    }

    internal sealed class Result
    {
        public bool Ok;
        public string Message = "", NewName = "", BaseName = "";
        public ObjectId NewId = ObjectId.Null, BaseId = ObjectId.Null;
        public NetTopologySuite.Geometries.Geometry? Region;
        public int BaseLines, BaseCut, BaseGone, BaseSpots, BaseSpotsGone, SurvContours, SurvBreaks, SurvSpots, DupSpots;
        public SurveyMerge.CrossReport? Cross;
        /// <summary>테두리 진단(옛 고리 표본 · 메움 점 · 도로 안 점 정리 · 자가 확인) — 진단 로그 · 시험용.</summary>
        public string BorderLog = "";
    }

    /// <summary>시험 고리 — 마지막 결과(범위 · 새 원지반).</summary>
    internal static Result? LastForTest;
    /// <summary>★[1123] 화면 없는 시험 고리 — 참이면 옛 원지반을 지우지 않고 숨기기만(예전 동작 · 옛 원지반과 견주는 시험용). 평소 거짓.</summary>
    internal static bool KeepOldForTest;

    static Point3 P3(Point3d p) => new(p.X, p.Y, p.Z);
    static Point3d P3d(Point3 p) => new(p.X, p.Y, p.Z);

    // ── 읽기 ────────────────────────────────────────────────────────────────
    /// <summary>측량도를 옆 DB로 읽는다(.dwg · .dxf) — 모형 공간의 선 · 점 · 블록만. 못 읽으면 예외(부르는 쪽이 알림).</summary>
    internal static Data Read(string path)
    {
        var d = new Data { Path = path };
        using var sdb = new Database(false, true);
        if (path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
        {
            string log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dh_survey_dxfin.log");
            sdb.DxfIn(path, log);
        }
        else sdb.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
        sdb.CloseInput(true);
        int id = 0;
        using var tr = sdb.TransactionManager.StartTransaction();
        // 블록 정의 모양(정의 좌표 · 한 번만 읽음 · 중첩 블록은 뺌)
        var defs = new Dictionary<ObjectId, List<(List<Point3d> P, bool Closed)>>();
        List<(List<Point3d> P, bool Closed)> DefOf(ObjectId btrId)
        {
            if (defs.TryGetValue(btrId, out var l)) return l;
            l = new();
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                int n = 0;
                foreach (ObjectId eid in btr)
                {
                    if (++n > 300) break;
                    try
                    {
                        if (tr.GetObject(eid, OpenMode.ForRead) is Curve cv)
                        {
                            var pp = GroundContourCommand.Sample(cv, out bool cl);
                            if (pp != null && pp.Count >= 2) l.Add((pp.Select(q => new Point3d(q.X, q.Y, q.Z)).ToList(), cl));
                        }
                    }
                    catch { }
                }
            }
            catch { }
            defs[btrId] = l;
            return l;
        }
        try
        {
            var lt = (LayerTable)tr.GetObject(sdb.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lid in lt)
                try
                {
                    var r = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    if (r.IsOff || r.IsFrozen) d.LayersOff.Add(r.Name);
                    try { d.LayerArgb[r.Name] = r.Color.ColorValue.ToArgb(); } catch { }
                }
                catch { }
        }
        catch { }
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(sdb), OpenMode.ForRead);
        foreach (ObjectId oid in ms)
        {
            try
            {
                if (tr.GetObject(oid, OpenMode.ForRead) is not Entity e) continue;
                d.Read++;
                string ly = e.Layer ?? "";
                int argb = 0;
                try { var ec = e.Color; argb = ec.IsByLayer || ec.IsByBlock ? (d.LayerArgb.TryGetValue(ly, out int la) ? la : 0) : ec.ColorValue.ToArgb(); } catch { }
                int before = d.Items.Count;
                if (e is DBPoint dp)
                {
                    if (Math.Abs(dp.Position.Z) < GroundContourCommand.ZeroTol) { d.Zero++; continue; }
                    d.Items.Add(new SurveyItem { Id = id++, Layer = ly, Kind = SurveyKind.Spot, Pts = { P3(dp.Position) }, Z0 = dp.Position.Z, Z1 = dp.Position.Z, Src = "점", Argb = argb });
                    continue;
                }
                if (e is BlockReference br)
                {
                    try
                    {
                        var xf = br.BlockTransform;
                        foreach (var (pp, cl) in DefOf(br.BlockTableRecord))
                        {
                            var dc = new SurveyDecor { Layer = ly, Argb = argb, Closed = cl };
                            foreach (var q in pp) { var w = q.TransformBy(xf); dc.Pts.Add((w.X, w.Y)); }
                            d.AddDecor(dc);
                        }
                    }
                    catch { }
                    // 측점 기호 블록 — 삽입점 표고가 곧 그 점의 표고(측량 성과도의 흔한 꼴). 표고 0이면 도곽 · 범례 같은 기호.
                    if (Math.Abs(br.Position.Z) < GroundContourCommand.ZeroTol) { d.BlockZero++; continue; }
                    string bn = ""; try { bn = br.Name; } catch { }
                    d.Items.Add(new SurveyItem { Id = id++, Layer = ly, Kind = SurveyKind.Spot, Pts = { P3(br.Position) }, Z0 = br.Position.Z, Z1 = br.Position.Z, Src = "블록 " + bn, Argb = argb });
                    continue;
                }
                if (e is not (Line or Polyline or Polyline2d or Polyline3d or Arc or Circle or Ellipse or Spline)) { d.Other++; continue; }
                var pts = GroundContourCommand.Sample((Curve)e, out bool closed);
                if (pts == null || pts.Count < 2) { d.Short++; continue; }
                double z0 = double.MaxValue, z1 = double.MinValue; bool anyZero = false;
                foreach (var p in pts) { z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); if (Math.Abs(p.Z) < GroundContourCommand.ZeroTol) anyZero = true; }
                if (anyZero) { d.Zero++; continue; }
                bool flat = z1 - z0 <= GroundContourCommand.FlatTol;
                var it = new SurveyItem { Id = id++, Layer = ly, Kind = flat ? SurveyKind.Contour : SurveyKind.Break, Closed = closed, Z0 = z0, Z1 = z1, Src = e.GetType().Name };
                foreach (var p in pts) it.Pts.Add(flat ? new Point3(p.X, p.Y, z0) : P3(p));
                d.Items.Add(it);
                for (int k = before; k < d.Items.Count; k++) d.Items[k].Argb = argb;
            }
            catch { d.Fail++; }
        }
        tr.Commit();
        return d;
    }

    // ── 백판 ────────────────────────────────────────────────────────────────
    sealed class BaseMat
    {
        public readonly List<(List<Point3> Pts, bool Closed, bool Flat)> Lines = new();
        public readonly List<Point3> Spots = new();
        public int Tagged, Untagged;
    }

    /// <summary>고른 원지반의 재료 — 원지반 재료 레이어(등고선 · 계곡선 · 표고점)에서 주인이 그 원지반인 것.
    /// 주인 표시가 하나도 없으면(옛 도면) 원지반이 하나일 때만 표시 없는 재료를 쓴다(둘 이상이면 어느 것의 재료인지 모름 → 빈 것).</summary>
    static BaseMat ReadBase(Database db, ObjectId groundId)
    {
        var bm = new BaseMat();
        string gh = groundId.IsNull ? "" : groundId.Handle.ToString();
        int grounds = GroundSet.List(db).Count;
        var layers = new HashSet<string>(ImportGisCommand.GroundImportLayers, StringComparer.OrdinalIgnoreCase);
        var tagged = new List<Entity>();
        var untagged = new List<Entity>();
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e || !layers.Contains(e.Layer ?? "")) continue;
                string o = GroundSet.OwnerOf(e);
                if (o.Length == 0) untagged.Add(e);
                else if (o.Equals(gh, StringComparison.OrdinalIgnoreCase)) tagged.Add(e);
            }
            catch { }
        var use = tagged.Count > 0 ? tagged : grounds <= 1 ? untagged : new List<Entity>();
        bm.Tagged = tagged.Count; bm.Untagged = untagged.Count;
        foreach (var e in use)
            try
            {
                if (e is DBPoint dp) { if (Math.Abs(dp.Position.Z) >= GroundContourCommand.ZeroTol) bm.Spots.Add(P3(dp.Position)); continue; }
                if (e is not Curve c) continue;
                var pts = GroundContourCommand.Sample(c, out bool closed);
                if (pts == null || pts.Count < 2) continue;
                double z0 = pts.Min(p => p.Z), z1 = pts.Max(p => p.Z);
                bool flat = z1 - z0 <= GroundContourCommand.FlatTol;
                bm.Lines.Add((pts.Select(p => flat ? new Point3(p.X, p.Y, z0) : P3(p)).ToList(), closed, flat));
            }
            catch { }
        tr.Commit();
        return bm;
    }

    /// <summary>미리보기용 백판(선 · 점 · 범위).</summary>
    internal static SurveyBase BaseForView(Database db, ObjectId groundId)
    {
        var sb = new SurveyBase();
        try
        {
            var bm = ReadBase(db, groundId);
            foreach (var (pts, _, _) in bm.Lines) { sb.Lines.Add(pts); foreach (var p in pts) sb.Grow(p.X, p.Y); }
            foreach (var p in bm.Spots) { sb.Spots.Add(p); sb.Grow(p.X, p.Y); }
        }
        catch { }
        return sb;
    }

    // ── 합성 ────────────────────────────────────────────────────────────────
    /// <summary>측량 범위만 측량 자료로 바꿔 원지반을 새로 짓는다 — 창 없음(화면 없는 시험도 이것). 못 하면 Ok 거짓 + 이유(도면 그대로).</summary>
    /// <summary>측량 자료의 평면 상자(여유 <paramref name="pad"/> m 포함) — 범위(무거운 셈) 전에 자리만 볼 때.</summary>
    internal static (double X0, double Y0, double X1, double Y1) BoxOf(IEnumerable<SurveyItem> use, double pad = 0)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var it in use) foreach (var p in it.Pts) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
        return (x0 - pad, y0 - pad, x1 + pad, y1 + pad);
    }

    /// <summary>측량 범위만 측량 자료로 바꿔 원지반을 새로 짓는다 — 창 없음(화면 없는 시험도 이것). 못 하면 Ok 거짓 + 이유(도면 그대로).
    /// <para>★[JACK 1122] <paramref name="surveyOnly"/> = 원지반과 다른 곳의 측량도 — 백판 없이 측량 자료만으로 새 원지반(옛 원지반은 숨기지 않음 · 다른 자리라 둘 다 씀).</para></summary>
    internal static Result Merge(Document doc, IReadOnlyList<SurveyItem> use, ObjectId groundId, double margin, double gap, bool surveyOnly = false)
    {
        var r = new Result { BaseId = groundId };
        LastForTest = r;
        var db = doc.Database;
        var ed = doc.Editor;
        r.BaseName = GroundSet.NameOf(db, groundId);
        if (use.Count == 0) { r.Message = "쓸 측량 자료가 없습니다."; return r; }
        var bm = ReadBase(db, groundId);
        if (surveyOnly)
        {
            // ★[검토 1122 MEDIUM] 측량도만으로는 «다른 곳(같은 좌표계)»일 때만 — 겹치면 보정을 · 좌표계가 다르면 거절(엉뚱한 자리에 원지반을 짓지 않게)
            if (bm.Lines.Count > 0 || bm.Spots.Count > 0)
            {
                double ax0 = double.MaxValue, ay0 = double.MaxValue, ax1 = double.MinValue, ay1 = double.MinValue;
                foreach (var (pts, _, _) in bm.Lines) foreach (var p in pts) { ax0 = Math.Min(ax0, p.X); ay0 = Math.Min(ay0, p.Y); ax1 = Math.Max(ax1, p.X); ay1 = Math.Max(ay1, p.Y); }
                foreach (var p in bm.Spots) { ax0 = Math.Min(ax0, p.X); ay0 = Math.Min(ay0, p.Y); ax1 = Math.Max(ax1, p.X); ay1 = Math.Max(ay1, p.Y); }
                var s0 = BoxOf(use, Math.Max(0.01, margin));
                var c0 = SurveyMerge.Classify(s0.X0, s0.Y0, s0.X1, s0.Y1, ax0, ay0, ax1, ay1);
                if (c0.Kind == SurveyMerge.Placement.Overlap) { r.Message = "측량도가 원지반과 겹칩니다 — [측량도만으로 원지반] 대신 [선택 레이어 사용]을 쓰세요."; return r; }
                if (c0.Kind == SurveyMerge.Placement.CrsMismatch) { r.Message = "측량도가 원지반과 겹치지 않습니다 — " + c0.Text; return r; }
            }
            return Build(doc, use, r, new BaseMat(), null, margin, gap, "", surveyOnly: true);
        }
        if (bm.Lines.Count == 0)
        {
            r.Message = $"'{r.BaseName}'의 재료(등고선)를 찾지 못했습니다" + (bm.Untagged > 0 && bm.Tagged == 0 ? " — 원지반이 둘 이상인 옛 도면이라 어느 것의 재료인지 모릅니다" : "")
                      + ".\n[원지반 가져오기]로 원지반을 다시 만든 뒤 해 보세요.";
            return r;
        }
        // ★[JACK 1122 «다른 곳 측량도 · 너무 오래 걸림»] 자리부터 본다(상자만 — 범위 셈 전) — 안 겹치면 범위를 셀 까닭이 없다
        double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
        foreach (var (pts, _, _) in bm.Lines) foreach (var p in pts) { bx0 = Math.Min(bx0, p.X); by0 = Math.Min(by0, p.Y); bx1 = Math.Max(bx1, p.X); by1 = Math.Max(by1, p.Y); }
        var sb = BoxOf(use, Math.Max(0.01, margin));
        var cls = SurveyMerge.Classify(sb.X0, sb.Y0, sb.X1, sb.Y1, bx0, by0, bx1, by1);
        if (cls.Kind != SurveyMerge.Placement.Overlap)
        {
            r.Message = "측량도가 원지반과 겹치지 않습니다 — " + cls.Text;
            return r;
        }
        var region = SurveyGeo.RegionOf(use, margin, gap);
        if (region == null || region.IsEmpty) { r.Message = "측량 범위를 만들지 못했습니다."; return r; }
        var env = region.EnvelopeInternal;
        double ix = Math.Min(env.MaxX, bx1) - Math.Max(env.MinX, bx0), iy = Math.Min(env.MaxY, by1) - Math.Max(env.MinY, by0);
        if (ix <= 0 || iy <= 0)
        {
            r.Message = "측량도가 원지반과 겹치지 않습니다 — 좌표계(원점)가 다를 수 있습니다.\n"
                      + $"측량 {env.MinX:F0},{env.MinY:F0} ~ {env.MaxX:F0},{env.MaxY:F0}\n원지반 {bx0:F0},{by0:F0} ~ {bx1:F0},{by1:F0}";
            return r;
        }
        string partWarn = "";
        return Build(doc, use, r, bm, region, margin, gap, partWarn, surveyOnly: false);
    }

    /// <summary>지표면 바깥 테두리 고리(Model 추출 · 임시 추출물은 지움) — 넓이 큰 순 · 닫음 중복점 뺌.</summary>
    static List<(List<Point3> Ring, double Area)> Borders(Transaction tr, CivilDb.TinSurface s)
    {
        var o = new List<(List<Point3>, double)>();
        foreach (ObjectId bid in s.ExtractBorder(Autodesk.Civil.SurfaceExtractionSettingsType.Model))
        {
            var ent = tr.GetObject(bid, OpenMode.ForWrite);
            var lp = new List<Point3>();
            if (ent is Polyline3d p3)
            {
                foreach (ObjectId vId in p3)
                    if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d pv) lp.Add(P3(pv.Position));
            }
            else if (ent is Polyline p2)
                for (int k = 0; k < p2.NumberOfVertices; k++)
                {
                    // ★[검토 1123 LOW] 2D 폴리선이면 높이가 하나뿐 — 면에서 다시 읽음(테두리 위라 못 읽으면 폴리선 높이)
                    var q = p2.GetPoint3dAt(k);
                    double z = q.Z;
                    try { z = s.FindElevationAtXY(q.X, q.Y); } catch (Autodesk.Civil.PointNotOnEntityException) { }
                    lp.Add(new Point3(q.X, q.Y, z));
                }
            ent.Erase();   // 재기용 임시 추출물
            if (lp.Count > 1 && Math.Abs(lp[0].X - lp[^1].X) < 1e-9 && Math.Abs(lp[0].Y - lp[^1].Y) < 1e-9) lp.RemoveAt(lp.Count - 1);
            if (lp.Count < 3) continue;
            double a = 0;
            for (int k = 0; k < lp.Count; k++) { var u = lp[k]; var v = lp[(k + 1) % lp.Count]; a += u.X * v.Y - v.X * u.Y; }
            o.Add((lp, Math.Abs(a) / 2));
        }
        o.Sort((x, y) => y.Item2.CompareTo(x.Item2));
        return o;
    }

    static NetTopologySuite.Geometries.LineString RingLine(IReadOnlyList<Point3> ring)
    {
        var cs = ring.Select(p => new NetTopologySuite.Geometries.Coordinate(p.X, p.Y)).ToList();
        cs.Add(cs[0]);
        return new NetTopologySuite.Geometries.GeometryFactory().CreateLineString(cs.ToArray());
    }

    /// <summary>옛 고리 표본 간격(m) — 꼭짓점 · 모서리 포함.</summary>
    const double RingStep = 2.5;

    /// <summary>옛 원지반 테두리를 새 원지반 바깥 경계로. 못 하면 알림 글(경계 없이 둠). <paramref name="ringOut"/> = 쓴 옛 고리(도로 안 점 정리가 테두리 점을 안 지우게).
    /// <para>★★[JACK 1123 «측량도 보정 · 도로 다듬기 뒤 처음 원지반 경계가 깨짐 — 깔끔한 네모였는데 찌그러져»] 자르기만으로는 모자란다 —
    /// 바뀐 범위(측량 · 도로)가 옛 테두리에 걸치면 그 자리 옛 재료(등고선 끝 · 테두리 채움 점)가 빠져 새 삼각망이 옛 테두리까지 안 닿을 수 있고,
    /// 바깥 경계는 자르기만 할 뿐 메우지 못한다. 그래서 옛 고리를 <see cref="RingStep"/> m마다(꼭짓점 포함) 재어 <b>새 삼각망이 안 닿는 자리에만</b>
    /// 옛 원지반 높이(옛 테두리 변 따라 직선 = 옛 원지반 그 자리 높이) 점을 넣고(지표면 편집 «점 추가» — 재료 아님 · 다음 보정은 이 원지반 테두리에서 다시 잼),
    /// 그다음 바깥 경계로 자른다. 닿는 자리는 손대지 않는다(바뀐 범위 안 = 새 자료 높이 · 밖 = 옛 원지반 그대로).</para></summary>
    static string ClipToOld(Database db, ObjectId oldId, ObjectId newId, NetTopologySuite.Geometries.Prepared.IPreparedGeometry? prep, out List<Point3>? ringOut, out string log)
    {
        ringOut = null; log = "";
        try
        {
            List<Point3>? ring = null;
            double best = 0;
            var areas = new List<double>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (oldId.IsNull || oldId.IsErased || tr.GetObject(oldId, OpenMode.ForRead) is not CivilDb.TinSurface old) { tr.Commit(); log = "옛 원지반을 못 읽음(없음 · 지워짐 · TIN 아님) — 경계 자르기 생략"; return ""; }
                var bs = Borders(tr, old);
                foreach (var (_, a) in bs) areas.Add(a);
                if (bs.Count > 0) { ring = bs[0].Ring; best = bs[0].Area; }
                tr.Commit();
            }
            if (ring == null) { log = "옛 원지반 테두리 고리 없음(추출 0개) — 경계 자르기 생략"; return "⚠옛 원지반 테두리를 못 읽음 — 경계 자르기 생략"; }
            ringOut = ring;   // ★[검토 1123 LOW] 메움 · 자르기가 실패해도 도로 안 점 정리의 테두리 띠 보호 · 자가 확인은 돈다
            var R = ring;
            // 옛 고리 표본(꼭짓점 + RingStep 이하 간격) — 높이는 옛 테두리 변 따라 직선 · 안쪽 1 cm 찔러 «닿음» 판정(선 위 반올림으로 «없음»이 나와 점이 생기지 않게)
            double sgn = 0;
            for (int k = 0; k < ring.Count; k++) { var u = ring[k]; var v = ring[(k + 1) % ring.Count]; sgn += u.X * v.Y - v.X * u.Y; }
            sgn = sgn >= 0 ? 1 : -1;   // 반시계면 안쪽 = 진행 방향 왼쪽
            int n = ring.Count;
            (double X, double Y) Nrm(int k)
            {
                var a = R[k]; var b = R[(k + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
                return l < 1e-9 ? (0, 0) : (-dy / l * sgn, dx / l * sgn);
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var add = new List<Point3d>();
            int samples = 0, gapIn = 0, gapOut = 0, dup = 0;
            string fillNote;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var tin = (CivilDb.TinSurface)tr.GetObject(newId, OpenMode.ForRead);
                    // ★[검토 1123 LOW] «밖»은 PointNotOnEntityException만 — 다른 예외는 메움 생략(아래 catch)으로
                    bool Has(double x, double y) { try { tin.FindElevationAtXY(x, y); return true; } catch (Autodesk.Civil.PointNotOnEntityException) { return false; } }
                    for (int k = 0; k < n; k++)
                    {
                        var a = ring[k]; var b = ring[(k + 1) % n];
                        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
                        if (len < 1e-9) continue;
                        var nc = Nrm(k); var np = Nrm((k - 1 + n) % n);
                        int m = Math.Max(1, (int)Math.Ceiling(len / RingStep));
                        for (int j = 0; j < m; j++)
                        {
                            double t = (double)j / m, x = a.X + dx * t, y = a.Y + dy * t, z = a.Z + (b.Z - a.Z) * t;
                            // 꼭짓점은 두 변 안쪽의 가운데(모서리 대각선 안쪽) · 변 가운데는 그 변 안쪽
                            double nx = nc.X, ny = nc.Y;
                            if (j == 0) { double bx = nc.X + np.X, by = nc.Y + np.Y, bl = Math.Sqrt(bx * bx + by * by); if (bl > 1e-6) { nx = bx / bl; ny = by / bl; } }
                            samples++;
                            if (Has(x + nx * 0.01, y + ny * 0.01)) continue;
                            add.Add(new Point3d(x, y, z));
                            if (prep != null && SurveyMerge.Inside(prep, x, y)) gapIn++; else gapOut++;
                        }
                    }
                    // ★[검토 1123 MEDIUM] 이미 꼭짓점이 있는 자리(5 mm 안 — 모서리에서 1 cm 찌른 곳만 밖인 경우) · 메움 점끼리 겹친 자리는 뺌
                    if (add.Count > 0 && add.Count < samples)
                    {
                        const double G = 0.005;
                        var have = new HashSet<(long, long)>();
                        foreach (CivilDb.TinSurfaceVertex v in tin.Vertices) have.Add(((long)Math.Round(v.Location.X / G), (long)Math.Round(v.Location.Y / G)));
                        bool Taken((long X, long Y) c) { for (long i = -1; i <= 1; i++) for (long j = -1; j <= 1; j++) if (have.Contains((c.X + i, c.Y + j))) return true; return false; }
                        var keepAdd = new List<Point3d>();
                        foreach (var p in add)
                        {
                            var c = ((long)Math.Round(p.X / G), (long)Math.Round(p.Y / G));
                            if (Taken(c)) { dup++; continue; }
                            have.Add(c); keepAdd.Add(p);
                        }
                        add = keepAdd;
                    }
                    tr.Commit();
                }
                if (add.Count > 0 && add.Count + dup >= samples)
                {
                    // ★[검토 1123 LOW] 표본이 전부 «안 닿음» = 새 원지반이 옛 자리에 없음(좌표 어긋남 등) — 테두리를 통째로 지어내지 않음
                    fillNote = $"메움 생략(표본 {samples}개 모두 새 삼각망 밖)";
                    add.Clear();
                }
                else fillNote = add.Count > 0 ? $"옛 높이 점 {add.Count}개 넣음" : "메울 곳 없음";
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var tin = (CivilDb.TinSurface)tr.GetObject(newId, OpenMode.ForWrite);
                    if (add.Count > 0) using (var pc = new Point3dCollection(add.ToArray())) tin.AddVertices(pc);
                    GradingBuilder.AddOuterBoundary(tin, ring, midOrd: 0.01);   // 다시 짓기는 이 안에서 한 번
                    tr.Commit();
                }
            }
            catch (System.Exception fx)
            {
                // ★[검토 1123 MEDIUM] 메움(점 추가 · 다시 짓기)이 실패해 거래가 통째로 취소되면 바깥 경계도 빠진다(1122 «긴 삼각형»이 돌아옴) — 경계만 넣는 옛 길로 한 번 더
                fillNote = "메움 생략(" + fx.Message + ")";
                using var tr = db.TransactionManager.StartTransaction();
                GradingBuilder.AddOuterBoundary((CivilDb.TinSurface)tr.GetObject(newId, OpenMode.ForWrite), ring, midOrd: 0.01);
                tr.Commit();
            }
            log = $"옛 테두리 꼭짓점 {n} · 표본 {samples}(간격 {RingStep} m 이하) 중 새 삼각망이 안 닿은 곳 {add.Count + dup}(바뀐 범위 안 {gapIn} · 밖 {gapOut} · 이미 꼭짓점 {dup}) → {fillNote} · {sw.ElapsedMilliseconds} ms";
            // ★[검토 1122 MEDIUM] 옛 원지반이 여러 조각이면 가장 큰 조각 테두리만 씀 — 알림
            int islands = areas.Count(x => x > best * 0.01) - 1;
            return "옛 원지반 테두리 밖은 자름" + (islands > 0 ? $" · ⚠옛 원지반이 여러 조각 — 가장 큰 조각만(작은 조각 {islands}개 자리는 새 원지반에 없음)" : "");
        }
        catch (System.Exception ex) { return "⚠옛 원지반 테두리로 자르지 못함 — " + ex.Message; }
    }

    /// <summary>자가 확인 — 새 원지반 바깥 테두리가 옛 고리와 같은가: 새 테두리 꼭짓점 중 옛 고리에서 5 cm 넘게 떨어진 수 · 옛 꼭짓점 중 새 테두리에서 5 cm 넘게 떨어진 수 · 최대 · 넓이 비(새/옛).</summary>
    static (int Off, int Miss, double Max, double Ratio) BorderCheck(Database db, ObjectId sid, List<Point3> ring)
    {
        List<Point3>? nr = null; double na = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bs = Borders(tr, (CivilDb.TinSurface)tr.GetObject(sid, OpenMode.ForRead));
            if (bs.Count > 0) { nr = bs[0].Ring; na = bs[0].Area; }
            tr.Commit();
        }
        if (nr == null) return (-1, -1, double.NaN, 0);
        var gf = new NetTopologySuite.Geometries.GeometryFactory();
        var dOld = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(RingLine(ring));
        var dNew = new NetTopologySuite.Operation.Distance.IndexedFacetDistance(RingLine(nr));
        int off = 0, miss = 0; double mx = 0;
        foreach (var p in nr) { double d = dOld.Distance(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(p.X, p.Y))); if (d > 0.05) off++; mx = Math.Max(mx, d); }
        foreach (var p in ring) { double d = dNew.Distance(gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(p.X, p.Y))); if (d > 0.05) miss++; mx = Math.Max(mx, d); }
        double oa = 0;
        for (int k = 0; k < ring.Count; k++) { var u = ring[k]; var v = ring[(k + 1) % ring.Count]; oa += u.X * v.Y - v.X * u.Y; }
        return (off, miss, mx, oa != 0 ? na / (Math.Abs(oa) / 2) : 0);
    }

    /// <summary>★★[JACK 1122 «원지반 도로 다듬기»] 다듬은 도로 테두리(3D)로 그 범위만 바꿔 원지반을 새로 짓는다 — 측량도 보정과 같은 한 벌
    /// (범위 안 옛 등고선 · 표고점 뺌 · 옛 원지반 숨김 · 옛 테두리로 자름 · 이벤트 뷰어 창 없음). <paramref name="note"/> = 결과 앞머리(다듬기 셈 요약).</summary>
    internal static Result MergeRoad(Document doc, ObjectId groundId, NetTopologySuite.Geometries.Geometry region, IReadOnlyList<List<Point3>> lines, string note, IReadOnlyList<Point3>? spots = null)
    {
        var r = new Result { BaseId = groundId };
        LastForTest = r;
        r.BaseName = GroundSet.NameOf(doc.Database, groundId);
        var bm = ReadBase(doc.Database, groundId);
        if (bm.Lines.Count == 0 && bm.Spots.Count == 0) { r.Message = $"'{r.BaseName}'의 재료(등고선 · 표고점)를 찾지 못했습니다.\n[원지반 가져오기]로 원지반을 다시 만든 뒤 해 보세요."; return r; }
        int id = 0;
        var use = new List<SurveyItem>();
        foreach (var l in lines)
        {
            if (l.Count < 2) continue;
            use.Add(new SurveyItem { Id = id++, Layer = "도로", Kind = SurveyKind.Break, Pts = l.ToList(), Z0 = l.Min(q => q.Z), Z1 = l.Max(q => q.Z), Src = "도로 테두리" });
        }
        if (spots != null)
            foreach (var q in spots) use.Add(new SurveyItem { Id = id++, Layer = "도로", Kind = SurveyKind.Spot, Pts = new List<Point3> { q }, Z0 = q.Z, Z1 = q.Z, Src = "도로 평탄화" });
        if (use.Count == 0) { r.Message = "다듬은 도로 선이 없습니다."; return r; }
        var keepAll = lines.ToList();
        if (spots != null && spots.Count > 0) keepAll.Add(spots.ToList());
        return Build(doc, use, r, bm, region, 0, 0, "", surveyOnly: false, roadNote: note, roadLines: keepAll);
    }

    /// <summary>★[JACK 1122 «다듬기 돌려도 울퉁불퉁»] Civil «평탄부 최소화»는 등고선 단계에서(도로 선 넣기 전) 빈 도로 자리의 평평한 삼각형에 등고선 사이 높이 점을 끼워 넣는다
    /// (1122 실측: 도로 한가운데 81.25 = 80 · 82.5 가운데) — 도로 범위 안에서 다듬은 선 위(0.6 m 안)가 아닌 점은 지운다(지표면 편집 «점 삭제»).
    /// <para>★★[JACK 1123 «도로 다듬기 뒤 원지반 경계가 찌그러짐»] 옛 테두리(<paramref name="ring"/>) 10 cm 안 점은 안 지운다 — 바깥 경계가 테두리에 찍은 점 · 테두리 메움 점을 지우면
    /// 그 자리 테두리가 안쪽으로 V자로 파인다(현장 덤프 A: 872개 지움 → 테두리 꼭짓점 334개가 옛 고리를 벗어남 · 최대 33.1 m 안쪽). <paramref name="kept"/> = 그래서 남긴 수.</para></summary>
    static int DropStrayInRoad(Database db, ObjectId sid, NetTopologySuite.Geometries.Geometry region, IReadOnlyList<List<Point3>> lines, IReadOnlyList<Point3>? ring, out int kept)
    {
        kept = 0;
        var band = ring == null ? null : NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(RingLine(ring).Buffer(0.1));
        // ★[검토 1123 LOW] 옛 고리 안 점만 지움(밖은 바깥 경계가 이미 버린 자리)
        NetTopologySuite.Geometries.Prepared.IPreparedGeometry? inRing = null;
        if (ring != null)
            try
            {
                var lr = RingLine(ring);
                var poly = lr.Factory.CreatePolygon(lr.Coordinates);
                inRing = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(poly.IsValid ? poly : poly.Buffer(0));
            }
            catch { inRing = null; }
        const double C = 0.5;
        var keep = new HashSet<(long, long)>();
        foreach (var l in lines) foreach (var q in l) keep.Add(((long)Math.Floor(q.X / C), (long)Math.Floor(q.Y / C)));
        bool Near(double x, double y)
        {
            long cx = (long)Math.Floor(x / C), cy = (long)Math.Floor(y / C);
            for (long dx = -1; dx <= 1; dx++) for (long dy = -1; dy <= 1; dy++) if (keep.Contains((cx + dx, cy + dy))) return true;
            return false;
        }
        var inner = region.Buffer(-0.5);
        if (inner.IsEmpty) return 0;
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(inner);
        var env = inner.EnvelopeInternal;
        var gf = inner.Factory;
        using var tr = db.TransactionManager.StartTransaction();
        if (tr.GetObject(sid, OpenMode.ForWrite) is not CivilDb.TinSurface tin) { tr.Commit(); return 0; }
        var del = new List<CivilDb.TinSurfaceVertex>();
        foreach (CivilDb.TinSurfaceVertex v in tin.Vertices)
        {
            var p = v.Location;
            if (!env.Contains(p.X, p.Y) || Near(p.X, p.Y)) continue;
            var pt = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(p.X, p.Y));
            if (!prep.Contains(pt)) continue;
            if (band != null && band.Contains(pt)) { kept++; continue; }
            if (inRing != null && !inRing.Contains(pt)) continue;
            del.Add(v);
        }
        if (del.Count > 0) tin.DeleteVertices(del);
        tr.Commit();
        return del.Count;
    }

    static Result Build(Document doc, IReadOnlyList<SurveyItem> use, Result r, BaseMat bm, NetTopologySuite.Geometries.Geometry? region, double margin, double gap, string partWarn, bool surveyOnly, string? roadNote = null, IReadOnlyList<List<Point3>>? roadLines = null)
    {
        var db = doc.Database;
        var ed = doc.Editor;
        ObjectId groundId = r.BaseId;
        r.Region = region;
        // 측량만: 백판 없음 → 자르기 · 숨기기 없음
        var prep = region != null ? SurveyMerge.Prepare(region) : null;

        // ── 백판 자르기 ──
        var keepLines = new List<(List<Point3> Pts, bool Closed, bool Flat)>();
        foreach (var (pts, closed, flat) in bm.Lines)
        {
            r.BaseLines++;
            var parts = SurveyMerge.ClipOutside(pts, closed, prep!);
            if (parts.Count == 0) { r.BaseGone++; continue; }
            if (parts.Count != 1 || parts[0].Pts.Count != pts.Count || parts[0].Closed != closed) r.BaseCut++;
            foreach (var (pp, cl) in parts)
                if (pp.Count >= 2) keepLines.Add((pp, cl, flat));
        }
        var keepSpots = new List<Point3>();
        foreach (var p in bm.Spots)
        {
            r.BaseSpots++;
            if (SurveyMerge.Inside(prep!, p.X, p.Y)) r.BaseSpotsGone++; else keepSpots.Add(p);
        }

        // ── 재료 사본 그리기(원지반 재료 레이어 · 꺼 둠) ──
        var contourIds = new ObjectIdCollection();
        var breakIds = new ObjectIdCollection();
        var spotIds = new ObjectIdCollection();
        var before = new HashSet<ObjectId>(GroundSet.List(db, legacy: false).Select(g => g.Id));
        using (doc.LockDocument())
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layMain = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerContour, 8);
                ObjectId layIdx = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerContourIndex, 30);
                ObjectId laySpot = ImportGisCommand.EnsureLayer(db, tr, ImportGisCommand.LayerSpot, 2);
                ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerContour);
                ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerContourIndex);
                ImportGisCommand.HideLayer(db, tr, ImportGisCommand.LayerSpot);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                ObjectId Add(Entity ent) { ms.AppendEntity(ent); tr.AddNewlyCreatedDBObject(ent, true); return ent.ObjectId; }
                void Line(List<Point3> pts, bool closed, bool flat)
                {
                    try
                    {
                        using var pc = new Point3dCollection(pts.Select(P3d).ToArray());
                        double q = pts[0].Z / 10.0;   // 계곡선 = 10 m 배수(재료 레이어 구분만 — 등고선으로 작성과 같음)
                        var pl = new Polyline3d(Poly3dType.SimplePoly, pc, closed) { LayerId = flat && Math.Abs(q - Math.Round(q)) < 1e-4 ? layIdx : layMain };
                        (flat ? contourIds : breakIds).Add(Add(pl));
                    }
                    catch { }
                }
                foreach (var (pts, closed, flat) in keepLines) Line(pts, closed, flat);
                // ★[JACK 1122 «이벤트 오류»] 같은 자리 표고점은 하나만(측량 먼저 · 백판은 남은 자리만) — Civil «중복 항목 무시»가 줄줄이 뜨지 않게
                var survSpots = use.Where(i => i.Kind == SurveyKind.Spot).Select(i => i.Pts[0]).ToList();
                var spotsOnce = SurveyMerge.DedupSpots(survSpots.Concat(keepSpots));
                var survSet = new HashSet<Point3>(survSpots);
                r.DupSpots = survSpots.Count + keepSpots.Count - spotsOnce.Count;
                foreach (var p in spotsOnce)
                    try { spotIds.Add(Add(new DBPoint(P3d(p)) { LayerId = laySpot })); if (survSet.Contains(p)) r.SurvSpots++; } catch { }
                foreach (var it in use)
                {
                    if (it.Kind == SurveyKind.Spot) continue;
                    int n0 = contourIds.Count + breakIds.Count;
                    Line(it.Pts, it.Closed, it.Kind == SurveyKind.Contour);
                    if (contourIds.Count + breakIds.Count > n0) { if (it.Kind == SurveyKind.Contour) r.SurvContours++; else r.SurvBreaks++; }
                }
                tr.Commit();
            }

            // ── 원지반 — 세 길과 같은 한 벌(새 이름 원지반_N · 주인 표시 · 스타일 · 레이어) ──
            try { r.Cross = SurveyMerge.Crossings(use.Where(i => i.Kind != SurveyKind.Spot).Select(i => (i.Layer, (IReadOnlyList<Point3>)i.Pts, i.Closed)).ToList()); } catch { }
            // ★[JACK 1123 «도로를 가로질러 솟구침»] 도로 다듬기는 등고선 전체를 «틈 메우기» 없이 — 도로 범위에서 잘린 같은 높이 두 끝을 Civil이 도로 위로 다시 잇지 않게
            //   (잘린 것만 따로 넣으면 첫 묶음의 평탄부 최소화가 빈 도로 자리를 먼저 채워 45° 칸이 늘어남 — 실측 VWorld 171 → 499)
            string surfNote;
            GroundElevLabels.Hold++;   // ★[JACK 1124] 표고는 자르기 · 옛 원지반 치우기 뒤 끝에서 한 번
            try { surfNote = ImportGisCommand.BuildGroundSurfaceFrom(db, ed, contourIds, spotIds, breakIds, roadNote != null ? contourIds : null); }
            finally { GroundElevLabels.Hold--; }
            DrawOrderFix.Apply(db);
            bool built = !surfNote.StartsWith("지표면 생성 실패", StringComparison.Ordinal) && !surfNote.StartsWith("지표면 생략", StringComparison.Ordinal);
            foreach (var g in GroundSet.List(db, legacy: false)) if (!before.Contains(g.Id)) { r.NewId = g.Id; r.NewName = g.Name; }
            string done = surveyOnly
                ? $"측량도만으로 — 측량 등고선 {r.SurvContours} · 3D 선 {r.SurvBreaks} · 표고점 {r.SurvSpots} · {surfNote}"
                : $"'{r.BaseName}' 백판 등고선 {r.BaseLines}가닥 중 범위에 걸려 자름 {r.BaseCut} · 통째 뺌 {r.BaseGone} · 표고점 {r.BaseSpots}개 중 뺌 {r.BaseSpotsGone}"
                  + (roadNote != null
                      ? $" → 다듬은 도로 테두리 {r.SurvBreaks}가닥 넣음 · {roadNote} · {surfNote}"
                      : $" → 측량 등고선 {r.SurvContours} · 3D 선 {r.SurvBreaks} · 표고점 {r.SurvSpots} 넣음 · 범위 {region!.Area:N0} ㎡(여유 {margin:0.##} m · 틈 메움 {gap:0.##} m) · {surfNote}");
            if (!built || r.NewId.IsNull)
            {
                r.Message = "원지반을 새로 짓지 못했습니다.\n\n" + done;
                try { DiagLog.Append($"\n■ DHSURVEYFIX — 실패: {done}\n"); } catch { }
                return r;
            }
            // ★[JACK 1122 스샷 «처음 부른 원지반보다 측량도가 넘어가도 처음 원지반을 넘어 작성되지 않게»]
            //   새 원지반 바깥 경계 = 옛 원지반 테두리(가장 큰 고리) — 측량이 밖으로 뻗어도 긴 삼각형으로 늘어나지 않음
            string clip = "";
            List<Point3>? oldRing = null;
            var bLog = new List<string>();
            if (!surveyOnly)
            {
                var evPrev = EventViewerMute.Begin();   // ★[검토 1122 MEDIUM] 자르며 다시 짓기도 이벤트 뷰어 창 없이
                try { clip = ClipToOld(db, groundId, r.NewId, prep, out oldRing, out string cl); if (cl.Length > 0) bLog.Add(cl); } finally { EventViewerMute.End(evPrev); }
            }
            if (clip.Length > 0) done += "\n  " + clip;
            if (roadLines != null && region != null)
            {
                var evPrev2 = EventViewerMute.Begin();
                try
                {
                    int nd = DropStrayInRoad(db, r.NewId, region, roadLines, oldRing, out int nk);
                    if (nd > 0) done += $"\n  도로 안 끼어든 점 {nd}개 지움(평탄부 최소화 점)";
                    bLog.Add($"도로 안 점 정리: 지움 {nd} · 옛 테두리 10 cm 안이라 남김 {nk}");
                }
                catch (System.Exception dex) { done += "\n  ⚠도로 안 점 정리 못 함 — " + dex.Message; }
                finally { EventViewerMute.End(evPrev2); }
            }
            // ★[JACK 1123] 자가 확인 — 새 원지반 바깥 테두리 = 옛 테두리(어긋나면 알림 한 줄 · 자세한 수는 진단 로그)
            if (oldRing != null)
                try
                {
                    var bc = BorderCheck(db, r.NewId, oldRing);
                    // ★[검토 1123 MEDIUM] 새 테두리를 못 읽음(-1) = 로그만(알림에 «-1곳 · NaN m»을 띄우지 않음)
                    if (bc.Off < 0) bLog.Add("테두리 확인: 새 원지반 테두리를 못 읽음");
                    else
                    {
                        bLog.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "테두리 확인: 새 꼭짓점 중 옛 고리 5 cm 밖 {0} · 옛 꼭짓점 중 새 테두리 5 cm 밖 {1} · 최대 {2:0.000} m · 넓이 새/옛 {3:0.000000}", bc.Off, bc.Miss, bc.Max, bc.Ratio));
                        if (bc.Off > 0 || bc.Miss > 0) done += $"\n  ⚠원지반 테두리 {Math.Max(bc.Off, bc.Miss)}곳 어긋남(최대 {bc.Max:0.00} m) → 결과 확인 뒤 다시 하기";
                    }
                }
                catch (System.Exception bex) { bLog.Add("테두리 확인 못 함 — " + bex.Message); }
            r.BorderLog = string.Join("\n", bLog);
            if (r.BorderLog.Length > 0) try { DiagLog.Append($"\n■ DHSURVEYFIX 테두리 — {r.BorderLog.Replace("\n", " · ")}\n"); } catch { }
            // ── 옛 원지반은 숨김(지우지 않음 — 그것을 쓰는 결과가 있을 수 있음 · [원지반 삭제]로 지울 수 있음) ──
            if (r.DupSpots > 0) done += $"\n  같은 자리 표고점 {r.DupSpots}개는 하나만 씀";
            if (roadNote == null && r.Cross is { ZDiff: > 0 } cx)
                done += $"\n  ⚠측량 선끼리 높이가 다르게 엇갈린 곳 {cx.ZDiff}곳 — 지표면이 한쪽만 씀(" + string.Join(" · ", cx.Pairs.Take(3).Select(p => p.A == p.B ? $"{p.A} {p.N}" : $"{p.A}×{p.B} {p.N}")) + ")"
                      + "\n    담장 · 건물 · 수목처럼 땅이 아닌 레이어면 창에서 빼고 다시 하세요";
            // ★★[JACK 1123 «다듬거나 측량도 보정했으면 최종 지표면만 원지반으로 두고 지우는 게 낫지 않아? [보기 → 전부]에서 막 겹쳐 나옴» → «자동 삭제 + 이름 원지반»]
            //   옛 원지반을 아무것도 안 쓰면 지움(그 재료 등고선 · 표고점도) + 새 원지반 이름을 옛 이름으로.
            //   쓰면(계획부지 붙여넣기 · 진입도로 · 표본선 · 관망 …) 지우지 않고 물러남(«…(옛)» · 숨김 · 표식 → 어느 보기에서도 다시 안 켬 · [원지반 삭제]로 지움).
            //   예전처럼 숨기기만은 KeepOldForTest(화면 없는 시험이 옛 원지반과 견줄 때)일 때만.
            string hid = surveyOnly ? $"\n  옛 '{r.BaseName}'은 그대로(다른 자리 — 둘 다 원지반으로 씀)" : "";
            string hidLog = "";
            if (!surveyOnly && KeepOldForTest) try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (!groundId.IsNull && !groundId.IsErased && tr.GetObject(groundId, OpenMode.ForWrite) is CivilDb.Surface old && old.Visible)
                {
                    old.Visible = false;
                    hid = $"\n  옛 '{r.BaseName}'은 숨김(지우지 않음 — 지우려면 [원지반 삭제])";
                    hidLog = "옛 원지반 숨김(시험)";
                }
                tr.Commit();
            }
            catch (System.Exception ex) { hid = "\n  ⚠옛 원지반 숨기기 못 함 — " + ex.Message; }
            else if (!surveyOnly && !groundId.IsNull && !groundId.IsErased) try
            {
                var used = Commands.GroundOffCommand.UsedBy(db, new System.Collections.Generic.List<ObjectId> { groundId });
                SurfaceRetire.Result sr;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    sr = SurfaceRetire.Apply(db, tr, new[] { groundId }, used);
                    tr.Commit();
                }
                if (sr.Erased > 0)
                {
                    string newName = r.NewName;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        if (!r.NewId.IsNull && !r.NewId.IsErased && !string.IsNullOrEmpty(r.BaseName)
                            && tr.GetObject(r.NewId, OpenMode.ForWrite) is CivilDb.Surface ns
                            && SurfaceRetire.UniqueName(db, tr, r.BaseName) == r.BaseName)
                        { ns.Name = r.BaseName; newName = r.BaseName; }
                        tr.Commit();
                    }
                    int fail = 0;
                    int nMat = Commands.GroundOffCommand.EraseMaterial(doc, ref fail);
                    hid = $"\n  옛 '{r.BaseName}' 지움(등고선 · 표고점 {nMat:N0}개 함께) — 새 원지반 이름 '{newName}'";
                    hidLog = $"옛 원지반 지움 · 재료 {nMat} · 못 지움 {fail} · 새 이름 {newName}";
                    r.NewName = newName;
                }
                else if (sr.Retired.Count > 0 || sr.StillUsed.Count > 0)
                {
                    string who = string.Join(" · ", used.Values.Distinct());
                    hid = $"\n  옛 '{r.BaseName}'은 쓰는 결과가 있어 지우지 않고 숨김({(who.Length > 0 ? who : "쓰는 것 있음")}) → 다 쓴 뒤 [원지반 삭제]";
                    hidLog = sr.Text;
                }
                else if (sr.Failed.Count > 0) { hid = "\n  ⚠옛 원지반 치우기 못 함 — " + string.Join(" · ", sr.Failed); hidLog = sr.Text; }
            }
            catch (System.Exception ex) { hid = "\n  ⚠옛 원지반 치우기 못 함 — " + ex.Message; hidLog = ex.ToString(); }
            r.Ok = true;
            r.Message = done + hid + partWarn;
            try { DiagLog.Append($"\n■ DHSURVEYFIX — {done} · 새 '{r.NewName}'{(hidLog.Length > 0 ? " · " + hidLog : "")}\n"); } catch { }
        }
        // ★[JACK 1124] 공통설정 «원지반 표시 = 표고 표시» — 새 원지반으로 옮김(옛 · 숨긴 원지반 표고는 지움 · 꺼져 있으면 기록 한 번 읽고 끝)
        string ge = GroundElevLabels.Sync(db);
        if (ge.Length > 0) ed.WriteMessage("\n[원지반 표고] " + ge);
        try { ed.Regen(); } catch { }
        return r;
    }
}
