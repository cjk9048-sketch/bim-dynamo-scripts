# -*- coding: utf-8 -*-
"""민원검토 도구 — QGIS Plugin entry point.

4단계 위자드(지번 → 용지경계 → 편입면적 → 산출물). 결과는 모두 QGIS 캔버스 레이어로
추가되고, 다이얼로그는 입력·액션 트리거만 담당한다 (gis_design_loader_v2 위자드 패턴).
실제 워크플로 오케스트레이션은 core.controller.ReviewController 가 담당.
"""
import os

from qgis.PyQt.QtGui import QIcon
from qgis.PyQt.QtWidgets import QAction, QMessageBox

from .core.controller import ReviewController


class LanduseReview:
    """QGIS Plugin Implementation."""

    MENU = "&민원검토 도구"

    def __init__(self, iface):
        self.iface = iface
        self.plugin_dir = os.path.dirname(__file__)
        self.actions = []
        self.toolbar = self.iface.addToolBar("민원검토 도구")
        self.toolbar.setObjectName("LanduseReview")
        self.controller = None

    def add_action(self, icon_path, text, callback, parent=None):
        icon = QIcon(icon_path) if icon_path and os.path.exists(icon_path) else QIcon()
        action = QAction(icon, text, parent)
        action.triggered.connect(callback)
        self.toolbar.addAction(action)
        self.iface.addPluginToMenu(self.MENU, action)
        self.actions.append(action)
        return action

    def initGui(self):
        icon_path = os.path.join(self.plugin_dir, "resources", "icons", "icon.png")
        self.add_action(icon_path, "민원검토서 작성", self.run, parent=self.iface.mainWindow())

    def unload(self):
        if self.controller:
            self.controller.cleanup()
            self.controller = None
        for action in self.actions:
            self.iface.removePluginMenu(self.MENU, action)
            self.iface.removeToolBarIcon(action)
        del self.toolbar

    def run(self):
        try:
            if self.controller is None:
                self.controller = ReviewController(self.iface)
            self.controller.show_dialog()
        except Exception as e:
            QMessageBox.critical(
                self.iface.mainWindow(),
                "민원검토 도구 오류",
                f"플러그인 실행 중 오류가 발생했습니다:\n{e}",
            )
