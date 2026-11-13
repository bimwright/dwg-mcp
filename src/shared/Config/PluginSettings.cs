using System;
using System.IO;
using Bimwright.Dwg.Plugin.Localization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Plugin-side settings at %LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json.
    /// Env var overlays the file value at load time:
    /// the MCPTOAST command writes the file; BIMWRIGHT_DWG_ENABLE_TOAST re-wins
    /// at next launch while it is set.
    /// </summary>
    public static partial class PluginSettings
    {
        public const string EnvEnableToast = "BIMWRIGHT_DWG_ENABLE_TOAST";
        public const string EnvAutoStartTransport = "BIMWRIGHT_DWG_AUTO_START_TRANSPORT";
        public const bool DefaultAutoStartTransport = true;
        public const bool DefaultEnableToast = true;
        public const int DefaultToastIdleSeconds = 20;
        public const bool DefaultShowBranding = false;
        public static readonly int[] ToastIdleChoices = { 10, 20, 30, 60 };

        /// <summary>Test hook: redirect the settings file to a fixture path.</summary>
        internal static string FilePathOverride { get; set; }

        public static string DefaultFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "dwg-mcp", "settings.json");

        private static string FilePath =>
            string.IsNullOrWhiteSpace(FilePathOverride) ? DefaultFilePath : FilePathOverride;

        internal static string LoadUiLanguage()
        {
            try
            {
                var value = JObject.Parse(File.ReadAllText(FilePath))["uiLanguage"];
                return LocaleResolver.Resolve(value?.Type == JTokenType.String ? (string)value : null);
            }
            catch { return LocaleResolver.Default; }
        }

        internal static bool SaveUiLanguage(string locale)
            => TryUpdate(root => root["uiLanguage"] = LocaleResolver.Resolve(locale), "SaveUiLanguage");

        public static bool LoadAutoStartTransport(out string source)
        {
            var env = ParseBool(Environment.GetEnvironmentVariable(EnvAutoStartTransport));
            if (env.HasValue)
            {
                source = EnvAutoStartTransport;
                return env.Value;
            }

            try
            {
                var value = JObject.Parse(File.ReadAllText(FilePath))["autoStartTransport"];
                if (value?.Type == JTokenType.Boolean)
                {
                    source = "settings.json";
                    return value.Value<bool>();
                }
            }
            catch { }
            source = "default";
            return DefaultAutoStartTransport;
        }

        public static bool SaveAutoStartTransport(bool enabled, out string error)
        {
            return TryUpdate(root => root["autoStartTransport"] = enabled, "SaveAutoStartTransport", out error);
        }

        public static bool LoadToastEnabled()
        {
            var env = ParseBool(Environment.GetEnvironmentVariable(EnvEnableToast));
            if (env.HasValue)
                return env.Value;

            var file = ReadEnableToast(FilePath);
            return file ?? DefaultEnableToast;
        }

        public static int NormalizeToastIdleSeconds(int seconds)
        {
            for (var i = 0; i < ToastIdleChoices.Length; i++)
            {
                if (ToastIdleChoices[i] == seconds)
                    return seconds;
            }
            return DefaultToastIdleSeconds;
        }

        public static int LoadToastIdleSeconds()
        {
            var file = ReadToastIdleSeconds(FilePath);
            return file.HasValue ? NormalizeToastIdleSeconds(file.Value) : DefaultToastIdleSeconds;
        }

        public static bool LoadShowBranding()
        {
            return ReadShowBranding(FilePath) ?? DefaultShowBranding;
        }

        public static bool LoadReleaseNotifications()
        {
            try
            {
                if (!File.Exists(FilePath)) return true;
                var value = JObject.Parse(File.ReadAllText(FilePath))["releaseNotificationsEnabled"];
                return value?.Type != JTokenType.Boolean || value.Value<bool>();
            }
            catch { return false; }
        }

        public static bool SaveReleaseNotifications(bool enabled)
            => TryUpdate(root => root["releaseNotificationsEnabled"] = enabled, "SaveReleaseNotifications");

        /// <summary>Persist only enableToast into the JSON file, preserving other keys.</summary>
        public static bool SaveEnableToast(bool enabled)
        {
            return TryUpdate(root => root["enableToast"] = enabled, "SaveEnableToast");
        }

        /// <summary>Persist toastIdleSeconds. Values outside 10/20/30/60 are stored as 20.</summary>
        public static bool SaveToastIdleSeconds(int seconds)
        {
            var normalized = NormalizeToastIdleSeconds(seconds);
            return TryUpdate(root => root["toastIdleSeconds"] = normalized, "SaveToastIdleSeconds");
        }

        /// <summary>Persist showBranding so the wordmark choice survives an AutoCAD restart.</summary>
        public static bool SaveShowBranding(bool show)
        {
            return TryUpdate(root => root["showBranding"] = show, "SaveShowBranding");
        }

        private static readonly object WriteGate = new object();
        private static bool TryUpdate(Action<JObject> mutate, string operation)
        {
            return TryUpdate(mutate, operation, out _);
        }
        private static bool TryUpdate(Action<JObject> mutate, string operation, out string error)
        {
            lock (WriteGate) return TryUpdateLocked(mutate, operation, out error);
        }
        private static bool TryUpdateLocked(Action<JObject> mutate, string operation, out string error)
        {
            error = null;
            var path = FilePath;
            try
            {
                JObject root = null;
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Never clobber a file the user wrote but we cannot parse.
                        try { root = JToken.Parse(text) as JObject; }
                        catch { root = null; }
                        if (root == null)
                        {
                            error = "Settings file must contain a valid JSON object.";
                            PluginLog.Debug("PluginSettings." + operation + " skipped: settings file is not a JSON object.");
                            return false;
                        }
                    }
                }
                root = root ?? new JObject();
                mutate(root);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, root.ToString(Newtonsoft.Json.Formatting.Indented));
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                PluginLog.Debug("PluginSettings." + operation + " failed: " + ex.Message);
                return false;
            }
        }

        internal static int? ReadToastIdleSeconds(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;
                return JObject.Parse(File.ReadAllText(path))["toastIdleSeconds"]?.Value<int?>();
            }
            catch
            {
                return null;
            }
        }

        internal static bool? ReadShowBranding(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;
                return JObject.Parse(File.ReadAllText(path))["showBranding"]?.Value<bool?>();
            }
            catch
            {
                return null;
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
