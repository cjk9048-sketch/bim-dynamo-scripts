using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DH.Grading.Core;
using CivilDb = Autodesk.Civil.DatabaseServices;

namespace DH.Grading.Civil;

/// <summary>★★[도로 마무리 ② · 계획_도로마무리.md 6.4 · 6.7] 측구 평면 선 · 수량 — 기록(나중 기록이 이김) ∩ 도로 구간(<see cref="RoadRanges"/> — 잘린 끝 · 빈 구간 몫 뺌).
/// <para>선 = 측구 가운데(옵셋 = 길어깨 끝 + 윗폭/2 · 높이 = 길어깨 끝) · 0.5 m마다 + 노선 요소 경계 + 구간 끝 · 길이 = 그 선을 따라(곡선 바깥이면 중심선보다 김 — 실제 시공 길이).</para></summary>
internal static class RoadDitchLines
{
    internal const string Layer = "DH도로-측구선";
    internal const short Aci = 4;

    internal sealed class DitchLine
    {
        public int Side; public double A, B; public double[] Rec = Array.Empty<double>(); public DitchSection.Shape Shape = null!;
        public List<Point3d> Pts = new();
        public double Length;
    }

    /// <summary>노선 측점 s · 옆거리 o(Civil — 오른쪽 +) 자리.</summary>
    static bool At(CivilDb.Alignment al, double s, double o, out double x, out double y)
    {
        x = 0; y = 0;
        try { al.PointLocation(s, o, ref x, ref y); return double.IsFinite(x) && double.IsFinite(y); } catch { return false; }
    }

    /// <summary>측점 — 0.5 m마다 + 노선 요소 경계 + 끝.</summary>
    internal static List<double> Stations(CivilDb.Alignment al, double a, double b, double step = 0.5)
    {
        var l = new List<double>();
        for (int k = 0; a + k * step < b; k++) l.Add(a + k * step);
        l.Add(b);
        try
        {
            foreach (CivilDb.AlignmentEntity e in al.Entities)
                for (int i = 0; i < e.SubEntityCount; i++) { var se = e[i]; foreach (double s in new[] { se.StartStation, se.EndStation }) if (s > a && s < b) l.Add(s); }
        }
        catch { }
        l.Sort();
        var o = new List<double>(l.Count);
        foreach (double s in l) if (o.Count == 0 || s - o[^1] > 1e-6) o.Add(s);
        return o;
    }

    /// <summary>측구 가운데 선들(쪽 · 구간마다) — 기록이 없으면 빈 목록.</summary>
    internal static List<DitchLine> Lines(Transaction tr, ObjectId aid, ObjectId planId, RoadDesign d, IReadOnlyList<(double A, double B)> ranges)
    {
        var o = new List<DitchLine>();
        if (d.Ditches.Count == 0 || ranges.Count == 0) return o;
        var al = (CivilDb.Alignment)tr.GetObject(aid, OpenMode.ForRead);
        CivilDb.Profile? pr = null;
        try { if (!planId.IsNull && !planId.IsErased) pr = (CivilDb.Profile)tr.GetObject(planId, OpenMode.ForRead); } catch { }
        var sec = d.Section();
        foreach (int side in new[] { -1, 1 })
            foreach (var (a0, b0, rec) in d.DitchSpans(side))
            {
                var sh = RoadDesign.ShapeOf(rec);
                if (sh == null) continue;
                double outer = side < 0 ? sec.OuterL : sec.OuterR, off = side * (outer + sh.TopWidth / 2);
                foreach (var (a, b) in RoadRanges.Clip(ranges, a0, b0))
                {
                    var dl = new DitchLine { Side = side, A = a, B = b, Rec = rec, Shape = sh };
                    foreach (double s in Stations(al, a, b))
                    {
                        if (!At(al, s, off, out double x, out double y)) continue;
                        double z = 0;
                        try { if (pr != null) z = pr.ElevationAt(s) + sec.Drop(side * outer); } catch { z = 0; }
                        var p = new Point3d(x, y, z);
                        if (dl.Pts.Count > 0) { double l = Math.Sqrt(Math.Pow(p.X - dl.Pts[^1].X, 2) + Math.Pow(p.Y - dl.Pts[^1].Y, 2)); if (l < 1e-6) continue; dl.Length += l; }
                        dl.Pts.Add(p);
                    }
                    if (dl.Pts.Count >= 2) o.Add(dl);
                }
            }
        return o;
    }

    /// <summary>■ 결과 수량 — 형식 · 치수 묶음마다 «U형 측구 B300×H300 2구간 85.2 m(콘크리트 21.7 ㎥)» · 기초 포함(U형 · 계획 12장).</summary>
    internal static string Quantities(List<DitchLine> ls)
    {
        if (ls.Count == 0) return "";
        var g = ls.GroupBy(l => (l.Shape.Kind, l.Shape.Label, Math.Round(l.Shape.Area, 6))).OrderBy(q => q.Key.Kind).ThenBy(q => q.Key.Label);
        return string.Join(" · ", g.Select(q =>
        {
            double len = q.Sum(l => l.Length), vol = len * q.Key.Item3;
            return $"{q.Key.Label} {q.Count()}구간 {len:F1} m(콘크리트 {vol:F1} ㎥{(q.Key.Kind == DitchSection.Kind.U ? " · 기초 포함" : "")})";
        }));
    }
}
