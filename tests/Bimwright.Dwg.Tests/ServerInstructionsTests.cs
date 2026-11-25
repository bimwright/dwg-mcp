using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ServerInstructionsTests
    {
        private const int MaxInstructionBytes = 2048;
        private const int MaxToolDescriptionChars = 4096;
        private const int MaxParameterDescriptionBytes = 2048;

        private static readonly (string Name, string Description, MethodInfo Method)[] AllToolMethods =
            typeof(QueryTools).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .Select(method => (Method: method, Tool: method.GetCustomAttribute<McpServerToolAttribute>(), Description: method.GetCustomAttribute<DescriptionAttribute>()))
                .Where(pair => pair.Tool != null)
                .Select(pair => (pair.Tool.Name, pair.Description?.Description ?? "", pair.Method))
                .ToArray();

        // INSTR-MAX-2KB: the Reporting part is the only startup-dependent content.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Instructions_fit_in_2048_utf8_bytes_INSTR_MAX_2KB(bool reporting)
        {
            var text = ServerInstructions.Compose(reporting);
            var bytes = Encoding.UTF8.GetByteCount(text);
            Assert.True(bytes <= MaxInstructionBytes,
                $"Instructions are {bytes} UTF-8 bytes (reporting={reporting}).");
        }

        [Fact]
        public void Compose_adds_the_reporting_part_only_when_requested()
        {
            Assert.Contains(ServerInstructions.Reporting, ServerInstructions.Compose(true), StringComparison.Ordinal);
            Assert.DoesNotContain(ServerInstructions.Reporting, ServerInstructions.Compose(false), StringComparison.Ordinal);
        }

        // INSTR-SAFETY-BLOCK, FC-SRV-06, BW-PERM-001 R-C
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Instructions_end_with_the_safety_and_permissions_block_FC_SRV_06(bool reporting)
        {
            var text = ServerInstructions.Compose(reporting);
            Assert.StartsWith("Safety & permissions:", ServerInstructions.SafetyAndPermissions, StringComparison.Ordinal);
            Assert.EndsWith(ServerInstructions.SafetyAndPermissions, text, StringComparison.Ordinal);
            Assert.Equal(text.IndexOf("Safety & permissions", StringComparison.Ordinal),
                text.LastIndexOf("Safety & permissions", StringComparison.Ordinal));
        }

        [Fact]
        public void Safety_block_keeps_lisp_refusal_and_no_send_code_bypass_INSTR_SAFETY_BLOCK()
        {
            var safety = ServerInstructions.SafetyAndPermissions;
            Assert.Contains("prefer read tools", safety, StringComparison.Ordinal);
            Assert.Contains("confirmation", safety, StringComparison.Ordinal);
            Assert.Contains("dwg_run_lisp execution is always blocked", safety, StringComparison.Ordinal);
            Assert.Contains("do not repeat it via dwg_send_code", safety, StringComparison.Ordinal);
            Assert.Contains("give full paths", safety, StringComparison.Ordinal);
        }

        // INSTR-ORDER (FC-SRV-06, BW-PERM-001 R-C): product keywords, target -> health -> [report], tool index, safety.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Instructions_follow_the_family_order_INSTR_ORDER(bool reporting)
        {
            var text = ServerInstructions.Compose(reporting);
            var positions = reporting
                ? new[]
                {
                    text.IndexOf(ServerInstructions.Product, StringComparison.Ordinal),
                    text.IndexOf("dwg_switch_target", StringComparison.Ordinal),
                    text.IndexOf("dwg_health(check_updates=true)", StringComparison.Ordinal),
                    text.IndexOf(ServerInstructions.Reporting, StringComparison.Ordinal),
                    text.IndexOf(ServerInstructions.ToolIndex, StringComparison.Ordinal),
                    text.IndexOf(ServerInstructions.SafetyAndPermissions, StringComparison.Ordinal)
                }
                : new[]
                {
                    text.IndexOf(ServerInstructions.Product, StringComparison.Ordinal),
                    text.IndexOf("dwg_switch_target", StringComparison.Ordinal),
                    text.IndexOf("dwg_health(check_updates=true)", StringComparison.Ordinal),
                    text.IndexOf(ServerInstructions.ToolIndex, StringComparison.Ordinal),
                    text.IndexOf(ServerInstructions.SafetyAndPermissions, StringComparison.Ordinal)
                };
            Assert.Equal(0, positions[0]);
            Assert.All(positions, position => Assert.True(position >= 0));
            Assert.Equal(positions.OrderBy(position => position), positions);
        }

        [Fact]
        public void Instructions_point_to_survey_before_modifying_INSTR_SURVEY_HINT()
        {
            Assert.Contains("Before modifying existing entities call dwg_survey_change_impact; afterwards read the result back.",
                ServerInstructions.Text, StringComparison.Ordinal);
        }

        // TOOL-SURVEY-HINT (BW-MCP-UPGRADE-001): modifying/erasing/compound tools point to dwg_survey_change_impact.
        [Theory]
        [InlineData("dwg_update_texts")]
        [InlineData("dwg_change_layer")]
        [InlineData("dwg_change_color")]
        [InlineData("dwg_move_entities")]
        [InlineData("dwg_rotate_entities")]
        [InlineData("dwg_scale_entities")]
        [InlineData("dwg_erase_entities")]
        [InlineData("dwg_translate_and_rewrite")]
        [InlineData("dwg_apply_unicode_style")]
        [InlineData("dwg_collapse_and_rewrite")]
        [InlineData("dwg_set_block_attributes")]
        [InlineData("dwg_explode_block")]
        [InlineData("dwg_batch_execute")]
        [InlineData("dwg_run_baked_tool")]
        [InlineData("dwg_send_code")]
        public void Modifying_and_compound_tools_point_to_survey_change_impact_TOOL_SURVEY_HINT(string toolName)
        {
            var tool = Assert.Single(AllToolMethods, t => t.Name == toolName);
            Assert.Contains("dwg_survey_change_impact", tool.Description, StringComparison.Ordinal);
        }

        [Fact]
        public void Instructions_keep_the_critical_behaviour_rules()
        {
            var text = ServerInstructions.Compose(true);
            Assert.Contains("Subagents never switch or rebind", text, StringComparison.Ordinal);
            Assert.Contains("Never retry a timed-out write without checking it", text, StringComparison.Ordinal);
            Assert.Contains("ACAD_BUSY/QUEUE_FULL mean not started", text, StringComparison.Ordinal);
            Assert.Contains("call dwg_report_task_result once before your final answer", text, StringComparison.Ordinal);
            Assert.Contains("--toolsets all", text, StringComparison.Ordinal);
        }

        // INSTR-MOVE-TO-TOOL: tool descriptions are read untruncated only up to 4096 characters (and UTF-8 bytes).
        [Fact]
        public void Every_tool_description_fits_in_4096_characters_INSTR_MOVE_TO_TOOL()
        {
            Assert.Equal(78, AllToolMethods.Length);
            Assert.All(AllToolMethods, tool =>
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has an empty description.");
                Assert.True(tool.Description.Length <= MaxToolDescriptionChars,
                    $"{tool.Name} description is {tool.Description.Length} characters.");
                var bytes = Encoding.UTF8.GetByteCount(tool.Description);
                Assert.True(bytes <= MaxToolDescriptionChars,
                    $"{tool.Name} description is {bytes} UTF-8 bytes.");
            });
        }

        [Fact]
        public void Every_tool_parameter_description_fits_in_2048_utf8_bytes()
        {
            foreach (var tool in AllToolMethods)
                foreach (var param in tool.Method.GetParameters())
                {
                    var desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                    var bytes = Encoding.UTF8.GetByteCount(desc);
                    Assert.True(bytes <= MaxParameterDescriptionBytes,
                        $"{tool.Name}.{param.Name} description is {bytes} UTF-8 bytes.");
                }
        }

        [Fact]
        public void Every_tool_name_and_index_entry_in_instructions_matches_real_tools()
        {
            var names = AllToolMethods.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var text = ServerInstructions.Compose(true);
            foreach (Match m in Regex.Matches(text, @"dwg_[a-z0-9_]+[a-z0-9]"))
                Assert.Contains(m.Value, names);
            foreach (Match m in Regex.Matches(ServerInstructions.ToolIndex, @"\b([a-z]+_[a-z0-9_]+)\b"))
                Assert.Contains("dwg_" + m.Groups[1].Value, names);
        }

        // INSTR-MOVE-TO-TOOL: per-tool guidance moved out of instructions lives in the owning tool description.
        [Theory]
        [InlineData("dwg_health", "update.notification")]
        [InlineData("dwg_health", "bundle_update_proposal_allowed")]
        [InlineData("dwg_get_drawing_profile", "PROFILE_SCOPE_UNSUPPORTED")]
        [InlineData("dwg_query_entities", "QUERY_FEATURE_UNSUPPORTED")]
        [InlineData("dwg_get_selected_texts", "TEXT_FEATURE_UNSUPPORTED")]
        [InlineData("dwg_get_drawing_info", "INSUNITS=0")]
        [InlineData("dwg_open_drawing", "dwg_list_recent_drawings")]
        [InlineData("dwg_switch_target", "dwg_get_current_target")]
        [InlineData("dwg_batch_execute", "scope=plugin_result")]
        [InlineData("dwg_send_code", "--disable-send-code")]
        public void Tool_specific_detail_lives_in_owning_tool_description_INSTR_MOVE_TO_TOOL(string toolName, string detail)
        {
            var tool = Assert.Single(AllToolMethods, t => t.Name == toolName);
            Assert.Contains(detail, tool.Description, StringComparison.Ordinal);
            Assert.DoesNotContain(detail, ServerInstructions.Compose(true), StringComparison.Ordinal);
        }

        // BW-PERM-001 R-D: dwg_capture_view_image states impact and recoverability in its first sentence.
        [Fact]
        public void Capture_view_image_states_impact_and_undo_in_first_sentence_BW_PERM_001_RD()
        {
            var tool = Assert.Single(AllToolMethods, t => t.Name == "dwg_capture_view_image");
            var endOfFirstSentence = tool.Description.IndexOf("). ", StringComparison.Ordinal);
            Assert.True(endOfFirstSentence > 0, "expected impact/undo parenthetical in first sentence");
            var firstSentence = tool.Description[..(endOfFirstSentence + 2)];
            Assert.StartsWith("Write a raster image file", firstSentence, StringComparison.Ordinal);
            Assert.Contains("UNDO", firstSentence, StringComparison.Ordinal);
            Assert.Contains("overwritten", firstSentence, StringComparison.OrdinalIgnoreCase);
        }

        // INSTR-MOVE-TO-TOOL: a passage lives either in the instructions or in one tool description.
        [Fact]
        public void Instruction_sentences_are_not_repeated_in_tool_descriptions_INSTR_MOVE_TO_TOOL()
        {
            var sentences = ServerInstructions.Compose(true)
                .Split(new[] { ". ", ".\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(sentence => sentence.Trim())
                .Where(sentence => sentence.Length >= 40)
                .ToArray();
            Assert.NotEmpty(sentences);
            foreach (var tool in AllToolMethods)
                foreach (var sentence in sentences)
                    Assert.False(tool.Description.Contains(sentence, StringComparison.Ordinal),
                        $"{tool.Name} repeats instruction text: {sentence}");
        }
    }
}
