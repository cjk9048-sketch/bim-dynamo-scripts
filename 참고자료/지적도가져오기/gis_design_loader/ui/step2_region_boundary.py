# -*- coding: utf-8 -*-
"""
Step 2: 사업지역 및 범위 설정 (통합)
행정구역 선택 OR 지도 그리기 OR 기존 레이어 선택으로 작업 범위를 설정한다.
선택된 방법에 따라 boundary_layer와 emd_codes를 모두 설정한다.
"""

import csv
from pathlib import Path

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QFrame, QComboBox,
    QPushButton, QMessageBox, QScrollArea, QSpinBox,
)
from qgis.PyQt.QtCore import Qt
from qgis.PyQt.QtGui import QColor
from qgis.core import (
    QgsProject, QgsVectorLayer, QgsRasterLayer, QgsFeature, QgsGeometry,
    QgsRectangle, QgsWkbTypes, QgsDataSourceUri,
    QgsCoordinateReferenceSystem, QgsCoordinateTransform,
    QgsFillSymbol,
)
from qgis.gui import QgsMapToolEmitPoint, QgsRubberBand

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE


# ---------------------------------------------------------------------------
# RectangleMapTool (step2_boundary.py 에서 가져옴)
# ---------------------------------------------------------------------------
class RectangleMapTool(QgsMapToolEmitPoint):
    """지도에서 사각형을 드래그하여 그리는 도구"""

    def __init__(self, canvas, callback):
        super().__init__(canvas)
        self.canvas = canvas
        self.callback = callback
        self.rubber_band = QgsRubberBand(canvas, QgsWkbTypes.PolygonGeometry)
        self.rubber_band.setColor(QColor(255, 0, 0, 100))
        self.rubber_band.setStrokeColor(QColor(255, 0, 0, 200))
        self.rubber_band.setWidth(2)
        self.start_point = None
        self.is_drawing = False

    def canvasPressEvent(self, event):
        self.start_point = self.toMapCoordinates(event.pos())
        self.is_drawing = True
        self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)

    def canvasMoveEvent(self, event):
        if not self.is_drawing or self.start_point is None:
            return
        end_point = self.toMapCoordinates(event.pos())
        self._update_rubber_band(self.start_point, end_point)

    def canvasReleaseEvent(self, event):
        if not self.is_drawing:
            return
        self.is_drawing = False
        end_point = self.toMapCoordinates(event.pos())
        self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)

        if self.start_point is None:
            return

        rect = QgsRectangle(self.start_point, end_point)
        if rect.width() > 0 and rect.height() > 0:
            self.callback(rect)

        self.start_point = None

    def _update_rubber_band(self, start, end):
        self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)
        rect = QgsRectangle(start, end)
        self.rubber_band.setToGeometry(QgsGeometry.fromRect(rect))

    def deactivate(self):
        self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)
        super().deactivate()


# ---------------------------------------------------------------------------
# PolygonMapTool - 다각형 범위 그리기
# ---------------------------------------------------------------------------
class PolygonMapTool(QgsMapToolEmitPoint):
    """지도에서 다각형을 클릭하여 그리는 도구

    - 좌클릭: 꼭짓점 추가
    - 우클릭 또는 더블클릭: 다각형 완성
    - Esc: 취소
    """

    def __init__(self, canvas, callback):
        super().__init__(canvas)
        self.canvas = canvas
        self.callback = callback
        self.rubber_band = QgsRubberBand(canvas, QgsWkbTypes.PolygonGeometry)
        self.rubber_band.setColor(QColor(255, 0, 0, 100))
        self.rubber_band.setStrokeColor(QColor(255, 0, 0, 200))
        self.rubber_band.setWidth(2)
        self.points = []

    def canvasPressEvent(self, event):
        if event.button() == Qt.LeftButton:
            point = self.toMapCoordinates(event.pos())
            self.points.append(point)
            self.rubber_band.addPoint(point, True)
        elif event.button() == Qt.RightButton:
            self._finish_polygon()

    def canvasMoveEvent(self, event):
        if self.points:
            point = self.toMapCoordinates(event.pos())
            # 임시 포인트로 미리보기 업데이트
            self.rubber_band.movePoint(point)

    def canvasDoubleClickEvent(self, event):
        self._finish_polygon()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key_Escape:
            self.points.clear()
            self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)
            self.callback(None)  # 취소 시 None 전달

    def _finish_polygon(self):
        if len(self.points) < 3:
            # 꼭짓점 3개 미만이면 무시
            return
        geometry = QgsGeometry.fromPolygonXY([self.points])
        self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)
        self.points.clear()
        self.callback(geometry)

    def deactivate(self):
        self.rubber_band.reset(QgsWkbTypes.PolygonGeometry)
        self.points.clear()
        super().deactivate()


# ---------------------------------------------------------------------------
# Step2RegionBoundary (통합 단계)
# ---------------------------------------------------------------------------
class Step2RegionBoundary(QWidget):
    """사업지역 및 범위 설정 통합 페이지

    방법 1: 행정구역 선택 -> 행정경계 폴리곤을 범위로 설정
    방법 2: 지도에서 사각형 드래그
    방법 3: 기존 폴리곤 레이어 선택
    """

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.region_data = {}  # {adm_cd: adm_nm}
        self.map_tool = None
        self.previous_map_tool = None
        self._load_region_data()
        self._setup_ui()

    # ------------------------------------------------------------------
    # CSV 로딩
    # ------------------------------------------------------------------
    def _load_region_data(self):
        csv_path = Path(__file__).resolve().parents[1] / "admin_regions.csv"
        with open(csv_path, "r", encoding="utf-8-sig") as f:
            reader = csv.DictReader(f)
            for row in reader:
                self.region_data[row["adm_cd"]] = row["adm_nm"]

    # ------------------------------------------------------------------
    # UI 구성
    # ------------------------------------------------------------------
    def _setup_ui(self):
        outer_layout = QVBoxLayout()
        outer_layout.setContentsMargins(0, 0, 0, 0)
        outer_layout.setSpacing(0)

        # 스크롤 영역
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setVerticalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        scroll.setStyleSheet("border: none; background-color: transparent;")

        scroll_widget = QWidget()
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(16)

        # 안내
        guide = QLabel(
            "작업 범위를 설정합니다.\n"
            "아래 3가지 방법 중 하나를 선택하여 사업 범위를 지정하세요."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # ---- 카드 1: 지도에서 범위 그리기 ----
        card1 = self._create_draw_card()
        layout.addWidget(card1)

        # ---- 카드 2: 행정구역으로 범위 설정 ----
        card2 = self._create_admin_region_card()
        layout.addWidget(card2)

        # ---- 카드 3: 기존 레이어 선택 ----
        card3 = self._create_layer_card()
        layout.addWidget(card3)

        # ---- 배경지도 투명도 ----
        bg_card = QFrame()
        bg_card.setStyleSheet(CARD_STYLE)
        bg_layout = QHBoxLayout()
        bg_layout.setContentsMargins(16, 10, 16, 10)

        bg_label = QLabel("배경지도 투명도:")
        bg_label.setStyleSheet("font-size: 13px; color: #374151; border: none;")
        bg_layout.addWidget(bg_label)

        self.opacity_spin = QSpinBox()
        self.opacity_spin.setRange(0, 100)
        self.opacity_spin.setValue(60)
        self.opacity_spin.setSuffix("%")
        bg_layout.addWidget(self.opacity_spin)

        btn_opacity = QPushButton("적용")
        btn_opacity.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_opacity.setCursor(Qt.PointingHandCursor)
        btn_opacity.setFixedWidth(80)
        btn_opacity.clicked.connect(self._apply_opacity)
        bg_layout.addWidget(btn_opacity)

        bg_card.setLayout(bg_layout)
        layout.addWidget(bg_card)

        layout.addStretch()
        scroll_widget.setLayout(layout)
        scroll.setWidget(scroll_widget)
        outer_layout.addWidget(scroll, 1)

        # ---- 하단 상태 바 ----
        status_frame = QFrame()
        status_frame.setFixedHeight(44)
        status_frame.setStyleSheet(
            "background-color: white; border-top: 1px solid #e5e7eb;"
        )
        status_layout = QHBoxLayout()
        status_layout.setContentsMargins(20, 8, 20, 8)

        self.status_label = QLabel("범위가 설정되지 않았습니다.")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #ef4444; font-weight: 600; border: none;"
        )
        status_layout.addWidget(self.status_label)
        status_layout.addStretch()
        status_frame.setLayout(status_layout)
        outer_layout.addWidget(status_frame)

        self.setLayout(outer_layout)

    # ------------------------------------------------------------------
    # 카드 1: 행정구역으로 범위 설정
    # ------------------------------------------------------------------
    def _create_admin_region_card(self):
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        card_layout = QVBoxLayout()
        card_layout.setContentsMargins(16, 16, 16, 16)
        card_layout.setSpacing(10)

        title = QLabel("방법 2: 행정구역으로 범위 설정")
        title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        card_layout.addWidget(title)

        desc = QLabel(
            "시도 / 시군구 / 읍면동을 선택하면 해당 행정구역 경계가 작업 범위로 설정됩니다."
        )
        desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        desc.setWordWrap(True)
        card_layout.addWidget(desc)

        # 시도
        row_sido = QHBoxLayout()
        lbl_sido = QLabel("시도")
        lbl_sido.setFixedWidth(60)
        lbl_sido.setStyleSheet(
            "font-size: 13px; font-weight: 600; color: #374151; border: none;"
        )
        COMPACT_COMBO = "padding: 4px 8px; font-size: 13px;"
        self.combo_sido = QComboBox()
        self.combo_sido.setMaxVisibleItems(20)
        self.combo_sido.setFixedHeight(30)
        self.combo_sido.setStyleSheet(COMPACT_COMBO)
        self.combo_sido.addItem("-- 시도 선택 --", None)
        for code, name in sorted(self.region_data.items(), key=lambda x: x[0]):
            if len(code) == 2:
                self.combo_sido.addItem(name, code)
        self.combo_sido.currentIndexChanged.connect(self._on_sido_changed)
        row_sido.addWidget(lbl_sido)
        row_sido.addWidget(self.combo_sido, 1)
        card_layout.addLayout(row_sido)

        # 시군구
        row_sigungu = QHBoxLayout()
        lbl_sigungu = QLabel("시군구")
        lbl_sigungu.setFixedWidth(60)
        lbl_sigungu.setStyleSheet(
            "font-size: 13px; font-weight: 600; color: #374151; border: none;"
        )
        self.combo_sigungu = QComboBox()
        self.combo_sigungu.setMaxVisibleItems(20)
        self.combo_sigungu.setFixedHeight(30)
        self.combo_sigungu.setStyleSheet(COMPACT_COMBO)
        self.combo_sigungu.addItem("-- 시군구 선택 --", None)
        self.combo_sigungu.currentIndexChanged.connect(self._on_sigungu_changed)
        row_sigungu.addWidget(lbl_sigungu)
        row_sigungu.addWidget(self.combo_sigungu, 1)
        card_layout.addLayout(row_sigungu)

        # 읍면동
        row_emd = QHBoxLayout()
        lbl_emd = QLabel("읍면동")
        lbl_emd.setFixedWidth(60)
        lbl_emd.setStyleSheet(
            "font-size: 13px; font-weight: 600; color: #374151; border: none;"
        )
        self.combo_emd = QComboBox()
        self.combo_emd.setMaxVisibleItems(20)
        self.combo_emd.setFixedHeight(30)
        self.combo_emd.setStyleSheet(COMPACT_COMBO)
        self.combo_emd.addItem("-- 읍면동 선택 --", None)
        row_emd.addWidget(lbl_emd)
        row_emd.addWidget(self.combo_emd, 1)
        card_layout.addLayout(row_emd)

        # 확인 버튼
        self.btn_admin_confirm = QPushButton("행정구역 경계를 범위로 설정")
        self.btn_admin_confirm.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_admin_confirm.setCursor(Qt.PointingHandCursor)
        self.btn_admin_confirm.setFixedHeight(38)
        self.btn_admin_confirm.clicked.connect(self._on_admin_confirm)
        card_layout.addWidget(self.btn_admin_confirm)

        card.setLayout(card_layout)
        return card

    # ------------------------------------------------------------------
    # 카드 2: 지도에서 범위 그리기
    # ------------------------------------------------------------------
    def _create_draw_card(self):
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        card_layout = QVBoxLayout()
        card_layout.setContentsMargins(16, 16, 16, 16)
        card_layout.setSpacing(10)

        title = QLabel("방법 1: 지도에서 범위 그리기")
        title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        card_layout.addWidget(title)

        desc = QLabel("사각형 드래그 또는 다각형 클릭으로 작업 범위를 설정합니다.")
        desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        card_layout.addWidget(desc)

        btn_row = QHBoxLayout()
        btn_row.setSpacing(8)

        self.btn_draw = QPushButton("사각형 범위 그리기")
        self.btn_draw.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_draw.setCursor(Qt.PointingHandCursor)
        self.btn_draw.setFixedHeight(38)
        self.btn_draw.clicked.connect(self._start_drawing)
        btn_row.addWidget(self.btn_draw)

        self.btn_draw_polygon = QPushButton("다각형 범위 그리기")
        self.btn_draw_polygon.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_draw_polygon.setCursor(Qt.PointingHandCursor)
        self.btn_draw_polygon.setFixedHeight(38)
        self.btn_draw_polygon.clicked.connect(self._start_polygon_drawing)
        btn_row.addWidget(self.btn_draw_polygon)

        card_layout.addLayout(btn_row)

        card.setLayout(card_layout)
        return card

    # ------------------------------------------------------------------
    # 카드 3: 기존 레이어 선택
    # ------------------------------------------------------------------
    def _create_layer_card(self):
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        card_layout = QVBoxLayout()
        card_layout.setContentsMargins(16, 16, 16, 16)
        card_layout.setSpacing(10)

        title = QLabel("방법 3: 기존 레이어를 범위로 선택")
        title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        card_layout.addWidget(title)

        desc = QLabel("프로젝트에 이미 로드된 폴리곤 레이어를 범위로 지정합니다.")
        desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        card_layout.addWidget(desc)

        self.layer_combo = QComboBox()
        self.layer_combo.setPlaceholderText("레이어를 선택하세요...")
        card_layout.addWidget(self.layer_combo)

        btn_use_layer = QPushButton("선택 레이어를 범위로 사용")
        btn_use_layer.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_use_layer.setCursor(Qt.PointingHandCursor)
        btn_use_layer.setFixedHeight(38)
        btn_use_layer.clicked.connect(self._use_existing_layer)
        card_layout.addWidget(btn_use_layer)

        card.setLayout(card_layout)
        return card

    # ------------------------------------------------------------------
    # 캐스케이딩 콤보박스 핸들러
    # ------------------------------------------------------------------
    def _on_sido_changed(self, index):
        self.combo_sigungu.blockSignals(True)
        self.combo_sigungu.clear()
        self.combo_sigungu.addItem("-- 시군구 선택 --", None)
        self.combo_sigungu.blockSignals(False)

        self.combo_emd.blockSignals(True)
        self.combo_emd.clear()
        self.combo_emd.addItem("-- 읍면동 선택 --", None)
        self.combo_emd.blockSignals(False)

        if index <= 0:
            return

        sido_code = self.combo_sido.currentData()
        if not sido_code:
            return

        self.combo_sigungu.blockSignals(True)
        for code, name in sorted(self.region_data.items(), key=lambda x: x[0]):
            if len(code) == 5 and code.startswith(sido_code):
                self.combo_sigungu.addItem(name, code)
        self.combo_sigungu.blockSignals(False)

    def _on_sigungu_changed(self, index):
        self.combo_emd.blockSignals(True)
        self.combo_emd.clear()
        self.combo_emd.addItem("-- 읍면동 선택 --", None)
        self.combo_emd.blockSignals(False)

        if index <= 0:
            return

        sigungu_code = self.combo_sigungu.currentData()
        if not sigungu_code:
            return

        self.combo_emd.blockSignals(True)
        for code, name in sorted(self.region_data.items(), key=lambda x: x[0]):
            if len(code) >= 8 and code.startswith(sigungu_code):
                self.combo_emd.addItem(name, code)
        self.combo_emd.blockSignals(False)

    # ------------------------------------------------------------------
    # 방법 1: 행정구역 경계를 범위로 설정
    # ------------------------------------------------------------------
    def _on_admin_confirm(self):
        """행정구역 선택 확인 -> DB에서 경계 geometry 조회 -> boundary_layer 생성"""
        emd_code = self.combo_emd.currentData()
        sigungu_code = self.combo_sigungu.currentData()
        sido_code = self.combo_sido.currentData()

        # 가장 세밀한 수준부터 확인
        if emd_code:
            target_code = emd_code
            sido_name = self.combo_sido.currentText()
            sigungu_name = self.combo_sigungu.currentText()
            emd_name = self.combo_emd.currentText()
            region_name = f"{sido_name} {sigungu_name} {emd_name}"
            emd_codes = [emd_code]
        elif sigungu_code:
            target_code = sigungu_code
            sido_name = self.combo_sido.currentText()
            sigungu_name = self.combo_sigungu.currentText()
            region_name = f"{sido_name} {sigungu_name}"
            # 시군구 선택 시 하위 읍면동 코드 수집
            emd_codes = [
                code for code in self.region_data
                if len(code) >= 8 and code.startswith(sigungu_code)
            ]
            if not emd_codes:
                emd_codes = [sigungu_code]
        elif sido_code:
            target_code = sido_code
            sido_name = self.combo_sido.currentText()
            region_name = sido_name
            emd_codes = []
        else:
            QMessageBox.warning(self, "알림", "행정구역을 선택해주세요.")
            return

        # DB에서 행정구역 경계 geometry 조회
        boundary_layer = self._create_boundary_from_admin(target_code, region_name)
        if boundary_layer is None:
            return

        # shared_data 설정
        self.shared_data["boundary_layer"] = boundary_layer
        self.shared_data["selected_emd_codes"] = emd_codes
        self.shared_data["selected_region_name"] = region_name

        # 지도에 추가
        QgsProject.instance().addMapLayer(boundary_layer)

        canvas = self.iface.mapCanvas()
        extent = boundary_layer.extent()

        # CRS 변환 (5186 -> 프로젝트 CRS)
        db_crs = QgsCoordinateReferenceSystem("EPSG:5186")
        project_crs = QgsProject.instance().crs()
        if db_crs.authid() != project_crs.authid():
            transform = QgsCoordinateTransform(
                db_crs, project_crs, QgsProject.instance()
            )
            extent = transform.transformBoundingBox(extent)

        extent.scale(1.1)
        canvas.setExtent(extent)
        canvas.refresh()

        self._update_status_success(boundary_layer, region_name)

    def _create_boundary_from_admin(self, target_code, region_name):
        """DB sgis_hjd에서 행정구역 경계를 조회하여 메모리 boundary 레이어 생성

        target_code 길이에 따라:
        - 2자리 (시도): adm_cd = 'XX'
        - 5자리 (시군구): adm_cd = 'XXXXX'
        - 8자리+ (읍면동): adm_cd = 'XXXXXXXX'

        시군구/시도 선택 시 해당 레벨의 단일 행정구역 폴리곤을 가져온다.
        """
        from ..db_env import DB_HOST, DB_PORT, DB_NAME, DB_SCHEMA, DB_USER, DB_PASSWORD

        sql_filter = f"adm_cd = '{target_code}'"

        uri = QgsDataSourceUri()
        uri.setConnection(DB_HOST, str(DB_PORT), DB_NAME, DB_USER, DB_PASSWORD)
        uri.setDataSource(DB_SCHEMA, "sgis_hjd", "geometry", sql_filter, "adm_cd")

        temp_layer = QgsVectorLayer(uri.uri(), "_boundary_source", "postgres")
        if not temp_layer.isValid() or temp_layer.featureCount() <= 0:
            self.status_label.setText("행정구역 geometry 조회 실패 (DB 연결 확인)")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; font-weight: 600; border: none;"
            )
            return None

        # 메모리 레이어 생성 (EPSG:5186)
        boundary_layer = QgsVectorLayer(
            "Polygon?crs=EPSG:5186&field=name:string&field=adm_cd:string",
            "작업범위",
            "memory",
        )
        dp = boundary_layer.dataProvider()

        # DB에서 feature 복사
        features = []
        for feat in temp_layer.getFeatures():
            new_feat = QgsFeature()
            new_feat.setGeometry(feat.geometry())
            new_feat.setAttributes([region_name, target_code])
            features.append(new_feat)

        if not features:
            self.status_label.setText("행정구역 geometry가 비어있습니다.")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; font-weight: 600; border: none;"
            )
            return None

        # 여러 feature가 있으면 union (dissolve)
        if len(features) > 1:
            combined_geom = features[0].geometry()
            for f in features[1:]:
                combined_geom = combined_geom.combine(f.geometry())
            union_feat = QgsFeature()
            union_feat.setGeometry(combined_geom)
            union_feat.setAttributes([region_name, target_code])
            dp.addFeatures([union_feat])
        else:
            dp.addFeatures(features)

        boundary_layer.updateExtents()

        # 빈 폴리곤 스타일 (속이 빈, 빨간 외곽선)
        symbol = QgsFillSymbol.createSimple({
            "color": "0,0,0,0",
            "outline_color": "0,0,0",
            "outline_width": "0.8",
        })
        boundary_layer.renderer().setSymbol(symbol)

        return boundary_layer

    # ------------------------------------------------------------------
    # 방법 2: 지도에서 범위 그리기
    # ------------------------------------------------------------------
    def _start_drawing(self):
        """지도에서 범위 그리기 도구 활성화"""
        self.previous_map_tool = self.iface.mapCanvas().mapTool()
        self.map_tool = RectangleMapTool(
            self.iface.mapCanvas(), self._on_rectangle_drawn
        )
        self.iface.mapCanvas().setMapTool(self.map_tool)
        self.btn_draw.setText("지도에서 드래그하세요...")
        self.btn_draw.setEnabled(False)

        # 다이얼로그를 최소화하여 지도를 볼 수 있게
        parent_dialog = self.window()
        if parent_dialog:
            parent_dialog.showMinimized()

    def _start_polygon_drawing(self):
        """지도에서 다각형 범위 그리기 도구 활성화"""
        self.previous_map_tool = self.iface.mapCanvas().mapTool()
        self.map_tool = PolygonMapTool(
            self.iface.mapCanvas(), self._on_polygon_drawn
        )
        self.iface.mapCanvas().setMapTool(self.map_tool)
        self.btn_draw_polygon.setText("좌클릭:꼭짓점, 우클릭:완성")
        self.btn_draw_polygon.setEnabled(False)
        self.btn_draw.setEnabled(False)

        parent_dialog = self.window()
        if parent_dialog:
            parent_dialog.showMinimized()

    def _on_polygon_drawn(self, geometry):
        """다각형 범위가 그려졌을 때 호출 (geometry: QgsGeometry 또는 None=취소)"""
        # UI 복원
        self.btn_draw_polygon.setText("다각형 범위 그리기")
        self.btn_draw_polygon.setEnabled(True)
        self.btn_draw.setEnabled(True)

        if self.previous_map_tool:
            self.iface.mapCanvas().setMapTool(self.previous_map_tool)

        parent_dialog = self.window()
        if parent_dialog:
            parent_dialog.showNormal()
            parent_dialog.raise_()

        if geometry is None:
            # 취소됨 (Esc)
            return

        # 범위 레이어 생성 (프로젝트 CRS)
        crs = QgsProject.instance().crs()
        boundary_layer = QgsVectorLayer(
            f"Polygon?crs={crs.authid()}&field=name:string",
            "작업범위",
            "memory",
        )

        feature = QgsFeature()
        feature.setGeometry(geometry)
        feature.setAttributes(["작업범위"])
        boundary_layer.dataProvider().addFeatures([feature])
        boundary_layer.updateExtents()

        # 빈 폴리곤 스타일 (속이 빈, 검정 외곽선)
        symbol = QgsFillSymbol.createSimple({
            "color": "0,0,0,0",
            "outline_color": "0,0,0",
            "outline_width": "0.8",
        })
        boundary_layer.renderer().setSymbol(symbol)

        QgsProject.instance().addMapLayer(boundary_layer)

        self.iface.mapCanvas().setExtent(boundary_layer.extent())
        self.iface.mapCanvas().refresh()

        self.shared_data["boundary_layer"] = boundary_layer

        # 행정구역 코드 자동 감지
        self._detect_and_set_emd_codes(boundary_layer)

        self._update_status_success(boundary_layer)

    def _on_rectangle_drawn(self, rect):
        """사각형 범위가 그려졌을 때 호출"""
        # 범위 레이어 생성 (프로젝트 CRS)
        crs = QgsProject.instance().crs()
        boundary_layer = QgsVectorLayer(
            f"Polygon?crs={crs.authid()}&field=name:string",
            "작업범위",
            "memory",
        )

        feature = QgsFeature()
        feature.setGeometry(QgsGeometry.fromRect(rect))
        feature.setAttributes(["작업범위"])
        boundary_layer.dataProvider().addFeatures([feature])
        boundary_layer.updateExtents()

        # 빈 폴리곤 스타일 (속이 빈, 빨간 외곽선)
        symbol = QgsFillSymbol.createSimple({
            "color": "0,0,0,0",
            "outline_color": "0,0,0",
            "outline_width": "0.8",
        })
        boundary_layer.renderer().setSymbol(symbol)

        QgsProject.instance().addMapLayer(boundary_layer)

        self.iface.mapCanvas().setExtent(rect)
        self.iface.mapCanvas().refresh()

        self.shared_data["boundary_layer"] = boundary_layer

        # 행정구역 코드 자동 감지
        self._detect_and_set_emd_codes(boundary_layer)

        # UI 복원
        self.btn_draw.setText("범위 그리기 시작")
        self.btn_draw.setEnabled(True)
        if self.previous_map_tool:
            self.iface.mapCanvas().setMapTool(self.previous_map_tool)

        self._update_status_success(boundary_layer)

        # 다이얼로그 복원
        parent_dialog = self.window()
        if parent_dialog:
            parent_dialog.showNormal()
            parent_dialog.raise_()

    # ------------------------------------------------------------------
    # 방법 3: 기존 레이어 선택
    # ------------------------------------------------------------------
    def _use_existing_layer(self):
        """기존 폴리곤 레이어를 범위로 사용"""
        layer_id = self.layer_combo.currentData()
        if not layer_id:
            QMessageBox.warning(self, "알림", "범위로 사용할 레이어를 선택하세요.")
            return

        layer = QgsProject.instance().mapLayer(layer_id)
        if layer is None:
            QMessageBox.warning(self, "오류", "선택한 레이어를 찾을 수 없습니다.")
            return

        self.shared_data["boundary_layer"] = layer

        # 행정구역 코드 자동 감지
        self._detect_and_set_emd_codes(layer)

        self._update_status_success(layer)

    # ------------------------------------------------------------------
    # 행정구역 코드 자동 감지 (방법 2, 3 공통)
    # ------------------------------------------------------------------
    def _detect_and_set_emd_codes(self, layer):
        """레이어 도형 기반으로 읍면동 코드 자동 감지 (범위 폴리곤과 ST_Intersects 하는 동만)"""
        try:
            from ..core.layer_loader import (
                transform_extent_to_db, boundary_geometry_to_db_wkt,
                detect_emd_codes, detect_region_code,
            )

            extent_5186 = transform_extent_to_db(layer.extent(), layer.crs())
            clip_wkt = boundary_geometry_to_db_wkt(layer)
            emd_codes = detect_emd_codes(extent_5186, clip_geom_wkt=clip_wkt)

            if emd_codes:
                self.shared_data["selected_emd_codes"] = emd_codes
                # 지역명 생성 (첫 번째 코드 기반)
                region_name = self._build_region_name_from_codes(emd_codes)
                self.shared_data["selected_region_name"] = region_name
            else:
                # 폴백: 시도 코드로 감지
                region_code = detect_region_code(extent_5186)
                if region_code:
                    self.shared_data["selected_emd_codes"] = [region_code]
                    name = self.region_data.get(region_code, region_code)
                    self.shared_data["selected_region_name"] = name
                else:
                    self.shared_data["selected_emd_codes"] = []
                    self.shared_data["selected_region_name"] = "자동 감지 실패"
        except Exception:
            self.shared_data["selected_emd_codes"] = []
            self.shared_data["selected_region_name"] = ""

    def _build_region_name_from_codes(self, emd_codes):
        """읍면동 코드 리스트로부터 지역명 생성"""
        if not emd_codes:
            return ""

        # 첫 번째 코드의 시도/시군구명 찾기
        first_code = emd_codes[0]
        sido_code = first_code[:2]
        sigungu_code = first_code[:5] if len(first_code) >= 5 else ""

        sido_name = self.region_data.get(sido_code, "")
        sigungu_name = self.region_data.get(sigungu_code, "") if sigungu_code else ""

        if len(emd_codes) == 1 and len(first_code) >= 8:
            emd_name = self.region_data.get(first_code, "")
            if sido_name and sigungu_name and emd_name:
                return f"{sido_name} {sigungu_name} {emd_name}"

        if sido_name and sigungu_name:
            return f"{sido_name} {sigungu_name} ({len(emd_codes)}개 읍면동)"
        elif sido_name:
            return f"{sido_name} ({len(emd_codes)}개 읍면동)"

        return f"{len(emd_codes)}개 읍면동"

    # ------------------------------------------------------------------
    # 상태 표시 갱신
    # ------------------------------------------------------------------
    def _update_status_success(self, layer, region_name=None):
        """범위 설정 성공 시 상태 업데이트"""
        extent = layer.extent()
        region = region_name or self.shared_data.get("selected_region_name", "")

        text_parts = [
            f"범위 설정 완료: {layer.name()} "
            f"({extent.width():.0f} x {extent.height():.0f} m)"
        ]
        if region:
            text_parts.append(f"  |  지역: {region}")

        self.status_label.setText("".join(text_parts))
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; font-weight: 600; border: none;"
        )

    # ------------------------------------------------------------------
    # 페이지 진입 / 초기화 / 실행
    # ------------------------------------------------------------------
    def _apply_opacity(self):
        """배경지도(래스터) 투명도 조절 — DEM 레이어는 제외"""
        opacity = self.opacity_spin.value() / 100.0
        count = 0
        for layer in QgsProject.instance().mapLayers().values():
            if isinstance(layer, QgsRasterLayer):
                if "DEM" in layer.name().upper():
                    continue
                layer.setOpacity(opacity)
                layer.triggerRepaint()
                count += 1
        self.iface.mapCanvas().refresh()

    def on_enter(self):
        """페이지 진입 시 레이어 목록 갱신 및 현재 상태 표시"""
        # 기존 폴리곤 레이어 목록 갱신 (방법 3)
        self.layer_combo.clear()
        for layer in QgsProject.instance().mapLayers().values():
            if isinstance(layer, QgsVectorLayer) and layer.geometryType() == QgsWkbTypes.PolygonGeometry:
                self.layer_combo.addItem(layer.name(), layer.id())

        # 현재 설정 상태 표시
        boundary = self.shared_data.get("boundary_layer")
        try:
            boundary_valid = boundary is not None and boundary.isValid()
        except RuntimeError:
            boundary_valid = False
            self.shared_data["boundary_layer"] = None

        if boundary_valid:
            region_name = self.shared_data.get("selected_region_name", "")
            self._update_status_success(boundary, region_name)
        else:
            self.status_label.setText("범위가 설정되지 않았습니다.")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; font-weight: 600; border: none;"
            )

    def reset(self):
        """상태 초기화"""
        self.combo_sido.setCurrentIndex(0)
        self.combo_sigungu.clear()
        self.combo_sigungu.addItem("-- 시군구 선택 --", None)
        self.combo_emd.clear()
        self.combo_emd.addItem("-- 읍면동 선택 --", None)
        self.layer_combo.clear()
        self.shared_data["selected_emd_codes"] = []
        self.shared_data["selected_region_name"] = ""
        self.shared_data["boundary_layer"] = None
        self.status_label.setText("범위가 설정되지 않았습니다.")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #ef4444; font-weight: 600; border: none;"
        )

    def execute_step(self):
        """다음 단계 진행 전 검증"""
        if self.shared_data.get("boundary_layer") is None:
            QMessageBox.warning(self, "알림", "작업 범위를 먼저 설정해주세요.")
            return False
        return True
