using System;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views.Toast;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToastContentBuilderTests
    {
        [Fact]
        public void BuildCompleted_get_drawing_info_includes_name_layout_layer()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "get_drawing_info",
                resultJson: "{\"drawing_name\":\"plan.dwg\",\"current_layout\":\"Model\",\"current_layer\":\"WALLS\"}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("plan.dwg", vm.Summary);
            Assert.Contains("Model", vm.Detail);
            Assert.Contains("WALLS", vm.Detail);
        }

        [Fact]
        public void BuildCompleted_capture_view_image_sets_thumbnail_and_size()
        {
            var capturesDir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "Dwg", "captures");
            System.IO.Directory.CreateDirectory(capturesDir);
            var path = System.IO.Path.Combine(capturesDir, "toast-test-thumb.png");
            System.IO.File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            try
            {
                var vm = ToastContentBuilder.BuildCompleted(
                    toolName: "capture_view_image",
                    resultJson: $"{{\"output_path\":\"{path.Replace("\\", "\\\\")}\",\"width\":1600,\"height\":900,\"image_format\":\"png\",\"capture_id\":\"abcd1234ef56\"}}",
                    success: true,
                    errorMessage: null,
                    toolDescription: null
                );

                Assert.Contains("toast-test-thumb.png", vm.Summary);
                Assert.Contains("1600", vm.Summary);
                Assert.Contains("PNG", vm.Summary);
                Assert.Equal(path, vm.ThumbnailPath);
                Assert.Contains("Click to open", vm.Detail);
                Assert.Contains("abcd1234", vm.Detail);
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void BuildCompleted_inspect_view_region_also_gets_snapshot_category()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "inspect_view_region",
                resultJson: "{\"output_path\":\"<local_path>\"}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Null(vm.ThumbnailPath);
        }

        [Fact]
        public void BuildCompleted_failure_uses_error_message()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "capture_view_image",
                resultJson: null,
                success: false,
                errorMessage: "output_path is required.",
                toolDescription: "Capture the active drawing."
            );

            Assert.Contains("output_path", vm.Summary);
            Assert.Contains("active drawing", vm.Detail);
            Assert.False(vm.Success);
        }

        [Fact]
        public void BuildCompleted_handles_local_path_placeholder_without_throwing()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "capture_view_image",
                resultJson: "{\"output_path\":\"<local_path>\",\"width\":800,\"height\":600}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Contains("image", vm.Summary);
            Assert.Null(vm.ThumbnailPath);
        }

        [Fact]
        public void BuildCompleted_send_code_shows_result_line()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "send_code",
                resultJson: "{\"ok\":true,\"result\":\"3 circles created\\nextra\",\"stdout\":\"\",\"error\":null}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("3 circles created", vm.Summary);
        }

        [Fact]
        public void BuildCompleted_send_code_inner_error_surfaces()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "send_code",
                resultJson: "{\"ok\":false,\"result\":null,\"stdout\":\"\",\"error\":\"compile error: CS1002\"}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Contains("compile error", vm.Summary);
        }

        [Fact]
        public void BuildCompleted_rewrite_reports_processed_and_failed()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "translate_and_rewrite",
                resultJson: "{\"results\":[{\"ok\":true},{\"ok\":false},{\"ok\":true}]}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("Processed 3 texts", vm.Summary);
            Assert.Contains("1 failed", vm.Detail);
        }

        [Fact]
        public void BuildCompleted_update_texts_accepts_top_level_array()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "update_texts",
                resultJson: "[{\"ok\":true},{\"ok\":true}]",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("Processed 2 texts", vm.Summary);
        }

        [Fact]
        public void BuildCompleted_list_layers_uses_array_length()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "list_layers",
                resultJson: "{\"layers\":[{\"name\":\"0\"},{\"name\":\"WALLS\"}]}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("Items: 2", vm.Summary);
        }

        [Fact]
        public void BuildCompleted_count_entities_uses_count()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "count_entities",
                resultJson: "{\"count\":42}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("Items: 42", vm.Summary);
        }

        [Fact]
        public void BuildCompleted_export_dxf_shows_filename()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "export_dxf",
                resultJson: "{\"output_path\":\"<local_path>\"}",
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("file", vm.Summary);
            Assert.Null(vm.ThumbnailPath);
        }

        [Fact]
        public void BuildCompleted_null_result_uses_generic_success_copy()
        {
            var vm = ToastContentBuilder.BuildCompleted(
                toolName: "list_layers",
                resultJson: null,
                success: true,
                errorMessage: null,
                toolDescription: null
            );

            Assert.Equal("Completed successfully", vm.Summary);
            Assert.Equal("List Layers", vm.Detail);
        }

        [Theory]
        [InlineData(@"C:\Windows\System32\cmd.exe")]
        [InlineData(null)]
        [InlineData("<local_path>")]
        public void IsSafeImagePath_rejects_unsafe_paths(string path)
        {
            Assert.False(ToastContentBuilder.IsSafeImagePath(path));
        }

        [Fact]
        public void IsSafeImagePath_rejects_sibling_of_captures_directory()
        {
            var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            var siblingDir = System.IO.Path.Combine(localAppData, "Bimwright", "Dwg", "capturesEvil");
            System.IO.Directory.CreateDirectory(siblingDir);
            var path = System.IO.Path.Combine(siblingDir, "evil.png");
            System.IO.File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            try
            {
                Assert.False(ToastContentBuilder.IsSafeImagePath(path));
            }
            finally
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                try { System.IO.Directory.Delete(siblingDir); } catch { }
            }
        }
    }

    public class ToolNameFormatterTests
    {
        [Theory]
        [InlineData("capture_view_image", "Capture View Image")]
        [InlineData("export_dxf", "Export DXF")]
        [InlineData("run_lisp", "Run LISP")]
        [InlineData("create_mtext", "Create MTEXT")]
        [InlineData("get_drawing_info", "Get Drawing Info")]
        public void Format_humanizes_wire_names(string wire, string expected)
        {
            Assert.Equal(expected, ToolNameFormatter.Format(wire));
        }
    }

    public class PluginSettingsTests : IDisposable
    {
        private readonly string _dir;

        public PluginSettingsTests()
        {
            _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwg-settings-test-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_dir);
            PluginSettings.FilePathOverride = System.IO.Path.Combine(_dir, "settings.json");
        }

        public void Dispose()
        {
            PluginSettings.FilePathOverride = null;
            try { System.IO.Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Fact]
        public void SaveEnableToast_round_trips_through_file()
        {
            PluginSettings.SaveEnableToast(false);
            Assert.Equal(false, PluginSettings.ReadEnableToast(PluginSettings.FilePathOverride));

            PluginSettings.SaveEnableToast(true);
            Assert.Equal(true, PluginSettings.ReadEnableToast(PluginSettings.FilePathOverride));
        }

        [Fact]
        public void SaveEnableToast_preserves_other_keys()
        {
            System.IO.File.WriteAllText(PluginSettings.FilePathOverride, "{\"otherKey\":42,\"enableToast\":true}");
            PluginSettings.SaveEnableToast(false);
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(PluginSettings.FilePathOverride));
            Assert.Equal(42, json["otherKey"].ToObject<int>());
            Assert.False(json["enableToast"].ToObject<bool>());
        }

        [Fact]
        public void LoadToastEnabled_defaults_true_when_nothing_set()
        {
            // FilePathOverride points at a nonexistent file in a fresh dir.
            var env = System.Environment.GetEnvironmentVariable(PluginSettings.EnvEnableToast);
            if (env != null)
                return; // env overlay legitimately wins; skip assertion under it
            Assert.True(PluginSettings.LoadToastEnabled());
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("true", true)]
        [InlineData("off", false)]
        [InlineData("garbage", null)]
        [InlineData(null, null)]
        public void ParseBool_handles_common_spellings(string raw, bool? expected)
        {
            Assert.Equal(expected, PluginSettings.ParseBool(raw));
        }

        [Fact]
        public void SaveToastIdleSeconds_keeps_only_the_allowed_values()
        {
            Assert.True(PluginSettings.SaveToastIdleSeconds(30));
            Assert.Equal(30, PluginSettings.LoadToastIdleSeconds());

            Assert.True(PluginSettings.SaveToastIdleSeconds(15));
            Assert.Equal(PluginSettings.DefaultToastIdleSeconds, PluginSettings.ReadToastIdleSeconds(PluginSettings.FilePathOverride));
        }

        [Fact]
        public void SaveToastIdleSeconds_preserves_enableToast()
        {
            PluginSettings.SaveEnableToast(false);
            PluginSettings.SaveToastIdleSeconds(60);
            Assert.False(PluginSettings.ReadEnableToast(PluginSettings.FilePathOverride));
            Assert.Equal(60, PluginSettings.ReadToastIdleSeconds(PluginSettings.FilePathOverride));
        }
    }
}
