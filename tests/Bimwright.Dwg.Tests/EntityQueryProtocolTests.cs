using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Actual production query handler against doubles, through TCP + real gateway + real SDK.</summary>
[Collection("Script handlers")]
public class EntityQueryProtocolTests : IDisposable
{
    private readonly DocumentCollection previous = Application.DocumentManager;
    public EntityQueryProtocolTests() { DrawingRevision.Shutdown(); Application.DocumentManager = new DocumentCollection(); }

    [Fact]
    public async Task Byte_trimmed_real_SDK_pages_union_without_skips_and_cursor_attests_last_delivered_handle()
    {
        var document = Model();
        await using var gateway = await Gateway.StartAsync(document);
        var args = new JObject { ["paged"] = true, ["fields"] = "[\"handle\",\"text\",\"position\"]", ["max_output_bytes"] = 8192 };
        for (var i = 0; i < 32; i++) args["ignored_" + i + new string('k', 56)] = "private-value-never-echo";
        var all = new List<string>(); var pages = 0;
        while (true)
        {
            var reply = await gateway.CallAsync("dwg_query_entities", args);
            Assert.True(Encoding.UTF8.GetByteCount(gateway.LastRawResult) <= 8192);
            var typed = System.Text.Json.JsonSerializer.Deserialize<CallToolResult>(gateway.LastRawResult, McpJsonUtilities.DefaultOptions);
            Assert.Equal(Encoding.UTF8.GetByteCount(gateway.LastRawResult), ResponseBudget.Measure(typed));
            var body = Body(reply); Assert.True(body.Value<bool>("ok")); var page = body["result"];
            var handles = page["entities"].Select(x => x.Value<string>("handle")).ToArray();
            Assert.NotEmpty(handles); Assert.Equal(handles.Length, page.Value<int>("count")); all.AddRange(handles);
            Assert.DoesNotContain("_checkpoints", gateway.LastRawResult); Assert.DoesNotContain("_continuation_allowed", gateway.LastRawResult);
            Assert.DoesNotContain("private-value-never-echo", gateway.LastRawResult);
            if (!page["output"].Value<bool>("has_more")) break;
            var cursor = page["output"].Value<string>("next_cursor");
            Assert.Equal(ulong.Parse(handles.Last(), NumberStyles.HexNumber), EntityQueryCursor.Read(cursor, page.Value<string>("snapshot"), page.Value<string>("query_hash")));
            args = (JObject)page["output"]["next_calls"][0]["arguments"].DeepClone();
            Assert.Equal(cursor, args.Value<string>("cursor")); Assert.True(++pages < 100);
        }
        Assert.True(pages > 1); Assert.Equal(40, all.Count); Assert.Equal(40, all.Distinct().Count());
        Assert.Equal(all.OrderBy(value => ulong.Parse(value, NumberStyles.HexNumber)), all);
        Assert.Equal(0, document.Database.ProfileMutations);
    }

    [Theory]
    [InlineData(false, "CURSOR_QUERY_MISMATCH:")]
    [InlineData(true, "QUERY_CHANGED:")]
    public async Task Changed_cursor_context_is_refused_by_actual_host_before_a_transaction(bool mutate, string prefix)
    {
        var document = Model(); await using var gateway = await Gateway.StartAsync(document);
        var first = Body(await gateway.CallAsync("dwg_query_entities", new JObject { ["paged"] = true, ["limit"] = 2, ["max_output_bytes"] = 8192 }))["result"];
        var next = (JObject)first["output"]["next_calls"][0]["arguments"].DeepClone();
        if (mutate) document.Database.Raise("modified", new Line()); else next["fields"] = "[\"handle\",\"text\"]";
        var starts = document.Database.ProfileTransactionStarts;
        var error = Body(await gateway.CallAsync("dwg_query_entities", next));
        Assert.False(error.Value<bool>("ok")); Assert.StartsWith(prefix, error.Value<string>("error"));
        Assert.Equal(starts, document.Database.ProfileTransactionStarts);
    }

    [Fact]
    public async Task Spatial_count_through_real_SDK_exposes_unknown_extents_and_INSUNITS()
    {
        var document = Model();
        var records = (BlockTable)document.Database.BlockTableId.Slot.Value;
        var model = (BlockTableRecord)records[BlockTableRecord.ModelSpace].Slot.Value;
        ((Entity)model.ProfileEntities[0].Slot.Value).QueryExtentsFailure = new InvalidOperationException("No extent");
        await using var gateway = await Gateway.StartAsync(document);
        var body = Body(await gateway.CallAsync("dwg_count_entities", new JObject { ["bbox"] = "{\"min\":[-1,-1,-1],\"max\":[100,100,100]}", ["space"] = "model" }));
        Assert.True(body.Value<bool>("ok")); var result = body["result"];
        Assert.Equal(JTokenType.Null, result["count"].Type); Assert.Equal(39, result.Value<int>("matched_count"));
        Assert.Equal(1, result["coverage"].Value<int>("extents_unavailable")); Assert.Equal(4, result["units"].Value<int>("insunits_code"));
        Assert.True(Encoding.UTF8.GetByteCount(gateway.LastRawResult) <= 16384);
    }

    [Fact]
    public async Task Unknown_count_byte_option_is_ignored_instead_of_changing_its_fixed_cap()
    {
        var document = Model(); var db = document.Database; var name = new string('界', 100);
        var blocks = (BlockTable)db.BlockTableId.Slot.Value;
        var layout = db.ProfileAdd(new Layout { LayoutName = name });
        var paper = new BlockTableRecord { Name = "*Paper_Space", IsLayout = true, LayoutId = layout };
        var id = db.ProfileAdd(paper); blocks.ProfileRecords.Add(id); blocks.ProfileNames.Add(paper.Name, id);
        await using var gateway = await Gateway.StartAsync(document);
        var args = new JObject { ["space"] = "layout:" + name, ["max_output_bytes"] = 4096 };
        for (var i = 0; i < 32; i++) args["unknown_" + i + new string('k', 55)] = "not echoed";
        var body = Body(await gateway.CallAsync("dwg_count_entities", args));
        Assert.True(body.Value<bool>("ok")); Assert.Equal(1, body["result"].Value<int>("query_count_version"));
        Assert.Equal(0, body["result"].Value<int>("count"));
        Assert.Contains(body["_ignored_arguments"]["unknown"], item => item.Value<string>("key") == "max_output_bytes");
        Assert.InRange(Encoding.UTF8.GetByteCount(gateway.LastRawResult), 4097, 16384);
    }

    [Theory]
    [InlineData("dwg_query_entities")]
    [InlineData("dwg_count_entities")]
    public async Task Legacy_plugin_success_without_spatial_contract_is_refused_without_claiming_data(string tool)
    {
        await using var gateway = await Gateway.StartAsync(Model(), legacyReply: true);
        var reply = await gateway.CallAsync(tool, new JObject { ["space"] = "model" });
        var body = Body(reply);
        Assert.True(reply["result"].Value<bool>("isError"));
        Assert.False(body.Value<bool>("ok")); Assert.StartsWith("QUERY_FEATURE_UNSUPPORTED:", body.Value<string>("error"));
        Assert.Null(body["result"]["count"]); Assert.Null(body["result"]["entities"]);
        Assert.Empty(body["result"]["output"]["next_calls"]);
        // The same older host reply still serves legacy calls without new options.
        var legacy = Body(await gateway.CallAsync(tool, new JObject()));
        Assert.True(legacy.Value<bool>("ok")); Assert.Equal(40, legacy["result"].Value<int>("count"));
    }

    private static JObject Body(JObject reply) => JObject.Parse(reply["result"]["content"].OfType<JObject>().First(x => x.Value<string>("type") == "text").Value<string>("text"));
    private static Document Model()
    {
        var db = new Database { Insunits = UnitsValue.Millimeters }; var blocks = new BlockTable();
        db.BlockTableId = db.ProfileAdd(blocks); var space = new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true };
        var id = db.ProfileAdd(space); blocks.ProfileRecords.Add(id); blocks.ProfileNames.Add(BlockTableRecord.ModelSpace, id);
        for (var i = 0; i < 40; i++) space.ProfileEntities.Add(db.ProfileAdd(new DBText
        {
            Layer = "A", TextString = new string('界', 150) + " \"+<>&", Position = new Point3d(i, 0, 0),
            QueryVertices = new[] { new Point3d(i, 0, 0), new Point3d(i + 1, 1, 1) }
        }));
        space.ProfileEntities.Reverse(); db.ProfileResetMetrics();
        return new Document { Name = "SDK-simulated.dwg", Database = db };
    }
    public void Dispose() { DrawingRevision.Shutdown(); Application.DocumentManager = previous; }

    internal sealed class Gateway : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-entity-query-gateway-" + Guid.NewGuid().ToString("N"));
        private Process process;
        private FakePluginServer host;
        private Task<string> stderr;
        private int id = 10;
        internal string LastRawResult;
        internal ConcurrentQueue<JObject> Requests { get; } = new();

        internal static async Task<Gateway> StartAsync(Document document, bool legacyReply = false)
        {
            var gateway = new Gateway();
            try { await gateway.Initialize(document, legacyReply); return gateway; }
            catch { await gateway.DisposeAsync(); throw; }
        }

        private async Task Initialize(Document document, bool legacyReply)
        {
            using var owner = Process.GetCurrentProcess();
            var ownerId = owner.Id;
            var startTime = owner.StartTime.ToUniversalTime();
            host = new FakePluginServer(line =>
            {
                var request = JObject.Parse(line);
                Requests.Enqueue(request);
                CommandResult result;
                switch (request.Value<string>("cmd"))
                {
                    case "query_entities": result = new QueryEntitiesHandler().Execute(document, request["params"]); break;
                    case "count_entities": result = new CountEntitiesHandler().Execute(document, request["params"]); break;
                    case "get_drawing_profile": result = new DrawingProfileHandler().Execute(document, request["params"]); break;
                    case "get_selected_texts": result = legacyReply
                        ? CommandResult.Success(new JObject { ["clusters"] = new JArray(new JObject { ["id"] = 1, ["text"] = "legacy selection" }) })
                        : new GetSelectedTextsHandler().Execute(document, request["params"]); break;
                    case "health": result = CommandResult.Success(new JObject { ["ok"] = true, ["target"] = new JObject
                        { ["protocol"] = 1, ["pid"] = ownerId, ["process_start_utc"] = startTime, ["document_id"] = DocumentTargetGuard.Id(document) } }); break;
                    default: result = CommandResult.Success(new JObject { ["accepted"] = true }); break;
                }
                if (legacyReply && result.Ok && request.Value<string>("cmd") is "query_entities" or "count_entities")
                {
                    var data = (JObject)JToken.FromObject(result.Result);
                    result = CommandResult.Success(request.Value<string>("cmd") == "query_entities"
                        ? new JObject { ["count"] = 40, ["entities"] = data["entities"] ?? new JArray() }
                        : new JObject { ["count"] = 40 });
                }
                return new JObject { ["id"] = request["id"], ["ok"] = result.Ok,
                    ["result"] = result.Result == null ? JValue.CreateNull() : JToken.FromObject(result.Result), ["error"] = result.Error }.ToString(Formatting.None);
            });
            var profile = Path.Combine(root, "Bimwright", "dwg-mcp");
            Directory.CreateDirectory(profile);
            File.WriteAllText(Path.Combine(profile, "acad-2024-" + owner.Id + ".json"), JsonConvert.SerializeObject(new DiscoveryInfo
            {
                Target = "2024", TargetId = "acad-2024-" + owner.Id, Pid = owner.Id, ProcessStartUtc = startTime,
                Port = host.Port, Token = "fixture"
            }));
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach (var argument in new[] { "--config", Path.Combine(root, "absent.json"), "--toolsets", "all", "--target", "pid:" + owner.Id,
                                             "--disable-call-log", "--read-only", "--response-warn-bytes", "1", "--response-strong-warn-bytes", "1" }) start.ArgumentList.Add(argument);
            foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["LOCALAPPDATA"] = root;
            process = Process.Start(start);
            stderr = process.StandardError.ReadToEndAsync();
            await Request("initialize", new JObject
            {
                ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "dwg-entity-query-tests", ["version"] = "1" }
            });
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await process.StandardInput.FlushAsync();
            var listed = await Request("tools/list", new JObject());
            var runtime = listed["result"]["tools"].OfType<JObject>()
                .Single(x => x.Value<string>("name") == "dwg_health")["_meta"]["bimwright/dwg-mcp/runtime"];
            Assert.Equal(process.Id, runtime.Value<int>("process_id"));
            Assert.Equal(process.StartTime.ToUniversalTime(), runtime.Value<DateTime>("process_started_utc"));
            Assert.Equal(typeof(Program).Assembly.ManifestModule.ModuleVersionId, Guid.Parse(runtime.Value<string>("module_version_id")));
            Assert.Equal(Program.ServerInformationalVersion, runtime.Value<string>("server_informational_version"));
        }

        internal Task<JObject> CallAsync(string tool, JObject arguments)
            => Request("tools/call", new JObject { ["name"] = tool, ["arguments"] = arguments });

        internal Task<JObject> CallRawAsync(string parameters)
            => Request("tools/call", JObject.Parse(parameters));

        private async Task<JObject> Request(string method, JObject parameters)
        {
            var requestId = id++;
            await process.StandardInput.WriteLineAsync(new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = requestId, ["method"] = method, ["params"] = parameters
            }.ToString(Formatting.None));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert.NotNull(line);
                var reply = JObject.Parse(line);
                if (reply.Value<int?>("id") == requestId)
                {
                    using var parsed = System.Text.Json.JsonDocument.Parse(line);
                    LastRawResult = parsed.RootElement.TryGetProperty("result", out var result) ? result.GetRawText() : null;
                    return reply;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (process != null)
                {
                    process.StandardInput.Close();
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                    if (stderr != null) await stderr;
                    process.Dispose();
                }
            }
            finally
            {
                host?.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
