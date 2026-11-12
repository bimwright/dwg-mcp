using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Cad;
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

/// <summary>Actual query/count/select handlers, collector, legacy Describe and final formatter on simulated geometry.</summary>
[Collection("Script handlers")]
public sealed class EntityQueryPageTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly DocumentCollection previous = Application.DocumentManager;
    public EntityQueryPageTests(ITestOutputHelper output)
    {
        this.output = output;
        DrawingRevision.Shutdown(); Application.DocumentManager = new DocumentCollection();
    }

    [Fact]
    public void Numeric_F_10_pages_union_to_the_independent_count_without_duplicates_or_gaps()
    {
        var f = new Fixture();
        foreach (var handle in new long[] { 0x21, 0x10, 0xF, 0x11, 0x2, 0x30 }) f.Add(new Line(), handle);
        var expected = new[] { "2", "F", "10", "11", "21", "30" };
        var request = Paged("handle"); request["limit"] = 2;
        var seen = new List<string>(); var pages = 0;
        while (true)
        {
            var page = Deliver(Query(f, request)); pages++;
            seen.AddRange(Handles(page));
            Assert.Equal(6, page.Value<long>("exact_match_count"));
            if (!page["output"].Value<bool>("has_more")) break;
            request = Next(page);
            Assert.True(pages < 6);
        }
        Assert.Equal(expected, seen);
        Assert.Equal(3, pages);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(6, Count(f, new JObject { ["space"] = "model" }).Value<long>("count"));
        AssertReadOnly(f);
    }

    [Theory]
    [InlineData("intersects", 3)]
    [InlineData("contains", 1)]
    public void Cheap_filters_precede_inclusive_3D_extents_and_projection_reuses_each_bbox_read(string mode, int expected)
    {
        var f = new Fixture();
        var touch = f.Add(new Line(), 0xF, min: new Point3d(-2, -2, -2), max: new Point3d(0, 0, 0));
        var inside = f.Add(new Line(), 0x10);
        var partial = f.Add(new Line(), 0x11, min: new Point3d(-.5, 0, 0), max: new Point3d(.5, 1, 1));
        var wrongZ = f.Add(new Line(), 0x12, min: new Point3d(0, 0, 2), max: new Point3d(1, 1, 3));
        var wrongType = f.Add(new Circle(), 0x13);
        var wrongLayer = f.Add(new Line(), 0x14, layer: "OTHER");
        var wrongColor = f.Add(new Line(), 0x15, color: 2);
        foreach (var entity in new[] { wrongType, wrongLayer, wrongColor }) entity.QueryExtentsFailure = new InvalidOperationException("Cheap filter must exclude this before extents.");
        var request = Paged("handle", "extents");
        request["entity_type"] = "LINE"; request["layer"] = "a"; request["color_index"] = 7;
        request["bbox"] = Box(0, 0, 0, 1, 1, 1); request["bbox_mode"] = mode;
        var page = Deliver(Query(f, request));
        Assert.Equal(expected, page.Value<long>("exact_match_count"));
        Assert.Equal(expected, page["entities"].Count());
        Assert.True(page["coverage"].Value<bool>("scan_complete"));
        Assert.All(new[] { touch, inside, partial, wrongZ }, e => Assert.Equal(1, e.QueryExtentsReads));
        Assert.All(new[] { wrongType, wrongLayer, wrongColor }, e => Assert.Equal(0, e.QueryExtentsReads));
        Assert.Equal(mode == "contains" ? new[] { "10" } : new[] { "F", "10", "11" }, Handles(page));
        AssertReadOnly(f);
    }

    [Fact]
    public void Named_layout_scope_and_negative_drawing_units_never_fall_back_to_model()
    {
        var f = new Fixture(); f.Database.Insunits = UnitsValue.Meters;
        f.Add(new Line(), 0xF);
        f.Add(new DBText { TextString = "Âm", Position = new Point3d(-2, -3, -4) }, 0x20,
            space: 2, min: new Point3d(-3, -4, -5), max: new Point3d(-1, -2, -3));
        var request = Paged("handle", "text", "position", "extents");
        request["space"] = "layout:Bản vẽ 2"; request["bbox"] = Box(-3, -4, -5, -1, -2, -3); request["bbox_mode"] = "contains";
        var page = Deliver(Query(f, request));
        Assert.Equal(new[] { "20" }, Handles(page));
        Assert.Equal("Bản vẽ 2", page["scope"].Value<string>("name"));
        Assert.Equal("drawing_units_no_conversion", page["units"].Value<string>("coordinates"));
        Assert.Equal("Meters", page["units"].Value<string>("insunits"));
        Assert.Equal(6, page["units"].Value<int>("insunits_code"));
        Assert.Equal(-3d, page["entities"][0]["extents"]["min"].Value<double>("x"));
        Assert.Equal(1, Count(f, new JObject { ["space"] = "layout:Bản vẽ 2", ["bbox"] = request["bbox"], ["bbox_mode"] = "contains" }).Value<long>("count"));
        var missing = Execute(f, new JObject { ["paged"] = true, ["space"] = "layout:missing" });
        Assert.False(missing.Ok); Assert.StartsWith("QUERY_SPACE_NOT_FOUND:", missing.Error);
        f.Layouts[0].ProfileNameFailure = new InvalidOperationException("Unresolvable layout metadata.");
        var unavailable = Execute(f, request);
        Assert.False(unavailable.Ok); Assert.StartsWith("QUERY_SPACE_UNAVAILABLE:", unavailable.Error);
        AssertReadOnly(f);
    }

    [Fact]
    public void Basic_and_text_projection_do_not_read_extents_or_eager_polyline_geometry()
    {
        var f = new Fixture();
        var polyline = new Polyline(); polyline.QueryPolylineVertices.AddRange(new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0) });
        f.Add(polyline, 0xF);
        f.Add(new DBText { TextString = "DBText Việt", Position = new Point3d(2, 3, 4) }, 0x10);
        f.Add(new MText { Contents = "MText\\P原文", Location = new Point3d(-2, 1, 9) }, 0x11);
        f.Add(new BlockReference { Position = new Point3d(7, 8, 9) }, 0x12);
        var basic = Deliver(Query(f, new JObject { ["paged"] = true }));
        Assert.All(basic["entities"].OfType<JObject>(), row => Assert.Equal(new[] { "handle", "type", "layer" }, row.Properties().Select(p => p.Name)));
        var page = Deliver(Query(f, Paged("text", "position")));
        var rows = page["entities"].ToDictionary(row => row.Value<string>("handle"));
        Assert.Equal("DBText Việt", rows["10"].Value<string>("text"));
        Assert.Equal(4d, rows["10"]["position"].Value<double>("z"));
        Assert.Equal("MText\\P原文", rows["11"].Value<string>("text"));
        Assert.Equal(9d, rows["12"]["position"].Value<double>("z"));
        Assert.Equal(JTokenType.Null, rows["F"]["text"].Type);
        Assert.Contains("text", rows["F"]["unavailable_fields"].Values<string>());
        Assert.Contains("position", rows["F"]["unavailable_fields"].Values<string>());
        Assert.All(f.Entities, entity => Assert.Equal(0, entity.QueryExtentsReads));
        Assert.Equal(0, polyline.QueryVertexReads);
        var invalid = Execute(f, Paged("vertices"));
        Assert.False(invalid.Ok); Assert.Contains("fields", invalid.Error);
        AssertReadOnly(f);
    }

    [Fact]
    public void Supported_text_with_a_null_value_is_explicitly_unavailable_without_losing_the_entity()
    {
        var f = new Fixture(); f.Add(new DBText { TextString = null }, 15);
        var page = Deliver(Query(f, Paged("text")));
        var row = page["entities"][0];
        Assert.Equal(JTokenType.Null, row["text"].Type);
        Assert.Contains("text", row["unavailable_fields"].Values<string>());
        Assert.Equal("unavailable", row["field_unavailability"].Value<string>("text"));
        Assert.Equal(1, page.Value<int>("count")); Assert.True(page["coverage"].Value<bool>("scan_complete"));
    }

    [Fact]
    public void Extents_projection_without_bbox_reads_only_delivered_candidates()
    {
        var f = new Fixture(); for (var i = 0; i < 30; i++) f.Add(new Line(), 100 - i);
        var request = Paged("handle", "extents"); request["limit"] = 3;
        var page = Deliver(Query(f, request));
        Assert.Equal(30, page.Value<long>("exact_match_count"));
        Assert.Equal(3, page["entities"].Count());
        Assert.Equal(3, f.ExtentsReads);
        AssertReadOnly(f);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("nan")]
    [InlineData("inverted")]
    public void Bbox_membership_with_unavailable_extents_has_null_exact_count_and_no_continuation(string fault)
    {
        var f = new Fixture(); f.Add(new Line(), 0xF); var broken = f.Add(new Line(), 0x10); f.Add(new Line(), 0x11);
        if (fault == "throw") broken.QueryExtentsFailure = new InvalidOperationException("No extents.");
        else broken.QueryExtentsOverride = () => fault == "nan"
            ? new Extents3d(new Point3d(double.NaN, 0, 0), new Point3d(1, 1, 1))
            : new Extents3d(new Point3d(2, 0, 0), new Point3d(1, 1, 1));
        var request = Paged("handle"); request["limit"] = 1; request["bbox"] = Box(-10, -10, -10, 10, 10, 10);
        var page = Deliver(Query(f, request));
        Assert.Equal(JTokenType.Null, page["exact_match_count"].Type);
        Assert.Equal(2, page.Value<long>("matched_count"));
        Assert.Equal(1, page["coverage"].Value<long>("extents_unavailable"));
        Assert.False(page["coverage"].Value<bool>("scan_complete"));
        Assert.False(page["output"].Value<bool>("continuation_allowed"));
        Assert.Null(page["output"].Value<string>("next_cursor"));
        Assert.Empty(page["output"]["next_calls"]);
        var count = Count(f, new JObject { ["bbox"] = request["bbox"] });
        Assert.Equal(JTokenType.Null, count["count"].Type);
        Assert.Equal(2, count.Value<long>("matched_count"));
        AssertReadOnly(f);
    }

    [Fact]
    public void Optional_projection_extents_failure_keeps_known_membership_and_delivers_explicit_null()
    {
        var f = new Fixture(); var broken = f.Add(new Line(), 0xF); f.Add(new Line(), 0x10);
        broken.QueryExtentsFailure = new InvalidOperationException("No projected extents.");
        var request = Paged("handle", "extents"); request["limit"] = 1;
        var page = Deliver(Query(f, request));
        Assert.Equal(2, page.Value<long>("exact_match_count"));
        Assert.Equal(JTokenType.Null, page["entities"][0]["extents"].Type);
        Assert.Contains("extents", page["entities"][0]["unavailable_fields"].Values<string>());
        Assert.Equal(0, page["coverage"].Value<long>("extents_unavailable"));
        Assert.Equal(1, page["coverage"].Value<long>("projection_extents_unavailable"));
        Assert.True(page["output"].Value<bool>("continuation_allowed"));
        Assert.NotEmpty(page["output"].Value<string>("next_cursor"));
        AssertReadOnly(f);
    }

    [Theory]
    [InlineData("entity", "entity_read_failed", 2)]
    [InlineData("filter", "filter_read_failed", 2)]
    [InlineData("enumerator", "enumeration_failed", 1)]
    public void Unreadable_eligible_population_never_promotes_partial_matches_to_an_exact_total_or_cursor(string fault, string counter, int observed)
    {
        var f = new Fixture(); f.Add(new Line(), 0xF); var broken = f.Add(new Line(), 0x10); f.Add(new Line(), 0x11);
        var request = Paged("handle"); request["limit"] = 1;
        if (fault == "entity") broken.ObjectId.Slot.OpenFailure = new InvalidOperationException("Unopened entity.");
        else if (fault == "filter") { broken.ProfileLayerFailure = new InvalidOperationException("Unreadable filter property."); request["layer"] = "A"; }
        else f.Spaces[0].ProfileEnumerationFaultAfter = 1;
        var page = Deliver(Query(f, request));
        Assert.Equal(JTokenType.Null, page["exact_match_count"].Type);
        Assert.Equal(observed, page.Value<long>("matched_count"));
        Assert.Equal(1, page["coverage"].Value<long>(counter));
        Assert.False(page["output"].Value<bool>("continuation_allowed"));
        Assert.Empty(page["output"]["next_calls"]);
        AssertReadOnly(f);
    }

    [Theory]
    [InlineData("tamper", "CURSOR_INVALID:")]
    [InlineData("fields", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("filter", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("bbox", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("space", "CURSOR_QUERY_MISMATCH:")]
    [InlineData("entity", "QUERY_CHANGED:")]
    [InlineData("document_lifetime", "QUERY_CHANGED:")]
    public void Signed_cursor_rejects_changed_context_before_database_access(string change, string prefix)
    {
        var f = new Fixture(); for (var i = 0; i < 4; i++) f.Add(new Line(), i + 15);
        var first = Paged("handle"); first["limit"] = 1;
        var next = Next(Deliver(Query(f, first)));
        var document = f.Document;
        switch (change)
        {
            case "tamper": var token = next.Value<string>("cursor"); next["cursor"] = (token[0] == 'A' ? "B" : "A") + token.Substring(1); break;
            case "fields": next["fields"] = new JArray("handle", "layer"); break;
            case "filter": next["layer"] = "OTHER"; break;
            case "bbox": next["bbox"] = Box(-1, -1, -1, 2, 2, 2); break;
            case "space": next["space"] = "layout:Layout A"; break;
            case "entity": f.Database.Raise("modified", new Line()); break;
            case "document_lifetime": document = new Document { Name = f.Document.Name, Database = f.Database }; break;
        }
        f.Database.ProfileResetMetrics();
        var result = new QueryEntitiesHandler().Execute(document, next);
        Assert.False(result.Ok); Assert.StartsWith(prefix, result.Error);
        Assert.Empty(f.Database.ProfileOpenedIds);
        Assert.Equal(0, f.Database.ProfileTransactionStarts);
    }

    [Fact]
    public void Revision_changed_during_scan_refuses_a_mixed_page()
    {
        var f = new Fixture(); for (var i = 0; i < 8; i++) f.Add(new Line(), i + 15);
        var raised = false;
        f.Spaces[0].ProfileOnMoveNext = index => { if (index == 3 && !raised) { raised = true; f.Database.Raise("modified", new Line()); } };
        var result = Execute(f, Paged("handle"));
        Assert.True(raised); Assert.False(result.Ok); Assert.StartsWith("QUERY_CHANGED:", result.Error);
        Assert.Null(result.Result);
        Assert.Equal(0, f.Database.TransactionManager.NumberOfActiveTransactions);
    }

    [Fact]
    public void Legacy_query_count_select_order_fields_and_full_geometry_use_the_actual_original_paths()
    {
        var f = new Fixture();
        f.Add(new Line { StartPoint = new Point3d(0, 0, 0), EndPoint = new Point3d(3, 4, 0) }, 0x10,
            min: new Point3d(0, 0, 0), max: new Point3d(3, 4, 0));
        f.Add(new Line(), 0xF); f.Add(new Line(), 0x11);
        var legacy = Body(new QueryEntitiesHandler().Execute(f.Document, new JObject { ["entity_type"] = "LINE", ["limit"] = 2 }));
        Assert.Null(legacy["query_page_version"]);
        Assert.Equal(new[] { "10", "F" }, legacy["entities"].Select(row => row.Value<string>("handle")));
        Assert.All(legacy["entities"].OfType<JObject>(), row => Assert.Equal(new[] { "handle", "type", "layer", "color_index" }, row.Properties().Select(p => p.Name)));
        Assert.Equal(0, f.ExtentsReads);
        Assert.Equal(3, Body(new CountEntitiesHandler().Execute(f.Document, new JObject { ["limit"] = 1 })).Value<int>("count"));
        Assert.Equal(new[] { "10", "F" }, Body(new SelectByLayerHandler().Execute(f.Document, new JObject { ["layer"] = "a", ["limit"] = 2 }))["handles"].Values<string>());
        Assert.Equal(new[] { "10", "F", "11" }, Body(new SelectByTypeHandler().Execute(f.Document, new JObject { ["entity_type"] = "Line" }))["handles"].Values<string>());
        var detailed = Body(new QueryEntitiesHandler().Execute(f.Document, new JObject { ["include_geometry"] = true, ["limit"] = 1 }));
        Assert.Equal(5d, detailed["entities"][0].Value<double>("length"));
        Assert.Equal(3d, detailed["entities"][0]["end"].Value<double>("x"));
        Assert.NotNull(detailed["entities"][0]["extents"]);
        Assert.Equal(1, f.ExtentsReads);
        AssertReadOnly(f);
    }

    [Theory]
    [InlineData("TEXT", "DBText")]
    [InlineData("LWPOLYLINE", "Polyline")]
    [InlineData("INSERT", "BlockReference")]
    public void Shared_type_aliases_work_on_actual_legacy_query_count_and_select(string alias, string expectedType)
    {
        var f = new Fixture(); f.Add(new DBText(), 0xF); f.Add(new Polyline(), 0x10); f.Add(new BlockReference(), 0x11);
        var args = new JObject { ["entity_type"] = alias };
        var query = Body(new QueryEntitiesHandler().Execute(f.Document, args));
        Assert.Equal(1, query.Value<int>("count"));
        Assert.Equal(expectedType, query["entities"][0].Value<string>("type"));
        Assert.Equal(1, Body(new CountEntitiesHandler().Execute(f.Document, args)).Value<int>("count"));
        Assert.Equal(1, Body(new SelectByTypeHandler().Execute(f.Document, args)).Value<int>("count"));
        AssertReadOnly(f);
    }

    [Fact]
    public void Legacy_full_polyline_projection_keeps_vertices_while_the_new_default_avoids_them()
    {
        var f = new Fixture(); var polyline = new Polyline { Closed = true, Area = .5 };
        polyline.QueryPolylineVertices.AddRange(new[] { new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(1, 1, 0) });
        f.Add(polyline, 0xF);
        var result = Body(new QueryEntitiesHandler().Execute(f.Document, new JObject { ["include_geometry"] = true }));
        Assert.Equal(3, result["entities"][0].Value<int>("vertex_count"));
        Assert.Equal(3, result["entities"][0]["vertices"].Count());
        Assert.Equal(3, polyline.QueryVertexReads);
        Assert.True(result["entities"][0].Value<bool>("is_closed"));
        AssertReadOnly(f);
    }

    [Fact]
    public void Simulated_50000_entity_full_paging_reports_real_AABB_rescan_cost_and_fits_final_SDK_bytes()
    {
        var f = new Fixture();
        for (var i = 0; i < 50000; i++)
        {
            var x = i % 251 - 125; var y = i / 251 - 100; var z = i % 9 - 4;
            f.Add(new Line(), 50014 - i, min: new Point3d(x, y, z), max: new Point3d(x + .5, y + .5, z + .5));
        }
        var original = Paged("handle", "type", "layer", "extents"); original["limit"] = 128;
        original["bbox"] = Box(-1000, -1000, -1000, 1000, 1000, 1000);
        f.ResetReads(); var firstClock = Stopwatch.StartNew();
        var defaultResult = Fit(Query(f, original), 16384); var defaultBytes = FinalBytes(defaultResult); firstClock.Stop();
        Assert.True(defaultBytes <= 16384); Assert.Equal(50000, f.ExtentsReads);
        Assert.InRange(Deliver(defaultResult)["entities"].Count(), 1, 127);
        output.WriteLine(JsonConvert.SerializeObject(new { kind = "SIMULATED_ENTITY_PAGE_DEFAULT", entity_count = 50000,
            elapsed_ms = firstClock.Elapsed.TotalMilliseconds, extents_getter_reads = f.ExtentsReads, final_mcp_utf8_bytes = defaultBytes, cap = 16384 }));
        Assert.True(firstClock.Elapsed.TotalMilliseconds <= 5000);

        var request = (JObject)original.DeepClone(); request["max_output_bytes"] = 65536;
        var seen = new List<string>(50000); var pageTimes = new List<double>(); long totalExtentsReads = 0;
        var total = Stopwatch.StartNew();
        while (true)
        {
            f.ResetReads(); var watch = Stopwatch.StartNew();
            var result = Fit(Query(f, request), 65536); var bytes = FinalBytes(result); watch.Stop();
            var page = Deliver(result); var handles = Handles(page).ToArray();
            pageTimes.Add(watch.Elapsed.TotalMilliseconds); totalExtentsReads += f.ExtentsReads; seen.AddRange(handles);
            Assert.Equal(50000, f.ExtentsReads); // Exactly once for every bbox-eligible candidate, never again in projection.
            Assert.Equal(50000, page.Value<long>("exact_match_count"));
            Assert.True(bytes <= 65536); Assert.InRange(handles.Length, 1, 128);
            Assert.True(watch.Elapsed.TotalMilliseconds <= 5000,
                "SIMULATED page " + pageTimes.Count + " took " + watch.Elapsed.TotalMilliseconds + "ms; eligible extents reads=" + f.ExtentsReads + ", delivered=" + handles.Length);
            if (pageTimes.Count == 1 || pageTimes.Count % 100 == 0 || !page["output"].Value<bool>("has_more"))
                output.WriteLine(JsonConvert.SerializeObject(new { kind = "SIMULATED_ENTITY_PAGE", page = pageTimes.Count,
                    elapsed_ms = watch.Elapsed.TotalMilliseconds, extents_getter_reads = f.ExtentsReads,
                    delivered = handles.Length, final_mcp_utf8_bytes = bytes, cap = 65536 }));
            if (!page["output"].Value<bool>("has_more")) break;
            request = Next(page);
            Assert.True(total.Elapsed.TotalSeconds <= 120,
                "SIMULATED full paging exceeded120s: pages=" + pageTimes.Count + ", total_ms=" + total.Elapsed.TotalMilliseconds
                + ", total_extents_reads=" + totalExtentsReads + "; retain partial metrics, review O(N per page), do not cache or rerun this benchmark.");
            Assert.True(pageTimes.Count <= 1000);
        }
        total.Stop();
        Assert.Equal(50000, seen.Count); Assert.Equal(50000, seen.Distinct().Count());
        Assert.Equal(Enumerable.Range(15, 50000).Select(value => value.ToString("X", CultureInfo.InvariantCulture)), seen);
        Assert.True(total.Elapsed.TotalSeconds <= 120);
        output.WriteLine(JsonConvert.SerializeObject(new { kind = "SIMULATED_ENTITY_ALL_PAGES", entity_count = 50000,
            total_pages = pageTimes.Count, total_ms = total.Elapsed.TotalMilliseconds, total_extents_getter_reads = totalExtentsReads,
            first_page_ms = pageTimes[0], middle_page_ms = pageTimes[pageTimes.Count / 2], last_page_ms = pageTimes.Last(),
            max_page_ms = pageTimes.Max(), max_per_page_ms = 5000, geometry = "AABB recomputed from8 stored vertices on every getter",
            native_AutoCAD_runtime = false, complexity = "full rescan per page; no cache; fixture allocation excluded" }));
        AssertReadOnly(f);
    }

    private static JObject Paged(params string[] fields) => new() { ["paged"] = true, ["fields"] = new JArray(fields) };
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_empty_reads_hint_only_live_model_space_types(bool select)
    {
        var f = new Fixture();
        f.Add(new Circle(), 0x10);
        f.Add(new DBText(), 0x11);
        var erased = f.Add(new MText(), 0x12);
        erased.ObjectId.Slot.Erased = true;
        var layout = f.Add(new Line(), 0x13, space: 1);
        IAcadCommand handler = select ? new SelectByTypeHandler() : new CountEntitiesHandler();

        var request = new JObject { ["entity_type"] = "AcDbText", ["layer"] = "missing" };
        var empty = Body(handler.Execute(f.Document, request));
        Assert.Equal(0, empty.Value<int>("count"));
        Assert.Equal("model", empty["hint"].Value<string>("space"));
        Assert.Equal(new[] { typeof(Circle).FullName, typeof(DBText).FullName }, empty["hint"]["available_types"].Values<string>());
        Assert.DoesNotContain(erased.ObjectId, f.Database.ProfileOpenedIds);
        Assert.DoesNotContain(layout.ObjectId, f.Database.ProfileOpenedIds);
        Assert.Equal(1, f.Spaces[0].ProfileEnumerationStarts);

        request.Remove("layer");
        var match = Body(handler.Execute(f.Document, request));
        Assert.Equal(1, match.Value<int>("count"));
        Assert.Null(match["hint"]);
    }

    [Fact]
    public void Spatial_zero_hint_describes_the_named_layout_before_bbox_and_requires_an_exact_zero()
    {
        var f = new Fixture();
        f.Add(new Line(), 0x10);
        var text = f.Add(new DBText(), 0x11, space: 1);
        f.Add(new MText(), 0x12, space: 1);
        f.Add(new Circle(), 0x13, space: 2);
        var request = new JObject { ["entity_type"] = "AcDbText", ["space"] = "layout:Layout A", ["bbox"] = Box(10, 10, 10, 20, 20, 20) };
        var empty = Count(f, request);
        Assert.Equal(0, empty.Value<int>("count"));
        Assert.Equal("layout:Layout A", empty["hint"].Value<string>("space"));
        Assert.Equal(new[] { typeof(DBText).FullName, typeof(MText).FullName }, empty["hint"]["available_types"].Values<string>());

        text.ObjectId.Slot.OpenFailure = new InvalidOperationException("unavailable entity");
        var incomplete = Count(f, request);
        Assert.Equal(JTokenType.Null, incomplete["count"].Type);
        Assert.Null(incomplete["hint"]);
    }

    [Fact]
    public void Type_hint_retains_twenty_distinct_names_in_stable_order()
    {
        var types = Enumerable.Range(1, 24).Select(rank => typeof(Line).MakeArrayType(rank)).ToArray();
        var hint = new CadEntityTypeHint();
        foreach (var type in Enumerable.Reverse(types).Concat(types)) hint.Observe(type);
        var result = hint.ToJson("model");
        Assert.Equal(types.Select(type => type.FullName).OrderBy(name => name, StringComparer.Ordinal).Take(20),
            result["available_types"].Values<string>());
        Assert.True(result.Value<bool>("types_truncated"));
    }

    private static JObject Box(double x0, double y0, double z0, double x1, double y1, double z1)
        => new() { ["min"] = new JArray(x0, y0, z0), ["max"] = new JArray(x1, y1, z1) };
    private static CommandResult Execute(Fixture f, JObject request) => new QueryEntitiesHandler().Execute(f.Document, request);
    private static JObject Query(Fixture f, JObject request) => Body(Execute(f, request));
    private static JObject Count(Fixture f, JObject request) => Body(new CountEntitiesHandler().Execute(f.Document, request));
    private static JObject Body(CommandResult result) { Assert.True(result.Ok, result.Error); return JObject.FromObject(result.Result); }
    private static CallToolResult Fit(JObject raw, int cap) => EntityQueryResponse.Fit(new CallToolResult
    {
        Content = new[] { new TextContentBlock { Text = new JObject { ["ok"] = true, ["result"] = raw }.ToString(Formatting.None) } }
    }, cap);
    private static JObject Deliver(JObject raw) => Deliver(Fit(raw, 16384));
    private static JObject Deliver(CallToolResult result)
        => (JObject)JObject.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text)["result"];
    private static IEnumerable<string> Handles(JObject page) => page["entities"].Select(row => row.Value<string>("handle"));
    private static int FinalBytes(CallToolResult result)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length;
    private static JObject Next(JObject page)
    {
        var call = Assert.Single(page["output"]["next_calls"]);
        Assert.Equal("dwg_query_entities", call.Value<string>("tool"));
        var args = (JObject)call["arguments"].DeepClone();
        if (args["fields"]?.Type == JTokenType.String) args["fields"] = JArray.Parse(args.Value<string>("fields"));
        if (args["bbox"]?.Type == JTokenType.String) args["bbox"] = JObject.Parse(args.Value<string>("bbox"));
        return args;
    }
    private static void AssertReadOnly(Fixture f)
    {
        Assert.All(f.Database.ProfileOpenModes, mode => Assert.Equal(OpenMode.ForRead, mode));
        Assert.Equal(0, f.Database.ProfileMutations); Assert.Equal(0, f.DatabaseEvents);
        Assert.Equal(23, f.Document.MutationCount); Assert.Same(f.Editor, f.Document.Editor);
        Assert.Equal(0, f.Document.NativeGetterReads);
        Assert.Equal(0, f.Database.TransactionManager.NumberOfActiveTransactions);
        Assert.Equal(f.Database.ProfileTransactionStarts, f.Database.ProfileTransactionDisposals);
    }
    private sealed class Fixture
    {
        internal Database Database { get; } = new();
        internal Document Document { get; }
        internal object Editor { get; }
        internal BlockTableRecord[] Spaces { get; }
        internal Layout[] Layouts { get; }
        internal List<Entity> Entities { get; } = new();
        internal int DatabaseEvents;
        internal int ExtentsReads => Entities.Sum(entity => entity.QueryExtentsReads);
        internal Fixture()
        {
            var blocks = new BlockTable(); Database.BlockTableId = Database.ProfileAdd(blocks);
            Layouts = new[] { new Layout { LayoutName = "Layout A" }, new Layout { LayoutName = "Bản vẽ 2" } };
            Spaces = new[]
            {
                new BlockTableRecord { Name = BlockTableRecord.ModelSpace, IsLayout = true },
                new BlockTableRecord { Name = "*Paper_Space", IsLayout = true, LayoutId = Database.ProfileAdd(Layouts[0]) },
                new BlockTableRecord { Name = "*Paper_Space0", IsLayout = true, LayoutId = Database.ProfileAdd(Layouts[1]) }
            };
            foreach (var space in Spaces) { var id = Database.ProfileAdd(space); blocks.ProfileRecords.Add(id); blocks.ProfileNames.Add(space.Name, id); }
            Document = new Document { Name = "SimulatedEntityPage.dwg", Database = Database, MutationCount = 23 };
            Editor = Document.Editor; Application.DocumentManager.Documents.Add(Document); Application.DocumentManager.MdiActiveDocument = Document;
            Database.ObjectModified += (_, _) => DatabaseEvents++;
            Database.ObjectAppended += (_, _) => DatabaseEvents++;
            Database.ObjectErased += (_, _) => DatabaseEvents++;
            Database.ObjectUnappended += (_, _) => DatabaseEvents++;
            Database.ObjectReappended += (_, _) => DatabaseEvents++;
        }
        internal Entity Add(Entity entity, long handle, int space = 0, string layer = "A", short color = 7, Point3d? min = null, Point3d? max = null)
        {
            entity.Layer = layer; entity.ColorIndex = color;
            var a = min ?? new Point3d(0, 0, 0); var b = max ?? new Point3d(1, 1, 1);
            entity.QueryVertices = new[] { a, new Point3d(b.X, a.Y, a.Z), new Point3d(a.X, b.Y, a.Z), new Point3d(a.X, a.Y, b.Z),
                new Point3d(b.X, b.Y, a.Z), new Point3d(b.X, a.Y, b.Z), new Point3d(a.X, b.Y, b.Z), b };
            var id = Database.ProfileAdd(entity); entity.Handle = new Handle(handle); Spaces[space].ProfileEntities.Add(id); Entities.Add(entity);
            return entity;
        }
        internal void ResetReads() { Database.ProfileResetMetrics(); foreach (var entity in Entities) entity.QueryExtentsReads = 0; }
    }
    public void Dispose() { DrawingRevision.Shutdown(); Application.DocumentManager = previous; }
}
