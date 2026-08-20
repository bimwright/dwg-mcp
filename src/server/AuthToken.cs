using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public static class AuthToken
    {
        public static readonly string[] AllVersions = { "2022", "2023", "2024", "2025", "2026", "2027" };

        public static string DefaultRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright");

        public static IReadOnlyList<DiscoveryInfo> ListAvailable(string root = null)
        {
            root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            CleanupLegacyDiscoveryFiles(root);

            var infos = new List<DiscoveryInfo>();
            var dir = JsonDiscoveryDir(root);
            if (!Directory.Exists(dir))
            {
                return infos;
            }

            foreach (var path in Directory.GetFiles(dir, "acad-*.json"))
            {
                if (TryReadJson(path, out var info))
                {
                    infos.Add(info);
                }
            }

            return infos
                .OrderByDescending(info => Array.IndexOf(AllVersions, info.Target))
                .ToArray();
        }

        public static DiscoveryInfo Resolve(string target = null, string root = null)
        {
            root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            target = NormalizeTarget(target);

            var available = ListAvailable(root);
            DiscoveryInfo selected = null;
            if (!string.IsNullOrWhiteSpace(target))
            {
                selected = available.FirstOrDefault(info => string.Equals(info.Target, target, StringComparison.Ordinal));
            }
            else
            {
                selected = available.FirstOrDefault();
            }

            if (selected != null)
            {
                return selected;
            }

            if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "2024", StringComparison.Ordinal))
            {
                var legacy = TryReadLegacy(root);
                if (legacy != null)
                {
                    return legacy;
                }
            }

            if (!string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException(
                    "No AutoCAD " + target + " discovery file found. Start AutoCAD " + target + " and load the Bimwright DWG plugin.");
            }

            throw new InvalidOperationException(
                "Plugin not responding - run MCPSTART in AutoCAD (no acad-YYYY.json or portAcad24.txt discovery file found).");
        }

        public static string NormalizeTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            target = target.Trim();
            if (AllVersions.Any(version => string.Equals(version, target, StringComparison.Ordinal)))
            {
                return target;
            }

            throw new ArgumentException(
                "Invalid --target value '" + target + "'. Expected a 4-digit AutoCAD year: " + string.Join(" | ", AllVersions) + ".",
                nameof(target));
        }

        public static void CleanupLegacyDiscoveryFiles(string root = null)
        {
            root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            var dir = JsonDiscoveryDir(root);
            if (Directory.Exists(dir))
            {
                foreach (var path in Directory.GetFiles(dir, "acad-*.json"))
                {
                    if (!TryReadJson(path, out _))
                    {
                        TryDelete(path);
                    }
                }
            }

            var legacy = LegacyDiscoveryPath(root);
            if (File.Exists(legacy) && !TryReadLegacy(legacy, out _))
            {
                TryDelete(legacy);
            }
        }

        private static bool TryReadJson(string path, out DiscoveryInfo info)
        {
            info = null;
            try
            {
                var raw = File.ReadAllText(path);
                var parsed = JsonConvert.DeserializeObject<DiscoveryInfo>(raw);
                if (parsed == null)
                {
                    return false;
                }

                parsed.Target = NormalizeTarget(parsed.Target ?? parsed.Version ?? (parsed.AcadYear > 0 ? parsed.AcadYear.ToString() : null));
                parsed.Version = parsed.Version ?? parsed.Target;
                parsed.AcadYear = parsed.AcadYear > 0 ? parsed.AcadYear : int.Parse(parsed.Target);
                parsed.Transport = string.IsNullOrWhiteSpace(parsed.Transport) ? "tcp" : parsed.Transport.Trim().ToLowerInvariant();
                if (!string.Equals(parsed.Transport, "tcp", StringComparison.Ordinal)
                    && !string.Equals(parsed.Transport, "pipe", StringComparison.Ordinal))
                {
                    return false;
                }

                parsed.Host = string.IsNullOrWhiteSpace(parsed.Host) ? "127.0.0.1" : parsed.Host.Trim();
                parsed.DiscoveryFile = path;
                parsed.PipeName = NormalizePipeName(parsed.PipeName);

                if (!IsProcessAlive(parsed.Pid))
                {
                    return false;
                }

                if (string.Equals(parsed.Transport, "tcp", StringComparison.OrdinalIgnoreCase) && (parsed.Port ?? 0) <= 0)
                {
                    return false;
                }

                if (string.Equals(parsed.Transport, "pipe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(parsed.PipeName))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(parsed.Token))
                {
                    return false;
                }

                info = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static DiscoveryInfo TryReadLegacy(string root)
        {
            return TryReadLegacy(LegacyDiscoveryPath(root), out var info) ? info : null;
        }

        private static bool TryReadLegacy(string path, out DiscoveryInfo info)
        {
            info = null;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                var lines = File.ReadAllLines(path);
                if (lines.Length < 3)
                {
                    return false;
                }

                var port = int.Parse(lines[0].Trim());
                var token = lines[1].Trim();
                var pid = int.Parse(lines[2].Trim());
                if (port <= 0 || string.IsNullOrWhiteSpace(token) || !IsProcessAlive(pid))
                {
                    return false;
                }

                info = new DiscoveryInfo
                {
                    SchemaVersion = 1,
                    Target = "2024",
                    Version = "2024",
                    Transport = "tcp",
                    Host = "127.0.0.1",
                    Port = port,
                    Token = token,
                    Pid = pid,
                    DiscoveryFile = path
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                using (Process.GetProcessById(pid))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizePipeName(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return null;
            }

            pipeName = pipeName.Trim();
            const string prefix = @"\\.\pipe\";
            if (pipeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return pipeName.Substring(prefix.Length);
            }

            return pipeName;
        }

        private static string JsonDiscoveryDir(string root)
            => Path.Combine(root, "Dwg");

        private static string LegacyDiscoveryPath(string root)
            => Path.Combine(root, "portAcad24.txt");

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
