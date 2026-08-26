# -*- coding: utf-8 -*-
"""좌표계 변환 — **단일 유틸로 수렴**(1~2단계 설계검토 §2-②).

진입점이 최소 3곳(V-World search point=4326 고정 / 지도 캔버스 CRS 임의 /
CAD 파일 좌표계 미상)이라 변환을 없앨 수는 없다. 그래서 변환 책임을 이 모듈
1곳으로 모으고, `ST_Intersection`·면적 산출 직전에 세 입력(API 필지 / 사용자
사업경계 / DB 필지)의 SRID 를 모두 **DB_SRID(5186)** 로 정렬한다.

가장 경계할 실패 경로: 사용자가 사업경계 좌표계를 잘못 지정(예 5174·5187)하면
교차가 0 이 되어 "편입 0%"라는 **무성(silent) 오답**이 난다. `overlap_sanity()`
가 경계 입력 직후 중첩 여부를 점검해 이 오답을 사전 경고한다.
"""
from .. import db_env

try:
    from qgis.core import (
        QgsCoordinateReferenceSystem, QgsCoordinateTransform,
        QgsProject, QgsGeometry, QgsRectangle,
    )
    import processing
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


def db_crs():
    """DB 표준 좌표계(EPSG:5186) QgsCoordinateReferenceSystem."""
    return QgsCoordinateReferenceSystem(f"EPSG:{db_env.DB_SRID}")


def is_db_crs(crs) -> bool:
    try:
        return crs.isValid() and crs.authid() == db_crs().authid()
    except Exception:
        return False


def reproject_layer_to_db(layer, *, target_name: str = None):
    """레이어를 DB_SRID(5186)로 변환. 이미 5186이면 (이름만 바꿔) 그대로 반환.

    CRS 미정 레이어는 변환 불가 → 호출 측이 먼저 사용자 지정(assign)하게 한다.
    여기서는 미정이면 None 을 반환(조용히 5186 가정하지 않음 — 무성 오답 방지).
    """
    if not _HAS_QGIS or layer is None:
        return None
    src = layer.crs()
    if not src.isValid():
        return None  # 좌표계 미정 — 호출 측에서 assign_crs() 선행 필요
    if is_db_crs(src):
        if target_name:
            layer.setName(target_name)
        return layer
    res = processing.run(
        "native:reprojectlayer",
        {"INPUT": layer, "TARGET_CRS": db_crs(), "OUTPUT": "memory:"},
    )
    out = res["OUTPUT"]
    if target_name:
        out.setName(target_name)
    return out


def assign_crs(layer, epsg: int):
    """좌표계 미포함(CAD 등) 레이어에 좌표계를 **지정**(reproject 아님).

    파일에 .prj/좌표계 정보가 없을 때 '이 파일의 좌표계 = EPSG:xxxx' 를 못박는 단계.
    지정 후 reproject_layer_to_db() 로 5186 정렬.
    """
    if not _HAS_QGIS or layer is None:
        return layer
    layer.setCrs(QgsCoordinateReferenceSystem(f"EPSG:{int(epsg)}"))
    return layer


def transform_extent_to_db(extent: "QgsRectangle", src_crs) -> "QgsRectangle":
    """extent(bbox)를 src_crs → 5186 으로 변환."""
    if not _HAS_QGIS:
        return extent
    if is_db_crs(src_crs):
        return extent
    tr = QgsCoordinateTransform(src_crs, db_crs(), QgsProject.instance())
    return tr.transformBoundingBox(extent)


# 우리나라 평면직각좌표 4개 원점(모두 Korea 2000 datum) — 빈도순.
# 서부(5185,125°E)·중부(5186,127°E)·동부(5187,129°E)·동해(5188,131°E).
# 네 원점 모두 false easting 200,000 을 공유해 **좌표 숫자만으로는 belt 를 못 가린다**
# (인접 원점이 둘 다 육지 좌표를 준다 — 사천 X≈118k 가 5186=전남/5187=사천).
# 그래서 기준(선택필지 5186 extent / 배경)과의 **겹침**으로 판정한다.
CRS_CANDIDATES = [5186, 5187, 5185, 5188]


def suggest_crs_for_boundary(boundary_layer, reference_extent_db, candidates=None):
    """좌표계 미상(CAD) 경계에 대해 **기준과 겹치는 좌표계 후보**를 추천한다.

    경계의 원본 좌표 bbox 를 각 후보 좌표계라고 가정해 5186 으로 변환한 뒤, 기준
    extent(=선택필지 5186 또는 배경 화면, 이미 5186)와 겹치는 후보만 남겨 겹침 정도
    내림차순으로 돌려준다. 좌표 숫자 단독 판별 금지(위 CRS_CANDIDATES 주석) — 반드시
    기준점 겹침으로 고른다.

    Args:
        boundary_layer: 좌표계 미지정 경계 레이어(원본 파일 좌표).
        reference_extent_db: 기준 bbox(EPSG:5186). 보통 선택필지 레이어의 extent.
    Returns:
        [(epsg:int, overlap_ratio:float), ...] 겹치는 후보만, 겹침 정도 내림차순. 없으면 [].
        overlap_ratio = (교차 넓이 / 기준 넓이) — 1 에 가까울수록 기준을 잘 덮음.
    """
    if not _HAS_QGIS or boundary_layer is None or reference_extent_db is None:
        return []
    raw = boundary_layer.extent()          # 파일 원본 좌표 숫자의 bbox
    if raw.isEmpty() or reference_extent_db.isEmpty():
        return []
    ref_area = reference_extent_db.area()
    out = []
    for epsg in (candidates or CRS_CANDIDATES):
        try:
            src = QgsCoordinateReferenceSystem(f"EPSG:{int(epsg)}")
            if not src.isValid():
                continue
            tr = QgsCoordinateTransform(src, db_crs(), QgsProject.instance())
            projected = tr.transformBoundingBox(raw)
            inter = projected.intersect(reference_extent_db)
            if inter is not None and not inter.isEmpty():
                ratio = (inter.area() / ref_area) if ref_area > 0 else 0.0
                out.append((int(epsg), ratio))
        except Exception:
            continue
    out.sort(key=lambda t: t[1], reverse=True)
    return out


def union_wkt_5186(layer) -> str:
    """레이어의 모든 폴리곤을 5186 으로 정렬 후 ST_Union 한 단일 WKT 반환.

    레이어가 이미 5186 정합돼 있어야 정확(boundary_input 이 사전 정렬).
    빈/무효 도형은 건너뛴다.
    """
    if not _HAS_QGIS or layer is None:
        return ""
    geoms = []
    for f in layer.getFeatures():
        g = f.geometry()
        if g and not g.isEmpty():
            geoms.append(g)
    if not geoms:
        return ""
    union = geoms[0]
    for g in geoms[1:]:
        union = union.combine(g)
    return union.asWkt()


def overlap_sanity(parcel_layer, boundary_layer) -> "tuple[bool, str]":
    """선택필지와 용지경계가 실제로 겹치는지 점검(좌표계 오지정 → 편입0% 무성오답 사전 경고).

    두 레이어가 모두 유효하고 extent 가 교차하면 (True, '') — 정상.
    교차하지 않으면 (False, 경고문) — 좌표계/위치 점검 유도.

    Returns:
        (ok, message)
    """
    if not _HAS_QGIS:
        return True, ""
    try:
        if parcel_layer is None or boundary_layer is None:
            return True, ""
        pe = parcel_layer.extent()
        be = boundary_layer.extent()
        if pe.isEmpty() or be.isEmpty():
            return True, ""
        # 두 레이어 모두 5186 정합 가정(선택필지=DB조회 5186, 용지경계=boundary_input 정렬).
        pe5186 = transform_extent_to_db(pe, parcel_layer.crs())
        be5186 = transform_extent_to_db(be, boundary_layer.crs())
        if pe5186.intersects(be5186):
            return True, ""
        return False, (
            "선택필지와 용지경계의 위치가 전혀 겹치지 않습니다.\n"
            "용지경계(SHP/DXF)의 좌표계를 잘못 지정했을 가능성이 큽니다 "
            "(예: 5174·5187 을 5186 으로 지정). 2단계에서 좌표계를 다시 확인하세요.\n"
            "이 상태로 진행하면 편입면적이 0 으로 계산될 수 있습니다."
        )
    except Exception:
        return True, ""
