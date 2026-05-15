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

from dataclasses import dataclass
from typing import Callable, Optional

from PyQt6.QtGui import QAction
from PyQt6.QtWidgets import QMenu


@dataclass(frozen=True)
class QuickAction:
    text: str
    callback: Optional[Callable[[], None]] = None
    separator_before: bool = False


def populate_quick_actions(menu: QMenu, actions: list[QuickAction]) -> dict[str, QAction]:
    built = {}
    for item in actions:
        if item.separator_before:
            menu.addSeparator()
        action = QAction(item.text, menu)
        if item.callback is None:
            action.setEnabled(False)
        else:
            action.triggered.connect(item.callback)
        menu.addAction(action)
        built[item.text] = action
    return built
