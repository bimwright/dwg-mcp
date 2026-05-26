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
            Assert.Contains("view", set);
            Assert.DoesNotContain("code", set);
            Assert.DoesNotContain("toolbaker", set);
            Assert.DoesNotContain("export", set);
            Assert.DoesNotContain("drawing", set);
            Assert.DoesNotContain("pid", set);
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

        [Fact]
        public void KnownToolsets_IncludePlan4ToolsetsButDefaultsKeepExportAndDrawingOff()
        {
            Assert.Contains("view", ToolsetFilter.KnownToolsets);
            Assert.Contains("export", ToolsetFilter.KnownToolsets);
            Assert.Contains("drawing", ToolsetFilter.KnownToolsets);

            Assert.Contains("view", ToolsetFilter.DefaultOn);
            Assert.DoesNotContain("export", ToolsetFilter.DefaultOn);
            Assert.DoesNotContain("drawing", ToolsetFilter.DefaultOn);
        }

        [Fact]
        public void Resolve_ReadOnlyStripsExportButKeepsViewAndDrawing()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "query", "view", "export", "drawing" },
                ReadOnly = true
            });

            Assert.Equal(new[] { "drawing", "query", "view" }, set.OrderBy(s => s).ToArray());
        }

        [Fact]
        public void KnownToolsets_IncludePidButDefaultExcludesIt()
        {
            Assert.Contains("pid", ToolsetFilter.KnownToolsets);
            Assert.DoesNotContain("pid", ToolsetFilter.DefaultOn);
        }

        [Fact]
        public void Resolve_ExplicitPidExposesIt()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "pid" }
            });

            Assert.Contains("pid", set);
        }

        [Fact]
        public void Resolve_AllIncludesPid()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "all" }
            });

            Assert.Contains("pid", set);
        }

        [Fact]
        public void Resolve_ReadOnlyStripsPid()
        {
            var set = ToolsetFilter.Resolve(new DwgMcpConfig
            {
                Toolsets = new List<string> { "pid" },
                ReadOnly = true
            });

            Assert.DoesNotContain("pid", set);
        }
    }
}
