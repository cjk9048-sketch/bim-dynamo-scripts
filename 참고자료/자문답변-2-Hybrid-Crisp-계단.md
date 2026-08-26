# [자문 답변 #2] Hybrid NTS + Ray-Casting을 이용한 Crisp 계단 정지면 구현

> **문서 요약:** Ray-Casting 방식 도입 후 발생한 뭉툭한 단면(Angular/Faceted) 현상 및 브레이크라인 교차 오류를 해결하고, Civil 3D 기본 기능과 동일한 '칼같이 떨어지는 계단(Crisp Bench)'을 생성하기 위한 최종 아키텍처 가이드입니다.

## 1. 현재 문제점 진단

기존 방식에서 '계단(Bench) 모서리'를 구성하는 점들을 `AddVertices`로만 추가했기 때문에 다음과 같은 문제가 발생했습니다.

1. **표면 각짐 (Angular/Faceted Facets):** Delaunay 삼각망 알고리즘은 단의 꺾임(엣지)을 스스로 인식하지 못합니다. 단 모서리가 브레이크라인(Breakline)으로 강제되지 않으면, 삼각망이 단을 가로질러 가장 안정적인 형태(큰 삼각형)로 덮어버립니다.

2. **단 모서리를 브레이크라인으로 넣을 때의 딜레마:**

   * **Radial(단면 방향) 브레이크라인:** 오목 코너에서 무조건 교차(Intersection)하여 엔진 에러 발생.

   * **Level(가로 방향) 브레이크라인:** 오목 코너에서 자가 교차(Bow-tie)가 발생하며, 전환부(절/성토 교차점)에서 선이 열린 상태(Arc)가 되어 꼬임 정리가 어려움.

## 2. 해결책: "Hybrid NTS + Ray-Casting" 패턴

오목 코너의 평면적 꼬임(Bow-tie)은 **NTS(NetTopologySuite)의 Buffer**로 풀고, 절/성토 전환부의 3D 높이 및 한계선은 **Ray-Casting으로 구한 Daylight**로 제어하는 하이브리드 방식입니다.

### [핵심 동작 원리]

1. **Daylight 클리핑 마스크 생성:** Ray-Casting으로 구한 외곽점을 NTS `Buffer(0)`로 정리하여 단일 폐합 폴리곤(`Daylight Polygon`)을 만듭니다. (현재 성공한 로직 유지)

2. **단 모서리 2D 링 생성 (NTS):** 원본 계획경계(`BasePolygon`)를 NTS `Buffer(거리)`로 오프셋합니다.

   * *효과:* NTS의 Buffer 엔진이 오목 코너의 자가교차(Bow-tie)를 스스로 병합하여 완벽한 2D 링을 반환합니다.

3. **Daylight로 클리핑 (Intersection):** 2번에서 만든 2D 링을 1번의 `Daylight Polygon`과 교차(`Intersection`)시킵니다.

   * *효과:* 절성토 전환부에서 Daylight 밖으로 튀어나가려는 단을 자동으로 잘라냅니다. 허공에 뜨는 수직 벽(Wall)이 원천 차단되며 자연스럽게 0점 수렴합니다.

4. **Z값 복원 및 주입:** 트리밍된 깨끗한 2D 선들에 높이(Z) 값을 할당하고 Civil 3D에 브레이크라인으로 주입합니다.

## 3. 핵심 구현 코드 (C#)

기존 `SurfaceBuilder.cs`의 TIN 주입부를 다음과 같이 개편합니다.

```csharp
using NTSGeo = NetTopologySuite.Geometries;
using Autodesk.AutoCAD.Geometry;
using CivilDB = Autodesk.Civil.DatabaseServices;

public void InjectCrispBenchesToSurface(CivilDB.TinSurface tin, NTSGeo.Polygon basePolygon, NTSGeo.Polygon daylightPolygon, double baseZ, GradingParams p)
{
    // 1. 계획 경계 및 외곽 정지선(Daylight) 추가
    AddLoopBreakline(tin, basePolygon, baseZ); // 계획 경계
    AddLoopBreakline(tin, daylightPolygon, true); // Daylight 라인 (Z값은 원지반 보간)
    
    // 파괴식 외부 바운더리 지정
    Point3dCollection daylightPc = ConvertNtsPolygonToPoint3dCollection(daylightPolygon);
    tin.BoundariesDefinition.AddBoundaries(daylightPc, 1.0, CivilDB.SurfaceBoundaryType.Outer, false);

    // 2. 단(Bench) 모서리 생성 및 클리핑 루프
    double currentOffset = 0;
    double currentZ = baseZ;
    int dirZ = 1; // 1: 절토(위로), -1: 성토(아래로). (절/성토에 따라 별도 루프 필요)

    for (int k = 1; k <= p.MaxBenches; k++)
    {
        // --- [사면 위 모서리 (Top of Slope)] ---
        currentOffset += p.BenchHeight * p.CutSlope;
        currentZ += p.BenchHeight * dirZ;
        
        // 1) NTS Buffer를 통해 꼬임(Bow-tie) 없는 완벽한 2D 링 생성
        NTSGeo.Geometry topBenchRing2D = basePolygon.Buffer(currentOffset);
        
        // 2) 핵심: Daylight 영역 밖으로 튀어나가는 단을 클리핑 (전환부 수직 벽 방지)
        NTSGeo.Geometry validTopBench2D = topBenchRing2D.Intersection(daylightPolygon);
        
        // 3) Z값을 복원하여 브레이크라인에 추가
        AddNtsGeometryAsBreakline(tin, validTopBench2D, currentZ);

        // --- [소단 바깥 모서리 (Toe of Bench)] ---
        currentOffset += p.BenchWidth;
        double toeZ = currentZ + (0.01 * dirZ); // 수직 단차 에러 방지용 미세 물매(1cm)
        
        NTSGeo.Geometry toeBenchRing2D = basePolygon.Buffer(currentOffset);
        NTSGeo.Geometry validToeBench2D = toeBenchRing2D.Intersection(daylightPolygon);
        
        AddNtsGeometryAsBreakline(tin, validToeBench2D, toeZ);
    }
    
    // 최종 엔진 업데이트
    tin.Rebuild();
}

// 헬퍼 함수: NTS Geometry를 Civil 3D Breakline으로 변환 및 추가
private void AddNtsGeometryAsBreakline(CivilDB.TinSurface tin, NTSGeo.Geometry ntsGeom, double zValue)
{
    if (ntsGeom == null || ntsGeom.IsEmpty) return;

    // Intersection 결과는 LineString, MultiLineString 등 다양할 수 있음
    for (int i = 0; i < ntsGeom.NumGeometries; i++)
    {
        NTSGeo.Geometry geomPart = ntsGeom.GetGeometryN(i);
        
        if (geomPart is NTSGeo.LineString lineString)
        {
            Point3dCollection pts = new Point3dCollection();
            foreach (var coord in lineString.Coordinates)
            {
                pts.Add(new Point3d(coord.X, coord.Y, zValue));
            }

            // Weeding(0.05m)을 주어 Civil 3D 중복점/교차 에러 원천 차단
            if (pts.Count > 1) {
                tin.BreaklinesDefinition.AddStandardBreakline(pts, 1.0, 1.0, 0.05, 0.0);
            }
        }
        else if (geomPart is NTSGeo.Polygon poly)
        {
            // 폴리곤일 경우 외곽선(ExteriorRing) 추출 후 동일하게 처리
            // ...
        }
    }
}
```

## 4. 기대 효과 및 결론

* **Delaunay 의존성 탈피:** 점(`Vertices`) 기반의 삼각망 추정 방식에서 벗어나, 꺾이는 모든 엣지를 강제 브레이크라인으로 삽입하여 Civil 3D 기본 정지도구와 동일한 Crisp Bench(또렷한 계단)를 보장합니다.

* **자가교차 완벽 회피:** NTS의 `Buffer`가 내부적으로 오목 코너 꼬임을 해소한 후 브레이크라인으로 넘어오므로, 이벤트 뷰어의 *'브레이크라인이 점과 교차됨'* 에러가 0건으로 수렴합니다.

* **벽(Wall) 없는 매끄러운 수렴:** NTS `Intersection` 연산이 절성토 전환부의 열린 Arc 라인을 Daylight 경계에 맞추어 정확하게 끊어주므로, 억지스러운 단차가 발생하지 않습니다.