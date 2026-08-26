# -*- coding: utf-8 -*-
"""M4: 편입면적 산출 + "편입구역" 캔버스 레이어 추가.

**1단계에서 취득한 필지 도형(API 우선·5186)을 그대로** 사업경계와 교차해 편입면적·
편입률·잔여면적을 산출한다(클라이언트/QGIS 기하 연산). DB 를 다시 치지 않으므로
분석이 데이터 출처(API/DB)와 무관하게 일관되고, `lsmd_cont_ldreg` 컬럼 차이(지목 등)에
영향받지 않는다.

설계검토 정합:
  · 분모(공부면적) = **토지대장 면적**(`al_d160.a22`), 없으면 필지 전체 도형 면적.
    경계로 미리 자르지 않는다(§2-⑤). 정의는 `parcel_lookup.book_area()` 한 곳.
  · 멀티파트 필지는 1단계 취득 시 `ST_Union`(DB) / MultiPolygon(API)로 이미 보존(§2-①).
  · 좌표계 오지정 → "편입 0%" 무성 오답 사전 경고(`coords.overlap_sanity`, §2-②).

**결과 상태 3분류** (도로부 V3 요구 8a / 결함 D3):
  · `not_found`      — 유효한 필지 도형이 하나도 없다(지번 오타 등). **오류**
  · `no_inclusion`   — 필지는 찾았고, 정말로 아무것도 편입되지 않는다. **정상 결과**
  · `coord_mismatch` — 필지와 경계가 전혀 겹치지 않는다. 좌표계 오지정 의심. **경고**
  · `ok`             — 편입 필지 있음
  '못 찾음' 과 '편입 0' 을 뭉개면 오답이 성공으로 보인다 — 이 도구가 막으려던 바로 그 실패다.
"""
from typing import List, Dict, Tuple, Optional, NamedTuple

from .. import db_env
from . import coords
from . import symbology
from . import layers
from .parcel_lookup import book_area_info

try:
    from qgis.core import (
        QgsVectorLayer, QgsField, QgsFeature, QgsGeometry, QgsProject,
        QgsMessageLog, Qgis,
    )
    from qgis.PyQt.QtCore import QVariant
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


def _log(msg: str):
    """QGIS 로그창에 남긴다(조용히 삼키지 않기 위함). 사용자에게 보여야 하는 실패는
    호출측이 status/messageBar 로 따로 띄운다."""
    if not _HAS_QGIS:
        return
    try:
        QgsMessageLog.logMessage(msg, "landuse_review", Qgis.Warning)
    except Exception:
        pass


# 도형면적↔대장면적 차가 이보다 크면 이상 필지로 로그(F1). 645-1(0.03%) 같은 정상은 안 남긴다.
_AREA_DISCREPANCY_LOG = 0.05


INCLUSION_ALIASES = {
    "pnu": "필지고유번호(PNU)",
    "jibun": "지번",
    "jimok": "지목",
    "total_area": "공부면적(㎡)",
    "incl_area": "편입면적(㎡)",
    "incl_ratio": "편입률(%)",
    "remain_area": "잔여면적(㎡)",
}


def _valid(g):
    """QgsGeometry makeValid (가능하면) — 자가교차·무효 도형 방어."""
    if g is None or g.isNull() or g.isEmpty():
        return None
    try:
        g2 = g.makeValid()
        if g2 and not g2.isNull() and not g2.isEmpty():
            return g2
    except Exception:
        pass
    return g


class Status(NamedTuple):
    """3단계 산출 결과의 성격. `level` 은 UI 표시 등급(error/warning/info/success)."""
    code: str
    level: str
    message: str


def calculate_to_canvas(parcels: List[Dict], boundary_layer, *,
                        target_name: str = "편입구역",
                        parcel_layer=None, project=None) -> Tuple[List[Dict], Optional[object], Status]:
    """편입면적 산출(클라이언트 교차) + 편입구역 레이어 캔버스 등록.

    Args:
        parcels: parcel_lookup.lookup() 결과 (geom_wkt=5186 포함, is_neighbor 제외)
        boundary_layer: 5186 정합된 용지경계 폴리곤 레이어
        parcel_layer: '선택필지' 캔버스 레이어(좌표계 오지정 sanity check 용, 선택)
    Returns:
        (intersections, inclusion_layer, status)
    """
    if not _HAS_QGIS:
        return [], None, Status("no_qgis", "error", "QGIS 환경이 아닙니다.")
    if boundary_layer is None:
        return [], None, Status("no_boundary", "error", "먼저 2단계에서 용지경계를 추가하세요.")

    requested = [p for p in (parcels or []) if not p.get("is_neighbor")]
    main = [p for p in requested if p.get("geom_wkt")]
    if not main:
        # 지번을 하나도 못 찾았다. 옛 코드는 빈 결과를 돌려주고 controller 가 이를
        # "편입 0건" **성공**으로 표시했다(D3) — 오답이 성공으로 보였다.
        return [], None, Status(
            "not_found", "error",
            f"검토 대상 필지 {len(requested)}건 중 도형을 가져온 필지가 없습니다. "
            f"지번을 다시 확인하세요(편입면적을 계산할 대상이 없습니다).")

    bnd_wkt = coords.union_wkt_5186(boundary_layer)
    bnd_geom = _valid(QgsGeometry.fromWkt(bnd_wkt)) if bnd_wkt else None
    if bnd_geom is None:
        return [], None, Status("empty_boundary", "error", "용지경계 도형이 비어 있습니다.")

    intersections = []
    any_incl = False
    for p in main:
        pg = _valid(QgsGeometry.fromWkt(p.get("geom_wkt") or ""))
        if pg is None:
            continue
        geom_area = float(pg.area())               # 도형 스케일 — 편입 비율의 기준
        book, book_src = book_area_info(p)         # 대장면적 우선(표시값·분모)
        if book <= 0.0:                            # 대장·API 면적 둘 다 무효 → 도형면적
            book, book_src = geom_area, "geometry"

        inter = pg.intersection(bnd_geom)
        incl_geom = (float(inter.area())
                     if (inter and not inter.isNull() and not inter.isEmpty()) else 0.0)

        # 편입 비율은 **도형 기준**(분자·분모 같은 스케일)으로 구한다 → 완전 편입은 정확히
        # 100%, 부분 편입은 도형↔대장 면적차와 무관하게 옳다. 표시 편입면적은 그 비율을
        # 대장면적에 적용해 환산한다. 옛 코드는 분자(도형)와 분모(대장) 스케일이 섞여,
        # 도형>대장 필지의 부분 편입이 100%로 인쇄됐다(645-1이 그 경우) — F1.
        ratio = (incl_geom / geom_area) if geom_area > 0 else 0.0
        if ratio > 1.0:                            # 교차는 필지를 넘지 못한다 — 부동소수 오차 보정
            ratio = 1.0
        incl = ratio * book

        if (book_src == "ledger" and geom_area > 0
                and abs(geom_area - book) / book > _AREA_DISCREPANCY_LOG):
            _log(f"필지 {p.get('pnu')}: 도형면적({geom_area:,.1f}㎡)과 대장면적({book:,.1f}㎡) 차 "
                 f"{abs(geom_area - book) / book * 100:.1f}% — 편입 비율은 도형 기준으로 산출.")

        if incl_geom > 0:
            any_incl = True
        intersections.append({
            "pnu": p.get("pnu"),
            "jibun": p.get("jibun") or "",
            "jimok": p.get("jimok") or "",
            "total_area": book,
            "incl_area": incl,
            "incl_ratio": round(ratio * 100, 2),
            "remain_area": max(0.0, book - incl),
            "incl_wkt": inter.asWkt() if incl_geom > 0 else "",
            "included": incl_geom > 0,
            "book_area_source": book_src,          # F5 — 폴백(도형면적)이면 검토서에 표시
        })

    incl_rows = [r for r in intersections if r.get("included")]
    inclusion_layer = _make_inclusion_layer(incl_rows, target_name) if incl_rows else None
    if project is not None:
        # None 이어도 교체한다 — 그래야 이전 산출의 '편입구역' 이 남아 오해를 주지 않는다.
        # 소유 마커가 있는 '편입구역' 만 지운다 — 사용자 동명 레이어 보호(F2).
        layers.replace_owned(project, target_name, inclusion_layer)

    n_miss = len(requested) - len(main)
    miss_note = f" (도형을 못 가져온 필지 {n_miss}건은 제외)" if n_miss else ""

    if any_incl:
        total_area = sum(r["incl_area"] for r in intersections)
        status = Status("ok", "success",
                        f"편입 {len(incl_rows)}건 / 합계 {total_area:,.1f}㎡{miss_note} — "
                        f"'편입구역' 레이어·속성테이블에서 확인")
    else:
        ok, msg = coords.overlap_sanity(parcel_layer, boundary_layer)
        if not ok:
            status = Status("coord_mismatch", "error", msg)
        else:
            # 정말로 안 겹친다. 이것은 **정상 결과**다 — 삽도·검토서를 그대로 낸다(V3-8a).
            status = Status("no_inclusion", "info",
                            f"편입되는 필지가 없습니다. 검토 대상 {len(main)}건이 모두 "
                            f"용지경계 밖입니다(편입면적 0㎡){miss_note}. "
                            f"이 결과 그대로 삽도·검토서를 출력할 수 있습니다.")
    return intersections, inclusion_layer, status


def _make_inclusion_layer(rows: List[Dict], name: str):
    layer = QgsVectorLayer(f"MultiPolygon?crs=EPSG:{db_env.DB_SRID}", name, "memory")
    pr = layer.dataProvider()
    pr.addAttributes([
        QgsField("pnu", QVariant.String),
        QgsField("jibun", QVariant.String),
        QgsField("jimok", QVariant.String),
        QgsField("total_area", QVariant.Double),
        QgsField("incl_area", QVariant.Double),
        QgsField("incl_ratio", QVariant.Double),
        QgsField("remain_area", QVariant.Double),
    ])
    layer.updateFields()
    feats = []
    for r in rows:
        geom = None
        try:
            g = QgsGeometry.fromWkt(r.get("incl_wkt") or "")
            if g and not g.isNull() and not g.isEmpty():
                g.convertToMultiType()
                geom = g
        except Exception:
            geom = None
        if geom is None:
            continue
        f = QgsFeature(layer.fields())
        f.setAttributes([
            r.get("pnu", ""), r.get("jibun", ""), r.get("jimok", ""),
            float(r.get("total_area") or 0),
            float(r.get("incl_area") or 0),
            float(r.get("incl_ratio") or 0),
            float(r.get("remain_area") or 0),
        ])
        f.setGeometry(geom)
        feats.append(f)
    if feats:
        pr.addFeatures(feats)
    layer.updateExtents()
    for i, fld in enumerate(layer.fields()):
        a = INCLUSION_ALIASES.get(fld.name())
        if a:
            layer.setFieldAlias(i, a)
    symbology.style_inclusion(layer)   # 반투명 주황 채움 — 삽도에서 편입부 강조
    symbology.label_inclusion_canvas(layer)   # 캔버스에 편입면적(A=㎡) 라벨 표시
    return layer
