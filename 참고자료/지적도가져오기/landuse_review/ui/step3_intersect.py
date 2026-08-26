# -*- coding: utf-8 -*-
"""Step 3: 편입면적 산출.

지적경계(선택필지) ∩ 용지경계 교차를 **QGIS(내 컴퓨터)에서** 계산한다. 1단계에서 이미
가져온 필지 도형을 그대로 쓰므로 DB 를 다시 치지 않는다(`core/area_calculator.py`).
결과는 캔버스 "편입구역" 레이어에 추가 (속성에 편입면적·편입률·잔여면적).
사용자는 QGIS 속성 테이블·식별 도구·라벨로 확인 — 다이얼로그에 표 없음.
"""
from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox,
)
from qgis.PyQt.QtCore import Qt, pyqtSignal

from .styles import (
    CARD_STYLE, GUIDE_STYLE, RESULT_STYLE,
    PRIMARY_BUTTON_STYLE,
)


class Step3Intersect(QWidget):
    intersection_calculated = pyqtSignal()  # controller가 계산 + 캔버스 추가 후 emit

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
            "선택필지 ∩ 용지경계 교차를 QGIS 에서 산출합니다(1단계에서 가져온 도형을 사용).\n"
            "결과는 캔버스 '편입구역' 레이어로 추가됩니다.\n"
            "속성: 지번 / 지목 / 공부면적(㎡) / 편입면적(㎡) / 편입률(%) / 잔여면적(㎡)\n"
            "공부면적은 토지대장 면적이며, 지적도를 실측한 넓이와는 조금 다릅니다.\n"
            "QGIS 속성 테이블·식별 도구·라벨로 자유롭게 확인하세요."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 입력 상태 카드 (1단계·2단계 결과 요약)
        status_card = QFrame()
        status_card.setStyleSheet(CARD_STYLE)
        sl = QVBoxLayout()
        sl.setContentsMargins(16, 14, 16, 14)
        sl.setSpacing(8)
        sl_title = QLabel("입력 데이터 상태")
        sl_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        sl.addWidget(sl_title)
        self.status_parcel = QLabel("선택필지: 아직 추가되지 않음")
        self.status_parcel.setStyleSheet("font-size:13px;color:#6b7280;border:none;")
        sl.addWidget(self.status_parcel)
        self.status_boundary = QLabel("용지경계: 아직 추가되지 않음")
        self.status_boundary.setStyleSheet("font-size:13px;color:#6b7280;border:none;")
        sl.addWidget(self.status_boundary)
        status_card.setLayout(sl)
        layout.addWidget(status_card)

        # 액션 버튼
        self.btn_calc = QPushButton("편입면적 산출 → '편입구역' 레이어 추가")
        self.btn_calc.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_calc.setCursor(Qt.PointingHandCursor)
        self.btn_calc.setFixedHeight(36)
        self.btn_calc.clicked.connect(self._on_calc_clicked)
        layout.addWidget(self.btn_calc)

        self.result_label = QLabel("산출 전입니다.")
        self.result_label.setStyleSheet("font-size:13px;color:#6b7280;padding:8px;border:none;")
        self.result_label.setWordWrap(True)
        layout.addWidget(self.result_label)

        layout.addStretch()
        self.setLayout(layout)

    def on_enter(self):
        """페이지 진입 시 입력 상태 갱신."""
        parcels = self.shared_data.get("parcels", [])
        if parcels:
            n_main = sum(1 for p in parcels if not p.get("is_neighbor"))
            self.status_parcel.setText(f"선택필지: {n_main}건 추가됨")
            self.status_parcel.setStyleSheet("font-size:13px;color:#059669;font-weight:600;border:none;")
        else:
            self.status_parcel.setText("선택필지: 아직 추가되지 않음")
            self.status_parcel.setStyleSheet("font-size:13px;color:#6b7280;border:none;")
        bnd = self.shared_data.get("boundary_layer")
        if bnd is not None:
            try:
                cnt = bnd.featureCount()
            except Exception:
                cnt = "?"
            self.status_boundary.setText(f"용지경계: 추가됨 ({cnt}개 폴리곤)")
            self.status_boundary.setStyleSheet("font-size:13px;color:#059669;font-weight:600;border:none;")
        else:
            self.status_boundary.setText("용지경계: 아직 추가되지 않음")
            self.status_boundary.setStyleSheet("font-size:13px;color:#6b7280;border:none;")

    def _on_calc_clicked(self):
        if not self.shared_data.get("parcels"):
            self.set_result("먼저 1단계에서 [선택필지]를 추가하세요.", "error")
            return
        if not self.shared_data.get("boundary_layer"):
            self.set_result("먼저 2단계에서 [용지경계]를 추가하세요.", "error")
            return
        self.intersection_calculated.emit()

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
        self.set_result("산출 전입니다.", "info")

    def execute_step(self):
        # bool(intersections) 로 판정하면 coord_mismatch(좌표계 오지정)여도 통과한다 —
        # 그러면 편입 0이 정상(no_inclusion)처럼 검토서에 실린다(F3). 필지·경계를 바꾼 뒤
        # 재산출하지 않으면 status 가 무효(None)라 stale 편입값을 막는다(F4). 산출 '성격'으로 막는다.
        status = self.shared_data.get("intersect_status")
        if status not in ("ok", "no_inclusion"):
            if status == "coord_mismatch":
                msg = ("필지와 용지경계가 전혀 겹치지 않습니다(좌표계 오지정 의심).\n"
                       "좌표계를 확인하고 편입면적 산출을 다시 실행하세요.")
            else:
                msg = "편입면적 산출을 먼저 실행하세요."
            QMessageBox.information(self, "안내", msg)
            return False
        return True
