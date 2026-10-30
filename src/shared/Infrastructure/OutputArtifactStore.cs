using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Complete output, separate from opt-in call logs. Only owned files are pruned.</summary>
    public static class OutputArtifactStore
    {
        public const long MaxFileBytes = 128L * 1024 * 1024;
        public const long MaxGroupBytes = 256L * 1024 * 1024;
        public const int MaxFiles = 64;
        public const int RetentionDays = 7;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex Owned = new Regex(@"^output-(?<time>\d{8}-\d{13})-[a-f0-9]{32}\.json(?:\.tmp)?$", RegexOptions.CultureInvariant);
        private static readonly AsyncLocal<string> Override = new AsyncLocal<string>();
        internal static string RootOverride { get => Override.Value; set => Override.Value = value; }
        internal static string Root
        {
            get
            {
                if (RootOverride != null) return RootOverride;
                var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrWhiteSpace(local)) local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "Bimwright", "dwg-mcp", "outputs");
            }
        }

        public static JObject Write(string json, string scope)
            => Write(json, scope, Root, DateTime.UtcNow, MaxFileBytes, MaxGroupBytes, MaxFiles);

        internal static JObject Write(string json, string scope, string root, DateTime utc, long fileLimit, long groupLimit, int countLimit)
        {
            var size = Utf8.GetByteCount(json ?? "null");
            if (size > fileLimit) return Unavailable(scope, "artifact_size_limit");
            string temporary = null, final = null;
            try
            {
                root = Path.GetFullPath(root);
                var lockPath = Path.Combine(root, "outputs.lock");
                BoundedLogFile.RefuseLink(lockPath);
                return BoundedLogFile.WithLock(lockPath, () =>
                {
                    BoundedLogFile.RefuseLink(lockPath);
                    Directory.CreateDirectory(root);
                    Prune(root, utc, size, groupLimit, countLimit - 1);
                    var name = "output-" + utc.ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json";
                    final = Path.Combine(root, name); temporary = final + ".tmp";
                    string hash;
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var sha = SHA256.Create())
                    {
                        using (var crypto = new CryptoStream(file, sha, CryptoStreamMode.Write, true))
                        {
                            using (var writer = new StreamWriter(crypto, Utf8, 8192, true)) { writer.Write(json ?? "null"); writer.Flush(); }
                            crypto.FlushFinalBlock();
                            file.Flush(true);
                            hash = Hex(sha.Hash);
                        }
                    }
                    File.Move(temporary, final); temporary = null;
                    var observed = FileOutputReceipt.Observe(final);
                    if (observed.Value<long?>("bytes") != size || observed.Value<string>("sha256") != hash)
                        throw new IOException("Artifact readback failed.");
                    return new JObject
                    {
                        ["status"] = "available", ["complete"] = true, ["scope"] = scope,
                        ["path"] = final, ["sha256"] = hash, ["bytes"] = size,
                        ["created_at_utc"] = utc.ToString("o", CultureInfo.InvariantCulture),
                        ["preview"] = Preview(json, 512), ["preview_complete"] = (json ?? "").Length <= 512,
                        ["retention"] = new JObject { ["max_age_days"] = RetentionDays, ["group_max_bytes"] = groupLimit,
                            ["max_files"] = countLimit, ["eviction"] = "oldest first; capacity may evict before max age" }
                    };
                }, 250);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TimeoutException || ex is ArgumentException || ex is NotSupportedException)
            {
                // Only this call's unique paths may be removed, never a user file.
                foreach (var path in new[] { temporary, final })
                    if (path != null) try { BoundedLogFile.RefuseLink(path); File.Delete(path); } catch { }
                return Unavailable(scope, ex is TimeoutException ? "storage_busy" : "storage_unavailable");
            }
        }

        internal static JObject Unavailable(string scope, string reason) => new JObject
        { ["status"] = "unavailable", ["complete"] = false, ["scope"] = scope, ["path"] = null, ["sha256"] = null, ["bytes"] = null, ["reason"] = reason };

        internal static string Preview(string text, int characters)
        {
            text = text ?? "";
            var length = Math.Min(text.Length, Math.Max(0, characters));
            if (length < text.Length && length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }

        internal static void Prune(string root, DateTime utc, long reserveBytes, long budget, int keepCount)
        {
            BoundedLogFile.RefuseLink(Path.Combine(root, "outputs.lock"));
            if (!Directory.Exists(root)) return;
            var files = Directory.GetFiles(root).Where(p => Owned.IsMatch(Path.GetFileName(p)))
                .OrderBy(p => p.EndsWith(".tmp", StringComparison.Ordinal) ? 0 : 1).ThenBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (var path in files) BoundedLogFile.RefuseLink(path);
            long total = files.Sum(p => new FileInfo(p).Length);
            var count = files.Length;
            foreach (var path in files)
            {
                var match = Owned.Match(Path.GetFileName(path));
                if (!DateTime.TryParseExact(match.Groups["time"].Value, "yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created)) continue;
                if (!path.EndsWith(".tmp", StringComparison.Ordinal) && created >= utc.AddDays(-RetentionDays)
                    && total + reserveBytes <= budget && count <= keepCount) continue;
                var size = new FileInfo(path).Length;
                File.Delete(path); // Locked/unsafe files prevent growth rather than bypassing caps.
                total -= size; count--;
            }
            if (total + reserveBytes > budget || count > keepCount) throw new IOException("Artifact retention capacity unavailable.");
        }

        public static void MaintainInBackground()
        {
            var root = Root;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!Directory.Exists(root)) return;
                    var lockPath = Path.Combine(root, "outputs.lock");
                    BoundedLogFile.RefuseLink(lockPath);
                    BoundedLogFile.WithLock(lockPath, () => { Prune(root, DateTime.UtcNow, 0, MaxGroupBytes, MaxFiles); return true; }, 250);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TimeoutException || ex is ArgumentException || ex is NotSupportedException) { }
            });
        }

        internal static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
