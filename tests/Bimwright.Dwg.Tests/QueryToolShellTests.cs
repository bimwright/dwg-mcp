using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using ModelContextProtocol.Server;
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
