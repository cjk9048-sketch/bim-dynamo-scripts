# -*- coding: utf-8 -*-
"""
전처리 엔진 - 레이어 클리핑 + 도형 수정 + 인코딩 설정
"""

from qgis.core import (
    QgsVectorLayer, QgsRasterLayer,
    QgsProcessingFeedback, QgsProcessingContext,
    QgsFeatureRequest,
    QgsTask, QgsApplication, QgsProject,
    QgsMessageLog, Qgis,
)

from .layer_loader import AVAILABLE_LAYERS
from .fix_geometry_helper import safe_fix_geometries
from .alias_helper import apply_korean_aliases

# query_type="spatial" 레이어 이름 집합 — fixgeometries 스킵 대상
_SPATIAL_QUERY_LAYER_NAMES = frozenset(
    l["name"] for l in AVAILABLE_LAYERS if l.get("query_type") == "spatial"
)

# function_name 이 "_clip" 으로 끝나는 레이어 = 서버측 DB 함수가 ST_Intersection 으로 이미 경계내로 자름.
# 이런 레이어는 클라이언트 clip 이 0건/실패해도 DB 반환본을 그대로 써도 안전(경계 밖 데이터 없음).
_SERVER_CLIPPED_LAYER_NAMES = frozenset(
    l["name"] for l in AVAILABLE_LAYERS
    if str(l.get("function_name", "")).endswith("_clip")
)
_LAYER_GROUP_BY_NAME = {l["name"]: l.get("group", "") for l in AVAILABLE_LAYERS}


def _may_keep_raw_on_clip_fail(name: str) -> bool:
    """클라이언트 native:clip 이 0건/실패했을 때 DB 반환본을 그대로 추가해도 되는가?

    전제(v1.2.0~): build_*_uri 가 작업 범위 폴리곤으로 서버측 1차 필터(`ST_Intersects(geom, 범위)`)를
    하므로, 로드된 raw feature 는 모두 *범위와 실제로 겹치는* 것들이다. 따라서 clip 이 실패해도
    범위와 무관한 데이터가 통째로 들어오는 일은 없다 (그게 v1.1.0 까지 있던 누수 — 이제 차단됨).
    여기서 raw 를 유지하면 기껏해야 *범위 경계를 살짝 가로지르는 feature 가 잘리지 않고 통째로* 나오는 정도.

    - 서버측 `*_clip` 함수 결과 / 그 외 일반 레이어 → 원본 유지 OK
    - `query_type="spatial"` 레이어(대용량 — 건물통합정보 14.4M, 토지소유정보 39.8M 등) → 보수적으로 제외.
      도형 깨짐 등으로 clip 이 통째로 실패하는 드문 경우, 잘리지 않은 통 레이어를 띄우느니 빼는 게 안전.
    - `range_wkt` 계산 실패로 bbox 폴백이 일어난 경우: spatial 제외 정책이 그 옛 누수도 그대로 막아준다.
    """
    if name in _SERVER_CLIPPED_LAYER_NAMES:
        return True
    if name in _SPATIAL_QUERY_LAYER_NAMES:
        return False
    return True


LOG_TAG = "GISDesignLoader"

# v1.4.1: AVAILABLE_LAYERS에 정의된 레이어 이름만 중복 제거 대상
# 작업범위 등 사용자 관리 레이어는 건드리지 않음
_MANAGED_LAYER_NAMES = frozenset(l["name"] for l in AVAILABLE_LAYERS)

# 로드 과정에서 레이어 이름 뒤에 붙는 접미사 (clip_vector/clip_raster가 "{원래이름}_clip"으로 명명)
# v1.1.0: 중복 감지/제거가 이 접미사를 못 떼서 "_clip" 레이어가 두 배로 쌓이던 버그 수정
_LOAD_NAME_SUFFIXES = ("_clip",)


def base_managed_layer_name(layer_name: str) -> str:
    """프로젝트 레이어 이름에서 로드 시 붙은 접미사를 떼어 AVAILABLE_LAYERS 기준 이름으로 정규화.

    예: "행정경계_시군구_clip" → "행정경계_시군구",  "DEM 90m" → "DEM 90m"
    AVAILABLE_LAYERS 에 그 base 이름이 없으면 원본을 그대로 반환.
    """
    for suf in _LOAD_NAME_SUFFIXES:
        if layer_name.endswith(suf) and len(layer_name) > len(suf):
            stripped = layer_name[: -len(suf)]
            if stripped in _MANAGED_LAYER_NAMES:
                return stripped
    return layer_name


def _remove_existing_layers_by_name(name: str) -> int:
    """프로젝트에서 동일 이름(AVAILABLE_LAYERS 소속)의 기존 레이어를 제거.

    - 재로드 시 같은 이름이 누적되는 "두 배로 생성" 문제 방지
    - AVAILABLE_LAYERS 에 등록된 이름만 제거 (작업범위 등 사용자 레이어는 보존)
    - 로드 시 "{name}_clip" 으로 명명된 기존 레이어도 함께 제거 (v1.1.0)

    Args:
        name: 이번에 추가될 레이어의 기준 이름 (AVAILABLE_LAYERS 의 name)
    Returns:
        제거된 레이어 개수
    """
    if name not in _MANAGED_LAYER_NAMES:
        return 0
    try:
        project = QgsProject.instance()
        stale_ids = [
            lyr.id() for lyr in project.mapLayers().values()
            if base_managed_layer_name(lyr.name()) == name
        ]
        if not stale_ids:
            return 0
        project.removeMapLayers(stale_ids)
        QgsMessageLog.logMessage(
            f"중복 방지 - 기존 레이어 {len(stale_ids)}개 제거: {name}",
            LOG_TAG, Qgis.Info,
        )
        return len(stale_ids)
    except Exception as e:
        QgsMessageLog.logMessage(
            f"중복 제거 실패 ({name}): {e}", LOG_TAG, Qgis.Warning,
        )
        return 0


class Preprocessor:
    """벡터/래스터 레이어 전처리"""

    @staticmethod
    def clip_vector(input_layer, boundary_layer, output_name=None):
        """벡터 레이어를 범위 레이어로 클리핑

        Args:
            input_layer: 입력 벡터 레이어
            boundary_layer: 클리핑 범위 폴리곤 레이어
            output_name: 출력 레이어 이름 (None이면 원래 이름 + '_clip')

        Returns:
            QgsVectorLayer: 클리핑된 메모리 레이어 또는 None
        """
        if not isinstance(input_layer, QgsVectorLayer):
            return None

        name = output_name or f"{input_layer.name()}_clip"

        try:
            # 입력 레이어에 공간 인덱스 생성 (메모리 레이어만, PostGIS는 서버측 인덱스 사용)
            if input_layer.dataProvider().name() != 'postgres':
                input_layer.dataProvider().createSpatialIndex()
            import processing
            # 무결하지 않은 도형 스킵 (건물통합정보 등 대용량 테이블 대응)
            context = QgsProcessingContext()
            context.setInvalidGeometryCheck(
                QgsFeatureRequest.GeometrySkipInvalid
            )
            result = processing.run(
                "native:clip",
                {
                    "INPUT": input_layer,
                    "OVERLAY": boundary_layer,
                    "OUTPUT": "memory:" + name,
                },
                feedback=QgsProcessingFeedback(),
                context=context,
            )
            output = result["OUTPUT"]
            if isinstance(output, QgsVectorLayer) and output.featureCount() > 0:
                output.dataProvider().createSpatialIndex()
                return output
            return None
        except Exception as e:
            QgsMessageLog.logMessage(
                f"Clip failed for {input_layer.name()}: {e}",
                LOG_TAG, Qgis.Warning,
            )
            return None

    @staticmethod
    def clip_raster(input_layer, boundary_layer, output_name=None):
        """래스터 레이어를 범위 레이어로 클리핑

        Args:
            input_layer: 입력 래스터 레이어
            boundary_layer: 클리핑 범위 폴리곤 레이어
            output_name: 출력 레이어 이름

        Returns:
            QgsRasterLayer 또는 None
        """
        if not isinstance(input_layer, QgsRasterLayer):
            return None

        try:
            import processing
            result = processing.run(
                "gdal:cliprasterbymasklayer",
                {
                    "INPUT": input_layer,
                    "MASK": boundary_layer,
                    "CROP_TO_CUTLINE": True,
                    "KEEP_RESOLUTION": False,
                    "NODATA": -9999,
                    "SET_RESOLUTION": False,
                    "ALPHA_BAND": True,
                    "OUTPUT": "TEMPORARY_OUTPUT",
                },
                feedback=QgsProcessingFeedback(),
            )
            output_path = result["OUTPUT"]
            name = output_name or f"{input_layer.name()}_clip"
            clipped = QgsRasterLayer(output_path, name)
            if clipped.isValid():
                return clipped
            return None
        except Exception as e:
            QgsMessageLog.logMessage(
                f"Raster clip failed for {input_layer.name()}: {e}",
                LOG_TAG, Qgis.Warning,
            )
            return None

    @staticmethod
    def clip_raster_by_extent(input_layer, boundary_layer, output_name=None):
        """래스터 레이어를 범위 레이어의 bbox(사각형)로 클리핑

        폴리곤 마스크 대신 사각형 영역으로 잘라 빈 구간 없이 가져옵니다.
        """
        if not isinstance(input_layer, QgsRasterLayer):
            return None

        try:
            import processing
            ext = boundary_layer.extent()
            extent_str = f"{ext.xMinimum()},{ext.xMaximum()},{ext.yMinimum()},{ext.yMaximum()} [{boundary_layer.crs().authid()}]"
            result = processing.run(
                "gdal:cliprasterbyextent",
                {
                    "INPUT": input_layer,
                    "PROJWIN": extent_str,
                    "NODATA": -9999,
                    "OUTPUT": "TEMPORARY_OUTPUT",
                },
                feedback=QgsProcessingFeedback(),
            )
            output_path = result["OUTPUT"]
            name = output_name or input_layer.name()
            clipped = QgsRasterLayer(output_path, name)
            if clipped.isValid():
                return clipped
            return None
        except Exception as e:
            QgsMessageLog.logMessage(
                f"Raster extent clip failed for {input_layer.name()}: {e}",
                LOG_TAG, Qgis.Warning,
            )
            return None

    @staticmethod
    def fix_geometries(layer, skip_fix=False):
        """벡터 레이어의 도형 오류를 수정 (3-tier 안전 정책 적용)

        Args:
            layer: 입력 벡터 레이어
            skip_fix: True이면 fixgeometries 건너뜀 (DB 신뢰 spatial 레이어용)

        Returns:
            QgsVectorLayer: 수정된 레이어 (실패 시 원본 반환, None 반환 안 함)
        """
        if not isinstance(layer, QgsVectorLayer):
            return None

        result = safe_fix_geometries(layer, skip_fix=skip_fix)
        # safe_fix_geometries는 항상 레이어를 반환 (None 없음)
        return result if isinstance(result, QgsVectorLayer) else None

    @staticmethod
    def preprocess_layer(input_layer, boundary_layer, fix_geom=True, skip_fix=False):
        """클리핑 + 도형 수정을 한번에 수행

        Args:
            input_layer: 입력 레이어
            boundary_layer: 범위 레이어
            fix_geom: 도형 수정 여부
            skip_fix: True이면 fixgeometries 건너뜀 (query_type="spatial" 레이어용)

        Returns:
            레이어 또는 None
        """
        if isinstance(input_layer, QgsRasterLayer):
            # DEM을 작업범위 bbox(사각형)로 클리핑 (빈 구간 방지, 실패 시 원본 반환)
            clipped = Preprocessor.clip_raster_by_extent(input_layer, boundary_layer)
            return clipped if clipped is not None else input_layer

        # 벡터: 클리핑 → 도형 수정
        clipped = Preprocessor.clip_vector(input_layer, boundary_layer)
        if clipped is None:
            return None

        if fix_geom:
            fixed = Preprocessor.fix_geometries(clipped, skip_fix=skip_fix)
            if fixed is not None:
                return fixed

        return clipped

    @staticmethod
    def enrich_river_boundary(river_boundary_layer, extent_5186, region_codes,
                              range_wkt_5186=None, center_layer=None):
        """실폭하천 레이어에 하천중심선의 하천명(name)을 공간 조인으로 추가.

        성능 개선 (2026-07-07 — 이 단계가 로딩 지연의 최대 원인이었음):
          ② 재사용: center_layer(이미 배치에 로드된 하천중심선)를 그대로 사용 → DB 2차 쿼리 제거.
          ③ 생략: 재사용할 하천중심선이 없으면 무거운 단독 로드를 하지 않고 조인을 건너뜀
             (실폭하천 원본 그대로 반환 — 하천명이 없을 뿐 데이터는 정상).
          ① 인덱스: JOIN 레이어를 메모리로 복사한 뒤 클라이언트 공간 인덱스를 만들어 조인.
             postgres 쿼리레이어는 클라이언트 공간 인덱스가 없어 native:joinattributesbylocation 이
             O(실폭 × 중심선) 무인덱스 근접탐색으로 크게 느려졌음("결합 레이어 공간 인덱스 없음" 경고).
             (postgres 프로바이더에 createSpatialIndex 를 걸면 DB 인덱스를 만들려 해 R/O·DB 불변 위배 →
              반드시 메모리 사본에만 인덱스 생성)
        """
        import processing

        # ②③ 이미 로드된 하천중심선 재사용 — 없으면 조인 생략 (단독 재로드 안 함)
        if (center_layer is None or not isinstance(center_layer, QgsVectorLayer)
                or not center_layer.isValid() or center_layer.featureCount() == 0):
            QgsMessageLog.logMessage(
                "하천중심선 미로드 → 실폭하천 하천명 부여 생략 "
                "(하천중심선을 함께 로드하면 하천명이 표시됩니다)",
                LOG_TAG, Qgis.Info,
            )
            return river_boundary_layer

        # ① JOIN 레이어를 메모리로 복사(materialize) + 공간 인덱스 생성 (조인 속도 핵심)
        try:
            indexed_join = center_layer.materialize(QgsFeatureRequest())
            if isinstance(indexed_join, QgsVectorLayer) and indexed_join.isValid():
                indexed_join.dataProvider().createSpatialIndex()
            else:
                indexed_join = center_layer
        except Exception as e:
            QgsMessageLog.logMessage(
                f"하천중심선 인덱스 준비 실패, 원본으로 조인: {e}", LOG_TAG, Qgis.Warning,
            )
            indexed_join = center_layer

        # 공간 조인: 실폭하천 + 하천중심선(name 필드)
        try:
            result = processing.run("native:joinattributesbylocation", {
                'INPUT': river_boundary_layer,
                'JOIN': indexed_join,
                'PREDICATE': [0],  # intersects
                'JOIN_FIELDS': ['name'],
                'METHOD': 0,  # one-to-many → take first
                'PREFIX': '',
                'OUTPUT': 'memory:'
            }, feedback=QgsProcessingFeedback())

            output = result['OUTPUT']
            if output and output.isValid():
                output.setName(river_boundary_layer.name())
                return output
        except Exception as e:
            QgsMessageLog.logMessage(
                f"실폭하천 하천명 조인 실패, 원본 반환: {e}", LOG_TAG, Qgis.Warning,
            )

        return river_boundary_layer


class BatchPreprocessTask(QgsTask):
    """QgsTask 기반 일괄 전처리 태스크

    여러 레이어를 하나의 태스크에서 **순차적으로** 처리합니다.
    processing.run()은 동시 실행 시 크래시하므로 반드시 순차 실행해야 합니다.

    사용 예:
        task = BatchPreprocessTask(layers_with_names, boundary_layer, style_callback)
        QgsApplication.taskManager().addTask(task)
    """

    def __init__(self, layers_with_names, boundary_layer, style_callback=None,
                 extent_5186=None, region_codes=None, range_wkt_5186=None):
        """
        Args:
            layers_with_names: [(layer, name), ...] 전처리할 레이어 목록
            boundary_layer: 클리핑 범위 폴리곤 레이어
            style_callback: 완료 후 스타일 적용 콜백 (layer, name) → None
            extent_5186: QgsRectangle (EPSG:5186, 실폭하천 enrichment용)
            region_codes: 행정구역 코드 리스트 (실폭하천 enrichment용)
            range_wkt_5186: 작업 범위 폴리곤 WKT (EPSG:5186, 실폭하천 enrichment 시 하천중심선 서버측 필터용)
        """
        super().__init__("전처리 일괄 처리", QgsTask.CanCancel)
        self.layers_with_names = layers_with_names
        self.boundary_layer = boundary_layer
        self.style_callback = style_callback
        self.extent_5186 = extent_5186
        self.region_codes = region_codes or []
        self.range_wkt_5186 = range_wkt_5186
        self.results = []  # [(layer, name, success), ...]

    def run(self):
        """백그라운드 스레드에서 실행 - 순차적으로 processing.run() 호출"""
        total = len(self.layers_with_names)

        for i, (layer, name) in enumerate(self.layers_with_names):
            if self.isCanceled():
                return False

            self.setProgress((i / total) * 100)

            QgsMessageLog.logMessage(
                f"전처리 중: {name} ({i + 1}/{total})",
                LOG_TAG, Qgis.Info,
            )

            try:
                # query_type="spatial" 레이어는 DB 도형을 신뢰 → fixgeometries 스킵
                skip_fix = any(name.startswith(n) for n in _SPATIAL_QUERY_LAYER_NAMES)
                result = Preprocessor.preprocess_layer(
                    layer, self.boundary_layer, skip_fix=skip_fix
                )

                # v1.4.0: 1차 clip 실패 시 도형 수리 후 재시도
                # 호수/저수지 등의 잘못된 원본 geometry로 native:clip이 실패하면
                # ST_MakeValid 보강 후 다시 한 번 시도 → "경계 밖 원본 유지" 감소
                if result is None:
                    try:
                        fixed = Preprocessor.fix_geometries(layer, skip_fix=False)
                        if fixed is None or not isinstance(fixed, QgsVectorLayer) or not fixed.isValid():
                            QgsMessageLog.logMessage(
                                f"재clip 포기 — 도형 수리 단계 실패: {name}",
                                LOG_TAG, Qgis.Warning,
                            )
                        else:
                            retry = Preprocessor.clip_vector(fixed, self.boundary_layer)
                            if retry is not None:
                                QgsMessageLog.logMessage(
                                    f"도형 수리 후 재clip 성공: {name}",
                                    LOG_TAG, Qgis.Info,
                                )
                                result = retry
                            else:
                                QgsMessageLog.logMessage(
                                    f"재clip도 0건 반환 — DB 반환본을 사용: {name} "
                                    f"(DB 함수가 이미 경계내 clip 수행중이면 안전)",
                                    LOG_TAG, Qgis.Info,
                                )
                    except Exception as fix_err:
                        QgsMessageLog.logMessage(
                            f"도형 수리 후 재clip 예외: {name} - {fix_err}",
                            LOG_TAG, Qgis.Warning,
                        )

                if name == "실폭하천" and result is not None:
                    # ② 이미 이 배치에 로드된 하천중심선 원본을 재사용(없으면 None → enrich 가 생략)
                    center = next(
                        (lyr for lyr, nm in self.layers_with_names if nm == "하천중심선"),
                        None,
                    )
                    result = Preprocessor.enrich_river_boundary(
                        result, self.extent_5186, self.region_codes, self.range_wkt_5186,
                        center_layer=center,
                    )
                if result is not None:
                    self.results.append((result, name, True))
                else:
                    # 클라이언트 clip 이 0건/실패. 원본(DB 반환본)을 그대로 쓸지 결정:
                    #   · 서버측 *_clip 함수 또는 행정구역 경계 레이어 → 원본 유지 (경계 밖 데이터 없음/whole feature가 의도)
                    #   · 그 외 raw 테이블 ST_Intersects(bbox) 쿼리(개발사업 등) → 제외
                    #     (원본을 쓰면 사용자 범위 bbox에 걸친 *다른 시군구* polygon 전체가 그대로 들어옴 = 버그)
                    fc = layer.featureCount() if hasattr(layer, 'featureCount') else 0
                    if fc > 0 and _may_keep_raw_on_clip_fail(name):
                        QgsMessageLog.logMessage(
                            f"[알림] 클라이언트 clip 건너뜀 → DB 반환본 사용: {name} ({fc:,}건) "
                            f"— 서버측 *_clip 함수/경계 레이어라 경계 밖 데이터 없음.",
                            LOG_TAG, Qgis.Info,
                        )
                        self.results.append((layer, name, False))
                    elif fc > 0:
                        QgsMessageLog.logMessage(
                            f"clip 0건/실패 — 원본은 사용자 범위 밖(bbox 내 타 지역) 데이터를 포함할 수 있어 사용 안 함, 제외: "
                            f"{name} (raw {fc:,}건)",
                            LOG_TAG, Qgis.Warning,
                        )
                    else:
                        QgsMessageLog.logMessage(
                            f"범위 내 데이터 없음 (제외): {name}",
                            LOG_TAG, Qgis.Info,
                        )
            except Exception as e:
                # 전처리 중 예외 — 원본 유지 여부는 clip 0건 케이스와 동일 정책
                fc = layer.featureCount() if hasattr(layer, 'featureCount') else 0
                if fc > 0 and _may_keep_raw_on_clip_fail(name):
                    QgsMessageLog.logMessage(
                        f"[경고] 전처리 오류 (서버측 clip 레이어라 원본 유지): {name} - {e}",
                        LOG_TAG, Qgis.Warning,
                    )
                    self.results.append((layer, name, False))
                elif fc > 0:
                    QgsMessageLog.logMessage(
                        f"[경고] 전처리 오류 — 원본은 범위 밖 데이터 포함 가능성 있어 제외: {name} (raw {fc:,}건) - {e}",
                        LOG_TAG, Qgis.Warning,
                    )
                else:
                    QgsMessageLog.logMessage(
                        f"범위 내 데이터 없음 (제외): {name}",
                        LOG_TAG, Qgis.Info,
                    )

        self.setProgress(100)
        return True

    def finished(self, success):
        """메인 스레드에서 호출 - 프로젝트에 레이어 일괄 추가"""
        root = QgsProject.instance().layerTreeRoot()

        for layer, name, preprocessed in self.results:
            # v1.4.1: 재로드 시 동일 이름 레이어 중복 생성 방지
            #   기존에 같은 이름의 레이어가 프로젝트에 있으면 먼저 제거
            #   (이후 사용자가 Step3에서 다시 로드할 때 "두 배로 생성" 현상 해결)
            _remove_existing_layers_by_name(name)

            if self.style_callback:
                self.style_callback(layer, name)
            QgsProject.instance().addMapLayer(layer)

            # DEM(래스터)은 작업범위 레이어 하단으로 이동
            if isinstance(layer, QgsRasterLayer):
                node = root.findLayer(layer.id())
                if node:
                    parent = node.parent()
                    clone = node.clone()
                    parent.addChildNode(clone)      # 맨 아래에 복제 추가
                    parent.removeChildNode(node)     # 원본 제거 (복제가 이미 있으므로 안전)

            # 한글 필드 별칭 적용 (clip/fix 후 최종 레이어에 적용)
            if isinstance(layer, QgsVectorLayer):
                # AVAILABLE_LAYERS에서 table_name 조회 (JSON 302컬럼 매핑용)
                layer_info = next(
                    (l for l in AVAILABLE_LAYERS if l["name"] == name), None
                )
                tbl = layer_info.get("function_name", "") if layer_info else ""
                apply_korean_aliases(layer, layer_name=name, table_name=tbl or None)

            status = "완료" if preprocessed else "원본"
            QgsMessageLog.logMessage(
                f"레이어 추가 ({status}): {name}",
                LOG_TAG, Qgis.Success if preprocessed else Qgis.Warning,
            )
