using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Pid;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidSetupLayersHandler : IAcadCommand
    {
        public string Name => "pid_setup_layers";
        public string Description => "Setup P&ID standard layers in current document.";
        public CommandSchema Schema => CommandSchemas.SetupLayers;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var includeWwtp = false;
            if (parameters is JObject obj && obj["include_wwtp_layers"] != null && obj["include_wwtp_layers"].Type != JTokenType.Null)
            {
                includeWwtp = obj["include_wwtp_layers"].Value<bool>();
            }

            var db = doc.Database;
            var createdLayers = new List<string>();
            var existingLayers = new List<string>();

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    // Create Standard Layers
                    foreach (var layerInfo in PidLayerCatalog.StandardLayers)
                    {
                        if (CadLayerService.TryEnsureLayer(db, tx, layerInfo.Name, layerInfo.ColorIndex, out _, out var created, out var error))
                        {
                            if (created) createdLayers.Add(layerInfo.Name);
                            else existingLayers.Add(layerInfo.Name);
                        }
                        else
                        {
                            return CommandResult.Fail($"Failed to create layer '{layerInfo.Name}': {error}");
                        }
                    }

                    // Create WWTP-specific Layers if requested
                    if (includeWwtp)
                    {
                        foreach (var layerInfo in PidLayerCatalog.WwtpLayers)
                        {
                            if (CadLayerService.TryEnsureLayer(db, tx, layerInfo.Name, layerInfo.ColorIndex, out _, out var created, out var error))
                            {
                                if (created) createdLayers.Add(layerInfo.Name);
                                else existingLayers.Add(layerInfo.Name);
                            }
                            else
                            {
                                return CommandResult.Fail($"Failed to create layer '{layerInfo.Name}': {error}");
                            }
                        }
                    }

                    tx.Commit();
                }

                return CommandResult.Success(new
                {
                    created_layers = createdLayers,
                    existing_layers = existingLayers
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail($"Failed to setup P&ID layers: {ex.Message}");
            }
        }
    }
}
