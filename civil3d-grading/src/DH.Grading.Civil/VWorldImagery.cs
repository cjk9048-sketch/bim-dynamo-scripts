using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DH.Grading.Civil;

/// <summary>브이월드(V-World) 위성영상 익스포터 — 부지 경계상자(한국 평면직각 TM, 원점은 파라미터)에 걸치는 위성 타일을
/// 받아 한 장으로 이어붙이고, 월드파일(.jgw)+투영정보(.prj)를 함께 저장한다(InfraWorks 래스터 지형피복용).
///
/// 타일 서비스: <c>http://api.vworld.kr/req/wmts/1.0.0/{키}/Satellite/{z}/{y}/{x}.jpeg</c>
///   — 표준 웹메르카토르(EPSG:3857) XYZ 타일, 위성 최대 줌 z19(≈0.24m/px @ 위도37°).
/// 좌표 흐름: 부지 TM(E,N) → 경위도(GRS80) → 웹메르카토르 타일. 모자이크는 3857 좌표로 월드파일 기록,
///   InfraWorks가 모델 좌표계로 재투영해 지형 위에 드리운다. (AutoCAD 의존성 없음 — 단독 테스트 가능)</summary>
public static class VWorldImagery
{
    /// <summary>VWorld 키 — 타일과 <b>지도 팝업</b>이 같은 것을 쓴다.
    /// <para>★[JACK 0901] 키를 두 곳에 적지 않는다(§50) — <c>MapPage</c>가 여기서 읽어 간다.</para></summary>
    internal const string ApiKey = "8EA87CD2-C75D-3407-A41C-D1FBE9B33CAA";
    private const string Layer = "Satellite";
    private const int TileSize = 256;
    private const int MaxZoom = 19;           // 위성 최대 줌(≈0.24m/px @위도37°) — JACK: 항상 최고해상도만
    private const int MaxTiles = 3000;        // 안전 상한(총 타일 수) — 초과 시 생략(≈800MB 비트맵 방지). 일반 현장은 수십 장.
    private const double OriginShift = System.Math.PI * 6378137.0;  // 웹메르카토르 반폭 = 20037508.342789

    private static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromSeconds(20) };

    // ── [배경지도 0731 — JACK] 도면 부착용 위성 이미지 상한(내보내기용 Export와 별개) ──
    private const int BasemapMaxTiles = 600;             // 중간 모자이크 메모리 상한(≈600×256²×4B ≈ 157MB)
    private const long BasemapMaxOutPixels = 9_000_000;  // 출력 픽셀 상한(≈3000×3000 → TIFF 27MB)

    /// <summary>[배경지도 0731 — JACK] 도면에 깔 위성 이미지 — 두 점으로 지정한 TM 범위를 목표 해상도(m/px)로
    /// 받아 outTifPath(GeoTIFF, 도면 좌표계 격자)로 저장한다. 줌은 목표 해상도에 맞춰 자동 선택하고,
    /// 타일·픽셀 상한을 넘으면 해상도를 한 단계씩 낮춰 재시도 → **어떤 범위를 찍어도 실패 없이 생성**된다.
    /// 반환=(성공, 안내문, 실제해상도 m/px, 가로픽셀, 세로픽셀) — 도면 배치 폭=W·Res, 높이=H·Res(좌하단=minE,minN).</summary>
    /// <param name="progress">[리뷰 0731 M-4] 타일 진행 알림(받은수, 전체수) — 호출부가 진행막대를 띄워
    /// 다운로드 동안 AutoCAD가 멈춘 것처럼 보이지 않게 한다. null이면 알림 없음.</param>
    public static (bool Ok, string Msg, double Res, int W, int H) ExportBasemap(
        double minE, double minN, double maxE, double maxN,
        string outTifPath, double targetRes,
        double lon0Deg, double falseNorthing, int epsgOut,
        System.Action<int, int>? progress = null)
    {
        if (maxE <= minE || maxN <= minN) return (false, "지정한 범위가 유효하지 않습니다.", 0, 0, 0);

        // 네 모서리 → 경위도 bbox(TM은 직각이 아니라 살짝 기울어 4모서리로 안전하게).
        var c = new[]
        {
            TmToLonLat(minE, minN, lon0Deg, falseNorthing), TmToLonLat(maxE, minN, lon0Deg, falseNorthing),
            TmToLonLat(minE, maxN, lon0Deg, falseNorthing), TmToLonLat(maxE, maxN, lon0Deg, falseNorthing),
        };
        double west = double.MaxValue, east = double.MinValue, south = double.MaxValue, north = double.MinValue;
        foreach (var (lon, lat) in c)
        { west = System.Math.Min(west, lon); east = System.Math.Max(east, lon); south = System.Math.Min(south, lat); north = System.Math.Max(north, lat); }
        double latMid = (south + north) / 2 * System.Math.PI / 180.0;
        double cosLat = System.Math.Max(0.1, System.Math.Cos(latMid));

        // 목표 해상도 → 줌 자동 선택. 상한 초과면 해상도를 2배씩 낮춰 재시도.
        double res = System.Math.Max(0.1, targetRes);
        int z = MaxZoom, xmin = 0, ymin = 0, xmax = 0, ymax = 0, cols = 0, rows = 0, outW = 0, outH = 0;
        bool fit = false;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            // 지상해상도(=mpp·cosLat)가 res가 되는 줌: 2^z = 2·OriginShift·cosLat / (TileSize·res)
            z = (int)System.Math.Round(System.Math.Log(2.0 * OriginShift * cosLat / (TileSize * res), 2));
            z = System.Math.Clamp(z, 8, MaxZoom);
            (xmin, ymin) = LonLatToTile(west, north, z);
            (xmax, ymax) = LonLatToTile(east, south, z);
            cols = xmax - xmin + 1; rows = ymax - ymin + 1;
            outW = (int)System.Math.Ceiling((maxE - minE) / res);
            outH = (int)System.Math.Ceiling((maxN - minN) / res);
            if (cols > 0 && rows > 0 && outW >= 2 && outH >= 2 &&
                (long)cols * rows <= BasemapMaxTiles && (long)outW * outH <= BasemapMaxOutPixels)
            { fit = true; break; }
            res *= 2.0;   // 한 단계 낮춰 재시도
        }
        if (!fit) return (false, "지정한 범위를 이 화질로 담을 수 없습니다 — 범위를 좁히거나 정지옵션에서 화질을 낮추세요.", 0, 0, 0);

        // 타일 다운로드 + 모자이크.
        int okTiles = 0, doneTiles = 0, totalTiles = cols * rows;
        var mosaic = new RenderTargetBitmap(cols * TileSize, rows * TileSize, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            for (int ty = ymin; ty <= ymax; ty++)
                for (int tx = xmin; tx <= xmax; tx++)
                {
                    var bmp = DownloadTile(z, ty, tx);
                    doneTiles++;
                    progress?.Invoke(doneTiles, totalTiles);
                    if (bmp == null) continue;
                    dc.DrawImage(bmp, new Rect((tx - xmin) * TileSize, (ty - ymin) * TileSize, TileSize, TileSize));
                    okTiles++;
                }
        }
        mosaic.Render(dv);
        // [JACK 0731] 전량 실패의 흔한 원인은 인터넷이 아니라 '좌표계가 틀려 위성사진이 없는 위치를 요청'한 경우.
        if (okTiles == 0) return (false, "해당하는 위치에 위성사진이 존재하지 않습니다.\n좌표계(원점)가 맞는지 확인하세요. (인터넷 차단일 수도 있음)", 0, 0, 0);

        int W = cols * TileSize, H = rows * TileSize;
        int stride = W * 4;
        long need = (long)H * stride;
        if (need > int.MaxValue) return (false, "이미지가 너무 커서 생성할 수 없습니다 — 범위를 좁히세요.", 0, 0, 0);
        var bgra = new byte[need];
        mosaic.CopyPixels(bgra, stride, 0);

        double mpp = 2.0 * OriginShift / (TileSize * System.Math.Pow(2, z));
        double x0 = -OriginShift + xmin * TileSize * mpp;
        double y0 = OriginShift - ymin * TileSize * mpp;

        // 도면 좌표계(TM) 격자로 재투영 → 도면에 그대로 앉힌다(좌표계 연동, JACK 0731).
        var rgbTm = ReprojectToTm(bgra, W, H, stride, x0, y0, mpp, minE, maxN, res, outW, outH, lon0Deg, falseNorthing);
        try { System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outTifPath)!); } catch { }
        WriteGeoTiff(outTifPath, rgbTm, outW, outH, res, minE, maxN, epsgOut);

        string note = res > targetRes + 1e-9 ? $" (범위가 넓어 화질 자동 조정: {targetRes:0.##}→{res:0.##}m/px)" : "";
        return (true, $"위성 {cols}×{rows}타일(z{z}, 성공 {okTiles}/{cols * rows}) · {res:0.##}m/px · {outW}×{outH}px{note}", res, outW, outH);
    }

    /// <summary>부지 경계상자(한국 평면직각 TM) → outFolder에 baseName.jpg/.jgw/.prj 저장. 반환=안내 문자열(개수·줌).
    /// lon0Deg=중앙자오선 경도(서부125·중부127·동부129·동해131), falseNorthing=원점가산 N(신600000·구500000·제주550000).
    /// [JACK 0728] epsgOut(한국 TM 벨트 EPSG, 예 5186)을 주면 웹메르카토르 모자이크를 그 좌표계 격자로
    /// **재투영해서** GeoTIFF에 내장 — InfraWorks에서 위성이 WGS84가 아니라 도면 좌표계로 인식되게 한다.</summary>
    public static string Export(double minE, double minN, double maxE, double maxN,
                                string outFolder, string baseName = "위성", double marginM = 20.0,
                                double lon0Deg = 127.0, double falseNorthing = 600000.0,
                                int epsgOut = 3857)
    {
        if (maxE <= minE || maxN <= minN) return "위성: 경계상자가 유효하지 않아 생략";
        minE -= marginM; minN -= marginM; maxE += marginM; maxN += marginM;

        // 네 모서리 → 경위도(TM은 직각이 아니라 살짝 기우니 4모서리로 안전하게 min/max).
        var c = new[]
        {
            TmToLonLat(minE, minN, lon0Deg, falseNorthing), TmToLonLat(maxE, minN, lon0Deg, falseNorthing),
            TmToLonLat(minE, maxN, lon0Deg, falseNorthing), TmToLonLat(maxE, maxN, lon0Deg, falseNorthing),
        };
        double west = double.MaxValue, east = double.MinValue, south = double.MaxValue, north = double.MinValue;
        foreach (var (lon, lat) in c)
        { west = System.Math.Min(west, lon); east = System.Math.Max(east, lon); south = System.Math.Min(south, lat); north = System.Math.Max(north, lat); }

        // [JACK 0723] 항상 최고해상도(z19)만 — 줌 자동 하향 없음. 안전 상한만 검사.
        int z = MaxZoom;
        var (xmin, ymin) = LonLatToTile(west, north, z);   // 북서(NW) = 최소 열·최소 행
        var (xmax, ymax) = LonLatToTile(east, south, z);   // 남동(SE) = 최대 열·최대 행
        int cols = xmax - xmin + 1, rows = ymax - ymin + 1;
        if (cols <= 0 || rows <= 0) return "위성: 타일 범위 계산 실패로 생략";
        if ((long)cols * rows > MaxTiles)
            return $"위성: 부지가 너무 넓어 최고해상도(z{z}) 타일 {cols}×{rows}={cols * rows}장 > 상한 {MaxTiles} — 생략(부지 축소 필요).";

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
        string tif = System.IO.Path.Combine(outFolder, baseName + ".tif");

        // 모자이크 → RGB 바이트(Pbgra32, 불투명 → 프리멀티 무영향).
        int W = cols * TileSize, H = rows * TileSize;
        int stride = W * 4;
        var bgra = new byte[H * (long)stride > int.MaxValue ? 0 : H * stride];
        if (bgra.Length == 0) return "위성: 이미지가 너무 커서 생략(부지 축소 필요)";
        mosaic.CopyPixels(bgra, stride, 0);

        // 3857 지오레퍼런싱 — 모자이크 좌상단 = 타일(xmin,ymin) 좌상단.
        double mpp = 2.0 * OriginShift / (TileSize * System.Math.Pow(2, z));   // 픽셀당 미터(3857)
        double x0 = -OriginShift + xmin * TileSize * mpp;   // 좌상단 X(3857)
        double y0 = OriginShift - ymin * TileSize * mpp;    // 좌상단 Y(3857)

        // ── [JACK 0728] 한국 TM 벨트로 재투영 — InfraWorks가 위성을 도면 좌표계로 인식하게 한다. ──
        if (epsgOut > 0 && epsgOut != 3857)
        {
            double latMid = (south + north) / 2 * System.Math.PI / 180.0;
            double res = System.Math.Max(0.05, mpp * System.Math.Cos(latMid));  // 지상 해상도(m/px)
            int outW = (int)System.Math.Ceiling((maxE - minE) / res);
            int outH = (int)System.Math.Ceiling((maxN - minN) / res);
            if (outW >= 2 && outH >= 2 && (long)outW * outH * 3 <= 400_000_000)
            {
                var rgbTm = ReprojectToTm(bgra, W, H, stride, x0, y0, mpp,
                                          minE, maxN, res, outW, outH, lon0Deg, falseNorthing);
                WriteGeoTiff(tif, rgbTm, outW, outH, res, minE, maxN, epsgOut);
                return $"위성.tif(GeoTIFF) 저장 — {cols}×{rows}타일(z{z}, 성공 {okTiles}/{cols * rows}), " +
                       $"{res:0.00}m/px, EPSG:{epsgOut} 재투영 내장(도면 좌표계 일치)";
            }
            // 너무 크면 재투영 생략하고 3857로 폴백(아래).
        }

        var rgb = new byte[W * H * 3];
        for (int i = 0, j = 0; i < bgra.Length; i += 4) { rgb[j++] = bgra[i + 2]; rgb[j++] = bgra[i + 1]; rgb[j++] = bgra[i]; }
        WriteGeoTiff(tif, rgb, W, H, mpp, x0, y0, 3857);   // EPSG:3857 내장 GeoTIFF(무손실)
        return $"위성.tif(GeoTIFF) 저장 — {cols}×{rows}타일(z{z}, 성공 {okTiles}/{cols * rows}), {mpp:0.00}m/px, EPSG:3857 내장";
    }

    /// <summary>웹메르카토르 모자이크(bgra)를 한국 TM 격자(res m/px, 좌상단 minE/maxN)로 리샘플(최근접).
    /// 투영은 매우 매끄러우므로 32px 격자에서만 정확 계산하고 그 사이는 쌍선형 보간 — 픽셀당 역투영 없이 빠르다.</summary>
    private static byte[] ReprojectToTm(byte[] bgra, int W, int H, int stride,
        double x0, double y0, double mpp, double e0, double n0, double res, int outW, int outH,
        double lon0Deg, double falseNorthing)
    {
        const int G = 32;
        int gx = outW / G + 2, gy = outH / G + 2;
        var fx = new double[gy, gx];
        var fy = new double[gy, gx];
        for (int j = 0; j < gy; j++)
            for (int i = 0; i < gx; i++)
            {
                double E = e0 + (i * (double)G + 0.5) * res;
                double N = n0 - (j * (double)G + 0.5) * res;
                var (lon, lat) = TmToLonLat(E, N, lon0Deg, falseNorthing);
                double mx = lon / 180.0 * OriginShift;
                double my = System.Math.Log(System.Math.Tan((90.0 + lat) * System.Math.PI / 360.0)) / System.Math.PI * OriginShift;
                fx[j, i] = (mx - x0) / mpp;
                fy[j, i] = (y0 - my) / mpp;
            }

        var rgb = new byte[outW * outH * 3];
        for (int py = 0; py < outH; py++)
        {
            int j0 = py / G; double tv = (py % G) / (double)G;
            for (int px = 0; px < outW; px++)
            {
                int i0 = px / G; double tu = (px % G) / (double)G;
                double sx = (fx[j0, i0] * (1 - tu) + fx[j0, i0 + 1] * tu) * (1 - tv)
                          + (fx[j0 + 1, i0] * (1 - tu) + fx[j0 + 1, i0 + 1] * tu) * tv;
                double sy = (fy[j0, i0] * (1 - tu) + fy[j0, i0 + 1] * tu) * (1 - tv)
                          + (fy[j0 + 1, i0] * (1 - tu) + fy[j0 + 1, i0 + 1] * tu) * tv;
                int ix = (int)(sx + 0.5), iy = (int)(sy + 0.5);   // 최근접(반올림 — 절삭 시 ~0.2m 좌하 편이)
                if (ix < 0 || iy < 0 || ix >= W || iy >= H) continue;   // 모자이크 밖 → 검정
                int src = iy * stride + ix * 4;
                int dst = (py * outW + px) * 3;
                rgb[dst] = bgra[src + 2]; rgb[dst + 1] = bgra[src + 1]; rgb[dst + 2] = bgra[src];
            }
        }
        return rgb;
    }

    /// <summary>최소 GeoTIFF 라이터(무손실 RGB, 무압축, 단일 스트립) — 외부 라이브러리 없이 GeoTIFF 태그를 직접 기록.
    /// 좌표계는 ProjectedCSTypeGeoKey=epsg(3857), 픽셀 스케일·타이포인트로 지오레퍼런싱. InfraWorks 래스터 임포트용.</summary>
    private static void WriteGeoTiff(string path, byte[] rgb, int W, int H, double mpp, double x0, double y0, int epsg)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        w.Write((byte)'I'); w.Write((byte)'I'); w.Write((ushort)42); w.Write((uint)0);   // 헤더(IFD 오프셋은 뒤에 패치)

        long imgOff = ms.Position;               // 8 — 이미지 데이터(단일 스트립)
        w.Write(rgb);
        if (ms.Position % 2 == 1) w.Write((byte)0);   // 워드 정렬

        long bpsOff = ms.Position; w.Write((ushort)8); w.Write((ushort)8); w.Write((ushort)8);      // BitsPerSample 8,8,8
        long xresOff = ms.Position; w.Write((uint)72); w.Write((uint)1);                             // XResolution 72/1
        long yresOff = ms.Position; w.Write((uint)72); w.Write((uint)1);
        long scaleOff = ms.Position; w.Write(mpp); w.Write(mpp); w.Write(0.0);                        // ModelPixelScale
        long tieOff = ms.Position; w.Write(0.0); w.Write(0.0); w.Write(0.0); w.Write(x0); w.Write(y0); w.Write(0.0); // ModelTiepoint
        long geoOff = ms.Position;
        foreach (var s in new ushort[] { 1, 1, 0, 3,  1024, 0, 1, 1,  1025, 0, 1, 1,  3072, 0, 1, (ushort)epsg }) w.Write(s); // GeoKeyDirectory

        if (ms.Position % 2 == 1) w.Write((byte)0);
        long ifdOff = ms.Position;
        void E(ushort tag, ushort type, uint count, uint val) { w.Write(tag); w.Write(type); w.Write(count); w.Write(val); }
        w.Write((ushort)16);                     // 태그 수 (오름차순)
        E(256, 4, 1, (uint)W);                   // ImageWidth
        E(257, 4, 1, (uint)H);                   // ImageLength
        E(258, 3, 3, (uint)bpsOff);              // BitsPerSample
        E(259, 3, 1, 1);                         // Compression=없음
        E(262, 3, 1, 2);                         // Photometric=RGB
        E(273, 4, 1, (uint)imgOff);              // StripOffsets
        E(277, 3, 1, 3);                         // SamplesPerPixel
        E(278, 4, 1, (uint)H);                   // RowsPerStrip
        E(279, 4, 1, (uint)rgb.Length);          // StripByteCounts
        E(282, 5, 1, (uint)xresOff);             // XResolution
        E(283, 5, 1, (uint)yresOff);             // YResolution
        E(284, 3, 1, 1);                         // PlanarConfig=chunky
        E(296, 3, 1, 2);                         // ResolutionUnit=inch
        E(33550, 12, 3, (uint)scaleOff);         // ModelPixelScaleTag
        E(33922, 12, 6, (uint)tieOff);           // ModelTiepointTag
        E(34735, 3, 16, (uint)geoOff);           // GeoKeyDirectoryTag
        w.Write((uint)0);                        // 다음 IFD 없음
        w.Flush();

        var bytes = ms.ToArray();
        System.BitConverter.GetBytes((uint)ifdOff).CopyTo(bytes, 4);   // 헤더의 IFD 오프셋 패치
        System.IO.File.WriteAllBytes(path, bytes);
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

    /// <summary>한국 평면직각 TM(GRS80) 역투영 → 경도·위도(도).
    /// <para>★★<b>식은 <see cref="DH.Grading.Core.KoreaTm"/> 한 곳에만 둔다</b>(§50).
    /// 예전에는 여기 private으로 같은 식이 한 벌 더 있었다 — 상수를 한쪽만 고치면
    /// 배경지도와 [지도범위]가 <b>서로 다른 자리</b>를 가리키는데 아무 오류도 안 난다.
    /// 지우기 전에 하니스 S93이 옛 식과 맞대 <b>1092점 전부 같은 값</b>임을 확인했다.</para></summary>
    private static (double lon, double lat) TmToLonLat(double E, double N, double lon0Deg, double FN)
    {
        var v = DH.Grading.Core.KoreaTm.ToLonLat(E, N, lon0Deg, FN);
        return (v.Lon, v.Lat);
    }
}
