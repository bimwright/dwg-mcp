using System;
using System.IO;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Toast position settings")]
public sealed class AutoStartTransportSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dwg-startup-" + Guid.NewGuid().ToString("N"));
    private readonly string _previousPath = PluginSettings.FilePathOverride;
    private readonly string _previousEnv = Environment.GetEnvironmentVariable(PluginSettings.EnvAutoStartTransport);
    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public AutoStartTransportSettingsTests()
    {
        Directory.CreateDirectory(_folder);
        PluginSettings.FilePathOverride = SettingsPath;
        Environment.SetEnvironmentVariable(PluginSettings.EnvAutoStartTransport, null);
    }

    [Fact]
    public void Missing_settings_defaults_on_without_creating_a_file()
    {
        Assert.True(PluginSettings.LoadAutoStartTransport(out var source));
        Assert.Equal("default", source);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Theory]
    [InlineData("false", null, false, "settings.json")]
    [InlineData("true", null, true, "settings.json")]
    [InlineData("false", "true", true, PluginSettings.EnvAutoStartTransport)]
    [InlineData("true", "0", false, PluginSettings.EnvAutoStartTransport)]
    [InlineData("true", " OFF ", false, PluginSettings.EnvAutoStartTransport)]
    [InlineData("false", "yes", true, PluginSettings.EnvAutoStartTransport)]
    [InlineData("false", "invalid", false, "settings.json")]
    [InlineData("null", null, true, "default")]
    [InlineData("\"false\"", null, true, "default")]
    [InlineData("0", null, true, "default")]
    public void Resolution_reports_effective_value_and_source(string jsonValue, string env, bool expected, string expectedSource)
    {
        File.WriteAllText(SettingsPath, "{\"autoStartTransport\":" + jsonValue + "}");
        Environment.SetEnvironmentVariable(PluginSettings.EnvAutoStartTransport, env);
        Assert.Equal(expected, PluginSettings.LoadAutoStartTransport(out var source));
        Assert.Equal(expectedSource, source);
    }

    [Fact]
    public void Manual_preferences_survive_reload_and_preserve_unrelated_keys()
    {
        File.WriteAllText(SettingsPath, "{\"enableToast\":false,\"toastIdleSeconds\":30,\"custom\":{\"keep\":42}}");
        foreach (var enabled in new[] { false, true, false })
        {
            Assert.True(PluginSettings.SaveAutoStartTransport(enabled, out var error));
            Assert.Null(error);
            Assert.Equal(enabled, PluginSettings.LoadAutoStartTransport(out var source));
            Assert.Equal("settings.json", source);
            var root = JObject.Parse(File.ReadAllText(SettingsPath));
            Assert.False(root.Value<bool>("enableToast"));
            Assert.Equal(30, root.Value<int>("toastIdleSeconds"));
            Assert.Equal(42, root["custom"].Value<int>("keep"));
        }
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Saving_does_not_override_environment_on_next_load()
    {
        Environment.SetEnvironmentVariable(PluginSettings.EnvAutoStartTransport, "1");
        Assert.True(PluginSettings.SaveAutoStartTransport(false, out var error));
        Assert.Null(error);
        Assert.False(JObject.Parse(File.ReadAllText(SettingsPath)).Value<bool>("autoStartTransport"));
        Assert.True(PluginSettings.LoadAutoStartTransport(out var source));
        Assert.Equal(PluginSettings.EnvAutoStartTransport, source);
        Environment.SetEnvironmentVariable(PluginSettings.EnvAutoStartTransport, null);
        Assert.False(PluginSettings.LoadAutoStartTransport(out _));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void Malformed_settings_use_default_and_are_never_overwritten(string original)
    {
        File.WriteAllText(SettingsPath, original);
        Assert.True(PluginSettings.LoadAutoStartTransport(out var source));
        Assert.Equal("default", source);
        Assert.False(PluginSettings.SaveAutoStartTransport(false, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(original, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Locked_settings_report_failure_preserve_bytes_and_allow_retry()
    {
        const string original = "{\"autoStartTransport\":true,\"keep\":42}";
        File.WriteAllText(SettingsPath, original);
        using (var locked = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(PluginSettings.SaveAutoStartTransport(false, out var error));
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(original, File.ReadAllText(SettingsPath));
            Assert.True(PluginSettings.LoadAutoStartTransport(out _));
            Assert.Single(Directory.GetFiles(_folder));
        }
        Assert.True(PluginSettings.SaveAutoStartTransport(false, out var retryError));
        Assert.Null(retryError);
        Assert.False(PluginSettings.LoadAutoStartTransport(out _));
        Assert.Equal(42, JObject.Parse(File.ReadAllText(SettingsPath)).Value<int>("keep"));
    }

    public void Dispose()
    {
        PluginSettings.FilePathOverride = _previousPath;
        Environment.SetEnvironmentVariable(PluginSettings.EnvAutoStartTransport, _previousEnv);
        Directory.Delete(_folder, recursive: true);
    }
}
