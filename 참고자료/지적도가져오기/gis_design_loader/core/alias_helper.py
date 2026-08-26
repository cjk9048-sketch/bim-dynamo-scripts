# -*- coding: utf-8 -*-
"""
alias_helper.py
---------------
레이어 필드에 한글 별칭(alias)을 적용하는 유틸리티.

사용 방법:
    from ..core.alias_helper import apply_korean_aliases
    count = apply_korean_aliases(layer, layer_name="건물통합정보")

동작 방식:
    1. resources/column_aliases.json 에서 table_name 기준 매핑 조회
    2. 없으면 layer_loader.FIELD_ALIASES 에서 레이어 한글명 기준 매핑 조회
    3. 매칭된 필드마다 layer.setFieldAlias() 호출
    4. JSON 파일이 없거나 table_name 미매핑이어도 조용히 no-op 처리
"""

import json
import os

from qgis.core import QgsMessageLog, Qgis

# ── 로그 태그 ─────────────────────────────────────────────────────────────────
LOG_TAG = "GISDesignLoader/Alias"

# ── QML/Expression이 원본 컬럼명을 참조하는 레이어 ───────────────────────────
# 여기에 등록된 컬럼은 실제 필드명을 유지(rename 제외)하고 alias로만 한글 표시.
# v1.3.0: 토지소유정보.qml을 한글 필드명(`소유구분명`, `고유번호`)으로 편집했으므로
#         preserve 대상 없음 → 전 컬럼이 실제 이름으로 한글화됨.
PRESERVE_FIELD_NAMES = {
    # 예시) 추후 QML 편집이 어려운 외부 스타일을 사용해야 하는 경우:
    # "레이어명": ["컬럼1", "컬럼2"],
}

# ── JSON 경로 (이 파일 기준 ../resources/column_aliases.json) ─────────────────
_RESOURCES_DIR = os.path.join(os.path.dirname(__file__), '..', 'resources')
_JSON_PATH = os.path.join(_RESOURCES_DIR, 'column_aliases.json')

# ── 모듈 레벨 캐시 ────────────────────────────────────────────────────────────
_alias_cache = None   # JSON에서 로드한 {table_name: {col: korean}}
_cache_loaded = False  # 로드 시도 여부 (실패해도 재시도 안 함)


def load_alias_map():
    """
    column_aliases.json을 읽어 {table_name: {col: korean}} 딕셔너리를 반환합니다.
    모듈 레벨에서 캐싱되므로 첫 호출 이후에는 파일을 다시 읽지 않습니다.

    Returns:
        dict: 테이블명 → 컬럼 별칭 딕셔너리. 로드 실패 시 빈 dict.
    """
    global _alias_cache, _cache_loaded
    if _cache_loaded:
        return _alias_cache or {}

    _cache_loaded = True
    json_path = os.path.normpath(_JSON_PATH)

    if not os.path.exists(json_path):
        QgsMessageLog.logMessage(
            f"column_aliases.json 없음 (경로: {json_path}) — 별칭 적용 건너뜀",
            LOG_TAG, Qgis.Info
        )
        _alias_cache = {}
        return _alias_cache

    try:
        with open(json_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        _alias_cache = data.get('tables', {})
        total_cols = sum(len(v) for v in _alias_cache.values())
        QgsMessageLog.logMessage(
            f"column_aliases.json 로드 완료: {len(_alias_cache)}개 테이블, {total_cols}개 컬럼",
            LOG_TAG, Qgis.Info
        )
    except Exception as e:
        QgsMessageLog.logMessage(
            f"column_aliases.json 로드 오류: {e}",
            LOG_TAG, Qgis.Warning
        )
        _alias_cache = {}

    return _alias_cache


def _get_field_aliases_fallback(layer_name):
    """
    layer_loader.FIELD_ALIASES에서 레이어 한글명으로 조회합니다.
    JSON 미매핑 시 폴백으로 사용됩니다.

    Args:
        layer_name: AVAILABLE_LAYERS의 'name' 값 (예: "건물통합정보")

    Returns:
        dict: {col_lower: korean} 또는 빈 dict
    """
    try:
        from .layer_loader import FIELD_ALIASES
        return FIELD_ALIASES.get(layer_name, {})
    except Exception:
        return {}


def apply_korean_aliases(layer, layer_name, table_name=None):
    """
    QgsVectorLayer 필드명을 한글로 변경(또는 alias 설정).

    동작:
      1) PRESERVE_FIELD_NAMES에 등록된 컬럼(예: 토지소유정보의 a0/a8)은
         실제 필드명은 유지, alias만 한글로 설정 (QML/expression 호환 보존)
      2) 그 외 컬럼은 provider가 허용하면 실제 필드명을 한글로 rename
         (memory / ogr provider에서만 동작 — postgres read-only는 건너뜀)
      3) rename 실패/미지원 시 setFieldAlias로 폴백 (기존 동작과 동일)

    조회 우선순위:
        1. column_aliases.json — table_name 기준 (예: "al_d160")
        2. layer_loader.FIELD_ALIASES — layer_name 기준 (예: "토지소유정보")

    Args:
        layer: QgsVectorLayer 인스턴스
        layer_name: 레이어 한글명 (AVAILABLE_LAYERS의 'name' 필드)
        table_name: PostgreSQL 테이블명 (예: "al_d160"). None이면 JSON 조회 건너뜀.

    Returns:
        int: 이름이 한글화된 필드 수 (rename + alias 합계)
    """
    # raster 레이어는 필드 없음 → skip
    try:
        from qgis.core import QgsVectorLayer as _QgsVL
        if not isinstance(layer, _QgsVL):
            return 0
    except Exception:
        pass

    # 별칭 매핑 결정
    col_map = {}

    # 1순위: JSON (table_name 기준)
    if table_name:
        alias_map = load_alias_map()
        col_map = alias_map.get(table_name, {})

    # 2순위: FIELD_ALIASES (layer_name 기준) — JSON 미매핑 시 폴백
    if not col_map and layer_name:
        col_map = _get_field_aliases_fallback(layer_name)

    if not col_map:
        return 0

    # QML/Expression 호환을 위해 원본명을 유지할 컬럼 목록
    preserve = set(n.lower() for n in PRESERVE_FIELD_NAMES.get(layer_name, []))

    # ── 1) 안전한 provider에서 실제 rename 시도 ────────────────────────────
    # postgres 레이어에 renameAttributes를 호출하면 DB 스키마 변경을 시도하므로
    # 반드시 memory / ogr 로 한정한다 (waterviewer는 읽기 전용이라 어차피 실패).
    provider = layer.dataProvider()
    provider_name = ""
    try:
        provider_name = provider.name() if provider else ""
    except Exception:
        provider_name = ""

    rename_count = 0
    if provider_name in ("memory", "ogr"):
        try:
            from qgis.core import QgsVectorDataProvider
            caps = provider.capabilities()
            if caps & QgsVectorDataProvider.RenameAttributes:
                rename_map = {}
                for i, field in enumerate(layer.fields()):
                    fname = field.name().lower()
                    if fname in preserve:
                        continue  # 원본명 유지
                    if fname in col_map and field.name() != col_map[fname]:
                        rename_map[i] = col_map[fname]
                if rename_map:
                    if provider.renameAttributes(rename_map):
                        layer.updateFields()
                        rename_count = len(rename_map)
        except Exception as e:
            QgsMessageLog.logMessage(
                f"renameAttributes 실패 ({layer_name}, provider={provider_name}): {e}",
                LOG_TAG, Qgis.Warning
            )

    # ── 2) 남은 필드(= preserve 대상 + rename 미지원 provider)에 alias 적용 ──
    alias_count = 0
    for i, field in enumerate(layer.fields()):
        fname = field.name().lower()
        if fname in col_map:
            desired_alias = col_map[fname]
            # 이미 rename되어 name == alias면 alias 중복 설정 생략
            if field.name() == desired_alias:
                continue
            layer.setFieldAlias(i, desired_alias)
            alias_count += 1

    total = rename_count + alias_count
    if total > 0:
        QgsMessageLog.logMessage(
            f"{layer_name}: 필드명 한글화 rename={rename_count} + alias={alias_count} "
            f"(provider={provider_name or '?'}, preserve={sorted(preserve) or '-'})",
            LOG_TAG, Qgis.Info
        )

    return total
