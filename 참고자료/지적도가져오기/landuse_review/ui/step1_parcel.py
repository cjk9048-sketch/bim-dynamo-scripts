# -*- coding: utf-8 -*-
"""Step 1: 지번 검색 → 후보 선택 → 캔버스에 필지 레이어 추가.

지번을 입력하고 [검색]을 누르면 V-World search API 로 **후보 목록**을 보여준다
('대전리 645-1' 처럼 전국에 동명 지번이 여럿이라 첫 결과가 원하는 곳이 아닐 수 있으므로).
사용자가 정확한 주소를 고른 뒤 [선택 필지 추가]로 캔버스에 올린다.

키가 없으면(API 불가) 검색 없이 입력 지번으로 바로 조회(DB 폴백)한다.

**다중 지번 검토**: 아래 [일괄 조회]로 여러 줄 지번을 한 번에 조회할 수 있다. 단일 검색·
일괄 조회 모두 기존 결과를 지우지 않고 누적 병합되며, "검토 대상 필지" 목록에서
확인·삭제할 수 있다.
"""
from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QLineEdit, QCheckBox, QSpinBox, QListWidget, QListWidgetItem, QMessageBox,
    QPlainTextEdit, QApplication,
)
from qgis.PyQt.QtCore import Qt, pyqtSignal

from ..core.parcel_lookup import parcel_key

from .styles import (
    CARD_STYLE, GUIDE_STYLE, RESULT_STYLE,
    PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE,
)


class Step1Parcel(QWidget):
    """지번 검색·후보 선택 페이지."""

    search_requested = pyqtSignal()         # [검색] — controller가 후보 조회 후 show_candidates 호출
    parcel_added = pyqtSignal()             # [선택 필지 추가] — controller가 캔버스에 누적 추가
    bulk_add_requested = pyqtSignal()       # [일괄 조회] — 여러 줄 지번을 누적 병합
    parcel_delete_requested = pyqtSignal()  # [선택 삭제] — 대상 필지 목록에서 고른 본 필지 제거
    parcel_clear_requested = pyqtSignal()   # [전체 비우기] — 누적 목록 전체 제거

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self._searched = False
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(14)

        guide = QLabel(
            "민원 지번을 입력하고 [검색]을 누르면 일치하는 주소 후보가 나옵니다.\n"
            "전국에 같은 지번(예: 대전리 645-1)이 여럿일 수 있으니 정확한 주소를 고르세요.\n"
            "고른 뒤 [선택 필지 추가]를 누르면 캔버스에 '선택필지'로 올라갑니다.\n"
            "여러 필지를 한 번에 검토하려면 아래 [일괄 조회]를 사용하세요 — 기존 결과는 지워지지 않고 누적됩니다.\n"
            "소유자 '이름'은 자동 불가 → 속성테이블 [소유자(직접입력)] 칸에 입력. 산필지는 '산45-1'."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 지번 입력 + 검색
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        cl = QVBoxLayout()
        cl.setContentsMargins(16, 14, 16, 14)
        cl.setSpacing(10)

        cl_title = QLabel("① 지번 검색")
        cl_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        cl.addWidget(cl_title)

        row = QHBoxLayout()
        self.jibun_edit = QLineEdit()
        self.jibun_edit.setPlaceholderText("예: 어상천면 대전리 645-1")
        self.jibun_edit.returnPressed.connect(self._on_search_clicked)
        row.addWidget(self.jibun_edit, 1)
        self.btn_search = QPushButton("검색")
        self.btn_search.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_search.setCursor(Qt.PointingHandCursor)
        self.btn_search.setFixedWidth(80)
        self.btn_search.clicked.connect(self._on_search_clicked)
        row.addWidget(self.btn_search)
        cl.addLayout(row)
        card.setLayout(cl)
        layout.addWidget(card)

        # ② 일괄 지번 입력 (다중 지번 검토)
        bulk_card = QFrame()
        bulk_card.setStyleSheet(CARD_STYLE)
        bl = QVBoxLayout()
        bl.setContentsMargins(16, 14, 16, 14)
        bl.setSpacing(8)

        bl_title = QLabel("② 일괄 지번 입력")
        bl_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        bl.addWidget(bl_title)

        bl_guide = QLabel("여러 지번을 줄바꿈으로 구분해 붙여넣으세요. (예: 어상천면 대전리 645-1)")
        bl_guide.setStyleSheet("font-size:12px;color:#6b7280;border:none;")
        bl_guide.setWordWrap(True)
        bl.addWidget(bl_guide)

        self.bulk_edit = QPlainTextEdit()
        self.bulk_edit.setFixedHeight(90)
        self.bulk_edit.setPlaceholderText("어상천면 대전리 645-1\n어상천면 대전리 645-6")
        bl.addWidget(self.bulk_edit)

        self.btn_bulk = QPushButton("일괄 조회")
        self.btn_bulk.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_bulk.setCursor(Qt.PointingHandCursor)
        self.btn_bulk.clicked.connect(self._on_bulk_clicked)
        bl.addWidget(self.btn_bulk)

        bulk_card.setLayout(bl)
        layout.addWidget(bulk_card)

        # ③ 후보 목록
        pick_card = QFrame()
        pick_card.setStyleSheet(CARD_STYLE)
        pl = QVBoxLayout()
        pl.setContentsMargins(16, 14, 16, 14)
        pl.setSpacing(8)
        pl_title = QLabel("③ 주소 후보 선택")
        pl_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        pl.addWidget(pl_title)

        self.cand_list = QListWidget()
        self.cand_list.setMinimumHeight(130)
        self.cand_list.setStyleSheet(
            "QListWidget{border:1px solid #d1d5db;border-radius:4px;font-size:13px;}"
            "QListWidget::item{padding:6px 8px;}"
            "QListWidget::item:selected{background:#1f2937;color:white;}"
        )
        self.cand_list.itemDoubleClicked.connect(lambda _it: self._on_add_clicked())
        pl.addWidget(self.cand_list)

        self.chk_radius = QCheckBox("인접필지 함께 표시 (GIS 보조 시각화)")
        pl.addWidget(self.chk_radius)

        radius_row = QHBoxLayout()
        radius_row.addWidget(QLabel("인접필지 반경:"))
        self.radius_spin = QSpinBox()
        self.radius_spin.setRange(50, 1000)
        self.radius_spin.setSingleStep(50)
        self.radius_spin.setValue(500)
        self.radius_spin.setSuffix(" m")
        self.radius_spin.setEnabled(False)
        self.chk_radius.toggled.connect(self.radius_spin.setEnabled)
        radius_row.addWidget(self.radius_spin)
        radius_row.addStretch()
        pl.addLayout(radius_row)
        pick_card.setLayout(pl)
        layout.addWidget(pick_card)

        # ④ 추가
        self.btn_add = QPushButton("선택 필지를 캔버스에 추가")
        self.btn_add.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_add.setCursor(Qt.PointingHandCursor)
        self.btn_add.setFixedHeight(36)
        self.btn_add.clicked.connect(self._on_add_clicked)
        layout.addWidget(self.btn_add)

        # ⑤ 검토 대상 필지 목록 (누적된 본 필지)
        list_card = QFrame()
        list_card.setStyleSheet(CARD_STYLE)
        ll = QVBoxLayout()
        ll.setContentsMargins(16, 14, 16, 14)
        ll.setSpacing(8)

        head_row = QHBoxLayout()
        self.list_title = QLabel("검토 대상 필지 (0건)")
        self.list_title.setStyleSheet("font-size:14px;font-weight:bold;color:#1f2937;border:none;")
        head_row.addWidget(self.list_title)
        head_row.addStretch()
        self.neighbor_summary_label = QLabel("")
        self.neighbor_summary_label.setStyleSheet("font-size:12px;color:#6b7280;border:none;")
        head_row.addWidget(self.neighbor_summary_label)
        ll.addLayout(head_row)

        self.parcel_list = QListWidget()
        self.parcel_list.setMinimumHeight(120)
        self.parcel_list.setSelectionMode(QListWidget.ExtendedSelection)
        self.parcel_list.setStyleSheet(
            "QListWidget{border:1px solid #d1d5db;border-radius:4px;font-size:13px;}"
            "QListWidget::item{padding:6px 8px;}"
            "QListWidget::item:selected{background:#1f2937;color:white;}"
        )
        ll.addWidget(self.parcel_list)

        list_btn_row = QHBoxLayout()
        self.btn_delete = QPushButton("선택 삭제")
        self.btn_delete.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_delete.setCursor(Qt.PointingHandCursor)
        self.btn_delete.clicked.connect(self._on_delete_clicked)
        list_btn_row.addWidget(self.btn_delete)

        self.btn_clear_all = QPushButton("전체 비우기")
        self.btn_clear_all.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_clear_all.setCursor(Qt.PointingHandCursor)
        self.btn_clear_all.clicked.connect(self._on_clear_all_clicked)
        list_btn_row.addWidget(self.btn_clear_all)
        list_btn_row.addStretch()
        ll.addLayout(list_btn_row)

        list_card.setLayout(ll)
        layout.addWidget(list_card)

        self.result_label = QLabel("지번을 검색하세요.")
        self.result_label.setStyleSheet("font-size:13px;color:#6b7280;padding:6px;border:none;")
        self.result_label.setWordWrap(True)
        layout.addWidget(self.result_label)

        layout.addStretch()
        self.setLayout(layout)

    # ── 검색 ──

    def _on_search_clicked(self):
        jibun = self.jibun_edit.text().strip()
        if not jibun:
            self.set_result("지번을 입력하세요.", "error")
            return
        self.shared_data["jibun_text"] = jibun
        self.cand_list.clear()
        self.set_result("검색 중...", "info")
        self.search_requested.emit()   # controller → search_candidates → show_candidates

    def show_candidates(self, candidates):
        """controller가 검색 결과(list)를 넘겨 후보 리스트를 채운다."""
        self._searched = True
        self.cand_list.clear()
        cands = candidates or []
        for c in cands:
            label = c.get("address") or c.get("pnu") or ""
            jb = c.get("jibun")
            item = QListWidgetItem(label if not jb else f"{label}")
            item.setData(Qt.UserRole, c)
            self.cand_list.addItem(item)
        if not cands:
            self.set_result(
                "검색 결과가 없습니다. 지번을 확인하거나(키가 없으면 검색 불가) "
                "정확한 지번으로 [선택 필지 추가]를 눌러 직접 조회하세요.", "info")
            return
        if len(cands) == 1:
            self.cand_list.setCurrentRow(0)
            self.set_result("후보 1건 — [선택 필지 추가]를 누르세요.", "info")
        else:
            self.set_result(f"후보 {len(cands)}건 — 원하는 주소를 고른 뒤 [선택 필지 추가].", "info")

    def _selected_candidate(self):
        it = self.cand_list.currentItem()
        return it.data(Qt.UserRole) if it else None

    # ── 추가 ──

    def _on_add_clicked(self):
        jibun = self.jibun_edit.text().strip()
        if not jibun:
            self.set_result("지번을 입력하세요.", "error")
            return
        self.shared_data["jibun_text"] = jibun
        self.shared_data["radius_m"] = self.radius_spin.value() if self.chk_radius.isChecked() else 0

        cand = self._selected_candidate()
        if cand is None and self.cand_list.count() > 0:
            QMessageBox.information(self, "안내", "주소 후보 목록에서 정확한 주소를 먼저 선택하세요.")
            return
        # cand 가 있으면 그 후보(PNU+대표점)로, 없으면(후보 0·키 없음) 입력 지번 직접 조회
        self.shared_data["selected_candidate"] = cand
        self.parcel_added.emit()

    # ── 일괄 조회 ──

    def _on_bulk_clicked(self):
        text = self.bulk_edit.toPlainText().strip()
        if not text:
            self.set_result("지번을 입력하세요.", "error")
            return
        self.shared_data["bulk_jibun_text"] = text
        self.shared_data["radius_m"] = self.radius_spin.value() if self.chk_radius.isChecked() else 0
        QApplication.setOverrideCursor(Qt.WaitCursor)
        try:
            self.bulk_add_requested.emit()
        finally:
            QApplication.restoreOverrideCursor()

    # ── 검토 대상 필지 목록 ──

    def refresh_parcel_list(self, parcels):
        """controller가 누적 상태(shared_data['parcels'])가 바뀔 때마다 호출해 목록을 다시 채운다.

        본 필지 + 미발견 행을 개별 행으로 표시하고, 인접필지는 요약 라벨로만 안내한다.
        미발견 행을 숨기면 사용자가 지울 수 없는 채로 한글 보고서에 빈 행으로 실린다.
        """
        self.parcel_list.clear()
        rows = [p for p in (parcels or []) if not p.get("is_neighbor")]
        n_neighbor = sum(1 for p in (parcels or []) if p.get("is_neighbor"))
        n_found = n_miss = 0
        for p in rows:
            if p.get("miss"):
                n_miss += 1
                label = f"⚠ {p.get('jibun', '')} — 찾지 못함 (지번 확인 후 삭제하세요)"
            else:
                n_found += 1
                area = p.get("area_sqm") or 0
                label = f"{p.get('jibun', '')} · {p.get('jimok', '') or '-'} · {area:,.1f}㎡"
            item = QListWidgetItem(label)
            item.setData(Qt.UserRole, parcel_key(p))   # controller 병합 키와 동일 정의
            self.parcel_list.addItem(item)
        self.list_title.setText(f"검토 대상 필지 ({n_found}건)")
        summary = []
        if n_neighbor:
            summary.append(f"인접 {n_neighbor}건")
        if n_miss:
            summary.append(f"⚠ 미발견 {n_miss}건")
        self.neighbor_summary_label.setText(" · ".join(summary))

    def _on_delete_clicked(self):
        items = self.parcel_list.selectedItems()
        if not items:
            QMessageBox.information(self, "안내", "삭제할 필지를 목록에서 선택하세요.")
            return
        self.shared_data["delete_keys"] = [it.data(Qt.UserRole) for it in items]
        self.parcel_delete_requested.emit()

    def _on_clear_all_clicked(self):
        if self.parcel_list.count() == 0:
            return
        reply = QMessageBox.question(
            self, "전체 비우기 확인",
            "검토 대상 필지를 모두 비우시겠습니까? (누적된 조회 결과가 모두 사라집니다)",
            QMessageBox.Yes | QMessageBox.No, QMessageBox.No,
        )
        if reply != QMessageBox.Yes:
            return
        self.parcel_clear_requested.emit()

    def set_result(self, text, level="info"):
        if level == "error":
            self.result_label.setText("⚠ " + text)
            self.result_label.setStyleSheet("font-size:13px;color:#dc2626;font-weight:600;padding:6px;border:none;")
        elif level == "success":
            self.result_label.setText("✓ " + text)
            self.result_label.setStyleSheet(RESULT_STYLE)
        else:
            self.result_label.setText(text)
            self.result_label.setStyleSheet("font-size:13px;color:#6b7280;padding:6px;border:none;")

    def reset(self):
        self.jibun_edit.clear()
        self.bulk_edit.clear()
        self.cand_list.clear()
        self.parcel_list.clear()
        self.chk_radius.setChecked(False)
        self.radius_spin.setValue(500)
        self.radius_spin.setEnabled(False)
        self._searched = False
        self.shared_data["selected_candidate"] = None
        self.shared_data["bulk_jibun_text"] = ""
        # 목록 위젯만 비우면 누적 필지가 살아남아 '0건' 표시인데 다음 단계로 넘어간다.
        self.shared_data["parcels"] = []
        self.list_title.setText("검토 대상 필지 (0건)")
        self.neighbor_summary_label.setText("")
        self.set_result("지번을 검색하세요.", "info")

    def on_enter(self):
        pass

    def execute_step(self):
        mains = [p for p in self.shared_data.get("parcels", [])
                 if not p.get("is_neighbor") and not p.get("miss")]
        if not mains:
            QMessageBox.information(
                self, "안내",
                "지번을 검색하거나 [일괄 조회]로 최소 1건의 필지를 추가하세요."
            )
            return False
        return True
