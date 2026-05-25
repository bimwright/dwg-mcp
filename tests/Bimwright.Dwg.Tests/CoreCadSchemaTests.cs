using System.Reflection;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CoreCadSchemaTests
    {
        [Fact]
        public void Validate_QueryEntitiesAcceptsOptionalFilters()
        {
            var schema = GetCommandSchema("QueryEntities");

            AssertValid("query_entities", null, schema);
            AssertValid(
                "query_entities",
                JObject.Parse("{\"entity_type\":\"Line\",\"layer\":\"A-WALL\",\"color_index\":7,\"limit\":25,\"include_geometry\":true}"),
                schema);
        }

        [Fact]
        public void Validate_CountEntitiesAcceptsOptionalFilters()
        {
            var schema = GetCommandSchema("CountEntities");

            AssertValid("count_entities", null, schema);
            AssertValid(
                "count_entities",
                JObject.Parse("{\"entity_type\":\"Circle\",\"layer\":\"A-PIPE\",\"color_index\":3}"),
                schema);
        }

        [Fact]
        public void Validate_SelectByLayerRequiresLayerAndAcceptsOptionalFilters()
        {
            var schema = GetCommandSchema("SelectByLayer");

            AssertMissingRequiredField(
                "select_by_layer",
                JObject.Parse("{}"),
                schema,
                "layer");
            AssertInvalidField(
                "select_by_layer",
                JObject.Parse("{\"layer\":\"\"}"),
                schema,
                "layer");
            AssertValid(
                "select_by_layer",
                JObject.Parse("{\"layer\":\"A-TEXT\",\"entity_type\":\"DBText\",\"color_index\":2,\"limit\":100}"),
                schema);
        }

        [Fact]
        public void Validate_SelectByTypeRequiresEntityTypeAndAcceptsOptionalFilters()
        {
            var schema = GetCommandSchema("SelectByType");

            AssertMissingRequiredField(
                "select_by_type",
                JObject.Parse("{}"),
                schema,
                "entity_type");
            AssertInvalidField(
                "select_by_type",
                JObject.Parse("{\"entity_type\":\"  \"}"),
                schema,
                "entity_type");
            AssertValid(
                "select_by_type",
                JObject.Parse("{\"entity_type\":\"Polyline\",\"layer\":\"A-AREA\",\"color_index\":4,\"limit\":50}"),
                schema);
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

        private static void AssertInvalidField(
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
