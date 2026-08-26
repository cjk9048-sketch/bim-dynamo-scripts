# -*- coding: utf-8 -*-
"""core — 민원검토 비즈니스 로직 모듈.

controller        : 워크플로 오케스트레이터 (UI 액션 → 모듈 호출 → 결과 표시)
parcel_lookup     : M1 지번 조회 (자연어 지번 → PNU → 연속지적도)
owner_collector   : M2 토지소유정보 수집 + PII 마스킹
boundary_input    : M3 용지경계 입력 (레이어 / SHP / 직접 그리기)
area_calculator   : M4 편입면적 산출 (QGIS 클라이언트 교차 — DB 재조회 없음)
inset_renderer    : M5a 삽도 PNG 생성 (QGIS Print Layout)
report_exporter   : M5b HWPX 검토서 초안 출력 (python-hwpx, 동봉)
"""
