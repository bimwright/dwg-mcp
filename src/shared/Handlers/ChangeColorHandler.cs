using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ChangeColorHandler : IAcadCommand
    {
        public string Name => "change_color";
        public string Description => "Apply an ACI color index to entities identified by handle.";
        public CommandSchema Schema => CommandSchemas.ChangeColor;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!TransformEntityHandlerSupport.TryReadHandles(parameters, out var handles, out var handlesError))
            {
                return CommandResult.Fail(handlesError);
            }

            if (!TryReadRequiredColorIndex(obj["color_index"], out var colorIndex, out var colorError))
            {
                return CommandResult.Fail(colorError);
            }

            var db = doc.Database;
            var results = new List<object>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var handleToken in handles)
                {
                    if (!TransformEntityHandlerSupport.TryReadHandle(handleToken, out var handle, out var handleError))
                    {
                        results.Add(new { handle, ok = false, error = handleError });
                        continue;
                    }

                    try
                    {
                        if (!TransformEntityHandlerSupport.TryOpenEntity(db, tx, handle, OpenMode.ForWrite, out var entity, out var openError))
                        {
                            results.Add(new { handle, ok = false, error = openError });
                            continue;
                        }

                        entity.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
                        results.Add(new { handle, ok = true, error = (string)null });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { handle, ok = false, error = ErrorSanitizer.Sanitize(ex.Message) });
                    }
                }

                tx.Commit();
            }

            return CommandResult.Success(new { color_index = colorIndex, results });
        }

        private static bool TryReadRequiredColorIndex(JToken token, out int colorIndex, out string error)
        {
            colorIndex = 0;
            error = null;

            if (token == null || token.Type == JTokenType.Null)
            {
                error = "color_index is required";
                return false;
            }

            if (token.Type != JTokenType.Integer)
            {
                error = "color_index must be an integer ACI color index between 1 and 256";
                return false;
            }

            long value;
            try
            {
                value = token.Value<long>();
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                error = "color_index must be an integer ACI color index between 1 and 256";
                return false;
            }

            if (value < 1L || value > 256L)
            {
                error = "color_index must be an ACI color index between 1 and 256";
                return false;
            }

            colorIndex = (int)value;
            return true;
        }
    }
}
