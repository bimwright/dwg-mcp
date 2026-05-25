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
        private const string EnvName = "BIMWRIGHT_DWG_ENABLE_SEND_CODE";

        [Fact]
        public void SendCodeIsDisabledByDefault()
        {
            var original = Environment.GetEnvironmentVariable(EnvName);
            try
            {
                Environment.SetEnvironmentVariable(EnvName, null);

                Assert.False(Program.IsSendCodeEnabled(Array.Empty<string>()));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvName, original);
            }
        }

        [Theory]
        [InlineData("--enable-send-code")]
        [InlineData("--ENABLE-SEND-CODE")]
        public void SendCodeCanBeEnabledByArgument(string arg)
        {
            Assert.True(Program.IsSendCodeEnabled(new[] { arg }));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("yes")]
        public void SendCodeCanBeEnabledByEnvironment(string value)
        {
            var original = Environment.GetEnvironmentVariable(EnvName);
            try
            {
                Environment.SetEnvironmentVariable(EnvName, value);

                Assert.True(Program.IsSendCodeEnabled(Array.Empty<string>()));
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvName, original);
            }
        }

        [Fact]
        public void SendCodeIsNotOnDefaultToolSurface()
        {
            Assert.DoesNotContain("code", ToolsetFilter.Resolve(new DwgMcpConfig()));
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
