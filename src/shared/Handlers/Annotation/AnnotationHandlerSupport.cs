using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    internal static class AnnotationHandlerSupport
    {
        internal static CommandResult AppendWithEntityOptions(
            Document doc,
            JObject obj,
            JToken parameters,
            Entity entity,
            string failurePrefix)
        {
            if (!CreatePrimitiveInput.TryReadEntityOptions(
                obj,
                parameters,
                out var layer,
                out var hasLayer,
                out var colorIndex,
                out var hasColorIndex,
                out var optionsError))
            {
                return CommandResult.Fail(optionsError);
            }

            return Append(doc, entity, failurePrefix, tx =>
            {
                if (!CreatePrimitiveInput.TryEnsureLayer(doc.Database, tx, layer, hasLayer, colorIndex, out var layerError))
                {
                    return layerError;
                }

                CreatePrimitiveInput.ApplyEntityOptions(entity, layer, hasLayer, colorIndex, hasColorIndex);
                return null;
            });
        }

        internal static CommandResult AppendWithLayerOption(
            Document doc,
            JObject obj,
            Entity entity,
            string failurePrefix)
        {
            var layer = obj["layer"]?.Value<string>();
            var hasLayer = obj["layer"] != null && obj["layer"].Type != JTokenType.Null;

            return Append(doc, entity, failurePrefix, tx =>
            {
                if (hasLayer &&
                    !CadLayerService.TryEnsureLayer(doc.Database, tx, layer, 7, out _, out _, out var layerError))
                {
                    return layerError;
                }

                if (hasLayer)
                {
                    entity.Layer = layer;
                }

                return null;
            });
        }

        private static CommandResult Append(
            Document doc,
            Entity entity,
            string failurePrefix,
            Func<Transaction, string> configure)
        {
            var db = doc.Database;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var configureError = configure(tx);
                    if (configureError != null)
                    {
                        return CommandResult.Fail(configureError);
                    }

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity);
                    var described = CadEntityProperties.Describe(entity, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = entity.Handle.ToString(),
                        entity = described
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(failurePrefix + ": " + ex.Message);
            }
        }
    }
}
