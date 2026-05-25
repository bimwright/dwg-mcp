using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.ToolBaker;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ApplyBakeSuggestionHandler : IAcadCommand
    {
        private readonly Func<string, JToken, CommandResult> _preflightCommand;

        public ApplyBakeSuggestionHandler(Func<string, JToken, CommandResult> preflightCommand = null)
        {
            _preflightCommand = preflightCommand;
        }

        public string Name => "apply_bake";
        public string Description => "Validate and smoke-test an accepted baked DWG tool before server persistence.";
        public CommandSchema Schema => CommandSchemas.ApplyBake;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try
            {
                var record = BakedToolRuntimeCommandFactory.FromApplyRequest(parameters as JObject);
                var compile = ToolCompiler.CompileAndSmokeTest(record, Preflight);
                if (!compile.Ok)
                {
                    return CommandResult.Success(new
                    {
                        success = false,
                        error_code = "compile_or_smoke_test_failed",
                        message = compile.Error
                    });
                }

                return CommandResult.Success(new
                {
                    success = true,
                    tool_name = record.Name,
                    description = record.Description,
                    params_schema = record.ParamsSchema,
                    source_code = record.SourceCode
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Success(new
                {
                    success = false,
                    error_code = "apply_bake_failed",
                    message = ex.Message
                });
            }
        }

        private BakePolicyResult Preflight(string commandName, JToken parameters)
        {
            if (_preflightCommand == null)
            {
                return new BakePolicyResult { Ok = true };
            }

            var result = _preflightCommand(commandName, parameters);
            return result.Ok
                ? new BakePolicyResult { Ok = true }
                : new BakePolicyResult { Ok = false, Error = result.Error };
        }
    }
}
