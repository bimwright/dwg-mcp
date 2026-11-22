using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Shared by gateway and host. Unknown commands are treated as writes.
    public static class TargetContract
    {
        private static readonly HashSet<string> Reads = new HashSet<string>(StringComparer.Ordinal)
        {
            "health", "set_tool_catalog", "ping", "list_recent_drawings", "list_open_documents", "get_drawing_info", "get_drawing_profile",
            "get_entity_properties", "get_selected_texts", "list_layers", "query_entities",
            "count_entities", "cluster_bands", "select_by_layer", "select_by_type", "list_blocks",
            "get_block_attributes", "list_baked_tools", "get_variables",
            "zoom_extents", "zoom_window", "zoom_to_entity", "report_task_result", "history_drawing_key", "survey_change_impact"
        };

        public static bool IsWrite(string command) => !Reads.Contains(command ?? "");

        internal static bool RequiresVerifiedDrawing(string command) => command is "history_drawing_key" or "survey_change_impact";

        internal static void CheckVerifiedDrawing(JObject expected, string documentId)
        {
            if (expected?.Value<int?>("protocol") != 1 || expected.Value<bool?>("selection_confirmed") != true
                || string.IsNullOrEmpty(expected.Value<string>("document_id")))
                throw new InvalidOperationException("DOCUMENT_UNCONFIRMED: verify the intended active drawing with dwg_switch_target before this operation.");
            CheckDocument(expected, documentId, false);
        }

        public static void CheckInstance(JObject expected, int pid, DateTime startUtc, bool write)
        {
            if (expected == null || expected.Value<int?>("protocol") != 1)
            {
                if (write) throw new InvalidOperationException("PAIRING_REQUIRED: update gateway and plugin together, then call dwg_switch_target to verify the instance and drawing.");
                return;
            }
            if (expected.Value<int?>("pid") != pid
                || expected.Value<DateTime?>("process_start_utc")?.ToUniversalTime() != startUtc.ToUniversalTime())
                throw new InvalidOperationException("TARGET_CHANGED: process identity differs; command was not started. Select the intended PID again.");
            if (write && expected.Value<bool?>("selection_confirmed") != true)
                throw new InvalidOperationException("TARGET_UNCONFIRMED: call dwg_switch_target with the intended PID before writes.");
        }

        public static void CheckDocument(JObject expected, string actualDocumentId, bool write)
        {
            var documentId = expected?.Value<string>("document_id");
            if (write && string.IsNullOrEmpty(documentId))
                throw new InvalidOperationException("DOCUMENT_UNCONFIRMED: verify the active drawing with dwg_switch_target before writes.");
            if (documentId != null && !string.Equals(documentId, actualDocumentId, StringComparison.Ordinal))
                throw new InvalidOperationException("DOCUMENT_CHANGED: the active drawing changed; command was not started. Inspect and verify the intended drawing again.");
        }
    }
}
