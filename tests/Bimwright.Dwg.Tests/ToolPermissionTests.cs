using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Plugin.ToolBaker;
using Bimwright.Dwg.Tests.Helpers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public class ToolPermissionTests
    {
        private static MethodInfo[] Methods => typeof(Program).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null).ToArray();

        [Fact]
        public void Every_non_code_tool_sets_all_four_hints_explicitly()
        {
            Assert.Equal(66, Methods.Length);
            foreach (var method in Methods)
            {
                var attribute = method.CustomAttributes.Single(a => a.AttributeType == typeof(McpServerToolAttribute));
                var name = method.GetCustomAttribute<McpServerToolAttribute>().Name;
                var hints = attribute.NamedArguments.Select(a => a.MemberName).Where(n => n != "Name").ToArray();
                if (name == "dwg_send_code") { Assert.Empty(hints); continue; }
                foreach (var hint in new[] { "ReadOnly", "Destructive", "Idempotent", "OpenWorld" })
                    Assert.True(hints.Contains(hint), name + " must explicitly set " + hint);
                Assert.Equal(name == "dwg_health", method.GetCustomAttribute<McpServerToolAttribute>().OpenWorld);
            }
        }

        [Theory]
        [InlineData("CaptureViewImage")]
        [InlineData("InspectViewRegion")]
        [InlineData("RestoreView")]
        public void Image_file_tools_are_writes(string method)
            => Assert.False(typeof(ViewTools).GetMethod(method).GetCustomAttribute<McpServerToolAttribute>().ReadOnly);

        public static IEnumerable<object[]> WriteCommands()
            => Methods.Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(tool => !tool.ReadOnly).Select(tool => new object[] { tool.Name });

        [Theory]
        [MemberData(nameof(WriteCommands))]
        public async Task Read_only_refuses_all_write_commands_before_either_wire_boundary(string name)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                var parameters = JObject.Parse("{\"commands\":[{\"cmd\":\"get_drawing_info\"},{\"cmd\":\"erase_entities\"}]}");
                foreach (var command in new[] { name, name.Substring(4) })
                {
                    var logged = await Capture.Local(() => ToolGateway.LoggedCall(command, parameters, parameters));
                    Assert.False(logged.Value<bool>("ok"));
                    Assert.Contains("read-only", logged.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
                    var raw = await Capture.Local(async () => Newtonsoft.Json.JsonConvert.SerializeObject(await ToolGateway.SendRaw(command, parameters)));
                    Assert.False(raw.Value<bool>("ok"));
                }
            }
            finally { ServerState.Config = previous; }
        }

        [Theory]
        [InlineData("get_drawing_info")]
        [InlineData("zoom_extents")]
        [InlineData("select_by_layer")]
        public async Task Read_only_keeps_queries_and_transient_UI_calls(string command)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                var capture = await Capture.Send(() => ToolGateway.LoggedCall(command, new { }, new { }));
                Assert.Equal(command, capture.Command);
            }
            finally { ServerState.Config = previous; }
        }

        [Theory]
        [InlineData("create_line")]
        [InlineData("save_drawing")]
        [InlineData("run_baked_tool")]
        [InlineData("capture_view_image")]
        [InlineData("batch_execute")]
        public async Task Full_mode_keeps_write_dispatch_unchanged(string command)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = false };
                var parameters = new { marker = "fixture" };
                var logged = await Capture.Send(() => ToolGateway.LoggedCall(command, parameters, parameters));
                Assert.Equal(command, logged.Command);
                Assert.Equal("fixture", logged.Json().Value<string>("marker"));
                var raw = await Capture.Send(async () => Newtonsoft.Json.JsonConvert.SerializeObject(await ToolGateway.SendRaw(command, parameters)));
                Assert.Equal(command, raw.Command);
                Assert.Equal("fixture", raw.Json().Value<string>("marker"));
            }
            finally { ServerState.Config = previous; }
        }

        [Theory]
        [InlineData("capture")]
        [InlineData("inspect")]
        [InlineData("restore")]
        public async Task Read_only_image_tools_refuse_before_file_or_camera_dispatch(string operation)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                var response = await Capture.Local(async () =>
                {
                    var result = operation switch
                    {
                        "capture" => await ViewTools.CaptureViewImage(),
                        "inspect" => await ViewTools.InspectViewRegion("fixture", new ViewImageRegion { Left = 0, Top = 0, Right = 1, Bottom = 1 }),
                        _ => await ViewTools.RestoreView("fixture")
                    };
                    Assert.True(result.IsError);
                    return ((TextContentBlock)Assert.Single(result.Content)).Text;
                });
                Assert.False(response.Value<bool>("ok"));
                Assert.Contains("read-only", response.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public void BakeDb_reads_of_absent_database_do_not_create_directories_or_files()
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-bake-read-" + Guid.NewGuid().ToString("N"));
            using var db = new BakeDb(Path.Combine(root, "bake.db"));
            Assert.Empty(db.ReadRegistryRecords());
            Assert.Null(db.GetRegistryRecord("missing"));
            Assert.Empty(db.ListSuggestions());
            Assert.Null(db.GetSuggestion("missing"));
            Assert.False(Directory.Exists(root));
        }

        [Theory]
        [InlineData("unknown_command")]
        [InlineData("apply_bake")]
        [InlineData(" DWG_CAPTURE_VIEW_IMAGE ")]
        public async Task Read_only_fails_closed_for_unknown_or_internal_write_commands(string name)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                var result = await Capture.Local(() => ToolGateway.LoggedCall(name, new { }, new { }));
                Assert.False(result.Value<bool>("ok"));
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public async Task Read_only_baked_tools_refuse_before_database_or_plugin_changes()
        {
            var previous = ServerState.Config;
            var paths = new BakePaths(Path.Combine(Path.GetTempPath(), "dwg-bake-guard-" + Guid.NewGuid().ToString("N")));
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                ToolBakerTools.PathsOverride = paths;
                foreach (var call in new Func<Task<string>>[]
                {
                    () => ToolBakerWriteTools.RunBakedTool("fixture"),
                    () => ToolBakerWriteTools.AcceptBakeSuggestion("fixture", "fixture"),
                    () => ToolBakerWriteTools.DismissBakeSuggestion("fixture", "never")
                })
                {
                    var result = await Capture.Local(call);
                    Assert.False(result.Value<bool>("ok"));
                    Assert.Contains("read-only", result.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
                }
                Assert.False(Directory.Exists(paths.Root));
            }
            finally
            {
                ToolBakerTools.PathsOverride = null;
                ServerState.Config = previous;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Baked_read_tools_never_create_or_change_database_files(bool existing)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-bake-read-tools-" + Guid.NewGuid().ToString("N"));
            var paths = new BakePaths(root);
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                if (existing)
                {
                    using var db = new BakeDb(paths);
                    db.Migrate();
                    db.TryInsertRegistryRecord(new BakedToolRecord { Name = "fixture", Description = "Fixture", Source = "preset", ParamsSchema = "{}" });
                    db.UpsertSuggestion(new BakeSuggestionRecord
                    {
                        Id = "fixture", ClusterKey = "fixture", Source = "preset", Title = "Fixture",
                        Description = "Fixture", State = BakeSuggestionStates.Open, Score = 1,
                        CreatedAt = DateTimeOffset.UtcNow.ToString("o"), UpdatedAt = DateTimeOffset.UtcNow.ToString("o"), PayloadJson = "{}"
                    });
                }
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                var before = Fingerprints(root);
                ToolBakerTools.PathsOverride = paths;
                var tools = await Capture.Local(ToolBakerTools.ListBakedTools);
                var suggestions = await Capture.Local(ToolBakerTools.ListBakeSuggestions);
                var issue = await Capture.Local(() => ToolBakerTools.CreateBakeIssueDraft("fixture"));
                Assert.Equal(existing ? 1 : 0, ((JArray)tools["tools"]).Count);
                Assert.Equal(existing ? 1 : 0, ((JArray)suggestions["suggestions"]).Count);
                Assert.Equal(existing, issue.Value<bool>("ok"));
                Assert.Equal(before, Fingerprints(root));
            }
            finally
            {
                ToolBakerTools.PathsOverride = null;
                ServerState.Config = previous;
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(root).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unsafe test cleanup path");
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Read_only_catalog_has_only_tools_that_are_exposed_as_reads()
        {
            var catalog = ToolCatalogBuilder.Build(new[] { typeof(ViewTools), typeof(CodeTools), typeof(MetaTools) }, readOnly: true);
            var names = catalog["tools"].Select(tool => tool.Value<string>("name")).ToArray();
            Assert.Equal(6, names.Length);
            Assert.Contains("dwg_switch_target", names);
            Assert.DoesNotContain("dwg_capture_view_image", names);
            Assert.DoesNotContain("dwg_restore_view", names);
            Assert.DoesNotContain("dwg_send_code", names);
        }

        private static string[] Fingerprints(string root)
            => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path).Select(path => Path.GetRelativePath(root, path) + ":" +
                    File.GetLastWriteTimeUtc(path).Ticks + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray()
                : Array.Empty<string>();
    }
}
