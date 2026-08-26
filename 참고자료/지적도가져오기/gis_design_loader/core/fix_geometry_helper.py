# -*- coding: utf-8 -*-
"""
도형 수정 헬퍼 - 3-tier 안전 정책

배경:
    native:fixgeometries METHOD=1 (Structure) 은 자가교차·링붕괴 폴리곤을
    LineString/GeometryCollection 으로 다운그레이드한다.
    "memory:" 싱크는 wkbType을 첫 피처 기준으로 고정하므로, 차원이 낮아진
    피처는 geometry=null 로 저장되어 사용자에게 폴리곤이 사라지는 것처럼 보인다.

해결 정책 (3-tier):
    1. METHOD=0 (Linework) 으로 fixgeometries 실행 → 차원 보존
    2. 출력 피처 중 geometry가 null이거나 GEOS 무효인 경우 원본 raw geometry 유지
       (피처 절대 drop 금지 — 수정 #4 "레이어 사라짐 방지"와 일관성 유지)
    3. skip_fix=True 이면 fixgeometries 자체를 건너뜀
       (DB 신뢰 공간쿼리 레이어: al_d160, n3a_a0110020 등 query_type="spatial")

사용 예:
    from .fix_geometry_helper import safe_fix_geometries
    fixed = safe_fix_geometries(clipped_layer, skip_fix=False)
"""

from qgis.core import (
    QgsVectorLayer,
    QgsProcessingFeedback,
    QgsMessageLog,
    Qgis,
    QgsFeature,
    QgsGeometry,
)

LOG_TAG = "GISDesignLoader"
FALLBACK_TAG = "GISDesignLoader/FixFallback"


def safe_fix_geometries(layer, skip_fix=False, log_tag=None):
    """3-tier 안전 도형 수정

    Args:
        layer (QgsVectorLayer): 입력 벡터 레이어
        skip_fix (bool): True이면 fixgeometries 건너뜀 (DB 신뢰 레이어용)
        log_tag (str): 로그 태그 (기본값: "GISDesignLoader")

    Returns:
        QgsVectorLayer: 수정된 레이어 (실패 시 원본 반환, None 반환 안 함)
    """
    tag = log_tag or LOG_TAG

    if not isinstance(layer, QgsVectorLayer):
        return layer

    # Tier 3: DB 신뢰 공간쿼리 레이어 — fixgeometries 스킵
    if skip_fix:
        QgsMessageLog.logMessage(
            f"[FixFallback] skip_fix=True, fixgeometries 건너뜀: {layer.name()}",
            tag, Qgis.Info,
        )
        return layer

    try:
        import processing

        # Tier 1: METHOD=0 (Linework) — 차원 보존
        result = processing.run(
            "native:fixgeometries",
            {
                "INPUT": layer,
                "METHOD": 0,  # Linework — 폴리곤 차원 보존 (Structure는 다운그레이드 위험)
                "OUTPUT": "memory:" + layer.name(),
            },
            feedback=QgsProcessingFeedback(),
        )
        output = result["OUTPUT"]

        if not isinstance(output, QgsVectorLayer) or not output.isValid():
            QgsMessageLog.logMessage(
                f"[FixFallback] fixgeometries 출력 무효, 원본 반환: {layer.name()}",
                FALLBACK_TAG, Qgis.Warning,
            )
            return layer

        # Tier 2: geometry null·무효 피처 감지 → 원본 geometry로 교체 (drop 금지)
        # 원본 피처를 pk 또는 순서로 매핑
        orig_features = {f.id(): f for f in layer.getFeatures()}
        fixed_features = list(output.getFeatures())

        fallback_count = 0
        output.startEditing()
        for feat in fixed_features:
            geom = feat.geometry()
            needs_fallback = geom is None or geom.isNull() or not geom.isGeosValid()
            if needs_fallback:
                orig_feat = orig_features.get(feat.id())
                if orig_feat is not None:
                    orig_geom = orig_feat.geometry()
                    pk_val = feat.id()
                    # pk 컬럼 값이 있으면 로그에 포함
                    QgsMessageLog.logMessage(
                        f"[FixFallback] geometry null/무효 → 원본 유지 "
                        f"(fid={pk_val}): {layer.name()}",
                        FALLBACK_TAG, Qgis.Warning,
                    )
                    output.changeGeometry(feat.id(), orig_geom)
                    fallback_count += 1
        output.commitChanges()

        if fallback_count > 0:
            QgsMessageLog.logMessage(
                f"[FixFallback] 원본 도형 복원: {fallback_count}건 / {layer.name()}",
                FALLBACK_TAG, Qgis.Warning,
            )

        output.dataProvider().createSpatialIndex()
        return output

    except Exception as e:
        QgsMessageLog.logMessage(
            f"[FixFallback] fixgeometries 예외 → 원본 반환: {layer.name()} — {e}",
            FALLBACK_TAG, Qgis.Warning,
        )
        return layer
