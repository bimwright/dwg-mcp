using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Diagnostics are best effort and never perform disk IO on the AutoCAD/UI caller.</summary>
    public static class DiagnosticLog
    {
        private sealed class Entry { public string Component, Message, Year, Root; public TaskCompletionSource<bool> Flush; public bool Cleanup; }
        private sealed class Repeat { public Entry Entry; public int Count; public long Started; }
        private sealed class Maintenance
        {
            public string Root, FailureReason;
            public List<Action> Pending;
            public int Attempt, Failed;
            public long Due;
        }
        public const int QueueCapacity = 256;
        public const int RepeatCapacity = 64;
        public const int MaxMessageCharacters = 2048;
        private static readonly BlockingCollection<Entry> Queue = new BlockingCollection<Entry>(QueueCapacity);
        private static readonly Thread Worker;
        private static readonly int Pid = Process.GetCurrentProcess().Id;
        private static long _dropped;
        private static int _pendingMaintenance;
        private static string _maintenanceStatus = "state=not_started";
        public const int MaintenanceMaxAttempts = 3;
        public const int MaintenanceRetryDelayMilliseconds = 1000;
        /// <summary>Latest bounded worker progress; not a per-request completion handle.</summary>
        public static string MaintenanceStatus => Volatile.Read(ref _maintenanceStatus);
        /// <summary>Worker-scheduled roots. Flush first to consume already queued startup requests.</summary>
        public static int PendingMaintenance => Volatile.Read(ref _pendingMaintenance);
        internal static string LocalAppDataOverride { get; set; }
        public static long Dropped => Interlocked.Read(ref _dropped);
        public const long FileBytes = 10L * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        static DiagnosticLog()
        {
            Worker = new Thread(Run) { IsBackground = true, Name = "Bimwright.Dwg.Diagnostics" };
            Worker.Start();
        }

        public static void Write(string component, string message, string year = null)
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(LocalAppDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "dwg-mcp"));
                if (!Queue.TryAdd(new Entry { Component = Safe(component, 64), Message = Safe(message, MaxMessageCharacters), Year = Safe(year ?? "unknown", 16), Root = root }))
                    Interlocked.Increment(ref _dropped);
            }
            catch { Interlocked.Increment(ref _dropped); }
        }

        private static string Safe(string text, int limit)
        {
            // Bound caller allocations before regex/escaping; redact the retained prefix too.
            text = text ?? "";
            var truncated = text.Length > limit;
            if (truncated) text = text.Substring(0, limit);
            text = SecretMasker.Mask(text).Replace("\r", "\\r").Replace("\n", "\\n");
            if (truncated || text.Length > limit)
                text = text.Substring(0, Math.Min(text.Length, limit - 12)) + " [truncated]";
            return text;
        }

        /// <summary>Drain queued writes; future maintenance retries may still be pending.</summary>
        public static bool Flush(int milliseconds = 1000)
        {
            var signal = new TaskCompletionSource<bool>();
            if (!Queue.TryAdd(new Entry { Flush = signal })) return false;
            return signal.Task.Wait(milliseconds);
        }

        public static void MaintainLegacyLogs()
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(LocalAppDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "dwg-mcp"));
                if (!Queue.TryAdd(new Entry { Cleanup = true, Root = root })) Interlocked.Increment(ref _dropped);
            }
            catch { Interlocked.Increment(ref _dropped); }
        }

        private static void Run()
        {
            var repeats = new Dictionary<string, Repeat>();
            var maintenance = new Dictionary<string, Maintenance>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                RunDueMaintenance(maintenance);
                var wait = 1000;
                foreach (var work in maintenance.Values)
                    wait = Math.Min(wait, (int)Math.Max(0, Math.Ceiling((work.Due - Stopwatch.GetTimestamp()) * 1000d / Stopwatch.Frequency)));
                Entry entry;
                Queue.TryTake(out entry, wait);
                if (entry?.Cleanup == true)
                {
                    // Coalesce repeated startup requests without resetting an active retry budget.
                    if (!maintenance.ContainsKey(entry.Root))
                    {
                        if (maintenance.Count >= RepeatCapacity)
                        { Interlocked.Increment(ref _dropped); Volatile.Write(ref _maintenanceStatus, "state=refused reason=maintenance_queue_full"); }
                        else
                        {
                            maintenance.Add(entry.Root, new Maintenance { Root = entry.Root, Pending = CleanupOperations(entry.Root), Due = Stopwatch.GetTimestamp() });
                            Interlocked.Increment(ref _pendingMaintenance);
                        }
                    }
                    continue;
                }
                if (entry?.Flush != null)
                {
                    foreach (var repeat in repeats.Values) Summary(repeat);
                    repeats.Clear();
                    entry.Flush.TrySetResult(true);
                    continue;
                }
                var now = Stopwatch.GetTimestamp();
                foreach (var key in new List<string>(repeats.Keys))
                    if ((now - repeats[key].Started) / (double)Stopwatch.Frequency >= 30)
                    { Summary(repeats[key]); repeats.Remove(key); }
                if (entry == null) continue;
                var keyName = entry.Root + "|" + entry.Component + "|" + entry.Year + "|" + entry.Message;
                Repeat existing;
                if (repeats.TryGetValue(keyName, out existing)) { if (existing.Count < int.MaxValue) existing.Count++; continue; }
                if (repeats.Count >= RepeatCapacity) { var first = new List<string>(repeats.Keys)[0]; Summary(repeats[first]); repeats.Remove(first); }
                repeats[keyName] = new Repeat { Entry = entry, Started = now };
                Append(entry, entry.Message);
            }
        }

        private static void Summary(Repeat repeat)
        {
            if (repeat.Count > 0)
                Append(repeat.Entry, Safe(repeat.Entry.Message, MaxMessageCharacters - 32) + " [repeated=" + repeat.Count + "]");
        }

        private static void Append(Entry entry, string message, bool throwOnError = false)
        {
            try
            {
                // Legacy tails and repeat summaries use this path too.
                message = Safe(message, MaxMessageCharacters);
                var root = entry.Root;
                BoundedLogFile.RefuseLink(Path.Combine(root, "dwg-mcp.log"));
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "dwg-mcp.log");
                var text = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + " pid=" + Pid + " autocad=" + entry.Year + " [" + entry.Component + "] " + message + "\n";
                var bytes = Utf8.GetBytes(text);
                BoundedLogFile.WithLock(path, () =>
                {
                    BoundedLogFile.RefuseLink(path);
                    var backup = path + ".1";
                    BoundedLogFile.RefuseLink(backup);
                    BoundedLogFile.CompactDiagnostics(path, FileBytes);
                    BoundedLogFile.CompactDiagnostics(backup, FileBytes);
                    if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > FileBytes)
                    {
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(path, backup);
                    }
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) stream.Write(bytes, 0, bytes.Length);
                    return true;
                }, 250);
            }
            catch
            {
                if (throwOnError) throw;
                Interlocked.Increment(ref _dropped); // No recursive log/fallback file.
            }
        }

        private static void RunDueMaintenance(Dictionary<string, Maintenance> maintenance)
        {
            Maintenance work = null;
            foreach (var candidate in maintenance.Values)
                if (candidate.Due <= Stopwatch.GetTimestamp() && (work == null || candidate.Due < work.Due)) work = candidate;
            if (work == null) return;
            work.Attempt++;
            SetMaintenanceStatus(work, "running", "none");
            var retry = new List<Action>();
            var retryReason = "none";
            foreach (var operation in work.Pending)
            {
                try { operation(); }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _dropped);
                    var reason = MaintenanceFailureReason(ex);
                    if (IsTransientMaintenanceFailure(ex) && work.Attempt < MaintenanceMaxAttempts)
                    { retry.Add(operation); retryReason = reason; }
                    else { work.Failed++; work.FailureReason = work.FailureReason ?? reason; }
                }
            }
            work.Pending = retry;
            if (retry.Count > 0)
            {
                work.Due = Stopwatch.GetTimestamp() + Stopwatch.Frequency * MaintenanceRetryDelayMilliseconds / 1000;
                SetMaintenanceStatus(work, "retry_pending", retryReason);
            }
            else
            {
                SetMaintenanceStatus(work, work.Failed == 0 ? "completed" : "failed", work.FailureReason ?? "none");
                maintenance.Remove(work.Root);
                Interlocked.Decrement(ref _pendingMaintenance);
            }
        }

        internal static bool IsTransientMaintenanceFailure(Exception error)
            => error is TimeoutException || error is IOException
                && (error.HResult == unchecked((int)0x80070020) || error.HResult == unchecked((int)0x80070021));

        private static string MaintenanceFailureReason(Exception error)
        {
            if (error is TimeoutException) return "mutex_timeout";
            if (error is IOException && error.HResult == unchecked((int)0x80070020)) return "sharing_violation";
            if (error is IOException && error.HResult == unchecked((int)0x80070021)) return "lock_violation";
            if (error is UnauthorizedAccessException || error is System.Security.SecurityException) return "access_denied";
            if (error is IOException && (error.Message == "Log maintenance refuses linked files." || error.Message == "Log maintenance refuses linked directories.")) return "unsafe_link";
            return error is IOException ? "io_error" : "non_transient_error";
        }

        private static void SetMaintenanceStatus(Maintenance work, string state, string reason)
        {
            var status = "state=" + state + " attempt=" + work.Attempt + "/" + MaintenanceMaxAttempts
                + " pending=" + work.Pending.Count + " failed=" + work.Failed + " reason=" + reason
                + " blocked_reason=" + (work.FailureReason ?? "none");
            Volatile.Write(ref _maintenanceStatus, status);
            if (state == "retry_pending" || state == "failed" || state == "completed" && work.Attempt > 1)
                Append(new Entry { Root = work.Root, Component = "LogMaintenance", Year = "unknown" }, status);
        }

        private static List<Action> CleanupOperations(string current)
        {
            var operations = new List<Action>();
            var local = Path.GetDirectoryName(Path.GetDirectoryName(current));
            var roots = new[] { current, Path.Combine(local, "Bimwright", "Dwg") };
            // Also bound the active diagnostics pair when no new messages arrive.
            var diagnostics = Path.Combine(current, "dwg-mcp.log");
            operations.Add(() =>
            {
                BoundedLogFile.WithLock(diagnostics, () =>
                {
                    BoundedLogFile.CompactDiagnostics(diagnostics, FileBytes);
                    BoundedLogFile.CompactDiagnostics(diagnostics + ".1", FileBytes);
                    return true;
                }, 250);
            });
            foreach (var root in roots)
                foreach (var name in new[] { "debug.log" })
                {
                    var old = Path.Combine(root, name);
                    operations.Add(() =>
                    {
                        if (!File.Exists(old)) return;
                        BoundedLogFile.WithLock(old, () =>
                        {
                            BoundedLogFile.CompactDiagnostics(old, 64 * 1024);
                            var tail = string.Join(" | ", BoundedLogFile.ReadTail(old, 8));
                            var entry = new Entry { Component = "LegacyDiagnostics", Year = "unknown", Root = current };
                            Append(entry, "Retained tail from " + Path.GetFileName(old) + ": " + SecretMasker.Mask(tail), throwOnError: true);
                            // Preserve the bounded original tail as recovery data; do not delete an active legacy writer's file.
                            return true;
                        }, 250);
                    });
                }
            foreach (var root in roots)
            {
                foreach (var group in new[] { "dwg-mcp-calls", "journal" })
                    operations.Add(() => BoundedLogFile.MaintainGroup(root, group));
            }
            return operations;
        }
    }
}
