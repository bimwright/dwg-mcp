using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ChangeLayerHandler : IAcadCommand
    {
        public string Name => "change_layer";
        public string Description => "Move entities identified by handle to a target layer.";
        public CommandSchema Schema => CommandSchemas.ChangeLayer;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var handles = obj["handles"] as JArray;
            if (handles == null)
            {
                return CommandResult.Fail("handles must be an array");
            }

            var layer = obj["layer"]?.Value<string>();
            var createLayer = obj["create_layer"]?.Value<bool>() ?? false;
            if (!CadWire.TryReadAciColor(parameters, "color_index", 7, out var colorIndex, out var colorError))
            {
                return CommandResult.Fail(colorError);
            }

            var db = doc.Database;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                bool createdLayer;
                if (createLayer)
                {
                    if (!CadLayerService.TryEnsureLayer(db, tx, layer, colorIndex, out _, out createdLayer, out var ensureError))
                    {
                        return CommandResult.Fail(ensureError);
                    }
                }
                else
                {
                    createdLayer = false;
                    if (!TryRequireExistingLayer(db, tx, layer, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }
                }

                var results = new List<object>();
                foreach (var handleToken in handles)
                {
                    if (!TryReadHandle(handleToken, out var handle, out var handleError))
                    {
                        results.Add(new { handle, ok = false, entity = (object)null, error = handleError });
                        continue;
                    }

                    if (!CadHandleResolver.TryResolve(db, handle, out var objectId, out var resolveError))
                    {
                        results.Add(new { handle, ok = false, entity = (object)null, error = resolveError });
                        continue;
                    }

                    try
                    {
                        var objForWrite = tx.GetObject(objectId, OpenMode.ForWrite);
                        var entity = objForWrite as Entity;
                        if (entity == null)
                        {
                            results.Add(new
                            {
                                handle,
                                ok = false,
                                entity = (object)null,
                                error = "object is not an entity: " + objForWrite.GetType().Name
                            });
                            continue;
                        }

                        entity.Layer = layer;
                        results.Add(new
                        {
                            handle,
                            ok = true,
                            entity = CadEntityProperties.Describe(entity, tx, includeGeometry: false),
                            error = (string)null
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { handle, ok = false, entity = (object)null, error = ErrorSanitizer.Sanitize(ex.Message) });
                    }
                }

                tx.Commit();
                return CommandResult.Success(new
                {
                    layer,
                    created_layer = createdLayer,
                    results
                });
            }
        }

        private static bool TryRequireExistingLayer(Database db, Transaction tx, string layer, out string error)
        {
            error = null;
            if (!CadLayerService.TryValidateLayerName(layer, out error))
            {
                return false;
            }

            var layerTable = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(layer))
            {
                error = "target layer does not exist";
                return false;
            }

            return true;
        }

        private static bool TryReadHandle(JToken token, out string handle, out string error)
        {
            handle = null;
            error = null;

            if (token == null || token.Type == JTokenType.Null)
            {
                error = "handle must be a string";
                return false;
            }

            if (token.Type != JTokenType.String)
            {
                handle = token.ToString(Formatting.None);
                error = "handle must be a string";
                return false;
            }

            handle = token.Value<string>();
            if (string.IsNullOrWhiteSpace(handle))
            {
                error = "handle must be a non-empty hexadecimal string";
                return false;
            }

            return true;
        }
    }
}
