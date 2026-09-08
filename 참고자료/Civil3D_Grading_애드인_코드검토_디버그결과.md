# Civil 3D Grading 애드인 코드 검토 및 디버그 결과

## 1. 검토 범위

현재 작성 중인 Civil 3D Grading 애드인은 **정지 계산뿐 아니라 최종 도면화까지 자동화**하는 것을 목표로 하고 있다.

현재 전체적인 흐름은 다음과 같다.

```text
원지반
 ↓
계획경계 + 계획고
 ↓
절토/성토 가상면
 ↓
원지반과 교선
 ↓
정지면_DH
 ↓
정지순수_DH
 ↓
노선
 ↓
종단 Profile
 ↓
측점
 ↓
SampleLine
 ↓
SectionView
 ↓
A1 도곽
 ↓
배치/Layout
```

계산부와 Civil 3D API를 분리한 현재 방향은 적절하다. 계산부는 Civil API에 직접 의존하지 않고 테스트 가능한 구조로 되어 있으며, 실제 도면 생성은 Commands/Civil 계층에서 담당하는 형태이다.

---

# 2. 가장 먼저 수정해야 할 문제

## 2.1 Surface Snapshot 처리

### 중요도: 최상

현재 `GradingBuilder.Freeze()`에는 다음과 같은 흐름이 있다.

```text
CreateSnapshot()
RebuildSnapshot()
Rebuild()
```

문제는 마지막에 `Rebuild()`를 다시 호출한다는 것이다.

현재 구조에서는 사실상 다음과 같은 흐름이 만들어진다.

```text
Paste
 ↓
Freeze()
 ├─ Snapshot
 ├─ RebuildSnapshot
 └─ Rebuild
 ↓
RebuildSurfacesStaged()
 ├─ Rebuild
 └─ RebuildSnapshot
```

즉 Surface 상태를 두 군데에서 서로 다른 방식으로 변경하고 있다.

특히 Snapshot을 만든 뒤 다시 `Rebuild()`하면 Snapshot과 현재 Surface 상태의 관계 때문에 `OutOfDate` 상태가 다시 발생할 가능성이 있다.

### 권장 구조

`Freeze()`는 다음 정도의 역할만 담당하도록 단순화하는 것이 좋다.

```csharp
private static void Freeze(TinSurface s)
{
    var fd = new StringBuilder();

    try
    {
        if (s.IsOutOfDate)
            s.Rebuild();

        if (s.HasSnapshot)
            s.RebuildSnapshot();
        else
            s.CreateSnapshot();
    }
    catch (Exception ex)
    {
        fd.Append($"⚠{ex.GetType().Name}:{ex.Message}");
    }

    LastFreezeDiag = fd.ToString();
}
```

더 좋은 방향은 `Composite()`에서는 Paste 작업만 수행하고, 최종 작업 단계에서 한 번만 다음 순서로 처리하는 것이다.

```text
Composite()
    ↓
Paste
    ↓
최종 합성
    ↓
RebuildSurfacesStaged()
    ├─ Source Rebuild
    ├─ Composite Rebuild
    └─ Snapshot
    ↓
최종 상태 검증
```

즉 Surface의 최종 상태를 결정하는 책임을 `RebuildSurfacesStaged()`로 통합하는 것이 좋다.

---

# 3. 하드코딩된 사용자 경로

### 중요도: 높음

현재 `CreateGradingCommand`에 다음과 같은 개발 PC 전용 경로가 존재한다.

```text
C:\Users\user\Desktop\AI\civil3d-grading\DHXSEC_진단_{label}.log
```

이 경로는 다른 PC에서는 존재하지 않을 가능성이 높다.

또한 관련 코드가 예외를 `catch { }`로 무시할 경우 사용자는 로그가 생성되지 않은 원인도 알 수 없다.

### 권장 수정

진단 로그의 기준 경로를 기존 `DiagLog.FilePath` 등 공통 로그 경로로 통일한다.

예:

```csharp
string dir = Path.GetDirectoryName(DiagLog.FilePath) ?? ".";
string path = Path.Combine(
    dir,
    $"DHXSEC_진단_{label}.log");
```

배포 전 반드시 수정해야 하는 항목이다.

---

# 4. `catch { }` 과다 사용

현재 코드에는 빈 예외 처리 구문이 상당히 많다.

특히 다음 영역에서 문제가 될 수 있다.

- Surface
- Paste
- Snapshot
- Profile
- SectionView
- SampleLine
- Style
- Layout

Civil 3D API는 상태에 따라 예외가 발생할 수 있기 때문에 `try/catch` 자체는 필요하다.

하지만 다음과 같은 형태가 반복되면 실제 오류 원인을 추적하기 어렵다.

```csharp
try
{
    ...
}
catch
{
}
```

### 문제

예를 들어 횡단면 하나가 생성되지 않았을 때:

```text
왜 안 만들어졌는가?
```

를 코드만으로 추적하기 어려워진다.

### 권장

최소한 다음 정도의 진단 정보는 남긴다.

```csharp
catch (Exception ex)
{
    log.AppendLine(
        $"재작성 실패: {ex.GetType().Name} / {ex.Message}");
}
```

특히 Surface 관련 `catch`는 반드시 로그화하는 것을 권장한다.

---

# 5. `RebuildSurfacesStaged()`의 예외 처리

현재 단계별 Surface 재작성에서 예외를 무시하는 부분이 있다.

예:

```csharp
catch { }
```

이 함수는 최종 정지면의 상태를 결정하는 핵심 단계이므로 실패 여부를 반드시 기록해야 한다.

권장:

```csharp
catch (Exception ex)
{
    log.AppendLine(
        $"'{surfaceName}' 재작성 실패: " +
        $"{ex.GetType().Name} / {ex.Message}");
}
```

최종 로그에는 최소한 다음 정보가 있으면 좋다.

```text
Surface명
작업 종류
성공/실패
예외 타입
예외 메시지
```

---

# 6. `Composite()`의 문자열 기반 성공 판정

현재 합성 결과를 문자열 로그로 만들고 호출부에서 다음과 같이 판정하는 구조가 있다.

```csharp
lg.Contains("성토:실패")
```

또는

```csharp
lg.Contains("합성 성공")
```

이 방식은 현재 동작할 수 있지만 유지보수에 취약하다.

예를 들어 나중에 로그 문구를:

```text
합성 성공
```

에서

```text
정지면 합성 완료
```

로 바꾸면 실제 작업은 성공했는데 프로그램의 성공 판정이 실패할 수 있다.

### 권장

결과를 명시적인 객체로 전달한다.

예:

```csharp
public record PasteResult(
    bool Success,
    string Label,
    string Error,
    ObjectId SurfaceId);
```

그러면:

```csharp
var result = Composite(...);

if (!result.Success)
{
    ...
}
```

형태로 처리할 수 있다.

---

# 7. `gradeOk` 역시 문자열 의존

현재 전체 성공 여부도 문자열을 기준으로 판단하는 부분이 있다.

예:

```csharp
bool gradeOk =
    pasteLog.Contains("합성 성공")
    && !anyMissed
    && !bundleFailed;
```

이 역시 결과 상태를 직접 관리하는 방향이 좋다.

예:

```text
compositeOk
boundaryOk
bundleOk
volumeOk
profileOk
sectionOk
sheetOk
plotOk
```

처럼 단계별 상태를 명시적으로 관리한다.

최종적으로:

```text
CalculationResult
DrawingResult
SheetResult
PlotResult
```

등의 결과 객체를 구성하는 방향이 좋다.

---

# 8. 계산부는 현재 방향 유지 권장

계산부의 구조는 현재 크게 뜯을 필요가 없다.

현재 다음과 같이 역할이 분리되어 있다.

```text
IGroundSurface
Point3
GradingGeometry
CrossSectionArea
WallRunBuilder
```

이런 구조는 Civil 3D API와 계산 로직을 분리하는 데 적절하다.

`GradingGeometry.Build()`에서 퇴화 폴리곤이나 면적이 0에 가까운 형상을 사전에 거부하는 것도 좋은 방어 방식이다.

또한 단수 무한루프를 방지하기 위해 실제 최소 단높이를 사용해 반복 예산을 계산하는 방식도 적절하다.

따라서 지금은 계산부보다 Civil 도면화부를 우선 정리하는 것이 효율적이다.

---

# 9. 수량 계산 로직

현재 기본 정의는 다음과 같다.

```text
절토 = G - P
성토 = P - G
터파기 기준면 = min(P, G)
```

그리고 5m 기준으로 얕은/깊은 터파기를 분리하고 있다.

또한 `NaN`과 `0`을 구분하는 방식도 적절하다.

```text
0   = 측정했지만 값이 없음
NaN = 측정 자체를 하지 못함
```

이는 향후 토적표 자동화에서 중요하다.

---

# 10. `Lower()` 처리 주의

현재 `Lower(G, P)`는 한쪽 값이 `NaN`인 경우 다른 값을 사용하는 구조다.

예:

```text
G = 정상
P = NaN
```

이면 G를 사용하는 방식이다.

현재 `NoPlanCells` 같은 진단용 카운트를 별도로 관리하고 있기 때문에 방어 구조는 존재한다.

다만 실시설계용 수량 계산에서는 향후 다음 정책을 검토하는 것이 좋다.

```text
계획면 데이터 없음
        ↓
해당 단면 수량 계산 제외
        ↓
검토 필요 경고
```

즉 계획면이 없는 구간을 조용히 다른 면으로 대체해서 계산하기보다는 명확한 경고 상태로 만드는 것이 안전하다.

---

# 11. 도면화 전체 구조

현재 애드인의 도면화 흐름은 다음과 같이 구성되어 있다.

```text
원지반
 ↓
계획경계
 ↓
계획고
 ↓
절토/성토 계산
 ↓
정지면 생성
 ↓
노선 생성
 ↓
종단 Profile
 ↓
측점 생성
 ↓
SampleLine
 ↓
SectionView
 ↓
도곽
 ↓
Layout 배치
```

현재 프로젝트 목적이 단순한 정지 계산이 아니라 **설계도면 자동 생성**이므로 이 방향은 적절하다.

---

# 12. 종단 자동축척

현재 A1 기준으로 다음과 같은 용지 조건을 사용하고 있다.

```text
841 × 594
좌 25
우 20
상 20
하 50
```

실제 노선 길이와 표고 범위를 기준으로 축척을 결정하는 구조다.

이 방식은 사용자가 원하는:

> 자동 축척으로 종단과 횡단을 만들고, 축척에 따라 도곽 크기도 조절하는 방식

과 잘 맞는다.

---

# 13. 종단/횡단 도곽 책임 분리

현재 `ProfileCommand`, `SheetCommand`, `SectionCommand`, `XsecViewCommand` 사이에 도면 배치 책임이 일부 섞여 있다.

특히 `SheetCommand`가 매우 큰 파일이므로 장기적으로 분리하는 것이 좋다.

권장 구조:

```text
Sheet
 ├─ SheetLayout
 ├─ SheetFrame
 ├─ SheetViewport
 ├─ SheetScale
 ├─ SheetTitle
 └─ SheetPlot

Profile
 ├─ ProfileBuilder
 ├─ ProfileStyle
 └─ ProfileSheetAdapter

Section
 ├─ SectionBuilder
 ├─ SectionStyle
 └─ SectionSheetAdapter
```

즉 `SheetCommand`는 명령 실행을 담당하고 실제 도곽 계산/배치 로직은 별도 클래스로 분리한다.

---

# 14. 횡단면도 축척

현재 횡단면도는 다음 방식으로 처리하고 있다.

```text
측점
 ↓
SampleLine
 ↓
SectionView
 ↓
실제 크기 측정
 ↓
축척 결정
 ↓
A1 내부 칸 배치
```

실제 SectionView의 크기를 측정하여 축척을 결정하는 방향은 좋다.

미리 축척을 고정하는 것보다 실제 생성된 SectionView의 크기를 기반으로 판단하는 것이 다양한 형상에 대응하기 쉽다.

---

# 15. 횡단면 축척과 주석축척

현재 다음 두 값이 분리되어 있다.

```text
횡단도 축척 = 1 : scale
도면 주석축척 = 1 : annoScale
```

둘이 다르면:

```text
횡단 글자
밴드
라벨
주석
```

등의 크기가 달라질 수 있다.

### 권장

최종적으로 하나의 공통 Scale Context를 사용하는 구조가 좋다.

예:

```text
DrawingScaleContext
 ├─ ModelScale
 ├─ AnnotationScale
 ├─ PaperWidth
 ├─ PaperHeight
 └─ ViewScale
```

Profile, Section, Band, Label, Frame이 같은 컨텍스트를 사용하도록 만든다.

---

# 16. PlotPaperMargins

현재 실제 용지 크기를 확인하고 도곽 크기와 비교하는 방식은 적절하다.

특히 Plot 설정에서 단위를 임의로 다시 곱하지 않고 실제 `PlotPaperSize`를 기준으로 검산하도록 한 부분은 유지하는 것이 좋다.

이 부분은 현재 구조를 크게 변경하지 않는 것을 권장한다.

---

# 17. `ActiveDocument` 의존성

코드 전반에서:

```csharp
CivilApplication.ActiveDocument
```

를 많이 사용하고 있다.

이미 함수에:

```csharp
Document doc
Database db
```

를 전달하고 있다면 가능하면 현재 작업 중인 CivilDocument도 명시적으로 전달하는 것이 좋다.

예:

```csharp
DoGrade(
    Document doc,
    CivilDocument cdoc,
    Database db,
    ...
)
```

이렇게 하면 여러 도면이 열려 있거나 DocumentLock이 개입되는 상황에서 다른 문서를 참조하는 위험을 줄일 수 있다.

---

# 18. 현재 코드의 가장 큰 구조적 위험

현재 프로젝트는 실제 문제를 발견할 때마다 방어 코드를 추가하면서 발전한 형태로 보인다.

코드에 다음과 같은 버전별 수정 흔적이 많이 존재한다.

```text
v32.2
v32.4
v32.5
v32.9
v32.12
v32.14
v32.16
v32.23
v32.24
v32.30
v32.35
v32.41
...
```

이것 자체가 나쁜 것은 아니다.

오히려 실제 Civil 3D에서 발생한 문제를 추적하고 수정해 온 기록이므로 중요한 정보다.

다만 이제는 한 번 리팩터링 단계가 필요하다.

---

# 19. 권장 디버그/리팩터링 순서

## 1단계 — Surface 상태 처리 통합

```text
Composite()
    ↓
Paste만
    ↓
최종 합성
    ↓
RebuildSurfacesStaged()
    ↓
Snapshot
    ↓
최종 검증
```

`Freeze()`의 중복 Snapshot/Rebuild 로직 제거.

---

## 2단계 — 빈 catch 제거

우선 다음 영역부터 로그화한다.

```text
Surface
Paste
Snapshot
Profile
SectionView
SampleLine
Style
Layout
```

---

## 3단계 — 하드코딩 경로 제거

```text
C:\Users\user\Desktop\AI\...
```

→ 공통 진단 로그 경로 사용.

---

## 4단계 — 문자열 기반 성공 판정 제거

현재:

```csharp
Contains("합성 성공")
Contains("성토:실패")
```

→ 결과 객체 또는 enum으로 전환.

---

## 5단계 — SheetCommand 분리

큰 파일을 한 번에 뜯지 말고 다음 순서로 분리한다.

```text
SheetCommand
SheetScaleCalculator
SheetFrameBuilder
SheetViewportBuilder
SheetLayoutBuilder
SheetTitleBuilder
```

---

## 6단계 — 공통 DrawingScaleContext 도입

Profile / Section / Band / Label / Frame이 동일한 축척 정보를 공유하도록 한다.

---

## 7단계 — 회귀 테스트

다음 순서로 실제 Civil 3D 테스트를 수행한다.

```text
① 단순 평지
② 단순 절토
③ 단순 성토
④ 절토 + 성토
⑤ 다단 절토
⑥ 옹벽
⑦ 복수 구역
⑧ 노선
⑨ 종단
⑩ 횡단
⑪ A1 배치
⑫ Layout
⑬ Plot
```

---

# 20. 최종 우선순위

| 우선순위 | 문제 | 위험도 |
|---|---|---|
| 1 | `Freeze()` Snapshot/Rebuild 순서 및 중복 | 🔴 매우 높음 |
| 2 | 빈 `catch {}` 과다 | 🔴 높음 |
| 3 | 하드코딩된 `C:\Users\user...` 경로 | 🔴 배포 치명적 |
| 4 | `Composite()` 문자열 기반 성공 판정 | 🔴 높음 |
| 5 | `gradeOk` 문자열 판정 | 🟠 높음 |
| 6 | `ActiveDocument` 의존 | 🟠 높음 |
| 7 | SheetCommand 대형화 | 🟠 유지보수 위험 |
| 8 | 횡단/종단 축척과 주석축척 분리 | 🟡 향후 도면 품질 문제 |
| 9 | 계산부 | 🟢 현재 구조 유지 권장 |

---

# 21. 결론

현재 코드를 처음부터 다시 만들 필요는 없다.

현재 구조는 상당히 잘 발전해 있으며, 특히 **계산부는 유지하면서 Civil 3D 도면화부를 정리하는 방향**이 적절하다.

가장 중요한 것은 앞으로 기능을 계속 추가하기 전에 다음 항목을 먼저 안정화하는 것이다.

```text
① Surface 상태 처리
② Snapshot/Rebuild 순서
③ 예외 로그
④ 결과 상태 관리
⑤ Sheet 구조
⑥ 공통 축척 관리
```

최종 목표는 다음과 같은 하나의 자동 도면 생성 파이프라인으로 만드는 것이다.

```text
[계산]
   ↓
[정지면]
   ↓
[노선]
   ↓
[종단]
   ↓
[횡단]
   ↓
[도곽]
   ↓
[Layout]
   ↓
[Plot]
   ↓
[완성 설계도면]
```

특히 현재 단계에서는 **새 기능을 무작정 추가하기보다 1~6번 구조적 문제를 먼저 정리한 뒤 도면화 기능을 확장하는 것이 가장 안전하다.**
