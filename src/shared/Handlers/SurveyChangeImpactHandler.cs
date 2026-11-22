using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;
using AcadException = Autodesk.AutoCAD.Runtime.Exception;

namespace Bimwright.Dwg.Plugin.Handlers
{
    internal sealed class HistoryDrawingKeyHandler : IAcadCommand
    {
        public string Name => "history_drawing_key";
        public string Description => "Read the verified active drawing's history identity.";
        public CommandSchema Schema => CommandSchema.Empty;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var key = DocumentChangeCapture.DrawingKey(doc);
            return key == null ? CommandResult.Fail("HISTORY_IDENTITY_UNAVAILABLE: a saved DWG identity is required.")
                : CommandResult.Success(new { drawing_key = key });
        }
    }

    internal sealed class SurveyChangeImpactHandler : IAcadCommand
    {
        public string Name => "survey_change_impact";
        public string Description => "Inspect direct relationships of entities in the verified active drawing.";
        public CommandSchema Schema { get; } = CommandSchema.Object(SchemaProperty.Required("handles", JTokenType.Array));

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var handles = parameters?["handles"] as JArray;
            if (handles == null || handles.Count is < 1 or > 50)
                return CommandResult.Fail("handles must contain 1–50 distinct DWG handles.");
            var requested = new List<string>(handles.Count);
            var seen = new HashSet<long>();
            foreach (var item in handles)
            {
                var handle = item.Type == JTokenType.String ? item.Value<string>() : null;
                if (handle is not { Length: >= 1 and <= 16 } || !CadWire.TryParseHandleValue(handle, out var value, out _) || value == 0 || !seen.Add(value))
                    return CommandResult.Fail("handles must be distinct nonzero hexadecimal DWG handles.");
                requested.Add(new Handle(value).ToString());
            }

            var db = doc.Database;
            using (var tx = db.TransactionManager.StartOpenCloseTransaction())
            {
                var results = new JArray();
                foreach (var handle in requested) results.Add(Inspect(db, tx, handle));
                return CommandResult.Success(new { scope = "direct", entities = results });
            }
        }

        private static JObject Inspect(Database db, Transaction tx, string handle)
        {
            var result = new JObject { ["handle"] = handle, ["status"] = "unresolved" };
            if (!CadHandleResolver.TryResolve(db, handle, out var id, out _) || !id.IsValid) return result;
            try
            {
                if (id.IsErased || id.IsEffectivelyErased)
                {
                    result["status"] = "erased";
                    return result;
                }
                if (!(tx.GetObject(id, OpenMode.ForRead) is Entity entity))
                {
                    result["status"] = "not_entity";
                    return result;
                }
                result["status"] = "resolved";
                result["type"] = entity.GetType().Name;
                result["layer"] = entity.Layer;
                result["owner"] = Relationship(() => DescribeOwner(tx, entity.OwnerId));
                result["layer_state"] = Relationship(() => DescribeLayer(tx, entity.LayerId));
                if (entity is BlockReference reference)
                    result["block_definition"] = Relationship(() => DescribeBlock(tx, reference.BlockTableRecord));
            }
            catch (AcadException) { result["status"] = "unavailable"; }
            return result;
        }

        private static JObject Relationship(Func<JObject> read)
        {
            try { return read() ?? new JObject { ["status"] = "unknown" }; }
            catch (AcadException) { return new JObject { ["status"] = "unknown" }; }
        }

        private static JObject DescribeOwner(Transaction tx, ObjectId id)
        {
            if (id.IsNull) return null;
            var owner = tx.GetObject(id, OpenMode.ForRead);
            var result = new JObject { ["status"] = "resolved", ["handle"] = id.Handle.ToString(), ["space"] = "unknown" };
            if (owner is BlockTableRecord block)
            {
                result["name"] = block.Name;
                result["space"] = DocumentChangeCapture.SpaceOf(block);
            }
            return result;
        }

        private static JObject DescribeLayer(Transaction tx, ObjectId id)
        {
            if (id.IsNull || !(tx.GetObject(id, OpenMode.ForRead) is LayerTableRecord layer)) return null;
            return new JObject { ["status"] = "resolved", ["handle"] = id.Handle.ToString(), ["locked"] = layer.IsLocked,
                ["off"] = layer.IsOff, ["frozen"] = layer.IsFrozen, ["dependent"] = layer.IsDependent };
        }

        private static JObject DescribeBlock(Transaction tx, ObjectId id)
        {
            if (id.IsNull || !(tx.GetObject(id, OpenMode.ForRead) is BlockTableRecord block)) return null;
            return new JObject { ["status"] = "resolved", ["handle"] = id.Handle.ToString(), ["name"] = block.Name,
                ["is_xref"] = block.IsFromExternalReference || block.IsFromOverlayReference };
        }
    }
}
