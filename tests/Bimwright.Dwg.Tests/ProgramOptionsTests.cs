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
        public void ServerInfoVersion_tracks_the_server_assembly()
        {
            var options = new ModelContextProtocol.Server.McpServerOptions();
            typeof(Program).GetMethod("ConfigureMcpServerOptions", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { options });
            Assert.Equal("dwg-mcp", options.ServerInfo.Name);
            Assert.Equal(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion, options.ServerInfo.Version);
        }

        [Fact]
        public void SendCodeRidesMetaToolsetOnDefaultSurface()
        {
            // send_code remains enabled by default; meta carries it unless the
            // server switch or read-only mode removes it.
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
