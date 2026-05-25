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
    }
}
