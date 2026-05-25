# DWG Annotation Block Dimension Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe annotation, block, and dimension tools after the core CAD helpers and primitives are stable.

**Architecture:** Public MCP names stay `dwg_*`; wire commands stay unprefixed. Add explicit toolsets `annotation`, `block`, and `dimension`. Split block read and block write wrappers so read-only mode can expose safe block inspection without exposing insertion or mutation.

**Tech Stack:** C#/.NET 8 server, AutoCAD plugin shells, AutoCAD .NET API, Newtonsoft.Json/JToken, xUnit.

---

## Dependencies

This plan assumes Plans 1-2 have landed with:
- `CadWire`
- `CadHandleResolver`
- `CadLayerService`
- `CadEntityProperties`
- `CadPrimitiveWriter`
- core geometry point/angle parsing
- entity transform and color helpers

## Scope

Ship:

| MCP tool | Wire command | Toolset | Read-only |
|---|---|---|---|
| `dwg_create_text` | `create_text` | `annotation` | no |
| `dwg_create_mtext` | `create_mtext` | `annotation` | no |
| `dwg_create_leader` | `create_leader` | `annotation` | no |
| `dwg_create_table` | `create_table` | `annotation` | no |
| `dwg_list_blocks` | `list_blocks` | `block` | yes |
| `dwg_get_block_attributes` | `get_block_attributes` | `block` | yes |
| `dwg_insert_block` | `insert_block` | `block` | no |
| `dwg_set_block_attributes` | `set_block_attributes` | `block` | no |
| `dwg_explode_block` | `explode_block` | `block` | no |
| `dwg_create_linear_dimension` | `create_linear_dimension` | `dimension` | no |
| `dwg_create_aligned_dimension` | `create_aligned_dimension` | `dimension` | no |
| `dwg_create_radial_dimension` | `create_radial_dimension` | `dimension` | no |
| `dwg_create_diameter_dimension` | `create_diameter_dimension` | `dimension` | no |

Defer: `dwg_set_text_style`, `dwg_create_block_definition`, angular dimensions, dynamic block properties, xrefs, advanced table styling, table formulas, associative dimension management.

## File Structure

Create:
- `src/server/Tools/AnnotationTools.cs`
- `src/server/Tools/BlockTools.cs`
- `src/server/Tools/BlockWriteTools.cs`
- `src/server/Tools/DimensionTools.cs`
- `src/shared/Annotation/AnnotationEntityFactory.cs`
- `src/shared/Blocks/BlockAttributeService.cs`
- `src/shared/Blocks/BlockDefinitionResolver.cs`
- `src/shared/Dimensions/DimensionRequestValidator.cs`
- `src/shared/Dimensions/DimensionEntityFactory.cs`
- annotation handlers under `src/shared/Handlers/Annotation/`
- block handlers under `src/shared/Handlers/Blocks/`
- dimension handlers under `src/shared/Handlers/Dimensions/`
- `tests/Bimwright.Dwg.Tests/AnnotationSchemaTests.cs`
- `tests/Bimwright.Dwg.Tests/BlockSchemaTests.cs`
- `tests/Bimwright.Dwg.Tests/DimensionSchemaTests.cs`
- `tests/Bimwright.Dwg.Tests/DimensionRequestValidatorTests.cs`

Modify:
- `src/server/Program.cs`
- `src/server/ToolsetFilter.cs`
- `src/shared/Infrastructure/CommandDispatcher.cs`
- `src/shared/Infrastructure/SchemaValidator.cs`
- `tests/Bimwright.Dwg.Tests/ToolsListSnapshotTests.cs`
- `tests/Bimwright.Dwg.Tests/ToolsetFilterTests.cs`
- `tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj`
- `README.md`
- `README.vi.md`
- `ARCHITECTURE.md`
- `CHANGELOG.md`

## Task 1: Toolset And Contract Tests

**Files:**
- Modify: `ToolsetFilterTests.cs`
- Modify: `ToolsListSnapshotTests.cs`
- Create: schema test files.

- [ ] **Step 1: Write read-only tests**

`annotation` and `dimension` are write-capable and stripped in read-only mode. `block` splits read and write registration: read-only keeps `dwg_list_blocks` and `dwg_get_block_attributes`, strips `dwg_insert_block`, `dwg_set_block_attributes`, and `dwg_explode_block`.

- [ ] **Step 2: Add snapshot expectations**

Add all scoped public tool names.

- [ ] **Step 3: Add schema tests**

Add required-field tests for each command. Example:

```csharp
[Fact]
public void Validate_InsertBlockRequiresNameAndInsertionPoint()
{
    var result = SchemaValidator.Validate("insert_block", JObject.Parse("{}"), CommandSchemas.InsertBlock);

    Assert.False(result.Ok);
    Assert.Contains("block_name", result.Error);
}
```

- [ ] **Step 4: Verify RED**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~ToolsetFilterTests|FullyQualifiedName~ToolsListSnapshotTests|FullyQualifiedName~AnnotationSchemaTests|FullyQualifiedName~BlockSchemaTests|FullyQualifiedName~DimensionSchemaTests"
```

Expected: FAIL.

## Task 2: Toolset Registration And Wrappers

**Files:**
- Create server tool classes.
- Modify: `Program.cs`, `ToolsetFilter.cs`, `SchemaValidator.cs`.

- [ ] **Step 1: Add toolsets**

Known toolsets gain `annotation`, `block`, and `dimension`. Keep them out of default-on.

- [ ] **Step 2: Register wrappers**

Register read/write block tools separately:

```csharp
if (enabled.Contains("block"))
{
    mcp = mcp.WithTools<BlockTools>();
    if (!ServerState.IsReadOnly) mcp = mcp.WithTools<BlockWriteTools>();
}
```

- [ ] **Step 3: Verify wrapper tests**

Run the focused test command from Task 1. Expected: PASS for toolset/snapshot/schema tests.

- [ ] **Step 4: Commit**

```powershell
git add src/server src/shared/Infrastructure tests/Bimwright.Dwg.Tests
git commit -m "feat(cad): register annotation block dimension toolsets"
```

## Task 3: Annotation Handlers

**Files:**
- Create: `src/shared/Annotation/AnnotationEntityFactory.cs`
- Create annotation handlers.
- Modify: `CommandDispatcher.cs`.

- [ ] **Step 1: Implement factories**

Support `DBText`, `MText`, `Leader` with optional text, and simple `Table` with fixed row/column text matrix.

- [ ] **Step 2: Implement handlers**

Each handler accepts a single object, not a batch array, in this plan. Return `{ ok=true, handle, entity }`.

- [ ] **Step 3: Register handlers**

Add explicit dictionary entries for `create_text`, `create_mtext`, `create_leader`, and `create_table`.

- [ ] **Step 4: Verify build**

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 5: Commit**

```powershell
git add src/shared/Annotation src/shared/Handlers/Annotation src/shared/Infrastructure/CommandDispatcher.cs
git commit -m "feat(annotation): add creation handlers"
```

## Task 4: Block Query And Mutation Handlers

**Files:**
- Create block services and handlers.
- Modify: `CommandDispatcher.cs`.

- [ ] **Step 1: Implement `BlockDefinitionResolver`**

Find existing block table records by name. For this plan, `insert_block` may import an external DWG path only when `block_path` is absolute and exists.

- [ ] **Step 2: Implement `BlockAttributeService`**

Read and set `AttributeReference` values by tag using case-insensitive matching. `strict_tags=true` returns per-tag errors for missing tags.

- [ ] **Step 3: Implement handlers**

`list_blocks` skips anonymous and layout records. `explode_block` only accepts `BlockReference` handles.

- [ ] **Step 4: Verify**

```powershell
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 5: Commit**

```powershell
git add src/shared/Blocks src/shared/Handlers/Blocks src/shared/Infrastructure/CommandDispatcher.cs
git commit -m "feat(blocks): add block inspection and mutation handlers"
```

## Task 5: Dimension Handlers

**Files:**
- Create dimension services and handlers.
- Create/modify tests for `DimensionRequestValidator`.

- [ ] **Step 1: Add validator tests**

Cover zero-length linear/aligned dimensions, negative radius leader length, missing circle/arc handle for radial/diameter dimensions, and degree/radian conversion.

- [ ] **Step 2: Implement validator**

Reject degenerate geometry before creating AutoCAD entities.

- [ ] **Step 3: Implement handlers**

Create linear, aligned, radial, and diameter dimensions using current database dimstyle unless `style_name` is present and exists.

- [ ] **Step 4: Verify**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug --filter "FullyQualifiedName~DimensionRequestValidatorTests"
dotnet build src\plugin-acad24\Bimwright.Dwg.Plugin.Acad24.csproj -c Debug
```

- [ ] **Step 5: Commit**

```powershell
git add src/shared/Dimensions src/shared/Handlers/Dimensions tests/Bimwright.Dwg.Tests src/shared/Infrastructure/CommandDispatcher.cs
git commit -m "feat(dimensions): add safe dimension creation handlers"
```

## Task 6: Docs And Smoke

**Files:**
- Modify docs and changelog.

- [ ] **Step 1: Update docs**

Document toolsets, read-only block behavior, and deferred angular dimensions.

- [ ] **Step 2: Manual smoke**

In a scratch DWG:
1. Create text, mtext, leader, and table.
2. List blocks.
3. Insert a known block from the drawing or an absolute throwaway DWG path.
4. Get and set block attributes if the block has attributes.
5. Explode a copied block reference.
6. Create linear, aligned, radial, and diameter dimensions.
7. Run one invalid-handle case for each family.

- [ ] **Step 3: Final verification**

```powershell
dotnet test tests\Bimwright.Dwg.Tests\Bimwright.Dwg.Tests.csproj -c Debug
dotnet build src\Bimwright.Dwg.sln -c Debug /m:1 /nr:false
git diff --check
```

- [ ] **Step 4: Commit**

```powershell
git add README.md README.vi.md ARCHITECTURE.md CHANGELOG.md
git commit -m "docs(cad): document annotation block dimension tools"
```
