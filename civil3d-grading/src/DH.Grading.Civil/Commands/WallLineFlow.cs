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

/// <summary>★★★[v113.0 · JACK 1104 «옹벽 폴리선 — 경계 2점 자르기 + 안쪽 다단»] 부지 [옹벽 변환] 폴리선(L) 길 — 계획 4장.
/// <para>폴리선 그리기 · 고르기(<see cref="WallLineDraw"/>) → 경계 판정(Core <see cref="BoundaryCut"/>) → 단높이 · 소단(Enter = 정지옵션) · 미리보기 →
/// 같은 트랜잭션에서 가상옹벽{번호}_DH(브레이크라인 = Core <see cref="WallByLine"/>)를 짓고 옹벽 목록 종류 1 «대기»로 적고 → 합성(DHWALLCOMP · 바꿔 끼움)을 이어 태운다.
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
        foreach (var w in walls.Where(w => w.Kind == 1 && w.No != selfNo && w.Src.Count >= 2).OrderBy(w => w.No))
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

    /// <summary>짓기 재료 — 판정 + 기하(질문 없음). P0 삼각형은 Z 둘레만.</summary>
    static WallByLine.Built BuildGeom(BoundaryCut.Result cut, IReadOnlyList<Point3> bd, GradingBundle region, List<(string, IReadOnlyList<Point3>)> bl,
        double h, double t, double slopeW, double faceRun, double grid, double margin, CachedGroundSurface g, CachedGroundSurface p0, IEnumerable<IReadOnlyList<Point3>>? clip = null)
    {
        double mnx = bd.Min(q => q.X) - margin - 5, mxx = bd.Max(q => q.X) + margin + 5, mny = bd.Min(q => q.Y) - margin - 5, mxy = bd.Max(q => q.Y) + margin + 5;
        // 측선 둘레만 — A · B와 가장 가까운 계획선 점을 품는 상자
        double sx0 = System.Math.Min(cut.A.X, cut.B.X), sx1 = System.Math.Max(cut.A.X, cut.B.X), sy0 = System.Math.Min(cut.A.Y, cut.B.Y), sy1 = System.Math.Max(cut.A.Y, cut.B.Y);
        foreach (var e in new[] { cut.A, cut.B }) { var n = WallByLine.Nearest(region.Boundary, e.X, e.Y); sx0 = System.Math.Min(sx0, n.X); sx1 = System.Math.Max(sx1, n.X); sy0 = System.Math.Min(sy0, n.Y); sy1 = System.Math.Max(sy1, n.Y); }
        var p0Tris = p0.TrianglesIn(sx0 - 2, sy0 - 2, sx1 + 2, sy1 + 2);
        System.Func<double, double, double?> gAt = (x, y) => g.TryGetElevation(x, y, out double z) ? z : null;
        System.Func<double, double, double?> pAt = (x, y) => p0.TryGetElevation(x, y, out double z) ? z : null;
        var inp = new WallByLine.Input
        {
            Line = cut.Line, Bd = bd, NewRing = cut.NewRing, CutRing = cut.CutRing, Plan = region.Boundary, H = h, T = t, SlopeW = slopeW, FaceRun = faceRun, Grid = grid, Margin = margin,
            Ground = gAt, P0 = pAt, P0Tris = p0Tris, Blockers = bl, MarginClip = clip,
        };
        var b = WallByLine.Build(inp);
        // 여유 밖 P0가 없으면(정지면이 경계에서 끝남) 여유 없이 다시
        if (b.Ok && b.MarginUsed && b.Lines[0].Any(q => pAt(q.X, q.Y) == null))
        {
            inp.Margin = 0;
            var b2 = WallByLine.Build(inp);
            b2.Warn.Insert(0, "정지면_DH가 경계 + 여유 자리를 덮지 않아 여유 없이 지었다");
            return b2;
        }
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
        WallDaylightBuilder.MarkSide(tr, wid, b.Up, block);
        string note = $"브레이크라인 의도 {bIn} / 정의됨 {bDef}";
        var tin = (TinSurface)tr.GetObject(wid, OpenMode.ForWrite);
        try { GradingBuilder.AddOuterBoundary(tin, b.Zone, midOrd: 0.001); }
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
        Draw(b.Zone, "DH-가상폴리곤", 4, true);
        return (wid.Handle.ToString(), block, note);
    }

    /// <summary>★[v113.0 · 화면 없는 탐침 · 대화 끝] 그린 점 · H · T로 목록 «대기»까지(한 트랜잭션 · 못 하면 물림) — 돌려주는 값: 번호(0 = 못 함) · 글.</summary>
    internal static (int No, string Note) Make(Document doc, IReadOnlyList<Point3> src, string srcHandle, double h, double t)
    {
        var db = doc.Database;
        var log = new System.Text.StringBuilder($"■ {Tag} {System.DateTime.Now:HH:mm:ss} — 폴리선 {src.Count}점{(srcHandle.Length > 0 ? $"(기존 폴리선 {srcHandle})" : "")} · 단높이 {h:0.##} · 소단 {t:0.##}m\n");
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
        var b = BuildGeom(cut, bd, region, bl, h, t, slopeW, faceRun, grid, System.Math.Max(0, GradingSettings.WallPolygonMargin), gC, pC, clipM);
        log.Append(b.Log);
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
            No = no, State = defer && blk.Length == 0 ? 1 : 0, Up = b.Up, Bench = -1, H = h, T = t, T0 = 0, T1 = 0, HeadZ = b.HeadZ,
            Seg = cut.Line.ToList(), Poly = b.Zone, IsWall = b.ZoneSide, WallHandle = wh, Block = blk, Stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            P0Handle = blk.Length == 0 ? p0h : "", SlopeW = slopeW, FaceRun = faceRun, Grid = grid, RulerKind = 2,
            Kind = 1, Line = cut.Line.ToList(), Src = src.Select(q => new Point3(q.X, q.Y, 0)).ToList(), PadZ = b.Up ? b.PadMin : b.PadMax, Tiers = b.Tiers, CapZ = b.CapZ, SrcHandle = srcHandle ?? "",
        };
        walls.Add(rec);
        WallListStore.Save(db, tr, head, walls);
        tr.Commit();
        log.Append($"  ★폴리선 옹벽 {no}({rec.Side} · 단 {b.Tiers} · 바닥 {b.PadMin:F3}{(b.PadMax - b.PadMin > 1e-6 ? $"~{b.PadMax:F3}" : "")} · 머리 {b.HeadZ:F3} · 뚜껑 {b.CapZ:F2}m) — {note}"
                 + (b.Warn.Count > 0 ? " · ⚠" + string.Join(" · ⚠", b.Warn) : "") + (blk.Length > 0 ? $" · ⚠합성 막음: {blk}" : "")
                 + $" · 목록 {(rec.State == 1 ? "미룬 옹벽(넣음)" : "«대기»")}\n"
                 + $"  자가검증 — 옛 경계 {cut.AreaOld:F2} = 새 {cut.AreaNew:F2} + 잘라낼 {cut.AreaCut:F2}㎡({(System.Math.Abs(cut.AreaOld - cut.AreaNew - cut.AreaCut) <= 0.01 ? "맞음" : "⚠어긋남")}) · 측선 {Dist(b.A, b.A2):F2} · {Dist(b.B, b.B2):F2}m · 필요 폭 {b.Need:F2} · 가장 좁은 곳 {b.Narrowest:F2}m\n");
        Log(log.ToString());
        SelfCheckLog.Line(Tag, blk.Length == 0, $"폴리선 옹벽 {no} 지음 — {rec.Side} {b.Tiers}단 · 교차각 {cut.AngleA:F0}°/{cut.AngleB:F0}°" + (blk.Length > 0 ? " · 합성 막음 " + blk : ""), doc.Name);
        GradingSettings.LastAddNo = blk.Length == 0 ? no : 0; GradingSettings.LastAddDeferred = rec.State == 1;
        GradingSettings.LastAddNote = blk.Length == 0 ? "" : $"옹벽 {no}는 지었지만 합성을 막을 까닭이 있다 — {blk}";
        return (no, note);
    }

    static double Dist(Point3 a, Point3 b) => System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>★[v113.0] [옹벽 추가](DHWALLADD · JACK 1104 — 옛 [옹벽 변환]의 L) 대화.</summary>
    internal static void Run(Document doc)
    {
        var ed = doc.Editor; var db = doc.Database;
        List<Point3>? bd; List<(string, IReadOnlyList<Point3>)> bl; GradingBundle region; List<IReadOnlyList<Point3>> clipR;
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
        var got = WallLineDraw.Run(doc, Tag, bdF, regF.Boundary, pts => BoundaryCut.Classify(pts, bdF, regF.Boundary, blF));
        if (got == null) return;
        // 제원 — 기본 = 정지옵션의 그쪽 값(절 · 성은 기하로 가린다)
        var p = GradingSettings.ToParams();
        double slopeW = System.Math.Max(GradingSettings.MinSlope, 0), faceRun = p.MinFaceRun, grid = System.Math.Max(1.0, p.VertexSpacing), margin = System.Math.Max(0, GradingSettings.WallPolygonMargin);
        // 절 · 성은 짓기 ①(원지반 − 바닥)에서 정해진다 — 폭 · 측선 거절이어도 방향은 정해져 있다(원지반 · 섞임 거절이면 절토 기본값)
        var b = BuildGeom(got.Cut, bdF, regF, blF, p.BenchHeightOf(true), p.BenchWidthOf(true), slopeW, faceRun, grid, margin, gC, pC, clipR);
        bool up = b.Code == "원지반" || b.Code == "섞임" || b.Code == "재료" || b.Up;
        double h = p.BenchHeightOf(up), t = p.BenchWidthOf(up);
        if (!up) b = BuildGeom(got.Cut, bdF, regF, blF, h, t, slopeW, faceRun, grid, margin, gC, pC, clipR);
        double mk = System.Math.Max(0.3, System.Math.Sqrt(System.Math.Pow(bdF.Max(q => q.X) - bdF.Min(q => q.X), 2) + System.Math.Pow(bdF.Max(q => q.Y) - bdF.Min(q => q.Y), 2)) * 0.006);
        while (true)
        {
            var live = b.Ok
                ? WallLineDraw.Show(System.Array.Empty<(double, double)>(), got.Cut.NewRing.Append(got.Cut.NewRing[0]).ToList(), mk, 3, b.Rows.Cast<IReadOnlyList<Point3>>().Append(b.CutExt.Append(b.CutExt[0]).ToList()), 8)
                : WallLineDraw.Show(b.Marks, got.Pts, mk);
            string spec = b.Ok ? $"〈단높이 {h:0.##}m · 소단 {t:0.##}m · 수직 · {(b.Up ? "절토" : "성토")} {b.Tiers}단{(b.PadMax - b.PadMin > 1e-6 ? " · 기운 바닥" : "")}〉" : $"〈⚠{b.Why}〉";
            PromptResult r;
            try
            {
                var pk = new PromptKeywordOptions($"\n[{Tag}] 제원 {spec} [단높이(H)/소단길이(T)/다시그리기(S)] (Enter = 적용): ");
                pk.Keywords.Add("H", "H", "단높이(H)"); pk.Keywords.Add("T", "T", "소단길이(T)"); pk.Keywords.Add("S", "S", "다시그리기(S)");
                pk.AllowNone = true; pk.AppendKeywordsToMessage = false;
                r = ed.GetKeywords(pk);
            }
            finally { WallLineDraw.Drop(live); }
            if (r.Status == PromptStatus.Cancel) { ed.WriteMessage($"\n[{Tag}] 그만두었습니다 — 아무것도 안 바꿨습니다."); return; }
            string kw = r.Status == PromptStatus.None ? "" : (r.StringResult ?? "").Trim().ToUpperInvariant();
            if (kw == "H" || kw == "T")
            {
                var pdo = new PromptDoubleOptions(kw == "H" ? "\n단높이 (m)" : "\n소단 길이 (m)") { DefaultValue = kw == "H" ? h : t, UseDefaultValue = true, AllowNegative = false, AllowZero = kw == "T" };
                var dr = ed.GetDouble(pdo);
                if (dr.Status == PromptStatus.OK)
                {
                    if (kw == "H" && dr.Value >= 0.2 && dr.Value <= 15) h = dr.Value;
                    else if (kw == "T" && dr.Value <= 60) t = dr.Value;
                    else ed.WriteMessage("\n → 범위 밖 값입니다(단높이 0.2~15 · 소단 0~60 m).");
                }
                b = BuildGeom(got.Cut, bdF, regF, blF, h, t, slopeW, faceRun, grid, margin, gC, pC, clipR);
                continue;
            }
            if (kw == "S")
            {
                var g2 = WallLineDraw.Run(doc, Tag, bdF, regF.Boundary, pts => BoundaryCut.Classify(pts, bdF, regF.Boundary, blF));
                if (g2 == null) return;
                got = g2;
                b = BuildGeom(got.Cut, bdF, regF, blF, h, t, slopeW, faceRun, grid, margin, gC, pC, clipR);
                continue;
            }
            if (!b.Ok) { ed.WriteMessage($"\n[{Tag}] {b.Why} — 값을 바꾸거나(H · T) 다시 그리세요(S)."); continue; }
            break;
        }
        ed.WriteMessage($"\n[{Tag}] 폴리선 옹벽을 짓습니다…");
        var (no, note) = Make(doc, got.Pts, got.SrcHandle, h, t);
        if (no == 0)
        {
            string why = GradingSettings.LastAddNote.Length > 0 ? GradingSettings.LastAddNote : note;
            ed.WriteMessage($"\n[{Tag}] ⚠{why} — 아무것도 안 바꿨습니다.");
            if (!SelfCheckLog.Headless) try { BusyPopup.Close(); AcadApp.ShowAlertDialog($"폴리선 옹벽을 짓지 않았습니다(정지면_DH · 지난 옹벽은 그대로).\n\n{why}"); } catch { }
            return;
        }
        Kick(doc, no);
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
        var b = BuildGeom(cut, bd, reg, bl, w.H, w.T, sW, fR, gr, System.Math.Max(0, GradingSettings.WallPolygonMargin), new CachedGroundSurface(gTin), new CachedGroundSurface(p0), clipB);
        if (!b.Ok) return $"폴리선 옹벽 — 새 정지면에서 못 지음: {b.Why}";
        string moved = WallByLine.SamePlace(w, cut, b);
        if (moved.Length > 0) return "폴리선 옹벽 자리가 움직였다 — " + moved + " · [옹벽→사면]으로 되돌리거나 다시 그리세요";
        var er = WallTags.Erase(db, tr, (n, r) => n == w.No && (r.Length == 0 || r == region), false, groundId);
        var (wh, blk, note) = BuildSurface(db, tr, b, w.No, region, groundId);
        if (wh.Length == 0) return note;
        w.WallHandle = wh; w.PureHandle = ""; w.Block = blk; w.Poly = b.Zone; w.IsWall = b.ZoneSide; w.HeadZ = b.HeadZ; w.CapZ = b.CapZ; w.Line = cut.Line.ToList(); w.Seg = cut.Line.ToList();
        log.AppendLine($"   폴리선 옹벽 {w.No} 다시 짓기 — 지운 것 {er.Text} · {note}");
        foreach (var line in b.Log.Split('\n')) if (line.Trim().Length > 0) log.AppendLine("      " + line.Trim());
        return blk.Length > 0 ? "합성을 막을 까닭이 생겼다 — " + blk : "";
    }
}
