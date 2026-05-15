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

from pathlib import Path

from PyQt6.QtCore import QPoint, Qt, pyqtSignal
from PyQt6.QtGui import QDragEnterEvent, QDropEvent, QIcon, QMouseEvent
from PyQt6.QtWidgets import QLabel, QMenu, QWidget

from sff.file_imports import has_supported_files
from sff.gui.quick_actions import QuickAction, populate_quick_actions


class FloatingLogo(QWidget):
    files_dropped = pyqtSignal(list)

    def __init__(self, icon: QIcon, actions: list[QuickAction], parent=None):
        super().__init__(parent)
        self._actions = actions
        self._drag_pos = QPoint()
        self.setAcceptDrops(True)
        self.setWindowTitle("SteaMidra Quick Actions")
        flags = (
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.Tool
            | Qt.WindowType.WindowStaysOnTopHint
        )
        self.setWindowFlags(flags)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground, True)
        self.setFixedSize(74, 74)
        self._label = QLabel(self)
        self._label.setFixedSize(64, 64)
        self._label.move(5, 5)
        pixmap = icon.pixmap(64, 64)
        self._label.setPixmap(pixmap)
        self._label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._label.setStyleSheet(
            "QLabel { background: rgba(24, 26, 32, 210); border: 1px solid rgba(255,255,255,70); border-radius: 16px; padding: 5px; }"
        )
        self.setStyleSheet("QWidget { background: transparent; }")
        self._place_default()

    def _place_default(self):
        screen = self.screen()
        if not screen:
            return
        area = screen.availableGeometry()
        self.move(area.right() - self.width() - 28, area.bottom() - self.height() - 28)

    def contextMenuEvent(self, event):
        menu = QMenu(self)
        populate_quick_actions(menu, self._actions)
        menu.exec(event.globalPos())

    def mousePressEvent(self, event: QMouseEvent):
        if event.button() == Qt.MouseButton.LeftButton:
            self._drag_pos = event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event: QMouseEvent):
        if event.buttons() & Qt.MouseButton.LeftButton:
            self.move(event.globalPosition().toPoint() - self._drag_pos)
            event.accept()
            return
        super().mouseMoveEvent(event)

    def mouseDoubleClickEvent(self, event: QMouseEvent):
        if event.button() == Qt.MouseButton.LeftButton and self.parent():
            self.parent().showNormal()
            self.parent().activateWindow()
            self.parent().raise_()
            event.accept()
            return
        super().mouseDoubleClickEvent(event)

    def dragEnterEvent(self, event: QDragEnterEvent):
        if has_supported_files(self._event_paths(event)):
            event.acceptProposedAction()
            return
        event.ignore()

    def dropEvent(self, event: QDropEvent):
        paths = self._event_paths(event)
        if has_supported_files(paths):
            self.files_dropped.emit(paths)
            event.acceptProposedAction()
            return
        event.ignore()

    def _event_paths(self, event):
        if not event.mimeData().hasUrls():
            return []
        return [Path(url.toLocalFile()) for url in event.mimeData().urls() if url.isLocalFile()]
