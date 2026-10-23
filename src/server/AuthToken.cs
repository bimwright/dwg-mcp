using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public static class AuthToken
    {
        public static readonly string[] AllVersions = { "2022", "2023", "2024", "2025", "2026", "2027" };

        public static string DefaultRoot
        {
            get
            {
                var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrWhiteSpace(localAppData))
                    localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "Bimwright");
            }
        }

        public static IReadOnlyList<DiscoveryInfo> ListAvailable(string root = null)
        {
            root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            CleanupLegacyDiscoveryFiles(root);

            var infos = new List<DiscoveryInfo>();
            var dir = JsonDiscoveryDir(root);
            if (!Directory.Exists(dir))
            {
                var legacyOnly = TryReadLegacy(root);
                if (legacyOnly != null) infos.Add(legacyOnly);
                return infos;
            }

            foreach (var year in AllVersions)
            {
                var path = Path.Combine(dir, "acad-" + year + ".json");
                // Per-year coordination also covers enumeration during atomic publication.
                try { Bimwright.Dwg.Plugin.TransportDiscovery.RunLocked(path, () =>
                {
                    var paths = Directory.GetFiles(dir, "acad-" + year + "-*.json")
                        .Concat(new[] { path });
                    foreach (var descriptor in paths)
                    {
                        if (TryReadJson(descriptor, out var info, out var transient)) infos.Add(info);
                        else if (!transient) TryDelete(descriptor);
                    }
                }); } catch (IOException) { } catch (TimeoutException) { }
            }

            var legacy = TryReadLegacy(root);
            if (legacy != null && !infos.Any(info => info.Target == "2024" && info.Pid == legacy.Pid)) infos.Add(legacy);

            return infos
                .GroupBy(info => info.TargetId)
                .Select(group => group.OrderByDescending(info => info.SchemaVersion)
                    .ThenByDescending(info => Path.GetFileNameWithoutExtension(info.DiscoveryFile) == info.TargetId).First())
                .OrderByDescending(info => Array.IndexOf(AllVersions, info.Target))
                .ThenBy(info => info.Pid)
                .ToArray();
        }

        public static DiscoveryInfo Resolve(string target = null, string root = null)
        {
            root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            target = NormalizeTarget(target);

            var available = ListAvailable(root);
            var matches = available.Where(info => target == null || info.Target == target
                || target == "pid:" + info.Pid || target == "id:" + info.TargetId).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException("AMBIGUOUS_TARGET: multiple AutoCAD instances match. Use dwg_list_available_targets and select pid:<PID> or id:<target_id>.");
            var selected = matches.SingleOrDefault();

            if (selected != null)
            {
                return selected;
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
            if (AllVersions.Any(version => string.Equals(version, target, StringComparison.Ordinal))
                || Regex.IsMatch(target, @"^pid:[1-9][0-9]*$") && int.TryParse(target.Substring(4), out _)
                || Regex.IsMatch(target, @"^id:acad-202[2-7]-[1-9][0-9]*$"))
            {
                return target;
            }

            throw new ArgumentException(
                "Invalid --target value '" + target + "'. Expected a 4-digit AutoCAD year (2022-2027), pid:<PID>, or id:acad-<year>-<PID>.",
                nameof(target));
        }

        public static void CleanupLegacyDiscoveryFiles(string root = null)
        {
            root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
            var dir = JsonDiscoveryDir(root);
            if (Directory.Exists(dir))
            {
                foreach (var year in AllVersions)
                {
                    var path = Path.Combine(dir, "acad-" + year + ".json");
                    // Publication and invalid-descriptor cleanup share ownership coordination.
                    // A transient read/parse failure never deletes a potentially live descriptor.
                    try { Bimwright.Dwg.Plugin.TransportDiscovery.RunLocked(path, () =>
                    {
                        foreach (var descriptor in Directory.GetFiles(dir, "acad-" + year + "-*.json").Concat(new[] { path }))
                            if (!TryReadJson(descriptor, out _, out var transient) && !transient) TryDelete(descriptor);
                    }); } catch (IOException) { } catch (TimeoutException) { }
                }
            }

            var legacy = LegacyDiscoveryPath(root);
            if (File.Exists(legacy))
            {
                try { Bimwright.Dwg.Plugin.TransportDiscovery.RunLocked(Path.Combine(dir, "acad-2024.json"), () =>
                {
                    if (!TryReadLegacy(legacy, out _, out var transient) && !transient) TryDelete(legacy);
                }); } catch (IOException) { } catch (TimeoutException) { }
            }
        }

        private static bool TryReadJson(string path, out DiscoveryInfo info)
            => TryReadJson(path, out info, out _);

        private static bool TryReadJson(string path, out DiscoveryInfo info, out bool transient)
        {
            info = null;
            transient = false;
            try
            {
                var raw = File.ReadAllText(path);
                var parsed = JsonConvert.DeserializeObject<DiscoveryInfo>(raw);
                if (parsed == null)
                {
                    return false;
                }

                parsed.Target = parsed.Target ?? parsed.Version ?? (parsed.AcadYear > 0 ? parsed.AcadYear.ToString() : null);
                if (!AllVersions.Contains(parsed.Target)) return false;
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
                using (var process = Process.GetProcessById(parsed.Pid))
                {
                    var actualStart = process.StartTime.ToUniversalTime();
                    if (parsed.SchemaVersion >= 3 && (parsed.ProcessStartUtc?.ToUniversalTime() != actualStart
                        || parsed.TargetId != "acad-" + parsed.Target + "-" + parsed.Pid)) return false;
                    parsed.ProcessStartUtc = actualStart;
                    parsed.TargetId = "acad-" + parsed.Target + "-" + parsed.Pid;
                }
                var name = Path.GetFileNameWithoutExtension(path);
                if (name != "acad-" + parsed.Target && name != parsed.TargetId) return false;

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
            catch (IOException) { transient = true; return false; }
            catch (UnauthorizedAccessException) { transient = true; return false; }
            catch (System.ComponentModel.Win32Exception) { transient = true; return false; }
            catch (JsonException) { transient = true; return false; }
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
            => TryReadLegacy(path, out info, out _);

        private static bool TryReadLegacy(string path, out DiscoveryInfo info, out bool transient)
        {
            info = null;
            transient = false;
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
                    AcadYear = 2024,
                    TargetId = "acad-2024-" + pid,
                    ProcessStartUtc = Process.GetProcessById(pid).StartTime.ToUniversalTime(),
                    DiscoveryFile = path
                };
                return true;
            }
            catch (IOException) { transient = true; return false; }
            catch (UnauthorizedAccessException) { transient = true; return false; }
            catch (FormatException) { transient = true; return false; }
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
            => Path.Combine(root, "dwg-mcp");

        private static string LegacyDiscoveryPath(string root)
            => Path.Combine(root, "portAcad24.txt");

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }
    }
}
