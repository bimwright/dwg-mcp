using System;
using System.IO;
using Bimwright.Dwg.Plugin.ToolBaker;

namespace Bimwright.Dwg.Server.Bake
{
    public sealed class BakePaths
    {
        public BakePaths()
            : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
        {
        }

        public BakePaths(string localApplicationData)
        {
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new ArgumentException("Local application data path is required.", nameof(localApplicationData));
            }

            Root = Path.Combine(localApplicationData, "Bimwright", "Dwg");
            BakedRoot = Path.Combine(Root, "baked");
            UsageJsonl = Path.Combine(BakedRoot, "usage.jsonl");
            BakeDb = Path.Combine(BakedRoot, "bake.db");
            AuditJsonl = Path.Combine(BakedRoot, "bake-audit.jsonl");
        }

        public string Root { get; }
        public string BakedRoot { get; }
        public string UsageJsonl { get; }
        public string BakeDb { get; }
        public string AuditJsonl { get; }
    }
}
