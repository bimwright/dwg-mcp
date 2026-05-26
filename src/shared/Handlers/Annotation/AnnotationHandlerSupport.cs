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
            var ownsEntity = true;
            try
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

                return Append(doc, entity, failurePrefix, () => ownsEntity = false, tx =>
                {
                    if (!CreatePrimitiveInput.TryEnsureLayer(doc.Database, tx, layer, hasLayer, colorIndex, out var layerError))
                    {
                        return layerError;
                    }

                    CreatePrimitiveInput.ApplyEntityOptions(entity, layer, hasLayer, colorIndex, hasColorIndex);
                    return null;
                });
            }
            finally
            {
                if (ownsEntity)
                {
                    entity.Dispose();
                }
            }
        }

        internal static CommandResult AppendWithLayerOption(
            Document doc,
            JObject obj,
            Entity entity,
            string failurePrefix)
        {
            var ownsEntity = true;
            try
            {
                var layer = obj["layer"]?.Value<string>();
                var hasLayer = obj["layer"] != null && obj["layer"].Type != JTokenType.Null;

                return Append(doc, entity, failurePrefix, () => ownsEntity = false, tx =>
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
            finally
            {
                if (ownsEntity)
                {
                    entity.Dispose();
                }
            }
        }

        private static CommandResult Append(
            Document doc,
            Entity entity,
            string failurePrefix,
            Action transferOwnership,
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
                    // AutoCAD transaction owns the entity after AddNewlyCreatedDBObject.
                    transferOwnership();
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
