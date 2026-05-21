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

            return ProcessDownload(luaPath, manifestsDir, outputPath, ddmodPath, dotnetPath);
        }

        public static int TriggerDownloadProcess(string luaPath, string? manifestsDir, string? outputPath, string ddmodPath, string dotnetPath, List<DepotInfo>? selectedDepots)
        {
            return ProcessDownload(luaPath, manifestsDir, outputPath, ddmodPath, dotnetPath, selectedDepots);
        }

        private static int ProcessDownload(string luaPath, string? manifestsDir, string? outputPath, string ddmodPath, string dotnetPath, List<DepotInfo>? selectedDepots = null)
        {
            void LogInfo(string message) => WriteColored(message, ConsoleColor.Cyan);
            void LogSuccess(string message) => WriteColored(message, ConsoleColor.Green);
            void LogWarning(string message) => WriteColored(message, ConsoleColor.Yellow);
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
                            var finalDepot = new DepotInfo { DepotId = sel.DepotId };
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

                if (depots.Count == 0)
                {
                    LogError("[Error] No depot decryption keys or manifest IDs found.");
                    return 1;
                }

                var manifestFiles = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(manifestsDir) && Directory.Exists(manifestsDir))
                {
                    LogInfo($"[Scan] Scanning manifest files in: {manifestsDir}...");
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
                LogInfo($"[Temp] Wrote temporary keys file: {_tempKeysPath}");

                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", $"App_{appId}");
                }
                outputPath = Path.GetFullPath(outputPath);
                Directory.CreateDirectory(outputPath);

                int successfulDepots = 0;
                int totalDepots = depots.Count;

                foreach (var depot in depots.Values)
                {
                    _lastLineLength = 0; // Reset length for the new depot progress bar
                    _lastPercentage = 0;
                    _activeValidationFile = null;
                    LogInfo($"\n--------------------------------------------------");
                    LogInfo($"Downloading Depot {depot.DepotId} ({++successfulDepots}/{totalDepots})...");
                    LogInfo($"--------------------------------------------------");

                    var argsList = new List<string>
                    {
                        $"\"{ddmodPath}\"",
                        "-app", appId,
                        "-depot", depot.DepotId,
                        "-depotkeys", $"\"{_tempKeysPath}\"",
                        "-max-downloads", "32",
                        "-os", "windows",
                        "-validate",
                        "-dir", $"\"{outputPath}\""
                    };

                    if (!string.IsNullOrEmpty(depot.ManifestId))
                    {
                        argsList.Add("-manifest");
                        argsList.Add(depot.ManifestId);

                        var keyCombo = $"{depot.DepotId}_{depot.ManifestId}";
                        if (manifestFiles.TryGetValue(keyCombo, out var manifestPath))
                        {
                            argsList.Add("-manifestfile");
                            argsList.Add($"\"{manifestPath}\"");
                            LogSuccess($"[Manifest] Found matching local manifest: {Path.GetFileName(manifestPath)}");
                        }
                        else if (manifestFiles.TryGetValue(depot.ManifestId, out var manifestPathById))
                        {
                            argsList.Add("-manifestfile");
                            argsList.Add($"\"{manifestPathById}\"");
                            LogSuccess($"[Manifest] Found manifest by ID: {Path.GetFileName(manifestPathById)}");
                        }
                        else
                        {
                            LogWarning($"[Manifest] No local .manifest file matched. Will attempt downing it.");
                        }
                    }

                    var processArgs = string.Join(" ", argsList);

                    var psi = new ProcessStartInfo
                    {
                        FileName = dotnetPath,
                        Arguments = processArgs,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(ddmodPath) ?? AppDomain.CurrentDomain.BaseDirectory
                    };

                    using (var process = new Process { StartInfo = psi })
                    {
                        process.OutputDataReceived += (sender, lineEventArgs) =>
                        {
                            ProcessProgressLine(lineEventArgs.Data);
                        };
                        process.ErrorDataReceived += (sender, lineEventArgs) =>
                        {
                            if (!string.IsNullOrEmpty(lineEventArgs.Data))
                            {
                                ClearCurrentConsoleLine();
                                LogError(lineEventArgs.Data);
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        ClearCurrentConsoleLine();

                        if (process.ExitCode != 0)
                        {
                            LogError($"[Error] DepotDownloaderMod exited with error code: {process.ExitCode}");
                        }
                        else
                        {
                            LogSuccess($"[Success] Depot {depot.DepotId} completed successfully.");
                        }
                    }
                }

                LogSuccess("\n==================================================");
                LogSuccess("            All Download Actions Done!           ");
                LogSuccess("==================================================");
                
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
                    LogSuccess($"[Library] Registered '{gameName}' in the local library index.");
                }
                catch (Exception ex)
                {
                    LogWarning($"[Library] Failed to update library index: {ex.Message}");
                }

                if (selectedDepots != null)
                {
                    Console.WriteLine("\nPress any key to return to system.");
                    Console.ReadKey();
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
                // Check for verbose/repetitive lines to filter out
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

                // Check if it's a validation line
                if (line.StartsWith("Validating ", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract file name
                    _activeValidationFile = Path.GetFileName(line.Substring(11).Trim());
                    DrawProgressBar(_lastPercentage);
                    return;
                }

                // Look for progress percentage supporting both comma and dot decimal separators
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
                    // Clear any residual progress bar characters before printing a normal output line
                    ClearCurrentConsoleLine();
                    
                    // Exclude some spammy Pre-allocating logs
                    if (line.StartsWith("Pre-allocating")) return;

                    Console.WriteLine(line);
                }
            }
            catch
            {
                // Fallback to printing the line normally if anything fails
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
                int barWidth = 30;
                int filledWidth = (int)Math.Round(percentage / 100.0 * barWidth);
                if (filledWidth < 0) filledWidth = 0;
                if (filledWidth > barWidth) filledWidth = barWidth;

                string filledBar = new string('█', filledWidth);
                string emptyBar = new string('░', barWidth - filledWidth);

                string statusPart = "";
                if (!string.IsNullOrEmpty(_activeValidationFile))
                {
                    statusPart = $" - Validating: {_activeValidationFile}";
                }

                string progressText = $"\rProgress: [{filledBar}{emptyBar}] {percentage:F1}%{statusPart}";

                // Prevent progressText from getting too long and wrapping on small terminals safely
                int maxLen = 110;
                try { maxLen = Console.WindowWidth - 1; } catch { }
                if (progressText.Length > maxLen && maxLen > 10)
                {
                    progressText = progressText.Substring(0, maxLen - 3) + "...";
                }

                int currentLength = progressText.Length - 1; // subtract 1 for \r
                if (currentLength < _lastLineLength)
                {
                    progressText += new string(' ', _lastLineLength - currentLength);
                }
                _lastLineLength = currentLength;

                Console.Write(progressText);
            }
            catch
            {
                // Simple fallback on error
                Console.Write($"\rProgress: {percentage:F1}%");
            }
        }

        private static void ClearCurrentConsoleLine()
        {
            if (_lastLineLength > 0)
            {
                Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
                _lastLineLength = 0;
            }
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
            Console.WriteLine("  -h, --help                  Show this usage help screen.");
            Console.WriteLine("\nNote: Launching DepotDL.CLI without arguments opens the interactive TUI dashboard mode.");
            Console.ForegroundColor = orig;
        }
    }
}
