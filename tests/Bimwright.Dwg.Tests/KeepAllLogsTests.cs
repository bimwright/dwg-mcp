using System;
using System.IO;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // The keep-all switch is process-global: share the serialized logging collection.
    [Collection("Script handlers")]
    public class KeepAllLogsTests
    {
        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData(" yes ")]
        [InlineData("On")]
        public void Keep_all_variable_accepts_1_true_yes_on_case_insensitive_and_trimmed(string value)
            => Assert.True(LogRetentionPolicy.IsTruthy(value));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("off")]
        [InlineData("no")]
        [InlineData("2")]
        [InlineData("enabled")]
        public void Keep_all_variable_is_off_for_unset_or_any_other_value(string value)
            => Assert.False(LogRetentionPolicy.IsTruthy(value));

        [Fact]
        public void Keep_all_override_replaces_the_environment_in_both_directions()
        {
            WithKeepAll(true, () => Assert.True(LogRetentionPolicy.KeepAll));
            WithKeepAll(false, () => Assert.False(LogRetentionPolicy.KeepAll));
        }

        [Fact]
        public void Keep_all_startup_maintenance_keeps_old_and_over_budget_records_but_still_rotates()
        {
            WithKeepAll(true, () => InRoot(root =>
            {
                Directory.CreateDirectory(root);
                var active = Path.Combine(root, "journal.jsonl");
                SetSize(active, BoundedLogFile.RecordFileBytes + 1);
                var old = Path.Combine(root, "journal-20200101-000000.jsonl");
                File.WriteAllText(old, "old");
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
                for (var i = 0; i < 21; i++)
                    SetSize(Path.Combine(root, "journal-20261015-" + i.ToString("000000") + ".jsonl"), BoundedLogFile.RecordFileBytes);
                BoundedLogFile.MaintainGroup(root, "journal");
                var files = BoundedLogFile.Files(root, "journal");
                Assert.Equal("old", File.ReadAllText(old));
                Assert.Equal(23, files.Length); // 21 archives + old + the rotated oversized active file
                Assert.False(File.Exists(active));
                Assert.True(files.Sum(file => new FileInfo(file).Length) > BoundedLogFile.RecordGroupBytes);
            }));
        }

        [Fact]
        public void Keep_all_append_rotates_to_archives_and_never_prunes_a_group_over_budget()
        {
            WithKeepAll(true, () => InRoot(root =>
            {
                var path = Path.Combine(root, "calls.jsonl");
                for (var i = 0; i < 12; i++)
                    BoundedLogFile.Append(path, new string('x', 99), fileBytes: 200, groupBytes: 500);
                var files = BoundedLogFile.Files(root, "calls");
                Assert.True(files.Length > 1, "Rotation must still archive the active file.");
                Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 200));
                Assert.Equal(12 * 100, files.Sum(file => new FileInfo(file).Length));
            }));
        }

        [Fact]
        public void Keep_all_off_append_still_prunes_the_group_to_its_budget()
        {
            WithKeepAll(false, () => InRoot(root =>
            {
                var path = Path.Combine(root, "calls.jsonl");
                for (var i = 0; i < 12; i++)
                    BoundedLogFile.Append(path, new string('x', 99), fileBytes: 200, groupBytes: 500);
                Assert.InRange(BoundedLogFile.Files(root, "calls").Sum(file => new FileInfo(file).Length), 1, 500);
            }));
        }

        [Fact]
        public void Keep_all_diagnostic_rollover_archives_with_timestamp_and_keeps_existing_backup()
        {
            WithKeepAll(true, () => InDiagnostics(root =>
            {
                var product = Path.Combine(root, "Bimwright", "dwg-mcp");
                Directory.CreateDirectory(product);
                var path = Path.Combine(product, "dwg-mcp.log");
                SetSize(path, DiagnosticLog.FileBytes);
                File.WriteAllText(path + ".1", "old-backup");
                DiagnosticLog.Write("Rotate", "new-record");
                Assert.True(DiagnosticLog.Flush(5000));
                Assert.Equal("old-backup", File.ReadAllText(path + ".1"));
                var archive = Assert.Single(Directory.GetFiles(product, "dwg-mcp-*.log"));
                Assert.Equal(DiagnosticLog.FileBytes, new FileInfo(archive).Length);
                Assert.Contains("new-record", File.ReadAllText(path));
                Assert.Equal(3, Directory.GetFiles(product).Length);
            }));
        }

        [Fact]
        public void Keep_all_startup_skips_diagnostic_and_debug_log_compaction_and_writes_one_notice()
        {
            WithKeepAll(true, () => InDiagnostics(root =>
            {
                var product = Path.Combine(root, "Bimwright", "dwg-mcp");
                Directory.CreateDirectory(product);
                var debug = Path.Combine(product, "debug.log");
                var debugText = new string('x', 100000) + "\nrecent-tail\n";
                File.WriteAllText(debug, debugText);
                var diagnostics = Path.Combine(product, "dwg-mcp.log");
                var backupText = new string('x', (int)DiagnosticLog.FileBytes + 100) + "\nbackup-tail\n";
                File.WriteAllText(diagnostics + ".1", backupText);
                DiagnosticLog.MaintainLegacyLogs();
                Assert.True(DiagnosticLog.Flush(10000));
                Assert.Equal(debugText, File.ReadAllText(debug));
                Assert.Equal(backupText, File.ReadAllText(diagnostics + ".1"));
                var lines = File.ReadAllLines(diagnostics);
                Assert.Single(lines, line => line.Contains("[LogRetention] " + LogRetentionPolicy.EnvironmentVariable + " is on"));
                Assert.Contains(lines, line => line.Contains("Retained tail from debug.log"));
            }));
        }

        [Fact]
        public void Keep_all_off_startup_writes_no_keep_all_notice()
        {
            WithKeepAll(false, () => InDiagnostics(root =>
            {
                DiagnosticLog.MaintainLegacyLogs();
                DiagnosticLog.Write("Probe", "after-startup");
                Assert.True(DiagnosticLog.Flush(10000));
                var text = File.ReadAllText(Path.Combine(root, "Bimwright", "dwg-mcp", "dwg-mcp.log"));
                Assert.Contains("after-startup", text);
                Assert.DoesNotContain(LogRetentionPolicy.EnvironmentVariable, text);
            }));
        }

        [Fact]
        public void Keep_all_report_retention_deletes_nothing()
        {
            WithKeepAll(true, () => InRoot(root =>
            {
                var report = OldReport(root);
                Assert.Equal(0, ReportRetention.Cleanup(root, TimeSpan.FromDays(30), DateTime.UtcNow, null));
                Assert.True(File.Exists(report));
            }));
        }

        [Fact]
        public void Keep_all_off_report_retention_deletes_reports_older_than_retention()
        {
            WithKeepAll(false, () => InRoot(root =>
            {
                var report = OldReport(root);
                Assert.Equal(1, ReportRetention.Cleanup(root, TimeSpan.FromDays(30), DateTime.UtcNow, null));
                Assert.False(File.Exists(report));
            }));
        }

        private static string OldReport(string root)
        {
            Directory.CreateDirectory(root);
            var report = Path.Combine(root, "20200101T000000000Z-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(report, "{}");
            File.SetLastWriteTimeUtc(report, DateTime.UtcNow.AddDays(-40));
            return report;
        }

        private static void WithKeepAll(bool on, Action test)
        {
            Assert.True(DiagnosticLog.Flush(10000));
            LogRetentionPolicy.KeepAllOverride = on;
            try { test(); }
            finally
            {
                DiagnosticLog.Flush(10000);
                LogRetentionPolicy.KeepAllOverride = false; // The test-process pin (see LogRetentionTestPin).
            }
        }

        private static void SetSize(string path, long size)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.SetLength(size);
        }

        private static void InRoot(Action<string> test)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-keep-all-" + Guid.NewGuid().ToString("N"));
            try { test(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void InDiagnostics(Action<string> test)
        {
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
