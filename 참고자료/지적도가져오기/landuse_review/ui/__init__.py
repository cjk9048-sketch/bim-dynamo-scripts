# -*- coding: utf-8 -*-
"""ui — 민원검토 도구 위자드 UI.

wizard_dialog : 메인 위자드 (4단계 QStackedWidget, gis_design_loader_v2 패턴)
step1_parcel  : 1단계 지번 입력 + 캔버스 추가
step2_boundary: 2단계 용지경계 입력 (레이어/SHP/그리기)
step3_intersect: 3단계 편입면적 산출 (ST_Intersection)
step4_export  : 4단계 산출물 출력 (삽도 PNG + HWPX)
styles        : 공통 스타일시트
"""
