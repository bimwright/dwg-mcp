using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
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

        // INSTR-MAX-2KB: the Reporting part is the only startup-dependent content.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Instructions_fit_in_2048_utf8_bytes_INSTR_MAX_2KB(bool reporting)
        {
            var text = ServerInstructions.Compose(reporting);
            Assert.True(Encoding.UTF8.GetByteCount(text) <= MaxInstructionBytes,
                $"Instructions are {Encoding.UTF8.GetByteCount(text)} UTF-8 bytes (reporting={reporting}).");
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

        // INSTR-ORDER: product keywords, target -> health -> report, tool index, safety.
        [Fact]
        public void Instructions_follow_the_family_order_INSTR_ORDER()
        {
            var text = ServerInstructions.Compose(true);
            var positions = new[]
            {
                text.IndexOf(ServerInstructions.Product, StringComparison.Ordinal),
                text.IndexOf("dwg_switch_target", StringComparison.Ordinal),
                text.IndexOf("dwg_health(check_updates=true)", StringComparison.Ordinal),
                text.IndexOf(ServerInstructions.Reporting, StringComparison.Ordinal),
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

        [Fact]
        public void Instructions_keep_the_critical_behaviour_rules()
        {
            var text = ServerInstructions.Compose(true);
            Assert.Contains("Subagents never switch or rebind", text, StringComparison.Ordinal);
            Assert.Contains("Never retry a timed-out write without checking it", text, StringComparison.Ordinal);
            Assert.Contains("ACAD_BUSY/QUEUE_FULL mean not started", text, StringComparison.Ordinal);
            Assert.Contains("call dwg_report_task_result once before your final answer", text, StringComparison.Ordinal);
        }

        // INSTR-MOVE-TO-TOOL: tool descriptions are read untruncated only up to 4096 characters.
        [Fact]
        public void Every_tool_description_fits_in_4096_characters_INSTR_MOVE_TO_TOOL()
        {
            var tools = ToolDescriptions();
            Assert.NotEmpty(tools);
            Assert.All(tools, tool => Assert.True(tool.Description.Length <= MaxToolDescriptionChars,
                $"{tool.Name} description is {tool.Description.Length} characters."));
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
            foreach (var tool in ToolDescriptions())
                foreach (var sentence in sentences)
                    Assert.False(tool.Description.Contains(sentence, StringComparison.Ordinal),
                        $"{tool.Name} repeats instruction text: {sentence}");
        }

        private static (string Name, string Description)[] ToolDescriptions()
            => typeof(QueryTools).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .Select(method => (Tool: method.GetCustomAttribute<McpServerToolAttribute>(), Description: method.GetCustomAttribute<DescriptionAttribute>()))
                .Where(pair => pair.Tool != null)
                .Select(pair => (pair.Tool.Name, pair.Description?.Description ?? ""))
                .ToArray();
    }
}
