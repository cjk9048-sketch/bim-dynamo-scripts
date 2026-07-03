# [자문 요청] Civil3D 2026 .NET API — TinSurface PasteSurface 합성 시 SurfaceException(Failure)

## 환경
- Civil 3D 2026 (R25.1), .NET 8, C# 플러그인 (AeccDbMgd / acdbmgd)
- 목적: 소규모 부지 계단식 절성토 자동화 플러그인 (명령 DHGRADE)

## 전체 파이프라인 (모두 정상 동작 확인됨)
1. **가상 절토면/성토면 생성**: 계획 폴리곤(3D 폴리선 지원)을 NTS Buffer로 동심 오프셋한 계단 링들을
   `TinSurface.Create` + `BreaklinesDefinition.AddStandardBreaklines`(링별)로 TIN 2개 생성.
   (+코너 능선/단차 경계선 등 열린 브레이크라인 추가, `Rebuild()`)
2. **교선 경계 주입**: 각 가상면과 원지반의 교선 영역을 NTS로 계산(삼각형쌍 overlap을 d=0 반평면으로
   정밀 클립한 면 조각들의 UnaryUnion + 계획폴리곤 합집합)한 **폐합 링(정점 200~800개, 1mm 스냅)**을
   `BoundariesDefinition.AddBoundaries(pc, 1.0, SurfaceBoundaryType.Outer, nonDestructive:true)`로 주입 + `Rebuild()`.
   → 두 가상면 모두 경계대로 정밀하게 잘리는 것 실측 검증(경계 안팎 표고 샘플링, 이탈 거의 0).
3. **최종 합성**: 빈 TIN `정지면_DH`에 `PasteSurface`로 원지반 → 가상성토 → 가상절토 순 합성.

## 문제 — 3단계 합성에서만 SurfaceException("Failure")
개별 표면은 완벽한데 **마지막 paste가 항상 깨짐**:

```
[전략 A]  빈면에 원지반 → 성토 → 절토 순 paste (각 paste 직후 CreateSnapshot+Rebuild로 굳힘)
  결과:  원지반:OK  성토:OK  절토:실패[SurfaceException] Failure

[전략 B]  (사용자 수동 실험에서 가끔 성공하던 순서)
  빈면A에 성토→절토 paste = 둘 다 OK  (성토+절토끼리는 합쳐짐!)
  빈면B에 원지반 → A(절성토임시) paste:
  결과:  원지반:OK  절성토임시:실패[SurfaceException] Failure
```

Civil UI에서 수동으로 같은 붙여넣기를 해도 동일하게 빨간 느낌표(작업 유형: 붙여넣기 오류).
수동 실험에서는 순서/조합에 따라 성공·실패가 **비결정적으로** 갈리기도 함.

## 관찰된 패턴
- 실패는 항상 **겹침이 이중으로 쌓이는 마지막 paste**에서 발생.
- 가상성토와 가상절토는 **계획 부지(pad) 영역을 둘 다 완전히 포함**(동일한 pad 링 기하 공유).
- 두 가상면의 Outer 경계 링은 계획 폴리곤 변 구간에서 **좌표까지 완전히 일치**(같은 계획 변 공유).
- 성토↔절토끼리(빈면에)는 합성 OK → "빈면+2겹"은 되고 "원지반+2겹"은 안 됨.

## 핵심 코드 발췌

```csharp
// 합성 (paste별 예외 캡처 + 스냅샷 굳히기)
ObjectId id = TinSurface.Create(db, name);
var final = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
foreach (var (sid, label) in pasteOrder)
{
    try
    {
        final.PasteSurface(sid);
        try { final.CreateSnapshot(); } catch { try { final.RebuildSnapshot(); } catch { } }
        try { final.Rebuild(); } catch { }
    }
    catch (System.Exception ex) { /* label:실패[SurfaceException] Failure */ }
}
```

```csharp
// 경계 주입 (두 가상면 각각)
var pc = new Point3dCollection();            // 폐합 링(중복 닫음점 제거, 1mm 스냅 좌표)
foreach (var pt in ring) pc.Add(new Point3d(pt.X, pt.Y, pt.Z));
tin.BoundariesDefinition.AddBoundaries(pc, 1.0, Autodesk.Civil.SurfaceBoundaryType.Outer, true);
tin.Rebuild();
```

## 이미 시도한 것
| 시도 | 결과 |
|---|---|
| paste마다 CreateSnapshot/RebuildSnapshot + Rebuild (굳히기) | 여전히 마지막 paste 실패 |
| 순서 A(원지반→성토→절토) / B(절성토 선합성→원지반) | 둘 다 마지막에서 실패 |
| Outer 경계 nonDestructive true/false | true=정밀절단 정상, false=톱니(무관) |
| 개별 표면 검증(경계 안팎 표고 샘플링) | 두 가상면 모두 정상 |

## (자문 요청 시점에 적용 중인 추가 시도)
- **절토면 도넛화**: 절토면에 계획폴리곤을 `SurfaceBoundaryType.Hide`(nonDestructive:true)로 추가해
  pad를 뚫고 바깥 계단 띠만 남김 → 성토(pad 포함)와 **영역 겹침 자체를 제거** 후 합성. (결과 대기)

## 질문
1. `PasteSurface`가 **SurfaceException("Failure")** 를 던지는 대표 원인은? (겹침 2중 스택? 경계선 좌표
   완전 일치? 비파괴 Outer 경계를 가진 소스 표면? 스냅샷 보유 소스? 정점 수/공차?)
2. 겹치는 유계(bounded) 표면 여러 장을 안정적으로 합성하는 **권장 순서/방법**이 있는지?
   (paste 전 소스 표면에 해야 할 전처리 — 스냅샷 제거? Simplify? 경계 대신 데이터 클립?)
3. 우리 '도넛(Hide로 pad 제거→겹침 제거)' 접근이 정석인지, 아니면 다른 정석(예: 표면 간 붙여넣기 대신
   포인트/삼각형 추출 후 단일 TIN 재구성, ReplaceSurface, 볼륨면 활용 등)이 있는지?
4. 비파괴(nonDestructive) 경계가 있는 표면을 paste할 때의 알려진 제약/버그가 있는지? (2026 기준)

## 참고 좌표/규모
- 부지 약 40×55m, 계획 폴리곤 8정점(단차 3D 폴리선, Z=101/105), 원지반 TIN 수만 삼각형
- 가상면: 계단 링 20~23개(브레이크라인), 경계 링 정점 200~800개
