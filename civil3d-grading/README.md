# DH 정지(절성토) — Civil3D 2026 플러그인

계획 폴리곤(평지 경계)을 그려두면, 그 가장자리에서 바깥쪽으로
**5m 정지 → 1m 소단 → 5m 정지 → 1m 소단 …** 을 반복하다가
**원지반(원래 땅)과 만나는 곳에서 자동으로 멈추는 계단식 비탈면**을
하나의 TIN Surface로 만들어 주고, **절토·성토량(m³)** 까지 계산합니다.

> **왜 코리더를 안 쓰나?** 코리더는 경계가 꺾이는 코너에서 단면이 겹쳐 보타이(X자 꼬임)가 생깁니다.
> 이 도구는 코리더 대신 **"경계에서 떨어진 거리"만으로 단을 만드는 distance-field 방식**이라
> 오목·볼록 코너에서 면이 절대 꼬이지 않습니다.

---

## 명령어

| 명령 | 설명 |
|------|------|
| `DHGRADESET` | **설정 팝업** — 단높이·소단폭·절토구배·성토구배·격자 해상도 입력 후 [저장] |
| `DHGRADE` | **설정 팝업**[확인] → 계획 폴리곤 클릭 → 원지반 표면 클릭 → 정지면 TIN Surface 생성 + 토공량 보고 |

리본 탭 **"DH 정지"** 에도 [정지 설정]·[정지면 생성] 버튼이 생깁니다.
파라미터 입력은 명령창 타이핑이 아니라 **팝업 창의 칸**에 숫자를 넣는 방식입니다.

구배 표기 **1:n = 수직 1 : 수평 n** (예: `1:1.5` = 수직 1m 올라갈 때 수평 1.5m). 절토·성토 각각 지정.

---

## 사용 순서

1. 평지(계획면)의 경계를 **닫힌 폴리라인**으로 그립니다. 폴리라인의 **표고(Z)가 계획고**가 됩니다.
   - 2D 폴리라인은 Elevation 한 값, 3D 폴리라인/피처라인은 정점별 Z를 사용합니다.
2. 원지반은 **Civil3D TIN Surface**로 준비합니다.
3. `DHGRADE` 실행 → **설정 팝업**이 뜨면 단높이 5 / 소단 1 / 절토·성토 구배 / 격자(기본 1m)를 확인·수정하고 [확인].
   (값만 미리 저장하려면 `DHGRADESET`으로 팝업을 열어 [저장].)
4. 폴리곤 클릭 → 원지반 표면 클릭.
5. 새 표면 **`정지면_DHGrade`** 가 생성되고, 절토·성토·순토공량이 팝업으로 표시됩니다.

절토/성토는 위치별 자동 판별: 계획고가 원지반보다 **높으면 성토**(아래로 비탈), **낮으면 절토**(위로 비탈).
한 폴리곤에서 한쪽 절토·다른 쪽 성토도 자동 처리됩니다.

---

## 빌드 (개발자용)

```
src/
  DH.Grading.Core        # 순수 기하 로직 (AutoCAD 의존성 0) — 단위테스트 대상
  DH.Grading.Core.Tests  # xUnit 테스트
  DH.Grading.Civil       # Civil3D 2026 플러그인 (UI·API 연동)
```

- 로직 테스트(어디서나 가능):
  ```
  dotnet test src/DH.Grading.Core.Tests/DH.Grading.Core.Tests.csproj
  ```
- 플러그인 빌드(**Civil3D 2026 설치 PC에서**):
  ```
  dotnet build src/DH.Grading.Civil/DH.Grading.Civil.csproj -c Release
  ```
  Civil3D 설치 경로가 기본값(`C:\Program Files\Autodesk\AutoCAD 2026\`)과 다르면
  `DH.Grading.Civil.csproj`의 `<AcadDir>` 한 줄만 수정하세요.

## 설치 (자동 로드)

`src/DH.Grading.Civil/DH.Grading.bundle` 폴더를 아래로 복사하고,
`Contents` 폴더에 빌드된 `DH.Grading.Civil.dll`·`DH.Grading.Core.dll` 을 넣으세요:

```
%APPDATA%\Autodesk\ApplicationPlugins\DH.Grading.bundle\
  PackageContents.xml
  Contents\DH.Grading.Civil.dll
  Contents\DH.Grading.Core.dll
```

Civil3D를 다시 켜면 자동 로드됩니다. (수동 로드는 `NETLOAD`로 DLL 지정)

---

## 동작 원리(요약)

격자(기본 1m)의 각 점에서 가장 가까운 경계점까지의 **바깥 거리 d**를 구하고,
`d`에 따라 계단 프로파일(`5n m 비탈 → 1m 소단 → 반복`)만큼 계획고에서 올리거나 내립니다.
원지반과 만나는 지점에서 잘라(daylight) 정지 영역을 확정합니다.
거리 `d`는 코너에서도 항상 한 값으로 정의되므로 면이 겹치지 않습니다.

> 토공량은 Civil3D 체적 표면(`TinVolumeSurface`)으로 정밀 산출하며,
> 실패 시 격자 기반 근사값으로 대체합니다. 격자 해상도를 낮추면(0.5m) daylight 경계가 더 매끈해집니다.
