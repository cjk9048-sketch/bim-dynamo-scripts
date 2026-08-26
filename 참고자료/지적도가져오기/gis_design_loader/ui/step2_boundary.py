# -*- coding: utf-8 -*-
"""
Step 3: 범위 설정 - 작업 범위 폴리곤 생성
지도 캔버스에서 드래그로 사각형 범위를 그리거나, 기존 레이어를 선택
"""

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QComboBox,
)
from qgis.PyQt.QtCore import Qt
from qgis.core import (
    QgsProject, QgsVectorLayer, QgsFeature, QgsGeometry,
    QgsRectangle, QgsWkbTypes,
)
from qgis.gui import QgsMapToolEmitPoint, QgsRubberBand
from qgis.PyQt.QtGui import QColor

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE


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


class Step2Boundary(QWidget):
    """작업 범위 설정 페이지"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.map_tool = None
        self.previous_map_tool = None
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(16)

        # 안내
        guide = QLabel(
            "작업 범위를 설정합니다.\n"
            "지도에서 직접 드래그하여 사각형 범위를 그리거나,\n"
            "이미 프로젝트에 로드된 폴리곤 레이어를 범위로 지정할 수 있습니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 방법 1: 직접 그리기
        card1 = QFrame()
        card1.setStyleSheet(CARD_STYLE)
        card1_layout = QVBoxLayout()
        card1_layout.setContentsMargins(16, 16, 16, 16)
        card1_layout.setSpacing(10)

        card1_title = QLabel("방법 1: 지도에서 범위 그리기")
        card1_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        card1_layout.addWidget(card1_title)

        card1_desc = QLabel("지도 캔버스에서 드래그하여 사각형 작업 범위를 설정합니다.")
        card1_desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        card1_layout.addWidget(card1_desc)

        self.btn_draw = QPushButton("범위 그리기 시작")
        self.btn_draw.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_draw.setCursor(Qt.PointingHandCursor)
        self.btn_draw.setFixedHeight(38)
        self.btn_draw.clicked.connect(self._start_drawing)
        card1_layout.addWidget(self.btn_draw)

        card1.setLayout(card1_layout)
        layout.addWidget(card1)

        # 방법 2: 기존 레이어 선택
        card2 = QFrame()
        card2.setStyleSheet(CARD_STYLE)
        card2_layout = QVBoxLayout()
        card2_layout.setContentsMargins(16, 16, 16, 16)
        card2_layout.setSpacing(10)

        card2_title = QLabel("방법 2: 기존 폴리곤 레이어 선택")
        card2_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        card2_layout.addWidget(card2_title)

        self.layer_combo = QComboBox()
        self.layer_combo.setPlaceholderText("레이어를 선택하세요...")
        card2_layout.addWidget(self.layer_combo)

        btn_use_layer = QPushButton("선택 레이어를 범위로 사용")
        btn_use_layer.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_use_layer.setCursor(Qt.PointingHandCursor)
        btn_use_layer.setFixedHeight(38)
        btn_use_layer.clicked.connect(self._use_existing_layer)
        card2_layout.addWidget(btn_use_layer)

        card2.setLayout(card2_layout)
        layout.addWidget(card2)

        # 상태 표시
        self.status_label = QLabel("범위가 설정되지 않았습니다.")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #ef4444; padding: 8px; font-weight: 600;"
        )
        layout.addWidget(self.status_label)

        layout.addStretch()
        self.setLayout(layout)

    def on_enter(self):
        """페이지 진입 시 레이어 목록 갱신"""
        self.layer_combo.clear()
        for layer in QgsProject.instance().mapLayers().values():
            if isinstance(layer, QgsVectorLayer) and layer.geometryType() == QgsWkbTypes.PolygonGeometry:
                self.layer_combo.addItem(layer.name(), layer.id())

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

    def _on_rectangle_drawn(self, rect):
        """사각형 범위가 그려졌을 때 호출"""
        # 범위 레이어 생성
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

        # 빈 폴리곤 스타일 (속이 빈)
        from qgis.core import QgsFillSymbol
        symbol = QgsFillSymbol.createSimple({
            "color": "0,0,0,0",
            "outline_color": "255,0,0",
            "outline_width": "0.8",
        })
        boundary_layer.renderer().setSymbol(symbol)

        QgsProject.instance().addMapLayer(boundary_layer)

        # 최상단에 배치
        root = QgsProject.instance().layerTreeRoot()
        tree_layer = root.findLayer(boundary_layer.id())
        if tree_layer:
            clone = tree_layer.clone()
            root.insertChildNode(0, clone)
            root.removeChildNode(tree_layer)

        self.iface.mapCanvas().setExtent(rect)
        self.iface.mapCanvas().refresh()

        self.shared_data["boundary_layer"] = boundary_layer

        # UI 복원
        self.btn_draw.setText("범위 그리기 시작")
        self.btn_draw.setEnabled(True)
        if self.previous_map_tool:
            self.iface.mapCanvas().setMapTool(self.previous_map_tool)

        self._update_status(boundary_layer)

        # 다이얼로그 복원
        parent_dialog = self.window()
        if parent_dialog:
            parent_dialog.showNormal()
            parent_dialog.raise_()

    def _use_existing_layer(self):
        """기존 레이어를 범위로 사용"""
        layer_id = self.layer_combo.currentData()
        if not layer_id:
            QMessageBox.warning(self, "알림", "범위로 사용할 레이어를 선택하세요.")
            return

        layer = QgsProject.instance().mapLayer(layer_id)
        if layer is None:
            QMessageBox.warning(self, "오류", "선택한 레이어를 찾을 수 없습니다.")
            return

        self.shared_data["boundary_layer"] = layer
        self._update_status(layer)

    def _update_status(self, layer):
        extent = layer.extent()
        self.status_label.setText(
            f"범위 설정 완료: {layer.name()} "
            f"({extent.width():.0f} x {extent.height():.0f} m)"
        )
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 8px; font-weight: 600;"
        )

    def execute_step(self):
        if self.shared_data.get("boundary_layer") is None:
            QMessageBox.warning(self, "알림", "작업 범위를 먼저 설정해주세요.")
            return False
        return True
