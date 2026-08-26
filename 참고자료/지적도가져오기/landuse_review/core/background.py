# -*- coding: utf-8 -*-
"""캔버스 배경지도 — 위성/수치지형(V-World WMTS) + 지적(DB). (개발 건 #2)

작업 시작 시 배경을 깔면 ① 공간 맥락을 얻고 ② **용지경계 좌표계 오지정을 눈으로 잡는다**
(엉뚱한 위치에 떨어지면 즉시 보임). 삽도(`inset_renderer`)와 소스는 같지만, 이쪽은
**캔버스 상시 표시**용이고 투명도 조절이 붙는다.

  · 위성 / 수치지형 : V-World WMTS(전역, 즉시). 키 없으면 생략.
  · 지적           : PROD `lsmd_cont_ldreg` — 위치(bbox)가 있어야 로드(전국 불가, 3000필지 상한).

배경 레이어에는 소유 마커(`layers.mark_owned`)를 달아 초기화 때 사용자 레이어와 구분한다.
"""
from .. import db_env
from ..db import queries
from . import symbology
from . import layers
from . import vworld_key as _vworld_key

try:
    from qgis.core import (
        QgsProject, QgsRasterLayer, QgsVectorLayer, QgsField, QgsFeature,
        QgsGeometry, QgsRectangle, QgsMessageLog, Qgis, QgsLayerTreeLayer,
    )
    from qgis.PyQt.QtCore import QVariant
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


# 캔버스 배경 레이어 이름(고정) — 재로드 시 교체, 초기화 시 제거 대상.
BG_LAYER_NAMES = {
    "satellite": "배경_위성사진",
    "hybrid": "배경_위성하이브리드",
    "topo": "배경_수치지형도",
    "cadastral": "배경_지적도",
}

# V-World WMTS (inset_renderer 와 동일 소스). 키 없으면 위성/수치지형 생략.
_WMTS = {
    "satellite": "type=xyz&url=https://api.vworld.kr/req/wmts/1.0.0/{key}/Satellite/{{z}}/{{y}}/{{x}}.jpeg",
    "topo": "type=xyz&url=https://api.vworld.kr/req/wmts/1.0.0/{key}/Base/{{z}}/{{y}}/{{x}}.png",
    "hybrid": "type=xyz&url=https://api.vworld.kr/req/wmts/1.0.0/{key}/Hybrid/{{z}}/{{y}}/{{x}}.png",
}


def _log(msg):
    if not _HAS_QGIS:
        return
    try:
        QgsMessageLog.logMessage(msg, "landuse_review", Qgis.Warning)
    except Exception:
        pass


def available_backgrounds(has_key: bool):
    """현재 켤 수 있는 배경 목록. 위성/수치지형은 V-World 키가 있어야 한다."""
    if has_key:
        return ["satellite", "hybrid", "topo", "cadastral"]
    return ["cadastral"]


def _wmts_layer(kind: str, vworld_key: str):
    if not vworld_key:
        return None
    tpl = _WMTS.get(kind)
    if not tpl:
        return None
    lyr = QgsRasterLayer(tpl.format(key=vworld_key), BG_LAYER_NAMES[kind], "wms")
    return lyr if lyr.isValid() else None


def _cadastral_layer(extent: "QgsRectangle", limit: int = 3000):
    """지적 배경 — 현재 화면 bbox 로 `lsmd_cont_ldreg` 조회(위치 필수, 상한 3000필지)."""
    if extent is None or extent.isEmpty():
        return None
    lyr = QgsVectorLayer(f"MultiPolygon?crs=EPSG:{db_env.DB_SRID}",
                         BG_LAYER_NAMES["cadastral"], "memory")
    pr = lyr.dataProvider()
    pr.addAttributes([QgsField("pnu", QVariant.String), QgsField("jibun", QVariant.String)])
    lyr.updateFields()
    conn = None
    try:
        conn = queries.connect()
        with conn.cursor() as cur:
            cur.execute("BEGIN")
            cur.execute("SET LOCAL statement_timeout = '12s'")
            cur.execute(queries.CADASTRAL_BY_BBOX.format(schema=db_env.DB_SCHEMA),
                        (extent.xMinimum(), extent.yMinimum(),
                         extent.xMaximum(), extent.yMaximum(), db_env.DB_SRID, limit))
            rows = cur.fetchall()
            cur.execute("COMMIT")
    except Exception as e:
        try:
            if conn is not None:
                conn.rollback()
        except Exception:
            pass
        _log(f"지적 배경 조회 실패(범위가 넓거나 시간초과): {e}")
        return None
    finally:
        try:
            if conn is not None:
                conn.close()
        except Exception:
            pass

    feats = []
    truncated = len(rows) >= limit
    for pnu, jibun, geom_wkt in rows:
        try:
            g = QgsGeometry.fromWkt(geom_wkt or "")
            if not g or g.isNull() or g.isEmpty():
                continue
            g.convertToMultiType()
            f = QgsFeature(lyr.fields())
            f.setAttributes([pnu or "", jibun or ""])
            f.setGeometry(g)
            feats.append(f)
        except Exception:
            continue
    if not feats:
        _log("지적 배경: 이 범위에서 지적선을 불러오지 못했습니다.")
        return None
    if truncated:
        _log(f"지적 배경이 {limit}필지를 넘어 잘렸습니다. 화면을 좁히면 전부 볼 수 있습니다.")
    pr.addFeatures(feats)
    lyr.updateExtents()
    symbology.style_cadastral_background(lyr)
    return lyr


def add_background(project, kind: str, *, opacity: float = 1.0,
                   extent: "QgsRectangle" = None, vworld_key: str = "") -> bool:
    """배경 레이어 1종을 캔버스에 추가(동명 배경 교체). 성공하면 True.

    Args:
        kind: 'satellite' | 'topo' | 'cadastral'
        opacity: 0.0~1.0 불투명도.
        extent: 지적('cadastral')에 필수(현재 화면 5186 bbox). 위성/수치지형은 무시.
        vworld_key: 위성/수치지형에 필요. 비면 설정값에서 취득 시도.
    """
    if not _HAS_QGIS or project is None:
        return False
    if kind in ("satellite", "hybrid", "topo"):
        if not vworld_key:
            vworld_key = _vworld_key.get_api_key()
        lyr = _wmts_layer(kind, vworld_key)
    elif kind == "cadastral":
        lyr = _cadastral_layer(extent)
    else:
        return False
    if lyr is None:
        return False
    try:
        lyr.setOpacity(max(0.0, min(1.0, float(opacity))))
    except Exception:
        pass
    # 배경은 **레이어 트리 맨 아래**에 둔다 — 분석 레이어(필지·경계·편입)를 가리지 않게.
    # 호출 순서 위성→수치지형→지적이면 아래에서 위로 위성<수치지형<지적로 쌓인다.
    layers.remove_owned(project, BG_LAYER_NAMES[kind])
    layers.mark_owned(lyr)
    project.addMapLayer(lyr, False)          # 레전드 자동 추가 안 함(순서 직접 제어)
    try:
        project.layerTreeRoot().insertChildNode(-1, QgsLayerTreeLayer(lyr))  # -1 = 맨 아래
    except Exception:
        project.layerTreeRoot().addLayer(lyr)
    return True


def set_background_opacity(project, kind: str, opacity: float) -> bool:
    """이미 올라온 배경의 불투명도만 조절."""
    if not _HAS_QGIS or project is None:
        return False
    name = BG_LAYER_NAMES.get(kind)
    if not name:
        return False
    ok = False
    for lyr in project.mapLayersByName(name):
        try:
            lyr.setOpacity(max(0.0, min(1.0, float(opacity))))
            lyr.triggerRepaint()
            ok = True
        except Exception:
            pass
    return ok


def remove_background(project, kind: str = None):
    """배경 레이어 제거(kind 미지정이면 3종 모두). 소유 마커가 있는 것만."""
    if not _HAS_QGIS or project is None:
        return
    names = [BG_LAYER_NAMES[kind]] if kind else list(BG_LAYER_NAMES.values())
    for name in names:
        layers.remove_owned(project, name)
