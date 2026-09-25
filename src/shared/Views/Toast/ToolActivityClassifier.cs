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
            "run_lisp",
            "run_baked_tool",
            "apply_bake"
        };

        private static readonly string[] WritePrefixes =
        {
            "create_",
            "delete_",
            "set_",
            "add_",
            "update_",
            "change_",
            "apply_",
            "insert_",
            "explode_",
            "move_",
            "rotate_",
            "scale_",
            "copy_",
            "erase_",
            "offset_",
            "collapse_",
            "translate_",
            "export_",
            "save_",
            "purge_",
            "clear_",
            "remove_",
            "rename_",
            "replace_",
            "import_",
            "wipe_"
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
