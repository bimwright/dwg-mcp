using System;
using System.IO;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public static class ToastPreferences
    {
        private static readonly object Gate = new object();
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "Dwg", "ui-settings.json");

        public static bool Load(string path = null, Func<string, string> env = null)
        {
            var value = (env ?? Environment.GetEnvironmentVariable)("BIMWRIGHT_DWG_ENABLE_TOAST");
            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            lock (Gate)
            {
                try
                {
                    var file = path ?? DefaultPath;
                    var settings = File.Exists(file) ? JObject.Parse(File.ReadAllText(file)) : new JObject();
                    return settings["enableToast"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean ? (bool)settings["enableToast"] : true;
                }
                catch { return true; }
            }
        }

        public static bool TrySave(bool enabled, out string error, string path = null)
        {
            error = null;
            lock (Gate)
            {
                string temporary = null;
                try
                {
                    var file = Path.GetFullPath(path ?? DefaultPath);
                    var settings = File.Exists(file) ? JObject.Parse(File.ReadAllText(file)) : new JObject();
                    settings["enableToast"] = enabled;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporary, settings.ToString());
                    if (File.Exists(file)) File.Replace(temporary, file, null);
                    else File.Move(temporary, file);
                    return true;
                }
                catch { error = "Could not save toast settings. Check the settings file and directory permissions."; return false; }
                finally { try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); } catch { } }
            }
        }
    }
}
