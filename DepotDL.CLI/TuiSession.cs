using System.Collections.Generic;

namespace DepotDL.CLI
{
    public class DepotInfo
    {
        public string DepotId { get; set; } = string.Empty;
        public string DecryptionKey { get; set; } = string.Empty;
        public string ManifestId { get; set; } = string.Empty;
    }

    public class TuiSession
    {
        public string? LuaPath { get; set; }
        public string? AppId { get; set; }
        public string? ManifestsDir { get; set; }
        public string? OutputDir { get; set; }
        public List<DepotInfo> AllDepots { get; set; } = new();
        public List<DepotInfo> SelectedDepots { get; set; } = new();
    }
}
