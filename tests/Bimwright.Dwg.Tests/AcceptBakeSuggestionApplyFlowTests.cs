using System;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.ToolBaker;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class AcceptBakeSuggestionApplyFlowTests
    {
        [Fact]
        public async Task AcceptBakeSuggestion_CallsPluginApplyAndRecordsAcceptedTool()
        {
            using var temp = new TempDir();
            var paths = new BakePaths(temp.Path);
            using var db = new BakeDb(paths);
            db.Migrate();
            db.UpsertSuggestion(new BakeSuggestionRecord
            {
                Id = "s1",
                ClusterKey = "cluster",
                Source = "preset",
                Title = "Fix notes",
                Description = "Repeated update_texts call",
                State = BakeSuggestionStates.Open,
                Score = 0.9,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
                PayloadJson = new JObject
                {
                    ["tool"] = "update_texts",
                    ["sample"] = new JObject
                    {
                        ["parameter_kinds"] = new JObject { ["items"] = "array" }
                    }
                }.ToString()
            });

            var response = await AcceptBakeSuggestionHandler.HandleAsync(
                db,
                "s1",
                "dwg_fix_note",
                pluginApply: request => Task.FromResult(new JObject
                {
                    ["success"] = true,
                    ["tool_name"] = (string)request["tool_name"],
                    ["description"] = (string)request["description"],
                    ["params_schema"] = (string)request["params_schema"],
                    ["source_code"] = (string)request["source_code"]
                }));

            var json = JObject.Parse(response);
            Assert.True(json.Value<bool>("ok"));
            Assert.Equal("dwg_fix_note", json.Value<string>("tool_name"));
            Assert.Equal(BakeSuggestionStates.Accepted, db.GetSuggestion("s1").State);
            Assert.Single(db.ReadRegistryRecords());
        }

        [Fact]
        public async Task AcceptBakeSuggestion_DoesNotPersistWhenPluginApplyFails()
        {
            using var temp = new TempDir();
            var paths = new BakePaths(temp.Path);
            using var db = new BakeDb(paths);
            db.Migrate();
            InsertUpdateTextsSuggestion(db, "s1");

            var response = await AcceptBakeSuggestionHandler.HandleAsync(
                db,
                "s1",
                "dwg_fix_note",
                pluginApply: request => Task.FromResult(new JObject
                {
                    ["success"] = false,
                    ["error_code"] = "compile_or_smoke_test_failed",
                    ["message"] = "Baked tool smoke test failed"
                }));

            var json = JObject.Parse(response);
            Assert.False(json.Value<bool>("ok"));
            Assert.Equal("compile_or_smoke_test_failed", json.Value<string>("error_code"));
            Assert.Equal(BakeSuggestionStates.Open, db.GetSuggestion("s1").State);
            Assert.Empty(db.ReadRegistryRecords());
        }

        [Fact]
        public async Task AcceptBakeSuggestion_InvalidSchemaOverrideReturnsFailure()
        {
            using var temp = new TempDir();
            var paths = new BakePaths(temp.Path);
            using var db = new BakeDb(paths);
            db.Migrate();
            InsertUpdateTextsSuggestion(db, "s1");

            var response = await AcceptBakeSuggestionHandler.HandleAsync(
                db,
                "s1",
                "dwg_fix_note",
                paramsSchema: "{",
                pluginApply: request => Task.FromResult(new JObject { ["success"] = true }));

            var json = JObject.Parse(response);
            Assert.False(json.Value<bool>("ok"));
            Assert.Equal("invalid_params_schema", json.Value<string>("error_code"));
            Assert.Empty(db.ReadRegistryRecords());
        }

        private static void InsertUpdateTextsSuggestion(BakeDb db, string id)
        {
            db.UpsertSuggestion(new BakeSuggestionRecord
            {
                Id = id,
                ClusterKey = "cluster-" + id,
                Source = "preset",
                Title = "Fix notes",
                Description = "Repeated update_texts call",
                State = BakeSuggestionStates.Open,
                Score = 0.9,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
                PayloadJson = new JObject
                {
                    ["tool"] = "update_texts",
                    ["sample"] = new JObject
                    {
                        ["parameter_kinds"] = new JObject { ["items"] = "array" }
                    }
                }.ToString()
            });
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwg-accept-" + Guid.NewGuid().ToString("N"));

            public TempDir()
            {
                System.IO.Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { System.IO.Directory.Delete(Path, recursive: true); } catch { }
            }
        }
    }
}
