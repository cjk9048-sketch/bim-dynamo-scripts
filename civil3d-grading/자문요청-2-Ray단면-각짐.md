# [자문 요청 #2] Ray-casting 단면 방식 정지면 — 계단이 '각지게'·표면 깨짐

> 이전 자문(Ray-casting 단면 + NTS Buffer(0))을 적용한 뒤의 후속 문제. 자족 문서.

## 1. 목표/환경 (요약)
- Civil3D 2026 / .NET 8 플러그인. 닫힌 **계획경계 폴리곤(설계고)** + **원지반 TinSurface** 선택 → 경계 바깥으로 **계단식 비탈(단높이 5m + 소단 1m, 절토 1:1·성토 1:1.5)** 자동 생성 → 원지반과 만나는 **daylight**까지 닫아 **TinSurface 1개** 생성.
- **혼합 절성토**(한 부지에 절토·성토 공존), **오목 L자 경계**가 난점.
- 격자(grid) 방식 영구 배제. Civil3D Grading API 막힘 → TinSurface 직접 생성. **NTS(NetTopologySuite)** 사용.
- 입력 수단: `tin.AddVertices(점)`, `tin.BreaklinesDefinition.AddStandardBreaklines(점목록)`, `tin.BoundariesDefinition.AddBoundaries(점목록, Outer/Hide, 파괴식)`.

## 2. 적용한 아키텍처 (이전 자문 채택 = Ray-casting)
- 경계를 1m로 쪼개 각 점에서 **바깥 법선으로 단면(profile)**을 쌓음: 사면(단높이만큼↑/↓, 수평=단높이×구배) + 소단(수평 1m, 미세물매 0.01m). **정지면이 원지반과 만나면 종료**(daylight). 영점(계획고≈원지반)에선 단 0 → daylight=경계(전환부 자연수렴, 벽 없음).
- 절/성은 점별 판정(계획면고 vs 원지반고).
- **TIN 주입**: 모든 단면 점 = `AddVertices`(교차 위험 0). **daylight 외곽선** = NTS `Buffer(0)`로 꼬임 정리 후 `AddStandardBreaklines` + `AddBoundaries(Outer, 파괴식)`. 계획경계 = 브레이크라인.

## 3. 현재 결과 (스크린샷 판독)
- ✅ **daylight 외곽선**: 깔끔한 단일 외곽 1선, 지형 따라 잘 나옴. 전환부 벽 없음(자연수렴 성공).
- ❌ **표면이 '각짐(angular)'**: 계단이 또렷한 단이 아니라 **큰 삼각형 facet**으로 가로질러짐. → 원인: 단면 점을 **정점(vertex)으로만** 넣어 **단 모서리 브레이크라인이 없어서** Delaunay가 계단을 안 살림.
- ❌ **표면 일부 찢김** + 이벤트 뷰어 오류 **"브레이크라인이 점과 교차됨 → 추가 안 됨" 15개**: daylight 브레이크라인 점이 정점과 겹쳐 거부됨. (→ daylight 점을 정점에서 제외하는 수정 적용함)

## 4. 핵심 질문 (각지지 않게 = crisp 계단)
**Ray-casting 단면 점들로 만든 계단식 TIN을, 오목 L자 + 혼합 절성토에서 "또렷한 단(crisp bench)"으로 만들려면 단 모서리를 브레이크라인으로 넣어야 하는데, 브레이크라인 교차 없이 어떻게?**

세부:
1. **단 모서리 브레이크라인을 "같은 레벨 선(level line)"으로 연결**할 때, 오목 코너에서 선이 자가교차(bow-tie)함. NTS `Buffer(0)`는 **닫힌 폴리곤**에만 쓰는데, 혼합 절성토에선 같은 레벨 선이 **열린 arc(전환부에서 끊김)**라 Buffer(0)를 못 씀. **열린 arc의 자가교차/꼬임은 어떻게 정리**하나? (NTS의 어떤 연산? `UnaryUnionOp`? `Densifier`? 아니면 다른 방식?)
2. **radial(단면별) 선을 브레이크라인**으로 넣는 건 어떤가? (각 단면 base→daylight를 한 브레이크라인). 오목 코너에서 인접 radial이 교차하는데 회피법은?
3. 아니면 **Civil3D AddVertices만으로 crisp 계단**을 얻는 방법(예: 단 모서리에 점을 아주 촘촘히, 또는 소단 안/바깥 모서리에 점 2줄)이 있나? 브레이크라인 없이 Delaunay가 계단을 살리게 하는 정점 배치 전략은?
4. **constrained Delaunay**를 NTS(`ConformingDelaunayTriangulationBuilder`)로 직접 만들어 삼각형을 Civil3D에 주입하는 게 현실적인가? (Civil3D TinSurface에 삼각형 직접 주입 API가 마땅찮음 — 점/브레이크라인/경계만 받음)
5. 단(bench) 한 개를 TIN으로 표현할 때 **사면 위 모서리 + 소단 바깥 모서리(레벨당 선 2개)**가 정석인가? 더 안정적인 표현은?

## 5. 현재 핵심 코드

### (A) Ray-casting 단면 생성 — `NtsGrading.cs` (Core)
```csharp
// 한 경계점에서 바깥 법선으로 단면을 쌓되 원지반과 만나면 종료. 영점이면 베이스만(=daylight).
private static List<Point3> CalculateProfile(double bx, double by, double nx, double ny,
    Plane plane, IGroundSurface ground, GradingParams p)
{
    double baseZ = plane.At(bx, by);
    var profile = new List<Point3> { new(bx, by, baseZ) };
    if (!ground.TryGetElevation(bx, by, out double g0)) return profile;
    if (Math.Abs(baseZ - g0) < 0.01) return profile; // 영점(전환부) → 단 없음 = daylight

    bool isCut = baseZ < g0;                 // 계획고<원지반 → 절토(위로)
    int dirZ = isCut ? 1 : -1;
    double slope = Math.Max(isCut ? p.CutSlope : p.FillSlope, p.MinSlope);
    double slopeRun = Math.Max(p.BenchHeight * slope, p.MinFaceRun);

    double dist = 0, curZ = baseZ;
    for (int k = 1; k <= p.MaxBenches; k++)
    {
        // 사면: 단높이만큼 상승/하강, 수평 slopeRun
        double sd = dist + slopeRun, nextZ = curZ + p.BenchHeight * dirZ;
        double sx = bx + nx * sd, sy = by + ny * sd;
        if (!ground.TryGetElevation(sx, sy, out double gs)) { /* 데이터끝 → daylight */ return profile; }
        if ((isCut && nextZ >= gs) || (!isCut && nextZ <= gs)) { profile.Add(InterpCross(...)); return profile; }
        dist = sd; curZ = nextZ; profile.Add(new Point3(sx, sy, curZ)); // 사면 위 모서리

        // 소단: 수평 benchWidth, 미세물매(수직단차 방지)
        double bd = dist + p.BenchWidth, benchZ = curZ + 0.01 * dirZ;
        double cx = bx + nx * bd, cy = by + ny * bd;
        if (!ground.TryGetElevation(cx, cy, out double gc)) { profile.Add(new Point3(cx, cy, benchZ)); return profile; }
        if ((isCut && benchZ >= gc) || (!isCut && benchZ <= gc)) { profile.Add(InterpCross(...)); return profile; }
        dist = bd; curZ = benchZ; profile.Add(new Point3(cx, cy, curZ)); // 소단 바깥 모서리
    }
    return profile;
}
// BuildRaycast: 경계 1m 샘플마다 CalculateProfile → 중간 단모서리 점=Vertices, 마지막=daylight.
// daylight XY를 gf.CreatePolygon(...).Buffer(0) → 최대폴리곤 외곽 → Z=원지반 복원 → weed(0.05m).
```

### (B) TIN 주입 — `SurfaceBuilder.cs` (Civil)
```csharp
// 단면 점 = 정점(교차 불가), 경계+daylight = 브레이크라인, daylight = Outer Boundary(파괴식)
tin.AddVertices(vertices);                       // 0.01m 격자 중복제거
tin.Rebuild();
AddLoopBreakline(tin, boundaryRing);             // 계획 경계
AddLoopBreakline(tin, daylight);                 // ← 여기서 "점과 교차" 15개 오류 (daylight가 정점과 겹침)
tin.Rebuild();
tin.BoundariesDefinition.AddBoundaries(daylightPc, 1.0, Outer, false); // 파괴식 트림
tin.Rebuild();
```

## 6. 요청
- 위 4번 질문(특히 **열린 arc 레벨선 꼬임 정리** + **crisp 계단 브레이크라인을 교차 없이**)에 대한 구체적 방법/코드.
- 가능하면 Civil3D TinSurface에 **constrained Delaunay 삼각형을 직접 주입**하는 현실적 방법이 있는지.
- 전체 접근(Ray-casting 단면 + NTS)에서 crisp 계단을 얻는 실무 표준 패턴.
