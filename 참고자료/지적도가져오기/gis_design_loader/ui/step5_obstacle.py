# -*- coding: utf-8 -*-
"""
Step 5: 로컬 데이터 로드
QGIS 탐색기에서 드래그앤드롭 또는 파일 선택으로 로컬 데이터 로드.
인코딩 선택 → 클리핑 → 저장.
"""

import os

from qgis.PyQt.QtWidgets import (
    QWidget, QVBoxLayout, QHBoxLayout, QLabel, QPushButton,
    QFrame, QMessageBox, QFileDialog, QListWidget, QListWidgetItem,
    QComboBox, QGroupBox, QAbstractItemView,
)
from qgis.PyQt.QtCore import Qt, QUrl
from qgis.core import (
    QgsProject, QgsVectorLayer, QgsVectorFileWriter,
    QgsCoordinateTransformContext,
)

from .styles import CARD_STYLE, GUIDE_STYLE, PRIMARY_BUTTON_STYLE, SECONDARY_BUTTON_STYLE
from ..core.preprocessor import Preprocessor
from ..core.style_manager import StyleManager


class DropListWidget(QListWidget):
    """드래그앤드롭을 지원하는 파일 목록 위젯 (폴더 구조 보존)"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setAcceptDrops(True)
        self.setDragDropMode(QAbstractItemView.DropOnly)
        self.setStyleSheet(
            "border: 2px dashed #d1d5db; border-radius: 8px; "
            "background-color: #f9fafb; font-size: 13px; "
            "min-height: 50px; max-height: 80px; padding: 8px;"
        )
        self._file_paths = []        # 개별 파일
        self._folder_map = {}        # {folder_name: [shp_paths]} — 서브그룹용

    def dragEnterEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()
        else:
            event.ignore()

    def dragMoveEvent(self, event):
        if event.mimeData().hasUrls():
            event.acceptProposedAction()

    def dropEvent(self, event):
        supported = ('.shp', '.gpkg', '.geojson', '.json', '.csv', '.gml', '.kml')
        for url in event.mimeData().urls():
            filepath = url.toLocalFile()
            if filepath and os.path.isfile(filepath):
                ext = os.path.splitext(filepath)[1].lower()
                if ext in supported:
                    if filepath not in self._file_paths:
                        self._file_paths.append(filepath)
                        self.addItem(os.path.basename(filepath))
            elif filepath and os.path.isdir(filepath):
                # 폴더 드롭 시 하위 디렉토리별 SHP 자동 탐색 (그룹 보존)
                self._scan_folder(filepath)
        event.acceptProposedAction()

    def _scan_folder(self, folder):
        """폴더 내 SHP 파일 탐색 — 하위 디렉토리별 그룹 보존"""
        found = False
        for entry in sorted(os.listdir(folder)):
            subdir = os.path.join(folder, entry)
            if not os.path.isdir(subdir):
                continue
            shp_files = sorted([
                os.path.join(subdir, f)
                for f in os.listdir(subdir)
                if f.lower().endswith('.shp')
            ])
            if not shp_files:
                continue
            folder_name = entry
            if folder_name not in self._folder_map:
                self._folder_map[folder_name] = []
            for shp in shp_files:
                if shp not in self._folder_map[folder_name]:
                    self._folder_map[folder_name].append(shp)
            self.addItem(f"\U0001f4c1 {folder_name}/ ({len(shp_files)}개 SHP)")
            found = True

        if not found:
            # 하위 디렉토리 없으면 루트의 SHP 직접 추가
            for f in sorted(os.listdir(folder)):
                if f.lower().endswith('.shp'):
                    full = os.path.join(folder, f)
                    if full not in self._file_paths:
                        self._file_paths.append(full)
                        self.addItem(os.path.basename(full))

    def get_file_paths(self):
        return list(self._file_paths)

    def get_folder_map(self):
        return dict(self._folder_map)

    def clear_all(self):
        self._file_paths.clear()
        self._folder_map.clear()
        self.clear()


class Step5LocalData(QWidget):
    """로컬 데이터 로드 페이지 (드래그앤드롭 + 인코딩 + 클리핑)"""

    def __init__(self, iface, shared_data, parent=None):
        super().__init__(parent)
        self.iface = iface
        self.shared_data = shared_data
        self.style_manager = StyleManager()
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout()
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        # 안내
        guide = QLabel(
            "로컬 데이터(Shapefile, GeoPackage 등)를 불러옵니다.\n"
            "아래 영역에 파일을 드래그앤드롭하거나 버튼으로 추가하세요.\n"
            "작업 범위에 맞게 자동으로 클리핑 및 도형 수정을 수행합니다."
        )
        guide.setStyleSheet(GUIDE_STYLE)
        guide.setWordWrap(True)
        layout.addWidget(guide)

        # 데이터 추가 카드
        data_card = QFrame()
        data_card.setStyleSheet(CARD_STYLE)
        dc_layout = QVBoxLayout()
        dc_layout.setContentsMargins(16, 12, 16, 12)
        dc_layout.setSpacing(8)

        dc_title = QLabel("데이터 추가")
        dc_title.setStyleSheet(
            "font-size: 15px; font-weight: bold; color: #1f2937; border: none;"
        )
        dc_layout.addWidget(dc_title)

        dc_hint = QLabel("파일을 여기에 드래그앤드롭 하세요")
        dc_hint.setStyleSheet("font-size: 12px; color: #9ca3af; border: none;")
        dc_hint.setAlignment(Qt.AlignCenter)
        dc_layout.addWidget(dc_hint)

        # 드래그앤드롭 리스트
        self.drop_list = DropListWidget()
        dc_layout.addWidget(self.drop_list)

        # 버튼 행
        btn_row = QHBoxLayout()
        btn_add = QPushButton("파일 추가...")
        btn_add.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_add.setCursor(Qt.PointingHandCursor)
        btn_add.clicked.connect(self._add_files)
        btn_row.addWidget(btn_add)

        btn_folder = QPushButton("폴더 추가...")
        btn_folder.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_folder.setCursor(Qt.PointingHandCursor)
        btn_folder.clicked.connect(self._add_folder)
        btn_row.addWidget(btn_folder)

        btn_clear = QPushButton("목록 초기화")
        btn_clear.setStyleSheet(SECONDARY_BUTTON_STYLE)
        btn_clear.setCursor(Qt.PointingHandCursor)
        btn_clear.clicked.connect(self._clear_files)
        btn_row.addWidget(btn_clear)

        btn_row.addStretch()
        dc_layout.addLayout(btn_row)

        data_card.setLayout(dc_layout)
        layout.addWidget(data_card)

        # 인코딩 설정 카드
        enc_card = QFrame()
        enc_card.setStyleSheet(CARD_STYLE)
        enc_layout = QHBoxLayout()
        enc_layout.setContentsMargins(16, 12, 16, 12)

        enc_label = QLabel("인코딩:")
        enc_label.setStyleSheet("font-size: 13px; color: #374151; border: none;")
        enc_layout.addWidget(enc_label)

        self.encoding_combo = QComboBox()
        self.encoding_combo.addItems(["자동 감지", "UTF-8", "EUC-KR (CP949)", "CP949"])
        self.encoding_combo.setCurrentIndex(0)
        enc_layout.addWidget(self.encoding_combo)

        enc_hint = QLabel("※ 한글 깨짐 시 EUC-KR 선택")
        enc_hint.setStyleSheet("font-size: 12px; color: #9ca3af; border: none;")
        enc_layout.addWidget(enc_hint)
        enc_layout.addStretch()

        enc_card.setLayout(enc_layout)
        layout.addWidget(enc_card)

        # 저장 위치 카드
        save_card = QFrame()
        save_card.setStyleSheet(CARD_STYLE)
        sv_layout = QHBoxLayout()
        sv_layout.setContentsMargins(16, 12, 16, 12)

        sv_label = QLabel("저장 위치:")
        sv_label.setStyleSheet("font-size: 13px; color: #374151; border: none;")
        sv_layout.addWidget(sv_label)

        self.save_path_label = QLabel("임시 레이어 (메모리)")
        self.save_path_label.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        sv_layout.addWidget(self.save_path_label)
        sv_layout.addStretch()

        self.btn_change_path = QPushButton("변경")
        self.btn_change_path.setStyleSheet(SECONDARY_BUTTON_STYLE)
        self.btn_change_path.setCursor(Qt.PointingHandCursor)
        self.btn_change_path.clicked.connect(self._change_save_path)
        sv_layout.addWidget(self.btn_change_path)

        save_card.setLayout(sv_layout)
        layout.addWidget(save_card)

        # 로드 버튼
        self.btn_load = QPushButton("로컬 데이터 로드 및 전처리")
        self.btn_load.setStyleSheet(PRIMARY_BUTTON_STYLE)
        self.btn_load.setCursor(Qt.PointingHandCursor)
        self.btn_load.setFixedHeight(42)
        self.btn_load.clicked.connect(self._load_local_data)
        layout.addWidget(self.btn_load)

        # 스타일 매칭 안내
        hint_card = QFrame()
        hint_card.setStyleSheet(CARD_STYLE)
        hint_layout = QVBoxLayout()
        hint_layout.setContentsMargins(16, 12, 16, 12)

        hint_title = QLabel("스타일 자동 매칭 안내")
        hint_title.setStyleSheet(
            "font-size: 13px; font-weight: bold; color: #1f2937; border: none;"
        )
        hint_layout.addWidget(hint_title)

        hint_text = QLabel(
            "파일명에 다음 키워드가 포함되면 스타일이 자동 적용됩니다:\n"
            "가스, 고압전기, 광역상수, 난방, 전기_저압, 지방상수, 통신, 하수"
        )
        hint_text.setStyleSheet("font-size: 13px; color: #6b7280; border: none;")
        hint_text.setWordWrap(True)
        hint_layout.addWidget(hint_text)

        hint_card.setLayout(hint_layout)
        layout.addWidget(hint_card)

        # 상태
        self.status_label = QLabel()
        self.status_label.setStyleSheet("font-size: 13px; color: #6b7280; padding: 4px;")
        layout.addWidget(self.status_label)

        layout.addStretch()
        self.setLayout(layout)

    def on_enter(self):
        """페이지 진입 시 저장 위치 갱신"""
        save_mode = self.shared_data.get("save_mode", "temporary")
        save_dir = self.shared_data.get("save_directory", "")
        if save_mode == "permanent" and save_dir:
            self.save_path_label.setText(save_dir)
            self.save_path_label.setStyleSheet(
                "font-size: 13px; color: #059669; border: none; font-weight: 600;"
            )
        else:
            self.save_path_label.setText("임시 레이어 (메모리)")
            self.save_path_label.setStyleSheet(
                "font-size: 13px; color: #6b7280; border: none;"
            )

    def _add_files(self):
        files, _ = QFileDialog.getOpenFileNames(
            self, "로컬 데이터 파일 선택", "",
            "공간 데이터 (*.shp *.gpkg *.geojson *.gml *.kml);;All Files (*)",
        )
        for f in files:
            if f not in self.drop_list._file_paths:
                self.drop_list._file_paths.append(f)
                self.drop_list.addItem(os.path.basename(f))

    def _add_folder(self):
        folder = QFileDialog.getExistingDirectory(self, "로컬 데이터 폴더 선택", "")
        if folder:
            self.drop_list._scan_folder(folder)

    def _clear_files(self):
        self.drop_list.clear_all()

    def _change_save_path(self):
        folder = QFileDialog.getExistingDirectory(self, "저장 위치 선택", "")
        if folder:
            self.shared_data["save_mode"] = "permanent"
            self.shared_data["save_directory"] = folder
            self.save_path_label.setText(folder)
            self.save_path_label.setStyleSheet(
                "font-size: 13px; color: #059669; border: none; font-weight: 600;"
            )

    def _get_encoding(self):
        """선택된 인코딩 반환 (자동 감지면 None)"""
        text = self.encoding_combo.currentText()
        if text == "자동 감지":
            return None
        elif text == "EUC-KR (CP949)":
            return "CP949"
        return text

    def _load_local_data(self):
        """로컬 데이터 로드 + 인코딩 적용 + 클리핑 + 폴더별 자동 그룹화"""
        file_paths = self.drop_list.get_file_paths()
        folder_map = self.drop_list.get_folder_map()
        if not file_paths and not folder_map:
            QMessageBox.warning(self, "알림", "로드할 파일을 추가해주세요.")
            return

        boundary = self.shared_data.get("boundary_layer")
        if boundary is None:
            QMessageBox.warning(self, "알림", "작업 범위가 설정되지 않았습니다.")
            return

        encoding = self._get_encoding()
        save_mode = self.shared_data.get("save_mode", "temporary")
        save_dir = self.shared_data.get("save_directory", "")

        # 로컬 데이터 루트 그룹 생성 (작업범위 바로 아래)
        root = QgsProject.instance().layerTreeRoot()
        local_group = root.findGroup("로컬 데이터")
        if local_group is None:
            # 작업범위 레이어가 최상단에 있으면 그 아래(위치 1)에 삽입
            boundary = self.shared_data.get("boundary_layer")
            insert_pos = 0
            try:
                if boundary is not None and boundary.isValid():
                    tree_layer = root.findLayer(boundary.id())
                    if tree_layer and tree_layer.parent() == root:
                        insert_pos = 1
            except RuntimeError:
                pass
            local_group = root.insertGroup(insert_pos, "로컬 데이터")

        loaded = []

        # 1) 폴더 기반 로드 (서브그룹별 자동 분류)
        for folder_name, shp_paths in sorted(folder_map.items()):
            sub_group = local_group.findGroup(folder_name)
            if sub_group is None:
                sub_group = local_group.addGroup(folder_name)
            for filepath in shp_paths:
                result = self._load_single_file(
                    filepath, boundary, sub_group, encoding, save_mode, save_dir
                )
                if result:
                    loaded.append(result)

        # 2) 개별 파일 로드 (루트 그룹에)
        for filepath in file_paths:
            result = self._load_single_file(
                filepath, boundary, local_group, encoding, save_mode, save_dir
            )
            if result:
                loaded.append(result)

        self.shared_data["obstacle_layers"] = loaded
        self.status_label.setText(f"로컬 데이터 로드 완료: {len(loaded)}개 레이어")
        self.status_label.setStyleSheet(
            "font-size: 13px; color: #059669; padding: 4px; font-weight: 600;"
        )
        self.iface.mapCanvas().refresh()

    def _load_single_file(self, filepath, boundary, target_group,
                          encoding, save_mode, save_dir):
        """단일 파일 로드 + 인코딩 + 클리핑 + 저장 + 그룹 추가"""
        basename = os.path.splitext(os.path.basename(filepath))[0]

        # 1. 레이어 로드
        layer = QgsVectorLayer(filepath, basename, "ogr")
        if not layer.isValid():
            self.status_label.setText(f"로드 실패: {basename}")
            return None

        # 2. 인코딩 적용 (클리핑 전에 반드시 적용!)
        if encoding:
            layer.dataProvider().setEncoding(encoding)
            layer.updateFields()

        # 3. 전처리 (클리핑 + 도형수정)
        processed = Preprocessor.preprocess_layer(layer, boundary)
        if processed is None:
            return None

        # 4. 영구 저장 (save_mode == "permanent")
        if save_mode == "permanent" and save_dir:
            save_fmt = self.shared_data.get("save_format", "GPKG")
            ext = ".gpkg" if save_fmt == "GPKG" else ".shp"
            driver = "GPKG" if save_fmt == "GPKG" else "ESRI Shapefile"
            saved_path = os.path.join(save_dir, f"{basename}{ext}")
            options = QgsVectorFileWriter.SaveVectorOptions()
            options.driverName = driver
            options.fileEncoding = "UTF-8"
            error = QgsVectorFileWriter.writeAsVectorFormatV3(
                processed, saved_path,
                QgsCoordinateTransformContext(), options
            )
            if error[0] == QgsVectorFileWriter.NoError:
                processed = QgsVectorLayer(saved_path, basename, "ogr")

        # 5. 스타일 적용
        self.style_manager.apply_style_to_layer(processed, basename)

        # 6. 프로젝트에 추가 (지정된 그룹에)
        QgsProject.instance().addMapLayer(processed, False)
        target_group.addLayer(processed)
        return processed

    def reset(self):
        self.drop_list.clear_all()
        self.status_label.setText("")

    def execute_step(self):
        return True
