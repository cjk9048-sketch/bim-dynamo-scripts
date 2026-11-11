using System.Globalization;
using System.Text;
using DH.CivilCoord.Core;
using Xunit;

namespace DH.CivilCoord.Tests
{
    /// <summary>Civil 3D 영역 파일(DH.CivilRegion3D v1 — 1111 Civil 터미널과 합의한 꼴) 읽기 · 성긴 격자. 좌표는 모두 가짜 값.</summary>
    public class Region3DTests
    {
        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>가짜 지표면: 네모 [x0, x0+w] × [y0, y0+h] 를 n×n 칸으로 · z = a + bx·(x−x0) + by·(y−y0) (평면).</summary>
        static string Sample(int n = 4, double x0 = 200000, double y0 = 450000, double w = 40, double h = 30, double a = 50, double bx = 0.02, double by = -0.01,
                             string extraMesh = "", string lines = "", string head = "")
        {
            var v = new StringBuilder(); var t = new StringBuilder();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    double x = x0 + w * i / n, y = y0 + h * j / n, z = a + bx * (x - x0) + by * (y - y0);
                    if (v.Length > 0) v.Append(',');
                    v.Append($"[{R(x)},{R(y)},{R(z)}]");
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int p = j * (n + 1) + i, q = p + 1, r = p + n + 2, s = p + n + 1;
                    if (t.Length > 0) t.Append(',');
                    t.Append($"[{p},{q},{r}],[{p},{r},{s}]");
                }
            return "{\"format\":\"DH.CivilRegion3D\",\"version\":1,\"units\":{\"length\":\"m\",\"toMeter\":1}" + head +
                   ",\"structure\":{\"name\":\"시험 구조물\"},\"pointsFile\":\"시험 구조물_Civil기준점.json\"," +
                   $"\"box\":{{\"x0\":{R(x0)},\"y0\":{R(y0)},\"x1\":{R(x0 + w)},\"y1\":{R(y0 + h)}}}," +
                   "\"meshes\":[{\"kind\":\"final\",\"name\":\"최종지표면_DH\",\"layer\":\"C-지표면\",\"verts\":[" + v + "],\"tris\":[" + t + "]}" + extraMesh + "]," +
                   "\"lines\":[" + lines + "]}";
        }

        [Fact]
        public void Reads_MeshesLinesBoxAndNames()
        {
            string json = Sample(lines: "{\"kind\":\"wall\",\"layer\":\"DH-옹벽선\",\"closed\":false,\"pts\":[[200001,450001,51.2],[200010,450001,51.3]]}," +
                                        "{\"kind\":\"slope\",\"closed\":true,\"pts\":[[200002,450002,50],[200003,450002,50],[200003,450003,50]]}");
            var r = Region3D.Parse(json, out var err);
            Assert.Null(err);
            Assert.Equal("시험 구조물", r.Name);
            Assert.Equal("시험 구조물_Civil기준점.json", r.PointsFile);
            Assert.Equal((200000.0, 450000.0, 200040.0, 450030.0), r.Box);
            Assert.Single(r.Meshes);
            Assert.Equal(32, r.Triangles);
            Assert.Equal(25, r.Meshes[0].VertexCount);
            Assert.Equal("최종지표면_DH", r.Meshes[0].Name);
            Assert.Equal(2, r.Lines.Count);
            Assert.Equal("slope", r.Lines[1].Kind);
            Assert.True(r.Lines[1].Closed);
            Assert.Equal(51.3, r.Lines[0].Pts[1].Z);
            var b = r.Bounds()!.Value;
            Assert.Equal(200000, b.X0); Assert.Equal(450030, b.Y1);
            Assert.Equal(0, r.DroppedTriangles + r.DroppedLines);
        }

        [Fact]
        public void BadTrianglesAndLines_AreDroppedAndCounted_NotFatal()
        {
            // 범위 밖 번호 · 같은 번호 · 숫자 아닌 꼭짓점을 쓰는 세모 · 점 하나 선 · 숫자 아닌 선 점
            string extra = ",{\"kind\":\"final\",\"verts\":[[1,2,3],[4,5,6],[\"x\",1,1],[7,8,9]],\"tris\":[[0,1,3],[0,1,9],[0,0,1],[0,1,2],[1,2,3]]}";
            string json = Sample(extraMesh: extra, lines: "{\"kind\":\"wall\",\"pts\":[[1,2,3]]},{\"kind\":\"site\",\"pts\":[[1,2,3],[\"a\",2,3]]}");
            var r = Region3D.Parse(json, out var err);
            Assert.Null(err);
            Assert.Equal(2, r.Meshes.Count);
            Assert.Equal(1, r.Meshes[1].TriangleCount);   // [0,1,3] 만
            Assert.Equal(4, r.DroppedTriangles);
            Assert.Empty(r.Lines);
            Assert.Equal(2, r.DroppedLines);
        }

        [Fact]
        public void EmptyMeshes_Allowed_LinesOnly()
        {
            string json = "{\"format\":\"DH.CivilRegion3D\",\"version\":1,\"units\":{\"length\":\"m\",\"toMeter\":1},\"meshes\":[],\"lines\":[{\"kind\":\"wall\",\"pts\":[[1,2,3],[4,5,6]]}]}";
            var r = Region3D.Parse(json, out var err);
            Assert.Null(err);
            Assert.Empty(r.Meshes);
            Assert.Single(r.Lines);
        }

        [Theory]
        [InlineData("{\"format\":\"DH.CivilStructPoints\",\"version\":1,\"units\":{\"length\":\"m\"}}", "3D 영역 파일이 아님")]
        [InlineData("{\"format\":\"DH.CivilRegion3D\",\"version\":2,\"units\":{\"length\":\"m\"}}", "판 2")]
        [InlineData("{\"format\":\"DH.CivilRegion3D\",\"version\":1,\"units\":{\"length\":\"mm\"}}", "m가 아님")]
        [InlineData("{\"format\":\"DH.CivilRegion3D\",\"version\":1,\"units\":{\"length\":\"m\",\"toMeter\":0.001}}", "toMeter")]
        [InlineData("{\"format\":\"DH.CivilRegion3D\",\"version\":1e10,\"units\":{\"length\":\"m\"}}", "판 1")]
        [InlineData("[1,2]", "객체가 아님")]
        [InlineData("{oops", "JSON이 아님")]
        public void Refuses_WrongFiles(string json, string why)
        {
            Region3D.Parse(json, out var err);
            Assert.NotNull(err);
            Assert.Contains(why, err);
        }

        [Fact]
        public void PeekFormat_ReadsOnlyTheHead()
        {
            string dir = Path.Combine(Path.GetTempPath(), "dhcc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string a = Path.Combine(dir, "a.json"), b = Path.Combine(dir, "b.json"), c = Path.Combine(dir, "c.json");
                File.WriteAllText(a, Sample(n: 120), new UTF8Encoding(true));   // BOM 붙은 큰 파일
                File.WriteAllText(b, "{\"format\":\"DH.CivilStructPoints\",\"version\":1}");
                File.WriteAllText(c, "not json");
                Assert.Equal("DH.CivilRegion3D", Region3D.PeekFormat(a));
                Assert.Equal("시험 구조물_Civil기준점.json", Region3D.PeekString(a, "pointsFile"));
                Assert.Equal("DH.CivilStructPoints", Region3D.PeekFormat(b));
                Assert.Equal("", Region3D.PeekFormat(c));
                Assert.Equal("", Region3D.PeekFormat(Path.Combine(dir, "없음.json")));
                var r = Region3D.Read(a, out var err);
                Assert.Null(err);
                Assert.Equal(120 * 120 * 2, r.Triangles);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void PointsFile_Region3dLink_StaysInSameFolder()
        {
            Assert.Equal(Path.Combine(@"C:\a\b", "x_3D영역.json"), CivilPoints.Region3DPath(@"C:\a\b\x_Civil기준점.json", "x_3D영역.json"));
            Assert.Equal(Path.Combine(@"C:\a\b", "evil.json"), CivilPoints.Region3DPath(@"C:\a\b\x.json", @"..\..\evil.json"));
            Assert.Equal("", CivilPoints.Region3DPath(@"C:\a\b\x.json", ""));
        }

        [Fact]
        public void Coarse_OnPlane_IsExactAndNearTarget()
        {
            var r = Region3D.Parse(Sample(n: 60), out var err);
            Assert.Null(err);
            var m = r.Meshes[0];
            var (v, t) = TerrainLod.Coarse(m, 800);
            Assert.InRange(t.Length / 3, 600, 1000);
            for (int i = 0; i < v.Length; i += 3)
            {
                double want = 50 + 0.02 * (v[i] - 200000) + -0.01 * (v[i + 1] - 450000);
                Assert.Equal(want, v[i + 2], 6);
            }
            // 위에서 봐서 반시계(법선 위)
            for (int k = 0; k < t.Length; k += 3)
            {
                int a = t[k] * 3, b = t[k + 1] * 3, c = t[k + 2] * 3;
                double cross = (v[b] - v[a]) * (v[c + 1] - v[a + 1]) - (v[b + 1] - v[a + 1]) * (v[c] - v[a]);
                Assert.True(cross > 0);
            }
            // 범위가 같음
            Assert.Equal(200000, v.Where((_, i) => i % 3 == 0).Min(), 6);
            Assert.Equal(200040, v.Where((_, i) => i % 3 == 0).Max(), 6);
        }

        [Fact]
        public void Coarse_TakesHighestWhereSurfacesOverlap_AndSkipsHoles()
        {
            // 두 겹(아래 50 · 위 55 · 같은 평면 자리) + 오른쪽 반은 비어 있음
            string json = "{\"format\":\"DH.CivilRegion3D\",\"version\":1,\"units\":{\"length\":\"m\"},\"meshes\":[{\"kind\":\"final\",\"verts\":" +
                          "[[0,0,50],[10,0,50],[10,10,50],[0,10,50],[0,0,55],[10,0,55],[10,10,55],[0,10,55],[30,0,40],[30,10,40]]," +
                          "\"tris\":[[0,1,2],[0,2,3],[4,5,6],[4,6,7]]}]}";
            var r = Region3D.Parse(json, out var err);
            Assert.Null(err);
            var (v, t) = TerrainLod.Coarse(r.Meshes[0], 200);
            Assert.NotEmpty(t);
            for (int i = 0; i < v.Length; i += 3) Assert.Equal(55, v[i + 2], 9);
            Assert.True(v.Where((_, i) => i % 3 == 0).Max() <= 10 + 1e-9);   // 빈 오른쪽(10~30)은 격자 점이 없음
        }
    }
}
