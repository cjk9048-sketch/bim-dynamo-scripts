# DH.Grading 애드인 배포·설치

## 다른 사람에게 배포할 때 (폴더 구성)

아래처럼 한 폴더에 담아 zip으로 전달하면 됩니다:

```
DH.Grading_설치/
  DH.Grading.bundle/      ← 애드인 본체(Contents\coordsys\ 포함)
  설치.bat                 ← 받는 사람이 더블클릭
  설치.ps1
```

받는 사람은 **Civil3D를 닫고 `설치.bat`을 더블클릭**하면 끝입니다.

## 설치 스크립트가 하는 일

1. **애드인 복사** — `DH.Grading.bundle` 을 `%APPDATA%\Autodesk\ApplicationPlugins\` 로 복사 (Civil3D 시작 시 자동 로드)
2. **한국 좌표계 정의 설치(딱 한 번)** — `KOREA_GRS80_{125·127·129·131}TM` 등이 사용자 좌표계 사전에 없으면 설치
   - 사용자 사전이 없음 → 신규 설치 (대부분 이 경우)
   - 이미 한국 좌표계 있음 → 건너뜀
   - 다른 커스텀 좌표계 사전이 이미 있음 → **덮어쓰지 않고 백업만**(`Coordsys.CSD.dhbak`), 안내 후 수동 확인

좌표계 정의가 설치되면, 좌표계가 지정 안 된 도면에서 DHINFRA 실행 시 애드인이 `KOREA_GRS80_{원점}TM` 을 도면에 자동 지정할 수 있습니다.

## 주의
- 설치 중에는 **Civil3D를 완전히 닫아야** 합니다(DLL 잠김 방지).
- 좌표계 정의는 Civil3D **재시작 후** 인식됩니다.
- 좌표계 정의 원본: 한국 Civil3D 사용자 좌표계 사전(`%LOCALAPPDATA%\Autodesk\User Geospatial Coordinate Systems`)에서 가져온 GRS80(Korea 2000)·Bessel(구 1985) 8종.
