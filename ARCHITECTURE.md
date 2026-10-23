# Architecture

## Two processes, one local transport

```
MCP client (Claude Code / Cursor / OpenCode / ...)
        |  stdio (NDJSON MCP)
        v
Bimwright.Dwg.Server  (.NET 8 console app, global tool)
        |  TCP (2022-2024) or named pipe (2025-2027), NDJSON + token auth, loopback only
        v
Bimwright.Dwg.Plugin  (AutoCAD 2022-2027 shells)
        |  main-thread dispatch (Application.Idle), then Document.LockDocument()
        v
AutoCAD .NET API  (ObjectARX 2022-2027)
```

**Server** is an MCP server. It talks stdio to the client, translates each tool call into a JSON envelope, and forwards it over localhost transport to the plugin. Server is a plain .NET 8 global tool with no AutoCAD reference. The default server registers query, modify, routing/meta, and batch tools. `dwg_send_code` ships on the default surface through the `meta` toolset (rvt-mcp parity) and is stripped by `--read-only`; the legacy `code` toolset name remains an explicit opt-in alias.

**Plugin** is an `IExtensionApplication` loaded by AutoCAD. Its background listener authenticates and dispatches requests. `DwgApiExecutor` serializes document/session work, and `MainThreadExecutor` queues that work for AutoCAD's `Application.Idle` callback. `DocumentInvoker` then acquires the active document lock before invoking a document handler. Document locking alone is not the main-thread dispatch mechanism. Mutation handlers own their transactions; the dispatcher does not supply a universal transaction around every command. Plugin-side C# execution is enabled when the listener starts; `MCPDISABLECODE` disables it for the current plugin session and `MCPENABLECODE` re-enables it.

## Discovery

After its listener is ready, the plugin publishes a per-instance descriptor and a legacy year alias:

| AutoCAD | Discovery file | Transport |
|---------|----------------|-----------|
| 2022-2024 | `%LOCALAPPDATA%\Bimwright\dwg-mcp\acad-YYYY-PID.json`, plus `acad-YYYY.json` | TCP loopback. The server also accepts named-pipe discovery. |
| 2025-2027 | `%LOCALAPPDATA%\Bimwright\dwg-mcp\acad-YYYY-PID.json`, plus `acad-YYYY.json` | Named pipe (`ACAD2025_OR_GREATER`). 2025 and 2026 are not binary-compatible with 2027. |
| 2024 only | `%LOCALAPPDATA%\Bimwright\portAcad24.txt` | TCP legacy fallback |

The current publisher writes schema v3, including the process start time used to detect PID reuse. A TCP descriptor has this shape:

```json
{
  "schema_version": 3,
  "acad_year": 2024,
  "target_id": "acad-2024-1234",
  "process_start_utc": "2026-05-25T00:00:00Z",
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

`AuthToken` reads and deduplicates discovery entries, checks process identity and supports `--target`, `BIMWRIGHT_DWG_TARGET`, and `dwg_switch_target` selectors: a calendar year (`2022`–`2027`), `pid:<PID>`, or `id:acad-<year>-<PID>`. More than one matching instance returns `AMBIGUOUS_TARGET`; the gateway does not choose the newest instance. Legacy year descriptors and the AutoCAD 2024 text descriptor remain compatibility inputs.

One gateway lifetime owns one `TargetBinding`. Calls capture its generation, and `PluginClient` checks that generation and process identity immediately before writing transport bytes. Losing a bound process does not select a replacement. `dwg_switch_target` verifies authenticated health and the active drawing by default; `verify=false` permits reads only. Opening or switching drawings requires re-verification before subsequent drawing writes. Optional `expected_document_id` checks a drawing lifetime, including when the same file has been reopened. The plugin checks the process before dispatch and the drawing under its document lock before the handler runs.

## Configuration ownership

The gateway and plugin currently have separate configuration readers and files:

| Owner | File | Scope |
|-------|------|-------|
| `DwgMcpConfig` in the gateway | `%LOCALAPPDATA%\Bimwright\dwg-mcp\dwgmcp.config.json` | Target, toolsets, read-only, send-code exposure, call-log files and ToolBaker exposure. `--config` selects another gateway file. |
| `PluginSettings` in AutoCAD | `%LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json` | Toast enabled state, branding, idle duration and toast position. |

Gateway resolution is CLI over `BIMWRIGHT_DWG_*` environment variables over JSON over code defaults. It happens at gateway startup. The plugin's toast-enabled preference also supports `BIMWRIGHT_DWG_ENABLE_TOAST` at load time. UI preference writes preserve unrelated JSON keys and refuse an existing malformed JSON object; they do not rewrite gateway settings.

`AllowLanBind` is parsed but is not implemented by the plugin; startup reports that limitation and the plugin remains local-only. `LogLevel` is parsed by `DwgMcpConfig`, but the current bootstrap does not apply it to the logging filter. Neither parsed option establishes a runtime capability.

The current request envelope carries command, parameters, authentication and target context. It does not carry a copy of the gateway configuration. Gateway read-only/send-code policy is enforced by registration and gateway call wrappers; the host independently checks target guards, blocks LISP and enforces its session C# toggle. UI history remains session-only and does not retain raw input or result bodies.

## Auth protocol

1. Plugin generates a new auth token (GUID) each time the listener starts.
2. Token is written to the discovery file.
3. Every TCP request envelope includes `"auth": "<token>"`.
4. Plugin rejects requests with missing or wrong token: `{ok:false, error:"unauthorized"}`.
5. The gateway captures the selected descriptor's token with its target binding and uses it on that binding's requests. Selecting and verifying a refreshed descriptor is an explicit target operation; a stale token never authorizes a fallback instance.

## Request lifecycle

1. MCP client sends `tools/call` over stdio.
2. The MCP SDK resolves the registered tool. `ToolArgumentGuard` checks top-level argument names against its final `InputSchema` before SDK binding and invocation. Default toolsets are `query`, `modify`, `meta`, and `view`; `code` is an alias for the code tools already registered by `meta`, while `toolbaker` is optional.
3. Gateway wrappers enforce read-only/send-code policy, capture target context and send the exposed tool catalog when needed. Call-log files are opt-in.
4. `PluginClient` validates the pinned binding before sending the authenticated request over a per-call TCP or named-pipe connection.
5. Plugin's listener thread reads the NDJSON line, `CommandDispatcher.Dispatch` is called:
   - Auth token and expected process identity verified.
   - Catalog and health requests use dedicated paths; health does not require a document lock.
   - `run_lisp` always rejected before document locking; no scanner verdict or session toggle authorizes it. `send_code` is rejected when `MCPDISABLECODE` disabled the current plugin session.
   - Handler looked up by command name.
   - Session handlers use `DocumentInvoker.InvokeSession` without requiring an active drawing.
   - Document handlers use `DocumentInvoker.Invoke`: queued main-thread dispatch, then active-document lock and drawing-identity validation.
   - General CAD handlers operate on that active document and resolve entity references from AutoCAD hex handles.
   - A mutation handler manages its own transaction.
   - Response serialized as JSON.
6. The sanitized response travels back over the selected transport.
7. Gateway logging and the host activity UI record their respective outcomes; opt-in file logging and in-memory UI history are separate.

Missing required keys return an MCP tool error (`isError=true`) containing `ok=false`, `INVALID_ARGUMENT`, suggestions and `invalid_arguments`, without invoking the tool or plugin. Names are case-sensitive. Undeclared top-level keys allow the call to run once and add `_ignored_arguments` to its result; an explicit `additionalProperties` allowance suppresses that warning. The guard preserves image content and error status. Unknown tools and schemas without declared properties retain SDK behavior, as do value coercion and binding/tool-body errors. Nested JSON and code contents are not validated by this guard.

Rejection diagnostics and opt-in rejection logs contain key names only, capped at 32 keys per list and 64 characters per name. Raw rejected argument values are never passed to the logger. Logging remains off by default.

Gateway response timeout is normally 30 seconds. `open_drawing` defaults to a 600-second wait and accepts `timeout_seconds` from 1 to 900. The main-thread queue has a separate default 15-second wait for work to start: a timed-out queued item is cancelled before execution, while work that has already started is allowed to finish. A caller timeout or disconnect does not cancel native work already running; inspect its outcome before retrying a write.

`send_code` runs synchronous Roslyn snippets on the AutoCAD main thread under the document lock, with cooperative 30-second cancellation. A script blocked in a native call can continue past a caller's wait. Syntax containing `async`/`await` or `#load` is rejected before execution, and external source resolution is disabled. Callers must not offload AutoCAD API calls to other threads. Return values use a contract resolver that rejects AutoCAD/COM runtime types before traversing nested values.

The plugin truncates result payloads above 10 MiB. There is currently no full-output spill file or configurable final-MCP-response budget. Transport framing separately limits requests to 16 MiB and responses to 32 MiB; inline view images have an 8 MiB file limit.

`run_lisp` is a compatibility refusal in this build: the server returns `lisp_execution_blocked` before reading any source or contacting AutoCAD. The plugin independently rejects it before document locking, and its handler also refuses direct calls. Batch preflight and ToolBaker deny it. There is no approved-script trust mechanism or isolated executor, so all `file`/`code`/`command` inputs are blocked, including apparently harmless expressions. `MCPENABLECODE` only enables C#; it cannot enable LISP. The former wrapper/command queue/result-file path has been removed: no SECURELOAD changes, trusted-path enrollment, delayed execution or result polling occurs through this tool.

`dwg_inspect_lisp` remains available for static triage. It continues severity detection after its 200-finding display cap and marks sources over 2,000,000 characters as incompletely inspected/dangerous. Reports always include `execution_authorized=false`, `safety_assured=false`, and limitations. `clean` only means no known pattern matched, not safe; unknown/dynamic dependencies remain outside static analysis. Bare SHELL input and literal command forms are flagged conservatively. `send_code` remains a full-trust escape hatch, not a sandbox or machine-wide malware protection. Agent instructions prohibit using it, batches or ToolBaker to bypass LISP refusal; this instruction is not a technical sandbox for arbitrary C#.

Both server and plugin must be updated and restarted for these guards to apply to all supported entry points. New server + old plugin rejects MCP `dwg_run_lisp` locally; new plugin + old server rejects the wire command. Old server + old plugin retains the previous execution behavior. Any future reintroduction of LISP execution requires a new isolation/trust design and lifecycle acceptance; the removed executor is not a safe fallback.

## Threading model

```
Background TCP / named-pipe listener
    |
    v authenticated request dispatch
    |
    v DwgApiExecutor queue
    |
    v MainThreadExecutor queue
    |
    v Application.Idle on AutoCAD's initialized main thread
    |
    v Document.LockDocument() + document guard (document handlers)
    |
    v handler.Execute() / ExecuteSession()
    |
    v Response written, connection closed
```

`DwgApiExecutor` bounds admitted document/session work to 16 requests and prevents interleaved execution. `MainThreadExecutor` has AutoCAD's Idle callback as its consumer; document locks are acquired only after that dispatch. Session commands share the queue but do not require a drawing. Earlier failures do not block later queued work. `batch_execute` is a logical batch with `transactional=false`: successful steps are not automatically rolled back when a later step fails.

## Handler dispatch

`CommandDispatcher` uses an explicit dictionary (not reflection). `send_code` sits in the dispatch table like every other command; dispatch rejects it only when `MCPDISABLECODE` has disabled the current plugin session (`MCPENABLECODE` re-enables). The snippet below is abbreviated around the full runtime class, but it includes representative Plan 2 query/create/modify wire commands so it stays aligned with the toolset table.

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
| `meta` | `dwg_batch_execute`, `dwg_list_available_targets`, `dwg_get_current_target`, `dwg_switch_target`, `dwg_list_recent_drawings`, `dwg_open_drawing`, `dwg_send_code`, `dwg_run_lisp` |
| `toolbaker` | `dwg_list_baked_tools`, `dwg_run_baked_tool`, `dwg_list_bake_suggestions`, `dwg_accept_bake_suggestion`, `dwg_dismiss_bake_suggestion`, `dwg_create_bake_issue_draft` |
| `code` | legacy alias — registers `dwg_send_code` and `dwg_run_lisp` (already on via `meta` by default) |
| `annotation` | `dwg_create_text`, `dwg_create_mtext`, `dwg_create_leader`, `dwg_create_table` |
| `block` | `dwg_list_blocks`, `dwg_get_block_attributes`, `dwg_insert_block`, `dwg_set_block_attributes`, `dwg_explode_block` |
| `dimension` | `dwg_create_linear_dimension`, `dwg_create_aligned_dimension`, `dwg_create_radial_dimension`, `dwg_create_diameter_dimension` |
| `view` | `dwg_zoom_extents`, `dwg_zoom_window`, `dwg_zoom_to_entity`, `dwg_capture_view_image`, `dwg_inspect_view_region`, `dwg_restore_view` |
| `export` | `dwg_export_dxf`, and deferred `dwg_export_pdf`, `dwg_export_image` |
| `drawing` | `dwg_get_variables`, `dwg_set_system_variable`, `dwg_save_drawing`, `dwg_purge_drawing` |

`--read-only` or `BIMWRIGHT_DWG_READ_ONLY=1` removes write-capable toolsets/methods completely (`modify`, `dwg_send_code`/`dwg_run_lisp`, `annotation`, `dimension`, `dwg_batch_execute`, ToolBaker write tools, `export` tools, and `drawing` write tools).
- **Block Toolset Split**: The `block` toolset splits registration between read-only `BlockTools` (`dwg_list_blocks`, `dwg_get_block_attributes`) and write-capable `BlockWriteTools` (`dwg_insert_block`, `dwg_set_block_attributes`, `dwg_explode_block`). In read-only mode, only the read-only wrappers are registered, preserving safe drawing inspection.
- **View Navigation and Read-Only**: The `view` toolset is default-on and retains viewport navigation (`dwg_zoom_extents`, `dwg_zoom_window`, `dwg_zoom_to_entity`) in read-only mode. It strips `dwg_capture_view_image`, `dwg_inspect_view_region` and `dwg_restore_view`, which write image files.
- **Routing and Read-Only**: `dwg_switch_target` is currently annotated read-only and remains exposed in this mode. It changes the gateway binding but does not activate, save or close a drawing. `dwg_open_drawing` is excluded.
- **Drawing Operations and Read-Only**: The `drawing` toolset retains `dwg_get_variables` in read-only mode, but strips `dwg_set_system_variable`, `dwg_save_drawing`, and `dwg_purge_drawing`.
- **Deferred Angular Dimensions**: The `dimension` toolset only registers linear, aligned, radial, and diametric dimension creators. Angular dimensions are deferred and not included in this release.
- **Deferred File Export Tools**: The `dwg_export_pdf` and `dwg_export_image` tools have been deferred to ensure absolute reliability of drawing view captures and plot configurations. `dwg_capture_view_image` is fully enabled by default.

The catalog is resolved from `Program.ToolRegistrations`, the selected toolsets, and per-method policy. `ServerState.EnabledToolTypes` is assigned before the plugin catalog is built. Use the configured gateway's `tools/list` for its actual exposed surface; `dwg_run_lisp`, when listed, only returns a compatibility refusal.

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

ToolBaker storage is server-owned SQLite at `%LOCALAPPDATA%\Bimwright\dwg-mcp\baked\bake.db`. Accepting a suggestion sends the internal `apply_bake` command to the plugin for policy validation and schema smoke-test. The server redacts baked source before persistence and records usage events in the `usage_events` table for pattern detection.

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
