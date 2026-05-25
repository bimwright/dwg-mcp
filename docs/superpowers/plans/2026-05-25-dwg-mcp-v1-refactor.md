# dwg-mcp v1 Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `dwg-mcp` from the current AutoCAD 2024-only v0.1 shape into a v1 architecture with prefixed tools, discoverable instructions, config/toolset gating, JSON discovery, serialized AutoCAD API access, multi-version scaffolding, and ToolBaker parity.

**Architecture:** Keep the six production text tools and their `Rewriting/`, `Unicode/`, and `Clustering/` logic stable. Build the new skeleton around them in small vertical slices: server config and tool exposure first, transport/discovery second, plugin execution safety third, then batch, multi-version packaging, and ToolBaker. Every AutoCAD API call goes through `DwgApiExecutor` before `Document.LockDocument()`.

**Tech Stack:** C#/.NET 8 server, AutoCAD plugin shells (`net48`, `net8.0-windows`, `net10.0-windows`), ModelContextProtocol 1.1.0, Newtonsoft.Json, xUnit, Microsoft.Data.Sqlite, Roslyn.

---

## Baseline

Worktree: `C:\Users\Admin\.config\superpowers\worktrees\dwg-mcp\refactor-v1.0`

Branch: `refactor/v1.0`

Baseline verification already run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
# Expected: Passed! Failed: 0, Passed: 93, Skipped: 0, Total: 93

dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
# Expected: Build succeeded. 0 Warning(s), 0 Error(s)
```

Do not run build/test commands in parallel in the same worktree. MSBuild node reuse can lock `src\plugin-acad24\obj\Debug\net48\Bimwright.Dwg.Plugin.Acad24.dll`. If that happens, run:

```powershell
dotnet build-server shutdown
```

## File Structure

Create:
- `docs/superpowers/plans/2026-05-25-dwg-mcp-v1-refactor.md` - this plan.
- `src/server/DwgMcpConfig.cs` - server/shared config model loaded from JSON, env, and CLI.
- `src/server/ServerState.cs` - current config and read-only guard helper.
- `src/server/ToolsetFilter.cs` - known/default/write-capable toolset resolver.
- `src/server/Tools/QueryTools.cs` - `dwg_get_selected_texts`.
- `src/server/Tools/ModifyTools.cs` - four write text tools.
- `src/server/Tools/MetaTools.cs` - target routing and batch wrappers.
- `src/server/Tools/CodeTools.cs` - opt-in `dwg_send_code`.
- `src/server/Tools/ToolBakerTools.cs` - ToolBaker MCP wrappers.
- `src/server/AuthToken.cs` - server-side JSON discovery scanner.
- `src/shared/Infrastructure/DwgApiExecutor.cs` - serialized AutoCAD API gate.
- `src/shared/Infrastructure/SchemaValidator.cs` - NJsonSchema validation helper.
- `src/shared/Infrastructure/ResponseSizeGuard.cs` - response truncation helper.
- `src/shared/Infrastructure/BatchExecutor.cs` - AutoCAD-free batch iteration logic.
- `src/shared/Security/ErrorSanitizer.cs` - path/stack trace sanitizer.
- `src/shared/Security/SecretMasker.cs` - log secret masking.
- `src/shared/Security/McpResponsePrivacy.cs` - per-tool response filtering.
- `src/shared/Transport/ITransportServer.cs` - common transport interface.
- `src/shared/Transport/TcpTransportServer.cs` - replacement for `SocketServer`.
- `src/shared/Transport/PipeTransportServer.cs` - named pipe transport for 2025+.

Modify:
- `src/server/Program.cs` - config load, ServerInstructions, toolset registration.
- `src/server/PluginClient.cs` - JSON discovery, TCP/pipe connection, target resolution.
- `src/server/Bimwright.Dwg.Server.csproj` - source layout and dependencies.
- `src/shared/Infrastructure/DocumentInvoker.cs` - route through `DwgApiExecutor`.
- `src/shared/Infrastructure/CommandDispatcher.cs` - schema validation, baked command hooks, response guards.
- `src/shared/Infrastructure/IAcadCommand.cs` - add description/schema contract if missing.
- `src/shared/Infrastructure/SocketServer.cs` - remove after `TcpTransportServer` lands.
- `src/plugin-acad24/App.cs` - transport startup, discovery v2, command registration.
- `src/plugin-acad24/Bimwright.Dwg.Plugin.Acad24.csproj` - constants/dependencies.
- `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj` - compile-include new AutoCAD-free shared modules.

Later create/modify:
- `src/plugin-acad22/`, `src/plugin-acad23/`, `src/plugin-acad25/`, `src/plugin-acad26/`, `src/plugin-acad27/`.
- `src/server/Bake/*`, `src/shared/ToolBaker/*`, `src/shared/Views/BakeInboxWindow.cs`.
- `scripts/PackageContents.xml`, `scripts/install.ps1`, `.github/workflows/build.yml`, `README.md`, `README.vi.md`, `ARCHITECTURE.md`, `CHANGELOG.md`.

## Task 1: Server Config, Instructions, Toolsets, and Prefixed Wrappers

**Files:**
- Create: `src/server/DwgMcpConfig.cs`
- Create: `src/server/ServerState.cs`
- Create: `src/server/ToolsetFilter.cs`
- Create: `src/server/Tools/QueryTools.cs`
- Create: `src/server/Tools/ModifyTools.cs`
- Create: `src/server/Tools/MetaTools.cs`
- Create: `src/server/Tools/CodeTools.cs`
- Modify: `src/server/Program.cs`
- Modify: `src/server/Tools.cs`
- Modify: `src/server/CodeTools.cs`
- Test: `tests/Bimwright.Dwg.Tests/DwgMcpConfigTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/ToolsetFilterTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`

- [x] **Step 1: Write config tests**

Create `tests\Bimwright.Dwg.Tests\DwgMcpConfigTests.cs` with tests for JSON < env < CLI, boolean env parsing, CSV toolsets, `--config`, and nullable fields. Use temporary files under `Path.GetTempPath()`.

- [x] **Step 2: Write toolset tests**

Create `tests\Bimwright.Dwg.Tests\ToolsetFilterTests.cs` proving the first safe wave defaults include only currently backed `query` and `modify`; `code` is off unless enabled; `meta` and `toolbaker` stay out of the default surface until their backing commands land; `--read-only` strips write-capable toolsets and keeps read/list tools.

- [x] **Step 3: Write wrapper snapshot test**

Create `tests\Bimwright.Dwg.Tests\ToolsListSnapshotTests.cs` that reflects `[McpServerTool]` methods and asserts MCP-facing names include `dwg_` prefixes and no legacy unprefixed public names.

- [x] **Step 4: Verify RED**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~DwgMcpConfigTests|FullyQualifiedName~ToolsetFilterTests|FullyQualifiedName~ToolsListSnapshotTests"
```

Expected: fail because `DwgMcpConfig`, `ToolsetFilter`, and prefixed wrapper classes do not exist yet.

- [x] **Step 5: Implement config and toolset resolver**

Implement `DwgMcpConfig`, `ServerState`, and `ToolsetFilter` in `src/server`. Keep parser hand-rolled and dependency-free. Env vars are `BIMWRIGHT_DWG_TARGET`, `BIMWRIGHT_DWG_TOOLSETS`, `BIMWRIGHT_DWG_READ_ONLY`, `BIMWRIGHT_DWG_ENABLE_SEND_CODE`, `BIMWRIGHT_DWG_ENABLE_TOOLBAKER`, `BIMWRIGHT_DWG_LOG_LEVEL`.

- [x] **Step 6: Split wrapper classes and configure ServerInstructions**

Move current wrapper methods from `Tools.cs` into `src/server/Tools/*.cs` with MCP names `dwg_*` and wire names unchanged. Keep `Tools.LoggedCall` or equivalent gateway shared. Add `ConfigureMcpServerOptions` with keyword-dense `ServerInstructions` under 2 KB.

- [x] **Step 7: Verify GREEN**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
```

Expected: 93 existing tests plus new tests pass; solution builds with 0 errors.

- [x] **Step 8: Commit**

```powershell
git add src/server tests/Bimwright.Dwg.Tests
git commit -m "feat(server): add v1 config and prefixed toolsets"
```

## Task 2: Discovery v2, Target Routing, and PluginClient

**Files:**
- Create: `src/server/AuthToken.cs`
- Modify: `src/server/PluginClient.cs`
- Modify: `src/server/Tools/MetaTools.cs`
- Test: `tests/Bimwright.Dwg.Tests/DiscoveryFileTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/PluginClientTests.cs`

- [x] **Step 1: Write discovery tests**

Cover parsing `%LOCALAPPDATA%\Bimwright\Dwg\acad-YYYY.json`, stale PID cleanup, invalid target rejection, priority `2027 > 2026 > 2025 > 2024 > 2023 > 2022`, and target pinning.

- [x] **Step 2: Write PluginClient tests**

Extend fake TCP plugin tests to use JSON discovery with `auth_token`. Add pipe path parsing tests without requiring a real AutoCAD process.

- [x] **Step 3: Verify RED**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~DiscoveryFileTests|FullyQualifiedName~PluginClientTests"
```

Expected: fail on missing JSON discovery support.

- [x] **Step 4: Implement discovery scanner and target routing**

Add server-side discovery types, `ListAvailable`, `TryReadTcp`, `TryReadPipe`, `CleanupLegacyDiscoveryFiles`, `Target`, and clear educational errors for non-4-digit targets.

- [x] **Step 5: Implement PluginClient TCP/pipe envelope**

Keep per-request connection semantics for `dwg-mcp`; read discovery on every call; send `{ id, command, params, auth }`. Support TCP first in tests; add pipe implementation behind code path that can be unit-tested with a named pipe fake later.

- [x] **Step 6: Verify GREEN**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
```

- [x] **Step 7: Commit**

```powershell
git add src/server tests/Bimwright.Dwg.Tests
git commit -m "feat(server): add discovery v2 and target routing"
```

## Task 3: Plugin Transport and Serialized AutoCAD API Executor

**Files:**
- Create: `src/shared/Infrastructure/DwgApiExecutor.cs`
- Create: `src/shared/Transport/ITransportServer.cs`
- Create: `src/shared/Transport/TcpTransportServer.cs`
- Create: `src/shared/Transport/PipeTransportServer.cs`
- Modify: `src/shared/Infrastructure/DocumentInvoker.cs`
- Modify: `src/shared/Infrastructure/CommandDispatcher.cs`
- Modify: `src/plugin-acad24/App.cs`
- Test: `tests/Bimwright.Dwg.Tests/DwgApiExecutorTests.cs`

- [x] **Step 1: Write executor tests**

Use async delegates with counters to prove max concurrency is 1, exceptions release the gate, and FIFO order is preserved for queued work.

- [x] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~DwgApiExecutorTests"
```

Expected: fail because `DwgApiExecutor` does not exist.

- [x] **Step 3: Implement DwgApiExecutor and wire DocumentInvoker**

Keep the executor AutoCAD-free. `DocumentInvoker.Invoke` calls `DwgApiExecutor.Invoke` before reading `Application.DocumentManager.MdiActiveDocument`.

- [x] **Step 4: Replace SocketServer with transport interface**

Port the existing TCP behavior into `TcpTransportServer`, write JSON discovery v2 to `%LOCALAPPDATA%\Bimwright\Dwg\acad-2024.json`, preserve `MCPSTART`, `MCPSTOP`, `MCPENABLECODE`, and `MCPDISABLECODE`.

- [x] **Step 5: Verify GREEN**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
```

- [x] **Step 6: Commit**

```powershell
git add src/shared src/plugin-acad24 tests/Bimwright.Dwg.Tests
git commit -m "feat(plugin): serialize AutoCAD API execution"
```

## Task 4: Schema Validation, Security Filters, and Batch Skeleton

**Files:**
- Create: `src/shared/Infrastructure/SchemaValidator.cs`
- Create: `src/shared/Infrastructure/ResponseSizeGuard.cs`
- Create: `src/shared/Infrastructure/BatchExecutor.cs`
- Create: `src/shared/Security/ErrorSanitizer.cs`
- Create: `src/shared/Security/SecretMasker.cs`
- Create: `src/shared/Security/McpResponsePrivacy.cs`
- Create: `src/shared/Handlers/BatchExecuteHandler.cs`
- Modify: `src/shared/Infrastructure/IAcadCommand.cs`
- Modify: `src/shared/Infrastructure/CommandDispatcher.cs`
- Test: `tests/Bimwright.Dwg.Tests/SchemaValidatorTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/ResponseSizeGuardTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/BatchExecutorTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/ErrorSanitizerTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/SecretMaskerTests.cs`

- [x] **Step 1: Write tests first**

Tests must cover missing required schema fields, invalid types, oversized response truncation, secret/path masking, nested batch rejection, `run_baked_tool` rejection inside batch, and partial failure detection.

- [x] **Step 2: Verify RED**

Run the new test filters and confirm missing types/functions fail.

- [x] **Step 3: Implement helpers and dispatcher integration**

Use NJsonSchema only if it restores cleanly for both server and plugin TFMs. If NJsonSchema causes net48/package trouble, use Newtonsoft-based minimal validation for v1 fields and document the deviation in this plan before committing.

Implementation note: this task uses a dependency-free Newtonsoft-based `CommandSchema` validator instead of adding NJsonSchema. It covers v1 required fields and primitive JSON token types while avoiding new net48 package risk.

- [x] **Step 4: Implement logical batch without hard-coded undo API**

Implement `BatchExecutor.Run` and `BatchExecuteHandler`. Keep AutoCAD undo grouping behind `AutoCadUndoGroup.TryBegin(doc)` only after a compile spike. If the spike is not proven in this task, ship logical batch and explicit non-rollback message.

- [x] **Step 5: Verify GREEN and commit**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
git add src/shared tests/Bimwright.Dwg.Tests
git commit -m "feat(plugin): add validation security and batch skeleton"
```

## Task 5: Multi-Version Plugin Shells and Packaging Scaffolding

**Files:**
- Create: `src/plugin-acad22/Bimwright.Dwg.Plugin.Acad22.csproj`
- Create: `src/plugin-acad23/Bimwright.Dwg.Plugin.Acad23.csproj`
- Create: `src/plugin-acad25/Bimwright.Dwg.Plugin.Acad25.csproj`
- Create: `src/plugin-acad26/Bimwright.Dwg.Plugin.Acad26.csproj`
- Create: `src/plugin-acad27/Bimwright.Dwg.Plugin.Acad27.csproj`
- Modify: `src/Bimwright.Dwg.sln`
- Modify: `scripts/PackageContents.xml`
- Modify: `scripts/install.ps1`
- Modify: `.github/workflows/build.yml`

- [x] **Step 1: Add shell csprojs with explicit ref properties**

Use `AutoCad2022Dir`, `AutoCad2023Dir`, `AutoCad2024Dir`, `AutoCad2025Dir`, `AutoCad2026Dir`, `AutoCad2027Dir`. Do not commit Autodesk DLLs. Use `net48` for 2022-2024, `net8.0-windows` for 2025-2026, `net10.0-windows` for 2027.

- [x] **Step 2: Add solution entries and CI skip logic**

Hosted CI may skip plugin jobs if refs are missing, but release packaging must require prepared-machine artifacts.

- [x] **Step 3: Verify available local shell**

Run:

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug /nr:false
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
```

- [x] **Step 4: Commit**

```powershell
git add src scripts .github
git commit -m "feat(plugin): scaffold AutoCAD 2022-2027 shells"
```

## Task 6: ToolBaker Storage, Runtime, and MCP Flow

**Files:**
- Create: `src/server/Bake/BakeDb.cs`
- Create: `src/server/Bake/BakePaths.cs`
- Create: `src/server/Bake/ClusterEngine.cs`
- Create: `src/server/Bake/SuggestionProposer.cs`
- Create: `src/server/Bake/ToolBakerAuditLog.cs`
- Create: `src/server/Bake/UsageEvent.cs`
- Create: `src/server/Bake/UsageEventLogger.cs`
- Create: `src/shared/ToolBaker/BakedToolRegistry.cs`
- Create: `src/shared/ToolBaker/BakedToolRecord.cs`
- Create: `src/shared/ToolBaker/BakedToolRuntimeCache.cs`
- Create: `src/shared/ToolBaker/BakedToolRuntimeSource.cs`
- Create: `src/shared/ToolBaker/BakedToolRuntimeCommandFactory.cs`
- Create: `src/shared/ToolBaker/ToolCompiler.cs`
- Create: `src/shared/ToolBaker/BakeCompilerPolicy.cs`
- Create: `src/shared/ToolBaker/BakedToolParameterDefaults.cs`
- Create: `src/shared/ToolBaker/BakedToolDispatchAuthorizer.cs`
- Create: `src/shared/Handlers/ListBakedToolsHandler.cs`
- Create: `src/shared/Handlers/RunBakedToolHandler.cs`
- Create: `src/shared/Handlers/ApplyBakeSuggestionHandler.cs`
- Modify: `src/server/Tools/ToolBakerTools.cs`
- Modify: `src/shared/Infrastructure/CommandDispatcher.cs`
- Test: ToolBaker unit tests matching rvt-mcp coverage with AutoCAD names.

- [ ] **Step 1: Port tests first**

Create tests for `BakeDb`, `ClusterEngine`, `SuggestionProposer`, `BakeCompilerPolicy`, `BakedToolRegistry`, `BakedToolRuntimeCache`, `BakedToolDispatchAuthorizer`, and `AcceptBakeSuggestionApplyFlow`. Replace `IRevitCommand` expectations with `IAcadCommand`.

- [ ] **Step 2: Verify RED**

Run ToolBaker test filters and confirm missing types fail.

- [ ] **Step 3: Port server-side bake storage and suggestions**

Use server-owned SQLite under `%LOCALAPPDATA%\Bimwright\Dwg\baked\bake.db`. Do not include `LegacyBakedToolImporter`.

- [ ] **Step 4: Port plugin runtime cache/compiler/policy**

Generated tools implement `IAcadCommand` and execute under `DwgApiExecutor`. Policy must block `System.IO`, `System.Net`, `System.Diagnostics.Process`, reflection escapes, and `Bimwright.Dwg.Plugin.ToolBaker` access.

- [ ] **Step 5: Implement internal apply_bake flow**

`dwg_accept_bake_suggestion` prepares a request and sends wire command `apply_bake`; plugin compiles, smoke-tests, registers, and persists only after success.

- [ ] **Step 6: Verify GREEN and commit**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
git add src/server src/shared tests/Bimwright.Dwg.Tests
git commit -m "feat(toolbaker): add accepted tool flow"
```

## Task 7: Documentation, Migration, and Release Gate

**Files:**
- Modify: `README.md`
- Modify: `README.vi.md`
- Modify: `ARCHITECTURE.md`
- Modify: `CHANGELOG.md`
- Modify: `server.json`
- Modify: `.mcp.json.example`
- Create: `docs/testing/fresh-install-checklist.md`

- [ ] **Step 1: Update docs for breaking tool names**

Document old-to-new `dwg_*` mapping, migration steps, discovery v2, multi-target routing, send_code dual gate, read-only mode, and ToolBaker.

- [ ] **Step 2: Update install and verification docs**

Make it explicit that server/tests can pass without all plugin shells being release-built; release requires prepared machine artifacts for the shipped AutoCAD years.

- [ ] **Step 3: Final verification**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
dotnet pack src\server\Bimwright.Dwg.Server.csproj -c Release --output artifacts-review
rg -n "get_selected_texts|translate_and_rewrite|send_code" README.md ARCHITECTURE.md CHANGELOG.md server.json .mcp.json.example
```

Expected: tests pass, build succeeds, pack succeeds, docs mention prefixed tool names and migration table.

- [ ] **Step 4: Commit**

```powershell
git add README.md README.vi.md ARCHITECTURE.md CHANGELOG.md server.json .mcp.json.example docs
git commit -m "docs: document dwg-mcp v1 migration"
```

## Final Gate

- [ ] `git status --short` is clean.
- [ ] `dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug` passes.
- [ ] `dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false` passes or documents missing prepared refs for unavailable shells.
- [ ] `dotnet pack src\server\Bimwright.Dwg.Server.csproj -c Release --output artifacts-review` passes.
- [ ] Current branch contains ordered commits for Tasks 1-7.
- [ ] Manual AutoCAD smoke remains explicitly listed if no live AutoCAD 2025-2027 instances are available.
