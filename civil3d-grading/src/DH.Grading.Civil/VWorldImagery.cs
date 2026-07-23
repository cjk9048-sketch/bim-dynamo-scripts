using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DH.Grading.Civil;

/// <summary>브이월드(V-World) 위성영상 익스포터 — 부지 경계상자(EPSG:5186 중부원점)에 걸치는 위성 타일을
/// 받아 한 장으로 이어붙이고, 월드파일(.jgw)+투영정보(.prj)를 함께 저장한다(InfraWorks 래스터 지형피복용).
///
/// 타일 서비스: <c>http://api.vworld.kr/req/wmts/1.0.0/{키}/Satellite/{ز}/{y}/{x}.jpeg</c>
///   — 표준 웹메르카토르(EPSG:3857) XYZ 타일, 위성 최대 줌 z19(≈0.24m/px @ 위도37°).
/// 좌표 흐름: 5186(E,N) → 경위도(WGS84≈GRS80) → 웹메르카토르 타일. 모자이크는 3857 좌표로 월드파일 기록,
///   InfraWorks가 모델 좌표계(5186)로 재투영해 지형 위에 드리운다. (AutoCAD 의존성 없음 — 단독 테스트 가능)</summary>
public static class VWorldImagery
{
    private const string ApiKey = "8EA87CD2-C75D-3407-A41C-D1FBE9B33CAA";
    private const string Layer = "Satellite";
    private const int TileSize = 256;
    private const int MaxZoom = 19;
    private const int MinZoom = 14;
    private const int MaxTilesPerAxis = 60;   // 한 축 최대 타일 수(초과 시 줌 한 단계 낮춰 재계산) — 과다 다운로드 방지
    private const double OriginShift = System.Math.PI * 6378137.0;  // 웹메르카토르 반폭 = 20037508.342789

    private static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromSeconds(20) };

    /// <summary>5186 경계상자 → outFolder에 baseName.jpg/.jgw/.prj 저장. 반환=안내 문자열(개수·줌).</summary>
    public static string Export(double minE, double minN, double maxE, double maxN,
                                string outFolder, string baseName = "위성", double marginM = 20.0)
    {
        if (maxE <= minE || maxN <= minN) return "위성: 경계상자가 유효하지 않아 생략";
        minE -= marginM; minN -= marginM; maxE += marginM; maxN += marginM;

        // 네 모서리 → 경위도(TM은 직각이 아니라 살짝 기우니 4모서리로 안전하게 min/max).
        var c = new[]
        {
            Tm5186ToLonLat(minE, minN), Tm5186ToLonLat(maxE, minN),
            Tm5186ToLonLat(minE, maxN), Tm5186ToLonLat(maxE, maxN),
        };
        double west = double.MaxValue, east = double.MinValue, south = double.MaxValue, north = double.MinValue;
        foreach (var (lon, lat) in c)
        { west = System.Math.Min(west, lon); east = System.Math.Max(east, lon); south = System.Math.Min(south, lat); north = System.Math.Max(north, lat); }

        // 줌 선택 — 최대 줌부터, 타일이 너무 많으면 한 단계씩 낮춘다.
        int z = MaxZoom, xmin = 0, xmax = 0, ymin = 0, ymax = 0;
        while (true)
        {
            (xmin, ymin) = LonLatToTile(west, north, z);   // 북서(NW) = 최소 열·최소 행
            (xmax, ymax) = LonLatToTile(east, south, z);   // 남동(SE) = 최대 열·최대 행
            if ((xmax - xmin + 1 <= MaxTilesPerAxis && ymax - ymin + 1 <= MaxTilesPerAxis) || z <= MinZoom) break;
            z--;
        }
        int cols = xmax - xmin + 1, rows = ymax - ymin + 1;
        if (cols <= 0 || rows <= 0) return "위성: 타일 범위 계산 실패로 생략";

        // 타일 다운로드 + WPF로 한 장에 합성.
        int okTiles = 0;
        var mosaic = new RenderTargetBitmap(cols * TileSize, rows * TileSize, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int ty = ymin; ty <= ymax; ty++)
                for (int tx = xmin; tx <= xmax; tx++)
                {
                    var bmp = DownloadTile(z, ty, tx);
                    if (bmp == null) continue;
                    double px = (tx - xmin) * TileSize, py = (ty - ymin) * TileSize;
                    dc.DrawImage(bmp, new Rect(px, py, TileSize, TileSize));
                    okTiles++;
                }
        }
        mosaic.Render(dv);

        System.IO.Directory.CreateDirectory(outFolder);
        string jpg = System.IO.Path.Combine(outFolder, baseName + ".jpg");
        string jgw = System.IO.Path.Combine(outFolder, baseName + ".jgw");
        string prj = System.IO.Path.Combine(outFolder, baseName + ".prj");

        var enc = new JpegBitmapEncoder { QualityLevel = 90 };
        enc.Frames.Add(BitmapFrame.Create(mosaic));
        using (var fs = System.IO.File.Create(jpg)) enc.Save(fs);

        // 월드파일(.jgw) — 3857 미터. 모자이크 좌상단 = 타일(xmin,ymin) 좌상단.
        double mpp = 2.0 * OriginShift / (TileSize * System.Math.Pow(2, z));   // 픽셀당 미터(3857)
        double x0 = -OriginShift + xmin * TileSize * mpp;   // 좌상단 X(3857)
        double y0 = OriginShift - ymin * TileSize * mpp;    // 좌상단 Y(3857)
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        System.IO.File.WriteAllText(jgw, string.Join("\r\n", new[]
        {
            mpp.ToString("R", inv),            // A: x 픽셀크기
            "0.0", "0.0",                       // D, B: 회전(없음)
            (-mpp).ToString("R", inv),         // E: y 픽셀크기(음수 — 아래로)
            (x0 + mpp / 2).ToString("R", inv), // C: (0,0)픽셀 중심 X
            (y0 - mpp / 2).ToString("R", inv), // F: (0,0)픽셀 중심 Y
        }) + "\r\n");

        System.IO.File.WriteAllText(prj, Epsg3857Wkt);

        return $"위성.jpg 저장 — {cols}×{rows}타일(z{z}, 성공 {okTiles}/{cols * rows}), {mpp:0.00}m/px, 3857 월드파일";
    }

    /// <summary>웹메르카토르 위성 타일 1장 다운로드 → BitmapSource(실패 시 null, 최대 2회 재시도).</summary>
    private static BitmapSource? DownloadTile(int z, int y, int x)
    {
        string url = $"http://api.vworld.kr/req/wmts/1.0.0/{ApiKey}/{Layer}/{z}/{y}/{x}.jpeg";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                byte[] bytes = Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (bytes.Length < 500) return null;   // 없는 타일은 작은 XML 예외로 옴
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new System.IO.MemoryStream(bytes);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { System.Threading.Thread.Sleep(300); }
        }
        return null;
    }

    // ── 좌표 변환 ─────────────────────────────────────────────────────────────
    // 경위도 → 웹메르카토르 XYZ 타일 인덱스(열 x, 행 y).
    private static (int x, int y) LonLatToTile(double lon, double lat, int z)
    {
        double n = System.Math.Pow(2, z);
        double xt = (lon + 180.0) / 360.0 * n;
        double latRad = lat * System.Math.PI / 180.0;
        double yt = (1.0 - System.Math.Asinh(System.Math.Tan(latRad)) / System.Math.PI) / 2.0 * n;
        int xi = (int)System.Math.Floor(xt), yi = (int)System.Math.Floor(yt);
        long max = (long)n - 1;
        return ((int)System.Math.Clamp(xi, 0, max), (int)System.Math.Clamp(yi, 0, max));
    }

    // EPSG:5186 (Korea 2000 / Central Belt 2010, TM) 역투영 → 경도·위도(도). GRS80.
    //   lat0=38°, lon0=127°, k0=1, FE=200000, FN=600000.
    private static (double lon, double lat) Tm5186ToLonLat(double E, double N)
    {
        const double a = 6378137.0;             // GRS80 장반경
        const double f = 1.0 / 298.257222101;   // GRS80 편평률
        const double k0 = 1.0, FE = 200000.0, FN = 600000.0;
        const double lat0 = 38.0 * System.Math.PI / 180.0;
        const double lon0 = 127.0 * System.Math.PI / 180.0;

        double e2 = 2 * f - f * f;              // 제1이심률²
        double ep2 = e2 / (1 - e2);             // 제2이심률²

        double M0 = MeridArc(lat0, a, e2);
        double M = M0 + (N - FN) / k0;
        double mu = M / (a * (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256));
        double e1 = (1 - System.Math.Sqrt(1 - e2)) / (1 + System.Math.Sqrt(1 - e2));

        double phi1 = mu
            + (3 * e1 / 2 - 27 * System.Math.Pow(e1, 3) / 32) * System.Math.Sin(2 * mu)
            + (21 * e1 * e1 / 16 - 55 * System.Math.Pow(e1, 4) / 32) * System.Math.Sin(4 * mu)
            + (151 * System.Math.Pow(e1, 3) / 96) * System.Math.Sin(6 * mu)
            + (1097 * System.Math.Pow(e1, 4) / 512) * System.Math.Sin(8 * mu);

        double sinp = System.Math.Sin(phi1), cosp = System.Math.Cos(phi1), tanp = System.Math.Tan(phi1);
        double C1 = ep2 * cosp * cosp;
        double T1 = tanp * tanp;
        double N1 = a / System.Math.Sqrt(1 - e2 * sinp * sinp);
        double R1 = a * (1 - e2) / System.Math.Pow(1 - e2 * sinp * sinp, 1.5);
        double D = (E - FE) / (N1 * k0);

        double lat = phi1 - (N1 * tanp / R1) * (D * D / 2
            - (5 + 3 * T1 + 10 * C1 - 4 * C1 * C1 - 9 * ep2) * System.Math.Pow(D, 4) / 24
            + (61 + 90 * T1 + 298 * C1 + 45 * T1 * T1 - 252 * ep2 - 3 * C1 * C1) * System.Math.Pow(D, 6) / 720);
        double lon = lon0 + (D
            - (1 + 2 * T1 + C1) * System.Math.Pow(D, 3) / 6
            + (5 - 2 * C1 + 28 * T1 - 3 * C1 * C1 + 8 * ep2 + 24 * T1 * T1) * System.Math.Pow(D, 5) / 120) / cosp;

        return (lon * 180.0 / System.Math.PI, lat * 180.0 / System.Math.PI);
    }

    // 적도~위도 phi 자오선호 길이.
    private static double MeridArc(double phi, double a, double e2)
    {
        return a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * phi
            - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * System.Math.Sin(2 * phi)
            + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * System.Math.Sin(4 * phi)
            - (35 * e2 * e2 * e2 / 3072) * System.Math.Sin(6 * phi));
    }

    // EPSG:3857 WKT(웹메르카토르) — InfraWorks가 래스터 좌표계를 알도록.
    private const string Epsg3857Wkt =
        "PROJCS[\"WGS 84 / Pseudo-Mercator\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\"," +
        "SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]]," +
        "AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]]," +
        "UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]]," +
        "PROJECTION[\"Mercator_1SP\"],PARAMETER[\"central_meridian\",0],PARAMETER[\"scale_factor\",1]," +
        "PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0]," +
        "UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH]," +
        "AUTHORITY[\"EPSG\",\"3857\"]]";
}
