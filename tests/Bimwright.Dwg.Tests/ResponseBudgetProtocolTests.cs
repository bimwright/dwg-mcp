using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Final result bytes over the real SDK stream transport, with per-test tools and no CAD/file logging.</summary>
public class ResponseBudgetProtocolTests : OutputArtifactTestScope
{
    [Fact]
    public async Task Actual_wire_measurement_includes_unicode_structured_content_and_existing_metadata()
    {
        await using var h = await Harness.StartAsync(new DwgMcpConfig());
        var reply = await h.CallAsync("budget_rich", new JObject());
        var typed = reply.Typed();
        Assert.Equal(reply.Bytes, ResponseBudget.Measure(typed));
        Assert.Equal("中文 Tiếng Việt 😀 \"quoted\" \\path\n+<>&", Assert.Single(typed.Content.OfType<TextContentBlock>()).Text);
        Assert.Equal("😀", reply.Result["structuredContent"].Value<string>("seen"));
        Assert.Equal("中文", reply.Result["_meta"].Value<string>("source"));
        Assert.True(reply.Bytes > Encoding.UTF8.GetByteCount(Assert.Single(typed.Content.OfType<TextContentBlock>()).Text));
        Assert.Equal(1, h.Tools.Calls);
        Assert.False(h.HasFiles);
    }

    [Fact]
    public async Task Inner_unknown_argument_warning_is_included_in_the_final_wire_budget()
    {
        var config = Limits();
        await using var h = await Harness.StartAsync(config);
        var arguments = new JObject { ["value"] = new string('x', 3000) };
        foreach (var i in Enumerable.Range(0, 32)) arguments["extra_" + i + new string('k', 56)] = "do-not-echo-this-value";
        var reply = await h.CallAsync("budget_required", arguments);
        AssertFits(reply, config);
        Assert.Equal("compacted", Body(reply)["_response_budget"].Value<string>("status"));
        Assert.True(Body(reply)["_response_budget"].Value<int>("original_bytes") > config.ResponseBudgetBytesOrDefault);
        Assert.DoesNotContain("do-not-echo-this-value", reply.WireResult);
        Assert.Equal(1, h.Tools.Calls);
        Assert.False(h.HasFiles);
    }

    [Fact]
    public async Task Missing_argument_rejection_is_budgeted_without_invoking_the_tool()
    {
        var config = Limits();
        await using var h = await Harness.StartAsync(config);
        var arguments = new JObject(Enumerable.Range(0, 40).Select(i =>
            new JProperty("unknown_" + i + new string('k', 80), "private-argument-value")));
        var reply = await h.CallAsync("budget_required", arguments);
        AssertFits(reply, config);
        Assert.True(reply.Result.Value<bool>("isError"));
        Assert.False(Body(reply).Value<bool>("ok"));
        Assert.StartsWith("INVALID_ARGUMENT:", Body(reply).Value<string>("error"));
        Assert.Equal("compacted", Body(reply)["_response_budget"].Value<string>("status"));
        Assert.DoesNotContain("private-argument-value", reply.WireResult);
        Assert.Equal(0, h.Tools.Calls);
        Assert.False(h.HasFiles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_write_runs_once_and_retains_reported_status_with_no_replay_guidance(bool failed)
    {
        var config = Limits();
        await using var h = await Harness.StartAsync(config);
        var reply = await h.CallAsync("budget_write", new JObject { ["failed"] = failed });
        AssertFits(reply, config);
        var body = Body(reply);
        Assert.Equal(failed, reply.Result.Value<bool>("isError"));
        Assert.Equal(failed, body["_response_budget"].Value<bool>("original_is_error"));
        Assert.Equal(!failed, body.Value<bool>("ok"));
        Assert.Equal(failed, body["result"].Value<bool>("partial_failure"));
        Assert.False(body["result"].Value<bool>("transactional"));
        if (failed)
        {
            Assert.Equal(JTokenType.Null, body["result"]["mutation_applied"].Type);
            Assert.StartsWith("HOST_FAILURE:", body.Value<string>("error"));
        }
        else Assert.True(body["result"].Value<bool>("mutation_applied"));
        var guidance = body["_response_budget"].Value<string>("guidance");
        Assert.Contains("Do not replay", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Inspect", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, h.Tools.Calls);
        var artifact = body["output_artifact"];
        Assert.True(artifact.Value<bool>("complete"));
        Assert.StartsWith(ArtifactRoot, artifact.Value<string>("path"));
        var stored = JObject.Parse(File.ReadAllText(artifact.Value<string>("path")));
        Assert.Equal(failed, stored.Value<bool>("isError"));
        Assert.Equal(!failed, JObject.Parse(stored["content"][0].Value<string>("text")).Value<bool>("ok"));
        Assert.False(h.HasFiles);
    }

    [Fact]
    public async Task Oversized_read_returns_narrowing_guidance_within_the_delivered_budget()
    {
        var config = Limits();
        await using var h = await Harness.StartAsync(config);
        var reply = await h.CallAsync("budget_read", new JObject());
        AssertFits(reply, config);
        var body = Body(reply);
        Assert.Equal("compacted", body["_response_budget"].Value<string>("status"));
        Assert.Contains("Narrow", body["_response_budget"].Value<string>("guidance"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new string('界', 100), reply.WireResult);
        Assert.Equal(1, h.Tools.Calls);
        Assert.False(h.HasFiles);
    }

    [Fact]
    public async Task Below_budget_mixed_image_keeps_content_metadata_structured_content_and_error_status()
    {
        var config = Limits(8192);
        await using var h = await Harness.StartAsync(config);
        var reply = await h.CallAsync("budget_mixed", new JObject());
        AssertFits(reply, config);
        Assert.True(reply.Result.Value<bool>("isError"));
        var image = Assert.Single(reply.Result["content"].Where(block => block.Value<string>("type") == "image"));
        Assert.Equal(Fixture.Image, image.Value<string>("data"));
        Assert.Equal("image/png", image.Value<string>("mimeType"));
        Assert.Equal("original-text", Body(reply).Value<string>("status"));
        Assert.Equal("overview界", reply.Result["structuredContent"].Value<string>("view"));
        Assert.Equal("kept", reply.Result["_meta"].Value<string>("fixture"));
        Assert.NotNull(reply.Result["_meta"]["bimwright/dwg-mcp/response_budget"]);
        Assert.Equal(1, h.Tools.Calls);
    }

    [Fact]
    public async Task Optional_warning_does_not_replace_a_valid_near_cap_payload_or_existing_metadata()
    {
        var config = Limits();
        var empty = Fixture.RequiredResult("");
        var overhead = JsonSerializer.SerializeToUtf8Bytes(empty, McpJsonUtilities.DefaultOptions).Length;
        var value = new string('x', config.ResponseBudgetBytesOrDefault - overhead - 1);
        await using var h = await Harness.StartAsync(config);
        var reply = await h.CallAsync("budget_required", new JObject { ["value"] = value });
        AssertFits(reply, config);
        Assert.Equal(config.ResponseBudgetBytesOrDefault - 1, reply.Bytes);
        Assert.Equal(value, Body(reply)["result"].Value<string>("value"));
        Assert.Equal("required-result", reply.Result["_meta"].Value<string>("origin"));
        Assert.Null(reply.Result["_meta"]["bimwright/dwg-mcp/response_budget"]);
        Assert.Null(Body(reply)["_response_budget"]);
        Assert.Equal(1, h.Tools.Calls);
        Assert.False(h.HasFiles);
    }

    private static DwgMcpConfig Limits(int budget = 4096) => new()
    { ResponseWarnBytes = 1024, ResponseStrongWarnBytes = 2048, ResponseBudgetBytes = budget };

    private static JObject Body(Reply reply) => JObject.Parse(reply.Result["content"]
        .First(block => block.Value<string>("type") == "text").Value<string>("text"));

    private static void AssertFits(Reply reply, DwgMcpConfig config)
    {
        Assert.True(reply.Bytes <= config.ResponseBudgetBytesOrDefault,
            $"SDK delivered {reply.Bytes} result bytes for budget {config.ResponseBudgetBytesOrDefault}.");
        Assert.Equal(reply.Bytes, ResponseBudget.Measure(reply.Typed()));
    }

    public sealed class Fixture
    {
        internal const string Image = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6xkAAAAASUVORK5CYII=";
        internal int Calls;

        [McpServerTool(Name = "budget_rich", ReadOnly = true)]
        public CallToolResult Rich()
        {
            Calls++;
            return new CallToolResult
            {
                IsError = false,
                Content = new[] { new TextContentBlock { Text = "中文 Tiếng Việt 😀 \"quoted\" \\path\n+<>&" } },
                StructuredContent = JsonSerializer.Deserialize<JsonElement>("{\"seen\":\"😀\"}"),
                Meta = new JsonObject { ["source"] = "中文" }
            };
        }

        [McpServerTool(Name = "budget_required", ReadOnly = true)]
        public CallToolResult Required(string value) { Calls++; return RequiredResult(value); }

        internal static CallToolResult RequiredResult(string value)
        {
            var result = Text(new JObject { ["ok"] = true, ["result"] = new JObject { ["value"] = value } });
            result.Meta = new JsonObject { ["origin"] = "required-result" };
            return result;
        }

        [McpServerTool(Name = "budget_write", ReadOnly = false)]
        public CallToolResult Write(bool failed)
        {
            Calls++;
            return Text(new JObject
            {
                ["ok"] = !failed, ["error"] = failed ? "HOST_FAILURE: failure after earlier effects; inspect before replay." : null,
                ["result"] = new JObject
                {
                    ["partial_failure"] = failed, ["transactional"] = false,
                    ["mutation_applied"] = failed ? JValue.CreateNull() : new JValue(true),
                    ["detail"] = new string('界', 16000)
                }
            }, failed);
        }

        [McpServerTool(Name = "budget_read", ReadOnly = true)]
        public CallToolResult Read()
        { Calls++; return Text(new JObject { ["ok"] = true, ["result"] = new JObject { ["items"] = new string('界', 16000) } }); }

        [McpServerTool(Name = "budget_mixed", ReadOnly = true)]
        public CallToolResult Mixed()
        {
            Calls++;
            var result = Text(new JObject { ["ok"] = false, ["status"] = "original-text", ["detail"] = new string('界', 400) }, true);
            result.Content.Add(ImageContentBlock.FromBytes(Convert.FromBase64String(Image), "image/png"));
            result.StructuredContent = JsonSerializer.Deserialize<JsonElement>("{\"view\":\"overview界\"}");
            result.Meta = new JsonObject { ["fixture"] = "kept" };
            return result;
        }

        private static CallToolResult Text(JObject body, bool failed = false) => new()
        {
            IsError = failed,
            Content = new System.Collections.Generic.List<ContentBlock>
                { new TextContentBlock { Text = body.ToString(Newtonsoft.Json.Formatting.None) } }
        };
    }

    private sealed class Reply
    {
        internal string WireResult { get; }
        internal JObject Result { get; }
        internal int Bytes => Encoding.UTF8.GetByteCount(WireResult);
        internal Reply(string wireResult) { WireResult = wireResult; Result = JObject.Parse(wireResult); }
        internal CallToolResult Typed() => JsonSerializer.Deserialize<CallToolResult>(WireResult, McpJsonUtilities.DefaultOptions);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-response-budget-sdk-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        private NamedPipeServerStream input;
        private NamedPipeClientStream output;
        private StreamReader reader;
        private IHost host;
        private int id = 10;
        internal Fixture Tools { get; } = new();
        internal bool HasFiles => Directory.Exists(root);

        internal static async Task<Harness> StartAsync(DwgMcpConfig config)
        {
            var h = new Harness();
            try { await h.Initialize(config); return h; }
            catch { await h.DisposeAsync(); throw; }
        }

        private async Task Initialize(DwgMcpConfig config)
        {
            var pipe = "dwg-response-budget-sdk-" + Guid.NewGuid().ToString("N");
            input = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            output = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(input.WaitForConnectionAsync(deadline.Token), output.ConnectAsync(deadline.Token));
            var tools = typeof(Fixture).GetMethods().Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
                .Select(method => McpServerTool.Create(method, _ => Tools)).ToArray();
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            var mcp = builder.Services.AddMcpServer().WithStreamServerTransport(input, input).WithTools(tools);
            mcp = ResponseBudget.Register(mcp, config);
            ToolArgumentGuard.Register(mcp, new ServerLogger(Path.Combine(root, "calls.jsonl"), () => false));
            host = builder.Build();
            await host.StartAsync(deadline.Token);
            reader = new StreamReader(output, new UTF8Encoding(false));
            await Request("initialize", JObject.Parse("{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"response-budget-sdk-tests\",\"version\":\"1\"}}"));
            await output.WriteAsync(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n"), deadline.Token);
            await output.FlushAsync(deadline.Token);
        }

        internal async Task<Reply> CallAsync(string name, JObject arguments)
        {
            var line = await Request("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments });
            using var response = JsonDocument.Parse(line);
            Assert.False(response.RootElement.TryGetProperty("error", out _), line);
            return new Reply(response.RootElement.GetProperty("result").GetRawText());
        }

        private async Task<string> Request(string method, JObject parameters)
        {
            var requestId = id++;
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = requestId, ["method"] = method, ["params"] = parameters };
            await output.WriteAsync(Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None) + "\n"), deadline.Token);
            await output.FlushAsync(deadline.Token);
            while (true)
            {
                var line = await reader.ReadLineAsync(deadline.Token);
                if (line == null) throw new EndOfStreamException();
                using var response = JsonDocument.Parse(line);
                if (response.RootElement.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.Number
                    && responseId.GetInt32() == requestId) return line;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { if (host != null) { await host.StopAsync(TimeSpan.FromSeconds(5)); host.Dispose(); } }
            finally
            {
                reader?.Dispose(); output?.Dispose(); input?.Dispose(); deadline.Dispose();
                if (Directory.Exists(root))
                {
                    var resolved = Path.GetFullPath(root);
                    if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                        || !Path.GetFileName(resolved).StartsWith("dwg-response-budget-sdk-", StringComparison.Ordinal))
                        throw new InvalidOperationException("Unexpected response-budget fixture cleanup path.");
                    Directory.Delete(resolved, recursive: true);
                }
            }
        }
    }
}
