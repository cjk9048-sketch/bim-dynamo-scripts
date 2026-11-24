using DH.Grading.Core;
using Xunit;

namespace DH.Grading.Core.Tests;

/// <summary>[JACK 1125 «진입도로 옹벽 변환 부분과 계획부지 옹벽 변환 부분이 닿으면 깨짐»] 부지 바닥에 붙은 도로 끝 곁 옹벽 기록 끝 → 도로 끝(짓기 안에서만).</summary>
public class RoadFloorSnapTests
{
    /// <summary>성토 1:1.5 · 단높이 5 · 소단 1 → 단 거리 8.5 m(절토 1:1 · 5 · 1 = 6 m보다 큼).</summary>
    static RoadGrade.Input Inp(bool start, bool end) => new()
    {
        Cut = new RoadGrade.SlopeRule { Slope = 1.0, BenchH = 5, BenchW = 1 },
        Fill = new RoadGrade.SlopeRule { Slope = 1.5, BenchH = 5, BenchW = 1 },
        FloorStart = start, FloorEnd = end,
    };

    [Fact]
    public void FloorStart_RecordNearStart_SnapsToRoadStart()
    {
        // 대안3: 좌 1단 기록 5.03 ~ 64.05 · 도로 0 ~ 138.6 · 시작 = 부지 바닥
        var recs = new List<RoadGrade.TierWall> { new(5.03, 64.05, -1, 1, 1) };
        var s = RoadRing.FloorSnaps(Inp(true, false), recs, 0, 138.6);
        Assert.Single(s);
        Assert.Equal((0, 0.0, 64.05), s[0]);
    }

    [Fact]
    public void FreeEnds_NothingMoves()
    {
        var recs = new List<RoadGrade.TierWall> { new(5.03, 64.05, -1, 1, 1) };
        Assert.Empty(RoadRing.FloorSnaps(Inp(false, false), recs, 0, 138.6));
    }

    [Fact]
    public void BeyondTierReach_NothingMoves()
    {
        // 1단 거리 8.5 m 밖(9 m) — 일부러 남긴 사면으로 봄 · 2단 기록은 17 m까지 붙음
        var recs = new List<RoadGrade.TierWall> { new(9, 40, -1, 1, 1), new(12, 40, 1, 2, 1) };
        var s = RoadRing.FloorSnaps(Inp(true, false), recs, 0, 100);
        Assert.Single(s);
        Assert.Equal((1, 0.0, 40.0), s[0]);
    }

    [Fact]
    public void OtherRecordInBetween_NothingMoves_EndSnaps()
    {
        // 같은 쪽 다른 기록(0 ~ 2)이 사이에 있으면 안 붙임 · 끝 쪽(부지 바닥)은 붙임
        var recs = new List<RoadGrade.TierWall> { new(0, 2, -1, 2, 1), new(5, 95, -1, 1, 1) };
        var s = RoadRing.FloorSnaps(Inp(true, true), recs, 0, 100);
        Assert.Single(s);
        Assert.Equal((1, 5.0, 100.0), s[0]);
    }
}
