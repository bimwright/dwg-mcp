using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Drawing;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class GetVariablesHandler : IAcadCommand
    {
        public string Name => "get_variables";
        public string Description => "Read current values of an allowlist of AutoCAD drawing system variables.";
        public CommandSchema Schema => CommandSchemas.GetVariables;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var variables = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var readables = new[] { "CLAYER", "INSUNITS", "LUNITS", "DIMSCALE", "TEXTSIZE", "OSMODE", "ORTHOMODE" };

            try
            {
                foreach (var name in readables)
                {
                    try
                    {
                        var value = Application.GetSystemVariable(name);
                        variables[name] = value;
                    }
                    catch (Exception ex)
                    {
                        variables[name] = "error: " + ex.Message;
                    }
                }

                return CommandResult.Success(new { variables });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to get system variables: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class SetSystemVariableHandler : IAcadCommand
    {
        public string Name => "set_system_variable";
        public string Description => "Set the value of a drawing system variable. Rejects variable names not in the write allowlist.";
        public CommandSchema Schema => CommandSchemas.SetSystemVariable;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var name = obj["name"]?.Value<string>();
            var rawValue = obj["value"];

            if (string.IsNullOrWhiteSpace(name))
            {
                return CommandResult.Fail("variable name is required");
            }

            name = name.Trim().ToUpperInvariant();

            if (!SystemVariableCatalog.IsWritable(name))
            {
                return CommandResult.Fail($"system variable {name} is read-only or not in the write allowlist");
            }

            // Coerce type
            JValue jVal = rawValue as JValue;
            object valObj = jVal?.Value;
            if (!SystemVariableCatalog.TryCoerceValue(name, valObj, out var coerced, out var error))
            {
                return CommandResult.Fail(error);
            }

            try
            {
                Application.SetSystemVariable(name, coerced);
                return CommandResult.Success(new
                {
                    name,
                    value = coerced
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail($"failed to set system variable {name}: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class SaveDrawingHandler : IAcadCommand
    {
        public string Name => "save_drawing";
        public string Description => "Save the current drawing. If output_path is omitted, saves the current drawing file (requires confirm=true).";
        public CommandSchema Schema => CommandSchemas.SaveDrawing;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var outputPath = obj["output_path"]?.Value<string>();
            var confirm = obj["confirm"]?.Value<bool>();
            var overwriteExisting = obj["overwrite_existing"]?.Value<bool>() ?? false;
            var allowRepoOutput = obj["allow_repo_output"]?.Value<bool>() ?? false;

            try
            {
                var savedPath = DrawingSaveService.Save(doc, outputPath, confirm, overwriteExisting, allowRepoOutput, out var error);
                if (savedPath == null)
                {
                    return CommandResult.Fail(error);
                }

                return CommandResult.Success(new { saved_path = savedPath });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to save drawing: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class PurgeDrawingHandler : IAcadCommand
    {
        public string Name => "purge_drawing";
        public string Description => "Purge unused named objects (layers, blocks, styles) from the current drawing.";
        public CommandSchema Schema => CommandSchemas.PurgeDrawing;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var dryRun = obj["dry_run"]?.Value<bool>() ?? false;
            var confirm = obj["confirm"]?.Value<bool>();

            try
            {
                var result = PurgeDrawingService.Purge(doc.Database, dryRun, confirm, out var error);
                if (result == null)
                {
                    return CommandResult.Fail(error);
                }

                return CommandResult.Success(new
                {
                    dry_run = dryRun,
                    purged_count = result.Items.Count,
                    items = result.Items
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to purge drawing: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }
}
