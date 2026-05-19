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
import logging
import re
import sys
from pathlib import Path

from PyQt6.QtCore import QObject, QThread, QTimer, QUrl, pyqtSignal
from PyQt6.QtGui import QTextCursor
from PyQt6.QtWebChannel import QWebChannel
from PyQt6.QtWebEngineCore import QWebEngineSettings
from PyQt6.QtWebEngineWidgets import QWebEngineView
from PyQt6.QtWidgets import (
    QApplication,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QFormLayout,
    QGroupBox,
    QHBoxLayout,
    QInputDialog,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMessageBox,
    QPlainTextEdit,
    QPushButton,
    QRadioButton,
    QScrollArea,
    QVBoxLayout,
    QWidget,
    QTabWidget,
)

from sff.gui.log_window import GlobalLogWindow, QtLogHandler
from sff.gui.themes import THEMES
from sff.i18n import T
from sff.structs import MainMenu, MainReturnCode

_ANSI_RE = re.compile(r"\x1b\[[0-9;]*m")
logger = logging.getLogger(__name__)


class StreamEmitter(QObject):
    text_written = pyqtSignal(str)

    def write(self, text):
        if text:
            self.text_written.emit(text)

    def flush(self):
        pass


class GenericWorker(QObject):
    finished = pyqtSignal(object)
    error = pyqtSignal(str)

    def __init__(self, func):
        super().__init__()
        self.func = func

    def run(self):
        try:
            result = self.func()
            self.finished.emit(result)
        except Exception as e:
            self.error.emit(str(e))
            self.finished.emit(None)


def _arrow_style_url(path):
    s = str(path.resolve()).replace("\\", "/")
    return f'"{s}"' if " " in s else s


_RESOURCES_DIR = Path(__file__).resolve().parent / "resources"


class SFFMainWindow(QMainWindow):
    def __init__(self, ui, steam_path):
        super().__init__()
        self.ui = ui
        self.steam_path = steam_path
        from sff.storage.settings import get_setting
        from sff.structs import Settings as _S
        _saved_theme = get_setting(_S.THEME)
        self._current_theme = _saved_theme if (_saved_theme and _saved_theme in THEMES) else "dark"
        self._music_muted = False
        self._last_web_stdout_time = 0.0
        self._game_list = []
        self._stream_emitter = StreamEmitter()
        self._log_window = GlobalLogWindow(self)
        self._log_handler = QtLogHandler()
        self._log_handler.setFormatter(
            __import__('logging').Formatter("%(name)s — %(message)s")
        )
        self._log_handler.setLevel(__import__('logging').DEBUG)
        self._log_handler.record_emitted.connect(self._log_window.append_record)
        self._log_handler.record_emitted.connect(self._forward_log_to_web)
        __import__('logging').getLogger().addHandler(self._log_handler)
        self._stream_emitter.text_written.connect(self._forward_stdout_to_web)
        self._stream_emitter.text_written.connect(self._log_window.append_text)
        self._worker = None
        self._worker_thread = None
        self.setWindowTitle("SteaMidra")
        self.setMinimumSize(960, 700)
        self.resize(1020, 780)
        from sff.gui.gui_prompts import update_parent
        update_parent(self)
        central = QWidget()
        self.setCentralWidget(central)
        root_layout = QVBoxLayout(central)
        root_layout.setContentsMargins(0, 0, 0, 0)
        root_layout.setSpacing(0)

        # ── New Web UI ──
        self._web_view = QWebEngineView()
        root_layout.addWidget(self._web_view)
        self._web_channel = QWebChannel()
        from sff.gui.web_bridge import WebBridge
        self._web_bridge = WebBridge(ui=ui, steam_path=steam_path, parent=self)
        self._web_channel.registerObject("bridge", self._web_bridge)
        self._web_view.page().setWebChannel(self._web_channel)
        # Allow loading Steam CDN images from local file:// page
        self._web_view.page().settings().setAttribute(
            QWebEngineSettings.WebAttribute.LocalContentCanAccessRemoteUrls, True
        )
        self._web_ui_active = True
        self._web_ui_loaded = False
        
        from sff.download_manager import DownloadManager
        self._download_manager = DownloadManager()
        self.ui.download_manager = self._download_manager

        # ── Menu bar ─────────────────────────────────────────────
        menubar = self.menuBar()
        settings_action = menubar.addAction(T("Settings"))
        settings_action.triggered.connect(self._show_settings)
        theme_menu = menubar.addMenu(T("Theme"))
        for key, (name, _) in THEMES.items():
            action = theme_menu.addAction(name)
            action.triggered.connect(lambda checked=False, k=key: self._set_theme(k))
        help_menu = menubar.addMenu(T("Help"))
        help_menu.addAction(T("About")).triggered.connect(self._show_about)
        help_menu.addAction(T("Check for updates")).triggered.connect(
            lambda: self._run_tool(lambda: self.ui.check_updates(self.ui.os_type))
        )
        help_menu.addAction(T("Scan game library")).triggered.connect(
            lambda: self._run_tool(lambda: self.ui.scan_library_menu())
        )
        help_menu.addAction(T("Analytics dashboard")).triggered.connect(
            lambda: self._run_tool(lambda: self.ui.analytics_dashboard_menu())
        )
        logs_action = menubar.addAction("Logs")
        logs_action.triggered.connect(self._show_log_window)
        # self._stream_emitter.text_written.connect(self._append_log)
        # Only persist the Qt fallback theme if there was no saved theme or the saved
        # theme is a known Qt theme. Web-only themes (photo themes, extra color themes)
        # are not in THEMES but must not be overwritten here.
        _should_save = _saved_theme is None or _saved_theme in THEMES
        self._set_theme(self._current_theme, save=_should_save)
        pass # self._on_source_changed()
        pass # self._refresh_game_list()
        # Start with new web UI by default — hide menu bar
        menubar.setVisible(False)
        self._load_web_ui()
        self._web_ui_loaded = True
        self._tray = None
        self._tray_hide_notified = False
        self._save_watcher_timer = QTimer(self)
        self._save_watcher_timer.timeout.connect(self._run_background_save_watcher)
        self._start_save_watcher()

    # ── Path / game source helpers ───────────────────────────────







    def _load_web_ui(self):
        """Load index.html into the QWebEngineView."""
        if getattr(sys, 'frozen', False):
            webui_dir = Path(sys._MEIPASS) / "sff" / "webui"
        else:
            webui_dir = Path(__file__).resolve().parent.parent / "webui"

        index_path = webui_dir / "index.html"
        if index_path.exists():
            self._web_view.setUrl(QUrl.fromLocalFile(str(index_path)))
        else:
            import logging
            logging.getLogger(__name__).error(
                "Web UI not found at %s", index_path
            )

    # ── Worker management ────────────────────────────────────────

    def _start_worker(self, func, label: str = "action", on_done=None):
        if self._worker_thread is not None and self._worker_thread.isRunning():
            QMessageBox.information(self, "Busy", "An action is already running.")
            return
        self._append_log(f"\n--- Running: {label} ---\n")
        old_stdout = sys.stdout
        old_stderr = sys.stderr
        sys.stdout = self._stream_emitter  # type: ignore[assignment]
        sys.stderr = self._stream_emitter  # type: ignore[assignment]
        self._worker = GenericWorker(func)
        self._worker_thread = QThread()
        self._worker.moveToThread(self._worker_thread)
        def _on_finish(_result):
            sys.stdout = old_stdout
            sys.stderr = old_stderr
            if self._worker_thread:
                self._worker_thread.quit()
                self._worker_thread.wait()
            self._worker_thread = None
            self._worker = None
            self._append_log(f"--- Done: {label} ---\n")
            if on_done:
                on_done()
        self._worker.finished.connect(_on_finish)
        self._worker.error.connect(lambda msg: self._append_log(f"Error: {msg}\n"))
        self._worker_thread.started.connect(self._worker.run)
        self._worker_thread.start()




    def _run_steam_auto_with_acf(self, acf):
        """Web UI entry point — ACF already resolved, runs on main thread via _start_worker."""
        import json
        from sff.steamauto import run_steamauto
        game_path = acf.path
        app_id = acf.app_id or "0"
        def _job():
            run_steamauto(game_path, app_id, print_func=print)
        def _done():
            if hasattr(self, '_web_bridge') and self._web_bridge:
                self._web_bridge.task_finished.emit(json.dumps({
                    "task": "steam_auto", "success": True,
                    "message": "SteamAutoCrack completed"
                }))
        self._start_worker(_job, label="SteamAutoCrack", on_done=_done)



    def _run_tool(self, func):
        label = getattr(func, "__name__", "tool")
        self._start_worker(func, label)

    def run_tool(self, func):
        """Public alias for _run_tool."""
        self._run_tool(func)

    # ── Theme ────────────────────────────────────────────────────

    def _set_theme(self, key, save=True):
        self._current_theme = key
        _, style = THEMES[key]
        self.setStyleSheet(style)
        # self.game_combo._update_arrow()
        if save:
            from sff.storage.settings import set_setting
            from sff.structs import Settings as _S
            set_setting(_S.THEME, key)

    # ── Log ──────────────────────────────────────────────────────

    def _append_log(self, text: str):
        self._log_window.append_text(text)
        self._forward_stdout_to_web(text)

    def _show_log_window(self):
        self._log_window.show()
        self._log_window.raise_()
        self._log_window.activateWindow()

    # ── Log forwarding to web UI ────────────────────────────────

    def _forward_log_to_web(self, levelno: int, html: str):
        """Forward log records to the web bridge so the web UI log panel shows them."""
        if not getattr(self, '_web_ui_active', True):
            return
        if hasattr(self, '_web_bridge') and self._web_bridge:
            import logging
            lvl = 'INFO'
            if levelno <= logging.DEBUG:
                lvl = 'DEBU'
            elif levelno <= logging.INFO:
                lvl = 'INFO'
            elif levelno <= logging.WARNING:
                lvl = 'WARN'
            else:
                lvl = 'ERRO'
            # Strip HTML tags for the web UI (it applies its own formatting)
            import re
            text = re.sub(r'<[^>]+>', '', html).strip()
            # Remove the leading HH:MM:SS timestamp already embedded by QtLogHandler
            # to avoid double-timestamps when the JS log panel adds its own.
            text = re.sub(r'^\d{2}:\d{2}:\d{2}\s*', '', text)
            self._web_bridge.log_message.emit(text)

    def _forward_stdout_to_web(self, text: str):
        """Forward _stream_emitter stdout lines to the web UI log panel."""
        if not getattr(self, '_web_ui_active', True):
            return
        import time as _time
        now = _time.monotonic()
        if now - self._last_web_stdout_time < 0.05:
            return
        self._last_web_stdout_time = now
        if hasattr(self, '_web_bridge') and self._web_bridge:
            text = _ANSI_RE.sub("", text).strip()
            if text:
                self._web_bridge.log_message.emit(f'[INFO] {text}')

    # ── Music mute ───────────────────────────────────────────────

    def _toggle_mute(self):
        if self.ui.midi_player is None:
            return
        self._music_muted = not self._music_muted
        self.ui.midi_player.set_muted(self._music_muted)
        self._mute_btn.setText("Unmute" if self._music_muted else "Mute")

    # ── Settings dialog ──────────────────────────────────────────

    def _show_settings(self):
        from sff.storage.settings import (
            clear_setting,
            export_settings,
            get_setting,
            import_settings,
            load_all_settings,
            set_setting,
        )
        from sff.structs import SettingCustomTypes, Settings
        dlg = QDialog(self)
        dlg.setWindowTitle("Settings")
        dlg.setMinimumSize(620, 500)
        layout = QVBoxLayout(dlg)
        layout.addWidget(QLabel("Double-click a setting to edit. Select and press Delete to clear."))
        win_only: set[Settings] = set()
        linux_only = {Settings.SLS_CONFIG_LOCATION}
        skip: set[Settings] = set()
        if sys.platform == "win32":
            skip = linux_only
        elif sys.platform == "linux":
            skip = win_only
        lw = QListWidget()
        saved = load_all_settings()
        settings_order: list[Settings] = [s for s in Settings if s not in skip]
        def _refresh_list():
            nonlocal saved
            saved = load_all_settings()
            lw.clear()
            for s in settings_order:
                raw = saved.get(s.key_name)
                if raw is None:
                    val_str = "(unset)"
                elif s.hidden:
                    val_str = "[ENCRYPTED]"
                elif s.type == dict:
                    val_str = "(managed internally)"
                else:
                    val_str = str(raw)
                item = QListWidgetItem(f"{s.clean_name}: {val_str}")
                item.setData(Qt.ItemDataRole.UserRole, s)
                lw.addItem(item)
        from PyQt6.QtCore import Qt
        _refresh_list()
        layout.addWidget(lw)
        btn_row = QHBoxLayout()
        edit_btn = QPushButton("Edit")
        delete_btn = QPushButton("Delete")
        export_btn = QPushButton("Export")
        import_btn = QPushButton("Import")
        btn_row.addWidget(edit_btn)
        btn_row.addWidget(delete_btn)
        btn_row.addStretch()
        btn_row.addWidget(export_btn)
        btn_row.addWidget(import_btn)
        layout.addLayout(btn_row)
        close_btn = QDialogButtonBox(QDialogButtonBox.StandardButton.Close)
        close_btn.rejected.connect(dlg.reject)
        layout.addWidget(close_btn)
        def _edit_setting():
            item = lw.currentItem()
            if not item:
                return
            s: Settings = item.data(Qt.ItemDataRole.UserRole)
            if s.type == dict:
                QMessageBox.information(dlg, "Info", f"{s.clean_name} is managed automatically.")
                return
            if s.type == bool:
                cur = get_setting(s)
                new_val = QMessageBox.question(
                    dlg,
                    s.clean_name,
                    f"Enable {s.clean_name}?",
                    QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                    QMessageBox.StandardButton.Yes if cur else QMessageBox.StandardButton.No,
                ) == QMessageBox.StandardButton.Yes
                set_setting(s, new_val)
            elif isinstance(s.type, list):
                names = [e.value for e in s.type]
                chosen, ok = QInputDialog.getItem(dlg, s.clean_name, "Select:", names, 0, False)
                if ok and chosen:
                    set_setting(s, chosen)
            elif s.type == SettingCustomTypes.DIR:
                path = QFileDialog.getExistingDirectory(dlg, s.clean_name)
                if path:
                    set_setting(s, str(Path(path).resolve()))
            elif s.type == SettingCustomTypes.FILE:
                path, _ = QFileDialog.getOpenFileName(dlg, s.clean_name)
                if path:
                    set_setting(s, str(Path(path).resolve()))
            elif s.type == str:
                if s.hidden:
                    val, ok = QInputDialog.getText(
                        dlg, s.clean_name, f"Enter {s.clean_name}:", QLineEdit.EchoMode.Password,
                    )
                else:
                    cur_val = get_setting(s) or ""
                    val, ok = QInputDialog.getText(
                        dlg, s.clean_name, f"Enter {s.clean_name}:", QLineEdit.EchoMode.Normal, str(cur_val),
                    )
                if ok:
                    set_setting(s, val)
            else:
                cur_val = get_setting(s) or ""
                val, ok = QInputDialog.getText(
                    dlg, s.clean_name, f"Enter {s.clean_name}:", QLineEdit.EchoMode.Normal, str(cur_val),
                )
                if ok:
                    set_setting(s, val)
            _refresh_list()
            self._apply_setting_live(s, dlg)
        def _delete_setting():
            item = lw.currentItem()
            if not item:
                return
            s: Settings = item.data(Qt.ItemDataRole.UserRole)
            if QMessageBox.question(
                dlg, "Delete", f"Clear {s.clean_name}?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            ) == QMessageBox.StandardButton.Yes:
                clear_setting(s)
                _refresh_list()
                self._apply_setting_live(s, dlg)
        def _export():
            path, _ = QFileDialog.getSaveFileName(dlg, "Export settings", "settings_export.json", "JSON (*.json)")
            if path:
                ok = export_settings(Path(path), include_sensitive=False)
                if ok:
                    QMessageBox.information(dlg, "Exported", f"Settings exported to {path}")
                else:
                    QMessageBox.warning(dlg, "Error", "Failed to export settings.")
        def _import():
            path, _ = QFileDialog.getOpenFileName(dlg, "Import settings", "", "JSON (*.json)")
            if not path:
                return
            if QMessageBox.question(
                dlg, "Import", "This will overwrite existing settings. Continue?",
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            ) != QMessageBox.StandardButton.Yes:
                return
            ok, msg = import_settings(Path(path))
            if ok:
                QMessageBox.information(dlg, "Imported", msg)
                _refresh_list()
            else:
                QMessageBox.warning(dlg, "Error", msg)
        edit_btn.clicked.connect(_edit_setting)
        lw.itemDoubleClicked.connect(lambda _: _edit_setting())
        delete_btn.clicked.connect(_delete_setting)
        export_btn.clicked.connect(_export)
        import_btn.clicked.connect(_import)
        dlg.exec()

    def _apply_setting_live(self, s, parent_widget=None):
        from sff.structs import Settings
        if s == Settings.PLAY_MUSIC:
            from sff.storage.settings import get_setting
            val = get_setting(Settings.PLAY_MUSIC)
            if val:
                self.ui.kill_midi_player()
                self.ui.init_midi_player()
            else:
                self.ui.kill_midi_player()
        elif s == Settings.STEAM_PATH:
            if parent_widget:
                QMessageBox.information(
                    parent_widget,
                    "Restart Recommended",
                    "Steam path changed. Please restart SteaMidra for all changes to take full effect.",
                )
        elif s == Settings.LANGUAGE:
            from sff.i18n import set_language
            from sff.storage.settings import get_setting
            set_language(get_setting(Settings.LANGUAGE))
        elif s == Settings.SAVE_WATCHER_INTERVAL:
            self._start_save_watcher()

    # ── Tray / close-to-tray ────────────────────────────────────

    def set_tray(self, tray):
        self._tray = tray

    def force_quit(self):
        self._save_watcher_timer.stop()
        if self._tray is not None:
            self._tray.minimize_to_tray = False
        self.close()

    def closeEvent(self, event):
        from sff.storage.settings import get_setting
        from sff.structs import Settings as _S
        minimize_on_close = get_setting(_S.MINIMIZE_TO_TRAY_ON_CLOSE)
        if minimize_on_close is None:
            minimize_on_close = getattr(self._tray, "minimize_to_tray", False)
        
        if self._tray is not None and minimize_on_close:
            event.ignore()
            self.hide()
            if not self._tray_hide_notified:
                self._tray_hide_notified = True
                self._tray.notify(
                    "SteaMidra",
                    "SteaMidra is running in the system tray. Click the ^ arrow near the clock to find it.",
                )
        else:
            self._save_watcher_timer.stop()
            event.accept()
            QApplication.quit() # Ensure app exits if tray is not holding it

    # ── Background save watcher ──────────────────────────────────

    def _start_save_watcher(self):
        from sff.storage.settings import get_setting
        from sff.structs import Settings as _S
        try:
            interval_min = int(get_setting(_S.SAVE_WATCHER_INTERVAL) or 10)
        except (ValueError, TypeError):
            interval_min = 10
        self._save_watcher_timer.stop()
        if interval_min > 0:
            self._save_watcher_timer.start(interval_min * 60 * 1000)

    def _run_background_save_watcher(self):
        import threading
        t = threading.Thread(target=self._do_background_save_backup, daemon=True)
        t.start()

    def _do_background_save_backup(self):
        import json
        from sff.storage.settings import get_setting
        from sff.structs import Settings as _S
        steam32_id = get_setting(_S.STEAM32_ID)
        steam_path = getattr(self, 'steam_path', None)
        provider_config_raw = get_setting(_S.LAST_BACKUP_PROVIDER_CONFIG)
        if not steam32_id or not steam_path:
            return
        try:
            if provider_config_raw:
                cfg = json.loads(provider_config_raw)
                self._cloud_save_backup(cfg, steam_path, steam32_id)
            else:
                self._local_save_backup(steam_path, steam32_id)
        except Exception:
            logger.debug('Save watcher error', exc_info=True)

    def _local_save_backup(self, steam_path, steam32_id):
        from sff.cloud_saves import CloudSaves
        userdata_dir = Path(steam_path) / 'userdata' / str(steam32_id)
        if not userdata_dir.exists():
            return
        cs = CloudSaves()
        backed_up = 0
        for app_dir in userdata_dir.iterdir():
            if not app_dir.is_dir():
                continue
            remote_dir = app_dir / 'remote'
            if not remote_dir.exists():
                continue
            all_files = [f for f in remote_dir.rglob('*') if f.is_file()]
            if not all_files:
                continue
            last_mtime = max(f.stat().st_mtime for f in all_files)
            existing = cs.get_backups(app_dir.name)
            if existing:
                newest_ts = max(b.timestamp for b in existing)
                if last_mtime <= newest_ts:
                    continue
            cs.backup(app_dir.name, str(remote_dir))
            backed_up += 1
        if backed_up:
            logger.debug('Save watcher (local): backed up %d game(s)', backed_up)

    def _cloud_save_backup(self, cfg, steam_path, steam32_id):
        from sff.cloud_saves import (
            scan_all_save_locations,
            backup_save_location_local,
            backup_save_location_rclone,
            backup_save_location_gdrive,
        )
        entries = scan_all_save_locations(steam_path=steam_path, steam32_id=steam32_id)
        if not entries:
            return
        provider = cfg.get('provider', 'local').lower()
        backed_up = 0
        if provider == 'local':
            dest_path = cfg.get('dest_path', '')
            if not dest_path:
                return
            for entry in entries:
                if backup_save_location_local(entry, dest_path):
                    backed_up += 1
        elif provider == 'rclone':
            import subprocess
            from concurrent.futures import ThreadPoolExecutor, as_completed
            import sys as _sys
            rclone_exe = cfg.get('rclone_exe', '')
            remote_dest = cfg.get('remote_dest', '')
            if not rclone_exe:
                from sff.utils import root_folder
                _bundled = root_folder() / "third_party" / "rclone" / ("rclone.exe" if _sys.platform == "win32" else "rclone")
                if _bundled.exists():
                    rclone_exe = str(_bundled)
            if not rclone_exe or not remote_dest:
                return
            unique_locs = list({e['location'] for e in entries})
            _no_window = {'creationflags': 0x08000000} if _sys.platform == 'win32' else {}
            for loc in unique_locs:
                subprocess.run(
                    [rclone_exe, 'mkdir',
                     remote_dest.rstrip('/') + f'/SteaMidraAllSaves/{loc}'],
                    capture_output=True, stdin=subprocess.DEVNULL, timeout=30, **_no_window,
                )
            with ThreadPoolExecutor(max_workers=10) as ex:
                futures = {ex.submit(backup_save_location_rclone, e, rclone_exe, remote_dest): e for e in entries}
                for fut in as_completed(futures):
                    try:
                        if fut.result():
                            backed_up += 1
                    except Exception:
                        pass
        elif provider == 'gdrive_api':
            from sff.google_drive import get_service, get_backup_root, is_authenticated, get_or_create_folder
            from concurrent.futures import ThreadPoolExecutor, as_completed
            if not is_authenticated():
                return
            svc = get_service()
            if not svc:
                return
            root_id = get_backup_root(svc)
            if not root_id:
                return
            folder_cache = {}
            for loc in {e['location'] for e in entries}:
                loc_id = get_or_create_folder(svc, loc, root_id)
                if loc_id:
                    folder_cache[(loc, root_id)] = loc_id
            with ThreadPoolExecutor(max_workers=10) as ex:
                futures = {ex.submit(backup_save_location_gdrive, e, get_service(), root_id,
                                     None, dict(folder_cache)): e for e in entries}
                for fut in as_completed(futures):
                    try:
                        if fut.result():
                            backed_up += 1
                    except Exception:
                        pass
        if backed_up:
            logger.debug('Save watcher (%s): backed up %d entries', provider, backed_up)

    # ── About ────────────────────────────────────────────────────

    def _show_about(self):
        from sff.strings import VERSION
        QMessageBox.about(
            self,
            "About SteaMidra",
            f"SteaMidra\nVersion {VERSION}\n\n"
            "https://github.com/Midrags/SFF/releases",
        )
