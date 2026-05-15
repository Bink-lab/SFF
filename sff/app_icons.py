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

import sys
from pathlib import Path
from typing import Iterable

from PyQt6.QtGui import QIcon

from sff.utils import root_folder

APP_USER_MODEL_ID = "Midrag.SteaMidra.SFF"
APP_ICON_NAMES = ("SFF.ico", "SFF.png", "sff.ico", "sff.png")


def app_icon_paths() -> Iterable[Path]:
    roots = []
    try:
        roots.append(root_folder(outside_internal=True))
    except Exception:
        pass
    roots.append(Path.cwd())
    if getattr(sys, "frozen", False) and hasattr(sys, "_MEIPASS"):
        roots.append(Path(sys._MEIPASS))
    seen = set()
    for base in roots:
        for name in APP_ICON_NAMES:
            path = (base / name).resolve()
            if path in seen:
                continue
            seen.add(path)
            yield path


def load_app_icon() -> QIcon:
    for path in app_icon_paths():
        if path.exists():
            icon = QIcon(str(path))
            if not icon.isNull():
                return icon
    return QIcon()


def set_windows_app_user_model_id():
    if sys.platform != "win32":
        return
    try:
        import ctypes
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_USER_MODEL_ID)
    except Exception:
        pass
