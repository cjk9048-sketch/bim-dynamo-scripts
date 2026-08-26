# -*- coding: utf-8 -*-
"""Step 4: 산출물 출력 — 삽도 PNG + HWPX 용지편입검토서 초안.

- 삽도 PNG: 현재 캔버스 뷰 + 사용자 선택 배경(위성/수치지형도) 합성
- 검토서 .hwpx: 하이브리드 엔진
    · 도로부 양식(resources/templates/pyeonib_report.hwpx)이 있으면 → gis_cn 엔진으로 양식 채움
    · 없으면 → 동봉 python-hwpx 로 기본 초안 생성(지금 즉시 동작)
- 소유자 '이름'은 '선택필지' 속성테이블에 직접 입력한 값을 사용(자동 불가).
- 마스킹 토글(default OFF) — 결재 첨부 직전 ON 권장.
- 검토서는 초안 — 설계자가 한글에서 확인 후 결재(자동 출력 ≠ 자동 결재).
"""
import os

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QRadioButton, QButtonGroup, QLineEdit, QCheckBox,
    QSlider, QSpinBox,
)
from qgis.PyQt.QtCore import Qt, pyqtSignal

from .styles import (
    CARD_STYLE, GUIDE_STYLE, RESULT_STYLE, WARN_STYLE,
    PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE,
)


class Step4Export(QWidget):
    inset_export_requested = pyqtSignal()
    report_export_requested = pyqtSignal()

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
            "산출물 두 가지를 생성합니다.\n"
            "• 삽도 PNG — 용지경계 전체 + 출력 시점 배경 합성\n"
            "• 용지편입검토서 .hwpx — 삽도 + 편입면적표 자동 채움 (설계자 확인 후 결재)"
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # ── 삽도 카드 ──
        inset_card = QFrame()
        inset_card.setStyleSheet(CARD_STYLE)
        il = QVBoxLayout()
        il.setContentsMargins(16, 14, 16, 14)
        il.setSpacing(10)
        il_title = QLabel("① 삽도 (PNG)")
        il_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        il.addWidget(il_title)

        bg_row = QHBoxLayout()
        bg_lbl = QLabel("배경:")
        bg_lbl.setStyleSheet("font-size:13px;color:#374151;border:none;")
        bg_row.addWidget(bg_lbl)
        self.rb_sat = QRadioButton("위성사진")
        self.rb_sat.setChecked(True)
        self.rb_hybrid = QRadioButton("위성 하이브리드")
        self.rb_cadastral = QRadioButton("지적 배경")
        self.rb_topo = QRadioButton("수치지형도")
        self.bg_group = QButtonGroup(self)
        self.bg_group.addButton(self.rb_sat)
        self.bg_group.addButton(self.rb_hybrid)
        self.bg_group.addButton(self.rb_topo)
        self.bg_group.addButton(self.rb_cadastral)
        for rb in (self.rb_sat, self.rb_hybrid, self.rb_topo, self.rb_cadastral):
            rb.setStyleSheet("font-size:13px;border:none;")
        bg_row.addWidget(self.rb_sat)
        bg_row.addWidget(self.rb_hybrid)
        bg_row.addWidget(self.rb_topo)
        bg_row.addWidget(self.rb_cadastral)
        bg_row.addStretch()
        il.addLayout(bg_row)

        bg_hint = QLabel("※ 위성·하이브리드·수치지형도는 V-World 키가 필요합니다. 지적 배경은 DB(연속지적도) 조회입니다. 출력 시점의 설정이 삽도에 반영됩니다.")
        bg_hint.setStyleSheet("font-size:11px;color:#9ca3af;border:none;")
        bg_hint.setWordWrap(True)
        il.addWidget(bg_hint)

        brightness_row = QHBoxLayout()
        brightness_row.addWidget(QLabel("위성 밝기:"))
        self.brightness_slider = QSlider(Qt.Horizontal)
        self.brightness_slider.setRange(0, 100)
        self.brightness_slider.setValue(60)
        self.brightness_value = QSpinBox()
        self.brightness_value.setRange(0, 100)
        self.brightness_value.setValue(60)
        self.brightness_value.setSuffix(" / 100")
        self.brightness_slider.valueChanged.connect(self.brightness_value.setValue)
        self.brightness_value.valueChanged.connect(self.brightness_slider.setValue)
        brightness_row.addWidget(self.brightness_slider, 1)
        brightness_row.addWidget(self.brightness_value)
        il.addLayout(brightness_row)

        extent_row = QHBoxLayout()
        extent_lbl = QLabel("범위:")
        extent_lbl.setStyleSheet("font-size:13px;color:#374151;border:none;")
        extent_row.addWidget(extent_lbl)
        self.rb_extent_canvas = QRadioButton("현재 화면")
        self.rb_extent_boundary = QRadioButton("용지경계 전체")
        self.rb_extent_boundary.setChecked(True)
        self.rb_extent_canvas = QRadioButton("현재 화면")
        self.extent_group = QButtonGroup(self)
        self.extent_group.addButton(self.rb_extent_boundary)
        self.extent_group.addButton(self.rb_extent_canvas)
        for rb in (self.rb_extent_boundary, self.rb_extent_canvas):
            rb.setStyleSheet("font-size:13px;border:none;")
        extent_row.addWidget(self.rb_extent_boundary)
        extent_row.addWidget(self.rb_extent_canvas)
        extent_row.addStretch()
        il.addLayout(extent_row)

        self.btn_inset = QPushButton("삽도 PNG 저장")
        self.btn_inset.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_inset.setCursor(Qt.PointingHandCursor)
        self.btn_inset.setFixedHeight(34)
        self.btn_inset.clicked.connect(self.inset_export_requested.emit)
        il.addWidget(self.btn_inset)

        inset_card.setLayout(il)
        layout.addWidget(inset_card)

        # ── 검토서 메타 + 출력 카드 ──
        report_card = QFrame()
        report_card.setStyleSheet(CARD_STYLE)
        rl = QVBoxLayout()
        rl.setContentsMargins(16, 14, 16, 14)
        rl.setSpacing(10)
        rl_title = QLabel("② 용지편입검토서 초안 (.hwpx)")
        rl_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        rl.addWidget(rl_title)

        # 메타 입력
        self.edit_project = QLineEdit()
        self.edit_project.setPlaceholderText("과업명 (예: OO 도로 확포장 공사)")
        rl.addWidget(self.edit_project)
        self.edit_site = QLineEdit()
        self.edit_site.setPlaceholderText("사업지구 (예: 단양군 어상천면)")
        rl.addWidget(self.edit_site)
        self.edit_author = QLineEdit()
        self.edit_author.setPlaceholderText("작성자 (미입력 시 PC 로그인 계정)")
        rl.addWidget(self.edit_author)

        # 마스킹 토글
        self.chk_mask = QCheckBox("소유자명 마스킹 (결재 첨부 직전 ON 권장 — 기본 OFF)")
        rl.addWidget(self.chk_mask)

        owner_hint = QLabel(
            "※ 소유자 '이름'은 자동 조회가 불가합니다. 1단계 '선택필지' 레이어의 속성테이블에서 "
            "[소유자(직접입력)] 칸에 토지대장/등기 확인값을 입력하세요."
        )
        owner_hint.setStyleSheet("font-size:11px;color:#9ca3af;border:none;")
        owner_hint.setWordWrap(True)
        rl.addWidget(owner_hint)

        warn = QLabel("⚠ 자동 생성 초안 — 설계자가 한글에서 열어 확인 후 결재하십시오.")
        warn.setStyleSheet(WARN_STYLE)
        warn.setWordWrap(True)
        rl.addWidget(warn)

        self.btn_report = QPushButton("검토서 .hwpx 출력")
        self.btn_report.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_report.setCursor(Qt.PointingHandCursor)
        self.btn_report.setFixedHeight(36)
        self.btn_report.clicked.connect(self.report_export_requested.emit)
        rl.addWidget(self.btn_report)

        self.engine_label = QLabel()
        self.engine_label.setStyleSheet("font-size:11px;color:#6b7280;border:none;")
        self.engine_label.setWordWrap(True)
        rl.addWidget(self.engine_label)

        report_card.setLayout(rl)
        layout.addWidget(report_card)

        self.result_label = QLabel("출력 전입니다.")
        self.result_label.setStyleSheet("font-size:13px;color:#6b7280;padding:8px;border:none;")
        self.result_label.setWordWrap(True)
        layout.addWidget(self.result_label)

        layout.addStretch()
        self.setLayout(layout)

    def get_background(self) -> str:
        if self.rb_hybrid.isChecked():
            return "hybrid"
        if self.rb_cadastral.isChecked():
            return "cadastral"
        return "satellite" if self.rb_sat.isChecked() else "topo"

    def get_extent_mode(self) -> str:
        return "boundary" if self.rb_extent_boundary.isChecked() else "canvas"

    def get_brightness(self) -> int:
        return self.brightness_value.value()

    def get_meta(self) -> dict:
        author = self.edit_author.text().strip()
        if not author:
            try:
                author = os.getlogin()
            except Exception:
                author = ""
        return {
            "project_name": self.edit_project.text().strip(),
            "site_name": self.edit_site.text().strip(),
            "author": author,
        }

    def get_mask(self) -> bool:
        return self.chk_mask.isChecked()

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
        self.rb_sat.setChecked(True)
        self.rb_extent_boundary.setChecked(True)
        self.brightness_slider.setValue(60)
        self.edit_project.clear()
        self.edit_site.clear()
        self.edit_author.clear()
        self.chk_mask.setChecked(False)
        self.set_result("출력 전입니다.", "info")

    def on_enter(self):
        # 검토서 엔진 상태 표시(양식 수령 여부)
        try:
            from ..core import report_exporter
            if report_exporter.has_template():
                self.engine_label.setText("검토서 엔진: 도로부 양식 정합 출력 (resources/templates/pyeonib_report.hwpx)")
            else:
                self.engine_label.setText(
                    "검토서 엔진: 기본 초안(도로부 양식 미수령). 양식(.hwpx)을 "
                    "resources/templates/pyeonib_report.hwpx 로 넣으면 양식 정합 출력으로 전환됩니다."
                )
        except Exception:
            self.engine_label.setText("")

    def execute_step(self):
        return True  # 마지막 단계
