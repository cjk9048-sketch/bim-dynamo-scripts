# -*- coding: utf-8 -*-
"""M3: 용지(사업)경계 입력 — 4가지 방식 + EPSG:5186 정합.

- prepare()        : 프로젝트 폴리곤 레이어 → 5186 reproject → 캔버스 등록
- load_from_file() : SHP/DXF 로드 → (CAD 는 좌표계 지정) → 폴리곤화 → 5186 → 캔버스
- start_drawing()  : 메모리 폴리곤 레이어 + 편집 모드 — 사용자가 폴리곤 그림

기획서 v2 §3-2단계: CAD(DXF/DWG) 파일은 좌표계 정보를 담지 않으므로 "이 파일의
좌표계 = 중부원점(EPSG:5186)" 을 한 번 지정·확인하는 단계가 있다. DWG 는 미지원 —
'DXF 로 저장' 또는 ODA File Converter 변환을 안내(v2 §11).

좌표계 변환은 `coords` 모듈 1곳으로 수렴(§2-②).
"""
import os

from .. import db_env
from . import coords
from . import symbology
from . import layers

try:
    from qgis.core import (
        QgsVectorLayer, QgsProject, QgsWkbTypes, QgsCoordinateReferenceSystem,
    )
    import processing
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


def _ensure_polygon(layer, target_name: str):
    """레이어가 라인 지오메트리면 폴리곤화(닫힌 폴리라인 → 폴리곤). 폴리곤이면 그대로."""
    if not _HAS_QGIS or layer is None:
        return layer
    try:
        gtype = layer.geometryType()
    except Exception:
        return layer
    if gtype == QgsWkbTypes.PolygonGeometry:
        return layer
    if gtype == QgsWkbTypes.LineGeometry:
        res = processing.run(
            "native:polygonize",
            {"INPUT": layer, "KEEP_FIELDS": False, "OUTPUT": "memory:"},
        )
        out = (res or {}).get("OUTPUT")
        if out is None:
            return layer
        out.setName(target_name)
        return out
    # 포인트 등은 경계로 부적합 — 원본 반환(호출 측 검증)
    return layer


def prepare(layer, *, assign_epsg: int = None,
            target_name: str = "용지경계", project=None):
    """프로젝트 폴리곤 레이어를 (필요 시 좌표계 지정 후) 5186 정합 → 캔버스 등록.

    Returns: (layer, warning). 좌표계 미정·미지정이면 layer=None + 안내.
    """
    if not _HAS_QGIS or layer is None:
        return None, "레이어가 없습니다."
    work = layer
    # assign_epsg 는 **폴백 전용** — 레이어에 유효 좌표계가 없을 때만 지정(정상 CRS 덮어쓰지 않음).
    if not work.crs().isValid() and assign_epsg:
        work = coords.assign_crs(work, assign_epsg)
    if not work.crs().isValid():
        return None, ("레이어 좌표계가 정의되지 않았습니다. 좌표계를 지정(예: EPSG:5186)한 후 다시 시도하세요.")
    work = _ensure_polygon(work, target_name)
    new_layer = coords.reproject_layer_to_db(work, target_name=target_name)
    if new_layer is None:
        return None, "좌표계 변환 실패 — 좌표계 지정을 확인하세요."
    symbology.style_boundary(new_layer)
    if project is not None:
        layers.replace_owned(project, target_name, new_layer)
    return new_layer, ""


def load_from_file(path: str, *, assign_epsg: int = None,
                   target_name: str = "용지경계", project=None):
    """SHP/DXF 파일 로드 → (CAD 좌표계 지정) → 폴리곤화 → 5186 → 캔버스.

    Args:
        path: .shp 또는 .dxf 경로.
        assign_epsg: 파일에 좌표계 정보가 없을 때 지정할 EPSG(보통 5186). CAD 필수.
    Returns: (layer, warning).
    """
    if not _HAS_QGIS or not path or not os.path.exists(path):
        return None, "파일을 찾을 수 없습니다."
    ext = os.path.splitext(path)[1].lower()
    if ext == ".dwg":
        return None, (
            "DWG 는 직접 지원하지 않습니다. AutoCAD 에서 'DXF 로 저장' 하거나 "
            "ODA File Converter 로 DXF 변환 후 업로드하세요."
        )

    layer = QgsVectorLayer(path, target_name, "ogr")
    if not layer.isValid():
        return None, f"파일 로드 실패: {os.path.basename(path)}"

    # 좌표계: SHP 는 .prj 우선, DXF 는 보통 없음 → 좌표계 미정일 때만 assign_epsg 로 지정(폴백).
    if not layer.crs().isValid() and assign_epsg:
        layer = coords.assign_crs(layer, assign_epsg)
    if not layer.crs().isValid():
        return None, (
            f"{os.path.basename(path)} 에 좌표계 정보(.prj)가 없습니다.\n"
            "이 파일의 좌표계(예: 중부원점 EPSG:5186)를 지정한 후 다시 시도하세요."
        )

    layer = _ensure_polygon(layer, target_name)
    try:
        if layer.geometryType() != QgsWkbTypes.PolygonGeometry:
            return None, (
                "이 CAD 파일에서 경계로 쓸 닫힌 폴리곤을 찾지 못했습니다.\n"
                "흔한 원인: ① 용지경계가 도로중심선·문자·치수(원)와 한 파일에 섞여 있어 "
                "폴리곤 레이어로 안 읽힘  ② 경계 폴리라인이 닫혀 있지 않음(시작점≠끝점).\n"
                "해결: CAD에서 용지경계 폴리라인만(또는 그 도면층만) 남겨 별도 파일로 저장해 "
                "불러오거나, 2단계 '지도에서 직접 그리기'로 그리세요."
            )
        # 폴리곤화 결과가 0개 — 미폐합 폴리라인(닫히지 않은 선) → 빈 경계 방지
        if layer.featureCount() == 0:
            return None, (
                "닫힌 폴리곤을 만들지 못했습니다(폴리곤 0개). CAD 경계선(폴리라인)이 "
                "완전히 닫혀 있는지(시작점=끝점), 경계만 담긴 파일인지 확인하세요."
            )
    except Exception:
        pass

    new_layer = coords.reproject_layer_to_db(layer, target_name=target_name)
    if new_layer is None:
        return None, "좌표계 변환 실패 — 좌표계 지정을 확인하세요."
    symbology.style_boundary(new_layer)
    if project is not None:
        layers.replace_owned(project, target_name, new_layer)
    return new_layer, ""


def start_drawing(*, iface, target_name: str = "용지경계"):
    """메모리 폴리곤 레이어(5186) 생성 + 편집 모드 진입. 사용자가 지도에 폴리곤을 그린다."""
    if not _HAS_QGIS:
        return None
    project = QgsProject.instance()
    layer = QgsVectorLayer(f"Polygon?crs=EPSG:{db_env.DB_SRID}", target_name, "memory")
    symbology.style_boundary(layer)
    layers.replace_owned(project, target_name, layer)
    if not layer.isEditable():
        layer.startEditing()
    if iface is not None:
        try:
            iface.setActiveLayer(layer)
            iface.mapCanvas().refresh()       # 레이어 paint 후 디지타이즈 툴 전환 신뢰성 ↑
            iface.actionAddFeature().trigger()
        except Exception:
            pass
    return layer
