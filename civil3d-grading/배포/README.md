# DH.Grading 애드인 배포·설치

## 가장 간단한 방법 — 단일 exe (v12.2~)

`DH정지플러그인_설치_vXX.exe` 파일 **하나만** 전달하면 됩니다.
받는 사람은 Civil3D를 닫고 더블클릭 → 애드인 복사 + 한국 좌표계 9종 설치까지 자동.
(.NET 미설치 PC에서도 동작하는 자체 완비형입니다. 만들기: `tools\setup`에서
`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true`
— 그 전에 배포 번들 내용물을 `tools\setup\bundle.zip`으로 갱신할 것.)

## zip + 설치 스크립트 방식 (폴더 구성)

아래처럼 한 폴더에 담아 zip으로 전달하면 됩니다:

```
DH.Grading_설치/
  DH.Grading.bundle/      ← 애드인 본체(Contents\coordsys\ 포함 — DLL·NetTopologySuite·좌표계 사전)
  설치.bat                 ← 받는 사람이 더블클릭
  설치.ps1
  README.md
```

받는 사람은 **Civil3D를 닫고 `설치.bat`을 더블클릭**하면 끝입니다.
(스크립트를 못 쓰는 환경이면 `DH.Grading.bundle` 폴더를
`%APPDATA%\Autodesk\ApplicationPlugins\` 에 복사만 해도 됩니다 — 아래 자동 검사 참고.)

## 설치 스크립트가 하는 일

1. **애드인 복사** — `DH.Grading.bundle` 을 `%APPDATA%\Autodesk\ApplicationPlugins\` 로 복사 (Civil3D 시작 시 자동 로드)
2. **한국 좌표계 정의 설치(딱 한 번)** — `KOREA_GRS80/BESSEL_{125·127·129·131}TM` + UTM-K 9종이 사용자 좌표계 사전에 없으면 설치
   - 사용자 사전이 없음 → 신규 설치 (대부분 이 경우)
   - 이미 한국 좌표계 있음 → 건너뜀
   - 다른 커스텀 좌표계 사전이 이미 있음 → **덮어쓰지 않고 백업만**(`Coordsys.CSD.dhbak`),
     `MAPCSLIBRARY` 명령 → 가져오기에서 `Contents\coordsys\CSLibrary.xml` 을 선택해 수동 병합

## 애드인 내장 자동 검사 (v12.1~)

애드인 자체가 Civil3D 시작 시 좌표계를 검사합니다 — 설치 스크립트를 안 거쳐도:
- 사용자 좌표계 사전이 없으면 → 자동으로 9종을 등록하고 1회 안내 팝업
- 이미 있으면 → 조용히 통과
- 다른 커스텀 사전이 있으면 → 덮어쓰지 않고 수동 가져오기(CSLibrary.xml) 안내 팝업

좌표계 사전은 **Windows 계정별**(`%LOCALAPPDATA%`)이므로, 한 PC를 여러 계정이 쓰는 경우에도
각 계정의 첫 실행 때 자동으로 채워집니다.

좌표계가 설치되면, 좌표계가 지정 안 된 도면에서 DHINFRA 실행 시 애드인이
`KOREA_GRS80_{원점}TM` 을 도면에 자동 지정하고, 위성.tif도 그 좌표계로 재투영해 내보냅니다.

## 지원 버전
- **Civil3D 2026 (검증 완료)**. 매니페스트는 2025(R25.0)도 허용하지만 2025는 미검증.
- 2024 이하는 불가(.NET 세대가 달라 재빌드 필요).

## 주의
- 설치 중에는 **Civil3D를 완전히 닫아야** 합니다(DLL 잠김 방지).
- 좌표계 정의는 등록 직후 인식되지만, 목록에 안 보이면 Civil3D를 **한 번 재시작**하세요.
- 위성 영상 내보내기는 인터넷이 필요합니다(안 되면 위성.tif만 생략, 나머지는 정상).
- 진단 로그: 배포 PC에서는 `%LOCALAPPDATA%\DHGrading\DHGRADE_진단.log` 에 기록됩니다.
- 좌표계 정의 원본: JACK이 직접 정의한 GRS80(Korea 2000)·Bessel(구 1985) 8종 + UTM-K
  (`Contents\coordsys\CSLibrary.xml`, 한국어판 Civil3D에도 기본 포함 아님).
