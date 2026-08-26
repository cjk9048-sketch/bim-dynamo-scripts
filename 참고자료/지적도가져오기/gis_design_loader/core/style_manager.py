# -*- coding: utf-8 -*-
"""
스타일 매니저 - QML 파일 기반 자동 스타일 적용
QML 우선, XML 심볼 라이브러리 폴백
"""

import os
import xml.etree.ElementTree as ET
from qgis.core import QgsVectorLayer, QgsRasterLayer, QgsReadWriteContext
from qgis.PyQt.QtXml import QDomDocument


# 레이어 이름 → QML 파일명 매핑 (확장자 없이)
LAYER_QML_MAP = {
    # 배경_지형
    "DEM 90m": "dem_90m",
    "등고선": "등고선",
    # 도로/교통
    "도로중심선": "검은색_선",
    "도로경계_면": "도로경계_면",
    "교량": "교량",
    "터널": "터널",
    # 수자원
    "실폭하천": "실폭하천",
    "하천중심선": "검은색_선",
    "하천용도구역": "소하천",
    "소하천": "소하천",
    "호수 및 저수지": "호수_및_저수지",
    # 건물/시설
    "건물통합정보": "건물통합정보",
    # 토지
    "토지소유정보": "토지소유정보",
    # 산업단지
    "단지경계": "단지경계",
    "단지시설용지": "단지시설용지",
    "단지용도지역": "단지경계",
    # 개발사업
    "개발제한구역": "개발제한구역",
    "개발진흥지구": "개발진흥지구",
    "도시개발구역": "도시개발구역",
    "택지개발": "택지개발",
    "혁신도시": "혁신도시",
    # 행정구역
    "행정경계_시도": "행정경계_시도",
    "행정경계_시군구": "행정경계_시군구",
    "행정경계_읍면동": "행정경계_읍면동",
    "행정경계_법정읍면동": "행정경계_법정읍면동",   # v1.1.0: 누락분 추가 (행정경계_법정읍면동.qml = 행정경계_읍면동.qml 동일)
    "행정경계_법정리": "행정경계_법정리",
}

# XML 심볼 폴백 매핑 (QML 없을 때)
LAYER_STYLE_MAP = {
    # 지장물 (로컬 로드용 — QML 없음, XML 심볼 사용)
    "지장물_가스": "지장물_가스",
    "지장물_고압전기": "지장물_고압전기",
    "지장물_광역상수": "지장물_광역상수",
    "지장물_난방": "지장물_난방",
    "지장물_전기_저압": "지장물_전기_저압",
    "지장물_지방상수": "지장물_지방상수",
    "지장물_통신": "지장물_통신",
    "지장물_하수": "지장물_하수",
    # 관로
    "관로_계획": "관로_계획",
    "관로_기존": "관로_기존",
    "계획 노선": "관로_계획",
}


class StyleManager:
    """QML 파일 기반 스타일 적용 (XML 심볼 폴백)"""

    def __init__(self):
        base_dir = os.path.dirname(os.path.dirname(__file__))
        self.qml_dir = os.path.join(base_dir, "styles", "qml")
        self.style_xml_path = os.path.join(base_dir, "styles", "qgis_layer_style_library.xml")
        self._symbol_cache = {}
        self._xml_loaded = False

    def apply_style_to_layer(self, layer, layer_name=None):
        """레이어에 스타일 적용 (QML 우선 → XML 폴백)"""
        name = layer_name or layer.name()

        # 1) QML 매핑 시도
        qml_name = LAYER_QML_MAP.get(name)
        if not qml_name:
            # 부분 매칭
            for key, val in LAYER_QML_MAP.items():
                if key in name or name in key:
                    qml_name = val
                    break

        if qml_name:
            qml_path = os.path.join(self.qml_dir, f"{qml_name}.qml")
            if os.path.exists(qml_path):
                msg, success = layer.loadNamedStyle(qml_path)
                if success:
                    layer.triggerRepaint()
                    return True

        # 2) XML 심볼 폴백 (지장물 등)
        if isinstance(layer, QgsVectorLayer):
            symbol_name = LAYER_STYLE_MAP.get(name)
            if not symbol_name:
                for key, val in LAYER_STYLE_MAP.items():
                    if key in name or name in key:
                        symbol_name = val
                        break
            if symbol_name:
                return self._apply_symbol_from_xml(layer, symbol_name)

        return False

    def _ensure_xml_loaded(self):
        """XML 파싱 (최초 1회)"""
        if self._xml_loaded:
            return
        if not os.path.exists(self.style_xml_path):
            return
        try:
            tree = ET.parse(self.style_xml_path)
            root = tree.getroot()
            symbols = root.find("symbols")
            if symbols is not None:
                for sym in symbols.findall("symbol"):
                    sym_name = sym.get("name", "")
                    if sym_name and not sym_name.startswith("@"):
                        self._symbol_cache[sym_name] = sym
            self._xml_loaded = True
        except Exception:
            pass

    def _apply_symbol_from_xml(self, layer, symbol_name):
        """XML의 심볼을 QgsVectorLayer에 적용"""
        self._ensure_xml_loaded()
        if symbol_name not in self._symbol_cache:
            return False
        try:
            sym_elem = self._symbol_cache[symbol_name]
            xml_str = ET.tostring(sym_elem, encoding="unicode")
            doc = QDomDocument()
            doc.setContent(xml_str)
            sym_node = doc.documentElement()
            context = QgsReadWriteContext()
            from qgis.core import QgsSymbolLayerUtils
            symbol = QgsSymbolLayerUtils.loadSymbol(sym_node, context)
            if symbol is not None:
                layer.renderer().setSymbol(symbol)
                layer.triggerRepaint()
                return True
        except Exception:
            pass
        return False

    def get_available_symbols(self):
        """사용 가능한 심볼 이름 목록"""
        self._ensure_xml_loaded()
        return list(self._symbol_cache.keys())
