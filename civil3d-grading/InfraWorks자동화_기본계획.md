# Civil3D → InfraWorks 원스톱 자동화 — 기본계획

> 목표: Civil3D 애드인 **버튼 1번** → 지형(LandXML) + 옹벽(3D DWG) [+나중에 V-World 위성사진]이
> 로드된 **InfraWorks 2026 모델이 자동으로 열림**. 완전 0클릭 지향(단, 실측으로 한계 확정).
> JACK 인터뷰(0722): 완전 0클릭 목표 / 지형+옹벽 먼저 / InfraWorks 2026 설치·임포트 경험 있음 / V-World 필수(나중).

## 1. 핵심 발견 — test.sqlite 분석 (매우 유리)
- InfraWorks 모델(.sqlite)의 **`DATA_SOURCES`** 테이블에 소스별로 저장:
  - `CONNECTION_STRING` = **파일 경로**, `SOURCE_CS` = 좌표계, `DATA_PROVIDER_GUID` = 제공자, `CONFIG` = 임포트 설정(JSON), `POSITION/OFFSET/ROTATION/SCALING`, `VISIBILITY` 등.
- **test 모델은 이미 완벽한 템플릿** — 데이터소스 2개:
  1. `test - Surfaces` → `TERRAIN_SURFACES`, provider `ALX_LANDXML_CFG`, CONN=`...\test.xml`, CONFIG.sourceIDs=`["정지면_DH"]`, srcBBox=[240151.8,450232.2,65, 240454.4,450574.5,117]
  2. `옹벽3D` → `CITY_FURNITURE`, provider `ALX_3DDWG_CFG`, CONN=`...\옹벽3D.dwg`, CONFIG(flipYZ 등)
- **좌표계 확정 = EPSG:5186 (중부원점)**: `KOREA_GRS80_127TM`, 중앙자오선 127°, FE 200000, FN 600000, lat0 38°.
- 모델 위치: `C:\Users\user\Documents\Autodesk InfraWorks Models\test.sqlite` (+ `test.files` 폴더).

## 1-B. Refresh vs Reimport 실측 (JACK 0722) — 중요
- InfraWorks는 임포트 시점에 소스를 **내부 모델로 변환·구축해 저장**(라이브 링크 아님).
- **Refresh = 소용없음**: 변환된 캐시만 갱신, 바뀐 파일 반영 안 됨(처음 모양 유지).
- **Reimport = 됨**: 파일 재변환으로 형상 갱신. **파일 이름(경로)만 같으면 그냥 Reimport → 즉시 반영, Configure 재설정 불필요.** ⇐ 채택 동작. ★설계 핵심규칙: 항상 같은 경로·같은 파일명으로 내보낼 것.
- ⇒ 갱신은 **반드시 Reimport(수동)** 에 묶임. 자동 트리거 공식 API 없음.
- **소스 종류별 갱신법 확정(JACK 0722 실측):**
  - **옹벽(3D DWG / City Furniture) = `Reimport`.** 같은 경로·같은 파일명이면 항상 갱신됨(파일 없으면 경고→재생성하면 복구). Refresh는 캐시만이라 무효.
  - **터레인(LandXML) = `Reimport` 항상 비활성.** InfraWorks가 임포트 시 옆에 **`<xml>.aecc.pnt` + `<xml>.aecc.tri` 캐시**를 굽고, Refresh는 그 캐시를 읽음 → xml만 바꾸면 캐시 불일치로 **지형 사라짐**. **★확정 갱신법(JACK 0722 실측): `지형.xml` 덮어쓰기 + `.aecc.pnt`/`.aecc.tri` 두 캐시 삭제 + `Refresh` → 지형 새로 구워짐(정상).** (원본 xml은 유지, 캐시만 삭제)
- **★자동화 절대규칙:** 파일을 **삭제하지 말고 제자리 덮어쓰기만**(소스 끊김·경고 방지) + **항상 같은 경로·같은 파일명**.
- **현실 착지점**: 버튼→파일 덮어쓰기→InfraWorks 모델 자동 열림→**옹벽 Reimport + 터레인 Refresh(각 1번)**. 재설정 불필요. (완전 0클릭은 공식 불가; UI자동화/InfraWorks 애드인은 나중 선택.)

## 2. 가능성 판정
- **데이터 준비(지형 LandXML / 옹벽 DWG / 위성 래스터): 확실히 가능.** 셋 다 InfraWorks 정식 데이터소스 형식.
- **완전 0클릭(밖에서 스크립트로 자동 임포트): 공식 기능 없음(불확실).** InfraWorks JS API는 앱 내부에서 "이미 추가된 소스 가공/객체 추가" 용도. 명령줄 `-script 자동구축`은 실제 동작과 불일치.
- **현실 최대 자동화:** 템플릿 모델(test.sqlite)이 가리키는 파일을 애드인이 덮어쓰고 → InfraWorks로 그 모델 열기 → (모델이 자동 반영하면 0클릭, 아니면 **'데이터 새로고침' 1번**).
- ⇒ **M0 실측**으로 "열 때 바뀐 소스를 자동 반영하는가?"를 먼저 확정. 여기서 0클릭 도달 여부가 갈림.

## 3. 아키텍처
- **고정 작업폴더**(예: `C:\DHInfra\`): 애드인이 항상 여기로 내보내고, 템플릿 데이터소스 경로도 여기로 맞춤(1회 세팅).
- **애드인 버튼(신규 or DHINFRA 확장)**:
  1. 활성 도면 **좌표계 검사**(없으면 차단).
  2. 지표면 → **LandXML**(지표면만 필터, 5186) → 고정경로 저장.
  3. **옹벽3D.dwg** → 고정경로 저장(기존 WallDwg 로직 재사용, 실좌표 유지=원점0 회피 충족).
  4. **InfraWorks.exe** 로 템플릿 모델(사본) 열기(`Process.Start`).
  5. (M0 결과대로) 자동 반영 유도 or 새로고침 최소화.
- **.sqlite 직접 편집은 최소화**(버전 취약) — 원칙은 "파일 교체 + 열기". 경로 세팅 등 불가피한 1회성만.

## 4. 단계
- **M0 — 자동화 검증 스파이크(1순위):** 템플릿+파일교체+열기로 "열 때 자동 반영/새로고침 필요"를 실측. 내가 세팅 → JACK이 InfraWorks 2026에서 실행·확인. 0클릭 한계 확정.
- **M1 — 지형 LandXML 내보내기:** Civil3D 지표면(정지면_DH)만 LandXML로. 어떤 경우든 필요 → 병행 착수.
- **M2 — 자동 열기:** 버튼이 파일 덮어쓰기 + InfraWorks 템플릿 열기 + 자동화 최대치.
- **M3(나중) — V-World 위성사진:** 바운딩박스 → TMS 타일(줌18+) 다운로드, **5186↔웹메르카토르(3857) 좌표변환**, `.jgw` 월드파일, InfraWorks 래스터 소스로 연결. (API Key: 8EA87CD2-C75D-3407-A41C-D1FBE9B33CAA)

## 5. 리스크·주의
- 완전 0클릭은 InfraWorks 미지원 가능 → 최악 "열림 + 새로고침 1번"(그래도 큰 진전).
- 좌표계 **5186 하드코딩**(자동인식 왜곡 차단). 옹벽 DWG 실좌표 유지(이미 됨).
- LandXML은 **지표면만** 내보내 가볍게.
- 템플릿(test.sqlite)의 경로/좌표계 세팅을 고정폴더로 1회 정렬 필요.

## 6. 열린 결정
- 고정 작업폴더 경로 확정(`C:\DHInfra\` 제안).
- 버튼: DHINFRA 확장 vs 신규 "InfraWorks 열기" 버튼.
- 템플릿: test.sqlite 사본을 배포 템플릿으로 고정할지.
