using System.IO;
using System.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TransportSpecTests
    {
        [Fact]
        public void App_SelectsPipeTransportForAcad2025OrGreater()
        {
            var source = ReadRepoFile("src", "plugin-acad24", "App.cs");

            Assert.Contains("#if ACAD2025_OR_GREATER", source);
            Assert.Contains("new PipeTransportServer", source);
            Assert.Contains("new TcpTransportServer", source);
        }

        [Fact]
        public void PluginProjects_DefineCumulativeVersionConstants()
        {
            Assert.DoesNotContain("ACAD2025_OR_GREATER", ReadProject("plugin-acad22", "22"));
            Assert.DoesNotContain("ACAD2025_OR_GREATER", ReadProject("plugin-acad23", "23"));

            Assert.Contains("ACAD2025_OR_GREATER", ReadProject("plugin-acad25", "25"));
            Assert.Contains("ACAD2025_OR_GREATER", ReadProject("plugin-acad26", "26"));
            var acad27 = ReadProject("plugin-acad27", "27");
            Assert.Contains("ACAD2025_OR_GREATER", acad27);
            Assert.Contains("ACAD2027_OR_GREATER", acad27);
        }

        [Fact]
        public void ITransportServer_ExposesDiscoveryMetadataContract()
        {
            var source = ReadRepoFile("src", "shared", "Transport", "ITransportServer.cs");

            Assert.Contains("TransportKind", source);
            Assert.Contains("IsClientConnected", source);
            Assert.Contains("LastCommandTime", source);
            Assert.Contains("PipeName", source);
        }

        [Fact]
        public void TransportDiscoveryJson_UsesAcadYearAndStablePipeNameFields()
        {
            var tcp = ReadRepoFile("src", "shared", "Transport", "TcpTransportServer.cs");
            var pipe = ReadRepoFile("src", "shared", "Transport", "PipeTransportServer.cs");

            Assert.Contains("acad_year", tcp);
            Assert.Contains("pipe_name = (string)null", tcp);
            Assert.Contains("acad_year", pipe);
            Assert.Contains("pipe_name = _pipeName", pipe);
        }

        private static string ReadProject(string folder, string suffix)
            => ReadRepoFile("src", folder, $"Bimwright.Dwg.Plugin.Acad{suffix}.csproj");

        private static string ReadRepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Bimwright.Dwg.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()));
        }
    }
}
