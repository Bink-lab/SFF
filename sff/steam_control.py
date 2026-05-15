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
import subprocess
import sys
import time
from pathlib import Path
from typing import Optional

from sff.processes import SteamProcess, is_proc_running


def restart_steam(steam_path: Path, applist_folder: Optional[Path] = None):
    if sys.platform == "linux":
        from sff.linux.steam_process import kill_steam, start_steam
        kill_steam()
        time.sleep(1)
        return start_steam()
    if sys.platform != "win32":
        raise RuntimeError("Restart Steam is only supported on Windows and Linux.")
    process = SteamProcess(steam_path, applist_folder or steam_path)
    if is_proc_running(process.exe_name):
        print("Killing Steam...", flush=True)
        process.kill()
        deadline = time.time() + 15
        while is_proc_running(process.exe_name) and time.time() < deadline:
            time.sleep(0.5)
    launcher = _find_windows_launcher(steam_path, applist_folder)
    if launcher is None:
        raise FileNotFoundError("Could not find steam.exe or DLLInjector.exe.")
    print(f"Launching Steam with {launcher.name}...")
    subprocess.Popen([str(launcher)], cwd=str(launcher.parent), creationflags=subprocess.CREATE_NO_WINDOW)
    return True


def _find_windows_launcher(steam_path: Path, applist_folder: Optional[Path]):
    candidates = []
    if applist_folder is not None:
        injector_dir = applist_folder.parent
        candidates.extend([injector_dir / "DLLInjector.exe", injector_dir / "steam.exe"])
    candidates.append(steam_path / "steam.exe")
    for path in candidates:
        if path.exists():
            return path.resolve()
    path_env = os.environ.get("PATH", "")
    for part in path_env.split(os.pathsep):
        candidate = Path(part) / "steam.exe"
        if candidate.exists():
            return candidate.resolve()
    return None
