using System;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Owner/dev switch: BIMWRIGHT_KEEP_ALL_LOGS=1|true|yes|on skips every automatic deletion of logs,
    /// call records, journals, reports and generated captures. It never enables a recorder or changes what is recorded.
    /// </summary>
    public static class LogRetentionPolicy
    {
        public const string EnvironmentVariable = "BIMWRIGHT_KEEP_ALL_LOGS";

        /// <summary>Test seam: a non-null value replaces the environment for the whole process.</summary>
        internal static bool? KeepAllOverride { get; set; }

        public static bool KeepAll => KeepAllOverride ?? IsTruthy(Environment.GetEnvironmentVariable(EnvironmentVariable));

        internal static bool IsTruthy(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "on": return true;
                default: return false;
            }
        }
    }
}
