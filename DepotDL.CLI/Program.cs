using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace DepotDL.CLI
{
    public class Program
    {
        private static string? _tempKeysPath;
        private static int _lastLineLength;
        private static string? _activeValidationFile;
        private static double _lastPercentage;

        static int Main(string[] args)
        {
            Console.Title = "DepotDL.CLI - Steam Depot Downloader Orchestrator";

            if (args.Length == 0)
            {
                // Resolve runtimes
                var dotnetPath = DialogHelpers.ResolveDotnetPath(null);
                var ddmodPath = DialogHelpers.ResolveDDModPath(null);

                if (dotnetPath == null)
                {
                    Console.Clear();
                    WriteColored("[Error] .NET 9 SDK or Runtime could not be found.", ConsoleColor.Red);
                    WriteColored("Please install the .NET 9 runtime to run this tool.", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey();
                    return 1;
                }

                if (ddmodPath == null || !File.Exists(ddmodPath))
                {
                    Console.Clear();
                    WriteColored("[Error] DepotDownloaderMod.dll not found in default search locations.", ConsoleColor.Red);
                    WriteColored("Please ensure DepotDownloaderMod.dll is in SFF/third_party/DDMod/ or adjacent to this tool.", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey();
                    return 1;
                }

                return TuiDashboard.RunInteractiveTui(ddmodPath, dotnetPath);
            }

            // Standard CLI Mode
            return RunCliMode(args);
        }

        private static int RunCliMode(string[] args)
        {
            void LogError(string message) => WriteColored(message, ConsoleColor.Red);

            string? luaPath = null;
            string? manifestsDir = null;
            string? outputPath = null;
            string? ddmodPath = null;
            string? dotnetPath = null;
            int maxDownloads = DepotDownloadDefaults.MaxDownloads;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--lua":
                    case "-l":
                        if (i + 1 < args.Length) luaPath = args[++i];
                        break;
                    case "--manifests-dir":
                    case "-m":
                        if (i + 1 < args.Length) manifestsDir = args[++i];
                        break;
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length) outputPath = args[++i];
                        break;
                    case "--ddmod":
                    case "-d":
                        if (i + 1 < args.Length) ddmodPath = args[++i];
                        break;
                    case "--dotnet":
                    case "-n":
                        if (i + 1 < args.Length) dotnetPath = args[++i];
                        break;
                    case "--max-downloads":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedMaxDownloads))
                        {
                            maxDownloads = DepotDownloadDefaults.NormalizeMaxDownloads(parsedMaxDownloads);
                        }
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                }
            }

            if (showHelp || string.IsNullOrEmpty(luaPath))
            {
                PrintUsage();
                return showHelp ? 0 : 1;
            }

            if (!File.Exists(luaPath))
            {
                LogError($"[Error] Lua file not found: {luaPath}");
                return 1;
            }

            // Resolve runtimes and libraries
            dotnetPath = DialogHelpers.ResolveDotnetPath(dotnetPath);
            if (dotnetPath == null)
            {
                LogError("[Error] .NET 9 SDK or Runtime could not be found.");
                LogError("Please install .NET 9 or specify the path to dotnet executable using '--dotnet <path>'.");
                return 1;
            }

            ddmodPath = DialogHelpers.ResolveDDModPath(ddmodPath);
            if (ddmodPath == null || !File.Exists(ddmodPath))
            {
                LogError($"[Error] DepotDownloaderMod.dll not found.");
                LogError("Please specify the path to DepotDownloaderMod.dll using '--ddmod <path>'.");
                return 1;
            }

            return ProcessDownload(luaPath, manifestsDir, outputPath, ddmodPath, dotnetPath, maxDownloads: maxDownloads);
        }

        public static int TriggerDownloadProcess(string luaPath, string? manifestsDir, string? outputPath, string ddmodPath, string dotnetPath, List<DepotInfo>? selectedDepots)
        {
            return ProcessDownload(luaPath, manifestsDir, outputPath, ddmodPath, dotnetPath, selectedDepots);
        }

        private static int ProcessDownload(string luaPath, string? manifestsDir, string? outputPath, string ddmodPath, string dotnetPath, List<DepotInfo>? selectedDepots = null, int maxDownloads = DepotDownloadDefaults.MaxDownloads)
        {
            void LogError(string message) => WriteColored(message, ConsoleColor.Red);

            // Register console cancel handlers for safe VDF cleanup
            AppDomain.CurrentDomain.ProcessExit += (s, e) => SafeCleanupKeys();
            Console.CancelKeyPress += (s, e) => SafeCleanupKeys();

            try
            {
                string luaContent = File.ReadAllText(luaPath);

                string appId;
                var allParsedDepots = LibraryManager.ParseLuaConfig(luaContent, out appId);

                if (string.IsNullOrEmpty(appId))
                {
                    LogError("[Error] Could not find Steam AppID in Lua file.");
                    return 1;
                }

                var depots = new Dictionary<string, DepotInfo>();
                if (selectedDepots != null)
                {
                    foreach (var sel in selectedDepots)
                    {
                        if (allParsedDepots.TryGetValue(sel.DepotId, out var parsedDepot))
                        {
                            var finalDepot = new DepotInfo
                            {
                                DepotId = sel.DepotId,
                                Name = !string.IsNullOrEmpty(sel.Name) ? sel.Name : parsedDepot.Name,
                                OsList = !string.IsNullOrEmpty(sel.OsList) ? sel.OsList : parsedDepot.OsList,
                                OsArch = !string.IsNullOrEmpty(sel.OsArch) ? sel.OsArch : parsedDepot.OsArch
                            };
                            finalDepot.DecryptionKey = !string.IsNullOrEmpty(sel.DecryptionKey) ? sel.DecryptionKey : parsedDepot.DecryptionKey;
                            finalDepot.ManifestId = !string.IsNullOrEmpty(sel.ManifestId) ? sel.ManifestId : parsedDepot.ManifestId;
                            depots[sel.DepotId] = finalDepot;
                        }
                        else
                        {
                            depots[sel.DepotId] = sel;
                        }
                    }
                }
                else
                {
                    depots = allParsedDepots;
                }

                depots = LibraryManager.FilterDownloadableDepots(depots, appId);

                if (depots.Count == 0)
                {
                    LogError("[Error] No downloadable depots with decryption keys found.");
                    return 1;
                }

                var manifestFiles = new Dictionary<string, string>();
                string? manifestScanStatus = null;
                if (!string.IsNullOrEmpty(manifestsDir) && Directory.Exists(manifestsDir))
                {
                    var files = Directory.GetFiles(manifestsDir, "*.manifest");
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var parts = name.Split('_');
                        if (parts.Length >= 2)
                        {
                            var depotId = parts[0];
                            var manifestId = parts[1];
                            manifestFiles[$"{depotId}_{manifestId}"] = file;
                        }
                        else
                        {
                            manifestFiles[name] = file;
                        }
                    }
                    manifestScanStatus = $"{files.Length} local manifests in {manifestsDir}";
                }

                var tempKeysContent = new List<string>();
                foreach (var depot in depots.Values)
                {
                    if (!string.IsNullOrEmpty(depot.DecryptionKey))
                    {
                        tempKeysContent.Add($"{depot.DepotId};{depot.DecryptionKey}");
                    }
                }

                _tempKeysPath = Path.Combine(Path.GetTempPath(), $"depotdl_keys_{Guid.NewGuid():N}.vdf");
                File.WriteAllLines(_tempKeysPath, tempKeysContent);

                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", $"App_{appId}");
                }
                outputPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(outputPath);
                DownloadTui.LeftPad = TuiDashboard.GetCenterLeftPad(80);
                DownloadTui.WriteHeader(appId, depots.Count, outputPath);
                if (manifestScanStatus != null)
                {
                    DownloadTui.WriteSetup("Manifest Cache", manifestScanStatus, ConsoleColor.Gray);
                }
                DownloadTui.WriteSetup("Keys File", Path.GetFileName(_tempKeysPath), ConsoleColor.DarkGray);

                int successfulDepots = 0;
                int totalDepots = depots.Count;
                bool hadErrors = false;

                foreach (var depot in depots.Values)
                {
                    _lastLineLength = 0;
                    _lastPercentage = 0;
                    _activeValidationFile = null;
                    DownloadTui.WriteDepotHeader(depot.DepotId, ++successfulDepots, totalDepots, depot.ManifestId);

                    var argsList = new List<string>
                    {
                        ddmodPath,
                        "-app", appId,
                        "-depot", depot.DepotId,
                        "-depotkeys", _tempKeysPath,
                        "-max-downloads", DepotDownloadDefaults.NormalizeMaxDownloads(maxDownloads).ToString(CultureInfo.InvariantCulture),
                        "-os", "windows",
                        "-validate",
                        "-dir", outputPath
                    };

                    if (!string.IsNullOrEmpty(depot.ManifestId))
                    {
                        argsList.Add("-manifest");
                        argsList.Add(depot.ManifestId);

                        var keyCombo = $"{depot.DepotId}_{depot.ManifestId}";
                        if (manifestFiles.TryGetValue(keyCombo, out var manifestPath))
                        {
                            argsList.Add("-manifestfile");
                            argsList.Add(manifestPath);
                            DownloadTui.WriteStatus("Manifest", $"Using local file {Path.GetFileName(manifestPath)}", ConsoleColor.Green);
                        }
                        else if (manifestFiles.TryGetValue(depot.ManifestId, out var manifestPathById))
                        {
                            argsList.Add("-manifestfile");
                            argsList.Add(manifestPathById);
                            DownloadTui.WriteStatus("Manifest", $"Using local file {Path.GetFileName(manifestPathById)}", ConsoleColor.Green);
                        }
                        else
                        {
                            DownloadTui.WriteStatus("Manifest", "No local match; DepotDownloaderMod will fetch it", ConsoleColor.Yellow);
                        }
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = dotnetPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(ddmodPath) ?? AppDomain.CurrentDomain.BaseDirectory
                    };
                    foreach (var arg in argsList)
                    {
                        psi.ArgumentList.Add(arg);
                    }

                    var depotOutputErrors = new List<string>();
                    string? lastOutputLine = null;

                    using (var process = new Process { StartInfo = psi })
                    {
                        process.OutputDataReceived += (sender, lineEventArgs) =>
                        {
                            if (!string.IsNullOrWhiteSpace(lineEventArgs.Data))
                            {
                                lastOutputLine = lineEventArgs.Data;
                            }
                            if (IsDepotDownloadFailure(lineEventArgs.Data))
                            {
                                depotOutputErrors.Add(lineEventArgs.Data!);
                            }
                            ProcessProgressLine(lineEventArgs.Data);
                        };
                        process.ErrorDataReceived += (sender, lineEventArgs) =>
                        {
                            if (!string.IsNullOrEmpty(lineEventArgs.Data))
                            {
                                lastOutputLine = lineEventArgs.Data;
                                if (IsDepotDownloadFailure(lineEventArgs.Data))
                                {
                                    depotOutputErrors.Add(lineEventArgs.Data);
                                }
                                ClearCurrentConsoleLine();
                                LogError(lineEventArgs.Data);
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        ClearCurrentConsoleLine();

                        if (process.ExitCode != 0 || depotOutputErrors.Count > 0)
                        {
                            hadErrors = true;
                            var reason = depotOutputErrors.Count > 0
                                ? depotOutputErrors[depotOutputErrors.Count - 1]
                                : !string.IsNullOrWhiteSpace(lastOutputLine)
                                    ? $"{lastOutputLine} (exit code {process.ExitCode})"
                                : $"DepotDownloaderMod exited with code {process.ExitCode}";
                            DownloadTui.WriteStatus("Failed", reason, ConsoleColor.Red);
                        }
                        else
                        {
                            DownloadTui.WriteStatus("Complete", $"Depot {depot.DepotId} completed successfully", ConsoleColor.Green);
                        }
                    }
                }

                DownloadTui.WriteFinal(!hadErrors);

                if (hadErrors)
                {
                    return 1;
                }

                try
                {
                    string gameName = Path.GetFileNameWithoutExtension(luaPath);
                    if (string.IsNullOrEmpty(gameName)) gameName = $"App_{appId}";

                    var depotIdsList = new List<string>();
                    foreach (var depot in depots.Keys)
                    {
                        depotIdsList.Add(depot);
                    }

                    var libGame = new LibraryGame
                    {
                        GameName = gameName,
                        AppId = appId,
                        LuaPath = Path.GetFullPath(luaPath),
                        OutputDir = outputPath,
                        DepotIds = depotIdsList,
                        InstallDate = DateTime.Now,
                        TotalSizeBytes = LibraryManager.GetDirectorySize(outputPath),
                        IsVerified = true
                    };
                    LibraryManager.AddOrUpdateGame(libGame);
                    DownloadTui.WriteStatus("Library", $"Registered '{gameName}' in the local library index", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    DownloadTui.WriteStatus("Library", $"Failed to update library index: {ex.Message}", ConsoleColor.Yellow);
                }

                return 0;
            }
            catch (Exception ex)
            {
                LogError($"[Fatal Error] An unhandled exception occurred: {ex.Message}");
                LogError(ex.StackTrace ?? "");
                Console.ReadKey();
                return 1;
            }
            finally
            {
                SafeCleanupKeys();
                SafeCleanupDepotDownloaderFolder(outputPath, ddmodPath);
            }
        }

        private static bool IsDepotDownloadFailure(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            return line.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("No valid depot key", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("unable to download", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("missing public subsection or manifest section", StringComparison.OrdinalIgnoreCase);
        }

        private static void SafeCleanupKeys()
        {
            if (!string.IsNullOrEmpty(_tempKeysPath) && File.Exists(_tempKeysPath))
            {
                try
                {
                    File.Delete(_tempKeysPath);
                    _tempKeysPath = null;
                }
                catch { }
            }
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            var orig = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = orig;
        }

        private static void ProcessProgressLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            try
            {
                if (line.StartsWith("Using depot keys from", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("No username given", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Connecting to Steam3", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Logging anonymously into Steam3", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Using Steam3 suggested CellID", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Got AppInfo for", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Using app branch", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Disconnected from Steam", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Processing depot", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Already have manifest", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Manifest ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Downloading depot", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Total downloaded:", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(line, @"^Depot \d+ - Downloaded"))
                {
                    return;
                }

                if (line.StartsWith("Validating ", StringComparison.OrdinalIgnoreCase))
                {
                    _activeValidationFile = Path.GetFileName(line.Substring(11).Trim());
                    DrawProgressBar(_lastPercentage);
                    return;
                }

                var pctMatch = Regex.Match(line, @"(\d+(?:[.,]\d+)?)%");
                if (pctMatch.Success)
                {
                    string pctStr = pctMatch.Groups[1].Value.Replace(',', '.');
                    if (double.TryParse(pctStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double percentage))
                    {
                        _lastPercentage = percentage;
                        DrawProgressBar(percentage);
                    }
                }
                else
                {
                    ClearCurrentConsoleLine();
                    
                    if (line.StartsWith("Pre-allocating")) return;

                    Console.WriteLine(line);
                }
            }
            catch
            {
                try
                {
                    ClearCurrentConsoleLine();
                    Console.WriteLine(line);
                }
                catch { }
            }
        }

        private static void DrawProgressBar(double percentage)
        {
            try
            {
                DownloadTui.DrawProgress(percentage, _activeValidationFile, ref _lastLineLength);
            }
            catch
            {
                Console.Write($"\r  │ Progress     │ {percentage:F1}%");
            }
        }

        private static void ClearCurrentConsoleLine()
        {
            DownloadTui.ClearProgress(ref _lastLineLength);
        }

        private static void SafeCleanupDepotDownloaderFolder(string? outputPath, string ddmodPath)
        {
            var pathsToTry = new List<string>();
            
            if (!string.IsNullOrEmpty(outputPath))
            {
                pathsToTry.Add(Path.Combine(outputPath, ".DepotDownloader"));
            }
            
            pathsToTry.Add(Path.Combine(Directory.GetCurrentDirectory(), ".DepotDownloader"));
            
            string? ddmodDir = Path.GetDirectoryName(ddmodPath);
            if (!string.IsNullOrEmpty(ddmodDir))
            {
                pathsToTry.Add(Path.Combine(ddmodDir, ".DepotDownloader"));
            }
            
            pathsToTry.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".DepotDownloader"));

            foreach (var path in pathsToTry)
            {
                LibraryManager.RobustDeleteDirectory(path);
            }
        }

        private static void PrintUsage()
        {
            var orig = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Usage: DepotDL.CLI --lua <path-to-lua-file> [options]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("\nRequired:");
            Console.WriteLine("  -l, --lua <path>            Path to the game Lua configuration file.");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  -m, --manifests-dir <dir>   Path to folder containing pre-downloaded *.manifest files.");
            Console.WriteLine("  -o, --output <dir>          Path to directory where files will be downloaded (Defaults to './downloads/App_<appid>').");
            Console.WriteLine("  -d, --ddmod <path>          Direct path to DepotDownloaderMod.dll (Auto-resolved if omitted).");
            Console.WriteLine("  -n, --dotnet <path>         Direct path to dotnet executable (Auto-resolved if omitted).");
            Console.WriteLine($"      --max-downloads <n>     Parallel chunk downloads per depot. Default: {DepotDownloadDefaults.MaxDownloads}, max: 128.");
            Console.WriteLine("  -h, --help                  Show this usage help screen.");
            Console.WriteLine("\nNote: Launching DepotDL.CLI without arguments opens the interactive TUI dashboard mode.");
            Console.ForegroundColor = orig;
        }
    }
}
