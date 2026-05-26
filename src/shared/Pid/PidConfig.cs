using System;

namespace Bimwright.Dwg.Pid
{
    public class PidConfig
    {
        public const string EnvPidLibraryPath = "BIMWRIGHT_DWG_PID_LIBRARY_PATH";
        public const string EnvPidSymbolMode = "BIMWRIGHT_DWG_PID_SYMBOL_MODE";
        public const string EnvPidFallback = "BIMWRIGHT_DWG_PID_FALLBACK";

        public string LibraryPath { get; set; }
        public string SymbolMode { get; set; } = "procedural";
        public bool Fallback { get; set; } = true;

        public bool UseProcedural =>
            string.Equals(SymbolMode, "procedural", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SymbolMode, "auto", StringComparison.OrdinalIgnoreCase);

        public static PidConfig Load(Func<string, string> envLookup = null)
        {
            envLookup = envLookup ?? Environment.GetEnvironmentVariable;

            var config = new PidConfig();

            var libPath = envLookup(EnvPidLibraryPath);
            if (!string.IsNullOrWhiteSpace(libPath))
            {
                config.LibraryPath = libPath.Trim();
            }

            var mode = envLookup(EnvPidSymbolMode);
            if (!string.IsNullOrWhiteSpace(mode))
            {
                config.SymbolMode = mode.Trim().ToLowerInvariant();
            }

            var fallbackStr = envLookup(EnvPidFallback);
            if (!string.IsNullOrWhiteSpace(fallbackStr))
            {
                if (bool.TryParse(fallbackStr.Trim(), out var parsedFallback))
                {
                    config.Fallback = parsedFallback;
                }
                else if (fallbackStr.Trim() == "1")
                {
                    config.Fallback = true;
                }
                else if (fallbackStr.Trim() == "0")
                {
                    config.Fallback = false;
                }
            }

            return config;
        }

        public void Validate()
        {
            if (string.Equals(SymbolMode, "external", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "External symbol library loading is deferred/unsupported in the current version. " +
                    "Please set BIMWRIGHT_DWG_PID_SYMBOL_MODE to 'procedural' or 'auto'.");
            }
        }
    }
}
