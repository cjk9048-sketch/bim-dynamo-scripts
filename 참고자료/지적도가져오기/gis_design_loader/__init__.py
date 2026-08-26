# -*- coding: utf-8 -*-
"""
GIS Design Loader - QGIS Plugin
GIS 설계 데이터 통합 로드 플러그인
"""


def classFactory(iface):
    """QGIS Plugin 로드 함수

    Args:
        iface: QgisInterface 인스턴스

    Returns:
        CivilPlanner: 플러그인 인스턴스
    """
    from .plugin import CivilPlanner
    return CivilPlanner(iface)
