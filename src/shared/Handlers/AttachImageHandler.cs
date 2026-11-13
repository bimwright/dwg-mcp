using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class AttachImageHandler : IAcadCommand
    {
        public string Name => "attach_image";
        public string Description => "Attach a raster image at an exact insertion point and size (IMAGEATTACH equivalent).";
        public CommandSchema Schema => CommandSchemas.AttachImage;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
                return CommandResult.Fail("params must be an object");

            var rawPath = obj["path"]?.Value<string>();
            if (!ImageAttachInput.TryValidatePath(rawPath, out var pathError))
                return CommandResult.Fail(pathError);
            var path = Path.GetFullPath(rawPath);
            if (new DriveInfo(Path.GetPathRoot(path)).DriveType == DriveType.Network)
                return CommandResult.Fail("mapped network drives are not allowed; copy the image to a local drive first");
            if (!File.Exists(path))
                return CommandResult.Fail("image file not found on the AutoCAD machine: " + path);

            if (!CadWire.TryParsePoint(obj["insertion"], out var insertion, out var insertionError))
                return CommandResult.Fail("insertion " + insertionError);

            var width = obj["width"]?.Value<double>() ?? 0d;
            var heightToken = obj["height"];
            double? height = heightToken == null || heightToken.Type == JTokenType.Null ? (double?)null : heightToken.Value<double>();
            var rotationToken = obj["rotation_degrees"];
            var rotation = rotationToken == null || rotationToken.Type == JTokenType.Null ? 0d : rotationToken.Value<double>();
            if (!ImageAttachInput.TryValidateSize(width, height, rotation, out var sizeError))
                return CommandResult.Fail(sizeError);

            var layer = obj["layer"]?.Value<string>();
            var hasLayer = obj["layer"] != null && obj["layer"].Type != JTokenType.Null;
            var sendToBack = obj["send_to_back"]?.Type == JTokenType.Boolean && obj.Value<bool>("send_to_back");
            var db = doc.Database;

            try
            {
                // One transaction: a failure leaves no layer, image definition or image behind.
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (hasLayer && !CadLayerService.TryEnsureLayer(db, tx, layer, 7, out _, out _, out var layerError))
                        return CommandResult.Fail(layerError);

                    var dictionaryId = RasterImageDef.GetImageDictionary(db);
                    if (dictionaryId.IsNull)
                        dictionaryId = RasterImageDef.CreateImageDictionary(db);
                    var dictionary = (DBDictionary)tx.GetObject(dictionaryId, OpenMode.ForWrite);

                    RasterImageDef definition = null;
                    var reused = false;
                    foreach (DBDictionaryEntry entry in dictionary)
                    {
                        if (tx.GetObject(entry.Value, OpenMode.ForRead) is RasterImageDef existing &&
                            (string.Equals(existing.SourceFileName, path, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(existing.ActiveFileName, path, StringComparison.OrdinalIgnoreCase)))
                        {
                            definition = existing;
                            reused = true;
                            break;
                        }
                    }

                    if (definition == null)
                    {
                        definition = new RasterImageDef { SourceFileName = path };
                        definition.Load();
                        dictionary.SetAt(UniqueKey(dictionary, Path.GetFileNameWithoutExtension(path)), definition);
                        tx.AddNewlyCreatedDBObject(definition, true);
                    }
                    else
                    {
                        // AssociateRasterDef adds a reactor to the definition, so a reused
                        // definition must be open for write (ForRead crashed AutoCAD 2025).
                        definition.UpgradeOpen();
                        if (!definition.IsLoaded)
                            definition.Load();
                    }

                    var pixels = definition.Size;
                    if (!(pixels.X > 0d) || !(pixels.Y > 0d))
                        return CommandResult.Fail("could not read the image pixel size from " + path);

                    var resolvedHeight = ImageAttachInput.ResolveHeight(width, height, pixels.X, pixels.Y);
                    ImageAttachInput.Orientation(width, resolvedHeight, rotation, out var ux, out var uy, out var vx, out var vy);

                    var image = new RasterImage();
                    image.SetDatabaseDefaults(db);
                    image.ImageDefId = definition.ObjectId;
                    image.Orientation = new CoordinateSystem3d(
                        new Point3d(insertion.X, insertion.Y, insertion.Z), new Vector3d(ux, uy, 0d), new Vector3d(vx, vy, 0d));
                    image.ShowImage = true;
                    if (hasLayer)
                        image.Layer = layer;

                    var imageId = CadPrimitiveWriter.AppendToCurrentSpace(db, tx, image);
                    RasterImage.EnableReactors(true);
                    image.AssociateRasterDef(definition);

                    if (sendToBack)
                    {
                        var space = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                        var drawOrder = (DrawOrderTable)tx.GetObject(space.DrawOrderTableId, OpenMode.ForWrite);
                        drawOrder.MoveToBottom(new ObjectIdCollection { imageId });
                    }

                    var result = new
                    {
                        handle = image.Handle.ToString(),
                        image_def_handle = definition.Handle.ToString(),
                        image_def_reused = reused,
                        path = definition.SourceFileName,
                        pixel_width = pixels.X,
                        pixel_height = pixels.Y,
                        insertion = new { x = insertion.X, y = insertion.Y, z = insertion.Z },
                        width,
                        height = resolvedHeight,
                        rotation_degrees = rotation,
                        sent_to_back = sendToBack,
                        entity = CadEntityProperties.Describe(image, tx, includeGeometry: true)
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to attach image: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }

        private static string UniqueKey(DBDictionary dictionary, string baseName)
        {
            var name = string.IsNullOrWhiteSpace(baseName) ? "image" : baseName;
            if (!dictionary.Contains(name)) return name;
            for (var i = 2; ; i++)
            {
                var candidate = name + "_" + i;
                if (!dictionary.Contains(candidate)) return candidate;
            }
        }
    }
}
