# [검토 요청] Civil3D 계단식 정지(절성토) 플러그인 — 깨끗한 정지면 생성 방법

> 다른 AI/전문가에게 검토받기 위한 자족 문서. 맥락 없이 읽어도 되게 작성.

## 1. 목표
Civil3D 2026 / .NET 8 플러그인. 사용자가 **닫힌 계획경계 폴리곤(설계 표고)** + **원지반 TIN 표면**을 선택하면:
- 경계에서 바깥으로 **계단식 비탈**(예: 단높이 5m + 소단 1m 반복, 절토 1:1·성토 1:1.5)을 자동 생성하고,
- 비탈이 **원지반과 만나는 선(daylight)** 까지 닫아,
- 깨끗한 정지면 **TinSurface** 1개를 만든다(토량 산출용).
- 대상: 배수지·정수장 같은 **소규모 부지**. 도로/철도 대규모 아님.
- **최대 난점: 혼합 절성토** — 한 부지 안에 절토 구역(원지반>계획고)과 성토 구역(원지반<계획고)이 공존.
- 사용자 요구: **절토/성토를 합친 "하나의 통합 정지면", 전환부에 인위적 '벽' 없이.** 격자(grid) 방식 **영구 배제**(계단 톱니 때문).

## 2. 환경/제약
- Civil3D 2026(=R25.1), .NET 8. AutoCAD/Civil3D 관리형 DLL 참조(AeccDbMgd 등).
- Civil3D 네이티브 Grading 객체 생성 API는 사실상 막힘(GradingGroup 타입 없음, Create 시그니처 블라인드). Corridor는 사용자가 보타이 위험으로 거부. → **TinSurface 직접 생성** 경로로 진행 중.
- TinSurface 입력 수단: `BreaklinesDefinition.AddStandardBreaklines(점목록)`, `BoundariesDefinition.AddBoundaries(점목록, Outer/Hide, 파괴식여부)`, 점 추가 `AddVertices`.
- NetTopologySuite(NTS) 2.5 도입함(벡터 기하: Buffer 오프셋, Buffer(0) 자가교차정리, Intersection, Union).

## 3. 시도한 방식과 실패 (시간순) — 같은 실수 반복 회피용
1. **격자 distance-field**: 격자 스캔으로 셀별 비탈고 → 가장자리 **계단 톱니/빗살**. daylight를 격자 마스크 경계로 추적 시 혼합지에서 다중루프·엉킴. → 격자 영구 폐기.
2. **차이표면(정지면−원지반) 0등고선으로 daylight**: 혼합지에서 **절↔성 전환선(부지 내부)까지** 0등고선이 잡혀 daylight가 내부까지 엉킴.
3. **기하 레이마칭 daylight**(경계점마다 바깥 광선으로 교선 탐색): **오목(L자) 입구를 직선으로 가로지름(chord)**, 못 찾은 점은 직선으로 메움 → 화면 횡단 직선.
4. **통합 단일 표면(격자, 절토+성토 한 장)**: 절토셀(계획고+h, 위로)과 성토셀(계획고−h, 아래로)이 **전환부에서 맞물려 가파른 '벽'(부채꼴 강제채움)**. 반복 재발.
5. **절토/성토 분리 + NTS 단 링을 브레이크라인으로**: Civil3D **"브레이크라인 교차" 오류 144개** → 표면 붕괴. 원인: ①daylight 링이 단 링들과 교차 ②NTS 2D 연산이 Z 소실(0/NaN) ③초근접/중복점.
6. **(최신·실패) NTS 통합**: 단 링 = 경계 Buffer 오프셋(동심, 비교차). daylight = 절/성 각각 march→Buffer(0)→폴리곤. **전환부 벽 제거하려고 단 링을 daylight 폴리곤으로 클립(Intersection)**. daylight=Union해서 Outer Boundary로만(브레이크라인 아님), 0.05m 점솎기+Z복원.
   - **결과: 표면 거의 전부 붕괴**(평평한 판 + 지느러미 몇 개, 계단 거의 없음).
   - **원인**: daylight march가 오목·전환부에서 여전히 꼬여(chord) Buffer(0)로도 깨끗한 폴리곤이 안 나옴 → 그 **망가진 daylight로 단을 클립**하니 단 대부분이 잘려나감. 즉 **가장 불안정한 요소(daylight)를 단 생성의 전제로 묶어** 전체가 무너짐.

## 4. 반복되는 핵심 미해결 문제 (검토 포인트)
**오목 L자 경계 + 혼합 절성토**에서:
- (A) 비탈이 원지반과 만나는 **daylight 선을 깨끗하게**(자가교차·chord 없이) 구하기.
- (B) **절토 영역과 성토 영역을 깨끗하게 분리**해 하나의 표면으로 합치되 전환부에 **벽/구멍/겹침 없이** 닫기.
- (C) NTS 벡터 결과를 **Civil3D TinSurface로 안정적으로 투입**(브레이크라인 교차·중복점·Z소실 없이).

## 5. 검토받고 싶은 질문
1. 오목 다각형 + 혼합 절성토에서 **daylight를 robust하게** 구하는 정석은? (per-vertex march의 chord 문제 회피법; NTS Buffer 기반 오프셋의 self-intersection을 daylight에 응용하는 법; 또는 그레이드 TIN ∩ 원지반 TIN을 Civil3D에서 직접 교차시키는 법)
2. **하나의 TinSurface**로 절토(위로)+성토(아래로)를 합치는 게 기하학적으로 타당한가? (단일값 표면에 +h와 −h가 공존 → 전환부 처리). 아니면 **절토 TIN + 성토 TIN을 따로 만들고 Civil3D `PasteSurface`로 합치는** 게 정석인가?
3. NTS 단 모서리 링을 Civil3D에 넣을 때 **브레이크라인 vs 점(AddVertices) vs Outer/Hide 경계** 중 무엇이 안정적인가? 동심 오프셋 링을 브레이크라인으로 넣어도 교차가 안 나려면?
4. 단을 daylight로 클립하는 대신, **전체 계단 표면(원지반 무시, N단)을 먼저 깨끗이 만들고**, daylight로는 **Outer Boundary 파괴식 트림만** 하는 게 더 안전한가? (클립을 표면 생성에서 분리)
5. 이 문제에 **NTS 벡터 + TinSurface** 접근 자체가 옳은 방향인가, 아니면 다른 접근(예: 그레이드를 코리더/어셈블리, 또는 외부 메시 라이브러리)을 권하나?
6. 소단(bench)이 있는 계단 비탈을 TIN으로 표현할 때 **각 단의 안/바깥 모서리 링(2개/단)**을 브레이크라인으로 주는 게 맞나, 더 나은 표현은?

## 6. 참고 (이미 가진 자료)
- `참고자료/NTS를 이용한 grading.txt`: per-vertex march daylight + NTS Buffer(0) + 브레이크라인/Outer 경계 예제(단일 경사, 소단 없음).
- `참고자료/NTS를 이용한 grading2.txt`: NTS→Civil3D 브레이크라인 오류 3원인 = ①Z 소실 ②초근접/중복점 → "데이터 세척(점솎기+Z복원)" 필수.

## 7. 현재 코드 위치 (참고)
- `src/DH.Grading.Core/NtsGrading.cs` (NTS 통합 시도 — 최신·실패본)
- `src/DH.Grading.Core/GradingEngine.cs` (격자 방식 — 폐기 예정, 코드 잔존)
- `src/DH.Grading.Civil/SurfaceBuilder.cs` (`BuildSurfaceFromRings`, `TrimToGroundIntersection`, `BuildFromBenchLoops`)
- `src/DH.Grading.Civil/Commands/CreateGradingCommand.cs` (DHGRADE 명령)
