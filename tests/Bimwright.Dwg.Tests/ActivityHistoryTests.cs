using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Views.Toast;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ActivityHistoryTests
    {
        [Fact]
        public void History_is_bounded_newest_first_and_copies_the_outcome()
        {
            var history = new ActivityHistory(2);
            var outcome = ToastContentBuilder.BuildCompleted("list_layers", "{\"count\":2}", true, null, null, 17);
            history.Record(outcome);
            outcome.Title = "Mutated later";
            Assert.Equal("List Layers", history.Snapshot()[0].ToolName);
            history.Record(ToastContentBuilder.BuildCompleted("create_line", null, true, null, null));
            history.Record(ToastContentBuilder.BuildCompleted("send_code", "{\"ok\":false,\"error\":\"compile error\"}", true, null, null, 23));
            var entries = history.Snapshot();
            Assert.Equal(new long[] { 3, 2 }, entries.Select(e => e.Index));
            Assert.False(entries[0].Success);
            Assert.Equal(ToolActivityKind.Write, entries[0].Kind);
            Assert.Equal(23, entries[0].DurationMs);
            Assert.Contains("compile error", entries[0].Summary);
        }

        [Fact]
        public void History_preserves_duration_and_does_not_retain_mutable_json()
        {
            var history = new ActivityHistory();
            history.Record(ToastContentBuilder.BuildCompleted("dwg_list_layers", "{\"count\":2}", true, null, null, 17));
            var entry = Assert.Single(history.Snapshot());
            Assert.Equal(17, entry.DurationMs);
            Assert.Equal("Read", entry.KindLabel);
            Assert.Equal("✓", entry.StatusLabel);
            Assert.DoesNotContain("{", entry.Summary);
            Assert.DoesNotContain(typeof(ActivityHistoryEntry).GetProperties(), p =>
                p.Name == "ParamsJson" || p.Name == "ResultJson" || p.Name == "Code");
        }

        [Fact]
        public void History_bounds_strings_and_rejects_unsafe_thumbnail_paths()
        {
            var history = new ActivityHistory();
            history.Record(new McpToastViewModel
            {
                Title = new string('t', 2000), Summary = new string('s', 2000),
                Success = true, DurationMs = -1, ThumbnailPath = @"C:\Windows\unsafe.png"
            });
            var entry = Assert.Single(history.Snapshot());
            Assert.Equal(160, entry.ToolName.Length);
            Assert.Equal(240, entry.Summary.Length);
            Assert.Equal(0, entry.DurationMs);
            Assert.Null(entry.ImagePath);
        }

        [Fact]
        public void Clear_starts_a_new_session_and_invalidates_the_ui_snapshot()
        {
            var history = new ActivityHistory();
            history.Record(new McpToastViewModel());
            var version = history.Version;
            history.Clear();
            Assert.Empty(history.Snapshot());
            Assert.True(history.Version > version);
            history.Record(new McpToastViewModel());
            Assert.Equal(1, history.Snapshot()[0].Index);
        }

        [Fact]
        public void Concurrent_completions_are_not_lost_or_duplicated()
        {
            var history = new ActivityHistory(16);
            Parallel.For(0, 5000, i => history.Record(new McpToastViewModel { Title = "tool", Success = true }));
            Assert.Equal(5000, history.Version);
            Assert.Equal(Enumerable.Range(4985, 16).Reverse().Select(i => (long)i), history.Snapshot().Select(e => e.Index));
        }

        [Fact]
        public void Invalid_capacity_and_null_outcome_are_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityHistory(0));
            Assert.Throws<ArgumentNullException>(() => new ActivityHistory().Record(null));
        }
    }

    public class ToolActivityClassifierTests
    {
        [Theory]
        [InlineData("list_layers", ToolActivityKind.Read)]
        [InlineData(" DWG_GET_DRAWING_INFO ", ToolActivityKind.Read)]
        [InlineData("select_by_layer", ToolActivityKind.Read)]
        [InlineData("zoom_window", ToolActivityKind.Read)]
        [InlineData("switch_target", ToolActivityKind.Read)]
        [InlineData("send_code", ToolActivityKind.Write)]
        [InlineData("dwg_run_baked_tool", ToolActivityKind.Write)]
        [InlineData("batch_execute", ToolActivityKind.Write)]
        [InlineData("move_entities", ToolActivityKind.Write)]
        [InlineData("rotate_entities", ToolActivityKind.Write)]
        [InlineData("scale_entities", ToolActivityKind.Write)]
        [InlineData("copy_entities", ToolActivityKind.Write)]
        [InlineData("erase_entities", ToolActivityKind.Write)]
        [InlineData("offset_entities", ToolActivityKind.Write)]
        [InlineData("explode_block", ToolActivityKind.Write)]
        [InlineData("export_dxf", ToolActivityKind.Write)]
        [InlineData("save_drawing", ToolActivityKind.Write)]
        [InlineData("unknown", ToolActivityKind.Write)]
        [InlineData(null, ToolActivityKind.Write)]
        public void Classify_handles_DWG_writes_and_transient_UI(string command, ToolActivityKind expected)
        {
            Assert.Equal(expected, ToolActivityClassifier.Classify(command));
        }

        [Fact]
        public void Every_explicit_read_only_server_tool_is_classified_as_read()
        {
            var tools = typeof(Program).Assembly.GetTypes().SelectMany(t => t.GetMethods())
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
                .Where(a => a != null && a.ReadOnly).ToArray();
            Assert.NotEmpty(tools);
            foreach (var tool in tools)
                Assert.Equal(ToolActivityKind.Read, ToolActivityClassifier.Classify(tool.Name));
        }
    }
}
