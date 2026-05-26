using System;
using System.Reflection;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BlockSchemaTests
    {
        [Fact]
        public void Validate_ListBlocksSchemaAcceptsNoParameters()
        {
            var schema = GetCommandSchema("ListBlocks");

            AssertValid("list_blocks", null, schema);
            AssertValid("list_blocks", JObject.Parse("{}"), schema);
        }

        [Theory]
        [InlineData("GetBlockAttributes", "get_block_attributes", "{\"handle\":\"1A2B\"}")]
        [InlineData("InsertBlock", "insert_block", "{\"block_name\":\"VALVE\",\"insertion_point\":{\"x\":10,\"y\":20},\"block_path\":\"C:\\\\Blocks\\\\valve.dwg\",\"scale\":1.25,\"rotation\":45,\"attributes\":{\"TAG\":\"V-101\"}}")]
        [InlineData("SetBlockAttributes", "set_block_attributes", "{\"handle\":\"1A2B\",\"attributes\":{\"TAG\":\"V-102\",\"SERVICE\":\"CW\"},\"strict_tags\":true}")]
        [InlineData("ExplodeBlock", "explode_block", "{\"handle\":\"1A2B\"}")]
        public void Validate_BlockSchemasAcceptValidShapes(
            string schemaName,
            string commandName,
            string json)
        {
            var schema = GetCommandSchema(schemaName);

            AssertValid(commandName, JObject.Parse(json), schema);
        }

        [Theory]
        [InlineData("GetBlockAttributes", "get_block_attributes", "handle")]
        [InlineData("InsertBlock", "insert_block", "block_name")]
        [InlineData("InsertBlock", "insert_block", "insertion_point")]
        [InlineData("SetBlockAttributes", "set_block_attributes", "handle")]
        [InlineData("SetBlockAttributes", "set_block_attributes", "attributes")]
        [InlineData("ExplodeBlock", "explode_block", "handle")]
        public void Validate_BlockSchemasRequireContractFields(
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
        public void Validate_SetBlockAttributesSchemaIncludesOptionalStrictTags()
        {
            var schema = GetCommandSchema("SetBlockAttributes");

            var strictTags = Assert.Single(schema.Properties, property => property.Name == "strict_tags");
            Assert.False(strictTags.IsRequired);
            Assert.Contains(JTokenType.Boolean, strictTags.AcceptedTypes);
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
                case "get_block_attributes":
                    return JObject.Parse("{\"handle\":\"1A2B\"}");
                case "insert_block":
                    return JObject.Parse("{\"block_name\":\"VALVE\",\"insertion_point\":{\"x\":10,\"y\":20}}");
                case "set_block_attributes":
                    return JObject.Parse("{\"handle\":\"1A2B\",\"attributes\":{\"TAG\":\"V-102\"}}");
                case "explode_block":
                    return JObject.Parse("{\"handle\":\"1A2B\"}");
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown block command.");
            }
        }
    }
}
