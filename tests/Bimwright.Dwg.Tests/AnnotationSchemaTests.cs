using System;
using System.Reflection;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class AnnotationSchemaTests
    {
        [Theory]
        [InlineData("CreateText", "create_text", "{\"text\":\"Room 101\",\"position\":{\"x\":0,\"y\":0},\"height\":2.5,\"rotation\":0,\"layer\":\"A-TEXT\"}")]
        [InlineData("CreateMText", "create_mtext", "{\"text\":\"General notes\",\"location\":{\"x\":1,\"y\":2},\"width\":80,\"height\":2.5,\"rotation\":15,\"layer\":\"A-NOTE\"}")]
        [InlineData("CreateLeader", "create_leader", "{\"points\":[{\"x\":0,\"y\":0},{\"x\":5,\"y\":5}],\"text\":\"Note\",\"layer\":\"A-ANNO\"}")]
        [InlineData("CreateTable", "create_table", "{\"insertion_point\":{\"x\":0,\"y\":0},\"rows\":2,\"columns\":2,\"cells\":[[\"A\",\"B\"],[\"C\",\"D\"]],\"layer\":\"A-TABLE\"}")]
        public void Validate_AnnotationSchemasAcceptValidShapes(
            string schemaName,
            string commandName,
            string json)
        {
            var schema = GetCommandSchema(schemaName);

            AssertValid(commandName, JObject.Parse(json), schema);
        }

        [Theory]
        [InlineData("CreateText", "create_text", "text")]
        [InlineData("CreateText", "create_text", "position")]
        [InlineData("CreateMText", "create_mtext", "text")]
        [InlineData("CreateMText", "create_mtext", "location")]
        [InlineData("CreateLeader", "create_leader", "points")]
        [InlineData("CreateTable", "create_table", "insertion_point")]
        [InlineData("CreateTable", "create_table", "rows")]
        [InlineData("CreateTable", "create_table", "columns")]
        [InlineData("CreateTable", "create_table", "cells")]
        public void Validate_AnnotationSchemasRequireContractFields(
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
                case "create_text":
                    return JObject.Parse("{\"text\":\"Room 101\",\"position\":{\"x\":0,\"y\":0}}");
                case "create_mtext":
                    return JObject.Parse("{\"text\":\"General notes\",\"location\":{\"x\":1,\"y\":2}}");
                case "create_leader":
                    return JObject.Parse("{\"points\":[{\"x\":0,\"y\":0},{\"x\":5,\"y\":5}]}");
                case "create_table":
                    return JObject.Parse("{\"insertion_point\":{\"x\":0,\"y\":0},\"rows\":2,\"columns\":2,\"cells\":[[\"A\",\"B\"],[\"C\",\"D\"]]}");
                default:
                    throw new ArgumentOutOfRangeException(nameof(commandName), commandName, "Unknown annotation command.");
            }
        }
    }
}
