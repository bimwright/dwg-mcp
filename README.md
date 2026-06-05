<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-autocad-versions"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#tools"><img src="https://img.shields.io/badge/MCP-35%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  English · <a href="README.vi.md">Tiếng Việt</a>
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

The agent talks MCP. The server talks to the plugin over localhost TCP. The plugin talks to the AutoCAD .NET API.

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
              | TCP (127.0.0.1)
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

### 1. Server — .NET global tool

```bash
dotnet tool install -g Bimwright.Dwg.Server
bimwright-dwg --help
```

Requires .NET 8 SDK.

### 2. Plugin — AutoCAD add-in

**Option A: Auto-deploy (.bundle)**

Download the plugin from [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest):

```powershell
pwsh scripts/install.ps1 -Version 2024 -WhatIf    # preview
pwsh scripts/install.ps1 -Version 2024            # install
pwsh scripts/install.ps1 -Uninstall               # remove
```

The script deploys to `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle\`. Restart AutoCAD to load.

**Option B: Manual NETLOAD (dev)**

In AutoCAD: `NETLOAD` → pick `src/plugin-acad24/bin/Debug/net48/Bimwright.Dwg.Plugin.Acad24.dll`. Listener auto-starts.

### 3. Wire up your MCP client

Add to your MCP client config (e.g., `.mcp.json`):

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": []
    }
  }
}
```

Pin a specific AutoCAD instance with a 4-digit target year:

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": ["--target", "2024"]
    }
  }
}
```

Use `--read-only` to expose only query/routing tools plus ToolBaker read tools if that toolset is enabled. Use `--toolsets query,modify,meta,annotation` (or `--toolsets all`) to opt into optional toolsets, or set the environment variable `BIMWRIGHT_DWG_TOOLSETS=query,modify,meta,annotation`.

`dwg_send_code` is hidden from the default tool list. To expose it, opt in on both sides:

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": ["--enable-send-code"]
    }
  }
}
```

Then run `MCPENABLECODE` inside AutoCAD for the current plugin session. `MCPDISABLECODE` turns it off again.

---

## Tools

Default startup exposes 35 tools: query, modify, routing/meta, batch, and view. Optional ToolBaker, annotation, block, dimension, export, and drawing toolsets, enabled through `--toolsets`, and `dwg_send_code` bring the backed MCP surface to 60 tools.

General CAD tools operate on the current active document in the selected AutoCAD target. Entity inputs and returned entity IDs use AutoCAD hex handles, such as `7F5AD`, returned by selection, creation, or property tools. Creation, copy, offset, and modify responses identify generated or modified entities by hex handle.

Plan 2 query expansion is model-space only: `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, and `dwg_select_by_type` scan model space, not paper-space/layout entities. `dwg_select_by_layer` and `dwg_select_by_type` return handle lists for the caller; they do not change AutoCAD's pickfirst selection.

| Tool | Purpose |
|------|---------|
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
| `dwg_list_available_targets` | List running AutoCAD targets discovered from v2 JSON and legacy 2024 discovery files |
| `dwg_get_current_target` | Show the pinned target year, if any |
| `dwg_switch_target` | Pin this server process to AutoCAD `2022` through `2027` |
| `dwg_batch_execute` | Run multiple internal wire commands as a logical batch |
| `dwg_send_code` | **Opt-in only.** Execute C# after server flag/env enablement and AutoCAD-side `MCPENABLECODE` consent |
| `dwg_zoom_extents` | Zoom to the extents of the drawing viewport |
| `dwg_zoom_window` | Zoom viewport to a window defined by two corner points |
| `dwg_zoom_to_entity` | Zoom viewport to the extents of a specific drawing entity identified by handle |

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
- **View Navigation and Read-Only**: The `view` toolset is default-on and retains the viewport navigation tools (`dwg_zoom_extents`, `dwg_zoom_window`, `dwg_zoom_to_entity`) in read-only mode, but strips the deferred `dwg_capture_view` tool.
- **Drawing Operations and Read-Only**: The `drawing` toolset retains `dwg_get_variables` in read-only mode, but strips `dwg_set_system_variable`, `dwg_save_drawing`, and `dwg_purge_drawing`.
- **Deferred Angular Dimensions**: Note that only linear, aligned, radial, and diametric dimension types are currently supported. Angular dimensions are deferred and not yet implemented.
- **Deferred File Export/Capture Tools**: The `dwg_export_pdf`, `dwg_export_image`, and `dwg_capture_view` tools have been deferred to ensure absolute reliability of drawing view captures and plot configurations.

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

### Plan 3 Manual Smoke Checklist (Manual Smoke Pending)

The manual smoke testing for Plan 3 annotation, block, and dimension tools is currently **pending** AutoCAD execution, but the following scenarios are designed:

1. Create text, mtext, leader, and table in a scratch DWG with `dwg_create_text`, `dwg_create_mtext`, `dwg_create_leader`, and `dwg_create_table`.
2. List block definitions with `dwg_list_blocks`.
3. Insert a known block from the drawing or from an absolute external DWG path with `dwg_insert_block`.
4. Get and set block attributes with `dwg_get_block_attributes` and `dwg_set_block_attributes`.
5. Explode a block reference with `dwg_explode_block`.
6. Create linear, aligned, radial, and diameter dimensions with `dwg_create_linear_dimension`, `dwg_create_aligned_dimension`, `dwg_create_radial_dimension`, and `dwg_create_diameter_dimension`, confirming linear projected-distance validation succeeds/rejects as expected.

### Plan 4 Manual Smoke Checklist (Manual Smoke Pending)

The manual smoke testing for Plan 4 view navigation, dxf export, and drawing variable/save/purge tools is currently **pending** AutoCAD execution, but the following scenarios are designed:

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

`dwg_send_code` executes arbitrary C# with full access to the AutoCAD process and local filesystem. It is not registered in the default MCP tool surface. To use it, start the server with `--enable-send-code` or `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`, then run `MCPENABLECODE` inside AutoCAD for that plugin session.

The security model relies on:

- **Local-only transport** — TCP on 127.0.0.1, no remote access.
- **Per-session auth token** — rotates on each plugin start, verified per request.
- **Two-sided code opt-in** — server-side tool registration plus AutoCAD-side consent.
- **Timeout boundary** — script execution runs on a dedicated thread with cancellation and abort on timeout.
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

Hand-forged MCP gateways for the AEC toolchain — one architecture, predictable / auditable / reversible:

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Vietnamese-first BIM knowledge base

---

## Disclaimer

AutoCAD and Autodesk are registered trademarks of Autodesk, Inc. bimwright is an independent open-source project and is not affiliated with, sponsored by, or endorsed by Autodesk, Inc.

---

## License

[Apache License 2.0](LICENSE)

Third-party notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
