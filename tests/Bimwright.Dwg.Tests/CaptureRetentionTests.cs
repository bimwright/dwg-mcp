using System;
using System.IO;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // PruneGenerated reads the process-wide keep-all switch: share the serialized collection that pins it.
    [Collection("Script handlers")]
    public class CaptureRetentionTests
    {
        private const string Guid32 = "0123456789abcdef0123456789abcdef";

        [Fact]
        public void PruneGenerated_drops_old_then_oldest_generated_captures_and_nothing_else()
        {
            var folder = Path.Combine(Path.GetTempPath(), "dwg-capture-prune-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            var now = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
            string Make(string name, int ageDays, int bytes)
            {
                var path = Path.Combine(folder, name);
                File.WriteAllBytes(path, new byte[bytes]);
                File.SetLastWriteTimeUtc(path, now.AddDays(-ageDays));
                return path;
            }
            try
            {
                var expired = Make("dwg-capture-20261001-101010-001-" + Guid32 + ".png", 40, 10);
                var oldest = Make("dwg-capture-20261101-101010-002-" + Guid32 + ".jpg", 20, 400);
                var middle = Make("dwg-capture-20261110-101010-003-" + Guid32 + ".bmp", 11, 400);
                var newest = Make("dwg-capture-20261121-101010-004-" + Guid32 + ".png", 1, 400);
                var named = Make("before-fix.png", 90, 400);
                var lookalike = Make("dwg-capture-notes.png", 90, 400);
                var nested = Make(Path.Combine("sub", "dwg-capture-20261001-101010-005-" + Guid32 + ".png"), 90, 400);

                // Budget 900: the expired file goes by age, then the oldest until the rest fits.
                Assert.Equal(2, CaptureRetention.PruneGenerated(folder, newest, now, days: 30, budget: 900));
                Assert.False(File.Exists(expired));
                Assert.False(File.Exists(oldest));
                foreach (var kept in new[] { middle, newest, named, lookalike, nested }) Assert.True(File.Exists(kept), kept);

                // The capture just written survives even when it alone exceeds the budget.
                Assert.Equal(1, CaptureRetention.PruneGenerated(folder, newest, now, days: 30, budget: 100));
                Assert.True(File.Exists(newest));
                Assert.False(File.Exists(middle));

                Assert.Equal(0, CaptureRetention.PruneGenerated(Path.Combine(folder, "missing"), null, now));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void PruneGenerated_keeps_every_capture_when_keep_all_is_on()
        {
            var folder = Path.Combine(Path.GetTempPath(), "dwg-capture-keepall-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var old = Path.Combine(folder, "dwg-capture-20200101-101010-001-" + Guid32 + ".png");
            File.WriteAllBytes(old, new byte[400]);
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-400));
            LogRetentionPolicy.KeepAllOverride = true;
            try
            {
                Assert.Equal(0, CaptureRetention.PruneGenerated(folder, null, DateTime.UtcNow, days: 30, budget: 1));
                Assert.True(File.Exists(old));
            }
            finally
            {
                LogRetentionPolicy.KeepAllOverride = false; // the test-process pin
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void Default_directory_is_the_dwg_mcp_captures_folder()
            => Assert.EndsWith(Path.Combine("Bimwright", "dwg-mcp", "captures"), CaptureRetention.DefaultDirectory);
    }
}
