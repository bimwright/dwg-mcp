using Bimwright.Dwg.Plugin.Views.Toast;
using Xunit;
using System;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Tests
{
    public class ToastContentTests
    {
        [Theory]
        [InlineData("move_entities")]
        [InlineData("rotate_entities")]
        [InlineData("scale_entities")]
        [InlineData("copy_entities")]
        [InlineData("erase_entities")]
        [InlineData("offset_entities")]
        [InlineData("explode_block")]
        [InlineData("insert_block")]
        [InlineData("translate_and_rewrite")]
        [InlineData("collapse_and_rewrite")]
        [InlineData("send_code")]
        [InlineData("create_layer")]
        [InlineData("create_line")]
        [InlineData("create_circle")]
        [InlineData("create_point")]
        [InlineData("create_polyline")]
        [InlineData("create_rectangle")]
        [InlineData("create_arc")]
        [InlineData("create_ellipse")]
        [InlineData("create_text")]
        [InlineData("create_mtext")]
        [InlineData("create_leader")]
        [InlineData("create_table")]
        [InlineData("set_block_attributes")]
        [InlineData("create_linear_dimension")]
        [InlineData("create_aligned_dimension")]
        [InlineData("create_radial_dimension")]
        [InlineData("create_diameter_dimension")]
        [InlineData("change_layer")]
        [InlineData("change_color")]
        [InlineData("update_texts")]
        [InlineData("apply_unicode_style")]
        [InlineData("set_system_variable")]
        [InlineData("save_drawing")]
        [InlineData("purge_drawing")]
        [InlineData("export_dxf")]
        [InlineData("apply_bake")]
        [InlineData("batch_execute")]
        [InlineData("run_baked_tool")]
        public void Dwg_mutations_are_writes(string command)
            => Assert.Equal(ToolActivityKind.Write, ToolActivityClassifier.Classify(command));

        [Fact]
        public void Batch_detects_nested_failure_even_if_partial_flag_is_false()
        {
            var vm = ToastContentBuilder.BuildCompleted("batch_execute", "{\"partial_failure\":false,\"results\":[{\"cmd\":\"get_drawing_info\",\"ok\":true},{\"cmd\":\"send_code\",\"ok\":true,\"result\":{\"ok\":false}}]}", true, null, 30);
            Assert.False(vm.Success);
            Assert.Equal("1 succeeded · 1 failed", vm.Summary);
        }

        [Theory]
        [InlineData(".png")]
        [InlineData(".jpg")]
        [InlineData(".jpeg")]
        [InlineData(".bmp")]
        public void Capture_uses_dwg_path_actual_size_and_safe_image(string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
            try
            {
                File.WriteAllBytes(path, new byte[] { 1 });
                var vm = ToastContentBuilder.BuildCompleted("capture_view_image", JsonConvert.SerializeObject(new { output_path = path, width = 1601, height = 900, image_format = extension.Substring(1) }), true, null, 10);
                Assert.Equal(path, vm.ThumbnailPath);
                Assert.Contains("1601×900", vm.Summary);
                Assert.Equal("Click to open", vm.Detail);
                Assert.Equal("MCP · Snapshot", vm.CategoryLabel);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Script_cannot_create_a_clickable_image()
        {
            var vm = ToastContentBuilder.BuildCompleted("send_code", "{\"ok\":true,\"output_path\":\"C:/Temp/a.png\"}", true, null, 10);
            Assert.Null(vm.ThumbnailPath);
        }

        [Theory]
        [InlineData("count_entities", "{\"count\":0}", "0 results")]
        [InlineData("query_entities", "{\"total\":42}", "42 results")]
        [InlineData("select_by_layer", "{\"handles\":[\"A\",\"B\"]}", "2 handles")]
        [InlineData("get_selected_texts", "{\"clusters\":[{},{}]}", "2 clusters")]
        [InlineData("list_layers", "{\"layers\":[{},{}]}", "2 layers")]
        public void Query_counts_are_accurate(string command, string json, string summary)
        {
            var vm = ToastContentBuilder.BuildCompleted(command, json, true, null, 42);
            Assert.Equal(summary, vm.Summary);
            Assert.Equal(42, vm.DurationMs);
            Assert.Equal("MCP · Query", vm.CategoryLabel);
        }

        [Fact]
        public void Image_policy_rejects_unsafe_roots_types_and_oversized_files()
        {
            var root = Path.Combine(Path.GetTempPath(), "toast-root");
            Assert.True(ToastImagePolicy.IsUnderRoot(Path.Combine(root, "a.png"), root));
            Assert.False(ToastImagePolicy.IsUnderRoot(root + "-escape/a.png", root));
            Assert.False(ToastImagePolicy.IsUnderRoot(Path.Combine(root, "..", "a.png"), root));
            Assert.False(ToastImagePolicy.IsSafeImagePath("https://example.com/a.png"));
            Assert.False(ToastImagePolicy.IsSafeImagePath("relative.png"));
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            var script = Path.ChangeExtension(path, ".cmd");
            try
            {
                using (var file = File.Create(path)) file.SetLength(ToastImagePolicy.MaxBytes + 1L);
                File.WriteAllText(script, "payload");
                Assert.False(ToastImagePolicy.IsSafeImagePath(path));
                Assert.Null(ToastThumbnailLoader.TryLoadBytes(path, ToastImagePolicy.MaxBytes));
                Assert.False(ToastImagePolicy.IsSafeImagePath(script));
                var vm = ToastContentBuilder.BuildCompleted("capture_view_image", JsonConvert.SerializeObject(new { output_path = path }), true, null, 1);
                Assert.True(vm.Success);
                Assert.Null(vm.ThumbnailPath);
            }
            finally { File.Delete(path); File.Delete(script); }
        }

        [Fact]
        public void Display_text_is_bounded_and_optional_capture_metadata_is_compatible()
        {
            var vm = ToastContentBuilder.BuildCompleted("get_drawing_info", JsonConvert.SerializeObject(new { drawing_name = new string('a', 3000), current_layout = new string('b', 3000) }), true, null, -1);
            Assert.True(vm.Summary.Length <= 120 && vm.Detail.Length <= 100);
            Assert.Equal(0, vm.DurationMs);
            var capture = ToastContentBuilder.BuildCompleted("capture_view_image", "{\"width\":1600,\"height\":900,\"capture_method\":\"native\",\"document\":{},\"camera\":{}}", true, null, 1);
            Assert.Contains("1600×900", capture.Summary);
            Assert.True(capture.Success);
        }

        [Theory]
        [InlineData("broken json")]
        [InlineData("{\"count\":{\"invalid\":true}}")]
        [InlineData("[]")]
        public void Unusual_result_shapes_degrade_to_generic_content(string json)
        {
            var vm = ToastContentBuilder.BuildCompleted("query_entities", json, true, null, 10);
            Assert.True(vm.Success);
            Assert.Equal("Completed successfully", vm.Summary);
        }
        [Fact]
        public void Nested_script_failure_is_not_a_success_toast()
        {
            var vm = ToastContentBuilder.BuildCompleted("send_code", "{\"ok\":false,\"error\":\"compile error password=canary\"}", true, null, 12);
            Assert.False(vm.Success);
            Assert.Equal("MCP · Failed", vm.CategoryLabel);
            Assert.Contains("compile error", vm.Summary);
            Assert.DoesNotContain("canary", vm.Summary);
        }

        [Fact]
        public void Script_result_and_stdout_are_not_displayed()
        {
            var vm = ToastContentBuilder.BuildCompleted("send_code", "{\"ok\":true,\"stdout\":\"private-value\",\"result\":{\"secret\":\"private-value\"}}", true, null, 12);
            Assert.True(vm.Success);
            Assert.Equal("MCP · Script", vm.CategoryLabel);
            Assert.DoesNotContain("private-value", vm.Summary + vm.Detail);
            Assert.Null(vm.ThumbnailPath);
        }
    }
}
