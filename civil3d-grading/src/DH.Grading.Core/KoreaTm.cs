using System;

namespace DH.Grading.Core;

/// <summary>★★★[JACK 0901] 한국 평면직각 TM(GRS80) ↔ 경도·위도 — <b>두 방향이 여기 한 곳에</b> 있다.
///
/// <para><b>왜 Core인가.</b> 지도에서 친 박스를 CAD 좌표로 옮기려면 <b>위경도 → TM</b>이 필요한데,
/// 반대 방향(<c>TM → 위경도</c>)은 이미 <c>VWorldImagery</c> 안에 <b>private</b>으로 있었다.
/// 두 방향이 서로 다른 파일에 흩어지면 상수를 한쪽만 고치는 일이 생긴다(§50) —
/// 게다가 이 셈은 <b>AutoCAD 없이 검증할 수 있는</b> 순수 계산이라 Core가 제자리다.</para>
///
/// <para><b>왕복이 맞는지는 하니스가 지킨다</b>(S92) — 한 방향만 맞으면 박스가 엉뚱한 데로 간다.</para>
///
/// <para>공통: GRS80 · <c>k0 = 1</c> · <c>FE = 200000</c> · <c>lat0 = 38°N</c>.
/// 원점별로 다른 것은 <b>중앙자오선</b>(서부125·중부127·동부129·동해131)과
/// <b>원점가산 N</b>(신 600000 · 구 500000 · 제주 550000)뿐이다.</para></summary>
public static class KoreaTm
{
    private const double A = 6378137.0;             // GRS80 장반경
    private const double F = 1.0 / 298.257222101;   // GRS80 편평률
    private const double K0 = 1.0, FE = 200000.0;
    private static readonly double Lat0 = 38.0 * Math.PI / 180.0;

    private const double E2 = 2 * F - F * F;        // 제1이심률²
    private const double Ep2 = E2 / (1 - E2);       // 제2이심률²

    /// <summary>TM → 경도·위도(도).</summary>
    public static (double Lon, double Lat) ToLonLat(double e, double n, double lon0Deg, double fn) => ToLonLat(e, n, lon0Deg, fn, K0, FE);

    /// <summary>★[v115 · JACK 1105 임상도] TM → 경도·위도 — 축척 <paramref name="k0"/> · 원점가산 E <paramref name="fe"/>를 받는 겹함수(UTM-K EPSG 5179 = 127.5° · 0.9996 · 1,000,000 · 2,000,000).
    /// 옛 함수(k0 = 1 · FE = 200000)는 이것을 부른다 — 같은 셈 차례라 비트까지 같다(하네스 S216 ②).</summary>
    public static (double Lon, double Lat) ToLonLat(double e, double n, double lon0Deg, double fn, double k0, double fe)
    {
        double lon0 = lon0Deg * Math.PI / 180.0;
        double m0 = MeridArc(Lat0);
        double m = m0 + (n - fn) / k0;
        double mu = m / (A * (1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256));
        double e1 = (1 - Math.Sqrt(1 - E2)) / (1 + Math.Sqrt(1 - E2));

        double phi1 = mu
            + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
            + (151 * Math.Pow(e1, 3) / 96) * Math.Sin(6 * mu)
            + (1097 * Math.Pow(e1, 4) / 512) * Math.Sin(8 * mu);

        double sinp = Math.Sin(phi1), cosp = Math.Cos(phi1), tanp = Math.Tan(phi1);
        double c1 = Ep2 * cosp * cosp;
        double t1 = tanp * tanp;
        double n1 = A / Math.Sqrt(1 - E2 * sinp * sinp);
        double r1 = A * (1 - E2) / Math.Pow(1 - E2 * sinp * sinp, 1.5);
        double d = (e - fe) / (n1 * k0);

        double lat = phi1 - (n1 * tanp / r1) * (d * d / 2
            - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * Ep2) * Math.Pow(d, 4) / 24
            + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * Ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lon = lon0 + (d
            - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6
            + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * Ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / cosp;

        return (lon * 180.0 / Math.PI, lat * 180.0 / Math.PI);
    }

    /// <summary>★ 경도·위도(도) → TM. <b>지도에서 친 박스를 CAD 좌표로 옮기는 자</b>다.</summary>
    public static (double E, double N) FromLonLat(double lonDeg, double latDeg, double lon0Deg, double fn) => FromLonLat(lonDeg, latDeg, lon0Deg, fn, K0, FE);

    /// <summary>★[v115 · JACK 1105 임상도] 경도·위도 → TM 겹함수(<see cref="ToLonLat(double, double, double, double, double, double)"/>와 짝).</summary>
    public static (double E, double N) FromLonLat(double lonDeg, double latDeg, double lon0Deg, double fn, double k0, double fe)
    {
        double lon = lonDeg * Math.PI / 180.0;
        double lat = latDeg * Math.PI / 180.0;
        double lon0 = lon0Deg * Math.PI / 180.0;

        double sinp = Math.Sin(lat), cosp = Math.Cos(lat), tanp = Math.Tan(lat);
        double n1 = A / Math.Sqrt(1 - E2 * sinp * sinp);
        double t = tanp * tanp;
        double c = Ep2 * cosp * cosp;
        double a1 = (lon - lon0) * cosp;
        double m = MeridArc(lat);
        double m0 = MeridArc(Lat0);

        double e = fe + k0 * n1 * (a1
            + (1 - t + c) * Math.Pow(a1, 3) / 6
            + (5 - 18 * t + t * t + 72 * c - 58 * Ep2) * Math.Pow(a1, 5) / 120);

        double n = fn + k0 * (m - m0 + n1 * tanp * (a1 * a1 / 2
            + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a1, 4) / 24
            + (61 - 58 * t + t * t + 600 * c - 330 * Ep2) * Math.Pow(a1, 6) / 720));

        return (e, n);
    }

    /// <summary>★[v115 · JACK 1105 임상도] UTM-K(EPSG 5179) ↔ 경위도 — 중앙자오선 127.5° · k0 0.9996 · FE 1,000,000 · FN 2,000,000 · 위도 원점 38°(같음).</summary>
    public const double UtmkLon0 = 127.5, UtmkK0 = 0.9996, UtmkFE = 1000000.0, UtmkFN = 2000000.0;
    /// <summary>5179 → 경위도 — 급수 역산 뒤 정산(Newton) 두 번: 정산 = 이 정산이 «정방향 <see cref="UtmkFromLonLat"/>으로 되돌렸을 때 제자리»가 되게.
    /// 1105 실측: 급수 역산만이면 중앙자오선에서 4° 넘는 동쪽(울릉 · 독도 쪽) 왕복이 4.5 mm(정방향은 PROJ와 0.54 mm) — 정산 뒤 왕복 &lt; 0.001 mm.
    /// 옛 함수 · 겹함수(TM 도면 좌표)는 그대로 둔다(비트 같음).</summary>
    public static (double Lon, double Lat) UtmkToLonLat(double x, double y)
    {
        var (lon, lat) = ToLonLat(x, y, UtmkLon0, UtmkFN, UtmkK0, UtmkFE);
        const double h = 1e-6;   // 도(≈ 0.1 m) — 편미분 차분
        for (int it = 0; it < 2; it++)
        {
            var p = UtmkFromLonLat(lon, lat);
            double ex = x - p.X, ey = y - p.Y;
            if (Math.Abs(ex) < 1e-7 && Math.Abs(ey) < 1e-7) break;
            var pl = UtmkFromLonLat(lon + h, lat); var pf = UtmkFromLonLat(lon, lat + h);
            double a = (pl.X - p.X) / h, b = (pf.X - p.X) / h, c = (pl.Y - p.Y) / h, d = (pf.Y - p.Y) / h, det = a * d - b * c;
            if (det == 0 || !double.IsFinite(det)) break;
            lon += (d * ex - b * ey) / det;
            lat += (a * ey - c * ex) / det;
        }
        return (lon, lat);
    }
    public static (double X, double Y) UtmkFromLonLat(double lon, double lat) => FromLonLat(lon, lat, UtmkLon0, UtmkFN, UtmkK0, UtmkFE);

    /// <summary>적도~위도 <paramref name="phi"/> 자오선호 길이.</summary>
    private static double MeridArc(double phi) =>
        A * ((1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256) * phi
           - (3 * E2 / 8 + 3 * E2 * E2 / 32 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(2 * phi)
           + (15 * E2 * E2 / 256 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(4 * phi)
           - (35 * E2 * E2 * E2 / 3072) * Math.Sin(6 * phi));
}
