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

- Confirm `dwg_send_code`/`dwg_run_lisp` are present on the default tool surface (via `meta`) and absent under `--read-only`.
- Confirm `dwg_send_code` works out of the box; run `MCPDISABLECODE` inside AutoCAD and confirm it fails, then `MCPENABLECODE` and confirm it works again.
- Confirm `dwg_run_lisp` with `code="(+ 1 2 3)"` returns `result` `6`; then a real `.lsp` via `file` + `command="(c:MYCMD)"`.
- Confirm a synchronous `dwg_send_code` DTO + stdout response works, while `async`/`await` is refused before any drawing mutation and `return doc;` (also nested in a DTO) reports a DTO error.
- Confirm `batch_execute` containing `run_lisp` rejects the whole batch before earlier items run; use the direct `dwg_run_lisp` tool instead.
- Automated tests cover severity detection after 200 findings, refusal above 2,000,000 characters (inline/file), and masked LISP error payloads. Run `dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj -c Release`.
- Live acceptance remains separate from these tests: verify LISP completion while AutoCAD is idle/busy, prompting commands, and timeout/queued-wrapper behavior in a disposable drawing. Test doubles and plugin compilation do not close these host-runtime gates.

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
