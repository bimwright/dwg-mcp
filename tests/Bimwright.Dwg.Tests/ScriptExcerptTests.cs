using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Scripting;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class ScriptExcerptTests
{
    private const string Seven = "var a = 1;\nvar b = 2;\nvar c = 3;\nvar d = 4;\nvar e = 5;\nvar f = 6;\nvar g = 7;";

    private static JArray Excerpt(string code, int line, int column)
        => JArray.FromObject(ScriptExcerpt.Build(ScriptExcerpt.SplitLines(code), line, column));

    [Fact]
    public void Middle_line_has_two_lines_each_side_and_a_marker_under_the_column()
    {
        var excerpt = Excerpt(Seven, 4, 5);
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, excerpt.Select(r => r.Value<int>("line")));
        Assert.Equal("var d = 4;", excerpt[2].Value<string>("text"));
        Assert.Equal("    ^", excerpt[2].Value<string>("marker"));
        Assert.All(excerpt.Where(r => r.Value<int>("line") != 4), r => Assert.Null(r["marker"]));
    }

    [Fact]
    public void First_and_last_lines_clip_the_context_to_the_source()
    {
        Assert.Equal(new[] { 1, 2, 3 }, Excerpt(Seven, 1, 1).Select(r => r.Value<int>("line")));
        Assert.Equal("^", Excerpt(Seven, 1, 1)[0].Value<string>("marker"));
        Assert.Equal(new[] { 5, 6, 7 }, Excerpt(Seven, 7, 3).Select(r => r.Value<int>("line")));
        Assert.Equal(new[] { 1 }, Excerpt("return x;", 1, 8).Select(r => r.Value<int>("line")));
    }

    [Fact]
    public void Crlf_and_tabs_keep_text_and_marker_aligned()
    {
        var excerpt = Excerpt("var a = 1;\r\n\tvar b = missing;\r\n", 2, 10);
        Assert.Equal("\tvar b = missing;", excerpt[1].Value<string>("text"));
        Assert.Equal("\t        ^", excerpt[1].Value<string>("marker"));
        Assert.Equal("", excerpt[2].Value<string>("text"));
    }

    [Fact]
    public void Long_lines_are_cut_to_200_chars_around_the_column()
    {
        var longLine = new string('a', 500) + "BAD" + new string('b', 500);
        var excerpt = Excerpt("var x = 1;\n" + longLine + "\nvar y = 2;", 2, 501);
        var row = excerpt[1];
        Assert.Equal(200, row.Value<string>("text").Length);
        Assert.True(row.Value<bool>("truncated"));
        var marker = row.Value<string>("marker");
        Assert.Equal('B', row.Value<string>("text")[marker.Length - 1]);
        // Short neighbours share the window, so they show as empty but truncated.
        Assert.Equal("", excerpt[0].Value<string>("text"));
        Assert.True(excerpt[0].Value<bool>("truncated"));
    }

    [Fact]
    public void Column_past_the_end_marks_the_end_and_outside_lines_give_no_excerpt()
    {
        Assert.Equal("        ^", Excerpt("return x", 1, 9)[0].Value<string>("marker"));
        Assert.Null(ScriptExcerpt.Build(ScriptExcerpt.SplitLines("x"), 0, 1));
        Assert.Null(ScriptExcerpt.Build(ScriptExcerpt.SplitLines("x"), 2, 1));
    }

    [Theory]
    [InlineData("return Missing1;\nvar b = 2;\nvar c = 3;", 1, new[] { 1, 2, 3 })]
    [InlineData("var a = 1;\nvar b = 2;\nreturn Missing1;\nvar d = 4;\nvar e = 5;", 3, new[] { 1, 2, 3, 4, 5 })]
    [InlineData("var a = 1;\nvar b = 2;\nreturn Missing1;", 3, new[] { 1, 2, 3 })]
    public async Task Real_compile_diagnostics_carry_the_excerpt(string code, int line, int[] lines)
    {
        var result = await Task.Run(() => new SendCodeHandler().Execute(new Document(),
            new JObject { ["code"] = code, ["script_inputs_version"] = 1, ["compile_only"] = true }));
        var payload = JObject.FromObject(result.Result);
        var diagnostic = Assert.Single((JArray)payload["diagnostics"]);
        Assert.Equal(line, diagnostic.Value<int>("line"));
        var excerpt = (JArray)diagnostic["excerpt"];
        Assert.Equal(lines, excerpt.Select(r => r.Value<int>("line")));
        var errorRow = excerpt.Single(r => r["marker"] != null);
        Assert.Equal(line, errorRow.Value<int>("line"));
        Assert.Equal("Missing1", errorRow.Value<string>("text").Substring(errorRow.Value<string>("marker").Length - 1, 8));
    }
}
