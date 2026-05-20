# SteaMidra - Steam game setup and manifest tool (SFF)
# Copyright (c) 2025-2026 Midrag (https://github.com/Midrags)
#
# This file is part of SteaMidra.
#
# SteaMidra is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# SteaMidra is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with SteaMidra.  If not, see <https://www.gnu.org/licenses/>.

import os
from PyQt6.QtCore import Qt, QPoint, QEvent, pyqtSignal
from PyQt6.QtGui import QPixmap, QAction, QCursor
from PyQt6.QtWidgets import QWidget, QLabel, QVBoxLayout, QMenu, QApplication

from sff.storage.settings import get_setting, set_setting
from sff.structs import Settings

class FloatingIcon(QWidget):
    """
    A floating, draggable icon that stays on top of all windows.
    Supports drag-and-drop for ZIP files.
    """
    file_dropped = pyqtSignal(str)
    exit_requested = pyqtSignal()

    def __init__(self, icon_path=None, parent=None):
        super().__init__(parent)
        self.setWindowFlags(
            Qt.WindowType.WindowStaysOnTopHint | 
            Qt.WindowType.FramelessWindowHint | 
            Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setAcceptDrops(True)

        self.layout = QVBoxLayout(self)
        self.layout.setContentsMargins(0, 0, 0, 0)

        self.label = QLabel(self)
        self.label.setAcceptDrops(True)
        self.label.installEventFilter(self)
        if icon_path and os.path.exists(icon_path):
            pixmap = QPixmap(icon_path)
            self.label.setPixmap(pixmap.scaled(64, 64, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation))
            self._base_stylesheet = ""
        else:
            self.label.setText("SFF")
            self._base_stylesheet = "color: white; background-color: rgba(0, 0, 0, 150); border-radius: 10px; padding: 10px;"
            self.label.setStyleSheet(self._base_stylesheet)

        self.layout.addWidget(self.label)

        self._drag_pos = QPoint()
        self.setToolTip("Drag .zip here to process\nRight-click to hide")
        self._restore_position()

    def eventFilter(self, obj, event):
        if obj is self.label:
            if event.type() == QEvent.Type.DragEnter:
                self._handle_drag_enter(event)
                return event.isAccepted()
            if event.type() == QEvent.Type.DragMove:
                self._handle_drag_move(event)
                return event.isAccepted()
            if event.type() == QEvent.Type.DragLeave:
                self._clear_drop_highlight()
                event.accept()
                return True
            if event.type() == QEvent.Type.Drop:
                self._handle_drop(event)
                return event.isAccepted()
        return super().eventFilter(obj, event)

    def _zip_paths(self, event):
        if not event.mimeData().hasUrls():
            return []
        return [
            url.toLocalFile()
            for url in event.mimeData().urls()
            if url.toLocalFile().lower().endswith(".zip")
        ]

    def _set_drop_highlight(self):
        highlight_style = "border: 2px solid #00ff00;"
        if self._base_stylesheet:
            self.label.setStyleSheet(self._base_stylesheet + " " + highlight_style)
        else:
            self.label.setStyleSheet(highlight_style)

    def _clear_drop_highlight(self):
        self.label.setStyleSheet(self._base_stylesheet)

    def _handle_drag_enter(self, event):
        if self._zip_paths(event):
            self._set_drop_highlight()
            event.acceptProposedAction()
            return
        event.ignore()

    def _handle_drag_move(self, event):
        if self._zip_paths(event):
            event.acceptProposedAction()
            return
        event.ignore()

    def _handle_drop(self, event):
        self._clear_drop_highlight()
        paths = self._zip_paths(event)
        if paths:
            self.file_dropped.emit(paths[0])
            event.acceptProposedAction()
            return
        event.ignore()

    def _restore_position(self):
        raw = get_setting(Settings.FLOATING_ICON_POSITION)
        if not raw:
            return
        try:
            x_str, y_str = str(raw).split(",", 1)
            pos = self._clamp_to_screen(QPoint(int(x_str), int(y_str)))
            self.move(pos)
        except (TypeError, ValueError):
            return

    def save_position(self):
        pos = self._clamp_to_screen(self.pos())
        set_setting(Settings.FLOATING_ICON_POSITION, f"{pos.x()},{pos.y()}")

    def _clamp_to_screen(self, pos):
        screen = QApplication.screenAt(pos)
        if screen is None:
            screen = QApplication.primaryScreen()
        if screen is None:
            return pos
        rect = screen.availableGeometry()
        max_x = max(rect.left(), rect.right() - self.width())
        max_y = max(rect.top(), rect.bottom() - self.height())
        x = min(max(pos.x(), rect.left()), max_x)
        y = min(max(pos.y(), rect.top()), max_y)
        return QPoint(x, y)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_pos = event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            event.accept()

    def mouseMoveEvent(self, event):
        if event.buttons() & Qt.MouseButton.LeftButton:
            self.move(event.globalPosition().toPoint() - self._drag_pos)
            event.accept()

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.save_position()
            event.accept()

    def closeEvent(self, event):
        self.save_position()
        super().closeEvent(event)

    def contextMenuEvent(self, event):
        menu = QMenu(self)
        hide_action = QAction("Hide Icon", self)
        hide_action.triggered.connect(self.hide)
        menu.addAction(hide_action)

        exit_action = QAction("Exit SteaMidra", self)
        exit_action.triggered.connect(self.exit_requested.emit)
        menu.addAction(exit_action)

        menu.exec(QCursor.pos())

    def dragEnterEvent(self, event):
        self._handle_drag_enter(event)

    def dragMoveEvent(self, event):
        self._handle_drag_move(event)

    def dragLeaveEvent(self, event):
        self._clear_drop_highlight()
        event.accept()

    def dropEvent(self, event):
        self._handle_drop(event)
