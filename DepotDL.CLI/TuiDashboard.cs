using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace DepotDL.CLI
{
    public static class TuiDashboard
    {
        public static int RunInteractiveTui(string ddmodPath, string dotnetPath)
        {
            void LogInfo(string message) => WriteColored(message, ConsoleColor.Cyan);
            void LogSuccess(string message) => WriteColored(message, ConsoleColor.Green);
            void LogWarning(string message) => WriteColored(message, ConsoleColor.Yellow);
            void LogError(string message) => WriteColored(message, ConsoleColor.Red);

            // Setup stateful TUI session
            var session = new TuiSession();
            
            // Resolve standard default manifests directory
            string defaultManifestsDir = "manifests";
            if (!Directory.Exists(defaultManifestsDir))
            {
                if (Directory.Exists("../manifests")) defaultManifestsDir = "../manifests";
                else if (Directory.Exists("depotcache")) defaultManifestsDir = "depotcache";
                else if (Directory.Exists("../depotcache")) defaultManifestsDir = "../depotcache";
            }
            session.ManifestsDir = defaultManifestsDir;

            // Startup Verification Check & Console Alert
            Console.Clear();
            LogInfo("[Scan] Scanning and verifying installed game library files...");
            int verified = LibraryManager.VerifyLibraryOnStartup(out int totalCount, out int missingCount);
            if (totalCount > 0)
            {
                if (missingCount == 0)
                {
                    LogSuccess($"\n[Library] Startup verification complete: {verified}/{totalCount} game(s) verified on disk!");
                }
                else
                {
                    LogWarning($"\n[Library] Startup verification alert: {verified}/{totalCount} game(s) verified on disk.");
                    LogWarning($"          {missingCount} game(s) missing or folders moved!");
                }
                System.Threading.Thread.Sleep(2000);
            }

            int menuIndex = 0;
            while (true)
            {
                Console.Clear();
                DrawHeaderAndBox(session);

                Console.WriteLine("Navigate with Up/Down Arrow keys, press Enter to configure.\n");

                var menuItems = new List<string>
                {
                    "1. Import Configs/Manifests from ZIP File",
                    "2. Manage Installed Library",
                    "3. Choose Game Lua File",
                    "4. Select Depots to Download " + (session.AllDepots.Count == 0 ? "(Unavailable - Load Lua first)" : ""),
                    "5. Configure Manifests Cache Manager",
                    "6. Configure Output Download Folder",
                    "7. Start Download",
                    "8. Exit"
                };

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == menuIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (i == 3 && session.AllDepots.Count == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        else if (i == 6 && string.IsNullOrEmpty(session.LuaPath))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        else if (i == 6)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        else
                        {
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        Console.ResetColor();
                    }
                }

                DrawFooter();

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    menuIndex = (menuIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    menuIndex = (menuIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    Console.Clear();
                    LogInfo("Goodbye!");
                    return 0;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (menuIndex == 0) // Import Configs/Manifests from ZIP File
                    {
                        RunZipImportAction(session);
                    }
                    else if (menuIndex == 1) // Manage Installed Library
                    {
                        RunLibraryDashboard(session, ddmodPath, dotnetPath);
                    }
                    else if (menuIndex == 2) // Choose Game Lua File
                    {
                        RunChooseLuaAction(session);
                    }
                    else if (menuIndex == 3) // Select Depots
                    {
                        if (session.AllDepots.Count == 0)
                        {
                            LogWarning("Please load a game Lua file first!");
                            Console.ReadKey();
                            continue;
                        }

                        session.SelectedDepots = RunCheckboxSelector($"SELECT DEPOTS FOR APP {session.AppId}", session.AllDepots, session.SelectedDepots);
                    }
                    else if (menuIndex == 4) // Configure Manifests Cache Manager
                    {
                        RunManifestCacheManager(session);
                    }
                    else if (menuIndex == 5) // Configure Output Download Folder
                    {
                        RunConfigureOutputAction(session);
                    }
                    else if (menuIndex == 6) // Start Download
                    {
                        if (string.IsNullOrEmpty(session.LuaPath))
                        {
                            WriteColored("[Error] No Lua config file loaded! Please select a Lua file first.", ConsoleColor.Red);
                            Console.ReadKey();
                            continue;
                        }
                        if (session.SelectedDepots.Count == 0)
                        {
                            WriteColored("[Error] No depots selected for download! Please select at least one depot.", ConsoleColor.Red);
                            Console.ReadKey();
                            continue;
                        }

                        if (string.IsNullOrEmpty(session.OutputDir))
                        {
                            session.OutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", GetGameName(session));
                        }

                        Console.Clear();
                        LogInfo("=== STARTING DOWNLOAD ===");
                        
                        int exitCode = Program.TriggerDownloadProcess(session.LuaPath, session.ManifestsDir, session.OutputDir, ddmodPath, dotnetPath, session.SelectedDepots);
                        
                        if (exitCode == 0)
                        {
                            LogSuccess("\n[Success] Game downloaded and registered successfully.");
                        }
                        else
                        {
                            LogError($"\n[Error] Download finished with exit code: {exitCode}");
                        }
                        Console.WriteLine("\nPress any key to return to main menu...");
                        Console.ReadKey(true);
                        continue;
                    }
                    else if (menuIndex == 7) // Exit
                    {
                        Console.Clear();
                        LogInfo("Goodbye!");
                        return 0;
                    }
                }
            }
        }

        private static void RunLibraryDashboard(TuiSession session, string ddmodPath, string dotnetPath)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                var games = LibraryManager.LoadLibrary();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                         INSTALLED GAMES LIBRARY                              ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                Console.WriteLine("Navigate with Up/Down Arrow keys, press Enter to view details.\n");

                var menuItems = new List<string>();
                foreach (var g in games)
                {
                    string status = g.IsVerified ? "[Verified]" : "[Missing]";
                    string sizeStr = FormatSize(g.TotalSizeBytes);
                    menuItems.Add($"{g.GameName} ({g.AppId}) {status} - {sizeStr}");
                }
                menuItems.Add("[BATCH OPERATIONS...]");
                menuItems.Add("[Back]");

                if (selectedIndex >= menuItems.Count) selectedIndex = menuItems.Count - 1;
                if (selectedIndex < 0) selectedIndex = 0;

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (i < games.Count)
                        {
                            if (games[i].IsVerified)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"   {menuItems[i]}");
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"   {menuItems[i]}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        Console.ResetColor();
                    }
                }

                DrawFooter();

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return; // Go back to main menu
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == menuItems.Count - 1) // [Back]
                    {
                        return;
                    }
                    else if (selectedIndex == menuItems.Count - 2) // [BATCH OPERATIONS...]
                    {
                        RunLibraryBatchActions(session, ddmodPath, dotnetPath);
                    }
                    else // Selected a specific game!
                    {
                        if (RunGameDetailsMenu(games[selectedIndex], session, ddmodPath, dotnetPath))
                        {
                            return; // Exit library back to main menu
                        }
                    }
                }
            }
        }

        private static bool RunGameDetailsMenu(LibraryGame game, TuiSession session, string ddmodPath, string dotnetPath)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine($"║ GAME DETAILS: {game.GameName.ToUpper().PadRight(54)} ║");
                Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
                Console.ResetColor();

                void DrawDetailRow(string label, string val, ConsoleColor valColor)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("║ ");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write(label.PadRight(18));
                    Console.ForegroundColor = valColor;
                    
                    string displayVal = val;
                    if (displayVal.Length > 56)
                    {
                        displayVal = displayVal.Substring(0, 53) + "...";
                    }
                    Console.Write(displayVal.PadRight(56));
                    
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" ║");
                }

                DrawDetailRow("App ID Target:", game.AppId, ConsoleColor.Green);
                DrawDetailRow("Lua Config Path:", game.LuaPath, ConsoleColor.Gray);
                DrawDetailRow("Output Folder:", game.OutputDir, ConsoleColor.Gray);
                DrawDetailRow("Depot IDs:", string.Join(", ", game.DepotIds), ConsoleColor.White);
                DrawDetailRow("Install Date:", game.InstallDate.ToString("g"), ConsoleColor.White);
                DrawDetailRow("Total Size:", FormatSize(game.TotalSizeBytes), ConsoleColor.White);
                DrawDetailRow("Verification:", game.IsVerified ? "Verified (Exists on disk)" : "Missing (Directory not found)", game.IsVerified ? ConsoleColor.Green : ConsoleColor.Red);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                Console.WriteLine("\nSelect an action:\n");

                var menuItems = new List<string>
                {
                    "1. Open Download Folder in File Explorer",
                    "2. Verify Files (Scan size & directory presence)",
                    "3. Load & Re-download/Update Game",
                    "4. Uninstall & Delete Game Files from Disk",
                    "5. Back"
                };

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (i == 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        else
                        {
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        Console.ResetColor();
                    }
                }

                DrawFooter();

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return false;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0) // Open Download Folder
                    {
                        Console.Clear();
                        WriteColored($"[Explorer] Launching explorer.exe for folder: {game.OutputDir}", ConsoleColor.Cyan);
                        try
                        {
                            if (Directory.Exists(game.OutputDir))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = $"\"{game.OutputDir}\"",
                                    UseShellExecute = true
                                });
                            }
                            else
                            {
                                WriteColored("[Error] Directory does not exist on disk.", ConsoleColor.Red);
                                Console.ReadKey();
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteColored($"[Error] Could not open explorer: {ex.Message}", ConsoleColor.Red);
                            Console.ReadKey();
                        }
                    }
                    else if (selectedIndex == 1) // Verify Files
                    {
                        Console.Clear();
                        WriteColored("[Verify] Scanning directory size and presence on disk...", ConsoleColor.Cyan);
                        
                        bool exists = Directory.Exists(game.OutputDir);
                        long size = exists ? LibraryManager.GetDirectorySize(game.OutputDir) : 0;
                        
                        game.IsVerified = exists;
                        game.TotalSizeBytes = size;
                        
                        LibraryManager.AddOrUpdateGame(game);
                        
                        if (exists)
                        {
                            WriteColored($"[Success] Directory verified successfully! Updated size to {FormatSize(size)}.", ConsoleColor.Green);
                        }
                        else
                        {
                            WriteColored("[Warning] Directory missing! Updated status in database.", ConsoleColor.Yellow);
                        }
                        Console.WriteLine("\nPress any key to return.");
                        Console.ReadKey();
                    }
                    else if (selectedIndex == 2) // Load & Re-download
                    {
                        Console.Clear();
                        WriteColored("[Load] Loading configuration to active session...", ConsoleColor.Cyan);
                        
                        session.LuaPath = game.LuaPath;
                        session.OutputDir = game.OutputDir;
                        
                        ParseLuaFileIntoSession(session);
                        
                        var restoredDepots = new List<DepotInfo>();
                        foreach (var depId in game.DepotIds)
                        {
                            var match = session.AllDepots.Find(d => d.DepotId == depId);
                            if (match != null) restoredDepots.Add(match);
                        }
                        if (restoredDepots.Count > 0)
                        {
                            session.SelectedDepots = restoredDepots;
                        }

                        WriteColored($"\n[Success] Active session populated with '{game.GameName}'!", ConsoleColor.Green);
                        WriteColored("You will be redirected to the Main Menu. Simply choose 'Start Download' to begin.", ConsoleColor.Cyan);
                        Console.WriteLine("\nPress any key to continue...");
                        Console.ReadKey();
                        return true; // Exit details and library dashboard to main menu
                    }
                    else if (selectedIndex == 3) // Uninstall & Delete Game
                    {
                        Console.Clear();
                        WriteColored("╔══════════════════════════════════════════════════════════════════════════════╗", ConsoleColor.Red);
                        WriteColored("║                           CONFIRM UNINSTALLATION                             ║", ConsoleColor.Red);
                        WriteColored("╚══════════════════════════════════════════════════════════════════════════════╝", ConsoleColor.Red);
                        Console.WriteLine($"You are about to delete: {game.GameName}");
                        Console.WriteLine($"This will recursively DELETE all files inside: {game.OutputDir}");
                        WriteColored("\nWARNING: THIS ACTION CANNOT BE UNDONE!", ConsoleColor.Yellow);
                        Console.Write("\nAre you absolutely sure you want to uninstall and delete files? (y/N): ");
                        
                        string? input = Console.ReadLine()?.Trim().ToLower();
                        if (input == "y" || input == "yes")
                        {
                            WriteColored("\n[Uninstall] Deleting files from disk...", ConsoleColor.Cyan);
                            bool success = LibraryManager.RobustDeleteDirectory(game.OutputDir);
                            if (success)
                            {
                                WriteColored("[Uninstall] Directory successfully cleared.", ConsoleColor.Green);
                            }
                            else
                            {
                                WriteColored("[Warning] Some files could not be deleted instantly, directory might be locked.", ConsoleColor.Yellow);
                            }
                            
                            LibraryManager.RemoveGame(game.AppId);
                            WriteColored("[Uninstall] Pruned game entry from database.", ConsoleColor.Green);
                            Console.WriteLine("\nPress any key to return.");
                            Console.ReadKey();
                            return false; // Break back to library dashboard
                        }
                        else
                        {
                            WriteColored("\nUninstall cancelled.", ConsoleColor.Gray);
                            Console.WriteLine("\nPress any key to return.");
                            Console.ReadKey();
                        }
                    }
                    else if (selectedIndex == 4) // Back
                    {
                        return false;
                    }
                }
            }
        }

        private static void RunLibraryBatchActions(TuiSession session, string ddmodPath, string dotnetPath)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                         LIBRARY BATCH OPERATIONS                             ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                var menuItems = new List<string>
                {
                    "1. Batch Verify All Games (Rescans all folders)",
                    "2. Batch Prune Missing Records (Removes entries without disk folders)",
                    "3. Batch Uninstall Selected (Deletes multiple games from disk)",
                    "4. Batch Download Queue (Sequential back-to-back downloads)",
                    "5. Back"
                };

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"   {menuItems[i]}");
                    }
                }

                DrawFooter();

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0) // Batch Verify All
                    {
                        Console.Clear();
                        WriteColored("=== BATCH VERIFYING ALL GAMES ===", ConsoleColor.Cyan);
                        var games = LibraryManager.LoadLibrary();
                        int verifiedCount = 0;
                        int missingCount = 0;
                        foreach (var g in games)
                        {
                            bool exists = Directory.Exists(g.OutputDir);
                            g.IsVerified = exists;
                            g.TotalSizeBytes = exists ? LibraryManager.GetDirectorySize(g.OutputDir) : 0;
                            if (exists) verifiedCount++; else missingCount++;
                        }
                        LibraryManager.SaveLibrary(games);
                        WriteColored($"\n[Complete] Scan finished.", ConsoleColor.Green);
                        WriteColored($"  - Verified on disk: {verifiedCount}", ConsoleColor.Green);
                        if (missingCount > 0)
                        {
                            WriteColored($"  - Missing folders:  {missingCount}", ConsoleColor.Yellow);
                        }
                        Console.WriteLine("\nPress any key to return.");
                        Console.ReadKey();
                    }
                    else if (selectedIndex == 1) // Batch Prune Missing
                    {
                        Console.Clear();
                        WriteColored("=== BATCH PRUNING MISSING RECORDS ===", ConsoleColor.Cyan);
                        var games = LibraryManager.LoadLibrary();
                        int initialCount = games.Count;
                        games.RemoveAll(g => !Directory.Exists(g.OutputDir));
                        int finalCount = games.Count;
                        LibraryManager.SaveLibrary(games);
                        
                        WriteColored($"\n[Pruned] Removed {initialCount - finalCount} missing game records.", ConsoleColor.Green);
                        Console.WriteLine("\nPress any key to return.");
                        Console.ReadKey();
                    }
                    else if (selectedIndex == 2) // Batch Uninstall Selected
                    {
                        RunBatchUninstallScreen();
                    }
                    else if (selectedIndex == 3) // Batch Download Queue
                    {
                        RunBatchDownloadScreen(session, ddmodPath, dotnetPath);
                    }
                    else if (selectedIndex == 4) // Back
                    {
                        return;
                    }
                }
            }
        }

        private static void RunBatchUninstallScreen()
        {
            var games = LibraryManager.LoadLibrary();
            if (games.Count == 0)
            {
                Console.Clear();
                WriteColored("No games registered in the library.", ConsoleColor.Yellow);
                Console.ReadKey();
                return;
            }

            var selected = RunCheckboxSelectorGames("SELECT GAMES FOR BULK UNINSTALL", games);
            if (selected.Count == 0) return;

            Console.Clear();
            WriteColored("╔══════════════════════════════════════════════════════════════════════════════╗", ConsoleColor.Red);
            WriteColored("║                      CONFIRM BULK UNINSTALLATION                            ║", ConsoleColor.Red);
            WriteColored("╚══════════════════════════════════════════════════════════════════════════════╝", ConsoleColor.Red);
            Console.WriteLine($"You are about to delete {selected.Count} game(s) and ALL of their files from disk:");
            foreach (var g in selected)
            {
                Console.WriteLine($"  - {g.GameName} ({g.OutputDir})");
            }
            WriteColored("\nWARNING: THIS WILL RECURSIVELY DELETE ALL GAME DIRECTORIES LISTED!", ConsoleColor.Yellow);
            Console.Write("\nAre you absolutely sure you want to proceed? (y/N): ");
            string? input = Console.ReadLine()?.Trim().ToLower();
            if (input == "y" || input == "yes")
            {
                foreach (var g in selected)
                {
                    WriteColored($"\n[Uninstalling] Deleting files for {g.GameName}...", ConsoleColor.Cyan);
                    LibraryManager.RobustDeleteDirectory(g.OutputDir);
                    LibraryManager.RemoveGame(g.AppId);
                }
                WriteColored("\n[Success] Bulk uninstallation complete.", ConsoleColor.Green);
                Console.WriteLine("\nPress any key to return.");
                Console.ReadKey();
            }
            else
            {
                WriteColored("\nCancelled bulk uninstallation.", ConsoleColor.Gray);
                Console.WriteLine("\nPress any key to return.");
                Console.ReadKey();
            }
        }

        private static void RunBatchDownloadScreen(TuiSession session, string ddmodPath, string dotnetPath)
        {
            var games = LibraryManager.LoadLibrary();
            if (games.Count == 0)
            {
                Console.Clear();
                WriteColored("No games registered in the library to download.", ConsoleColor.Yellow);
                Console.ReadKey();
                return;
            }

            var selected = RunCheckboxSelectorGames("SELECT GAMES FOR SEQUENTIAL DOWNLOAD QUEUE", games);
            if (selected.Count == 0) return;

            Console.Clear();
            WriteColored("=== SEQUENTIAL BATCH DOWNLOAD QUEUE ===", ConsoleColor.Cyan);
            Console.WriteLine($"You have selected {selected.Count} game(s) to download sequentially back-to-back:\n");
            for (int i = 0; i < selected.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {selected[i].GameName} (App ID: {selected[i].AppId})");
            }
            Console.Write("\nPress Enter to begin the batch download queue or Escape to cancel...");
            var k = Console.ReadKey(true).Key;
            if (k != ConsoleKey.Enter) return;

            for (int i = 0; i < selected.Count; i++)
            {
                var game = selected[i];
                Console.Clear();
                WriteColored($"================================================================================", ConsoleColor.Cyan);
                WriteColored($" BATCH QUEUE: {i + 1} OF {selected.Count} - {game.GameName.ToUpper()}", ConsoleColor.Cyan);
                WriteColored($"================================================================================", ConsoleColor.Cyan);

                if (!File.Exists(game.LuaPath))
                {
                    WriteColored($"[Error] Lua configuration file not found at: {game.LuaPath}", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to skip to next game in queue...");
                    Console.ReadKey();
                    continue;
                }

                // Temporary session for this batch execution
                var batchSession = new TuiSession
                {
                    LuaPath = game.LuaPath,
                    OutputDir = game.OutputDir,
                    ManifestsDir = session.ManifestsDir
                };
                ParseLuaFileIntoSession(batchSession);

                // Overwrite with the specifically tracked depots
                var restoredDepots = new List<DepotInfo>();
                foreach (var depId in game.DepotIds)
                {
                    var match = batchSession.AllDepots.Find(d => d.DepotId == depId);
                    if (match != null) restoredDepots.Add(match);
                }
                if (restoredDepots.Count > 0)
                {
                    batchSession.SelectedDepots = restoredDepots;
                }

                WriteColored($"[Queue] Triggering download for {game.GameName}...", ConsoleColor.Cyan);
                int exitCode = Program.TriggerDownloadProcess(
                    batchSession.LuaPath, 
                    batchSession.ManifestsDir, 
                    batchSession.OutputDir, 
                    ddmodPath, 
                    dotnetPath, 
                    batchSession.SelectedDepots
                );

                if (exitCode == 0)
                {
                    WriteColored($"\n[Success] Batch step {i + 1}/{selected.Count} for {game.GameName} complete!", ConsoleColor.Green);
                }
                else
                {
                    WriteColored($"\n[Warning] Batch step {i + 1}/{selected.Count} for {game.GameName} finished with exit code: {exitCode}", ConsoleColor.Yellow);
                }

                if (i < selected.Count - 1)
                {
                    WriteColored("\nProceeding to the next game in the queue...", ConsoleColor.Cyan);
                    System.Threading.Thread.Sleep(2000);
                }
            }

            WriteColored("\n================================================================================", ConsoleColor.Green);
            WriteColored("               ALL SEQUENTIAL BATCH DOWNLOADS FINISHED!                         ", ConsoleColor.Green);
            WriteColored("================================================================================", ConsoleColor.Green);
            Console.WriteLine("\nPress any key to return to batch menu.");
            Console.ReadKey();
        }

        private static List<LibraryGame> RunCheckboxSelectorGames(string prompt, List<LibraryGame> options)
        {
            int index = 0;
            var selected = new bool[options.Count];

            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"=== {prompt} ===");
                Console.ResetColor();
                Console.WriteLine("Use Up/Down Arrow keys to navigate, Space to toggle, Enter to confirm, Escape to cancel.\n");

                for (int i = 0; i < options.Count; i++)
                {
                    var isCurrent = i == index;
                    var isChecked = selected[i];
                    var checkbox = isChecked ? "[✔]" : "[ ]";

                    if (isCurrent)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {checkbox} {options[i].GameName} ({options[i].AppId})");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (isChecked)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   {checkbox} {options[i].GameName} ({options[i].AppId})");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine($"   {checkbox} {options[i].GameName} ({options[i].AppId})");
                        }
                        Console.ResetColor();
                    }
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    index = (index - 1 + options.Count) % options.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    index = (index + 1) % options.Count;
                }
                else if (key == ConsoleKey.Spacebar)
                {
                    selected[index] = !selected[index];
                }
                else if (key == ConsoleKey.Enter)
                {
                    var result = new List<LibraryGame>();
                    for (int i = 0; i < options.Count; i++)
                    {
                        if (selected[i]) result.Add(options[i]);
                    }
                    return result;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return new List<LibraryGame>(); // cancel
                }
            }
        }

        private static void RunManifestCacheManager(TuiSession session)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                        MANIFEST CACHE MANAGER                                ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                string currentDir = session.ManifestsDir ?? "manifests";
                int manifestCount = 0;
                int luaCount = 0;
                long totalSize = 0;

                try
                {
                    if (Directory.Exists(currentDir))
                    {
                        var di = new DirectoryInfo(currentDir);
                        foreach (var file in di.GetFiles("*.manifest", SearchOption.TopDirectoryOnly))
                        {
                            manifestCount++;
                            totalSize += file.Length;
                        }
                    }
                    var luaFiles = FindLuaFiles();
                    luaCount = luaFiles.Count;
                }
                catch { }

                void DrawCacheRow(string label, string val, ConsoleColor valColor)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("║ ");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write(label.PadRight(22));
                    Console.ForegroundColor = valColor;
                    
                    string displayVal = val;
                    if (displayVal.Length > 52)
                    {
                        displayVal = displayVal.Substring(0, 49) + "...";
                    }
                    Console.Write(displayVal.PadRight(52));
                    
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" ║");
                }

                DrawCacheRow("Cache Directory:", Path.GetFullPath(currentDir), ConsoleColor.White);
                DrawCacheRow("Manifest Files Count:", manifestCount.ToString(), ConsoleColor.Green);
                DrawCacheRow("Manifest Cache Size:", FormatSize(totalSize), ConsoleColor.Green);
                DrawCacheRow("Lua Configs Found:", luaCount.ToString(), ConsoleColor.White);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                Console.WriteLine("\nSelect manifest actions:\n");

                var menuItems = new List<string>
                {
                    "1. Configure/Select Manifests Cache Folder",
                    "2. Import Individual Manifest Files (Multiple Selection)",
                    "3. Import Configs & Manifests from ZIP File",
                    "4. Scan & List Detailed Cached Manifests",
                    "5. Clear Manifest Cache Folder (Deletes manifest files)",
                    "6. Back"
                };

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"   {menuItems[i]}");
                    }
                }

                DrawFooter();

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0) // Configure manifests folder
                    {
                        RunConfigureManifestsFolderAction(session);
                    }
                    else if (selectedIndex == 1) // Import individual manifest files
                    {
                        RunImportIndividualManifestFilesAction(session);
                    }
                    else if (selectedIndex == 2) // Import configs/manifests from ZIP file
                    {
                        RunZipImportAction(session);
                    }
                    else if (selectedIndex == 3) // Scan & list detailed cached manifests
                    {
                        RunScanCachedManifestDetails(session);
                    }
                    else if (selectedIndex == 4) // Clear manifest cache
                    {
                        Console.Clear();
                        WriteColored("╔══════════════════════════════════════════════════════════════════════════════╗", ConsoleColor.Red);
                        WriteColored("║                           CLEAR MANIFEST CACHE                               ║", ConsoleColor.Red);
                        WriteColored("╚══════════════════════════════════════════════════════════════════════════════╝", ConsoleColor.Red);
                        Console.WriteLine($"You are about to delete ALL manifest files inside: {Path.GetFullPath(currentDir)}");
                        WriteColored("\nWARNING: THIS WILL PERMANENTLY DELETE ALL CACHED *.MANIFEST FILES IN THIS FOLDER!", ConsoleColor.Yellow);
                        Console.Write("\nAre you sure you want to clear the manifest cache? (y/N): ");
                        string? input = Console.ReadLine()?.Trim().ToLower();
                        if (input == "y" || input == "yes")
                        {
                            try
                            {
                                int deletedCount = 0;
                                if (Directory.Exists(currentDir))
                                {
                                    var di = new DirectoryInfo(currentDir);
                                    foreach (var file in di.GetFiles("*.manifest"))
                                    {
                                        file.Delete();
                                        deletedCount++;
                                    }
                                }
                                WriteColored($"\n[Success] Cleaned manifest cache! Deleted {deletedCount} file(s).", ConsoleColor.Green);
                            }
                            catch (Exception ex)
                            {
                                WriteColored($"[Error] Could not clear manifest files: {ex.Message}", ConsoleColor.Red);
                            }
                            Console.WriteLine("\nPress any key to return.");
                            Console.ReadKey();
                        }
                        else
                        {
                            WriteColored("\nAction cancelled.", ConsoleColor.Gray);
                            Console.WriteLine("\nPress any key to return.");
                            Console.ReadKey();
                        }
                    }
                    else if (selectedIndex == 5) // Back
                    {
                        return;
                    }
                }
            }
        }

        private static void RunScanCachedManifestDetails(TuiSession session)
        {
            Console.Clear();
            WriteColored("=== SCANNING CACHED MANIFEST DETAILS ===", ConsoleColor.Cyan);
            string currentDir = session.ManifestsDir ?? "manifests";
            if (!Directory.Exists(currentDir))
            {
                WriteColored("\nManifest folder does not exist yet on disk.", ConsoleColor.Yellow);
                Console.WriteLine("\nPress any key to return.");
                Console.ReadKey();
                return;
            }

            var files = Directory.GetFiles(currentDir, "*.manifest");
            if (files.Length == 0)
            {
                WriteColored("\nNo manifest files found in cache folder.", ConsoleColor.Yellow);
                Console.WriteLine("\nPress any key to return.");
                Console.ReadKey();
                return;
            }

            Console.WriteLine($"Found {files.Length} cached manifest file(s):\n");

            // Format nicely as a table
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{"Depot ID".PadRight(15)} │ {"Manifest ID".PadRight(22)} │ {"File Size".PadRight(15)} │ {"Filename"}");
            Console.WriteLine(new string('─', 80));
            Console.ResetColor();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var fi = new FileInfo(file);
                var sizeStr = FormatSize(fi.Length);

                string depotId = "Unknown";
                string manifestId = "Unknown";

                var parts = name.Split('_');
                if (parts.Length >= 2)
                {
                    depotId = parts[0];
                    manifestId = parts[1];
                }
                else
                {
                    depotId = name;
                }

                Console.WriteLine($"{depotId.PadRight(15)} │ {manifestId.PadRight(22)} │ {sizeStr.PadRight(15)} │ {Path.GetFileName(file)}");
            }

            Console.WriteLine("\nPress any key to return to Manifest Cache Manager.");
            Console.ReadKey();
        }

        private static void RunZipImportAction(TuiSession session)
        {
            Console.Clear();
            WriteColored("=== IMPORT CONFIG & MANIFESTS FROM ZIP ARCHIVE ===", ConsoleColor.Cyan);
            
            string? zipPath = null;
            bool isWindows = OperatingSystem.IsWindows();
            if (isWindows)
            {
                var options = new List<string>
                {
                    "[Open Windows File Explorer...]",
                    "[Type manual zip path...]",
                    "[Cancel]"
                };
                int selIndex = RunSelector("SELECT ZIP IMPORT METHOD", options);
                if (selIndex == -1 || options[selIndex] == "[Cancel]") return;
                
                if (selIndex == 0)
                {
                    Console.Clear();
                    WriteColored("Opening Windows File Explorer...", ConsoleColor.Cyan);
                    zipPath = DialogHelpers.OpenWindowsFileDialog("Select ZIP Archive Containing Configs/Manifests", "ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*");
                }
            }
            
            if (zipPath == null)
            {
                Console.Clear();
                WriteColored("=== ENTER ZIP FILE PATH ===", ConsoleColor.Cyan);
                Console.Write("\nPath: ");
                var pathInput = ReadLineWithEscape()?.Trim();
                if (pathInput == null)
                {
                    return; // Escape pressed, go back
                }
                if (string.IsNullOrEmpty(pathInput) || !File.Exists(pathInput))
                {
                    WriteColored("[Error] Invalid or non-existent zip file path.", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to return.");
                    Console.ReadKey();
                    return;
                }
                zipPath = pathInput;
            }

            Console.Clear();
            WriteColored($"[Extract] Scanning and extracting from archive: {zipPath}...", ConsoleColor.Cyan);
            
            var result = ZipHelper.ImportZip(zipPath, session.ManifestsDir ?? "manifests");
            
            WriteColored($"\n[Extraction Complete]", ConsoleColor.Green);
            WriteColored($"  - Extracted Lua Configurations: {result.luaCount}", ConsoleColor.Cyan);
            WriteColored($"  - Extracted Steam Manifests:    {result.manifestCount}", ConsoleColor.Cyan);
            
            if (!string.IsNullOrEmpty(result.firstLuaPath))
            {
                session.LuaPath = result.firstLuaPath;
                ParseLuaFileIntoSession(session);
                WriteColored($"\n[Auto-Load] Auto-loaded game configuration: {Path.GetFileName(result.firstLuaPath)}", ConsoleColor.Green);
            }
            
            Console.WriteLine("\nPress any key to return.");
            Console.ReadKey();
        }

        private static void RunChooseLuaAction(TuiSession session)
        {
            var luaFiles = FindLuaFiles();
            var options = new List<string>();
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                options.Add("[Open Windows File Explorer...]");
            }

            options.AddRange(luaFiles);
            options.Add("[Type manual path...]");
            options.Add("[Cancel]");

            int selIndex = RunSelector("SELECT GAME LUA CONFIGURATION FILE", options);
            if (selIndex == -1 || options[selIndex] == "[Cancel]")
            {
                return;
            }

            string? selectedPath = null;
            if (isWindows && options[selIndex] == "[Open Windows File Explorer...]")
            {
                Console.Clear();
                WriteColored("Opening Windows File Explorer...", ConsoleColor.Cyan);
                selectedPath = DialogHelpers.OpenWindowsFileDialog("Select Game Lua Configuration File", "Lua Files (*.lua)|*.lua|All Files (*.*)|*.*");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    WriteColored("No file selected.", ConsoleColor.Yellow);
                    Console.WriteLine("\nPress any key to return.");
                    Console.ReadKey();
                    return;
                }
            }
            else if (options[selIndex] == "[Type manual path...]")
            {
                Console.Clear();
                WriteColored("=== ENTER LUA CONFIG FILE PATH ===", ConsoleColor.Cyan);
                Console.Write("\nPath: ");
                var pathInput = ReadLineWithEscape()?.Trim();
                if (pathInput == null)
                {
                    return; // Escape pressed, go back
                }
                if (string.IsNullOrEmpty(pathInput) || !File.Exists(pathInput))
                {
                    WriteColored("[Error] Invalid or non-existent file path.", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to return.");
                    Console.ReadKey();
                    return;
                }
                selectedPath = pathInput;
            }
            else
            {
                selectedPath = options[selIndex];
            }

            if (!string.IsNullOrEmpty(selectedPath))
            {
                session.LuaPath = selectedPath;
                ParseLuaFileIntoSession(session);
            }
        }

        private static void RunConfigureManifestsFolderAction(TuiSession session)
        {
            var options = new List<string>();
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                options.Add("[Open Windows Folder Selector...]");
            }
            options.Add("[Type manual folder path...]");
            options.Add("[Cancel]");

            int selIndex = RunSelector("CONFIGURE MANIFESTS CACHE FOLDER", options);
            if (selIndex == -1 || options[selIndex] == "[Cancel]")
            {
                return;
            }

            if (isWindows && options[selIndex] == "[Open Windows Folder Selector...]")
            {
                Console.Clear();
                WriteColored("Opening Windows File Explorer...", ConsoleColor.Cyan);
                var selectedPath = DialogHelpers.OpenWindowsFolderDialog("Select Manifests Folder");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    WriteColored("No folder selected.", ConsoleColor.Yellow);
                    Console.WriteLine("\nPress any key to return.");
                    Console.ReadKey();
                    return;
                }
                session.ManifestsDir = selectedPath;
            }
            else if (options[selIndex] == "[Type manual folder path...]")
            {
                Console.Clear();
                WriteColored("=== CONFIGURE MANIFESTS FOLDER ===", ConsoleColor.Cyan);
                var selectedPath = PromptText("Folder containing local *.manifest files:", session.ManifestsDir ?? "manifests");
                if (selectedPath == null)
                {
                    return; // Escape pressed, go back
                }
                session.ManifestsDir = selectedPath;
            }
        }

        private static void RunImportIndividualManifestFilesAction(TuiSession session)
        {
            if (!OperatingSystem.IsWindows())
            {
                WriteColored("[Error] Individual manifest selection requires Windows.", ConsoleColor.Red);
                Console.ReadKey();
                return;
            }

            Console.Clear();
            WriteColored("Opening Windows File Explorer (Multi-Select)...", ConsoleColor.Cyan);
            var selectedFiles = DialogHelpers.OpenWindowsMultiFileDialog("Select Manifest Files", "Manifest Files (*.manifest)|*.manifest|All Files (*.*)|*.*");
            if (selectedFiles.Count == 0)
            {
                WriteColored("No manifest files selected.", ConsoleColor.Yellow);
                Console.WriteLine("\nPress any key to return.");
                Console.ReadKey();
                return;
            }

            var targetDir = session.ManifestsDir ?? "manifests";
            Directory.CreateDirectory(targetDir);

            int copiedCount = 0;
            foreach (var file in selectedFiles)
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                try
                {
                    File.Copy(file, dest, overwrite: true);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    WriteColored($"Failed to copy {Path.GetFileName(file)}: {ex.Message}", ConsoleColor.Red);
                }
            }

            WriteColored($"\nSuccessfully imported {copiedCount} manifest files into '{targetDir}'!", ConsoleColor.Green);
            Console.WriteLine("\nPress any key to return.");
            Console.ReadKey();
        }

        private static void RunConfigureOutputAction(TuiSession session)
        {
            var options = new List<string>();
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                options.Add("[Open Windows Folder Selector...]");
            }
            options.Add("[Type manual path...]");
            options.Add("[Cancel]");

            int selIndex = RunSelector("CONFIGURE OUTPUT DOWNLOAD FOLDER", options);
            if (selIndex == -1 || options[selIndex] == "[Cancel]")
            {
                return;
            }

            string? selectedPath = null;
            if (isWindows && options[selIndex] == "[Open Windows Folder Selector...]")
            {
                Console.Clear();
                WriteColored("Opening Windows File Explorer...", ConsoleColor.Cyan);
                selectedPath = DialogHelpers.OpenWindowsFolderDialog("Select Output Download Folder");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    WriteColored("No folder selected.", ConsoleColor.Yellow);
                    Console.WriteLine("\nPress any key to return.");
                    Console.ReadKey();
                    return;
                }
            }
            else
            {
                Console.Clear();
                WriteColored("=== CONFIGURE OUTPUT FOLDER ===", ConsoleColor.Cyan);
                string defaultOut = string.IsNullOrEmpty(session.OutputDir) 
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", GetGameName(session)) 
                    : session.OutputDir;
                selectedPath = PromptText("Output folder for downloaded game files:", defaultOut);
                if (selectedPath == null)
                {
                    return; // Escape pressed, go back
                }
            }

            if (!string.IsNullOrEmpty(selectedPath))
            {
                string gameName = GetGameName(session);
                string lastFolder = Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.Equals(lastFolder, gameName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedPath = Path.Combine(selectedPath, gameName);
                }
                session.OutputDir = selectedPath;
            }
        }

        private static void DrawHeaderAndBox(TuiSession session)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                 DepotDL - INTERACTIVE TUI DASHBOARD CONTROL                  ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            Console.ResetColor();

            void DrawRow(string label, string val, ConsoleColor valColor)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(label.PadRight(22));
                Console.ForegroundColor = valColor;
                
                string displayVal = val;
                if (displayVal.Length > 52)
                {
                    displayVal = displayVal.Substring(0, 49) + "...";
                }
                Console.Write(displayVal.PadRight(52));
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(" ║");
            }

            string luaName = string.IsNullOrEmpty(session.LuaPath) ? "[None loaded - Select Lua configuration]" : Path.GetFileName(session.LuaPath);
            DrawRow("Active Lua File:", luaName, string.IsNullOrEmpty(session.LuaPath) ? ConsoleColor.Yellow : ConsoleColor.White);
            
            string appId = string.IsNullOrEmpty(session.AppId) ? "[None]" : session.AppId;
            DrawRow("App ID Target:", appId, string.IsNullOrEmpty(session.AppId) ? ConsoleColor.DarkGray : ConsoleColor.Green);

            string depotSel = session.AllDepots.Count == 0 
                ? "0 depots (Load Lua first)" 
                : $"{session.SelectedDepots.Count} of {session.AllDepots.Count} selected";
            DrawRow("Selected Depots:", depotSel, session.SelectedDepots.Count == 0 ? ConsoleColor.DarkGray : ConsoleColor.White);

            string manifestsVal = string.IsNullOrEmpty(session.ManifestsDir) ? "[Default]" : Path.GetFullPath(session.ManifestsDir);
            DrawRow("Manifests Cache:", manifestsVal, ConsoleColor.Gray);

            string outputVal = string.IsNullOrEmpty(session.OutputDir) ? "[Set automatically on download]" : Path.GetFullPath(session.OutputDir);
            DrawRow("Download Folder:", outputVal, string.IsNullOrEmpty(session.OutputDir) ? ConsoleColor.DarkGray : ConsoleColor.Gray);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        private static void DrawFooter()
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("\n════════════════════════════════════════════════════════════════════════════════");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("  [↑/↓] Navigate   ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("│");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("   [Enter] Select   ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("│");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("   [Space] Toggle   ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("│");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("   [Esc] Cancel / Back");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("════════════════════════════════════════════════════════════════════════════════");
            Console.ResetColor();
        }

        private static void ParseLuaFileIntoSession(TuiSession session)
        {
            if (string.IsNullOrEmpty(session.LuaPath) || !File.Exists(session.LuaPath)) return;

            string luaContent = File.ReadAllText(session.LuaPath);

            string appId;
            var parsedDepots = LibraryManager.ParseLuaConfig(luaContent, out appId);

            if (string.IsNullOrEmpty(appId)) return;
            session.AppId = appId;

            session.AllDepots = new List<DepotInfo>(parsedDepots.Values);
            session.SelectedDepots = new List<DepotInfo>(session.AllDepots); // Default select all

            string gameName = Path.GetFileNameWithoutExtension(session.LuaPath);
            if (string.IsNullOrEmpty(gameName)) gameName = $"App_{appId}";

            if (string.IsNullOrEmpty(session.OutputDir) || 
                session.OutputDir.Contains("App_Game") || 
                session.OutputDir.Contains("App_") || 
                session.OutputDir.EndsWith("downloads", StringComparison.OrdinalIgnoreCase))
            {
                session.OutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", gameName);
            }
        }

        private static int RunSelector(string prompt, List<string> options)
        {
            int index = 0;
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"=== {prompt} ===");
                Console.ResetColor();
                Console.WriteLine("Use Up/Down Arrow keys to navigate, Enter to select, Escape to cancel.\n");

                for (int i = 0; i < options.Count; i++)
                {
                    if (i == index)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {options[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"   {options[i]}");
                    }
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    index = (index - 1 + options.Count) % options.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    index = (index + 1) % options.Count;
                }
                else if (key == ConsoleKey.Enter)
                {
                    return index;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return -1;
                }
            }
        }

        private static List<DepotInfo> RunCheckboxSelector(string prompt, List<DepotInfo> options, List<DepotInfo> currentlySelected)
        {
            int index = 0;
            var selected = new bool[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                selected[i] = currentlySelected.Exists(d => d.DepotId == options[i].DepotId);
            }

            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"=== {prompt} ===");
                Console.ResetColor();
                Console.WriteLine("Use Up/Down Arrow keys to navigate, Space to toggle, Enter to confirm, Escape to cancel.\n");

                for (int i = 0; i < options.Count; i++)
                {
                    var isCurrent = i == index;
                    var isChecked = selected[i];
                    var checkbox = isChecked ? "[✔]" : "[ ]";

                    if (isCurrent)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {checkbox} Depot {options[i].DepotId} (Manifest: {options[i].ManifestId})");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (isChecked)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   {checkbox} Depot {options[i].DepotId} (Manifest: {options[i].ManifestId})");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine($"   {checkbox} Depot {options[i].DepotId} (Manifest: {options[i].ManifestId})");
                        }
                        Console.ResetColor();
                    }
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    index = (index - 1 + options.Count) % options.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    index = (index + 1) % options.Count;
                }
                else if (key == ConsoleKey.Spacebar)
                {
                    selected[index] = !selected[index];
                }
                else if (key == ConsoleKey.Enter)
                {
                    var result = new List<DepotInfo>();
                    for (int i = 0; i < options.Count; i++)
                    {
                        if (selected[i]) result.Add(options[i]);
                    }
                    return result;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return currentlySelected;
                }
            }
        }

        private static List<string> FindLuaFiles()
        {
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.GetFiles(".", "*.lua", SearchOption.TopDirectoryOnly));
                foreach (var sub in Directory.GetDirectories("."))
                {
                    var name = Path.GetFileName(sub).ToLower();
                    if (name.StartsWith(".") || name == "bin" || name == "obj" || name == "dist" || name == "node_modules" || name == "venv" || name == ".git")
                        continue;

                    files.AddRange(Directory.GetFiles(sub, "*.lua", SearchOption.TopDirectoryOnly));
                }
            }
            catch { }
            return files;
        }

        private static string? PromptText(string prompt, string defaultValue)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{prompt} ");
            Console.ResetColor();
            if (!string.IsNullOrEmpty(defaultValue))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[{defaultValue}] ");
                Console.ResetColor();
            }

            var input = ReadLineWithEscape();
            if (input == null) return null; // Escape pressed

            return string.IsNullOrEmpty(input) ? defaultValue : input.Trim();
        }

        private static string? ReadLineWithEscape()
        {
            var builder = new System.Text.StringBuilder();
            while (true)
            {
                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return builder.ToString();
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    return null; // Cancel signal
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (builder.Length > 0)
                    {
                        builder.Remove(builder.Length - 1, 1);
                        if (Console.CursorLeft > 0)
                        {
                            Console.Write("\b \b");
                        }
                    }
                }
                else if (keyInfo.KeyChar >= 32)
                {
                    builder.Append(keyInfo.KeyChar);
                    Console.Write(keyInfo.KeyChar);
                }
            }
        }

        private static string GetGameName(TuiSession session)
        {
            if (!string.IsNullOrEmpty(session.LuaPath))
            {
                string name = Path.GetFileNameWithoutExtension(session.LuaPath);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            if (!string.IsNullOrEmpty(session.AppId))
            {
                return $"App_{session.AppId}";
            }
            return "Game";
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            var orig = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = orig;
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int order = 0;
            while (val >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                val /= 1024;
            }
            return $"{val:F2} {suffixes[order]}";
        }
    }
}
