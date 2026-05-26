using System;
using System.Reflection;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DimensionSchemaTests
    {
        [Theory]
        [InlineData("CreateLinearDimension", "create_linear_dimension", "{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":10,\"y\":0},\"dimension_line_point\":{\"x\":5,\"y\":2},\"rotation\":45,\"layer\":\"A-DIMS\"}")]
        [InlineData("CreateAlignedDimension", "create_aligned_dimension", "{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":10,\"y\":5},\"dimension_line_point\":{\"x\":5,\"y\":7},\"layer\":\"A-DIMS\"}")]
        [InlineData("CreateRadialDimension", "create_radial_dimension", "{\"entity_handle\":\"1A2B\",\"dimension_line_point\":{\"x\":8,\"y\":4},\"layer\":\"A-DIMS\"}")]
        [InlineData("CreateDiameterDimension", "create_diameter_dimension", "{\"entity_handle\":\"1A2B\",\"dimension_line_point\":{\"x\":8,\"y\":4},\"layer\":\"A-DIMS\"}")]
        public void Validate_DimensionSchemasAcceptValidShapes(
            string schemaName,
            string commandName,
            string json)
        {
            var schema = GetCommandSchema(schemaName);

            AssertValid(commandName, JObject.Parse(json), schema);
        }

        [Theory]
        [InlineData("CreateLinearDimension", "create_linear_dimension", "start")]
        [InlineData("CreateLinearDimension", "create_linear_dimension", "end")]
        [InlineData("CreateLinearDimension", "create_linear_dimension", "dimension_line_point")]
        [InlineData("CreateAlignedDimension", "create_aligned_dimension", "start")]
        [InlineData("CreateAlignedDimension", "create_aligned_dimension", "end")]
        [InlineData("CreateAlignedDimension", "create_aligned_dimension", "dimension_line_point")]
        [InlineData("CreateRadialDimension", "create_radial_dimension", "entity_handle")]
        [InlineData("CreateRadialDimension", "create_radial_dimension", "dimension_line_point")]
        [InlineData("CreateDiameterDimension", "create_diameter_dimension", "entity_handle")]
        [InlineData("CreateDiameterDimension", "create_diameter_dimension", "dimension_line_point")]
        public void Validate_DimensionSchemasRequireContractFields(
            string schemaName,
            string commandName,
            string fieldName)
        {
            var schema = GetCommandSchema(schemaName);
            var parameters = ValidParameters(commandName);
            parameters.Remove(fieldName);

            AssertMissingRequiredField(commandName, parameters, schema, fieldName);
        }

        [Fact]
        public void Validate_CreateLinearDimensionSchemaIncludesOptionalRotation()
        {
            var schema = GetCommandSchema("CreateLinearDimension");

            var rotation = Assert.Single(schema.Properties, property => property.Name == "rotation");
            Assert.False(rotation.IsRequired);
            Assert.Contains(JTokenType.Float, rotation.AcceptedTypes);
            Assert.Contains(JTokenType.Integer, rotation.AcceptedTypes);
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
                case "create_linear_dimension":
                    return JObject.Parse("{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":10,\"y\":0},\"dimension_line_point\":{\"x\":5,\"y\":2}}");
                case "create_aligned_dimension":
                    return JObject.Parse("{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":10,\"y\":5},\"dimension_line_point\":{\"x\":5,\"y\":7}}");
                case "create_radial_dimension":
                    return JObject.Parse("{\"entity_handle\":\"1A2B\",\"dimension_line_point\":{\"x\":8,\"y\":4}}");
                case "create_diameter_dimension":
                    return JObject.Parse("{\"entity_handle\":\"1A2B\",\"dimension_line_point\":{\"x\":8,\"y\":4}}");
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown dimension command.");
            }
        }
    }
}
