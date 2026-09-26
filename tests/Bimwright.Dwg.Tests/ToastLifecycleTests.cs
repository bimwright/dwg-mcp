using System;
using System.IO;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToastLifecycleTests
    {
        [Fact]
        public void Missing_settings_default_on_and_unwritable_target_reports_failure()
        {
            var path = Path.Combine(Path.GetTempPath(), "dwg-toast-settings-" + Guid.NewGuid());
            Assert.True(ToastPreferences.Load(path, _ => null));
            Assert.True(ToastPreferences.Load(path, _ => "invalid"));
            Directory.CreateDirectory(path);
            try
            {
                Assert.False(ToastPreferences.TrySave(false, out var error, path));
                Assert.False(string.IsNullOrWhiteSpace(error));
                Assert.True(Directory.Exists(path));
            }
            finally { Directory.Delete(path); }
        }

        [Fact]
        public void Toggle_persists_without_losing_other_settings()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{\"other\":42}");
                Assert.True(ToastPreferences.Load(path, _ => null));
                Assert.True(ToastPreferences.TrySave(false, out var error, path), error);
                Assert.False(ToastPreferences.Load(path, _ => null));
                Assert.Equal(42, (int)JObject.Parse(File.ReadAllText(path))["other"]);
                Assert.True(ToastPreferences.Load(path, _ => "1"));
                Assert.False(ToastPreferences.Load(path, _ => "false"));
                File.WriteAllText(path, "broken");
                Assert.False(ToastPreferences.TrySave(true, out error, path));
                Assert.Equal("broken", File.ReadAllText(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Queue_is_bounded_and_stop_invalidates_old_publishers()
        {
            var queue = new ToastNotificationQueue();
            var publish = queue.Start();
            for (int i = 0; i < 10; i++) publish(new McpToastViewModel { Title = i.ToString() });
            var items = queue.Drain();
            Assert.Equal(4, items.Length);
            Assert.Equal("6", items[0].Title);
            Assert.Equal("9", items[3].Title);
            queue.Stop();
            var current = queue.Start();
            publish(new McpToastViewModel { Title = "stale" });
            Assert.Empty(queue.Drain());
            current(new McpToastViewModel());
            queue.Enabled = false;
            queue.Enabled = true;
            Assert.Empty(queue.Drain());
            current(new McpToastViewModel());
            Assert.Single(queue.Drain());
        }

        [Fact]
        public void Completion_emits_once_and_cannot_change_the_wire_response()
        {
            const string response = "{\"id\":\"a\",\"ok\":true,\"result\":{\"count\":3}}";
            var count = 0;
            McpToastViewModel observed = null;
            Assert.Same(response, ToastCompletion.Publish(response, "count_entities", 1, "a.dwg", vm => { count++; observed = vm; }));
            Assert.Equal(1, count);
            Assert.Contains("3", observed.Summary);
            Assert.Same(response, ToastCompletion.Publish(response, "count_entities", 1, null, vm => throw new Exception("UI failed")));
            Assert.Same(response, ToastCompletion.Publish(response, "count_entities", 1, null, null));
        }
    }
}
