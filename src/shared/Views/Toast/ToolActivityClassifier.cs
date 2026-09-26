using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public enum ToolActivityKind
    {
        Read,
        Write
    }

    public static class ToolActivityClassifier
    {
        private static readonly HashSet<string> ExactWriteCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "batch_execute",
            "send_code",
            "run_baked_tool",
            "translate_and_rewrite",
            "collapse_and_rewrite"
        };

        private static readonly string[] WritePrefixes =
        {
            "move_", "rotate_", "scale_", "copy_", "erase_", "offset_", "explode_", "insert_",
            "create_",
            "delete_",
            "set_",
            "add_",
            "update_",
            "change_",
            "apply_",
            "assign_",
            "tag_",
            "override_",
            "clear_",
            "remove_",
            "rename_",
            "replace_",
            "duplicate_",
            "connect_",
            "bind_",
            "operate_",
            "load_family",
            "unload_",
            "reload_",
            "place_",
            "purge_",
            "renumber_",
            "wipe_",
            "auto_",
            "acquire_",
            "publish_",
            "import_",
            "export_",
            "batch_export",
            "save_",
            "link_",
            "workflow_",
            "color_"
        };

        public static ToolActivityKind Classify(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return ToolActivityKind.Read;

            if (ExactWriteCommands.Contains(commandName))
                return ToolActivityKind.Write;

            foreach (var prefix in WritePrefixes)
            {
                if (commandName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return ToolActivityKind.Write;
            }

            return ToolActivityKind.Read;
        }
    }
}
