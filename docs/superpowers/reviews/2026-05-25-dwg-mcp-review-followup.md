# dwg-mcp refactor/v1.0 Review Follow-up

Date: 2026-05-25
Branch: `refactor/v1.0`

## Fixed

- Transport routing now selects `PipeTransportServer` for `ACAD2025_OR_GREATER` and `TcpTransportServer` for 2022-2024.
- AutoCAD 2025/2026/2027 plugin projects now define cumulative version constants.
- `ITransportServer` now exposes `Kind`, `IsClientConnected`, `LastCommandTime`, and `PipeName`.
- Discovery writers now emit `acad_year` and stable `pipe_name` fields. The server reader still accepts transitional `target`/`version` fields for compatibility.
- `ResponseSizeGuard` now uses a 10MB UTF-8 byte limit and reports `original_size_bytes`.
- MCP tool surface now includes `dwg_batch_execute` and `dwg_create_bake_issue_draft`.
- Read-only registration no longer exposes `dwg_batch_execute`; ToolBaker read tools and write tools are split.
- `BakeRedactor` redacts source before accepted baked records are persisted.
- `BakeDb` now creates `usage_events`; `UsageEventLogger` writes both SQLite usage events and JSONL.
- `DwgMcpConfig` now parses `BIMWRIGHT_DWG_ALLOW_LAN_BIND` and `--allow-lan-bind`.
- Minimal `Memory/` and `Logging/` modules were added for session context, journal entries, pattern detection, session logs, and summaries.
- `MCPSTART` resets plugin-side `send_code` consent before starting a new transport.
- `dwg_switch_target` is no longer marked read-only.

## Notes and Pushback

- `AllowLanBind` is parsed but not yet wired into plugin bind behavior. The current plugin and server still run local-only by default. Full LAN binding needs an explicit plugin-side trust decision because the server config is not automatically available inside AutoCAD.
- `ITransportServer.Start(Func<string, Task<string>> onRequest)` from the original spec was not adopted in this patch. The metadata/heartbeat contract was fixed, but the current transport still owns `CommandDispatcher` internally to avoid a broader dispatcher rewrite.
- `BakeInboxWindow.cs` remains deferred. Current v1 interaction is through MCP tools (`dwg_list_bake_suggestions`, `dwg_accept_bake_suggestion`, and `dwg_create_bake_issue_draft`). Adding WPF UI requires a separate Windows Desktop compile pass across `net48`, `net8.0-windows`, and `net10.0-windows`.
- ToolBaker remains opt-in by default, although the original spec lists it as default-on. This is intentional for this branch because accepted baked tools can execute drawing mutations; read-only behavior is now split correctly when the toolset is enabled.
- The reviewer note about `_server.Port` null dereference was not reproduced. The transport status message was still changed to describe either TCP port or named pipe correctly.
- The current ToolBaker implementation remains declarative preset/macro runtime rather than a full Roslyn compiled-command pipeline. Redaction and policy hooks are in place, but full generated-source compilation should remain a separate release gate.
- Test count is 164 after this follow-up. Spot-checked tests cover executor FIFO/single-flight behavior, discovery parsing, toolset filtering, response byte truncation, usage event persistence, and the 16-tool surface.

