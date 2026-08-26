# 민원검토 도구 (`landuse_review`)

> QGIS 플러그인 · 발주: 도화엔지니어링 도로부 · 개발: 기술개발연구원 AX팀
> 상태: **MVP (기획서 v2 정합) — gis_design_loader 동일 UI 4단계 + core 교정 완료**. 1~2단계 설계검토 교정(멀티파트 ST_Union·좌표 단일유틸·소유자명 수동·키 분리·DXF/산필지·하이브리드 hwpx) 반영. 도로부 양식(G1)·V-World 키(G2) 수령 시 무코드 전환.
> 기획·설계 문서: 사내 `gis-db-management` repo `docs/08_플러그인기획/` 참조

---

## 목적

도로 설계 중 발생하는 **민원 검토서 작성**을 자동화한다.

지번(地番) 입력 → 토지소유정보 자동 수집 → 용지경계(用地境界) 입력 → 편입면적 산출 →
편입현황 삽도(揷圖) 자동 생성 → 한글(HWPX) 검토서 초안 출력 → 설계자 확인 후 결재.

기존 5단계 수작업(V-World 다운로드 · SEE:REAL 조회 · CAD 면적 산출 · HWP 타이핑)을
플러그인 1개로 연결한다. 목표: 민원검토 1건 처리시간 90% 단축.

## UI 패턴 — 4단계 위자드 + 캔버스 중심

`gis_design_loader_v2` 위자드 패턴(다크 헤더 + 단계 표시 바 + `QStackedWidget` + 하단 네비게이션).
**결과 데이터는 모두 QGIS 캔버스에 메모리 레이어로 추가**되고, 사용자는 평소 쓰던 QGIS 도구
(레이어 패널·속성 테이블·식별 도구·측정 도구·라벨·스냅)로 자유 분석한다.
다이얼로그는 입력·액션 트리거 + 단계 흐름만 담당한다.

```
┌─ 민원검토 도구 ───────────────────┐
│ [헤더]  지번 → 소유정보 → 편입 → HWPX │
│ [ 1.지번입력 | 2.용지경계 | 3.편입 | 4.산출물 ] ← 단계 표시 바
├──────────────────────────────────────┤
│ 1. 지번 입력                          │  → 캔버스 '선택필지' (+옵션 '인접필지')
│    □ 인접필지 함께 표시  반경 50~1000m │
│    [캔버스에 '선택필지' 레이어 추가]   │
│ 2. 용지경계 (3가지)                    │  → 캔버스 '용지경계' (EPSG:5186 자동 정합)
│    ◉ 프로젝트 레이어  ○ SHP  ○ 직접그리기 │
│    [캔버스에 '용지경계' 레이어 추가]   │
│ 3. 편입면적 산출                       │  → 캔버스 '편입구역' (편입㎡·편입률·잔여㎡ 속성)
│    [산출 → '편입구역' 레이어 추가]     │
│ 4. 산출물 출력                         │
│    배경: ◉위성 ○지적배경 ○수치지형도   │
│    범위: ◉현재화면 ○전체범위           │
│    [삽도 PNG 저장]  [검토서 .hwpx 출력] │
│    ── 사업 단위 일괄 (v0.3+ 비활성) ── │
├──────────────────────────────────────┤
│ [초기화]  [이전]      1/4 지번입력  [다음] │
└──────────────────────────────────────┘

[QGIS 메인 캔버스 — 모든 결과가 여기]
  ├ (사용자 켜기) 위성/수치지형도 배경
  ├ 선택필지          (한글 alias 적용, 속성: PNU·지번·지목·면적·소유구분·소유자(마스킹))
  ├ 인접필지          (옵션, 입력 반경, 반투명)
  ├ 용지경계          (사용자 입력 폴리곤, 5186)
  └ 편입구역          (ST_Intersection 결과, 속성: 편입면적·편입률·잔여면적)
```

사용자는 QGIS 속성 테이블로 소유자(마스킹)·지목·면적 확인, 식별 도구로 필지 클릭.
4단계 [삽도 PNG]는 현재 캔버스 뷰 + 배경을 Print Layout으로 합성해 PNG 저장,
[검토서 .hwpx]는 위 결과 + 삽도 → HWPX 초안 → 한글에서 열어 결재.

## 데이터 소스

- **1차 (PROD DB, R/O `waterviewer`, port 6432, EPSG:5186)**: `lsmd_cont_ldreg`(연속지적) · `al_d160`(토지소유) · `bnd_emd_bjd`/`bnd_li_bjd`(법정 읍면동·리).
- **데이터 출처 전환**: `core/config.py`의 `DATA_SOURCE` 상수 1곳으로 DB↔API 전환(키 전 "DB" / 키 후 "API"). API 우선 시 V-World 연속지적(`LP_PA_CBND_BUBUN`) → 미스 시 DB 폴백. 세션 + 7일 SQLite 이중 캐시.
- **V-World 키**: `core/vworld_key.py`가 QSettings(설정값) 1순위 + `.env`(개발) 폴백 — **코드/zip 평문 내장 금지**(절대 룰 #4). 키는 설정창/설정값으로 나중 입력, V-World 콘솔 IP 화이트리스트 권장.
- **소유자 "이름"은 자동 불가**(V-World·공공데이터·`al_d160` 전부 미제공, 소유구분만) → `owner_collector`는 **소유구분(a8)만** 자동 수집, 이름은 '선택필지' 레이어 `owner_name`(빈칸·편집가능)에 설계자가 **속성테이블에서 직접 입력**. 마스킹은 출력 시 토글(default OFF).
- 입력 용지경계가 다른 CRS면 `core/coords.py` 단일 유틸이 `native:reprojectlayer`로 5186 변환. `.prj` 없는 파일(특히 DXF)은 좌표계 지정 단계. DWG 미지원('DXF 저장' 안내).

## 검토서 출력 — 하이브리드 (한컴오피스 불필요)

- **(A) 양식 채움**: `resources/templates/pyeonib_report.hwpx`(도로부 양식의 빈 서식)가 있으면 사내 `gis_cn` 검증 hwpx 엔진(`core/hwpx_writer.py` — `_Doc`/`fill_field`/`render_dynamic_table`/`_validate`/`save` 차용, lxml only)으로 누름틀·편입면적표를 채운다. 삽도 PNG는 템플릿의 **플레이스홀더 이미지 바이트 교체**(한글이 등록한 구조 재사용 → 견고). 빈 양식 제작법: `resources/templates/README.md`.
- **(B) 폴백**: 양식이 아직 없으면 동봉 `python-hwpx`(`_vendor/hwpx` 2.9.1)로 표·삽도를 직접 조립한 기본 초안. 지금 즉시 동작.
- 둘 다 한컴오피스 불필요(순수 Python). `__init__.py`가 `_vendor/`를 `sys.path`에 추가. `lxml`은 QGIS 번들 사용.
- 엔진 검증(2026-06-11): 차용 hwpx_writer가 예시 템플릿에서 동적 행 복제 + 자체검증 통과 + mimetype STORED 유효 hwpx 생성 확인. python-hwpx 폴백도 표+이미지 포함 유효 hwpx 생성 확인.

## 설치·테스트 (v0.2 시제품)

1. QGIS **3.40.12 LTR (Bratislava)** 설치 (사내 표준)
2. 플러그인 → ZIP에서 설치 (배포 zip: `03_DB 관리/plugins/배포/landuse_review.zip` — `pack_and_copy.py landuse_review`로 빌드)
3. 메뉴 `플러그인 → 민원검토 도구 → 민원검토서 작성` 또는 툴바 아이콘
4. 위자드 표시 확인. DB(`.env` 또는 배포 시 `db_env.py` 하드코딩 `waterviewer`)에 접속 가능해야 1단계 조회가 동작. DB 미접속/미스 시 1단계는 안내 메시지만 표시.
5. UI 레이아웃은 QGIS 환경에서만 확인 가능 (`ui/*.py`가 `qgis.PyQt`에 의존 — 순수 PyQt5 스탠드얼론 미지원).

## 데이터 흐름 요약

```
[설계자] 지번/용지경계
   │
   ▼ controller.ReviewController (ui.wizard_dialog.ReviewWizard 시그널 수신)
   ├ Step1: parcel_lookup.lookup() ──(config.DATA_SOURCE, port 6432, waterviewer R/O)──▶ lsmd_cont_ldreg(ST_Union)
   │        └(API 우선/미스)──▶ api.vworld_fallback.lookup_parcel() ──▶ V-World data API (+이중 캐시)
   │        owner_collector.collect() ──▶ al_d160 소유구분(a8) + 대장면적(a22=공부면적) / 소유자명은 수동
   │        parcel_lookup.add_to_canvas() ──▶ '선택필지'/'인접필지' 메모리 레이어
   ├ Step2: boundary_input.prepare()/load_from_file(SHP·DXF)/start_drawing() ──▶ coords(5186) ──▶ '용지경계'
   ├ Step3: area_calculator.calculate_to_canvas() ──▶ QGIS 클라이언트 교차(DB 재조회 없음) ──▶ '편입구역'
   └ Step4: inset_renderer.render_to_png() ──▶ 삽도 PNG (Print Layout)
            report_exporter.export() ──▶ 하이브리드(양식 채움 / python-hwpx) ──▶ 용지편입검토서 초안 .hwpx
   ▼
[설계자가 한글에서 열어 검수 → 결재]   ※ 자동 출력 ≠ 자동 결재
```

## 디렉터리 구조

```
landuse_review/
├── __init__.py              classFactory + _vendor sys.path 부트스트랩
├── metadata.txt             name=민원검토 도구 / version=1.0.0 / qgisMinimumVersion=3.40.12 / experimental=True
├── plugin.py                QGIS 진입점 (QAction → ReviewController)
├── db_env.py                waterviewer R/O 하드코딩 (DB_ENV 마커) — 배포 시 pack_and_copy가 교체
├── core/
│   ├── controller.py        워크플로 오케스트레이터 — 위자드 시그널 → core 모듈 → 캔버스 레이어
│   ├── config.py           데이터 출처 상수(DATA_SOURCE: DB↔API 1곳 전환)
│   ├── vworld_key.py        V-World 키 설정값 분리(QSettings + .env 폴백, 평문 금지)
│   ├── coords.py            좌표 변환 단일 유틸(5186 정렬) + 편입0% 무성오답 sanity
│   ├── parcel_lookup.py     M1 지번(자연어/PNU/산필지) → lsmd_cont_ldreg(ST_Union, +API) → '선택필지'(owner_name 빈칸)
│   ├── owner_collector.py   M2 al_d160 소유구분(a8)만 자동 — 소유자명은 수동(미제공)
│   ├── boundary_input.py    M3 용지경계 3가지(레이어/파일 SHP·DXF/그리기) + 좌표계 지정 + 5186
│   ├── area_calculator.py   M4 편입면적 산출(QGIS 클라이언트 교차, DB 무접속) → '편입구역'
│   ├── layers.py            캔버스 레이어 소유 마커 — 플러그인 생성분만 교체·삭제(사용자 동명 레이어 보호)
│   ├── inset_renderer.py    M5a Print Layout → 삽도 PNG (V-World 키 설정값 연동)
│   ├── hwpx_writer.py       gis_cn 차용 hwpx 엔진(lxml only) + 삽도 PNG 플레이스홀더 교체
│   ├── hwpx_report.py       PyeonibRow 도메인 + 누름틀/표 빌더
│   └── report_exporter.py   M5b 하이브리드(템플릿 채움 / python-hwpx 폴백)
├── ui/
│   ├── wizard_dialog.py     메인 위자드 (4단계 QStackedWidget, gis_design_loader_v2 패턴)
│   ├── step1_parcel.py      1단계 지번 입력
│   ├── step2_boundary.py    2단계 용지경계 입력 (레이어/SHP/그리기)
│   ├── step3_intersect.py   3단계 편입면적 산출
│   ├── step4_export.py      4단계 산출물 출력 (삽도 PNG + HWPX, 사업 단위 일괄은 v0.3+ 비활성)
│   └── styles.py            공통 스타일시트 (사내 8종 플러그인 디자인 시스템)
├── db/queries.py            표준 SQL (PROD public 읽기 전용, port 6432)
├── api/vworld_fallback.py   V-World 보강 + 세션·7일 SQLite 이중 캐시 (stdlib만)
├── resources/icons/icon.png 툴바 아이콘 (placeholder)
├── resources/templates/     HWPX 검토서 템플릿 — 도로부 양식 입수 후 추가 (현재 README만)
└── _vendor/hwpx             python-hwpx 2.9.1 (lxml 제외)
```

## 남은 작업 (MVP → 시범)

- **G1 도로부 양식 입수 → `resources/templates/pyeonib_report.hwpx` 빈 서식 제작** (제작법: `resources/templates/README.md`). 있으면 자동으로 양식 채움 경로로 전환(코드 무변경).
- **G2 V-World 키 발급 → 설정값 입력 + `core/config.py` `DATA_SOURCE="API"` 1줄 전환** + `attrFilter`·응답 도형타입 실호출 확정(§7).
- **PROD 중복 실측**(SELECT only): `COUNT(*)`/`COUNT(DISTINCT pnu)`/`COUNT(DISTINCT ufid)` → dedup 전략 확정.
- QGIS 3.40.12 실환경 통합 테스트 (1~4단계 + DB 접속 + 삽도 PNG + HWPX 한컴 열기) — 검증 기준 대전리 645-1 편입 58.1%.
- 편입면적 정확도 검증 (sample 10필지 AutoCAD 수기 비교).
- 캔버스 레이어 QML 스타일(선택필지/용지경계/편입구역) 추가 (현재 한글 alias만).
- 다필지 UI 노출(현재 백엔드 `pnus:list`·`pnu=ANY` 구현, jibun 입력은 단필지) + 사업 단위 용지조서 풀버전(후속).

## 배포

- 배포 표준: `version=1.0.0` / `author=도화엔지니어링 기술개발연구원` / `qgisMinimumVersion=3.40.12` / `waterviewer` R/O 하드코딩.
- 빌드: `03_DB 관리/scripts/pack_and_copy.py landuse_review [--commit --push]` (PLUGINS 레지스트리에 등록됨).
- v1.0 DIDAS 공식 등재 조건: ① 베타 만족도 ≥4.0/5 ② 면적 정확도 ±1% ③ 도로부 검토서 양식 입수 완료. 승격 시 기획서를 `03_DB 관리/docs/06_DIDAS/`로 이관.
