using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Export;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ZoomExtentsHandler : IAcadCommand
    {
        public string Name => "zoom_extents";
        public string Description => "Zoom to the extents of the drawing viewport.";
        public CommandSchema Schema => CommandSchemas.ZoomExtents;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try
            {
                var viewInfo = ViewZoomService.ZoomExtents(doc);
                return CommandResult.Success(viewInfo);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to zoom extents: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class ZoomWindowHandler : IAcadCommand
    {
        public string Name => "zoom_window";
        public string Description => "Zoom viewport to a window defined by two corner points.";
        public CommandSchema Schema => CommandSchemas.ZoomWindow;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!CadWire.TryParsePoint(obj["corner1"], out var c1, out var err1))
            {
                return CommandResult.Fail("corner1 " + err1);
            }

            if (!CadWire.TryParsePoint(obj["corner2"], out var c2, out var err2))
            {
                return CommandResult.Fail("corner2 " + err2);
            }

            try
            {
                var pt1 = new Point3d(c1.X, c1.Y, c1.Z);
                var pt2 = new Point3d(c2.X, c2.Y, c2.Z);
                var viewInfo = ViewZoomService.ZoomWindow(doc, pt1, pt2);
                return CommandResult.Success(viewInfo);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to zoom window: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class ZoomToEntityHandler : IAcadCommand
    {
        public string Name => "zoom_to_entity";
        public string Description => "Zoom viewport to the extents of a specific drawing entity identified by handle.";
        public CommandSchema Schema => CommandSchemas.ZoomToEntity;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var handle = obj["handle"]?.Value<string>();
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!CadHandleResolver.TryResolve(db, handle, out var objectId, out var err))
                    {
                        return CommandResult.Fail(err);
                    }

                    var entity = tx.GetObject(objectId, OpenMode.ForRead) as Entity;
                    if (entity == null)
                    {
                        return CommandResult.Fail("object is not a geometric entity");
                    }

                    var viewInfo = ViewZoomService.ZoomToEntity(doc, entity);
                    tx.Commit();
                    return CommandResult.Success(viewInfo);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to zoom to entity: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class CaptureViewImageHandler : IAcadCommand
    {
        public string Name => "capture_view_image";
        public string Description => "Capture the active drawing inside AutoCAD and return its image path, document and camera metadata.";
        public CommandSchema Schema => CommandSchemas.CaptureViewImage;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var obj = parameters as JObject ?? new JObject();

            var outputPath = obj["output_path"]?.Value<string>();
            var pixelSize = obj["pixel_size"]?.Value<int>() ?? 1600;
            var imageFormat = obj["image_format"]?.Value<string>();
            var overwrite = obj["overwrite_existing"]?.Value<bool>() ?? false;
            var allowRepo = obj["allow_repo_output"]?.Value<bool>() ?? false;
            var expectedFingerprint = obj["expected_document_fingerprint"]?.Value<string>();

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = CaptureViewService.BuildDefaultOutputPath(imageFormat, out var pathError);
                if (outputPath == null)
                {
                    return CommandResult.Fail(pathError);
                }
            }

            var normalizedPath = ExportPathPolicy.ValidateAndNormalize(
                outputPath, ".image", overwrite, allowRepo, out var error);
            if (normalizedPath == null)
            {
                return CommandResult.Fail(error);
            }

            try
            {
                var result = CaptureViewService.Capture(doc, normalizedPath, pixelSize, overwrite, expectedFingerprint);
                ViewReadingService.History.Remember(result);
                return CommandResult.Success(result);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to capture view: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class InspectViewRegionHandler : IAcadCommand
    {
        public string Name => "inspect_view_region";
        public string Description => "Zoom to a normalized region of a source capture, regenerate and return a new capture.";
        public CommandSchema Schema => CommandSchemas.InspectViewRegion;
        public CommandResult Execute(Document doc, JToken parameters) => Navigate(doc, parameters, false);

        internal static CommandResult Navigate(Document doc, JToken parameters, bool restoring)
        {
            try
            {
                var result = ViewReadingService.Navigate(doc, (string)parameters["source_capture_id"],
                    parameters["region"] as JObject, restoring, (int?)parameters["pixel_size"] ?? 1600);
                return CommandResult.Success(result);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("view navigation failed: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class RestoreViewHandler : IAcadCommand
    {
        public string Name => "restore_view";
        public string Description => "Restore a recent supported capture's camera and return a fresh image.";
        public CommandSchema Schema => CommandSchemas.RestoreView;
        public CommandResult Execute(Document doc, JToken parameters) => InspectViewRegionHandler.Navigate(doc, parameters, true);
    }
}
