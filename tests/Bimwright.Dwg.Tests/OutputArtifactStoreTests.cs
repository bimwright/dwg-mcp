using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class OutputArtifactStoreTests : OutputArtifactTestScope
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        private JObject Write(string json, long file = 2048, long group = 8192, int count = 8)
            => OutputArtifactStore.Write(json, "test_result", ArtifactRoot, Now, file, group, count);
        private string Seed(DateTime time, string text, bool temporary = false)
        {
            Directory.CreateDirectory(ArtifactRoot);
            var path = Path.Combine(ArtifactRoot, "output-" + time.ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json" + (temporary ? ".tmp" : ""));
            File.WriteAllText(path, text, new UTF8Encoding(false)); return path;
        }
        private static void Complete(JObject receipt, string expected)
        {
            Assert.Equal("available", receipt.Value<string>("status")); Assert.True(receipt.Value<bool>("complete"));
            var bytes = File.ReadAllBytes(receipt.Value<string>("path"));
            Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
            Assert.Equal(bytes.Length, receipt.Value<long>("bytes"));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), receipt.Value<string>("sha256"));
        }

        [Fact]
        public void Full_utf8_output_is_atomically_written_hashed_and_read_back_with_a_bounded_preview()
        {
            var json = new JObject { ["text"] = "Nguyễn-秘密🔐\n" + new string('x', 1000), ["ok"] = false }.ToString(Formatting.None);
            var receipt = Write(json); Complete(receipt, json);
            Assert.Equal("test_result", receipt.Value<string>("scope"));
            Assert.True(receipt.Value<string>("preview").Length <= 512); Assert.False(receipt.Value<bool>("preview_complete"));
            Assert.Equal(7, receipt["retention"].Value<int>("max_age_days"));
            Assert.Empty(Directory.GetFiles(ArtifactRoot, "*.tmp"));
        }

        [Fact]
        public void Too_large_output_does_not_create_partial_artifacts_or_invent_identity()
        {
            var receipt = Write(new string('中', 100), file: 200);
            Assert.False(receipt.Value<bool>("complete")); Assert.Equal("artifact_size_limit", receipt.Value<string>("reason"));
            Assert.Equal(JTokenType.Null, receipt["path"].Type); Assert.Equal(JTokenType.Null, receipt["sha256"].Type);
            Assert.False(Directory.Exists(ArtifactRoot));
        }

        [Fact]
        public void Age_count_and_byte_retention_only_remove_exact_owned_files()
        {
            var expired = Seed(Now.AddDays(-8), new string('a', 40));
            var oldest = Seed(Now.AddHours(-2), new string('b', 40));
            var newest = Seed(Now.AddHours(-1), new string('c', 40));
            var partial = Seed(Now, "orphaned partial", true);
            var personal = Path.Combine(ArtifactRoot, "personal.json"); File.WriteAllText(personal, "keep personal");
            var subdir = Path.Combine(ArtifactRoot, "captures"); Directory.CreateDirectory(subdir); File.WriteAllText(Path.Combine(subdir, "user.dwg"), "keep drawing");
            var result = Write(new string('d', 40), file: 64, group: 85, count: 2); Complete(result, new string('d', 40));
            Assert.False(File.Exists(expired)); Assert.False(File.Exists(oldest)); Assert.False(File.Exists(partial));
            Assert.True(File.Exists(newest)); Assert.Equal("keep personal", File.ReadAllText(personal));
            Assert.Equal("keep drawing", File.ReadAllText(Path.Combine(subdir, "user.dwg")));
            Assert.Equal(2, Directory.GetFiles(ArtifactRoot, "output-*.json").Length);
        }

        [Fact]
        public void Locked_retention_blocks_growth_then_recovers_after_unlock_without_retrying_the_tool()
        {
            var retained = Seed(Now.AddDays(-8), "old output");
            using (var held = new FileStream(retained, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = Write("new output"); Assert.False(result.Value<bool>("complete"));
                Assert.Single(Directory.GetFiles(ArtifactRoot));
            }
            Complete(Write("new output"), "new output"); Assert.False(File.Exists(retained));
        }

        [Fact]
        public void Storage_error_is_an_unavailable_receipt_with_no_partial_payload()
        {
            File.WriteAllText(ArtifactRoot, "not a directory");
            try
            {
                var result = Write("full payload"); Assert.False(result.Value<bool>("complete"));
                Assert.Equal("storage_unavailable", result.Value<string>("reason"));
                Assert.Equal("not a directory", File.ReadAllText(ArtifactRoot));
            }
            finally { File.Delete(ArtifactRoot); }
        }

        [Fact]
        public void Concurrent_writers_have_unique_complete_files_and_enforce_the_count_cap()
        {
            var results = new JObject[12];
            Parallel.For(0, results.Length, i => results[i] = Write("{\"value\":" + i + "}", count: 16));
            for (var i = 0; i < results.Length; i++) Complete(results[i], "{\"value\":" + i + "}");
            Assert.Equal(12, results.Select(x => x.Value<string>("path")).Distinct().Count());
            var last = Write("last", count: 2); Complete(last, "last");
            Assert.Equal(2, Directory.GetFiles(ArtifactRoot).Length);
        }

        [Fact]
        public void Background_maintenance_returns_without_waiting_for_storage_and_prunes_on_a_later_start()
        {
            var expired = Seed(DateTime.UtcNow.AddDays(-8), "expired");
            BoundedLogFile.WithLock(Path.Combine(ArtifactRoot, "outputs.lock"), () =>
            {
                var watch = Stopwatch.StartNew(); OutputArtifactStore.MaintainInBackground();
                Assert.True(watch.ElapsedMilliseconds < 500);
                Thread.Sleep(350); Assert.True(File.Exists(expired)); return true;
            });
            OutputArtifactStore.MaintainInBackground();
            Assert.True(SpinWait.SpinUntil(() => !File.Exists(expired), TimeSpan.FromSeconds(3)));
        }

        [Fact]
        public void Linked_artifact_roots_are_refused_without_writing_or_pruning_the_target()
        {
            Directory.CreateDirectory(ArtifactRoot);
            var target = Path.Combine(ArtifactRoot, "target"); Directory.CreateDirectory(target);
            var personal = Path.Combine(target, "personal.json"); File.WriteAllText(personal, "keep");
            var link = Path.Combine(ArtifactRoot, "linked");
            var start = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null" }) start.ArgumentList.Add(arg);
            using (var process = Process.Start(start)) { Assert.True(process.WaitForExit(5000)); Assert.Equal(0, process.ExitCode); }
            try
            {
                var receipt = OutputArtifactStore.Write("payload", "test", link, Now, 2048, 4096, 4);
                Assert.False(receipt.Value<bool>("complete")); Assert.Equal("keep", File.ReadAllText(personal));
                Assert.Single(Directory.GetFiles(target));
            }
            finally { Directory.Delete(link); }
        }
    }

    public class FileOutputReceiptTests : OutputArtifactTestScope
    {
        [Fact]
        public void Readback_distinguishes_new_changed_and_unchanged_content()
        {
            Directory.CreateDirectory(ArtifactRoot); var path = Path.Combine(ArtifactRoot, "drawing.dxf");
            var before = FileOutputReceipt.Observe(path); File.WriteAllText(path, "first");
            var created = FileOutputReceipt.Verify(path, before); Assert.True(created.Value<bool>("verified")); Assert.Equal("verified_new", created.Value<string>("status"));
            before = FileOutputReceipt.Observe(path); File.WriteAllText(path, "first");
            var unchanged = FileOutputReceipt.Verify(path, before); Assert.False(unchanged.Value<bool>("verified")); Assert.False(unchanged.Value<bool>("new_or_changed"));
            File.WriteAllText(path, "changed");
            var changed = FileOutputReceipt.Verify(path, before); Assert.True(changed.Value<bool>("verified")); Assert.Equal("verified_changed", changed.Value<string>("status"));
        }

        [Fact]
        public void Missing_or_unreadable_measurements_remain_null_and_do_not_claim_fresh_output()
        {
            Directory.CreateDirectory(ArtifactRoot); var path = Path.Combine(ArtifactRoot, "drawing.dxf");
            var missing = FileOutputReceipt.Verify(path, FileOutputReceipt.Observe(path));
            Assert.False(missing.Value<bool>("verified")); Assert.Equal(JTokenType.Null, missing["bytes"].Type); Assert.Equal(JTokenType.Null, missing["sha256"].Type);
            File.WriteAllText(path, "exists"); JObject unavailable;
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) unavailable = FileOutputReceipt.Observe(path);
            Assert.Equal(JTokenType.Null, unavailable["exists"].Type); Assert.Equal(JTokenType.Null, unavailable["sha256"].Type);
            var after = FileOutputReceipt.Verify(path, unavailable);
            Assert.False(after.Value<bool>("verified")); Assert.Equal(JTokenType.Null, after["new_or_changed"].Type);
        }
    }
}
