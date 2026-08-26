# -*- coding: utf-8 -*-
"""Step 2: 용지(사업)경계 입력.

3가지 방식으로 용지경계 폴리곤을 QGIS 캔버스 "용지경계" 레이어로 추가한다(EPSG:5186 정합):
  - 프로젝트 레이어 선택
  - 파일 업로드 (SHP / DXF) — CAD 는 좌표계 지정 필수
  - 지도에서 직접 그리기

CAD(DXF) 파일은 좌표계 정보를 담지 않으므로, "이 파일의 좌표계 = 중부원점(EPSG:5186)"
을 한 번 지정하는 단계를 둔다(기획서 v2 §3-2단계). DWG 는 미지원 — 'DXF 저장' 안내.
"""
from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QRadioButton, QButtonGroup, QComboBox, QMessageBox,
)
from qgis.PyQt.QtCore import Qt, pyqtSignal

try:
    from qgis.gui import QgsMapLayerComboBox
    from qgis.core import QgsMapLayerProxyModel
    _HAS_QGIS = True
except ImportError:
    _HAS_QGIS = False

from .styles import (
    CARD_STYLE, GUIDE_STYLE, RESULT_STYLE,
    PRIMARY_BUTTON_STYLE,
)


# 좌표계 지정 옵션 (파일에 좌표계 정보가 없을 때만 적용 — 폴백)
CRS_OPTIONS = [
    ("중부원점 EPSG:5186 (권장)", 5186),
    ("파일 좌표계 사용 (.prj 있을 때)", None),
    ("EPSG:5187 (동부원점)", 5187),
    ("EPSG:5185 (서부원점)", 5185),
    ("EPSG:5174 (구 중부)", 5174),
    ("EPSG:5179 (UTM-K)", 5179),
]


class Step2Boundary(QWidget):
    boundary_added = pyqtSignal()  # controller가 캔버스 추가 후 emit

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(16)

        guide = QLabel(
            "사업으로 편입되는 땅의 경계(용지경계)를 입력합니다.\n"
            "3가지 방식 중 선택 가능하며, 모두 EPSG:5186으로 자동 정합됩니다.\n"
            "CAD(DXF) 파일은 좌표계 정보가 없으므로 아래에서 좌표계를 지정하세요."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 방식 선택 카드
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        cl = QVBoxLayout()
        cl.setContentsMargins(16, 14, 16, 14)
        cl.setSpacing(10)

        cl_title = QLabel("입력 방식 선택")
        cl_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        cl.addWidget(cl_title)

        self.rb_layer = QRadioButton("프로젝트 레이어 선택 (현재 프로젝트의 폴리곤 레이어)")
        self.rb_layer.setChecked(True)
        self.rb_file = QRadioButton("파일 업로드 (SHP / DXF) — DWG 는 'DXF 저장' 후")
        self.rb_draw = QRadioButton("지도에서 직접 그리기 (캔버스에서 폴리곤을 그림)")
        self.mode_group = QButtonGroup(self)
        for rb in (self.rb_layer, self.rb_file, self.rb_draw):
            rb.setStyleSheet("font-size:14px;border:none;")
            self.mode_group.addButton(rb)
            cl.addWidget(rb)
        self.mode_group.buttonClicked.connect(self._on_mode_changed)

        # 레이어 선택 콤보 (프로젝트 모드)
        layer_row = QHBoxLayout()
        layer_lbl = QLabel("레이어:")
        layer_lbl.setStyleSheet("font-size:13px;color:#374151;border:none;")
        layer_row.addWidget(layer_lbl)
        if _HAS_QGIS:
            self.layer_combo = QgsMapLayerComboBox()
            try:
                self.layer_combo.setFilters(QgsMapLayerProxyModel.PolygonLayer)
            except Exception:
                pass
        else:
            self.layer_combo = QComboBox()
            self.layer_combo.addItem("(QGIS 환경에서만 동작)")
        layer_row.addWidget(self.layer_combo, 1)
        cl.addLayout(layer_row)

        # 좌표계 지정 (파일/CAD 좌표계 미포함 시 적용)
        crs_row = QHBoxLayout()
        crs_lbl = QLabel("좌표계 지정:")
        crs_lbl.setStyleSheet("font-size:13px;color:#374151;border:none;")
        crs_row.addWidget(crs_lbl)
        self.crs_combo = QComboBox()
        for label, epsg in CRS_OPTIONS:
            self.crs_combo.addItem(label, epsg)
        crs_row.addWidget(self.crs_combo, 1)
        cl.addLayout(crs_row)
        crs_hint = QLabel("※ 좌표계 정보가 없는 파일(특히 DXF)에만 적용됩니다. .prj 가 있는 SHP 는 파일 좌표계 우선.")
        crs_hint.setStyleSheet("font-size:11px;color:#9ca3af;border:none;")
        crs_hint.setWordWrap(True)
        cl.addWidget(crs_hint)

        card.setLayout(cl)
        layout.addWidget(card)

        # 액션 버튼
        self.btn_add = QPushButton("캔버스에 '용지경계' 레이어 추가")
        self.btn_add.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_add.setCursor(Qt.PointingHandCursor)
        self.btn_add.setFixedHeight(36)
        self.btn_add.clicked.connect(self._on_add_clicked)
        layout.addWidget(self.btn_add)

        self.result_label = QLabel("아직 용지경계가 추가되지 않았습니다.")
        self.result_label.setStyleSheet("font-size:13px;color:#6b7280;padding:8px;border:none;")
        self.result_label.setWordWrap(True)
        layout.addWidget(self.result_label)

        layout.addStretch()
        self.setLayout(layout)
        self._on_mode_changed()

    def get_mode(self) -> str:
        if self.rb_file.isChecked():
            return "file"
        if self.rb_draw.isChecked():
            return "draw"
        return "layer"

    def get_selected_layer(self):
        if _HAS_QGIS and hasattr(self.layer_combo, "currentLayer"):
            return self.layer_combo.currentLayer()
        return None

    def _on_mode_changed(self, *_):
        is_layer = self.rb_layer.isChecked()
        self.layer_combo.setEnabled(is_layer)
        self.crs_combo.setEnabled(not is_layer)  # 파일/그리기 모드에서만 좌표계 지정 의미
        if self.rb_file.isChecked():
            self.btn_add.setText("파일 선택(SHP/DXF) → '용지경계' 추가")
        elif self.rb_draw.isChecked():
            self.btn_add.setText("그리기 시작 → '용지경계' 레이어 생성")
        else:
            self.btn_add.setText("캔버스에 '용지경계' 레이어 추가")

    def _on_add_clicked(self):
        mode = self.get_mode()
        if mode == "layer":
            layer = self.get_selected_layer()
            if layer is None:
                self.set_result("프로젝트의 폴리곤 레이어를 선택하세요.", "error")
                return
            self.shared_data["boundary_input_layer"] = layer
        self.shared_data["boundary_mode"] = mode
        self.shared_data["boundary_assign_epsg"] = self.crs_combo.currentData()
        self.boundary_added.emit()

    def set_result(self, text, level="info"):
        if level == "error":
            self.result_label.setText("⚠ " + text)
            self.result_label.setStyleSheet("font-size:13px;color:#dc2626;font-weight:600;padding:8px;border:none;")
        elif level == "success":
            self.result_label.setText("✓ " + text)
            self.result_label.setStyleSheet(RESULT_STYLE)
        else:
            self.result_label.setText(text)
            self.result_label.setStyleSheet("font-size:13px;color:#6b7280;padding:8px;border:none;")

    def reset(self):
        self.rb_layer.setChecked(True)
        self.crs_combo.setCurrentIndex(0)
        self._on_mode_changed()
        self.set_result("아직 용지경계가 추가되지 않았습니다.", "info")

    def on_enter(self):
        pass

    def execute_step(self):
        if not self.shared_data.get("boundary_layer"):
            QMessageBox.information(self, "안내", "용지경계를 캔버스에 먼저 추가하세요.")
            return False
        return True
