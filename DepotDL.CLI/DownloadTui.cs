using System;
using System.IO;

namespace DepotDL.CLI
{
    internal static class DownloadTui
    {
        public static void WriteHeader(string appId, int depotCount, string outputPath)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╔════════════════════════════════════════╦═════════════════════════════════════╗");
            Console.Write("║ ");
            WriteColor("DepotDL Download Queue".PadRight(38), ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ║ ");
            WriteColor($"APP {appId}".PadRight(35), ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine("╠════════════════════════════════════════╩═════════════════════════════════════╣");
            WriteInfoRow("Selected Depots", depotCount.ToString(), ConsoleColor.White);
            WriteInfoRow("Output Folder", ShortenPath(outputPath, 58), ConsoleColor.Gray);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void WriteSetup(string label, string value, ConsoleColor color)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  │ ");
            WriteColor(label.PadRight(16), ConsoleColor.DarkCyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(value, color);
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void WriteDepotHeader(string depotId, int index, int total, string? manifestId)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.Write("║ ");
            WriteColor($"Depot {depotId}".PadRight(24), ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor($"Queue {index}/{total}".PadRight(16), ConsoleColor.White);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(ShortenValue(string.IsNullOrEmpty(manifestId) ? "Latest manifest" : manifestId, 28).PadRight(28), ConsoleColor.Gray);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        public static void WriteStatus(string label, string message, ConsoleColor color)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  │ ");
            WriteColor(label.PadRight(12), color);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(message, ConsoleColor.Gray);
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void DrawProgress(double percentage, string? activeValidationFile, ref int lastLineLength)
        {
            int barWidth = 34;
            int filledWidth = (int)Math.Round(percentage / 100.0 * barWidth);
            if (filledWidth < 0) filledWidth = 0;
            if (filledWidth > barWidth) filledWidth = barWidth;

            string filledBar = new string('█', filledWidth);
            string emptyBar = new string('░', barWidth - filledWidth);
            string validation = string.IsNullOrEmpty(activeValidationFile)
                ? string.Empty
                : $"  validating {ShortenValue(activeValidationFile, 24)}";
            const string progressPrefix = "\r  │ Progress     │ ";
            string progressBody = $"{percentage,5:F1}% [{filledBar}{emptyBar}]{validation}";
            string progressText = progressPrefix + progressBody;

            int maxLen = 110;
            try { maxLen = Console.WindowWidth - 1; } catch { }
            if (progressText.Length > maxLen && maxLen > 10)
            {
                progressText = progressText.Substring(0, maxLen - 3) + "...";
            }

            int currentLength = progressText.Length - 1;
            if (currentLength < lastLineLength)
            {
                progressText += new string(' ', lastLineLength - currentLength);
            }
            lastLineLength = currentLength;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(progressPrefix);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(progressText[progressPrefix.Length..]);
            Console.ResetColor();
        }

        public static void ClearProgress(ref int lastLineLength)
        {
            if (lastLineLength > 0)
            {
                Console.Write("\r" + new string(' ', lastLineLength) + "\r");
                lastLineLength = 0;
            }
        }

        public static void WriteFinal(bool success)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.Write("║ ");
            WriteColor((success ? "Download actions completed" : "Download actions finished with errors").PadRight(76), success ? ConsoleColor.Green : ConsoleColor.Red);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        private static void WriteInfoRow(string key, string value, ConsoleColor valueColor)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("║ ");
            WriteColor(key.PadRight(18), ConsoleColor.DarkCyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(value.PadRight(55), valueColor);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
        }

        private static void WriteColor(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }

        private static string ShortenPath(string path, int maxLength)
        {
            if (path.Length <= maxLength) return path;
            string fileName = Path.GetFileName(path);
            if (fileName.Length + 4 >= maxLength) return "..." + path[^Math.Max(0, maxLength - 3)..];
            return "..." + Path.DirectorySeparatorChar + fileName;
        }

        private static string ShortenValue(string value, int maxLength)
        {
            if (value.Length <= maxLength) return value;
            return value[..Math.Max(0, maxLength - 3)] + "...";
        }
    }
}
