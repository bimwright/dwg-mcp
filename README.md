<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-autocad-versions"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#tools"><img src="https://img.shields.io/badge/MCP-44%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  English · <a href="README.vi.md">Tiếng Việt</a> · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a>
</p>

---

## Drawing Translation Should Not Stop At Manual Copy-Paste

Construction and engineering drawings carry dense technical text — specifications, notes, dimensions, material callouts, legends. When those drawings arrive in a foreign language, translation is not optional. It is required before the project team can act.

The usual workflow is painful: select text one entity at a time, copy to a translator, paste back, fix the font (because SHX fonts cannot render Vietnamese or CJK), adjust the height, hope nothing shifted. Multiply by hundreds of text fragments per sheet, dozens of sheets per project.

`dwg-mcp` exists to compress that loop into two steps: select the text, let the AI agent read, translate, and rewrite it in place — with correct font, correct height, correct spatial grouping, and a single undo.

---

## What dwg-mcp Is

`dwg-mcp` is a local MCP gateway for Autodesk AutoCAD 2022-2027 DWG workflows.

It has two parts:

- **Bimwright.Dwg.Server**: a .NET 8 MCP server launched by Claude Code, Cursor, OpenCode, or another stdio MCP client.
- **Bimwright.Dwg.Plugin**: version-specific AutoCAD add-in shells loaded inside AutoCAD, executing commands against the drawing database.

The agent talks MCP. The server talks to the plugin over a local wire: TCP NDJSON for AutoCAD 2022–2024, and a Named Pipe (loopback, avoids the firewall prompt) for 2025–2027. The plugin talks to the AutoCAD .NET API.

Everything stays on your machine.

---

## Why It Matters

AI agents make it possible to describe "translate all selected text to Vietnamese" and have it happen — correctly — in the drawing. But intent alone is not enough. AutoCAD text operations require understanding spatial layout, fragment grouping, font limitations, MText vs DBText, block references, and height scaling.

`dwg-mcp` handles that complexity:

- **Spatial clustering** groups fragmented text into logical sentences (by block, row, column, paragraph).
- **Automatic font handling** creates a Unicode-capable text style and applies it — no more SHX question marks.
- **Height scaling** compensates for the different visual density of Latin vs CJK text.
- **MText conversion** upgrades single-line fragments into multi-line text when safe.
- **Single undo** wraps each operation in a transaction.

---

## Usage Evidence

220 completed tool calls over 19 days of active use on production construction drawings. 98.2% success rate.

| Tool | Calls |
|------|-------|
| get_selected_texts | ~100 |
| translate_and_rewrite | ~77 |
| send_code | ~28 |
| collapse_and_rewrite | ~11 |
| update_texts | ~10 |
| apply_unicode_style | ~4 |

---

## Architecture

```text
+---------------------------+
| AI Client                 |
| Claude / Cursor / OpenCode|
+---------------------------+
              |
              | stdio MCP
              v
+---------------------------+
| Bimwright.Dwg.Server      |
| .NET 8 / C#               |
+---------------------------+
              |
               | TCP NDJSON (2022-2024) / Named Pipe (2025-2027)
               | token auth
              v
+---------------------------+
| Bimwright.Dwg.Plugin      |
| AutoCAD 2022-2027 shells |
+---------------------------+
              |
              | LockDocument()
              v
+---------------------------+
| AutoCAD .NET API          |
| ObjectARX 2022-2027       |
+---------------------------+
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for threading, discovery, and auth details.

---

## Install

Download the client setup ZIP from [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest). It includes a self-contained MCP server and the AutoCAD plugin years that were compiled for that release (see `manifest.json` inside the ZIP). This machine’s v1.0.0 ZIP ships **2024** and **2027**. Other years: build the plugin from source with that year’s AutoCAD SDK.

```powershell
$tag = (Invoke-RestMethod https://api.github.com/repos/bimwright/dwg-mcp/releases/latest).tag_name
$zip = "$env:TEMP\DwgMcp.Setup-$tag-win-x64.zip"
$dir = "$env:TEMP\DwgMcp.Setup-$tag-win-x64"
Invoke-WebRequest "https://github.com/bimwright/dwg-mcp/releases/download/$tag/DwgMcp.Setup-$tag-win-x64.zip" -OutFile $zip
Expand-Archive $zip -DestinationPath $dir -Force

powershell -ExecutionPolicy Bypass -File "$dir\install.ps1" -WhatIf
powershell -ExecutionPolicy Bypass -File "$dir\install.ps1"
```

The installer:

- detects installed AutoCAD years (a year counts when its `acad.exe` exists) and installs every packed year that matches; `-Years 2024,2027` forces an explicit list;
- requires AutoCAD to be closed, and rolls every change back on error;
- deploys `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle\` (removing stale bundles with the same ProductCode) and the server at the fixed path `%LOCALAPPDATA%\Bimwright\dwg-mcp\server\current\dwg-mcp.exe`;
- seeds `%LOCALAPPDATA%\Bimwright\dwg-mcp\dwgmcp.config.json` with `"toolsets": ["all"]` on a fresh install — an existing `toolsets` key is kept;
- wires every detected MCP client by default (`-Client <names>` limits it, `-Client none` skips it); config edits keep JSONC comments and get a `<config>.bak` backup. The entry name is `dwg-mcp`.

Restart AutoCAD, then restart wired MCP clients to load `dwg-mcp`.

**Updating:** close AutoCAD and run the new ZIP's `install.ps1` the same way — no need to uninstall first. The server path stays `server\current\`, so clients only need a restart.

**Uninstall:** `uninstall.ps1` removes the bundle, the server copies, discovery files and caches under `%LOCALAPPDATA%\Bimwright\dwg-mcp\` (settings, config, logs and captures are kept; `-Purge` removes those too). Client config entries are never touched — remove the `dwg-mcp` entry yourself, or run `install.ps1 -Uninstall -Client <names>` first.

Do **not** `dotnet tool install -g Bimwright.Dwg.Server` — that package is not the supported client install.

**Developer (local SDK):** `dotnet build` the year you have, then `pwsh scripts/install.ps1 -Years 2024` from the repo (copies that year’s `bin` output). `NETLOAD` remains available for Debug DLLs.

### 3. Wire up your MCP client

The installer already did this for detected clients (Step `Client :` lines in its summary). To wire by hand, add to your MCP client config (e.g., `.mcp.json`):

```json
{
  "mcpServers": {
    "dwg-mcp": {
      "command": "C:\\Users\\<user>\\AppData\\Local\\Bimwright\\dwg-mcp\\server\\current\\dwg-mcp.exe",
      "args": []
    }
  }
}
```

Select an initial AutoCAD instance by PID (or an unambiguous 4-digit year):

```json
{
  "mcpServers": {
    "dwg-mcp": {
      "command": "C:\\Users\\<user>\\AppData\\Local\\Bimwright\\dwg-mcp\\server\\current\\dwg-mcp.exe",
      "args": ["--target", "pid:12345"]
    }
  }
}
```

Before writes, call `dwg_list_available_targets` and `dwg_switch_target(target="pid:<PID>")`. The default `verify=true` authenticates the selected process and confirms the active drawing; `verify=false` permits reads only. `--target` controls initial selection and does not authorize writes. A year or automatic selection matching multiple instances returns `AMBIGUOUS_TARGET`.

Once bound, changing instances requires `pid:<PID>` or `id:acad-<year>-<PID>`; leaving a live confirmed target also requires `rebind=true`. Reselecting the same process with `verify=false` removes write confirmation. It retains the previous expected drawing lifetime ID unless `rebind=true` resets it or an explicit `expected_document_id` replaces it. After opening or switching drawings, inspect `dwg_health().target`; confirming a different drawing requires its matching `expected_document_id` or `rebind=true`. Failed verification preserves the previous binding, and pending calls under an older binding are refused before sending. Update gateway and plugin together: legacy plugins cannot be verified for writes, and new plugins refuse unguarded writes from old gateways.

With `--read-only`, `dwg_switch_target` is unavailable and direct calls are refused. Start the gateway with `--read-only --target pid:<PID>` (or `--target id:acad-<year>-<PID>`), then check health and the current target. On `TARGET_LOST` or `TARGET_CHANGED`, call `dwg_list_available_targets` and restart the gateway with the intended explicit `--target`. A lost binding never selects another instance automatically.

Use `--read-only` to strip write-capable toolsets (query/view/meta routing remain as configured). Use `--toolsets all` or an explicit list that **includes** the defaults you need (a custom list **replaces** the default set — e.g. `query,modify,meta,view,annotation`). Env: `BIMWRIGHT_DWG_TOOLSETS=…`.

`dwg_send_code` is available by default via `meta`. Use `--enable-send-code` / `--disable-send-code` (JSON `enableSendCode`; env `BIMWRIGHT_DWG_ENABLE_SEND_CODE`) to control server exposure. Disabled code is also refused inside batches before any command is sent. `MCPDISABLECODE` disables C# execution for the AutoCAD session; `MCPENABLECODE` only re-enables the host gate and cannot override the server setting. `dwg_run_lisp` remains registered for compatibility but refuses every request at both server and plugin. This build has no isolated LISP executor or approved-script trust mechanism. `--read-only` removes both tools.

Call logging is **off by default**. Use `--enable-call-log` / `--disable-call-log` (JSON `enableCallLog`; env `BIMWRIGHT_DWG_ENABLE_CALL_LOG`) to opt in/out. Precedence is JSON → environment → CLI; the last occurrence of each enable/disable pair wins. When off, the server creates/appends no call log or call journal. When on, `%LOCALAPPDATA%\Bimwright\dwg-mcp\dwg-mcp-calls.jsonl` records redacted parameters and timing/outcome metadata, never results. Code fields, including nested batch code, contain only character length and SHA-256 of the original UTF-8 body; code-related error text is omitted because it may echo source. No code-body cache or body journal is implemented. ToolBaker adaptive usage, diagnostic logs and session-only History are separate from this switch; the plugin has no persistent call-log writer.

New call-log segments rotate before the next UTF-8 record would exceed 5 MiB; each generated group retains at most 100 MiB for 30 days. Startup applies retention even when recording is off. Recent oversized legacy segments are preserved intact within that group budget. Diagnostics use a background writer with a 10 MiB active file and one backup, with repeated messages summarized. Maintenance only handles known generated logs in the current and legacy DWG data roots; settings, drawings, captures and ToolBaker state are preserved.

Local transport accepts requests up to 16 MiB and responses up to 32 MiB. Each gateway admits up to eight concurrent calls; the TCP listener admits up to eight clients and the AutoCAD API queue up to 16 requests. Idle/incomplete input and blocked writes have 30-second deadlines, separate from host execution and the longer `dwg_open_drawing` response wait. Stop closes the endpoint and old connections and skips queued work that has not started. A running operation can still complete after a timeout or disconnect; inspect its outcome before retrying a write.

Activity toasts are on by default. Finished tool calls update one card in the AutoCAD window corner. The title names the gateway and the AutoCAD year (`dwg-mcp 2024`), the next line names the latest tool, and the Success · Failed · Capture counts spread evenly across the card; the latest result is the counter tooltip. The card's left edge is a vertical gradient. An allowlisted capture shows as a thumbnail in one fixed frame, centred on both axes, and stays at least 5 seconds: a later result without an image clears it only after that, back-to-back captures switch to the newest one every 5 seconds, and the preview does not change while the pointer is on the card. Click the thumbnail to open the image; the card then closes. A click elsewhere on the card opens/focuses History and dismisses it. The close button only closes the card and stays transparent on hover. Counters roll like an odometer, and the thumbnail opens, cross-fades to the next capture and fades out; all motion follows Windows' **Animation effects** setting, and with it off everything changes at once. The card hides after an idle interval (10, 20, 30 or 60 seconds, default 20) and pauses while the pointer is over it. While AutoCAD is minimized or a modal dialog is open, counts are kept and the card is shown once when the frame is usable again. Toggle with `MCPTOAST` or open `MCPSETTINGS`. `enableToast`, `toastIdleSeconds` and `showBranding` are stored in `%LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json`. `BIMWRIGHT_ENABLE_TOAST=0` re-applies at every launch while set. Show branding is off until turned on in Settings and is remembered across AutoCAD restarts. Toasts never steal keyboard focus.

`dwg_send_code` accepts inline synchronous snippets only: `async`/`await` and `#load` directives are rejected before execution. Keep AutoCAD API calls on the calling thread; do not offload them to `Task.Run` or other threads. Return JSON-safe DTOs; AutoCAD/COM objects are rejected even when nested. LISP inspection is available through `dwg_inspect_lisp`: sources over 2,000,000 characters are reported uninspectable/dangerous, and the 200-finding display cap does not stop severity detection. Regex matching has a timeout; timed-out scans are reported as incomplete/dangerous, never clean.

---

`MCPHISTORY` opens the same modeless History window. It keeps the latest 1,000 compact outcomes of plugin-processed calls in memory, including when toasts are off, with search, Success/Failed and Read/Write filters, duration and selection details. Closing a card does not clear History; **New session** clears its list. History adds no persistent call-log writer and retains no raw inputs, code bodies or JSON results. Server-only tool notifications are not yet included.

## Health runtime and update checks (Unreleased)

`dwg_health()` is offline and read-only. At session start, select the intended target and call `dwg_health(check_updates=true)` once; later routine probes remain offline. It reports host/document state without requiring or locking a drawing, and loaded server/plugin versions, build IDs and alignment. No target preserves gateway identity with `ok:false`, `host_connected:false`. A responding legacy plugin can return `HEALTH_UNSUPPORTED`: connection responsiveness is not verified health or loaded-plugin identity. A busy UI retains plugin identity but marks document state unavailable. Health is excluded from operation toasts and cannot be nested in drawing batches.

`setup_gateway`/`setup_plugin` describe loaded assemblies and their MVID; SHA-256 observes the disk file at identity capture, not loaded memory or publisher trust. The optional GitHub check compares both components against one stable release/tag revision, with a shared five-second budget and bounded metadata. `checked` confirms lookup, not artifact verification. Missing evidence stays `unknown`; equal versions or `update_available=false` do not establish a release build match.

Inspect both `update.components` and the full `update.notification` message, `lookup_reason`, `protected_component_reasons` and `unobserved_components`. `UPDATE_CHECK_UNAVAILABLE` reports external lookup failure, not a runtime fault or installation authorization. `LOCAL_VERSION_AHEAD` is informational: preserve newer components; never downgrade. Only `update_guidance.mode=prepare_update` with `bundle_update_proposal_allowed=true` permits an update proposal, and installation still needs user authorization. Other modes only check/report/review. Health never installs, downloads packages, changes client configs, saves/closes drawings or restarts processes. See [agent guidance](AGENTS.md).

After reconnect, compare fresh `initialize.serverInfo` and the health tool's `_meta["bimwright/dwg-mcp/runtime"]` in `tools/list` with `client_verification` on the same connection, including schema/annotations. The server cannot prove a client cache refresh. Health remains available under `--read-only`; its `openWorldHint` is true only because the optional check reaches GitHub. This feature requires the new gateway and plugin; published v2.0.1 does not include it. Host-free protocol tests and AutoCAD 2024/2027 builds are verified; installed-host acceptance remains pending.

## Tools

Development startup exposes 44 tools: query (including `dwg_inspect_lisp`), modify, meta (including `dwg_send_code`/`dwg_run_lisp`), view, and default-on `dwg_capture_view_image`. Optional ToolBaker, annotation, block, dimension, export, and drawing toolsets, enabled through `--toolsets`, bring the backed MCP surface to 68 tools. The registered counts include `dwg_run_lisp`, which only returns a refusal; no scan verdict authorizes execution.

With no drawing open, use `dwg_list_recent_drawings` → user chooses → `dwg_open_drawing(path)`. Both are standalone `meta` tools; recent is available under `--read-only`, open is not (even with file `read_only=true`). Native dialogs may require the user; timeouts do not cancel an Open already running. See [drawing session tools](#drawing-session-tools) for parameters, API references and verification limits.

General CAD tools operate on the current active document in the selected AutoCAD target. Entity inputs and returned entity IDs use AutoCAD hex handles, such as `7F5AD`, returned by selection, creation, or property tools. Creation, copy, offset, and modify responses identify generated or modified entities by hex handle.

Plan 2 query expansion is model-space only: `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, and `dwg_select_by_type` scan model space, not paper-space/layout entities. `dwg_select_by_layer` and `dwg_select_by_type` return handle lists for the caller; they do not change AutoCAD's pickfirst selection.

| Tool | Purpose |
|------|---------|
| `dwg_health` | Read-only host/runtime identity probe; optional stable-release check, offline by default |
| `dwg_get_drawing_info` | Read current drawing name, current layer, current space/layout, and unit scalars |
| `dwg_get_entity_properties` | Read properties for entities identified by AutoCAD hex handles |
| `dwg_list_layers` | List layers in the current drawing with color and state flags |
| `dwg_query_entities` | Query model-space entities by optional type, layer, color, limit, and geometry flags |
| `dwg_count_entities` | Count model-space entities by optional type, layer, or color filters |
| `dwg_select_by_layer` | Return model-space entity handle lists for one layer without changing pickfirst selection |
| `dwg_select_by_type` | Return model-space entity handle lists for one entity type without changing pickfirst selection |
| `dwg_get_selected_texts` | Read pickfirst selection, spatially cluster text entities, return grouped text with rewrite mode hints |
| `dwg_update_texts` | Write new text by handle in one transaction |
| `dwg_create_layer` | Ensure a layer exists without overwriting an existing layer's properties |
| `dwg_create_line` | Create one line in the current drawing space |
| `dwg_create_circle` | Create one circle in the current drawing space |
| `dwg_create_point` | Create one point and return its hex handle |
| `dwg_create_polyline` | Create a lightweight polyline from vertices and return its hex handle |
| `dwg_create_rectangle` | Create a rectangle polyline and return its hex handle |
| `dwg_create_arc` | Create one arc and return its hex handle |
| `dwg_create_ellipse` | Create one ellipse and return its hex handle |
| `dwg_change_layer` | Move entities identified by hex handles to another layer |
| `dwg_change_color` | Change entity color by AutoCAD color index |
| `dwg_move_entities` | Move entities identified by hex handles by a displacement vector |
| `dwg_rotate_entities` | Rotate entities identified by hex handles around a base point |
| `dwg_scale_entities` | Scale entities identified by hex handles around a base point |
| `dwg_copy_entities` | Copy entities identified by hex handles and return copied handles |
| `dwg_erase_entities` | Erase entities identified by hex handles |
| `dwg_offset_entities` | Offset curve entities and return generated handles |
| `dwg_translate_and_rewrite` | **Preferred.** Write translated text back: anchor, delete, MText, font, height |
| `dwg_apply_unicode_style` | Ensure `Bimwright_Unicode` style exists and apply to targets |
| `dwg_collapse_and_rewrite` | Low-level rewrite primitive with explicit geometric control |
| `dwg_list_available_targets` | List AutoCAD instances with PID, target ID and process start |
| `dwg_get_current_target` | Show binding state, generation, PID and confirmed drawing |
| `dwg_switch_target` | Bind and verify an instance; changing instances requires `pid:`/`id:`, plus `rebind=true` when leaving a live confirmed target. Excluded from read-only mode |
| `dwg_list_recent_drawings` | List up to 50 recent DWGs for the connected AutoCAD, with optional filename filter; works with no drawing open |
| `dwg_open_drawing` | Open an absolute DWG path, optionally read-only; reuse an open drawing, never save or close drawings; default wait 600s |
| `dwg_batch_execute` | Run multiple internal wire commands as a logical batch |
| `dwg_send_code` | Execute a C# snippet against the AutoCAD .NET API (globals `doc`/`db`/`ed`, `return` value + captured stdout, cooperative 30s cancellation; `MCPDISABLECODE`/`MCPENABLECODE` toggle per session) |
| `dwg_run_lisp` | Compatibility endpoint only: all `file`/`code`/`command` inputs are refused with `lisp_execution_blocked`. No file read, plugin dispatch or execution from this tool. Use `dwg_inspect_lisp` for analysis only. |
| `dwg_inspect_lisp` | Static safety scan of a `.lsp` file or inline AutoLISP — flags process exec (`startapp`/`shell`), dangerous COM (`WScript.Shell`/`XMLHTTP`), persistence (`acaddoc.lsp`, registry writes), file deletion, staged `(load …)`, obfuscation (`eval`/`read`). Returns `verdict` clean/caution/dangerous + findings. Pure local analysis; no execution, no AutoCAD needed; available even under `--read-only`. `clean` does not certify safety. Reports include `execution_authorized=false` and `safety_assured=false`. |
| `dwg_zoom_extents` | Zoom to the extents of the drawing viewport |
| `dwg_zoom_window` | Zoom viewport to a window defined by two corner points |
| `dwg_zoom_to_entity` | Zoom viewport to the extents of a specific drawing entity identified by handle |
| `dwg_capture_view_image` | Capture drawing graphics inside AutoCAD; return inline image, path, actual size, hash, document/viewport/camera metadata (default-on; path policy applies) |
| `dwg_inspect_view_region` | Choose a normalized rectangle on a capture; zoom, regenerate and return a fresh inline image with parent linkage |
| `dwg_restore_view` | Return to an earlier capture's camera and obtain a fresh inline image |

Visual reading: capture → inspect the image → choose a region → `dwg_inspect_view_region` → inspect again. Keep ancestor IDs and use `dwg_restore_view` to return to the overview. Source history lasts 30 minutes / 64 captures in one AutoCAD process. Region navigation initially supports one top-down, unrotated, orthographic model-space viewport; unsupported or stale sources are refused. Capture responses now include an MCP image block (8 MiB limit) alongside the existing JSON text. These tools change the visible camera and save images, and are unavailable under `--read-only`; they do not edit geometry or save DWG. Installed-host acceptance is pending. See the workflow described above.

Optional ToolBaker tools are exposed when the `toolbaker` toolset is enabled:

| Tool | Purpose |
|------|---------|
| `dwg_list_baked_tools` | List accepted baked tools from the server-owned SQLite registry |
| `dwg_run_baked_tool` | Run an accepted baked tool by name |
| `dwg_list_bake_suggestions` | List detected repeated-workflow suggestions |
| `dwg_accept_bake_suggestion` | Validate, smoke-test, and accept a suggestion |
| `dwg_dismiss_bake_suggestion` | Dismiss or suppress a suggestion |
| `dwg_create_bake_issue_draft` | Generate a GitHub issue draft for a suggestion without submitting it |

Optional Annotation tools are exposed when the `annotation` toolset is enabled:

| Tool | Purpose |
|------|---------|
| `dwg_create_text` | Create single-line text (DBText) with target height, rotation, and properties |
| `dwg_create_mtext` | Create multi-line text (MText) with formatting and width |
| `dwg_create_leader` | Create a multileader (MLeader) with optional leader text |
| `dwg_create_table` | Create an AutoCAD table with specified row/column text contents |

Optional Block tools are exposed when the `block` toolset is enabled:

| Tool | Purpose |
|------|---------|
| `dwg_list_blocks` | List block definitions in the current drawing (read-only safe) |
| `dwg_get_block_attributes` | Read attributes of a block reference by handle (read-only safe) |
| `dwg_insert_block` | Insert a block reference, optionally importing from an external DWG |
| `dwg_set_block_attributes` | Set attributes of a block reference by handle |
| `dwg_explode_block` | Explode a block reference and return the handles of generated parts |

Optional Dimension tools are exposed when the `dimension` toolset is enabled:

| Tool | Purpose |
|------|---------|
| `dwg_create_linear_dimension` | Create a rotated linear dimension with rotation degrees |
| `dwg_create_aligned_dimension` | Create an aligned dimension between two points |
| `dwg_create_radial_dimension` | Create a radial dimension for a circle or arc |
| `dwg_create_diameter_dimension` | Create a diametric dimension for a circle or arc |

Optional Export tools are exposed when the `export` toolset is enabled:

| Tool | Purpose |
|------|---------|
| `dwg_export_dxf` | Export the drawing to a DXF file (guarded by output path policy) |

Optional Drawing tools are exposed when the `drawing` toolset is enabled:

| Tool | Purpose |
|------|---------|
| `dwg_get_variables` | Read current values of drawing system variables |
| `dwg_set_system_variable` | Set the value of a drawing system variable |
| `dwg_save_drawing` | Save the current drawing to a file (requires confirm=true) |
| `dwg_purge_drawing` | Purge unused named objects (blocks, layers, styles) (supports dry_run=true, actual purge requires confirm=true) |

### Drawing session tools

The default `meta` toolset supports recent → user chooses → open, even on AutoCAD's Start page with no active drawing. AutoCAD and the updated add-in must already be running; these tools do not launch AutoCAD.

`dwg_list_recent_drawings`:

```json
{"name":"Plant","max_items":20}
```

Both arguments are optional: `name` is a case-insensitive **filename** substring, and `max_items` is 1–50 (default 50). Filtering happens before the limit. Only absolute `.dwg` entries are returned, numerically ordered by MRU `FileN` index, with duplicate paths removed. Each entry contains `index`, `path`, `name`, `exists`, and `pinned`. Missing files remain visible with `exists=false`.

The plugin reads `Recent File List` under the connected product's HKCU root reported by `HostApplicationServices.Current.UserRegistryProductRootKey`; it never guesses a release code or uses another installed year's list. The result includes `acad_year`, `source`, `registry_found`, `count`, and an instruction to ask the user which path to open. A missing registry key returns an empty list with `registry_found=false`; unreadable registry data returns an error. This registry layout was observed locally, but is **not a documented managed MRU API contract**. Registry contents may lag the UI; this is not a filesystem search.

`dwg_open_drawing`:

```json
{
  "path":"D:\\Models\\Plant.dwg",
  "activate":true,
  "read_only":false,
  "timeout_seconds":600
}
```

- `path`: required absolute drive or ordinary UNC path on the **AutoCAD machine**, not the MCP client. Relative/device paths, URLs, DXF and templates are refused. Existence/access are checked by the host process.
- `activate`: default `true`, makes it active. `false` requires an existing active drawing and restores it after opening; this is a UI document, not a viewless background database.
- `read_only`: default `false`, requests writable file access; `true` requests read-only access. An already-open drawing is reused, but an access-mode mismatch is refused without changing or closing it.
- `timeout_seconds`: integer 1–900, default 600. Changes only this command's gateway response wait; connections and other commands retain their 30-second budgets. Set the client's tool deadline above this budget plus transport grace.

Returns `opened`, `was_already_open`, `title`, `path`, actual `read_only`, `activated`, and `active_document`. The tool does not call Save, SaveAs, Close, audit or recovery, and does not overwrite drawing bytes. Existing drawings, including unsaved changes, remain open. Opening an older DWG does not persist converted bytes unless subsequently saved.

Open runs on AutoCAD's main thread in application context without a document lock. It requires multi-document mode (`SDI=0`) and refuses while a drawing command is in progress; it does not change SDI or cancel user commands. Native dialogs (locked files, references, format/password problems) remain user-handled. A timeout **does not cancel** an Open already running: inspect AutoCAD/native dialogs before retrying, rather than assuming the file remained closed.

Both tools must be standalone calls, not drawing batches or baked drawing workflows. A batch containing either is rejected before earlier items execute. Ordinary CAD tools still require an active document and now guide agents to this workflow when none is open. With an explicitly supplied path and instruction to open it, discovery can be skipped. Gateway `--read-only` permits recent but excludes/refuses open, even with file `read_only=true`: file access mode is not permission to change the document session.

Autodesk API references: [Open(DocumentCollection, string, bool)](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_ApplicationServices_DocumentCollectionExtension_Open_this_DocumentCollection_string__MarshalAsUnmanagedType_U1__bool.html), [MdiActiveDocument](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_ApplicationServices_DocumentCollection_MdiActiveDocument.html), [IsApplicationContext](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_ApplicationServices_DocumentCollection_IsApplicationContext.html), [UserRegistryProductRootKey](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_HostApplicationServices_UserRegistryProductRootKey.html), [Document.IsReadOnly](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_ApplicationServices_Document_IsReadOnly.html).

Production-handler tests use explicit Autodesk doubles; stdio discovery/permission and TCP/pipe deadline tests are host-free. Compilation against installed Autodesk assemblies is separate evidence from live-host behavior. Start-page opening, native dialogs and UI recent-list freshness still require installed-host acceptance; tests do not claim those were exercised in AutoCAD.

### Output Path Policy
All export operations are strictly guarded by a path policy that enforces:
- Output paths must be absolute.
- File extensions must match the specific tool (e.g., `.dxf` for DXF export).
- Existing files are not overwritten unless `overwrite_existing=true` is explicitly provided.
- Writing to the repository root directory is rejected unless `allow_repo_output=true` is set.

### Optional Toolsets and Read-Only Behavior

By default, only `query`, `modify`, `meta`, and `view` toolsets are enabled. You can opt-in to others using the `--toolsets` flag (e.g., `--toolsets all` or `--toolsets query,modify,meta,view,annotation,block,dimension,export,drawing`).

- **Read-Only Mode (`--read-only`)**: When read-only mode is active, all write-capable toolsets (`modify`, `code`, `annotation`, `dimension`, `export`, and `drawing` write tools) are completely disabled.
- **Block Toolset Split**: The `block` toolset is split into read-only and write-capable tools. If `--read-only` is active, `dwg_list_blocks` and `dwg_get_block_attributes` are still available (safe read inspection), but the mutation/creation tools (`dwg_insert_block`, `dwg_set_block_attributes`, `dwg_explode_block`) are stripped.
- **View and Read-Only**: Read-only retains transient zoom tools. `dwg_capture_view_image`, `dwg_inspect_view_region` and `dwg_restore_view` write image files and are excluded.
- **Drawing Operations and Read-Only**: The `drawing` toolset retains `dwg_get_variables` in read-only mode, but strips `dwg_set_system_variable`, `dwg_save_drawing`, and `dwg_purge_drawing`.
- **Deferred Angular Dimensions**: Note that only linear, aligned, radial, and diametric dimension types are currently supported. Angular dimensions are deferred and not yet implemented.
- **Native View Capture**: `dwg_capture_view_image` uses AutoCAD's `Document.CapturePreviewImage` API, with no desktop automation or DWG save. It rejects an active command or a document/camera change during capture. Optional `expected_document_fingerprint` binds a subsequent capture to a prior result. Save the returned metadata alongside the image for a reading atlas; the tool itself writes only the image. Native output dimensions may differ slightly from the requested size. Camera metadata describes the active viewport, not a universal pixel-to-world mapping for tiled views or paper layouts. See the workflow described above.
- **Deferred File Export Tools**: `dwg_export_pdf` and `dwg_export_image` remain deferred; viewport capture does not configure or validate plotting.

### Manual smoke checklist

In a scratch DWG:

1. Run `dwg_get_drawing_info`.
2. Run `dwg_list_layers`.
3. Create `BIMWRIGHT_TEST` with `dwg_create_layer`.
4. Create a point, polyline, rectangle, arc, and ellipse on `BIMWRIGHT_TEST` with `dwg_create_point`, `dwg_create_polyline`, `dwg_create_rectangle`, `dwg_create_arc`, and `dwg_create_ellipse`; record the returned hex handles and reserve one curve, such as the arc or ellipse, for color and offset checks.
5. Query, count, and select those entities by layer and type with `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, and `dwg_select_by_type`; confirm select tools return handle lists and do not change pickfirst selection.
6. Move, rotate, and scale non-reserved scratch entities with `dwg_move_entities`, `dwg_rotate_entities`, and `dwg_scale_entities`.
7. Copy one non-reserved scratch entity with `dwg_copy_entities`, then erase only that disposable copied temp entity with `dwg_erase_entities`.
8. Change color on the reserved curve with `dwg_change_color`, then offset that curve with `dwg_offset_entities` and confirm the returned generated handles are hex handles.
9. Confirm the existing text translation workflow still works: select scratch text, run `dwg_get_selected_texts`, then rewrite it with `dwg_translate_and_rewrite`.

### Optional smoke — annotation / block / dimension

Enable the optional toolsets first (`--toolsets …` or `--toolsets all`). On a scratch DWG:

1. Create text, mtext, leader, and table in a scratch DWG with `dwg_create_text`, `dwg_create_mtext`, `dwg_create_leader`, and `dwg_create_table`.
2. List block definitions with `dwg_list_blocks`.
3. Insert a known block from the drawing or from an absolute external DWG path with `dwg_insert_block`.
4. Get and set block attributes with `dwg_get_block_attributes` and `dwg_set_block_attributes`.
5. Explode a block reference with `dwg_explode_block`.
6. Create linear, aligned, radial, and diameter dimensions with `dwg_create_linear_dimension`, `dwg_create_aligned_dimension`, `dwg_create_radial_dimension`, and `dwg_create_diameter_dimension`, confirming linear projected-distance validation succeeds/rejects as expected.

### Optional smoke — view / export / drawing

With `view`, `export`, and `drawing` toolsets enabled (as needed):

1. Run `dwg_zoom_extents`.
2. Run `dwg_zoom_window` with coordinates.
3. Zoom to an entity with `dwg_zoom_to_entity` using a recorded hex handle.
4. Read drawing variables with `dwg_get_variables`.
5. Export drawing to dxf with `dwg_export_dxf`.
6. Run `dwg_purge_drawing` with `dry_run=true`, then with `confirm=true` (on a copied/disposable DWG only).
7. Run `dwg_save_drawing` with `confirm=true` (on a copied/disposable DWG only).

### Migration from 0.1.x tool names

MCP tool names now use the `dwg_` prefix. Raw plugin command names remain internal wire commands.

| 0.1.x MCP name | 1.0 MCP name |
|----------------|--------------|
| `get_selected_texts` | `dwg_get_selected_texts` |
| `update_texts` | `dwg_update_texts` |
| `translate_and_rewrite` | `dwg_translate_and_rewrite` |
| `apply_unicode_style` | `dwg_apply_unicode_style` |
| `collapse_and_rewrite` | `dwg_collapse_and_rewrite` |
| `send_code` | `dwg_send_code` |
| `run_lisp` | `dwg_run_lisp` |

---

## Standard Workflow

```
1. User selects text entities in AutoCAD
2. Agent calls dwg_get_selected_texts -> receives clustered text groups
3. Agent translates each cluster
4. Agent calls dwg_translate_and_rewrite([{id, new_text}, ...])
   Tool handles: anchor, delete, MText, font style, height. Done.
5. User runs REGEN if needed
```

Two steps from the agent's perspective: read, then write.

---

## Supported AutoCAD Versions

| Version | ObjectARX release | Plugin TFM | Status |
|---------|-------------------|------------|--------|
| AutoCAD 2022 | 24.1 | `net48` | Shell scaffolded; release build requires local Autodesk refs |
| AutoCAD 2023 | 24.2 | `net48` | Shell scaffolded; release build requires local Autodesk refs |
| AutoCAD 2024 | 24.3 | `net48` | Default supported shell and normal solution build |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | Shell scaffolded; release build requires local Autodesk refs |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | Shell scaffolded; binary-compatible with 2025 but built as its own shell |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | Shell scaffolded; not binary-compatible with 2025/2026 |

The server and tests can pass without every AutoCAD shell being release-built. Shipping an AutoCAD year requires building that shell on a prepared machine with the matching Autodesk managed assemblies.

---

## Security

`dwg_send_code` executes arbitrary C# with full access to AutoCAD and the local filesystem; it is for trusted agents, not a sandbox. `dwg_run_lisp` is blocked regardless of input, scanner verdict or `MCPENABLECODE`. No LISP wrapper is created or queued, and no trusted path or SECURELOAD setting is changed. Do not bypass a LISP refusal through C#, batch or ToolBaker. `dwg_inspect_lisp` is static triage, not antivirus or machine-wide malware protection; `clean` means no known pattern matched, not safe. All reports set `execution_authorized=false` and `safety_assured=false`. Update both server and plugin and restart their processes to apply the restriction; old binaries retain their old behavior.

The security model relies on:

- **Local-only transport** — TCP on 127.0.0.1 for AutoCAD 2022–2024, loopback Named Pipe for 2025–2027, no remote access.
- **Per-session auth token** — rotates on each plugin start, verified per request.
- **Session kill-switch** — `MCPDISABLECODE` / `MCPENABLECODE` control `dwg_send_code`. LISP remains blocked even after enabling code or restarting the listener.
- **Timeout boundary** — script execution runs inline on the document-lock thread with cooperative 30s cancellation.
- **Trusted agent assumption** — only use with MCP clients you control.

Do not expose the plugin port to the network.

---

## Project Structure

```
dwg-mcp/
├── src/
│   ├── Bimwright.Dwg.sln
│   ├── server/            # .NET 8 MCP server (global tool)
│   ├── shared/            # Handlers, clustering, rewriting, unicode
│   ├── plugin-acad22/     # AutoCAD 2022 shell (.NET 4.8)
│   ├── plugin-acad23/     # AutoCAD 2023 shell (.NET 4.8)
│   ├── plugin-acad24/     # AutoCAD 2024 shell (.NET 4.8)
│   ├── plugin-acad25/     # AutoCAD 2025 shell (.NET 8)
│   ├── plugin-acad26/     # AutoCAD 2026 shell (.NET 8)
│   └── plugin-acad27/     # AutoCAD 2027 shell (.NET 10)
├── tests/                 # xUnit
├── scripts/               # install/uninstall PowerShell
├── lib/acad24/            # Notes only; Autodesk DLLs are never committed
└── .github/workflows/     # CI
```

---

## The bimwright family

Open-source tools connecting AI assistants to BIM and CAD applications.

The name **bimwright** combines **BIM** with **wright**, an old word for a maker or builder—as in *shipwright*.

See [how the gateway names are chosen](https://github.com/bimwright/.github/blob/master/profile/README.md#naming).

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Vietnamese-first BIM knowledge base

---

## Disclaimer

Autodesk, AutoCAD, and DWG are trademarks or registered trademarks of Autodesk, Inc., and/or its subsidiaries and/or affiliates. DWG is the native file format for Autodesk AutoCAD software. bimwright is an independent open-source project and is not affiliated with, sponsored by, or endorsed by Autodesk, Inc.

---

## License

[Apache License 2.0](LICENSE)

Third-party notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Permissions & auto mode

Every tool except `dwg_send_code` declares read-only, destructive, idempotent and open-world hints. Full mode has 68 tools; default read-only mode has 16, and `--toolsets all --read-only` exposes the 22 read-only tools below. Zoom can change transient UI state. Target switching, capture, region inspection and restore are excluded from read-only mode. `dwg_health` is read-only; its explicit update check can contact GitHub.

Use exact rules for the tools you intend to allow. Avoid `mcp__dwg-mcp__*`: it also permits drawing/file writes and code execution. `dwg_send_code` has no annotations and is deliberately absent from this read-only allowlist; any persistent approval for it must be an explicit choice in your MCP client.

<!-- read-only-allowlist:start -->
```text
mcp__dwg-mcp__dwg_count_entities
mcp__dwg-mcp__dwg_create_bake_issue_draft
mcp__dwg-mcp__dwg_get_block_attributes
mcp__dwg-mcp__dwg_get_current_target
mcp__dwg-mcp__dwg_get_drawing_info
mcp__dwg-mcp__dwg_get_entity_properties
mcp__dwg-mcp__dwg_get_selected_texts
mcp__dwg-mcp__dwg_get_variables
mcp__dwg-mcp__dwg_health
mcp__dwg-mcp__dwg_inspect_lisp
mcp__dwg-mcp__dwg_list_available_targets
mcp__dwg-mcp__dwg_list_bake_suggestions
mcp__dwg-mcp__dwg_list_baked_tools
mcp__dwg-mcp__dwg_list_blocks
mcp__dwg-mcp__dwg_list_layers
mcp__dwg-mcp__dwg_list_recent_drawings
mcp__dwg-mcp__dwg_query_entities
mcp__dwg-mcp__dwg_select_by_layer
mcp__dwg-mcp__dwg_select_by_type
mcp__dwg-mcp__dwg_zoom_extents
mcp__dwg-mcp__dwg_zoom_to_entity
mcp__dwg-mcp__dwg_zoom_window
```
<!-- read-only-allowlist:end -->
