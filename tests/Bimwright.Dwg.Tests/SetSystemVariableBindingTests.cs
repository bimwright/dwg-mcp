using System.Linq;
using System.Text.Json;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    /// <summary>dwg_set_system_variable as the MCP SDK sees it: published schema and argument binding.</summary>
    public class SetSystemVariableBindingTests
    {
        private static System.Reflection.MethodInfo Method => typeof(DrawingWriteTools).GetMethod(nameof(DrawingWriteTools.SetSystemVariable));

        [Fact]
        public void Published_value_schema_admits_scalars()
        {
            var value = McpServerTool.Create(Method).ProtocolTool.InputSchema.GetProperty("properties").GetProperty("value");

            // A bare `true` schema makes some models send structured values as JSON strings.
            Assert.Equal(JsonValueKind.Object, value.ValueKind);
            // The plugin only accepts scalars; the JToken-derived schema demanded an array.
            if (value.TryGetProperty("type", out var type))
            {
                var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()).ToArray() : new[] { type.GetString() };
                Assert.Contains("number", types);
                Assert.Contains("string", types);
            }
        }

        [Theory]
        [InlineData("1")]
        [InlineData("\"Standard\"")]
        [InlineData("0.5")]
        public void Sdk_serializer_binds_scalar_values_to_the_declared_parameter_type(string json)
        {
            // The SDK binds each argument with System.Text.Json; a Newtonsoft JToken parameter threw here.
            var type = Method.GetParameters().Single(p => p.Name == "value").ParameterType;
            using var document = JsonDocument.Parse(json);

            var bound = document.RootElement.Deserialize(type, McpJsonUtilities.DefaultOptions);

            Assert.NotNull(bound);
        }
    }
}
