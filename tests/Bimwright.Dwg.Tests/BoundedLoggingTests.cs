using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server.Logging;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public class BoundedLoggingTests
    {
        [Fact]
        public void Call_writer_never_creates_a_file_larger_than_five_MiB()
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-bounded-log-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "calls.jsonl");
            try
            {
                // Reach the real writer and privacy serializer without touching user logs.
                try { new CallLogWriter(path, () => true).Append(new { payload = new string('x', 6 * 1024 * 1024) }); }
                catch (InvalidDataException) { }
                Assert.True(!File.Exists(path) || new FileInfo(path).Length <= 5L * 1024 * 1024,
                    "A single enabled call must never exceed the 5 MiB file budget.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        [Fact]
        public void Utf8_budget_is_checked_before_directory_creation()
        {
            InRoot(root =>
            {
                Assert.Throws<InvalidDataException>(() => BoundedLogFile.Append(Path.Combine(root, "calls.jsonl"),
                    new string('界', 100), fileBytes: 256, groupBytes: 1024));
                Assert.False(Directory.Exists(root));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Recent_legacy_call_records_survive_startup_or_first_append_within_group_budget(bool maintainFirst)
        {
            InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "calls.jsonl");
                var legacy = "{\"payload\":\"" + new string('x', 6 * 1024 * 1024) + "\"}\n";
                File.WriteAllText(path, legacy);
                if (maintainFirst) BoundedLogFile.MaintainGroup(root, "calls");
                new CallLogWriter(path, () => true).Append(new { marker = "new-record" });
                var archive = Assert.Single(BoundedLogFile.Files(root, "calls").Where(file => file != path));
                Assert.Equal(legacy, File.ReadAllText(archive));
                Assert.Contains("new-record", File.ReadAllText(path));
                Assert.True(new FileInfo(path).Length <= BoundedLogFile.RecordFileBytes);
                Assert.True(BoundedLogFile.Files(root, "calls").Sum(file => new FileInfo(file).Length) <= BoundedLogFile.RecordGroupBytes);
            });
        }

        [Fact]
        public void Rotation_accounts_for_incoming_bytes_and_prunes_only_its_exact_group()
        {
            InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "calls.jsonl");
                var unrelated = Path.Combine(root, "calls-personal.jsonl");
                File.WriteAllText(unrelated, "keep");
                for (var i = 0; i < 12; i++)
                    BoundedLogFile.Append(path, new string('x', 99), fileBytes: 200, groupBytes: 500);
                var files = BoundedLogFile.Files(root, "calls");
                Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 200));
                Assert.InRange(files.Sum(file => new FileInfo(file).Length), 1, 500);
                Assert.Equal("keep", File.ReadAllText(unrelated));
            });
        }

        [Fact]
        public void Recreated_file_with_tunneled_creation_time_does_not_rotate_each_append()
        {
            InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "calls.jsonl");
                File.WriteAllText(path, "existing\n");
                // NTFS may preserve creation time when rotation recreates the same path.
                File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-1));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                BoundedLogFile.Append(path, "next");
                BoundedLogFile.Append(path, "last");
                Assert.Single(BoundedLogFile.Files(root, "calls"));
                Assert.Equal(new[] { "existing", "next", "last" }, File.ReadAllLines(path));
            });
        }

        [Fact]
        public void Daily_rollover_uses_last_write_day_and_preserves_previous_records()
        {
            InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, "calls.jsonl");
                File.WriteAllText(path, "yesterday\n");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
                BoundedLogFile.Append(path, "today");
                var archive = Assert.Single(BoundedLogFile.Files(root, "calls").Where(file => file != path));
                Assert.Equal("yesterday\n", File.ReadAllText(archive));
                Assert.Equal("today\n", File.ReadAllText(path));
            });
        }

        [Fact]
        public void Concurrent_real_call_writers_preserve_records_and_file_cap()
        {
            InRoot(root =>
            {
                var path = Path.Combine(root, "calls.jsonl");
                Parallel.For(0, 8, worker =>
                {
                    var writer = new CallLogWriter(path, () => true);
                    for (var i = 0; i < 30; i++) writer.Append(new { worker, i, text = new string('x', 30000) });
                });
                var files = BoundedLogFile.Files(root, "calls");
                Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, BoundedLogFile.RecordFileBytes));
                Assert.Equal(240, files.Sum(file => File.ReadLines(file).Count()));
            });
        }

        [Fact]
        public void Locked_rotation_never_falls_back_to_unbounded_append()
        {
            InRoot(root =>
            {
                var path = Path.Combine(root, "calls.jsonl");
                BoundedLogFile.Append(path, new string('x', 99), fileBytes: 100, groupBytes: 500);
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    Assert.ThrowsAny<IOException>(() => BoundedLogFile.Append(path, "new", fileBytes: 100, groupBytes: 500));
                Assert.Equal(100, new FileInfo(path).Length);
                Assert.Single(Directory.GetFiles(root));
            });
        }

        [Fact]
        public async Task Canonical_mutex_timeout_never_runs_action_without_lock()
        {
            await InRootAsync(async root =>
            {
                var path = Path.Combine(root, "calls.jsonl");
                using var locked = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                var task = Task.Run(() => BoundedLogFile.WithLock(path, () =>
                {
                    locked.Set(); release.Wait(); return true;
                }));
                Assert.True(locked.Wait(3000));
                var ran = false;
                try
                {
                    Assert.Throws<TimeoutException>(() => BoundedLogFile.WithLock(Path.Combine(root, ".", "calls.jsonl"),
                        () => { ran = true; return true; }, 50));
                    Assert.False(ran);
                }
                finally { release.Set(); await task; }
                Assert.False(Directory.Exists(root));
            });
        }

        [Fact]
        public void Startup_maintenance_applies_age_file_and_group_caps_without_writing_records()
        {
            InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var active = Path.Combine(root, "calls.jsonl");
                SetSize(active, BoundedLogFile.RecordFileBytes + 1);
                var old = Path.Combine(root, "calls-20200101-000000.jsonl");
                File.WriteAllText(old, "old");
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
                for (var i = 0; i < 21; i++)
                    SetSize(Path.Combine(root, "calls-20261015-" + i.ToString("000000") + ".jsonl"), BoundedLogFile.RecordFileBytes);
                var unrelated = Path.Combine(root, "personal.jsonl");
                File.WriteAllText(unrelated, "preserve");
                BoundedLogFile.MaintainGroup(root, "calls");
                var files = BoundedLogFile.Files(root, "calls");
                Assert.False(File.Exists(old));
                Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, BoundedLogFile.RecordFileBytes));
                Assert.True(files.Sum(file => new FileInfo(file).Length) <= BoundedLogFile.RecordGroupBytes);
                Assert.Equal("preserve", File.ReadAllText(unrelated));
            });
        }

        [Fact]
        public void Reparse_directories_are_refused_without_modifying_the_target()
        {
            InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var target = Path.Combine(root, "target");
                var link = Path.Combine(root, "link");
                Directory.CreateDirectory(target);
                // A junction works on Windows without symbolic-link privilege.
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
                start.ArgumentList.Add(link); start.ArgumentList.Add(target);
                using (var process = Process.Start(start)) { process.WaitForExit(); Assert.Equal(0, process.ExitCode); }
                try
                {
                    Assert.Throws<IOException>(() => BoundedLogFile.Append(Path.Combine(link, "calls.jsonl"), "blocked"));
                    Assert.Throws<IOException>(() => BoundedLogFile.MaintainGroup(link, "calls"));
                    Assert.Empty(Directory.GetFiles(target));
                }
                finally { Directory.Delete(link); }
            });
        }

        [Fact]
        public void Diagnostic_repeats_are_summarized_and_secrets_and_multiline_are_bounded()
        {
            InDiagnostics(root =>
            {
                for (var i = 0; i < 10; i++) DiagnosticLog.Write("Test", "password=hidden\r\nrepeat", "2027");
                DiagnosticLog.Write("Long", new string('x', 5000));
                Assert.True(DiagnosticLog.Flush(5000));
                var lines = File.ReadAllLines(Path.Combine(root, "Bimwright", "dwg-mcp", "dwg-mcp.log"));
                Assert.Equal(3, lines.Length);
                Assert.DoesNotContain("hidden", string.Join("", lines));
                Assert.Contains(lines, line => line.Contains("[repeated=9]"));
                Assert.Contains(lines, line => line.Contains("autocad=2027"));
                Assert.Contains(lines, line => line.Contains("[truncated]"));
                Assert.All(lines, line => Assert.InRange(line.Length, 1, DiagnosticLog.MaxMessageCharacters + 150));
            });
        }

        [Fact]
        public void Diagnostic_caller_does_not_wait_for_disk_lock_and_storm_drops_safely()
        {
            InDiagnostics(root =>
            {
                var path = Path.Combine(root, "Bimwright", "dwg-mcp", "dwg-mcp.log");
                var before = DiagnosticLog.Dropped;
                BoundedLogFile.WithLock(path, () =>
                {
                    var timer = Stopwatch.StartNew();
                    for (var i = 0; i < 10000; i++) DiagnosticLog.Write("Storm", "message-" + i);
                    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), "The caller waited on a locked disk sink.");
                    Assert.True(DiagnosticLog.Dropped > before);
                    Assert.False(File.Exists(path));
                    return true;
                });
                // A flush can itself be dropped while the finite queue is full.
                Assert.True(SpinWait.SpinUntil(() => DiagnosticLog.Flush(5000), 10000));
                Assert.InRange(new FileInfo(path).Length, 1, DiagnosticLog.FileBytes);
            });
        }

        [Fact]
        public void Startup_maintenance_preserves_diagnostic_tails_and_only_known_files_in_both_roots()
        {
            InDiagnostics(root =>
            {
                foreach (var relative in new[] { "dwg-mcp", "Dwg" })
                {
                    var product = Path.Combine(root, "Bimwright", relative);
                    Directory.CreateDirectory(product);
                    File.WriteAllText(Path.Combine(product, "debug.log"), new string('x', 100000) + "\nrecent-tail\n");
                    SetSize(Path.Combine(product, "dwg-mcp-calls.jsonl"), BoundedLogFile.RecordGroupBytes + 1);
                    File.WriteAllText(Path.Combine(product, "drawing.jsonl"), "preserve");
                    File.WriteAllText(Path.Combine(product, "dwgmcp.config.json"), "config");
                    Directory.CreateDirectory(Path.Combine(product, "baked"));
                    File.WriteAllText(Path.Combine(product, "baked", "bake.db"), "db");
                }
                var current = Path.Combine(root, "Bimwright", "dwg-mcp");
                var diagnostics = Path.Combine(current, "dwg-mcp.log");
                File.WriteAllText(diagnostics, new string('x', (int)DiagnosticLog.FileBytes + 100) + "\ncurrent-tail\n");
                File.WriteAllText(diagnostics + ".1", new string('x', (int)DiagnosticLog.FileBytes + 100) + "\nbackup-tail\n");
                DiagnosticLog.MaintainLegacyLogs();
                Assert.True(DiagnosticLog.Flush(10000));
                foreach (var relative in new[] { "dwg-mcp", "Dwg" })
                {
                    var product = Path.Combine(root, "Bimwright", relative);
                    Assert.InRange(new FileInfo(Path.Combine(product, "debug.log")).Length, 1, 64 * 1024);
                    Assert.Contains("recent-tail", File.ReadAllText(Path.Combine(product, "debug.log")));
                    Assert.Empty(BoundedLogFile.Files(product, "dwg-mcp-calls"));
                    Assert.Equal("preserve", File.ReadAllText(Path.Combine(product, "drawing.jsonl")));
                    Assert.Equal("config", File.ReadAllText(Path.Combine(product, "dwgmcp.config.json")));
                    Assert.Equal("db", File.ReadAllText(Path.Combine(product, "baked", "bake.db")));
                }
                Assert.InRange(new FileInfo(diagnostics).Length, 1, DiagnosticLog.FileBytes);
                Assert.InRange(new FileInfo(diagnostics + ".1").Length, 1, DiagnosticLog.FileBytes);
                Assert.Contains("current-tail", File.ReadAllText(diagnostics));
                Assert.Contains("backup-tail", File.ReadAllText(diagnostics + ".1"));
            });
        }

        [Fact]
        public void Diagnostic_rotation_keeps_one_backup_and_never_exceeds_ten_MiB()
        {
            InDiagnostics(root =>
            {
                var product = Path.Combine(root, "Bimwright", "dwg-mcp");
                Directory.CreateDirectory(product);
                var path = Path.Combine(product, "dwg-mcp.log");
                SetSize(path, DiagnosticLog.FileBytes);
                File.WriteAllText(path + ".1", "old-backup");
                DiagnosticLog.Write("Rotate", "new-record");
                Assert.True(DiagnosticLog.Flush(5000));
                Assert.Equal(DiagnosticLog.FileBytes, new FileInfo(path + ".1").Length);
                Assert.Contains("new-record", File.ReadAllText(path));
                Assert.Equal(2, Directory.GetFiles(product).Length);
            });
        }

        [Fact]
        public void Queued_diagnostics_and_repeat_summaries_keep_the_captured_root()
        {
            InDiagnostics(root =>
            {
                var path = Path.Combine(root, "Bimwright", "dwg-mcp", "dwg-mcp.log");
                var secondProfile = Path.Combine(root, "second-profile");
                BoundedLogFile.WithLock(path, () =>
                {
                    DiagnosticLog.Write("CapturedRoot", "same-message");
                    DiagnosticLog.Write("CapturedRoot", "same-message");
                    DiagnosticLog.MaintainLegacyLogs();
                    DiagnosticLog.LocalAppDataOverride = secondProfile;
                    return true;
                });
                Assert.True(DiagnosticLog.Flush(5000));
                Assert.Contains("same-message", File.ReadAllText(path));
                Assert.Contains("[repeated=1]", File.ReadAllText(path));
                Assert.False(Directory.Exists(secondProfile));
            });
        }

        private static void SetSize(string path, long size)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.SetLength(size);
        }

        private static void InRoot(Action<string> test)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-bounded-log-" + Guid.NewGuid().ToString("N"));
            try { test(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static async Task InRootAsync(Func<string, Task> test)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-bounded-log-" + Guid.NewGuid().ToString("N"));
            try { await test(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void InDiagnostics(Action<string> test)
        {
            Assert.True(DiagnosticLog.Flush(10000));
            var previous = DiagnosticLog.LocalAppDataOverride;
            InRoot(root =>
            {
                DiagnosticLog.LocalAppDataOverride = root;
                try { test(root); }
                finally { DiagnosticLog.Flush(10000); DiagnosticLog.LocalAppDataOverride = previous; }
            });
        }
    }
}
