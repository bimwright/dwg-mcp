using System;
using System.IO;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[CollectionDefinition("Toast position settings", DisableParallelization = true)]
public sealed class ToastPositionSettingsCollection { }

[Collection("Toast position settings")]
public sealed class ToastPositionSettingsTests
{
    [Fact]
    public void Saved_position_survives_branding_idle_changes_and_reset_preserves_other_keys()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dwg-toast-position-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        PluginSettings.FilePathOverride = path;
        try
        {
            File.WriteAllText(path, "{\"other\":42,\"enableToast\":false}");
            Assert.True(PluginSettings.SaveToastPosition(new ToastPositionOptions(true, true, true, -140, 20)));
            var read = PluginSettings.LoadToastPosition();
            Assert.True(read.Right && read.Bottom && read.DragEnabled && read.HasOffset);
            Assert.Equal(-140, read.OffsetX);
            Assert.Equal(20, read.OffsetY);
            Assert.True(PluginSettings.SaveShowBranding(true));
            Assert.True(PluginSettings.SaveToastIdleSeconds(30));
            Assert.True(PluginSettings.LoadToastPosition().HasOffset);
            Assert.True(PluginSettings.SaveToastPosition(read.WithOffset(null, null)));
            Assert.False(PluginSettings.LoadToastPosition().HasOffset);
            Assert.Equal(42, (int)JObject.Parse(File.ReadAllText(path))["other"]);
            File.WriteAllText(path, "broken json");
            Assert.False(PluginSettings.LoadToastPosition().HasOffset);
            Assert.False(PluginSettings.SaveToastPosition(read));
            Assert.Equal("broken json", File.ReadAllText(path));
        }
        finally { PluginSettings.FilePathOverride = null; Directory.Delete(folder, true); }
    }

    [Fact]
    public void Inline_capture_keeps_count_and_actual_duration_in_recent_timeline()
    {
        var activity = new ActivityAggregator();
        activity.RecordResult("Capture View", "Returned inline", true, true, true, durationMs: 155);
        var card = activity.TakeRender().Card;
        Assert.Equal(1, card.Images);
        Assert.Equal(155, card.RecentEntries[0].DurationMs);
        Assert.Contains("155 ms", card.RecentEntries[0].TooltipText);
    }

    [Fact]
    public void Malformed_position_values_use_finite_defaults()
    {
        var read = PluginSettings.ReadToastPosition(JObject.Parse("{\"toastHorizontalAlign\":true,\"toastDragEnabled\":\"true\",\"toastDragOffset\":{\"x\":\"NaN\",\"y\":3}}"));
        Assert.False(read.Right || read.Bottom || read.DragEnabled || read.HasOffset);
    }
}
