using System;
using System.IO;
using System.IO.Compression;

namespace DepotDL.CLI
{
    public static class ZipHelper
    {
        public static (int luaCount, int manifestCount, string? firstLuaPath) ImportZip(string zipPath, string manifestsDir)
        {
            int luaCount = 0;
            int manifestCount = 0;
            string? firstLuaPath = null;

            try
            {
                if (!File.Exists(zipPath)) return (0, 0, null);

                // Ensure manifests directory exists
                if (string.IsNullOrEmpty(manifestsDir))
                {
                    manifestsDir = "manifests";
                }
                Directory.CreateDirectory(manifestsDir);

                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        var ext = Path.GetExtension(entry.FullName).ToLower();
                        if (ext == ".lua")
                        {
                            // Extract to current directory
                            var fileName = Path.GetFileName(entry.FullName);
                            if (string.IsNullOrEmpty(fileName)) continue;

                            var targetPath = Path.Combine(".", fileName);
                            
                            entry.ExtractToFile(targetPath, overwrite: true);
                            
                            luaCount++;
                            if (firstLuaPath == null)
                            {
                                firstLuaPath = Path.GetFullPath(targetPath);
                            }
                        }
                        else if (ext == ".manifest")
                        {
                            // Extract to manifests directory
                            var fileName = Path.GetFileName(entry.FullName);
                            if (string.IsNullOrEmpty(fileName)) continue;

                            var targetPath = Path.Combine(manifestsDir, fileName);
                            entry.ExtractToFile(targetPath, overwrite: true);
                            manifestCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error during ZIP extraction] {ex.Message}");
            }

            return (luaCount, manifestCount, firstLuaPath);
        }
    }
}
