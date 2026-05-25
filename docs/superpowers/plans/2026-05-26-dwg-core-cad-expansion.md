# DWG Core CAD Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the general CAD surface with entity query filters, additional 2D primitives, and basic entity transforms after the foundation slice has landed.

**Architecture:** Reuse Plan 1 helpers in `src/shared/Cad`. Add separate server tool classes only where they improve read-only gating; plugin handlers remain explicit `IAcadCommand` classes registered in `CommandDispatcher`. Avoid P&ID, block, annotation, dimension, view, export, and drawing-file operations in this plan.

**Tech Stack:** C#/.NET 8 server, AutoCAD plugin shells, AutoCAD .NET API, Newtonsoft.Json/JToken, xUnit.

---

## Dependencies

This plan assumes Plan 1 has landed with:
- `CadWire`
- `CadHandleResolver`
- `CadLayerService`
- `CadEntityProperties`
- `CadPrimitiveWriter`
- `dwg_get_entity_properties`
- `dwg_list_layers`
- `dwg_create_layer`

Do not begin this plan until `dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug` and `dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false` pass on Plan 1.

## Scope

Ship:

| MCP tool | Wire command | Toolset |
|---|---|---|
| `dwg_query_entities` | `query_entities` | `query` |
| `dwg_count_entities` | `count_entities` | `query` |
| `dwg_select_by_layer` | `select_by_layer` | `query` |
| `dwg_select_by_type` | `select_by_type` | `query` |
| `dwg_create_point` | `create_point` | `modify` |
| `dwg_create_polyline` | `create_polyline` | `modify` |
| `dwg_create_rectangle` | `create_rectangle` | `modify` |
| `dwg_create_arc` | `create_arc` | `modify` |
| `dwg_create_ellipse` | `create_ellipse` | `modify` |
| `dwg_move_entities` | `move_entities` | `modify` |
| `dwg_rotate_entities` | `rotate_entities` | `modify` |
| `dwg_scale_entities` | `scale_entities` | `modify` |
| `dwg_copy_entities` | `copy_entities` | `modify` |
| `dwg_erase_entities` | `erase_entities` | `modify` |
| `dwg_change_color` | `change_color` | `modify` |
| `dwg_offset_entities` | `offset_entities` | `modify` |

Defer: mirror, array, join, explode, hatch, region, spline, 3D solids, trim, extend, fillet, chamfer, xref, block, dimension, leader, table, export.

## File Structure

Create:
- `src/server/Tools/CoreCadTools.cs` - wrappers for query/create/modify additions if `ModifyTools.cs` becomes too large.
- `src/shared/Cad/CadTransformService.cs`
- `src/shared/Cad/CadQueryService.cs`
- `src/shared/Handlers/QueryEntitiesHandler.cs`
- `src/shared/Handlers/CountEntitiesHandler.cs`
- `src/shared/Handlers/SelectByLayerHandler.cs`
- `src/shared/Handlers/SelectByTypeHandler.cs`
- `src/shared/Handlers/CreatePointHandler.cs`
- `src/shared/Handlers/CreatePolylineHandler.cs`
- `src/shared/Handlers/CreateRectangleHandler.cs`
- `src/shared/Handlers/CreateArcHandler.cs`
- `src/shared/Handlers/CreateEllipseHandler.cs`
- `src/shared/Handlers/CreateMTextHandler.cs`
- `src/shared/Handlers/MoveEntitiesHandler.cs`
- `src/shared/Handlers/RotateEntitiesHandler.cs`
- `src/shared/Handlers/ScaleEntitiesHandler.cs`
- `src/shared/Handlers/CopyEntitiesHandler.cs`
- `src/shared/Handlers/EraseEntitiesHandler.cs`
- `src/shared/Handlers/ChangeColorHandler.cs`
- `src/shared/Handlers/OffsetEntitiesHandler.cs`
- `tests/Bimwright.Dwg.Tests/CoreCadSchemaTests.cs`
- `tests/Bimwright.Dwg.Tests/CadTransformServiceTests.cs`

Modify:
- `src/server/Program.cs`
- `src/server/Tools/QueryTools.cs`
- `src/server/Tools/ModifyTools.cs`
- `src/shared/Infrastructure/SchemaValidator.cs`
- `src/shared/Infrastructure/CommandDispatcher.cs`
- `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`
- `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`
- `README.md`
- `README.vi.md`
- `ARCHITECTURE.md`
- `CHANGELOG.md`

## Task 1: Query Expansion

**Files:**
- Create: `src/shared/Cad/CadQueryService.cs`
- Create: `src/shared/Handlers/QueryEntitiesHandler.cs`
- Create: `src/shared/Handlers/CountEntitiesHandler.cs`
- Create: `src/shared/Handlers/SelectByLayerHandler.cs`
- Create: `src/shared/Handlers/SelectByTypeHandler.cs`
- Modify: `src/server/Tools/QueryTools.cs`
- Modify: `src/shared/Infrastructure/SchemaValidator.cs`
- Modify: `src/shared/Infrastructure/CommandDispatcher.cs`
- Test: `tests/Bimwright.Dwg.Tests/CoreCadSchemaTests.cs`
- Test: `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`

- [ ] **Step 1: Write failing tests**

Add snapshot expectations for `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, and `dwg_select_by_type`. Add schema tests for optional fields: `entity_type`, `layer`, `color_index`, `limit`, `include_geometry`.

- [ ] **Step 2: Implement query service**

`CadQueryService` iterates model space only in this plan, applies filters case-insensitively, clamps `limit` to `[1, 5000]`, and uses `CadEntityProperties.Describe`.

- [ ] **Step 3: Add wrappers and handlers**

Wrappers forward unprefixed wire command names. Handlers return `{ count, entities }` or `{ count, handles }`.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~CoreCadSchemaTests|FullyQualifiedName~ToolsListSnapshotTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src/server/Tools src/shared/Cad src/shared/Handlers src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(cad): add entity query expansion"
```

## Task 2: Additional Primitive Creation

**Files:**
- Create handlers for point, polyline, rectangle, arc, and ellipse.
- Modify wrappers, schemas, dispatcher, snapshot tests.

- [ ] **Step 1: Add failing schema and snapshot tests**

Each create command requires geometry input:
- point: `point`
- polyline: `points`
- rectangle: `corner1`, `corner2`
- arc: `center`, `radius`, `start_angle`, `end_angle`
- ellipse: `center`, `major_radius`, `minor_radius`, `rotation`

- [ ] **Step 2: Implement handlers**

Use `CadPrimitiveWriter.AppendToCurrentSpace`. Return `{ ok=true, handle, entity }`. Inputs use degrees for angles.

- [ ] **Step 3: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~CoreCadSchemaTests|FullyQualifiedName~ToolsListSnapshotTests"
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 4: Commit**

```powershell
git add src/server/Tools src/shared/Handlers src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(cad): add core 2d primitive creation tools"
```

## Task 3: Transform And Lifecycle Modify Tools

**Files:**
- Create: `src/shared/Cad/CadTransformService.cs`
- Create transform/lifecycle handlers.
- Test: `tests/Bimwright.Dwg.Tests/CadTransformServiceTests.cs`

- [ ] **Step 1: Add AutoCAD-free transform tests**

Test angle degree-to-radian conversion, scale factor validation `(0, 1000]`, and vector parsing.

- [ ] **Step 2: Implement `CadTransformService`**

Include:

```csharp
internal static bool TryReadScale(double factor, out double value, out string error);
internal static double DegreesToRadians(double degrees);
```

- [ ] **Step 3: Implement handlers**

`move_entities`, `rotate_entities`, `scale_entities`, `copy_entities`, and `erase_entities` accept `handles` arrays and return per-item `{ handle, ok, new_handle, error }` records. `copy_entities` returns `new_handle`.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~CadTransformServiceTests|FullyQualifiedName~ToolsListSnapshotTests"
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 5: Commit**

```powershell
git add src/server/Tools src/shared/Cad src/shared/Handlers src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(cad): add basic transform and lifecycle tools"
```

## Task 4: Color And Offset

**Files:**
- Create: `src/shared/Handlers/ChangeColorHandler.cs`
- Create: `src/shared/Handlers/OffsetEntitiesHandler.cs`
- Modify wrappers, schemas, dispatcher, tests.

- [ ] **Step 1: Add tests**

`change_color` accepts only `color_index` in `[1,256]` for this plan. `offset_entities` requires `handles` and `distance`.

- [ ] **Step 2: Implement handlers**

`change_color` applies AutoCAD ACI color. `offset_entities` supports `Curve` entities only and returns created handles; unsupported entities produce per-item errors without aborting siblings.

- [ ] **Step 3: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
```

- [ ] **Step 4: Commit**

```powershell
git add src/server/Tools src/shared/Handlers src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(cad): add color and offset tools"
```

## Task 5: Docs And Smoke Checklist

**Files:**
- Modify: `README.md`
- Modify: `README.vi.md`
- Modify: `ARCHITECTURE.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Update tool tables**

Document exact tool names and state that Plan 2 remains model-space only.

- [ ] **Step 2: Manual smoke**

In a scratch DWG:
1. Create polyline, rectangle, arc, and ellipse.
2. Query/count/select by layer and type.
3. Move, rotate, scale, copy, erase.
4. Change color and offset one curve.
5. Confirm existing text translation workflow still works.

- [ ] **Step 3: Final verification**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
git diff --check
```

- [ ] **Step 4: Commit**

```powershell
git add README.md README.vi.md ARCHITECTURE.md CHANGELOG.md
git commit -m "docs(cad): document core CAD expansion"
```
