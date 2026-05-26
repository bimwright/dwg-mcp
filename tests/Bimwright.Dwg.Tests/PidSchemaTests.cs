using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class PidSchemaTests
    {
        [Fact]
        public void Validate_PidSetupLayers_ValidAndInvalid()
        {
            var schema = CommandSchemas.SetupLayers;

            var validEmpty = JObject.Parse("{}");
            var resultEmpty = SchemaValidator.Validate("pid_setup_layers", validEmpty, schema);
            Assert.True(resultEmpty.Ok);

            var validParams = JObject.Parse("{\"include_wwtp_layers\": true}");
            var resultParams = SchemaValidator.Validate("pid_setup_layers", validParams, schema);
            Assert.True(resultParams.Ok);

            var invalidParams = JObject.Parse("{\"include_wwtp_layers\": \"string_instead_of_bool\"}");
            var resultInvalid = SchemaValidator.Validate("pid_setup_layers", invalidParams, schema);
            Assert.False(resultInvalid.Ok);
        }

        [Fact]
        public void Validate_PidListCategories_Valid()
        {
            var schema = CommandSchemas.ListCategories;
            var result = SchemaValidator.Validate("pid_list_categories", null, schema);
            Assert.True(result.Ok);
        }

        [Fact]
        public void Validate_PidListSymbols_ValidAndInvalid()
        {
            var schema = CommandSchemas.ListSymbols;

            var valid = JObject.Parse("{\"category\": \"PUMPS-BLOWERS\"}");
            var resultValid = SchemaValidator.Validate("pid_list_symbols", valid, schema);
            Assert.True(resultValid.Ok);

            var invalidMissing = JObject.Parse("{}");
            var resultMissing = SchemaValidator.Validate("pid_list_symbols", invalidMissing, schema);
            Assert.False(resultMissing.Ok);

            var invalidEmptyStr = JObject.Parse("{\"category\": \"   \"}");
            var resultEmptyStr = SchemaValidator.Validate("pid_list_symbols", invalidEmptyStr, schema);
            Assert.False(resultEmptyStr.Ok);
        }

        [Fact]
        public void Validate_PidDrawPipe_ValidAndInvalid()
        {
            var schema = CommandSchemas.DrawPipe;

            var valid = JObject.Parse("{\"start\": {\"x\": 1.0, \"y\": 2.0}, \"end\": {\"x\": 3.0, \"y\": 4.0}, \"layer\": \"PID-PROCESS-PIPING\"}");
            var resultValid = SchemaValidator.Validate("pid_draw_pipe", valid, schema);
            Assert.True(resultValid.Ok);

            var invalidMissing = JObject.Parse("{\"start\": {\"x\": 1.0, \"y\": 2.0}}");
            var resultMissing = SchemaValidator.Validate("pid_draw_pipe", invalidMissing, schema);
            Assert.False(resultMissing.Ok);
        }

        [Fact]
        public void Validate_PidInsertSymbol_ValidAndInvalid()
        {
            var schema = CommandSchemas.InsertSymbol;

            var valid = JObject.Parse("{\"category\": \"VALVES\", \"symbol\": \"VA-KNIFEGATE\", \"position\": {\"x\":0,\"y\":0}, \"scale\": 2.5, \"rotation\": 90, \"text_content\": \"KV-101\"}");
            var resultValid = SchemaValidator.Validate("pid_insert_symbol", valid, schema);
            Assert.True(resultValid.Ok);

            var invalidMissing = JObject.Parse("{\"category\": \"VALVES\", \"symbol\": \"VA-KNIFEGATE\"}");
            var resultMissing = SchemaValidator.Validate("pid_insert_symbol", invalidMissing, schema);
            Assert.False(resultMissing.Ok);
        }

        [Fact]
        public void Validate_PidAddFlowArrow_ValidAndInvalid()
        {
            var schema = CommandSchemas.AddFlowArrow;

            var valid = JObject.Parse("{\"position\": {\"x\": 5, \"y\": 5}, \"direction\": {\"x\": 1, \"y\": 0}}");
            var resultValid = SchemaValidator.Validate("pid_add_flow_arrow", valid, schema);
            Assert.True(resultValid.Ok);

            var invalidMissing = JObject.Parse("{\"position\": {\"x\": 5, \"y\": 5}}");
            var resultMissing = SchemaValidator.Validate("pid_add_flow_arrow", invalidMissing, schema);
            Assert.False(resultMissing.Ok);
        }

        [Fact]
        public void Validate_PidAddEquipmentTag_ValidAndInvalid()
        {
            var schema = CommandSchemas.AddEquipmentTag;

            var valid = JObject.Parse("{\"position\": {\"x\": 10, \"y\": 10}, \"tag_text\": \"T-101\"}");
            var resultValid = SchemaValidator.Validate("pid_add_equipment_tag", valid, schema);
            Assert.True(resultValid.Ok);

            var invalidMissing = JObject.Parse("{\"position\": {\"x\": 10, \"y\": 10}}");
            var resultMissing = SchemaValidator.Validate("pid_add_equipment_tag", invalidMissing, schema);
            Assert.False(resultMissing.Ok);
        }

        [Fact]
        public void Validate_PidAddLineNumber_ValidAndInvalid()
        {
            var schema = CommandSchemas.AddLineNumber;

            var valid = JObject.Parse("{\"position\": {\"x\": 15, \"y\": 15}, \"line_text\": \"150-Process-01\"}");
            var resultValid = SchemaValidator.Validate("pid_add_line_number", valid, schema);
            Assert.True(resultValid.Ok);

            var invalidMissing = JObject.Parse("{\"position\": {\"x\": 15, \"y\": 15}}");
            var resultMissing = SchemaValidator.Validate("pid_add_line_number", invalidMissing, schema);
            Assert.False(resultMissing.Ok);
        }
    }
}
