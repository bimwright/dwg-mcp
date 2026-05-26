using System;
using System.Collections.Generic;
using Bimwright.Dwg.Pid;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class PidConfigTests
    {
        [Fact]
        public void Load_WithDefaults_ReturnsExpectedValues()
        {
            var config = PidConfig.Load(key => null);

            Assert.Null(config.LibraryPath);
            Assert.Equal("procedural", config.SymbolMode);
            Assert.True(config.Fallback);
            Assert.True(config.UseProcedural);
        }

        [Fact]
        public void Load_WithEnvironmentVariables_LoadsCorrectly()
        {
            var env = new Dictionary<string, string>
            {
                [PidConfig.EnvPidLibraryPath] = @"C:\custom\pid\lib",
                [PidConfig.EnvPidSymbolMode] = "auto",
                [PidConfig.EnvPidFallback] = "false"
            };

            var config = PidConfig.Load(key => env.TryGetValue(key, out var val) ? val : null);

            Assert.Equal(@"C:\custom\pid\lib", config.LibraryPath);
            Assert.Equal("auto", config.SymbolMode);
            Assert.False(config.Fallback);
            Assert.True(config.UseProcedural);
        }

        [Fact]
        public void Load_WithExternalMode_ThrowsOrFailsValidation()
        {
            var env = new Dictionary<string, string>
            {
                [PidConfig.EnvPidSymbolMode] = "external"
            };

            var config = PidConfig.Load(key => env.TryGetValue(key, out var val) ? val : null);

            Assert.Equal("external", config.SymbolMode);
            Assert.False(config.UseProcedural);
            
            var ex = Assert.Throws<NotSupportedException>(() => config.Validate());
            Assert.Contains("deferred", ex.Message);
        }
    }
}
