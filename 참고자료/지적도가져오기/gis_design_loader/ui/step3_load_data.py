# -*- coding: utf-8 -*-
"""
Step 4: 현황 데이터 로드 및 전처리
3단계 범위 폴리곤의 extent → DB 공간 쿼리 → 클리핑 → 도형 수정
"""

import os
import time

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QCheckBox, QScrollArea, QProgressBar, QStackedWidget,
)
from qgis.PyQt.QtCore import Qt, QTimer
from qgis.core import (
    QgsProject, QgsVectorLayer, QgsRasterLayer,
    QgsVectorFileWriter, QgsCoordinateTransformContext,
)

from .styles import (
    CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE,
    STEP_ACTIVE_STYLE, STEP_INACTIVE_STYLE,
)
from ..core.layer_loader import (
    AVAILABLE_LAYERS, LAYER_GROUP_ORDER, LayerLoaderThread,
    transform_extent_to_db, boundary_geometry_to_db_wkt,
    detect_emd_codes, detect_sigungu_code, detect_region_code,
)
from ..core.preprocessor import BatchPreprocessTask
from ..core.style_manager import StyleManager
from ..core.alias_helper import apply_korean_aliases


class Step3LoadData(QWidget):
    """데이터 로드 및 전처리 페이지 (범위 기반 공간 쿼리)"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.loader_thread = None
        self.style_manager = StyleManager()
        self._loaded_raw_layers = []
        self._pending_layers = []  # (layer, name) 전처리 대기 목록
        self._batch_task = None
        self._setup_ui()

    def _setup_ui(self):
        """3단계 화면 = 하위 탭 2개(데이터 로드 / 지형 내보내기)로 분리.

        지형 내보내기를 로드 화면 맨 아래에 두었더니 스크롤해야 보여서 눈에 띄지 않았다.
        단계는 그대로 두고 **단계 안에서만** 화면을 전환한다(위자드 흐름 불변).
        """
        root = QVBoxLayout()
        root.setContentsMargins(20, 20, 20, 20)
        root.setSpacing(12)

        root.addLayout(self._create_subtab_bar())

        self.sub_stack = QStackedWidget()
        self.sub_stack.addWidget(self._create_load_page())     # 0
        self.sub_stack.addWidget(self._create_export_page())   # 1
        root.addWidget(self.sub_stack, 1)

        self.setLayout(root)

    def _create_subtab_bar(self):
        """단계 칩(1.작업환경…)과 같은 시각 언어의 하위 탭 바."""
        bar = QHBoxLayout()
        bar.setSpacing(6)
        self._subtabs = []
        for i, text in enumerate(("데이터 로드", "지형 내보내기 (CAD 연계)")):
            btn = QPushButton(text)
            btn.setCursor(Qt.PointingHandCursor)
            btn.setFixedHeight(30)
            btn.clicked.connect(lambda _checked=False, idx=i: self._switch_subtab(idx))
            bar.addWidget(btn)
            self._subtabs.append(btn)
        bar.addStretch()
        self._switch_subtab(0)
        return bar

    def _switch_subtab(self, index):
        for i, btn in enumerate(self._subtabs):
            btn.setStyleSheet(STEP_ACTIVE_STYLE if i == index else STEP_INACTIVE_STYLE)
        if hasattr(self, "sub_stack"):
            self.sub_stack.setCurrentIndex(index)

    def _create_export_page(self):
        page = QWidget()
        v = QVBoxLayout()
        v.setContentsMargins(0, 0, 0, 0)
        v.setSpacing(12)
        v.addWidget(self._create_terrain_export_card())
        v.addStretch()
        page.setLayout(v)
        return page

    def _create_load_page(self):
        page = QWidget()
        layout = QVBoxLayout()
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(12)

        # 안내
        guide = QLabel(
            "이전 단계에서 설정한 작업 범위를 기준으로\n"
            "DB에서 해당 영역의 현황 데이터를 자동으로 조회합니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 범위 정보 카드
        boundary_card = QFrame()
        boundary_card.setStyleSheet(CARD_STYLE)
        bc_layout = QVBoxLayout()
        bc_layout.setContentsMargins(16, 12, 16, 12)
        bc_layout.setSpacing(6)

        bc_title = QLabel("작업 범위")
        bc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        bc_layout.addWidget(bc_title)

        self.boundary_info = QLabel("범위가 설정되지 않았습니다.")
        self.boundary_info.setStyleSheet(
            "font-size: 13px; color: #6b7280; border: none;"
        )
        self.boundary_info.setWordWrap(True)
        bc_layout.addWidget(self.boundary_info)

        boundary_card.setLayout(bc_layout)
        layout.addWidget(boundary_card)

        # 레이어 선택 카드
        layer_card = self._create_layer_card()
        layout.addWidget(layer_card, 1)

        # 진행률
        self.progress_frame = QFrame()
        self.progress_frame.setVisible(False)
        progress_layout = QVBoxLayout()
        progress_layout.setContentsMargins(0, 0, 0, 0)
        self.progress_bar = QProgressBar()
        self.progress_bar.setFixedHeight(8)
        progress_layout.addWidget(self.progress_bar)
        self.progress_label = QLabel()
        self.progress_label.setStyleSheet("font-size: 12px; color: #6b7280;")
        progress_layout.addWidget(self.progress_label)
        self.progress_frame.setLayout(progress_layout)
        layout.addWidget(self.progress_frame)

        # 경과시간 프레임 (progress_frame 다음에)
        self.timer_frame = QFrame()
        self.timer_frame.setVisible(False)
        timer_layout = QHBoxLayout()
        timer_layout.setContentsMargins(0, 0, 0, 0)
        self.elapsed_label = QLabel("경과시간: 00:00")
        self.elapsed_label.setStyleSheet("font-size: 13px; color: #6b7280;")
        timer_layout.addWidget(self.elapsed_label)
        timer_layout.addStretch()
        self.remaining_label = QLabel("")
        self.remaining_label.setStyleSheet("font-size: 13px; color: #6b7280;")
        timer_layout.addWidget(self.remaining_label)
        self.timer_frame.setLayout(timer_layout)
        layout.addWidget(self.timer_frame)

        # 타이머
        self._start_time = 0
        self._elapsed_timer = QTimer(self)
        self._elapsed_timer.timeout.connect(self._update_elapsed)

        # 저장 위치 표시
        self.save_info = QLabel()
        self.save_info.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")
        layout.addWidget(self.save_info)

        # 로드 버튼
        self.btn_load = QPushButton("데이터 로드 및 전처리")
        self.btn_load.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_load.setCursor(Qt.PointingHandCursor)
        self.btn_load.setFixedHeight(42)
        self.btn_load.clicked.connect(self._start_loading)
        layout.addWidget(self.btn_load)

        # 상태
        self.status_label = QLabel()
        self.status_label.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")
        layout.addWidget(self.status_label)

        page.setLayout(layout)
        return page

    # ── 지형 내보내기 카드 ────────────────────────────────────────
    # ⚠ CARD_STYLE 처럼 **선택자 없는** 스타일시트를 QFrame 에 걸면 Qt 가 그것을
    #   자식 위젯에까지 적용한다 → 모든 QLabel 에 테두리가 생겨 글씨가 전부
    #   '입력칸'처럼 보인다(1.3.6 화면의 실제 원인). objectName 선택자로 범위를
    #   카드 자신에게 한정한다.
    _CARD_SCOPED = (
        "#terrainCard { background-color: white; border: 1px solid #e5e7eb;"
        " border-radius: 6px; }")
    _TXT = "border: none; background: transparent;"

    # 글자 크기·색은 **이 플러그인이 이미 쓰는 값만** 쓴다(11·12·13·14·15px /
    # #1f2937·#374151·#6b7280·#9ca3af·#059669). 화면 구성은 새로 짜되 다른
    # 플러그인처럼 보이면 안 되기 때문 — 목업의 19·17·16px 과 빨강 강조는 버린다.
    _T_CARD = "font-size: 15px; font-weight: bold; color: #1f2937; "   # 카드 제목
    _T_LEAD = "font-size: 13px; color: #6b7280; "                      # 카드 설명
    _T_NUM = "font-size: 12px; font-weight: bold; color: #9ca3af; "    # 01 / 02
    _T_SEC = "font-size: 14px; font-weight: bold; color: #1f2937; "    # 주 섹션 제목
    _T_SEC_SUB = "font-size: 13px; font-weight: bold; color: #374151; "  # 보조 섹션 제목
    _T_DESC = "font-size: 12px; color: #6b7280; "                      # 섹션 설명
    _T_NOTE = "font-size: 11px; color: #9ca3af; "                      # 주석

    def _create_terrain_export_card(self):
        """작업 범위 지형을 파일로 내보내는 카드.

        레이어 로드와 무관하게 **작업 범위만 정해져 있으면** 동작한다
        (받는 쪽은 QGIS 를 거치지 않고 파일 하나만 열면 되도록 하는 것이 목적).

        화면 구성 원칙 — 형식이 3개가 되면서 '무엇을 눌러야 하나'가 헷갈렸다.
          · 용도로 먼저 나눈다: 01 등고선(설계용·권장) / 02 DEM(광역 검토용)
          · 01 안에서 형식을 고른다: DXF(강조) / SHP
          · 부가 설명은 버튼 아래 작은 글씨로 내려 버튼 자체는 크고 단순하게
        """
        card = QFrame()
        card.setObjectName("terrainCard")
        card.setStyleSheet(self._CARD_SCOPED)
        v = QVBoxLayout()
        v.setContentsMargins(16, 12, 16, 12)   # 다른 카드와 같은 여백
        # Qt 스타일시트는 line-height 를 지원하지 않는다 → 한 라벨에 \n 을 넣으면 줄이 붙어 답답하다.
        # 라벨을 나누고 레이아웃 간격으로 숨 쉴 공간을 준다.
        v.setSpacing(8)

        title = QLabel("지형 내보내기")
        title.setStyleSheet(self._T_CARD + self._TXT)
        v.addWidget(title)

        desc = QLabel("작업 범위의 지형을 파일로 저장합니다. "
                      "받는 쪽은 QGIS 없이 파일만 열면 됩니다.")
        desc.setStyleSheet(self._T_LEAD + self._TXT)
        desc.setWordWrap(True)
        v.addWidget(desc)

        v.addWidget(self._hline())

        # ── 01 등고선 ─────────────────────────────────────────
        v.addLayout(self._section_head("01", "등고선 5m", badge="권장")["layout"])

        sub = QLabel("부지정지 · 사면 검토용 — 도면에 바로 얹어 쓰는 벡터 지형")
        sub.setStyleSheet(self._T_DESC + self._TXT)
        sub.setWordWrap(True)
        v.addWidget(sub)

        row = QHBoxLayout()
        row.setSpacing(8)
        self.btn_export_terrain = self._format_button(
            "DXF", "CAD 지표면 — AutoCAD · Civil 3D", primary=True)
        self.btn_export_terrain.clicked.connect(self._export_terrain_dxf)
        self.btn_export_shp = self._format_button(
            "SHP", "GIS용 — QGIS · ArcGIS", primary=False)
        self.btn_export_shp.clicked.connect(self._export_terrain_shp)
        row.addWidget(self.btn_export_terrain)
        row.addWidget(self.btn_export_shp)
        v.addLayout(row)

        # 주석 두 줄은 **한 덩어리**로 읽혀야 하므로 카드 기본 간격(8)보다 좁게 묶는다
        notes = QVBoxLayout()
        notes.setSpacing(3)
        notes.setContentsMargins(0, 0, 0, 0)
        for note in ("등고선은 원본 5m 간격 그대로 나갑니다. 표고는 Z 좌표에 들어갑니다.",
                     "SHP 은 표고값(elev) 속성도 함께 저장됩니다."):
            n = QLabel("·  " + note)
            n.setStyleSheet(self._T_NOTE + self._TXT)
            n.setWordWrap(True)
            notes.addWidget(n)
        v.addLayout(notes)

        v.addWidget(self._hline())

        # ── 02 DEM — 보조 수단이므로 한 단 작게, 버튼도 오른쪽에 한 개만 ──
        dem_row = QHBoxLayout()
        dem_row.setSpacing(12)
        dem_col = QVBoxLayout()
        dem_col.setSpacing(5)   # 3 줄이 붙어 답답했다 — 라벨 사이를 벌린다
        dem_col.addLayout(self._section_head(
            "02", "래스터 지형 (DEM 90m 격자)", small=True)["layout"])
        d1 = QLabel("광역 검토 · 개략 계획용 — 부지정지에는 쓸 수 없습니다")
        d1.setStyleSheet(self._T_DESC + self._TXT)
        d1.setWordWrap(True)
        dem_col.addWidget(d1)
        d2 = QLabel("격자가 배수지 · 가압장 부지보다 커서 정지계획에는 부적합합니다")
        d2.setStyleSheet(self._T_NOTE + self._TXT)
        d2.setWordWrap(True)
        dem_col.addWidget(d2)
        dem_row.addLayout(dem_col, 1)

        self.btn_export_dem = QPushButton("GeoTIFF 내보내기")
        self.btn_export_dem.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_export_dem.setCursor(Qt.PointingHandCursor)
        self.btn_export_dem.setFixedHeight(34)
        self.btn_export_dem.clicked.connect(self._export_dem_tif)
        dem_row.addWidget(self.btn_export_dem, 0, Qt.AlignVCenter)
        v.addLayout(dem_row)

        v.addWidget(self._hline())

        # ── 결과 표시 ─────────────────────────────────────────
        v.addLayout(self._create_result_row())

        card.setLayout(v)
        return card

    def _hline(self):
        """구분선 — 섹션 사이 경계를 테두리 없이 만든다."""
        line = QFrame()
        line.setFrameShape(QFrame.HLine)
        line.setFixedHeight(1)
        line.setStyleSheet("border: none; background-color: #e5e7eb;")
        return line

    def _section_head(self, number, text, badge=None, small=False):
        """'01  제목  [배지]' 머리줄.

        배지는 위자드 단계 칩(`STEP_ACTIVE_STYLE`)과 같은 시각 언어 — 이 플러그인에
        빨간색은 오류(#ef4444)를 뜻하므로 강조용으로 쓰지 않는다.
        """
        h = QHBoxLayout()
        h.setSpacing(8)
        num = QLabel(number)
        num.setStyleSheet(self._T_NUM + self._TXT)
        h.addWidget(num, 0, Qt.AlignVCenter)

        label = QLabel(text)
        label.setStyleSheet((self._T_SEC_SUB if small else self._T_SEC) + self._TXT)
        h.addWidget(label)

        if badge:
            b = QLabel(badge)
            b.setStyleSheet(
                "font-size: 11px; font-weight: bold; color: white;"
                " background-color: #1f2937; border: none; border-radius: 3px;"
                " padding: 1px 6px;")
            h.addWidget(b, 0, Qt.AlignVCenter)

        h.addStretch()
        return {"layout": h, "label": label}

    def _format_button(self, code, note, primary):
        """형식 버튼 — 형식명 + 용도 두 줄.

        `PRIMARY_BUTTON_STYLE`/`SECONDARY_BUTTON_STYLE` 과 같은 색·모서리를 쓰되,
        한 버튼에 글자 크기가 두 가지 필요해 라벨을 안에 넣고
        `WA_TransparentForMouseEvents` 로 클릭을 버튼에 그대로 통과시킨다.
        (`QPushButton` 자체 텍스트는 크기를 하나만 쓴다)
        """
        btn = QPushButton()
        btn.setCursor(Qt.PointingHandCursor)
        btn.setFixedHeight(46)
        btn.setStyleSheet(
            ("QPushButton { background-color: #1f2937; border: none;"
             " border-radius: 4px; }"
             "QPushButton:hover { background-color: #374151; }"
             "QPushButton:pressed { background-color: #111827; }"
             "QPushButton:disabled { background-color: #9ca3af; }")
            if primary else
            ("QPushButton { background-color: white; border: 1px solid #d1d5db;"
             " border-radius: 4px; }"
             "QPushButton:hover { background-color: #f9fafb; border-color: #9ca3af; }"
             "QPushButton:pressed { background-color: #f3f4f6; }"
             "QPushButton:disabled { border-color: #e5e7eb; }"))

        fg = "white" if primary else "#374151"
        dim = "#d1d5db" if primary else "#9ca3af"

        col = QVBoxLayout(btn)
        col.setContentsMargins(12, 6, 12, 6)
        col.setSpacing(1)
        t = QLabel(code)
        t.setStyleSheet("font-size: 14px; font-weight: bold; color: {c}; {t}".format(
            c=fg, t=self._TXT))
        s = QLabel(note)
        s.setStyleSheet("font-size: 11px; color: {c}; {t}".format(c=dim, t=self._TXT))
        col.addWidget(t)
        col.addWidget(s)
        for w in (t, s):
            w.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        return btn

    def _create_result_row(self):
        """내보내기 결과 — 상태 한 줄 + 경로 + '폴더 열기'."""
        row = QHBoxLayout()
        row.setSpacing(12)
        col = QVBoxLayout()
        col.setSpacing(5)

        self.terrain_status = QLabel("아직 내보낸 파일이 없습니다.")
        self.terrain_status.setStyleSheet(self._T_DESC + self._TXT)
        self.terrain_status.setWordWrap(True)
        col.addWidget(self.terrain_status)

        self.terrain_path = QLabel("")
        self.terrain_path.setStyleSheet(
            "font-family: 'Consolas', 'D2Coding', monospace; " + self._T_NOTE + self._TXT)
        self.terrain_path.setWordWrap(True)
        self.terrain_path.setVisible(False)
        col.addWidget(self.terrain_path)
        row.addLayout(col, 1)

        self.btn_open_folder = QPushButton("폴더 열기")
        self.btn_open_folder.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_open_folder.setCursor(Qt.PointingHandCursor)
        self.btn_open_folder.setFixedHeight(32)
        self.btn_open_folder.setVisible(False)
        self.btn_open_folder.clicked.connect(self._open_export_folder)
        row.addWidget(self.btn_open_folder, 0, Qt.AlignVCenter)
        return row

    # 완료/진행/실패 색은 이 파일의 `status_label` 과 같은 값을 쓴다
    # (성공 #059669·600 / 평상 #6b7280 / 실패 #ef4444).
    def _show_export_result(self, summary, path):
        """완료 표시 — 요약은 진하게, 경로는 고정폭으로 따로 둔다(경로가 길어도 요약이 밀리지 않게)."""
        self._last_export_path = path
        self.terrain_status.setText(summary)
        self.terrain_status.setStyleSheet(
            "font-size: 13px; color: #059669; font-weight: 600; " + self._TXT)
        self.terrain_path.setText(path)
        self.terrain_path.setVisible(True)
        self.btn_open_folder.setVisible(True)

    def _set_export_message(self, text, failed=False):
        """진행/실패 안내 — 경로·폴더 버튼은 감춘다."""
        self.terrain_status.setText(text)
        self.terrain_status.setStyleSheet(
            "font-size: 13px; color: {c}; {t}".format(
                c="#ef4444" if failed else "#6b7280", t=self._TXT))
        self.terrain_path.setVisible(False)
        self.btn_open_folder.setVisible(False)

    def _open_export_folder(self):
        """마지막으로 내보낸 파일이 있는 폴더 열기."""
        from qgis.PyQt.QtCore import QUrl
        from qgis.PyQt.QtGui import QDesktopServices

        path = getattr(self, "_last_export_path", "")
        folder = os.path.dirname(path) if path else ""
        if folder and os.path.isdir(folder):
            QDesktopServices.openUrl(QUrl.fromLocalFile(folder))

    def _ask_boundary_and_path(self, title, default_name, file_filter, ext):
        """작업범위 확인 → 저장 경로 묻기. (boundary, path) 또는 (None, None)."""
        from qgis.PyQt.QtWidgets import QFileDialog, QMessageBox

        boundary = self.shared_data.get("boundary_layer")
        if boundary is None or not boundary.isValid():
            QMessageBox.warning(
                self, "작업 범위 없음",
                "먼저 '범위설정' 단계에서 작업 범위를 지정해 주세요.")
            return None, None

        base_dir = self.shared_data.get("save_directory") or os.path.expanduser("~")
        path, _ = QFileDialog.getSaveFileName(
            self, title, os.path.join(base_dir, default_name), file_filter)
        if not path:
            return None, None
        if not path.lower().endswith(ext):
            path += ext
        return boundary, path

    def _run_export(self, func, boundary, path):
        """내보내기 실행 공통 — 커서·버튼 상태·오류 안내 처리. 실패 시 None."""
        from qgis.PyQt.QtWidgets import QMessageBox, QApplication
        from ..core.terrain_export import TerrainExportError

        for b in self._export_buttons():
            b.setEnabled(False)
        # DXF 는 정점을 텍스트로 풀어 쓰므로 범위가 커지면 수십 초까지 걸린다
        # (실측 5km 약 8초 / 10km 약 27초). 멈춘 것으로 오해하지 않도록 미리 알린다.
        # (SHP·GeoTIFF 는 이진 형식이라 1초 안쪽)
        self._set_export_message("내보내는 중… DXF 는 범위가 크면 수십 초 걸릴 수 있습니다.")
        QApplication.processEvents()
        QApplication.setOverrideCursor(Qt.WaitCursor)
        try:
            return func(boundary, path)
        except TerrainExportError as e:
            self._set_export_message("내보내기 실패", failed=True)
            QMessageBox.warning(self, "지형 내보내기 실패", str(e))
            return None
        except Exception as e:
            self._set_export_message("내보내기 실패", failed=True)
            QMessageBox.critical(self, "지형 내보내기 오류", "예기치 못한 오류: {}".format(e))
            return None
        finally:
            QApplication.restoreOverrideCursor()
            for b in self._export_buttons():
                b.setEnabled(True)

    def _export_buttons(self):
        """내보내기 버튼 모음 — 새 형식을 추가해도 잠금/해제를 빠뜨리지 않도록."""
        return [b for b in (getattr(self, n, None) for n in
                            ("btn_export_terrain", "btn_export_shp", "btn_export_dem"))
                if b is not None]

    def _export_terrain_shp(self):
        from qgis.PyQt.QtWidgets import QMessageBox
        from ..core.terrain_export import export_contours_shp, LAYER_MAIN, LAYER_INDEX

        boundary, path = self._ask_boundary_and_path(
            "등고선 Shapefile 저장", "등고선_작업범위.shp", "Shapefile (*.shp)", ".shp")
        if not path:
            return
        info = self._run_export(export_contours_shp, boundary, path)
        if info is None:
            return

        self._show_export_result(
            "완료 — 등고선 {f:,}개 / {s:.1f} MB".format(
                f=info["features"], s=info["size_mb"]),
            info["path"])
        QMessageBox.information(
            self, "등고선 SHP 내보내기 완료",
            "등고선 {f:,}개를 저장했습니다. ({s:.1f} MB)\n\n"
            "저장 위치\n{p}\n\n"
            "── 파일 구성 ──\n"
            "· 좌표계: {crs} (.prj 로 함께 저장됨)\n"
            "· 속성: {flds}\n"
            "   Layer — {lm}(주곡선 5m) / {li}(계곡선 25m)\n"
            "   elev  — 등고선 표고(m). 표고는 Z 좌표에도 들어 있습니다\n"
            "· 원본 정점 그대로 저장했습니다(형상 손실 없음)\n\n"
            "※ GIS 프로그램용입니다. Civil 3D 에서 지표면을 만들려면 "
            "'DXF' 를 쓰세요.".format(
                f=info["features"], s=info["size_mb"],
                p=info["path"], crs=info["crs"], flds=", ".join(info["fields"]),
                lm=LAYER_MAIN, li=LAYER_INDEX))

    def _export_dem_tif(self):
        from qgis.PyQt.QtWidgets import QMessageBox
        from ..core.terrain_export import export_dem_geotiff

        boundary, path = self._ask_boundary_and_path(
            "DEM GeoTIFF 저장", "DEM_작업범위.tif", "GeoTIFF 파일 (*.tif)", ".tif")
        if not path:
            return
        info = self._run_export(export_dem_geotiff, boundary, path)
        if info is None:
            return

        self._show_export_result(
            "완료 — DEM {w}×{h} 격자 / {s:.1f} MB".format(
                w=info["width"], h=info["height"], s=info["size_mb"]),
            info["path"])
        QMessageBox.information(
            self, "DEM 내보내기 완료",
            "DEM 을 저장했습니다. ({w}×{h} 격자, {s:.2f} MB)\n\n"
            "저장 위치\n{p}\n\n"
            "── CAD 에서 열 때 ──\n"
            "· 좌표계: {crs}  ← 도면 좌표계를 같게 설정해야 위치가 맞습니다\n"
            "· 격자 간격 {px:.0f} m\n\n"
            "⚠ 이 DEM 은 90m 격자입니다. 배수지·가압장 부지(30~80m)는 격자 한 칸보다 작아\n"
            "   지표면이 평면으로 만들어집니다. **광역 검토·개략 계획용**으로만 쓰시고,\n"
            "   부지정지·사면 검토에는 위 01 의 '등고선 DXF'를 사용하세요.".format(
                w=info["width"], h=info["height"], s=info["size_mb"],
                p=info["path"], crs=info["crs"], px=info["pixel_m"]))

    def _export_terrain_dxf(self):
        from qgis.PyQt.QtWidgets import QMessageBox
        from ..core.terrain_export import export_contours_dxf

        boundary, path = self._ask_boundary_and_path(
            "등고선 DXF 저장", "등고선_작업범위.dxf", "DXF 파일 (*.dxf)", ".dxf")
        if not path:
            return
        info = self._run_export(export_contours_dxf, boundary, path)
        if info is None:
            return

        self._show_export_result(
            "완료 — 등고선 {f:,}개 / {s:.1f} MB".format(
                f=info["features"], s=info["size_mb"]),
            info["path"])
        QMessageBox.information(
            self, "지형 내보내기 완료",
            "등고선 {f:,}개를 저장했습니다. ({s:.1f} MB)\n\n"
            "저장 위치\n{p}\n\n"
            "── CAD 에서 열 때 반드시 확인 ──\n"
            "· 좌표계: {crs}  ← 도면 좌표계를 같게 설정해야 위치가 맞습니다\n"
            "· 도면 레이어 2개\n"
            "   {lm} : 주곡선(5m 간격) → 지표면 생성용\n"
            "   {li} : 계곡선(25m) → 브레이크라인으로 추가하면 품질이 좋아집니다\n"
            "· 표고는 Z 좌표에 들어 있습니다\n"
            "{simp}\n"
            "※ 5m 등고선은 기본계획·개략 검토 수준입니다. "
            "실시설계·토공량 산정에는 측량 성과가 필요합니다.".format(
                f=info["features"], s=info["size_mb"], p=info["path"],
                crs=info["crs"], lm=info["layers"][0], li=info["layers"][1],
                simp=("· 선 형상을 {:.1f} m 허용오차로 정리했습니다\n"
                      "  (원본 수치지형도 1:5,000 의 도면 정밀도 안쪽 — 형상 손실 없이 파일이 작아집니다)\n"
                      .format(info["simplify_m"]) if info.get("simplify_m") else ""))
        )

    def _create_layer_card(self):
        card = QFrame()
        card.setStyleSheet(CARD_STYLE)
        card_layout = QVBoxLayout()
        card_layout.setContentsMargins(16, 12, 16, 12)
        card_layout.setSpacing(8)

        header = QHBoxLayout()
        title = QLabel("로드할 레이어")
        title.setStyleSheet(
            "font-size: 14px; font-weight: bold; color: #1f2937; border: none;"
        )
        header.addWidget(title)
        header.addStretch()

        btn_all = QPushButton("전체선택")
        btn_all.setStyleSheet(
            "font-size: 12px; color: #6b7280; border: none; background: transparent;"
        )
        btn_all.setCursor(Qt.PointingHandCursor)
        btn_all.clicked.connect(self._select_all)
        header.addWidget(btn_all)

        btn_none = QPushButton("전체해제")
        btn_none.setStyleSheet(
            "font-size: 12px; color: #6b7280; border: none; background: transparent;"
        )
        btn_none.setCursor(Qt.PointingHandCursor)
        btn_none.clicked.connect(self._deselect_all)
        header.addWidget(btn_none)

        card_layout.addLayout(header)

        # 안내 (이미 로드된 레이어는 자동 제외됨)
        info_label = QLabel("이미 프로젝트에 있는 레이어는 가져오지 않습니다 (다시 가져오려면 레이어 패널에서 먼저 삭제). 중복(2개 이상) 레이어가 있으면 아래 안내에 표시됩니다.")
        info_label.setStyleSheet(
            "font-size: 11px; color: #6b7280; border: none; padding: 2px 0px;"
        )
        info_label.setWordWrap(True)
        card_layout.addWidget(info_label)

        # 그룹별로 레이어 분류
        from collections import OrderedDict
        groups = OrderedDict()
        for group_name in LAYER_GROUP_ORDER:
            groups[group_name] = []
        for layer_info in AVAILABLE_LAYERS:
            g = layer_info.get("group", "기타")
            if g not in groups:
                groups[g] = []
            groups[g].append(layer_info)

        self.layer_checkboxes = []

        # 2열 배치: 좌측(행정구역+개발사업+토지_지적), 우측(현황_하천_도로_건물+배경_지형)
        left_groups = ["행정구역", "개발사업", "토지_지적"]
        right_groups = ["현황_하천_도로_건물", "배경_지형"]

        columns_layout = QHBoxLayout()
        columns_layout.setSpacing(12)

        for col_group_names in (left_groups, right_groups):
            col_widget = QWidget()
            col_layout = QVBoxLayout()
            col_layout.setContentsMargins(0, 0, 0, 0)
            col_layout.setSpacing(3)

            for group_name in col_group_names:
                layers = groups.get(group_name, [])
                if not layers:
                    continue
                grp_header = QLabel(f"▸ {group_name}")
                grp_header.setStyleSheet(
                    "font-size: 12px; font-weight: bold; color: #1f2937; "
                    "margin-top: 6px; border: none; padding-left: 2px;"
                )
                col_layout.addWidget(grp_header)
                for layer_info in layers:
                    ltype = layer_info.get("layer_type", "vector")
                    badge = "[R]" if ltype == "raster" else "[V]"
                    cb = QCheckBox(f" {badge} {layer_info['name']}")
                    cb.setChecked(True)
                    cb.layer_info = layer_info
                    self.layer_checkboxes.append(cb)
                    col_layout.addWidget(cb)

            col_widget.setLayout(col_layout)
            columns_layout.addWidget(col_widget)

        card_layout.addLayout(columns_layout, 1)

        card.setLayout(card_layout)
        return card

    def _select_all(self):
        for cb in self.layer_checkboxes:
            if cb.isEnabled():
                cb.setChecked(True)

    def _deselect_all(self):
        for cb in self.layer_checkboxes:
            cb.setChecked(False)

    def _get_loaded_layer_names(self):
        """현재 QGIS 프로젝트에 로드된 레이어 이름 집합 반환.

        로드 시 "{이름}_clip" 으로 명명되므로, 원본 이름(`base_managed_layer_name`)도 함께 포함한다.
        예: 프로젝트에 "행정경계_시군구_clip" 이 있으면 {"행정경계_시군구_clip", "행정경계_시군구"} 둘 다 반환.
        """
        try:
            from ..core.preprocessor import base_managed_layer_name
            names = set()
            for lyr in QgsProject.instance().mapLayers().values():
                n = lyr.name()
                names.add(n)
                names.add(base_managed_layer_name(n))
            return names
        except Exception:
            return set()

    def _count_managed_layers_in_project(self):
        """프로젝트에 로드된 관리 레이어를 기준 이름별로 카운트 → {기준이름: 개수}.
        2개 이상이면 '중복' 상태.
        """
        try:
            from collections import Counter
            from ..core.preprocessor import base_managed_layer_name
            managed = {l["name"] for l in AVAILABLE_LAYERS}
            counts = Counter()
            for lyr in QgsProject.instance().mapLayers().values():
                b = base_managed_layer_name(lyr.name())
                if b in managed:
                    counts[b] += 1
            return counts
        except Exception:
            return {}

    def _refresh_loaded_status(self):
        """이미 로드된 레이어 체크박스를 비활성화 + 시각적 표시 + 중복 안내
        on_enter() 호출 시 매번 갱신 — 사용자가 레이어를 수동 삭제했다면 재활성화
        """
        loaded_names = self._get_loaded_layer_names()
        counts = self._count_managed_layers_in_project()
        duplicated = {k for k, v in counts.items() if v >= 2}

        skipped_names = []
        for cb in self.layer_checkboxes:
            layer_name = cb.layer_info["name"]
            ltype = cb.layer_info.get("layer_type", "vector")
            badge = "[R]" if ltype == "raster" else "[V]"

            if layer_name in loaded_names:
                cb.setChecked(False)
                cb.setEnabled(False)
                if layer_name in duplicated:
                    cb.setText(f" {badge} {layer_name}  — 이미 있음 ⚠ 중복 {counts[layer_name]}개")
                    cb.setStyleSheet("color: #b45309; font-weight: 600;")
                    cb.setToolTip(
                        f"'{layer_name}' 레이어가 이미 프로젝트에 {counts[layer_name]}개 들어가 있습니다.\n"
                        "가져오기는 건너뜁니다. 레이어 패널에서 중복된 레이어를 정리(삭제)하세요."
                    )
                else:
                    cb.setText(f" {badge} {layer_name}  — 이미 프로젝트에 있음 (제외)")
                    cb.setStyleSheet("color: #9ca3af;")
                    cb.setToolTip(
                        "이미 QGIS 프로젝트에 있는 레이어입니다 (중복 방지로 가져오지 않습니다).\n"
                        "다시 가져오려면 레이어 패널에서 해당 레이어를 먼저 삭제하세요."
                    )
                skipped_names.append(layer_name)
            else:
                # 사용자가 수동 삭제했다면 재활성화
                cb.setEnabled(True)
                cb.setText(f" {badge} {layer_name}")
                cb.setStyleSheet("")
                cb.setToolTip("")

        # 안내 메시지 갱신
        dup_list = sorted(duplicated)
        if skipped_names:
            head = ", ".join(skipped_names[:6])
            more = f" 외 {len(skipped_names) - 6}개" if len(skipped_names) > 6 else ""
            msg = f"이미 프로젝트에 있어 가져오지 않는 레이어 {len(skipped_names)}개: {head}{more}"
            if dup_list:
                dhead = ", ".join(dup_list[:5])
                dmore = f" 외 {len(dup_list) - 5}개" if len(dup_list) > 5 else ""
                msg += f"\n⚠ 같은 레이어가 2개 이상 들어가 있음 ({len(dup_list)}종): {dhead}{dmore} — 레이어 패널에서 정리하세요."
            self.status_label.setText(msg)
            self.status_label.setStyleSheet(
                "font-size: 12px; color: #b45309; padding: 4px;" if dup_list
                else "font-size: 13px; color: #2563eb; padding: 4px;"
            )
        elif dup_list:
            dhead = ", ".join(dup_list[:5])
            dmore = f" 외 {len(dup_list) - 5}개" if len(dup_list) > 5 else ""
            self.status_label.setText(
                f"⚠ 같은 레이어가 2개 이상 들어가 있음 ({len(dup_list)}종): {dhead}{dmore} — 레이어 패널에서 정리하세요."
            )
            self.status_label.setStyleSheet("font-size: 12px; color: #b45309; padding: 4px;")
        else:
            self.status_label.setText("")
            self.status_label.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")

    def _is_boundary_valid(self):
        """boundary_layer가 유효한지 확인 (C++ 객체 삭제 대응)"""
        boundary = self.shared_data.get("boundary_layer")
        try:
            return boundary is not None and boundary.isValid()
        except RuntimeError:
            self.shared_data["boundary_layer"] = None
            return False

    def on_enter(self):
        """페이지 진입 시 범위 정보 표시"""
        boundary = self.shared_data.get("boundary_layer")
        if self._is_boundary_valid():
            ext = boundary.extent()
            self.boundary_info.setText(
                f"레이어: {boundary.name()}\n"
                f"범위: {ext.width():.0f} x {ext.height():.0f} m\n"
                f"좌표: ({ext.xMinimum():.1f}, {ext.yMinimum():.1f}) ~ "
                f"({ext.xMaximum():.1f}, {ext.yMaximum():.1f})"
            )
            self.boundary_info.setStyleSheet(
                "font-size: 13px; color: #059669; border: none; font-weight: 600;"
            )
            # 사전 선택 지역 정보 추가 표시
            region_name = self.shared_data.get("selected_region_name", "")
            if region_name:
                self.boundary_info.setText(
                    self.boundary_info.text() + f"\n사전 선택 지역: {region_name}"
                )
        else:
            self.boundary_info.setText("범위가 설정되지 않았습니다.")
            self.boundary_info.setStyleSheet(
                "font-size: 13px; color: #ef4444; border: none;"
            )

        # 저장 위치 표시
        save_mode = self.shared_data.get("save_mode", "temporary")
        save_dir = self.shared_data.get("save_directory", "")
        if save_mode == "permanent" and save_dir:
            self.save_info.setText(f"저장 위치: {save_dir}")
            self.save_info.setStyleSheet("font-size: 13px; color: #059669; padding: 4px; font-weight: 600;")
        elif save_mode == "permanent":
            self.save_info.setText("저장 위치: 영구 레이어 (저장 폴더 미선택 - 1단계에서 지정하세요)")
            self.save_info.setStyleSheet("font-size: 13px; color: #ef4444; padding: 4px; font-weight: 600;")
        else:
            self.save_info.setText("저장 위치: 임시 레이어 (메모리, 세션 종료 시 삭제)")
            self.save_info.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")

        # 이미 로드된 레이어 체크박스 비활성화 갱신
        self._refresh_loaded_status()

    def _start_loading(self):
        """범위 기반 데이터 로드 시작"""
        if not self._is_boundary_valid():
            QMessageBox.warning(self, "알림", "범위 설정 단계에서 작업 범위를 먼저 설정해주세요.")
            return
        boundary = self.shared_data["boundary_layer"]

        # 체크된 레이어 중 이미 로드된 것은 안전망으로 한번 더 제외
        loaded_names = self._get_loaded_layer_names()
        selected = [
            cb.layer_info for cb in self.layer_checkboxes
            if cb.isChecked() and cb.layer_info["name"] not in loaded_names
        ]
        if not selected:
            QMessageBox.warning(
                self, "알림",
                "로드할 레이어를 선택해주세요.\n\n"
                "(이미 QGIS 프로젝트에 있는 레이어는 자동으로 제외됩니다.\n"
                "다시 가져오려면 해당 레이어를 먼저 삭제하세요.)"
            )
            return

        # 작업 범위 폴리곤 → DB CRS(5186): WKT(서버측 정밀 필터용) + extent(폴백/래스터 클립용)
        extent_5186 = transform_extent_to_db(boundary.extent(), boundary.crs())
        self._extent_5186 = extent_5186
        range_wkt_5186 = boundary_geometry_to_db_wkt(boundary)
        self._range_wkt_5186 = range_wkt_5186
        if range_wkt_5186 is None:
            from qgis.core import QgsMessageLog, Qgis
            QgsMessageLog.logMessage(
                "작업 범위 폴리곤 WKT 계산 불가 — bbox 기반 필터 + 클라이언트 clip 으로 폴백합니다.",
                "GISDesignLoader", Qgis.Info,
            )

        # 메인 스레드에서 행정구역(읍면동) 코드 감지 — DB 함수 호출에 필요
        # (range_wkt 가 있으면 bbox 가 아니라 실제 범위 폴리곤과 겹치는 동만 → over-capture 감소)
        self.status_label.setText("행정구역 감지 중...")
        region_codes = detect_emd_codes(extent_5186, clip_geom_wkt=range_wkt_5186)

        if not region_codes:
            # 폴백 1: 시군구/시도 코드로 감지
            code = detect_sigungu_code(extent_5186) or detect_region_code(extent_5186)
            if code:
                region_codes = [code]

        if not region_codes:
            # 폴백 2: 사전 선택된 읍면동 코드 사용 (DB 감지 실패 시)
            region_codes = self.shared_data.get("selected_emd_codes", [])

        if not region_codes:
            QMessageBox.warning(
                self, "알림",
                "범위에 해당하는 행정구역을 찾을 수 없습니다.\nDB 연결을 확인해주세요.",
            )
            return

        self.boundary_info.setText(
            self.boundary_info.text() +
            f"\n감지된 읍면동: {len(region_codes)}개 ({', '.join(region_codes[:5])}{'...' if len(region_codes) > 5 else ''})"
        )

        self._region_codes = region_codes

        self.btn_load.setEnabled(False)
        self.progress_frame.setVisible(True)
        self._start_time = time.time()
        self.timer_frame.setVisible(True)
        self._elapsed_timer.start(1000)
        self._loaded_raw_layers = []
        self._pending_layers = []

        self._cleanup_thread()
        self.loader_thread = LayerLoaderThread(
            selected, region_codes, extent_5186, range_wkt_5186
        )
        self.loader_thread.progress_changed.connect(self._on_progress)
        self.loader_thread.uri_ready.connect(self._on_uri_ready)
        self.loader_thread.error_occurred.connect(self._on_error)
        self.loader_thread.all_completed.connect(self._on_all_completed)
        self.loader_thread.start()

    def _cleanup_thread(self):
        """스레드 정리"""
        if self.loader_thread and self.loader_thread.isRunning():
            self.loader_thread.cancel()
            self.loader_thread.wait(5000)

    def _on_progress(self, pct, msg):
        self.progress_bar.setValue(pct)
        self.progress_label.setText(msg)

    def _on_uri_ready(self, uri_str, name, layer_type, provider):
        """URI 수신 → 메인 스레드에서 레이어 생성 → 목록에 수집"""
        if self.loader_thread and self.loader_thread._is_cancelled:
            return

        if layer_type == "raster":
            layer = QgsRasterLayer(uri_str, name, provider)
        else:
            layer = QgsVectorLayer(uri_str, name, provider)

        if not layer.isValid():
            from qgis.core import QgsMessageLog, Qgis
            QgsMessageLog.logMessage(
                f"레이어 로드 실패: {name}", "GISDesignLoader", Qgis.Warning
            )
            self.status_label.setText(f"로드 실패: {name}")
            self.status_label.setStyleSheet("font-size: 13px; color: #ef4444; padding: 4px;")
            return

        # 전처리 대기 목록에 추가 (한글 별칭은 clip/fix 후 적용)
        self._pending_layers.append((layer, name))
        self.status_label.setText(f"로드 완료: {name} ({len(self._pending_layers)}개)")

    def _apply_style_callback(self, layer, name):
        """BatchPreprocessTask.finished()에서 호출되는 스타일 콜백"""
        self.style_manager.apply_style_to_layer(layer, name)

    def _on_error(self, msg):
        self.status_label.setText(f"오류: {msg}")
        self.status_label.setStyleSheet("font-size: 13px; color: #ef4444; padding: 4px;")

    def _update_elapsed(self):
        """경과시간 + 예상 남은시간 갱신"""
        elapsed = time.time() - self._start_time
        mins, secs = divmod(int(elapsed), 60)
        self.elapsed_label.setText(f"경과시간: {mins:02d}:{secs:02d}")

        # 진행률 기반 남은시간 추정
        progress = self.progress_bar.value()
        if progress > 5:
            remaining = elapsed * (100 - progress) / progress
            r_mins, r_secs = divmod(int(remaining), 60)
            self.remaining_label.setText(f"예상 남은시간: ~{r_mins:02d}:{r_secs:02d}")

    def _on_all_completed(self):
        """URI 준비 스레드 완료 → 수집된 레이어를 일괄 전처리 (순차, 크래시 방지)"""
        self._elapsed_timer.stop()
        self.timer_frame.setVisible(False)
        total = time.time() - self._start_time if self._start_time else 0
        mins, secs = divmod(int(total), 60)

        self.progress_frame.setVisible(False)

        if not self._pending_layers:
            self.btn_load.setEnabled(True)
            self.status_label.setText("로드된 레이어가 없습니다.")
            return

        boundary = self.shared_data.get("boundary_layer")
        try:
            boundary_valid = boundary is not None and boundary.isValid()
        except RuntimeError:
            boundary_valid = False

        if not boundary_valid:
            # 범위 없으면 원본 그대로 추가 + 그룹화
            from ..core.preprocessor import _remove_existing_layers_by_name
            for layer, name in self._pending_layers:
                _remove_existing_layers_by_name(name)  # v1.4.1 중복 방지
                QgsProject.instance().addMapLayer(layer)
                self._loaded_raw_layers.append(layer)
            self.shared_data["loaded_layers"] = self._loaded_raw_layers
            count = len(self._loaded_raw_layers)
            total = time.time() - self._start_time if self._start_time else 0
            mins, secs = divmod(int(total), 60)
            try:
                self._auto_group_and_style()
            except Exception as e:
                from qgis.core import QgsMessageLog, Qgis
                QgsMessageLog.logMessage(
                    f"자동 그룹화 오류: {str(e)}", "GISDesignLoader", Qgis.Warning
                )
            # no-boundary 경로에도 alias 재적용
            self._reapply_korean_aliases()
            self.status_label.setText(f"전체 완료: {count}개 레이어 로드·그룹화·스타일 적용 (총 {mins:02d}:{secs:02d})")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
            )
            self.iface.mapCanvas().refresh()
            self.btn_load.setEnabled(True)
            return

        # 단일 QgsTask에서 순차 전처리 (동시 실행 크래시 방지)
        self.status_label.setText(
            f"전처리 시작: {len(self._pending_layers)}개 레이어..."
        )

        from qgis.core import QgsApplication
        self._batch_task = BatchPreprocessTask(
            self._pending_layers, boundary,
            style_callback=self._apply_style_callback,
            extent_5186=getattr(self, '_extent_5186', None),
            region_codes=getattr(self, '_region_codes', []),
            range_wkt_5186=getattr(self, '_range_wkt_5186', None),
        )
        self._batch_task.taskCompleted.connect(self._on_batch_finished)
        self._batch_task.taskTerminated.connect(self._on_batch_finished)
        QgsApplication.taskManager().addTask(self._batch_task)

    def _on_batch_finished(self):
        """일괄 전처리 태스크 완료"""
        self._elapsed_timer.stop()
        self.timer_frame.setVisible(False)
        total = time.time() - self._start_time if self._start_time else 0
        mins, secs = divmod(int(total), 60)

        self.btn_load.setEnabled(True)
        if hasattr(self, '_batch_task') and self._batch_task:
            for layer, name, success in self._batch_task.results:
                self._loaded_raw_layers.append(layer)

        self.shared_data["loaded_layers"] = self._loaded_raw_layers
        count = len(self._loaded_raw_layers)

        from qgis.core import QgsMessageLog, Qgis
        QgsMessageLog.logMessage(
            f"_on_batch_finished: {count}개 레이어 수집됨", "GISDesignLoader", Qgis.Info
        )

        # DEM 임시 테이블 정리 (운영 DB 오염 방지)
        if self.loader_thread:
            self.loader_thread.cleanup_temp_tables()

        # 영구 모드 시 파일로 저장
        saved_count = 0
        save_mode = self.shared_data.get("save_mode", "temporary")
        save_dir = self.shared_data.get("save_directory", "")
        if save_mode == "permanent" and save_dir:
            save_fmt = self.shared_data.get("save_format", "GPKG")
            ext = ".gpkg" if save_fmt == "GPKG" else ".shp"
            driver = "GPKG" if save_fmt == "GPKG" else "ESRI Shapefile"
            replaced_layers = []
            for layer in self._loaded_raw_layers:
                if not isinstance(layer, QgsVectorLayer):
                    replaced_layers.append(layer)
                    continue
                layer_name = layer.name()
                save_path = os.path.join(save_dir, f"{layer_name}{ext}")
                options = QgsVectorFileWriter.SaveVectorOptions()
                options.driverName = driver
                options.fileEncoding = "UTF-8"
                try:
                    error = QgsVectorFileWriter.writeAsVectorFormatV3(
                        layer, save_path,
                        QgsCoordinateTransformContext(), options
                    )
                    if error[0] == QgsVectorFileWriter.NoError:
                        saved_count += 1
                        file_layer = QgsVectorLayer(save_path, layer_name, "ogr")
                        if file_layer.isValid():
                            # isValid 확인 후에만 원본 제거 (segfault 방지)
                            QgsProject.instance().removeMapLayer(layer.id())
                            QgsProject.instance().addMapLayer(file_layer)
                            replaced_layers.append(file_layer)
                        else:
                            replaced_layers.append(layer)
                    else:
                        replaced_layers.append(layer)
                except Exception:
                    replaced_layers.append(layer)
            self._loaded_raw_layers = replaced_layers
            self.shared_data["loaded_layers"] = replaced_layers
        # 자동 그룹화 + QML 스타일 적용
        try:
            self._auto_group_and_style()
        except Exception as e:
            from qgis.core import QgsMessageLog, Qgis
            QgsMessageLog.logMessage(
                f"자동 그룹화 오류: {str(e)}", "GISDesignLoader", Qgis.Warning
            )

        # 한글 alias 재적용 (loadNamedStyle이 QML <aliases> 블록으로 덮어쓸 수 있어
        # 반드시 _auto_group_and_style 이후에 실행)
        self._reapply_korean_aliases()

        if saved_count > 0:
            self.status_label.setText(
                f"전체 완료: {count}개 레이어 로드·그룹화 + {saved_count}개 저장됨 (총 {mins:02d}:{secs:02d})"
            )
        else:
            self.status_label.setText(f"전체 완료: {count}개 레이어 로드·그룹화·스타일 적용 (총 {mins:02d}:{secs:02d})")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )
        self.iface.mapCanvas().refresh()

    def _reapply_korean_aliases(self):
        """로드된 모든 레이어에 한글 alias 일괄 재적용

        반드시 _auto_group_and_style() 이후에 호출해야 함.
        loadNamedStyle()이 QML <aliases> 블록(24/25 QML에 존재)으로 alias를
        덮어쓸 수 있어, 스타일 적용 직후 다시 한글 alias를 보장한다.
        영구 모드(GPKG/SHP)에서는 파일 reload 시 in-memory alias가 사라지므로
        이 호출이 유일한 보존 수단이다.
        """
        for layer in self._loaded_raw_layers:
            if not isinstance(layer, QgsVectorLayer):
                continue
            name = layer.name()
            layer_info = next(
                (l for l in AVAILABLE_LAYERS if l["name"] == name), None
            )
            tbl = layer_info.get("function_name", "") if layer_info else ""
            try:
                apply_korean_aliases(layer, layer_name=name, table_name=tbl or None)
            except Exception as e:
                from qgis.core import QgsMessageLog, Qgis
                QgsMessageLog.logMessage(
                    f"alias 재적용 실패 ({name}): {e}", "GISDesignLoader", Qgis.Warning
                )

    def _auto_group_and_style(self):
        """로드 완료 후 자동 그룹화 + QML 스타일 적용"""
        from collections import OrderedDict

        root = QgsProject.instance().layerTreeRoot()
        loaded = self._loaded_raw_layers

        if not loaded:
            return

        # 그룹별 레이어명 매핑
        group_layers = OrderedDict()
        for group_name in LAYER_GROUP_ORDER:
            group_layers[group_name] = []
        for info in AVAILABLE_LAYERS:
            g = info.get("group", "기타")
            if g not in group_layers:
                group_layers[g] = []
            group_layers[g].append(info["name"])

        # bridge 비활성화 — 노드 이동 시 레이어 삭제 방지
        bridge = QgsProject.instance().layerTreeRegistryBridge()
        bridge.setEnabled(False)

        try:
            # 1) 레이어를 그룹으로 분류
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
                            if isinstance(layer, QgsRasterLayer):
                                # 래스터(DEM, XYZ 타일)는 clone+remove 시
                                # C++ 객체 파괴 위험 → addLayer로 직접 추가
                                group_node.addLayer(layer)
                                parent = tree_layer.parent()
                                if parent:
                                    parent.removeChildNode(tree_layer)
                            else:
                                clone = tree_layer.clone()
                                group_node.addChildNode(clone)
                                parent = tree_layer.parent()
                                if parent:
                                    parent.removeChildNode(tree_layer)
            # 2) 기존 VWorld 레이어가 있으면 배경_지형 그룹에 포함
            vworld_keywords = ["vworld", "브이월드", "위성", "satellite", "hybrid", "하이브리드"]
            bg_group = root.findGroup("배경_지형")
            if bg_group is None:
                bg_group = root.addGroup("배경_지형")
            for tree_node in list(root.children()):
                if hasattr(tree_node, 'layer') and tree_node.layer():
                    lyr = tree_node.layer()
                    name_lower = lyr.name().lower()
                    if any(kw in name_lower for kw in vworld_keywords):
                        if isinstance(lyr, QgsRasterLayer):
                            bg_group.addLayer(lyr)
                            root.removeChildNode(tree_node)

            # 3) DEM을 배경_지형 그룹 맨 아래로 이동
            #    순서: 등고선(위) → VWorld → DEM(맨 아래)
            for child in list(bg_group.children()):
                if hasattr(child, 'layer') and child.layer():
                    if child.layer().name() == "DEM 90m":
                        bg_group.addLayer(child.layer())
                        bg_group.removeChildNode(child)
                        break
        finally:
            bridge.setEnabled(True)

        # 4) QML 스타일 적용
        for layer in loaded:
            self.style_manager.apply_style_to_layer(layer)

    def execute_step(self):
        if not self.shared_data.get("loaded_layers"):
            QMessageBox.warning(self, "알림", "데이터를 먼저 로드해주세요.")
            return False
        return True
