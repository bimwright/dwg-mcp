using System;
using System.IO;
using Bimwright.Dwg.Plugin.ToolBaker;
using Bimwright.Dwg.Server.Bake;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BakeDbTests
    {
        [Fact]
        public void BakePaths_UsesDwgBakedRoot()
        {
            using var temp = new TempDir();

            var paths = new BakePaths(temp.Path);

            Assert.EndsWith(Path.Combine("Bimwright", "Dwg", "baked", "bake.db"), paths.BakeDb);
        }

        [Fact]
        public void BakeDb_MigratesAndRoundtripsRegistryAndSuggestions()
        {
            using var temp = new TempDir();
            var paths = new BakePaths(temp.Path);
            using var db = new BakeDb(paths);
            db.Migrate();

            var inserted = db.TryInsertRegistryRecord(new BakedToolRecord
            {
                Name = "dwg_note_fix",
                Description = "Fix notes",
                Source = "preset",
                ParamsSchema = "{\"type\":\"object\"}",
                SourceCode = "{}",
                ReviewedByUser = true
            });

            Assert.True(inserted);
            Assert.Single(db.ReadRegistryRecords());
            Assert.Equal("dwg_note_fix", db.GetRegistryRecord("dwg_note_fix").Name);

            db.UpsertSuggestion(new BakeSuggestionRecord
            {
                Id = "s1",
                ClusterKey = "cluster",
                Source = "preset",
                Title = "Fix common notes",
                Description = "Repeated update_texts call",
                State = BakeSuggestionStates.Open,
                Score = 0.9,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
                PayloadJson = new JObject { ["tool"] = "update_texts" }.ToString()
            });

            var suggestion = Assert.Single(db.ListSuggestions());
            Assert.Equal("s1", suggestion.Id);
            Assert.True(db.TryUpdateSuggestionState("s1", BakeSuggestionStates.Accepted));
            Assert.Equal(BakeSuggestionStates.Accepted, db.GetSuggestion("s1").State);
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwg-bake-" + Guid.NewGuid().ToString("N"));

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
