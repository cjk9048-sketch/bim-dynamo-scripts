# -*- coding: utf-8 -*-
"""M5a: 편입현황 삽도(揷圖) PNG — 현재 캔버스 뷰 + 배경 합성.

QGIS Print Layout(`QgsPrintLayout` + `QgsLayoutItemMap`) + `QgsLayoutExporter.exportToImage`
로 PNG 파일을 생성한다.
"""
import os

from .. import db_env
from ..db import queries
from . import symbology
from . import vworld_key as _vworld_key

try:
    from qgis.core import (
        QgsProject, QgsPrintLayout, QgsLayoutItemMap, QgsLayoutSize,
        QgsLayoutPoint, QgsUnitTypes, QgsRectangle,
        QgsLayoutExporter, QgsRasterLayer, QgsMapSettings,
        QgsCoordinateReferenceSystem, QgsCoordinateTransform,
        QgsVectorLayer, QgsField, QgsFeature, QgsGeometry,
        QgsMessageLog, Qgis,
    )
    from qgis.PyQt.QtCore import QVariant
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


# V-World 위성 / 수치지형도 WMTS 타일 (사내 접속 가능한 공개 엔드포인트 예시 — 운영 시 키 별도)
# 키가 없으면 배경 없이 결과만 렌더한다.
_BG_URLS = {
    "satellite": (
        "type=xyz&url=https://api.vworld.kr/req/wmts/1.0.0/{key}/Satellite/{{z}}/{{y}}/{{x}}.jpeg"
    ),
    "topo": (
        "type=xyz&url=https://api.vworld.kr/req/wmts/1.0.0/{key}/Base/{{z}}/{{y}}/{{x}}.png"
    ),
    "hybrid": (
        "type=xyz&url=https://api.vworld.kr/req/wmts/1.0.0/{key}/Hybrid/{{z}}/{{y}}/{{x}}.png"
    ),
}


def _raster_background_key(background: str) -> str:
    return background


def _brighten_raster(layer, brightness: int):
    try:
        flt = layer.brightnessFilter()
        if flt is not None and hasattr(flt, "setBrightness"):
            flt.setBrightness(brightness)
    except Exception:
        pass


def _make_background(background: str, vworld_key: str = "", brightness: int = 60):
    if not _HAS_QGIS or not vworld_key:
        return None
    bg_key = _raster_background_key(background)
    tpl = _BG_URLS.get(bg_key)
    if not tpl:
        return None
    uri = tpl.format(key=vworld_key)
    layer = QgsRasterLayer(uri, f"배경_{background}", "wms")
    if layer.isValid() and bg_key in ("satellite", "hybrid"):
        _brighten_raster(layer, brightness)
    return layer if layer.isValid() else None


def _inset_clone(layer, project):
    """삽도 전용 스타일을 입힐 **복제 레이어**. 캔버스 원본을 안 건드리기 위함.

    메모리 레이어를 clone 하면 데이터(피처·도형)는 공유하고 렌더러·라벨은 독립이라,
    복제본에만 삽도 스타일을 걸면 캔버스 원본(편집용 스타일)은 그대로다. 렌더 후
    호출측이 project 에서 제거한다.
    """
    if not _HAS_QGIS or layer is None:
        return None
    try:
        clone = layer.clone()
        if clone is None:
            return None
        project.addMapLayer(clone, False)   # 레전드에 안 띄우고 렌더에만 사용
        return clone
    except Exception:
        return None


def _compute_extent(layers):
    rect = None
    for lyr in layers:
        if lyr is None:
            continue
        try:
            ext = lyr.extent()
        except Exception:
            continue
        if ext is None or ext.isEmpty():
            continue
        if rect is None:
            rect = QgsRectangle(ext)
        else:
            rect.combineExtentWith(ext)
    if rect is None:
        return None
    # 15% 여백
    pad = max(rect.width(), rect.height()) * 0.15
    rect.grow(pad)
    return rect


def _canvas_extent_5186(iface):
    if not _HAS_QGIS or iface is None:
        return None
    try:
        canvas = iface.mapCanvas()
        rect = QgsRectangle(canvas.extent())
        if rect.isEmpty():
            return None
        target = QgsCoordinateReferenceSystem("EPSG:5186")
        source = canvas.mapSettings().destinationCrs()
        if source and source.isValid() and source.authid() != target.authid():
            transform = QgsCoordinateTransform(source, target, QgsProject.instance())
            rect = transform.transformBoundingBox(rect)
        return rect
    except Exception:
        return None


def _make_cadastral_layer(extent, limit: int = 3000):
    if not _HAS_QGIS or extent is None or extent.isEmpty():
        return None
    layer = QgsVectorLayer(f"MultiPolygon?crs=EPSG:{db_env.DB_SRID}", "cadastral_background", "memory")
    provider = layer.dataProvider()
    provider.addAttributes([
        QgsField("pnu", QVariant.String),
        QgsField("jibun", QVariant.String),
    ])
    layer.updateFields()

    conn = None
    try:
        conn = queries.connect()
        with conn.cursor() as cur:
            cur.execute("BEGIN")
            cur.execute("SET LOCAL statement_timeout = '12s'")
            cur.execute(
                queries.CADASTRAL_BY_BBOX.format(schema=db_env.DB_SCHEMA),
                (
                    extent.xMinimum(), extent.yMinimum(),
                    extent.xMaximum(), extent.yMaximum(),
                    db_env.DB_SRID, limit,
                ),
            )
            rows = cur.fetchall()
            cur.execute("COMMIT")
    except Exception:
        try:
            if conn is not None:
                conn.rollback()
        except Exception:
            pass
        try:
            QgsMessageLog.logMessage(
                "지적 배경: 범위가 넓거나 조회 실패로 지적선을 불러오지 못했습니다(위성/빈 배경으로 대체).",
                "landuse_review", Qgis.Warning)
        except Exception:
            pass
        return None
    finally:
        try:
            if conn is not None:
                conn.close()
        except Exception:
            pass

    features = []
    for pnu, jibun, geom_wkt in rows:
        try:
            geom = QgsGeometry.fromWkt(geom_wkt or "")
            if not geom or geom.isNull() or geom.isEmpty():
                continue
            geom.convertToMultiType()
            feat = QgsFeature(layer.fields())
            feat.setAttributes([pnu or "", jibun or ""])
            feat.setGeometry(geom)
            features.append(feat)
        except Exception:
            continue
    if not features:
        try:
            QgsMessageLog.logMessage(
                "지적 배경: 범위가 넓거나 조회 실패로 지적선을 불러오지 못했습니다(위성/빈 배경으로 대체).",
                "landuse_review", Qgis.Warning)
        except Exception:
            pass
        return None
    provider.addFeatures(features)
    layer.updateExtents()
    symbology.style_cadastral_background(layer)
    return layer


def render_to_png(iface, *, output_path: str, background: str = "satellite",
                  parcel_layer=None, boundary_layer=None, inclusion_layer=None,
                  vworld_key: str = "", dpi: int = 200,
                  extent_mode: str = "boundary", brightness: int = 60) -> str:
    """편입현황 삽도 PNG를 생성하여 경로를 반환한다.

    vworld_key 가 비면 설정값(QSettings/.env)에서 자동으로 읽는다(키 없으면 배경 생략).
    """
    if not _HAS_QGIS:
        raise RuntimeError("QGIS 환경이 아닙니다.")
    if not vworld_key:
        vworld_key = _vworld_key.get_api_key()

    project = QgsProject.instance()
    layout = QgsPrintLayout(project)
    layout.initializeDefaults()
    layout.setName("landuse_review_inset")

    page = layout.pageCollection().pages()[0]
    page.setPageSize(QgsLayoutSize(297, 210, QgsUnitTypes.LayoutMillimeters))

    map_item = QgsLayoutItemMap(layout)
    layout.addLayoutItem(map_item)
    map_item.attemptMove(QgsLayoutPoint(0, 0, QgsUnitTypes.LayoutMillimeters))
    map_item.attemptResize(QgsLayoutSize(297, 210, QgsUnitTypes.LayoutMillimeters))

    # 레이어 합성 — QGIS setLayers() 는 리스트 **앞쪽이 위에** 그려진다.
    # 분석 레이어(용지경계 > 편입구역 > 선택필지)를 위로, 배경 래스터를 맨 아래로 둔다.
    # (옛 버그: 배경을 맨 앞에 넣어 불투명 위성/수치지형도가 분석 레이어를 전부 덮었음.)
    #
    # 용지경계·편입구역은 **삽도 전용 스타일(빨강 경계·파란 빗금·주기·지번 라벨)** 을
    # 입힌 복제본으로 그린다 — 캔버스 원본(편집·식별용 스타일)은 건드리지 않는다(도로부 표준 삽도).
    layers = []
    inset_clones = []
    b_inset = _inset_clone(boundary_layer, project)
    if b_inset is not None:
        symbology.style_boundary_inset(b_inset)
        inset_clones.append(b_inset)
        layers.append(b_inset)
    i_inset = _inset_clone(inclusion_layer, project)
    if i_inset is not None:
        symbology.style_inclusion_inset(i_inset)
        inset_clones.append(i_inset)
        layers.append(i_inset)
    if parcel_layer is not None:
        # 선택필지 = 회색 배경. 삽도에선 지번 라벨을 끈다 — 편입구역(위)이 지번+면적 주기를
        # 담당하므로, 캔버스용 선택필지 지번 라벨을 그대로 두면 삽도에서 지번이 중복된다.
        p_inset = _inset_clone(parcel_layer, project)
        if p_inset is not None:
            try:
                p_inset.setLabelsEnabled(False)
            except Exception:
                pass
            inset_clones.append(p_inset)
            layers.append(p_inset)
        else:
            layers.append(parcel_layer)
    brightness = max(0, min(100, int(brightness)))
    if extent_mode == "boundary":
        ext = _compute_extent([boundary_layer])
    elif extent_mode == "full":
        ext = _compute_extent([parcel_layer, boundary_layer, inclusion_layer])
    else:
        ext = _canvas_extent_5186(iface) or _compute_extent([parcel_layer, boundary_layer, inclusion_layer])

    cadastral = _make_cadastral_layer(ext) if background == "cadastral" else None
    if cadastral is not None:
        project.addMapLayer(cadastral, False)
        layers.append(cadastral)
    bg = _make_background(background, vworld_key, brightness)
    if bg is not None:
        project.addMapLayer(bg, False)
        layers.append(bg)          # 배경 = 리스트 맨 뒤 = 맨 아래
    map_item.setLayers(layers)

    if ext is not None:
        # setExtent() 는 용지 비율에 맞추려고 맵 아이템 크기를 바꾼다(→ 하단 잘림/흰 여백).
        # zoomToExtent() 는 아이템 크기를 유지한 채 범위가 다 보이도록 축척만 바꾼다.
        map_item.zoomToExtent(ext)
    map_item.setCrs(QgsCoordinateReferenceSystem("EPSG:5186"))

    # 내보내기
    exporter = QgsLayoutExporter(layout)
    settings = QgsLayoutExporter.ImageExportSettings()
    settings.dpi = dpi

    os.makedirs(os.path.dirname(output_path) or ".", exist_ok=True)
    try:
        res = exporter.exportToImage(output_path, settings)
    finally:
        # 임시 배경 래스터 정리(반복 내보내기 시 숨은 레이어 누적 방지). 분석 레이어는 캔버스 유지.
        if bg is not None:
            try:
                project.removeMapLayer(bg.id())
            except Exception:
                pass
        if cadastral is not None:
            try:
                project.removeMapLayer(cadastral.id())
            except Exception:
                pass
        # 삽도 전용 스타일 복제본 제거(캔버스 원본은 그대로 유지).
        for c in inset_clones:
            try:
                project.removeMapLayer(c.id())
            except Exception:
                pass
    if res != QgsLayoutExporter.Success:
        raise RuntimeError("삽도 PNG 내보내기 실패")
    return output_path
