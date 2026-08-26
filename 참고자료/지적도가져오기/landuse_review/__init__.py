# -*- coding: utf-8 -*-
"""민원검토 도구 (landuse_review) — QGIS Plugin

도로 설계 중 발생하는 민원 검토서 작성을 자동화한다:
지번 입력 → 토지소유정보 수집 → 용지경계 입력 → 편입면적 산출 →
삽도(揷圖) 자동 생성 → HWPX(한컴오피스 표준문서) 검토서 초안 출력.

발주: 도화엔지니어링 도로부 / 개발: 기술개발연구원 AX팀
"""
import os
import sys

# 동봉(vendored) 패키지 경로를 sys.path 앞에 추가 — python-hwpx 등.
# lxml은 QGIS 번들(5.3.0)을 사용하므로 동봉하지 않음.
_VENDOR_DIR = os.path.join(os.path.dirname(__file__), "_vendor")
if os.path.isdir(_VENDOR_DIR) and _VENDOR_DIR not in sys.path:
    sys.path.insert(0, _VENDOR_DIR)


def classFactory(iface):
    """QGIS 진입점."""
    from .plugin import LanduseReview
    return LanduseReview(iface)
