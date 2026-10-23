using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Guard contracts through the real SDK with per-test tool instances and stream transports.</summary>
public class ToolArgumentGuardTests
{
    [Theory]
    [InlineData("{\"height\":900}")]
    [InlineData("{\"height\":\"900\"}")]
    public async Task Valid_values_keep_sdk_numeric_coercion_and_dispatch_once(string arguments)
    {
        await using var h = await Harness.StartAsync();
        var result = await h.CallAsync("fixture_required", arguments);
        Assert.NotEqual(true, result.Value<bool?>("isError"));
        Assert.Equal(900d, Text(result).Value<double>("height"));
        Assert.Null(Text(result)["_ignored_arguments"]);
        Assert.Equal(1, h.Tools.Calls);
    }

    [Fact]
    public async Task Nested_arguments_are_preserved_without_recursive_key_validation()
    {
        await using var h = await Harness.StartAsync();
        const string arguments = "{\"arbitrary\":[{\"height\":900,\"extra\":\"untouched\"}],\"code\":\"body stays nested\"}";
        var result = await h.CallAsync("fixture_nested", "{\"arguments\":" + arguments + "}");
        Assert.True(JToken.DeepEquals(JObject.Parse(arguments), Text(result)["seen"]));
        Assert.Null(Text(result)["_ignored_arguments"]);
        Assert.Equal(1, h.Tools.Calls);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("{}")]
    public async Task Explicit_additional_properties_allow_extras_without_warning(string additional)
    {
        await using var h = await Harness.StartAsync("fixture_required",
            "{\"type\":\"object\",\"properties\":{\"height\":{\"type\":\"number\"}},\"required\":[\"height\"],\"additionalProperties\":" + additional + "}");
        var result = await h.CallAsync("fixture_required", "{\"height\":900,\"extra\":\"kept by schema\"}");
        Assert.Equal(900d, Text(result).Value<double>("height"));
        Assert.Null(Text(result)["_ignored_arguments"]);
        Assert.Equal(1, h.Tools.Calls);
    }

    [Fact]
    public async Task Schema_without_properties_keeps_sdk_behavior()
    {
        await using var h = await Harness.StartAsync("fixture_optional", "{\"type\":\"object\"}");
        var result = await h.CallAsync("fixture_optional", "{\"extra\":\"SDK decides\"}");
        Assert.Equal(5, Text(result).Value<int>("limit"));
        Assert.Null(Text(result)["_ignored_arguments"]);
        Assert.Equal(1, h.Tools.Calls);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    public async Task Untyped_property_schema_does_not_break_rejection_diagnostics(string property)
    {
        await using var h = await Harness.StartAsync("fixture_required",
            "{\"type\":\"object\",\"properties\":{\"height\":" + property + "},\"required\":[\"height\"]}");
        var result = await h.CallAsync("fixture_required", "{}");
        Assert.True(result.Value<bool>("isError"));
        Assert.Equal("any", Text(result)["invalid_arguments"]["expected"][0].Value<string>("type"));
        Assert.Equal(0, h.Tools.Calls);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("argument")]
    public async Task Tool_body_exceptions_are_not_reclassified_as_argument_rejections(string kind)
    {
        await using var h = await Harness.StartAsync();
        var result = await h.CallAsync("fixture_throw", "{\"kind\":\"" + kind + "\"}");
        Assert.True(result.Value<bool>("isError"));
        Assert.DoesNotContain("INVALID_ARGUMENT", result.ToString());
        Assert.DoesNotContain("invalid_arguments", result.ToString());
        Assert.Equal(1, h.Tools.Calls);
    }

    [Fact]
    public async Task Invalid_json_type_remains_an_sdk_binding_error()
    {
        await using var h = await Harness.StartAsync();
        var result = await h.CallAsync("fixture_required", "{\"height\":{\"not\":\"a number\"}}");
        Assert.True(result.Value<bool>("isError"));
        Assert.DoesNotContain("INVALID_ARGUMENT", result.ToString());
        Assert.Equal(0, h.Tools.Calls);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("mixed")]
    [InlineData("plain")]
    public async Task Warning_preserves_image_blocks_plain_text_and_error_status(string mode)
    {
        await using var h = await Harness.StartAsync();
        var result = await h.CallAsync("fixture_content", "{\"mode\":\"" + mode + "\",\"extra\":\"not echoed\"}");
        Assert.True(result.Value<bool>("isError"));
        var texts = result["content"].Where(x => x.Value<string>("type") == "text").Select(x => x.Value<string>("text")).ToArray();
        Assert.Contains(texts, x => x.Contains("_ignored_arguments"));
        Assert.DoesNotContain("not echoed", result.ToString());
        if (mode == "plain") Assert.StartsWith("Error: fixture text", Assert.Single(texts));
        else
        {
            var image = Assert.Single(result["content"].Where(x => x.Value<string>("type") == "image"));
            Assert.Equal(Fixture.Image, image.Value<string>("data"));
            Assert.Equal("image/png", image.Value<string>("mimeType"));
            if (mode == "mixed") Assert.Equal("original", JObject.Parse(Assert.Single(texts)).Value<string>("reason"));
        }
        Assert.Equal(1, h.Tools.Calls);
    }

    [Fact]
    public async Task Rejection_response_and_log_are_bounded_valid_key_only_json()
    {
        await using var h = await Harness.StartAsync();
        var arguments = new JObject(Enumerable.Range(0, 40).Select(i =>
            new JProperty("extra_" + i + new string('k', 80), "never-return-this-value")));
        var result = await h.CallAsync("fixture_required", arguments.ToString());
        var invalid = Text(result)["invalid_arguments"];
        Assert.Equal(32, invalid["received"].Count());
        Assert.Equal(32, invalid["unknown"].Count());
        Assert.All(invalid["received"].Values<string>(), key => Assert.Equal(64, key.Length));
        Assert.DoesNotContain("never-return-this-value", result.ToString());
        var logs = h.Logs();
        Assert.Equal(2, logs.Length);
        Assert.Equal(32, logs[0]["params"]["received_keys"].Count());
        Assert.Equal(32, logs[0]["params"]["unknown"].Count());
        Assert.DoesNotContain("never-return-this-value", string.Join("\n", logs.Select(x => x.ToString())));
        Assert.Equal(0, h.Tools.Calls);
    }

    [Theory]
    [InlineData("{\"Code\":\"private-code-marker\"}")]
    [InlineData("{\"payload\":{\"code\":\"private-code-marker\"}}")]
    public async Task Rejection_logging_contains_only_keys_and_paired_failure_metadata(string arguments)
    {
        await using var h = await Harness.StartAsync();
        var result = await h.CallAsync("fixture_code", arguments);
        Assert.True(result.Value<bool>("isError"));
        Assert.False(Text(result).Value<bool>("ok"));
        Assert.Equal(0, h.Tools.Calls);
        var logs = h.Logs();
        Assert.Equal(2, logs.Length);
        Assert.Equal("fixture_code", logs[0].Value<string>("tool"));
        Assert.True(logs[0]["params"].Value<bool>("rejected"));
        Assert.Equal("missing_required", logs[0]["params"].Value<string>("reason"));
        Assert.Equal("code", Assert.Single(logs[0]["params"]["missing"].Values<string>()));
        Assert.Equal(logs[0]["request_id"], logs[1]["request_id"]);
        Assert.Equal("finish", logs[1].Value<string>("phase"));
        Assert.False(logs[1].Value<bool>("success"));
        Assert.Null(logs[0]["params"]["code"]);
        Assert.DoesNotContain("private-code-marker", result.ToString());
        Assert.DoesNotContain("private-code-marker", string.Join("\n", logs.Select(x => x.ToString())));
    }

    [Fact]
    public async Task Logging_off_creates_no_log_for_rejection()
    {
        await using var h = await Harness.StartAsync(logging: false);
        var result = await h.CallAsync("fixture_required", "{}");
        Assert.StartsWith("INVALID_ARGUMENT:", Text(result).Value<string>("error"));
        Assert.Empty(h.Logs());
        Assert.Equal(0, h.Tools.Calls);
    }

    [Fact]
    public async Task Valid_call_cannot_spoof_guard_rejection_logging()
    {
        await using var h = await Harness.StartAsync();
        var result = await h.CallAsync("fixture_code", "{\"code\":\"private-code-marker\",\"rejected\":true,\"reason\":\"missing_required\"}");
        Assert.True(Text(result).Value<bool>("ok"));
        Assert.NotNull(Text(result)["_ignored_arguments"]);
        Assert.Equal(1, h.Tools.Calls);
        Assert.Empty(h.Logs()); // This fixture has no body logger; the guard must not create a rejection log.
        Assert.DoesNotContain("private-code-marker", result.ToString());
    }

    private static JObject Text(JObject result) => JObject.Parse(result["content"].First(x => x.Value<string>("type") == "text").Value<string>("text"));

    public sealed class Fixture
    {
        internal const string Image = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6xkAAAAASUVORK5CYII=";
        internal int Calls;

        [McpServerTool(Name = "fixture_required")]
        public string Required(double height, string label = null)
        { Calls++; return new JObject { ["ok"] = true, ["height"] = height, ["label"] = label }.ToString(); }

        [McpServerTool(Name = "fixture_nested")]
        public string Nested(JsonElement arguments)
        { Calls++; return new JObject { ["ok"] = true, ["seen"] = JToken.Parse(arguments.GetRawText()) }.ToString(); }

        [McpServerTool(Name = "fixture_optional")]
        public string Optional(int limit = 5)
        { Calls++; return new JObject { ["ok"] = true, ["limit"] = limit }.ToString(); }

        [McpServerTool(Name = "fixture_code")]
        public string Code(string code)
        { Calls++; return "{\"ok\":true}"; }

        [McpServerTool(Name = "fixture_throw")]
        public string Throw(string kind)
        { Calls++; throw kind == "json" ? new System.Text.Json.JsonException("body error") : new ArgumentException("body error", "arguments"); }

        [McpServerTool(Name = "fixture_content")]
        public CallToolResult Content(string mode)
        {
            Calls++;
            if (mode == "plain") return new CallToolResult { IsError = true, Content = new[] { new TextContentBlock { Text = "Error: fixture text" } } };
            ContentBlock image = ImageContentBlock.FromBytes(Convert.FromBase64String(Image), "image/png");
            return new CallToolResult
            {
                IsError = true,
                Content = mode == "image" ? new[] { image }
                    : new ContentBlock[] { image, new TextContentBlock { Text = "{\"ok\":false,\"reason\":\"original\"}" } }
            };
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-guard-sdk-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        private NamedPipeServerStream input;
        private NamedPipeClientStream output;
        private StreamReader reader;
        private IHost host;
        private int id = 10;
        internal Fixture Tools { get; } = new();

        internal static async Task<Harness> StartAsync(string schemaTool = null, string schema = null, bool logging = true)
        {
            var h = new Harness();
            try { await h.Initialize(schemaTool, schema, logging); return h; }
            catch { await h.DisposeAsync(); throw; }
        }

        private async Task Initialize(string schemaTool, string schema, bool logging)
        {
            var pipe = "dwg-guard-sdk-" + Guid.NewGuid().ToString("N");
            input = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            output = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(input.WaitForConnectionAsync(deadline.Token), output.ConnectAsync(deadline.Token));
            var tools = typeof(Fixture).GetMethods().Where(x => x.GetCustomAttribute<McpServerToolAttribute>() != null)
                .Select(method => McpServerTool.Create(method, _ => Tools)).ToArray();
            if (schemaTool != null)
                tools.Single(x => x.ProtocolTool.Name == schemaTool).ProtocolTool.InputSchema = JsonSerializer.Deserialize<JsonElement>(schema);
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            var mcp = builder.Services.AddMcpServer().WithStreamServerTransport(input, input).WithTools(tools);
            ToolArgumentGuard.Register(mcp, new ServerLogger(Path.Combine(root, "calls.jsonl"), () => logging));
            host = builder.Build();
            await host.StartAsync(deadline.Token);
            reader = new StreamReader(output, new UTF8Encoding(false));
            await Request("initialize", JObject.Parse("{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"guard-sdk-tests\",\"version\":\"1\"}}"));
            await output.WriteAsync(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n"), deadline.Token);
            await output.FlushAsync(deadline.Token);
        }

        internal async Task<JObject> CallAsync(string name, string arguments)
            => (JObject)(await Request("tools/call", new JObject { ["name"] = name, ["arguments"] = JObject.Parse(arguments) }))["result"];

        private async Task<JObject> Request(string method, JObject parameters)
        {
            var requestId = id++;
            var message = new JObject { ["jsonrpc"] = "2.0", ["id"] = requestId, ["method"] = method, ["params"] = parameters };
            await output.WriteAsync(Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None) + "\n"), deadline.Token);
            await output.FlushAsync(deadline.Token);
            while (true)
            {
                var line = await reader.ReadLineAsync(deadline.Token);
                if (line == null) throw new EndOfStreamException();
                var response = JObject.Parse(line);
                if (response.Value<int?>("id") == requestId) return response;
            }
        }

        internal JObject[] Logs()
        {
            var path = Path.Combine(root, "calls.jsonl");
            return File.Exists(path) ? File.ReadAllLines(path).Select(JObject.Parse).ToArray() : Array.Empty<JObject>();
        }

        public async ValueTask DisposeAsync()
        {
            try { if (host != null) { await host.StopAsync(TimeSpan.FromSeconds(5)); host.Dispose(); } }
            finally
            {
                reader?.Dispose(); output?.Dispose(); input?.Dispose(); deadline.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
