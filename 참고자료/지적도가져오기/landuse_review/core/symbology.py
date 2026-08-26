# -*- coding: utf-8 -*-
"""분석 레이어 4종 심볼 — 캔버스·삽도(揷圖) 공통.

메모리 레이어 기본 랜덤 스타일이면 삽도에서 선택필지/용지경계/편입구역이 서로 구분되지
않으므로, 한곳에서 명시 심볼을 입힌다.

  · 선택필지   : 거의 투명한 회색 채움 + 진회색 실선(필지 전체 경계). 배경(위성/수치지형)이 비침.
  · 인접필지   : 채움 없음 + 연회색 점선(보조 시각화).
  · 용지경계   : 채움 없음 + 굵은 파란 실선(사업경계 — 편입구역 주황과 대비).
  · 편입구역   : 반투명 주황 채움 + 진주황 실선(실제 편입되는 부분 강조).
"""
try:
    from qgis.core import (
        Qgis, QgsWkbTypes,
        QgsFillSymbol, QgsLineSymbol, QgsLinePatternFillSymbolLayer,
        QgsPalLayerSettings, QgsTextFormat, QgsTextBufferSettings,
        QgsVectorLayerSimpleLabeling, QgsRuleBasedLabeling, QgsSimpleLineCallout,
        QgsUnitTypes,
    )
    from qgis.PyQt.QtGui import QColor
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False


def _apply(layer, props):
    if not _HAS_QGIS or layer is None:
        return layer
    try:
        sym = QgsFillSymbol.createSimple(props)
        r = layer.renderer()
        if r is not None and hasattr(r, "setSymbol"):
            r.setSymbol(sym)
            layer.triggerRepaint()
    except Exception:
        pass
    return layer


def style_parcel(layer):
    """선택필지 — 거의 투명한 채움(선택·식별 가능) + 진회색 실선."""
    return _apply(layer, {
        "color": "51,51,51,25",
        "outline_color": "51,51,51,255",
        "outline_width": "0.4",
        "outline_width_unit": "MM",
    })


def style_neighbor(layer):
    """인접필지 — 채움 없음 + 연회색 점선."""
    return _apply(layer, {
        "style": "no",
        "outline_color": "136,136,136,255",
        "outline_style": "dash",
        "outline_width": "0.25",
        "outline_width_unit": "MM",
    })


def style_cadastral_background(layer):
    """Inset-only cadastral overlay: no fill + muted gray outlines."""
    return _apply(layer, {
        "style": "no",
        "outline_color": "90,90,90,175",
        "outline_width": "0.18",
        "outline_width_unit": "MM",
    })


def style_boundary(layer):
    """용지경계 — 채움 없음 + 굵은 파란 실선(사업경계)."""
    return _apply(layer, {
        "style": "no",
        "outline_color": "21,101,192,255",
        "outline_width": "0.7",
        "outline_width_unit": "MM",
    })


def style_inclusion(layer):
    """편입구역 — 반투명 주황 채움 + 진주황 실선."""
    return _apply(layer, {
        "color": "255,87,34,110",
        "outline_color": "216,67,21,255",
        "outline_width": "0.4",
        "outline_width_unit": "MM",
    })


# ─────────────────────────────────────────────────────────────────────────
# 삽도(揷圖) 전용 스타일·주기(라벨+지시선) — 도로부 표준 "용지 편입현황" 정합.
# 캔버스 편집용 스타일(위)과 분리 — 삽도 렌더 시 복제 레이어에만 적용한다.
#   · 용지경계(예정)  : 빨간 실선 + '용지경계(예정)' 주기(지시선)
#   · 편입예정지      : 파란 외곽 + 빗금 채움 + 지번 라벨 + '편입예정지(A=○○㎡)' 주기
# 색·문구는 목표 이미지(03_참조자료/삽도/목표_용지편입현황_삽도.png) 재현.
# ─────────────────────────────────────────────────────────────────────────

_RED = "227,26,28"        # 용지경계
_BLUE = "13,71,161"       # 편입예정지 외곽·주기·빗금


def _text_format(size_pt, rgb, *, bold=False, buffer=True):
    fmt = QgsTextFormat()
    fmt.setSize(size_pt)
    fmt.setSizeUnit(QgsUnitTypes.RenderPoints)
    r, g, b = [int(x) for x in rgb.split(",")]
    fmt.setColor(QColor(r, g, b))
    try:
        f = fmt.font(); f.setBold(bold); fmt.setFont(f)
    except Exception:
        pass
    if buffer:
        buf = QgsTextBufferSettings()
        buf.setEnabled(True)
        buf.setSize(1.0)
        buf.setColor(QColor(255, 255, 255))
        fmt.setBuffer(buf)
    return fmt


def _callout(color, width=0.3):
    """라벨 → 대상 도형 지시선(callout)."""
    c = QgsSimpleLineCallout()
    c.setEnabled(True)
    try:
        c.setLineSymbol(QgsLineSymbol.createSimple(
            {"color": color, "width": str(width), "width_unit": "MM"}))
    except Exception:
        pass
    return c


def _label_settings(expr, fmt, *, placement=None, callout_color=None,
                    dist=0.0, offset=None, priority=None, show_all=False):
    s = QgsPalLayerSettings()
    s.fieldName = expr
    s.isExpression = True
    s.setFormat(fmt)
    if placement is not None:
        s.placement = placement
    if priority is not None:
        s.priority = priority          # 0~10, 높을수록 충돌 시 살아남음
    if show_all:
        # 다른 라벨과 겹쳐도 반드시 표시(면적 주기가 조밀한 필지에서 생략되지 않게).
        try:
            s.displayAll = True
        except Exception:
            pass
    if dist:
        s.dist = dist
    if offset is not None:
        # 라벨을 도형 밖으로 밀어 지시선이 보이게 (mm)
        try:
            s.xOffset, s.yOffset = offset
            s.offsetUnits = Qgis.RenderUnit.Millimeters
        except Exception:
            pass
    if callout_color is not None:
        s.setCallout(_callout(callout_color))
        try:
            s.lineSettings().setOverrunDistance(0)
        except Exception:
            pass
    return s


def label_parcel_canvas(layer):
    """캔버스 선택필지 라벨 — 지번(작업 화면에서 바로 확인). 삽도와 별개.

    사용자는 QGIS 캔버스에서 작업하므로, 편입 산출 즉시 지번·면적이 화면에 보여야 한다.
    (삽도 PNG 는 최종 출력물이고 도로부 표준 주기가 따로 붙는다.)
    """
    if not _HAS_QGIS or layer is None:
        return layer
    try:
        s = _label_settings('"jibun"', _text_format(9, "31,41,55", bold=True),
                            placement=Qgis.LabelPlacement.Horizontal, show_all=True)
        layer.setLabeling(QgsVectorLayerSimpleLabeling(s))
        layer.setLabelsEnabled(True)
    except Exception:
        pass
    return layer


def label_inclusion_canvas(layer):
    """캔버스 편입구역 라벨 — 편입면적(A=○○㎡). 지번은 선택필지가 담당(중복 방지)."""
    if not _HAS_QGIS or layer is None:
        return layer
    try:
        expr = "'A=' || format_number(\"incl_area\", 0) || '㎡'"
        s = _label_settings(expr, _text_format(8, "216,67,21", bold=True),
                            placement=Qgis.LabelPlacement.OverPoint,
                            show_all=True, priority=10)
        layer.setLabeling(QgsVectorLayerSimpleLabeling(s))
        layer.setLabelsEnabled(True)
    except Exception:
        pass
    return layer


def style_boundary_inset(layer):
    """삽도용 용지경계 — 빨간 선(폴리곤이면 투명 채움+빨강 외곽) + '용지경계(예정)' 주기."""
    if not _HAS_QGIS or layer is None:
        return layer
    try:
        is_polygon = layer.geometryType() == QgsWkbTypes.PolygonGeometry
    except Exception:
        is_polygon = True
    try:
        if is_polygon:
            # 용지경계는 boundary_input 이 폴리곤화한다 → 채움 없이 빨강 외곽만.
            sym = QgsFillSymbol.createSimple(
                {"color": "0,0,0,0", "outline_color": _RED, "outline_width": "0.6",
                 "outline_width_unit": "MM"})
        else:
            sym = QgsLineSymbol.createSimple(
                {"color": _RED, "width": "0.6", "width_unit": "MM"})
        layer.renderer().setSymbol(sym)
    except Exception:
        pass
    fmt = _text_format(9, _RED, bold=True)
    s = _label_settings("'용지경계(예정)'", fmt,
                        placement=Qgis.LabelPlacement.Line,
                        callout_color=_RED)
    try:
        s.lineSettings().setPlacementFlags(Qgis.LabelLinePlacementFlag.AboveLine)
    except Exception:
        pass
    layer.setLabeling(QgsVectorLayerSimpleLabeling(s))
    layer.setLabelsEnabled(True)
    return layer


def style_inclusion_inset(layer):
    """삽도용 편입구역 — 파란 외곽 + 빗금 채움 + 지번 라벨 + '편입예정지(A=㎡)' 주기.

    rule-based 라벨 2룰: ① 지번(폴리곤 중앙) ② 편입예정지 면적 주기(지시선).
    """
    if not _HAS_QGIS or layer is None:
        return layer
    # 파란 외곽 + 빗금 채움
    try:
        sym = QgsFillSymbol.createSimple(
            {"color": "0,0,0,0", "outline_color": _BLUE, "outline_width": "0.5",
             "outline_width_unit": "MM"})
        hatch = QgsLinePatternFillSymbolLayer()
        r, g, b = [int(x) for x in _BLUE.split(",")]
        hatch.setColor(QColor(r, g, b))
        hatch.setLineWidth(0.15)
        hatch.setDistance(1.6)
        hatch.setLineAngle(45)
        try:
            hatch.setLineWidthUnit(QgsUnitTypes.RenderMillimeters)
            hatch.setDistanceUnit(QgsUnitTypes.RenderMillimeters)
        except Exception:
            pass
        sym.appendSymbolLayer(hatch)
        layer.renderer().setSymbol(sym)
    except Exception:
        pass

    # rule-based 라벨: 지번 + 면적 주기
    try:
        root = QgsRuleBasedLabeling.Rule(None)

        # ① 지번(있으면) — 폴리곤 중앙, 검정. 면적 주기에 자리를 양보(priority 낮음).
        jibun = _label_settings('"jibun"', _text_format(9, "0,0,0"),
                                placement=Qgis.LabelPlacement.Horizontal,
                                priority=4)
        rule_jibun = QgsRuleBasedLabeling.Rule(jibun)
        rule_jibun.setFilterExpression("coalesce(\"jibun\",'') <> ''")
        root.appendChild(rule_jibun)

        # ② 편입예정지 면적 주기 — 지시선(파랑), 도형 아래로 밀어냄.
        # **반드시 표시**(show_all): 조밀한 필지(산8 등)에서 충돌로 생략되던 것을 막는다.
        area_expr = ("'편입예정지(A=' || format_number(\"incl_area\", 0) || '㎡)'")
        area = _label_settings(area_expr, _text_format(8, _BLUE, bold=True),
                               placement=Qgis.LabelPlacement.OverPoint,
                               callout_color=_BLUE, offset=(0.0, 10.0),
                               priority=10, show_all=True)
        rule_area = QgsRuleBasedLabeling.Rule(area)
        root.appendChild(rule_area)

        layer.setLabeling(QgsRuleBasedLabeling(root))
        layer.setLabelsEnabled(True)
    except Exception:
        pass
    return layer
