# Civil 3D 2026 지표면 Definition 탭 느낌표(⚠) — 자문 답변 및 해결방안

## 1. 결론

이번 증상은 단순히 `Surface.IsOutOfDate`가 `true`로 남아 있는 문제가 아니라, **Surface Definition의 개별 operation 상태와 Snapshot 상태가 API 경로에서 완전히 정상화되지 않는 문제**로 보는 것이 가장 타당하다.

문서의 실측 결과는 다음과 같다.

- `Surface.IsOutOfDate == false`
- `Surface.IsSnapshotOutOfDate == false`
- `HasSnapshot == true`
- 실제 TIN 형상은 정상
- `Rebuild()`만 수행하면 Definition 탭의 ⚠가 그대로 유지됨
- 저장 후 재오픈해도 ⚠가 유지됨
- Prospector의 **스냅샷 재작성(Rebuild Snapshot)**을 실행하면 ⚠가 모두 사라짐

따라서 `Rebuild()` 호출 횟수를 늘리는 접근보다는 **Snapshot lifecycle 자체를 다시 만드는 방식**을 우선 검증하는 것이 좋다.

권장 1차 해결책은 다음 순서다.

```text
Paste 1
  ↓
Paste 2
  ↓
Paste 3
  ↓
기존 Snapshot 제거
  ↓
전체 Definition 재작성
  ↓
새 Snapshot 생성
  ↓
Commit
```

즉 현재의 `RebuildSnapshot()` 중심 처리보다 다음 순서를 우선 시험한다.

```csharp
if (s.HasSnapshot)
    s.RemoveSnapshot();

s.Rebuild();

s.CreateSnapshot();
```

첫 번째 테스트에서는 마지막 `Rebuild()`를 추가하지 않고 결과를 확인하는 것을 권장한다.

---

## 2. ⚠의 의미

Autodesk 문서상 Definition 탭의 경고 표시는 다음 세 종류로 설명된다.

| 표시 | 의미 |
|---|---|
| `Item modified` | 항목이 추가된 후 수정됨 |
| `Item not found` | 대화상자를 열 때 항목을 찾지 못함 |
| `Item not OK` | 표면 변경 때문에 항목 추가가 성공하지 못함 |

현재 증상만 놓고 보면 **`Item modified` 계열 상태일 가능성이 가장 높다.**

그 이유는 다음과 같다.

- Paste operation들이 모두 존재함
- Snapshot도 존재함
- 최종 TIN geometry는 정상임
- 전체 Surface의 `IsOutOfDate`는 `false`
- `Rebuild()`는 아무 변화가 없음
- UI의 `Rebuild Snapshot`만 수행하면 일괄적으로 정상화됨

다만 현재 공개 API만으로는 개별 `SurfaceOperation`이 `Item modified / Item not found / Item not OK` 중 어느 상태인지 직접 읽는 방법은 확인되지 않는다.

---

## 3. `IsOutOfDate`와 Definition operation의 ⚠는 같은 상태가 아니다

현재 다음과 같은 상태가 충분히 가능하다.

```text
Surface.IsOutOfDate         = false
Surface.IsSnapshotOutOfDate = false
HasSnapshot                = true

Paste 1                     = ⚠
Paste 2                     = ⚠
Paste 3                     = ⚠
Snapshot                    = ⚠
```

즉,

> `Rebuild()`가 성공했다 = Definition의 모든 operation 경고 상태가 정상화되었다

라고 볼 수 없다.

`IsOutOfDate`는 Surface 전체의 재작성 필요 여부를 나타내는 개념이고, Definition 목록의 operation별 상태 표시는 별도의 내부 상태일 수 있다.

---

## 4. `Rebuild()`와 `RebuildSnapshot()`의 차이

Snapshot이 존재하면 일반적인 `Rebuild()`는 기존 Snapshot을 시작점으로 사용한다.

현재 Definition이:

```text
Paste Surface1
Paste Surface2
Paste Surface3
Snapshot
```

이라면 일반적인 `Rebuild()`는 사실상 다음과 같은 구조가 된다.

```text
Snapshot
  ↓
그 이후 operation
```

반면 `RebuildSnapshot()`은 Snapshot보다 앞에 있는 Definition operation을 다시 처리하여 Snapshot을 새로 만드는 작업이다.

따라서 다음 관찰은 Civil 3D Snapshot 모델과 논리적으로 일치한다.

```text
Rebuild()
    → ⚠ 유지

RebuildSnapshot()
    → ⚠ 제거
```

문제는 API에서 `RebuildSnapshot()`을 호출했는데도 UI 결과와 동일하지 않다는 점이다.

이 부분은 다음 세 가지 가능성을 열어두는 것이 좋다.

1. Prospector의 `Rebuild Snapshot`이 공개 API 호출 외에 내부 상태 갱신을 추가로 수행한다.
2. 기존 Snapshot을 직접 재작성하는 것보다 `RemoveSnapshot → Rebuild → CreateSnapshot`이 더 완전한 초기화 경로다.
3. `PasteSurface + Snapshot` 조합에서 Civil 3D 2026의 내부 operation 상태 bookkeeping 문제가 발생한다.

---

## 5. 가장 먼저 적용할 코드 수정

현재:

```csharp
private static void Freeze(TinSurface s)
{
    try { s.Rebuild(); } catch { }
    try
    {
        if (s.HasSnapshot)
            s.RebuildSnapshot();
        else
            s.CreateSnapshot();
    }
    catch { }
}
```

보다 다음 방식을 우선 검증한다.

```csharp
private static void Freeze(Surface s, StringBuilder log)
{
    try
    {
        if (s.HasSnapshot)
            s.RemoveSnapshot();

        s.Rebuild();

        s.CreateSnapshot();

        log.AppendLine(
            $"{s.Name}: snapshot recreated");
    }
    catch (Exception ex)
    {
        log.AppendLine(
            $"{s.Name}: Freeze FAILED: {ex}");
        throw;
    }
}
```

Composite 전체도 다음처럼 단순화하는 것이 좋다.

```csharp
foreach (var (sid, label) in pasteOrder)
{
    if (sid.IsNull)
        continue;

    final.PasteSurface(sid);
}

Freeze(final, log);
```

즉 다음 순서를 유지한다.

```text
Create
↓
Paste 1
↓
Paste 2
↓
Paste 3
↓
RemoveSnapshot
↓
Rebuild
↓
CreateSnapshot
↓
Commit
```

---

## 6. 왜 `RemoveSnapshot()`이 중요한가?

현재 코드는 이미 Snapshot이 존재하면 바로:

```csharp
s.RebuildSnapshot();
```

을 호출한다.

하지만 이번 문제의 핵심이 **기존 Snapshot과 Definition operation의 내부 상태가 서로 어긋난 상태**라면, 기존 Snapshot을 다시 계산하는 것보다 Snapshot 자체를 제거하고 완전히 새로 만드는 것이 더 강한 초기화가 된다.

따라서 다음 두 방식을 명확히 분리해서 시험해야 한다.

### A. 현재 방식

```text
existing snapshot
    ↓
RebuildSnapshot()
```

### B. 권장 실험

```text
existing snapshot
    ↓
RemoveSnapshot()
    ↓
Rebuild()
    ↓
CreateSnapshot()
```

B가 UI의 `Rebuild Snapshot`과 동일한 상태를 만드는지 확인하는 것이 핵심이다.

---

## 7. `AutoRebuild = true`는 근본 해결책이 아니다

현재 측정값:

```text
AutoRebuild  = false
IsOutOfDate  = false
```

이므로 `AutoRebuild = true`로 바꾸는 것만으로 현재의 Definition ⚠를 제거할 근거는 없다.

`AutoRebuild`은 일반적으로 Surface가 `OutOfDate` 상태일 때 자동 재작성 여부에 관련된다.

따라서 이번 문제에서는 `AutoRebuild`을 원인 해결용으로 사용하기보다 재현 실험용으로만 취급하는 것이 좋다.

---

## 8. Paste Operation을 삭제하는 것은 권장하지 않는다

`SurfaceOperationCollection.RemoveAt()` 등의 API로 Paste operation을 제거하는 것은 기술적으로 가능하지만, 현재 단계에서는 권장하지 않는다.

현재:

```text
Paste Surface1
Paste Surface2
Paste Surface3
Snapshot
```

에서 Paste operation을 모두 삭제해 Snapshot만 남기면 현재 형상은 유지될 가능성이 있지만, 다음과 같은 문제가 생길 수 있다.

- Definition의 재현성이 떨어짐
- 원본 Surface가 변경되어도 재계산하기 어려움
- Snapshot만 남은 Surface를 다시 source로 사용할 때 동작 차이가 생길 수 있음
- 향후 Snapshot 재생성 시 원래 Paste history가 필요할 수 있음

따라서 **현재는 Paste operation을 유지하고 Snapshot lifecycle 문제를 먼저 해결하는 것이 안전하다.**

---

## 9. `AutoRebuild`, `RemoveAt`보다 더 중요한 것: Source Surface 안정화

Composite 전에 source surface들이 완전히 안정화되어 있는지 확인하는 것이 좋다.

Composite 직전에 다음 값을 로그로 남긴다.

```csharp
var src = (Surface)tr.GetObject(sid, OpenMode.ForRead);

log += $"{src.Name}: " +
       $"OutOfDate={src.IsOutOfDate}, " +
       $"Snapshot={src.HasSnapshot}, " +
       $"SnapshotOutOfDate={src.IsSnapshotOutOfDate}";
```

가능하면 모든 source가:

```text
IsOutOfDate = false
IsSnapshotOutOfDate = false
HasSnapshot = true
```

인지 확인한다.

Source가 불안정한 상태에서 `PasteSurface()`를 수행하면 Composite operation의 상태에도 영향을 줄 가능성이 있다.

---

## 10. Visibility 처리 코드는 현재 방식이 타당하다

다음과 같이 실제 값이 변경될 때만 `Visible`을 쓰는 방식은 유지하는 것이 좋다.

```csharp
if (eRead.Visible == keep)
    continue;

var e = (Entity)tr.GetObject(sid, OpenMode.ForWrite);
e.Visible = keep;
```

visibility 변경이 Surface stale 상태에 영향을 줄 수 있다는 기존 조사 결과가 있으므로, 불필요한 property write를 줄이는 방향은 타당하다.

다만 이 수정으로도 ⚠가 유지된다는 실험 결과가 있으므로 현재 문제의 주원인으로 볼 필요는 낮다.

---

## 11. 임시 `TinVolumeSurface`는 최소 재현 테스트에서 제외

다음 작업은 일단 최소 재현에서는 제외하는 것을 권장한다.

```csharp
TinVolumeSurface.Create(
    db,
    "_DH토량임시",
    원지반,
    정지면_DH);
```

목적은 원인을 하나씩 제거하는 것이다.

### 최소 재현 1

```text
Paste 1
Paste 2
Paste 3
Snapshot
```

여기서 바로 ⚠가 발생하면 `TinVolumeSurface`는 원인이 아니다.

### 최소 재현 2

```text
Paste 1
Paste 2
Paste 3
Snapshot
VolumeSurface 생성
VolumeSurface 삭제
```

여기서만 ⚠가 발생하면 VolumeSurface 경로를 조사한다.

---

## 12. 현재 코드의 `catch { }`는 반드시 개선

현재처럼 예외를 모두 무시하는 방식은 디버깅과 운영 모두에서 위험하다.

```csharp
try { s.Rebuild(); } catch { }
```

특히 snapshot 관련 API에서는 실패 여부가 매우 중요하다.

최소한 다음처럼 로그를 남기도록 한다.

```csharp
try
{
    s.RebuildSnapshot();
}
catch (Exception ex)
{
    log += $"RebuildSnapshot failed: {ex}";
    throw;
}
```

현재 실험에서 “예외 없음”이 확인되었다고 하더라도 최종 코드에서는 예외를 숨기지 않는 편이 좋다.

---

## 13. 권장 검증 순서

다음 세 가지 테스트를 순서대로 실행한다.

| 테스트 | 방법 | 목적 |
|---|---|---|
| 1 | 기존 코드의 `RebuildSnapshot()` | 기준선 확보 |
| 2 | `RemoveSnapshot → Rebuild → CreateSnapshot` | Snapshot lifecycle 재생성 검증 |
| 3 | Paste + Snapshot만 실행, VolumeSurface/Visibility 경로 제거 | 외부 경로 영향 제거 |

각 테스트마다 새 transaction에서 다음을 기록한다.

```csharp
log.AppendLine(
    $"OutOfDate={s.IsOutOfDate}, " +
    $"SnapshotOutOfDate={s.IsSnapshotOutOfDate}, " +
    $"HasSnapshot={s.HasSnapshot}, " +
    $"Operations={s.Operations.Count}");
```

그리고 반드시 사람이 Definition 탭의 ⚠ 유무를 확인한다.

---

## 14. 현재 ⚠를 무시해도 되는가?

현재 증거만으로는 “무조건 무시해도 된다”고 판단하기 어렵다.

Definition 탭의 warning은 단순한 장식이 아니라 operation 상태를 나타내므로, `Item not found` 또는 `Item not OK` 상태라면 실제 재생성 과정에서 문제가 될 수 있다.

다만 현재처럼:

```text
TIN geometry 정상
토공량 정상
종단/횡단 결과 정상
IsOutOfDate = false
IsSnapshotOutOfDate = false
```

이고 UI의 `Rebuild Snapshot` 한 번으로 정상화된다면, 현재 ⚠가 즉시 geometry 손상을 의미한다고 볼 근거도 부족하다.

실무적인 판단은 다음이 가장 적절하다.

> **현재 ⚠를 치명적인 geometry 오류로 볼 근거는 부족하지만, Definition history가 정상적인 상태라고도 볼 수 없으므로 자동화 코드에서 방치하는 것은 권장하지 않는다.**

---

## 15. 최종 권고

이번 문제에서 가장 먼저 적용할 수정은 다음 한 가지다.

```csharp
if (s.HasSnapshot)
    s.RemoveSnapshot();

s.Rebuild();

s.CreateSnapshot();
```

그리고 `PasteSurface()` 이후에는 이 과정을 **한 번만** 수행한다.

즉:

```text
Paste 1
Paste 2
Paste 3
→ RemoveSnapshot
→ Rebuild
→ CreateSnapshot
→ Commit
```

이 방식에서도 UI의 `Rebuild Snapshot`과 결과가 다르다면, 그때는 다음과 같이 판단하는 것이 합리적이다.

> **Civil 3D 2026의 `PasteSurface + Snapshot` 조합에서 공개 API 경로와 Prospector UI 경로 사이에 내부 상태 처리 차이가 있거나, 관련 내부 버그가 존재할 가능성이 높다.**

이 경우에는 operation warning을 공개 API로 직접 조작하려 하기보다 Definition을 생성하는 방식 자체를 변경하거나, 최소 재현 파일을 만들어 Autodesk 지원/커뮤니티에 제출하는 방향이 더 안전하다.

---

## 16. 한 줄 결론

**`Rebuild()` 반복으로 해결하려 하지 말고, 기존 Snapshot을 제거한 뒤 전체 Paste Definition을 다시 Build하고 새 Snapshot을 생성하는 방식으로 전환하는 것이 현재 가장 우선순위가 높은 해결책이다.**

