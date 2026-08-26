# -*- coding: utf-8 -*-
"""
Step 6: 조판생성 - A3 가로 레이아웃 자동 생성
QgsPrintLayout 기반 지도 + 제목 + 방위기호 + 축적바 + 범례
"""

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QLineEdit,
)
from qgis.PyQt.QtCore import Qt
from qgis.PyQt.QtGui import QFont, QColor
from qgis.core import (
    QgsProject, QgsPrintLayout, QgsLayoutItemMap,
    QgsLayoutItemLegend, QgsLayoutItemScaleBar,
    QgsLayoutSize, QgsUnitTypes,
    QgsLayoutItemLabel, QgsLayoutPoint,
    QgsLayoutItemPicture, QgsLegendStyle,
    QgsMessageLog, Qgis,
)

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE


class Step7LayoutCreator(QWidget):
    """조판생성 페이지 - A3 가로 레이아웃 자동 생성"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        # 안내
        guide = QLabel(
            "현재 지도 화면을 기반으로 A3 가로 조판을 자동 생성합니다.\n"
            "제목을 입력하고 '조판 생성' 버튼을 클릭하세요."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 설정 카드
        settings_card = QFrame()
        settings_card.setStyleSheet(CARD_STYLE)
        sc_layout = QVBoxLayout()
        sc_layout.setContentsMargins(16, 12, 16, 12)
        sc_layout.setSpacing(10)

        sc_title = QLabel("조판 설정")
        sc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        sc_layout.addWidget(sc_title)

        # 제목 입력
        title_row = QHBoxLayout()
        title_label = QLabel("도면 제목:")
        title_label.setStyleSheet("font-size: 13px; color: #374151; border: none;")
        title_label.setFixedWidth(80)
        title_row.addWidget(title_label)

        self.title_input = QLineEdit("계 획 평 면 도")
        self.title_input.setStyleSheet(
            "font-size: 13px; padding: 6px 10px; border: 1px solid #d1d5db; "
            "border-radius: 4px; background: white;"
        )
        title_row.addWidget(self.title_input)
        sc_layout.addLayout(title_row)

        # 용지 정보
        paper_label = QLabel("용지: A3 가로 (420 x 297 mm)")
        paper_label.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        sc_layout.addWidget(paper_label)

        # 구성 요소 안내
        components_label = QLabel(
            "구성 요소: 지도 + 제목 + 방위기호 + 축적바 + 범례"
        )
        components_label.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        sc_layout.addWidget(components_label)

        settings_card.setLayout(sc_layout)
        layout.addWidget(settings_card)

        layout.addStretch()

        # 조판 생성 버튼
        self.btn_create = QPushButton("조판 생성")
        self.btn_create.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_create.setCursor(Qt.PointingHandCursor)
        self.btn_create.setFixedHeight(42)
        self.btn_create.clicked.connect(self._create_layout)
        layout.addWidget(self.btn_create)

        # 상태
        self.status_label = QLabel()
        self.status_label.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")
        layout.addWidget(self.status_label)

        self.setLayout(layout)

    def _create_layout(self):
        """A3 가로 레이아웃 자동 생성"""
        title_text = self.title_input.text().strip()
        if not title_text:
            QMessageBox.warning(self, "알림", "도면 제목을 입력해주세요.")
            return

        try:
            project = QgsProject.instance()
            manager = project.layoutManager()
            layout_name = f"GIS_Design_{title_text}"

            # 기존 동일 이름 레이아웃 확인
            existing = manager.layoutByName(layout_name)
            if existing:
                reply = QMessageBox.question(
                    self, "조판 덮어쓰기",
                    f"'{layout_name}' 조판이 이미 존재합니다.\n덮어쓰시겠습니까?",
                    QMessageBox.Yes | QMessageBox.No, QMessageBox.No,
                )
                if reply != QMessageBox.Yes:
                    return
                manager.removeLayout(existing)

            # 새 레이아웃 생성 (A3 가로: 420 x 297 mm)
            print_layout = QgsPrintLayout(project)
            print_layout.initializeDefaults()
            print_layout.setName(layout_name)

            page = print_layout.pageCollection().pages()[0]
            page.setPageSize(
                QgsLayoutSize(420, 297, QgsUnitTypes.LayoutMillimeters)
            )
            manager.addLayout(print_layout)

            # 지도 항목 (여백: 좌우 15mm, 상하 10mm)
            canvas = self.iface.mapCanvas()
            map_item = QgsLayoutItemMap(print_layout)
            print_layout.addLayoutItem(map_item)
            map_item.attemptMove(
                QgsLayoutPoint(15, 10, QgsUnitTypes.LayoutMillimeters)
            )
            map_item.attemptResize(
                QgsLayoutSize(390, 277, QgsUnitTypes.LayoutMillimeters)
            )
            map_item.setExtent(canvas.extent())
            map_item.setFrameEnabled(True)

            # 도면 제목 (좌측 상단)
            title = QgsLayoutItemLabel(print_layout)
            print_layout.addLayoutItem(title)
            title.setText(title_text)
            title_font = QFont("Malgun Gothic", 24, QFont.Bold)
            title.setFont(title_font)
            title.setHAlign(Qt.AlignHCenter)
            title.setVAlign(Qt.AlignVCenter)
            title.setBackgroundEnabled(True)
            title.setBackgroundColor(QColor(245, 245, 245, 230))
            title.setFrameEnabled(True)
            title.attemptResize(
                QgsLayoutSize(80, 20, QgsUnitTypes.LayoutMillimeters)
            )
            title.attemptMove(
                QgsLayoutPoint(25, 20, QgsUnitTypes.LayoutMillimeters)
            )

            # 방위기호 (우측 상단)
            north_arrow = QgsLayoutItemPicture(print_layout)
            print_layout.addLayoutItem(north_arrow)
            north_arrow.setPicturePath(
                ":/images/north_arrows/layout_default_north_arrow.svg"
            )
            north_arrow.setLinkedMap(map_item)
            north_arrow.attemptResize(
                QgsLayoutSize(20, 20, QgsUnitTypes.LayoutMillimeters)
            )
            north_arrow.attemptMove(
                QgsLayoutPoint(375, 20, QgsUnitTypes.LayoutMillimeters)
            )

            # 축적바 (방위기호 아래)
            scalebar = QgsLayoutItemScaleBar(print_layout)
            print_layout.addLayoutItem(scalebar)
            scalebar.setStyle("Double Box")
            scalebar.setLinkedMap(map_item)
            scalebar.setUnitLabel("m")
            scalebar.setUnitsPerSegment(500)
            scalebar.setNumberOfSegments(2)
            scalebar.attemptMove(
                QgsLayoutPoint(350, 45, QgsUnitTypes.LayoutMillimeters)
            )

            # 범례 (우측 하단)
            legend = QgsLayoutItemLegend(print_layout)
            print_layout.addLayoutItem(legend)
            legend.setLinkedMap(map_item)
            legend.setBackgroundEnabled(True)
            legend.setBackgroundColor(QColor(255, 255, 255, 240))
            legend.setFrameEnabled(True)
            legend_font = QFont("Malgun Gothic", 8)
            legend.style(QgsLegendStyle.Title).setFont(
                QFont("Malgun Gothic", 9, QFont.Bold)
            )
            legend.style(QgsLegendStyle.Group).setFont(legend_font)
            legend.style(QgsLegendStyle.Subgroup).setFont(legend_font)
            legend.style(QgsLegendStyle.SymbolLabel).setFont(legend_font)

            # v1.4.6: 배경_지형 그룹(DEM/VWorld/등고선)은 범례에서 제외
            # (사용자가 범례에 표기하고 싶지 않은 바탕 레이어)
            try:
                legend.setAutoUpdateModel(False)
                root = legend.model().rootGroup()
                if root is not None:
                    bg_group = root.findGroup("배경_지형")
                    if bg_group is not None:
                        root.removeChildNode(bg_group)
            except Exception as exc:
                QgsMessageLog.logMessage(
                    f"Legend에서 배경_지형 제외 실패: {exc}",
                    "GISDesignLoader", Qgis.Warning,
                )

            legend.attemptMove(
                QgsLayoutPoint(320, 220, QgsUnitTypes.LayoutMillimeters)
            )

            # shared_data에 레이아웃 저장
            self.shared_data["layout"] = print_layout
            self.shared_data["layout_title"] = title_text

            # 조판 디자이너 열기
            self.iface.openLayoutDesigner(print_layout)

            self.status_label.setText(
                f"조판 '{layout_name}' 생성 완료! 조판 디자이너가 열렸습니다."
            )
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
            )

        except Exception as e:
            self.status_label.setText(f"조판 생성 실패: {str(e)}")
            self.status_label.setStyleSheet(
                "font-size: 13px; color: #ef4444; padding: 4px;"
            )
            QMessageBox.critical(self, "오류", f"조판 생성 중 오류가 발생했습니다:\n{str(e)}")

    def on_enter(self):
        """페이지 진입 시 상태 갱신"""
        pass

    def reset(self):
        """초기화"""
        self.title_input.setText("계 획 평 면 도")
        self.status_label.setText("")
