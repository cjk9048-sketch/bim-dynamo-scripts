# -*- coding: utf-8 -*-
"""
GIS Design Loader - 메인 위자드 다이얼로그
QStackedWidget 기반 6단계 위자드
"""

from qgis.PyQt.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QWidget, QFrame, QStackedWidget, QSizePolicy, QMessageBox,
)
from qgis.PyQt.QtCore import Qt

from .styles import (
    DIALOG_STYLESHEET, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE,
    STEP_ACTIVE_STYLE, STEP_INACTIVE_STYLE, STEP_DONE_STYLE,
)
from .step1_setup import Step1Setup
from .step2_region_boundary import Step2RegionBoundary
from .step3_load_data import Step3LoadData
from .step5_obstacle import Step5LocalData
from .step6_route import Step7FacilityLayer
from .step7_layout import Step7LayoutCreator

# v1.5.0: 배포 변형용 기능 토글 (water / lite)
try:
    from ..core.feature_flags import INCLUDE_PLAN_FACILITY_STEP
except Exception:
    INCLUDE_PLAN_FACILITY_STEP = True  # import 실패 시 안전하게 풀 기능


def _build_step_titles():
    titles = ["작업환경", "범위설정", "서버 로드", "로컬 로드"]
    if INCLUDE_PLAN_FACILITY_STEP:
        titles.append("계획시설")
    titles.append("조판생성")
    return titles


STEP_TITLES = _build_step_titles()


class CivilPlannerWizard(QDialog):
    """6단계 위자드 메인 다이얼로그"""

    def __init__(self, iface, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.current_step = 0
        self.completed_steps = set()

        # 각 단계에서 공유하는 데이터
        self.shared_data = {
            "selected_emd_codes": [],       # 2단계 사업지역 선택
            "selected_region_name": "",      # 2단계 선택된 지역명
            "boundary_layer": None,          # 3단계에서 생성된 범위 레이어
            "loaded_layers": [],             # 4단계에서 로드된 레이어 목록
            "obstacle_layers": [],           # 6단계에서 로드된 지장물 레이어 목록
            "route_layer": None,             # 7단계에서 생성된 관로 레이어
            "save_mode": "temporary",        # 1단계 생성방식 (temporary/permanent)
            "save_directory": "",             # 1단계 저장 폴더 경로
            "save_format": "GPKG",           # 1단계 저장 형식 (GPKG/SHP)
            "layout_title": "계 획 평 면 도", # 6단계 조판 제목
            "layout": None,                   # 6단계 생성된 QgsPrintLayout 객체
        }

        self.setWindowFlags(Qt.Window)
        self.setWindowTitle("GIS Design Loader")
        self.setMinimumSize(580, 480)
        self.resize(680, 958)
        self.setStyleSheet(DIALOG_STYLESHEET)

        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setSpacing(0)
        layout.setContentsMargins(0, 0, 0, 0)

        # 헤더
        header = self._create_header()
        layout.addWidget(header)

        # 단계 표시 바
        self.step_bar = self._create_step_bar()
        layout.addWidget(self.step_bar)

        # 콘텐츠 (QStackedWidget + QScrollArea로 축소 가능하게)
        # v1.5.0: feature_flag로 계획시설 단계 포함 여부 결정
        self.stack = QStackedWidget()
        self.step_pages = [
            Step1Setup(self.iface, self.shared_data),
            Step2RegionBoundary(self.iface, self.shared_data),
            Step3LoadData(self.iface, self.shared_data),
            Step5LocalData(self.iface, self.shared_data),
        ]
        if INCLUDE_PLAN_FACILITY_STEP:
            self.step_pages.append(Step7FacilityLayer(self.iface, self.shared_data))
        self.step_pages.append(Step7LayoutCreator(self.iface, self.shared_data))
        for page in self.step_pages:
            self.stack.addWidget(page)

        # 콘텐츠를 스크롤 가능하게 감싸서 창 축소 시에도 자유롭게 리사이즈
        from qgis.PyQt.QtWidgets import QScrollArea
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.NoFrame)
        scroll.setStyleSheet("background-color: #f9fafb; border: none;")
        scroll.setWidget(self.stack)
        layout.addWidget(scroll, 1)

        # 하단 네비게이션
        nav = self._create_navigation()
        layout.addWidget(nav)

        self.setLayout(layout)
        self._update_ui()

    def _create_header(self):
        header = QFrame()
        header.setFixedHeight(56)
        header.setStyleSheet(
            "background-color: #1f2937; border: none;"
        )
        layout = QHBoxLayout()
        layout.setContentsMargins(20, 0, 20, 0)

        title = QLabel("GIS Design Loader")
        title.setStyleSheet(
            "color: white; font-size: 18px; font-weight: bold; border: none;"
        )
        layout.addWidget(title)
        layout.addStretch()

        subtitle = QLabel("GIS 설계 데이터 통합 워크플로우")
        subtitle.setStyleSheet(
            "color: #9ca3af; font-size: 13px; border: none;"
        )
        layout.addWidget(subtitle)

        header.setLayout(layout)
        return header

    def _create_step_bar(self):
        bar = QFrame()
        bar.setFixedHeight(48)
        bar.setStyleSheet(
            "background-color: white; border-bottom: 1px solid #e5e7eb;"
        )
        self.step_bar_layout = QHBoxLayout()
        self.step_bar_layout.setContentsMargins(16, 8, 16, 8)
        self.step_bar_layout.setSpacing(6)

        self.step_labels = []
        for i, title in enumerate(STEP_TITLES):
            lbl = QLabel(f" {i + 1}. {title} ")
            lbl.setAlignment(Qt.AlignCenter)
            lbl.setCursor(Qt.PointingHandCursor)
            lbl.mousePressEvent = lambda event, idx=i: self._on_step_clicked(idx)
            self.step_labels.append(lbl)
            self.step_bar_layout.addWidget(lbl, 1)
        bar.setLayout(self.step_bar_layout)
        return bar

    def _create_navigation(self):
        nav = QFrame()
        nav.setFixedHeight(64)
        nav.setStyleSheet(
            "background-color: white; border-top: 1px solid #e5e7eb;"
        )
        layout = QHBoxLayout()
        layout.setContentsMargins(20, 12, 20, 12)

        # 초기화 버튼
        self.btn_reset = QPushButton("초기화")
        self.btn_reset.setFixedWidth(80)
        self.btn_reset.setStyleSheet("""
            QPushButton {
                background-color: #fee2e2; border: 1px solid #fca5a5;
                border-radius: 4px; color: #dc2626;
                font-size: 13px; font-weight: 600; padding: 8px 12px;
            }
            QPushButton:hover { background-color: #fecaca; }
        """)
        self.btn_reset.setCursor(Qt.PointingHandCursor)
        self.btn_reset.clicked.connect(self._reset_wizard)
        layout.addWidget(self.btn_reset)

        # 이전 버튼
        self.btn_prev = QPushButton("이전")
        self.btn_prev.setFixedWidth(100)
        self.btn_prev.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_prev.setCursor(Qt.PointingHandCursor)
        self.btn_prev.clicked.connect(self._go_prev)
        layout.addWidget(self.btn_prev)

        layout.addStretch()

        # 현재 단계 레이블
        self.step_info_label = QLabel()
        self.step_info_label.setStyleSheet(
            "color: #6b7280; font-size: 13px; border: none;"
        )
        layout.addWidget(self.step_info_label)

        layout.addStretch()

        # 다음/완료 버튼
        self.btn_next = QPushButton("다음")
        self.btn_next.setFixedWidth(100)
        self.btn_next.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_next.setCursor(Qt.PointingHandCursor)
        self.btn_next.clicked.connect(self._go_next)
        layout.addWidget(self.btn_next)

        nav.setLayout(layout)
        return nav

    def _on_step_clicked(self, idx):
        """단계 표시 바 클릭 시 자유롭게 이동"""
        if 0 <= idx < len(self.step_pages):
            self.current_step = idx
            self._update_ui()
            page = self.step_pages[self.current_step]
            if hasattr(page, "on_enter"):
                page.on_enter()

    def _go_prev(self):
        if self.current_step > 0:
            self.current_step -= 1
            self._update_ui()
            page = self.step_pages[self.current_step]
            if hasattr(page, "on_enter"):
                page.on_enter()

    def _go_next(self):
        self.completed_steps.add(self.current_step)

        last_step = len(self.step_pages) - 1
        if self.current_step < last_step:
            self.current_step += 1
            self._update_ui()
            # 다음 페이지 진입 시 갱신
            next_page = self.step_pages[self.current_step]
            if hasattr(next_page, "on_enter"):
                next_page.on_enter()
        else:
            # 마지막 단계 완료
            self.completed_steps.add(last_step)
            self._update_ui()

    def _update_ui(self):
        """UI 상태 갱신"""
        self.stack.setCurrentIndex(self.current_step)

        # 단계 바 스타일 업데이트
        for i, lbl in enumerate(self.step_labels):
            if i in self.completed_steps and i != self.current_step:
                lbl.setStyleSheet(STEP_DONE_STYLE)
            elif i == self.current_step:
                lbl.setStyleSheet(STEP_ACTIVE_STYLE)
            else:
                lbl.setStyleSheet(STEP_INACTIVE_STYLE)

        # 네비게이션 버튼
        self.btn_prev.setEnabled(self.current_step > 0)
        last_step = len(self.step_pages) - 1
        if self.current_step == last_step:
            self.btn_next.setText("완료")
        else:
            self.btn_next.setText("다음")

        self.step_info_label.setText(
            f"{self.current_step + 1} / {len(self.step_pages)}  {STEP_TITLES[self.current_step]}"
        )

    def _reset_wizard(self):
        """전체 위자드 초기화"""
        reply = QMessageBox.question(
            self, "초기화 확인",
            "모든 작업 상태를 초기화하시겠습니까?\n"
            "로드된 레이어와 설정이 모두 리셋됩니다.",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if reply != QMessageBox.Yes:
            return

        # 상태 초기화
        self.current_step = 0
        self.completed_steps.clear()
        self.shared_data["selected_emd_codes"] = []
        self.shared_data["selected_region_name"] = ""
        self.shared_data["boundary_layer"] = None
        self.shared_data["loaded_layers"] = []
        self.shared_data["obstacle_layers"] = []
        self.shared_data["route_layer"] = None
        self.shared_data["save_mode"] = "temporary"
        self.shared_data["save_directory"] = ""
        self.shared_data["save_format"] = "GPKG"
        self.shared_data["layout_title"] = "계 획 평 면 도"
        self.shared_data["layout"] = None

        # 각 페이지 리셋 (reset 메서드가 있으면 호출)
        for page in self.step_pages:
            if hasattr(page, "reset"):
                page.reset()

        self._update_ui()
