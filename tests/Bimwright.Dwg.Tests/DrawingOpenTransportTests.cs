using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        var pipeName = "dwg-open-budget-" + Guid.NewGuid().ToString("N");
        using var pipeServer = pipe ? new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous) : null;
        if (!pipe) listener.Start();
        var discovery = pipe ? new DiscoveryInfo { Transport = "pipe", PipeName = pipeName, Token = "fixture" }
            : new DiscoveryInfo { Port = ((IPEndPoint)listener.LocalEndpoint).Port, Token = "fixture" };
        var server = Task.Run(async () =>
        {
            using var tcp = pipe ? null : await listener.AcceptTcpClientAsync();
            if (pipe) await pipeServer.WaitForConnectionAsync();
            Stream stream = pipe ? pipeServer : tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, true, -1, leaveOpen: true);
            var request = JObject.Parse(await reader.ReadLineAsync());
            Assert.Equal("open_drawing", request.Value<string>("cmd"));
            Assert.Equal(1, request["params"].Value<int>("timeout_seconds"));
            // Deliberately no response: wait for the client to close after its
            // response deadline, without leaving timer tasks or background writes.
            await reader.ReadToEndAsync();
        });
        var client = new PluginClient(() => discovery);
        var response = await client.SendAsync("open_drawing", new { path = @"C:\Models\Plant.dwg", timeout_seconds = 1 });
        await server.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(response.Ok);
        Assert.Contains("does not cancel", response.Error);
        Assert.Contains("before retrying", response.Error);
        listener.Stop();
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
