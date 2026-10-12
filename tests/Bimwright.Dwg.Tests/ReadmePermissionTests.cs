using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class ReadmePermissionTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("README.vi.md")]
    [InlineData("README.zh-CN.md")]
    [InlineData("README.ja.md")]
    public void Read_only_allowlist_matches_explicit_annotations(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "server", "Bimwright.Dwg.Server.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var readme = File.ReadAllText(Path.Combine(directory.FullName, file));
        var block = Regex.Match(readme, @"<!-- read-only-allowlist:start -->(.*?)<!-- read-only-allowlist:end -->", RegexOptions.Singleline);
        Assert.True(block.Success, file + " must contain a generated read-only allowlist");
        var expected = typeof(Program).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(tool => tool != null && tool.ReadOnly)
            .Select(tool => "mcp__dwg-mcp__" + tool.Name).OrderBy(name => name).ToArray();
        var actual = block.Groups[1].Value.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("mcp__dwg-mcp__", StringComparison.Ordinal)).OrderBy(name => name).ToArray();
        Assert.Equal(actual.Length, actual.Distinct().Count());
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("mcp__dwg-mcp__dwg_send_code", actual);
        Assert.Contains("mcp__dwg-mcp__*", readme); // The prose must also warn against wildcard rules.
    }
}
