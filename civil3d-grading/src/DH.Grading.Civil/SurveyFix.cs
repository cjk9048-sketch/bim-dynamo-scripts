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
    }

    /// <summary>시험 고리 — 마지막 결과(범위 · 새 원지반).</summary>
    internal static Result? LastForTest;

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
        try
        {
            var lt = (LayerTable)tr.GetObject(sdb.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lid in lt)
                try { var r = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead); if (r.IsOff || r.IsFrozen) d.LayersOff.Add(r.Name); } catch { }
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
                if (e is DBPoint dp)
                {
                    if (Math.Abs(dp.Position.Z) < GroundContourCommand.ZeroTol) { d.Zero++; continue; }
                    d.Items.Add(new SurveyItem { Id = id++, Layer = ly, Kind = SurveyKind.Spot, Pts = { P3(dp.Position) }, Z0 = dp.Position.Z, Z1 = dp.Position.Z, Src = "점" });
                    continue;
                }
                if (e is BlockReference br)
                {
                    // 측점 기호 블록 — 삽입점 표고가 곧 그 점의 표고(측량 성과도의 흔한 꼴). 표고 0이면 도곽 · 범례 같은 기호.
                    if (Math.Abs(br.Position.Z) < GroundContourCommand.ZeroTol) { d.BlockZero++; continue; }
                    string bn = ""; try { bn = br.Name; } catch { }
                    d.Items.Add(new SurveyItem { Id = id++, Layer = ly, Kind = SurveyKind.Spot, Pts = { P3(br.Position) }, Z0 = br.Position.Z, Z1 = br.Position.Z, Src = "블록 " + bn });
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
                if (c0.Kind == SurveyMerge.Placement.Overlap) { r.Message = "측량도가 원지반과 겹칩니다 — [측량도만으로 원지반] 대신 [보정 실행]을 쓰세요."; return r; }
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
        string partWarn = ix * iy < 0.5 * env.Width * env.Height ? "\n  ⚠측량 범위의 절반 넘게가 원지반 밖 — 밖은 측량 자료만으로 지음" : "";
        return Build(doc, use, r, bm, region, margin, gap, partWarn, surveyOnly: false);
    }

    static Result Build(Document doc, IReadOnlyList<SurveyItem> use, Result r, BaseMat bm, NetTopologySuite.Geometries.Geometry? region, double margin, double gap, string partWarn, bool surveyOnly)
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
            string surfNote = ImportGisCommand.BuildGroundSurfaceFrom(db, ed, contourIds, spotIds, breakIds);
            DrawOrderFix.Apply(db);
            bool built = !surfNote.StartsWith("지표면 생성 실패", StringComparison.Ordinal) && !surfNote.StartsWith("지표면 생략", StringComparison.Ordinal);
            foreach (var g in GroundSet.List(db, legacy: false)) if (!before.Contains(g.Id)) { r.NewId = g.Id; r.NewName = g.Name; }
            string done = surveyOnly
                ? $"측량도만으로 — 측량 등고선 {r.SurvContours} · 3D 선 {r.SurvBreaks} · 표고점 {r.SurvSpots} · {surfNote}"
                : $"'{r.BaseName}' 백판 등고선 {r.BaseLines}가닥 중 범위에 걸려 자름 {r.BaseCut} · 통째 뺌 {r.BaseGone} · 표고점 {r.BaseSpots}개 중 뺌 {r.BaseSpotsGone}"
                  + $" → 측량 등고선 {r.SurvContours} · 3D 선 {r.SurvBreaks} · 표고점 {r.SurvSpots} 넣음 · 범위 {region!.Area:N0} ㎡(여유 {margin:0.##} m · 틈 메움 {gap:0.##} m) · {surfNote}";
            if (!built || r.NewId.IsNull)
            {
                r.Message = "원지반을 새로 짓지 못했습니다.\n\n" + done;
                try { DiagLog.Append($"\n■ DHSURVEYFIX — 실패: {done}\n"); } catch { }
                return r;
            }
            // ── 옛 원지반은 숨김(지우지 않음 — 그것을 쓰는 결과가 있을 수 있음 · [원지반 삭제]로 지울 수 있음) ──
            if (r.DupSpots > 0) done += $"\n  같은 자리 표고점 {r.DupSpots}개는 하나만 씀";
            if (r.Cross is { ZDiff: > 0 } cx)
                done += $"\n  ⚠측량 선끼리 높이가 다르게 엇갈린 곳 {cx.ZDiff}곳 — 지표면이 한쪽만 씀(" + string.Join(" · ", cx.Pairs.Take(3).Select(p => p.A == p.B ? $"{p.A} {p.N}" : $"{p.A}×{p.B} {p.N}")) + ")"
                      + "\n    담장 · 건물 · 수목처럼 땅이 아닌 레이어면 창에서 빼고 다시 하세요";
            string hid = surveyOnly ? $"\n  옛 '{r.BaseName}'은 그대로(다른 자리 — 둘 다 원지반으로 씀)" : "";
            if (!surveyOnly) try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (!groundId.IsNull && !groundId.IsErased && tr.GetObject(groundId, OpenMode.ForWrite) is CivilDb.Surface old && old.Visible)
                {
                    old.Visible = false;
                    hid = $"\n  옛 '{r.BaseName}'은 숨김(지우지 않음 — 지우려면 [원지반 삭제])";
                }
                tr.Commit();
            }
            catch (System.Exception ex) { hid = "\n  ⚠옛 원지반 숨기기 못 함 — " + ex.Message; }
            r.Ok = true;
            r.Message = done + hid + partWarn;
            try { DiagLog.Append($"\n■ DHSURVEYFIX — {done} · 새 '{r.NewName}'{(hid.Length > 0 ? " · 옛 원지반 숨김" : "")}\n"); } catch { }
        }
        try { ed.Regen(); } catch { }
        return r;
    }
}
