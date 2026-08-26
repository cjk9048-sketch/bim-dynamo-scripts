# -*- coding: utf-8 -*-
"""데이터 출처 전환 — 한 곳에서 DB↔API 결정.

기획서 v2 §6.1 / 1~2단계 설계검토 §3: "API 우선(키 후) / DB 임시(키 전)".
→ **키 유무가 곧 스위치**다. 기본 "AUTO": V-World 키가 설정돼 있으면 API,
없으면 DB. 강제하려면 "DB"/"API" 로 둔다(테스트·오프라인용).

데이터 경로 분기는 반드시 `resolve_use_api()`만 보고 결정한다(분기 로직 분산 금지).
"""

# "AUTO" = 키 있으면 API·없으면 DB (기본·권장)
# "DB"   = 항상 사내 PROD PostGIS 강제
# "API"  = 항상 V-World API 강제(키 필요)
DATA_SOURCE = "AUTO"


def resolve_use_api(has_key: bool) -> bool:
    """현재 데이터 출처가 API 인지 — has_key(키 보유 여부)와 DATA_SOURCE 로 결정."""
    s = str(DATA_SOURCE).strip().upper()
    if s == "API":
        return True
    if s == "DB":
        return False
    return bool(has_key)   # AUTO


def source_label(has_key: bool = False) -> str:
    """사용자 표시용 출처 라벨."""
    return "V-World API(최신본)" if resolve_use_api(has_key) else "사내 DB(적재본)"
