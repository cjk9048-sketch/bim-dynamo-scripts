# -*- coding: utf-8 -*-
"""민원검토 도구 — 메인 위자드 다이얼로그.

gis_design_loader_v2 와 동일한 위자드 패턴: 헤더(다크) + 단계 표시 바 + QStackedWidget +
하단 네비게이션(초기화·이전·다음). 4단계 워크플로:
  1. 지번 입력      → 캔버스에 '선택필지' (+ 옵션: 입력 반경 인접필지)
  2. 용지경계 입력  → 캔버스에 '용지경계'
  3. 편입면적 산출  → 캔버스에 '편입구역'
  4. 산출물 출력    → 삽도 PNG + HWPX 검토서 초안

모든 결과 데이터는 QGIS 캔버스 레이어로 추가되고, 사용자는 속성 테이블·식별 도구로 분석한다.
다이얼로그는 입력·액션 트리거 + 단계 진행 흐름만 담당한다.
"""
from qgis.PyQt.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QWidget, QFrame, QStackedWidget, QScrollArea, QMessageBox,
    QCheckBox, QSlider,
)
from qgis.PyQt.QtCore import Qt, pyqtSignal

from .styles import (
    DIALOG_STYLESHEET, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE,
    STEP_ACTIVE_STYLE, STEP_INACTIVE_STYLE, STEP_DONE_STYLE,
)
from .step1_parcel import Step1Parcel
from .step2_boundary import Step2Boundary
from .step3_intersect import Step3Intersect
from .step4_export import Step4Export


STEP_TITLES = ["지번 입력", "용지경계", "편입면적", "산출물"]


class ReviewWizard(QDialog):
    """4단계 위자드 메인 다이얼로그."""

    # controller가 연결할 액션 시그널 (각 step 위젯이 발신 → 위자드가 중계)
    search_requested = pyqtSignal()           # Step1 [검색] — 지번 후보 조회
    parcel_add_requested = pyqtSignal()       # Step1 [선택 필지 추가]
    bulk_parcel_add_requested = pyqtSignal()  # Step1 [일괄 조회] — 여러 줄 지번 누적 병합
    parcel_delete_requested = pyqtSignal()    # Step1 [선택 삭제] — 대상 필지 목록에서 제거
    parcel_clear_requested = pyqtSignal()     # Step1 [전체 비우기] — 누적 목록 전체 제거
    boundary_add_requested = pyqtSignal()     # Step2 [캔버스에 용지경계 추가]
    intersection_requested = pyqtSignal()     # Step3 [편입면적 산출]
    inset_export_requested = pyqtSignal()     # Step4 [삽도 PNG 저장]
    report_export_requested = pyqtSignal()    # Step4 [검토서 .hwpx]
    wizard_reset = pyqtSignal()               # [초기화] — controller가 캔버스 레이어 제거
    background_changed = pyqtSignal(str, bool, float)  # 배경 바(개발 건 #2): kind, on, opacity

    def __init__(self, iface, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.current_step = 0
        self.completed_steps = set()

        # 단계 간 공유 데이터 — controller가 채움/소비
        self.shared_data = {
            # Step1 입력 / 결과
            "jibun_text": "",
            "radius_m": 0,
            "candidates": [],          # search_candidates 결과(후보 목록)
            "selected_candidate": None,# 사용자가 고른 후보(PNU+대표점)
            "bulk_jibun_text": "",      # Step1 [일괄 조회] 입력 텍스트(여러 줄)
            "delete_keys": [],          # Step1 [선택 삭제] 대상 parcel_key 목록(전달용)
            "parcels": [],          # parcel_lookup 결과(누적 병합)
            "owners": [],           # owner_collector 결과
            "parcel_layer": None,   # 캔버스 핸들
            "neighbor_layer": None,
            # Step2 입력 / 결과
            "boundary_mode": "layer",       # 'layer' | 'file' | 'draw'
            "boundary_input_layer": None,   # 사용자가 선택한 원본 레이어
            "boundary_assign_epsg": None,   # 파일 좌표계 미포함 시 지정할 EPSG(폴백)
            "boundary_layer": None,         # 5186 정합 후 캔버스 추가된 레이어
            # Step3 결과
            "intersections": [],
            "inclusion_layer": None,
            # Step4 입력 / 결과
            "inset_background": "satellite",
            "inset_extent_mode": "canvas",
            "inset_png_path": None,
            "report_path": None,
        }

        self.setWindowFlags(Qt.Window)
        self.setWindowTitle("민원검토 도구")
        self.setMinimumSize(580, 540)
        self.resize(680, 760)
        self.setStyleSheet(DIALOG_STYLESHEET)

        self._setup_ui()

    # ── 레이아웃 ──

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setSpacing(0)
        layout.setContentsMargins(0, 0, 0, 0)

        layout.addWidget(self._create_header())

        self.step_bar = self._create_step_bar()
        layout.addWidget(self.step_bar)

        # 상시 배경 바(개발 건 #2) — 어느 단계서나 배경지도를 켜고 투명도를 조절한다.
        layout.addWidget(self._create_background_bar())

        # 콘텐츠 스택
        self.stack = QStackedWidget()
        self.step_pages = [
            Step1Parcel(self.iface, self.shared_data),
            Step2Boundary(self.iface, self.shared_data),
            Step3Intersect(self.iface, self.shared_data),
            Step4Export(self.iface, self.shared_data),
        ]
        for page in self.step_pages:
            self.stack.addWidget(page)

        # 각 step 시그널을 위자드가 중계
        self.step_pages[0].search_requested.connect(self.search_requested.emit)
        self.step_pages[0].parcel_added.connect(self.parcel_add_requested.emit)
        self.step_pages[0].bulk_add_requested.connect(self.bulk_parcel_add_requested.emit)
        self.step_pages[0].parcel_delete_requested.connect(self.parcel_delete_requested.emit)
        self.step_pages[0].parcel_clear_requested.connect(self.parcel_clear_requested.emit)
        self.step_pages[1].boundary_added.connect(self.boundary_add_requested.emit)
        self.step_pages[2].intersection_calculated.connect(self.intersection_requested.emit)
        self.step_pages[3].inset_export_requested.connect(self.inset_export_requested.emit)
        self.step_pages[3].report_export_requested.connect(self.report_export_requested.emit)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.NoFrame)
        scroll.setStyleSheet("background-color: #f9fafb; border: none;")
        scroll.setWidget(self.stack)
        layout.addWidget(scroll, 1)

        layout.addWidget(self._create_navigation())

        self.setLayout(layout)
        self._update_ui()

    def _create_header(self):
        header = QFrame()
        header.setFixedHeight(56)
        header.setStyleSheet("background-color:#1f2937;border:none;")
        lay = QHBoxLayout()
        lay.setContentsMargins(20, 0, 20, 0)
        title = QLabel("민원검토 도구")
        title.setStyleSheet("color:white;font-size:18px;font-weight:bold;border:none;")
        lay.addWidget(title)
        lay.addStretch()
        subtitle = QLabel("지번 → 소유정보 → 편입면적 → 삽도 → HWPX 검토서")
        subtitle.setStyleSheet("color:#9ca3af;font-size:13px;border:none;")
        lay.addWidget(subtitle)
        header.setLayout(lay)
        return header

    def _create_background_bar(self):
        """배경지도 상시 컨트롤 — 위성/수치지형/지적 체크 + 투명도 슬라이더(개발 건 #2).

        배경을 켜면 작업 위치를 눈으로 확인할 수 있어, 용지경계 좌표계를 잘못 지정하면
        엉뚱한 곳에 떨어지는 것이 바로 보인다.
        """
        bar = QFrame()
        bar.setFixedHeight(40)
        bar.setStyleSheet("background-color:#f3f4f6;border-bottom:1px solid #e5e7eb;")
        lay = QHBoxLayout()
        lay.setContentsMargins(16, 4, 16, 4)
        lay.setSpacing(10)

        lbl = QLabel("배경지도")
        lbl.setStyleSheet("color:#374151;font-size:12px;font-weight:600;border:none;")
        lay.addWidget(lbl)

        self.bg_controls = {}
        for kind, name in (("satellite", "위성"), ("topo", "수치지형"), ("cadastral", "지적")):
            cb = QCheckBox(name)
            cb.setStyleSheet("font-size:12px;color:#374151;border:none;")
            cb.setCursor(Qt.PointingHandCursor)
            cb.stateChanged.connect(lambda _st, k=kind: self._emit_background(k))
            sld = QSlider(Qt.Horizontal)
            sld.setRange(0, 100)
            sld.setValue(70)
            sld.setFixedWidth(56)
            sld.setToolTip(f"{name} 투명도")
            sld.valueChanged.connect(lambda _v, k=kind: self._emit_background(k))
            self.bg_controls[kind] = (cb, sld)
            lay.addWidget(cb)
            lay.addWidget(sld)

        lay.addStretch()
        bar.setLayout(lay)
        return bar

    def _emit_background(self, kind):
        cb, sld = self.bg_controls[kind]
        # 슬라이더만 움직였는데 체크 안 됐으면 무시(불필요한 재조회 방지).
        if not cb.isChecked():
            self.background_changed.emit(kind, False, sld.value() / 100.0)
            return
        self.background_changed.emit(kind, True, sld.value() / 100.0)

    def _create_step_bar(self):
        bar = QFrame()
        bar.setFixedHeight(48)
        bar.setStyleSheet("background-color:white;border-bottom:1px solid #e5e7eb;")
        lay = QHBoxLayout()
        lay.setContentsMargins(16, 8, 16, 8)
        lay.setSpacing(6)
        self.step_labels = []
        for i, title in enumerate(STEP_TITLES):
            lbl = QLabel(f" {i + 1}. {title} ")
            lbl.setAlignment(Qt.AlignCenter)
            lbl.setCursor(Qt.PointingHandCursor)
            lbl.mousePressEvent = lambda event, idx=i: self._on_step_clicked(idx)
            self.step_labels.append(lbl)
            lay.addWidget(lbl, 1)
        bar.setLayout(lay)
        return bar

    def _create_navigation(self):
        nav = QFrame()
        nav.setFixedHeight(64)
        nav.setStyleSheet("background-color:white;border-top:1px solid #e5e7eb;")
        lay = QHBoxLayout()
        lay.setContentsMargins(20, 12, 20, 12)

        self.btn_reset = QPushButton("초기화")
        self.btn_reset.setFixedWidth(80)
        self.btn_reset.setStyleSheet("""
            QPushButton {
                background-color:#fee2e2;border:1px solid #fca5a5;border-radius:4px;
                color:#dc2626;font-size:13px;font-weight:600;padding:8px 12px;
            }
            QPushButton:hover { background-color:#fecaca; }
        """)
        self.btn_reset.setCursor(Qt.PointingHandCursor)
        self.btn_reset.clicked.connect(self._reset_wizard)
        lay.addWidget(self.btn_reset)

        self.btn_prev = QPushButton("이전")
        self.btn_prev.setFixedWidth(100)
        self.btn_prev.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_prev.setCursor(Qt.PointingHandCursor)
        self.btn_prev.clicked.connect(self._go_prev)
        lay.addWidget(self.btn_prev)

        lay.addStretch()

        self.step_info_label = QLabel()
        self.step_info_label.setStyleSheet("color:#6b7280;font-size:13px;border:none;")
        lay.addWidget(self.step_info_label)

        lay.addStretch()

        self.btn_next = QPushButton("다음")
        self.btn_next.setFixedWidth(100)
        self.btn_next.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_next.setCursor(Qt.PointingHandCursor)
        self.btn_next.clicked.connect(self._go_next)
        lay.addWidget(self.btn_next)

        nav.setLayout(lay)
        return nav

    # ── 네비게이션 ──

    def _on_step_clicked(self, idx):
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
        # 현재 페이지의 execute_step()이 False면 진행 차단
        page = self.step_pages[self.current_step]
        if hasattr(page, "execute_step") and not page.execute_step():
            return
        self.completed_steps.add(self.current_step)
        last = len(self.step_pages) - 1
        if self.current_step < last:
            self.current_step += 1
            self._update_ui()
            nxt = self.step_pages[self.current_step]
            if hasattr(nxt, "on_enter"):
                nxt.on_enter()
        else:
            self.completed_steps.add(last)
            self._update_ui()

    def _update_ui(self):
        self.stack.setCurrentIndex(self.current_step)
        for i, lbl in enumerate(self.step_labels):
            if i in self.completed_steps and i != self.current_step:
                lbl.setStyleSheet(STEP_DONE_STYLE)
            elif i == self.current_step:
                lbl.setStyleSheet(STEP_ACTIVE_STYLE)
            else:
                lbl.setStyleSheet(STEP_INACTIVE_STYLE)
        self.btn_prev.setEnabled(self.current_step > 0)
        last = len(self.step_pages) - 1
        self.btn_next.setText("완료" if self.current_step == last else "다음")
        self.step_info_label.setText(
            f"{self.current_step + 1} / {len(self.step_pages)}  {STEP_TITLES[self.current_step]}"
        )

    def _reset_wizard(self):
        reply = QMessageBox.question(
            self, "초기화 확인",
            "모든 작업 상태와 캔버스 결과 레이어를 초기화하시겠습니까?\n"
            "(선택필지·인접필지·용지경계·편입구역 4개 레이어가 제거됩니다)",
            QMessageBox.Yes | QMessageBox.No, QMessageBox.No,
        )
        if reply != QMessageBox.Yes:
            return
        self.current_step = 0
        self.completed_steps.clear()
        # shared_data 초기화
        for k in ("jibun_text", "boundary_mode", "inset_background"):
            self.shared_data[k] = "" if k == "jibun_text" else ("layer" if k == "boundary_mode" else "satellite")
        self.shared_data["inset_extent_mode"] = "canvas"
        self.shared_data["radius_m"] = 0
        for k in ("parcels", "owners", "intersections"):
            self.shared_data[k] = []
        for k in ("parcel_layer", "neighbor_layer", "boundary_input_layer",
                  "boundary_assign_epsg", "boundary_layer", "inclusion_layer",
                  "inset_png_path", "report_path"):
            self.shared_data[k] = None
        for page in self.step_pages:
            if hasattr(page, "reset"):
                page.reset()
        self.wizard_reset.emit()
        self._update_ui()

    # ── controller가 호출하는 결과 보고 API ──

    def report_step_result(self, step_idx: int, text: str, level: str = "info"):
        """controller가 액션 완료 후 해당 step 페이지에 결과 메시지 표시."""
        if 0 <= step_idx < len(self.step_pages):
            page = self.step_pages[step_idx]
            if hasattr(page, "set_result"):
                page.set_result(text, level)
