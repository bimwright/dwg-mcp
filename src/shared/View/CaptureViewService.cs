using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Bimwright.Dwg.Plugin.View
{
    /// <summary>
    /// Captures drawing graphics inside AutoCAD through its document preview API.
    /// Runs on the calling document-lock thread; never uses desktop screen capture.
    /// </summary>
    internal static class CaptureViewService
    {
        internal static object Capture(Document doc, string outputPath, int pixelSize,
            bool overwriteExisting = false, string expectedDocumentFingerprint = null)
        {
            var watch = Stopwatch.StartNew();
            var before = ReadContext(doc);
            CaptureViewContract.ValidateExpectedDocument(expectedDocumentFingerprint,
                (string)before["document"]["fingerprint"]);
            var (width, height) = CaptureViewMath.ComputeOutputSize(
                (int)before["viewport"]["screen_width"], (int)before["viewport"]["screen_height"], pixelSize);
            var capturedAt = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            using (var bitmap = doc.CapturePreviewImage((uint)width, (uint)height))
            {
                if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                    throw new InvalidOperationException("AutoCAD returned an empty capture image");

                // Do not publish an image with metadata from a different view/document.
                CaptureViewContract.EnsureUnchanged(before, ReadContext(doc));
                string hash;
                using (var stream = new FileStream(outputPath,
                    overwriteExisting ? FileMode.Create : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    bitmap.Save(stream, ResolveFormat(outputPath));
                    stream.Position = 0;
                    using (var sha = SHA256.Create())
                        hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }

                return CaptureViewContract.BuildResult(before, outputPath,
                    Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant(), bitmap.Width, bitmap.Height,
                    width, height, hash, capturedAt, watch.ElapsedMilliseconds);
            }
        }

        private static JObject ReadContext(Document doc)
        {
            if (doc == null || !ReferenceEquals(AcadApplication.DocumentManager.MdiActiveDocument, doc))
                throw new InvalidOperationException("capture requires the active drawing");
            if (Convert.ToInt32(AcadApplication.GetSystemVariable("CMDACTIVE")) != 0)
                throw new InvalidOperationException("AutoCAD command is active; retry capture after it completes");

            var db = doc.Database;
            var screen = (Point2d)AcadApplication.GetSystemVariable("SCREENSIZE");
            if (screen.X <= 0 || screen.Y <= 0)
                throw new InvalidOperationException("AutoCAD viewport has no drawable area");
            using (var view = doc.Editor.GetCurrentView())
            {
                return new JObject
                {
                    ["document"] = new JObject
                    {
                        ["name"] = Path.GetFileName(doc.Name),
                        ["fingerprint"] = Guid.Parse(db.FingerprintGuid).ToString("D"),
                        ["layout"] = LayoutManager.Current.CurrentLayout,
                        ["space_handle"] = db.CurrentSpaceId.Handle.ToString(),
                        ["tile_mode"] = db.TileMode
                    },
                    ["viewport"] = new JObject
                    {
                        ["number"] = Convert.ToInt32(AcadApplication.GetSystemVariable("CVPORT")),
                        ["screen_width"] = (int)screen.X,
                        ["screen_height"] = (int)screen.Y
                    },
                    ["camera"] = new JObject
                    {
                        ["center_dcs"] = new JArray(view.CenterPoint.X, view.CenterPoint.Y),
                        ["target_wcs"] = new JArray(view.Target.X, view.Target.Y, view.Target.Z),
                        ["direction_wcs"] = new JArray(view.ViewDirection.X, view.ViewDirection.Y, view.ViewDirection.Z),
                        ["width"] = view.Width,
                        ["height"] = view.Height,
                        ["twist_radians"] = view.ViewTwist,
                        ["perspective"] = view.PerspectiveEnabled,
                        ["lens_length"] = view.LensLength,
                        ["front_clip_enabled"] = view.FrontClipEnabled,
                        ["back_clip_enabled"] = view.BackClipEnabled,
                        ["front_clip_distance"] = view.FrontClipDistance,
                        ["back_clip_distance"] = view.BackClipDistance
                    }
                };
            }
        }

        /// <summary>
        /// Builds a default capture path under %LOCALAPPDATA%\Bimwright\Dwg\captures\ and ensures the directory exists.
        /// </summary>
        internal static string BuildDefaultOutputPath(string imageFormat, out string error)
        {
            error = null;
            string fmt = imageFormat?.Trim().ToLowerInvariant();
            string ext = (fmt == "jpeg" || fmt == "jpg") ? "jpg" : (fmt == "bmp" ? "bmp" : "png");

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Bimwright", "Dwg", "captures");
                Directory.CreateDirectory(dir);
                string name = "dwg-capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + "." + ext;
                return Path.Combine(dir, name);
            }
            catch (Exception ex)
            {
                error = "could not create captures directory: " + ex.Message;
                return null;
            }
        }

        private static ImageFormat ResolveFormat(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg": return ImageFormat.Jpeg;
                case ".bmp": return ImageFormat.Bmp;
                default: return ImageFormat.Png;
            }
        }
    }
}
