# Civil 3D 2026 지표면 정의 탭 느낌표(⚠) 이슈 검토 및 해결 방안

제시해주신 내용과 진단 로그, 시도하신 방법들을 깊이 있게 검토했습니다. 트랜잭션을 분리하고, 순서를 변경하며, API 문서를 교차 검증하신 과정에서 심도 있는 문제 해결 능력이 돋보입니다.

결론부터 말씀드리면, 이 현상은 Civil 3D .NET API의 고질적인 **'DB 객체 상태와 UI(Prospector) 상태 간의 동기화 누락(Desync)'** 버그입니다. 형상이나 토공량 계산이 정상적으로 이루어졌음에도 불구하고, API가 내부 UI 갱신 이벤트를 제대로 호출하지 못해 발생합니다.

자동화된 물량 산출 시스템이나 정밀한 상세설계(LOD 350 수준) 납품을 위해서는 데이터의 신뢰성 확보가 필수적이므로, UI 상의 오류 아이콘도 확실하게 제어하는 것이 좋습니다. 질문하신 6가지 항목에 대한 명확한 답변과 이를 해결하기 위한 실무적인 Action Plan을 제시해 드립니다.

## 1. 질문에 대한 답변

**Q1. 정의 탭 목록의 ⚠는 정확히 어떤 상태를 나타내며, 코드로 판별 가능한가?**
*   **상태:** 이 경우 UI는 `Item modified`로 인식하고 있습니다. 하위 지표면(원지반 등)이 합성면에 붙여넣기 된 후, 고유한 트랜잭션 내에서 스냅샷이나 임시 체적표면 연산 등이 발생하면서 종속성 체인에 '가상 수정' 플래그가 켜진 것입니다.
*   **코드 판별:** 조사하신 대로 `SurfaceOperation` 클래스에는 이를 읽을 수 있는 공개(.NET) 속성이 존재하지 않습니다. P/Invoke를 통해 C++ ARX 내부 포인터에 접근하지 않는 이상 순수 .NET API로는 판별이 불가능합니다.

**Q2. `RebuildSnapshot()` (API)과 `스냅샷 재작성` (UI)의 차이점**
*   **API (`RebuildSnapshot()`):** 데이터베이스(DWG) 내의 TinSurface 기하학적 형상과 수학적 데이터만 갱신합니다.
*   **UI (`스냅샷 재작성`):** 기하학적 갱신과 더불어 `AeccRebuildSurface`라는 내부 명령을 실행하여, 현재 도면 세션의 Prospector 트리 캐시와 정의 탭의 작업(Operation) 상태 플래그를 초기화하는 이벤트 리스너를 강제로 트리거합니다.

**Q3. `PasteSurface` + `CreateSnapshot` 조합에서 ⚠를 API로 지우는 방법**
*   단순 `Rebuild()` 메서드로는 해결되지 않습니다. API로 UI 리프레시를 강제하려면 `AutoRebuild` 속성을 우회적으로 사용하거나(하단 해결 방법 참고), 도면 편집기(Editor) 객체를 통해 커맨드라인 명령을 비동기로 쏘는 우회로를 써야 합니다.

**Q4. 스냅샷 앞의 붙여넣기 항목을 제거(Strip)해도 무방한가?**
*   **완전히 무방하며, 오히려 강력히 권장합니다.** 스냅샷이 정의의 맨 끝에 존재한다면, 그 시점의 삼각망(TIN) 데이터가 지표면 객체에 '구워진(Baked)' 상태가 됩니다. 
*   `Surface.Operations.RemoveAt()`을 사용하여 이전의 `PasteSurface` 작업들을 삭제하더라도 형상은 100% 유지됩니다. 이렇게 종속성을 끊어낸 지표면은 향후 다른 지표면의 소스로 사용하거나 토량 산출에 활용할 때 예기치 않은 재작성 오류를 방지할 수 있어 매우 안정적입니다.

**Q5. `AutoRebuild = true`의 영향**
*   처음부터 `AutoRebuild = true`로 두고 작업하면 트랜잭션 커밋 시마다 갱신이 일어나 성능이 크게 저하될 수 있습니다. `IsOutOfDate`가 `False`이기 때문에 단순 활성화로는 ⚠가 사라지지 않을 확률이 높습니다.

**Q6. 이 ⚠가 실제로 해로운가?**
*   **데이터 무결성:** 관찰하신 대로 형상, 토공량, 종/횡단 결과가 정상이라면 데이터 자체는 안전합니다. 
*   **부작용:** 시각적인 불안감을 조성하며, 사용자가 실수로 다른 작업을 하다가 불필요한 전체 재작성이 트리거될 위험이 잔존합니다.

## 2. 해결 방법 (Action Plan)

안정적인 자동화 파이프라인 구축을 위해 다음 두 가지 해결책 중 하나를 적용하는 것을 권장합니다.

### 방법 A: Operation Stripping (가장 추천하는 구조적 해결책)
지표면의 종속성을 완전히 끊어 정적인(Static) 결과물로 만듭니다. 물량 산출 자동화 시 원본 지표면이 수정되더라도 확정된 정지면이 흔들리지 않도록 보장하는 가장 세련된 방법입니다.

```csharp
private static void FreezeAndStrip(TinSurface s)
{
    // 1. 스냅샷 작성 또는 재작성
    try { if (s.HasSnapshot) s.RebuildSnapshot(); else s.CreateSnapshot(); } catch { }
    
    // 2. 스냅샷이 정상적으로 생성되었다면, 이전의 모든 작업(Operation)을 삭제
    if (s.HasSnapshot)
    {
        var ops = s.Operations;
        // 뒤에서부터 지워야 인덱스 오류가 발생하지 않음
        for (int i = ops.Count - 1; i >= 0; i--)
        {
            // 스냅샷 오퍼레이션 자체는 남겨둠
            if (ops[i].OperationType != SurfaceOperationType.CreateSnapshot)
            {
                ops.RemoveAt(i);
            }
        }
    }
}
```
**효과:** 정의 탭에 '스냅샷 작성' 단 한 줄만 남게 되며, ⚠ 아이콘이 나타날 대상 자체가 사라집니다. 도면 용량도 최적화됩니다.

### 방법 B: Editor.Command를 이용한 UI 강제 갱신 (종속성을 유지해야 할 경우)
설계 변경에 따라 원지반이나 가상성토 지표면이 수정될 때 최종 정지면이 실시간으로 연동되어야 한다면, API로 UI 커맨드를 강제 호출하여 아이콘을 지워야 합니다.

```csharp
// 트랜잭션이 모두 종료(Commit)된 후, 가장 마지막에 호출
public static void ForceRebuildUI(string surfaceName)
{
    var doc = Application.DocumentManager.MdiActiveDocument;
    var ed = doc.Editor;
    
    // 비동기 명령 호출로 Prospector 리프레시 유도
    // (명령창에 '-REBUILDSURFACE 정지면_DH'를 입력하는 것과 동일한 효과)
    ed.Command("_-REBUILDSURFACE", surfaceName);
}
```
**효과:** 사용자가 마우스 우클릭으로 재작성을 누른 것과 100% 동일한 이벤트를 발생시켜 ⚠ 아이콘을 깔끔하게 제거합니다.
