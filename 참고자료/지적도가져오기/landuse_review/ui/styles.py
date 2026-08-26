# -*- coding: utf-8 -*-
"""민원검토 도구 UI 스타일 — gis_design_loader_v2와 동일한 디자인 시스템.

다른 사내 8종 플러그인과 시각적 일관성 유지 (Pretendard 폰트, gray-scale 팔레트,
darkest header, 카드형 콘텐츠, 단계 표시 바)."""

DIALOG_STYLESHEET = """
* {
    font-family: 'Pretendard', 'Pretendard Variable', 'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif;
    font-weight: 500;
}
QDialog { background-color: #f9fafb; }
QLabel { color: #374151; font-weight: 500; }
QComboBox {
    border: 1px solid #d1d5db;
    border-radius: 4px;
    padding: 8px 12px;
    background-color: #f9fafb;
    font-size: 14px;
    color: #374151;
    combobox-popup: 0;
}
QComboBox:hover { border-color: #9ca3af; background-color: white; }
QComboBox::drop-down {
    subcontrol-origin: padding;
    subcontrol-position: center right;
    width: 28px;
    border-left: 1px solid #e5e7eb;
    background-color: #f9fafb;
}
QComboBox QAbstractItemView {
    border: 1px solid #d1d5db;
    background-color: white;
    selection-background-color: #e5e7eb;
}
QLineEdit {
    border: 1px solid #d1d5db;
    border-radius: 4px;
    padding: 8px 12px;
    background-color: #f9fafb;
    font-size: 14px;
    color: #374151;
}
QLineEdit:hover { border-color: #9ca3af; }
QLineEdit:focus { border-color: #6b7280; background-color: white; }
QScrollArea { border: none; background-color: transparent; }
QScrollBar:vertical { background-color: #f3f4f6; width: 10px; border-radius: 5px; margin: 2px; }
QScrollBar::handle:vertical { background-color: #9ca3af; border-radius: 4px; min-height: 30px; }
QScrollBar::handle:vertical:hover { background-color: #6b7280; }
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0px; }
QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical { background: none; }
QCheckBox { spacing: 6px; font-size: 13px; color: #374151; }
QCheckBox::indicator {
    width: 15px; height: 15px;
    border: 2px solid #d1d5db; border-radius: 3px; background-color: white;
}
QCheckBox::indicator:checked { background-color: #1f2937; border-color: #1f2937; }
QCheckBox::indicator:hover { border-color: #9ca3af; }
QRadioButton { spacing: 6px; font-size: 13px; color: #374151; }
QGroupBox {
    font-size: 14px; font-weight: 600; color: #374151;
    border: 1px solid #e5e7eb; border-radius: 6px;
    margin-top: 12px; padding-top: 20px;
}
QGroupBox::title { subcontrol-origin: margin; left: 12px; padding: 0 6px; }
"""

# 카드 (흰 배경 + 라운드)
CARD_STYLE = """
    background-color: white;
    border: 1px solid #e5e7eb;
    border-radius: 6px;
"""

# 안내문 (연한 파랑)
GUIDE_STYLE = """
    font-size: 13px;
    color: #374151;
    background-color: #eff6ff;
    border: 1px solid #bfdbfe;
    border-radius: 6px;
    padding: 10px 12px;
"""

# 결과/요약 (연한 초록)
RESULT_STYLE = """
    font-size: 13px;
    color: #065f46;
    background-color: #d1fae5;
    border: 1px solid #6ee7b7;
    border-radius: 6px;
    padding: 10px 12px;
"""

# 경고 (연한 노랑)
WARN_STYLE = """
    font-size: 12px;
    color: #92400e;
    background-color: #fef3c7;
    border: 1px solid #fde68a;
    border-radius: 6px;
    padding: 8px 10px;
"""

PRIMARY_BUTTON_STYLE = """
QPushButton {
    background-color: #1f2937;
    border: none; border-radius: 4px;
    color: white;
    font-size: 14px; font-weight: bold;
    padding: 10px 20px;
}
QPushButton:hover { background-color: #374151; }
QPushButton:pressed { background-color: #111827; }
QPushButton:disabled { background-color: #9ca3af; color: #d1d5db; }
"""

SECONDARY_BUTTON_STYLE = """
QPushButton {
    background-color: white;
    border: 1px solid #d1d5db; border-radius: 4px;
    color: #374151;
    font-size: 14px; font-weight: 500;
    padding: 10px 20px;
}
QPushButton:hover { background-color: #f9fafb; border-color: #9ca3af; }
QPushButton:pressed { background-color: #f3f4f6; }
QPushButton:disabled { color: #9ca3af; border-color: #e5e7eb; }
"""

STEP_ACTIVE_STYLE = """
    background-color: #1f2937;
    color: white;
    font-size: 13px; font-weight: bold;
    border-radius: 10px;
    padding: 4px 10px;
    border: none;
"""

STEP_INACTIVE_STYLE = """
    background-color: #e5e7eb;
    color: #6b7280;
    font-size: 13px; font-weight: 500;
    border-radius: 10px;
    padding: 4px 10px;
    border: none;
"""

STEP_DONE_STYLE = """
    background-color: #d1fae5;
    color: #065f46;
    font-size: 13px; font-weight: bold;
    border-radius: 10px;
    padding: 4px 10px;
    border: none;
"""
