# DWG CAD Foundation Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first general CAD tool slice: drawing info, entity properties, layer list/create, line/circle creation, and layer reassignment.

**Architecture:** Keep the existing server -> `ToolGateway.LoggedCall` -> plugin `CommandDispatcher` -> `IAcadCommand` route. Public MCP names stay prefixed as `dwg_*`; plugin wire command names stay unprefixed. This plan creates the shared CAD helper contract that later plans consume instead of inventing parallel helpers.

**Tech Stack:** C#/.NET 8 server, AutoCAD plugin shells, AutoCAD .NET API in `src/shared`, Newtonsoft.Json/JToken, ModelContextProtocol, xUnit.

---

## Scope

Ship these tools:

| MCP tool | Wire command | Tool class | Handler |
|---|---|---|---|
| `dwg_get_drawing_info` | `get_drawing_info` | `QueryTools` | `GetDrawingInfoHandler` |
| `dwg_get_entity_properties` | `get_entity_properties` | `QueryTools` | `GetEntityPropertiesHandler` |
| `dwg_list_layers` | `list_layers` | `QueryTools` | `ListLayersHandler` |
| `dwg_create_layer` | `create_layer` | `ModifyTools` | `CreateLayerHandler` |
| `dwg_create_line` | `create_line` | `ModifyTools` | `CreateLineHandler` |
| `dwg_create_circle` | `create_circle` | `ModifyTools` | `CreateCircleHandler` |
| `dwg_change_layer` | `change_layer` | `ModifyTools` | `ChangeLayerHandler` |

Exclude from this plan: move/rotate/scale/copy/erase, block tools, annotation tools, dimensions, P&ID, export, raw AutoCAD commands, layer delete/rename, paper-space support beyond returning current layout info.

## File Structure

Create:
- `src/shared/Cad/CadWire.cs` - AutoCAD-free wire parsing helpers used by tests.
- `src/shared/Cad/CadHandleResolver.cs` - hex handle to `ObjectId` resolution.
- `src/shared/Cad/CadLayerService.cs` - layer validation, ensure, and listing helpers.
- `src/shared/Cad/CadEntityProperties.cs` - stable entity summary serialization.
- `src/shared/Cad/CadPrimitiveWriter.cs` - append entities to the current drawing space.
- `src/shared/Handlers/GetDrawingInfoHandler.cs`
- `src/shared/Handlers/GetEntityPropertiesHandler.cs`
- `src/shared/Handlers/ListLayersHandler.cs`
- `src/shared/Handlers/CreateLayerHandler.cs`
- `src/shared/Handlers/CreateLineHandler.cs`
- `src/shared/Handlers/CreateCircleHandler.cs`
- `src/shared/Handlers/ChangeLayerHandler.cs`
- `tests/Bimwright.Dwg.Tests/CadWireTests.cs`

Modify:
- `src/server/Tools/QueryTools.cs`
- `src/server/Tools/ModifyTools.cs`
- `src/server/Program.cs`
- `src/shared/Infrastructure/SchemaValidator.cs`
- `src/shared/Infrastructure/CommandDispatcher.cs`
- `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`
- `tests/Bimwright.Dwg.Tests/SchemaValidatorTests.cs`
- `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`
- `ARCHITECTURE.md`
- `README.md`
- `README.vi.md`
- `CHANGELOG.md`

## Shared Helper Contract

Create these exact public-in-assembly helper shapes so Plans 2-5 can depend on them.

```csharp
namespace Bimwright.Dwg.Plugin.Cad
{
    internal readonly struct CadPointInput
    {
        public CadPointInput(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    internal static class CadWire
    {
        internal static bool TryParsePoint(Newtonsoft.Json.Linq.JToken token, out CadPointInput point, out string error);
        internal static bool TryParseHandleValue(string handle, out long value, out string error);
        internal static string[] ReadStringArray(Newtonsoft.Json.Linq.JToken parameters, string fieldName);
        internal static bool TryReadAciColor(Newtonsoft.Json.Linq.JToken parameters, string fieldName, int fallback, out int colorIndex, out string error);
    }
}
```

AutoCAD-dependent helpers:

```csharp
internal static class CadHandleResolver
{
    internal static bool TryResolve(Autodesk.AutoCAD.DatabaseServices.Database db, string handle, out Autodesk.AutoCAD.DatabaseServices.ObjectId objectId, out string error);
}

internal static class CadLayerService
{
    internal static object[] ListLayers(Autodesk.AutoCAD.DatabaseServices.Database db);
    internal static bool TryEnsureLayer(Autodesk.AutoCAD.DatabaseServices.Database db, Autodesk.AutoCAD.DatabaseServices.Transaction tx, string name, int colorIndex, out Autodesk.AutoCAD.DatabaseServices.ObjectId layerId, out bool created, out string error);
    internal static bool TryValidateLayerName(string name, out string error);
}

internal static class CadEntityProperties
{
    internal static object Describe(Autodesk.AutoCAD.DatabaseServices.Entity entity, Autodesk.AutoCAD.DatabaseServices.Transaction tx, bool includeGeometry);
}

internal static class CadPrimitiveWriter
{
    internal static Autodesk.AutoCAD.DatabaseServices.ObjectId AppendToCurrentSpace(Autodesk.AutoCAD.DatabaseServices.Database db, Autodesk.AutoCAD.DatabaseServices.Transaction tx, Autodesk.AutoCAD.DatabaseServices.Entity entity);
}
```

## Task 1: Contract Tests

**Files:**
- Modify: `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`
- Modify: `tests/Bimwright.Dwg.Tests/SchemaValidatorTests.cs`
- Modify: `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`
- Create: `tests/Bimwright.Dwg.Tests/CadWireTests.cs`

- [ ] **Step 1: Add tool snapshot expectations**

Add the seven scoped tool names to the expected sorted list in `ToolsListSnapshotTests.CurrentBackedMcpToolsUseDwgPrefix`.

- [ ] **Step 2: Add schema tests**

Add schema assertions:

```csharp
[Fact]
public void Validate_CreateLineRequiresStartAndEnd()
{
    var result = SchemaValidator.Validate("create_line", JObject.Parse("{}"), CommandSchemas.CreateLine);

    Assert.False(result.Ok);
    Assert.Contains("start", result.Error);
}
```

Repeat for `CreateCircle` requiring `center` and `radius`, `CreateLayer` requiring `name`, `ChangeLayer` requiring `handles` and `layer`, and `GetEntityProperties` requiring `handles`.

- [ ] **Step 3: Add `CadWireTests`**

Create tests for:
- `TryParsePoint` accepts `{ "x": 1, "y": 2 }` and fills `z=0`.
- `TryParsePoint` accepts `{ "x": 1, "y": 2, "z": 3 }`.
- `TryParsePoint` rejects `{ "x": 1 }`.
- `TryParseHandleValue` accepts `"7F5AD"`.
- `TryReadAciColor` rejects `0` and `257`, accepts `1` and `256`.

- [ ] **Step 4: Verify RED**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~ToolsListSnapshotTests|FullyQualifiedName~SchemaValidatorTests|FullyQualifiedName~CadWireTests"
```

Expected: FAIL because new schemas, tools, and `CadWire` do not exist.

## Task 2: Wire Helpers And Schemas

**Files:**
- Create: `src/shared/Cad/CadWire.cs`
- Modify: `src/shared/Infrastructure/SchemaValidator.cs`
- Modify: `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`

- [ ] **Step 1: Implement `CadWire`**

Implement only AutoCAD-free parsing logic. Do not reference `Autodesk.AutoCAD.*` in this file.

- [ ] **Step 2: Include `CadWire.cs` in tests**

Add:

```xml
<Compile Include="..\..\src\shared\Cad\CadWire.cs" LinkBase="Shared\Cad" />
```

- [ ] **Step 3: Add command schemas**

Add `CommandSchemas.GetDrawingInfo`, `GetEntityProperties`, `ListLayers`, `CreateLayer`, `CreateLine`, `CreateCircle`, and `ChangeLayer`.

- [ ] **Step 4: Verify GREEN for helper tests**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~SchemaValidatorTests|FullyQualifiedName~CadWireTests"
```

Expected: PASS.

## Task 3: AutoCAD CAD Helpers

**Files:**
- Create: `src/shared/Cad/CadHandleResolver.cs`
- Create: `src/shared/Cad/CadLayerService.cs`
- Create: `src/shared/Cad/CadEntityProperties.cs`
- Create: `src/shared/Cad/CadPrimitiveWriter.cs`

- [ ] **Step 1: Implement handle resolution**

Use `CadWire.TryParseHandleValue`, `new Handle(value)`, and `db.GetObjectId(false, handle, 0)`.

- [ ] **Step 2: Implement layer helpers**

Use `Autodesk.AutoCAD.Internal.SymbolUtilityServices.ValidateSymbolName(name, false)` when available; if this API is unavailable in the target AutoCAD version, catch the exception and return a clear validation error.

- [ ] **Step 3: Implement entity serialization**

Support at least `Line`, `Circle`, `Arc`, `Polyline`, `DBText`, `MText`, `BlockReference`, `Hatch`, `Ellipse`, and fallback `{ handle, type, layer, colorIndex }`.

- [ ] **Step 4: Compile plugin**

Run:

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

Expected: build succeeds.

## Task 4: Query Tools And Handlers

**Files:**
- Create: `src/shared/Handlers/GetDrawingInfoHandler.cs`
- Create: `src/shared/Handlers/GetEntityPropertiesHandler.cs`
- Create: `src/shared/Handlers/ListLayersHandler.cs`
- Modify: `src/server/Tools/QueryTools.cs`
- Modify: `src/shared/Infrastructure/CommandDispatcher.cs`

- [ ] **Step 1: Add `QueryTools` wrappers**

Add `dwg_get_drawing_info`, `dwg_get_entity_properties`, and `dwg_list_layers`. Wrappers forward to `get_drawing_info`, `get_entity_properties`, and `list_layers`.

- [ ] **Step 2: Add handlers**

Handlers return JSON-safe objects. `get_entity_properties` accepts `{ "handles": ["7F5AD"], "include_geometry": true }` and returns per-handle result records.

- [ ] **Step 3: Register handlers**

Add explicit dictionary entries in `CommandDispatcher`.

- [ ] **Step 4: Verify snapshot**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~ToolsListSnapshotTests|FullyQualifiedName~SchemaValidatorTests"
```

Expected: PASS.

## Task 5: Modify Tools And Handlers

**Files:**
- Create: `src/shared/Handlers/CreateLayerHandler.cs`
- Create: `src/shared/Handlers/CreateLineHandler.cs`
- Create: `src/shared/Handlers/CreateCircleHandler.cs`
- Create: `src/shared/Handlers/ChangeLayerHandler.cs`
- Modify: `src/server/Tools/ModifyTools.cs`
- Modify: `src/shared/Infrastructure/CommandDispatcher.cs`

- [ ] **Step 1: Add wrappers**

Add `dwg_create_layer`, `dwg_create_line`, `dwg_create_circle`, and `dwg_change_layer`.

- [ ] **Step 2: Implement handlers**

`create_layer` is ensure-only. If layer exists, return `{ created=false }` and do not overwrite existing layer properties.

`change_layer` accepts `{ "handles": ["7F5AD"], "layer": "BIMWRIGHT_TEST", "create_layer": false }`. It fails missing layers unless `create_layer=true`.

- [ ] **Step 3: Register handlers**

Add explicit dictionary entries in `CommandDispatcher`.

- [ ] **Step 4: Verify build**

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
```

Expected: tests and build pass.

## Task 6: Docs And Manual Smoke Checklist

**Files:**
- Modify: `Program.cs`
- Modify: `ARCHITECTURE.md`
- Modify: `README.md`
- Modify: `README.vi.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Update server instructions**

Add the new general CAD tools to the `query` and `modify` instruction bullets.

- [ ] **Step 2: Update docs**

Document that these tools operate on current AutoCAD active document and use hex handles.

- [ ] **Step 3: Manual smoke**

In a scratch DWG:
1. Run `dwg_get_drawing_info`.
2. Run `dwg_list_layers`.
3. Create `BIMWRIGHT_TEST` with `dwg_create_layer`.
4. Create one line and one circle.
5. Read both handles with `dwg_get_entity_properties`.
6. Move both entities to another layer with `dwg_change_layer`.
7. Confirm one AutoCAD undo reverses each write command's transaction.

- [ ] **Step 4: Commit**

```powershell
git add src/server/Tools/QueryTools.cs src/server/Tools/ModifyTools.cs src/shared/Cad src/shared/Handlers src/shared/Infrastructure/CommandDispatcher.cs src/shared/Infrastructure/SchemaValidator.cs tests/Bimwright.Dwg.Tests README.md README.vi.md ARCHITECTURE.md CHANGELOG.md
git commit -m "feat(cad): add DWG foundation tool slice"
```

## Final Verification

Run:

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
git diff --check
```

If MSBuild locks plugin outputs, run `dotnet build-server shutdown` and retry the build once.
