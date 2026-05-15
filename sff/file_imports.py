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

import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

from sff.steam_tools_compat import sync_manifest_to_config_depotcache

SUPPORTED_DROP_SUFFIXES = {".lua", ".zip", ".manifest"}


@dataclass
class ImportBatch:
    lua_like: list[Path] = field(default_factory=list)
    manifests: list[Path] = field(default_factory=list)
    ignored: list[Path] = field(default_factory=list)

    @property
    def accepted_count(self) -> int:
        return len(self.lua_like) + len(self.manifests)


def split_supported_files(paths: Iterable[Path]) -> ImportBatch:
    batch = ImportBatch()
    for path in paths:
        if not path.is_file():
            batch.ignored.append(path)
            continue
        suffix = path.suffix.lower()
        if suffix in {".lua", ".zip"}:
            batch.lua_like.append(path)
        elif suffix == ".manifest":
            batch.manifests.append(path)
        else:
            batch.ignored.append(path)
    return batch


def has_supported_files(paths: Iterable[Path]) -> bool:
    return any(path.is_file() and path.suffix.lower() in SUPPORTED_DROP_SUFFIXES for path in paths)


def import_manifest_files(paths: Iterable[Path], steam_path: Path) -> list[Path]:
    staging = Path.cwd() / "manifests"
    depotcache = steam_path / "depotcache"
    staging.mkdir(exist_ok=True)
    depotcache.mkdir(parents=True, exist_ok=True)
    written = []
    for source in paths:
        target = depotcache / source.name
        staged = staging / source.name
        shutil.copy2(source, staged)
        shutil.copy2(source, target)
        sync_manifest_to_config_depotcache(steam_path, target)
        written.append(target)
    return written
