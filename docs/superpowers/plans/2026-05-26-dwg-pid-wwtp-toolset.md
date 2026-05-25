# DWG P&ID WWTP Toolset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a default-off `pid` toolset for lightweight P&ID/WWTP drafting primitives without requiring CTO, LISP, ezdxf, or commercial symbol assets.

**Architecture:** Public MCP names use `dwg_pid_*`; internal wire commands use `pid_*`. Phase 1 is procedural-first and writes simple AutoCAD geometry. External CTO/custom DWG library support is reserved in config but not used by handlers in this plan.

**Tech Stack:** C#/.NET 8 server, AutoCAD plugin shells, AutoCAD .NET API, Newtonsoft.Json/JToken, xUnit.

---

## Source Facts

Puran Water P&ID source is useful for catalog names, layer names, and attribute schemas. Its live AutoCAD P&ID path depends on `pid_tools.lsp`, which is not present in the cloned repo. Its ezdxf backend uses procedural placeholders. Therefore this plan rewrites behavior natively in .NET and does not call LISP or require `C:\PIDv4-CTO`.

## Scope

Ship:

| MCP tool | Wire command | Toolset |
|---|---|---|
| `dwg_pid_setup_layers` | `pid_setup_layers` | `pid` |
| `dwg_pid_list_categories` | `pid_list_categories` | `pid` |
| `dwg_pid_list_symbols` | `pid_list_symbols` | `pid` |
| `dwg_pid_draw_pipe` | `pid_draw_pipe` | `pid` |
| `dwg_pid_insert_symbol` | `pid_insert_symbol` | `pid` |
| `dwg_pid_add_flow_arrow` | `pid_add_flow_arrow` | `pid` |
| `dwg_pid_add_equipment_tag` | `pid_add_equipment_tag` | `pid` |
| `dwg_pid_add_line_number` | `pid_add_line_number` | `pid` |

Defer: external DWG import, CTO path loading, valve/pump/tank/instrument-specific wrappers, equipment connection by ports, WWTP skids, treatment train generation, ISA validator, BOM/line-list export, compliance checks.

## File Structure

Create:
- `src/server/Tools/PidTools.cs`
- `src/shared/Pid/PidCatalog.cs`
- `src/shared/Pid/PidConfig.cs`
- `src/shared/Pid/PidLayerCatalog.cs`
- `src/shared/Pid/PidProceduralGeometry.cs`
- `src/shared/Handlers/Pid/PidSetupLayersHandler.cs`
- `src/shared/Handlers/Pid/PidListCategoriesHandler.cs`
- `src/shared/Handlers/Pid/PidListSymbolsHandler.cs`
- `src/shared/Handlers/Pid/PidDrawPipeHandler.cs`
- `src/shared/Handlers/Pid/PidInsertSymbolHandler.cs`
- `src/shared/Handlers/Pid/PidAddFlowArrowHandler.cs`
- `src/shared/Handlers/Pid/PidAddEquipmentTagHandler.cs`
- `src/shared/Handlers/Pid/PidAddLineNumberHandler.cs`
- `tests/Bimwright.Dwg.Tests/PidCatalogTests.cs`
- `tests/Bimwright.Dwg.Tests/PidConfigTests.cs`
- `tests/Bimwright.Dwg.Tests/PidSchemaTests.cs`

Modify:
- `src/server/ToolsetFilter.cs`
- `src/server/Program.cs`
- `src/shared/Infrastructure/CommandDispatcher.cs`
- `src/shared/Infrastructure/SchemaValidator.cs`
- `tests/Bimwright.Dwg.Tests/ToolsetFilterTests.cs`
- `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`
- `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`
- `README.md`
- `README.vi.md`
- `ARCHITECTURE.md`
- `CHANGELOG.md`

## Catalog And Config Constants

Toolset:

```csharp
public const string PidToolset = "pid";
```

Environment names reserved by `PidConfig`:

```csharp
public const string EnvPidLibraryPath = "BIMWRIGHT_DWG_PID_LIBRARY_PATH";
public const string EnvPidSymbolMode = "BIMWRIGHT_DWG_PID_SYMBOL_MODE";
public const string EnvPidFallback = "BIMWRIGHT_DWG_PID_FALLBACK";
```

Supported mode behavior in this plan:
- `procedural`: always use procedural symbols.
- `auto`: use procedural symbols.
- `external`: reject with a clear message that external symbol import is deferred.

Standard layers:
- `PID-EQUIPMENT`, ACI 6
- `PID-PROCESS-PIPING`, ACI 4
- `PID-UTILITY-PIPING`, ACI 3
- `PID-INSTRUMENTS`, ACI 5
- `PID-ELECTRICAL`, ACI 1
- `PID-ANNOTATION`, ACI 7
- `PID-VALVES`, ACI 2

WWTP layers:
- `PID-CHEMICAL-DOSING`, ACI 30
- `PID-AIR-DIFFUSION`, ACI 151
- `PID-SLUDGE`, ACI 34
- `PID-EFFLUENT`, ACI 130

Fallback categories:
- `ACTUATORS`
- `ANNOTATION`
- `EQUIPMENT`
- `PUMPS-BLOWERS`
- `TANKS`
- `VALVES`

## Task 1: Default-Off Toolset Gate

**Files:**
- Modify: `ToolsetFilter.cs`
- Modify: `Program.cs`
- Modify: `ToolsetFilterTests.cs`

- [ ] **Step 1: Write failing tests**

Assert:
- default toolsets exclude `pid`.
- explicit `Toolsets = ["pid"]` includes `pid`.
- `Toolsets = ["all"]` includes `pid`.
- read-only removes `pid` because every Plan 5 tool can write drawing state or expose future catalog paths.

- [ ] **Step 2: Implement toolset**

Add `pid` to `KnownToolsets`, not `DefaultOn`, and add it to write-capable set.

- [ ] **Step 3: Register wrapper class**

Add `PidTools` registration only when enabled and not read-only.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~ToolsetFilterTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src/server tests/Bimwright.Dwg.Tests/ToolsetFilterTests.cs
git commit -m "feat(pid): add default-off pid toolset"
```

## Task 2: Catalog And Config

**Files:**
- Create: `src/shared/Pid/PidCatalog.cs`
- Create: `src/shared/Pid/PidConfig.cs`
- Create: `src/shared/Pid/PidLayerCatalog.cs`
- Test: `PidCatalogTests.cs`
- Test: `PidConfigTests.cs`
- Modify test csproj to include AutoCAD-free Pid files.

- [ ] **Step 1: Write catalog tests**

Assert categories and known symbols:
- `PUMPS-BLOWERS` contains `PUMP-METERING`.
- `VALVES` contains `VA-KNIFEGATE`.
- `EQUIPMENT` contains `EQUIP-CLARIFIER`.
- `ANNOTATION` contains `ANNOT-FLOWARROW`.

- [ ] **Step 2: Write config tests**

Assert default mode is procedural and external mode is rejected by Plan 5 handlers.

- [ ] **Step 3: Implement catalog/config**

Keep all catalog data in code. Do not scan `C:\PIDv4-CTO` in this plan.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~PidCatalogTests|FullyQualifiedName~PidConfigTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src/shared/Pid tests/Bimwright.Dwg.Tests
git commit -m "feat(pid): add procedural catalog and config"
```

## Task 3: Wrappers And Schemas

**Files:**
- Create: `src/server/Tools/PidTools.cs`
- Modify: `SchemaValidator.cs`
- Modify: `ToolsListSnapshotTests.cs`
- Create: `PidSchemaTests.cs`

- [ ] **Step 1: Add failing schema and snapshot tests**

Add the eight scoped `dwg_pid_*` tool names and schemas.

- [ ] **Step 2: Implement wrappers**

Wrappers forward:
- `pid_setup_layers`
- `pid_list_categories`
- `pid_list_symbols`
- `pid_draw_pipe`
- `pid_insert_symbol`
- `pid_add_flow_arrow`
- `pid_add_equipment_tag`
- `pid_add_line_number`

- [ ] **Step 3: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~PidSchemaTests|FullyQualifiedName~ToolsListSnapshotTests"
```

- [ ] **Step 4: Commit**

```powershell
git add src/server/Tools/PidTools.cs src/shared/Infrastructure/SchemaValidator.cs tests/Bimwright.Dwg.Tests
git commit -m "feat(pid): expose procedural pid MCP wrappers"
```

## Task 4: Procedural Handlers

**Files:**
- Create: `PidProceduralGeometry.cs`
- Create all `src/shared/Handlers/Pid/*Handler.cs`
- Modify: `CommandDispatcher.cs`

- [ ] **Step 1: Implement layer setup**

Create standard layers and optional WWTP layers when `include_wwtp_layers=true`.

- [ ] **Step 2: Implement catalog handlers**

Return static categories and symbols from `PidCatalog`.

- [ ] **Step 3: Implement pipe and annotation geometry**

`pid_draw_pipe` creates a `Line` on the selected PID layer. `pid_add_flow_arrow` creates a triangular closed polyline. `pid_add_equipment_tag` and `pid_add_line_number` create `DBText` on `PID-ANNOTATION`.

- [ ] **Step 4: Implement procedural symbol insertion**

For Plan 5:
- pump: circle plus triangle marker.
- tank: rectangle.
- valve: diamond.
- generic equipment: rectangle plus symbol text.

Return `{ source="procedural", category, symbol, handles }`.

- [ ] **Step 5: Register handlers**

Add explicit `CommandDispatcher` entries for all `pid_*` wire commands.

- [ ] **Step 6: Verify build**

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 7: Commit**

```powershell
git add src/shared/Pid src/shared/Handlers/Pid src/shared/Infrastructure/CommandDispatcher.cs
git commit -m "feat(pid): implement procedural pid primitives"
```

## Task 5: Docs And Smoke

**Files:**
- Modify docs and changelog.

- [ ] **Step 1: Document opt-in**

Document `--toolsets query,modify,meta,pid` and `BIMWRIGHT_DWG_TOOLSETS=query,modify,meta,pid`. State that `pid` is default-off and procedural-first.

- [ ] **Step 2: Document exclusions**

State that CTO, `pid_tools.lsp`, external DWG import, and ISA-grade symbol replacement are not included in this plan.

- [ ] **Step 3: Manual smoke**

With AutoCAD open:
1. Start server with `BIMWRIGHT_DWG_TOOLSETS=query,modify,meta,pid`.
2. Run `dwg_pid_setup_layers`.
3. Run `dwg_pid_list_categories`.
4. Run `dwg_pid_list_symbols` for `PUMPS-BLOWERS`.
5. Run `dwg_pid_draw_pipe`.
6. Run `dwg_pid_insert_symbol` with `category="PUMPS-BLOWERS"` and `symbol="PUMP-METERING"`.
7. Run `dwg_pid_add_flow_arrow`.
8. Run `dwg_pid_add_equipment_tag`.
9. Run `dwg_pid_add_line_number`.

- [ ] **Step 4: Final verification**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
rg -n "PIDv4|pid_tools|ezdxf|SendStringToExecute" src tests
git diff --check
```

Expected: references to `PIDv4` appear only in docs/config comments if added; no runtime dependency on `pid_tools`, `ezdxf`, or `SendStringToExecute`.

- [ ] **Step 5: Commit**

```powershell
git add README.md README.vi.md ARCHITECTURE.md CHANGELOG.md
git commit -m "docs(pid): document procedural WWTP P&ID toolset"
```
