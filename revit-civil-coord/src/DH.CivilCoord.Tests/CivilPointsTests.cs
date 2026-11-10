using DH.CivilCoord.Core;
using Xunit;

namespace DH.CivilCoord.Tests;

/// <summary>Civil 기준점 파일(계획_Civil구조물기준점_내보내기.md 3절 꼴) 읽기 — 가짜 좌표.</summary>
public class CivilPointsTests
{
    // 3절 견본과 같은 꼴(좌표는 가짜) · check 는 x · y 로 셈한 값
    const string Sample = """
    {
      "format": "DH.CivilStructPoints",
      "version": 1,
      "structure": { "name": "시험 구조물", "note": "" },
      "units": { "length": "m", "toMeter": 1.0, "angle": "deg" },
      "axes": "X=동(Easting) · Y=북(Northing) · Z=표고 — 도면 WCS",
      "crs": { "epsg": 5186, "name": "Korea 2000 / Central Belt 2010", "civilCode": "", "source": "drawing" },
      "drawing": { "file": "시험.dwg", "path": "C:\\x\\시험.dwg", "insunits": 6, "insunitsName": "Meters", "unitAssumed": false, "ucsWasWorld": true },
      "app": { "civil": "Civil 3D 2026 (R25.1)", "addin": "DH.Grading v116.7.0" },
      "created": "2026-11-10T15:20:00+09:00",
      "points": [
        { "no": 1, "x": 200123.45, "y": 450678.9, "z": 0.0, "ground": { "surface": "원지반", "elevation": 49.85 }, "final": { "surface": "최종", "elevation": 50.2 } },
        { "no": 2, "x": 200155.36, "y": 450677.84, "z": 0.0, "ground": null, "final": null }
      ],
      "check": { "distance": 31.927600, "bearingDeg": -1.902567 }
    }
    """;

    static string With(string from, string to) { Assert.Contains(from, Sample); return Sample.Replace(from, to); }

    [Fact]
    public void Sample_ReadsEverything()
    {
        var cp = CivilPoints.Parse(Sample, out var err);
        Assert.Null(err);
        Assert.Equal(1, cp.Version);
        Assert.Equal("시험 구조물", cp.Name);
        Assert.Equal("시험.dwg", cp.DrawingFile);
        Assert.Equal(5186, cp.Epsg);
        Assert.Equal(6, cp.Insunits);
        Assert.True(cp.UcsWasWorld);
        Assert.Equal(200123.45, cp.Points[0].X);
        Assert.Equal(450677.84, cp.Points[1].Y);
        Assert.Equal(49.85, cp.Points[0].Ground);
        Assert.Equal("원지반", cp.Points[0].GroundSurface);
        Assert.Null(cp.Points[1].Ground);
        Assert.Equal("«시험 구조물» · 시험.dwg · EPSG 5186", cp.Summary);
        Assert.Equal("지반 49.850 · 계획 50.200 m", CivilPoints.Heights(cp.Points[0]));
        Assert.Equal("", CivilPoints.Heights(cp.Points[1]));
    }

    [Fact]
    public void Points_OrderedByNo()
    {
        string swapped = Sample.Replace("\"no\": 1,", "\"no\": 9,").Replace("\"no\": 2,", "\"no\": 1,").Replace("\"no\": 9,", "\"no\": 2,");
        var cp = CivilPoints.Parse(swapped.Replace("\"check\": { \"distance\": 31.927600, \"bearingDeg\": -1.902567 }", "\"check\": null"), out var err);
        Assert.Null(err);
        Assert.Equal(200155.36, cp.Points[0].X);   // no 1 이 된 둘째 줄
    }

    [Theory]
    [InlineData("\"DH.CivilStructPoints\"", "\"Other\"", "format")]
    [InlineData("\"version\": 1", "\"version\": 2", "판 2")]
    [InlineData("\"length\": \"m\"", "\"length\": \"mm\"", "m가 아님")]
    [InlineData("\"toMeter\": 1.0", "\"toMeter\": 0.001", "toMeter")]
    [InlineData("\"x\": 200123.45", "\"x\": \"abc\"", "숫자가 아님")]
    [InlineData("\"distance\": 31.927600", "\"distance\": 31.9", "거리")]
    [InlineData("\"bearingDeg\": -1.902567", "\"bearingDeg\": 1.902567", "방향")]
    public void Bad_Files_AreRefused(string from, string to, string why)
    {
        CivilPoints.Parse(With(from, to), out var err);
        Assert.NotNull(err);
        Assert.Contains(why, err);
    }

    [Fact]
    public void ThreePoints_AreRefused()
    {
        string three = With("\"ground\": null, \"final\": null }", "\"ground\": null, \"final\": null }, { \"no\": 3, \"x\": 1, \"y\": 2 }");
        CivilPoints.Parse(three, out var err);
        Assert.Contains("3개", err);
    }

    [Fact]
    public void NotJson_IsRefused()
    {
        CivilPoints.Parse("X 200123.45 Y 450678.9", out var err);
        Assert.Contains("JSON", err);
    }

    [Fact]
    public void Minimal_File_IsEnough()
    {
        const string min = """{"format":"DH.CivilStructPoints","version":1,"units":{"length":"m"},"points":[{"no":1,"x":"10.5","y":20},{"no":2,"x":40,"y":20}]}""";
        var cp = CivilPoints.Parse(min, out var err);
        Assert.Null(err);
        Assert.Equal(10.5, cp.Points[0].X);
        Assert.Equal("", cp.Summary);
    }

    [Fact]
    public void State_TakesCivilValues_AndNoticesHandEdits()
    {
        var cp = CivilPoints.Parse(Sample, out _);
        var st = new FitState { R1 = new[] { 0.0, 0.0 }, R2 = new[] { 31.9276, 0.0 } };
        st.ApplyCivil(cp, @"C:\x\a.json");
        Assert.Equal("200,123.45", st.C1X);
        Assert.Equal("450,677.84", st.C2Y);
        Assert.True(st.CivilUntouched);
        var fit = st.Fit()!;
        Assert.Equal(200123.45, fit.C1.X, 9);
        Assert.True(Math.Abs(fit.DistanceError) < 1e-4);
        st.C2Y = "450,677.85";
        Assert.False(st.CivilUntouched);
        var back = FitState.FromJson(st.ToJson())!;
        Assert.Equal(@"C:\x\a.json", back.CivilFile);
        Assert.Equal(st.CivilLoaded, back.CivilLoaded);
    }
}
