# -*- coding: utf-8 -*-
"""
Step 1: 작업환경 설정 - 프로젝트 CRS를 EPSG:5186으로 설정
"""

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QRadioButton, QGroupBox, QFileDialog, QComboBox,
)
from qgis.PyQt.QtCore import Qt
from qgis.core import QgsProject, QgsCoordinateReferenceSystem

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE


# 빠른 선택용 CRS 목록
CRS_PRESETS = [
    ("EPSG:5186", "Korea 2000 / Central Belt 2010", True),
    ("EPSG:5179", "Korea 2000 / Unified CS", False),
    ("EPSG:5174", "Korea 1985 / Central Belt", False),
    ("EPSG:4326", "WGS 84 (위경도)", False),
]


class Step1Setup(QWidget):
    """프로젝트 CRS 설정 페이지"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.selected_crs = "EPSG:5186"
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(16)

        # 안내 텍스트
        guide = QLabel(
            "프로젝트의 좌표계(CRS)를 설정합니다.\n"
            "토목 설계 작업에는 EPSG:5186 (GRS80 중부원점)을 권장합니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # CRS 선택 카드
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        card_layout = QVBoxLayout()
        card_layout.setContentsMargins(16, 14, 16, 14)
        card_layout.setSpacing(8)

        card_title = QLabel("좌표계 선택")
        card_title.setStyleSheet(
            "font-size: 14px; font-weight: bold; color: #1f2937; border: none;"
        )
        card_layout.addWidget(card_title)

        self.crs_buttons = []
        for epsg, name, is_default in CRS_PRESETS:
            btn = QPushButton(f"{epsg}  -  {name}")
            btn.setCursor(Qt.PointingHandCursor)
            btn.setCheckable(True)
            btn.setChecked(is_default)
            btn.clicked.connect(lambda checked, e=epsg: self._on_crs_selected(e))
            self.crs_buttons.append((btn, epsg))
            card_layout.addWidget(btn)

        card.setLayout(card_layout)
        layout.addWidget(card)

        # 현재 프로젝트 CRS 표시
        self.current_crs_label = QLabel()
        self.current_crs_label.setStyleSheet(
            "font-size: 13px; color: #6b7280; padding: 8px;"
        )
        layout.addWidget(self.current_crs_label)

        # 적용 버튼
        btn_apply = QPushButton("CRS 적용")
        btn_apply.setStyleSheet(PRIMARY_BUTTON_STYLE)
        btn_apply.setCursor(Qt.PointingHandCursor)
        btn_apply.setFixedHeight(32)
        btn_apply.clicked.connect(self._apply_crs)
        layout.addWidget(btn_apply)

        # 생성방식 선택 카드
        save_card = QFrame()
        save_card.setStyleSheet(CARD_STYLE)
        sv_layout = QVBoxLayout()
        sv_layout.setContentsMargins(16, 14, 16, 14)
        sv_layout.setSpacing(10)

        sv_title = QLabel("레이어 생성 방식")
        sv_title.setStyleSheet(
            "font-size: 14px; font-weight: bold; color: #1f2937; border: none;"
        )
        sv_layout.addWidget(sv_title)

        sv_desc = QLabel("데이터 로드 시 레이어를 어떤 방식으로 생성할지 선택합니다.")
        sv_desc.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        sv_desc.setWordWrap(True)
        sv_layout.addWidget(sv_desc)

        # v1.4.6: 배치 순서 — 영구 레이어 → 저장폴더 → 저장형식 → 임시 레이어(맨 아래)
        self.radio_perm = QRadioButton("영구 레이어 (파일로 저장)")
        self.radio_perm.setStyleSheet("font-size: 14px; border: none;")
        self.radio_perm.toggled.connect(self._on_save_mode_changed)
        sv_layout.addWidget(self.radio_perm)

        # 저장 폴더 선택 (영구 레이어 바로 아래)
        folder_row = QHBoxLayout()
        self.btn_folder = QPushButton("저장 폴더 선택...")
        self.btn_folder.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_folder.setCursor(Qt.PointingHandCursor)
        self.btn_folder.setEnabled(False)
        self.btn_folder.clicked.connect(self._select_save_folder)
        folder_row.addWidget(self.btn_folder)

        self.folder_label = QLabel("폴더가 선택되지 않았습니다.")
        self.folder_label.setStyleSheet("font-size: 13px; color: #9ca3af; border: none;")
        folder_row.addWidget(self.folder_label, 1)
        sv_layout.addLayout(folder_row)

        # 저장 형식
        format_row = QHBoxLayout()
        format_lbl = QLabel("저장 형식:")
        format_lbl.setStyleSheet("font-size: 13px; color: #374151; border: none;")
        format_row.addWidget(format_lbl)

        self.format_combo = QComboBox()
        self.format_combo.addItem("GeoPackage (.gpkg)", "GPKG")
        self.format_combo.addItem("Shapefile (.shp)", "SHP")
        self.format_combo.setEnabled(False)
        self.format_combo.currentIndexChanged.connect(self._on_format_changed)
        format_row.addWidget(self.format_combo)
        format_row.addStretch()
        sv_layout.addLayout(format_row)

        # 임시 레이어 (맨 아래)
        self.radio_temp = QRadioButton("임시 레이어 (세션 종료 시 삭제)")
        self.radio_temp.setStyleSheet("font-size: 14px; border: none;")
        sv_layout.addWidget(self.radio_temp)

        # 기본값: 영구 레이어 (toggled 시그널이 btn_folder/format_combo 활성화)
        self.radio_perm.setChecked(True)

        save_card.setLayout(sv_layout)
        layout.addWidget(save_card)

        layout.addStretch()
        self.setLayout(layout)
        self._update_crs_display()
        self._update_button_styles()

    def _on_crs_selected(self, epsg):
        self.selected_crs = epsg
        # 라디오 버튼 효과
        for btn, e in self.crs_buttons:
            btn.setChecked(e == epsg)
        self._update_button_styles()

    def _update_button_styles(self):
        for btn, epsg in self.crs_buttons:
            if btn.isChecked():
                btn.setStyleSheet("""
                    QPushButton {
                        background-color: #1f2937; color: white;
                        border: none; border-radius: 4px;
                        font-size: 14px; font-weight: 600;
                        padding: 7px 12px; text-align: left;
                    }
                """)
            else:
                btn.setStyleSheet("""
                    QPushButton {
                        background-color: white; color: #374151;
                        border: 1px solid #e5e7eb; border-radius: 4px;
                        font-size: 14px; font-weight: 500;
                        padding: 7px 12px; text-align: left;
                    }
                    QPushButton:hover {
                        background-color: #f9fafb; border-color: #9ca3af;
                    }
                """)

    def _update_crs_display(self):
        project = QgsProject.instance()
        crs = project.crs()
        if crs.isValid():
            self.current_crs_label.setText(
                f"현재 프로젝트 CRS: {crs.authid()} ({crs.description()})"
            )
        else:
            self.current_crs_label.setText("현재 프로젝트 CRS: 설정되지 않음")

    def _apply_crs(self):
        crs = QgsCoordinateReferenceSystem(self.selected_crs)
        if not crs.isValid():
            QMessageBox.warning(self, "오류", f"유효하지 않은 CRS: {self.selected_crs}")
            return

        QgsProject.instance().setCrs(crs)
        self._update_crs_display()
        QMessageBox.information(
            self, "완료",
            f"프로젝트 CRS가 {self.selected_crs}로 설정되었습니다."
        )

    def _on_save_mode_changed(self, checked):
        """영구/임시 전환 시"""
        self.btn_folder.setEnabled(checked)
        self.format_combo.setEnabled(checked)
        if checked:
            self.shared_data["save_mode"] = "permanent"
            self._on_format_changed()
        else:
            self.shared_data["save_mode"] = "temporary"
            self.shared_data["save_directory"] = ""

    def _on_format_changed(self):
        """저장 형식 변경 시"""
        fmt = self.format_combo.currentData()
        self.shared_data["save_format"] = fmt  # "GPKG" or "SHP"

    def _select_save_folder(self):
        """저장 폴더 선택"""
        folder = QFileDialog.getExistingDirectory(self, "저장 폴더 선택", "")
        if folder:
            self.shared_data["save_directory"] = folder
            self.folder_label.setText(folder)
            self.folder_label.setStyleSheet(
                "font-size: 13px; color: #059669; border: none; font-weight: 600;"
            )

    def reset(self):
        """위자드 초기화 시 저장 모드 UI + 상태 리셋"""
        # 영구 레이어를 기본값으로 재설정 (toggled 시그널로 btn_folder/format_combo 활성화)
        self.radio_temp.setChecked(False)
        self.radio_perm.setChecked(True)
        self.folder_label.setText("폴더가 선택되지 않았습니다.")
        self.folder_label.setStyleSheet(
            "font-size: 13px; color: #9ca3af; border: none;"
        )
        self.format_combo.setCurrentIndex(0)
        # shared_data도 명시적으로 초기화
        self.shared_data["save_mode"] = "permanent"
        self.shared_data["save_directory"] = ""
        self.shared_data["save_format"] = "GPKG"

    def execute_step(self):
        """다음 버튼 클릭 시 호출"""
        project_crs = QgsProject.instance().crs()
        if not project_crs.isValid():
            QMessageBox.warning(self, "알림", "프로젝트 CRS를 먼저 설정해주세요.")
            return False
        # 영구 레이어 모드인데 저장 폴더 미선택 시 차단 (데이터 영구 손실 방지)
        if self.shared_data.get("save_mode") == "permanent" and not self.shared_data.get("save_directory"):
            QMessageBox.warning(
                self, "저장 폴더 필요",
                "영구 레이어 모드에서는 저장 폴더를 먼저 선택해야 합니다.\n"
                "'저장 폴더 선택...' 버튼으로 폴더를 지정하거나, 임시 레이어를 선택하세요."
            )
            return False
        return True
