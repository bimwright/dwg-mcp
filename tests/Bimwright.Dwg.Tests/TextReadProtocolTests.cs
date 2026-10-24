using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class TextReadProtocolTests : IDisposable
{
    private readonly DocumentCollection previous = Application.DocumentManager;
    public TextReadProtocolTests()
    {
        DrawingRevision.Shutdown(); ClusterStateStore.Replace(Array.Empty<ClusterState>());
        Application.DocumentManager = new DocumentCollection();
    }

    [Fact]
    public async Task Whole_cluster_pages_cross_actual_gateway_SDK_without_selection_or_writable_state()
    {
        var (doc, handles) = Model();
        ClusterStateStore.Replace(new[] { new ClusterState { Id = 999 } });
        await using var gateway = await EntityQueryProtocolTests.Gateway.StartAsync(doc);
        var args = new JObject { ["handles"] = handles.ToString(Newtonsoft.Json.Formatting.None), ["includeEntities"] = true, ["max_output_bytes"] = 8192 };
        for (var i = 0; i < 32; i++) args["extra_" + i + new string('k', 60)] = "private-value";
        var keys = new List<string>(); var pages = 0;
        while (true)
        {
            var reply = await gateway.CallAsync("dwg_get_selected_texts", args);
            Assert.True(Encoding.UTF8.GetByteCount(gateway.LastRawResult) <= 8192);
            var typed = System.Text.Json.JsonSerializer.Deserialize<CallToolResult>(gateway.LastRawResult, McpJsonUtilities.DefaultOptions);
            Assert.Equal(Encoding.UTF8.GetByteCount(gateway.LastRawResult), ResponseBudget.Measure(typed));
            var body = Body(reply); Assert.True(body.Value<bool>("ok")); var page = body["result"];
            Assert.True(page.Value<bool>("read_only")); Assert.False(page.Value<bool>("writeback_ready"));
            Assert.True(page.Value<bool>("legacy_cluster_state_preserved")); Assert.True(ClusterStateStore.HasState());
            var clusters = page["clusters"].ToArray(); Assert.NotEmpty(clusters);
            foreach (var cluster in clusters)
            {
                Assert.Null(cluster["id"]); Assert.Null(cluster["rewrite_mode"]); Assert.Equal(1, cluster.Value<int>("entity_count"));
                keys.Add(cluster.Value<string>("cluster_key"));
            }
            Assert.DoesNotContain("_checkpoints", gateway.LastRawResult); Assert.DoesNotContain("private-value", gateway.LastRawResult);
            if (!page["output"].Value<bool>("has_more")) break;
            var cursor = page["output"].Value<string>("next_cursor");
            var ordinal = int.Parse(keys.Last().Split(':').Last(), NumberStyles.HexNumber);
            Assert.Equal((ulong)ordinal, EntityQueryCursor.Read(cursor, page.Value<string>("snapshot"), page.Value<string>("query_hash")));
            Assert.Equal("dwg_get_selected_texts", page["output"]["next_calls"][0].Value<string>("tool"));
            args = (JObject)page["output"]["next_calls"][0]["arguments"].DeepClone();
            Assert.Null(args["paged"]); Assert.Null(args["fields"]); Assert.True(++pages < 100);
        }
        Assert.True(pages > 0); Assert.Equal(40, keys.Count); Assert.Equal(40, keys.Distinct().Count());
        Assert.Equal(0, doc.Editor.TextReadSelectCalls); Assert.Equal(0, doc.Editor.TextReadSetSelectionCalls);
        Assert.Equal(0, doc.Database.ProfileMutations);
    }

    [Fact]
    public async Task Older_plugin_selection_payload_is_refused_for_explicit_sources()
    {
        var (doc, _) = Model(); await using var gateway = await EntityQueryProtocolTests.Gateway.StartAsync(doc, legacyReply: true);
        var reply = await gateway.CallAsync("dwg_get_selected_texts", new JObject { ["handles"] = "[]" });
        var body = Body(reply); Assert.False(body.Value<bool>("ok"));
        Assert.StartsWith("TEXT_FEATURE_UNSUPPORTED:", body.Value<string>("error"));
        Assert.True(reply["result"].Value<bool>("isError")); Assert.Null(body["result"]["clusters"]);
        Assert.Empty(body["result"]["output"]["next_calls"]);
        Assert.True(Body(await gateway.CallAsync("dwg_get_selected_texts", new JObject())).Value<bool>("ok"));
    }

    [Fact]
    public async Task Profile_region_query_and_text_grouping_complete_without_manual_selection()
    {
        var (doc, _) = Model(); await using var gateway = await EntityQueryProtocolTests.Gateway.StartAsync(doc);
        var profile = Body(await gateway.CallAsync("dwg_get_drawing_profile", new JObject { ["space"] = "model" }));
        Assert.True(profile.Value<bool>("ok")); Assert.Equal(40, profile["result"].Value<int>("exact_total_count"));
        const string box = "{\"min\":[-1,-1,-1],\"max\":[4500,4500,1]}";
        var entities = Body(await gateway.CallAsync("dwg_query_entities", new JObject { ["bbox"] = box, ["fields"] = "[\"handle\",\"type\"]" }));
        Assert.True(entities.Value<bool>("ok")); Assert.Equal(5, entities["result"].Value<int>("exact_match_count"));
        var text = Body(await gateway.CallAsync("dwg_get_selected_texts", new JObject { ["bbox"] = box, ["groupingStrength"] = "weak" }));
        Assert.True(text.Value<bool>("ok")); Assert.Equal("bbox", text["result"]["source"].Value<string>("kind"));
        Assert.Equal(5, text["result"].Value<int>("exact_match_count")); Assert.True(text["result"]["output"].Value<bool>("complete"));
        Assert.True(Encoding.UTF8.GetByteCount(gateway.LastRawResult) <= 16384);
        Assert.Equal(0, doc.Editor.TextReadSelectCalls); Assert.Equal(0, doc.Editor.TextReadSetSelectionCalls);
    }

    [Fact]
    public async Task Model_text_cursor_survives_navigation_but_refuses_entity_changes()
    {
        var (doc, handles) = Model(); await using var gateway = await EntityQueryProtocolTests.Gateway.StartAsync(doc);
        var first = Body(await gateway.CallAsync("dwg_get_selected_texts", new JObject { ["handles"] = handles.ToString(), ["max_output_bytes"] = 8192 }))["result"];
        var next = (JObject)first["output"]["next_calls"][0]["arguments"].DeepClone();
        doc.Database.Raise("modified", new ViewportTableRecord());
        var second = Body(await gateway.CallAsync("dwg_get_selected_texts", next)); Assert.True(second.Value<bool>("ok"));
        doc.Database.Raise("modified", new Line()); var starts = doc.Database.ProfileTransactionStarts;
        var error = Body(await gateway.CallAsync("dwg_get_selected_texts", next));
        Assert.False(error.Value<bool>("ok")); Assert.StartsWith("QUERY_CHANGED:", error.Value<string>("error"));
        Assert.Equal(starts, doc.Database.ProfileTransactionStarts);
    }

    private static JObject Body(JObject reply) => JObject.Parse(reply["result"]["content"].First(x => x.Value<string>("type") == "text").Value<string>("text"));
    private static (Document Document, JArray Handles) Model()
    {
        var db = new Database { Insunits = UnitsValue.Millimeters }; var blocks = new BlockTable(); db.BlockTableId = db.ProfileAdd(blocks);
        var space = new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true };
        var spaceId = db.ProfileAdd(space); blocks.ProfileRecords.Add(spaceId); blocks.ProfileNames.Add(space.Name, spaceId);
        var handles = new JArray();
        for (var i = 0; i < 40; i++)
        {
            var x = i * 1000d; var y = i * 1000d;
            var text = new DBText { Layer = "A", Height = 1, TextString = new string('界', 100), Position = new Point3d(x, y, 0),
                QueryVertices = new[] { new Point3d(x, y, 0), new Point3d(x + 10, y + 1, 0) } };
            space.ProfileEntities.Add(db.ProfileAdd(text)); handles.Add(text.Handle.ToString());
        }
        space.ProfileEntities.Reverse(); db.ProfileResetMetrics();
        return (new Document { Name = "SDK-text-simulated.dwg", Database = db }, handles);
    }
    public void Dispose()
    {
        DrawingRevision.Shutdown(); ClusterStateStore.Replace(Array.Empty<ClusterState>()); Application.DocumentManager = previous;
    }
}
