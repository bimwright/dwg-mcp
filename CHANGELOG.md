# Changelog

## 2.1.0 — Unreleased

### Health runtime and release checks

- Add one read-only `dwg_health` tool to query (including read-only mode): host/document state without requiring or locking a drawing, plus loaded server/plugin version/build/alignment. No target preserves gateway identity; legacy plugins remain explicitly unverified. A busy UI retains identity while marking document state unavailable. Authentication precedes health dispatch; health bypasses drawing locks, operation toasts and drawing batches.
- Optional `check_updates=true` compares both components with one official stable release/tag revision using a shared five-second budget and bounded metadata; routine calls remain offline. State-specific notifications distinguish new releases, revision review, missing identity, failed external lookup and newer local versions. Preserve protected components and obtain unobserved identities separately. Only a permitted `prepare_update` proposal contains installation steps and needs authorization; health never downloads packages, installs, edits client configs, saves/closes drawings or restarts processes.
- Full informational version in initialize, per-process health tool metadata in tools/list, and `client_verification` support same-connection reconnect checks. Loaded assembly/MVID and capture-time disk SHA-256 have separate evidence limits; neither alignment nor revision equality proves artifact bytes, compatibility or publisher trust. The server cannot verify client cache refresh.
- Development tool counts become 42 default / 66 all-enabled / 22 all-enabled read-only. Existing tools keep their parameters; health combines version/update checking in one endpoint. Add host-free protocol/runtime/release regression tests and agent/README guidance. AutoCAD 2024 and 2027 plugin builds verified; installed-host health acceptance is pending.

### Runtime settings and privacy

- Add `--enable-send-code` / `--disable-send-code` (default enabled) and `--enable-call-log` / `--disable-call-log` (default disabled), with JSON/env overrides and JSON → env → CLI precedence. Read-only still removes send_code; the server gate also refuses code-containing batches before dispatch and cannot be overridden by `MCPENABLECODE`.
- Call-log and legacy call-journal writers no longer create directories/files while disabled. Opted-in logs redact before persistence; code fields contain only length + UTF-8 SHA-256, including nested batch inputs. Code-related error text is omitted to avoid source echoes. Existing logs are preserved; no body cache/journal is added. ToolBaker adaptive usage and diagnostic logs are separate.
- Correct the CLI help's default toolset list (`query,modify,meta,view`) and document the paired switches in all README languages.

### Toast

- **History click-through** — clicking the card body opens/focuses a modeless History window and dismisses the card, like rvt-mcp. `MCPHISTORY` opens the same window. Search, Success/Failed and Read/Write filters, duration and a compact detail panel cover plugin-processed calls even when toasts are off. History keeps only the latest 1,000 outcomes in memory; no raw inputs/code bodies/JSON results or persistent history files are added.
- **Script failures count as Failed** — an inner `send_code` result with `ok:false` no longer increments Success merely because the transport envelope succeeded. The card, red failure accent and History agree.
- **Branding save feedback** — a failed settings save is shown under the branding switch while the chosen value remains active in the current session.
- **Server version identity** — the MCP initialize version is derived from the server assembly rather than a separate hard-coded version.

- **Toast card layout** — synced with rvt-mcp. The title names the gateway and AutoCAD year (`dwg-mcp 2024`), replacing the footer label and the `DWG-MCP -` prefix. The next line names the latest tool, and the Success · Failed · Capture counts spread evenly across the card, each number sized and aligned with its label.
- **Capture thumbnail** — the latest capture shows on the card in one fixed frame with the image centred on both axes, so the card keeps its size between captures of different shapes. It stays at least 5 seconds: a later result without an image clears it only after that, and back-to-back captures switch to the newest one every 5 seconds. The preview does not change while the pointer is on the card. Click the thumbnail to open the image (the card then closes); a click elsewhere on the card opens History and dismisses it.
- **Toast motion** — counters roll like an odometer: only the digits that change move, a new value continues the roll instead of restarting it, and a number that gains a digit widens gradually. The thumbnail frame opens with the card growing around it, cross-fades to the next capture, and fades out before the card closes up. All of it follows Windows' animation setting: with **Animation effects** off, everything changes at once.
- **Plugin version stamping** — all six AutoCAD plugin shells carry `<Version>`, so the add-in DLLs report the release version instead of `1.0.0` and server/add-in pairing checks can match them (rvt-mcp / ipt-mcp parity).
- **Installer wires Claude Code on Windows PowerShell 5.1** — client CLI calls (`claude`, `codex`, `grok`) no longer run under `$ErrorActionPreference = 'Stop'`, so a harmless stderr line such as `No MCP server named "dwg-mcp" in local scope` on a fresh machine no longer fails the wiring (it was reported as `claude: failed` while the install exited 0).
- **Settings file is never clobbered** — if `settings.json` has content that is not a JSON object, saving a toast setting now leaves the file untouched and returns failure instead of replacing it with a fresh object (rvt-mcp / ipt-mcp parity).
- **Show branding persists** — the Settings choice is saved as `showBranding` in `%LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json` and restored at the next AutoCAD launch (rvt-mcp parity). It was session-only before.
- **WPF toast regression harness** — `tests/Bimwright.Dwg.Toast.Tests` runs the production toast window and manager on a real WPF dispatcher without AutoCAD (`dotnet run --project tests/Bimwright.Dwg.Toast.Tests`; `-- --demo` opens an interactive preview). It covers the card layout, odometer rolling, thumbnail centring, open / cross-fade / close, hover branding and the manager lifecycle.

### Setup

- **Data folder moved to `%LOCALAPPDATA%\Bimwright\dwg-mcp\`** — all per-user state (config, discovery, logs, captures, ToolBaker data, fonts cache) and the server (`server\current\dwg-mcp.exe`) now live under the shared bimwright family root. The installer moves an existing `%LOCALAPPDATA%\Bimwright\Dwg\` once, byte-identical (AutoCAD and MCP clients using dwg-mcp must be closed), repoints known client entries to the new `server\current` path, restores the old folder if the install fails, and stops when both folders exist. Server and add-ins must come from the same release.

### Fixed

- **`dwg_set_system_variable` accepts values again** — the `value` argument was declared as a Newtonsoft `JToken`, which the MCP SDK's System.Text.Json binder cannot create, so every call failed before reaching AutoCAD (since 1.0.0). Its published schema also required an array. `value` now binds any JSON number or string, the schema no longer restricts it to arrays, and the plugin wire format is unchanged.

## 2.0.1 — 2026-09-28

### Setup

- Installer rewritten for rvt-mcp parity: detects installed AutoCAD 2022–2027 years (a year counts when its `acad.exe` exists), requires AutoCAD closed, stages with per-file rollback on error, sweeps duplicate bundles with the same ProductCode, and blocks on a machine-wide `%ProgramData%` copy.
- The server installs at the fixed path `%LOCALAPPDATA%\Bimwright\Dwg\server\current\dwg-mcp.exe`, is smoke-checked with `--help`, and the run seeds `%LOCALAPPDATA%\Bimwright\Dwg\dwgmcp.config.json` with `toolsets=["all"]` (an existing `toolsets` key is kept). `-PruneOldServers` removes legacy versioned copies.
- The installer wires every detected MCP client by default (`-Client <names>` limits it, `-Client none` skips it): minimal JSONC-safe text edits with `<config>.bak` backup, repointing of old versioned server paths, and legacy `bimwright-dwg*` entries reported, never replaced.
- `uninstall-all.ps1` removes the bundle, the legacy global tool, server copies, discovery files and the spill cache while keeping settings, config, logs and captures; `-Purge` removes those too (`-KeepLogs` keeps logs). Client configs are never touched.
- `package-client-setup.ps1` refuses a dirty working tree unless `-AllowDirty` (recorded as `dirty` in `manifest.json`), ships `uninstall.ps1`/`uninstall-all.ps1`/`README.md`, and records SHA-256 + bytes for every file.
- Server: `--help` prints usage and exits; `DwgMcpConfig` defaults to `%LOCALAPPDATA%\Bimwright\Dwg\dwgmcp.config.json` when no `--config` is passed.

### Visual reading

- `dwg_capture_view_image` now returns an inline MCP image alongside the existing JSON envelope, with hash verification and an 8 MiB inline limit. Clients must accept multiple content blocks. Image-delivery failures preserve successful capture metadata and set MCP `isError`.
- Add default-on `dwg_inspect_view_region` and `dwg_restore_view`: navigate by a normalized rectangle on a source image, capture fresh detail with parent linkage, and return to an earlier camera. No coordinate dump or known entity handle is required.
- Keep bounded capture history (64 entries, 30 minutes) and reject stale/unsupported sources before zoom. Initial mapping is limited to one unrotated, top-down, orthographic model-space viewport; native image aspect checks and camera readback guard the operation. Failed navigation attempts restore the previous camera when context permits.
- These view tools remain available under `--read-only`, change the visible camera and write image files. Installed-host image/mapping acceptance remains pending; see [visual reading contract](docs/design/2026-09-24-visual-reading-loop.md).

### Fixed

- Reject `#load` before any `send_code` execution and disable external source resolution; loaded scripts can no longer bypass the synchronous syntax check.
- Sanitize LISP inspection file-read errors using the shared error sanitizer.
- Avoid quadratic SHELL matching on blank lines; bound regex matching with a timeout and report timed-out scans as incomplete/dangerous.
- Reject `run_lisp` during batch preflight before any item executes; direct execution is also blocked (see breaking change below).
- Detect bare AutoCAD SHELL commands and literal command forms in LISP inspection; report `execution_authorized=false`, `safety_assured=false` and scanner limitations even for `clean` findings.
- Remove the LISP wrapper/queued-command/result-file executor, eliminating its untrusted-load, early-result and timeout cleanup paths. This closes those paths by disabling execution, not by claiming a working sandbox or repaired asynchronous executor.
- Keep LISP severity detection active after the 200-finding display cap; refuse sources over 2,000,000 characters, including oversized files read with a bounded buffer.
- Reject `async`/`await` before executing `send_code` snippets, preserving the synchronous document-lock contract. Callers must not offload AutoCAD API work to other threads.
- Reject AutoCAD/COM return values before JSON property traversal, including values nested in DTOs or collections; preserve JSON-safe DTOs and stdout.
- Sanitize nested C# errors and keep LISP refusal messages free of input content. Add behavioral regression tests using the actual handlers with explicit AutoCAD test doubles.

### Changed (breaking)

- **LISP execution is blocked.** `dwg_run_lisp` retains its name/parameters for compatibility but refuses every input locally with `lisp_execution_blocked`, without file access or plugin dispatch. The plugin independently refuses `run_lisp` before document locking, including calls from older servers. No scanner verdict, `MCPENABLECODE`, input override, batch or ToolBaker authorizes it. There is no isolated executor or approved-script trust mechanism in this build. Both server and plugin need updating/restarting; previously installed binaries are unchanged. C# `send_code` remains full-trust and must not be used to bypass LISP refusal.
- `dwg_send_code` is now always-on like `revit_send_code_to_revit`: it ships on the default surface via the `meta` toolset and no longer needs `--enable-send-code` / `BIMWRIGHT_DWG_ENABLE_SEND_CODE` / `MCPENABLECODE` opt-in. The `code` toolset name remains as an explicit opt-in alias; `--read-only` still strips it.
- `MCPDISABLECODE` is now a per-session kill-switch in AutoCAD (enabled by default when the listener starts); `MCPENABLECODE` re-enables.
- `send_code` now runs its Roslyn script inline on the document-lock thread instead of a dedicated worker thread — `Document.LockDocument()` is thread-affine, so worker-thread execution could not write to the database (`eLockViolation`). The 30s limit is now cooperative cancellation; there is no abort fallback.
- `send_code` responses now carry a `result` field with the script's return value (`return <expr>;` or a trailing expression), alongside the existing `stdout` capture.

### Added

- `dwg_run_lisp` (`run_lisp`) — compatibility refusal only. Registered via `meta`, denied to ToolBaker and stripped by `--read-only`. With the visual-reading additions, the default registered surface is 41 tools, including this non-executing endpoint.
- `dwg_inspect_lisp` — static safety scan of `.lsp` files or inline AutoLISP (server-side, no AutoCAD needed; lives in `query` so it survives `--read-only`). Flags process exec, dangerous COM progIds, persistence vectors (`acaddoc.lsp`, registry writes), destructive file ops, staged `(load …)`, and obfuscation; returns `verdict` clean/caution/dangerous + findings.
- `dwg_inspect_lisp` is analysis only: `clean` means no known pattern matched, never authorization or a malware-free certificate. Static inspection cannot establish arbitrary code safety.
- **Activity toast and Settings (plugin)** — one card replaces the stack of four. It counts succeeded, failed and captured commands for the current card. The latest result is the counter tooltip. Clicking the card opens an allowlisted capture from that result, then closes the card; the close button only closes it. The footer shows the AutoCAD year, and the left edge is a vertical gradient. Idle is 10/20/30/60 seconds (default 20). Hover restarts the full interval. A minimized or modal AutoCAD frame parks the card instead of replaying each result. `MCPTOAST` and Settings share one on/off switch and show a confirmation card. `MCPSETTINGS` opens a modeless window (General, Toast, Tools, About) through AutoCAD's modeless-window API. Show branding is session-only and off by default. The server sends `set_tool_catalog` once per AutoCAD session; the Tools tab lists that catalog. `enableToast` and `toastIdleSeconds` persist in `%LOCALAPPDATA%\Bimwright\Dwg\settings.json`. `BIMWRIGHT_ENABLE_TOAST` still wins at the next launch while set.

## 1.0.0 — 2026-08-28

First GitHub Release. Client setup ZIP: `DwgMcp.Setup-v1.0.0-win-x64.zip` (self-contained `dwg-mcp.exe`). **Plugin years in this ZIP** are those that compiled on the release machine (AutoCAD **2024** and **2027**). Source still supports 2022–2027; other years need a local AutoCAD SDK build.

### Added

- `dwg_capture_view_image` — render the current view to an image.
- Japanese and Simplified Chinese README mirrors.

### Changed

- Public tool counts aligned in docs; P&ID toolset removed from the public surface.
- README locale parity (badges, read-only capture wording, family cross-links).

### Fixed

- Named-pipe discovery: nullable `Port`, stop the fake pipe server on dispose, and sequential connect retries so Linux CI does not flake (PR #2).
- CI accepts Unix absolute paths and refreshes the tools snapshot.

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
- Plan 3 annotation, block, and dimension expansion tools: create text/mtext/leader/table; list blocks, insert block, get/set block attributes, and explode block; create linear/aligned/radial/diameter dimensions.
- Explicit rotation angle (degrees) and projected measurement distance validation along the rotation axis for linear dimensions.
- Split block toolset registration into read-only BlockTools and write-capable BlockWriteTools, allowing safe block inspection in read-only mode.
- Expanded manual smoke checklist for Plan 3 CAD tools (status: manual smoke pending).
- Plan 4 view navigation, guarded DXF export, and drawing variable/save/purge tools: zoom_extents, zoom_window, zoom_to_entity, export_dxf, get_variables, set_system_variable, save_drawing, and purge_drawing.
- Guarded file output policy enforcing absolute paths, matching extensions, overwrite checks, and repository root write protection.
- Deferred export_pdf, export_image, and capture_view from Plan 4 to prioritize stable DXF export and robust path security.
- Expanded manual smoke checklist for Plan 4 CAD tools (status: manual smoke pending).
- Manual smoke checklist covers Plan 2 core CAD operations, Plan 3 CAD operations (pending), Plan 4 drawing operations (pending), and the existing text translation workflow.

Notes:

- Default startup exposes 35 tools. Optional `code`, `toolbaker`, `annotation`, `block`, `dimension`, `export`, and `drawing` toolsets bring the backed MCP surface to 60 tools.
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
