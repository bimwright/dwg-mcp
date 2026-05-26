using System;
using System.Reflection;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DrawingOpsSchemaTests
    {
        [Fact]
        public void Validate_ZoomExtentsSchemaAcceptsNoParameters()
        {
            var schema = GetCommandSchema("ZoomExtents");
            AssertValid("zoom_extents", null, schema);
            AssertValid("zoom_extents", JObject.Parse("{}"), schema);
        }

        [Fact]
        public void Validate_GetVariablesSchemaAcceptsNoParameters()
        {
            var schema = GetCommandSchema("GetVariables");
            AssertValid("get_variables", null, schema);
            AssertValid("get_variables", JObject.Parse("{}"), schema);
        }

        [Theory]
        [InlineData("ZoomWindow", "zoom_window", "{\"corner1\":{\"x\":0,\"y\":0},\"corner2\":{\"x\":100,\"y\":100}}")]
        [InlineData("ZoomToEntity", "zoom_to_entity", "{\"handle\":\"1A2B\"}")]
        [InlineData("ExportDxf", "export_dxf", "{\"output_path\":\"C:\\\\Temp\\\\cap.dxf\",\"overwrite_existing\":false}")]
        [InlineData("SetSystemVariable", "set_system_variable", "{\"name\":\"CLAYER\",\"value\":\"0\"}")]
        [InlineData("SetSystemVariable", "set_system_variable", "{\"name\":\"ORTHOMODE\",\"value\":1}")]
        [InlineData("SaveDrawing", "save_drawing", "{\"output_path\":\"C:\\\\Temp\\\\doc.dwg\",\"confirm\":true}")]
        [InlineData("PurgeDrawing", "purge_drawing", "{\"dry_run\":true,\"confirm\":false}")]
        public void Validate_DrawingOpsSchemasAcceptValidShapes(
            string schemaName,
            string commandName,
            string json)
        {
            var schema = GetCommandSchema(schemaName);
            AssertValid(commandName, JObject.Parse(json), schema);
        }

        [Theory]
        [InlineData("ZoomWindow", "zoom_window", "corner1")]
        [InlineData("ZoomWindow", "zoom_window", "corner2")]
        [InlineData("ZoomToEntity", "zoom_to_entity", "handle")]
        [InlineData("ExportDxf", "export_dxf", "output_path")]
        [InlineData("SetSystemVariable", "set_system_variable", "name")]
        [InlineData("SetSystemVariable", "set_system_variable", "value")]
        public void Validate_DrawingOpsSchemasRequireContractFields(
            string schemaName,
            string commandName,
            string fieldName)
        {
            var schema = GetCommandSchema(schemaName);
            var parameters = ValidParameters(commandName);
            parameters.Remove(fieldName);

            AssertMissingRequiredField(commandName, parameters, schema, fieldName);
        }

        private static CommandSchema GetCommandSchema(string name)
        {
            var field = typeof(CommandSchemas).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(field != null, $"CommandSchemas.{name} is missing.");

            var schema = field.GetValue(null) as CommandSchema;
            Assert.True(schema != null, $"CommandSchemas.{name} must be a CommandSchema.");
            return schema;
        }

        private static void AssertValid(string commandName, JToken parameters, CommandSchema schema)
        {
            var result = SchemaValidator.Validate(commandName, parameters, schema);
            Assert.True(result.Ok, result.Error);
        }

        private static void AssertMissingRequiredField(
            string commandName,
            JObject parameters,
            CommandSchema schema,
            string fieldName)
        {
            var result = SchemaValidator.Validate(commandName, parameters, schema);
            Assert.False(result.Ok);
            Assert.Contains(fieldName, result.Error);
        }

        private static JObject ValidParameters(string commandName)
        {
            switch (commandName)
            {
                case "zoom_window":
                    return JObject.Parse("{\"corner1\":{\"x\":0,\"y\":0},\"corner2\":{\"x\":100,\"y\":100}}");
                case "zoom_to_entity":
                    return JObject.Parse("{\"handle\":\"1A2B\"}");
                case "export_dxf":
                    return JObject.Parse("{\"output_path\":\"C:\\\\Temp\\\\cap.dxf\"}");
                case "set_system_variable":
                    return JObject.Parse("{\"name\":\"CLAYER\",\"value\":\"0\"}");
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown command.");
            }
        }
    }
}
