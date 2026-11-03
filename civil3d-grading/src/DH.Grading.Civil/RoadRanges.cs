using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using CivilApp = Autodesk.Civil.ApplicationServices;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[도로 마무리 ① · 계획_도로마무리.md 5.1] 부지 바닥(평탄면) 조각 — 구역마다 계획폴리곤 − 뒤 구역 발자국(«계획면.shp»와 같은 판정 · <see cref="Pieces"/>를 둘이 씀).
/// 정지 기록이 없으면 바닥 없음(지금과 같음 — 끝은 «자유»).</summary>
internal static class PadFloors
{
    /// <summary>구역 <paramref name="ri"/>의 계획면 조각(«계획면.shp» · 도로 끝이 같은 함수).</summary>
    internal static List<(List<IReadOnlyList<Point3>> Rings, double Area)> Pieces(IReadOnlyList<GradingBundle> regions, int ri, out double excluded)
        => GradingPolygons.PlanMinusFootprints(regions[ri].Boundary, GradingBundle.LaterFootprints(regions, ri), out excluded);

    /// <summary>바닥 조각들(구역 번호 · 다각형) — 정지 기록이 없거나 못 읽으면 빈 목록(<paramref name="note"/> = 까닭).</summary>
    internal static List<RoadEnds.Floor> Load(Database db, Transaction tr, out string note)
    {
        note = "";
        var o = new List<RoadEnds.Floor>();
        List<GradingBundle>? regions;
        try { regions = GradingBundleStore.TryLoadAll(db, tr, out note); }
        catch (Exception ex) { note = "정지 기록 못 읽음 — " + ex.Message; return o; }
        if (regions == null || regions.Count == 0) return o;
        var gf = new GeometryFactory();
        for (int ri = 0; ri < regions.Count; ri++)
        {
            try
            {
                foreach (var pc in Pieces(regions, ri, out _))
                {
                    if (pc.Rings.Count == 0 || pc.Rings[0].Count < 3) continue;
                    LinearRing Ring(IReadOnlyList<Point3> r)
                    {
                        var cs = r.Select(p => new Coordinate(p.X, p.Y)).ToList();
                        if (!cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
                        return gf.CreateLinearRing(cs.ToArray());
                    }
                    Geometry g = gf.CreatePolygon(Ring(pc.Rings[0]), pc.Rings.Skip(1).Where(h => h.Count >= 3).Select(Ring).ToArray());
                    if (!g.IsValid) g = g.Buffer(0);
                    if (!g.IsEmpty) o.Add(new RoadEnds.Floor(ri, g));
                }
            }
            catch (Exception ex) { note += $" · 구역{ri + 1} 바닥 못 읽음 — {ex.Message}"; }
        }
        return o;
    }
}

/// <summary>★★★[도로 마무리 ① · 계획_도로마무리.md 5.8 · 비평 H1] 도로가 실제로 있는 측점 구간 — 노선 ∩ 계획 종단 · <see cref="RoadDesign.EndsOn"/>이면 가상 자르기(바닥 · 본 도로 포장) · 빈 구간(Q6) 뺌.
/// <para>엔진 표본 · 3D 포장 · 차선 · 횡단 측점 · 종단도 도장 · 측구 수량이 모두 이 하나를 쓴다(각자 노선 전체를 읽으면 빈 구간 · 가상 자르기를 놓침).
/// <see cref="RoadDesign.EndsOn"/>이 거짓인 도로 = 노선 ∩ 종단 하나(지금과 같음 · 비트 같음).</para></summary>
internal static class RoadRanges
{
    const double Step = 0.25;

    /// <summary>노선 중심선 표본(0.25 m + 요소 경계) · 포장 반폭(길어깨 끝 + 그 쪽 측구 윗폭 중 가장 넓은 것).</summary>
    internal static RoadEnds.Road? Centre(Transaction tr, ObjectId aid, RoadDesign d)
    {
        if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) return null;
        double s0 = al.StartingStation, s1 = al.EndingStation;
        if (!(s1 - s0 > 0.1)) return null;
        var st = new SortedSet<double>();
        for (int k = 0; s0 + k * Step < s1; k++) st.Add(s0 + k * Step);
        st.Add(s1);
        try
        {
            foreach (CivilDb.AlignmentEntity e in al.Entities)
                for (int i = 0; i < e.SubEntityCount; i++) { var se = e[i]; foreach (double s in new[] { se.StartStation, se.EndStation }) if (s > s0 && s < s1) st.Add(s); }
        }
        catch { }
        var S = new List<double>(); var X = new List<double>(); var Y = new List<double>();
        foreach (double s in st)
        {
            double x = 0, y = 0;
            try { al.PointLocation(s, 0, ref x, ref y); } catch { continue; }
            if (S.Count > 0 && s - S[^1] < 1e-3) { if (s >= s1) { S[^1] = s; X[^1] = x; Y[^1] = y; } continue; }   // 1 mm 안 토막 없음(끝 방향이 흔들리지 않게)
            S.Add(s); X.Add(x); Y.Add(y);
        }
        if (S.Count < 2) return null;
        var sec = d.Section();
        double wl = 0, wr = 0;
        foreach (var w in d.Ditches) { var sh = RoadDesign.ShapeOf(w); if (sh == null) continue; if (w[2] < 0) wl = Math.Max(wl, sh.TopWidth); else wr = Math.Max(wr, sh.TopWidth); }
        return new RoadEnds.Road(aid.Handle.Value, S, X, Y, sec.OuterL + wl, sec.OuterR + wr);
    }

    /// <summary>이 도면의 진입도로 노선들(설계 기록 있음).</summary>
    internal static List<ObjectId> Roads(Transaction tr, Database db)
    {
        var o = new List<ObjectId>();
        var cdoc = CivilApp.CivilDocument.GetCivilDocument(db);
        foreach (ObjectId id in cdoc.GetAlignmentIds()) { try { if (!id.IsErased && RoadDesign.Has(tr, id)) o.Add(id); } catch { } }
        return o;
    }

    /// <summary>본 도로 포장 높이(가지 도로 끝 높이 — 본 도로 계획 종단 + 횡단) · 못 재면 null.</summary>
    static double? RoadZ(Transaction tr, Database db, long handle, double x, double y)
    {
        try
        {
            if (!db.TryGetObjectId(new Handle(handle), out var aid) || aid.IsErased) return null;
            var d = RoadDesign.Read(tr, aid);
            if (d == null || d.PlanProfile.Length == 0 || !db.TryGetObjectId(new Handle(Convert.ToInt64(d.PlanProfile, 16)), out var pid) || pid.IsErased) return null;
            var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
            var pr = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForRead);
            double st = 0, off = 0;
            al.StationOffset(x, y, ref st, ref off);
            return pr.ElevationAt(st) + d.Section().DropC(off);   // Civil 옵셋 왼쪽 − = Core 좌(−)
        }
        catch { return null; }
    }

    /// <summary>판정 — 이 도면의 모든 진입도로를 함께(가지 판정) · 이 노선 몫. 바닥이 없고 다른 도로도 없으면 null.</summary>
    internal static RoadEnds.Result? Solve(Transaction tr, Database db, ObjectId aid, List<RoadEnds.Floor> floors, Func<double, double, double?>? floorZ, bool needZ, RoadEnds.Options? opt = null, RoadDesign? self = null)
        => SolveAll(tr, db, aid, floors, floorZ, needZ, opt, self)?.FirstOrDefault(r => r.Handle == aid.Handle.Value);

    /// <summary>★[도로 마무리 ① · 계획 5.5] 같은 판정 — 모든 도로 몫(가지 사슬 · 최종면 차례가 씀). 이 노선이 판정에 못 들거나 바닥 · 다른 도로가 없으면 null.</summary>
    internal static List<RoadEnds.Result>? SolveAll(Transaction tr, Database db, ObjectId aid, List<RoadEnds.Floor> floors, Func<double, double, double?>? floorZ, bool needZ, RoadEnds.Options? opt = null, RoadDesign? self = null)
    {
        var roads = new List<RoadEnds.Road>();
        foreach (var id in Roads(tr, db))
        {
            var d = RoadDesign.Read(tr, id);
            if (d == null) continue;
            var c = Centre(tr, id, d);
            if (c != null) roads.Add(c);
        }
        // 막 만드는 도로(설계 기록을 아직 안 씀)
        if (self != null && !roads.Any(r => r.Handle == aid.Handle.Value)) { var c = Centre(tr, aid, self); if (c != null) roads.Add(c); }
        if (!roads.Any(r => r.Handle == aid.Handle.Value)) return null;
        if (floors.Count == 0 && roads.Count < 2) return null;
        return RoadEnds.Solve(roads, floors, floorZ ?? ((_, _) => null), needZ ? (h, x, y) => RoadZ(tr, db, h, x, y) : null, opt);
    }

    /// <summary>노선 ∩ 계획 종단 범위(없으면 null).</summary>
    internal static (double A, double B)? AlignProfile(Transaction tr, ObjectId aid, RoadDesign d)
    {
        var db = aid.Database;
        if (tr.GetObject(aid, OpenMode.ForRead) is not CivilDb.Alignment al) return null;
        if (d.PlanProfile.Length == 0 || !db.TryGetObjectId(new Handle(Convert.ToInt64(d.PlanProfile, 16)), out var pid) || pid.IsErased) return null;
        var pr = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForRead);
        double a = Math.Max(al.StartingStation, pr.StartingStation), b = Math.Min(al.EndingStation, pr.EndingStation);
        return b > a ? (a, b) : null;
    }

    /// <summary>판정 결과 → 도로 구간(노선 ∩ 종단과 겹침 · 노선 끝 1 cm 안 자르기는 노선 끝으로 · 0.5 m 미만 조각 뺌).</summary>
    internal static List<(double A, double B)> FromResult(RoadEnds.Result? res, (double A, double B) ap, double alS, double alE)
    {
        var o = new List<(double A, double B)>();
        if (res == null || res.Ranges.Count == 0) { o.Add(ap); return o; }
        foreach (var (ra, rb) in res.Ranges)
        {
            double a = Math.Abs(ra - alS) < 0.01 ? alS : ra, b = Math.Abs(rb - alE) < 0.01 ? alE : rb;
            a = Math.Max(a, ap.A); b = Math.Min(b, ap.B);
            if (b - a >= 0.5) o.Add((a, b));
        }
        if (o.Count == 0) o.Add(ap);   // 자르면 남는 것이 없음 — 엔진이 알림 · 지금처럼 전체
        return o;
    }

    /// <summary>도로 구간(노선 측점) — <see cref="RoadDesign.EndsOn"/>이 거짓이면 노선 ∩ 종단 하나(지금과 같음). 종단이 없으면 빈 목록.
    /// <paramref name="floors"/>가 null이면 정지 기록에서 읽음(한 명령에서 여러 도로면 한 번 읽어 넘김).</summary>
    internal static List<(double A, double B)> Of(Transaction tr, ObjectId aid, RoadDesign d, List<RoadEnds.Floor>? floors = null)
    {
        var ap = AlignProfile(tr, aid, d);
        if (ap == null) return new();
        if (!d.EndsOn) return new() { ap.Value };
        var db = aid.Database;
        floors ??= PadFloors.Load(db, tr, out _);
        RoadEnds.Result? res = null;
        try { res = Solve(tr, db, aid, floors, null, false); } catch (Exception ex) { try { DiagLog.Append($"\n[진입도로 끝] 판정 실패 — {ex.Message}"); } catch { } }
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        return FromResult(res, ap.Value, al.StartingStation, al.EndingStation);
    }

    /// <summary>측점이 구간 안인가(끝 1 mm 여유).</summary>
    internal static bool Inside(IReadOnlyList<(double A, double B)> ranges, double s) => ranges.Any(r => s >= r.A - 1e-3 && s <= r.B + 1e-3);

    /// <summary>[a, b]를 구간들과 겹친 조각으로.</summary>
    internal static List<(double A, double B)> Clip(IReadOnlyList<(double A, double B)> ranges, double a, double b)
    {
        var o = new List<(double A, double B)>();
        foreach (var r in ranges) { double x = Math.Max(a, r.A), y = Math.Min(b, r.B); if (y > x + 1e-9) o.Add((x, y)); }
        return o;
    }

    /// <summary>제안 글(■ 결과 · 명령창) — «시점 자름 3.1 m · 종점 늘림 8.2 m · 빈 구간 1»(바뀔 것이 없으면 빈 글).</summary>
    internal static string Proposal(RoadEnds.Result? res, double alS, double alE, bool own)
    {
        if (res == null) return "";
        var p = new List<string>();
        void End(RoadEnds.End e, string nm, double edge)
        {
            switch (e.Kind)
            {
                case RoadEnds.Kind.Floor:
                case RoadEnds.Kind.Branch:
                    double cut = Math.Abs(e.Station - edge);
                    if (cut >= 0.01) p.Add($"{nm} 자름 {cut:F1} m" + (e.Kind == RoadEnds.Kind.Branch ? "(본 도로)" : ""));
                    break;
                case RoadEnds.Kind.Extend:
                    p.Add(own ? $"{nm} 늘림 {e.Ext!.Length:F1} m" + (e.Ext.Straight ? "" : "(꺾어)") : $"{nm} 바닥까지 {e.Ext!.Length:F1} m 모자람(사람 노선이라 안 늘림)");
                    break;
            }
        }
        End(res.Start, "시점", alS); End(res.Finish, "종점", alE);
        if (res.Gaps.Count > 0) p.Add($"빈 구간 {res.Gaps.Count}");
        return string.Join(" · ", p);
    }
}
