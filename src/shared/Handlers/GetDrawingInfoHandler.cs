using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class GetDrawingInfoHandler : IAcadCommand
    {
        public string Name => "get_drawing_info";
        public string Description => "Return small metadata for the current AutoCAD drawing.";
        public CommandSchema Schema => CommandSchemas.GetDrawingInfo;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var db = doc.Database;
            var documentName = ReadScalar(() => doc.Name);
            var databaseFilename = ReadScalar(() => db.Filename);
            var originalFilename = ReadScalar(() => db.OriginalFileName);
            var safeDocumentName = SafeFileName(documentName);
            var drawingName = FirstNonBlank(
                SafeFileName(databaseFilename),
                SafeFileName(originalFilename),
                safeDocumentName);

            return CommandResult.Success(new
            {
                drawing_name = drawingName,
                document_name = safeDocumentName,
                has_saved_path = HasSavedPath(databaseFilename, originalFilename, documentName),
                current_layer = GetCurrentLayerName(db),
                current_space = db.TileMode ? "model" : "paper",
                current_space_record = GetCurrentSpaceRecordName(db),
                current_layout = GetCurrentLayoutName(),
                tile_mode = db.TileMode,
                insunits = db.Insunits.ToString(),
                insunits_code = (int)db.Insunits,
                lunits = db.Lunits,
                aunits = db.Aunits,
                measurement = db.Measurement.ToString()
            });
        }

        private static string GetCurrentLayerName(Database db)
        {
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var layer = (LayerTableRecord)tx.GetObject(db.Clayer, OpenMode.ForRead);
                    var name = layer.Name;
                    tx.Commit();
                    return name;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetCurrentSpaceRecordName(Database db)
        {
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var currentSpace = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    var name = currentSpace.Name;
                    tx.Commit();
                    return name;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string GetCurrentLayoutName()
        {
            try
            {
                return LayoutManager.Current?.CurrentLayout;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadScalar(Func<string> read)
        {
            try
            {
                var value = read();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasSavedPath(string databaseFilename, string originalFilename, string documentName)
        {
            return !string.IsNullOrWhiteSpace(databaseFilename) ||
                !string.IsNullOrWhiteSpace(originalFilename) ||
                IsRootedPath(documentName);
        }

        private static bool IsRootedPath(string value)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                var fileName = Path.GetFileName(value.Trim());
                return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
