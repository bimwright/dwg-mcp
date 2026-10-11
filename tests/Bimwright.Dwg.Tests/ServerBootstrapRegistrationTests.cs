using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ServerBootstrapRegistrationTests
    {
        [Fact]
        public void RegistrationTableCoversKnownToolsetsAndDeclaredToolClassesWithoutDuplicateTypes()
        {
            var registrations = Program.ToolRegistrations;
            Assert.Equal(ToolsetFilter.KnownToolsets.OrderBy(name => name, StringComparer.Ordinal),
                registrations.SelectMany(row => row.Toolsets).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal(registrations.Length, registrations.Select(row => row.ToolType).Distinct().Count());
            var declared = typeof(Program).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal);
            Assert.Equal(declared, registrations.Select(row => row.ToolType).OrderBy(type => type.FullName, StringComparer.Ordinal));
        }

        [Theory]
        [InlineData("meta")]
        [InlineData("code")]
        [InlineData("meta,code")]
        public void CodeAliasSelectsOneClassAndReadOnlyRemovesIt(string toolsets)
        {
            var enabled = new HashSet<string>(toolsets.Split(','), StringComparer.OrdinalIgnoreCase);
            var resolver = typeof(Program).GetMethod("ResolveToolTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            var writable = (IEnumerable<Type>)resolver.Invoke(null, new object[] { enabled, false });
            var readOnly = (IEnumerable<Type>)resolver.Invoke(null, new object[] { enabled, true });
            Assert.Single(writable.Where(type => type == typeof(CodeTools)));
            Assert.DoesNotContain(typeof(CodeTools), readOnly);
        }
    }
}
