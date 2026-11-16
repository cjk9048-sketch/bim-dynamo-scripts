using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;

namespace DH.Grading.Core.Forest;

/// <summary>★[v117.19 · JACK 1116 «벌개제근»] 벌개제근 면적 = 공사 범위(노리선 안 · 부지 + 진입도로) ∩ 임상도 입목지 폴리곤 — 도면 좌표 ㎡.
/// 토공량표엔 합계 한 줄(K-water BIM 수량산출기준 부속서 10 «벌개제근공 ㎡») · 임상 · 경급별 값은 여기 장부에(엑셀 수량산출서가 나중에 읽는다).
/// <para>입목지 = 임상 코드 1~4(침엽 · 활엽 · 혼효 · 죽림 = storunst «1»). 0(무립목지 · 경작지 · 비산림) · 모름은 뺀다.</para></summary>
public sealed class ClearingResult
{
    public static readonly string[] FrtpNames = { "", "침엽수림", "활엽수림", "혼효림", "죽림" };
    public static readonly string[] DmNames = { "치수(경급 0)", "소경목(경급 1)", "중경목(경급 2)", "대경목(경급 3)", "경급 모름" };

    /// <summary>잰 값이 있나 — false면 «—»(0이 아님).</summary>
    public bool Known;
    public double Total;
    /// <summary>임상별 ㎡ — 인덱스 1~4(0 칸은 안 씀).</summary>
    public readonly double[] ByFrtp = new double[5];
    /// <summary>경급별 ㎡ — 인덱스 0~3 · 4 = 모름.</summary>
    public readonly double[] ByDm = new double[5];
    public int Polys;
    /// <summary>★[수량산출서] 임분고 면적가중 합(㎡·m) · 키를 아는 면적 — 평균 = <see cref="MeanHeight"/>. 임상도 HEIGHT 구간 가운데 값.</summary>
    public double HtSum, HtArea;
    public double MeanHeight => HtArea > 0 ? HtSum / HtArea : double.NaN;
    public string Source = "", Note = "";

    public string Detail()
    {
        string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
        return $"벌개제근 {F(Total)} ㎡ · 임상 {string.Join(" ", Enumerable.Range(1, 4).Select(i => FrtpNames[i] + " " + F(ByFrtp[i])))} · 경급 {string.Join(" ", Enumerable.Range(0, 5).Select(i => DmNames[i] + " " + F(ByDm[i])))} · 폴리곤 {Polys}" + (Source.Length > 0 ? " · " + Source : "");
    }

    public static ClearingResult None(string note) => new() { Known = false, Note = note };
}

public static class ForestClearing
{
    /// <summary><paramref name="toDwg"/> = 5179 → 도면 좌표. <paramref name="footprint"/> = 도면 좌표 공사 범위(노리선 안쪽 전부).</summary>
    public static ClearingResult Compute(IEnumerable<ForestTile> tiles, Func<double, double, (double X, double Y)> toDwg, Geometry footprint)
    {
        var r = new ClearingResult { Known = true };
        var gf = footprint.Factory;
        var prep = PreparedGeometryFactory.Prepare(footprint);
        var fenv = footprint.EnvelopeInternal;
        foreach (var t in tiles)
            foreach (var p in t.Polys)
            {
                if (p.Frtp < 1 || p.Frtp > 4) continue;   // 입목지만
                var poly = ForestTileFormat.ToPolygon(t, p, gf);
                if (poly == null) continue;
                var shell = gf.CreateLinearRing(poly.ExteriorRing.Coordinates.Select(c => { var d = toDwg(c.X, c.Y); return new Coordinate(d.X, d.Y); }).ToArray());
                var holes = Enumerable.Range(0, poly.NumInteriorRings).Select(i => gf.CreateLinearRing(poly.GetInteriorRingN(i).Coordinates.Select(c => { var d = toDwg(c.X, c.Y); return new Coordinate(d.X, d.Y); }).ToArray())).ToArray();
                Geometry g = gf.CreatePolygon(shell, holes);
                if (!fenv.Intersects(g.EnvelopeInternal)) continue;
                double a;
                try
                {
                    if (!g.IsValid) g = g.Buffer(0);
                    if (!prep.Intersects(g)) continue;
                    a = footprint.Intersection(g).Area;
                }
                catch (TopologyException)
                {
                    try { a = NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(footprint).Intersection(NetTopologySuite.Geometries.Utilities.GeometryFixer.Fix(g)).Area; }
                    catch (TopologyException) { r.Note = "일부 폴리곤 겹침 셈 실패(빠짐)"; continue; }
                }
                if (a <= 0) continue;
                var hr = ForestCodes.HeightRange(p.Hgt, p.Dm, p.Frtp);
                r.HtSum += a * (hr.HasValue ? (hr.Value.Lo + hr.Value.Hi) / 2 : 0.5); r.HtArea += a;
                r.Total += a; r.ByFrtp[p.Frtp] += a; r.ByDm[p.Dm <= 3 ? p.Dm : 4] += a; r.Polys++;
            }
        return r;
    }
}
