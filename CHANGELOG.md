# Changelog

## 1.0.0-dev — 2026-05-25

Breaking changes:

- MCP tools now use the `dwg_` prefix. For example, `get_selected_texts` is now `dwg_get_selected_texts`, and `send_code` is now `dwg_send_code`.
- Server startup now supports toolset filtering with `--toolsets`, `--read-only`, and 4-digit AutoCAD target routing.

Added:

- AutoCAD 2022, 2023, 2025, 2026, and 2027 shell projects, with 2024 remaining the default local solution shell.
- Discovery v2 through `%LOCALAPPDATA%\Bimwright\Dwg\acad-YYYY.json`, plus legacy `portAcad24.txt` fallback for AutoCAD 2024.
- Target routing tools: `dwg_list_available_targets`, `dwg_get_current_target`, and `dwg_switch_target`.
- Optional ToolBaker toolset backed by server-owned SQLite storage.
- `dwg_batch_execute` and `dwg_create_bake_issue_draft` for meta and ToolBaker workflows.
- General CAD foundation tools: `dwg_get_drawing_info`, `dwg_get_entity_properties`, `dwg_list_layers`, `dwg_create_layer`, `dwg_create_line`, `dwg_create_circle`, and `dwg_change_layer`.
- AutoCAD API execution serialization through `DwgApiExecutor`.
- Command schema validation, response-size guardrails, batch execution preflight, and error/secret sanitization.
- Discovery v2 now writes `acad_year` and stable `pipe_name` fields; server still reads transitional `target`/`version` fields.
- Baked source redaction, `usage_events` storage, and minimal Memory/Logging support for ToolBaker pattern detection.
- Manual scratch-DWG smoke checklist for the CAD foundation tools, including active-document and hex-handle expectations.
- Plan 2 core CAD expansion tools: model-space query/count/select by layer/type, create point/polyline/rectangle/arc/ellipse, move/rotate/scale/copy/erase, change color, and offset curve entities.
- Manual smoke checklist now covers Plan 2 core CAD operations and the existing text translation workflow.

Notes:

- Default startup exposes 32 tools. Optional `code` and `toolbaker` toolsets bring the backed MCP surface to 39 tools.
- Plan 2 query expansion is model-space only. `dwg_select_by_layer` and `dwg_select_by_type` return handle lists and do not change AutoCAD pickfirst selection.
- `dwg_send_code` still requires both server opt-in (`--enable-send-code` or `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`) and AutoCAD-side `MCPENABLECODE`.
- Server/tests can pass without release-building every AutoCAD shell. Shipping a year requires matching Autodesk managed assemblies on the release machine.
- `BIMWRIGHT_DWG_ALLOW_LAN_BIND` / `--allow-lan-bind` is parsed and reserved for a future plugin-side LAN bind transport path. The server emits a stderr warning when the flag is set so the operator is not misled.

Documented deviations from the design spec:

- ToolBaker stays opt-in by toolset selection (`--toolsets query,modify,meta,toolbaker` or `--toolsets all`). The spec listed it as default-on; v1.0 keeps it off to prevent accepted baked tools from running drawing mutations without an explicit opt-in. `--disable-toolbaker` or `BIMWRIGHT_DWG_ENABLE_TOOLBAKER=0` can still suppress it when requested.
- Schema validation uses a Newtonsoft-based `CommandSchema` validator instead of NJsonSchema. Net48 packaging risk drove the substitution; migration to NJsonSchema is planned for v1.1.
- `dwg_batch_execute` runs sub-commands as a logical batch without an AutoCAD undo group. Failed batches commit partial changes; a `TransactionGroup`-equivalent wrapper is a v1.1 candidate after a compile spike.
- ToolBaker baked tools are declarative preset/macro records dispatching existing `IAcadCommand` handlers. Full Roslyn-compiled user code is deferred to a separate release gate.
- `--allow-lan-bind` is parsed but not yet wired to the plugin transport binding. The plugin still listens on loopback only; the option is reserved.
- The `BakeInboxWindow` WPF UI is deferred to v1.1. v1.0 exposes the same workflow through MCP tools (`dwg_list_bake_suggestions`, `dwg_accept_bake_suggestion`, `dwg_dismiss_bake_suggestion`, `dwg_create_bake_issue_draft`).
- `Memory/` and `Logging/` modules ship as minimal scaffolding (session context, journal entries, pattern detector, session log, summary generator). Full audit-grade JSONL + rolling debug log roll-up is planned for v1.1.

## 0.1.0 — 2026-05-03

Initial public release.

- 6 MCP tools: get_selected_texts, translate_and_rewrite, collapse_and_rewrite, update_texts, apply_unicode_style, send_code
- Spatial text clustering (block-aware, Y-rows, X-columns, paragraphs)
- Automatic MText conversion, Unicode style, height scaling
- .NET 8 MCP server (dotnet global tool)
- AutoCAD 2024 plugin (.NET 4.8)
- TCP transport with token auth and PID-verified discovery
- Auto-deploy via ApplicationPlugins .bundle
- GitHub Actions CI (server + plugin)
- 86 unit tests
