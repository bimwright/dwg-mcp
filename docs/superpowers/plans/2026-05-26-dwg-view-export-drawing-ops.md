# DWG View Export Drawing Ops Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add conservative view navigation, guarded export/capture, and drawing operations after core CAD creation and inspection tools are stable.

**Architecture:** Add explicit toolsets `view`, `export`, and `drawing`. Keep zoom/read-only tools separate from file-output and drawing-write tools so `--read-only` has a predictable surface. No raw AutoCAD command execution is introduced in this plan.

**Tech Stack:** C#/.NET 8 server, AutoCAD plugin shells, AutoCAD .NET API, P/Invoke only for `capture_view` if it compiles cleanly, Newtonsoft.Json/JToken, xUnit.

---

## Scope

Ship:

| MCP tool | Wire command | Toolset | Read-only |
|---|---|---|---|
| `dwg_zoom_extents` | `zoom_extents` | `view` | yes |
| `dwg_zoom_window` | `zoom_window` | `view` | yes |
| `dwg_zoom_to_entity` | `zoom_to_entity` | `view` | yes |
| `dwg_capture_view` | `capture_view` | `view` | no |
| `dwg_export_pdf` | `export_pdf` | `export` | no |
| `dwg_export_dxf` | `export_dxf` | `export` | no |
| `dwg_export_image` | `export_image` | `export` | no |
| `dwg_get_variables` | `get_variables` | `drawing` | yes |
| `dwg_set_system_variable` | `set_system_variable` | `drawing` | no |
| `dwg_save_drawing` | `save_drawing` | `drawing` | no |
| `dwg_purge_drawing` | `purge_drawing` | `drawing` | no |

Defer: `export_dwg`, `undo`, `trim`, `extend`, `fillet`, `chamfer`, and raw `send_command`.

## File Structure

Create:
- `src/server/Tools/ViewTools.cs`
- `src/server/Tools/ViewOutputTools.cs`
- `src/server/Tools/ExportTools.cs`
- `src/server/Tools/DrawingTools.cs`
- `src/server/Tools/DrawingWriteTools.cs`
- `src/shared/View/ViewZoomService.cs`
- `src/shared/View/ViewCaptureService.cs`
- `src/shared/Export/ExportPathPolicy.cs`
- `src/shared/Export/PdfExportService.cs`
- `src/shared/Export/DxfExportService.cs`
- `src/shared/Export/ImageExportService.cs`
- `src/shared/Drawing/SystemVariableCatalog.cs`
- `src/shared/Drawing/DrawingSaveService.cs`
- `src/shared/Drawing/PurgeDrawingService.cs`
- handlers under `src/shared/Handlers/View/`, `Export/`, and `Drawing/`
- `tests/Bimwright.Dwg.Tests/ViewToolsTests.cs`
- `tests/Bimwright.Dwg.Tests/ExportPathPolicyTests.cs`
- `tests/Bimwright.Dwg.Tests/DrawingVariableCatalogTests.cs`
- `tests/Bimwright.Dwg.Tests/DrawingOpsSchemaTests.cs`

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

## Task 1: Toolset Contract

**Files:**
- Modify toolset, snapshot, schema tests.
- Modify server registration.

- [ ] **Step 1: Write failing tests**

Test:
- `view` is known and default-on.
- `export` and `drawing` are known but default-off.
- read-only keeps zoom tools and `dwg_get_variables`.
- read-only strips `capture_view`, export tools, save, purge, and set variable.

- [ ] **Step 2: Implement tool classes**

Register:

```csharp
if (enabled.Contains("view"))
{
    mcp = mcp.WithTools<ViewTools>();
    if (!ServerState.IsReadOnly) mcp = mcp.WithTools<ViewOutputTools>();
}
if (enabled.Contains("export") && !ServerState.IsReadOnly) mcp = mcp.WithTools<ExportTools>();
if (enabled.Contains("drawing"))
{
    mcp = mcp.WithTools<DrawingTools>();
    if (!ServerState.IsReadOnly) mcp = mcp.WithTools<DrawingWriteTools>();
}
```

- [ ] **Step 3: Add schemas**

Add schemas for all scoped wire commands.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~ToolsetFilterTests|FullyQualifiedName~ToolsListSnapshotTests|FullyQualifiedName~DrawingOpsSchemaTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src/server src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(server): add view export drawing tool contracts"
```

## Task 2: View Navigation

**Files:**
- Create: `src/shared/View/ViewZoomService.cs`
- Create view handlers.

- [ ] **Step 1: Implement zoom service**

Use `Editor.GetCurrentView` and `Editor.SetCurrentView`. Compute center/height/width from drawing extents, window points, or entity geometric extents.

- [ ] **Step 2: Implement handlers**

Handlers return `{ center, width, height, target }`. `zoom_to_entity` resolves hex handle with `CadHandleResolver`.

- [ ] **Step 3: Verify plugin build**

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 4: Commit**

```powershell
git add src/shared/View src/shared/Handlers/View src/shared/Infrastructure/CommandDispatcher.cs
git commit -m "feat(view): add viewport navigation tools"
```

## Task 3: Guarded File Output

**Files:**
- Create: `src/shared/Export/ExportPathPolicy.cs`
- Create export/capture services and handlers.
- Test: `ExportPathPolicyTests.cs`

- [ ] **Step 1: Write path policy tests**

Rules:
- output path must be absolute.
- extension must match the tool.
- existing file requires `overwrite_existing=true`.
- repo-root output is rejected unless `allow_repo_output=true`.
- return normalized path.

- [ ] **Step 2: Implement path policy**

Use `Path.GetFullPath`, extension allowlist, and repo root detection from `AppContext.BaseDirectory` walking only when available.

- [ ] **Step 3: Implement export services**

Implement `export_dxf` using `Database.DxfOut`. Implement `export_pdf` with AutoCAD plot APIs. Implement `export_image` only if a stable AutoCAD API path compiles; otherwise omit it from registration and snapshot in the same commit.

- [ ] **Step 4: Implement capture**

Use `Application.MainWindow.Handle` and `PrintWindow` only if it compiles in the target shell. If compile fails, remove `dwg_capture_view` from this plan's scope and document deferral; do not use `SendStringToExecute`.

- [ ] **Step 5: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~ExportPathPolicyTests|FullyQualifiedName~ToolsListSnapshotTests"
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 6: Commit**

```powershell
git add src/server/Tools src/shared/Export src/shared/View src/shared/Handlers src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(export): add guarded file output tools"
```

## Task 4: Drawing Variables Save And Purge

**Files:**
- Create: `src/shared/Drawing/SystemVariableCatalog.cs`
- Create: `src/shared/Drawing/DrawingSaveService.cs`
- Create: `src/shared/Drawing/PurgeDrawingService.cs`
- Create drawing handlers.
- Test: `DrawingVariableCatalogTests.cs`

- [ ] **Step 1: Write variable catalog tests**

Read allowlist includes `CLAYER`, `INSUNITS`, `LUNITS`, `DIMSCALE`, `TEXTSIZE`, `OSMODE`, `ORTHOMODE`. Write allowlist is smaller: `CLAYER`, `DIMSCALE`, `TEXTSIZE`, `OSMODE`, `ORTHOMODE`.

- [ ] **Step 2: Implement get/set variables**

Use typed coercion based on allowlist metadata. Reject unknown names.

- [ ] **Step 3: Implement save and purge**

`save_drawing` requires `confirm=true` when saving to current file. `purge_drawing` supports `dry_run=true`; actual purge requires `confirm=true`.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~DrawingVariableCatalogTests|FullyQualifiedName~DrawingOpsSchemaTests"
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 5: Commit**

```powershell
git add src/shared/Drawing src/shared/Handlers/Drawing src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(drawing): add guarded drawing operations"
```

## Task 5: Docs And Smoke Gate

**Files:**
- Modify docs and changelog.

- [ ] **Step 1: Document output policy**

Document absolute path requirement, no overwrite default, and no repo-root writes by default.

- [ ] **Step 2: Manual AutoCAD smoke**

Use a copied throwaway DWG:
1. `dwg_zoom_extents`
2. `dwg_zoom_window`
3. `dwg_zoom_to_entity`
4. `dwg_get_variables`
5. `dwg_capture_view` to `%LOCALAPPDATA%\Bimwright\Dwg\smoke\capture.png` if shipped
6. `dwg_export_dxf` to `%LOCALAPPDATA%\Bimwright\Dwg\smoke\sample.dxf`
7. `dwg_export_pdf` to `%LOCALAPPDATA%\Bimwright\Dwg\smoke\sample.pdf`
8. `dwg_purge_drawing` with `dry_run=true`
9. actual purge only on the copied file with `confirm=true`
10. `dwg_save_drawing` only on the copied file with `confirm=true`

- [ ] **Step 3: Final verification**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
rg -n "send_command|export_dwg|trim_entity|extend_entity|fillet|chamfer" src tests
git diff --check
```

Expected: no shipped raw command, `export_dwg`, trim, extend, fillet, or chamfer tools.

- [ ] **Step 4: Commit**

```powershell
git add README.md README.vi.md ARCHITECTURE.md CHANGELOG.md
git commit -m "docs(cad): document view export drawing operations"
```
