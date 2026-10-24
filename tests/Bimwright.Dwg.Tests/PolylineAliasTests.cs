using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class PolylineAliasTests
{
    [Theory]
    [InlineData("query")]
    [InlineData("count")]
    [InlineData("select_layer")]
    [InlineData("select_type")]
    [InlineData("paged_query")]
    [InlineData("spatial_count")]
    public void Direct_handlers_refuse_reserved_DXF_token_even_on_an_empty_drawing_before_any_transaction(string operation)
    {
        var document = CreateDrawing(out var database, out _);
        foreach (var token in new[] { "POLYLINE", " \tPOLYLINE\r\n" })
        {
            var result = Execute(operation, document, token);
            Assert.False(result.Ok);
            AssertHint(result.Error);
            Assert.Equal(0, database.ProfileTransactionStarts);
            Assert.Empty(database.ProfileOpenedIds);
        }
    }

    [Theory]
    [InlineData("query")]
    [InlineData("count")]
    [InlineData("select_layer")]
    [InlineData("select_type")]
    [InlineData("paged_query")]
    [InlineData("spatial_count")]
    public async Task Server_shells_refuse_reserved_DXF_token_without_contacting_a_plugin(string operation)
    {
        foreach (var token in new[] { "POLYLINE", " \tPOLYLINE\r\n" })
        {
            var result = await Capture.Local(() => Call(operation, token));
            Assert.False(result.Value<bool>("ok"));
            AssertHint(result.Value<string>("error"));
        }
    }

    [Theory]
    [InlineData("Polyline", "Polyline")]
    [InlineData("polyline", "Polyline")]
    [InlineData("pOlYlInE", "Polyline")]
    [InlineData("LWPOLYLINE", "Polyline")]
    [InlineData("lwpolyline", "Polyline")]
    [InlineData("Polyline2d", "Polyline2d")]
    [InlineData("Polyline3d", "Polyline3d")]
    [InlineData("Autodesk.AutoCAD.DatabaseServices.Polyline", "Polyline")]
    public void Four_legacy_handlers_keep_lightweight_and_explicit_old_style_types_distinct(string filter, string expectedType)
    {
        var document = CreateDrawing(out var database, out var model);
        var expected = new Dictionary<string, string>();
        foreach (var entity in new Entity[] { new Polyline(), new Polyline2d(), new Polyline3d() })
        {
            entity.Layer = "A";
            model.ProfileEntities.Add(database.ProfileAdd(entity));
            expected[entity.GetType().Name] = entity.Handle.ToString();
        }
        foreach (var operation in new[] { "query", "count", "select_layer", "select_type" })
        {
            var result = Execute(operation, document, filter);
            Assert.True(result.Ok, result.Error);
            var body = JObject.FromObject(result.Result);
            Assert.Equal(1, body.Value<int>("count"));
            if (operation == "query")
            {
                var row = Assert.Single(body["entities"]);
                Assert.Equal(expectedType, row.Value<string>("type"));
                Assert.Equal(expected[expectedType], row.Value<string>("handle"));
            }
            else if (operation.StartsWith("select", StringComparison.Ordinal))
                Assert.Equal(expected[expectedType], Assert.Single(body["handles"]).Value<string>());
        }
        Assert.All(database.ProfileOpenModes, mode => Assert.Equal(OpenMode.ForRead, mode));
        Assert.Equal(0, database.ProfileMutations);
    }

    [Theory]
    [InlineData("Polyline")]
    [InlineData("polyline")]
    [InlineData("LWPOLYLINE")]
    [InlineData("Polyline2d")]
    [InlineData("Polyline3d")]
    public async Task Valid_managed_and_alias_names_keep_the_original_four_shell_payloads(string filter)
    {
        foreach (var operation in new[] { "query", "count", "select_layer", "select_type" })
        {
            var sent = await Capture.Send(() => Call(operation, filter));
            Assert.Equal(filter, sent.Json().Value<string>("entity_type"));
            Assert.Null(sent.Json()["paged"]);
        }
    }

    private static void AssertHint(string error)
    {
        Assert.StartsWith("AMBIGUOUS_ENTITY_TYPE:", error);
        Assert.Contains("POLYLINE", error);
        Assert.Contains("LWPOLYLINE", error);
        Assert.Contains("Polyline2d", error);
        Assert.Contains("Polyline3d", error);
    }

    private static Task<string> Call(string operation, string type) => operation switch
    {
        "query" => QueryTools.QueryEntities(type),
        "count" => QueryTools.CountEntities(type),
        "select_layer" => QueryTools.SelectByLayer("A", type),
        "select_type" => QueryTools.SelectByType(type),
        "paged_query" => QueryTools.QueryEntities(type, paged: true),
        "spatial_count" => QueryTools.CountEntities(type, space: "model"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static CommandResult Execute(string operation, Document document, string type)
    {
        var parameters = new JObject { ["entity_type"] = type, ["layer"] = "A" };
        IAcadCommand handler;
        switch (operation)
        {
            case "query": handler = new QueryEntitiesHandler(); break;
            case "count": handler = new CountEntitiesHandler(); break;
            case "select_layer": handler = new SelectByLayerHandler(); break;
            case "select_type": handler = new SelectByTypeHandler(); break;
            case "paged_query": handler = new QueryEntitiesHandler(); parameters["paged"] = true; break;
            case "spatial_count": handler = new CountEntitiesHandler(); parameters["space"] = "model"; break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
        return handler.Execute(document, parameters);
    }

    private static Document CreateDrawing(out Database database, out BlockTableRecord model)
    {
        database = new Database();
        var blocks = new BlockTable();
        database.BlockTableId = database.ProfileAdd(blocks);
        model = new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true };
        var id = database.ProfileAdd(model);
        blocks.ProfileRecords.Add(id);
        blocks.ProfileNames.Add(BlockTableRecord.ModelSpace, id);
        return new Document { Name = "PolylineAlias-simulated.dwg", Database = database };
    }
}
