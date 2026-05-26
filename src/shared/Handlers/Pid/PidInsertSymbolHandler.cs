using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Pid;
using Bimwright.Dwg.Pid;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidInsertSymbolHandler : IAcadCommand
    {
        public string Name => "pid_insert_symbol";
        public string Description => "Insert a procedural P&ID symbol in current drawing space.";
        public CommandSchema Schema => CommandSchemas.InsertSymbol;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var category = obj["category"]?.Value<string>();
            var symbol = obj["symbol"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(symbol))
            {
                return CommandResult.Fail("category and symbol are required");
            }

            // Load and validate config
            var config = PidConfig.Load();
            try
            {
                config.Validate();
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(ex.Message);
            }

            if (!CadWire.TryParsePoint(obj["position"], out var position, out var posError))
            {
                return CommandResult.Fail("position " + posError);
            }

            double scale = 1.0;
            if (obj["scale"] != null && obj["scale"].Type != JTokenType.Null)
            {
                scale = obj["scale"].Value<double>();
            }

            double rotationDeg = 0.0;
            if (obj["rotation"] != null && obj["rotation"].Type != JTokenType.Null)
            {
                rotationDeg = obj["rotation"].Value<double>();
            }
            double rotationRad = rotationDeg * Math.PI / 180.0;

            var textContent = obj["text_content"]?.Value<string>();

            // Check if symbol exists in catalog
            var validSymbols = PidCatalog.GetSymbols(category);
            if (!validSymbols.Contains(symbol))
            {
                return CommandResult.Fail($"Symbol '{symbol}' is not supported under category '{category}' in current catalog.");
            }

            var centerPoint = new Point3d(position.X, position.Y, position.Z);
            var db = doc.Database;

            // Resolve target layer and color
            string layerName = "PID-EQUIPMENT";
            int layerColor = 6;

            if (string.Equals(category, "VALVES", StringComparison.OrdinalIgnoreCase))
            {
                layerName = "PID-VALVES";
                layerColor = 2;
            }
            else if (string.Equals(category, "ANNOTATION", StringComparison.OrdinalIgnoreCase))
            {
                layerName = "PID-ANNOTATION";
                layerColor = 7;
            }

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!CadLayerService.TryEnsureLayer(db, tx, layerName, layerColor, out _, out _, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    List<Entity> entities;
                    if (string.Equals(category, "PUMPS-BLOWERS", StringComparison.OrdinalIgnoreCase))
                    {
                        entities = PidProceduralGeometry.DrawPump(centerPoint, scale, rotationRad);
                    }
                    else if (string.Equals(category, "TANKS", StringComparison.OrdinalIgnoreCase))
                    {
                        entities = PidProceduralGeometry.DrawTank(centerPoint, scale, rotationRad, symbol);
                    }
                    else if (string.Equals(category, "VALVES", StringComparison.OrdinalIgnoreCase))
                    {
                        entities = PidProceduralGeometry.DrawValve(centerPoint, scale, rotationRad);
                    }
                    else
                    {
                        // Default/Generic Equipment
                        string displayText = string.IsNullOrWhiteSpace(textContent) ? symbol : textContent;
                        entities = PidProceduralGeometry.DrawGenericEquipment(centerPoint, scale, rotationRad, displayText);
                    }

                    var handles = new List<string>();
                    foreach (var ent in entities)
                    {
                        ent.Layer = layerName;
                        CadPrimitiveWriter.AppendToCurrentSpace(db, tx, ent);
                        handles.Add(ent.Handle.ToString());
                    }

                    tx.Commit();

                    return CommandResult.Success(new
                    {
                        source = "procedural",
                        category,
                        symbol,
                        handles
                    });
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to insert symbol: " + ex.Message);
            }
        }
    }
}
