using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>Called by DocumentInvoker on the AutoCAD thread under its document lock.</summary>
    public sealed class DrawingProfileHandler : IAcadCommand
    {
        public string Name => "get_drawing_profile";
        public string Description => "Count top-level model/layout entities without expanding blocks or external references.";
        public CommandSchema Schema => CommandSchemas.GetDrawingProfile;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var validation = SchemaValidator.Validate(Name, parameters, Schema);
            if (!validation.Ok) return CommandResult.Fail(validation.Error);
            DrawingProfileOptions options;
            try
            {
                var values = parameters as JObject;
                options = DrawingProfileOptions.Create(values?.Value<string>("section") ?? "summary",
                    values?.Value<int?>("offset") ?? 0,
                    values?.Value<int?>("max_output_bytes") ?? DrawingProfileOptions.DefaultMaxOutputBytes,
                    values?.Value<string>("expected_snapshot"));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            { return CommandResult.Fail("INVALID_ARGUMENT: " + ex.Message); }
            if (doc == null) return CommandResult.Fail(DrawingSessionContract.NoActiveDocument);

            try
            {
                var revision = DrawingRevision.For(doc);
                var snapshot = Snapshot(revision);
                if (options.ExpectedSnapshot != null && !string.Equals(options.ExpectedSnapshot, snapshot, StringComparison.Ordinal))
                    return CommandResult.Fail("PROFILE_CHANGED: the drawing lifetime or revision changed; restart the profile at offset=0.");
                var profile = Collect(doc.Database);
                if (!string.Equals(snapshot, Snapshot(revision), StringComparison.Ordinal))
                    return CommandResult.Fail("PROFILE_CHANGED: the drawing changed during enumeration; restart the profile at offset=0.");
                return CommandResult.Success(profile.ToJson(DocumentTargetGuard.Id(doc), snapshot, options));
            }
            catch (Exception ex)
            { return CommandResult.Fail("Could not inspect the active drawing: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }

        private static string Snapshot(DrawingRevision revision)
            => revision.SessionId + ":" + revision.ProfileRevision.ToString(CultureInfo.InvariantCulture);

        private static DrawingProfile Collect(Database database)
        {
            var profile = new DrawingProfile();
            try
            {
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                    var modelId = blocks[BlockTableRecord.ModelSpace];
                    foreach (ObjectId recordId in blocks)
                    {
                        BlockTableRecord record;
                        try
                        {
                            if (recordId.IsErased) continue;
                            record = (BlockTableRecord)transaction.GetObject(recordId, OpenMode.ForRead);
                            if (!record.IsLayout) continue;
                        }
                        catch { profile.FailedSpaceRead(); continue; }
                        var metadataComplete = true;
                        string id = null, recordName = null, layoutName = null;
                        try
                        {
                            id = record.Handle.ToString();
                            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Space handle is unavailable.");
                        }
                        catch { id = null; metadataComplete = false; profile.FailedSpaceRead(); }
                        try
                        {
                            recordName = record.Name;
                            if (string.IsNullOrEmpty(recordName)) throw new InvalidOperationException("Space record name is unavailable.");
                        }
                        catch { recordName = null; metadataComplete = false; profile.FailedSpaceRead(); }
                        var model = recordId == modelId;
                        if (!model)
                        {
                            try
                            {
                                layoutName = ((Layout)transaction.GetObject(record.LayoutId, OpenMode.ForRead)).LayoutName;
                                if (string.IsNullOrEmpty(layoutName)) throw new InvalidOperationException("Layout name is unavailable.");
                            }
                            catch { layoutName = null; metadataComplete = false; profile.FailedSpaceRead(); }
                        }
                        var space = profile.AddSpace(id, model ? "Model" : layoutName ?? recordName,
                            model ? "model" : "layout", recordName, layoutName,
                            model ? "model" : layoutName == null ? "block_table_record" : "layout", metadataComplete);
                        try
                        {
                            foreach (ObjectId entityId in record)
                            {
                                profile.SawId();
                                Entity entity;
                                try
                                {
                                    if (entityId.IsErased) { profile.SkippedErased(); continue; }
                                    entity = transaction.GetObject(entityId, OpenMode.ForRead) as Entity;
                                    if (entity == null) throw new InvalidOperationException("Layout record contains a non-entity object.");
                                }
                                catch { profile.FailedEntityRead(space); continue; }
                                string type = null, layer = null;
                                try { type = entity.GetType().FullName; } catch { }
                                try { layer = entity.Layer; } catch { }
                                var block = entity as BlockReference;
                                bool? xref = null;
                                if (block != null)
                                {
                                    try
                                    {
                                        var definition = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                                        xref = definition.IsFromExternalReference || definition.IsFromOverlayReference;
                                    }
                                    catch { }
                                }
                                profile.ReadEntity(space, type, layer, block != null, xref);
                            }
                        }
                        catch { profile.FailedEnumeration(space); }
                    }
                    transaction.Commit();
                }
            }
            catch { profile.FailedEnumeration(); }
            return profile;
        }
    }
}
