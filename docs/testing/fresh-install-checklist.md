# Fresh Install Checklist

Use this checklist before publishing a dwg-mcp release or validating a clean machine install.

## Server

- Install or pack the server:

```powershell
dotnet pack src\server\Bimwright.Dwg.Server.csproj -c Release --output artifacts-review
dotnet tool install -g Bimwright.Dwg.Server --add-source artifacts-review --version 1.0.0-dev
```

- Confirm startup:

```powershell
bimwright-dwg --target 2024
```

- Confirm read-only mode exposes only query/routing tools and ToolBaker read tools if the toolset is enabled:

```powershell
bimwright-dwg --read-only --target 2024
```

## Plugin

- Build the target AutoCAD shell on a prepared machine:

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Release /nr:false
```

- Install the bundle for the same AutoCAD year:

```powershell
pwsh scripts\install.ps1 -Version 2024 -SourceDir src\plugin-acad24\bin\Release\net48
```

- Restart AutoCAD and confirm the command line reports that Bimwright DWG is listening.
- If auto-start fails, run `MCPSTART`.
- Confirm discovery exists at `%LOCALAPPDATA%\Bimwright\Dwg\acad-2024.json`.
- For AutoCAD 2024 only, confirm legacy `%LOCALAPPDATA%\Bimwright\portAcad24.txt` exists.

## MCP Smoke

- Call `dwg_list_available_targets` and confirm the expected year, PID, and transport.
- Call `dwg_get_current_target`; if no target is pinned, it may return `null`.
- Call `dwg_switch_target` with a 4-digit year such as `2024`; do not use R-codes.
- Call `dwg_batch_execute` with a read-only command and confirm nested batch or baked-tool calls are rejected.
- Select text entities in AutoCAD and call `dwg_get_selected_texts`.
- Run a small `dwg_translate_and_rewrite` or `dwg_apply_unicode_style` operation and verify a single AutoCAD undo reverses it.

## Security Gates

### Deferred live tests — owner decision, 2026-09-23

- [ ] **`dwg_send_code` — TEST LATER:** live AutoCAD DTO/stdout, drawing write + undo, async/await and host-object refusal, kill-switch, read-only and timeout behavior.
- [ ] **`dwg_run_lisp` — TEST LATER:** live refusal at the MCP and authenticated plugin wire boundaries, including after `MCPENABLECODE`/listener restart and while busy; no wrapper, queued command or host security-setting change. Execution remains blocked. Testing a future enabled LISP executor requires a separate approved isolation/trust design.

These two live acceptance gates are explicitly deferred, not passed. The 452 passing automated tests and successful 2024/2027 plugin builds do not close them. No deployment or live AutoCAD acceptance was performed for this change.

### Acceptance steps

- Confirm `dwg_send_code`/`dwg_run_lisp` are present on the default tool surface (via `meta`) and absent under `--read-only`.
- Confirm `dwg_send_code` works out of the box; run `MCPDISABLECODE` inside AutoCAD and confirm it fails, then `MCPENABLECODE` and confirm it works again.
- Confirm `dwg_run_lisp` refuses `code="(+ 1 2 3)"`, `file`, bare `command`, and combined inputs with `error_code="lisp_execution_blocked"`; no LISP should be executed or queued. `MCPENABLECODE` and listener restart must not enable LISP.
- Confirm a synchronous `dwg_send_code` DTO + stdout response works, while `async`/`await` is refused before any drawing mutation and `return doc;` (also nested in a DTO) reports a DTO error.
- Confirm `batch_execute` containing `run_lisp` rejects the whole batch before earlier items run; ToolBaker must also deny it. Neither response should suggest another execution route.
- Inspect clean, SHELL, staged-load and opaque sources without executing them. Every successful inspection report must include `execution_authorized=false`, `safety_assured=false` and limitations; `clean` must not be presented as a safety guarantee.
- Automated tests cover SHELL command forms, severity detection after 200 findings, source size limits, local refusal without discovery/file access, direct-handler refusal without command queueing, and input privacy. Run `dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj -c Release`.
- Update and restart both server and plugin. On a disposable drawing, confirm authenticated direct wire `run_lisp` is refused by the new plugin even from an older server, both before and after `MCPENABLECODE`, and while the drawing is busy. No wrapper/result files or delayed LISP commands should appear. Verify SECURELOAD/TRUSTEDPATHS remain unchanged. Compilation and test doubles do not close this live acceptance gate.
- Future LISP execution is a separate design/release gate: enforceable isolation/trust, exact inspected source/dependencies, atomic completion and safe idle/busy/prompt/timeout/cancel lifecycle must all be established before re-enabling it. No such executor is shipped by this change.

## ToolBaker

- Start the server with ToolBaker enabled:

```powershell
bimwright-dwg --target 2024 --toolsets query,modify,meta,toolbaker
```

- Confirm `dwg_list_baked_tools` returns the server-owned SQLite registry contents.
- Confirm `dwg_create_bake_issue_draft` returns a draft body and does not submit anything.
- Accepting a suggestion must call plugin `apply_bake` first; the server should persist to `%LOCALAPPDATA%\Bimwright\Dwg\baked\bake.db` only after plugin validation succeeds.
- Confirm accepted baked source is redacted before persistence and usage events are written to the `usage_events` table.
- `dwg_run_baked_tool` should fail for unknown names and should run only tools present in the server registry.

## Multi-Version Release Gate

Server tests and the normal solution build can pass without every AutoCAD shell being release-built. For each AutoCAD year included in a release, verify that machine has the matching Autodesk managed assemblies and run the year-specific shell build:

| AutoCAD | Project | TFM |
|---------|---------|-----|
| 2022 | `src\plugin-acad22\Bimwright.Dwg.Plugin.Acad22.csproj` | `net48` |
| 2023 | `src\plugin-acad23\Bimwright.Dwg.Plugin.Acad23.csproj` | `net48` |
| 2024 | `src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj` | `net48` |
| 2025 | `src\plugin-acad25\Bimwright.Dwg.Plugin.Acad25.csproj` | `net8.0-windows` |
| 2026 | `src\plugin-acad26\Bimwright.Dwg.Plugin.Acad26.csproj` | `net8.0-windows` |
| 2027 | `src\plugin-acad27\Bimwright.Dwg.Plugin.Acad27.csproj` | `net10.0-windows` |
