using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class GetEntityPropertiesHandler : IAcadCommand
    {
        public string Name => "get_entity_properties";
        public string Description => "Return properties for entities identified by handle.";
        public CommandSchema Schema => CommandSchemas.GetEntityProperties;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            string[] handles;
            try
            {
                handles = CadWire.ReadStringArray(parameters, "handles");
            }
            catch (ArgumentException ex)
            {
                return CommandResult.Fail(ex.Message);
            }

            var includeGeometry = parameters?["include_geometry"]?.Value<bool>() ?? false;
            var results = new List<object>();
            var db = doc.Database;

            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var handle in handles)
                {
                    if (!CadHandleResolver.TryResolve(db, handle, out var objectId, out var error))
                    {
                        results.Add(new { handle, ok = false, entity = (object)null, error });
                        continue;
                    }

                    try
                    {
                        var obj = tx.GetObject(objectId, OpenMode.ForRead);
                        var entity = obj as Entity;
                        if (entity == null)
                        {
                            results.Add(new
                            {
                                handle,
                                ok = false,
                                entity = (object)null,
                                error = $"object is not an entity: {obj.GetType().Name}"
                            });
                            continue;
                        }

                        results.Add(new
                        {
                            handle,
                            ok = true,
                            entity = CadEntityProperties.Describe(entity, tx, includeGeometry),
                            error = (string)null
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { handle, ok = false, entity = (object)null, error = ex.Message });
                    }
                }

                tx.Commit();
            }

            return CommandResult.Success(new { entities = results });
        }
    }
}
