using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class QueryToolShellTests
    {
        private static readonly string[] QueryToolsInClass =
        {
            "dwg_count_entities",
            "dwg_get_drawing_info",
            "dwg_get_drawing_profile",
            "dwg_get_entity_properties",
            "dwg_get_selected_texts",
            "dwg_inspect_lisp",
            "dwg_list_layers",
            "dwg_query_entities",
            "dwg_select_by_layer",
            "dwg_select_by_type"
        };

        [Fact]
        public void Every_query_tool_is_listed_for_a_shell_case()
        {
            var declared = typeof(QueryTools)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .Where(name => name != null)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(QueryToolsInClass, declared);
        }

        [Fact]
        public async Task Get_drawing_info_sends_empty_payload()
        {
            var sent = await Capture.Send(() => QueryTools.GetDrawingInfo());

            Assert.Equal("get_drawing_info", sent.Command);
            Assert.Empty(sent.Json().Properties());
        }

        private static async Task<string> ProfileText(Task<CallToolResult> call)
            => ((TextContentBlock)(await call).Content[0]).Text;

        [Fact]
        public async Task Drawing_profile_sends_its_own_default_byte_cap()
        {
            var sent = await Capture.Send(() => ProfileText(QueryTools.GetDrawingProfile()));
            Assert.Equal("get_drawing_profile", sent.Command);
            Assert.Equal(JObject.Parse("{\"section\":\"summary\",\"offset\":0,\"max_output_bytes\":16384}"), sent.Json());
        }

        [Fact]
        public async Task Drawing_profile_forwards_group_continuation_without_normalizing_snapshot()
        {
            var sent = await Capture.Send(() => ProfileText(QueryTools.GetDrawingProfile("layers", 40, 8192, "snapshot:12")));
            Assert.Equal(JObject.Parse("{\"section\":\"layers\",\"offset\":40,\"max_output_bytes\":8192,\"expected_snapshot\":\"snapshot:12\"}"), sent.Json());
        }

        [Theory]
        [InlineData("all", 0, 16384, null)]
        [InlineData("summary", 1, 16384, "snapshot:12")]
        [InlineData("layers", -1, 16384, null)]
        [InlineData("layers", 1, 16384, null)]
        [InlineData("summary", 0, 4095, null)]
        [InlineData("summary", 0, 65537, null)]
        public async Task Invalid_profile_options_do_not_contact_the_plugin(string section, int offset, int bytes, string snapshot)
        {
            var error = await Capture.Local(() => ProfileText(QueryTools.GetDrawingProfile(section, offset, bytes, snapshot)));
            Assert.False(error.Value<bool>("ok"));
            Assert.NotEmpty(error.Value<string>("error"));
        }

        [Fact]
        public async Task List_layers_sends_empty_payload()
        {
            var sent = await Capture.Send(() => QueryTools.ListLayers());

            Assert.Equal("list_layers", sent.Command);
            Assert.Empty(sent.Json().Properties());
        }

        [Fact]
        public async Task Get_entity_properties_rejects_blank_handles_without_sending()
        {
            var json = await Capture.Local(() => QueryTools.GetEntityProperties("  "));

            Assert.False(json.Value<bool>("ok"));
            Assert.Contains("handles", json.Value<string>("error"));
        }

        [Fact]
        public async Task Get_entity_properties_rejects_malformed_handles_without_sending()
        {
            var json = await Capture.Local(() => QueryTools.GetEntityProperties("not-json"));

            Assert.False(json.Value<bool>("ok"));
            Assert.Contains("handles", json.Value<string>("error"));
        }

        [Fact]
        public async Task Get_entity_properties_sends_parsed_handles()
        {
            var sent = await Capture.Send(() => QueryTools.GetEntityProperties("[\"7F5AD\",\"2A4F\"]", true));

            Assert.Equal("get_entity_properties", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"handles\":[\"7F5AD\",\"2A4F\"],\"include_geometry\":true}"),
                sent.Json());
        }

        [Fact]
        public async Task Query_entities_omits_unset_filters()
        {
            var sent = await Capture.Send(() => QueryTools.QueryEntities());

            Assert.Equal("query_entities", sent.Command);
            Assert.Equal(JObject.Parse("{\"include_geometry\":false}"), sent.Json());
        }

        [Fact]
        public async Task Query_entities_forwards_filters()
        {
            var sent = await Capture.Send(() => QueryTools.QueryEntities("Line", "A-WALL", 7, 25, true));

            Assert.Equal("query_entities", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"entity_type\":\"Line\",\"layer\":\"A-WALL\",\"color_index\":7,\"limit\":25,\"include_geometry\":true}"),
                sent.Json());
        }

        [Fact]
        public async Task Paged_query_parses_only_new_JSON_options_and_preserves_legacy_filter_names()
        {
            var sent = await Capture.Send(() => QueryTools.QueryEntities("TEXT", "A", 7, 3, false, paged: true,
                bbox: "{\"min\":[-1,-2,-3],\"max\":[4,5,6]}", bbox_mode: "contains", space: "layout:Sheet A",
                fields: "[\"handle\",\"text\"]", cursor: "opaque", max_output_bytes: 4096));
            Assert.Equal("query_entities", sent.Command);
            var request = sent.Json();
            Assert.True(request.Value<bool>("paged"));
            Assert.Equal("TEXT", request.Value<string>("entity_type"));
            Assert.IsType<JObject>(request["bbox"]); Assert.IsType<JArray>(request["fields"]);
            Assert.Equal("layout:Sheet A", request.Value<string>("space"));
            Assert.Equal("opaque", request.Value<string>("cursor")); Assert.Equal(4096, request.Value<int>("max_output_bytes"));
        }

        [Theory]
        [InlineData("[\"arbitrary_property\"]", null, false)]
        [InlineData("not-json", null, false)]
        [InlineData(null, "{\"min\":[2,0,0],\"max\":[1,1,1]}", false)]
        [InlineData(null, "not-json", false)]
        [InlineData(null, null, true)]
        public async Task Paged_query_invalid_fields_box_or_geometry_never_reach_the_plugin(string fields, string bbox, bool geometry)
        {
            var result = await Capture.Local(() => QueryTools.QueryEntities(include_geometry: geometry, paged: true, fields: fields, bbox: bbox));
            Assert.False(result.Value<bool>("ok")); Assert.StartsWith("INVALID_ARGUMENT:", result.Value<string>("error"));
        }

        [Fact]
        public async Task Spatial_count_uses_the_same_box_wire_shape_without_query_only_fields()
        {
            var sent = await Capture.Send(() => QueryTools.CountEntities(bbox: "{\"min\":[0,0,0],\"max\":[1,2,3]}", bbox_mode: "intersects", space: "model"));
            Assert.Equal("count_entities", sent.Command); Assert.IsType<JObject>(sent.Json()["bbox"]);
            Assert.Equal(new[] { "bbox", "bbox_mode", "space" }, sent.Json().Properties().Select(p => p.Name).OrderBy(p => p).ToArray());
        }

        [Theory]
        [InlineData("query_entities", "{\"paged\":true}")]
        [InlineData("count_entities", "{\"space\":\"model\"}")]
        [InlineData("get_drawing_profile", "{\"space\":\"model\"}")]
        [InlineData("get_selected_texts", "{\"handles\":[]}")]
        public async Task Extended_reads_in_a_batch_are_refused_before_any_gateway_send(string command, string parameters)
        {
            var commands = new JArray(new JObject { ["cmd"] = "update_texts", ["params"] = new JObject() },
                new JObject { ["cmd"] = command, ["params"] = JObject.Parse(parameters) });
            var result = await Capture.Local(() => BatchTools.BatchExecute(commands.ToString(Newtonsoft.Json.Formatting.None)));
            Assert.False(result.Value<bool>("ok")); Assert.Contains("PAGED_QUERY_DIRECT_TOOL_ONLY", result.Value<string>("error"));
        }

        [Fact]
        public async Task Legacy_query_and_count_batches_keep_the_existing_wire_contract()
        {
            const string commands = "[{\"cmd\":\"query_entities\",\"params\":{\"limit\":2}},{\"cmd\":\"count_entities\",\"params\":{}}]";
            var sent = await Capture.Send(() => BatchTools.BatchExecute(commands));
            Assert.Equal("batch_execute", sent.Command); Assert.Equal(JArray.Parse(commands), sent.Json()["commands"]);
        }

        [Fact]
        public async Task Count_entities_omits_unset_filters()
        {
            var sent = await Capture.Send(() => QueryTools.CountEntities());

            Assert.Equal("count_entities", sent.Command);
            Assert.Empty(sent.Json().Properties());
        }

        [Fact]
        public async Task Count_entities_forwards_filters()
        {
            var sent = await Capture.Send(() => QueryTools.CountEntities("Circle", "A-PIPE", 3));

            Assert.Equal("count_entities", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"entity_type\":\"Circle\",\"layer\":\"A-PIPE\",\"color_index\":3}"),
                sent.Json());
        }

        [Fact]
        public async Task Select_by_layer_forwards_layer_and_optional_filters()
        {
            var sent = await Capture.Send(() => QueryTools.SelectByLayer("A-TEXT", "DBText", 2, 100));

            Assert.Equal("select_by_layer", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"entity_type\":\"DBText\",\"layer\":\"A-TEXT\",\"color_index\":2,\"limit\":100}"),
                sent.Json());
        }

        [Fact]
        public async Task Select_by_type_forwards_type_and_optional_filters()
        {
            var sent = await Capture.Send(() => QueryTools.SelectByType("Polyline", "A-AREA", 4, 50));

            Assert.Equal("select_by_type", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"entity_type\":\"Polyline\",\"layer\":\"A-AREA\",\"color_index\":4,\"limit\":50}"),
                sent.Json());
        }

        [Fact]
        public async Task Get_selected_texts_sends_defaults()
        {
            var sent = await Capture.Send(() => QueryTools.GetSelectedTexts());

            Assert.Equal("get_selected_texts", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"grouping_strength\":\"normal\",\"include_entities\":false}"),
                sent.Json());
        }

        [Fact]
        public async Task Get_selected_texts_forwards_grouping_and_children()
        {
            var sent = await Capture.Send(() => QueryTools.GetSelectedTexts("weak", true));

            Assert.Equal("get_selected_texts", sent.Command);
            Assert.Equal(
                JObject.Parse("{\"grouping_strength\":\"weak\",\"include_entities\":true}"),
                sent.Json());
        }

        [Fact]
        public async Task Text_handles_mode_sends_explicit_source_without_replacing_old_parameter_names()
        {
            var sent = await Capture.Send(() => QueryTools.GetSelectedTexts("weak", true, handles: "[\"F\",\"10\"]", space: "model", cursor: "opaque", max_output_bytes: 8192));
            Assert.Equal("get_selected_texts", sent.Command); var data = sent.Json();
            Assert.Equal("weak", data.Value<string>("grouping_strength")); Assert.True(data.Value<bool>("include_entities"));
            Assert.Equal(new JArray("F", "10"), data["handles"]); Assert.Equal("opaque", data.Value<string>("cursor"));
            Assert.Equal(8192, data.Value<int>("max_output_bytes"));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("not-json", null)]
        [InlineData("[]", "{\"min\":[0,0,0],\"max\":[1,1,1]}")]
        public async Task Invalid_explicit_text_sources_do_not_fall_back_to_pickfirst(string handles, string bbox)
        {
            var data = await Capture.Local(() => QueryTools.GetSelectedTexts(handles: handles, bbox: bbox, max_output_bytes: 8192));
            Assert.False(data.Value<bool>("ok")); Assert.StartsWith("INVALID_ARGUMENT:", data.Value<string>("error"));
        }

        [Fact]
        public async Task Inspect_lisp_scans_inline_code_without_sending()
        {
            var json = await Capture.Local(() => QueryTools.InspectLisp(code: "(+ 1 2)"));

            Assert.True(json.Value<bool>("ok"));
            Assert.Equal("clean", json.Value<string>("verdict"));
            Assert.False(json.Value<bool>("execution_authorized"));
        }

        [Fact]
        public async Task Inspect_lisp_requires_file_or_code_without_sending()
        {
            var json = await Capture.Local(() => QueryTools.InspectLisp());

            Assert.False(json.Value<bool>("ok"));
            Assert.Contains("at least one", json.Value<string>("error"));
        }
    }
}
