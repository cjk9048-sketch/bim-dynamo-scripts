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
    [InlineData("\"version\": 1", "\"version\": 1e10", "판 1")]
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
    public void State_FileAndManual_AreKeptApart()
    {
        var cp = CivilPoints.Parse(Sample, out _);
        var st = new FitState { R1 = new[] { 0.0, 0.0 }, R2 = new[] { 31.9276, 0.0 }, C1X = "1", C1Y = "2", C2X = "41", C2Y = "2" };
        st.ApplyCivil(cp, @"C:\x\a.json");
        Assert.Equal("file", st.Mode);
        Assert.Equal("1", st.C1X);   // 직접 입력 칸은 그대로
        Assert.Equal(("200,123.45", "450,678.9", "200,155.36", "450,677.84"), st.FileValues);
        var fit = st.Fit()!;          // 파일 모드 = 파일 값
        Assert.Equal(200123.45, fit.C1.X, 9);
        Assert.True(Math.Abs(fit.DistanceError) < 1e-4);
        st.Mode = "manual";           // 직접 입력 = 칸 값
        Assert.Equal(1, st.Fit()!.C1.X, 9);
        var back = FitState.FromJson(st.ToJson())!;
        Assert.Equal(@"C:\x\a.json", back.CivilFile);
        Assert.Equal(st.CivilLoaded, back.CivilLoaded);
        Assert.Equal("manual", back.Mode);
    }

    [Fact]
    public void Sketch_IsReadAndBadLinesDropped()
    {
        string withSketch = With("\"check\": {", """
            "sketch": { "center": [200140, 450678], "radius": 80, "lines": [
              { "kind": "site", "layer": "부지", "closed": true, "pts": [[200100, 450650], [200180, 450650], [200180, 450710], [200100, 450710]] },
              { "kind": "road", "pts": [[200090, 450640], [200190, 450640]] },
              { "kind": "wall", "pts": [[1, "x"]] },
              { "pts": [[5, 5]] },
              "not a line"
            ] },
            "check": {
            """);
        var cp = CivilPoints.Parse(withSketch, out var err);
        Assert.Null(err);
        Assert.Equal(2, cp.Sketch.Count);
        Assert.Equal(3, cp.SketchDropped);
        Assert.Equal("site", cp.Sketch[0].Kind);
        Assert.True(cp.Sketch[0].Closed);
        Assert.Equal("other", new CivilPoints.SketchLine().Kind);
        Assert.Equal(new P2(200190, 450640), cp.Sketch[1].Pts[1]);
    }

    [Fact]
    public void Sketch_Missing_IsFine_AndFileChangeIsNoticed()
    {
        var cp = CivilPoints.Parse(Sample, out _);
        Assert.Empty(cp.Sketch);
        var st = new FitState();
        st.ApplyCivil(cp, "a.json");
        Assert.False(st.FileDiffers(cp));
        var moved = CivilPoints.Parse(Sample.Replace("\"x\": 200155.36", "\"x\": 200155.46").Replace("\"check\": { \"distance\": 31.927600, \"bearingDeg\": -1.902567 }", "\"check\": null"), out var err);
        Assert.Null(err);
        Assert.True(st.FileDiffers(moved));
    }

    [Fact]
    public void Hull_Square_WithInnerAndCollinearPoints()
    {
        var pts = new[] { new P2(0, 0), new P2(10, 0), new P2(10, 5), new P2(0, 5), new P2(5, 2), new P2(5, 0), new P2(0, 0) };
        var h = Hull.Convex(pts);
        Assert.Equal(4, h.Count);
        Assert.Equal(50, Hull.Area(h), 9);
        Assert.Equal(2, Hull.Convex(new[] { new P2(0, 0), new P2(1, 1) }).Count);
    }

    [Fact]
    public void State_FileMode_WithoutFile_HasNoFit()
    {
        var st = new FitState { Mode = "file", R1 = new[] { 0.0, 0.0 }, R2 = new[] { 30.0, 0.0 }, C1X = "1", C1Y = "2", C2X = "31", C2Y = "2" };
        Assert.Null(st.Fit());
    }

    [Fact]
    public void State_OldRecord_ResolvesMode()
    {
        var typed = new FitState { C1X = "1", C1Y = "2", C2X = "31", C2Y = "2" };   // v1.0 에서 손으로 넣은 기록(Mode 없음)
        typed.ResolveMode();
        Assert.Equal("manual", typed.Mode);
        var empty = new FitState();
        empty.ResolveMode();
        Assert.Equal("file", empty.Mode);
        // v1.0: 파일 값을 칸에 넣은 그대로 → 파일 · 칸을 손으로 고친 기록 → 직접 입력(고친 값이 말없이 바뀌지 않게)
        var asLoaded = new FitState { CivilFile = "a.json", CivilLoaded = "1|2|31|2", C1X = "1", C1Y = "2", C2X = "31", C2Y = "2" };
        asLoaded.ResolveMode();
        Assert.Equal("file", asLoaded.Mode);
        var edited = new FitState { R1 = new[] { 0.0, 0.0 }, R2 = new[] { 30.0, 0.0 }, CivilFile = "a.json", CivilLoaded = "1|2|31|2", C1X = "1", C1Y = "2", C2X = "31.5", C2Y = "2" };
        edited.ResolveMode();
        Assert.Equal("manual", edited.Mode);
        Assert.Equal(31.5, edited.Fit()!.C2.X, 9);
    }
}
