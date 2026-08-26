# -*- coding: utf-8 -*-
"""
Step 7: 계획시설 레이어 생성
9색 × 2선종(이중실선/이중점선) 템플릿 기반 LineString 레이어 생성
"""

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QLineEdit, QComboBox, QListWidget,
    QGroupBox, QFileDialog,
)
from qgis.PyQt.QtCore import Qt, QVariant
from qgis.PyQt.QtGui import QColor, QPixmap, QPainter, QPen
from qgis.core import (
    QgsProject, QgsVectorLayer, QgsField, QgsFields,
    QgsWkbTypes, QgsVectorFileWriter,
    QgsLineSymbol, QgsSimpleLineSymbolLayer,
    QgsSnappingConfig, QgsTolerance,
)

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE


# 9가지 색상 템플릿
FACILITY_COLORS = [
    ("1번색 (빨강)", "#FF0000"),
    ("2번색 (주황)", "#FF8C00"),
    ("3번색 (노랑)", "#FFD700"),
    ("4번색 (초록)", "#00AA00"),
    ("5번색 (파랑)", "#0000FF"),
    ("6번색 (남색)", "#4B0082"),
    ("7번색 (보라)", "#8B00FF"),
    ("8번색 (분홍)", "#FF69B4"),
    ("9번색 (회색)", "#808080"),
]

# 2가지 선종
LINE_STYLES = [
    ("이중실선", Qt.SolidLine),
    ("이중점선", Qt.DashLine),
]


def create_double_line_symbol(color_hex, pen_style):
    """이중선(outer+inner) 심볼 생성

    Args:
        color_hex: 색상 hex (예: "#FF0000")
        pen_style: Qt.SolidLine 또는 Qt.DashLine

    Returns:
        QgsLineSymbol
    """
    symbol = QgsLineSymbol()

    # 외곽선 (두꺼운 색상 선)
    outer = QgsSimpleLineSymbolLayer()
    outer.setColor(QColor(color_hex))
    outer.setWidth(1.2)
    outer.setPenStyle(pen_style)

    # 내부선 (가는 흰색 선)
    inner = QgsSimpleLineSymbolLayer()
    inner.setColor(QColor("#FFFFFF"))
    inner.setWidth(0.4)
    inner.setPenStyle(pen_style)

    symbol.changeSymbolLayer(0, outer)
    symbol.appendSymbolLayer(inner)

    return symbol


class Step7FacilityLayer(QWidget):
    """계획시설 레이어 생성 페이지 (9색 × 2선종 템플릿)"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self._created_layers = []
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(16, 12, 16, 12)
        layout.setSpacing(8)

        # 안내
        guide = QLabel(
            "계획시설 레이어를 생성합니다. "
            "9가지 색상 × 2가지 선종(이중실선/이중점선) 중 선택하여 생성합니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 레이어 설정 카드
        setting_card = QFrame()
        setting_card.setStyleSheet(CARD_STYLE)
        sc_layout = QVBoxLayout()
        sc_layout.setContentsMargins(16, 10, 16, 10)
        sc_layout.setSpacing(6)

        sc_title = QLabel("계획시설 레이어 설정")
        sc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        sc_layout.addWidget(sc_title)

        # 레이어 이름
        name_row = QHBoxLayout()
        name_label = QLabel("레이어 이름:")
        name_label.setStyleSheet("font-size: 13px; border: none; min-width: 90px;")
        name_row.addWidget(name_label)
        self.name_input = QLineEdit()
        self.name_input.setPlaceholderText("예: 상수관로, 하수관로 등")
        name_row.addWidget(self.name_input)
        sc_layout.addLayout(name_row)

        # 선 색상
        color_row = QHBoxLayout()
        color_label = QLabel("선 색상:")
        color_label.setStyleSheet("font-size: 13px; border: none; min-width: 90px;")
        color_row.addWidget(color_label)
        self.color_combo = QComboBox()
        for name, _ in FACILITY_COLORS:
            self.color_combo.addItem(name)
        self.color_combo.currentIndexChanged.connect(self._update_preview)
        color_row.addWidget(self.color_combo)
        sc_layout.addLayout(color_row)

        # 선 스타일
        style_row = QHBoxLayout()
        style_label = QLabel("선 스타일:")
        style_label.setStyleSheet("font-size: 13px; border: none; min-width: 90px;")
        style_row.addWidget(style_label)
        self.style_combo = QComboBox()
        for name, _ in LINE_STYLES:
            self.style_combo.addItem(name)
        self.style_combo.currentIndexChanged.connect(self._update_preview)
        style_row.addWidget(self.style_combo)
        sc_layout.addLayout(style_row)

        # 생성 방식
        type_row = QHBoxLayout()
        type_label = QLabel("생성 방식:")
        type_label.setStyleSheet("font-size: 13px; border: none; min-width: 90px;")
        type_row.addWidget(type_label)
        self.type_combo = QComboBox()
        self.type_combo.addItem("메모리 레이어 (임시)", "memory")
        self.type_combo.addItem("Shapefile로 저장", "shp")
        type_row.addWidget(self.type_combo)
        sc_layout.addLayout(type_row)

        # 미리보기
        preview_row = QHBoxLayout()
        preview_label = QLabel("미리보기:")
        preview_label.setStyleSheet("font-size: 13px; border: none; min-width: 90px;")
        preview_row.addWidget(preview_label)
        self.preview_widget = QLabel()
        self.preview_widget.setFixedSize(200, 30)
        self.preview_widget.setStyleSheet("border: 1px solid #e5e7eb; border-radius: 4px;")
        preview_row.addWidget(self.preview_widget)
        preview_row.addStretch()
        sc_layout.addLayout(preview_row)

        setting_card.setLayout(sc_layout)
        layout.addWidget(setting_card)

        # 생성 버튼
        self.btn_create = QPushButton("계획시설 레이어 생성")
        self.btn_create.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_create.setCursor(Qt.PointingHandCursor)
        self.btn_create.setFixedHeight(36)
        self.btn_create.clicked.connect(self._create_facility_layer)
        layout.addWidget(self.btn_create)

        # 생성된 레이어 목록 카드
        list_card = QFrame()
        list_card.setStyleSheet(CARD_STYLE)
        lc_layout = QVBoxLayout()
        lc_layout.setContentsMargins(16, 10, 16, 10)
        lc_layout.setSpacing(6)

        lc_title = QLabel("생성된 레이어 목록")
        lc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        lc_layout.addWidget(lc_title)

        self.layer_list = QListWidget()
        self.layer_list.setMaximumHeight(150)
        self.layer_list.setStyleSheet(
            "border: 1px solid #e5e7eb; border-radius: 4px; font-size: 13px;"
        )
        lc_layout.addWidget(self.layer_list)

        # 편집 도구 행
        edit_row = QHBoxLayout()
        self.btn_edit = QPushButton("편집 모드 시작")
        self.btn_edit.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_edit.setCursor(Qt.PointingHandCursor)
        self.btn_edit.setEnabled(False)
        self.btn_edit.clicked.connect(self._toggle_editing)
        edit_row.addWidget(self.btn_edit)

        self.btn_snap = QPushButton("스냅 설정")
        self.btn_snap.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_snap.setCursor(Qt.PointingHandCursor)
        self.btn_snap.setEnabled(False)
        self.btn_snap.clicked.connect(self._configure_snapping)
        edit_row.addWidget(self.btn_snap)

        lc_layout.addLayout(edit_row)

        edit_desc = QLabel(
            "편집 모드 시작 후:\n"
            "- 도구모음에서 '라인 피처 추가'를 선택\n"
            "- 지도에서 클릭하여 시설 노선을 그립니다\n"
            "- 마우스 우클릭으로 피처 완성"
        )
        edit_desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        edit_desc.setWordWrap(True)
        lc_layout.addWidget(edit_desc)

        list_card.setLayout(lc_layout)
        layout.addWidget(list_card)

        # 상태
        self.status_label = QLabel()
        self.status_label.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")
        layout.addWidget(self.status_label)

        self.setLayout(layout)

        # 초기 미리보기
        self._update_preview()

    def on_enter(self):
        """페이지 진입 시"""
        pass

    def _update_preview(self):
        """선택한 색상+선종 미리보기 갱신"""
        color_idx = self.color_combo.currentIndex()
        style_idx = self.style_combo.currentIndex()

        if color_idx < 0 or style_idx < 0:
            return

        color_hex = FACILITY_COLORS[color_idx][1]
        pen_style = LINE_STYLES[style_idx][1]

        # 미리보기 이미지 생성
        pixmap = QPixmap(200, 30)
        pixmap.fill(QColor("#FFFFFF"))
        painter = QPainter(pixmap)
        painter.setRenderHint(QPainter.Antialiasing)

        # 외곽선
        pen = QPen(QColor(color_hex))
        pen.setWidth(6)
        pen.setStyle(pen_style)
        painter.setPen(pen)
        painter.drawLine(10, 15, 190, 15)

        # 내부선 (흰색)
        pen2 = QPen(QColor("#FFFFFF"))
        pen2.setWidth(2)
        pen2.setStyle(pen_style)
        painter.setPen(pen2)
        painter.drawLine(10, 15, 190, 15)

        painter.end()
        self.preview_widget.setPixmap(pixmap)

    def _create_facility_layer(self):
        """계획시설 레이어 생성"""
        layer_name = self.name_input.text().strip()
        if not layer_name:
            QMessageBox.warning(self, "알림", "레이어 이름을 입력해주세요.")
            return

        crs = QgsProject.instance().crs()
        create_type = self.type_combo.currentData()

        color_idx = self.color_combo.currentIndex()
        style_idx = self.style_combo.currentIndex()
        color_hex = FACILITY_COLORS[color_idx][1]
        pen_style = LINE_STYLES[style_idx][1]

        # 레이어 생성
        if create_type == "memory":
            layer = self._create_memory_layer(layer_name, crs)
        else:
            layer = self._create_shp_layer(layer_name, crs)

        if layer is None or not layer.isValid():
            QMessageBox.critical(self, "오류", "레이어 생성에 실패했습니다.")
            return

        # 이중선 스타일 적용
        symbol = create_double_line_symbol(color_hex, pen_style)
        layer.renderer().setSymbol(symbol)
        layer.triggerRepaint()

        # 프로젝트 최상단에 추가
        QgsProject.instance().addMapLayer(layer)
        root = QgsProject.instance().layerTreeRoot()
        tree_layer = root.findLayer(layer.id())
        if tree_layer:
            clone = tree_layer.clone()
            root.insertChildNode(0, clone)
            root.removeChildNode(tree_layer)

        self._created_layers.append(layer)
        self.shared_data["route_layer"] = layer

        # 목록에 추가
        color_name = FACILITY_COLORS[color_idx][0]
        style_name = LINE_STYLES[style_idx][0]
        self.layer_list.addItem(f"{layer_name} [{color_name}, {style_name}]")

        self.btn_edit.setEnabled(True)
        self.btn_snap.setEnabled(True)
        self.name_input.clear()

        self.status_label.setText(f"계획시설 레이어 생성 완료: {layer_name}")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )

    def _create_memory_layer(self, name, crs):
        """메모리 레이어 생성"""
        uri = (
            f"LineString?crs={crs.authid()}"
            f"&field=id:integer"
            f"&field=name:string(100)"
            f"&field=type:string(50)"
            f"&field=diameter:double"
            f"&field=material:string(50)"
            f"&field=memo:string(255)"
        )
        layer = QgsVectorLayer(uri, name, "memory")
        return layer if layer.isValid() else None

    def _create_shp_layer(self, name, crs):
        """Shapefile 레이어 생성"""
        filepath, _ = QFileDialog.getSaveFileName(
            self, "계획시설 Shapefile 저장 위치",
            f"{name}.shp", "Shapefile (*.shp)",
        )
        if not filepath:
            return None

        fields = QgsFields()
        fields.append(QgsField("id", QVariant.Int))
        fields.append(QgsField("name", QVariant.String, len=100))
        fields.append(QgsField("type", QVariant.String, len=50))
        fields.append(QgsField("diameter", QVariant.Double))
        fields.append(QgsField("material", QVariant.String, len=50))
        fields.append(QgsField("memo", QVariant.String, len=255))

        options = QgsVectorFileWriter.SaveVectorOptions()
        options.driverName = "ESRI Shapefile"
        options.fileEncoding = "UTF-8"

        writer = QgsVectorFileWriter.create(
            filepath, fields, QgsWkbTypes.LineString, crs,
            QgsProject.instance().transformContext(), options,
        )
        if writer is None or writer.hasError() != QgsVectorFileWriter.NoError:
            return None
        del writer
        layer = QgsVectorLayer(filepath, name, "ogr")
        return layer if layer.isValid() else None

    def _toggle_editing(self):
        """선택된 레이어의 편집 모드 토글"""
        idx = self.layer_list.currentRow()
        if idx < 0 and self._created_layers:
            idx = len(self._created_layers) - 1

        if idx < 0 or idx >= len(self._created_layers):
            QMessageBox.warning(self, "알림", "편집할 레이어를 선택해주세요.")
            return

        layer = self._created_layers[idx]
        self.iface.setActiveLayer(layer)

        if layer.isEditable():
            layer.commitChanges()
            self.btn_edit.setText("편집 모드 시작")
            self.status_label.setText("편집 저장 완료")
        else:
            layer.startEditing()
            self.btn_edit.setText("편집 저장")
            self.status_label.setText(
                "편집 모드 활성화 - 도구모음에서 '라인 피처 추가' 선택"
            )
            parent_dialog = self.window()
            if parent_dialog:
                parent_dialog.showMinimized()

    def _configure_snapping(self):
        """스냅 설정"""
        config = QgsProject.instance().snappingConfig()
        config.setEnabled(True)
        config.setMode(QgsSnappingConfig.AllLayers)
        config.setType(QgsSnappingConfig.VertexAndSegment)
        config.setTolerance(10)
        config.setUnits(QgsTolerance.Pixels)
        QgsProject.instance().setSnappingConfig(config)

        self.status_label.setText("스냅 설정 완료 (모든 레이어, 정점+세그먼트, 10px)")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )

    def reset(self):
        self._created_layers.clear()
        self.layer_list.clear()
        self.name_input.clear()
        self.btn_edit.setEnabled(False)
        self.btn_snap.setEnabled(False)
        self.status_label.setText("")

    def execute_step(self):
        for layer in self._created_layers:
            if layer.isEditable():
                layer.commitChanges()
        return True
