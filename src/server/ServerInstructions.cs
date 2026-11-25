namespace Bimwright.Dwg.Server
{
    // INSTR-MAX-2KB: every composition must stay at or below 2048 UTF-8 bytes (tested).
    // INSTR-ORDER: product keywords, session start (target -> health -> report), tool index, safety last.
    // Detail that belongs to one tool lives in that tool's description, not here (INSTR-MOVE-TO-TOOL).
    internal static class ServerInstructions
    {
        internal const string Product =
            "dwg-mcp - Autodesk AutoCAD 2022-2027 MCP gateway for DWG drawings: metadata, layers, blocks, entities, lines, text, dimensions, "
            + "DBText/MText, SHX/Unicode style repair, Vietnamese translation writeback, note cleanup, text rewriting, view capture, DXF export. "
            + "Tools act on the active drawing; entities are hex handles.";

        internal const string Session =
            "Start: dwg_list_available_targets, then dwg_switch_target('pid:<PID>') to verify instance and drawing before writes "
            + "(--read-only: launch with --target pid:<PID>). Then dwg_health(check_updates=true) once; follow its update guidance. "
            + "Subagents never switch or rebind the parent's target. "
            + "Never retry a timed-out write without checking it; ACAD_BUSY/QUEUE_FULL mean not started: retry when AutoCAD is idle. "
            + "Verify output_artifact SHA-256 before reading; never replay a write to recover output. "
            // INSTR-SURVEY-HINT
            + "Before modifying existing entities call dwg_survey_change_impact; afterwards read the result back.";

        internal const string Reporting =
            "After AutoCAD work that changed a drawing or found entities to check, call dwg_report_task_result once "
            + "before your final answer (not for chat-only answers).";

        internal const string ToolIndex =
            "Tools (dwg_ prefix):\n"
            + "- read: get_drawing_profile (large drawings), query_entities, get_entity_properties, list_layers\n"
            + "- edit: create_line, create_text, insert_block, move_entities, erase_entities, update_texts\n"
            + "- text: get_selected_texts -> translate_and_rewrite\n"
            + "- view: capture_view_image -> inspect_view_region\n"
            + "- drawings: list_open_documents, open_drawing, save_drawing, export_dxf\n"
            + "- meta: batch_execute, send_code, run_baked_tool";

        // INSTR-SAFETY-BLOCK / FC-SRV-06 / BW-PERM-001 R-C: always the last block.
        internal const string SafetyAndPermissions =
            "Safety & permissions: prefer read tools and dry_run previews. "
            + "Before erasing or overwriting anything not created in this session, name the exact handles or paths, "
            + "get the user's confirmation, then pass those literal values. "
            + "dwg_run_lisp execution is always blocked. "
            + "If LISP, a permission check or auto mode refuses a call, do not repeat it via dwg_send_code, batch or a baked tool; tell the user. "
            + "dwg_send_code is full-trust C#, not a sandbox. Work in the user's project folder; give full paths.";

        internal const string Text = Product + "\n\n" + Session + "\n\n" + ToolIndex + "\n\n" + SafetyAndPermissions;

        internal const string TextWithReporting =
            Product + "\n\n" + Session + "\n\n" + Reporting + "\n\n" + ToolIndex + "\n\n" + SafetyAndPermissions;

        /// <summary>The only startup-dependent part is Reporting, present when dwg_report_task_result is registered.</summary>
        internal static string Compose(bool reporting) => reporting ? TextWithReporting : Text;
    }
}
