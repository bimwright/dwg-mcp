using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToolsetFilterTests
    {
        [Fact]
        public void Resolve_DefaultsExposeBackedExistingToolsets()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig());

            Assert.Contains("query", set);
            Assert.Contains("modify", set);
            Assert.Contains("meta", set);
            Assert.DoesNotContain("code", set);
            Assert.DoesNotContain("toolbaker", set);
        }

        [Fact]
        public void Resolve_ReadOnlyKeepsQueryAndStripsWriteToolsets()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "query", "modify", "code", "meta" },
                ReadOnly = true,
                EnableSendCode = true
            });

            Assert.Equal(new[] { "meta", "query" }, set.OrderBy(s => s).ToArray());
        }

        [Fact]
        public void Resolve_EnableSendCodeAllowsCodeToolsetWhenRequested()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "query", "code" },
                EnableSendCode = true
            });

            Assert.Contains("query", set);
            Assert.Contains("code", set);
        }

        [Fact]
        public void Resolve_EnableSendCodeAddsCodeToDefaultSurface()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                EnableSendCode = true
            });

            Assert.Contains("query", set);
            Assert.Contains("modify", set);
            Assert.Contains("meta", set);
            Assert.Contains("code", set);
        }

        [Fact]
        public void Resolve_DropsUnknownToolsets()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "query", "bogus" }
            });

            Assert.Equal(new[] { "query" }, set.ToArray());
        }

        [Fact]
        public void KnownToolsets_IncludePlan3ToolsetsButDefaultsKeepThemOff()
        {
            Assert.Contains("annotation", ToolsetFilter.KnownToolsets);
            Assert.Contains("block", ToolsetFilter.KnownToolsets);
            Assert.Contains("dimension", ToolsetFilter.KnownToolsets);

            Assert.DoesNotContain("annotation", ToolsetFilter.DefaultOn);
            Assert.DoesNotContain("block", ToolsetFilter.DefaultOn);
            Assert.DoesNotContain("dimension", ToolsetFilter.DefaultOn);
        }

        [Fact]
        public void Resolve_ReadOnlyStripsAnnotationAndDimensionButKeepsBlock()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "query", "annotation", "block", "dimension" },
                ReadOnly = true
            });

            Assert.Equal(new[] { "block", "query" }, set.OrderBy(s => s).ToArray());
        }
    }
}
