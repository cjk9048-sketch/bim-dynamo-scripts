using NetTopologySuite.Geometries;

namespace DH.Grading.Core;

/// <summary>
/// 공유 NTS 방어 헬퍼(ralplan C5) — 링→폴리곤 변환 시 항상 같은 방어를 적용:
/// PrecisionModel 1mm 스냅 + 자기접촉/무효 링 Buffer(0) 정규화 + 최대 폴리곤 선택.
/// GradingGeometry·SlopeHatchGenerator의 기존 방어(GradingGeometry.ToPolygon 등)와 같은 규약.
/// 신규 코드(GradingPolygons, 번들 소비자)는 반드시 이걸 쓴다.
/// </summary>
public static class NtsSupport
{
    /// <summary>1mm 스냅 팩토리 — 소수점 미세 단차 위상오류 차단(설계도 방어로직 1).</summary>
    public static GeometryFactory Factory() => new(new PrecisionModel(1000.0));

    /// <summary>링(닫힘 여부 무관)을 유효한 폴리곤으로 — 무효면 Buffer(0), 다중이면 최대 폴리곤.
    /// 실패(정점 부족·완전 퇴화) 시 null.</summary>
    public static Polygon? ToCleanPolygon(IReadOnlyList<Point3> ring, GeometryFactory? gf = null)
    {
        if (ring == null || ring.Count < 3) return null;
        gf ??= Factory();
        var coords = new List<Coordinate>(ring.Count + 1);
        foreach (var p in ring)
        {
            var c = new Coordinate(p.X, p.Y);
            if (coords.Count == 0 || coords[^1].Distance(c) > 1e-9) coords.Add(c);
        }
        if (coords.Count < 3) return null;
        if (coords[0].Distance(coords[^1]) > 1e-9) coords.Add(coords[0].Copy());
        else coords[^1] = coords[0].Copy();
        if (coords.Count < 4) return null;
        Geometry g;
        try { g = gf.CreatePolygon(coords.ToArray()); }
        catch { return null; }
        if (!g.IsValid) g = g.Buffer(0);
        return LargestPolygon(g);
    }

    /// <summary>링을 유효 지오메트리로 — 무효면 Buffer(0), **조각 전부 유지**(멀티폴리곤 가능). 실패 시 null.
    /// [다중 구역 0804 — JACK 스샷] 교선(daylight) 링은 자기접촉(핀치)이 흔한데(경계 주입 IllegalBoundary가 그 증거),
    /// ToCleanPolygon처럼 최대 조각만 남기면 나머지 로브가 마스크/차집합에서 통째로 빠진다 —
    /// 앞 구역의 패널·사면 띠가 뒤 구역 사면 위에 살아남던 원인. 발자국·구멍·도넛용은 반드시 이걸 쓴다.</summary>
    public static Geometry? ToCleanGeometry(IReadOnlyList<Point3> ring, GeometryFactory? gf = null)
    {
        if (ring == null || ring.Count < 3) return null;
        gf ??= Factory();
        var coords = new List<Coordinate>(ring.Count + 1);
        foreach (var p in ring)
        {
            var c = new Coordinate(p.X, p.Y);
            if (coords.Count == 0 || coords[^1].Distance(c) > 1e-9) coords.Add(c);
        }
        if (coords.Count < 3) return null;
        if (coords[0].Distance(coords[^1]) > 1e-9) coords.Add(coords[0].Copy());
        else coords[^1] = coords[0].Copy();
        if (coords.Count < 4) return null;
        Geometry g;
        try { g = gf.CreatePolygon(coords.ToArray()); }
        catch { return null; }
        if (!g.IsValid)
        {
            // ※ Buffer(0)은 안 된다 — 나비넥타이(핀치)에서 감김 방향이 반대인 로브를 **소거**한다
            //   (S19 재현: 두 로브 50㎡ → 25㎡). 링 선을 노딩(자기교차점을 정점으로)한 뒤 폴리곤화해
            //   둘러싸인 셀을 전부 복원하고 합집합으로 정리한다 — '링이 감싼 모든 영역'이 발자국의 의미.
            try
            {
                var noded = gf.CreateLineString(coords.ToArray()).Union();   // 단항 합집합 = 자기교차 노딩
                var pz = new NetTopologySuite.Operation.Polygonize.Polygonizer();
                pz.Add(noded);
                var cells = new List<Geometry>();
                foreach (var c in pz.GetPolygons())
                    if (c is Polygon p && !p.IsEmpty) cells.Add(p);
                if (cells.Count == 0) return null;
                g = cells.Count == 1 ? cells[0]
                    : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(cells);
            }
            catch
            {
                try { g = g.Buffer(0); } catch { return null; }   // 최후 폴백(종전 동작)
            }
        }
        return g == null || g.IsEmpty || g is not IPolygonal ? null : g;
    }

    /// <summary>(멀티)지오메트리에서 면적 최대 폴리곤 — 없으면 null.</summary>
    public static Polygon? LargestPolygon(Geometry g)
    {
        Polygon? best = null; double bestA = -1;
        for (int i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon pg && !pg.IsEmpty && pg.Area > bestA) { bestA = pg.Area; best = pg; }
        return best;
    }
}

/// <summary>
/// 표고 없는 스텁 지반(ralplan 영속 설계) — GradingGeometry.Build는 링 생성에 ground를 쓰지 않으므로
/// (null 가드 유일), 번들에서 링을 결정적으로 복원할 때 주입한다. 클립 모드 SlopeHatchGenerator도
/// ground 미사용. 레거시(부호 판정) 경로에는 절대 쓰지 말 것 — 항상 false를 반환한다.
/// </summary>
public sealed class NullGround : IGroundSurface
{
    public bool TryGetElevation(double x, double y, out double z) { z = 0; return false; }
}
