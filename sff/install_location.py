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

from sff.storage.settings import get_setting
from sff.storage.vdf import get_steam_libs
from sff.structs import Settings


def get_default_game_install_path(steam_path):
    default_path = get_setting(Settings.DEFAULT_GAME_INSTALL_PATH)
    if not default_path:
        return None
    try:
        selected = Path(default_path).resolve()
        for library in get_steam_libs(Path(steam_path)):
            if library.resolve() == selected:
                return selected
    except Exception:
        return None
    return None


def get_install_libraries(steam_path):
    try:
        return get_steam_libs(Path(steam_path))
    except Exception:
        return []


def should_prompt_for_install_path(steam_path):
    return get_default_game_install_path(steam_path) is None and len(get_install_libraries(steam_path)) > 1
