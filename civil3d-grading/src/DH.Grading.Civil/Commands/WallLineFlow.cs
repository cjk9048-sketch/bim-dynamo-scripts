using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace DH.Grading.Civil.Commands;

/// <summary>★★★[v114.0 · JACK 1105 «옹벽 추가 새 개념 — 선에서 바깥으로 수평 계단»] 부지 [옹벽 추가] — 옛 v113 «안쪽 다단» 길을 바꿈(그리기 화면 WallLineDraw는 그대로).
/// <para>(옛 머리말) ★★★[v113.0 · JACK 1104] 부지 [옹벽 변환] 폴리선(L) 길 — 계획 4장.
/// <para>폴리선 그리기 · 고르기(<see cref="WallLineDraw"/>) → 경계 판정(Core <see cref="BoundaryCut"/>) → 단높이 · 소단(Enter = 정지옵션) · 미리보기 →
/// 같은 트랜잭션에서 가상옹벽{번호}_DH(브레이크라인 = Core <see cref="WallByLine"/>)를 짓고 옹벽 목록 종류 2 «대기»로 적고 → 합성(DHWALLCOMP · 한 판 RuleOf)을 이어 태운다.</para>
/// 정지면은 안 다시 짓는다. 못 하면 아무것도 안 바꾼다(트랜잭션째 물림).</para>
/// <para>화면 없는 탐침: <see cref="Make"/>(그린 점 · H · T를 받아 목록까지 — 질문 없음)를 부른 뒤 DHWALLCOMP.</para></summary>
internal static class WallLineFlow
{
    const string Tag = "옹벽 추가";
    static void Log(string s) { try { DiagLog.AppendCarry("\n" + s); } catch { } }

    /// <summary>생성된 지표면 경계 Bd(마지막 구역 — 클립링 ∪ 계획선 · 없으면 순수교선) + 막을 다각형(구멍 · 다른 옹벽 · 굳은 옹벽 · 앞 구역 · 진입도로).</summary>
    /// <param name="clip">★[v113.0 · 설치 전 검토 M5] R_out 여유가 비켜 갈 다각형 — 다른 옹벽 폴리곤(여유 포함).</param>
    internal static (List<Point3>? Bd, List<(string Name, IReadOnlyList<Point3> Ring)> Blockers, string Why) BoundaryOf(Database db, Transaction tr,
        IReadOnlyList<GradingBundle> regions, IEnumerable<WallRec> walls, WallListHead head, int selfNo, out List<IReadOnlyList<Point3>> clip)
    {
        clip = new List<IReadOnlyList<Point3>>();
        var bl = new List<(string, IReadOnlyList<Point3>)>();
        var region = regions[^1];
        var gf = NtsSupport.Factory();
        var parts = new List<Geometry>();
        void Add(List<Point3>? r) { var g = r != null && r.Count >= 3 ? NtsSupport.ToCleanGeometry(r, gf) : null; if (g != null && !g.IsEmpty) parts.Add(g); }
        if (region.CutClipRing is { Count: >= 3 }) Add(region.CutClipRing); else if (region.CutFinalRings != null) foreach (var r in region.CutFinalRings) Add(r);
        if (region.FillClipRing is { Count: >= 3 }) Add(region.FillClipRing); else if (region.FillFinalRings != null) foreach (var r in region.FillFinalRings) Add(r);
        Add(region.Boundary);
        if (parts.Count == 0) return (null, bl, "구역 경계를 못 읽었습니다");
        Geometry u;
        try { u = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(parts); } catch (System.Exception ex) { return (null, bl, $"경계를 못 합쳤습니다({ex.GetType().Name})"); }
        var planP = NtsSupport.ToCleanPolygon(region.Boundary, gf);
        if (planP == null) return (null, bl, "계획선을 다각형으로 못 만들었습니다");
        var ip = planP.InteriorPoint;
        Polygon? hold = null;
        for (int i = 0; i < u.NumGeometries; i++) if (u.GetGeometryN(i) is Polygon pg && pg.Contains(ip)) { hold = pg; break; }
        if (hold == null) return (null, bl, "계획선을 품는 경계 조각이 없습니다");
        List<Point3> R(LineString l) => l.Coordinates.Take(l.NumPoints - 1).Select(c => new Point3(c.X, c.Y, 0)).ToList();
        var bd = R(hold.ExteriorRing);
        for (int i = 0; i < hold.NumInteriorRings; i++) bl.Add(($"경계 안 구멍 {i + 1}", R(hold.GetInteriorRingN(i))));
        // ★[v113.0 · 설치 전 검토 M5] 앞서 지은 폴리선 옹벽이 경계를 이미 잘랐다 — 그 원본 폴리선으로 번호 차례대로 잘라 «지금 경계»로(자기 자신은 뺌)
        var marginClip = clip;
        foreach (var w in walls.Where(w => w.Kind != 0 && w.No != selfNo && w.Src.Count >= 2).OrderBy(w => w.No))   // ★[v114.0] Kind != 0(앞 선 옹벽의 R_out을 뺀 경계 · 그 바꿀 자리는 아래 막을 다각형)
        {
            var cw = BoundaryCut.Classify(w.Src, bd, region.Boundary, null);
            if (cw.Ok) bd = cw.NewRing;
            else Log($"  ⚠폴리선 옹벽 {w.No}의 폴리선이 지금 경계에서 {BoundaryCut.CodeName(cw.Code)} — 그 옹벽 몫은 경계에서 안 뺐다({cw.Why})");
        }
        // 막을 다각형 = 다른 옹벽 폴리곤 ∩ 생성된 경계(여유 10 m는 빼고 — 여유끼리 겹친다고 막지 않게) · 여유는 따로 비켜 간다(MarginClip)
        foreach (var w in walls)
        {
            if (w.No == selfNo || w.Poly.Count < 3) continue;
            marginClip.Add(w.Poly);
            var wp = NtsSupport.ToCleanGeometry(w.Poly, gf);
            Geometry? cut0 = null;
            try { if (wp != null) cut0 = wp.Intersection(hold); } catch { cut0 = wp; }
            if (cut0 == null || cut0.IsEmpty) continue;
            for (int i = 0; i < cut0.NumGeometries; i++) if (cut0.GetGeometryN(i) is Polygon cp && cp.Area > 1e-6) bl.Add(($"옹벽 {w.No}", R(cp.ExteriorRing)));
        }
        foreach (var f in head.Frozen) if (f.Poly.Count >= 3) bl.Add(($"굳은 옹벽 {f.No}", f.Poly));
        for (int k = 0; k + 1 < regions.Count; k++)
        {
            var b = regions[k];
            if (b?.Boundary is { Count: >= 3 }) bl.Add(($"앞 구역 {k + 1} 계획선", b.Boundary));
            if (b?.CutClipRing is { Count: >= 3 }) bl.Add(($"앞 구역 {k + 1} 절토", b.CutClipRing));
            if (b?.FillClipRing is { Count: >= 3 }) bl.Add(($"앞 구역 {k + 1} 성토", b.FillClipRing));
        }
        try
        {
            var rf = RoadSession.Footprints(db, tr, out int nR, out _);
            if (rf != null)
                for (int i = 0; i < rf.NumGeometries; i++)
                    if (rf.GetGeometryN(i) is Polygon rp) bl.Add(($"진입도로 {i + 1}", R(rp.ExteriorRing)));
        }
        catch { }
        return (bd, bl, "");
    }

    /// <summary>합성 전 면(P0) — 정지면_DH가 합성이면 그 합성 전 면 · 아니면 정지면_DH(DHWALLCOMP ③과 같은 규칙).</summary>
    internal static (TinSurface? P0, string Why) P0Of(Transaction tr, ObjectId groundId)
    {
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var (cIds, cNames) = WallCompositeCommand.Candidates(civ, tr, WallCompositeCommand.PlanName, groundId);
        if (cIds.Count != 1) return (null, cIds.Count == 0 ? "정지면_DH가 없습니다" : $"정지면_DH 후보가 {cIds.Count}개입니다({string.Join(", ", cNames)})");
        if (tr.GetObject(cIds[0], OpenMode.ForRead) is not TinSurface cur) return (null, "정지면이 TIN이 아닙니다");
        if (!WallCompositeCommand.IsComposite(tr, cur)) return (cur, "");
        string bh = WallCompositeCommand.CompositeBaseHandle(tr, cur);
        var (bIds, _) = WallCompositeCommand.Candidates(civ, tr, WallCompositeCommand.BaseName, groundId);
        if (bIds.Count != 1 || bIds[0].Handle.ToString() != bh || tr.GetObject(bIds[0], OpenMode.ForRead) is not TinSurface b0)
            return (null, "정지면_DH는 옹벽 합성인데 그 합성 전 면을 못 찾았습니다");
        return (b0, "");
    }

    // ★[v114.3 · JACK 1105 소단 높이 · 재검토 M-C] 옛 사면 링 — 열쇠(계획선 핸들 · 전이 단계 · 경계 · 번들 제원 · 링 셈 구간 비트)가 같으면 다시 안 짓는다
    //   (DHWALLADD 한 번에 BuildGeom이 여러 번 · DHWALLCOMP ③-b가 옹벽마다 부른다 — 모두 같은 링). 지은 수 · 쓴 수 · 한 번 짓는 시간은 로그에
    static (string Key, WallByLine.OldBench? Cut, WallByLine.OldBench? Fill, string WhyC, string WhyF, long Ms)? _rings;
    static int _ringBuilt, _ringUsed;

    /// <summary>★[v114.3 · 계획 2판 1절 · 재검토 M-A] 옛 소단 단 표 — 정지면 생성과 같은 부르기(번들 경계 · 제원 · <see cref="CreateGradingCommand.RingZonesOf"/> 구간)로 링을 다시 만든다.</summary>
    internal static (WallByLine.OldBench? Cut, WallByLine.OldBench? Fill, string Note, string WhyC, string WhyF) OldBenchOf(GradingBundle reg, IGroundSurface g)
    {
        var zc = CreateGradingCommand.RingZonesOf(reg.CutWallZones); var zf = CreateGradingCommand.RingZonesOf(reg.FillWallZones);
        string key = (reg.PlanHandle ?? "") + "|" + GradingSettings.TransitionStage + "|" + WallByLine.OldBench.Fingerprint(reg.Boundary, reg.Params, zc, zf);
        _ringUsed++;
        static string Why(string wc, string wf) => (wc.Length > 0 ? " · 절토: " + wc : "") + (wf.Length > 0 ? " · 성토: " + wf : "");
        if (_rings is { } c && c.Key == key)
            return (c.Cut, c.Fill, $"링 다시 씀(한 번 짓기 {c.Ms} ms · 지음 {_ringBuilt} · 씀 {_ringUsed}) · 절토 {c.Cut?.K ?? 0}단 · 성토 {c.Fill?.K ?? 0}단" + Why(c.WhyC, c.WhyF), c.WhyC, c.WhyF);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        WallByLine.OldBench? oc = null, of = null; string wc = "", wf = "";
        try { oc = WallByLine.OldBench.Of(GradingGeometry.Build(reg.Boundary, g, reg.Params, true, zc), reg.Params, true, out wc); }
        catch (System.Exception ex) { wc = $"링을 못 만들었다({ex.GetType().Name}: {ex.Message})"; }
        try { of = WallByLine.OldBench.Of(GradingGeometry.Build(reg.Boundary, g, reg.Params, false, zf), reg.Params, false, out wf); }
        catch (System.Exception ex) { wf = $"링을 못 만들었다({ex.GetType().Name}: {ex.Message})"; }
        _ringBuilt++;
        _rings = (key, oc, of, wc, wf, sw.ElapsedMilliseconds);
        return (oc, of, $"링 지음 {sw.ElapsedMilliseconds} ms(지음 {_ringBuilt} · 씀 {_ringUsed}) · 절토 {oc?.K ?? 0}단 · 성토 {of?.K ?? 0}단 · 구간 절 {zc.Count} · 성 {zf.Count}" + Why(wc, wf), wc, wf);
    }

    /// <summary>★[v114.0] 짓기 재료 — 판정 + 기하(질문 없음). P0 · 원지반 삼각형은 경계 + 여유 상자만.
    /// ★[v114.3 · JACK 1105 소단 높이] 벽 소단 높이 = 옛 사면 소단 높이(옛 소단 방식 — 링에서 · H는 기하에 안 씀) · 링 꼭짓점이 합성 전 면과 맞는지 먼저 잰다(재검토 M-A).</summary>
    static WallByLine.Built BuildGeom(BoundaryCut.Result cut, IReadOnlyList<Point3> bd, GradingBundle region, List<(string, IReadOnlyList<Point3>)> bl,
        double hc, double tc, double hf, double tf, double slopeW, double faceRun, double grid, double reach, CachedGroundSurface g, CachedGroundSurface p0, bool topStep = false, double blockLen = 9,
        WallByLine.WallShape shape = WallByLine.WallShape.Out, double tierH = double.NaN, double tierB = double.NaN, double tierZ1 = double.NaN)
    {
        // ★[v117.3 · 계획 4절] 다단 — 소단 구역(깊이 D_T)이 선 안쪽으로 들어가므로 상자를 10 m 더(모자라면 Core가 «상자»로 거절)
        double box = reach + 5 + (shape == WallByLine.WallShape.Tiered ? 10 : 0);
        double sx0 = cut.CutRing.Concat(cut.Line).Min(q => q.X) - box, sx1 = cut.CutRing.Concat(cut.Line).Max(q => q.X) + box;
        double sy0 = cut.CutRing.Concat(cut.Line).Min(q => q.Y) - box, sy1 = cut.CutRing.Concat(cut.Line).Max(q => q.Y) + box;
        var p0Tris = p0.TrianglesIn(sx0, sy0, sx1, sy1);
        var gTris = g.TrianglesIn(sx0, sy0, sx1, sy1);
        System.Func<double, double, double?> gAt = (x, y) => g.TryGetElevation(x, y, out double z) ? z : null;
        System.Func<double, double, double?> pAt = (x, y) => p0.TryGetElevation(x, y, out double z) ? z : null;
        // ★[v117.0 · JACK 1111 · 계획 3절] 수직 옹벽 — 소단을 안 쓰므로 옛 사면 링(OldBenchOf) · 링 꼭짓점 일치(VertexMatch)를 건너뛴다(«옛 소단» 헛거절 막음) · 모양 · 기울기만 넘김
        if (shape is WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered)   // ★[v117.3] 다단도 같은 재료(단높이 · 소단 = 절토 Hc · Tc / 성토 Hf · Tf · 손 값이 있으면 그것)
            return WallByLine.Build(new WallByLine.Input
            {
                Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = region.Boundary, SlopeW = slopeW, FaceRun = faceRun, Grid = grid, Reach = reach,
                Ground = gAt, GroundTris = gTris, P0 = pAt, P0Tris = p0Tris, Blockers = bl, Shape = shape,
                Hc = hc, Tc = tc, Hf = hf, Tf = tf, TierH = tierH, TierB = tierB, TierZ1 = tierZ1,
            });
        var (oc, of, rnote, whyC, whyF) = OldBenchOf(region, g);
        // ★[재검토 M-A] 다시 만든 링 = 정지면을 만든 그 링인가 — 상자 안 · 그 쪽 사면 자리(데이라잇 고리 안 1 cm · 계획선 밖 1 cm) 링 꼭짓점이 합성 전 면 꼭짓점과 0.1 mm(평면) · 1 mm(높이) 안에서 겹치는 몫.
        //   0단계 실측(1105 현장): 136점 중 134점(98.5 %) — 안 겹친 2점은 0.22~0.24 mm 옆에 면 꼭짓점이 있고 그 자리 면 높이 = 링 높이(보조선 교차 끼움 자리로 보임 · 재지 않음).
        //   90 % 아래면 다른 링(구간 · 제원이 정지면을 만든 때와 다름)이라 짓지 않는다 — 틀린 높이로 짓느니 까닭을 남긴다
        var gf = NtsSupport.Factory();
        var vm = new System.Text.StringBuilder();
        var planB = NtsSupport.ToCleanPolygon(region.Boundary, gf);
        foreach (var (nm, obx, clip) in new[] { ("절토", oc, region.CutClipRing), ("성토", of, region.FillClipRing) })
        {
            if (obx == null) continue;
            if (clip == null || clip.Count < 3 || planB == null) { vm.Append($" · {nm} 꼭짓점 일치 못 잼(데이라잇 고리 없음)"); continue; }
            var clipP = NtsSupport.ToCleanPolygon(clip, gf);
            if (clipP == null) { vm.Append($" · {nm} 꼭짓점 일치 못 잼(데이라잇 고리가 다각형이 아님)"); continue; }
            var inP = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(clipP.Buffer(-0.01));
            var outP = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(planB.Buffer(0.01));
            var (n, hit, miss) = obx.VertexMatch(p0Tris, (x, y) => { var pt = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)); return inP.Contains(pt) && !outP.Contains(pt); }, sx0, sy0, sx1, sy1);
            vm.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " · {0} 꼭짓점 일치 {1}/{2}", nm, hit, n));
            if (n >= 8 && hit < 0.9 * n)
            {
                var no = new WallByLine.Built { Ok = false, Code = "옛 소단", Why = string.Format(System.Globalization.CultureInfo.InvariantCulture, "옛 사면 링이 정지면과 다름({0} 꼭짓점 {1}/{2}만 겹침) — 정지면을 다시 만든 뒤 하세요(구간 · 제원이 정지면을 만든 때와 다름)", nm, hit, n) };
                if (miss is { } m) no.Marks.Add(m);
                no.Log = $"  ⓪ 옛 소단 링 — {rnote}{vm}\n";
                return no;
            }
        }
        var b = WallByLine.Build(new WallByLine.Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = region.Boundary, Hc = hc, Tc = tc, Hf = hf, Tf = tf, SlopeW = slopeW, FaceRun = faceRun, Grid = grid, Reach = reach,
            Ground = gAt, GroundTris = gTris, P0 = pAt, P0Tris = p0Tris, Blockers = bl,
            LevelMode = WallByLine.LevelKind.OldBench, CutBench = oc, FillBench = of, RowDens = System.Math.Max(0.3, System.Math.Min(region.Params.VertexSpacing, 1.0)),
            CutBenchWhy = whyC, FillBenchWhy = whyF,   // ★[v115 · 설치 전 검토 S1] 링을 못 만든 까닭을 화면 까닭에
            TopStep = topStep, BlockLen = blockLen,   // ★[v116.3 · JACK 1109 옹벽 천단 선택]
        });
        b.Log = $"  ⓪ 옛 소단 링 — {rnote}{vm}\n" + b.Log;
        return b;
    }

    /// <summary>가상옹벽{번호}_DH · 선 · 폴리곤(이 트랜잭션) — 됐으면 면 핸들 · 아니면 빈 값 + 까닭.</summary>
    static (string Handle, string Block, string Note) BuildSurface(Database db, Transaction tr, WallByLine.Built b, int no, string region, ObjectId groundId)
    {
        string name = WallRec.WallSurfaceName(no);
        var wid = GradingBuilder.BuildVirtualSlope(db, tr, new List<List<Point3>>(), name, b.Lines, groundId, null, midOrd: 0.001);
        int bIn = GradingBuilder.LastIntended, bDef = GradingBuilder.LastDefined;
        string block = bDef < 0 ? "브레이크라인 수를 못 셌다" : bDef < bIn ? $"브레이크라인을 {bIn}개 중 {bDef}개만 받았다(줄이 빠진 면)" : "";
        try { WallTags.TagSurface(tr, wid, no, region); } catch { }
        WallDaylightBuilder.MarkSide(tr, wid, b.HasCut, block);
        string note = $"브레이크라인 의도 {bIn} / 정의됨 {bDef}";
        var tin = (TinSurface)tr.GetObject(wid, OpenMode.ForWrite);
        try { GradingBuilder.AddOuterBoundary(tin, b.Poly, midOrd: 0.001); }
        catch (System.Exception be)
        {
            // ★[v113.0 · 설치 전 검토 L1] 경계 없는 면을 남기지 않는다(다시 짓기는 트랜잭션을 안 물리므로 — 옛 핸들이 엉뚱한 면을 가리키지 않게)
            try { tin.Erase(); } catch { }
            return ("", "", $"⚠경계를 못 붙였다({be.GetType().Name}) — 면을 지웠다(이 옹벽은 안 지음)");
        }
        try { using var tc = tin.GetTriangles(false); note += $" · 삼각형 {tc.Count}개"; } catch { }
        try { note += " · " + GradingBuilder.MakeSurfaceVisible(db, tr, name, "DH-가상옹벽면", "DH-가상옹벽", 6); } catch { }
        // 줄(DH-가상옹벽선) · 바꿀 자리(DH-가상폴리곤) — 번호 표지(사면 변환으로 되돌릴 때 이 선을 고른다)
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        void Draw(IReadOnlyList<Point3> l, string layer, short aci, bool close)
        {
            var lid = GradingBuilder.EnsureLayer(db, tr, layer, aci);
            var pl = new Polyline3d { LayerId = lid };
            ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            foreach (var q in l) { var v = new PolylineVertex3d(new Point3d(q.X, q.Y, q.Z)); pl.AppendVertex(v); tr.AddNewlyCreatedDBObject(v, true); }
            if (close) pl.Closed = true;
            WallTags.Tag(db, tr, pl, no, region);
        }
        foreach (var r in b.Rows) Draw(r, "DH-가상옹벽선", 6, false);
        foreach (var r in b.EndFaces) Draw(r, "DH-가상옹벽선", 6, false);
        Draw(b.Poly, "DH-가상폴리곤", 4, true);
        return (wid.Handle.ToString(), block, note);
    }

    /// <summary>★[v113.0 · 화면 없는 탐침 · 대화 끝] 그린 점 · H · T로 목록 «대기»까지(한 트랜잭션 · 못 하면 물림) — 돌려주는 값: 번호(0 = 못 함) · 글.</summary>
    internal static (int No, string Note) Make(Document doc, IReadOnlyList<Point3> src, string srcHandle, double hc, double tc, double hf, double tf, bool topStep = false, double blockLen = 9,
        WallByLine.WallShape shape = WallByLine.WallShape.Out, double tierH = double.NaN, double tierB = double.NaN, double tierZ1 = double.NaN)
    {
        var db = doc.Database;
        bool vert = shape is WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered;   // ★[v117.3] 다단 = 수직 옹벽 + 소단(기록 · 자가검증 길 같음)
        var log = new System.Text.StringBuilder($"■ {Tag} {System.DateTime.Now:HH:mm:ss} — 폴리선 {src.Count}점{(srcHandle.Length > 0 ? $"(기존 폴리선 {srcHandle})" : "")}  · "
            + (vert ? $"수직 옹벽 1:{System.Math.Max(GradingSettings.MinSlope, 0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" : $"절토 H {hc:0.##} · T {tc:0.##} · 성토 H {hf:0.##} · T {tf:0.##}m")
            + (shape == WallByLine.WallShape.Tiered ? $" · 다단(단높이 {(double.IsNaN(tierH) ? "정지옵션" : tierH.ToString("0.##"))} · 소단 {(double.IsNaN(tierB) ? "정지옵션" : tierB.ToString("0.##"))} · 첫 소단 {(double.IsNaN(tierZ1) ? "자동" : tierZ1.ToString("0.###"))})" : "") + "\n");
        using var tr = db.TransactionManager.StartTransaction();
        (int, string) Stop(string why) { tr.Abort(); log.Append("  ⚠멈춤(안 바꿈) — " + why + "\n"); Log(log.ToString()); SelfCheckLog.Line(Tag, '⚠', "멈춤 — " + why, doc.Name); return (0, why); }
        var regions = GradingBundleStore.TryLoadAll(db, tr, out string rwhy);
        if (regions == null || regions.Count == 0) return Stop("정지면 번들이 없습니다 — " + rwhy);
        var region = regions[^1];
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return Stop("옹벽 목록을 못 읽어 덮지 않고 멈춘다 — " + lwhy);
        var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
        if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, region.GroundHandle);
        if (groundId.IsNull || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return Stop("원지반을 못 찾았습니다");
        var (p0, pwhy) = P0Of(tr, groundId);
        if (p0 == null) return Stop(pwhy);
        string p0h = p0.ObjectId.Handle.ToString();
        if (head.VerifiedP0.Length > 0 && head.VerifiedP0 != p0h)
            return Stop($"정지면_DH가 끝까지 만든 정지면이 아니다(확인된 {head.VerifiedP0} · 지금 {p0h}) — 정지면을 다시 만든 뒤 하세요");
        string regionH = region.PlanHandle ?? "";
        // 지난 «대기»(합성에 안 든 것) 치우기 — 옹벽 더하기(BuildWallAdd ②)와 같은 규칙
        var stale = walls.Where(w => w.State != 1).ToList();
        if (stale.Count > 0)
        {
            var nos = new HashSet<int>(stale.Select(w => w.No));
            var er = WallTags.Erase(db, tr, (n, r) => nos.Contains(n), false, groundId);
            walls.RemoveAll(w => w.State != 1);
            log.Append($"  지난 «대기» 옹벽 {string.Join(",", nos)}을 치웠다 — {er.Text}\n");
        }
        if (head.PlanHandle.Length > 0 && head.PlanHandle != regionH)
        {
            log.Append($"  ⚠옹벽 목록이 다른 구역(계획선 {head.PlanHandle})의 것 — 비우고 시작(그 옹벽은 그 구역 정지면에 굳어 있다)\n");
            walls.Clear();
        }
        head.PlanHandle = regionH; head.GroundHandle = groundId.Handle.ToString();
        var (bd, bl, bwhy) = BoundaryOf(db, tr, regions, walls, head, 0, out var clipM);
        if (bd == null) return Stop(bwhy);
        var cut = BoundaryCut.Classify(src, bd, region.Boundary, bl);
        if (!cut.Ok) return Stop($"{BoundaryCut.CodeName(cut.Code)} — {cut.Why}");
        var p = GradingSettings.ToParams();
        double slopeW = System.Math.Max(GradingSettings.MinSlope, 0), faceRun = p.MinFaceRun, grid = System.Math.Max(1.0, p.VertexSpacing);
        var gC = new CachedGroundSurface(gTin); var pC = new CachedGroundSurface(p0);
        var b = BuildGeom(cut, bd, region, bl, hc, tc, hf, tf, slopeW, faceRun, grid, System.Math.Max(0, GradingSettings.WallPolygonMargin), gC, pC, topStep, blockLen, shape, tierH, tierB, tierZ1);
        log.Append(b.Log);
        if (vert) SelfCheckLog.Line(Tag, b.Ok, b.SelfCheck, doc.Name);   // ★[v117.0 · 계획 1-6] 자가검증 한 줄(키트가 읽음 · 거절도)
        if (!b.Ok) return Stop($"{b.Code} — {b.Why}");
        // 번호 — 목록 · 남은 가상옹벽 면 중 큰 것 + 1(다시 안 쓴다)
        int maxNo = walls.Count > 0 ? walls.Max(w => w.No) : 0;
        var civ = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        foreach (ObjectId sid in civ.GetSurfaceIds())
            if (tr.GetObject(sid, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s0 && WallTags.IsWallSurface(s0.Name, out int nn0)) maxNo = System.Math.Max(maxNo, nn0);
        head.NextNo = System.Math.Max(System.Math.Max(head.NextNo, 1), maxNo + 1);
        int no = head.NextNo++;
        string wh, blk, note;
        try { (wh, blk, note) = BuildSurface(db, tr, b, no, regionH, groundId); }
        catch (System.Exception ex) { return Stop($"가상옹벽 면 짓기가 터졌다 — {ex.GetType().Name}: {ex.Message}"); }
        if (wh.Length == 0) return Stop(note);
        bool defer = WallListStore.ReadDefer(db, tr);
        var rec = new WallRec
        {
            // ★[v114.0] 종류 2 — 하나뿐인 Up · HeadZ는 표시용(절토가 있으면 절토 · 머리 = 가장 높은 면 머리) · 기하는 Faces · 바꿀 자리 고리
            No = no, State = defer && blk.Length == 0 ? 1 : 0, Up = b.Shape == 3 ? b.Dir > 0 : b.HasCut, Bench = -1, H = b.HasCut ? hc : hf, T = b.HasCut ? tc : tf, T0 = 0, T1 = 0,
            HeadZ = b.Faces.Count > 0 ? b.Faces.Max(f => f.Top.Count > 0 ? f.Top.Max(q => q.Z) : f.Level) : 0,
            Seg = cut.Line.ToList(), Poly = b.Poly, IsWall = b.IsWall, WallHandle = wh, Block = blk, Stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            P0Handle = blk.Length == 0 ? p0h : "", SlopeW = slopeW, FaceRun = faceRun, Grid = grid, RulerKind = 2,
            Kind = 2, Line = cut.Line.ToList(), Src = src.Select(q => new Point3(q.X, q.Y, 0)).ToList(), SrcHandle = srcHandle ?? "",
            Hc = hc, Tc = tc, Hf = hf, Tf = tf, Reach = System.Math.Max(0, GradingSettings.WallPolygonMargin), FootMax = b.FootMax, FootAtX = b.FootAt.X, FootAtY = b.FootAt.Y,
            TopStep = topStep, BlockLen = blockLen,
            ZoneF = b.ZoneF, ZoneC = b.ZoneC, Faces = b.Faces,
            BenchSamples = WallByLine.SamplesOf(b).Bench, FootSamples = WallByLine.SamplesOf(b).Foot,   // ★[v114.0 · 소단 높이 관문] 합성 되읽기가 Civil 면에서 다시 잰다
        };
        if (vert)
        {
            // ★[v117.0 · 계획 3절] 수직 옹벽 — 단높이 · 소단 · 천단 계단 없음 · H = 최고(표시) · 기록 판 9(모양 2)
            rec.Shape = 2; rec.Hc = rec.Tc = rec.Hf = rec.Tf = 0; rec.H = b.HMax; rec.T = 0; rec.TopStep = false; rec.FootMax = 0;
            // ★[v117.3 · 계획 4절] 다단으로 지어졌으면 모양 3 + 단높이 · 소단 · 첫 소단(자동이어도 쓴 값 — 다시 지어도 같은 높이) · 단이 필요 없었으면 모양 2(판 9 그대로)
            if (b.Shape == 3) { rec.Shape = 3; rec.TierH = b.TierH; rec.TierB = b.TierB; rec.TierZ1 = b.TierZ1; }
        }
        walls.Add(rec);
        WallListStore.Save(db, tr, head, walls);
        tr.Commit();
        if (vert && b.Shape == 3)
            log.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  ★다단 옹벽 {0}({1}) — {2}단 · 단높이 {3:0.##} · 소단 {4:0.##} · 첫 소단 {5:F3} · 바깥 최고 {6:F2} m · 안쪽 최고 {7:F2} m · 끊김 {8}곳 — {9}{10}{11} · 목록 {12}\n",
                no, b.Dir > 0 ? "절토" : "성토", b.TierN, b.TierH, b.TierB, b.TierZ1, b.OuterHMax, b.InnerHMax, b.BreakN, note,
                b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "", blk.Length > 0 ? $" · ⚠합성 막음: {blk}" : "", rec.State == 1 ? "미룬 옹벽(넣음)" : "«대기»"));
        else if (vert)
            log.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  ★수직 옹벽 {0}({1}) — 길이 {2:F2} m(절토 {3:F2} · 성토 {4:F2}) · 최고 {5:F2} m · 면적 {6:F1} ㎡ · 바깥 {7} · 쌍 {8} · 바늘 {9} — {10}{11}{12} · 목록 {13}\n",
                no, (b.HasCut ? "절토" : "") + (b.HasCut && b.HasFill ? "·" : "") + (b.HasFill ? "성토" : ""), b.LineLen, b.LenC, b.LenF, b.HMax, b.Area, b.OutsideN, b.PairsUnder01, b.Needles, note,
                b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "", blk.Length > 0 ? $" · ⚠합성 막음: {blk}" : "", rec.State == 1 ? "미룬 옹벽(넣음)" : "«대기»")
                + $"  자가검증 — 옛 경계 {cut.AreaOld:F2} = 새 {cut.AreaNew:F2} + 잘라낼 {cut.AreaCut:F2}㎡({(System.Math.Abs(cut.AreaOld - cut.AreaNew - cut.AreaCut) <= 0.01 ? "맞음" : "⚠어긋남")})\n");
        else
        log.Append($"  ★선 옹벽 {no}({(b.HasCut ? "절토" : "")}{(b.HasCut && b.HasFill ? "·" : "")}{(b.HasFill ? "성토" : "")} · 부분 {b.Parts.Count} · 면 토막 {b.Faces.Count} · 마구리 {b.EndFaceN}{(topStep ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " · 천단 계단 {0:0.##} m(가른 줄기 {1})", blockLen, b.StepCuts) : "")}) — {note}"
                 + (b.FootMax > 0 ? $" · ⚠선 바깥 최대 {b.FootMax:F2} m({b.FootAt.X:F1}, {b.FootAt.Y:F1})" : "")
                 + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "") + (blk.Length > 0 ? $" · ⚠합성 막음: {blk}" : "")
                 + $" · 목록 {(rec.State == 1 ? "미룬 옹벽(넣음)" : "«대기»")}\n"
                 + $"  자가검증 — 옛 경계 {cut.AreaOld:F2} = 새 {cut.AreaNew:F2} + 잘라낼 {cut.AreaCut:F2}㎡({(System.Math.Abs(cut.AreaOld - cut.AreaNew - cut.AreaCut) <= 0.01 ? "맞음" : "⚠어긋남")}) · 0.1 mm 쌍 {b.PairsUnder01} · 바늘 {b.Needles}\n");
        Log(log.ToString());
        SelfCheckLog.Line(Tag, blk.Length == 0, $"선 옹벽 {no} 지음 — 부분 {b.Parts.Count} · 선 바깥 {b.FootMax:F2} m · 교차각 {cut.AngleA:F0}°/{cut.AngleB:F0}°" + (blk.Length > 0 ? " · 합성 막음 " + blk : ""), doc.Name);
        GradingSettings.LastAddNo = blk.Length == 0 ? no : 0; GradingSettings.LastAddDeferred = rec.State == 1;
        GradingSettings.LastAddNote = blk.Length == 0 ? "" : $"옹벽 {no}는 지었지만 합성을 막을 까닭이 있다 — {blk}";
        return (no, note);
    }


    /// <summary>★[v113.0] [옹벽 추가](DHWALLADD · JACK 1104 — 옛 [옹벽 변환]의 L) 대화.</summary>
    internal static void Run(Document doc)
    {
        var ed = doc.Editor; var db = doc.Database;
        List<Point3>? bd; List<(string, IReadOnlyList<Point3>)> bl; GradingBundle region; List<IReadOnlyList<Point3>> clipR;   // clipR = 옛 여유 비켜 가기(v113) — 쓰지 않음
        CachedGroundSurface gC, pC;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var regions = GradingBundleStore.TryLoadAll(db, tr, out string rwhy);
            if (regions == null || regions.Count == 0) { ed.WriteMessage($"\n[{Tag}] 정지면 번들이 없습니다 — [정지면 생성]을 먼저 하세요."); return; }
            region = regions[^1];
            if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) { ed.WriteMessage($"\n[{Tag}] 옹벽 목록을 못 읽었습니다 — {lwhy}"); return; }
            string err;
            (bd, bl, err) = BoundaryOf(db, tr, regions, walls.Where(w => w.State == 1), head, 0, out clipR);
            if (bd == null) { ed.WriteMessage($"\n[{Tag}] {err} — 아무것도 안 바꿨습니다."); return; }
            var groundId = NoriCommand.FindByHandle(db, GradingSettings.LastGroundHandle);
            if (groundId.IsNull) groundId = NoriCommand.FindByHandle(db, region.GroundHandle);
            if (groundId.IsNull || tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) { ed.WriteMessage($"\n[{Tag}] 원지반을 못 찾았습니다."); return; }
            var (p0, pwhy) = P0Of(tr, groundId);
            if (p0 == null) { ed.WriteMessage($"\n[{Tag}] {pwhy} — 아무것도 안 바꿨습니다."); return; }
            gC = new CachedGroundSurface(gTin); pC = new CachedGroundSurface(p0);
            tr.Commit();
        }
        var bdF = bd; var blF = bl; var regF = region;
        // ★[v114.0 · 계획 2절 기능 스위치] 켜면 선 길을 숨긴다(옛 기록은 지우지 않음)
        if (GradingSettings.WallLineOff || WallByLine.Off) { ed.WriteMessage($"\n[{Tag}] ⚠선 옹벽(v114)이 꺼져 있습니다(정지옵션 WallLineOff) — 아무것도 안 바꿨습니다."); return; }
        var got = WallLineDraw.Run(doc, Tag, bdF, regF.Boundary, pts => BoundaryCut.Classify(pts, bdF, regF.Boundary, blF));
        if (got == null) return;
        // ★★[v117.0 · JACK 1111 «선 그은 부분은 그냥 수직 1:0.01로 끝까지» · 계획 3절] 새로 그리는 부지 옹벽 = 수직 옹벽 — 묻는 것 없음([다시그리기(S)] · Enter = 적용).
        //   옛 «제원 절토(C) · 성토(F) · 천단(P)» 묻기는 없앰(소단 · 단높이 · 천단 계단이 없다) — 옛 기록(Shape 0)은 다시 짓기(Rebuild)에서 옛 모양 그대로
        var p = GradingSettings.ToParams();
        double slopeW = System.Math.Max(GradingSettings.MinSlope, 0), faceRun = p.MinFaceRun, grid = System.Math.Max(1.0, p.VertexSpacing), margin = System.Math.Max(0, GradingSettings.WallPolygonMargin);
        var rp = regF.Params;
        double hc = rp.BenchHeightOf(true), tc = rp.BenchWidthOf(true), hf = rp.BenchHeightOf(false), tf = rp.BenchWidthOf(false);
        // ★★[v117.3 · JACK 1111 «거기서 경계 중심 방향으로 소단» · 계획 4절] 기본 = 다단(1차 최고 ≤ 단높이면 저절로 수직 그대로) · [수직만(V)] = v117 수직 옹벽
        bool tierMode = true; double tH = double.NaN, tB = double.NaN, tZ = double.NaN;
        WallByLine.WallShape shape = WallByLine.WallShape.Tiered;
        WallByLine.Built Geo() { shape = tierMode ? WallByLine.WallShape.Tiered : WallByLine.WallShape.Vertical; return BuildGeom(got.Cut, bdF, regF, blF, hc, tc, hf, tf, slopeW, faceRun, grid, margin, gC, pC, false, 9, shape, tH, tB, tZ); }
        var b = Geo();
        double mk = System.Math.Max(0.3, System.Math.Sqrt(System.Math.Pow(bdF.Max(q => q.X) - bdF.Min(q => q.X), 2) + System.Math.Pow(bdF.Max(q => q.Y) - bdF.Min(q => q.Y), 2)) * 0.006);
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        while (true)
        {
            // 미리보기 — 벽 띠 자리(초록 3) · L(새 데이라잇 = 그은 선 · 빨강 1) · 가름점 C(빨간 X)
            // 미리보기 — 벽 띠 · 소단 자리(초록 3) · 안쪽 단 머리 줄(초록 3) · L(빨강 1) · 가름점 C · 바깥 단 끊김(빨간 X)
            bool tierUi = tierMode && b.Shape == 3;
            List<Autodesk.AutoCAD.DatabaseServices.Entity> live;
            if (b.Ok)
            {
                var more = new List<IReadOnlyList<Point3>>();
                foreach (var z in b.ZoneF.Concat(b.ZoneC)) more.Add(z.Append(z[0]).ToList());
                more.AddRange(b.TierRows);
                live = WallLineDraw.Show(b.Points.Select(q => (q.X, q.Y)).Concat(b.BreakAt), got.Cut.Line, mk, 2, more, 3);
                live.AddRange(WallLineDraw.Show(System.Array.Empty<(double, double)>(), null, mk, 2, b.Foot.Cast<IReadOnlyList<Point3>>(), 1));
            }
            else live = WallLineDraw.Show(b.Marks, got.Pts, mk);
            string spec = !b.Ok ? $"〈⚠{b.Why}〉"
                : b.Shape == 3 ? string.Format(ic, "〈다단 {0}단 · 단높이 {1:0.##} · 소단 {2:0.##} · 첫 소단 {3:F2}({4}) · 바깥 최고 {5:F2} m · 끊김 {6}곳{7}〉", b.TierN, b.TierH, b.TierB, b.TierZ1,
                    double.IsNaN(tZ) ? "자동" : "손", b.OuterHMax, b.BreakN, b.BreakN > 0 ? " — 구간 나눔 권함(다음 판)" : "")
                : string.Format(ic, "〈수직 1:{0} · 길이 {1:F2} m(절토 {2:F2} · 성토 {3:F2}) · 최고 {4:F2} m · 면적 {5:F1} ㎡{6}〉", slopeW.ToString("0.###", ic), b.LineLen, b.LenC, b.LenF, b.HMax, b.Area,
                    b.TierNone ? string.Format(ic, " · 단높이 {0:0.##} 안 — 단 없이 됨", b.TierH) : "");
            PromptResult r;
            try
            {
                var pk = new PromptKeywordOptions(tierUi ? $"\n[{Tag}] {spec} [단높이(H)/소단폭(B)/첫소단(Z)/수직만(V)/다시그리기(S)] (Enter = 적용): "
                    : !tierMode ? $"\n[{Tag}] {spec} [다단(T)/다시그리기(S)] (Enter = 적용): " : $"\n[{Tag}] {spec} [다시그리기(S)] (Enter = 적용): ");
                pk.Keywords.Add("S", "S", "다시그리기(S)");
                if (tierUi) { pk.Keywords.Add("H", "H", "단높이(H)"); pk.Keywords.Add("B", "B", "소단폭(B)"); pk.Keywords.Add("Z", "Z", "첫소단(Z)"); pk.Keywords.Add("V", "V", "수직만(V)"); }
                else pk.Keywords.Add("V", "V", "수직만(V)", false, true);   // 숨김 — 스크립트의 V 한 줄이 단이 필요 없는 선에서도 그대로 지나가게
                if (!tierMode) pk.Keywords.Add("T", "T", "다단(T)");
                pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
                r = ed.GetKeywords(pk);
            }
            finally { WallLineDraw.Drop(live); }
            if (r.Status == PromptStatus.Cancel) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return; }
            string kw = r.Status == PromptStatus.None ? "" : (r.StringResult ?? "").Trim().ToUpperInvariant();
            if (kw == "S")
            {
                var g2 = WallLineDraw.Run(doc, Tag, bdF, regF.Boundary, pts => BoundaryCut.Classify(pts, bdF, regF.Boundary, blF));
                if (g2 == null) return;
                got = g2;
                b = Geo();
                continue;
            }
            if (kw == "V") { if (tierMode) { tierMode = false; b = Geo(); } continue; }
            if (kw == "T") { tierMode = true; b = Geo(); continue; }
            if (kw is "H" or "B" or "Z")
            {
                double cur = kw == "H" ? b.TierH : kw == "B" ? b.TierB : b.TierZ1;
                string now = double.IsFinite(cur) ? cur.ToString(kw == "Z" ? "0.###" : "0.##", ic) : "—";
                // Enter = 그대로(None — 기본값을 쓰지 않는다: 자동 첫 소단이 Enter 한 번에 손 값으로 바뀌지 않게)
                var pdo = new PromptDoubleOptions(kw == "H" ? $"\n단높이 (m · 지금 {now} · Enter = 그대로)" : kw == "B" ? $"\n소단 폭 (m · 지금 {now} · Enter = 그대로)" : $"\n첫 소단 높이 (m · 지금 {now}{(double.IsNaN(tZ) ? " 자동" : "")} · Enter = 그대로) [자동(A)]")
                { AllowNegative = kw == "Z", AllowZero = kw != "H", AllowNone = true };
                if (kw == "Z") { pdo.Keywords.Add("A", "A", "자동(A)"); pdo.AppendKeywordsToMessage = false; }
                var dr = ed.GetDouble(pdo);
                if (dr.Status == PromptStatus.Keyword && (dr.StringResult ?? "").Trim().ToUpperInvariant() == "A") tZ = double.NaN;
                else if (dr.Status == PromptStatus.OK)
                {
                    if (kw == "H") { if (dr.Value < 0.5 || dr.Value > 30) ed.WriteMessage("\n → 범위 밖 값입니다(단높이 0.5~30 m) — 그대로."); else tH = dr.Value; }
                    else if (kw == "B") { if (dr.Value > 20) ed.WriteMessage("\n → 범위 밖 값입니다(소단 폭 0~20 m) — 그대로."); else tB = dr.Value; }
                    else tZ = dr.Value;
                }
                b = Geo();
                continue;
            }
            if (!b.Ok) { ed.WriteMessage($"\n[{Tag}] {b.Why} — {(tierUi ? "단을 바꾸거나 수직만(V) · " : "")}다시 그리세요(S)."); continue; }
            break;
        }
        ed.WriteMessage($"\n[{Tag}] {(b.Shape == 3 ? "다단 옹벽" : "수직 옹벽")}을 짓습니다…");
        var (no, note) = Make(doc, got.Pts, got.SrcHandle, hc, tc, hf, tf, false, 9, shape, tH, tB, tZ);
        if (no == 0)
        {
            string why = GradingSettings.LastAddNote.Length > 0 ? GradingSettings.LastAddNote : note;
            ed.WriteMessage($"\n[{Tag}] ⚠{why} — 아무것도 안 바꿨습니다.");
            if (!SelfCheckLog.Headless) try { BusyPopup.Close(); AcadApp.ShowAlertDialog($"폴리선 옹벽을 짓지 않았습니다(정지면_DH · 지난 옹벽은 그대로).\n\n{why}"); } catch { }
            return;
        }
        Kick(doc, no);
    }

    /// <summary>★[v116.3 · JACK 1109 옹벽 천단 선택] 천단 묻기 — 경사(L) · 계단(T) · 계단이면 블록 길이(m · 1~100 · Enter = 지금 값). 진입도로 [옹벽 추가]도 쓴다.</summary>
    internal static (bool Step, double Block) AskTop(Autodesk.AutoCAD.EditorInput.Editor ed, bool step, double block)
    {
        var pk = new PromptKeywordOptions($"\n천단 〈지금 {(step ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "계단 {0:0.##} m", block) : "경사")}〉 [경사(L)/계단(T)]: ");
        pk.Keywords.Add("L", "L", "경사(L)"); pk.Keywords.Add("T", "T", "계단(T)");
        pk.Keywords.Default = step ? "T" : "L"; pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
        var r = ed.GetKeywords(pk);
        if (r.Status == PromptStatus.Cancel) return (step, block);
        string kw = r.Status == PromptStatus.None ? (step ? "T" : "L") : (r.StringResult ?? "").Trim().ToUpperInvariant();
        if (kw != "T") return (false, block);
        var pdo = new PromptDoubleOptions("\n블록 길이 (m — 이 길이마다 수평 · 경계에 단)") { DefaultValue = block > 0 ? block : 9, UseDefaultValue = true, AllowNegative = false, AllowZero = false };
        var dr = ed.GetDouble(pdo);
        if (dr.Status == PromptStatus.OK)
        {
            if (dr.Value < 1 || dr.Value > 100) ed.WriteMessage("\n → 범위 밖 값입니다(블록 1~100 m) — 지금 값 그대로.");
            else block = dr.Value;
        }
        return (true, block > 0 ? block : 9);
    }

    /// <summary>지은 뒤 — 미루기 켬이면 알리고 끝 · 아니면 합성(DHWALLCOMP)을 이어 태운다(옹벽 더하기와 같은 출구).</summary>
    internal static void Kick(Document doc, int no)
    {
        var ed = doc.Editor;
        if (GradingSettings.LastAddDeferred)
        {
            ed.WriteMessage($"\n[{Tag}] 옹벽 {no}를 지었습니다 — 합성은 미룹니다(정지 창 [옹벽 합성]).");
            if (!SelfCheckLog.Headless) try { GradingPalette.RefreshComposite(); } catch { }
            return;
        }
        ed.WriteMessage($"\n[{Tag}] 옹벽 {no}를 지었습니다 — 이어서 정지면_DH에 합성합니다(바꿔 끼움 · 지난 옹벽과 함께)…");
        GradingSettings.AutoCompDoc = doc.Name; GradingSettings.AutoCompNo = no;
        try { doc.SendStringToExecute("DHWALLCOMP ", true, false, true); }
        catch (System.Exception sx)
        {
            GradingSettings.AutoCompDoc = ""; GradingSettings.AutoCompNo = 0;
            ed.WriteMessage($"\n[{Tag}] ⚠합성을 못 태웠다({sx.GetType().Name}) — 명령줄에 DHWALLCOMP를 치세요");
        }
    }

    /// <summary>★[v113.0] 정지면을 다시 지은 뒤 같은 번호로 다시 짓기(DHWALLCOMP ③-b) — 저장한 원본 폴리선을 새 경계로 다시 판정 ·
    /// 제자리(교점 1 mm · 단 수 · 바닥 1 mm)면 결과물을 지우고 다시 짓고 빈 값 · 아니면 까닭(결과물은 그대로 · 부른 쪽이 «다시 짓기 실패» 보류).</summary>
    internal static string Rebuild(Database db, Transaction tr, WallRec w, string region, ObjectId groundId, System.Text.StringBuilder log)
    {
        if (w.Src.Count < 2) return "저장한 폴리선 원본이 없다";
        var regions = GradingBundleStore.TryLoadAll(db, tr, out string rwhy);
        if (regions == null || regions.Count == 0) return "정지면 번들이 없다 — " + rwhy;
        var reg = regions[^1];
        if (!WallListStore.TryLoad(db, tr, out var head, out var walls, out string lwhy)) return "옹벽 목록을 못 읽었다 — " + lwhy;
        if (tr.GetObject(groundId, OpenMode.ForRead) is not TinSurface gTin) return "원지반을 못 찾았다";
        var (p0, pwhy) = P0Of(tr, groundId);
        if (p0 == null) return pwhy;
        var (bd, bl, bwhy) = BoundaryOf(db, tr, regions, walls.Where(x => x.State == 1), head, w.No, out var clipB);
        if (bd == null) return bwhy;
        var cut = BoundaryCut.Classify(w.Src, bd, reg.Boundary, bl);
        if (!cut.Ok) return $"폴리선 옹벽 — 새 경계에서 {BoundaryCut.CodeName(cut.Code)}: {cut.Why}";
        var p = GradingSettings.ToParams();
        double sW = w.SlopeW >= 0 ? w.SlopeW : System.Math.Max(GradingSettings.MinSlope, 0), fR = w.FaceRun >= 0 ? w.FaceRun : p.MinFaceRun, gr = w.Grid >= 0 ? w.Grid : System.Math.Max(1.0, p.VertexSpacing);
        if (WallByLine.Off || GradingSettings.WallLineOff) return "선 옹벽(v114)이 꺼져 있다(WallLineOff) — 쉰다(지우지 않음)";
        // ★[v117.0 · 계획 3절] 모양 그대로 다시 짓기 — 수직 옹벽(Shape 2)은 수직으로 · 옛 기록(Shape 0)은 옛 모양 그대로(옮김은 다음 판)
        //   ★[v117.3] 다단(모양 3)은 저장한 단높이 · 소단 · 첫 소단 그대로(같은 높이) — 단높이 · 소단은 이 칸 값(TierH · TierB)으로
        var shape = w.Shape == 3 ? WallByLine.WallShape.Tiered : w.Shape == 2 ? WallByLine.WallShape.Vertical : WallByLine.WallShape.Out;
        var b = BuildGeom(cut, bd, reg, bl, w.Hc, w.Tc, w.Hf, w.Tf, sW, fR, gr, w.Reach, new CachedGroundSurface(gTin), new CachedGroundSurface(p0), w.TopStep, w.BlockLen, shape,
            w.Shape == 3 ? w.TierH : double.NaN, w.Shape == 3 ? w.TierB : double.NaN, w.Shape == 3 ? w.TierZ1 : double.NaN);
        if (shape is WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered) SelfCheckLog.Line(Tag, b.Ok, "다시 짓기 " + b.SelfCheck, "");
        if (!b.Ok) return $"폴리선 옹벽 — 새 정지면에서 못 지음: {b.Why}";
        string drift = WallByLine.Drift(w, b);   // ★[v114.0 · 설치 전 검토 7] 저장 값과 10 % 넘게 다르면 ⚠(막지 않음)
        // ★[v114.3 · JACK 1105 소단 높이 · 계획 2판 3절 LOW] 옛 규칙(선기준)으로 지은 옹벽을 새 규칙(옛 소단)으로 다시 지으면 소단 높이가 바뀐다 — 한 줄 남긴다(막지 않음)
        //   ★[v117.0] 수직 옹벽은 소단이 없다 — 견주지 않음(Drift · SamePlace는 그대로)
        if (shape is not (WallByLine.WallShape.Vertical or WallByLine.WallShape.Tiered))
        {
            static string Lv(IEnumerable<WallByLine.FaceSeg> fs) => string.Join(" · ", fs.Where(f => f.Kind == 0).Select(f => System.Math.Round(f.Level, 2)).Distinct().OrderBy(z => z).Select(z => z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            string lo = Lv(w.Faces), ln = Lv(b.Faces);
            if (lo.Length > 0 && lo != ln) drift += (drift.Length > 0 ? " · " : "") + $"⚠옹벽 {w.No} 모양 바뀜(소단 {lo} → {ln})";
        }
        string moved = WallByLine.SamePlace(w, cut);
        if (moved.Length > 0) return "폴리선 옹벽 자리가 움직였다 — " + moved + " · [옹벽→사면]으로 되돌리거나 다시 그리세요";
        var er = WallTags.Erase(db, tr, (n, r) => n == w.No && (r.Length == 0 || r == region), false, groundId);
        var (wh, blk, note) = BuildSurface(db, tr, b, w.No, region, groundId);
        if (wh.Length == 0) return note;
        w.WallHandle = wh; w.PureHandle = ""; w.Block = blk; w.Poly = b.Poly; w.IsWall = b.IsWall; w.ZoneF = b.ZoneF; w.ZoneC = b.ZoneC; w.Faces = b.Faces; (w.BenchSamples, w.FootSamples) = WallByLine.SamplesOf(b); w.FootMax = b.FootMax; w.FootAtX = b.FootAt.X; w.FootAtY = b.FootAt.Y; w.Line = cut.Line.ToList(); w.Seg = cut.Line.ToList();
        log.AppendLine($"   폴리선 옹벽 {w.No} 다시 짓기 — 지운 것 {er.Text} · {note}" + (drift.Length > 0 ? " · " + drift : ""));
        foreach (var line in b.Log.Split('\n')) if (line.Trim().Length > 0) log.AppendLine("      " + line.Trim());
        return blk.Length > 0 ? "합성을 막을 까닭이 생겼다 — " + blk : "";
    }
}
