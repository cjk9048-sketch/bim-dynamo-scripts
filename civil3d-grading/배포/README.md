# DH 정지 플러그인 — 배포판 v117.44.1

## 받는 사람이 할 일

**Civil 3D를 완전히 닫고**, 아래 파일 하나를 더블클릭하면 끝입니다.

| 파일 | 크기 | 쓰는 경우 |
|---|---|---|
| `DH정지플러그인_설치_v117.44.1.exe` | 214.0 MB | **보통은 이것** — .NET이 없는 PC에서도 돕니다 |
| `DH정지플러그인_v117.44.1_수동설치.zip` | 178.9 MB | 회사 보안정책으로 exe가 막힐 때 |

설치가 끝나면 창에 **버전과 지문**이 찍힙니다 — 그것으로 무엇이 깔렸는지 확인하세요.

```
   버전 117.44.1 · 지문 561083a332f0
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
  PackageContents.xml            자동 로드 매니페스트(명령 268개)
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

이 판은 도면에 저장하는 정지 기록이 **번들 v14**입니다.

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

## 새 판 올리는 법 (JACK) — 설치한 사람에게 «새 판 있음»이 뜬다

1. `GradingSettings.Version` 을 올리고 `src/DH.Grading.Civil/Resources/ReleaseNotes.txt` 맨 위에 그 판 칸을 적는다
   (`# v117.19.0 (2026-11-24)` 아래 `- 바뀐 점` 한 줄씩 — [정보] 창과 알림에 그대로 나온다).
2. 아래 «다시 만들기» 순서대로 빌드 → 설치 exe → `python tools/setup/mklatest.py` (→ `배포/latest.json` —
   판 · 날짜 · sha256 · 노트는 전부 파일에서 잰다 · 꼭 깔아야 하는 판이면 `--mandatory`).
3. 공유 위치에 **exe 먼저, latest.json 나중에** 복사한다(반대로 하면 잠깐 동안 없는 exe를 가리킨다).
4. 끝 — 받은 사람의 Civil 3D가 켜질 때(하루 한 번 · 3초 · 회사망 밖이면 조용히 넘어감) 오른쪽 아래 알림 + 명령줄 한 줄,
   리본 [기타 › 정보]에 «새 판». [설치 파일 열기] → Civil 3D를 닫고 exe 실행.

**확인 위치 넣기** — 위치 값은 저장소에 안 남긴다. 설치 묶음을 만들 때만 넣는다(원본 `dh.keys.json`은 그대로):

```
python tools/setup/mkbundle.py --update \\회사PC\DH정지\latest.json [--update https://…/latest.json]
python tools/setup/mkzip.py    --update \\회사PC\DH정지\latest.json
```

(이 PC에 한 번만 해 두려면 환경 변수 `DH_GRADING_UPDATE` = 위치들을 `;`로 이어서 — `--update`가 없을 때 쓴다.)
여러 곳이면 차례로 보되 **전부 합쳐 3초**. 공유 PC가 꺼져 있으면 조용히 넘어간다. 위치가 하나도 없으면 알림 기능이 꺼지고
[정보] 창에 «업데이트 위치 미설정»이 뜬다(창의 «위치 바꾸기(고급)»에서 사람마다 직접 넣을 수도 있다).

**위치를 옮길 때** — 옛 자리의 latest.json에 `"moved": ["\\\\새서버\\DH정지\\latest.json"]`을 더해 둔다.
한 번이라도 그것을 읽은 PC는 새 위치를 사용자 기록(`%APPDATA%\DH.Grading\update.json`)에 저장하고, 다음부터 새 위치를 먼저 본다.
옛 자리는 모두가 한 번씩 켤 때까지(한두 주) 남겨 둔다.

**latest.json 꼴** — `mklatest.py` 가 만든다(손으로 고칠 때만 참고):

```json
{ "version": "117.19.0", "date": "2026-11-24", "mandatory": false,
  "notes": ["바뀐 점 하나", "바뀐 점 둘"],
  "installer": "DH정지플러그인_설치_v117.19.0.exe",
  "sha256": "64자 16진(설치 exe의 SHA-256)" }
```

- `installer` — 상대 경로면 latest.json 자리 기준 · UNC/절대 경로 · `https://` 주소(받아서 sha256을 맞춘 뒤 안내 — https만 · sha256 필수).
- `moved` — (선택) 새 확인 위치 목록(UNC · 절대 경로 · https). 위 «위치를 옮길 때».
- 판 비교는 숫자로(117.9 < 117.10) · 같거나 낮은 판은 안 알린다 · «이 판 알리지 않기»는 그 판만(`mandatory`면 늘 알림).

## 다시 만들기

```
dotnet build src/DH.Grading.Civil/DH.Grading.Civil.csproj -c Release
python tools/setup/pkg.py         # PackageContents 버전·명령 목록 맞추기
python tools/setup/mkbundle.py --update <위치>   # src의 .bundle → tools/setup/bundle.zip(확인 위치는 묶음에만)
dotnet publish tools/setup/DHGradingSetup.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
python tools/setup/mkzip.py --update <위치>      # 수동 설치 zip
python tools/setup/relnote.py     # 이 문서(숫자는 전부 파일에서 잰다)
python tools/setup/mklatest.py    # 새 판 알림 배포/latest.json(판 · 날짜 · sha256 · 노트) — 공유 위치에 exe 다음에 복사
```

## 지문 (SHA-256)

```
4256efb0e675d85918431906dbcce06f685dc8762695898e01ce915132b0d861  DH정지플러그인_설치_v117.44.1.exe
388d7eee2303340205f8808b64320d1c3919ceccd0350549f26f08caf5e41a62  DH정지플러그인_v117.44.1_수동설치.zip
```
