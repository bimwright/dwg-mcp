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

        [Theory]
        [InlineData("CreatePoint", "create_point", "{\"point\":{\"x\":0,\"y\":0},\"layer\":\"A-POINT\",\"color_index\":2}")]
        [InlineData("CreatePolyline", "create_polyline", "{\"points\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0}],\"closed\":true,\"layer\":\"A-LINE\",\"color_index\":3}")]
        [InlineData("CreateRectangle", "create_rectangle", "{\"corner1\":{\"x\":0,\"y\":0},\"corner2\":{\"x\":10,\"y\":5},\"layer\":\"A-RECT\",\"color_index\":4}")]
        [InlineData("CreateArc", "create_arc", "{\"center\":{\"x\":0,\"y\":0},\"radius\":5,\"start_angle\":0,\"end_angle\":90,\"layer\":\"A-ARC\",\"color_index\":5}")]
        [InlineData("CreateEllipse", "create_ellipse", "{\"center\":{\"x\":0,\"y\":0},\"major_radius\":6,\"minor_radius\":3,\"rotation\":30,\"layer\":\"A-ELLIPSE\",\"color_index\":6}")]
        public void Validate_CreatePrimitiveSchemasAcceptValidShapes(
            string schemaName,
            string commandName,
            string json)
        {
            var schema = GetCommandSchema(schemaName);

            AssertValid(commandName, JObject.Parse(json), schema);
        }

        [Theory]
        [InlineData("CreatePoint", "create_point", "point")]
        [InlineData("CreatePolyline", "create_polyline", "points")]
        [InlineData("CreateRectangle", "create_rectangle", "corner1")]
        [InlineData("CreateRectangle", "create_rectangle", "corner2")]
        [InlineData("CreateArc", "create_arc", "center")]
        [InlineData("CreateArc", "create_arc", "radius")]
        [InlineData("CreateArc", "create_arc", "start_angle")]
        [InlineData("CreateArc", "create_arc", "end_angle")]
        [InlineData("CreateEllipse", "create_ellipse", "center")]
        [InlineData("CreateEllipse", "create_ellipse", "major_radius")]
        [InlineData("CreateEllipse", "create_ellipse", "minor_radius")]
        [InlineData("CreateEllipse", "create_ellipse", "rotation")]
        public void Validate_CreatePrimitiveSchemasRequireGeometryFields(
            string schemaName,
            string commandName,
            string fieldName)
        {
            var schema = GetCommandSchema(schemaName);
            var parameters = ValidPrimitiveParameters(commandName);
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

        private static JObject ValidPrimitiveParameters(string commandName)
        {
            switch (commandName)
            {
                case "create_point":
                    return JObject.Parse("{\"point\":{\"x\":0,\"y\":0}}");
                case "create_polyline":
                    return JObject.Parse("{\"points\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0}]}");
                case "create_rectangle":
                    return JObject.Parse("{\"corner1\":{\"x\":0,\"y\":0},\"corner2\":{\"x\":10,\"y\":5}}");
                case "create_arc":
                    return JObject.Parse("{\"center\":{\"x\":0,\"y\":0},\"radius\":5,\"start_angle\":0,\"end_angle\":90}");
                case "create_ellipse":
                    return JObject.Parse("{\"center\":{\"x\":0,\"y\":0},\"major_radius\":6,\"minor_radius\":3,\"rotation\":30}");
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown primitive command.");
            }
        }
    }
}
