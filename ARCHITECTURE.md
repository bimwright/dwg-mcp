# Architecture

## Two processes, one pipe

```
MCP client (Claude Code / Cursor / OpenCode / ...)
        |  stdio (NDJSON MCP)
        v
Bimwright.Dwg.Server  (.NET 8 console app, global tool)
        |  TCP NDJSON + token auth (127.0.0.1)
        v
Bimwright.Dwg.Plugin  (AutoCAD 2024 add-in, .NET 4.8)
        |  Document.LockDocument()
        v
AutoCAD .NET API  (ObjectARX 2024)
```

**Server** is an MCP server. It talks stdio to the client, translates each tool call into a JSON envelope, and forwards it over TCP to the plugin. Server is a plain .NET 8 global tool — no GUI, no AutoCAD reference.

**Plugin** is an `IExtensionApplication` loaded by AutoCAD. It runs a TCP listener on a background thread, dispatches requests, locks the document, and executes commands within transactions. Unlike Revit, AutoCAD allows `Document.LockDocument()` from background threads, so there is no `ExternalEvent` marshalling.

## Discovery

Plugin writes a discovery file on startup:

| AutoCAD | Discovery file | Transport |
|---------|----------------|-----------|
| 2024    | `portAcad24.txt` | TCP |

Path: `%LOCALAPPDATA%\Bimwright\portAcad24.txt`

File has three lines:

```
<tcp-port>       (int, OS-assigned ephemeral port)
<auth-token>     (32-char hex, Guid.NewGuid().ToString("N"))
<pid>            (int, AutoCAD process ID)
```

Server reads this on connect, verifies the PID is alive, and auto-deletes orphan files.

## Auth protocol

1. Plugin generates a new auth token (GUID) each time the listener starts.
2. Token is written to the discovery file.
3. Every TCP request envelope includes `"auth": "<token>"`.
4. Plugin rejects requests with missing or wrong token: `{ok:false, error:"unauthorized"}`.
5. Server reads token from discovery file before each connection.

## Request lifecycle

1. MCP client sends `tools/call` over stdio.
2. Server's `Tools` class receives the call via `[McpServerTool]` attribute.
3. `LoggedCall` wrapper logs start, creates request envelope with auth token.
4. `PluginClient.SendAsync` opens a new TCP connection to plugin.
5. Plugin's listener thread reads the NDJSON line, `CommandDispatcher.Dispatch` is called:
   - Auth token verified.
   - Handler looked up by command name.
   - `DocumentInvoker.Invoke` locks the active document.
   - Handler executes within a Transaction.
   - Response serialized as JSON.
6. Response travels back over TCP.
7. `LoggedCall` logs finish (duration, success/error).

Timeout: 30s per request on the server side. Connection-per-call (no persistent connection).

## Threading model

```
Background TCP listener thread
    |
    v (new thread per client connection)
Client handler thread
    |
    v Document.LockDocument()
    |
    v Transaction { handler.Execute() }
    |
    v Response written, connection closed
```

AutoCAD allows multiple threads to lock the same document sequentially. Each request gets its own lock scope and transaction. No queue, no ExternalEvent — simpler than Revit.

## Handler dispatch

`CommandDispatcher` uses an explicit dictionary (not reflection):

```csharp
_commands = new Dictionary<string, IAcadCommand>
{
    { "get_selected_texts",      new GetSelectedTextsHandler() },
    { "update_texts",            new UpdateTextsHandler() },
    { "send_code",               new SendCodeHandler() },
    { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
    { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
    { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
};
```

## Multi-version future path

When adding AutoCAD 2025+ support:

- Add `src/plugin-acad25/` (.NET 8) and `src/plugin-acad26/` (.NET 8)
- Transport abstraction: `ITransportServer` (TCP vs Named Pipe)
- Discovery files: `portAcad24.txt`, `pipeAcad25.txt`, etc.
- `#if` fences for API differences (if any)
- CI matrix expansion

The `src/shared/` glob pattern ensures all shells compile the same handlers.
