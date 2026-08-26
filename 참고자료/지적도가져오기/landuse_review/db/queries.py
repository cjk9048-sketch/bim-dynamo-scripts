# -*- coding: utf-8 -*-
"""표준 SQL — PROD public 읽기 전용 (절대 룰 #1 DB 쓰기 0건).

플러그인 쿼리는 port 6432(PgBouncer)를 기본으로 한다. 단일 지번/소수 필지 조회는
가벼운 쿼리라 6432. psycopg2 연결 시 sslmode='require'. PgBouncer 는 prepared
statement 를 제한하므로 prepared 의존 기능을 피한다.

DB 는 **API 키 없을 때의 폴백 경로**다(기본은 V-World API). 실제 `lsmd_cont_ldreg`
컬럼 = gid, sgg_oid, **jibun**, bchk, **pnu**, col_adm_se, **geom** — **`jimok` 컬럼은 없다**.
지목은 `jibun` 끝에 붙어 온다(예: '645-1전' = 지번 645-1 + 지목 전) → Python 에서 분리.

⚠ 멀티파트 필지(하천·섬으로 갈린 한 필지)는 `ST_Union(geom) GROUP BY pnu` 로 보존 합산
  (`DISTINCT ON (pnu)` 금지 — 한 조각만 남겨 면적 과소 계산, 1~2단계 설계검토 §2-①).

⚠ 편입면적(3단계)은 DB 를 다시 치지 않는다 — 1단계에서 취득한 필지 도형(API 우선)을
  그대로 클라이언트(QGIS)에서 사업경계와 교차한다(`area_calculator`). 그래야 분석이
  데이터 출처(API/DB)와 무관하게 일관된다.

⚠ **`lsmd_cont_ldreg` 에는 `pnu` 인덱스가 없다** (2026-07-10 실측: 인덱스는 `gid` PK 와
  `geom` GiST 둘뿐, 7,780만 행 / 32GB). `WHERE pnu = ...` 는 `Parallel Seq Scan` 이라
  분 단위가 걸린다. 그래서 필지 조회는 **PNU 에서 법정경계 코드를 역산해
  `geom && (그 경계의 도형)` 으로 GiST 를 먼저 태운 뒤** pnu 를 건다(0.3초).
  DB 는 읽기 전용(절대 룰 #1)이라 인덱스 추가로는 못 고친다 — 쿼리로 우회한다.

⚠ 공부면적(편입률 분모)은 이 테이블의 `ST_Area(geom)` 가 **아니다**. 연속지적도는
  도엽을 이어 붙인 도면이라 토지대장 면적과 어긋난다(645-1 실측: 도형 4,670.7 vs
  대장 4,669.5). 공식 검토서가 쓰는 값은 **`al_d160.a22`(대장면적)** 다 → `OWNERS_BY_PNU`.
"""
import psycopg2

from ..db_env import DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD, DB_SCHEMA, DB_SRID  # noqa


def connect():
    """psycopg2 연결 생성 (port 6432, sslmode='require', autocommit).

    ⚠ PgBouncer(6432)는 `options=-c statement_timeout` 시작 파라미터를 거부한다
      (FATAL: unsupported startup parameter). 서버측 타임아웃은 무거운 쿼리에서
      트랜잭션 내 `SET LOCAL statement_timeout`(transaction pooling 호환)으로 건다 —
      `set_statement_timeout()` 참조.
    """
    conn = psycopg2.connect(
        host=DB_HOST,
        port=int(DB_PORT),
        dbname=DB_NAME,
        user=DB_USER,
        password=DB_PASSWORD,
        sslmode="require",
        connect_timeout=10,
    )
    conn.autocommit = True
    return conn


# 지번 텍스트 → 읍면동 코드 **정확 일치** 검색.
# ⚠ 옛 `LIKE %name%` + 정렬 없는 rows[0] 은 동명 지역에서 조용히 다른 시군구를 골랐다
#   (예: '대전리' 전국 17곳). 호출측(`parcel_lookup._resolve_prefix`)이 여러 건이면
#   사용자에게 되묻는다 — 임의로 하나 고르지 않는다.
EMD_BY_NAME = """
SELECT emd_cd, emd_nm
FROM {schema}.bnd_emd_bjd
WHERE emd_nm = %s
ORDER BY emd_cd
"""

# 지번 텍스트 → 법정리 코드 **정확 일치** 검색.
# ⚠ 실제 컬럼은 `ri_cd`/`ri_nm` 이다(`li_cd`/`li_nm` 아님 — V-World 원본 SHP 컬럼명).
#   옛 쿼리는 존재하지 않는 컬럼을 조회해 DB 폴백 경로 전체가 UndefinedColumn 으로 죽었다.
LI_BY_NAME = """
SELECT ri_cd, ri_nm
FROM {schema}.bnd_li_bjd
WHERE ri_nm = %s
ORDER BY ri_cd
"""

# ── 필지 조회 (pnu 인덱스 부재 우회) ────────────────────────────────────────
# 법정경계(리 10자리 / 읍면동 8자리) 도형의 bbox 로 GiST 를 먼저 태운 뒤 pnu 를 건다.
# `l.geom && a.geom` 은 bbox 교차라, 그 경계에 속한 필지의 **모든 조각**이 통과한다
# (조각 탈락 → 면적 과소계산 없음). `DISTINCT ON (pnu)` 금지 — `ST_Union GROUP BY pnu`.
# 법정경계 존재 확인 — 가속 쿼리가 0행을 냈을 때 "필지가 없다" 인지
# "경계 자체가 없어 못 찾았다" 인지 구분한다. 이걸 안 하면 존재하지 않는 후보 PNU
# 하나 때문에 매번 전수 스캔(수십 초)으로 떨어진다.
RI_EXISTS = "SELECT 1 FROM {schema}.bnd_li_bjd WHERE ri_cd = %s LIMIT 1"
EMD_EXISTS = "SELECT 1 FROM {schema}.bnd_emd_bjd WHERE emd_cd = %s LIMIT 1"

# 리(里) 소속 필지 — PNU[0:10] = ri_cd
PARCEL_BY_PNU_IN_RI = """
WITH area AS (
    SELECT ST_Union(geom) AS geom FROM {schema}.bnd_li_bjd WHERE ri_cd = %s
)
SELECT l.pnu,
       max(l.jibun) AS jibun,
       ST_Area(ST_Force2D(ST_MakeValid(ST_Union(l.geom))))::double precision AS area_sqm,
       ST_AsText(ST_Multi(ST_Force2D(ST_MakeValid(ST_Union(l.geom))))) AS geom_wkt
FROM {schema}.lsmd_cont_ldreg l, area a
WHERE l.geom && a.geom
  AND l.pnu = ANY(%s)
GROUP BY l.pnu
"""

# 읍면동 소속(도시 동 등, PNU[8:10]='00') 필지 — PNU[0:8] = emd_cd
PARCEL_BY_PNU_IN_EMD = """
WITH area AS (
    SELECT ST_Union(geom) AS geom FROM {schema}.bnd_emd_bjd WHERE emd_cd = %s
)
SELECT l.pnu,
       max(l.jibun) AS jibun,
       ST_Area(ST_Force2D(ST_MakeValid(ST_Union(l.geom))))::double precision AS area_sqm,
       ST_AsText(ST_Multi(ST_Force2D(ST_MakeValid(ST_Union(l.geom))))) AS geom_wkt
FROM {schema}.lsmd_cont_ldreg l, area a
WHERE l.geom && a.geom
  AND l.pnu = ANY(%s)
GROUP BY l.pnu
"""

# 최후 폴백 — 법정경계에서 못 찾을 때만. **전수 스캔이라 느리다**(호출측이 타임아웃을 건다).
PARCEL_BY_PNU = """
SELECT pnu,
       max(jibun) AS jibun,
       ST_Area(ST_Force2D(ST_MakeValid(ST_Union(geom))))::double precision AS area_sqm,
       ST_AsText(ST_Multi(ST_Force2D(ST_MakeValid(ST_Union(geom))))) AS geom_wkt
FROM {schema}.lsmd_cont_ldreg
WHERE pnu = ANY(%s)
GROUP BY pnu
"""

# 반경 N미터 인접 필지의 **PNU 목록만** — 대상 도형(WKT, 5186) 기준 ST_DWithin.
# ⚠ ST_DWithin(n.geom, <리터럴 도형>, d) 는 GiST 공간 인덱스 가속(bbox && 후 정밀) →
#   대상 부근만 스캔(빠름). 옛 `ST_Buffer + 전체 cross join` 은 인덱스 미사용 →
#   lsmd_cont_ldreg(7,780만 행) 풀스캔 = UI 멈춤(freeze). 그래서 폐기.
# ⚠ 여기서 **도형·면적을 함께 집계하면 안 된다**. WHERE 가 행을 먼저 거르므로 멀티파트
#   필지(하천·섬으로 갈린 한 필지)의 반경 밖 조각이 탈락해 그 필지 면적이 과소 계산된다.
#   PNU 만 받아서 `PARCEL_BY_PNU_IN_*` 로 **전체 도형을 다시 가져온다**.
NEIGHBOR_PNUS_BY_GEOM = """
WITH target AS (
    SELECT ST_GeomFromText(%s, %s) AS geom
)
SELECT n.pnu,
       min(ST_Distance(n.geom, target.geom))::double precision AS distance_m
FROM {schema}.lsmd_cont_ldreg n, target
WHERE n.pnu IS NOT NULL
  AND n.pnu <> ALL(%s)
  AND ST_DWithin(n.geom, target.geom, %s)
GROUP BY n.pnu
ORDER BY distance_m ASC
LIMIT %s
"""

# Canvas bbox cadastral background for inset maps. Keep this read-only and bbox-bound;
# do not use DISTINCT ON(pnu), because duplicated/metapolygon parcels must be preserved.
CADASTRAL_BY_BBOX = """
WITH bbox AS (
    SELECT ST_MakeEnvelope(%s, %s, %s, %s, %s) AS geom
)
SELECT l.pnu,
       max(l.jibun) AS jibun,
       ST_AsText(ST_Multi(ST_Force2D(ST_MakeValid(ST_Union(l.geom))))) AS geom_wkt
FROM {schema}.lsmd_cont_ldreg l, bbox b
WHERE l.pnu IS NOT NULL
  AND l.geom && b.geom
  AND ST_Intersects(l.geom, b.geom)
GROUP BY l.pnu
LIMIT %s
"""

# PNU 목록에 대한 토지소유 구분 + **대장면적** — al_d160 (a0=PNU, a8=소유구분명, a22=면적).
# v2 §6.2: 소유구분만 자동, 소유자 성명은 미제공. al_d160 은 a-컬럼 보유.
# ⚠ `a22` 가 검토서의 **공부면적**이다(645-1=4669.5 / 645-6=561.3, 문서 B 와 정확히 일치).
#   편입률 분모로 이 값을 써야 한다 — `ST_Area(lsmd_cont_ldreg.geom)` 는 도면 면적이라 다르다.
#   (a0 에는 `idx_al_d160_pnu` 인덱스가 있어 이 조회는 빠르다.)
OWNERS_BY_PNU = """
SELECT a0 AS pnu, a8 AS owner_type, a22 AS area_sqm
FROM {schema}.al_d160
WHERE a0 = ANY(%s)
"""
