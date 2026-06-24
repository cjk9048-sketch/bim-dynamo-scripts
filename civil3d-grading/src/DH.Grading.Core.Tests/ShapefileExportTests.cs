using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

public class ShapefileExportTests
{
    // 평탄 계획면(z=20) + 평탄 원지반(z=0) → 성토. SHP 3종(전체부지·계획·소단)이 분류되고 사면은 없어야 한다.
    [Fact]
    public void ExtractAreaPolygons_ProducesSiteBenchPlan()
    {
        var boundary = new List<Point3>
        {
            new(0, 0, 20), new(40, 0, 20), new(40, 40, 20), new(0, 40, 20),
        };
        var ground = new FlatGround(0);
        var p = new GradingParams { BenchHeight = 5, BenchWidth = 1, CutSlope = 1, FillSlope = 1, CellSize = 1.0, MaxBenches = 20 };

        var areas = GradingEngine.ExtractAreaPolygons(boundary, ground, p);

        Assert.Contains(areas, a => a.Category == "전체부지" && a.Index == 0);
        Assert.Contains(areas, a => a.Category == "계획" && a.Index == 0);
        Assert.Contains(areas, a => a.Category == "소단");
        Assert.DoesNotContain(areas, a => a.Category == "사면");

        // 계획 폴리곤은 입력 그대로
        var plan = areas.First(a => a.Category == "계획");
        Assert.NotEmpty(plan.Rings);
        Assert.All(plan.Rings, r => Assert.True(r.Count >= 3));

        // 전체부지(정지경계)는 계획보다 바깥 → 면적이 더 커야 한다
        var site = areas.First(a => a.Category == "전체부지");
        double PlanArea(List<Point3> r) { double s = 0; for (int i = 0, j = r.Count - 1; i < r.Count; j = i++) s += r[j].X * r[i].Y - r[i].X * r[j].Y; return Math.Abs(s) / 2; }
        Assert.True(site.Rings.Sum(PlanArea) > plan.Rings.Sum(PlanArea), "전체부지가 계획면보다 넓어야 함");
    }

    // SHP/SHX/DBF가 규격 헤더로 생성되고, 사각형 좌표가 왕복되어야 한다.
    [Fact]
    public void ShapefileWriter_WritesValidPolygonAndRoundTrips()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dhshp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string basePath = Path.Combine(dir, "사각형");
            var feat = new ShapefileWriter.Feature { Kind = "TEST", Step = 1, Elevation = 12.5 };
            feat.Rings.Add(new List<Point3> { new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0) });
            ShapefileWriter.Write(basePath, new[] { feat });

            Assert.True(File.Exists(basePath + ".shp"));
            Assert.True(File.Exists(basePath + ".shx"));
            Assert.True(File.Exists(basePath + ".dbf"));

            byte[] shp = File.ReadAllBytes(basePath + ".shp");
            Assert.Equal(9994, ReadIntBE(shp, 0));   // 파일코드
            Assert.Equal(1000, ReadIntLE(shp, 28));  // 버전
            Assert.Equal(5, ReadIntLE(shp, 32));     // 폴리곤 타입

            // 첫 레코드: 번호=1, 본문 시작 100바이트
            Assert.Equal(1, ReadIntBE(shp, 100));
            int recShapeType = ReadIntLE(shp, 108);
            Assert.Equal(5, recShapeType);
            int numParts = ReadIntLE(shp, 108 + 4 + 32);
            int numPoints = ReadIntLE(shp, 108 + 4 + 32 + 4);
            Assert.Equal(1, numParts);
            Assert.Equal(5, numPoints); // 닫힌 사각형(4+1)

            // 좌표 왕복: 첫 점 읽기
            int ptsOffset = 108 + 4 + 32 + 4 + 4 + numParts * 4;
            double x0 = ReadDoubleLE(shp, ptsOffset);
            double y0 = ReadDoubleLE(shp, ptsOffset + 8);
            // 외곽 CW 보정으로 시작점이 (0,0) 또는 (0,10)일 수 있음 → 좌표값 자체만 검증
            Assert.True(Math.Abs(x0) < 1e-9 || Math.Abs(x0 - 10) < 1e-9);

            // DBF: dBASE III, 레코드 1개
            byte[] dbf = File.ReadAllBytes(basePath + ".dbf");
            Assert.Equal(0x03, dbf[0]);
            Assert.Equal(1, ReadIntLE(dbf, 4));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static int ReadIntBE(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
    private static int ReadIntLE(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static double ReadDoubleLE(byte[] b, int o) => BitConverter.ToDouble(b, o);
}
