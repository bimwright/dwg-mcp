using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.Rewriting;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Actual source extraction, unchanged SpatialClusterer, legacy handler and final whole-cluster delivery.</summary>
[Collection("Script handlers")]
public sealed class TextReadTests : IDisposable
{
    private readonly DocumentCollection previous = Application.DocumentManager;
    public TextReadTests()
    {
        DrawingRevision.Shutdown(); ClusterStateStore.Replace(Array.Empty<ClusterState>());
        UnicodeStyleService.TextReadEnsureStyleCalls = RewriteExecutor.TextReadExecuteCalls = 0;
        Application.DocumentManager = new DocumentCollection();
    }

    [Fact]
    public void Explicit_direct_handles_match_actual_legacy_selected_text_extraction_and_clustering()
    {
        var f = new Fixture();
        f.Add(new DBText { TextString = "zero" }, 0x10);
        f.Add(new DBText { TextString = "one" }, 0x11, x: 1);
        f.Add(new MText { Contents = "MText 原文" }, 0x12, x: 50, y: 50);
        var leader = f.Add(new MLeader(), 0x13, x: 100, y: 100);
        var clones = LeaderClones(leader);
        f.Add(new AttributeReference { TextString = "Attribute ref" }, 0x14, x: 150, y: 150);
        f.Add(new AttributeDefinition { TextString = "Constant definition", Constant = true }, 0x15, x: 200, y: 200);
        f.Select(f.Entities);
        var legacy = Success(Execute(f, new JObject { ["grouping_strength"] = "weak", ["include_entities"] = true }));
        Assert.True(ClusterStateStore.HasState());
        Assert.All(legacy["clusters"], row => { Assert.NotNull(row["id"]); Assert.NotNull(row["rewrite_mode"]); });
        var selectedCalls = f.Document.Editor.TextReadSelectCalls;
        var request = Handles(f.Entities); request["grouping_strength"] = "weak"; request["include_entities"] = true;
        var explicitRead = Success(Execute(f, request));
        Assert.Equal(Canonical(legacy), Canonical(explicitRead));
        AssertReadOnlyContract(explicitRead);
        Assert.True(ClusterStateStore.HasState());
        Assert.Equal(selectedCalls, f.Document.Editor.TextReadSelectCalls);
        Assert.Equal(2, leader.TextReadCloneReads);
        Assert.Equal(2, clones.Count); Assert.All(clones, clone => Assert.Equal(1, clone.TextReadDisposeCalls));
        AssertNoMutation(f, expectedSelectionReads: 1);
    }

    [Fact]
    public void Legacy_selection_still_expands_one_block_level_while_explicit_source_excludes_it()
    {
        var f = new Fixture();
        var definition = new BlockTableRecord { Name = "TextBlock" };
        var definitionId = f.Database.ProfileAdd(definition);
        var child = f.Add(new DBText { TextString = "inside" }, 0x21, space: -1);
        definition.ProfileEntities.Add(child.ObjectId);
        var attribute = f.Add(new AttributeReference { TextString = "attribute" }, 0x22, x: 200, y: 200, space: -1);
        var block = f.Add(new BlockReference { BlockTableRecord = definitionId, BlockTransform = Matrix3d.Displacement(new Vector3d(100, 20, 3)) }, 0x20);
        block.AttributeCollection.Add(attribute.ObjectId);
        f.Select(new[] { block });
        var legacy = Success(Execute(f, new JObject { ["include_entities"] = true }));
        Assert.Contains(legacy["clusters"], row => row.Value<bool>("in_block"));
        var inside = legacy["clusters"].SelectMany(row => row["entities"]).Single(row => row.Value<string>("handle") == "21");
        Assert.Equal(100d, inside.Value<double>("x")); Assert.Equal(20d, inside.Value<double>("y"));
        f.Database.ProfileResetMetrics();
        var read = Success(Execute(f, Handles(new Entity[] { block })));
        Assert.Empty(read["clusters"]); Assert.Equal(0, read.Value<long>("exact_match_count"));
        Assert.Equal(1, read["coverage"].Value<long>("block_references_excluded"));
        Assert.DoesNotContain(child.ObjectId, f.Database.ProfileOpenedIds);
        Assert.DoesNotContain(attribute.ObjectId, f.Database.ProfileOpenedIds);
        Assert.DoesNotContain(definitionId, f.Database.ProfileOpenedIds);
        AssertReadOnlyContract(read); Assert.True(ClusterStateStore.HasState());
        AssertNoMutation(f, expectedSelectionReads: 1);
    }

    [Fact]
    public void Explicit_handles_open_only_requested_top_level_objects_and_known_unsupported_types_are_excluded()
    {
        var f = new Fixture(); var text = f.Add(new DBText { TextString = "requested" }, 0xF);
        var unrelated = f.Add(new DBText { TextString = "unreadable unrelated" }, 0x10);
        unrelated.ObjectId.Slot.OpenFailure = new InvalidOperationException("Must never open this unrelated object.");
        var block = f.Add(new BlockReference(), 0x11);
        var nonconstant = f.Add(new AttributeDefinition { Constant = false, TextString = "template" }, 0x12);
        var line = f.Add(new Line(), 0x13);
        var read = Success(Execute(f, Handles(new Entity[] { text, block, nonconstant, line })));
        Assert.True(read["coverage"].Value<bool>("scan_complete"));
        Assert.Equal(1, read["source"].Value<int>("text_records"));
        Assert.Equal(3, read["coverage"].Value<long>("unsupported_entity_count"));
        Assert.Equal(1, read["coverage"].Value<long>("nonconstant_attribute_definitions_excluded"));
        Assert.DoesNotContain(unrelated.ObjectId, f.Database.ProfileOpenedIds);
        Assert.Equal(1, text.QueryExtentsReads);
        Assert.Equal(0, block.QueryExtentsReads); Assert.Equal(0, line.QueryExtentsReads);
        AssertNoMutation(f);
    }

    [Fact]
    public void Bbox_reads_apply_inclusive_WCS_Z_negative_coordinates_layout_and_reuse_extraction_extents()
    {
        var f = new Fixture(); f.Database.Insunits = UnitsValue.Meters;
        f.Add(new DBText { TextString = "model" }, 0xF);
        var accepted = f.Add(new DBText { TextString = "negative layout" }, 0x10, x: -3, y: -4, z: -5, space: 1);
        var wrongZ = f.Add(new DBText { TextString = "outside Z" }, 0x11, x: -3, y: -4, z: 10, space: 1);
        var read = Success(Execute(f, new JObject
        {
            ["bbox"] = Box(-3, -4, -5, -2.2, -3.2, -4.8), ["bbox_mode"] = "contains",
            ["space"] = "layout:Sheet", ["include_entities"] = true
        }));
        Assert.Equal(1, read["source"].Value<int>("text_records"));
        Assert.Equal("negative layout", Assert.Single(read["clusters"]).Value<string>("text"));
        Assert.Equal(-3d, read["clusters"][0]["entities"][0].Value<double>("x"));
        Assert.Equal("Meters", read["units"].Value<string>("insunits"));
        Assert.Equal("drawing_units_no_conversion", read["units"].Value<string>("coordinates"));
        Assert.Equal(1, accepted.QueryExtentsReads); Assert.Equal(1, wrongZ.QueryExtentsReads);
        var missing = Execute(f, new JObject { ["handles"] = new JArray("10"), ["space"] = "model" });
        AssertIncomplete(Success(missing), "missing_requested_handles", 1);
        AssertNoMutation(f);
    }

    [Theory]
    [InlineData("missing", "missing_requested_handles")]
    [InlineData("entity", "entity_read_failed")]
    [InlineData("extents", "extents_unavailable")]
    [InlineData("nan_extents", "extents_unavailable")]
    [InlineData("height", "text_read_failed")]
    [InlineData("position", "text_read_failed")]
    [InlineData("enumeration", "space_enumeration_failed")]
    public void Incomplete_explicit_source_never_clusters_partial_records_or_issues_continuation(string fault, string counter)
    {
        var f = new Fixture(); var first = f.Add(new DBText { TextString = "readable" }, 0xF);
        var bad = f.Add(new DBText { TextString = "bad" }, 0x10, x: 100);
        var request = Handles(new Entity[] { first, bad });
        switch (fault)
        {
            case "missing": request["handles"] = new JArray("F", "DEAD"); break;
            case "entity": bad.ObjectId.Slot.OpenFailure = new InvalidOperationException("Unreadable requested entity."); break;
            case "extents": bad.QueryExtentsFailure = new InvalidOperationException("Missing bounds."); break;
            case "nan_extents": bad.QueryExtentsOverride = () => new Extents3d(new Point3d(double.NaN, 0, 0), new Point3d(1, 1, 1)); break;
            case "height": bad.Height = 0; break;
            case "position": bad.Position = new Point3d(double.NaN, 0, 0); break;
            case "enumeration": f.Model.ProfileEnumerationFaultAfter = 1; break;
        }
        SeedLegacyState();
        var read = Success(Execute(f, request));
        AssertIncomplete(read, counter, 1);
        Assert.True(ClusterStateStore.HasState());
        var delivered = Deliver(read);
        Assert.Empty(delivered["clusters"]);
        Assert.False(delivered["output"].Value<bool>("continuation_allowed"));
        Assert.Empty(delivered["output"]["next_calls"]);
        AssertNoMutation(f);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("anchor")]
    [InlineData("contents")]
    [InlineData("height")]
    public void MLeader_uses_one_text_clone_and_disposes_it_once_even_on_invalid_geometry(string fault)
    {
        var f = new Fixture(); var leader = f.Add(new MLeader(), 0xF);
        var clones = LeaderClones(leader, fault == "contents");
        if (fault == "anchor") leader.TextReadAnchorFailure = new InvalidOperationException("No real anchor.");
        if (fault == "height") leader.TextHeight = 0;
        var read = Success(Execute(f, Handles(new Entity[] { leader })));
        Assert.Equal(1, leader.TextReadCloneReads);
        Assert.Equal(1, Assert.Single(clones).TextReadDisposeCalls);
        if (fault == "none") Assert.Equal(1, read.Value<long>("exact_match_count"));
        else AssertIncomplete(read, "text_read_failed", 1);
        AssertNoMutation(f);
    }

    [Fact]
    public void Clustering_the_whole_source_preserves_a_cluster_with_more_than_128_entities()
    {
        var f = new Fixture();
        for (var i = 0; i < 200; i++) f.Add(new DBText { TextString = "W" + i.ToString("D3") }, i + 0x10, x: i * .2);
        var request = Handles(f.Entities); request["grouping_strength"] = "weak"; request["include_entities"] = true;
        var raw = Success(Execute(f, request));
        Assert.Equal(1, raw.Value<long>("exact_match_count"));
        Assert.Equal(200, Assert.Single(raw["clusters"]).Value<int>("entity_count"));
        var small = Deliver(raw, 16384);
        Assert.Empty(small["clusters"]); Assert.False(small["output"].Value<bool>("continuation_allowed"));
        Assert.Empty(small["output"]["next_calls"]); Assert.NotNull(small["output"]["blocked"]);
        var large = Deliver(raw, 65536);
        var cluster = Assert.Single(large["clusters"]);
        Assert.Equal(200, cluster.Value<int>("entity_count")); Assert.Equal(200, cluster["entities"].Count());
        Assert.Contains("W000", cluster.Value<string>("text")); Assert.Contains("W199", cluster.Value<string>("text"));
        AssertReadOnlyContract(large); AssertNoMutation(f);
    }

    [Fact]
    public void Whole_cluster_pages_have_no_gaps_duplicates_or_skipped_checkpoint_after_byte_trimming()
    {
        var f = ClusterFixture(); var request = Handles(f.Entities); request["grouping_strength"] = "weak";
        var seen = new List<string>(); var texts = new List<string>(); var pages = 0;
        while (true)
        {
            var page = Deliver(Success(Execute(f, request))); pages++;
            AssertReadOnlyContract(page); Assert.Equal(160, page.Value<long>("exact_match_count"));
            seen.AddRange(page["clusters"].Select(row => row.Value<string>("cluster_key")));
            texts.AddRange(page["clusters"].Select(row => row.Value<string>("text")));
            if (!page["output"].Value<bool>("has_more")) break;
            var lastKey = page["clusters"].Last.Value<string>("cluster_key");
            var lastOrdinal = ulong.Parse(lastKey.Split(':').Last(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.Equal(lastOrdinal, EntityQueryCursor.Read(page["output"].Value<string>("next_cursor"), page.Value<string>("snapshot"), page.Value<string>("query_hash")));
            request = Next(page); Assert.True(pages < 100);
        }
        Assert.Equal(160, seen.Count); Assert.Equal(160, seen.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 160).Select(i => "T" + i.ToString("D3")), texts);
        Assert.True(pages > 1); Assert.False(ClusterStateStore.HasState()); AssertNoMutation(f);
    }

    [Theory]
    [InlineData("tamper", "CURSOR_INVALID:")]
    [InlineData("grouping", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("include", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("handles", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("space", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("entity", "QUERY_CHANGED:")]
    public void Invalid_or_stale_cursor_preserves_previous_legacy_state_and_refuses_before_database_scan(string change, string code)
    {
        var f = ClusterFixture(); var request = Handles(f.Entities); request["grouping_strength"] = "weak";
        var next = Next(Deliver(Success(Execute(f, request))));
        var saved = SeedLegacyState();
        switch (change)
        {
            case "tamper": var token = next.Value<string>("cursor"); next["cursor"] = (token[0] == 'A' ? "B" : "A") + token.Substring(1); break;
            case "grouping": next["grouping_strength"] = "strong"; break;
            case "include": next["include_entities"] = true; break;
            case "handles": ((JArray)next["handles"]).RemoveAt(0); break;
            case "space": next["space"] = "layout:Sheet"; break;
            case "entity": f.Database.Raise("modified", new DBText()); break;
        }
        f.Database.ProfileResetMetrics();
        var result = Execute(f, next);
        Assert.False(result.Ok); Assert.StartsWith(code, result.Error);
        Assert.Same(saved, ClusterStateStore.Get(saved.Id));
        Assert.Equal(0, f.Database.ProfileTransactionStarts); Assert.Empty(f.Database.ProfileOpenedIds);
    }

    [Fact]
    public void Model_navigation_and_canonical_handle_reordering_allow_valid_text_continuation()
    {
        var f = ClusterFixture(); var request = Handles(f.Entities); request["grouping_strength"] = "weak";
        var first = Deliver(Success(Execute(f, request))); var next = Next(first);
        next["handles"] = new JArray(next["handles"].Values<string>().Reverse().Concat(new[] { "10" }));
        f.Database.Raise("modified", new Viewport()); SeedLegacyState();
        var second = Deliver(Success(Execute(f, next)));
        Assert.Equal(first["snapshot"], second["snapshot"]);
        Assert.Empty(first["clusters"].Select(row => row.Value<string>("cluster_key")).Intersect(second["clusters"].Select(row => row.Value<string>("cluster_key"))));
        Assert.True(ClusterStateStore.HasState());
    }

    [Fact]
    public void Invalid_source_arguments_preserve_legacy_state_without_selection_or_database_reads()
    {
        var f = new Fixture(); var saved = SeedLegacyState();
        foreach (var args in new[]
        {
            new JObject { ["handles"] = new JArray("F"), ["bbox"] = Box(-1, -1, -1, 1, 1, 1) },
            new JObject { ["space"] = "model" }, new JObject { ["handles"] = new JArray("0") },
            new JObject { ["handles"] = new JArray(Enumerable.Range(1, 5001).Select(i => i.ToString("X"))) }
        })
        {
            Assert.False(Execute(f, args).Ok); Assert.Same(saved, ClusterStateStore.Get(saved.Id));
        }
        Assert.Equal(0, f.Database.ProfileTransactionStarts); AssertNoMutation(f);
    }

    [Fact]
    public void Accepted_read_preserves_manual_selection_state_so_legacy_writer_reaches_rewrite()
    {
        var f = new Fixture(); var saved = SeedLegacyState();
        var read = Success(Execute(f, new JObject { ["handles"] = new JArray() }));
        Assert.Equal(0, read.Value<long>("exact_match_count")); Assert.Empty(read["clusters"]);
        AssertReadOnlyContract(read); Assert.Same(saved, ClusterStateStore.Get(saved.Id));
        // The stub styling service marks the first write step; reaching it proves the old IDs were not refused.
        var write = Assert.Throws<InvalidOperationException>(() => new TranslateAndRewriteHandler().Execute(f.Document, new JObject
        { ["translations"] = new JArray(new JObject { ["id"] = saved.Id, ["new_text"] = "translated" }) }));
        Assert.Contains("Unreachable styling path", write.Message);
        Assert.Equal(1, UnicodeStyleService.TextReadEnsureStyleCalls); AssertNoMutation(f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_record_or_UTF8_limit_refuses_before_partial_clustering_and_preserves_old_state(bool bytes)
    {
        var f = new Fixture();
        if (bytes) f.Add(new DBText { TextString = new string('界', 2796203) }, 0xF);
        else for (var i = 0; i < 5001; i++) f.Add(new DBText { TextString = "x" }, i + 0x10, x: i * .2);
        SeedLegacyState();
        var result = Execute(f, new JObject { ["bbox"] = Box(-1, -1, -1, 2000, 2, 2) });
        Assert.False(result.Ok); Assert.StartsWith("TEXT_SOURCE_TOO_LARGE:", result.Error);
        Assert.Null(result.Result); Assert.DoesNotContain("cleared", result.Error);
        Assert.True(ClusterStateStore.HasState()); AssertNoMutation(f);
    }

    private static List<MText> LeaderClones(MLeader leader, bool unreadable = false)
    {
        var clones = new List<MText>();
        leader.TextReadMTextFactory = () =>
        {
            var clone = new MText { Contents = "Leader text" };
            if (unreadable) clone.TextReadContentsFailure = new InvalidOperationException("Unreadable clone contents.");
            clones.Add(clone); return clone;
        };
        return clones;
    }
    private static ClusterState SeedLegacyState()
    {
        var state = new ClusterState { Id = 77, CombinedText = "old legacy selection", AllHandles = new List<string> { "F" } };
        ClusterStateStore.Replace(new[] { state }); return state;
    }
    private static Fixture ClusterFixture()
    {
        var f = new Fixture();
        for (var i = 0; i < 160; i++) f.Add(new DBText { TextString = "T" + i.ToString("D3") }, i + 0x10, x: i * 100);
        return f;
    }
    private static JObject Handles(IEnumerable<Entity> entities) => new() { ["handles"] = new JArray(entities.Select(entity => entity.Handle.ToString())) };
    private static JObject Box(double a, double b, double c, double x, double y, double z) => new() { ["min"] = new JArray(a, b, c), ["max"] = new JArray(x, y, z) };
    private static CommandResult Execute(Fixture f, JObject args) => new GetSelectedTextsHandler().Execute(f.Document, args);
    private static JObject Success(CommandResult result) { Assert.True(result.Ok, result.Error); return JObject.FromObject(result.Result); }
    private static JObject Deliver(JObject raw, int cap = 16384)
    {
        var reply = TextReadResponse.Fit(new CallToolResult { Content = new[]
        { new TextContentBlock { Text = new JObject { ["ok"] = true, ["result"] = raw }.ToString(Formatting.None) } } }, cap);
        Assert.True(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reply, McpJsonUtilities.DefaultOptions).Length <= cap);
        return (JObject)JObject.Parse(Assert.Single(reply.Content.OfType<TextContentBlock>()).Text)["result"];
    }
    private static JObject Next(JObject page)
    {
        var call = Assert.Single(page["output"]["next_calls"]); Assert.Equal("dwg_get_selected_texts", call.Value<string>("tool"));
        var args = (JObject)call["arguments"].DeepClone();
        args["grouping_strength"] = args["groupingStrength"]; args.Remove("groupingStrength");
        args["include_entities"] = args["includeEntities"]; args.Remove("includeEntities");
        if (args["handles"]?.Type == JTokenType.String) args["handles"] = JArray.Parse(args.Value<string>("handles"));
        if (args["bbox"]?.Type == JTokenType.String) args["bbox"] = JObject.Parse(args.Value<string>("bbox"));
        return args;
    }
    private static string[] Canonical(JObject result) => result["clusters"].Select(cluster => new JObject
    {
        ["text"] = cluster["text"], ["count"] = cluster["entity_count"],
        ["entities"] = new JArray(cluster["entities"].Select(entity => new JObject
        {
            ["handle"] = entity["handle"], ["kind"] = entity["kind"], ["text"] = entity["text"],
            ["x"] = entity["x"], ["y"] = entity["y"], ["layer"] = entity["layer"]
        }).OrderBy(entity => entity.Value<string>("handle"), StringComparer.Ordinal))
    }.ToString(Formatting.None)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static void AssertReadOnlyContract(JObject result)
    {
        Assert.True(result.Value<bool>("read_only")); Assert.False(result.Value<bool>("writeback_ready"));
        Assert.True(result.Value<bool>("legacy_cluster_state_preserved"));
        Assert.False(result["source"].Value<bool>("sampling")); Assert.False(result["source"].Value<bool>("estimated"));
        Assert.All(result["clusters"], row => { Assert.Null(row["id"]); Assert.Null(row["rewrite_mode"]); Assert.StartsWith("read:", row.Value<string>("cluster_key")); Assert.False(row.Value<bool>("in_block")); });
    }
    private static void AssertIncomplete(JObject read, string counter, long count)
    {
        Assert.False(read["coverage"].Value<bool>("scan_complete")); Assert.Equal(count, read["coverage"].Value<long>(counter));
        Assert.Equal(JTokenType.Null, read["exact_match_count"].Type); Assert.Empty(read["clusters"]); AssertReadOnlyContract(read);
    }
    private static void AssertNoMutation(Fixture f, int expectedSelectionReads = 0)
    {
        Assert.All(f.Database.ProfileOpenModes, mode => Assert.Equal(OpenMode.ForRead, mode));
        Assert.Equal(0, f.Database.ProfileMutations); Assert.Equal(0, f.Document.Editor.TextReadSetSelectionCalls);
        Assert.Equal(0, f.DatabaseEvents);
        Assert.Equal(expectedSelectionReads, f.Document.Editor.TextReadSelectCalls); Assert.Equal(31, f.Document.MutationCount);
        Assert.Equal(0, f.Database.TransactionManager.NumberOfActiveTransactions);
        Assert.Equal(f.Database.ProfileTransactionStarts, f.Database.ProfileTransactionDisposals);
    }
    private sealed class Fixture
    {
        internal Database Database { get; } = new();
        internal BlockTableRecord Model { get; }
        private readonly BlockTableRecord paper;
        internal Document Document { get; }
        internal List<Entity> Entities { get; } = new();
        internal int DatabaseEvents;
        internal Fixture()
        {
            var blocks = new BlockTable(); Database.BlockTableId = Database.ProfileAdd(blocks);
            Model = new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true };
            paper = new BlockTableRecord { Name = "*Paper_Space", IsLayout = true, LayoutId = Database.ProfileAdd(new Layout { LayoutName = "Sheet" }) };
            foreach (var record in new[] { Model, paper }) { var id = Database.ProfileAdd(record); blocks.ProfileRecords.Add(id); blocks.ProfileNames.Add(record.Name, id); }
            Document = new Document { Name = "TextReadSimulation.dwg", Database = Database, MutationCount = 31 };
            Application.DocumentManager.Documents.Add(Document); Application.DocumentManager.MdiActiveDocument = Document;
            Database.ObjectModified += (_, _) => DatabaseEvents++;
            Database.ObjectAppended += (_, _) => DatabaseEvents++;
            Database.ObjectErased += (_, _) => DatabaseEvents++;
            Database.ObjectUnappended += (_, _) => DatabaseEvents++;
            Database.ObjectReappended += (_, _) => DatabaseEvents++;
        }
        internal T Add<T>(T entity, long handle, double x = 0, double y = 0, double z = 0, int space = 0) where T : Entity
        {
            entity.Layer = "TEXT";
            var point = new Point3d(x, y, z);
            if (entity is DBText text) { text.Position = point; text.Height = 1; }
            else if (entity is MText mtext) { mtext.Location = point; mtext.TextHeight = 1; }
            else if (entity is MLeader leader) { leader.TextReadAnchor = point; leader.TextHeight = 1; }
            entity.QueryVertices = new[] { point, new Point3d(x + .8, y + .8, z + .2) };
            var id = Database.ProfileAdd(entity); entity.Handle = new Handle(handle);
            if (space >= 0) (space == 0 ? Model : paper).ProfileEntities.Add(id);
            Entities.Add(entity); return entity;
        }
        internal void Select(IEnumerable<Entity> entities)
            => Document.Editor.TextReadSelection = new PromptSelectionResult(PromptStatus.OK, new SelectionSet(entities.Select(entity => entity.ObjectId)));
    }
    public void Dispose()
    {
        DrawingRevision.Shutdown(); ClusterStateStore.Replace(Array.Empty<ClusterState>());
        UnicodeStyleService.TextReadEnsureStyleCalls = RewriteExecutor.TextReadExecuteCalls = 0;
        Application.DocumentManager = previous;
    }
}
