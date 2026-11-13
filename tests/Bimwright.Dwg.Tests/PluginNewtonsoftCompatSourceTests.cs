using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // Plugin code runs inside AutoCAD, which may have loaded an older Newtonsoft.Json first.
    // Newtonsoft.Json 13.0.4 adds JToken.ToString(Formatting); a call compiled against it fails
    // with MissingMethodException on older host-loaded versions. Plugin code must call
    // ToString(Formatting, params JsonConverter[]) explicitly, e.g. Array.Empty<JsonConverter>().
    public class PluginNewtonsoftCompatSourceTests
    {
        private static readonly Regex SingleArgFormattingToString =
            new Regex(@"\.ToString\(\s*Formatting\.\w+\s*\)", RegexOptions.Compiled);

        [Fact]
        public void PluginSources_DoNotCallSingleArgumentFormattingToString()
        {
            var src = Path.Combine(FindRepoRoot(), "src");
            var roots = new[] { Path.Combine(src, "shared") }
                .Concat(Directory.GetDirectories(src, "plugin-acad*"));

            var offenders = roots
                .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                .Where(file => !IsBuildOutput(file))
                .SelectMany(file => SingleArgFormattingToString.Matches(File.ReadAllText(file))
                    .Cast<Match>()
                    .Select(m => $"{Path.GetRelativePath(src, file)}: {m.Value}"))
                .ToList();

            Assert.True(offenders.Count == 0,
                "Use ToString(Formatting.X, Array.Empty<JsonConverter>()) in plugin code:\n"
                + string.Join("\n", offenders));
        }

        private static bool IsBuildOutput(string file)
        {
            var sep = Path.DirectorySeparatorChar;
            return file.Contains($"{sep}bin{sep}") || file.Contains($"{sep}obj{sep}");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Bimwright.Dwg.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
