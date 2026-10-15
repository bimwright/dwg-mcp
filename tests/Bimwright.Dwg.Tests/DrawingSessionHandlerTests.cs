using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;
using FactAttribute = Bimwright.Dwg.Tests.Helpers.WindowsFactAttribute;
using TheoryAttribute = Bimwright.Dwg.Tests.Helpers.WindowsTheoryAttribute;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class DrawingSessionHandlerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-session-" + Guid.NewGuid().ToString("N"));
    private readonly DocumentCollection previous = Application.DocumentManager;
    private readonly int previousSdi = Application.Sdi;
    private readonly DocumentCollection documents = new DocumentCollection();
    private string FilePath => Path.Combine(root, "Nhà máy.dwg");

    public DrawingSessionHandlerTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath, "Test fixture only; never passed to Autodesk.");
        Application.DocumentManager = documents;
        Application.Sdi = 0;
        DocumentInvoker.Initialize();
    }

    private CommandResult Open(object extra = null)
    {
        var request = extra == null ? new JObject() : JObject.FromObject(extra);
        request["path"] = FilePath;
        return DocumentInvoker.InvokeSession(() => new OpenDrawingHandler().ExecuteSession(request));
    }

    [Fact]
    public void Opens_from_zero_document_state_without_lock_and_reports_actual_read_only()
    {
        var result = Open(new { read_only = true });
        Assert.True(result.Ok, result.Error);
        var json = JObject.FromObject(result.Result);
        Assert.True(json.Value<bool>("opened"));
        Assert.False(json.Value<bool>("was_already_open"));
        Assert.True(json.Value<bool>("activated"));
        Assert.True(json.Value<bool>("read_only"));
        Assert.Equal("Nhà máy.dwg", json.Value<string>("title"));
        Assert.Equal(1, documents.OpenCalls);
        Assert.Equal(0, documents.MdiActiveDocument.LockCount);
    }

    [Fact]
    public void Activate_false_keeps_prior_drawing_and_never_locks_it()
    {
        var original = new Document { Name = Path.Combine(root, "Original.dwg"), MutationCount = 3 };
        documents.Documents.Add(original);
        documents.MdiActiveDocument = original;
        var result = Open(new { activate = false });
        Assert.True(result.Ok, result.Error);
        Assert.Same(original, documents.MdiActiveDocument);
        Assert.Equal(2, documents.Documents.Count);
        Assert.False(JObject.FromObject(result.Result).Value<bool>("activated"));
        Assert.Equal(0, original.LockCount);
        Assert.Equal(3, original.MutationCount);
    }

    [Fact]
    public void Already_open_is_reused_even_if_disk_file_has_disappeared()
    {
        var existing = new Document { Name = FilePath.ToUpperInvariant() };
        documents.Documents.Add(existing);
        File.Delete(FilePath);
        var result = Open();
        Assert.True(result.Ok, result.Error);
        Assert.True(JObject.FromObject(result.Result).Value<bool>("was_already_open"));
        Assert.Same(existing, documents.MdiActiveDocument);
        Assert.Equal(0, documents.OpenCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Access_mode_mismatch_never_reopens_or_changes_existing_document(bool readOnly)
    {
        var existing = new Document { Name = FilePath, IsReadOnly = !readOnly };
        documents.Documents.Add(existing);
        Assert.False(Open(new { read_only = readOnly }).Ok);
        Assert.Equal(!readOnly, existing.IsReadOnly);
        Assert.Equal(0, documents.OpenCalls);
    }

    [Theory]
    [InlineData("command")]
    [InlineData("context")]
    [InlineData("sdi")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("timeout")]
    public void Refusals_do_not_open_any_file(string condition)
    {
        var request = new JObject();
        if (condition == "command") documents.Documents.Add(new Document { CommandInProgress = "LINE" });
        if (condition == "context") documents.IsApplicationContext = false;
        if (condition == "sdi") Application.Sdi = 1;
        if (condition == "inactive") request["activate"] = false;
        if (condition == "missing") File.Delete(FilePath);
        if (condition == "timeout") request["timeout_seconds"] = 901;
        Assert.False(Open(request).Ok);
        Assert.Equal(0, documents.OpenCalls);
        Assert.Null(documents.MdiActiveDocument);
    }

    [Fact]
    public void Native_error_warns_against_blind_retry_and_keeps_other_drawings()
    {
        var existing = new Document { Name = Path.Combine(root, "Existing.dwg") };
        documents.Documents.Add(existing);
        documents.MdiActiveDocument = existing;
        documents.OnOpen = (_, _) => throw new InvalidOperationException("Native open refused");
        var result = Open();
        Assert.False(result.Ok);
        Assert.Contains("inspect AutoCAD", result.Error);
        Assert.Same(existing, documents.MdiActiveDocument);
        Assert.Single(documents.Documents);
    }

    [Theory]
    [InlineData("open_drawing")]
    [InlineData("list_recent_drawings")]
    public void Session_commands_cannot_run_inside_a_drawing_batch(string command)
    {
        var executed = 0;
        var result = BatchExecutor.Run(JObject.Parse("{'commands':[{'cmd':'create_line'},{'cmd':'" + command + "'}]}"),
            (_, _) => { executed++; return CommandResult.Success(null); });
        Assert.False(result.Ok);
        Assert.Equal(0, executed);
        IAcadSessionCommand handler = command == "open_drawing" ? new OpenDrawingHandler() : new ListRecentDrawingsHandler();
        Assert.False(handler.Execute(new Document(), new JObject()).Ok);
    }

    [Fact]
    public void Recent_missing_registry_list_is_reported_without_requiring_a_drawing_or_creating_a_key()
    {
        var previousRoot = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.Current.UserRegistryProductRootKey;
        try
        {
            Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.Current.UserRegistryProductRootKey = @"Software\Bimwright\Missing-" + Guid.NewGuid().ToString("N");
            var result = DocumentInvoker.InvokeSession(() => new ListRecentDrawingsHandler().ExecuteSession(JValue.CreateNull()));
            Assert.True(result.Ok, result.Error);
            var json = JObject.FromObject(result.Result);
            Assert.False(json.Value<bool>("registry_found"));
            Assert.Equal(0, json.Value<int>("count"));
            Assert.Contains("absolute path", json.Value<string>("instruction"));
            Assert.Equal(0, documents.OpenCalls);
            Assert.Null(documents.MdiActiveDocument);
        }
        finally { Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.Current.UserRegistryProductRootKey = previousRoot; }
    }

    [Theory]
    [InlineData("{'path':'x.dwg','activate':'yes'}")]
    [InlineData("{'path':'x.dwg','read_only':1}")]
    [InlineData("{'path':'x.dwg','timeout_seconds':1.5}")]
    [InlineData("{}")]
    public void Open_schema_rejects_bad_wire_types_before_native_calls(string json)
    {
        var result = new OpenDrawingHandler().ExecuteSession(JObject.Parse(json));
        Assert.False(result.Ok);
        Assert.Equal(0, documents.OpenCalls);
    }

    [Fact]
    public void Regular_tools_still_require_a_document_and_offer_the_recent_open_workflow()
    {
        var error = Assert.Throws<InvalidOperationException>(() => DocumentInvoker.Invoke(_ => 1));
        Assert.Contains("dwg_list_recent_drawings", error.Message);
        Assert.Contains("dwg_open_drawing", error.Message);
    }

    public void Dispose()
    {
        DocumentInvoker.Shutdown();
        Application.DocumentManager = previous;
        Application.Sdi = previousSdi;
        Directory.Delete(root, recursive: true);
    }
}

public class DrawingSessionContractTests
{
    [Theory]
    [InlineData("relative.dwg")]
    [InlineData(@"C:relative.dwg")]
    [InlineData(@"\root-relative.dwg")]
    [InlineData("https://example.test/model.dwg")]
    [InlineData(@"C:\Models\model.dxf")]
    [InlineData(@"C:\Models\template.dwt")]
    [InlineData(@"\\?\C:\Models\model.dwg")]
    public void Unsupported_paths_are_refused(string path)
        => Assert.Throws<ArgumentException>(() => DrawingSessionContract.NormalizeDwgPath(path));

    [Fact]
    public void Ordinary_unc_paths_and_quoted_unicode_drive_paths_are_supported()
    {
        Assert.Equal(@"\\server\share\Nhà máy.dwg", DrawingSessionContract.NormalizeDwgPath(@"\\server\share\Nhà máy.dwg"));
        Assert.Equal(@"C:\Models\Nhà máy.DWG", DrawingSessionContract.NormalizeDwgPath("  \"C:\\Models\\Nhà máy.DWG\"  "));
    }

    [Fact]
    public void Recent_parser_preserves_numeric_mru_order_filters_before_limit_and_deduplicates()
    {
        var values = new Dictionary<string, object>
        {
            ["File10"] = @"C:\Models\Plant ten.dwg", ["File1"] = @"C:\Models\Other.dwg",
            ["File2"] = @"C:\Models\Plant two.dwg", ["FilePinned2"] = 1,
            ["FileTime2"] = "123.0", ["File3"] = @"c:\models\plant TWO.dwg",
            ["File4"] = @"C:\Models\Plant.dxf", ["File5"] = 12, ["File6"] = "relative.dwg",
            ["File7"] = "", ["File0"] = @"C:\Models\Zero.dwg", ["File-1"] = @"C:\Models\Negative.dwg"
        };
        var files = RecentDrawingList.Parse(values, "PLANT", 2);
        Assert.Equal(new[] { 2, 10 }, files.Select(file => file.Index));
        Assert.True(files[0].Pinned);
        Assert.False(files[1].Pinned);
        Assert.Equal(@"C:\Models\Plant two.dwg", files[0].Path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Recent_limit_is_bounded(int limit)
        => Assert.Throws<ArgumentException>(() => RecentDrawingList.Parse(null, maxItems: limit));

    [Fact]
    public void Long_response_wait_is_scoped_to_open_and_respects_the_requested_budget()
    {
        Assert.Equal(600, DrawingSessionContract.ReadTimeoutSeconds("open_drawing", null));
        Assert.Equal(900, DrawingSessionContract.ReadTimeoutSeconds("open_drawing", JObject.Parse("{'timeout_seconds':900}")));
        Assert.Equal(30, DrawingSessionContract.ReadTimeoutSeconds("get_drawing_info", JObject.Parse("{'timeout_seconds':900}")));
    }
}
