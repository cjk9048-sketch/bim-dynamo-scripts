# -*- coding: utf-8 -*-
"""
레이어 로더 - PostGIS DB에서 작업 범위 폴리곤 기반으로 레이어를 로드

동작 방식:
1. 작업 범위 폴리곤을 DB CRS(5186) WKT로 변환 (build_*_uri 의 서버측 필터용)
2. sgis_hjd 에서 범위 폴리곤과 ST_Intersects 하는 읍면동 코드 감지
   (DB 함수 호출에 필요 — 함수가 행정구역 코드 인자를 받으므로)
3. DB 함수/테이블 쿼리 시 서버측에서 `WHERE geom && rng AND ST_Intersects(geom, rng)` 로
   *범위 폴리곤과 실제로 겹치는 feature 만* 가져옴 → 작업 범위 밖(범위 bbox 에만 걸치거나,
   over-capture 된 인접 행정동에서 나온) 데이터가 애초에 들어오지 않음
4. 클라이언트 native:clip 으로 도형을 범위에 맞춰 잘라냄 (3에서 이미 무관한 feature 는
   걸러졌으므로, clip 이 실패/0건이어도 범위 밖 데이터가 노출되지 않음)

* 범위 WKT 계산이 불가능한 경우(레이어 무효·복잡 레이어 등) 기존 bbox 기반 동작으로 폴백.
"""

import re
import time

from qgis.PyQt.QtCore import QThread, pyqtSignal
from qgis.core import (
    QgsVectorLayer, QgsRasterLayer, QgsProject,
    QgsDataSourceUri, QgsRectangle, QgsGeometry,
    QgsCoordinateReferenceSystem, QgsCoordinateTransform,
    QgsMessageLog, Qgis,
)

from ..db_env import (
    DB_HOST, DB_PORT, DB_NAME, DB_SCHEMA,
    DB_USER, DB_PASSWORD, DB_GEOM_COLUMN,
)

# DB 저장 좌표계
DB_SRID = 5186

# 레이어 그룹 순서 (step3 체크박스 + step4 그룹화 공유)
LAYER_GROUP_ORDER = ["행정구역", "개발사업", "토지_지적", "현황_하천_도로_건물", "배경_지형"]

# 레이어별 한글 필드 별칭 (속성 테이블에서 한글 컬럼명 표시)
FIELD_ALIASES = {
    "건축물정보": {
        "ufid": "UFID", "a0": "도형ID", "a1": "GIS건물통합식별번호", "a2": "고유번호",
        "a3": "법정동코드", "a4": "법정동명", "a5": "특수지구분코드", "a6": "특수지구분명",
        "a7": "지번", "a8": "건물식별번호", "a9": "집합건물구분코드", "a10": "집합건물구분",
        "a11": "대장종류코드", "a12": "대장종류", "a13": "건물명", "a14": "건물동명",
        "a15": "건물연면적", "a16": "건축물구조코드", "a17": "건축물구조명",
        "a18": "주요용도코드", "a19": "주요용도명", "a20": "건물높이(m)",
        "a21": "지상층수", "a22": "지하층수", "a23": "허가일자", "a24": "사용승인일자",
        "a25": "건물연령", "a26": "연령대구분코드", "a27": "연령대구분명",
        "a28": "연령대5계급코드", "a29": "연령대5계급명", "a30": "데이터기준일자",
        "region_code": "행정구역코드",
    },
    "단지경계": {
        "ufid": "UFID", "dan_id": "단지ID", "dan_name": "단지명",
        "danji_type": "단지분류", "cre_date": "생성일자", "upd_date": "갱신일자",
        "gosi": "고시", "remark": "비고",
    },
    "단지시설용지": {
        "ufid": "UFID", "dan_id": "단지ID", "yoj_id": "용지ID",
        "cre_date": "생성일자", "upd_date": "갱신일자", "gosi": "고시",
    },
    "단지용도지역": {
        "ufid": "UFID", "dan_id": "단지ID", "yod_id": "용도ID",
        "cre_date": "생성일자", "upd_date": "갱신일자", "gosi": "고시",
    },
    "단지유치업종": {
        "ufid": "UFID", "dan_id": "단지ID", "upj_id": "업종ID",
        "upj_code": "업종코드", "upj2": "업종코드분류2", "upj3": "업종코드분류3",
        "upj4": "업종코드분류4", "upj5": "업종코드분류5", "upj6": "업종코드분류6",
        "cre_date": "생성일자", "upd_date": "갱신일자", "gosi": "고시",
    },
    "도로중심선": {
        "ufid": "UFID", "rdnu": "도로번호", "name": "명칭", "rddv": "도로구분",
        "stpt": "시점", "edpt": "종점", "pvqt": "포장재질", "dvyn": "분리대유무",
        "rdln": "차로수", "rvwd": "도로폭", "onsd": "일방통행", "rest": "기타",
        "rdnm": "도로명", "scls": "통합코드", "fmta": "제작정보", "region_code": "행정구역코드",
    },
    "등고선": {
        "ufid": "UFID", "divi": "구분", "cont": "등고수치", "scls": "통합코드", "fmta": "제작정보",
        "region_code": "행정구역코드",
    },
    "교량": {
        "gid": "GID", "ufid": "UFID", "name": "명칭", "leng": "연장", "widt": "폭",
        "mate": "재질", "scls": "통합코드", "fmta": "제작정보",
    },
    "도로경계_면": {
        "gid": "GID", "ufid": "UFID", "rdcl": "도로분류",
        "scls": "통합코드", "fmta": "제작정보",
    },
    "터널": {
        "ufid": "UFID", "name": "명칭", "leng": "연장", "widt": "폭", "heig": "높이",
        "scls": "통합코드", "fmta": "제작정보", "region_code": "행정구역코드",
    },
    "토지소유정보": {
        "gid": "GID", "a0": "고유번호", "a1": "법정동코드", "a2": "법정동명",
        "a3": "대장구분코드", "a4": "대장구분명", "a5": "지번", "a6": "지번지목부호",
        "a7": "소유구분코드", "a8": "소유구분명", "a9": "공유인수",
        "a10": "연령대구분코드", "a11": "연령대구분", "a12": "거주지구분코드",
        "a13": "거주지구분", "a14": "국가기관구분코드", "a15": "국가기관구분",
        "a16": "소유권변동원인코드", "a17": "소유권변동원인", "a18": "소유권변동일자",
        "a19": "지목코드", "a20": "지목", "a21": "라벨", "a22": "토지면적",
        "a23": "데이터기준일자", "a24": "시군구코드",
    },
    "실폭하천": {
        "ufid": "UFID", "scls": "통합코드", "fmta": "제작정보",
        "region_code": "행정구역코드",
    },
    "하천중심선": {
        "ufid": "UFID", "rvnu": "하천번호", "name": "명칭", "divi": "구분",
        "type": "형태", "stat": "상태", "scls": "통합코드", "fmta": "제작정보",
        "region_code": "행정구역코드",
    },
    "행정경계_시도": {"adm_cd": "행정구역코드", "adm_nm": "시도명"},
    "행정경계_시군구": {"adm_cd": "행정구역코드", "adm_nm": "시군구명"},
    "행정경계_읍면동": {"adm_cd": "행정구역코드", "adm_nm": "읍면동명"},
    "행정경계_법정리": {"ri_cd": "법정리코드", "ri_nm": "법정리명"},
    "행정경계_법정읍면동": {"emd_cd": "법정읍면동코드", "emd_nm": "법정읍면동명"},
    "호수 및 저수지": {
        "ufid": "UFID", "name": "명칭", "serv": "용도", "mara": "면적",
        "mngt": "관리기관", "scls": "통합코드", "fmta": "제작정보", "region_code": "행정구역코드",
    },
    # 건물통합정보 (f_fac_building)
    "건물통합정보": {
        "ufid": "UFID", "bld_nm": "건물명칭", "dong_nm": "동명칭",
        "grnd_flr": "지상층수", "ugrnd_flr": "지하층수", "pnu": "토지코드",
        "archarea": "건축면적", "totalarea": "연면적", "platarea": "대지면적",
        "height": "건물높이", "strct_cd": "구조", "usability": "용도",
        "bc_rat": "건폐율", "vl_rat": "용적율", "bldrgst_pk": "건축물대장_PK",
        "useapr_day": "승인일자", "regist_day": "데이터생성변경일자", "gb_cd": "구분",
        "viol_bd_yn": "위반건축물", "geoidn": "참조체계연계키",
        "bldg_pnu": "건물필지고유번호", "bldg_pnu_y": "건물필지고유번호유무",
        "bld_unlice": "건물무허가여부", "bd_mgt_sn": "도로명주소건물관리번호",
        "sgg_oid": "원천도형ID", "col_adm_se": "원천시군구코드",
    },
    # 연속주제도 공통 (7개 테이블 동일 구조)
    "소하천": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
    "하천용도구역": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
    "개발제한구역": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
    "택지개발": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
    "혁신도시": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
    "개발진흥지구": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
    "도시개발구역": {
        "mnum": "관리번호", "alias": "별칭", "remark": "비고",
        "ntfdate": "고시일자", "sgg_oid": "원천오브젝트ID", "col_adm_se": "원천시군구코드",
    },
}

# 레이어 목록 (그룹별 가나다순 정렬)
AVAILABLE_LAYERS = [
    # ── 행정구역 ──
    # v2.0.0 BETA (2026-05-06): 행정경계 이원화
    #   - 행정경계_법정리: 단일 소스 전환 (sgis_hjd LENGTH=10 0건 → bnd_li_bjd 활성화, 16,643행)
    #   - 행정경계_법정읍면동: 신규 (bnd_emd_bjd, 5,536행) — 법정 체계 분리
    #   상세 설계: docs/02_DB설계/행정경계_이원화_설계.md (Phase 5)
    # query_type="spatial" — 법정 코드 ≠ 행정 코드 체계라 LIKE 필터 매칭 부정확.
    # 사용자가 선택한 행정구역 polygon과 ST_Intersects 하는 법정 읍면동/리만 반환.
    {"name": "행정경계_법정리", "group": "행정구역", "function_name": "bnd_li_bjd", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "행정경계_법정읍면동", "group": "행정구역", "function_name": "bnd_emd_bjd", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "행정경계_시군구", "group": "행정구역", "function_name": "sgis_hjd", "geom_column": "geometry", "layer_type": "vector", "query_type": "table", "pk_column": "adm_cd", "sql_extra_filter": "LENGTH(adm_cd) = 5"},
    {"name": "행정경계_시도", "group": "행정구역", "function_name": "sgis_hjd", "geom_column": "geometry", "layer_type": "vector", "query_type": "table", "pk_column": "adm_cd", "sql_extra_filter": "LENGTH(adm_cd) = 2"},
    {"name": "행정경계_읍면동", "group": "행정구역", "function_name": "sgis_hjd", "geom_column": "geometry", "layer_type": "vector", "query_type": "table", "pk_column": "adm_cd", "sql_extra_filter": "LENGTH(adm_cd) >= 8 AND LENGTH(adm_cd) < 10"},
    # ── 개발사업 ──
    {"name": "개발제한구역", "group": "개발사업", "function_name": "lsmd_cont_ud801", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "개발진흥지구", "group": "개발사업", "function_name": "lsmd_cont_uq129", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "단지경계", "group": "개발사업", "function_name": "complex_outline_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "단지시설용지", "group": "개발사업", "function_name": "complex_facility_site_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "단지용도지역", "group": "개발사업", "function_name": "complex_landuse_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "도시개발구역", "group": "개발사업", "function_name": "lsmd_cont_ud901", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "택지개발", "group": "개발사업", "function_name": "lsmd_cont_ud301", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "혁신도시", "group": "개발사업", "function_name": "lsmd_cont_ub811", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    # ── 토지_지적 ──
    {"name": "토지소유정보", "group": "토지_지적", "function_name": "al_d160", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    # ── 현황_하천_도로_건물 ──
    {"name": "건물통합정보", "group": "현황_하천_도로_건물", "function_name": "f_fac_building", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "교량", "group": "현황_하천_도로_건물", "function_name": "n3a_a0070000", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "도로경계_면", "group": "현황_하천_도로_건물", "function_name": "n3a_a0010000", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "도로중심선", "group": "현황_하천_도로_건물", "function_name": "road_center_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "소하천", "group": "현황_하천_도로_건물", "function_name": "lsmd_cont_uj301", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "터널", "group": "현황_하천_도로_건물", "function_name": "n3a_a0110020", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "ufid"},
    {"name": "실폭하천", "group": "현황_하천_도로_건물", "function_name": "river_boundary_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "하천용도구역", "group": "현황_하천_도로_건물", "function_name": "lsmd_cont_uj201", "geom_column": "geom", "layer_type": "vector", "query_type": "spatial", "pk_column": "gid"},
    {"name": "하천중심선", "group": "현황_하천_도로_건물", "function_name": "river_centerline_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "호수 및 저수지", "group": "현황_하천_도로_건물", "function_name": "reservoir_clip", "geom_column": "geom", "layer_type": "vector"},
    # ── 배경_지형 (레이어 패널 순서: 등고선(위) → VWorld → DEM(아래)) ──
    {"name": "등고선", "group": "배경_지형", "function_name": "contour_clip", "geom_column": "geom", "layer_type": "vector"},
    {"name": "DEM 90m", "group": "배경_지형", "function_name": "korea_dem_90m", "layer_type": "raster"},
]


def transform_extent_to_db(extent, source_crs):
    """source_crs 기준 extent를 DB CRS(5186)로 변환

    Args:
        extent: QgsRectangle
        source_crs: extent의 좌표계 (보통 작업범위 레이어의 crs() — 프로젝트 crs와 다를 수 있음)
    """
    db_crs = QgsCoordinateReferenceSystem(f"EPSG:{DB_SRID}")
    if not source_crs.isValid() or source_crs.authid() == db_crs.authid():
        return extent
    transform = QgsCoordinateTransform(source_crs, db_crs, QgsProject.instance())
    return transform.transformBoundingBox(extent)


def boundary_geometry_to_db_wkt(boundary_layer, max_features=200, max_wkt_chars=8_000_000):
    """작업 범위 폴리곤 레이어의 모든 도형을 합쳐 DB CRS(5186) WKT 문자열로 반환.

    서버측 정밀 필터(`ST_Intersects(geom, ST_GeomFromText(...))`)에 쓰기 위한 범위 geometry.
    다음 경우 None 을 반환 → 호출측은 None 이면 기존 bbox 기반 동작으로 폴백:
      - 레이어가 무효이거나 도형이 없음
      - feature 가 너무 많음 (사용자가 임의의 큰 레이어를 범위로 지정한 경우 보호 — 그땐 클라이언트 clip 으로만)
      - 변환 결과 WKT 가 과도하게 큼

    ⚠ 메인 스레드에서 호출할 것 (QGIS 레이어/도형 접근).
    """
    try:
        if boundary_layer is None or not boundary_layer.isValid():
            return None
        try:
            fc = boundary_layer.featureCount()
        except Exception:
            fc = -1
        if fc == 0 or (fc > max_features):
            return None

        geoms = []
        for feat in boundary_layer.getFeatures():
            g = feat.geometry()
            if g is not None and not g.isEmpty():
                geoms.append(QgsGeometry(g))
            if len(geoms) > max_features:
                return None
        if not geoms:
            return None

        merged = geoms[0] if len(geoms) == 1 else QgsGeometry.collectGeometry(geoms)
        if merged is None or merged.isEmpty():
            return None

        src_crs = boundary_layer.crs()
        db_crs = QgsCoordinateReferenceSystem(f"EPSG:{DB_SRID}")
        if src_crs.isValid() and src_crs.authid() != db_crs.authid():
            transform = QgsCoordinateTransform(src_crs, db_crs, QgsProject.instance())
            if merged.transform(transform) != 0:
                return None

        wkt = merged.asWkt()
        if not wkt:
            return None
        # asWkt()는 'MULTIPOLYGON (((x y, ...)))' 형태만 출력 — 작은따옴표/세미콜론/주석 없음.
        # 만에 하나 변환 결과가 이상하면 SQL 에 끼워넣지 않고 폴백.
        if "'" in wkt or ";" in wkt or "--" in wkt:
            return None
        if len(wkt) > max_wkt_chars:
            return None
        return wkt
    except Exception:
        return None


def detect_region_code(extent_5186):
    """범위와 겹치는 행정구역 코드를 sgis_hjd에서 자동 감지

    sgis_hjd 테이블에서 범위와 교차하는 시도(2자리) 코드를 찾습니다.
    메인 스레드에서 호출해야 합니다.

    Args:
        extent_5186: QgsRectangle (EPSG:5186)

    Returns:
        str: 시도 코드 (예: "11") 또는 None
    """
    xmin = extent_5186.xMinimum()
    ymin = extent_5186.yMinimum()
    xmax = extent_5186.xMaximum()
    ymax = extent_5186.yMaximum()

    # sgis_hjd에서 범위와 교차하는 가장 넓은(시도급) 행정구역 찾기
    sql_filter = (
        f"ST_Intersects(geometry, "
        f"ST_MakeEnvelope({xmin}, {ymin}, {xmax}, {ymax}, {DB_SRID})) "
        f"AND LENGTH(adm_cd) = 2"
    )

    uri = QgsDataSourceUri()
    uri.setConnection(DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD)
    uri.setDataSource(DB_SCHEMA, "sgis_hjd", "geometry", sql_filter, "adm_cd")

    layer = QgsVectorLayer(uri.uri(), "_region_detect", "postgres")
    if not layer.isValid() or layer.featureCount() <= 0:
        return None

    # 첫 번째 시도 코드 반환
    for feat in layer.getFeatures():
        return feat["adm_cd"]

    return None


def detect_emd_codes(extent_5186, clip_geom_wkt=None):
    """범위와 겹치는 읍면동(8자리+) 코드를 감지

    DB 함수(complex_*_clip 등)는 행정구역 코드 인자를 받으므로, 범위에 걸리는 읍면동
    코드 목록을 만들어 전달한다. clip_geom_wkt(EPSG:5186 WKT)가 주어지면 범위 *bbox* 가
    아니라 *실제 범위 폴리곤* 과 ST_Intersects 하는 읍면동만 골라 over-capture 를 줄인다
    (예: 영통구를 범위로 잡으면 bbox 모서리에만 걸치는 용인·화성 동들이 빠짐 — 단, 빠지지
    않더라도 build_*_uri 의 서버측 필터가 그 동에서 나온 데이터를 다시 걸러내므로 정확성에는
    영향 없고 함수 호출 수만 줄어드는 최적화).

    ⚠ 메인 스레드에서 호출해야 합니다.

    Args:
        extent_5186: QgsRectangle (EPSG:5186) — clip_geom_wkt 가 없을 때 사용
        clip_geom_wkt: 작업 범위 폴리곤 WKT (EPSG:5186), 선택

    Returns:
        list[str]: 읍면동 코드 리스트 (예: ["31240101", "31240102"]) 또는 빈 리스트
    """
    if clip_geom_wkt:
        sql_filter = (
            f"ST_Intersects(geometry, "
            f"ST_MakeValid(ST_Force2D(ST_GeomFromText('{clip_geom_wkt}', {DB_SRID})))) "
            f"AND LENGTH(adm_cd) >= 8"
        )
    else:
        xmin = extent_5186.xMinimum()
        ymin = extent_5186.yMinimum()
        xmax = extent_5186.xMaximum()
        ymax = extent_5186.yMaximum()
        sql_filter = (
            f"ST_Intersects(geometry, "
            f"ST_MakeEnvelope({xmin}, {ymin}, {xmax}, {ymax}, {DB_SRID})) "
            f"AND LENGTH(adm_cd) >= 8"
        )

    uri = QgsDataSourceUri()
    uri.setConnection(DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD)
    uri.setDataSource(DB_SCHEMA, "sgis_hjd", "geometry", sql_filter, "adm_cd")

    layer = QgsVectorLayer(uri.uri(), "_emd_detect", "postgres")
    if not layer.isValid() or layer.featureCount() <= 0:
        return []

    return [feat["adm_cd"] for feat in layer.getFeatures()]


def detect_sigungu_code(extent_5186):
    """범위와 겹치는 시군구(5자리) 코드를 감지 (폴백용)
    메인 스레드에서 호출해야 합니다.
    """
    xmin = extent_5186.xMinimum()
    ymin = extent_5186.yMinimum()
    xmax = extent_5186.xMaximum()
    ymax = extent_5186.yMaximum()

    sql_filter = (
        f"ST_Intersects(geometry, "
        f"ST_MakeEnvelope({xmin}, {ymin}, {xmax}, {ymax}, {DB_SRID})) "
        f"AND LENGTH(adm_cd) = 5"
    )

    uri = QgsDataSourceUri()
    uri.setConnection(DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD)
    uri.setDataSource(DB_SCHEMA, "sgis_hjd", "geometry", sql_filter, "adm_cd")

    layer = QgsVectorLayer(uri.uri(), "_sigungu_detect", "postgres")
    if not layer.isValid() or layer.featureCount() <= 0:
        return None

    codes = [feat["adm_cd"] for feat in layer.getFeatures()]
    if len(codes) == 1:
        return codes[0]
    return codes[0][:2]


def _range_clip_subquery(source_sql, geom_col, range_wkt, extra_where=None):
    """범위 폴리곤(WKT, EPSG:5186)과 *실제로 겹치는 feature 만* 추려내는 쿼리 레이어 서브쿼리 문자열.

    Args:
        source_sql: FROM 절 소스 — 테이블이면 "public.tbl", DB 함수 묶음이면
                    "(SELECT * FROM f('c1') UNION ALL SELECT * FROM f('c2') ...)"
        geom_col: source 의 도형 컬럼명
        range_wkt: 작업 범위 폴리곤 WKT (EPSG:5186) — 작은따옴표·세미콜론 미포함이 보장된 값
        extra_where: 추가 WHERE 조건 (행정경계 레벨 필터 등), 선택

    Returns:
        str: "(SELECT ROW_NUMBER() OVER() AS _uid, t.* FROM <source> AS t,
              (SELECT ST_MakeValid(ST_Force2D(ST_GeomFromText('<wkt>', 5186))) AS g) AS rng
              WHERE [<extra> AND] t.<geom> && rng.g AND ST_Intersects(t.<geom>, rng.g))"
        키 컬럼은 항상 "_uid" (ROW_NUMBER), 도형 컬럼은 그대로 geom_col.

    도형을 ST_Intersection 으로 잘라내지는 않는다(컬럼 전개가 까다로움) — *범위 밖 feature 를
    아예 빼버리는 것* 만 서버에서 하고, 실제 잘라내기는 클라이언트 native:clip 에 맡긴다.
    핵심은: clip 이 0건/실패해도 범위와 무관한 feature 가 통째로 들어오는 일이 없다는 것
    (function 타입에서 over-capture 된 인접 행정동 함수 호출 결과, spatial 타입에서 bbox 모서리에만
    걸치는 타 시군구 폴리곤 등이 여기 WHERE 에서 전부 제거됨).
    """
    rng = (
        f"(SELECT ST_MakeValid(ST_Force2D(ST_GeomFromText('{range_wkt}', {DB_SRID}))) AS g) AS rng"
    )
    conds = []
    if extra_where:
        conds.append(f"({extra_where})")
    conds.append(f"t.{geom_col} && rng.g")
    conds.append(f"ST_Intersects(t.{geom_col}, rng.g)")
    return (
        f"(SELECT ROW_NUMBER() OVER() AS _uid, t.* "
        f"FROM {source_sql} AS t, {rng} "
        f"WHERE {' AND '.join(conds)})"
    )


def build_function_uri(function_name, region_code, layer_info=None, extent_5186=None, range_wkt_5186=None):
    """단일 행정구역 코드용 — build_union_uri 의 1코드 래퍼."""
    return build_union_uri(function_name, [region_code], layer_info, extent_5186, range_wkt_5186)


def build_union_uri(function_name, region_codes, layer_info=None, extent_5186=None, range_wkt_5186=None):
    """DB 함수/테이블 기반 레이어 URI 생성 (작업 범위 폴리곤으로 서버측 1차 필터)

    range_wkt_5186 가 주어지면(정상 경로):
      - function 타입: 감지된 읍면동 코드별 함수 호출을 UNION ALL → 그 결과 중 범위 폴리곤과
        ST_Intersects 하는 row 만 (over-capture 된 인접 동에서 나온 데이터는 여기서 제거됨)
      - spatial / table 타입: 원본 테이블에서 범위 폴리곤과 ST_Intersects 하는 row 만 (GiST 인덱스 활용)
    range_wkt_5186 가 None 이면 기존 bbox / adm_cd LIKE 동작으로 폴백.

    Args:
        function_name: DB 함수 이름 또는 테이블 이름
        region_codes: 행정구역(읍면동) 코드 리스트 — function 타입 호출에 사용
        layer_info: 레이어 추가 정보 (query_type, geom_column, pk_column, sql_extra_filter)
        extent_5186: QgsRectangle (EPSG:5186 bbox) — 폴백 경로용
        range_wkt_5186: 작업 범위 폴리곤 WKT (EPSG:5186), 선택

    Returns:
        QgsDataSourceUri
    """
    # Validate all region codes
    for code in region_codes:
        if not re.match(r'^[0-9]+$', code):
            raise ValueError(f"Invalid region code: {code}")

    uri = QgsDataSourceUri()
    uri.setConnection(DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD)

    geom_col = DB_GEOM_COLUMN
    if layer_info and "geom_column" in layer_info:
        geom_col = layer_info["geom_column"]

    query_type = layer_info.get("query_type", "function") if layer_info else "function"
    pk_col = layer_info.get("pk_column", "") if layer_info else ""
    extra_filter = layer_info.get("sql_extra_filter", "") if layer_info else ""

    # ── 정상 경로: 작업 범위 폴리곤(WKT)으로 서버측 1차 필터 ──
    if range_wkt_5186:
        if query_type in ("spatial", "table"):
            source_sql = f"{DB_SCHEMA}.{function_name}"
            extra_where = extra_filter or None
        else:
            # DB 함수 — 감지된 코드별 호출을 UNION ALL 로 묶어 소스로
            subqueries = [
                f"SELECT * FROM {DB_SCHEMA}.{function_name}('{code}')"
                for code in region_codes
            ]
            source_sql = "(" + " UNION ALL ".join(subqueries) + ")"
            extra_where = None
        table = _range_clip_subquery(source_sql, geom_col, range_wkt_5186, extra_where)
        uri.setDataSource("", table, geom_col, "", "_uid")
        return uri

    # ── 폴백: range WKT 계산 실패 시 기존 bbox / adm_cd LIKE 동작 ──
    if query_type == "spatial":
        if extent_5186 is not None:
            xmin = extent_5186.xMinimum()
            ymin = extent_5186.yMinimum()
            xmax = extent_5186.xMaximum()
            ymax = extent_5186.yMaximum()
            sql_filter = (
                f"ST_Intersects({geom_col}, "
                f"ST_MakeEnvelope({xmin}, {ymin}, {xmax}, {ymax}, {DB_SRID}))"
            )
        else:
            code_list = ", ".join(f"'{c}'" for c in region_codes)
            sql_filter = (
                f"ST_Intersects({geom_col}, "
                f"(SELECT ST_Union(geometry) FROM {DB_SCHEMA}.sgis_hjd "
                f"WHERE adm_cd IN ({code_list})))"
            )
        uri.setDataSource(DB_SCHEMA, function_name, geom_col, sql_filter, pk_col)
    elif query_type == "table":
        # 행정경계 레벨 필터에 맞게 코드 길이 조정 (읍면동 8자리 → 시도 필터엔 2자리로 축약 등)
        adjusted_codes = set()
        for code in region_codes:
            if "LENGTH(adm_cd) = 2" in extra_filter:
                adjusted_codes.add(code[:2])
            elif "LENGTH(adm_cd) = 5" in extra_filter:
                adjusted_codes.add(code[:5])
            else:
                adjusted_codes.add(code)
        adjusted_codes = sorted(adjusted_codes)

        if len(adjusted_codes) == 1:
            sql_filter = f"adm_cd LIKE '{adjusted_codes[0]}%'"
        else:
            conditions = [f"adm_cd LIKE '{code}%'" for code in adjusted_codes]
            sql_filter = f"({' OR '.join(conditions)})"
        if extra_filter:
            sql_filter = f"({sql_filter}) AND ({extra_filter})"

        uri.setDataSource(DB_SCHEMA, function_name, geom_col, sql_filter, pk_col)
    else:
        # DB 함수 — UNION ALL + ROW_NUMBER() 고유 PK
        subqueries = [
            f"SELECT * FROM {DB_SCHEMA}.{function_name}('{code}')"
            for code in region_codes
        ]
        inner = ' UNION ALL '.join(subqueries)
        table = f"(SELECT ROW_NUMBER() OVER() AS _uid, t.* FROM ({inner}) AS t)"
        uri.setDataSource("", table, geom_col, "", pk_col or "_uid")

    return uri


class LayerLoaderThread(QThread):
    """비동기 레이어 URI 준비 스레드

    동작:
    1. 외부에서 미리 감지된 region_code를 받아 사용
    2. 기존 DB 함수를 감지된 코드로 호출하는 URI 생성
    3. 메인 스레드로 URI 전달 → 레이어 생성 + 클리핑

    주의: region_code 감지(detect_sigungu_code / detect_region_code)는
    반드시 메인 스레드에서 수행한 뒤 생성자에 전달해야 합니다.
    """

    progress_changed = pyqtSignal(int, str)
    uri_ready = pyqtSignal(str, str, str, str)  # (uri_str, name, layer_type, provider)
    error_occurred = pyqtSignal(str)
    all_completed = pyqtSignal()

    def __init__(self, layers_to_load, region_codes, extent_5186=None,
                 range_wkt_5186=None, parent=None):
        """
        Args:
            layers_to_load: AVAILABLE_LAYERS 중 선택된 항목 리스트
            region_codes: 읍면동 코드 리스트 (예: ["31240101", "31240102"])
            extent_5186: QgsRectangle (EPSG:5186) — range WKT 폴백/래스터용
            range_wkt_5186: 작업 범위 폴리곤 WKT (EPSG:5186) — 서버측 1차 필터용
        """
        super().__init__(parent)
        self.layers_to_load = layers_to_load
        self.region_codes = region_codes  # 여러 읍면동 코드
        self.extent_5186 = extent_5186
        self.range_wkt_5186 = range_wkt_5186
        self._is_cancelled = False
        self._temp_tables = []

    def cancel(self):
        self._is_cancelled = True

    def run(self):
        total = len(self.layers_to_load)
        region_codes = self.region_codes

        if not region_codes:
            self.error_occurred.emit("범위에 해당하는 행정구역을 찾을 수 없습니다.")
            self.all_completed.emit()
            return

        # 각 레이어 × 각 읍면동 코드로 URI 생성
        for i, layer_info in enumerate(self.layers_to_load):
            if self._is_cancelled:
                break

            name = layer_info["name"]
            func = layer_info["function_name"]
            ltype = layer_info.get("layer_type", "vector")

            pct = int(((i + 1) / total) * 100)
            self.progress_changed.emit(pct, f"준비 중: {name} ({i + 1}/{total})")

            try:
                if ltype == "raster":
                    uri_str = self._build_clipped_raster_uri(func)
                    self.uri_ready.emit(uri_str, name, "raster", "gdal")
                else:
                    # 레이어당 1회 쿼리 — 작업 범위 폴리곤으로 서버측 1차 필터 (range WKT 없으면 bbox 폴백)
                    uri = build_union_uri(
                        func, region_codes, layer_info,
                        self.extent_5186, self.range_wkt_5186,
                    )
                    self.uri_ready.emit(uri.uri(), name, "vector", "postgres")
            except Exception as e:
                QgsMessageLog.logMessage(
                    f"LayerLoaderThread - {name}: {str(e)}",
                    "GISDesignLoader",
                    Qgis.Warning,
                )
                self.error_occurred.emit(f"{name}: {str(e)}")

        self.progress_changed.emit(100, "완료")
        self.all_completed.emit()

    def _build_clipped_raster_uri(self, table_name):
        """사업지역 bbox 기준으로 클리핑된 래스터 URI 생성"""
        temp_table = f"temp_dem_{int(time.time())}"

        try:
            import psycopg2
            conn = psycopg2.connect(
                host=DB_HOST, port=int(DB_PORT), dbname=DB_NAME,
                user=DB_USER, password=DB_PASSWORD, sslmode='require'
            )
            conn.autocommit = True
            cur = conn.cursor()

            # Drop if exists
            cur.execute(f"DROP TABLE IF EXISTS {DB_SCHEMA}.{temp_table}")

            if self.extent_5186 is not None:
                xmin = self.extent_5186.xMinimum()
                ymin = self.extent_5186.yMinimum()
                xmax = self.extent_5186.xMaximum()
                ymax = self.extent_5186.yMaximum()
                cur.execute(
                    f"CREATE TABLE {DB_SCHEMA}.{temp_table} AS "
                    f"SELECT * FROM {DB_SCHEMA}.dem_clip_by_bbox("
                    f"{xmin}, {ymin}, {xmax}, {ymax}, {DB_SRID})"
                )
            else:
                # Fallback: load entire table
                cur.execute(
                    f"CREATE TABLE {DB_SCHEMA}.{temp_table} AS "
                    f"SELECT ROW_NUMBER() OVER()::BIGINT AS rid, rast "
                    f"FROM {DB_SCHEMA}.{table_name}"
                )

            cur.close()
            conn.close()
            self._temp_tables.append(temp_table)

        except Exception as e:
            QgsMessageLog.logMessage(
                f"DEM temp table creation failed: {str(e)}",
                "GISDesignLoader", Qgis.Warning
            )
            # Fallback to full table load
            return (
                f"PG: dbname='{DB_NAME}' "
                f"host='{DB_HOST}' "
                f"port='{DB_PORT}' "
                f"user='{DB_USER}' "
                f"password='{DB_PASSWORD}' "
                f"schema='{DB_SCHEMA}' "
                f"table='{table_name}' "
                f"mode=2"
            )

        return (
            f"PG: dbname='{DB_NAME}' "
            f"host='{DB_HOST}' "
            f"port='{DB_PORT}' "
            f"user='{DB_USER}' "
            f"password='{DB_PASSWORD}' "
            f"schema='{DB_SCHEMA}' "
            f"table='{temp_table}' "
            f"mode=2"
        )

    def cleanup_temp_tables(self):
        """임시 래스터 테이블 정리"""
        if not self._temp_tables:
            return
        try:
            import psycopg2
            conn = psycopg2.connect(
                host=DB_HOST, port=int(DB_PORT), dbname=DB_NAME,
                user=DB_USER, password=DB_PASSWORD, sslmode='require'
            )
            conn.autocommit = True
            cur = conn.cursor()
            for t in self._temp_tables:
                cur.execute(f"DROP TABLE IF EXISTS {DB_SCHEMA}.{t}")
            cur.close()
            conn.close()
            self._temp_tables.clear()
        except Exception:
            pass
