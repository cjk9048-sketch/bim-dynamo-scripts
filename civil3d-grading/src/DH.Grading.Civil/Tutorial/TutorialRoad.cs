using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using RoadSnap = DH.Grading.Civil.Tutorial.TutorialFlow.RoadSnap;

namespace DH.Grading.Civil.Tutorial;

/// <summary>★★[튜토리얼 5단원 진입도로 · JACK 1117 «기능을 한번씩 다 써보는걸로»] 도면에서 진입도로 기록 읽기 · 노선 따라 그릴 선.
/// <para>판정(무엇이 끝났나)은 <see cref="TutorialFlow.RoadDid"/>(Autodesk 형 없음 · 화면 없이 시험) — 여기는 읽기만. 명령 중에는 부르지 않는다(부르는 쪽이 한가할 때만).</para></summary>
internal static class TutRoad
{
    /// <summary>노선 따라 그릴 선을 담아 둔 레이어(기본 자료 tut_ch5.dwg — 자료 만들기가 그림).</summary>
    internal const string GuideLayer = "TUT-노선";

    /// <summary>지금 진입도로 기록 한 벌.</summary>
    internal static RoadSnap Read(Document doc)
    {
        var db = doc.Database;
        bool any = false;
        try { any = Commands.ResetCommand.AnyRoad(db); } catch { }
        var s = RoadSession.Of(doc);
        if (s == null) return new RoadSnap(AnyRoad: any);
        try { if (s.AlignId.IsNull || s.AlignId.IsErased) return new RoadSnap(AnyRoad: any); } catch { return new RoadSnap(AnyRoad: any); }   // 지운 노선(초기화 · U) = 도로 없음
        try
        {
            var d = s.Fresh();
            bool surf = false; try { surf = s.HasSurface(); } catch { }
            bool need = false; try { need = RoadPanel.NeedUpdate(s); } catch { }
            int pvi = 0; string sig = "", dir = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    if (!s.PlanId.IsNull && !s.PlanId.IsErased && tr.GetObject(s.PlanId, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Profile pr)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (Autodesk.Civil.DatabaseServices.ProfilePVI q in pr.PVIs) { pvi++; sb.Append($"{q.RawStation:F2}/{q.Elevation:F2};"); }
                        sig = sb.ToString();
                    }
                }
                catch { }
                try
                {
                    if (!s.AlignId.IsNull && !s.AlignId.IsErased && tr.GetObject(s.AlignId, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Alignment al)
                    {
                        double x = 0, y = 0;
                        al.PointLocation(al.StartingStation, 0, ref x, ref y);
                        dir = $"{x:F2},{y:F2}";
                    }
                }
                catch { }
                tr.Commit();
            }
            return new RoadSnap(
                Road: true, Surf: surf, Tier: d.TierWalls?.Count ?? 0, Bound: d.BoundWalls?.Count ?? 0,
                Sheet: RoadSession.AnyAlive(db, d.SheetViews), Xsec: RoadSession.AnyAlive(db, d.XsecViews),
                Pvi: pvi, PviSig: sig, Dir: dir, Pave: d.PaveKind, Need: need, AnyRoad: any);
        }
        catch { return new RoadSnap(Road: true, AnyRoad: any); }
    }

    /// <summary>노선 따라 그릴 선 — 기본 자료의 «TUT-노선» 열린 폴리선(가장 긴 것) · 없으면(내 결과) 정지 기록 첫 경계에서 셈(<see cref="TutorialFlow.RoadPaths"/>).
    /// 돌려줌 = (그려 둔 선 · 없으면 Null, 점들 · 없으면 null).</summary>
    internal static (ObjectId Line, List<Point2d>? Pts) Guide(Database db, Transaction tr)
    {
        ObjectId best = ObjectId.Null; double bestLen = 0;
        foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl || pl.Closed || pl.NumberOfVertices < 2) continue;
            if (!string.Equals(pl.Layer, GuideLayer, StringComparison.OrdinalIgnoreCase)) continue;
            if (pl.Length > bestLen) { bestLen = pl.Length; best = id; }
        }
        if (!best.IsNull)
        {
            var pl = (Polyline)tr.GetObject(best, OpenMode.ForRead);
            var l = new List<Point2d>();
            for (int k = 0; k < pl.NumberOfVertices; k++) l.Add(pl.GetPoint2dAt(k));
            return (best, l);
        }
        var pts = Compute(db, tr);
        return (ObjectId.Null, pts);
    }

    /// <summary>정지 기록 첫 구역 경계(계획고) + 원지반 → 가장 좋은 노선 후보 점 셋. 못 셈 = null.</summary>
    internal static List<Point2d>? Compute(Database db, Transaction tr)
    {
        try
        {
            var regs = GradingBundleStore.TryLoadAll(db, tr, out _);
            if (regs == null || regs.Count == 0 || regs[0]?.Boundary is not { Count: >= 3 } bd) return null;
            var gid = GradingBuilder.FindSurfaceByBaseName(tr, Commands.ImportGisCommand.GroundSurfaceName);
            if (gid.IsNull || tr.GetObject(gid, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.Surface g) return null;
            var pad = new List<(double X, double Y)>();
            double zs = 0; int zn = 0;
            foreach (var p in bd) { pad.Add((p.X, p.Y)); if (double.IsFinite(p.Z) && Math.Abs(p.Z) > 1e-6) { zs += p.Z; zn++; } }
            double padZ = zn > 0 ? zs / zn : TutorialFlow.MeanIn(pad, Gz(g)) ?? 0;
            var c = TutorialFlow.RoadPaths(pad, padZ, Gz(g));
            if (c.Count == 0) return null;
            var o = new List<Point2d>();
            foreach (var q in c[0].Pts) o.Add(new Point2d(q.X, q.Y));
            return o;
        }
        catch { return null; }
    }

    static Func<double, double, double?> Gz(Autodesk.Civil.DatabaseServices.Surface s) =>
        (x, y) => { try { return s.FindElevationAtXY(x, y); } catch { return null; } };
}
