using Autodesk.AutoCAD.DatabaseServices;
using DH.Grading.Core;
using NetTopologySuite.Geometries;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[v111.8 · 계획_v111_8.md 6장 · JACK 1102 Q1 · Q5 답] [3D 보기] 진입도로 포장 · 차로 다각형 · 흰 차선 — <b>설계에서</b>(노선 <c>PointLocation(s, o)</c> + 횡단 폭 · 면 세모에서 끝선을 찾지 않음).
/// <para>측점 = 범위 [S0, S1](<see cref="RoadBuilder.Sample"/>와 같은 max(노선 시작, 종단 시작) ~ min(노선 끝, 종단 끝)) 0.5 m마다 + 노선 요소(직선 · 곡선) 시작 · 끝 + 범위 끝.
/// 포장 = 차로 + 길어깨(OuterL/R) · 차로 = HalfL/R(JACK 1102 «차로 = 아스팔트 · 길어깨 = 콘크리트») · 차선 = 차로 끝 안쪽 0.1 m · 폭 0.1 m 양쪽(<see cref="RoadMarks.Lanes"/>).</para></summary>
internal static class RoadLanes
{
    const double Step = 0.5;

    /// <summary>도로 하나 — 노선 · 이름 · 범위 · 폭(지금 설계는 한 값 — 확폭이 생기면 측점마다로) · 그 도로 면 발자국 F(차선 칸이 F 밖이면 버림).</summary>
    /// <param name="Ranges">★[도로 마무리 ① · 계획 5.8] 도로 구간(<see cref="RoadRanges"/> — 잘린 끝 · 빈 구간 뺌 · 하나면 [S0, S1]).</param>
    /// <param name="D">★[도로 마무리 ②] 설계(측구 구간의 포장 폭 — 측구 윗면 = 콘크리트 «길어깨» 구분). 측구가 없으면 폭은 한 값(지금과 같음).</param>
    internal sealed record Road(ObjectId Aid, string Name, double S0, double S1, double HalfL, double HalfR, double OuterL, double OuterR, Geometry? Foot,
                                List<(double A, double B)>? Ranges = null, RoadDesign? D = null)
    {
        internal List<(double A, double B)> Spans => Ranges is { Count: > 0 } ? Ranges : new() { (S0, S1) };
        /// <summary>그 측점 포장 바깥 반폭(좌 · 우) — 측구 구간이면 + 측구 윗폭.</summary>
        internal (double L, double R) OuterAt(double s)
        {
            if (D == null || D.Ditches.Count == 0) return (OuterL, OuterR);
            double wl = D.DitchAt(-1, s) is { } a ? RoadDesign.ShapeOf(a)?.TopWidth ?? 0 : 0, wr = D.DitchAt(1, s) is { } b ? RoadDesign.ShapeOf(b)?.TopWidth ?? 0 : 0;
            return (OuterL + wl, OuterR + wr);
        }
    }

    internal static Road? Of(Transaction tr, ObjectId aid, RoadDesign d, string name, Geometry? foot)
    {
        var db = aid.Database;
        if (d.PlanProfile.Length == 0 || !db.TryGetObjectId(new Handle(Convert.ToInt64(d.PlanProfile, 16)), out var pid) || pid.IsErased) return null;
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        var pr = (CivilDb.Profile)tr.GetObject(pid, OpenMode.ForRead);
        double s0 = Math.Max(al.StartingStation, pr.StartingStation), s1 = Math.Min(al.EndingStation, pr.EndingStation);
        if (!(s1 > s0)) return null;
        var sec = d.Section();
        var rg = RoadRanges.Of(tr, aid, d);   // ★[도로 마무리 ① · 계획 5.8] 끝 맞춤 꺼진 도로 = [s0, s1] 하나(지금과 같음)
        if (rg.Count == 0) return null;
        return new Road(aid, name, rg[0].A, rg[^1].B, sec.HalfL, sec.HalfR, sec.OuterL, sec.OuterR, foot, rg, d);
    }

    /// <summary>측점 — 0.5 m마다(곱으로 — 누적 반올림 없음) + 노선 요소 경계(범위 안) + 범위 끝 · 1 µm 안 겹침 뺌.</summary>
    /// <summary>★[도로 마무리 ②] 측구 구간 경계(포장 폭이 바뀜) — 경계 ±1 mm 측점(폭이 거의 직각으로 바뀌게).</summary>
    static List<double> DitchCuts(Road r, double s0, double s1)
    {
        var o = new List<double>();
        if (r.D == null || r.D.Ditches.Count == 0) return o;
        foreach (var w in r.D.Ditches)
            if (w.Length >= 2)
                foreach (double c in new[] { Math.Min(w[0], w[1]), Math.Max(w[0], w[1]) })
                    foreach (double q in new[] { c - 1e-3, c + 1e-3 }) if (q > s0 && q < s1) o.Add(q);
        return o;
    }

    static List<double> Stations(CivilDb.Alignment al, double s0, double s1, IEnumerable<double>? extra = null)
    {
        var l = new List<double>();
        if (extra != null) l.AddRange(extra);
        for (int k = 0; s0 + k * Step < s1; k++) l.Add(s0 + k * Step);
        l.Add(s1);
        try
        {
            foreach (CivilDb.AlignmentEntity e in al.Entities)
                for (int i = 0; i < e.SubEntityCount; i++)
                {
                    var se = e[i];
                    foreach (double s in new[] { se.StartStation, se.EndStation })
                        if (s > s0 && s < s1) l.Add(s);
                }
        }
        catch { }
        l.Sort();
        var o = new List<double>(l.Count);
        foreach (double s in l) if (o.Count == 0 || s - o[^1] > 1e-6) o.Add(s);
        return o;
    }

    static (double X, double Y)? At(CivilDb.Alignment al, double s, double o)
    {
        try { double x = 0, y = 0; al.PointLocation(s, o, ref x, ref y); return double.IsFinite(x) && double.IsFinite(y) ? (x, y) : null; }
        catch { return null; }
    }

    /// <summary>포장(차로 + 길어깨) · 차로 다각형 — 왼쪽(−오프셋) 앞으로 + 오른쪽(+) 뒤로 = 링. 곡선 안쪽이 꼬이면 Buffer(0)로 바로잡음. 못 하면 null.</summary>
    /// <remarks>★[v111.8 · 검토 L4] 한쪽이라도 자리를 못 얻은 측점은 양쪽에서 뺀다(한쪽만 빼면 링이 비뚤어짐) — 뺀 수 = <c>Dropped</c>(포장 · 차로 합 · 부르는 쪽이 로그).</remarks>
    internal static (Geometry? Pave, Geometry? Lane, int Dropped) Pavement(Transaction tr, Road r, GeometryFactory gf)
    {
        var al = (CivilDb.Alignment)tr.GetObject(r.Aid, OpenMode.ForRead);
        int dropped = 0;
        List<double> st = null!;
        Geometry? Band(Func<double, (double L, double R)> w)
        {
            var lf = new List<Coordinate>(st.Count); var rt = new List<Coordinate>(st.Count);
            foreach (double s in st)
            {
                var (left, right) = w(s);
                if (At(al, s, -left) is { } a && At(al, s, right) is { } b) { lf.Add(new Coordinate(a.X, a.Y)); rt.Add(new Coordinate(b.X, b.Y)); }
                else dropped++;
            }
            if (lf.Count < 2) return null;
            var cs = new List<Coordinate>(lf.Count * 2 + 1);
            cs.AddRange(lf);
            for (int i = rt.Count - 1; i >= 0; i--) cs.Add(rt[i]);
            cs.Add(cs[0].Copy());
            Geometry g = gf.CreatePolygon(cs.ToArray());
            if (!g.IsValid) g = g.Buffer(0);
            return g.IsEmpty ? null : g;
        }
        // ★[도로 마무리 ① · ②] 도로 구간마다(빈 구간 · 잘린 끝 밖은 없음) · 포장 폭 = 측점마다(측구 구간이면 측구 바깥까지)
        var paves = new List<Geometry>(); var lanes = new List<Geometry>();
        foreach (var (a0, b0) in r.Spans)
        {
            st = Stations(al, a0, b0, DitchCuts(r, a0, b0));
            if (Band(r.OuterAt) is { } p) paves.Add(p);
            if (Band(_ => (r.HalfL, r.HalfR)) is { } q) lanes.Add(q);
        }
        static Geometry? U(List<Geometry> g) => g.Count == 0 ? null : g.Count == 1 ? g[0] : NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(g);
        return (U(paves), U(lanes), dropped);
    }

    /// <summary>흰 차선 띠(양쪽) — 높이 = <paramref name="zAt"/> + 0.03 m(Core <see cref="RoadMarks.Lift"/>) · 지형 없음 · 곡선 안쪽 좁음 · 꼬인 칸은 Core가 버리고 센다.</summary>
    internal static RoadMarks.RibbonResult Lanes(Transaction tr, Road r, Func<double, double, double?> zAt)
    {
        var al = (CivilDb.Alignment)tr.GetObject(r.Aid, OpenMode.ForRead);
        // ★[도로 마무리 ① · 계획 5.8] 빈 구간은 자리 없음(NaN) 한 측점으로 끊음 — 구간 밖에 차선 칸 0
        var sp = r.Spans;
        var st = new List<double>();
        for (int i = 0; i < sp.Count; i++) { if (i > 0) st.Add((sp[i - 1].B + sp[i].A) / 2); st.AddRange(Stations(al, sp[i].A, sp[i].B)); }
        return RoadMarks.Lanes(st, (s, o) => RoadRanges.Inside(sp, s) ? At(al, s, o) : null, _ => (r.HalfL, r.HalfR), zAt);
    }
}
