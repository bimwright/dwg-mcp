using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public class DrawingSessionToolTests
{
    private static MethodInfo Tool(string name)
        => Assert.Single(typeof(MetaTools).GetMethods(), method =>
            method.GetCustomAttribute<McpServerToolAttribute>()?.Name == name);

    [Fact]
    public async Task Recent_drawings_is_a_read_and_forwards_bounded_name_filter()
    {
        var tool = Tool("dwg_list_recent_drawings");
        Assert.True(tool.GetCustomAttribute<McpServerToolAttribute>().ReadOnly);
        Assert.Equal(new[] { "name", "max_items" }, tool.GetParameters().Select(p => p.Name));
        var capture = await Capture.Send(() => (Task<string>)tool.Invoke(null, new object[] { "Plant", 12 }));
        Assert.Equal("list_recent_drawings", capture.Command);
        Assert.Equal("Plant", capture.Json().Value<string>("name"));
        Assert.Equal(12, capture.Json().Value<int>("max_items"));
    }

    [Fact]
    public async Task Open_drawing_forwards_explicit_access_and_activation_with_long_wait()
    {
        var tool = Tool("dwg_open_drawing");
        Assert.False(tool.GetCustomAttribute<McpServerToolAttribute>().ReadOnly);
        Assert.Equal(new[] { "path", "activate", "read_only", "timeout_seconds" }, tool.GetParameters().Select(p => p.Name));
        var capture = await Capture.Send(() => (Task<string>)tool.Invoke(null,
            new object[] { @"D:\Models\Plant.dwg", true, true, 900 }));
        Assert.Equal("open_drawing", capture.Command);
        Assert.Equal(@"D:\Models\Plant.dwg", capture.Json().Value<string>("path"));
        Assert.True(capture.Json().Value<bool>("read_only"));
        Assert.Equal(900, capture.Json().Value<int>("timeout_seconds"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(901)]
    public async Task Invalid_open_wait_is_refused_before_dispatch(int wait)
    {
        var tool = Tool("dwg_open_drawing");
        var reply = await Capture.Local(() => (Task<string>)tool.Invoke(null,
            new object[] { @"D:\Models\Plant.dwg", true, false, wait }));
        Assert.False(reply.Value<bool>("ok"));
        Assert.Contains("timeout_seconds", reply.Value<string>("error"));
    }
}
