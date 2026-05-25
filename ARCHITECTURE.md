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

**Server** is an MCP server. It talks stdio to the client, translates each tool call into a JSON envelope, and forwards it over localhost transport to the plugin. Server is a plain .NET 8 global tool with no AutoCAD reference. The default server registers query, modify, and routing tools. `dwg_send_code` lives in a separate `CodeTools` surface and is registered only when the process starts with `--enable-send-code` or `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`.

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
  "target": "2024",
  "version": "2024",
  "transport": "tcp",
  "host": "127.0.0.1",
  "port": 49152,
  "auth_token": "32-char hex token",
  "pid": 1234,
  "process_name": "acad",
  "started_at_utc": "2026-05-25T00:00:00Z"
}
```

Server reads v2 files from `%LOCALAPPDATA%\Bimwright\Dwg\`, verifies the PID is alive, removes invalid stale files, and auto-selects the newest discovered target unless `--target`, `BIMWRIGHT_DWG_TARGET`, or `dwg_switch_target` pins a year. Target values are always 4-digit years: `2022` through `2027`.

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

`CommandDispatcher` uses an explicit dictionary (not reflection). `send_code` remains in the dispatch table so opt-in calls can be handled, but dispatch rejects it unless `MCPENABLECODE` has enabled the current plugin session:

```csharp
_commands = new Dictionary<string, IAcadCommand>
{
    { "get_selected_texts",      new GetSelectedTextsHandler() },
    { "update_texts",            new UpdateTextsHandler() },
    { "send_code",               new SendCodeHandler() },
    { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
    { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
    { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
    { "list_baked_tools",        new ListBakedToolsHandler() },
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
| `query` | `dwg_get_selected_texts` |
| `modify` | `dwg_update_texts`, `dwg_translate_and_rewrite`, `dwg_apply_unicode_style`, `dwg_collapse_and_rewrite` |
| `meta` | `dwg_list_available_targets`, `dwg_get_current_target`, `dwg_switch_target` |
| `toolbaker` | `dwg_list_baked_tools`, `dwg_run_baked_tool`, `dwg_list_bake_suggestions`, `dwg_accept_bake_suggestion`, `dwg_dismiss_bake_suggestion` |
| `code` | `dwg_send_code` |

`--read-only` or `BIMWRIGHT_DWG_READ_ONLY=1` removes write-capable toolsets: `modify`, `toolbaker`, and `code`. `--enable-send-code` only registers `code`; AutoCAD-side `MCPENABLECODE` is still required.

## ToolBaker

ToolBaker storage is server-owned SQLite at `%LOCALAPPDATA%\Bimwright\Dwg\baked\bake.db`. Accepting a suggestion sends the internal `apply_bake` command to the plugin for policy validation and schema smoke-test. The server persists the accepted record only after plugin validation succeeds.

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
