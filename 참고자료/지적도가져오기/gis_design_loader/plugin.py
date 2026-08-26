# -*- coding: utf-8 -*-
"""
GIS Design Loader - QGIS 진입점
"""

import os.path
from qgis.PyQt.QtGui import QIcon
from qgis.PyQt.QtWidgets import QAction

from .ui.wizard_dialog import CivilPlannerWizard


def _read_plugin_name(plugin_dir: str, default: str = "GIS Design Loader") -> str:
    """metadata.txt의 `name=` 값을 읽어 반환.

    배포 변형(예: `GIS Design Loader` / `GIS Design Loader Water`)이
    같은 QGIS에 동시에 설치되어도 toolbar·menu·objectName 이 충돌하지 않도록
    플러그인 이름을 metadata 단일 소스에서 읽는다.
    """
    try:
        meta_path = os.path.join(plugin_dir, "metadata.txt")
        with open(meta_path, "r", encoding="utf-8") as f:
            for line in f:
                if line.startswith("name="):
                    return line[len("name=") :].strip() or default
    except Exception:
        pass
    return default


class CivilPlanner:
    """QGIS Plugin Implementation."""

    def __init__(self, iface):
        self.iface = iface
        self.plugin_dir = os.path.dirname(__file__)
        self.actions = []
        # 배포 변형별 unique identifier — metadata.txt::name 에서 derive
        self.plugin_name = _read_plugin_name(self.plugin_dir)
        self.menu = f"&{self.plugin_name}"
        self.toolbar = self.iface.addToolBar(self.plugin_name)
        # objectName 은 공백·특수문자 제거한 식별자 (Qt saveState 호환)
        self.toolbar.setObjectName(
            "".join(ch for ch in self.plugin_name if ch.isalnum()) or "GISDesignLoader"
        )
        self.dialog = None

    def add_action(self, icon_path, text, callback, parent=None):
        icon = QIcon(icon_path)
        action = QAction(icon, text, parent or self.iface.mainWindow())
        action.triggered.connect(callback)
        self.toolbar.addAction(action)
        self.iface.addPluginToMenu(self.menu, action)
        self.actions.append(action)
        return action

    def initGui(self):
        icon_path = os.path.join(self.plugin_dir, "icon.png")
        self.add_action(
            icon_path,
            self.plugin_name,
            self.run,
        )

    def unload(self):
        for action in self.actions:
            self.iface.removePluginMenu(self.menu, action)
            self.iface.removeToolBarIcon(action)
        if self.toolbar:
            self.toolbar.deleteLater()
            self.toolbar = None
        if self.dialog:
            self.dialog.close()
            self.dialog = None

    def run(self):
        # 다이얼로그 재사용 (작업 상태 유지)
        if self.dialog is None:
            self.dialog = CivilPlannerWizard(self.iface)
        # 창 크기 고정 후 표시 (최초 축소 현상 방지)
        self.dialog.resize(680, 958)
        self.dialog.show()
        self.dialog.raise_()
        self.dialog.activateWindow()
