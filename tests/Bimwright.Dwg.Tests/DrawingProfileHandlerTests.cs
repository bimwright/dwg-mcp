using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.View;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Bimwright.Dwg.Tests;

/// <summary>Actual production collector/formatter against explicit simulated database objects, never AutoCAD runtime.</summary>
[Collection("Script handlers")]
public sealed class DrawingProfileHandlerTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly DocumentCollection previous = Application.DocumentManager;

    public DrawingProfileHandlerTests(ITestOutputHelper output)
    {
        this.output = output;
        DrawingRevision.Shutdown();
        Application.DocumentManager = new DocumentCollection();
    }

    [Fact]
    public void Simulated_50000_entity_scan_and_final_16KiB_page_meet_the_local_five_second_budget()
    {
        // Fixture allocation and its independent expected data are outside every measured interval.
        var fixture = new Fixture(50000, 2500, longLayerNames: true);
        var elapsed = new List<double>();
        for (var repetition = 1; repetition <= 3; repetition++)
        {
            fixture.Database.ProfileResetMetrics();
            var watch = Stopwatch.StartNew();
            var command = new DrawingProfileHandler().Execute(fixture.Document, new JObject());
            var delivered = Format(command);
            var bytes = FinalBytes(delivered);
            watch.Stop();
            elapsed.Add(watch.Elapsed.TotalMilliseconds);
            output.WriteLine(JsonConvert.SerializeObject(new
            {
                kind = "SIMULATED_DRAWING_PROFILE", repetition, entity_count = 50000,
                elapsed_ms = watch.Elapsed.TotalMilliseconds, scan_object_opens = fixture.Database.ProfileOpenedIds.Count,
                final_mcp_utf8_bytes = bytes, budget_bytes = 16384,
                timing_scope = "actual handler enumeration + aggregation + final formatter + SDK serialization; fixture allocation excluded"
            }));
            Assert.True(command.Ok, command.Error);
            Assert.NotEqual(true, delivered.IsError);
            Assert.True(watch.Elapsed.TotalMilliseconds <= 5000, "SIMULATED scan+format exceeded5000ms; see recorded metrics.");
            Assert.True(bytes <= 16384);
            var raw = Assert.IsType<JObject>(command.Result);
            AssertCompleteProfile(raw, 50000);
            Assert.Equal(10000, raw.Value<long>("direct_block_reference_count"));
            Assert.Equal(6666, raw.Value<long>("direct_xref_insert_count"));
            Assert.Equal(2500, raw["histograms"]["layers"].Value<int>("total_groups"));
            Assert.All(Rows(raw, "layers"), row => Assert.Equal(20, row.Value<long>("count")));
            Assert.Equal(fixture.ExpectedTypes, Histogram(raw, "types"));
            Assert.Equal(new long[] { 16667, 16667, 16666 }, fixture.Spaces.Select(space =>
                Rows(raw, "spaces").Single(row => row.Value<string>("id") == space.Handle.ToString()).Value<long>("count")));
            var page = DeliveredProfile(delivered);
            Assert.True(page["coverage"].Value<bool>("complete"));
            Assert.False(page["output"].Value<bool>("complete"));
            Assert.InRange(Rows(page, "layers").Count(), 1, 127); // Actual byte fitting, beyond the128 candidate-row limit.
            Assert.Contains(page["output"]["next_calls"], call => call["arguments"].Value<string>("section") == "layers");
            Assert.Equal(50000, fixture.Database.ProfileOpenedIds.Count(id => id.Slot.Value is Entity));
            AssertNoExpansionOrMutation(fixture);
        }
        output.WriteLine(JsonConvert.SerializeObject(new
        {
            kind = "SIMULATED_DRAWING_PROFILE_SUMMARY", repetitions = elapsed.Count,
            min_ms = elapsed.Min(), median_ms = elapsed.OrderBy(value => value).ElementAt(1), max_ms = elapsed.Max(),
            native_AutoCAD_runtime = false
        }));
    }

    [Fact]
    public void Exact_runtime_types_layers_and_three_spaces_exclude_nested_unused_and_xref_contents()
    {
        var fixture = new Fixture(300, 17);
        var profile = Inspect(fixture);
        AssertCompleteProfile(profile, 300);
        Assert.Equal(fixture.ExpectedTypes, Histogram(profile, "types"));
        Assert.Equal(fixture.ExpectedLayers, Histogram(profile, "layers"));
        Assert.Equal(60, profile.Value<long>("direct_block_reference_count"));
        Assert.Equal(40, profile.Value<long>("direct_xref_insert_count"));
        Assert.Equal(3, profile["histograms"]["spaces"].Value<int>("total_groups"));
        Assert.All(Rows(profile, "spaces"), row => Assert.Equal(100, row.Value<long>("count")));
        var model = Rows(profile, "spaces").Single(row => row.Value<string>("kind") == "model");
        Assert.Equal("Model", model.Value<string>("name"));
        Assert.Equal("model", model.Value<string>("name_source"));
        foreach (var name in new[] { "Layout A", "Bản vẽ 2" })
        {
            var row = Rows(profile, "spaces").Single(item => item.Value<string>("name") == name);
            Assert.Equal("layout", row.Value<string>("name_source"));
            Assert.Equal(name, row.Value<string>("layout_name"));
            Assert.StartsWith("*Paper_Space", row.Value<string>("record_name"));
        }
        AssertNoExpansionOrMutation(fixture);
    }

    [Fact]
    public void Erased_entity_ids_are_explicitly_excluded_and_never_opened()
    {
        var fixture = new Fixture(90, 7);
        var erased = fixture.Database.ProfileAdd(new Line { Layer = "ERASED_NOT_COUNTED" }, erased: true);
        fixture.Spaces[0].ProfileEntities.Add(erased);
        var profile = Inspect(fixture);
        AssertCompleteProfile(profile, 90);
        Assert.Equal(91, profile["coverage"].Value<long>("entity_ids_seen"));
        Assert.Equal(1, profile["coverage"].Value<long>("erased_ids_skipped"));
        Assert.DoesNotContain(erased, fixture.Database.ProfileOpenedIds);
        Assert.DoesNotContain(Rows(profile, "layers"), row => row.Value<string>("name") == "ERASED_NOT_COUNTED");
        AssertNoExpansionOrMutation(fixture);
    }

    [Fact]
    public void Empty_readable_spaces_report_exact_zero_with_complete_coverage()
    {
        var fixture = new Fixture(0, 1);
        var profile = Inspect(fixture);
        AssertCompleteProfile(profile, 0);
        Assert.Empty(Rows(profile, "types"));
        Assert.Empty(Rows(profile, "layers"));
        Assert.Equal(3, Rows(profile, "spaces").Count());
        Assert.All(Rows(profile, "spaces"), row => Assert.Equal(0, row.Value<long>("count")));
        AssertNoExpansionOrMutation(fixture);
    }

    [Theory]
    [InlineData("entity_open", "entity_read_failed", 89)]
    [InlineData("space_open", "space_read_failed", 60)]
    [InlineData("space_enumerator", "space_enumeration_failed", 63)]
    [InlineData("table_enumerator", "space_enumeration_failed", 30)]
    public void Unreadable_population_has_observed_counts_and_null_exact_totals(string fault, string counter, long observed)
    {
        var fixture = new Fixture(90, 7);
        switch (fault)
        {
            case "entity_open": fixture.EntityIds[0].Slot.OpenFailure = new InvalidOperationException("Simulated entity open failure."); break;
            case "space_open": fixture.Spaces[1].ObjectId.Slot.OpenFailure = new InvalidOperationException("Simulated space open failure."); break;
            case "space_enumerator": fixture.Spaces[0].ProfileEnumerationFaultAfter = 3; break;
            case "table_enumerator": fixture.Blocks.ProfileEnumerationFaultAfter = 1; break;
        }
        var profile = Inspect(fixture);
        Assert.False(profile["coverage"].Value<bool>("complete"));
        Assert.Equal(1, profile["coverage"].Value<long>(counter));
        Assert.Equal(observed, profile.Value<long>("observed_entity_count"));
        Assert.Equal(JTokenType.Null, profile["exact_total_count"].Type);
        Assert.False(profile["histograms"]["types"].Value<bool>("counts_complete"));
        Assert.False(profile["histograms"]["layers"].Value<bool>("counts_complete"));
        Assert.False(profile["histograms"]["spaces"].Value<bool>("counts_complete"));
        AssertNoExpansionOrMutation(fixture);
    }

    [Fact]
    public void Unreadable_layer_keeps_known_population_and_types_but_marks_layer_counts_incomplete()
    {
        var fixture = new Fixture(90, 7);
        ((Entity)fixture.EntityIds[7].Slot.Value).ProfileLayerFailure = new InvalidOperationException("Simulated layer getter failure.");
        var profile = Inspect(fixture);
        Assert.False(profile["coverage"].Value<bool>("complete"));
        Assert.Equal(90, profile.Value<long>("exact_total_count"));
        Assert.Equal(90, profile.Value<long>("observed_entity_count"));
        Assert.Equal(1, profile["coverage"].Value<long>("layer_unknown"));
        Assert.True(profile["histograms"]["types"].Value<bool>("counts_complete"));
        Assert.False(profile["histograms"]["layers"].Value<bool>("counts_complete"));
        Assert.Equal(89, Rows(profile, "layers").Sum(row => row.Value<long>("count")));
        AssertNoExpansionOrMutation(fixture);
    }

    [Fact]
    public void Unreadable_xref_metadata_is_unknown_rather_than_zero_or_a_guessed_xref_count()
    {
        var fixture = new Fixture(90, 7);
        fixture.AttachDefinition.ProfileXrefFailure = new InvalidOperationException("Simulated xref metadata failure.");
        var profile = Inspect(fixture);
        Assert.False(profile["coverage"].Value<bool>("complete"));
        Assert.Equal(90, profile.Value<long>("exact_total_count"));
        Assert.Equal(18, profile.Value<long>("direct_block_reference_count"));
        Assert.Equal(6, profile["coverage"].Value<long>("xref_classification_unknown"));
        Assert.Equal(JTokenType.Null, profile["direct_xref_insert_count"].Type);
        Assert.Equal(6, profile.Value<long>("observed_direct_xref_insert_count"));
        Assert.True(profile["histograms"]["types"].Value<bool>("counts_complete"));
        Assert.True(profile["histograms"]["layers"].Value<bool>("counts_complete"));
        AssertNoExpansionOrMutation(fixture);
    }

    [Fact]
    public void Unreadable_layout_name_falls_back_to_record_name_and_marks_space_metadata_incomplete()
    {
        var fixture = new Fixture(90, 7);
        fixture.Layouts[0].ProfileNameFailure = new InvalidOperationException("Simulated layout name failure.");
        var profile = Inspect(fixture);
        var space = Rows(profile, "spaces").Single(row => row.Value<string>("id") == fixture.Spaces[1].Handle.ToString());
        Assert.Equal(fixture.Spaces[1].Name, space.Value<string>("name"));
        Assert.Equal("block_table_record", space.Value<string>("name_source"));
        Assert.Null(space.Value<string>("layout_name"));
        Assert.Equal(JTokenType.Null, space["count"].Type);
        Assert.Equal(30, space.Value<long>("observed_count"));
        Assert.False(space.Value<bool>("complete"));
        Assert.Equal(JTokenType.Null, profile["exact_total_count"].Type);
        Assert.Equal(90, profile.Value<long>("observed_entity_count"));
        Assert.Equal(1, profile["coverage"].Value<long>("space_read_failed"));
        AssertNoExpansionOrMutation(fixture);
    }

    [Fact]
    public void Actual_handler_continuation_returns_distinct_exact_group_names_under_the_same_snapshot()
    {
        var fixture = new Fixture(300, 300, longLayerNames: true);
        var first = DeliveredProfile(Format(Execute(fixture, new JObject { ["section"] = "layers" })));
        var next = (JObject)first["output"]["next_calls"].Single(call => call["arguments"].Value<string>("section") == "layers")["arguments"];
        Assert.Equal(Rows(first, "layers").Count(), next.Value<int>("offset"));
        var second = DeliveredProfile(Format(Execute(fixture, next)));
        Assert.Equal(first["snapshot"], second["snapshot"]);
        Assert.Empty(Rows(first, "layers").Select(row => row.Value<string>("name"))
            .Intersect(Rows(second, "layers").Select(row => row.Value<string>("name"))));
        Assert.Equal(fixture.ExpectedLayers.Keys.Skip(next.Value<int>("offset")).Take(Rows(second, "layers").Count()),
            Rows(second, "layers").Select(row => row.Value<string>("name")));
        Assert.All(Rows(second, "layers"), row => Assert.Equal(1, row.Value<long>("count")));
        Assert.True(second["coverage"].Value<bool>("complete"));
        AssertNoExpansionOrMutation(fixture);
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("viewport")]
    [InlineData("document_lifetime")]
    public void Changed_snapshot_rejects_continuation_before_database_enumeration(string change)
    {
        var fixture = new Fixture(30, 30);
        var profile = Inspect(fixture);
        var tracker = DrawingRevision.For(fixture.Document);
        var normalRevision = tracker.Revision;
        var document = fixture.Document;
        if (change == "document_lifetime") document = new Document { Name = fixture.Document.Name, Database = fixture.Database };
        else fixture.Database.Raise("modified", change == "viewport" ? new Viewport() : new Line());
        if (change == "viewport") Assert.Equal(normalRevision, tracker.Revision); // Capture-navigation rules remain independent.
        fixture.Database.ProfileResetMetrics();
        var result = new DrawingProfileHandler().Execute(document, new JObject
        {
            ["section"] = "layers", ["offset"] = 1, ["expected_snapshot"] = profile["snapshot"]
        });
        Assert.False(result.Ok);
        Assert.StartsWith("PROFILE_CHANGED:", result.Error);
        Assert.Equal(0, fixture.Database.ProfileTransactionStarts);
        Assert.Empty(fixture.Database.ProfileOpenedIds);
    }

    [Fact]
    public void Revision_change_during_actual_enumeration_discards_the_mixed_profile()
    {
        var fixture = new Fixture(90, 7);
        var raised = false;
        fixture.Spaces[0].ProfileOnMoveNext = index =>
        {
            if (index != 3 || raised) return;
            raised = true; fixture.Database.Raise("modified", new Viewport());
        };
        var result = Execute(fixture);
        Assert.True(raised);
        Assert.False(result.Ok);
        Assert.StartsWith("PROFILE_CHANGED:", result.Error);
        Assert.Null(result.Result);
        Assert.All(fixture.Database.ProfileOpenModes, mode => Assert.Equal(OpenMode.ForRead, mode));
        Assert.Equal(0, fixture.Database.ProfileMutations);
        Assert.Equal(0, fixture.Database.TransactionManager.NumberOfActiveTransactions);
    }

    [Fact]
    public void Block_usage_counts_direct_nested_and_dynamic_references_and_profiles_only_unused_definitions()
    {
        var fixture = new Fixture(18, 3);
        var definition = fixture.Definitions[0];
        definition.IsDynamicBlock = true;
        var variant = fixture.AddBlock(new BlockTableRecord { Name = "*U1", IsAnonymous = true });
        definition.ProfileAnonymousIds.Add(variant.ObjectId);
        var variantInsert = fixture.AddReference(fixture.Spaces[2], variant);
        definition.ProfileReferenceIds.Add(variantInsert); // A reference returned by both records still counts once.
        fixture.AddReference(fixture.Spaces[0], variant, erased: true);
        var nestedOnly = fixture.AddBlock(new BlockTableRecord { Name = "NestedOnly" });
        fixture.AddReference(fixture.Definitions[3], nestedOnly);
        definition.ProfileEntities.Add(fixture.Database.ProfileAdd(new AttributeDefinition()));
        var unknown = fixture.AddBlock(new BlockTableRecord { Name = "UnknownBlock", ProfileReferenceFailure = new InvalidOperationException() });

        var listed = new ListBlocksHandler().Execute(fixture.Document, new JObject());
        Assert.True(listed.Ok, listed.Error);
        var rows = JObject.FromObject(listed.Result)["blocks"].ToDictionary(row => row.Value<string>("name"));
        Assert.Equal(2, rows[definition.Name].Value<int>("reference_count"));
        Assert.Equal(3, rows[definition.Name].Value<int>("nested_reference_count"));
        Assert.Equal(1, rows[definition.Name].Value<int>("entity_count"));
        Assert.Equal(1, rows[definition.Name].Value<int>("attribute_definition_count"));
        Assert.Equal(0, rows[nestedOnly.Name].Value<int>("reference_count"));
        Assert.Equal(1, rows[nestedOnly.Name].Value<int>("nested_reference_count"));
        Assert.False(rows[nestedOnly.Name].Value<bool>("unused"));
        Assert.True(rows["UnusedBlock"].Value<bool>("unused"));
        Assert.Equal(JTokenType.Null, rows[unknown.Name]["unused"].Type);
        Assert.DoesNotContain(variant.Name, rows.Keys);

        var unused = Inspect(fixture)["unused_blocks"];
        Assert.Equal("UnusedBlock", Assert.Single(unused["rows"]).Value<string>("name"));
        Assert.False(unused.Value<bool>("counts_complete"));
        Assert.Equal(JTokenType.Null, unused["total_groups"].Type);
    }

    [Fact]
    public void Unused_blocks_continue_at_the_delivered_offset_under_the_profile_byte_cap()
    {
        const int cap = 4096;
        var fixture = new Fixture(0, 1);
        for (var i = 0; i < 140; i++)
            fixture.AddBlock(new BlockTableRecord { Name = "Unreferenced_" + i.ToString("D3") + new string('x', 65) });

        var first = Format(Execute(fixture, new JObject { ["section"] = "unused_blocks", ["max_output_bytes"] = cap }), cap);
        Assert.True(FinalBytes(first) <= cap);
        var page = DeliveredProfile(first);
        var rows = (JArray)page["unused_blocks"]["rows"];
        Assert.NotEmpty(rows);
        var next = Assert.Single(page["output"]["next_calls"].Select(call => (JObject)call["arguments"])
            .Where(arguments => arguments.Value<string>("section") == "unused_blocks"));
        Assert.Equal(rows.Count, next.Value<int>("offset"));
        var second = Format(Execute(fixture, next), cap);
        Assert.True(FinalBytes(second) <= cap);
        var secondPage = DeliveredProfile(second);
        Assert.Equal(page.Value<string>("snapshot"), secondPage.Value<string>("snapshot"));
        Assert.True(string.CompareOrdinal(rows.Last.Value<string>("name"), secondPage["unused_blocks"]["rows"].First.Value<string>("name")) < 0);
        Assert.Null(((JObject)Execute(fixture, new JObject { ["space"] = "model" }).Result)["unused_blocks"]);
    }

    [Fact]
    public void Text_stats_are_opt_in_scoped_and_bounded_without_losing_counts()
    {
        var fixture = new Fixture(0, 1);
        var model = fixture.Spaces[0];
        void Add(BlockTableRecord owner, Entity entity, bool erased = false)
        {
            entity.Layer = "TEXT";
            owner.ProfileEntities.Add(fixture.Database.ProfileAdd(entity, erased));
        }

        Add(model, new DBText { Height = 1, TextString = new string('x', 119) + "😀 tail" });
        var multiline = Enumerable.Range(0, 10).Select(_ => new MText
        {
            TextHeight = 10000, Text = new string('界', 120), Contents = "formatted contents"
        }).ToArray();
        foreach (var entity in multiline) Add(model, entity);
        Add(model, new DBText { Height = double.NaN, TextString = "invalid height" });
        Add(model, new DBText { Height = 3, TextString = "erased" }, erased: true);
        Add(model, new AttributeDefinition { Height = 3, TextString = "attribute" });
        Add(fixture.Spaces[1], new MText { TextHeight = 2, Text = "paper space" });
        Add(fixture.Definitions[0], new DBText { Height = 2, TextString = "nested definition" });

        Assert.Null(Inspect(fixture)["text_stats"]);
        Assert.All(multiline, entity => Assert.Equal(0, entity.ProfilePlainTextReads));

        var command = Execute(fixture, new JObject { ["space"] = "model", ["include_text_stats"] = true });
        Assert.True(command.Ok, command.Error);
        var stats = ((JObject)command.Result)["text_stats"];
        Assert.Equal(2, stats.Value<int>("dbtext_count"));
        Assert.Equal(10, stats.Value<int>("mtext_count"));
        var heights = stats["height_histogram"];
        Assert.Equal(1, heights.Value<int>("unknown_count"));
        var bins = (JArray)heights["bins"];
        Assert.Equal(20, bins.Count);
        Assert.Equal(1, bins.Single(bin => bin[0].Value<double>() == 1)[2].Value<int>());
        Assert.Equal(10, bins.Last[2].Value<int>());
        Assert.Equal(JTokenType.Null, bins.Last[1].Type);
        var samples = (JArray)stats["samples"];
        Assert.Equal(8, samples.Count);
        Assert.Equal(new string('x', 119), samples[0].Value<string>("text"));
        Assert.True(samples[0].Value<bool>("truncated"));
        Assert.Equal(new string('界', 120), samples[1].Value<string>("text"));
        Assert.Equal(7, multiline.Sum(entity => entity.ProfilePlainTextReads));

        var bounded = Format(command, 4096);
        Assert.True(FinalBytes(bounded) <= 4096);
        var boundedProfile = DeliveredProfile(bounded);
        var delivered = boundedProfile["text_stats"];
        Assert.Equal(10, delivered.Value<int>("mtext_count"));
        Assert.Equal(20, ((JArray)delivered["height_histogram"]["bins"]).Count);
        var omitted = 8 - ((JArray)delivered["samples"]).Count;
        Assert.Equal(omitted, delivered.Value<int>("samples_omitted_for_budget"));
        if (omitted > 0) Assert.False(boundedProfile["output"].Value<bool>("complete"));
    }

    [Fact]
    public void List_layers_counts_match_profile_and_incomplete_reads_do_not_claim_zeros()
    {
        var fixture = new Fixture(18, 3);
        var layerTable = new LayerTable();
        fixture.Database.LayerTableId = fixture.Database.ProfileAdd(layerTable);
        foreach (var name in fixture.ExpectedLayers.Keys.Append("Empty"))
        {
            var id = fixture.Database.ProfileAdd(new LayerTableRecord { Name = name });
            layerTable.ProfileRecords.Add(id);
            layerTable.ProfileNames.Add(name, id);
        }
        fixture.EntityIds[0].Slot.Erased = true;

        JObject List(bool includeCounts)
        {
            var result = new ListLayersHandler().Execute(fixture.Document, new JObject { ["include_counts"] = includeCounts });
            Assert.True(result.Ok, result.Error);
            return JObject.FromObject(result.Result);
        }

        var legacy = List(false);
        Assert.Null(legacy["counts_complete"]);
        Assert.All(legacy["layers"], row => Assert.Null(row["entity_count"]));
        Assert.Equal(0, fixture.Blocks.ProfileEnumerationStarts);

        var counted = List(true);
        Assert.True(counted.Value<bool>("counts_complete"));
        Assert.Equal("model_and_layout_top_level", counted.Value<string>("count_scope"));
        var counts = counted["layers"].ToDictionary(row => row.Value<string>("name"), row => row.Value<long>("entity_count"));
        Assert.Equal(0, counts["Empty"]);
        Assert.Equal(17, counts.Values.Sum());
        Assert.Equal(Histogram(Inspect(fixture), "layers").ToArray(), counts.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());

        fixture.EntityIds[1].Slot.OpenFailure = new InvalidOperationException("simulated read failure");
        var partial = List(true);
        Assert.False(partial.Value<bool>("counts_complete"));
        Assert.All(partial["layers"], row => Assert.Equal(JTokenType.Null, row["entity_count"].Type));
        Assert.Equal(16, partial["layers"].Sum(row => row.Value<long>("observed_entity_count")));
    }

    private static CommandResult Execute(Fixture fixture, JObject parameters = null)
        => new DrawingProfileHandler().Execute(fixture.Document, parameters ?? new JObject());

    private static JObject Inspect(Fixture fixture)
    {
        var command = Execute(fixture);
        Assert.True(command.Ok, command.Error);
        return Assert.IsType<JObject>(command.Result);
    }

    private static CallToolResult Format(CommandResult command, int cap = 16384)
        => DrawingProfileResponse.FromJson(JsonConvert.SerializeObject(new McpResponse
        { Id = "simulated-profile", Ok = command.Ok, Result = command.Result, Error = command.Error }), cap);

    private static int FinalBytes(CallToolResult result)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length;

    private static JObject DeliveredProfile(CallToolResult result)
        => (JObject)JObject.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text)["result"];

    private static IEnumerable<JToken> Rows(JObject profile, string section) => profile["histograms"][section]["rows"];

    private static SortedDictionary<string, long> Histogram(JObject profile, string section)
        => new(Rows(profile, section).ToDictionary(row => row.Value<string>("name"), row => row.Value<long>("count")), StringComparer.Ordinal);

    private static void AssertCompleteProfile(JObject profile, long expectedCount)
    {
        Assert.Equal(expectedCount, profile.Value<long>("exact_total_count"));
        Assert.Equal(expectedCount, profile.Value<long>("observed_entity_count"));
        Assert.True(profile["coverage"].Value<bool>("complete"));
        Assert.False(profile["source"].Value<bool>("sampling"));
        Assert.False(profile["source"].Value<bool>("estimated"));
        Assert.Equal("full_enumeration", profile["source"].Value<string>("counting"));
        Assert.Equal("Entity.GetType().FullName", profile["source"].Value<string>("type_basis"));
        Assert.Equal("model_and_layout_top_level", profile["scope"].Value<string>("spaces"));
        Assert.False(profile["scope"].Value<bool>("block_expansion"));
        Assert.False(profile["scope"].Value<bool>("xref_traversal"));
        Assert.All(new[] { "types", "layers", "spaces" }, section => Assert.True(profile["histograms"][section].Value<bool>("counts_complete")));
    }

    private static void AssertNoExpansionOrMutation(Fixture fixture)
    {
        Assert.All(fixture.Definitions, record => Assert.Equal(0, record.ProfileEnumerationStarts));
        Assert.DoesNotContain(fixture.Database.ProfileOpenedIds, id => fixture.DefinitionEntityIds.Contains(id));
        Assert.All(fixture.Database.ProfileOpenModes, mode => Assert.Equal(OpenMode.ForRead, mode));
        Assert.Equal(0, fixture.Database.ProfileMutations);
        Assert.Equal(0, fixture.DatabaseEvents);
        Assert.Equal(37, fixture.Document.MutationCount);
        Assert.Equal(0, fixture.Document.LockCount);
        Assert.Equal(0, fixture.Database.TransactionManager.NumberOfActiveTransactions);
        Assert.Equal(fixture.Database.ProfileTransactionStarts, fixture.Database.ProfileTransactionDisposals);
    }

    private sealed class Fixture
    {
        internal Database Database { get; } = new();
        internal Document Document { get; }
        internal BlockTable Blocks { get; } = new();
        internal BlockTableRecord[] Spaces { get; }
        internal Layout[] Layouts { get; }
        internal BlockTableRecord[] Definitions { get; }
        internal BlockTableRecord AttachDefinition => Definitions[1];
        internal ObjectId[] EntityIds { get; }
        internal HashSet<ObjectId> DefinitionEntityIds { get; } = new();
        internal SortedDictionary<string, long> ExpectedTypes { get; } = new(StringComparer.Ordinal);
        internal SortedDictionary<string, long> ExpectedLayers { get; } = new(StringComparer.Ordinal);
        internal int DatabaseEvents;

        internal BlockTableRecord AddBlock(BlockTableRecord record)
        {
            var id = Database.ProfileAdd(record);
            Blocks.ProfileRecords.Add(id); Blocks.ProfileNames.Add(record.Name, id);
            return record;
        }

        internal ObjectId AddReference(BlockTableRecord owner, BlockTableRecord definition, bool erased = false)
        {
            var id = Database.ProfileAdd(new BlockReference { OwnerId = owner.ObjectId, BlockTableRecord = definition.ObjectId, Layer = "INSERTS" }, erased);
            owner.ProfileEntities.Add(id);
            definition.ProfileReferenceIds.Add(id);
            return id;
        }

        internal Fixture(int entityCount, int layerCount, bool longLayerNames = false)
        {
            Database.BlockTableId = Database.ProfileAdd(Blocks);
            Layouts = new[] { new Layout { LayoutName = "Layout A" }, new Layout { LayoutName = "Bản vẽ 2" } };
            var layouts = Layouts.Select(layout => Database.ProfileAdd(layout)).ToArray();
            Spaces = new[]
            {
                new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true },
                new BlockTableRecord { Name = "*Paper_Space", IsLayout = true, LayoutId = layouts[0] },
                new BlockTableRecord { Name = "*Paper_Space0", IsLayout = true, LayoutId = layouts[1] }
            };
            Definitions = new[]
            {
                new BlockTableRecord { Name = "ReferencedBlock" },
                new BlockTableRecord { Name = "AttachedXref", IsFromExternalReference = true },
                new BlockTableRecord { Name = "OverlayXref", IsFromOverlayReference = true },
                new BlockTableRecord { Name = "UnusedBlock" }
            };
            foreach (var record in Spaces.Concat(Definitions)) AddBlock(record);
            foreach (var record in Definitions)
            {
                var line = Database.ProfileAdd(new Line { Layer = "INSIDE_DEFINITION_NOT_COUNTED" });
                record.ProfileEntities.Add(line);
                DefinitionEntityIds.Add(line);
                if (record != Definitions[0]) DefinitionEntityIds.Add(AddReference(record, Definitions[0]));
            }
            var typeNames = new[] { typeof(Line).FullName, typeof(Circle).FullName, typeof(DBText).FullName, typeof(BlockReference).FullName, typeof(ProxyEntity).FullName };
            EntityIds = new ObjectId[entityCount];
            for (var i = 0; i < entityCount; i++)
            {
                var layer = "Layer_" + (i % layerCount).ToString("D4", System.Globalization.CultureInfo.InvariantCulture)
                    + (longLayerNames ? new string('界', 60) + "\"+<>&" : "");
                Entity entity = (i % 5) switch
                {
                    0 => new Line(), 1 => new Circle(), 2 => new DBText(),
                    3 => new BlockReference { BlockTableRecord = Definitions[(i / 5) % 3].ObjectId },
                    _ => new ProxyEntity()
                };
                entity.Layer = layer;
                var id = Database.ProfileAdd(entity); EntityIds[i] = id;
                var owner = Spaces[i % 3];
                owner.ProfileEntities.Add(id);
                entity.OwnerId = owner.ObjectId;
                if (entity is BlockReference reference)
                    Definitions[(i / 5) % 3].ProfileReferenceIds.Add(id);
                var type = typeNames[i % 5];
                ExpectedTypes.TryGetValue(type, out var typeCount); ExpectedTypes[type] = typeCount + 1;
                ExpectedLayers.TryGetValue(layer, out var layerEntities); ExpectedLayers[layer] = layerEntities + 1;
            }
            Document = new Document { Name = "SimulatedProfile.dwg", Database = Database, MutationCount = 37 };
            Application.DocumentManager.Documents.Add(Document); Application.DocumentManager.MdiActiveDocument = Document;
            Database.ObjectModified += (_, _) => DatabaseEvents++;
            Database.ObjectAppended += (_, _) => DatabaseEvents++;
            Database.ObjectUnappended += (_, _) => DatabaseEvents++;
            Database.ObjectReappended += (_, _) => DatabaseEvents++;
            Database.ObjectErased += (_, _) => DatabaseEvents++;
            Database.ProfileResetMetrics();
        }
    }

    public void Dispose()
    {
        DrawingRevision.Shutdown();
        Application.DocumentManager = previous;
    }
}
