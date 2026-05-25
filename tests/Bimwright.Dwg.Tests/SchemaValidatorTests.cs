using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class SchemaValidatorTests
    {
        [Fact]
        public void Validate_ReportsMissingRequiredField()
        {
            var schema = CommandSchema.Object(
                SchemaProperty.Required("items", JTokenType.Array));

            var result = SchemaValidator.Validate("update_texts", JObject.Parse("{}"), schema);

            Assert.False(result.Ok);
            Assert.Contains("items", result.Error);
        }

        [Fact]
        public void Validate_ReportsInvalidFieldType()
        {
            var schema = CommandSchema.Object(
                SchemaProperty.Required("items", JTokenType.Array));

            var result = SchemaValidator.Validate("update_texts", JObject.Parse("{\"items\":\"bad\"}"), schema);

            Assert.False(result.Ok);
            Assert.Contains("items", result.Error);
            Assert.Contains("array", result.Error);
        }

        [Fact]
        public void Validate_AcceptsExpectedShape()
        {
            var schema = CommandSchema.Object(
                SchemaProperty.Required("commands", JTokenType.Array));

            var result = SchemaValidator.Validate(
                "batch_execute",
                JObject.Parse("{\"commands\":[{\"cmd\":\"get_selected_texts\",\"params\":{}}]}"),
                schema);

            Assert.True(result.Ok);
        }

        [Fact]
        public void Validate_AllowsNullParamsWhenOnlyOptionalFieldsExist()
        {
            var result = SchemaValidator.Validate(
                "get_selected_texts",
                null,
                CommandSchemas.GetSelectedTexts);

            Assert.True(result.Ok);
        }

        [Fact]
        public void Validate_CreateLineRequiresStartAndEnd()
        {
            var result = SchemaValidator.Validate("create_line", JObject.Parse("{}"), CommandSchemas.CreateLine);

            Assert.False(result.Ok);
            Assert.Contains("start", result.Error);

            AssertMissingRequiredField(
                "create_line",
                JObject.Parse("{\"start\":{\"x\":1,\"y\":2}}"),
                CommandSchemas.CreateLine,
                "end");
        }

        [Fact]
        public void Validate_CreateCircleRequiresCenterAndRadius()
        {
            var result = SchemaValidator.Validate("create_circle", JObject.Parse("{}"), CommandSchemas.CreateCircle);

            Assert.False(result.Ok);
            Assert.Contains("center", result.Error);

            AssertMissingRequiredField(
                "create_circle",
                JObject.Parse("{\"center\":{\"x\":1,\"y\":2}}"),
                CommandSchemas.CreateCircle,
                "radius");
        }

        [Fact]
        public void Validate_CreateLayerRequiresName()
        {
            AssertMissingRequiredField(
                "create_layer",
                JObject.Parse("{}"),
                CommandSchemas.CreateLayer,
                "name");
        }

        [Fact]
        public void Validate_ChangeLayerRequiresHandlesAndLayer()
        {
            var result = SchemaValidator.Validate("change_layer", JObject.Parse("{}"), CommandSchemas.ChangeLayer);

            Assert.False(result.Ok);
            Assert.Contains("handles", result.Error);

            AssertMissingRequiredField(
                "change_layer",
                JObject.Parse("{\"handles\":[]}"),
                CommandSchemas.ChangeLayer,
                "layer");
        }

        [Fact]
        public void Validate_GetEntityPropertiesRequiresHandles()
        {
            AssertMissingRequiredField(
                "get_entity_properties",
                JObject.Parse("{}"),
                CommandSchemas.GetEntityProperties,
                "handles");
        }

        [Theory]
        [InlineData("move_entities", "handles")]
        [InlineData("move_entities", "vector")]
        [InlineData("rotate_entities", "handles")]
        [InlineData("rotate_entities", "basePoint")]
        [InlineData("rotate_entities", "angleDegrees")]
        [InlineData("scale_entities", "handles")]
        [InlineData("scale_entities", "basePoint")]
        [InlineData("scale_entities", "scale")]
        [InlineData("copy_entities", "handles")]
        [InlineData("copy_entities", "vector")]
        [InlineData("erase_entities", "handles")]
        public void Validate_TransformSchemasRequireExpectedFields(string commandName, string fieldName)
        {
            var parameters = ValidTransformParameters(commandName);
            parameters.Remove(fieldName);

            AssertMissingRequiredField(
                commandName,
                parameters,
                TransformSchema(commandName),
                fieldName);
        }

        [Theory]
        [InlineData("change_color", "handles")]
        [InlineData("change_color", "color_index")]
        [InlineData("offset_entities", "handles")]
        [InlineData("offset_entities", "distance")]
        public void Validate_ColorAndOffsetSchemasRequireExpectedFields(string commandName, string fieldName)
        {
            var parameters = ValidColorOrOffsetParameters(commandName);
            parameters.Remove(fieldName);

            AssertMissingRequiredField(
                commandName,
                parameters,
                ColorOrOffsetSchema(commandName),
                fieldName);
        }

        [Theory]
        [InlineData("change_color", "{\"handles\":[\"7F5AD\"],\"color_index\":7}")]
        [InlineData("change_color", "{\"handles\":[\"7F5AD\"],\"color_index\":0}")]
        [InlineData("change_color", "{\"handles\":[\"7F5AD\"],\"color_index\":257}")]
        [InlineData("offset_entities", "{\"handles\":[\"7F5AD\"],\"distance\":125.5}")]
        [InlineData("offset_entities", "{\"handles\":[\"7F5AD\"],\"distance\":125}")]
        public void Validate_ColorAndOffsetSchemasAcceptValidShapes(string commandName, string json)
        {
            var result = SchemaValidator.Validate(
                commandName,
                JObject.Parse(json),
                ColorOrOffsetSchema(commandName));

            Assert.True(result.Ok, result.Error);
        }

        [Fact]
        public void Validate_ChangeColorRequiresIntegerColorIndex()
        {
            var result = SchemaValidator.Validate(
                "change_color",
                JObject.Parse("{\"handles\":[\"7F5AD\"],\"color_index\":7.5}"),
                ColorOrOffsetSchema("change_color"));

            Assert.False(result.Ok);
            Assert.Contains("color_index", result.Error);
        }

        [Fact]
        public void Validate_OffsetEntitiesRequiresNumericDistance()
        {
            var result = SchemaValidator.Validate(
                "offset_entities",
                JObject.Parse("{\"handles\":[\"7F5AD\"],\"distance\":\"125\"}"),
                ColorOrOffsetSchema("offset_entities"));

            Assert.False(result.Ok);
            Assert.Contains("distance", result.Error);
        }

        private static CommandSchema TransformSchema(string commandName)
        {
            switch (commandName)
            {
                case "move_entities":
                    return CommandSchemas.MoveEntities;
                case "rotate_entities":
                    return CommandSchemas.RotateEntities;
                case "scale_entities":
                    return CommandSchemas.ScaleEntities;
                case "copy_entities":
                    return CommandSchemas.CopyEntities;
                case "erase_entities":
                    return CommandSchemas.EraseEntities;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown transform command.");
            }
        }

        private static CommandSchema ColorOrOffsetSchema(string commandName)
        {
            var fieldName = commandName == "change_color"
                ? "ChangeColor"
                : commandName == "offset_entities"
                    ? "OffsetEntities"
                    : null;

            Assert.False(fieldName == null, $"Unknown color/offset command: {commandName}");

            var field = typeof(CommandSchemas).GetField(fieldName);
            Assert.True(field != null, $"CommandSchemas.{fieldName} is missing.");

            var schema = field.GetValue(null) as CommandSchema;
            Assert.True(schema != null, $"CommandSchemas.{fieldName} must be a CommandSchema.");
            return schema;
        }

        private static JObject ValidTransformParameters(string commandName)
        {
            switch (commandName)
            {
                case "move_entities":
                case "copy_entities":
                    return JObject.Parse("{\"handles\":[\"7F5AD\"],\"vector\":{\"x\":1,\"y\":2}}");
                case "rotate_entities":
                    return JObject.Parse("{\"handles\":[\"7F5AD\"],\"basePoint\":{\"x\":0,\"y\":0},\"angleDegrees\":90}");
                case "scale_entities":
                    return JObject.Parse("{\"handles\":[\"7F5AD\"],\"basePoint\":{\"x\":0,\"y\":0},\"scale\":2}");
                case "erase_entities":
                    return JObject.Parse("{\"handles\":[\"7F5AD\"]}");
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown transform command.");
            }
        }

        private static JObject ValidColorOrOffsetParameters(string commandName)
        {
            switch (commandName)
            {
                case "change_color":
                    return JObject.Parse("{\"handles\":[\"7F5AD\"],\"color_index\":7}");
                case "offset_entities":
                    return JObject.Parse("{\"handles\":[\"7F5AD\"],\"distance\":125}");
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown color/offset command.");
            }
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
    }
}
