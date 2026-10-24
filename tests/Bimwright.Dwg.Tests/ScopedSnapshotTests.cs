using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Actual query/count/profile handlers with scope-bound snapshots; no native host or geometry benchmark.</summary>
[Collection("Script handlers")]
public sealed class ScopedSnapshotTests : IDisposable
{
    private readonly DocumentCollection previous = Application.DocumentManager;
    public ScopedSnapshotTests()
    {
        DrawingRevision.Shutdown();
        Application.DocumentManager = new DocumentCollection();
    }

    [Theory]
    [InlineData("viewport")]
    [InlineData("view_table")]
    public void Model_query_cursor_survives_navigation_changes_between_pages(string kind)
    {
        var fixture = new Fixture();
        var request = QueryRequest("model");
        var first = Query(fixture, request);
        var next = WithCursor(request, Cursor(first));
        fixture.Database.Raise("modified", NavigationObject(kind));
        var second = Query(fixture, next);
        Assert.Equal(first["snapshot"], second["snapshot"]);
        Assert.Equal(6, second.Value<long>("exact_match_count"));
        Assert.Equal(new[] { "12", "13" }, second["entities"].Select(row => row.Value<string>("handle")));
        AssertReadOnly(fixture);
    }

    [Theory]
    [InlineData("viewport", false)]
    [InlineData("view_table", false)]
    [InlineData("viewport", true)]
    [InlineData("view_table", true)]
    public void Model_query_and_count_remain_valid_when_navigation_changes_during_enumeration(string kind, bool count)
    {
        var fixture = new Fixture();
        var before = DrawingRevision.For(fixture.Document).Snapshot("model");
        var raised = false;
        fixture.Model.ProfileOnMoveNext = index =>
        {
            if (index != 2 || raised) return;
            raised = true; fixture.Database.Raise("modified", NavigationObject(kind));
        };
        var command = count
            ? new CountEntitiesHandler().Execute(fixture.Document, new JObject { ["space"] = "model" })
            : new QueryEntitiesHandler().Execute(fixture.Document, QueryRequest("model"));
        var result = Success(command);
        Assert.True(raised);
        Assert.Equal(6, result.Value<long>(count ? "count" : "exact_match_count"));
        Assert.True(result["coverage"].Value<bool>("scan_complete"));
        Assert.Equal(before, DrawingRevision.For(fixture.Document).Snapshot("model"));
        AssertReadOnly(fixture);
    }

    [Fact]
    public void Model_query_cursor_still_rejects_entity_changes_before_enumeration()
    {
        var fixture = new Fixture(); var request = QueryRequest("model");
        var next = WithCursor(request, Cursor(Query(fixture, request)));
        fixture.Database.Raise("modified", new Line());
        fixture.Database.ProfileResetMetrics();
        var result = new QueryEntitiesHandler().Execute(fixture.Document, next);
        Assert.False(result.Ok); Assert.StartsWith("QUERY_CHANGED:", result.Error);
        AssertNoTransaction(fixture);
    }

    [Theory]
    [InlineData("viewport")]
    [InlineData("view_table")]
    public void Layout_query_cursor_rejects_navigation_changes_before_enumeration(string kind)
    {
        var fixture = new Fixture(); var request = QueryRequest("layout:Sheet");
        var next = WithCursor(request, Cursor(Query(fixture, request)));
        fixture.Database.Raise("modified", NavigationObject(kind));
        fixture.Database.ProfileResetMetrics();
        var result = new QueryEntitiesHandler().Execute(fixture.Document, next);
        Assert.False(result.Ok); Assert.StartsWith("QUERY_CHANGED:", result.Error);
        AssertNoTransaction(fixture);
    }

    [Theory]
    [InlineData("viewport")]
    [InlineData("view_table")]
    public void Model_profile_expected_snapshot_survives_navigation_but_keeps_its_model_scope(string kind)
    {
        var fixture = new Fixture(); var first = Profile(fixture, "model");
        fixture.Database.Raise("modified", NavigationObject(kind));
        var second = Success(new DrawingProfileHandler().Execute(fixture.Document, ProfileRequest("model", first.Value<string>("snapshot"), 1)));
        Assert.Equal(first["snapshot"], second["snapshot"]);
        Assert.Equal("model", second["scope"].Value<string>("requested_space"));
        Assert.Equal("model_top_level", second["scope"].Value<string>("spaces"));
        Assert.Equal(6, second.Value<long>("exact_total_count"));
        AssertReadOnly(fixture);
    }

    [Theory]
    [InlineData("viewport")]
    [InlineData("view_table")]
    public void Model_profile_survives_navigation_during_its_database_scan(string kind)
    {
        var fixture = new Fixture(); var raised = false;
        fixture.Model.ProfileOnMoveNext = index =>
        {
            if (index != 2 || raised) return;
            raised = true; fixture.Database.Raise("modified", NavigationObject(kind));
        };
        var profile = Profile(fixture, "model");
        Assert.True(raised); Assert.Equal(6, profile.Value<long>("exact_total_count"));
        Assert.True(profile["coverage"].Value<bool>("complete"));
        AssertReadOnly(fixture);
    }

    [Fact]
    public void Model_profile_still_rejects_entity_change_before_its_next_scan()
    {
        var fixture = new Fixture(); var profile = Profile(fixture, "model");
        fixture.Database.Raise("modified", new Line());
        fixture.Database.ProfileResetMetrics();
        var result = new DrawingProfileHandler().Execute(fixture.Document, ProfileRequest("model", profile.Value<string>("snapshot"), 1));
        Assert.False(result.Ok); Assert.StartsWith("PROFILE_CHANGED:", result.Error);
        AssertNoTransaction(fixture);
    }

    [Theory]
    [InlineData("all", "viewport")]
    [InlineData("all", "view_table")]
    [InlineData("layout:Sheet", "viewport")]
    [InlineData("layout:Sheet", "view_table")]
    public void All_and_layout_profile_continuation_rejects_navigation_revision_changes(string space, string kind)
    {
        var fixture = new Fixture(); var first = Profile(fixture, space);
        fixture.Database.Raise("modified", NavigationObject(kind));
        fixture.Database.ProfileResetMetrics();
        var result = new DrawingProfileHandler().Execute(fixture.Document, ProfileRequest(space, first.Value<string>("snapshot"), 1));
        Assert.False(result.Ok); Assert.StartsWith("PROFILE_CHANGED:", result.Error);
        AssertNoTransaction(fixture);
    }

    [Theory]
    [InlineData("all", "model")]
    [InlineData("model", "all")]
    [InlineData("model", "layout:Sheet")]
    [InlineData("layout:Sheet", "model")]
    [InlineData("all", "layout:Sheet")]
    [InlineData("layout:Sheet", "all")]
    public void Profile_expected_snapshot_cannot_cross_space_scopes_before_enumeration(string from, string to)
    {
        var fixture = new Fixture(); var first = Profile(fixture, from);
        fixture.Database.ProfileResetMetrics();
        var result = new DrawingProfileHandler().Execute(fixture.Document, ProfileRequest(to, first.Value<string>("snapshot"), 1));
        Assert.False(result.Ok); Assert.StartsWith("PROFILE_CHANGED:", result.Error);
        AssertNoTransaction(fixture);
    }

    [Fact]
    public void Model_profile_does_not_read_unrelated_unreadable_layout_records_or_enumerate_the_block_table()
    {
        var fixture = new Fixture();
        fixture.Blocks.ProfileEnumerationFaultAfter = 0;
        fixture.Paper.ObjectId.Slot.OpenFailure = new InvalidOperationException("Unrelated layout record unavailable.");
        fixture.Layout.ProfileNameFailure = new InvalidOperationException("Unrelated layout name unavailable.");
        var profile = Profile(fixture, "model");
        Assert.Equal(6, profile.Value<long>("exact_total_count"));
        Assert.True(profile["coverage"].Value<bool>("complete"));
        Assert.Equal(1, profile["histograms"]["spaces"].Value<int>("total_groups"));
        Assert.Equal(0, fixture.Blocks.ProfileEnumerationStarts);
        Assert.DoesNotContain(fixture.Paper.ObjectId, fixture.Database.ProfileOpenedIds);
        Assert.DoesNotContain(fixture.Layout.ObjectId, fixture.Database.ProfileOpenedIds);
        AssertReadOnly(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_layout_profile_fails_closed_without_reading_a_fallback_space(bool unreadable)
    {
        var fixture = new Fixture();
        if (unreadable) fixture.Layout.ObjectId.Slot.OpenFailure = new InvalidOperationException("Requested layout metadata unavailable.");
        var result = new DrawingProfileHandler().Execute(fixture.Document, ProfileRequest(unreadable ? "layout:Sheet" : "layout:missing"));
        Assert.False(result.Ok); Assert.Null(result.Result);
        Assert.DoesNotContain(fixture.Database.ProfileOpenedIds, id => id.Slot.Value is Entity);
        Assert.Equal(0, fixture.Model.ProfileEnumerationStarts);
        Assert.Equal(0, fixture.Paper.ProfileEnumerationStarts);
        AssertReadOnly(fixture);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("layout:Sheet")]
    public void Profile_final_content_next_calls_preserve_the_requested_space(string space)
    {
        var fixture = new Fixture();
        var command = new DrawingProfileHandler().Execute(fixture.Document, new JObject { ["section"] = "types", ["space"] = space });
        Assert.True(command.Ok, command.Error);
        var result = DrawingProfileResponse.FromJson(Envelope(command.Result), 16384, space);
        var page = Text(result)["result"];
        Assert.NotEmpty(page["output"]["next_calls"]);
        Assert.All(page["output"]["next_calls"], call =>
        {
            Assert.Equal(space, call["arguments"].Value<string>("space"));
            Assert.Equal(page["snapshot"], call["arguments"]["expected_snapshot"]);
        });
        var layers = page["output"]["next_calls"].Single(call => call["arguments"].Value<string>("section") == "layers");
        var continued = Success(new DrawingProfileHandler().Execute(fixture.Document, layers["arguments"]));
        Assert.Equal(space, continued["scope"].Value<string>("requested_space"));
        Assert.Equal(6, continued.Value<long>("exact_total_count"));
        AssertReadOnly(fixture);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("layout:Sheet")]
    public void Scoped_profile_rejects_an_old_plugin_reply_without_requested_space_confirmation(string space)
    {
        var fixture = new Fixture(); var old = Profile(fixture, "all");
        ((JObject)old["scope"]).Remove("requested_space");
        ((JObject)old["request"]).Remove("space");
        var result = DrawingProfileResponse.FromJson(Envelope(old), 16384, space);
        Assert.True(result.IsError);
        var body = Text(result);
        Assert.False(body.Value<bool>("ok")); Assert.StartsWith("PROFILE_SCOPE_UNSUPPORTED:", body.Value<string>("error"));
        Assert.Null(body["result"]);
    }

    [Fact]
    public void Default_all_profile_still_accepts_the_legacy_unscoped_reply_shape()
    {
        var fixture = new Fixture(); var old = Profile(fixture, "all");
        ((JObject)old["scope"]).Remove("requested_space");
        ((JObject)old["request"]).Remove("space");
        var result = DrawingProfileResponse.FromJson(Envelope(old), 16384);
        Assert.NotEqual(true, result.IsError);
        var body = Text(result);
        Assert.True(body.Value<bool>("ok")); Assert.Equal(12, body["result"].Value<long>("exact_total_count"));
    }

    private static DBObject NavigationObject(string kind) => kind == "viewport" ? new Viewport() : new ViewportTableRecord();
    private static JObject QueryRequest(string space) => new() { ["paged"] = true, ["space"] = space, ["limit"] = 2, ["fields"] = new JArray("handle") };
    private static JObject WithCursor(JObject request, string cursor) { var next = (JObject)request.DeepClone(); next["cursor"] = cursor; return next; }
    private static JObject Query(Fixture f, JObject request) => Success(new QueryEntitiesHandler().Execute(f.Document, request));
    private static string Cursor(JObject page)
    {
        var result = EntityQueryResponse.Fit(new CallToolResult { Content = new[] { new TextContentBlock { Text = Envelope(page) } } }, 16384);
        var cursor = Text(result)["result"]["output"].Value<string>("next_cursor");
        Assert.False(string.IsNullOrEmpty(cursor)); return cursor;
    }
    private static JObject ProfileRequest(string space, string snapshot = null, int offset = 0)
    {
        var request = new JObject { ["section"] = "layers", ["space"] = space, ["offset"] = offset };
        if (snapshot != null) request["expected_snapshot"] = snapshot;
        return request;
    }
    private static JObject Profile(Fixture f, string space) => Success(new DrawingProfileHandler().Execute(f.Document, ProfileRequest(space)));
    private static JObject Success(CommandResult command) { Assert.True(command.Ok, command.Error); return JObject.FromObject(command.Result); }
    private static string Envelope(object profile) => JsonConvert.SerializeObject(new McpResponse { Ok = true, Result = profile });
    private static JObject Text(CallToolResult result) => JObject.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
    private static void AssertNoTransaction(Fixture fixture)
    {
        Assert.Equal(0, fixture.Database.ProfileTransactionStarts); Assert.Empty(fixture.Database.ProfileOpenedIds);
    }
    private static void AssertReadOnly(Fixture fixture)
    {
        Assert.All(fixture.Database.ProfileOpenModes, mode => Assert.Equal(OpenMode.ForRead, mode));
        Assert.Equal(0, fixture.Database.ProfileMutations);
        Assert.Equal(17, fixture.Document.MutationCount);
        Assert.Equal(0, fixture.Database.TransactionManager.NumberOfActiveTransactions);
        Assert.Equal(fixture.Database.ProfileTransactionStarts, fixture.Database.ProfileTransactionDisposals);
    }

    private sealed class Fixture
    {
        internal Database Database { get; } = new();
        internal BlockTable Blocks { get; } = new();
        internal BlockTableRecord Model { get; }
        internal BlockTableRecord Paper { get; }
        internal Layout Layout { get; } = new Layout { LayoutName = "Sheet" };
        internal Document Document { get; }
        internal Fixture()
        {
            Database.BlockTableId = Database.ProfileAdd(Blocks);
            Model = new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true };
            Paper = new BlockTableRecord { Name = "*Paper_Space", IsLayout = true, LayoutId = Database.ProfileAdd(Layout) };
            foreach (var record in new[] { Model, Paper })
            {
                var id = Database.ProfileAdd(record); Blocks.ProfileRecords.Add(id); Blocks.ProfileNames.Add(record.Name, id);
            }
            for (var i = 0; i < 6; i++)
            {
                Add(Model, i % 2 == 0 ? new Line() : new DBText { TextString = "Model" }, 0x15 - i, "ModelLayer" + i);
                Add(Paper, i % 2 == 0 ? new DBText { TextString = "Sheet" } : new Line(), 0x25 - i, "PaperLayer" + i);
            }
            Document = new Document { Name = "ScopedSnapshot.dwg", Database = Database, MutationCount = 17 };
            Application.DocumentManager.Documents.Add(Document); Application.DocumentManager.MdiActiveDocument = Document;
            Database.ProfileResetMetrics();
        }
        private void Add(BlockTableRecord record, Entity entity, long handle, string layer)
        {
            entity.Layer = layer;
            var id = Database.ProfileAdd(entity); entity.Handle = new Handle(handle); record.ProfileEntities.Add(id);
        }
    }
    public void Dispose() { DrawingRevision.Shutdown(); Application.DocumentManager = previous; }
}
