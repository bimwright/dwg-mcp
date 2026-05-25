using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class QueryTools
    {
        [McpServerTool(Name = "dwg_get_selected_texts", ReadOnly = true, Idempotent = true), Description(
            "Read and cluster text entities currently selected in AutoCAD. " +
            "Returns pre-clustered groups with combined text in reading order. " +
            "Each cluster has id, text, entity_count, in_block (bool) and " +
            "rewrite_mode (one of: update, collapse, rewrite_in_block) - the " +
            "mode that dwg_translate_and_rewrite will apply if given a translation " +
            "for this cluster. Use this BEFORE dwg_translate_and_rewrite. The user " +
            "must select entities in AutoCAD BEFORE calling this tool.")]
        public static Task<string> GetSelectedTexts(
            [Description("Optional grouping strength: weak, normal, or strong. Use weak when an image/layout reference indicates nearby labels should stay separate.")] string groupingStrength = "normal",
            [Description("When true, include each child text entity's handle/text/position so callers can recover from over-grouped clusters.")] bool includeEntities = false)
        {
            var request = new { grouping_strength = groupingStrength, include_entities = includeEntities };
            return ToolGateway.LoggedCall("get_selected_texts", request, request);
        }
    }
}
