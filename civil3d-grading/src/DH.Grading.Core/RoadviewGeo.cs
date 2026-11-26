namespace DH.Grading.Core;

/// <summary>★[v117.44 · JACK 1126 «기타 → 로드뷰 · 찍은 위치 위 작은 창 · 보는 방향 도면 표시»] 로드뷰 방향 · 거리 셈.
/// <para>로드뷰(카카오)의 방위각(pan)은 <b>진북</b> 기준 시계방향(도)이다. 도면은 TM 격자라 진북과 격자북이 <b>자오선 수렴각</b>만큼 어긋난다
/// (중부원점에서 1° 떨어진 곳 ≈ 0.6°). 방위각으로 10 m 앞 점을 위경도로 잡고 그 점을 도면 좌표로 바꿔 방향을 얻으면 수렴각이 저절로 들어간다.</para></summary>
public static class RoadviewGeo
{
    const double A = 6378137.0;               // GRS80 장반경
    const double E2 = 0.00669438002290;       // GRS80 이심률²

    /// <summary>위경도(lon, lat) 자리에서 진북 기준 방위각 <paramref name="panDeg"/>(시계방향 도)이 도면(TM: 중앙경선 <paramref name="lon0"/> · 가산 북 <paramref name="fn"/>)에서 가리키는 단위 벡터.</summary>
    public static (double Dx, double Dy) Heading(double lon, double lat, double panDeg, double lon0, double fn)
    {
        const double d = 10.0;   // m — 방향만 쓰므로 짧게(타원체 곡률 차이 무시 가능)
        double phi = lat * System.Math.PI / 180, az = panDeg * System.Math.PI / 180;
        double s = System.Math.Sin(phi), w = System.Math.Sqrt(1 - E2 * s * s);
        double m = A * (1 - E2) / (w * w * w);   // 자오선 곡률 반지름
        double n = A / w;                        // 묘유선 곡률 반지름
        double dLat = d * System.Math.Cos(az) / m * 180 / System.Math.PI;
        double dLon = d * System.Math.Sin(az) / (n * System.Math.Cos(phi)) * 180 / System.Math.PI;
        var p0 = KoreaTm.FromLonLat(lon, lat, lon0, fn);
        var p1 = KoreaTm.FromLonLat(lon + dLon, lat + dLat, lon0, fn);
        double dx = p1.E - p0.E, dy = p1.N - p0.N, len = System.Math.Sqrt(dx * dx + dy * dy);
        return len > 1e-9 ? (dx / len, dy / len) : (0, 1);
    }
}
