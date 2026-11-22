using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Presentation;
using Bimwright.Dwg.Tests.Reports;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ReportsPresentationTests
{
    [Fact]
    public void A_report_matches_the_same_open_drawing_or_the_same_file_reopened()
    {
        var doc = new ReportDocument { Token = "token-a", Path = @"C:\Work\Plan.dwg" };
        Assert.True(ReportDocumentMatch.Matches(doc, new ActiveDocumentIdentity { Token = "token-a" }));
        Assert.True(ReportDocumentMatch.Matches(doc, new ActiveDocumentIdentity { Token = "reopened", Path = @"c:\work\plan.DWG" }));
        Assert.False(ReportDocumentMatch.Matches(doc, new ActiveDocumentIdentity { Token = "TOKEN-A", Path = @"C:\Work\Other.dwg" }));
        // Two unsaved drawings never match by their missing path.
        Assert.False(ReportDocumentMatch.Matches(new ReportDocument { Token = "x" }, new ActiveDocumentIdentity { Token = "y" }));
        Assert.False(ReportDocumentMatch.Matches(null, new ActiveDocumentIdentity()));
        Assert.False(ReportDocumentMatch.Matches(new ReportDocument(), null));
    }

    [Fact]
    public void A_live_link_navigates_with_its_entities_and_a_dimmed_link_does_not()
    {
        var open = new List<ActiveDocumentIdentity> { new() { Token = "site" }, new() { Token = "plan" } };
        var nav = new FakeNavigation();
        var document = new ReportDocument { Ref = "d0", Token = "plan", Title = "plan.dwg" };
        var link = new ReportLink { Label = "walls", Doc = "d0", Entities = new List<ReportEntity> { new() { Handle = "2A" }, new() { Handle = "2B" } } };
        var vm = new ReportLinkViewModel(document, link, nav, target => open.Any(candidate => ReportDocumentMatch.Matches(target, candidate)));

        Assert.True(vm.IsLive);
        Assert.Equal(2, vm.EntityCount);
        vm.OpenCommand.Execute(null);
        Assert.Same(document, nav.LastRequest.Document);
        Assert.Equal(new[] { "2A", "2B" }, nav.LastRequest.Entities.Select(e => e.Handle));

        nav.LastRequest = null;
        open.RemoveAt(1);
        vm.UpdateActiveDocument();
        Assert.False(vm.IsLive);
        Assert.False(vm.OpenCommand.CanExecute(null));
        vm.OpenCommand.Execute(null);
        Assert.Null(nav.LastRequest);
    }

    [Fact]
    public void Window_state_reads_leniently()
    {
        var state = PluginSettings.ReadReportsWindow(JObject.Parse(
            "{\"mode\":\"compact\",\"compact\":{\"left\":10,\"top\":20.5,\"width\":400,\"height\":600},\"wide\":{\"left\":\"x\"},\"textScale\":125}"));
        Assert.Equal(ReportsWindowMode.Compact, state.Mode);
        Assert.Equal(400, state.Compact.Width);
        Assert.Null(state.Wide);
        Assert.Equal(125, state.TextScale);

        var fallback = PluginSettings.ReadReportsWindow(JObject.Parse("{\"mode\":\"tall\",\"textScale\":133}"));
        Assert.Equal(ReportsWindowMode.Wide, fallback.Mode);
        Assert.Equal(100, fallback.TextScale);
        Assert.Equal(ReportsWindowMode.Wide, PluginSettings.ReadReportsWindow(null).Mode);
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("3650", 3650)]
    [InlineData("0", 30)]
    [InlineData("3651", 30)]
    [InlineData("\"7\"", 30)]
    [InlineData("7.5", 30)]
    [InlineData("null", 30)]
    public void Retention_days_fall_back_to_thirty(string json, int expected)
        => Assert.Equal(expected, PluginSettings.NormalizeReportRetentionDays(JToken.Parse(json)));
}
