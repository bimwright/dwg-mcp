using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Export;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.View
{
    internal static class ViewReadingService
    {
        internal static readonly ViewCaptureStore History = new ViewCaptureStore();

        internal static JObject Navigate(Document doc, string sourceId, JObject region, bool restoring, int pixelSize)
        {
            var source = History.Get(sourceId);
            // Validate before allocating a path or changing the view.
            ViewReadingContract.ValidateSource(source, CaptureViewService.ReadContext(doc), restoring);
            if (!restoring) ViewReadingContract.RegionCamera(source, region);
            string path = CaptureViewService.BuildDefaultOutputPath("png", out var error);
            if (path == null) throw new InvalidOperationException(error);
            path = ExportPathPolicy.ValidateAndNormalize(path, ".image", false, false, out error);
            if (path == null) throw new InvalidOperationException(error);
            var result = ViewReadingWorkflow.Run(source, region, restoring,
                () => CaptureViewService.ReadContext(doc),
                camera => ApplyCamera(doc, camera),
                applied => CaptureViewService.Capture(doc, path, pixelSize, false,
                    (string)source["document"]["fingerprint"], applied));
            History.Remember(result);
            return result;
        }

        private static void ApplyCamera(Document doc, JObject camera)
        {
            using (var view = doc.Editor.GetCurrentView())
            {
                view.CenterPoint = new Point2d((double)camera["center_dcs"][0], (double)camera["center_dcs"][1]);
                view.Width = (double)camera["width"];
                view.Height = (double)camera["height"];
                view.Target = new Point3d((double)camera["target_wcs"][0], (double)camera["target_wcs"][1], (double)camera["target_wcs"][2]);
                view.ViewDirection = new Vector3d((double)camera["direction_wcs"][0], (double)camera["direction_wcs"][1], (double)camera["direction_wcs"][2]);
                view.ViewTwist = (double)camera["twist_radians"];
                view.PerspectiveEnabled = (bool)camera["perspective"];
                view.LensLength = (double)camera["lens_length"];
                view.FrontClipEnabled = (bool)camera["front_clip_enabled"];
                view.BackClipEnabled = (bool)camera["back_clip_enabled"];
                view.FrontClipDistance = (double)camera["front_clip_distance"];
                view.BackClipDistance = (double)camera["back_clip_distance"];
                doc.Editor.SetCurrentView(view);
            }
            // Synchronous regeneration plus camera readback, not a guessed sleep.
            // Pixel freshness still requires host-specific live acceptance.
            doc.Editor.Regen();
        }
    }
}
