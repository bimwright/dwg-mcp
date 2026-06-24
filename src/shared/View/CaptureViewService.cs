using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using GsManager = Autodesk.AutoCAD.GraphicsSystem.Manager;
using GsView = Autodesk.AutoCAD.GraphicsSystem.View;
using GsDevice = Autodesk.AutoCAD.GraphicsSystem.Device;
using GsModel = Autodesk.AutoCAD.GraphicsSystem.Model;

namespace Bimwright.Dwg.Plugin.View
{
    /// <summary>
    /// Captures the current AutoCAD view to a raster image using an off-screen GraphicsSystem device.
    /// Mirrors the rvt-mcp capture workflow: render to a file the agent can read back.
    /// Uses the AutoCAD 2015+ GraphicsKernel API.
    /// </summary>
    internal static class CaptureViewService
    {
        internal static object Capture(Document doc, string outputPath, int pixelSize)
        {
            var db = doc.Database;
            GsManager gsm = doc.GraphicsManager;

            int vpn = Convert.ToInt32(
                Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("CVPORT"));

            // SCREENSIZE = current viewport size in pixels (X=width, Y=height); drives output aspect ratio.
            var screen = (Autodesk.AutoCAD.Geometry.Point2d)
                Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("SCREENSIZE");
            var (width, height) = CaptureViewMath.ComputeOutputSize((int)screen.X, (int)screen.Y, pixelSize);

            var descriptor = new Autodesk.AutoCAD.GraphicsSystem.KernelDescriptor();
            descriptor.addRequirement(Autodesk.AutoCAD.UniqueString.Intern("3D Drawing"));
            var kernel = GsManager.AcquireGraphicsKernel(descriptor);

            using (GsView view = new GsView())
            {
                // Copy the current viewport's camera so the snapshot matches what the user sees.
                gsm.SetViewFromViewport(view, vpn);

                GsDevice dev = gsm.CreateAutoCADOffScreenDevice(kernel);
                using (dev)
                {
                    dev.OnSize(new Size(width, height));
                    dev.DeviceRenderType = Autodesk.AutoCAD.GraphicsSystem.RendererType.Default;
                    dev.BackgroundColor = Color.White;
                    dev.Add(view);

                    using (GsModel model = gsm.CreateAutoCADModel(kernel))
                    {
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                            view.Add(space, model);
                            tr.Commit();
                        }

                        dev.Update();

                        var rect = new Rectangle(0, 0, width, height);
                        using (Bitmap bitmap = view.GetSnapshot(rect))
                        {
                            bitmap.Save(outputPath, ResolveFormat(outputPath));
                        }

                        view.EraseAll();
                        dev.Erase(view);
                    }
                }
            }

            return new
            {
                output_path = outputPath,
                width,
                height,
                image_format = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant()
            };
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
                string name = "dwg-capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + ext;
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
