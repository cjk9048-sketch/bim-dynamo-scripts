using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★★[도로 마무리 ① · 계획_도로마무리.md 5.3 · 5.4 · 측정 S0] 끝 맞추기 — 노선을 실제로 자르고 늘림 + 끝 · 빈 구간 PVI 고정 높이.
/// <para>S0 실측: 종점 = 끝 직선 <c>PassThroughPoint2</c>(Length 쓰기는 예외) · 시점 = 첫 직선 <c>PassThroughPoint1</c>(Civil이 기준점을 새 시점 · 측점 0으로) ·
/// 꺾어 늘림 = <c>AddFixedLine</c> + <c>AddFreeCurve</c> · 계획 종단 PVI는 Civil을 믿지 않고 우리가 다시 씀.</para>
/// <para>시점이 Δ만큼 움직이면(늘림 +Δ · 자름 −Δ) 측점에 묶인 것(옹벽 · 측구 기록 · 수동 측점 · 지운 측점 · PVI)을 같은 트랜잭션에서 Δ만큼 옮기고,
/// 옮기기 전 자리(XY)와 1 mm 안인지 확인 — 하나라도 어긋나면 예외(부르는 쪽이 트랜잭션을 물림 → 가상 자르기 · 고정 높이만).</para></summary>
internal static class RoadEndFix
{
    internal sealed class Report
    {
        public bool AlignChanged, PviChanged; public double Delta; public int Shifted, Checked; public double WorstMm; public List<string> Notes = new();
        public string Text => string.Join(" · ", Notes) + (Shifted > 0 ? $" · 측점 옮김 {Shifted}(Δ {Delta:+0.000;-0.000} m · 확인 {Checked} · 최대 {WorstMm:F3} mm)" : "");
    }

    static (double X, double Y)? P(CivilDb.Alignment al, double s, double o = 0)
    {
        try { double x = 0, y = 0; al.PointLocation(s, o, ref x, ref y); return (x, y); } catch { return null; }
    }

    /// <summary>직선의 반대쪽 통과점을 그 끝점으로(모양 그대로) — 통과점이 가운데에 있으면 끝을 그 너머로 옮길 때 «PointNotOnEntity»(V98e 실측 1103).</summary>
    static void Pin1(CivilDb.AlignmentLine l) { var p = l.StartPoint; if (l.PassThroughPoint1.GetDistanceTo(p) > 1e-9) l.PassThroughPoint1 = p; }
    static void Pin2(CivilDb.AlignmentLine l) { var p = l.EndPoint; if (l.PassThroughPoint2.GetDistanceTo(p) > 1e-9) l.PassThroughPoint2 = p; }

    /// <summary>넣은 곡선 반지름(못 넣으면 NaN).</summary>
    static double TryCurve(CivilDb.Alignment al, int a, int b, double radius)
    {
        for (double r = Math.Max(3, radius); r >= 3 - 1e-9; r *= 0.7)
        {
            try { al.Entities.AddFreeCurve(a, b, r, CivilDb.CurveParamType.Radius, false, CivilDb.CurveType.Compound); return r; } catch { }
        }
        return double.NaN;
    }

    /// <summary>꺾인 자리 곡선이 옛 끝 꼭짓점에서 깎아 먹는 길이(접선 길이 T) + 여유.</summary>
    static double Tangent(double r, double bendDeg) => r * Math.Tan(Math.Clamp(bendDeg, 0, 89) * Math.PI / 360) * 1.05 + 0.01;

    /// <summary>끝 맞추기 한 번(같은 트랜잭션) — <paramref name="alignEdits"/> 거짓 = 노선은 그대로(가상 자르기) · PVI 고정만. 1 mm 확인 실패 · API 실패 = 예외.</summary>
    internal static Report Fix(Transaction tr, ObjectId aid, ObjectId planId, RoadDesign d, RoadEnds.Result res, bool alignEdits)
    {
        var rep = new Report();
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForWrite);
        var pr = (CivilDb.Profile)tr.GetObject(planId, OpenMode.ForWrite);
        double sA0 = al.StartingStation, sB0 = al.EndingStation;
        bool own = d.OwnCurves && alignEdits;
        // 옮기기 전 — 측점에 묶인 것의 자리(시점을 바꾸면 Δ만큼 옮긴 뒤 같은 자리 1 mm)
        var marks = StationMarks.Load(tr, aid); var drops = StationMarks.LoadDropped(tr, aid);
        var pvOld = new List<(double S, double Z)>(); foreach (CivilDb.ProfilePVI q in pr.PVIs) pvOld.Add((q.RawStation, q.Elevation));
        var before = new List<(string What, double S, double X, double Y, double Z)>();
        void Mark(string w, double s, double z = double.NaN) { if (P(al, s) is { } p) before.Add((w, s, p.X, p.Y, z)); }
        foreach (var w in d.TierWalls) if (w.Length >= 2) { Mark("옹벽", w[0]); Mark("옹벽", w[1]); }
        foreach (var w in d.Ditches) if (w.Length >= 2) { Mark("측구", w[0]); Mark("측구", w[1]); }
        foreach (var m in marks) Mark("수동 측점", m.Station);
        foreach (double s in drops) Mark("지운 측점", s);
        foreach (var (s, z) in pvOld) Mark("PVI", s, z);
        // ★[설치 전 검토 M3 1104] Δ 기준점 = 남는 구간의 가운데 — 옛 가운데가 잘려 나가면 StationOffset이 노선 밖이라 헛되이 물렸다
        double keepA = sA0, keepB = sB0;
        if (own && res.Start.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && res.Start.Station > sA0 + 0.01) keepA = res.Start.Station;
        if (own && res.Finish.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && res.Finish.Station < sB0 - 0.01) keepB = res.Finish.Station;
        double refS = keepB > keepA ? (keepA + keepB) / 2 : (sA0 + sB0) / 2; var refP = P(al, refS);

        // ① 종점
        bool endTrimmed = false, startTrimmed = false;
        // ★[설치 전 검토 M1 1104] 끝마다 따로 — 한쪽만 늘었는데 rep.AlignChanged(아무 끝이나)로 반대쪽까지 늘린 높이를 고정했다
        bool startExtended = false, endExtended = false;
        // ★[설치 전 검토 M2] 꺾어 늘린 끝 — 곡선이 옛 꼭짓점에서 접선 길이 T만큼 깎아 그 안 자리는 XY가 옮겨짐 → 확인에서 빼고 옛 끝 PVI는 새 끝이 대신
        double bentStartT = 0, bentEndT = 0;
        if (own)
        {
            int n = al.Entities.Count;
            var f = res.Finish;
            if (f.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && f.Station < sB0 - 0.01)
            {
                if (al.Entities.GetEntityByOrder(n - 1) is CivilDb.AlignmentLine last && f.Station > last.StartStation + 0.5 && P(al, f.Station) is { } c)
                { Pin1(last); last.PassThroughPoint2 = new Point2d(c.X, c.Y); endTrimmed = true; rep.AlignChanged = true; rep.Notes.Add($"종점 {sB0 - f.Station:F2} m 자름"); }
                else rep.Notes.Add("종점은 끝 요소가 직선이 아니라 가상으로 자름");
            }
            else if (f.Kind == RoadEnds.Kind.Extend && f.Ext != null)
            {
                if (al.Entities.GetEntityByOrder(n - 1) is not CivilDb.AlignmentLine last) rep.Notes.Add("종점 끝 요소가 직선이 아니라 안 늘림");
                else
                {
                    var e0 = last.StartPoint; var e1 = last.EndPoint; double L = last.Length, ux = (e1.X - e0.X) / L, uy = (e1.Y - e0.Y) / L;
                    if (f.Ext.Straight) { Pin1(last); last.PassThroughPoint2 = new Point2d(e1.X + ux * f.Ext.Length, e1.Y + uy * f.Ext.Length); }
                    else
                    {
                        var nl = al.Entities.AddFixedLine(new Point3d(e1.X, e1.Y, 0), new Point3d(f.Ext.Qx, f.Ext.Qy, 0));
                        double rr = TryCurve(al, last.EntityId, nl.EntityId, d.CurveR > 0 ? d.CurveR : 30);
                        if (!double.IsFinite(rr)) throw new InvalidOperationException("종점 꺾인 자리에 곡선을 못 넣음");
                        bentEndT = Tangent(rr, f.Ext.BendDeg);
                    }
                    endExtended = true; rep.AlignChanged = true; rep.Notes.Add($"종점 {f.Ext.Length:F2} m 늘림" + (f.Ext.Straight ? "" : $"(꺾어 {f.Ext.BendDeg:F0}°)"));
                }
            }
        }
        // ② 시점
        if (own)
        {
            var s0e = res.Start;
            if (s0e.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch && s0e.Station > sA0 + 0.01)
            {
                if (al.Entities.GetEntityByOrder(0) is CivilDb.AlignmentLine first && s0e.Station < first.EndStation - 0.5 && P(al, s0e.Station) is { } c)
                { Pin2(first); first.PassThroughPoint1 = new Point2d(c.X, c.Y); startTrimmed = true; rep.AlignChanged = true; rep.Notes.Add($"시점 {s0e.Station - sA0:F2} m 자름"); }
                else rep.Notes.Add("시점은 첫 요소가 직선이 아니라 가상으로 자름");
            }
            else if (s0e.Kind == RoadEnds.Kind.Extend && s0e.Ext != null)
            {
                if (al.Entities.GetEntityByOrder(0) is not CivilDb.AlignmentLine first) rep.Notes.Add("시점 첫 요소가 직선이 아니라 안 늘림");
                else
                {
                    var p0 = first.StartPoint; var p1 = first.EndPoint; double L = first.Length, ux = (p1.X - p0.X) / L, uy = (p1.Y - p0.Y) / L;
                    if (s0e.Ext.Straight) { Pin2(first); first.PassThroughPoint1 = new Point2d(p0.X - ux * s0e.Ext.Length, p0.Y - uy * s0e.Ext.Length); }
                    else
                    {
                        var nl = al.Entities.AddFixedLine(new Point3d(s0e.Ext.Qx, s0e.Ext.Qy, 0), new Point3d(p0.X, p0.Y, 0));
                        double rr = TryCurve(al, nl.EntityId, first.EntityId, d.CurveR > 0 ? d.CurveR : 30);
                        if (!double.IsFinite(rr)) throw new InvalidOperationException("시점 꺾인 자리에 곡선을 못 넣음");
                        bentStartT = Tangent(rr, s0e.Ext.BendDeg);
                    }
                    startExtended = true; rep.AlignChanged = true; rep.Notes.Add($"시점 {s0e.Ext.Length:F2} m 늘림" + (s0e.Ext.Straight ? "" : $"(꺾어 {s0e.Ext.BendDeg:F0}°)"));
                }
            }
        }
        // 시점 측점 = 처음 값(지금 도로 = 0) — Civil이 기준점을 옮기지 않았으면 우리가
        if (rep.AlignChanged && Math.Abs(al.StartingStation - sA0) > 1e-9)
        {
            var sp = P(al, al.StartingStation);
            if (sp != null) { al.ReferencePoint = new Point2d(sp.Value.X, sp.Value.Y); al.ReferencePointStation = sA0; }
        }
        // Δ = 옛 가운데 자리가 새 노선에서 몇 m 밀렸나(늘림 + · 자름 −)
        double delta = 0;
        if (rep.AlignChanged && refP != null)
        {
            double s = 0, o = 0;
            al.StationOffset(refP.Value.X, refP.Value.Y, ref s, ref o);
            if (Math.Abs(o) > 1e-3) throw new InvalidOperationException($"노선을 고친 뒤 옛 자리가 노선에서 {o * 1000:F1} mm 벗어남");
            delta = s - refS;
            if (Math.Abs(delta) < 1e-6) delta = 0;
        }
        rep.Delta = delta;
        double sA = al.StartingStation, sB = al.EndingStation;
        // ③ 측점에 묶인 기록 옮김(0 아래 몫은 잘라냄)
        if (delta != 0)
        {
            List<double[]> ShiftRecs(List<double[]> recs)
            {
                var o = new List<double[]>();
                foreach (var w in recs)
                {
                    if (w.Length < 2) { o.Add(w); continue; }
                    var sp = RoadEnds.ShiftSpan(Math.Min(w[0], w[1]), Math.Max(w[0], w[1]), delta);
                    if (sp == null) continue;
                    var c = (double[])w.Clone(); c[0] = sp.Value.S0; c[1] = sp.Value.S1; o.Add(c); rep.Shifted++;
                }
                return o;
            }
            d.TierWalls = ShiftRecs(d.TierWalls); d.Ditches = ShiftRecs(d.Ditches);
            var m2 = new List<StationMarks.Mark>();
            foreach (var m in marks) { var v = RoadEnds.Shift(m.Station, delta); if (v != null) { m2.Add(m with { Station = v.Value }); rep.Shifted++; } }
            var d2 = new List<double>();
            foreach (double s in drops) { var v = RoadEnds.Shift(s, delta); if (v != null) { d2.Add(v.Value); rep.Shifted++; } }
            if (marks.Count > 0 && !StationMarks.Save(tr, aid, m2)) throw new InvalidOperationException("수동 측점을 못 옮김");
            if (drops.Count > 0 && !StationMarks.SaveDropped(tr, aid, d2)) throw new InvalidOperationException("지운 측점을 못 옮김");
        }
        // ④ PVI — 옛 PVI + Δ → 범위 · 끝 · 빈 구간 고정 높이(5.4)
        var pvS = pvOld.Where(p => !(bentStartT > 0 && p.S <= sA0 + 1e-6) && !(bentEndT > 0 && p.S >= sB0 - 1e-6))
                       .Select(p => (S: p.S + delta, p.Z)).ToList();
        if (pvS.Count == 0) throw new InvalidOperationException("계획 종단 PVI가 없음");
        var st = res.Start; var fi = res.Finish;
        double sa = st.Kind switch
        {
            RoadEnds.Kind.Floor or RoadEnds.Kind.Branch => startTrimmed ? sA : st.Station + delta,
            RoadEnds.Kind.Extend => startExtended ? sA : Math.Max(sA, pvS[0].S),
            _ => Math.Max(sA, pvS[0].S),
        };
        double sb = fi.Kind switch
        {
            RoadEnds.Kind.Floor or RoadEnds.Kind.Branch => endTrimmed ? sB : fi.Station + delta,
            RoadEnds.Kind.Extend => endExtended ? sB : Math.Min(sB, pvS[^1].S),
            _ => Math.Min(sB, pvS[^1].S),
        };
        bool extS = st.Kind == RoadEnds.Kind.Extend && startExtended, extF = fi.Kind == RoadEnds.Kind.Extend && endExtended;
        double za = st.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch || extS ? st.Z : double.NaN;
        double zb = fi.Kind is RoadEnds.Kind.Floor or RoadEnds.Kind.Branch || extF ? fi.Z : double.NaN;
        var gaps = res.Gaps.Select(g => g with { A = g.A + delta, B = g.B + delta }).ToList();
        var want = RoadEnds.PinPvis(pvS, sa, za, sb, zb, gaps);
        // 같으면 안 씀(옛 PVI 그대로)
        bool same = want.Count == pvOld.Count && want.Zip(pvOld).All(q => Math.Abs(q.First.S - q.Second.S) < 1e-6 && Math.Abs(q.First.Z - q.Second.Z) < 1e-6);
        if (!same)
        {
            if (want.Count < 2) throw new InvalidOperationException("고정 PVI가 두 개보다 적음");
            for (int i = pr.PVIs.Count - 2; i >= 1; i--) pr.PVIs.RemoveAt(i);
            var first = pr.PVIs[0]; var last = pr.PVIs[pr.PVIs.Count - 1];
            var (ns0, nz0) = want[0]; var (ns1, nz1) = want[^1];
            if (ns0 >= last.RawStation || (ns1 < last.RawStation && ns1 > first.RawStation)) { last.RawStation = ns1; first.RawStation = ns0; }
            else { first.RawStation = ns0; last.RawStation = ns1; }
            first.Elevation = nz0; last.Elevation = nz1;
            for (int k = 1; k + 1 < want.Count; k++) pr.PVIs.AddPVI(want[k].S, want[k].Z);
            if (pr.PVIs.Count != want.Count) throw new InvalidOperationException($"계획 종단 PVI 수가 다름({pr.PVIs.Count}/{want.Count})");
            rep.PviChanged = true;
            rep.Notes.Add($"끝 PVI 고정({(double.IsFinite(za) ? $"시점 {za:F3}" : "")}{(double.IsFinite(za) && double.IsFinite(zb) ? " · " : "")}{(double.IsFinite(zb) ? $"종점 {zb:F3}" : "")}{(gaps.Count > 0 ? $" · 빈 구간 {gaps.Count}" : "")})");
        }
        // ⑤ 1 mm 확인 — 옮긴 기록 · PVI의 자리(XY)와 PVI 높이(남은 것)
        if (rep.AlignChanged)
        {
            foreach (var b in before)
            {
                double ns = b.S + delta;
                if (ns < sA - 1e-6 || ns > sB + 1e-6) continue;   // 잘린 몫(앞) · 범위 밖(뒤) — 안 씀
                if (bentStartT > 0 && b.S < sA0 + bentStartT || bentEndT > 0 && b.S > sB0 - bentEndT) continue;   // 꺾인 곡선이 깎은 자리(M2)
                if (b.What == "PVI" && !want.Any(q => Math.Abs(q.S - ns) < 1e-6 && Math.Abs(q.Z - b.Z) < 1e-6)) continue;   // 고정 · 지운 PVI
                var p = P(al, ns);
                if (p == null) continue;
                double e = Math.Sqrt(Math.Pow(p.Value.X - b.X, 2) + Math.Pow(p.Value.Y - b.Y, 2));
                rep.Checked++; rep.WorstMm = Math.Max(rep.WorstMm, e * 1000);
                if (e > 1e-3) throw new InvalidOperationException($"{b.What} 측점 {b.S:F3} → {ns:F3}의 자리가 {e * 1000:F1} mm 어긋남 — 끝 맞추기를 물림");
            }
            if (delta != 0)
            {
                pvOld.Clear(); foreach (CivilDb.ProfilePVI q in pr.PVIs) pvOld.Add((q.RawStation, q.Elevation));
                foreach (var b in before.Where(q => q.What == "PVI"))
                {
                    double ns = b.S + delta;
                    if (!want.Any(q => Math.Abs(q.S - ns) < 1e-6 && Math.Abs(q.Z - b.Z) < 1e-6)) continue;
                    if (!pvOld.Any(q => Math.Abs(q.S - ns) < 1e-6 && Math.Abs(q.Z - b.Z) <= 1e-3)) throw new InvalidOperationException($"PVI {b.S:F3}이 옮긴 자리 {ns:F3}에 없음 — 끝 맞추기를 물림");
                }
            }
        }
        return rep;
    }
}
