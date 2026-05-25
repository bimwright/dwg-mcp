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
- `dwg_batch_execute` and `dwg_create_bake_issue_draft`, completing the v1 16-tool surface.
- AutoCAD API execution serialization through `DwgApiExecutor`.
- Command schema validation, response-size guardrails, batch execution preflight, and error/secret sanitization.
- Discovery v2 now writes `acad_year` and stable `pipe_name` fields; server still reads transitional `target`/`version` fields.
- Baked source redaction, `usage_events` storage, and minimal Memory/Logging support for ToolBaker pattern detection.

Notes:

- `dwg_send_code` still requires both server opt-in (`--enable-send-code` or `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`) and AutoCAD-side `MCPENABLECODE`.
- Server/tests can pass without release-building every AutoCAD shell. Shipping a year requires matching Autodesk managed assemblies on the release machine.
- `BIMWRIGHT_DWG_ALLOW_LAN_BIND` / `--allow-lan-bind` is parsed and reserved for a future plugin-side LAN bind transport path.

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
