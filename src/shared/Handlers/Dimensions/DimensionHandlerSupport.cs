using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Dimensions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    internal delegate bool DimensionFactory(
        Database db,
        Transaction tx,
        ObjectId dimensionStyleId,
        out Dimension entity,
        out string error);

    internal static class DimensionHandlerSupport
    {
        internal static CommandResult CreateAndAppend(
            Document doc,
            JObject obj,
            string failurePrefix,
            DimensionFactory factory)
        {
            var db = doc.Database;
            Dimension entity = null;
            var ownsEntity = true;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var styleToken = obj["style_name"];
                    var hasStyleName = styleToken != null && styleToken.Type != JTokenType.Null;
                    var styleName = styleToken?.Value<string>();
                    if (!DimensionStyleResolver.TryResolve(db, tx, styleName, hasStyleName, out var dimensionStyleId, out var styleError))
                    {
                        return CommandResult.Fail(styleError);
                    }

                    if (!factory(db, tx, dimensionStyleId, out entity, out var factoryError))
                    {
                        return CommandResult.Fail(factoryError);
                    }

                    if (!TryApplyLayerOption(db, tx, obj, entity, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    Action transferOwnership = () => ownsEntity = false;
                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity);
                    // AutoCAD transaction owns the dimension after AddNewlyCreatedDBObject.
                    transferOwnership();

                    var result = new
                    {
                        handle = entity.Handle.ToString(),
                        entity = CadEntityProperties.Describe(entity, tx, includeGeometry: true)
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(failurePrefix + ": " + ErrorSanitizer.Sanitize(ex.Message));
            }
            finally
            {
                if (ownsEntity && entity != null)
                {
                    entity.Dispose();
                }
            }
        }

        private static bool TryApplyLayerOption(
            Database db,
            Transaction tx,
            JObject obj,
            Entity entity,
            out string error)
        {
            error = null;

            var layer = obj["layer"]?.Value<string>();
            var hasLayer = obj["layer"] != null && obj["layer"].Type != JTokenType.Null;
            if (!hasLayer)
            {
                return true;
            }

            if (!CadLayerService.TryEnsureLayer(db, tx, layer, 7, out _, out _, out error))
            {
                return false;
            }

            entity.Layer = layer;
            return true;
        }
    }
}
