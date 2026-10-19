# DH 정지 플러그인 — 배포판 v103.1.12

## 받는 사람이 할 일

**Civil 3D를 완전히 닫고**, 아래 파일 하나를 더블클릭하면 끝입니다.

| 파일 | 크기 | 쓰는 경우 |
|---|---|---|
| `DH정지플러그인_설치_v103.1.12.exe` | 38.9 MB | **보통은 이것** — .NET이 없는 PC에서도 돕니다 |
| `DH정지플러그인_v103.1.12_수동설치.zip` | 3.8 MB | 회사 보안정책으로 exe가 막힐 때 |

설치가 끝나면 창에 **버전과 지문**이 찍힙니다 — 그것으로 무엇이 깔렸는지 확인하세요.

```
   버전 103.1.12 · 지문 2f2b6fe65531
```

### ⚠ 처음 받는 분이 겪는 것 — 미리 알려 주세요

이 exe는 **코드 서명이 없습니다**(사내 배포용). 그래서 처음 실행하면 Windows가 막습니다:

```
Windows에서 PC를 보호했습니다   →  [추가 정보]를 누르고  →  [실행] 을 누르세요
```

인터넷(사내망 공유 포함)에서 내려받은 파일은 **차단 표시**가 붙기도 합니다 —
그때는 파일 우클릭 → 속성 → 아래쪽 **[차단 해제]** 체크 → 확인.

## 설치되는 것

```
%APPDATA%\Autodesk\ApplicationPlugins\DH.Grading.bundle\
  PackageContents.xml            자동 로드 매니페스트(명령 53개)
  Contents\*.dll                 애드인 본체 + NetTopologySuite · Npgsql · WebView2
  Contents\*.pdb                 진단 로그에 줄번호가 찍히게(문제 추적용)
  Contents\3D_VIEW_LOCATION.LSP  3D 뷰 전환 단축(v1~v4 · vv1~vv3)
  Contents\coordsys\             한국 좌표계 9종
```

한국 좌표계(KOREA_GRS80/BESSEL 125·127·129·131TM + UTM-K)는 **사용자 사전이 없을 때만** 새로 넣습니다.
이미 쓰던 사전이 있으면 **건드리지 않고** 백업(`.dhbak`)만 남기고 안내합니다.

- 설치 위치가 사용자 계정 폴더라 **관리자 권한이 필요 없습니다**
- 대상: AutoCAD Civil 3D **2025 ~ 2026** (R25.0 ~ R25.1) · Windows 64비트

## ★ 여러 사람이 쓸 때 — 판을 섞지 마세요

이 판은 도면에 저장하는 정지 기록이 **번들 v12**입니다.

- **낮은 판으로 만든 도면** → 이 판에서 그대로 읽힙니다(문제 없음)
- **이 판으로 만든 도면을 낮은 판에서 열면** → *"더 최신 애드인으로 만들어졌습니다"* 라고 뜹니다.
  그 상태로 [계획부지생성하기]를 누르면 **옹벽·사면 구간 설정이 지워집니다.**
  (지우기 전에 말은 해 줍니다 — 조용히 날리지는 않습니다.)

→ **같은 도면을 함께 보는 사람들은 같은 판을 쓰세요.**

## 안 될 때

- **"실행 중입니다"** — 작업관리자에서 `acad.exe`가 남아 있는지 보세요.
  Civil 3D가 켜져 있으면 두 설치 방식 모두 **아무것도 바꾸지 않고 멈춥니다**
  (파일이 잠겨 옛 것과 새 것이 섞이는 것을 막으려고 일부러 그렇게 했습니다).
- **리본이 안 보인다** — 명령창에 `DHGRADE`를 쳐 보세요. 그래도 없으면 `NETLOAD`로
  `Contents\DH.Grading.Civil.dll`을 직접 지정해 보시면 원인이 자동로드 쪽인지 갈립니다.
- **지우려면** 위 `DH.Grading.bundle` 폴더를 통째로 지우면 됩니다.

## 이 판에 대해 (솔직히)

- **정지 · 종단 · 횡단 · 도곽 · 터파기 · 서버 지표면** — 현장에서 계속 쓰고 있는 기능입니다.
- **옹벽 전체 구간 변환** — 종전대로 돕니다.
- **옹벽 <u>부분 구간</u> 변환** — 옹벽마다 번호(`가상옹벽N_DH` · `순수옹벽N_DH`)가 붙어 쌓이고,
  `정지면_DH`에 합성됩니다. 사면 변환으로 그 옹벽만 되돌릴 수 있습니다.
  합성이 어긋나면 **찢어진 면을 내놓지 않고 멈추고** 알립니다(까닭은 진단 로그에).
- **옹벽 합성 미루기**(정지 창 «■ 계획부지 수정»의 체크칸 · 기본 꺼짐 · 도면마다) — 켜면 옹벽 변환은 옹벽만 짓고,
  합성은 [옹벽 합성]으로 한 번에 합니다. 합성에 안 들어가는 옹벽은 지우지 않고 «보류»로 남깁니다.
  InfraWorks 내보내기 · 계획면 기준 터파기 · «이어서» 전에 빠진 옹벽이 있으면 먼저 합성하고, 그래도 빠지면 묻습니다.
- **아직 안 된 것** — 종단 · 횡단 · 옹벽 3D · 사면 SHP는 **부분 구간 옹벽을 아직 안 그립니다**(전체 구간 옹벽만).
  InfraWorks로 내보내는 지표면은 옹벽이 합성된 `정지면_DH`입니다.

문제가 나면 `%LOCALAPPDATA%\DHGrading\` 폴더의 `DHGRADE_진단.log` · `DH_자가점검.log` 를 함께 보내 주시면 원인을 짚을 수 있습니다.

## 다시 만들기

```
dotnet build src/DH.Grading.Civil/DH.Grading.Civil.csproj -c Release
python tools/setup/pkg.py         # PackageContents 버전·명령 목록 맞추기
python tools/setup/mkbundle.py    # src의 .bundle → tools/setup/bundle.zip
dotnet publish tools/setup/DHGradingSetup.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
python tools/setup/mkzip.py       # 수동 설치 zip
python tools/setup/relnote.py     # 이 문서(숫자는 전부 파일에서 잰다)
```

## 지문 (SHA-256)

```
04a2cca561da98ec3669829c038400ebee46e0bc6ac5fb88682d6971c1a99ee1  DH정지플러그인_설치_v103.1.12.exe
4421457509873e246e610f784b56d56a4da97a8565a79342a4f4d411b9af3677  DH정지플러그인_v103.1.12_수동설치.zip
```
