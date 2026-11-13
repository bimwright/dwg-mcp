using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    /// <summary>Real stdio server, no Autodesk/client installation or host calls.</summary>
    public class RuntimeSwitchProtocolTests
    {
        [Theory]
        [InlineData("", 49, true, true)]
        [InlineData("--disable-send-code", 48, false, true)]
        [InlineData("--read-only --enable-send-code", 19, false, false)]
        [InlineData("--toolsets all", 73, true, true)]
        [InlineData("--toolsets all --disable-send-code", 72, false, true)]
        [InlineData("--toolsets all --read-only --enable-send-code", 25, false, false)]
        [InlineData("--toolsets all --disable-toolbaker", 67, true, true)]
        [InlineData("--toolsets query", 11, false, false)]
        [InlineData("--toolsets meta", 11, true, true)]
        [InlineData("--toolsets code", 2, true, true)]
        [InlineData("--toolsets meta,code", 11, true, true)]
        [InlineData("--toolsets meta --disable-send-code", 10, false, true)]
        [InlineData("--toolsets code --disable-send-code", 1, false, true)]
        [InlineData("--toolsets view --read-only", 3, false, false)]
        [InlineData("--toolsets block --read-only", 2, false, false)]
        [InlineData("--toolsets drawing --read-only", 1, false, false)]
        public async Task Tools_list_honors_switches_over_stdio(string flags, int count, bool code, bool lisp)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-stdio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(Path.Combine(root, "absent-config.json"));
            start.ArgumentList.Add("--disable-call-log");
            foreach (var flag in flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)) start.ArgumentList.Add(flag);
            // Don't inherit the owner's runtime preferences. No discovery calls are
            // made: the only tools/call below is refused locally before dispatch.
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["LOCALAPPDATA"] = root;
            using var process = Process.Start(start);
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                var initialize = await Request(process, 1, "initialize", new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new { },
                    clientInfo = new { name = "dwg-offline-test", version = "1.0.0" }
                });
                Assert.Equal(Program.ServerInformationalVersion, initialize["result"]["serverInfo"]["version"].Value<string>());
                await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                await process.StandardInput.FlushAsync();
                var listed = await Request(process, 2, "tools/list", new { });
                var names = ((JArray)listed["result"]["tools"]).Select(tool => tool.Value<string>("name")).ToArray();
                Assert.Equal(count, names.Length);
                Assert.Equal(code, names.Contains("dwg_send_code"));
                Assert.Equal(lisp, names.Contains("dwg_run_lisp"));
                var instructions = initialize["result"].Value<string>("instructions");
                Assert.Equal(names.Contains("dwg_report_task_result"),
                    instructions.Contains(ServerInstructions.Reporting, StringComparison.Ordinal));
                var config = DwgMcpConfig.Load(flags.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                    Path.Combine(root, "absent-config.json"), _ => null);
                var previousConfig = ServerState.Config;
                ServerState.Config = config;
                try
                {
                    // The plugin catalog is the list RegisterToolsets stored, not a second resolver pass.
                    var services = new ServiceCollection();
                    var register = typeof(Program).GetMethod("RegisterToolsets", BindingFlags.NonPublic | BindingFlags.Static);
                    Assert.NotNull(register);
                    register.Invoke(null, new object[] { services.AddMcpServer(), ToolsetFilter.Resolve(config) });
                    var pluginCatalog = ToolCatalogBuilder.Build(
                        ServerState.EnabledToolTypes, config.EnableSendCodeOrDefault, config.ReadOnlyOrDefault);
                    Assert.Equal(pluginCatalog["tools"].Select(tool => tool.Value<string>("name")).OrderBy(name => name),
                        names.OrderBy(name => name));
                }
                finally
                {
                    ServerState.EnabledToolTypes = Array.Empty<Type>();
                    ServerState.Config = previousConfig;
                }
                foreach (var tool in (JArray)listed["result"]["tools"])
                {
                    if (tool.Value<string>("name") == "dwg_send_code")
                    {
                        Assert.Null(tool["annotations"]);
                        Assert.Null(tool["_meta"]?["anthropic/requiresUserInteraction"]);
                        continue;
                    }
                    foreach (var hint in new[] { "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint" })
                        Assert.Equal(JTokenType.Boolean, tool["annotations"]?[hint]?.Type);
                    Assert.Equal(tool.Value<string>("name") is "dwg_health" or "dwg_apply_unicode_style" or "dwg_attach_image",
                        tool["annotations"].Value<bool>("openWorldHint"));
                }
                if (flags.Contains("--read-only"))
                {
                    var unfilteredConfig = DwgMcpConfig.Load(flags.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                        Path.Combine(root, "absent-config.json"), _ => null);
                    unfilteredConfig.ReadOnly = false;
                    var resolver = typeof(Program).GetMethod("ResolveToolTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Static);
                    var allTypes = (System.Collections.Generic.IEnumerable<Type>)resolver.Invoke(null,
                        new object[] { ToolsetFilter.Resolve(unfilteredConfig), false });
                    var readNames = allTypes.SelectMany(type => type.GetMethods())
                        .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                        .Where(tool => tool != null && tool.ReadOnly).Select(tool => tool.Name).OrderBy(name => name).ToArray();
                    Assert.Equal(readNames, names.OrderBy(name => name).ToArray());
                    Assert.All((JArray)listed["result"]["tools"], tool => Assert.True(tool["annotations"].Value<bool>("readOnlyHint")));
                    foreach (var blockedTool in new[] { "dwg_capture_view_image", "dwg_inspect_view_region", "dwg_restore_view",
                        "dwg_batch_execute", "dwg_run_baked_tool", "dwg_send_code", "dwg_open_drawing", "dwg_activate_document", "dwg_switch_target" })
                    {
                        await process.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(new
                        { jsonrpc = "2.0", id = 10, method = "tools/call", @params = new { name = blockedTool, arguments = new { } } }));
                        await process.StandardInput.FlushAsync();
                        var refused = JObject.Parse(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)));
                        Assert.Equal(10, refused.Value<int>("id"));
                        Assert.True(refused["error"] != null || refused["result"]?.Value<bool?>("isError") == true);
                    }
                }
                if (!code && lisp && names.Contains("dwg_batch_execute"))
                {
                    var blocked = await Request(process, 3, "tools/call", new
                    {
                        name = "dwg_batch_execute",
                        arguments = new { commands = "[{\"cmd\":\"create_line\"},{\"cmd\":\"send_code\",\"params\":{\"code\":\"return 1;\"}}]" }
                    });
                    var text = blocked["result"]["content"][0]["text"].Value<string>();
                    Assert.False(JObject.Parse(text).Value<bool>("ok"));
                    Assert.Contains("disabled", text);
                }
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(0, process.ExitCode);
                Assert.Empty(Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories));
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await stderr;
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task<JObject> Request(Process process, int id, string method, object parameters)
        {
            await process.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(new { jsonrpc = "2.0", id, method, @params = parameters }));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.NotNull(line);
                var message = JObject.Parse(line); // Any non-JSON stdout fails.
                if (message.Value<int?>("id") == id)
                {
                    Assert.Null(message["error"]);
                    return message;
                }
            }
        }
    }
}
