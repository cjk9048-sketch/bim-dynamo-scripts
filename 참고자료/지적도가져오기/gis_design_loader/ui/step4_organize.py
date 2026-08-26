# -*- coding: utf-8 -*-
"""
Step 5: 레이어 병합 및 스타일 정리
로드된 레이어를 그룹화하고 스타일을 일괄 적용
"""

import os

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QCheckBox, QScrollArea, QSpinBox,
    QComboBox, QGridLayout,
)
from qgis.PyQt.QtCore import Qt
from qgis.core import QgsProject, QgsVectorLayer, QgsRasterLayer

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE
from ..core.style_manager import StyleManager
from ..core.layer_loader import AVAILABLE_LAYERS, LAYER_GROUP_ORDER


class Step4Organize(QWidget):
    """레이어 정리 및 스타일 적용 페이지"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.style_manager = StyleManager()
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        guide = QLabel(
            "로드된 레이어를 그룹별로 정리하고 스타일을 적용합니다.\n"
            "배경지도(Vworld) 투명도도 조절할 수 있습니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 그룹화 카드
        group_card = QFrame()
        group_card.setStyleSheet(CARD_STYLE)
        gc_layout = QVBoxLayout()
        gc_layout.setContentsMargins(16, 12, 16, 12)
        gc_layout.setSpacing(8)

        gc_title = QLabel("레이어 그룹화")
        gc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        gc_layout.addWidget(gc_title)

        gc_desc = QLabel(
            "로드된 레이어를 용도별 그룹(현황_도로, 현황_수계 등)으로 자동 분류합니다."
        )
        gc_desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        gc_desc.setWordWrap(True)
        gc_layout.addWidget(gc_desc)

        btn_group = QPushButton("레이어 그룹화 실행")
        btn_group.setStyleSheet(PRIMARY_BUTTON_STYLE)
        btn_group.setCursor(Qt.PointingHandCursor)
        btn_group.setFixedHeight(38)
        btn_group.clicked.connect(self._execute_grouping)
        gc_layout.addWidget(btn_group)

        group_card.setLayout(gc_layout)
        layout.addWidget(group_card)

        # 스타일 매핑 카드
        style_card = QFrame()
        style_card.setStyleSheet(CARD_STYLE)
        sc_layout = QVBoxLayout()
        sc_layout.setContentsMargins(16, 12, 16, 12)
        sc_layout.setSpacing(6)

        sc_header = QHBoxLayout()
        sc_title = QLabel("QML 스타일 매핑")
        sc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        sc_header.addWidget(sc_title)
        sc_header.addStretch()

        btn_style = QPushButton("일괄 적용")
        btn_style.setStyleSheet(PRIMARY_BUTTON_STYLE)
        btn_style.setCursor(Qt.PointingHandCursor)
        btn_style.setFixedWidth(100)
        btn_style.clicked.connect(self._apply_styles)
        sc_header.addWidget(btn_style)
        sc_layout.addLayout(sc_header)

        # QML 매핑 스크롤 영역
        qml_scroll = QScrollArea()
        qml_scroll.setWidgetResizable(True)
        qml_scroll.setStyleSheet("border: none;")
        qml_scroll.setMaximumHeight(280)

        self.qml_list_widget = QWidget()
        self.qml_list_layout = QVBoxLayout()
        self.qml_list_layout.setContentsMargins(0, 0, 0, 0)
        self.qml_list_layout.setSpacing(2)
        self.qml_list_widget.setLayout(self.qml_list_layout)
        qml_scroll.setWidget(self.qml_list_widget)
        sc_layout.addWidget(qml_scroll)

        style_card.setLayout(sc_layout)
        layout.addWidget(style_card)

        # QML 파일 목록 캐싱
        self._qml_files = self._scan_qml_files()
        self._layer_combos = {}  # layer_name → QComboBox

        # 배경지도 투명도 카드
        bg_card = QFrame()
        bg_card.setStyleSheet(CARD_STYLE)
        bg_layout = QHBoxLayout()
        bg_layout.setContentsMargins(16, 12, 16, 12)

        bg_label = QLabel("배경지도(래스터) 투명도:")
        bg_label.setStyleSheet(
            "font-size: 13px; color: #374151; border: none;"
        )
        bg_layout.addWidget(bg_label)

        self.opacity_spin = QSpinBox()
        self.opacity_spin.setRange(0, 100)
        self.opacity_spin.setValue(60)
        self.opacity_spin.setSuffix("%")
        bg_layout.addWidget(self.opacity_spin)

        btn_opacity = QPushButton("적용")
        btn_opacity.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_opacity.setCursor(Qt.PointingHandCursor)
        btn_opacity.setFixedWidth(60)
        btn_opacity.clicked.connect(self._apply_opacity)
        bg_layout.addWidget(btn_opacity)

        bg_card.setLayout(bg_layout)
        layout.addWidget(bg_card)

        # 상태
        self.status_label = QLabel()
        self.status_label.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")
        layout.addWidget(self.status_label)

        layout.addStretch()
        self.setLayout(layout)

    def _execute_grouping(self):
        """레이어를 그룹별로 분류 (layer_loader 그룹 기준)"""
        root = QgsProject.instance().layerTreeRoot()
        loaded = self.shared_data.get("loaded_layers", [])

        if not loaded:
            QMessageBox.warning(self, "알림", "로드된 레이어가 없습니다.")
            return

        # layer_loader의 AVAILABLE_LAYERS에서 그룹별 레이어명 매핑 생성
        from collections import OrderedDict
        group_layers = OrderedDict()
        for group_name in LAYER_GROUP_ORDER:
            group_layers[group_name] = []
        for info in AVAILABLE_LAYERS:
            g = info.get("group", "기타")
            if g not in group_layers:
                group_layers[g] = []
            group_layers[g].append(info["name"])

        # 전체 노드 이동 중 레이어 자동 삭제 방지
        from qgis.core import QgsLayerTreeLayer
        bridge = QgsProject.instance().layerTreeRegistryBridge()
        bridge.setEnabled(False)

        try:
            # 1) 로드된 레이어를 그룹으로 분류
            grouped_count = 0
            for group_name, layer_names in group_layers.items():
                group_node = None

                for layer in loaded:
                    if layer.name() in layer_names or any(
                        ln in layer.name() for ln in layer_names
                    ):
                        if group_node is None:
                            existing = root.findGroup(group_name)
                            group_node = existing or root.addGroup(group_name)

                        tree_layer = root.findLayer(layer.id())
                        if tree_layer:
                            clone = tree_layer.clone()
                            group_node.addChildNode(clone)
                            parent = tree_layer.parent()
                            if parent:
                                parent.removeChildNode(tree_layer)
                            grouped_count += 1

            # 2) 래스터 레이어(브이월드 배경지도 등)를 맨 하단으로 이동
            raster_nodes = []
            for child in list(root.children()):
                if hasattr(child, 'layerId'):
                    map_layer = QgsProject.instance().mapLayer(child.layerId())
                    if isinstance(map_layer, QgsRasterLayer):
                        raster_nodes.append((child, map_layer))
            for node, layer_ref in raster_nodes:
                root.removeChildNode(node)
                root.addChildNode(QgsLayerTreeLayer(layer_ref))

            # 3) 작업범위 레이어를 맨 상단으로 이동
            boundary = self.shared_data.get("boundary_layer")
            try:
                boundary_valid = boundary is not None and boundary.isValid()
            except RuntimeError:
                boundary_valid = False

            if boundary_valid:
                tree_layer = root.findLayer(boundary.id())
                if tree_layer:
                    parent = tree_layer.parent()
                    if parent:
                        parent.removeChildNode(tree_layer)
                    root.insertChildNode(0, QgsLayerTreeLayer(boundary))

        finally:
            bridge.setEnabled(True)

        # 4) QML 스타일 자동 적용
        applied = []
        skipped = []
        for layer in loaded:
            if self.style_manager.apply_style_to_layer(layer):
                applied.append(layer.name())
            else:
                skipped.append(layer.name())

        self.iface.mapCanvas().refresh()

        msg = f"그룹화 완료: {grouped_count}개 분류, 스타일 적용: {len(applied)}/{len(loaded)}개"
        if skipped:
            msg += f"\n미적용: {', '.join(skipped)}"
        self.status_label.setText(msg)
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )

    def _scan_qml_files(self):
        """플러그인 styles/qml/ 폴더에서 QML 파일 목록 스캔"""
        qml_dir = os.path.join(
            os.path.dirname(os.path.dirname(__file__)), "styles", "qml"
        )
        if not os.path.isdir(qml_dir):
            return []
        return sorted([
            os.path.splitext(f)[0]
            for f in os.listdir(qml_dir) if f.endswith(".qml")
        ])

    def _refresh_qml_grid(self):
        """로드된 레이어 기반으로 QML 매핑 목록 갱신"""
        # 기존 위젯 제거
        while self.qml_list_layout.count():
            item = self.qml_list_layout.takeAt(0)
            w = item.widget()
            if w:
                w.deleteLater()
            elif item.layout():
                while item.layout().count():
                    sub = item.layout().takeAt(0)
                    if sub.widget():
                        sub.widget().deleteLater()
        self._layer_combos.clear()

        loaded = self.shared_data.get("loaded_layers", [])
        if not loaded:
            lbl = QLabel("서버 로드 후 이 페이지에서 확인하세요.")
            lbl.setStyleSheet("font-size: 12px; color: #9ca3af; border: none;")
            self.qml_list_layout.addWidget(lbl)
            return

        from ..core.style_manager import LAYER_QML_MAP
        qml_dir = os.path.join(
            os.path.dirname(os.path.dirname(__file__)), "styles", "qml"
        )

        for layer in loaded:
            name = layer.name()
            current_qml = LAYER_QML_MAP.get(name, "")
            has_qml = current_qml and os.path.exists(
                os.path.join(qml_dir, f"{current_qml}.qml")
            )

            row = QHBoxLayout()
            row.setSpacing(6)

            # 상태 아이콘
            status = QLabel("O" if has_qml else "X")
            status.setFixedWidth(20)
            status.setAlignment(Qt.AlignCenter)
            status.setStyleSheet(
                f"font-size: 12px; font-weight: bold; border: none; "
                f"color: {'#059669' if has_qml else '#ef4444'};"
            )
            row.addWidget(status)

            # 레이어명
            lbl = QLabel(name)
            lbl.setFixedWidth(140)
            lbl.setStyleSheet("font-size: 12px; color: #374151; border: none;")
            row.addWidget(lbl)

            # QML 콤보박스
            combo = QComboBox()
            combo.setStyleSheet("font-size: 11px;")
            combo.addItem("(없음)", "")
            for qml_name in self._qml_files:
                combo.addItem(f"{qml_name}.qml", qml_name)

            if current_qml:
                idx = combo.findData(current_qml)
                if idx >= 0:
                    combo.setCurrentIndex(idx)

            self._layer_combos[name] = combo
            row.addWidget(combo, 1)

            self.qml_list_layout.addLayout(row)

        self.qml_list_layout.addStretch()

    def _apply_styles(self):
        """콤보박스 선택 기반으로 QML 스타일 적용"""
        loaded = self.shared_data.get("loaded_layers", [])
        if not loaded:
            QMessageBox.warning(self, "알림", "로드된 레이어가 없습니다.")
            return

        qml_dir = os.path.join(
            os.path.dirname(os.path.dirname(__file__)), "styles", "qml"
        )
        applied = []
        skipped = []
        for layer in loaded:
            name = layer.name()
            combo = self._layer_combos.get(name)
            if combo:
                qml_name = combo.currentData()
                if qml_name:
                    qml_path = os.path.join(qml_dir, f"{qml_name}.qml")
                    if os.path.exists(qml_path):
                        msg, success = layer.loadNamedStyle(qml_path)
                        if success:
                            layer.triggerRepaint()
                            applied.append(name)
                            continue
            # 콤보 없거나 (없음) 선택 시 기존 style_manager 폴백
            if self.style_manager.apply_style_to_layer(layer):
                applied.append(name)
            else:
                skipped.append(name)

        self.iface.mapCanvas().refresh()
        msg = f"스타일 적용: {len(applied)}/{len(loaded)}개"
        if skipped:
            msg += f"\n미적용: {', '.join(skipped)}"
        self.status_label.setText(msg)
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )

    def _apply_opacity(self):
        """배경지도(래스터) 투명도 조절 — DEM 레이어는 제외"""
        opacity = self.opacity_spin.value() / 100.0
        count = 0
        for layer in QgsProject.instance().mapLayers().values():
            if isinstance(layer, QgsRasterLayer):
                name = layer.name().upper()
                # DEM 레이어는 배경지도가 아니므로 제외
                if "DEM" in name:
                    continue
                layer.setOpacity(opacity)
                layer.triggerRepaint()
                count += 1

        self.status_label.setText(f"투명도 {self.opacity_spin.value()}% 적용: {count}개 배경지도 레이어")

    def on_enter(self):
        """페이지 진입 시 QML 매핑 그리드 갱신"""
        self._refresh_qml_grid()

    def execute_step(self):
        return True
