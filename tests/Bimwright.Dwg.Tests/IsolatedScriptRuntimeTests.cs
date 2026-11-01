using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class IsolatedScriptRuntimeTests
{
    [Fact]
    public async Task Host_core_4_0_can_coexist_with_the_actual_script_engine_4_8()
    {
        var probe = Path.Combine(AppContext.BaseDirectory, "script-probe", "Bimwright.Dwg.Scripting.Probe.dll");
        using var process = Process.Start(new ProcessStartInfo("dotnet") {
            ArgumentList = { probe }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        });
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stderr);
        using var json = JsonDocument.Parse(await stdout);
        var result = json.RootElement;
        Assert.Equal(2, result.GetProperty("math").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
        Assert.Equal(42, result.GetProperty("globalsCounter").GetInt32());
        Assert.Equal(42, result.GetProperty("shared").GetProperty("value").GetInt32());
        Assert.Equal("shared-host", result.GetProperty("shared").GetProperty("marker").GetString());
        Assert.True(result.GetProperty("sameThread").GetBoolean());
        Assert.Contains("CS0103", result.GetProperty("compileError").GetString());
        Assert.Equal("script-failure", result.GetProperty("runtimeError").GetString());
        Assert.True(result.GetProperty("asyncGuard").GetBoolean());
        Assert.True(result.GetProperty("loadGuard").GetBoolean());
        Assert.Equal("4.0.0.0", result.GetProperty("hostCore").GetString());
        Assert.Equal("4.8.0.0", result.GetProperty("engineCore").GetString());
    }
}
