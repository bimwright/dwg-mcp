using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class TargetReceiptTests : IDisposable
{
    private readonly DocumentCollection previous = Application.DocumentManager;
    private readonly Document original = new() { Name = "original.dwg" };
    public TargetReceiptTests()
    {
        Application.DocumentManager = new DocumentCollection { MdiActiveDocument = original };
        DocumentInvoker.Initialize();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Drawing_receipt_uses_the_locked_execution_document_even_if_handler_changes_active_drawing(bool fail)
    {
        JObject observed = null;
        var uiThread = Environment.CurrentManagedThreadId;
        var observedThread = 0;
        var activeLocks = 0;
        var other = new Document { Name = "other.dwg" };
        CommandResult Execute(Document doc)
        {
            Assert.Same(original, doc);
            Application.DocumentManager.MdiActiveDocument = other;
            if (fail) throw new InvalidOperationException("handler failed");
            return CommandResult.Success(new { unchanged = 42 });
        }
        void Observe(JObject target)
        {
            observed = target;
            observedThread = Environment.CurrentManagedThreadId;
            activeLocks = original.ActiveLockCount;
        }
        if (fail) Assert.Equal("handler failed", Assert.Throws<InvalidOperationException>(() =>
            DocumentInvoker.Invoke(Execute, observeTarget: Observe)).Message);
        else Assert.True(DocumentInvoker.Invoke(Execute, observeTarget: Observe).Ok);
        Assert.Equal(uiThread, observedThread);
        Assert.Equal(1, activeLocks);
        Assert.Equal(0, original.ActiveLockCount);
        Assert.Equal(DocumentTargetGuard.Id(original), observed.Value<string>("document_id"));
        Assert.Equal("original.dwg", observed.Value<string>("document_title"));
        using var process = Process.GetCurrentProcess();
        Assert.Equal(process.Id, observed.Value<int>("pid"));
        Assert.Equal(process.StartTime.ToUniversalTime(), observed.Value<DateTime>("process_start_utc"));
        Assert.Equal(int.Parse(PluginTarget.AutoCadYear), observed.Value<int>("acad_year"));
        Assert.Equal("acad-" + PluginTarget.AutoCadYear + "-" + process.Id, observed.Value<string>("target_id"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Queued_drawing_switch_or_reopen_keeps_guard_and_reports_actual_observation(bool reopen)
    {
        var expected = DocumentTargetGuard.Capture(original);
        var changed = new Document { Name = reopen ? original.Name : "changed.dwg" };
        JObject observed = null;
        using var entered = new ManualResetEventSlim();
        var pending = Task.Run(() =>
        {
            entered.Set();
            return DocumentInvoker.Invoke(doc => { doc.MutationCount++; return true; }, expectedTarget: expected,
                command: "send_code", observeTarget: target => observed = target);
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        Application.DocumentManager.MdiActiveDocument = changed;
        Assert.True(SpinWait.SpinUntil(() => { Application.RaiseIdle(); return pending.IsCompleted; }, TimeSpan.FromSeconds(3)));
        var error = Assert.IsType<InvalidOperationException>(Assert.Single(pending.Exception.InnerExceptions));
        Assert.StartsWith("DOCUMENT_CHANGED:", error.Message);
        Assert.Equal(0, original.MutationCount + changed.MutationCount);
        Assert.Equal(DocumentTargetGuard.Id(changed), observed.Value<string>("document_id"));
        Assert.NotEqual(expected.Value<string>("document_id"), observed.Value<string>("document_id"));
        Assert.Equal(changed.Name, observed.Value<string>("document_title"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Session_receipt_observes_active_drawing_after_success_or_exception_without_lock(bool fail)
    {
        var opened = new Document { Name = "opened.dwg" };
        JObject observed = null;
        int Execute()
        {
            Application.DocumentManager.MdiActiveDocument = opened;
            if (fail) throw new InvalidOperationException("partial open");
            return 42;
        }
        if (fail) Assert.Equal("partial open", Assert.Throws<InvalidOperationException>(() =>
            DocumentInvoker.InvokeSession(Execute, observeTarget: target => observed = target)).Message);
        else Assert.Equal(42, DocumentInvoker.InvokeSession(Execute, observeTarget: target => observed = target));
        Assert.Equal(DocumentTargetGuard.Id(opened), observed.Value<string>("document_id"));
        Assert.Equal(0, original.LockCount + opened.LockCount);
    }

    [Fact]
    public void Start_page_session_receipt_has_observed_process_and_null_drawing()
    {
        Application.DocumentManager.MdiActiveDocument = null;
        JObject observed = null;
        Assert.Equal(42, DocumentInvoker.InvokeSession(() => 42, observeTarget: target => observed = target));
        Assert.Equal(Environment.ProcessId, observed.Value<int>("pid"));
        Assert.Null(observed.Value<string>("document_id"));
        Assert.Null(observed.Value<string>("document_title"));
        var wire = JObject.Parse(observed.ToString(Formatting.None));
        Assert.Equal(JTokenType.Null, wire["document_id"].Type);
        Assert.Equal(JTokenType.Null, wire["document_title"].Type);
    }

    [Fact]
    public void Stopped_or_unavailable_document_does_not_fabricate_an_observation()
    {
        JObject observed = null;
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(_ => ++calls,
            isAccepting: () => false, observeTarget: target => observed = target));
        Assert.Throws<InvalidOperationException>(() => DocumentInvoker.InvokeSession(() => ++calls,
            isAccepting: () => false, observeTarget: target => observed = target));
        Application.DocumentManager.MdiActiveDocument = null;
        Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(_ => ++calls, observeTarget: target => observed = target));
        Assert.Equal(0, calls);
        Assert.Null(observed);
    }

    [Fact]
    public void Receipt_callback_failure_cannot_bypass_document_guard()
    {
        var expected = DocumentTargetGuard.Capture(new Document { Name = "elsewhere.dwg" });
        var error = Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(doc => ++doc.MutationCount,
            expectedTarget: expected, command: "send_code", observeTarget: _ => throw new Exception("receipt failed")));
        Assert.StartsWith("DOCUMENT_CHANGED:", error.Message);
        Assert.Equal(0, original.MutationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Gateway_preserves_observed_receipt_and_old_fields_over_real_transport(bool ok)
    {
        var observed = DocumentTargetGuard.Capture(original);
        using var host = new FakePluginServer(line =>
        {
            var request = JObject.Parse(line);
            return new JObject { ["id"] = request["id"], ["ok"] = ok, ["result"] = new JObject { ["value"] = 42 },
                ["error"] = ok ? null : "handler failed", ["_target"] = observed.DeepClone() }.ToString(Formatting.None);
        });
        using var process = Process.GetCurrentProcess();
        var client = new PluginClient(() => new DiscoveryInfo { Pid = process.Id, ProcessStartUtc = process.StartTime.ToUniversalTime(), Port = host.Port });
        var response = await client.SendAsync("get_drawing_info", new { }, "receipt-request");
        Assert.Equal("receipt-request", response.Id);
        Assert.Equal(ok, response.Ok);
        Assert.Equal(42, ((JObject)response.Result).Value<int>("value"));
        Assert.True(JToken.DeepEquals(observed, response.Target));
        Assert.True(JToken.DeepEquals(observed, JObject.Parse(JsonConvert.SerializeObject(response))["_target"]));
    }

    [Fact]
    public async Task Offline_failure_and_legacy_response_have_no_receipt()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var process = Process.GetCurrentProcess();
        var client = new PluginClient(() => new DiscoveryInfo { Pid = process.Id, ProcessStartUtc = process.StartTime.ToUniversalTime(), Port = port });
        var response = await client.SendAsync("get_drawing_info", new { });
        Assert.False(response.Ok);
        Assert.Null(response.Target);
        Assert.Null(JObject.Parse(JsonConvert.SerializeObject(response))["_target"]);
        Assert.Null(JsonConvert.DeserializeObject<McpResponse>("{\"ok\":true,\"result\":42}").Target);
    }

    public void Dispose() { DocumentInvoker.Shutdown(); Application.DocumentManager = previous; }
}
