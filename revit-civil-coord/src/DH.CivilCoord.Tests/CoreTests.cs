using DH.CivilCoord.Core;
using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core
{
    // Civil 쪽 IfcMesh.cs 가 쓰는 점 꼴(DH.Grading.Core/Models.cs 와 같은 모양)
    public readonly record struct Point3(double X, double Y, double Z);
}

namespace DH.CivilCoord.Tests
{
    public class FitTests
    {
        [Fact]
        public void TwoPoints_RotateAndMove_ExactWhenDistancesAgree()
        {
            // Revit: 북쪽으로 20 m · Civil: 서쪽으로 20 m → 반시계 90°
            var f = new Fit2D(new P2(10, 0), new P2(10, 20), new P2(1000, 500), new P2(980, 500));
            Assert.Null(f.Problem);
            Assert.Equal(90.0, f.AngleDegrees, 9);
            var a = f.Apply(f.R1); var b = f.Apply(f.R2);
            Assert.Equal(1000, a.X, 9); Assert.Equal(500, a.Y, 9);
            Assert.Equal(980, b.X, 9); Assert.Equal(500, b.Y, 9);
            // R·p + T 꼴도 같은 값
            var t = f.Translation; double c = Math.Cos(f.Angle), s = Math.Sin(f.Angle);
            Assert.Equal(a.X, c * 10 - s * 0 + t.X, 9); Assert.Equal(a.Y, s * 10 + c * 0 + t.Y, 9);
        }

        [Fact]
        public void TwoPoints_DistanceMismatch_SplitsHalfEach()
        {
            // Civil 두 점이 2 cm 더 멂 → 두 점 모두 1 cm 씩 어긋남(한쪽에 몰리지 않음)
            var f = new Fit2D(new P2(0, 0), new P2(30, 0), new P2(500000, 200000), new P2(500030.02, 200000));
            Assert.Equal(0.02, f.DistanceError, 9);
            Assert.Equal(0.01, P2.Dist(f.Apply(f.R1), f.C1), 9);
            Assert.Equal(0.01, P2.Dist(f.Apply(f.R2), f.C2), 9);
        }

        [Fact]
        public void TwoPoints_TooClose_IsProblem()
        {
            Assert.NotNull(new Fit2D(new P2(0, 0), new P2(0.5, 0), new P2(0, 0), new P2(10, 0)).Problem);
            Assert.NotNull(new Fit2D(new P2(0, 0), new P2(10, 0), new P2(5, 5), new P2(5.2, 5)).Problem);
        }

        [Fact]
        public void State_ParsesCommasAndKeepsText()
        {
            var st = new FitState { R1 = new[] { 1.0, 2.0 }, R2 = new[] { 11.0, 2.0 }, C1X = "200,123.450", C1Y = " 450678.9 ", C2X = "200133.450", C2Y = "450,678.900", Dz = "" };
            var f = st.Fit();
            Assert.NotNull(f);
            Assert.Equal(200123.45, f!.C1.X, 9);
            Assert.Equal(450678.9, f.C2.Y, 9);
            Assert.Equal(0, st.DzValue);
            var back = FitState.FromJson(st.ToJson())!;
            Assert.Equal("200,123.450", back.C1X);
            Assert.Equal(11.0, back.R2![0]);
            Assert.Null(new FitState { R1 = new[] { 1.0, 2.0 } }.Fit());
        }
    }

    public class GuidTests
    {
        // buildingSMART 참고 구현과 같은 방식(첫 바이트 → 2자 · 24비트씩 → 4자)으로 따로 계산해 견줌
        static string Reference(Guid g)
        {
            const string D = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
            string h = g.ToString("N");   // 큰 자리부터 32자
            byte[] b = Enumerable.Range(0, 16).Select(i => Convert.ToByte(h.Substring(i * 2, 2), 16)).ToArray();
            var sb = new System.Text.StringBuilder();
            void Put(long v, int n) { var c = new char[n]; for (int i = n - 1; i >= 0; i--) { c[i] = D[(int)(v % 64)]; v /= 64; } sb.Append(c); }
            Put(b[0], 2);
            for (int k = 1; k < 16; k += 3) Put((b[k] << 16) | (b[k + 1] << 8) | b[k + 2], 4);
            return sb.ToString();
        }

        [Fact]
        public void Compress_KnownEnds()
        {
            Assert.Equal("0000000000000000000000", IfcGuid.Compress(Guid.Empty));
            Assert.Equal("3$$$$$$$$$$$$$$$$$$$$$", IfcGuid.Compress(new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        }

        [Fact]
        public void Compress_MatchesReference_AndRoundTrips()
        {
            var rnd = new Random(7);
            for (int i = 0; i < 2000; i++)
            {
                var bytes = new byte[16]; rnd.NextBytes(bytes);
                var g = new Guid(bytes);
                string c = IfcGuid.Compress(g);
                Assert.Equal(Reference(g), c);
                Assert.Equal(g, IfcGuid.Expand(c));
            }
        }

        [Fact]
        public void FromName_IsStable()
        {
            Assert.Equal(IfcGuid.FromName("a/b"), IfcGuid.FromName("a/b"));
            Assert.NotEqual(IfcGuid.FromName("a/b"), IfcGuid.FromName("a/c"));
        }
    }

    public class WriterTests
    {
        static MeshShell Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var s = new MeshShell { Closed = true };
            for (int i = 0; i < 8; i++) s.V.Add(((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] q = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (var f in q) { s.T.Add((f[0], f[1], f[2])); s.T.Add((f[0], f[2], f[3])); }
            return s;
        }

        static (IfcJob Job, Fit2D Fit) Sample(double zOffset)
        {
            // Revit 내부 좌표(m) 근처의 구조물 · Civil 좌표 수십만 m · 돌림 −33.7° 쯤(가짜 값)
            var r1 = new P2(-12.0, -8.0); var r2 = new P2(20.0, 5.0);
            double th = -33.7 * Math.PI / 180, d = P2.Dist(r1, r2), a0 = Math.Atan2(r2.Y - r1.Y, r2.X - r1.X) + th;
            var c1 = new P2(200123.450, 450678.900); var c2 = new P2(c1.X + d * Math.Cos(a0), c1.Y + d * Math.Sin(a0));
            var fit = new Fit2D(r1, r2, c1, c2);
            var t = fit.Translation;
            var job = new IfcJob { FileName = "시험.ifc", ProjectName = "시험 '프로젝트'", Tx = t.X, Ty = t.Y, Tz = zOffset, Angle = fit.Angle };
            job.Storeys.Add(new StoreyInfo { Key = "L1", Name = "EL 50.00", Elevation = 50.0 });
            var wall = new MeshPart { Entity = "IFCWALL", Name = "기본 벽:콘크리트 300:1234", ObjectType = "기본 벽:콘크리트 300", Tag = "1234", StoreyKey = "L1", Guid = IfcGuid.Compress(Guid.NewGuid()) };
            wall.Shells.Add(Box(r1.X, r1.Y, 50.0, r2.X, r1.Y + 0.3, 58.0));   // 점 1 이 꼭짓점
            wall.Props.Add(("Revit ID", "1234")); wall.Props.Add(("범주", "벽"));
            var slab = new MeshPart { Entity = "IFCSLAB", Predefined = ".BASESLAB.", Name = "바닥", Tag = "77" };
            slab.Shells.Add(Box(r1.X, r1.Y, 49.4, r2.X, r2.Y, 50.0));   // 점 2 도 꼭짓점
            var open = new MeshPart { Entity = "IFCBUILDINGELEMENTPROXY", Name = "열린 면", Tag = "9" };
            var os = new MeshShell { Closed = false };
            os.V.Add((0, 0, 75)); os.V.Add((1, 0, 75)); os.V.Add((0, 1, 75)); os.T.Add((0, 1, 2)); os.T.Add((0, 0, 1));   // 둘째는 퇴화(빠져야 함)
            open.Shells.Add(os);
            var empty = new MeshPart { Entity = "IFCCOLUMN", Name = "형상 없음", Tag = "5" };
            job.Parts.AddRange(new[] { wall, slab, open, empty });
            return (job, fit);
        }

        static string Text(IfcJob job) { var sw = new StringWriter(); IfcWriter.Write(job, sw); return sw.ToString(); }

        [Fact]
        public void CivilReader_PlacesEveryVertexAtCivilCoordinates()
        {
            var (job, fit) = Sample(0.25);
            string text = Text(job);
            var r = IfcMesh.Parse(text);
            Assert.Equal("IFC4", r.Schema);
            Assert.Equal(1.0, r.UnitM);
            Assert.False(r.MapConversion);
            Assert.Empty(r.Skipped);
            Assert.Equal(3, r.Parts.Count);   // 형상 없는 기둥은 안 씀
            Assert.Equal(3, IfcWriter.WrittenParts(job));
            foreach (var (src, got) in job.Parts.Take(3).Zip(r.Parts))
            {
                var want = src.Shells.SelectMany(s => s.V).Select(job.ToWorld).ToList();
                foreach (var v in got.V)
                    Assert.Contains(want, w => Math.Abs(w.X - v.X) < 1e-6 && Math.Abs(w.Y - v.Y) < 1e-6 && Math.Abs(w.Z - v.Z) < 1e-6);
                Assert.Equal(src.Entity, got.Type);
                Assert.Equal(src.Name, got.Name);
            }
            // 찍은 두 점이 Civil 이 넣은 두 좌표로(높이 = 레벨 높이 + 0.25)
            var all = r.Parts.SelectMany(p => p.V).ToList();
            Assert.Contains(all, v => Math.Abs(v.X - fit.C1.X) < 1e-6 && Math.Abs(v.Y - fit.C1.Y) < 1e-6 && Math.Abs(v.Z - 50.25) < 1e-6);
            Assert.Contains(all, v => Math.Abs(v.X - fit.C2.X) < 1e-6 && Math.Abs(v.Y - fit.C2.Y) < 1e-6 && Math.Abs(v.Z - 49.65) < 1e-6);
            // 퇴화 세모는 빠짐 · 상자는 세모 12
            Assert.Equal(12, r.Parts[0].T.Count);
            Assert.Single(r.Parts[2].T);
        }

        [Fact]
        public void File_HasEndMarker_AndRequiredShape()
        {
            var (job, _) = Sample(0);
            string text = Text(job);
            Assert.StartsWith("ISO-10303-21;", text);
            Assert.Contains("FILE_SCHEMA(('IFC4'));", text);
            Assert.EndsWith("END-ISO-10303-21;\r\n", text);
            Assert.Contains("IFCSLAB(", text);
            Assert.Contains(".BASESLAB.", text);
            Assert.DoesNotContain("IFCMAPCONVERSION", text);
            Assert.DoesNotContain("IFCCOLUMN(", text);
            // 한글은 \X2\ 로 · ASCII 밖 글자가 날로 남지 않음
            Assert.All(text, ch => Assert.True(ch < 0x80));
            Assert.Contains("'\\X2\\C2DCD5D8\\X0\\ ''\\X2\\D504B85CC81DD2B8\\X0\\'''", text);
        }

        [Fact]
        public void Numbers_AreStepReals()
        {
            Assert.Equal("12.0", IfcWriter.F(12));
            Assert.Equal("0.", IfcWriter.F(-0.0));
            Assert.Equal("0.", IfcWriter.F(1e-13));
            Assert.Equal("-3.5", IfcWriter.F(-3.5));
            Assert.Equal("200123.4534567891", IfcWriter.F(200123.4534567891));
            Assert.Equal("0.", IfcWriter.F(double.NaN));
            Assert.Equal("2.E-07", IfcWriter.F(2e-7));
            Assert.Equal("-1.5E-06", IfcWriter.F(-1.5e-6));
        }

        [Fact]
        public void CivilReader_ReadsExponentReals()
        {
            // 0 가까운 꼭짓점(«2.E-07») 도 Civil 이 같은 값으로 읽는지
            var job = new IfcJob { FileName = "e.ifc", ProjectName = "e" };
            var p = new MeshPart { Entity = "IFCWALL", Name = "w", Tag = "1" };
            var s = new MeshShell();
            s.V.Add((2e-7, -1.5e-6, 0)); s.V.Add((1, 0, 0)); s.V.Add((0, 1, 0)); s.T.Add((0, 1, 2));
            p.Shells.Add(s); job.Parts.Add(p);
            var sw = new StringWriter(); IfcWriter.Write(job, sw);
            var r = IfcMesh.Parse(sw.ToString());
            Assert.Contains(r.Parts[0].V, v => Math.Abs(v.X - 2e-7) < 1e-12 && Math.Abs(v.Y + 1.5e-6) < 1e-12);
        }

        [Fact]
        public void Strings_Escape()
        {
            Assert.Equal("'a''b'", IfcWriter.S("a'b"));
            Assert.Equal("'\\X2\\BCBD\\X0\\1'", IfcWriter.S("벽1"));
            Assert.Equal("'a\\X2\\005C\\X0\\b'", IfcWriter.S("a\\b"));
            Assert.Equal("'a b'", IfcWriter.S("a\nb"));
        }

        [Fact]
        public void Storeys_OnlyThoseUsed_AndChainIntoSite()
        {
            var (job, _) = Sample(0);
            job.Storeys.Add(new StoreyInfo { Key = "L9", Name = "빈 층", Elevation = 99 });
            string text = Text(job);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(text, "IFCBUILDINGSTOREY\\(").Count);
        }
    }
}
