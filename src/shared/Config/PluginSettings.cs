using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Plugin-side settings at %LOCALAPPDATA%\Bimwright\Dwg\settings.json.
    /// Env var overlays the file value at load time, same posture as rvt-mcp:
    /// the MCPTOAST command writes the file; BIMWRIGHT_ENABLE_TOAST re-wins
    /// at next launch while it is set.
    /// </summary>
    public static class PluginSettings
    {
        public const string EnvEnableToast = "BIMWRIGHT_ENABLE_TOAST";
        public const bool DefaultEnableToast = true;

        /// <summary>Test hook: redirect the settings file to a fixture path.</summary>
        internal static string FilePathOverride { get; set; }

        public static string DefaultFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "Dwg", "settings.json");

        private static string FilePath =>
            string.IsNullOrWhiteSpace(FilePathOverride) ? DefaultFilePath : FilePathOverride;

        public static bool LoadToastEnabled()
        {
            var env = ParseBool(Environment.GetEnvironmentVariable(EnvEnableToast));
            if (env.HasValue)
                return env.Value;

            var file = ReadEnableToast(FilePath);
            return file ?? DefaultEnableToast;
        }

        /// <summary>Persist only enableToast into the JSON file, preserving other keys.</summary>
        public static void SaveEnableToast(bool enabled)
        {
            var path = FilePath;
            try
            {
                JObject root = null;
                if (File.Exists(path))
                {
                    try { root = JObject.Parse(File.ReadAllText(path)); }
                    catch { root = null; }
                }
                root = root ?? new JObject();
                root["enableToast"] = enabled;

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                PluginLog.Debug("PluginSettings.SaveEnableToast failed: " + ex.Message);
            }
        }

        internal static bool? ReadEnableToast(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;
                return JObject.Parse(File.ReadAllText(path))["enableToast"]?.Value<bool?>();
            }
            catch
            {
                return null;
            }
        }

        internal static bool? ParseBool(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    return null;
            }
        }
    }
}
