# -*- coding: utf-8 -*-
"""
Step 2: 사업지역 선택
행정구역 계층(시도→시군구→읍면동)을 선택하여 작업 지역을 사전 설정한다.
선택 시 지도가 해당 지역으로 이동하고, 데이터 로드 단계에서 사전 선택 코드를 사용한다.
"""

import csv
from pathlib import Path

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QFrame, QComboBox, QPushButton,
)
from qgis.PyQt.QtCore import Qt

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE


class Step2Region(QWidget):
    """사업지역 선택 페이지 (시도 → 시군구 → 읍면동 계층 선택)"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.region_data = {}  # {adm_cd: adm_nm}
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
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(16)

        # 안내
        guide = QLabel(
            "사업 대상 지역을 선택합니다.\n"
            "시도 → 시군구 → 읍면동 순서로 선택한 후\n"
            "확인 버튼을 누르면 지도가 해당 지역으로 이동합니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 행정구역 선택 카드
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        card_layout = QVBoxLayout()
        card_layout.setContentsMargins(16, 16, 16, 16)
        card_layout.setSpacing(12)

        card_title = QLabel("행정구역 선택")
        card_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        card_layout.addWidget(card_title)

        # 시도
        row_sido = QHBoxLayout()
        lbl_sido = QLabel("시도")
        lbl_sido.setFixedWidth(60)
        lbl_sido.setStyleSheet("font-size: 13px; font-weight: 600; color: #374151; border: none;")
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
        lbl_sigungu.setStyleSheet("font-size: 13px; font-weight: 600; color: #374151; border: none;")
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
        lbl_emd.setStyleSheet("font-size: 13px; font-weight: 600; color: #374151; border: none;")
        self.combo_emd = QComboBox()
        self.combo_emd.setMaxVisibleItems(20)
        self.combo_emd.setFixedHeight(30)
        self.combo_emd.setStyleSheet(COMPACT_COMBO)
        self.combo_emd.addItem("-- 읍면동 선택 --", None)
        self.combo_emd.currentIndexChanged.connect(self._on_emd_changed)
        row_emd.addWidget(lbl_emd)
        row_emd.addWidget(self.combo_emd, 1)
        card_layout.addLayout(row_emd)

        card.setLayout(card_layout)
        layout.addWidget(card)

        # 확인 버튼
        self.btn_confirm = QPushButton("지역 확인 및 이동")
        self.btn_confirm.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_confirm.setCursor(Qt.PointingHandCursor)
        self.btn_confirm.setFixedHeight(42)
        self.btn_confirm.clicked.connect(self._on_confirm)
        layout.addWidget(self.btn_confirm)

        # 상태 표시
        self.status_label = QLabel("지역이 선택되지 않았습니다.")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #ef4444; padding: 8px; font-weight: 600;"
        )
        layout.addWidget(self.status_label)

        layout.addStretch()
        self.setLayout(layout)

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

    def _on_emd_changed(self, index):
        if index <= 0:
            return

        emd_code = self.combo_emd.currentData()
        if not emd_code:
            return

        self.shared_data["selected_emd_codes"] = [emd_code]

        sido_name = self.combo_sido.currentText()
        sigungu_name = self.combo_sigungu.currentText()
        emd_name = self.combo_emd.currentText()
        self.shared_data["selected_region_name"] = f"{sido_name} {sigungu_name} {emd_name}"

    def _on_confirm(self):
        """확인 버튼: 선택된 수준(시도/시군구/읍면동)에 따라 지도 이동"""
        # 가장 세밀한 수준부터 확인
        emd_code = self.combo_emd.currentData()
        sigungu_code = self.combo_sigungu.currentData()
        sido_code = self.combo_sido.currentData()

        if emd_code:
            target_code = emd_code
            sido_name = self.combo_sido.currentText()
            sigungu_name = self.combo_sigungu.currentText()
            emd_name = self.combo_emd.currentText()
            self.shared_data["selected_emd_codes"] = [emd_code]
            self.shared_data["selected_region_name"] = f"{sido_name} {sigungu_name} {emd_name}"
        elif sigungu_code:
            target_code = sigungu_code
            sido_name = self.combo_sido.currentText()
            sigungu_name = self.combo_sigungu.currentText()
            self.shared_data["selected_emd_codes"] = []
            self.shared_data["selected_region_name"] = f"{sido_name} {sigungu_name}"
        elif sido_code:
            target_code = sido_code
            sido_name = self.combo_sido.currentText()
            self.shared_data["selected_emd_codes"] = []
            self.shared_data["selected_region_name"] = sido_name
        else:
            self.status_label.setText("지역을 선택해주세요.")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; padding: 8px; font-weight: 600;"
            )
            return

        self._zoom_to_region(target_code)

    # ------------------------------------------------------------------
    # 지도 이동 (sgis_hjd 역질의: code → bbox)
    # ------------------------------------------------------------------
    def _zoom_to_region(self, emd_code):
        from qgis.core import (
            QgsDataSourceUri, QgsVectorLayer, QgsProject,
            QgsCoordinateReferenceSystem, QgsCoordinateTransform,
        )
        from ..db_env import DB_HOST, DB_PORT, DB_NAME, DB_SCHEMA, DB_USER, DB_PASSWORD

        sql_filter = f"adm_cd = '{emd_code}'"

        uri = QgsDataSourceUri()
        uri.setConnection(DB_HOST, str(DB_PORT), DB_NAME, DB_USER, DB_PASSWORD)
        uri.setDataSource(DB_SCHEMA, "sgis_hjd", "geometry", sql_filter, "adm_cd")

        layer = QgsVectorLayer(uri.uri(), "_region_zoom", "postgres")
        if not layer.isValid() or layer.featureCount() <= 0:
            self.status_label.setText("지역 geometry 조회 실패 (DB 연결 확인)")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; padding: 4px;"
            )
            return False

        for feat in layer.getFeatures():
            geom = feat.geometry()
            bbox_5186 = geom.boundingBox()
            break
        else:
            return False

        # CRS 변환 (DB 5186 → 프로젝트 CRS)
        db_crs = QgsCoordinateReferenceSystem("EPSG:5186")
        project_crs = QgsProject.instance().crs()
        if db_crs.authid() != project_crs.authid():
            transform = QgsCoordinateTransform(
                db_crs, project_crs, QgsProject.instance()
            )
            bbox_project = transform.transformBoundingBox(bbox_5186)
        else:
            bbox_project = bbox_5186

        canvas = self.iface.mapCanvas()
        bbox_project.scale(1.1)
        canvas.setExtent(bbox_project)
        canvas.refresh()

        region_name = self.region_data.get(emd_code, emd_code)
        self.status_label.setText(f"지도 이동 완료: {region_name}")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )
        return True

    # ------------------------------------------------------------------
    # 페이지 진입 / 초기화
    # ------------------------------------------------------------------
    def on_enter(self):
        """페이지 진입 시 현재 선택 상태 표시"""
        region_name = self.shared_data.get("selected_region_name", "")
        if region_name:
            self.status_label.setText(f"선택된 지역: {region_name}")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #059669; padding: 8px; font-weight: 600;"
            )
        else:
            self.status_label.setText("지역이 선택되지 않았습니다.")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; padding: 8px; font-weight: 600;"
            )

    def reset(self):
        """상태 초기화"""
        self.combo_sido.setCurrentIndex(0)
        self.combo_sigungu.clear()
        self.combo_sigungu.addItem("-- 시군구 선택 --", None)
        self.combo_emd.clear()
        self.combo_emd.addItem("-- 읍면동 선택 --", None)
        self.shared_data["selected_emd_codes"] = []
        self.shared_data["selected_region_name"] = ""
        self.status_label.setText("지역이 선택되지 않았습니다.")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #ef4444; padding: 8px; font-weight: 600;"
        )
