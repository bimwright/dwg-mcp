# Changelog

## 2.2.0

- Move the gateway to .NET 10 LTS. Setup includes the gateway runtime. The framework-dependent NuGet tool requires .NET 10.
- Update the gateway SQLite provider and native engine used by ToolBaker and drawing history.

### Drawing edits and scripting

- Add opt-in `atomic=true` to `dwg_batch_execute`. Supported drawing commands run in one outer AutoCAD transaction. A failed step, including a reported per-entity error, rolls back earlier changes and skips later steps. The gateway checks `atomic_batch_version` before dispatch. The default remains `atomic=false`.
- Add `dwg_attach_image` (issue #6). Link a local PNG, JPG, TIFF or BMP with an exact insertion point and width, optional height, rotation, layer and send-to-back. The image, definition, layer and draw order are written in one transaction. An existing definition is reused when its saved or resolved image path matches. Mapped network drives are refused.
- Add `result_contract=task_v1` to `dwg_send_code`. Scripts return `McpTaskResult.Pass(value)` or `McpTaskResult.Fail(code, message, details)`. The result includes `_task.status` (`passed`, `failed` or `not_reported`). The gateway checks `script_task_contract_version` before dispatch. Calls without `task_v1` keep their legacy behavior.
- Add optional `usage_context=project|test|unknown` to `dwg_send_code`. The gateway records it only when call logging is enabled. Compiler diagnostics now include an `excerpt` of up to two lines on either side of the error, a column marker and a 200-character cap per line.
- Return `ACAD_BUSY` or `QUEUE_FULL` when host admission rejects a request before it starts.

### Reports, workflows and localization

- Add `dwg_report_task_result` and the `MCPREPORTS` window. Reports support clickable `dwg:` entity links for selection and zoom. The AutoCAD toast shows the host PID and can open the report. Successful write responses can include a `_report` reminder when reporting is enabled in the tool catalog.
- Expose the MCP prompts `dwg_translate_text` and `dwg_review_large_drawing`. Translation requires review before writeback. Large-drawing review uses WCS bounds and band clustering. Read-only sessions omit writeback and image capture.
- Check for stable releases in the background at most once per 24 hours and show an optional notice in AutoCAD. Settings can disable the notice. The notice links to release information.
- Add opt-in SQLite history for supported committed drawing edits. `dwg_record_change` records the goal and reason, `dwg_get_change_records` queries saved records, and `dwg_survey_change_impact` inspects supplied handles for context. Reports accept optional `call_ids` and `dwg_get_report_history` reads saved provenance even with AutoCAD closed.
- Add English, Vietnamese and Simplified Chinese catalogs for the plugin UI. English is the default. Language changes in Settings take effect after restarting AutoCAD.

### Large drawings and open documents

- `dwg_list_blocks` reports direct and nested references and unused definitions. `dwg_get_drawing_profile` can page unused block definitions and collect opt-in DBText/MText counts, a height histogram and bounded samples. Profile pages preserve coverage and continuation under the response budget.
- `dwg_list_layers(include_counts=true)` adds opt-in entity counts. Type filters normalize `AcDb` aliases, and zero-match count and select-by-type results can suggest types present in the requested space. Use `dwg_query_entities` for selection by WCS bounding box.
- Add `dwg_list_open_documents` and standalone `dwg_activate_document`. Verify the new drawing with `dwg_switch_target` before writing.
- Add read-only `dwg_cluster_bands` for X/Y band clustering with gap, WCS bounding-box, space and type filters, optional plain-text samples and cursor pagination. Entities without usable extents are counted separately.
- The catalog exposes 54 tools by default and 78 with `--toolsets all`. Read-only mode exposes 23 by default and 29 with all toolsets.

## 2.1.2 — 2026-11-01

- Add optional JSON `data` and `compile_only` to `dwg_send_code`, with structured compiler diagnostics and runtime source locations. Enhanced calls verify plugin support before dispatch and are refused inside batches/baked tools; code-only calls remain compatible.
- Keep script input values out of session History and call logs; History retains only input length and SHA-256.
- Fix issue #5 C# scripting on modern AutoCAD hosts that load an older Roslyn compiler: isolate the matching scripting/compiler set while sharing Autodesk types and retaining main-thread execution.
- Initialize leader MText before setting its location; use no-content leaders for omitted text and avoid text-only getters when describing non-text leaders.
- Require the scripting engine in plugin payloads and add a process-level compiler-conflict regression plus non-text leader readback coverage.
- Upgrade the gateway and AutoCAD plugins together to use the new script inputs. `modules` and `task_v1` result contracts are not part of this release.

## 2.1.1 — 2026-10-30

### Distribution and upgrading

- Ship Setup payloads for all six AutoCAD years, 2022–2027, with an exact file inventory and SHA-256 sidecar. Source, gateway and plugins are tied to the release commit.
- Publish the server-only `Bimwright.Dwg.Server` NuGet tool (requires .NET 8) alongside the Setup ZIP. AutoCAD plugins still come from Setup.
- Ship the Claude Desktop MCPB with the current 69-tool catalog and an exact SHA-256 pin to the Setup gateway. It does not install the gateway/plugins or fall back to another executable.
- Upgrade gateway and plugins together. Writes require verified PID/process and drawing binding; `--target` alone selects reads only. Existing settings and personal data are preserved by the installer.

### Rewrite height receipts

- Report `final_text_height` from the resulting `new_handle` entity after Unicode normalization and final scale, as read inside a successful rewrite transaction; unavailable values are null. Style-only receipts describe the anchor, and block-definition heights use local definition units.

### Explicit-source text reads

- Extend `dwg_get_selected_texts` with mutually exclusive handles/bbox sources and model/layout scope, without manual selection. Keep the original two-parameter selection/writeback path unchanged. Complete bounded sources are clustered before whole-cluster paging; cursors bind the text source, grouping and last delivered cluster ordinal.
- Mark source results read-only and not ready for writeback, using opaque cluster keys. Source reads leave existing manual-selection rewrite groups unchanged; per-entity translation can use `includeEntities=true` with `dwg_update_texts`, while cluster rewrite still requires a manual-selection read. Incomplete sources emit coverage without clusters or continuation; oversized sources must be narrowed.
- Bound final cluster pages to 16 KiB by default (4–64 KiB), preserve whole clusters, and refuse enhanced text reads in batch/baked dispatch before execution. `TEXT_FEATURE_UNSUPPORTED` rejects older-plugin selection data that did not honor the source. Tool counts remain unchanged.

### Spatial and paged entity reads

- Extend existing query/count tools with WCS 3D extents filtering and explicit model/layout scope. Opt-in query pages project allowlisted fields, retain mandatory handles and bind signed continuations to the document/revision, filters, fields and last delivered numeric handle. Incomplete matching coverage reports observed counts and disables continuation.
- Keep legacy query fields, geometry output, enumeration order and default/clamped limits; append new options without adding a tool. Legacy query/count/select calls without spatial or paging options now also accept the aliases `TEXT` -> `DBText`, `INSERT` -> `BlockReference`, and `LWPOLYLINE` -> `Polyline`; managed-name matching remains available. Extended query/count are direct calls, refused by gateway and plugin whole-batch preflight; select handle-list contracts remain unchanged.

- Refuse extended reads from older plugins with `QUERY_FEATURE_UNSUPPORTED` and discard legacy-shaped data that could ignore spatial/paging options. Original-parameter query/count calls remain available.
- Refuse the exact uppercase `POLYLINE` DXF token before query/count/select scans, including empty drawings. Use `Polyline`/`LWPOLYLINE` for lightweight polylines or explicit `Polyline2d`/`Polyline3d` managed types; existing managed-name spellings remain available.

### Drawing profile

- Add optional `space="all"` (unchanged default), `model` or one named layout. Model query/count/profile snapshots ignore viewport-only changes; all/layout profiles and layout queries retain those invalidations. Snapshots bind scope, model-only profiles avoid layout reads, and scoped replies from older plugins are refused with `PROFILE_SCOPE_UNSUPPORTED` instead of accepting ignored scope.
- Add read-only `dwg_get_drawing_profile` to default `query`: scan top-level entities in model space and every layout, count exact runtime types, layers and spaces, and keep observed/incomplete counts distinct from complete totals. Block references count once; block definitions and xref contents are not expanded.
- Start with `section="summary"`, then follow `output.next_calls` for `types`, `layers` or `spaces`. Every page rescans the drawing; continuation requires its `expected_snapshot` and refuses changes with `PROFILE_CHANGED`. `max_output_bytes` defaults to 16384 (4096–65536) and caps the final MCP tool result together with the global response budget. No cache or full-entity dump is introduced.

### Response budgets

- Measure final MCP tool results as serialized UTF-8, including escaping, metadata and image blocks. Configurable warning thresholds and a byte budget default to 80 MiB to preserve existing 8 MiB inline captures.
- Compact oversized output after a single invocation, preserving reported failure/partial/truncation facts and bounded effect evidence. Writes retain outcome-inspection guidance without replay; reads receive narrowing guidance. No output spill or automatic retry is performed.

### Tool argument diagnostics

- Check required top-level argument keys before SDK binding. Missing keys return `INVALID_ARGUMENT` with case-sensitive name suggestions and key-only diagnostics, without invoking the tool or AutoCAD. Rejection logs remain opt-in and exclude argument values.
- Warn about ignored extra keys with `_ignored_arguments` while running the tool once. Preserve SDK value coercion, tool errors and image content; schemas that explicitly allow additional properties keep accepting them without warnings.

### Instance and drawing targeting

- Publish per-instance `acad-<year>-<PID>.json` discovery with process start identity, retaining legacy year aliases. List and select by PID/target ID; refuse ambiguous automatic/year selection.
- Keep one binding for every host tool in a gateway, including health, catalog and send-code. Pin calls before asynchronous work, check binding generation immediately before transport writes, and never route to another instance after process loss or PID reuse. Concurrent or failed verification cannot replace a newer or previously valid binding.
- `dwg_switch_target` verifies authenticated host and drawing identity by default. Changing an existing instance binding requires an explicit `pid:`/`id:` selector; leaving a live confirmed target also requires `rebind=true`. Initial/`verify=false` bindings allow reads only; reselecting the same process without verification retains its previous expected drawing lifetime ID unless `rebind=true` resets it or an explicit `expected_document_id` replaces it. Confirming a different drawing requires a matching explicit `expected_document_id` or `rebind=true`. Validate the document lifetime on the AutoCAD UI thread under its lock before handlers, including batch/baked/send-code. Opening itself requires only a confirmed instance and never saves/closes drawings.
- Classify target switching as a routing write: exclude `dwg_switch_target` from read-only catalogs and refuse direct calls before changing the binding or contacting the host. Read-only startup selects `--target pid:<PID>`/`id:acad-<year>-<PID>`; target-loss recovery lists available targets and asks for a gateway restart with an explicit selector. No fallback instance is selected.
- Include process and drawing identities in routing recovery errors. Retry guidance uses the current verified process identity; lost or reused PID information is marked historical and requires review before reselection.
- Update gateway and plugin together for writes. Old plugins cannot satisfy target verification; new plugins refuse unguarded writes from old gateways. `--target` selects the initial instance and does not authorize writes. See the routing workflow in [README](README.md).

### send_code

- Mark AutoCAD 2025–2027 plugins as dynamically loaded so builds include Roslyn scripting and its runtime dependencies alongside the plugin DLL.
- Reject incomplete repo, Setup and installed plugin payloads, including missing runtime libraries declared by modern plugins' dependency manifests. Ship the shared validator with Setup and check dependency copying without AutoCAD in both PowerShell CI jobs.
- Report failed C# execution inside a batch as a failed item with `partial_failure=true`, preserving stdout and detailed compile/runtime errors while continuing subsequent commands.

### Drawing session tools

- Add `dwg_list_recent_drawings` and `dwg_open_drawing` on default `meta`, usable without an active drawing. Recent reads the connected product's HKCU MRU list using the Autodesk-reported registry root, returns up to 50 DWGs with existence/pinned metadata, and supports a case-insensitive filename filter before the limit. Ask the user to choose before opening.
- Open uses `DocumentCollectionExtension.Open` on the main thread in application context without a document lock. Requires an absolute drive/UNC DWG path and idle multi-document mode; optionally requests file read-only access and preserves the previous active drawing. Reuses already-open drawings, refuses access-mode mismatches, and never calls Save/Close or overwrites files. Native dialogs remain user-handled; response wait defaults to 600 seconds (1–900), and a timeout does not cancel running host work.
- Keep recent available in gateway read-only mode; exclude/refuse open even when file `read_only=true`. Refuse session commands during batch preflight before earlier drawing mutations; document/baked contexts cannot execute them. No-document errors now guide agents to recent → choose → open.
- Release counts are 45 default / 69 all-enabled / 17 default read-only / 23 all-enabled read-only. Existing production-handler tests with explicit Autodesk doubles, MRU/path/timeout regressions and stdio discovery/permission checks are retained. See [drawing session tools](README.md#drawing-session-tools).

### Bounded logs and transport lifecycle

- Call-log segments rotate before the next UTF-8 record would exceed 5 MiB; each generated group keeps at most 100 MiB for 30 days, and startup applies retention even when recording is off. Diagnostics use a bounded background writer (10 MiB active file plus one backup) with repeated messages summarized. Maintenance only touches known generated logs.
- Local transport limits: requests up to 16 MiB, responses up to 32 MiB, eight concurrent gateway calls, eight TCP clients and 16 queued AutoCAD API requests. Idle/incomplete input and blocked writes have 30-second deadlines, separate from host execution and the `dwg_open_drawing` wait.
- Stop closes the endpoint and connected clients and skips queued work that has not started; the pipe endpoint is reserved before readiness is published, and a failed Start rolls back. A running operation can still complete after a timeout or disconnect, so inspect the outcome before retrying a write. See [README](README.md).

### CI

- Fix installer matrix shell selection by resolving it through job defaults; Windows PowerShell and PowerShell 7 remain separate jobs.
- Run the server tests on Windows, like the rest of the family. The gateway only runs beside AutoCAD; on the Linux runner, repeated `Process.StartTime` reads of one process differed by microseconds, breaking the exact process-identity checks, and file-lock, junction and pipe tests need Windows. `WindowsFact` tests, previously skipped, now run in CI.
- Make the plugin dependency test independent of earlier builds (it runs `PrepareForBuild` with an isolated intermediate directory) so it passes on a fresh checkout, and run `upgrade.Tests.ps1` in the installer matrix. Installer test scripts exit 0 explicitly when every case passes: GitHub's PowerShell steps otherwise return the exit code of the last native command, which some cases intentionally fail.
- Run main-thread executor tests on dedicated workers so ThreadPool contention in parallel CI does not consume their five-second host-pump deadline.
- Start toast cleanup checks with real animation clocks attached synchronously, avoiding a race to observe a short animation through hover timers and render frames; keep the separate motion checks and post-close cleanup assertions.
- Create the output-artifact junction fixture with `cmd /c mklink`, as the log tests do, instead of starting PowerShell within its five-second setup deadline.
- Isolate the output-artifact storage test collection from parallel suites so its real background-maintenance and file-mutex timing checks retain their original deadlines without competing for ThreadPool workers.
- Add a public-tree CI check with positive/negative path fixtures. Move internal release checklists and WPF harness notes out of the public product tree.

### Setup integrity

- Write a SHA-256 sidecar next to each Setup ZIP and align the Autodesk bundle template version with the gateway. Product documentation points to each package's manifest for included AutoCAD years and its release notes for host validation coverage.

### Plugin builds

- All six AutoCAD shells (2022–2027) compile against Autodesk's `AutoCAD.NET` reference packages from nuget.org (24.1.51000, 24.2.0, 24.3.0, 25.0.1, 25.1.0, 26.0.0) instead of a local AutoCAD install, so the Setup ZIP and CI build every year. The packages are compile-time only; no Autodesk DLL is copied into the plugin output or the ZIP. A build does not verify the plugin in a running AutoCAD of that year.
- `package-client-setup.ps1` fails when a plugin year does not build instead of skipping it with a warning, and the CI plugin matrix no longer skips years without a local AutoCAD.

### Documentation

- Document exact read-only permission rules in all four README languages and check them against tool annotations. Correct capture/navigation tools that write image files being described as available in read-only mode.
- Add the security policy and agent install-guide markers. Expand send-code guidance with active-document requirements, transaction/undo limits, default namespaces and the current output truncation limitation.

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

- **Toast and History no longer drop calls with structured results** — a `send_code` script returning an object or array, or any tool result whose display field had an unexpected JSON type, threw `Cannot cast Newtonsoft.Json.Linq.JObject to Newtonsoft.Json.Linq.JToken` in `McpToastNotifier.OnCompleted`, so that call was missing from the toast and History. Scalar script results are still shown; other values use the generic completion text.
- **`-Years` and `-Client` lists work under `powershell -File`** — `install.ps1 -Years 2024,2025` arrived as one string and failed with "20242025 … greater than the maximum allowed range of 2027"; `-Client cursor,claude` failed the client name check. Comma or space lists are now split and validated the same way for `-File` and in-session calls.
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
