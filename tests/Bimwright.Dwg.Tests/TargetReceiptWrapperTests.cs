using System;
using System.IO;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class TargetReceiptWrapperTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Bake_accept_preserves_observed_receipt_through_success_or_failure_and_keeps_legacy_absence(bool ok, bool observed)
    {
        var root = Path.Combine(Path.GetTempPath(), "dwg-target-wrapper-" + Guid.NewGuid().ToString("N"));
        var previousPaths = ToolBakerTools.PathsOverride;
        var previousSend = ToolGateway.SendOverride;
        var previousConfig = ServerState.Config;
        var target = new JObject { ["pid"] = 123, ["acad_year"] = 2024, ["document_id"] = "observed-document" };
        var paths = new BakePaths(root);
        try
        {
            using (var db = new BakeDb(paths))
            {
                db.Migrate();
                db.UpsertSuggestion(new BakeSuggestionRecord
                {
                    Id = "s1", ClusterKey = "cluster", Source = "preset", Title = "Fix notes",
                    Description = "Controlled update_texts suggestion",
                    State = BakeSuggestionStates.Open, Score = 0.9,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("o"), UpdatedAt = DateTimeOffset.UtcNow.ToString("o"),
                    PayloadJson = new JObject { ["tool"] = "update_texts", ["sample"] = new JObject
                    { ["parameter_kinds"] = new JObject { ["items"] = "array" } } }.ToString()
                });
            }
            ToolBakerTools.PathsOverride = paths;
            ServerState.Config = new DwgMcpConfig();
            var calls = 0;
            ToolGateway.SendOverride = (command, request) =>
            {
                calls++;
                Assert.Equal("apply_bake", command);
                return Task.FromResult(new McpResponse { Ok = ok, Error = ok ? null : "apply failed",
                    Result = new { success = true }, Target = observed ? target : null });
            };
            var response = JObject.Parse(await ToolBakerWriteTools.AcceptBakeSuggestion("s1", "dwg_receipt_test"));
            Assert.Equal(1, calls);
            Assert.Equal(ok, response.Value<bool>("ok"));
            if (observed) Assert.True(JToken.DeepEquals(target, response["_target"]));
            else Assert.Null(response["_target"]);
            using var readback = new BakeDb(paths);
            Assert.Equal(ok ? BakeSuggestionStates.Accepted : BakeSuggestionStates.Open, readback.GetSuggestion("s1").State);
        }
        finally
        {
            ToolBakerTools.PathsOverride = previousPaths;
            ToolGateway.SendOverride = previousSend;
            ServerState.Config = previousConfig;
            using var fixturePool = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                { DataSource = paths.BakeDb, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate, Pooling = true }.ToString());
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(fixturePool);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
