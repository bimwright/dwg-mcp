using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Readback evidence, not proof of drawing transactions or rollback.</summary>
    public static class FileOutputReceipt
    {
        public static JObject Observe(string path)
        {
            var receipt = new JObject { ["path"] = path, ["exists"] = null, ["bytes"] = null, ["sha256"] = null };
            try
            {
                BoundedLogFile.RefuseLink(path);
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("Output path is linked or not a file.");
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    receipt["bytes"] = stream.Length;
                    receipt["sha256"] = OutputArtifactStore.Hex(sha.ComputeHash(stream));
                    receipt["exists"] = true;
                }
            }
            catch (FileNotFoundException) { receipt["exists"] = false; }
            catch (DirectoryNotFoundException) { receipt["exists"] = false; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            { receipt["reason"] = "readback_unavailable"; }
            return receipt;
        }

        public static JObject Verify(string path, JObject before)
        {
            var after = Observe(path);
            bool? changed = after["sha256"]?.Type == JTokenType.String && before?.Value<bool?>("exists") == false ? true
                : after["sha256"]?.Type == JTokenType.String && before?["sha256"]?.Type == JTokenType.String
                    ? after.Value<string>("sha256") != before.Value<string>("sha256") : (bool?)null;
            after["new_or_changed"] = changed.HasValue ? new JValue(changed.Value) : JValue.CreateNull();
            after["verified"] = changed == true;
            after["status"] = changed == true ? (before.Value<bool?>("exists") == false ? "verified_new" : "verified_changed")
                : changed == false ? "unchanged" : "unavailable";
            return after;
        }
    }
}
