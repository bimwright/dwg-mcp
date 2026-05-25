using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.ToolBaker
{
    public static class BakedToolDispatchAuthorizer
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "get_selected_texts",
            "update_texts",
            "apply_unicode_style",
            "collapse_and_rewrite",
            "translate_and_rewrite"
        };

        private static readonly HashSet<string> Denied = new HashSet<string>(StringComparer.Ordinal)
        {
            "send_code",
            "batch_execute",
            "run_baked_tool",
            "apply_bake",
            "list_baked_tools"
        };

        public static bool IsAllowed(string command)
            => !string.IsNullOrWhiteSpace(command) && !Denied.Contains(command) && Allowed.Contains(command);
    }
}
