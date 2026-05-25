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
