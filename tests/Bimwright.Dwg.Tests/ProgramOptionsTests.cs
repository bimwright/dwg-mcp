using System;
using System.Linq;
using System.Reflection;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ProgramOptionsTests
    {
        [Fact]
        public void SendCodeRidesMetaToolsetOnDefaultSurface()
        {
            // dwg_send_code is always-on like revit_send_code_to_revit: no
            // --enable-send-code flag exists; meta (a default toolset) carries it.
            Assert.Contains("meta", ToolsetFilter.Resolve(new DwgMcpConfig()));
            Assert.True(HasMcpToolAttribute(typeof(CodeTools).GetMethod("SendCode")));
        }

        [Fact]
        public void UnwiredOptionWarning_ReturnsNull_WhenNoUnwiredOptionsSet()
        {
            Assert.Null(Program.UnwiredOptionWarning(new DwgMcpConfig()));
        }

        [Fact]
        public void UnwiredOptionWarning_ReturnsNull_WhenConfigIsNull()
        {
            Assert.Null(Program.UnwiredOptionWarning(null));
        }

        [Fact]
        public void UnwiredOptionWarning_DescribesAllowLanBind_WhenSet()
        {
            var warning = Program.UnwiredOptionWarning(new DwgMcpConfig { AllowLanBind = true });

            Assert.NotNull(warning);
            Assert.Contains("--allow-lan-bind", warning);
            Assert.Contains("loopback", warning);
        }

        private static bool HasMcpToolAttribute(MethodInfo method)
            => method != null
            && method.GetCustomAttributes()
                .Any(a => string.Equals(a.GetType().Name, "McpServerToolAttribute", StringComparison.Ordinal));
    }
}
