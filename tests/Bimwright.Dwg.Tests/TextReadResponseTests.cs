using Bimwright.Dwg.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class TextReadResponseTests
{
    [Theory]
    [InlineData("Error: argument binding failed before plugin invocation.")]
    [InlineData("{\"message\":\"SDK tool error\"}")]
    public void Genuine_SDK_errors_are_preserved_instead_of_becoming_plugin_compatibility_errors(string text)
    {
        var original = new CallToolResult { IsError = true, Content = new[] { new TextContentBlock { Text = text } } };
        var fitted = TextReadResponse.Fit(original, 4096);
        Assert.True(fitted.IsError);
        if (text.StartsWith("{"))
            Assert.Equal(JObject.Parse(text).Value<string>("message"), JObject.Parse(((TextContentBlock)fitted.Content[0]).Text).Value<string>("message"));
        else Assert.Equal(text, ((TextContentBlock)fitted.Content[0]).Text);
        Assert.DoesNotContain("TEXT_FEATURE_UNSUPPORTED", ((TextContentBlock)fitted.Content[0]).Text);
        Assert.True(ResponseBudget.Measure(fitted) <= 4096);
    }
}
