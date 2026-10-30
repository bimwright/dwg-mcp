using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Xunit;
using FactAttribute = Bimwright.Dwg.Tests.Helpers.WindowsFactAttribute;
using TheoryAttribute = Bimwright.Dwg.Tests.Helpers.WindowsTheoryAttribute;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class LogMaintenanceRetryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-maintenance-retry-" + Guid.NewGuid().ToString("N"));
    private readonly string previous = DiagnosticLog.LocalAppDataOverride;
    private string Current => Path.Combine(root, "Bimwright", "dwg-mcp");
    private string Diagnostics => Path.Combine(Current, "dwg-mcp.log");

    public LogMaintenanceRetryTests()
    {
        WaitIdle();
        DiagnosticLog.LocalAppDataOverride = root;
        Directory.CreateDirectory(Current);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), true)]
    [InlineData(unchecked((int)0x80070021), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(unchecked((int)0x80070003), false)]
    [InlineData(unchecked((int)0x80131620), false)]
    public void Only_explicit_transient_file_error_codes_are_retried(int code, bool retry)
        => Assert.Equal(retry, DiagnosticLog.IsTransientMaintenanceFailure(new IOException("fixture", code)));

    [Fact]
    public void Mutex_timeout_is_retryable_but_permission_security_path_and_unknown_errors_are_not()
    {
        Assert.True(DiagnosticLog.IsTransientMaintenanceFailure(new TimeoutException()));
        foreach (var error in new Exception[] { new UnauthorizedAccessException(), new SecurityException(),
            new PathTooLongException(), new DirectoryNotFoundException(), new InvalidOperationException(),
            new IOException("Log maintenance refuses linked files.") })
            Assert.False(DiagnosticLog.IsTransientMaintenanceFailure(error));
    }

    [Theory]
    [InlineData("dwg-mcp")]
    [InlineData("Dwg")]
    public void Locked_debug_tail_completes_after_unlock_in_both_existing_roots(string product)
    {
        var path = Path.Combine(root, "Bimwright", product, "debug.log");
        Oversize(path, 100000);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            StartAndWaitRetry("sharing_violation");
            Assert.Contains("attempt=1/3", DiagnosticLog.MaintenanceStatus);
            Assert.InRange(DiagnosticLog.MaintenanceStatus.Length, 1, 256);
        }
        WaitIdle();
        Assert.Contains("state=completed", DiagnosticLog.MaintenanceStatus);
        Assert.InRange(new FileInfo(path).Length, 1, 64 * 1024);
        Assert.Contains("recent-tail", File.ReadAllText(path));
        Assert.Contains("recent-tail", File.ReadAllText(Diagnostics));
        Assert.Contains("state=completed", File.ReadAllText(Diagnostics));
    }

    [Theory]
    [InlineData("dwg-mcp", "dwg-mcp-calls")]
    [InlineData("dwg-mcp", "journal")]
    [InlineData("Dwg", "dwg-mcp-calls")]
    [InlineData("Dwg", "journal")]
    public void Locked_generated_archive_is_pruned_after_unlock_without_touching_personal_files(string product, string group)
    {
        var folder = Path.Combine(root, "Bimwright", product);
        Directory.CreateDirectory(folder);
        var archive = Path.Combine(folder, group + "-20200101-000000.jsonl");
        File.WriteAllText(archive, "expired");
        File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddDays(-40));
        foreach (var name in new[] { "drawing.dwg", "settings.json", "dwgmcp.config.json", "personal.jsonl" })
            File.WriteAllText(Path.Combine(folder, name), "preserve");
        foreach (var name in new[] { "captures", "baked" })
        {
            Directory.CreateDirectory(Path.Combine(folder, name));
            File.WriteAllText(Path.Combine(folder, name, "personal.bin"), "preserve");
        }
        using (var held = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.None)) StartAndWaitRetry("sharing_violation");
        WaitIdle();
        Assert.False(File.Exists(archive));
        Assert.Contains("state=completed", DiagnosticLog.MaintenanceStatus);
        foreach (var path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != "dwg-mcp.log"))
            Assert.Equal("preserve", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Locked_active_or_backup_diagnostics_are_compacted_after_unlock(bool backup)
    {
        var path = Diagnostics + (backup ? ".1" : "");
        Oversize(path, DiagnosticLog.FileBytes + 100);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) StartAndWaitRetry("sharing_violation");
        WaitIdle();
        Assert.Contains("state=completed", DiagnosticLog.MaintenanceStatus);
        Assert.InRange(new FileInfo(path).Length, 1, DiagnosticLog.FileBytes);
        Assert.Contains("recent-tail", File.ReadAllText(path));
        Assert.All(Directory.GetFiles(Current, "dwg-mcp.log*"), file => Assert.True(new FileInfo(file).Length <= DiagnosticLog.FileBytes));
    }

    [Fact]
    public void Retained_legacy_tail_is_retried_when_only_the_diagnostic_sink_is_locked()
    {
        var debug = Path.Combine(Current, "debug.log"); Oversize(debug, 100000);
        File.AppendAllText(debug, "distinct-debug-tail\n");
        File.WriteAllText(Diagnostics, "existing-diagnostics\n");
        using (var held = new FileStream(Diagnostics, FileMode.Open, FileAccess.Read, FileShare.None))
            StartAndWaitRetry("sharing_violation");
        WaitIdle();
        Assert.Contains("state=completed", DiagnosticLog.MaintenanceStatus);
        Assert.InRange(new FileInfo(debug).Length, 1, 64 * 1024);
        Assert.Contains("distinct-debug-tail", File.ReadAllText(Diagnostics));
    }

    [Fact]
    public async Task Mutex_contention_does_not_block_STA_caller_or_worker_during_backoff()
    {
        var path = Path.Combine(Current, "debug.log");
        Oversize(path, 100000);
        using var locked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => BoundedLogFile.WithLock(path, () => { locked.Set(); release.Wait(); return true; }));
        Assert.True(locked.Wait(3000));
        try
        {
            var elapsed = TimeSpan.MaxValue;
            var ui = new Thread(() => { var timer = Stopwatch.StartNew(); DiagnosticLog.MaintainLegacyLogs(); elapsed = timer.Elapsed; });
            if (OperatingSystem.IsWindows()) ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            Assert.True(ui.Join(3000), "The STA caller waited for maintenance.");
            Assert.True(elapsed < TimeSpan.FromMilliseconds(500), "The STA caller waited for file/mutex I/O.");
            WaitRetry("mutex_timeout");
            DiagnosticLog.Write("RetryProgress", "worker-kept-running");
            Assert.True(DiagnosticLog.Flush(500), "The diagnostic worker slept through the retry backoff.");
            Assert.Contains("worker-kept-running", File.ReadAllText(Diagnostics));
        }
        finally { release.Set(); await holder; }
        WaitIdle();
        Assert.InRange(new FileInfo(path).Length, 1, 64 * 1024);
    }

    [Fact]
    public async Task Three_attempts_exhaust_and_duplicate_requests_cannot_reset_active_budget()
    {
        var path = Path.Combine(Current, "debug.log");
        Oversize(path, 100000);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            StartAndWaitRetry("sharing_violation");
            for (var i = 0; i < 20; i++) DiagnosticLog.MaintainLegacyLogs();
            WaitIdle();
            Assert.Contains("state=failed attempt=3/3", DiagnosticLog.MaintenanceStatus);
            Assert.Contains("reason=sharing_violation", DiagnosticLog.MaintenanceStatus);
        }
        var final = DiagnosticLog.MaintenanceStatus;
        await Task.Delay(DiagnosticLog.MaintenanceRetryDelayMilliseconds + 200);
        Assert.Equal(final, DiagnosticLog.MaintenanceStatus);
        Assert.True(new FileInfo(path).Length > 64 * 1024);
        Assert.Equal(2, File.ReadAllLines(Diagnostics).Count(line => line.Contains("state=retry_pending")));
        DiagnosticLog.MaintainLegacyLogs();
        WaitIdle();
        Assert.Contains("state=completed attempt=1/3", DiagnosticLog.MaintenanceStatus);
        Assert.InRange(new FileInfo(path).Length, 1, 64 * 1024);
    }

    [Fact]
    public async Task Access_denied_is_not_retried_while_other_generated_cleanup_still_completes()
    {
        var path = Path.Combine(Current, "debug.log");
        Oversize(path, 100000);
        var expired = Path.Combine(Current, "journal-20200101-000000.jsonl");
        File.WriteAllText(expired, "expired"); File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-40));
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            DiagnosticLog.MaintainLegacyLogs();
            WaitIdle();
            Assert.Contains("state=failed attempt=1/3", DiagnosticLog.MaintenanceStatus);
            Assert.Contains("reason=access_denied", DiagnosticLog.MaintenanceStatus);
            Assert.False(File.Exists(expired));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        var final = DiagnosticLog.MaintenanceStatus;
        await Task.Delay(DiagnosticLog.MaintenanceRetryDelayMilliseconds + 200);
        Assert.Equal(final, DiagnosticLog.MaintenanceStatus);
        Assert.True(new FileInfo(path).Length > 64 * 1024);
    }

    [Fact]
    public void Permanent_failure_is_not_replayed_when_another_operation_retries()
    {
        var denied = Path.Combine(Current, "debug.log"); Oversize(denied, 100000);
        var locked = Path.Combine(root, "Bimwright", "Dwg", "debug.log"); Oversize(locked, 100000);
        File.SetAttributes(denied, FileAttributes.ReadOnly);
        try
        {
            using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                StartAndWaitRetry("sharing_violation");
                Assert.Contains("failed=1", DiagnosticLog.MaintenanceStatus);
                Assert.Contains("blocked_reason=access_denied", DiagnosticLog.MaintenanceStatus);
                // Make the permanent failure recoverable while another operation is pending.
                File.SetAttributes(denied, FileAttributes.Normal);
            }
            WaitIdle();
            Assert.InRange(new FileInfo(locked).Length, 1, 64 * 1024);
            Assert.True(new FileInfo(denied).Length > 64 * 1024, "A permanent failure was replayed during another operation's retry.");
            Assert.Contains("state=failed", DiagnosticLog.MaintenanceStatus);
            Assert.Contains("reason=access_denied", DiagnosticLog.MaintenanceStatus);
        }
        finally { File.SetAttributes(denied, FileAttributes.Normal); }
    }

    [Fact]
    public async Task Linked_root_is_refused_once_and_never_modifies_the_link_target()
    {
        var target = Path.Combine(root, "junction-target");
        Directory.CreateDirectory(target);
        var debug = Path.Combine(target, "debug.log"); Oversize(debug, 100000);
        var link = Path.Combine(root, "Bimwright", "Dwg");
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using (var process = Process.Start(start)) { await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); }
        try
        {
            var original = File.ReadAllBytes(debug);
            DiagnosticLog.MaintainLegacyLogs(); WaitIdle();
            Assert.Contains("state=failed attempt=1/3", DiagnosticLog.MaintenanceStatus);
            Assert.Contains("reason=unsafe_link", DiagnosticLog.MaintenanceStatus);
            await Task.Delay(DiagnosticLog.MaintenanceRetryDelayMilliseconds + 200);
            Assert.Contains("attempt=1/3", DiagnosticLog.MaintenanceStatus);
            Assert.Equal(original, File.ReadAllBytes(debug));
            Assert.Single(Directory.GetFiles(target));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void Retry_keeps_captured_root_when_the_profile_override_changes()
    {
        var path = Path.Combine(Current, "debug.log"); Oversize(path, 100000);
        var other = Path.Combine(root, "other-profile");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            StartAndWaitRetry("sharing_violation");
            DiagnosticLog.LocalAppDataOverride = other;
        }
        WaitIdle();
        Assert.InRange(new FileInfo(path).Length, 1, 64 * 1024);
        Assert.False(Directory.Exists(other));
        Assert.Contains("state=completed", File.ReadAllText(Diagnostics));
    }

    private static void StartAndWaitRetry(string reason) { DiagnosticLog.MaintainLegacyLogs(); WaitRetry(reason); }
    private static void WaitRetry(string reason) => Assert.True(SpinWait.SpinUntil(() =>
        DiagnosticLog.MaintenanceStatus.Contains("state=retry_pending") && DiagnosticLog.MaintenanceStatus.Contains("reason=" + reason), 5000), DiagnosticLog.MaintenanceStatus);
    private static void WaitIdle()
    {
        Assert.True(DiagnosticLog.Flush(10000));
        Assert.True(SpinWait.SpinUntil(() => DiagnosticLog.PendingMaintenance == 0, 10000), DiagnosticLog.MaintenanceStatus);
        Assert.True(DiagnosticLog.Flush(10000));
    }
    private static void Oversize(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.SetLength(bytes); file.Position = bytes;
        var tail = System.Text.Encoding.UTF8.GetBytes("\nrecent-tail\n"); file.Write(tail);
    }
    public void Dispose()
    {
        WaitIdle(); DiagnosticLog.LocalAppDataOverride = previous;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
