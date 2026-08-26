# gis_design_loader 변경 이력

> **배포 버전 라인**: `1.0.0`(초기 릴리즈) → `1.1.0`(행정경계 법정 읍면동·리 추가, 2026-05-12) → `1.2.0`(작업 범위 밖 데이터 누수 근본 수정 + 클립 아키텍처 개선, 2026-05-13).
> 아래 `[1.3.0]`~`[1.5.1]` 항목은 2026-04-18 개발 중 임시로 붙였던 내부 번호로, 배포본 `metadata.txt::version=`은 줄곧 `1.0.0`이었음(`qgis_plugin_axteam` commit `1bc0121`에서 1.0.0으로 재고정). 기능 이력 참고용으로만 남겨둠.

## [1.2.0] - 2026-05-13
- **작업 범위 밖 데이터 누수 근본 수정 — 전 레이어 서버측 범위 필터로 일반화** (3단계 서버 로드)
  - **증상**: 작은 행정구역(예 영통구)을 작업 범위로 잡으면 인접 시군구(화성·용인)의 산업단지(`단지경계`·`단지시설용지`·`단지용도지역`)나 개발사업 폴리곤이 도면에 통째로 박힘.
  - **원인 사슬**: `detect_emd_codes(범위 *bbox*)` 가 bbox 에 모서리만 걸치는 인접 행정동까지 다 수집(over-capture) → 그 코드들로 `complex_*_clip(코드)` 를 호출 → 함수는 산업단지를 *그 동 폴리곤* 으로만 잘라줌(=범위 밖) → 클라이언트 `native:clip(범위 폴리곤)` 으로 0건 → fallback `_may_keep_raw_on_clip_fail` 이 "서버측 `*_clip` 함수라 경계 안"이라며 그 0건의 *원본*(범위 밖 산업단지)을 그대로 추가. v1.1.0 의 `query_type="spatial"` 누수 픽스는 raw 테이블 레이어만 다뤘고 `function` 레이어가 예외였음 — 그 예외가 곧 이 버그.
  - **수정**: `core/layer_loader.py` — `build_union_uri`/`build_function_uri` 가 작업 범위 폴리곤 WKT(EPSG:5186)를 받아, `table`/`spatial`/`function` 3종 모두 쿼리 레이어로 `(SELECT ROW_NUMBER() OVER() AS _uid, t.* FROM <소스> AS t, (SELECT ST_MakeValid(ST_Force2D(ST_GeomFromText('<범위WKT>',5186))) AS g) rng WHERE t.<geom> && rng.g AND ST_Intersects(t.<geom>, rng.g))` 형태로 반환 → **DB 에서 범위 폴리곤과 실제로 겹치는 feature 만** 가져옴. over-capture 된 인접 동에서 나온 산업단지는 `ST_Intersects` 에서 전부 제거됨. function 타입의 `<소스>` = 감지 코드별 함수 호출 UNION ALL, spatial/table 타입의 `<소스>` = 원본 테이블(+기존 `sql_extra_filter`). 도형 잘라내기 자체는 기존대로 클라이언트 `native:clip` 이 수행하되, 무관한 feature 가 이미 걸러진 뒤라 clip 이 0건/실패해도 범위 밖 데이터가 노출되지 않음.
  - `boundary_geometry_to_db_wkt()` 헬퍼 신설 — 작업 범위 레이어의 도형을 합쳐 5186 WKT 로 변환(레이어 무효/도형 없음/feature 200개 초과/WKT 8MB 초과 시 None → 기존 bbox 동작으로 안전 폴백). `transform_extent_to_db()` 가 프로젝트 CRS 가 아니라 *작업 범위 레이어의 CRS* 를 쓰도록 정정(범위 레이어가 5186 메모리 레이어이고 프로젝트가 5179 인 경우의 잠재 버그).
  - `detect_emd_codes(extent, clip_geom_wkt=...)` — 범위 WKT 가 있으면 bbox 가 아니라 실제 범위 폴리곤과 `ST_Intersects` 하는 읍면동만 감지 → over-capture 감소(정확성엔 영향 없고 함수 호출 수만 줄어듦). `ui/step2_region_boundary.py`(다각형 그리기·기존 레이어 모드)에도 동일 적용.
  - `core/preprocessor.py` — `BatchPreprocessTask`/`enrich_river_boundary` 가 범위 WKT 를 전달받아 실폭하천 enrich 시 하천중심선도 같은 서버측 필터로 로드. `_may_keep_raw_on_clip_fail` docstring 갱신(이제 raw 는 항상 범위와 겹치는 feature 들이라 clip 실패 시 유지해도 누수 아님).
  - **부수효과 — 빠른 로드**: 건물통합정보(14.4M)·토지소유정보(39.8M) 등 대용량 레이어를 통째로 가져와 클라이언트에서 자르던 것을, 이제 서버가 GiST 인덱스로 미리 추려서 보냄.
  - DB 무변경 (함수 시그니처·GeoServer SQL View·civil_planner 무영향). 범위 WKT 계산이 불가능한 경우(매우 큰/복잡한 사용자 지정 범위 레이어 등)에는 기존 bbox 기반 동작으로 폴백.
  - 잔여(이론적·드묾): 범위 *경계선* 을 따라 인접 동과 정확히 맞닿은 feature 는 `ST_Intersects` 가 TRUE 라 들어올 수 있고, 그 feature 의 `native:clip` 이 0건이 되면 통째로 유지될 수 있음. 별건 후속(검토): `*_clip` DB 함수 14종이 코드 대신 범위 geometry 를 받게 리팩터(DDL 페어리뷰 + civil_planner·GeoServer 뷰 동반 수정).
- 베타 사용자: 기존 `GIS Design Loader v2 (BETA)` 는 제거 후 새 `GIS Design Loader` v1.2.0(또는 Water) 설치 — 플러그인 ID 가 달라 자동 업그레이드되지 않음.

## [1.1.0] - 2026-05-12
- **행정경계 이원화 — 법정 읍면동·리 분리** (옛 `gis_design_loader_v2` BETA 분기를 메인 트리로 흡수)
  - `core/layer_loader.py`:
    - `행정경계_법정리`: 단일 소스 전환 — `sgis_hjd`(LENGTH(adm_cd)=10, 0건 적재) → 신규 `bnd_li_bjd`(16,643행)
    - `행정경계_법정읍면동`: 신규 등록 — `bnd_emd_bjd`(5,536행)
    - 두 레이어 `query_type="spatial"` — 법정 코드 ≠ 행정 코드 체계라 LIKE 필터 부정확.
      사용자가 고른 행정구역 polygon과 `ST_Intersects` 하는 법정 읍면동/리만 반환
    - 표시필드 alias: `행정경계_법정리`={ri_cd:법정리코드, ri_nm:법정리명}, `행정경계_법정읍면동`={emd_cd:법정읍면동코드, emd_nm:법정읍면동명}
  - `styles/qml/행정경계_법정읍면동.qml` 신규 추가 — `행정경계_읍면동.qml`과 byte 단위 동일한 스타일. `행정경계_법정리.qml`은 별도 스타일(황금색 계열) 유지, 라벨 fieldName만 소폭 정정
  - `core/style_manager.py`: `LAYER_QML_MAP`에 `"행정경계_법정읍면동": "행정경계_법정읍면동"` 추가 — 누락 시 QML 미적용 → QGIS 기본 랜덤색(분홍 등)으로 그려지던 문제 수정
  - 배포: 일반판(`gis_design_loader.zip`)·물산업판(`gis_design_loader_water.zip`) 양쪽에 자동 반영
    (행정경계 변경은 LAYER_REGISTRY/QML/style_manager 영역 → `INCLUDE_PLAN_FACILITY_STEP` feature flag와 무관)
  - 상세 설계: `docs/02_DB설계/행정경계_이원화_설계.md` (Phase 5)
- **3단계 서버 로드: 이미 있는 레이어 중복 적재 방지 버그 수정**
  - 원인: 로드 시 벡터 레이어가 `"{이름}_clip"` 으로 명명되는데(예 `행정경계_시군구_clip`), 중복 감지·제거 로직은 `_clip` 없는 원본 이름으로만 비교 → `_clip` 레이어가 다시 적재되어 두 배로 쌓임. 래스터(DEM)는 `_clip` 안 붙어서 이 문제 없었음
  - `core/preprocessor.py`: `base_managed_layer_name()` 헬퍼 추가(말미 `_clip` 정규화). `_remove_existing_layers_by_name()`이 `_clip` 변형까지 제거 — 재로드 시 옛 사본(2개 이상도) 모두 정리
  - `ui/step3_load_data.py`: `_get_loaded_layer_names()`가 `_clip` 떼어낸 base 이름도 포함 → "행정경계_시군구" 체크박스가 "행정경계_시군구_clip" 존재를 인식해 자동 비활성화. 중복(2개 이상) 레이어는 체크박스/안내문에 ⚠ 표시하고 목록 안내 ("이미 프로젝트에 있어 가져오지 않는 레이어 N개: …", "같은 레이어가 2개 이상 들어가 있음: …")
  - 주의: 이미 프로젝트에 쌓여 있던 중복 사본은 자동 삭제하지 않음 — 안내문 보고 레이어 패널에서 직접 정리. 이후 재로드 시에는 더 쌓이지 않음
- **개발사업 레이어가 작업 범위 밖 데이터까지 가져오던 버그 수정**
  - 원인: `개발제한구역`·`개발진흥지구`·`도시개발구역`·`택지개발`·`혁신도시`는 `query_type="spatial"` 로 원본 테이블에 `ST_Intersects(geom, bbox)` 필터만 걸어 가져옴(= 범위 bbox에 걸치는 *통/시군구 폴리곤 전체*) → 클라이언트 `native:clip` 으로 잘라야 하는데, 그 clip 이 0건/실패하면 **자르지 않은 DB 원본을 그대로 추가**하던 fallback 때문에 다른 시군구 폴리곤이 통째로 들어옴. (`*_clip` DB 함수 레이어는 서버측 ST_Intersection 이라 무관 — `단지경계`·`단지시설용지`·`단지용도지역` 등)
  - `core/preprocessor.py`: 개발사업 그룹의 raw 테이블 쿼리 레이어는 clip 0건/실패 시 원본을 쓰지 않고 **제외**(`_may_keep_raw_on_clip_fail()`). 서버측 `*_clip` 함수 레이어는 기존대로 원본 유지. clip 이 정상 동작하면 당연히 경계내로 잘려서 들어옴
- 베타 사용자: 기존 `GIS Design Loader v2 (BETA)` 플러그인은 제거하고 새 `GIS Design Loader` v1.1.0(또는 Water) 설치 — 플러그인 ID가 달라 자동 업그레이드되지 않음

## [1.5.1] - 2026-04-18
- **lite / water 독립 설치 지원** — 두 변형이 같은 QGIS에 동시에 설치 가능
  - `plugin.py`가 `metadata.txt::name=` 값에서 menu·toolbar·objectName을 자동 derive
    → 변형별로 UI 식별자가 달라져 Qt saveState 충돌 없음
  - `pack_and_copy.py` 가 빌드 시 변형별로:
    - 다른 `wrap_folder` (`gis_design_loader` vs `gis_design_loader_water`)
    - `metadata.txt::name`·`description=` 치환 (`_apply_metadata_overrides`)
  - 이전엔 두 변형이 같은 폴더·이름이라 하나를 설치하면 다른 하나가 덮어씌워짐

## [1.5.0] - 2026-04-18
- **2-변형 배포 지원** — 하나의 소스트리로 두 종류 zip 생성
  - `gis_design_loader.zip` (일반) — **5번 계획시설 단계 제외** (조판생성이 5번으로 이동)
  - `gis_design_loader_water.zip` (풀 기능) — 모든 단계 포함 (물 관련 계획시설 포함)
  - `core/feature_flags.py::INCLUDE_PLAN_FACILITY_STEP` 토글
  - `scripts/pack_and_copy.py`가 빌드 시 이 파일 내용을 오버라이드해서 zip에 기록
    → 개발 소스트리는 건드리지 않음, 각 zip이 자기 flag 값을 가짐
- `ui/wizard_dialog.py`: feature flag에 따라 `Step7FacilityLayer` 페이지/`"계획시설"` 타이틀
  조건부 추가

## [1.4.6] - 2026-04-18
- Step1 레이어 생성 방식 카드 재배치
  - 이전: 영구 레이어 → **임시 레이어** → 저장 폴더 → 저장 형식
  - 현재: 영구 레이어 → 저장 폴더 → 저장 형식 → **임시 레이어**
  - 영구/저장 옵션이 시각적으로 묶이고, 임시 모드는 맨 아래에 배치
- Step7 조판 생성 시 범례(Legend)에서 **배경_지형 그룹 제외**
  - DEM/VWorld/등고선 등 바탕 레이어는 범례에 표시 안 됨
  - `legend.setAutoUpdateModel(False)` + `model.rootGroup().removeChildNode()` 방식

## [1.4.5] - 2026-04-18
- 라벨 fieldName 일괄 audit 및 rename 결과와 일치시킴
  - `단지경계.qml`: `단지명칭` → `단지명` (FIELD_ALIASES.dan_name=단지명과 일치, 7곳 치환)
  - `개발진흥지구.qml`: `ALIAS` → `별칭` (column_aliases.json.lsmd_cont_uq129.alias=별칭)
  - `도시개발구역.qml`: `ALIAS` → `별칭` (lsmd_cont_ud901)
  - `혁신도시.qml`: `ALIAS` → `별칭` (lsmd_cont_ub811)
  - `택지개발.qml`: `REMARK` → `비고` (lsmd_cont_ud301.remark=비고)
- 원인: QML이 rename 이전의 원본 영문/구 한글 필드명으로 라벨 fieldName을 참조해
  rename 후 필드 조회 실패 → 라벨 렌더링 안 됨

## [1.4.4] - 2026-04-18
- 행정경계 라벨 fieldName을 JSON 매핑과 일치시킴
  - `column_aliases.json::sgis_hjd.adm_nm` → **"행정동명"** (모든 레벨 공통)
  - 1.4.2에서 `"시도명"/"시군구명"/"읍면동명"`으로 개별 설정했지만, 실제 rename은
    JSON 매핑이 우선이라 실 필드명은 "행정동명"으로 통일돼 있어 라벨 매칭 실패
  - 4개 행정경계 QML 모두 `fieldName="행정동명"`으로 통일

## [1.4.3] - 2026-04-18
- **행정경계_법정리 레이어 일시 제거** — `sgis_hjd` 테이블에 LENGTH(adm_cd)=10
  데이터가 적재되지 않은 상태라 로드해도 0건 반환 → Step3 UI 체크박스와 그룹에서
  제외하여 사용자 혼란 방지
  - `core/layer_loader.py::AVAILABLE_LAYERS`에서 해당 엔트리 주석 처리
  - QML / FIELD_ALIASES / style_manager 매핑은 유지 (추후 데이터 들어오면 되살림)

## [1.4.2] - 2026-04-18
- **행정경계 레이어 라벨 복원** — v1.3.0 도입된 한글 rename 이후 QML labeling이
  참조하던 `fieldName="adm_nm"` 필드가 사라져 라벨이 표시되지 않던 문제 해결
  - `행정경계_시도.qml`: `fieldName="adm_nm"` → `"시도명"`
  - `행정경계_시군구.qml`: → `"시군구명"`
  - `행정경계_읍면동.qml`: → `"읍면동명"`
  - `행정경계_법정리.qml`: → `"법정리명"` (DB에 데이터가 적재되면 작동, 현재는 미적재)
- 주의: 행정경계_법정리는 **`sgis_hjd` 테이블에 데이터 없음** (LENGTH(adm_cd)=10 미적재).
  SGIS 원천 데이터 확보 또는 별도 ETL 필요 — DB 재적재 전까지는 0건 반환.

## [1.4.1] - 2026-04-18
- **중복 레이어 생성 방지** — Step3에서 동일 이름 레이어를 다시 로드해도 두 배로
  쌓이지 않도록 수정
  - `core/preprocessor.py`에 `_remove_existing_layers_by_name()` 모듈 함수 추가
  - `finished()`의 `addMapLayer` 직전에 호출하여 동일 이름 기존 레이어를 먼저 제거
  - no-boundary 경로(`ui/step3_load_data.py`)도 동일하게 적용
  - `AVAILABLE_LAYERS` 등록 이름만 제거 대상 → 사용자가 관리하는 "작업범위" 등은 보존
- 클라이언트 clip 실패 로그 톤 조정
  - "[경고] clip 실패로 원본 유지" → "[알림] 클라이언트 clip 건너뜀 → DB 반환본 사용"
    (DB 함수 v2 적용 후에는 DB 반환본이 이미 경계내 clip 상태이므로 경고 수준 아님)
  - 도형 수리 단계 실패 시 별도 로그 (`재clip 포기 — 도형 수리 단계 실패`)
  - 재clip이 0건일 경우 별도 로그 (`재clip도 0건 반환 — DB 반환본을 사용`)

## [1.4.0] - 2026-04-18
- 행정구역 범위 로드 시 경계 밖 데이터(호수/저수지 등)가 보이던 문제 개선
  - 1차 `native:clip` 실패 시 `ST_MakeValid` 기반 도형 수리를 자동 수행한 뒤 재clip 시도
  - 2차도 실패할 때만 원본 유지 (DB 함수 v2 배포 후에는 원본도 이미 서버측 clip됨)
  - 로그 메시지 태그 강화(`[경고]`) — `보기 → 패널 → 로그 메시지 → GISDesignLoader`에서
    "clip 실패로 원본 유지" 항목을 사용자가 확인 가능
- 관련 DB 변경 (`scripts/sql/create_functions_5186.sql`, 별도 배포):
  - 14개 `*_clip`/`*_filter` 함수가 기존 `ST_Intersects`(겹침 선택만)에서
    `ST_Intersection(ST_MakeValid(r.geom), b.geometry)`로 변경되어
    **서버측에서 실제 clip** 이 수행됨 → 경계 밖 geometry 원천 제거

## [1.3.0] - 2026-04-18
- 토지소유정보의 **모든** 컬럼명을 한글로 전환 (`a0`, `a8` 포함)
  - `styles/qml/토지소유정보.qml` 편집:
    - `<renderer-v2 ... attr="a8">` → `attr="소유구분명"` (categorized renderer)
    - `COALESCE("a0", …)` → `COALESCE("고유번호", …)` (previewExpression + customproperty)
  - `PRESERVE_FIELD_NAMES`에서 토지소유정보 제거 → `gid→GID`, `a0→고유번호`,
    `a1~a7, a9~a24, a8→소유구분명` 모두 실제 필드 이름으로 rename
  - QML이 rename 이후 재적용(`_auto_group_and_style`)되므로 categorized 색상 분류 유지

## [1.2.0] - 2026-04-18
- 속성 테이블의 실제 필드명을 한글로 변경 (alias → rename)
  - 기존: `setFieldAlias()`만 호출 → QGIS의 "Show field names" 설정/QML <aliases>
    덮어쓰기/GPKG·SHP 재로드 등으로 `a0, a1, a2…`가 그대로 보이는 현상
  - 변경: memory/ogr provider에 한해 `provider.renameAttributes()`로 **실제 필드명을
    한글로 rename**. postgres 원본 레이어는 안전상 기존 alias 방식 유지
  - QML 스타일/expression이 참조하는 원본 컬럼은 `PRESERVE_FIELD_NAMES`에 등록해
    원본명을 보존 + alias로만 한글 표시 → 스타일/Preview가 깨지지 않음
  - 토지소유정보: `a0` (previewExpression), `a8` (categorized renderer) 원본 유지,
    나머지 23개 컬럼은 실제 필드명을 한글로 rename
- `_reapply_korean_aliases()` 재호출에도 idempotent 하게 동작 (중복 rename 방지)

## [1.0.0] - 2026-03-16
- 초기 릴리즈
- 6단계 위자드 기반 토목 관로 설계 워크플로우
  - 1단계: 프로젝트 CRS 설정 (EPSG:5186 권장)
  - 2단계: 작업 범위 폴리곤 생성 (드래그 또는 기존 레이어)
  - 3단계: DB 레이어 로드 + 범위 클리핑 + 도형수정
  - 4단계: 레이어 그룹화 + 상하수도 스타일 자동 적용
  - 5단계: 지장물 Shapefile 로드 + 전처리
  - 6단계: 관로 LineString 레이어 생성 + 편집 + 스냅 설정
- gis_layer_loader 16종 레이어 연동
- qgis_layer_style_library.xml (00_상하수도) 스타일 자동 매칭
- .env 기반 DB 접속 관리 (pack_plugin.py 패키징 시 하드코딩 교체)
