using System;
using System.IO;
using System.Linq;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Logging;
using Bimwright.Dwg.Server.Memory;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class MemoryLoggingTests
    {
        [Fact]
        public void JournalLogger_AppendsJsonlEntries()
        {
            using var temp = new TempDir();
            var logger = new JournalLogger(temp.Path);

            logger.Append(new JournalEntry
            {
                SessionId = "s1",
                EventName = "accepted_bake",
                Detail = "dwg_fix_note"
            });

            var path = Path.Combine(temp.Path, "journal.jsonl");
            Assert.True(File.Exists(path));
            Assert.Contains("accepted_bake", File.ReadAllText(path));
        }

        [Fact]
        public void PatternDetector_GroupsRepeatedSuccessfulToolUsage()
        {
            var context = SessionContext.Create("s1", "2024");
            var detector = new PatternDetector();

            var candidates = detector.Detect(context, new[]
            {
                new UsageEvent { Tool = "update_texts", ParamsHash = "same", Success = true },
                new UsageEvent { Tool = "update_texts", ParamsHash = "same", Success = true },
                new UsageEvent { Tool = "update_texts", ParamsHash = "same", Success = true },
                new UsageEvent { Tool = "update_texts", ParamsHash = "different", Success = true }
            });

            var candidate = Assert.Single(candidates);
            Assert.Equal("update_texts:same", candidate.ClusterKey);
            Assert.Equal(3, candidate.Count);
        }

        [Fact]
        public void McpSessionLog_AppendsEntriesAndSummaryGeneratorCountsThem()
        {
            using var temp = new TempDir();
            var sessionLog = new McpSessionLog(temp.Path, "s1");

            sessionLog.Append(new McpSessionLogEntry { Tool = "dwg_get_selected_texts", Success = true, DurationMs = 5 });
            sessionLog.Append(new McpSessionLogEntry { Tool = "dwg_update_texts", Success = false, DurationMs = 10 });

            var summary = SummaryGenerator.Generate(sessionLog.Path);

            Assert.Equal(2, summary.TotalCalls);
            Assert.Equal(1, summary.Successes);
            Assert.Equal(1, summary.Failures);
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwg-memory-" + Guid.NewGuid().ToString("N"));

            public TempDir()
            {
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); } catch { }
            }
        }
    }
}
