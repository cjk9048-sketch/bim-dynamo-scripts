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
    public static (double Lon, double Lat) ToLonLat(double e, double n, double lon0Deg, double fn)
    {
        double lon0 = lon0Deg * Math.PI / 180.0;
        double m0 = MeridArc(Lat0);
        double m = m0 + (n - fn) / K0;
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
        double d = (e - FE) / (n1 * K0);

        double lat = phi1 - (n1 * tanp / r1) * (d * d / 2
            - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * Ep2) * Math.Pow(d, 4) / 24
            + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * Ep2 - 3 * c1 * c1) * Math.Pow(d, 6) / 720);
        double lon = lon0 + (d
            - (1 + 2 * t1 + c1) * Math.Pow(d, 3) / 6
            + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * Ep2 + 24 * t1 * t1) * Math.Pow(d, 5) / 120) / cosp;

        return (lon * 180.0 / Math.PI, lat * 180.0 / Math.PI);
    }

    /// <summary>★ 경도·위도(도) → TM. <b>지도에서 친 박스를 CAD 좌표로 옮기는 자</b>다.</summary>
    public static (double E, double N) FromLonLat(double lonDeg, double latDeg, double lon0Deg, double fn)
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

        double e = FE + K0 * n1 * (a1
            + (1 - t + c) * Math.Pow(a1, 3) / 6
            + (5 - 18 * t + t * t + 72 * c - 58 * Ep2) * Math.Pow(a1, 5) / 120);

        double n = fn + K0 * (m - m0 + n1 * tanp * (a1 * a1 / 2
            + (5 - t + 9 * c + 4 * c * c) * Math.Pow(a1, 4) / 24
            + (61 - 58 * t + t * t + 600 * c - 330 * Ep2) * Math.Pow(a1, 6) / 720));

        return (e, n);
    }

    /// <summary>적도~위도 <paramref name="phi"/> 자오선호 길이.</summary>
    private static double MeridArc(double phi) =>
        A * ((1 - E2 / 4 - 3 * E2 * E2 / 64 - 5 * E2 * E2 * E2 / 256) * phi
           - (3 * E2 / 8 + 3 * E2 * E2 / 32 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(2 * phi)
           + (15 * E2 * E2 / 256 + 45 * E2 * E2 * E2 / 1024) * Math.Sin(4 * phi)
           - (35 * E2 * E2 * E2 / 3072) * Math.Sin(6 * phi));
}
