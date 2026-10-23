using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class DrawingOpenTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_one_second_open_budget_reaches_both_transports_and_timeout_warns_not_to_retry(bool pipe)
    {
        using var process = Process.GetCurrentProcess();
        var processStart = process.StartTime.ToUniversalTime();
        const string documentId = "open-budget-document";
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        var pipeName = "dwg-open-budget-" + Guid.NewGuid().ToString("N");
        if (!pipe) listener.Start();
        var discovery = pipe ? new DiscoveryInfo { Transport = "pipe", PipeName = pipeName, Token = "fixture" }
            : new DiscoveryInfo { Port = ((IPEndPoint)listener.LocalEndpoint).Port, Token = "fixture" };
        discovery.Pid = process.Id;
        discovery.ProcessStartUtc = processStart;
        var server = Task.Run(async () =>
        {
            for (var requestIndex = 0; requestIndex < 2; requestIndex++)
            {
                // Health and open use separate client connections; do not reuse a broken pipe instance.
                using var pipeServer = pipe ? new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous) : null;
                using var tcp = pipe ? null : await listener.AcceptTcpClientAsync(stop.Token);
                if (pipe) await pipeServer.WaitForConnectionAsync(stop.Token);
                Stream stream = pipe ? pipeServer : tcp.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, true, -1, leaveOpen: true);
                var request = JObject.Parse(await reader.ReadLineAsync(stop.Token));
                Assert.Equal("fixture", request.Value<string>("auth"));
                if (requestIndex == 0)
                {
                    Assert.Equal("health", request.Value<string>("cmd"));
                    var response = new JObject
                    {
                        ["id"] = request["id"], ["ok"] = true,
                        ["result"] = new JObject
                        {
                            ["ok"] = true, ["target"] = new JObject
                            {
                                ["protocol"] = 1, ["pid"] = process.Id,
                                ["process_start_utc"] = processStart,
                                ["document_id"] = documentId
                            }
                        }
                    };
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                    await writer.WriteLineAsync(response.ToString(Newtonsoft.Json.Formatting.None));
                    await writer.FlushAsync(stop.Token);
                }
                else
                {
                    Assert.Equal("open_drawing", request.Value<string>("cmd"));
                    Assert.Equal(1, request["params"].Value<int>("timeout_seconds"));
                    Assert.Equal(process.Id, request["target"].Value<int>("pid"));
                    Assert.Equal(processStart, request["target"].Value<DateTime>("process_start_utc"));
                    Assert.Equal(documentId, request["target"].Value<string>("document_id"));
                    Assert.True(request["target"].Value<bool>("selection_confirmed"));
                    // No open response: the client's deadline must close this connection.
                }
                await reader.ReadToEndAsync(stop.Token);
            }
        });
        try
        {
            var client = new PluginClient(() => discovery);
            var target = await client.SwitchTargetAsync("pid:" + process.Id);
            Assert.True(target.Value<bool>("selection_confirmed"));
            Assert.Equal(documentId, target.Value<string>("document_id"));
            var response = await client.SendAsync("open_drawing", new { path = @"C:\Models\Plant.dwg", timeout_seconds = 1 });
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(response.Ok);
            Assert.Contains("does not cancel", response.Error);
            Assert.Contains("before retrying", response.Error);
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await server.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (ex is OperationCanceledException || ex is IOException || ex is ObjectDisposedException) { }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(901)]
    public async Task Invalid_open_wait_never_resolves_or_connects_to_a_host(int seconds)
    {
        var discoveryCalls = 0;
        var client = new PluginClient(() => { discoveryCalls++; throw new InvalidOperationException("should not resolve"); });
        var response = await client.SendAsync("open_drawing", new { timeout_seconds = seconds });
        Assert.False(response.Ok);
        Assert.Equal(0, discoveryCalls);
        Assert.Contains("timeout_seconds", response.Error);
    }

    [Fact]
    public void Catalog_reports_the_long_open_wait_without_changing_other_tool_budgets()
    {
        var catalog = ToolCatalogBuilder.Build(new[] { typeof(MetaTools) });
        Assert.Equal(600, catalog["tools"].Single(tool => tool.Value<string>("name") == "dwg_open_drawing").Value<int>("timeout_seconds"));
        Assert.Equal(30, catalog["tools"].Single(tool => tool.Value<string>("name") == "dwg_list_recent_drawings").Value<int>("timeout_seconds"));
    }
}
