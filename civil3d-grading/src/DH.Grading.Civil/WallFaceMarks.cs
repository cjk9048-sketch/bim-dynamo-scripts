using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using CivilDb = Autodesk.Civil.DatabaseServices;
using Tri = DH.Grading.Core.WallDaylight.Tri;

namespace DH.Grading.Civil;

/// <summary>★★★[v103.2 · 계획 2-5] <b>옹벽 폴리곤 안의 측점 — 번들 재료를 가리고 최종면을 걸어서 낸다.</b>
/// <para>최종면에 든 옹벽(합성 표지 ∩ 유효한 뚜껑 · 순수면에 반영된 굳은 옹벽)마다 선형이 폴리곤 안(밖으로 1cm까지)을 지나는 측점 구간을 찾아
/// ①번들 · 도면에서 나온 사면 · 소단 · 데이라잇 · 옹벽 측점과 옹벽 막대 · (전)(후)를 버리고(그 자리는 옹벽이 바꿨다)
/// ②지금 그리는 «정지면» 삼각형을 직접 걸어(<see cref="WallWalk"/>) 벽(막대 · (전)(후) · 가운데 측점 «옹벽(최종면)») · 끊기는 자리(«데이라잇(최종면)») · 꺾임(«꺾임(최종면)»)을 낸다.</para>
/// <para>세 수집 자리(종단의 검토선 · 측점 명령의 자동 측점 · 막대 수집)가 이 한 함수를 부른다 — 같은 자리에 같은 측점이 선다.</para></summary>
internal static class WallFaceMarks
{
    /// <summary>폴리곤 안에서 가리는 사유(앞 글자) — 번들 · 도면이 준 것. 수동 · 절성 경계 · 원지반 꺾임 · 터파기 · 정체인은 안 가린다.</summary>
    /// «데이라잇»은 앞 글자 — 종단은 «데이라잇(복원)» · «데이라잇(도면)», 측점 명령(<c>StationCommand.AutoMarks</c>)은 그냥 «데이라잇»이라 적는다(«…(최종면)»은 위에서 먼저 뺀다).
    static readonly string[] HideWhy = { "사면·소단", "데이라잇", "옹벽(복원)", "옹벽" };
    internal const string WallWhy = "옹벽(최종면)", BreakWhy = "데이라잇(최종면)", BendWhy = "꺾임(최종면)";

    static bool Hidden(string why)
    {
        if (string.IsNullOrEmpty(why)) return false;
        if (why == WallWhy || why == BreakWhy || why == BendWhy) return false;
        foreach (var h in HideWhy) if (why.StartsWith(h, System.StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>선형 표본(평면 · 측점) — 0.25m마다 + 끝점.</summary>
    static System.Collections.Generic.List<(double X, double Y, double S)> Sample(CivilDb.Alignment al, double step = 0.25)
    {
        var res = new System.Collections.Generic.List<(double, double, double)>();
        double s0 = al.StartingStation, s1 = al.EndingStation;
        int n = System.Math.Max(1, (int)System.Math.Ceiling((s1 - s0) / step));
        for (int i = 0; i <= n; i++)
        {
            double s = i == n ? s1 : s0 + i * (s1 - s0) / n;
            double x = 0, y = 0;
            try { al.PointLocation(s, 0, ref x, ref y); res.Add((x, y, s)); } catch { }
        }
        return res;
    }

    /// <summary>폴리곤 안 측점 구간(밖으로 1cm) — 표본 선분마다 폴리곤과 <b>바로 교차</b>시킨다(★[코드 검토 낮음 8] 종전엔 0.25m 표본의 안팎만 봐서
    /// 모서리를 그보다 짧게 스치는 구간을 놓쳤다). 이어진 조각은 하나로 묶는다. 점으로만 닿는 자리는 구간이 아니다.</summary>
    internal static System.Collections.Generic.List<(double S0, double S1)> Intervals(System.Collections.Generic.IReadOnlyList<(double X, double Y, double S)> smp, Geometry poly, GeometryFactory gf)
    {
        var pieces = new System.Collections.Generic.List<(double A, double B)>();
        var env = poly.EnvelopeInternal;
        var prep = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(poly);
        for (int k = 0; k + 1 < smp.Count; k++)
        {
            var p = smp[k]; var q = smp[k + 1];
            double dx = q.X - p.X, dy = q.Y - p.Y, l2 = dx * dx + dy * dy;
            if (l2 < 1e-18) continue;
            if (System.Math.Max(p.X, q.X) < env.MinX || System.Math.Min(p.X, q.X) > env.MaxX || System.Math.Max(p.Y, q.Y) < env.MinY || System.Math.Min(p.Y, q.Y) > env.MaxY) continue;
            var seg = gf.CreateLineString(new[] { new Coordinate(p.X, p.Y), new Coordinate(q.X, q.Y) });
            if (!prep.Intersects(seg)) continue;
            Geometry g = prep.Covers(seg) ? seg : seg.Intersection(poly);
            double S(Coordinate c) { double t = System.Math.Clamp(((c.X - p.X) * dx + (c.Y - p.Y) * dy) / l2, 0, 1); return p.S + t * (q.S - p.S); }
            for (int i = 0; i < g.NumGeometries; i++)
                if (g.GetGeometryN(i) is LineString ls && ls.NumPoints >= 2 && ls.Length > 0)
                {
                    double a = S(ls.GetCoordinateN(0)), b = S(ls.GetCoordinateN(ls.NumPoints - 1));
                    pieces.Add((System.Math.Min(a, b), System.Math.Max(a, b)));
                }
        }
        pieces.Sort((x, y) => x.A.CompareTo(y.A));
        var res = new System.Collections.Generic.List<(double, double)>();
        foreach (var (a, b) in pieces)
        {
            if (res.Count > 0 && a <= res[^1].Item2 + 1e-6) res[^1] = (res[^1].Item1, System.Math.Max(res[^1].Item2, b));
            else res.Add((a, b));
        }
        return res;
    }

    /// <summary>가리고 걷는다. 돌려주는 값 = 로그 한 줄(옹벽이 없으면 빈 문자열).</summary>
    /// <param name="walls">최종면에 든 옹벽 — null이면 지금 목록 · 표지로 읽는다(<see cref="PureWithWallsBuilder.CurrentWalls"/>).</param>
    /// <param name="padSurfaceId">지금 그리는 «정지면» 표면(옹벽면 또는 정지순수_DH) — 걷기가 읽는다.</param>
    internal static string Apply(Transaction tr, Database db, CivilDb.Alignment al, System.Collections.Generic.List<PureWithWallsBuilder.ReflWall>? walls,
                                 ObjectId padSurfaceId, System.Collections.Generic.List<StationMarks.Mark> marks,
                                 System.Collections.Generic.List<StationMarks.VertBar>? bars, System.Collections.Generic.List<StationMarks.WallSpan>? spans,
                                 StringBuilder? log)
    {
        if (walls == null || walls.Count == 0 || al == null) return "";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gf = new GeometryFactory();
        var smp = Sample(al);
        if (smp.Count < 2) return "";
        var ivs = new System.Collections.Generic.List<(double S0, double S1, PureWithWallsBuilder.ReflWall W, Envelope Env)>();
        foreach (var w in walls)
        {
            if (w.Poly == null || w.Poly.Count < 3) continue;
            var ring = w.Poly.Select(q => new Coordinate(q.X, q.Y)).ToList(); ring.Add(ring[0].Copy());
            Geometry pg = gf.CreatePolygon(ring.ToArray());
            if (!pg.IsValid) pg = pg.Buffer(0);
            var pgB = pg.Buffer(0.01);
            foreach (var (a, b) in Intervals(smp, pgB, gf)) ivs.Add((a, b, w, pgB.EnvelopeInternal));
        }
        if (ivs.Count == 0) return "";
        bool InAny(double s) => ivs.Any(v => s >= v.S0 - 1e-6 && s <= v.S1 + 1e-6);
        // ① 가리기
        int hm = marks.RemoveAll(m => Hidden(m.Why) && InAny(m.Station));
        int hb = bars?.RemoveAll(b => (b.Kind ?? "").Contains("옹벽") && InAny(b.Station)) ?? 0;
        int hs = spans?.RemoveAll(sp => (sp.Kind ?? "").Contains("옹벽") && InAny(sp.Mid)) ?? 0;
        // ② 걷기 — 지금 그리는 면의 삼각형(구간 폴리곤 상자 둘레만)
        int nw = 0, nb = 0, nbe = 0, small = 0; double covered = 0;
        string surfNote = "";
        if (!padSurfaceId.IsNull && tr.GetObject(padSurfaceId, OpenMode.ForRead) is CivilDb.TinSurface pad)
        {
            var env = new Envelope();
            foreach (var v in ivs) env.ExpandToInclude(v.Env);
            env.ExpandBy(5);
            var tris = new System.Collections.Generic.List<Tri>();
            using (var tc = pad.GetTriangles(false))
                foreach (CivilDb.TinSurfaceTriangle t in tc)
                {
                    try
                    {
                        var a = t.Vertex1.Location; var b = t.Vertex2.Location; var c = t.Vertex3.Location;
                        if (System.Math.Max(a.X, System.Math.Max(b.X, c.X)) < env.MinX || System.Math.Min(a.X, System.Math.Min(b.X, c.X)) > env.MaxX ||
                            System.Math.Max(a.Y, System.Math.Max(b.Y, c.Y)) < env.MinY || System.Math.Min(a.Y, System.Math.Min(b.Y, c.Y)) > env.MaxY) continue;
                        tris.Add(new Tri(new Point3(a.X, a.Y, a.Z), new Point3(b.X, b.Y, b.Z), new Point3(c.X, c.Y, c.Z)));
                    }
                    catch { }
                    finally { t.Dispose(); }
                }
            surfNote = $"{pad.Name} 삼각형 {tris.Count}";
            foreach (var (s0, s1, w, _) in ivs)
            {
                var pcs = WallWalk.Profile(smp, s0, s1, tris);
                var r = WallWalk.Walk(pcs, s0, s1, w.FaceSlope);
                covered += r.Covered; small += r.SmallSkipped;
                foreach (var wl in r.Walls)
                {
                    marks.Add(new StationMarks.Mark(wl.Mid, WallWhy, wl.ZTop));
                    bars?.Add(new StationMarks.VertBar(wl.Mid, wl.ZTop, wl.ZBottom, "옹벽", w.FaceSlope));
                    spans?.Add(new StationMarks.WallSpan(wl.Mid, System.Math.Min(wl.Front, wl.Back), System.Math.Max(wl.Front, wl.Back), "옹벽"));
                    nw++;
                }
                foreach (double b in r.Breaks) { marks.Add(new StationMarks.Mark(b, BreakWhy)); nb++; }
                foreach (var (s, z) in r.Bends) { marks.Add(new StationMarks.Mark(s, BendWhy, z)); nbe++; }
            }
        }
        else surfNote = "⚠그리는 정지면을 못 열어 걷지 못했다";
        string line = $"옹벽 폴리곤 안 측점(최종면) — 옹벽 {walls.Count}개 · 지나는 구간 {ivs.Count}({string.Join(" ", ivs.Select(v => $"{v.W.No}:{v.S0:F2}~{v.S1:F2}"))})" +
                      $" · 가림 측점 {hm} · 막대 {hb} · (전)(후) {hs} · 걸어 낸 벽 {nw}(5cm 미만 {small}) · 끊김 {nb} · 꺾임 {nbe} · 덮은 길이 {covered:F2}m · {surfNote} · {sw.ElapsedMilliseconds}ms";
        log?.AppendLine("  " + line);
        return line;
    }

    /// <summary>★[v103.2 · 계획 2-5] 종단을 다시 안 그리는 길(측점 명령 · 검토선 · 막대의 물러섬 수집) — 선형에 적힌 «쓴 면»과 지금 옹벽으로 가리고 걷는다.
    /// <para>적힌 면 도장이 지금과 같고 반영됐으면 최종면 옹벽 전부를 그 면에서 걷는다. 도장이 다르거나 ⚠로 그렸거나 적힌 것이 없으면
    /// 순수면에 든 굳은 옹벽만(그 면 어디에나 들어 있다) — 그 까닭은 <paramref name="warn"/>에 한 줄(부른 쪽이 한 번만 보인다).</para></summary>
    internal static string ApplyFromRecord(Transaction tr, Database db, CivilDb.Alignment al, System.Collections.Generic.List<StationMarks.Mark> marks,
                                           System.Collections.Generic.List<StationMarks.VertBar>? bars, System.Collections.Generic.List<StationMarks.WallSpan>? spans,
                                           StringBuilder? log, out string warn)
    {
        warn = "";
        if (al == null) return "";
        var surfs = Commands.SectionCommand.FindSurfaces(db, Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument);
        ObjectId g = surfs.Find(sp => sp.Label == "원지반").SurfId;
        ObjectId pad = surfs.Find(sp => sp.Label == "정지면").SurfId;
        var walls = PureWithWallsBuilder.CurrentWalls(db, tr, g, out _);
        // ★[코드 검토 낮음 9] 도장은 목록과 따로 — 옹벽이 0개여도(사면으로 되돌림 등) 기록 · 도장부터 견준다
        string head = PureWithWallsBuilder.StampHead(PureWithWallsBuilder.CurrentStamp(db, g));
        var rec = PureWithWallsBuilder.ReadAlign(tr, al);
        bool all = false;
        if (rec == null)
        {
            if (walls.Any(w => !w.Frozen)) warn = "⚠이 노선은 옹벽 반영 전에 그린 종단입니다 — [종단도]를 다시 그리면 옹벽이 든 면으로 측점을 잡습니다.";
        }
        else
        {
            var r = rec.Value;
            ObjectId sid = ObjectId.Null;
            if (r.Handle.Length > 0)
                try { sid = db.GetObjectId(false, new Handle(System.Convert.ToInt64(r.Handle, 16)), 0); } catch { }
            bool alive = !sid.IsNull && !sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is CivilDb.TinSurface;
            if (alive) pad = sid;
            if (!alive) warn = "⚠종단이 쓴 면이 없습니다 — [종단도]를 다시 그리세요.";
            else if (PureWithWallsBuilder.StampHead(r.Stamp) != head) warn = "⚠정지면이 종단을 그린 뒤 바뀌었습니다 — [종단도]를 다시 그리세요.";
            else if (r.Kind == PureWithWallsBuilder.Kind.NotReflected) warn = "⚠종단이 옹벽이 반영 안 된 면으로 그려졌습니다 — " + r.Why;
            else
            {
                all = true;
                // ★[코드 검토 중간 2] 반영은 됐어도 관문 «예»로 빠진 옹벽이 있으면 같은 ⚠
                if (r.Missing.Length > 0) warn = $"⚠옹벽 {r.Missing}은(는) 정지면_DH에 없어 종단 · 횡단에 안 들어갔습니다";
            }
        }
        if (warn.Length > 0) log?.AppendLine("  " + warn);
        if (walls.Count == 0) return "";
        if (!all) walls = walls.Where(w => w.Frozen).ToList();
        return Apply(tr, db, al, walls, pad, marks, bars, spans, log);
    }
}
