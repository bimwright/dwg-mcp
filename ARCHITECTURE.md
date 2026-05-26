# Architecture

## Two processes, one local transport

```
MCP client (Claude Code / Cursor / OpenCode / ...)
        |  stdio (NDJSON MCP)
        v
Bimwright.Dwg.Server  (.NET 8 console app, global tool)
        |  TCP NDJSON + token auth (127.0.0.1)
        v
Bimwright.Dwg.Plugin  (AutoCAD 2022-2027 shells)
        |  Document.LockDocument()
        v
AutoCAD .NET API  (ObjectARX 2022-2027)
```

**Server** is an MCP server. It talks stdio to the client, translates each tool call into a JSON envelope, and forwards it over localhost transport to the plugin. Server is a plain .NET 8 global tool with no AutoCAD reference. The default server registers query, modify, routing/meta, and batch tools. `dwg_send_code` lives in a separate `CodeTools` surface and is registered only when the process starts with `--enable-send-code` or `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`.

**Plugin** is an `IExtensionApplication` loaded by AutoCAD. It runs a local listener on a background thread, dispatches requests through `DwgApiExecutor`, locks the document, and executes commands within transactions. Unlike Revit, AutoCAD allows `Document.LockDocument()` from background threads; `DwgApiExecutor` still serializes AutoCAD API work so concurrent requests do not interleave drawing mutations. Plugin-side code execution is disabled until the user runs `MCPENABLECODE` in AutoCAD; `MCPDISABLECODE` revokes it for the current plugin session.

## Discovery

Plugin writes discovery files on startup:

| AutoCAD | Discovery file | Transport |
|---------|----------------|-----------|
| 2022-2027 | `%LOCALAPPDATA%\Bimwright\Dwg\acad-YYYY.json` | TCP in current shells; server also accepts named pipe discovery |
| 2024 only | `%LOCALAPPDATA%\Bimwright\portAcad24.txt` | TCP legacy fallback |

The v2 JSON file contains:

```json
{
  "schema_version": 2,
  "acad_year": 2024,
  "transport": "tcp",
  "host": "127.0.0.1",
  "port": 49152,
  "pipe_name": null,
  "auth_token": "32-char hex token",
  "pid": 1234,
  "process_name": "acad",
  "started_at_utc": "2026-05-25T00:00:00Z"
}
```

Server reads v2 files from `%LOCALAPPDATA%\Bimwright\Dwg\`, verifies the PID is alive, removes invalid stale files, and auto-selects the newest discovered target unless `--target`, `BIMWRIGHT_DWG_TARGET`, or `dwg_switch_target` pins a year. Target values are always 4-digit years: `2022` through `2027`. The reader still accepts the transitional `target`/`version` string fields for compatibility with earlier refactor builds.

## Auth protocol

1. Plugin generates a new auth token (GUID) each time the listener starts.
2. Token is written to the discovery file.
3. Every TCP request envelope includes `"auth": "<token>"`.
4. Plugin rejects requests with missing or wrong token: `{ok:false, error:"unauthorized"}`.
5. Server reads token from the selected discovery file before each connection.

## Request lifecycle

1. MCP client sends `tools/call` over stdio.
2. Server tool classes receive the call via `[McpServerTool]`. Default toolsets are `query`, `modify`, and `meta`; `code` and `toolbaker` are opt-in toolsets.
3. `LoggedCall` wrapper logs start, creates request envelope with auth token.
4. `PluginClient.SendAsync` opens a TCP or named pipe connection based on discovery.
5. Plugin's listener thread reads the NDJSON line, `CommandDispatcher.Dispatch` is called:
   - Auth token verified.
   - `send_code` rejected unless AutoCAD-side consent is enabled.
   - Handler looked up by command name.
   - `DocumentInvoker.Invoke` locks the active document.
   - General CAD handlers operate on that active document and resolve entity references from AutoCAD hex handles.
   - Handler executes within a Transaction.
   - Response serialized as JSON.
6. Response travels back over TCP.
7. `LoggedCall` logs finish (duration, success/error).

Timeout: 30s per request on the server side. `send_code` also runs its Roslyn script on a dedicated plugin thread with cancellation and abort fallback before the handler returns. Connection-per-call for TCP; named pipe transport is also supported by the server discovery contract.

## Threading model

```
Background TCP listener thread
    |
    v (new thread per client connection)
Client handler thread
    |
    v DwgApiExecutor queue
    |
    v Document.LockDocument()
    |
    v Transaction { handler.Execute() }
    |
    v Response written, connection closed
```

AutoCAD allows multiple threads to lock the same document sequentially. Each request gets its own lock scope and transaction. `DwgApiExecutor` provides a process-local queue above the lock so requests are processed in order and earlier failures do not block later work.

## Handler dispatch

`CommandDispatcher` uses an explicit dictionary (not reflection). `send_code` remains in the dispatch table so opt-in calls can be handled, but dispatch rejects it unless `MCPENABLECODE` has enabled the current plugin session. The snippet below is abbreviated around the full runtime class, but it includes representative Plan 2 query/create/modify wire commands so it stays aligned with the toolset table.

```csharp
_commands = new Dictionary<string, IAcadCommand>
{
    { "get_drawing_info",       new GetDrawingInfoHandler() },
    { "get_entity_properties",  new GetEntityPropertiesHandler() },
    { "list_layers",            new ListLayersHandler() },
    { "query_entities",         new QueryEntitiesHandler() },
    { "count_entities",         new CountEntitiesHandler() },
    { "select_by_layer",        new SelectByLayerHandler() },
    { "select_by_type",         new SelectByTypeHandler() },
    { "get_selected_texts",      new GetSelectedTextsHandler() },
    { "update_texts",            new UpdateTextsHandler() },
    { "create_layer",           new CreateLayerHandler() },
    { "create_line",            new CreateLineHandler() },
    { "create_circle",          new CreateCircleHandler() },
    { "create_point",           new CreatePointHandler() },
    { "create_polyline",        new CreatePolylineHandler() },
    { "create_rectangle",       new CreateRectangleHandler() },
    { "create_arc",             new CreateArcHandler() },
    { "create_ellipse",         new CreateEllipseHandler() },
    { "change_layer",           new ChangeLayerHandler() },
    { "change_color",           new ChangeColorHandler() },
    { "move_entities",          new MoveEntitiesHandler() },
    { "rotate_entities",        new RotateEntitiesHandler() },
    { "scale_entities",         new ScaleEntitiesHandler() },
    { "copy_entities",          new CopyEntitiesHandler() },
    { "erase_entities",         new EraseEntitiesHandler() },
    { "offset_entities",        new OffsetEntitiesHandler() },
    { "send_code",               new SendCodeHandler() },
    { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
    { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
    { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
    { "list_baked_tools",        new ListBakedToolsHandler() },
    { "zoom_extents",            new ZoomExtentsHandler() },
    { "zoom_window",             new ZoomWindowHandler() },
    { "zoom_to_entity",          new ZoomToEntityHandler() },
    { "export_dxf",              new ExportDxfHandler() },
    { "get_variables",           new GetVariablesHandler() },
    { "set_system_variable",     new SetSystemVariableHandler() },
    { "save_drawing",            new SaveDrawingHandler() },
    { "purge_drawing",           new PurgeDrawingHandler() },
    { "pid_setup_layers",        new PidSetupLayersHandler() },
    { "pid_list_categories",     new PidListCategoriesHandler() },
    { "pid_list_symbols",        new PidListSymbolsHandler() },
    { "pid_draw_pipe",           new PidDrawPipeHandler() },
    { "pid_insert_symbol",       new PidInsertSymbolHandler() },
    { "pid_add_flow_arrow",      new PidAddFlowArrowHandler() },
    { "pid_add_equipment_tag",   new PidAddEquipmentTagHandler() },
    { "pid_add_line_number",     new PidAddLineNumberHandler() },
};
_commands.Add("apply_bake", new ApplyBakeSuggestionHandler((cmd, p) => ValidateCommand(cmd, p, out _)));
_commands.Add("batch_execute", new BatchExecuteHandler(ExecuteCommand));
_commands.Add("run_baked_tool", new RunBakedToolHandler(ExecuteCommand));
```

MCP-facing names are registered separately on the server with a `dwg_` prefix. For example, `dwg_translate_and_rewrite` forwards the internal wire command `translate_and_rewrite`.

## Toolsets and read-only mode

Toolsets are resolved by `DwgMcpConfig` and `ToolsetFilter`:

| Toolset | MCP tools |
|---------|-----------|
| `query` | `dwg_get_drawing_info`, `dwg_get_entity_properties`, `dwg_list_layers`, `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, `dwg_select_by_type`, `dwg_get_selected_texts` |
| `modify` | `dwg_create_layer`, `dwg_create_line`, `dwg_create_circle`, `dwg_create_point`, `dwg_create_polyline`, `dwg_create_rectangle`, `dwg_create_arc`, `dwg_create_ellipse`, `dwg_change_layer`, `dwg_change_color`, `dwg_move_entities`, `dwg_rotate_entities`, `dwg_scale_entities`, `dwg_copy_entities`, `dwg_erase_entities`, `dwg_offset_entities`, `dwg_update_texts`, `dwg_translate_and_rewrite`, `dwg_apply_unicode_style`, `dwg_collapse_and_rewrite` |
| `meta` | `dwg_batch_execute`, `dwg_list_available_targets`, `dwg_get_current_target`, `dwg_switch_target` |
| `toolbaker` | `dwg_list_baked_tools`, `dwg_run_baked_tool`, `dwg_list_bake_suggestions`, `dwg_accept_bake_suggestion`, `dwg_dismiss_bake_suggestion`, `dwg_create_bake_issue_draft` |
| `code` | `dwg_send_code` |
| `annotation` | `dwg_create_text`, `dwg_create_mtext`, `dwg_create_leader`, `dwg_create_table` |
| `block` | `dwg_list_blocks`, `dwg_get_block_attributes`, `dwg_insert_block`, `dwg_set_block_attributes`, `dwg_explode_block` |
| `dimension` | `dwg_create_linear_dimension`, `dwg_create_aligned_dimension`, `dwg_create_radial_dimension`, `dwg_create_diameter_dimension` |
| `view` | `dwg_zoom_extents`, `dwg_zoom_window`, `dwg_zoom_to_entity`, and deferred `dwg_capture_view` |
| `export` | `dwg_export_dxf`, and deferred `dwg_export_pdf`, `dwg_export_image` |
| `drawing` | `dwg_get_variables`, `dwg_set_system_variable`, `dwg_save_drawing`, `dwg_purge_drawing` |
| `pid` | `dwg_pid_setup_layers`, `dwg_pid_list_categories`, `dwg_pid_list_symbols`, `dwg_pid_draw_pipe`, `dwg_pid_insert_symbol`, `dwg_pid_add_flow_arrow`, `dwg_pid_add_equipment_tag`, `dwg_pid_add_line_number` |

`--read-only` or `BIMWRIGHT_DWG_READ_ONLY=1` removes write-capable toolsets/methods completely (`modify`, `code`, `annotation`, `dimension`, `dwg_batch_execute`, ToolBaker write tools, `export` tools, `drawing` write tools, and `pid` tools).
- **P&ID Toolset (`pid`)**: The `pid` toolset is default-off and write-capable, so it is completely stripped in read-only mode.
- **P&ID Exclusions**: The procedural-first P&ID toolset has zero runtime dependencies on `C:\PIDv4-CTO` path scanning, `pid_tools.lsp`, ezdxf, or `SendStringToExecute`. It uses standard AutoCAD .NET transactions and native 2D geometry primitives.
- **Block Toolset Split**: The `block` toolset splits registration between read-only `BlockTools` (`dwg_list_blocks`, `dwg_get_block_attributes`) and write-capable `BlockWriteTools` (`dwg_insert_block`, `dwg_set_block_attributes`, `dwg_explode_block`). In read-only mode, only the read-only wrappers are registered, preserving safe drawing inspection.
- **View Navigation and Read-Only**: The `view` toolset is default-on and retains the viewport navigation tools (`dwg_zoom_extents`, `dwg_zoom_window`, `dwg_zoom_to_entity`) in read-only mode, but strips the deferred `dwg_capture_view` tool.
- **Drawing Operations and Read-Only**: The `drawing` toolset retains `dwg_get_variables` in read-only mode, but strips `dwg_set_system_variable`, `dwg_save_drawing`, and `dwg_purge_drawing`.
- **Deferred Angular Dimensions**: The `dimension` toolset only registers linear, aligned, radial, and diametric dimension creators. Angular dimensions are deferred and not included in this release.
- **Deferred File Export/Capture Tools**: The `dwg_export_pdf`, `dwg_export_image`, and `dwg_capture_view` tools have been deferred to ensure absolute reliability of drawing view captures and plot configurations.

The default startup surface is 35 tools. Enabling the optional `code`, `toolbaker`, `annotation`, `block`, `dimension`, `export`, `drawing`, and `pid` toolsets exposes the full 68 backed MCP tools.

Plan 2 entity query/select tools are model-space only. `dwg_select_by_layer` and `dwg_select_by_type` return handle lists and do not mutate AutoCAD pickfirst selection. Create, copy, offset, and modify handlers identify generated or modified entities with AutoCAD hex handles.

## Manual smoke checklist

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
10. Verify Plan 4 View, Export, and Drawing check:
    - Run `dwg_zoom_extents`.
    - Run `dwg_zoom_window` with target points.
    - Zoom to an entity with `dwg_zoom_to_entity` using a recorded hex handle.
    - Read drawing variables with `dwg_get_variables`.
    - Export drawing to dxf with `dwg_export_dxf` (guarded by path policy).
    - Run `dwg_purge_drawing` with `dry_run=true`, then with `confirm=true` (on a copied/disposable DWG only).
    - Run `dwg_save_drawing` with `confirm=true` (on a copied/disposable DWG only).

## ToolBaker

ToolBaker storage is server-owned SQLite at `%LOCALAPPDATA%\Bimwright\Dwg\baked\bake.db`. Accepting a suggestion sends the internal `apply_bake` command to the plugin for policy validation and schema smoke-test. The server redacts baked source before persistence and records usage events in the `usage_events` table for pattern detection.

At runtime, `dwg_run_baked_tool` reads the accepted record from SQLite and sends that record to the plugin. The plugin does not own a separate registry file. V1 baked tools are declarative preset or macro records that dispatch existing `IAcadCommand` handlers; future generated-source paths must pass `BakeCompilerPolicy` before they can be enabled.

## Multi-version shells

The repo contains shell projects for AutoCAD 2022-2027:

| Shell | TFM |
|-------|-----|
| `src/plugin-acad22` | `net48` |
| `src/plugin-acad23` | `net48` |
| `src/plugin-acad24` | `net48` |
| `src/plugin-acad25` | `net8.0-windows` |
| `src/plugin-acad26` | `net8.0-windows` |
| `src/plugin-acad27` | `net10.0-windows` |

The normal solution build includes the available local 2024 shell. Release packaging for another AutoCAD year requires a prepared machine with that year's Autodesk managed assemblies and should build the matching shell explicitly.
