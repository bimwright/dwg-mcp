using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public enum ToolActivityKind { Read, Write }

    /// <summary>
    /// DWG activity classification, including transient selection/camera state.
    /// This is UI metadata, not authorization. Unknown commands are conservatively
    /// shown as writes. Keep this list aligned with the server's read-only surface.
    /// </summary>
    public static class ToolActivityClassifier
    {
        private static readonly HashSet<string> ReadCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "get_drawing_info", "get_entity_properties", "get_selected_texts",
            "list_layers", "query_entities", "count_entities", "select_by_layer", "select_by_type",
            "list_blocks", "get_block_attributes", "get_variables", "inspect_lisp",
            "list_available_targets", "get_current_target", "switch_target",
            "zoom_extents", "zoom_window", "zoom_to_entity",
            "capture_view_image", "inspect_view_region", "restore_view",
            "list_baked_tools", "list_bake_suggestions", "create_bake_issue_draft"
        };

        public static ToolActivityKind Classify(string commandName) =>
            ReadCommands.Contains(Normalize(commandName)) ? ToolActivityKind.Read : ToolActivityKind.Write;

        internal static string Normalize(string commandName)
        {
            var name = (commandName ?? string.Empty).Trim();
            if (name.StartsWith("dwg_", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(4);
            return name.ToLowerInvariant();
        }
    }
}
