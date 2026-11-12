using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Reports.Binding;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Acad
{
    /// <summary>Resolves report links against open drawings. Main thread only; reads never lock or add undo records.</summary>
    internal sealed class AcadReportResolver : IReportResolver
    {
        public ReportDocument ActiveDocument => Describe(Application.DocumentManager.MdiActiveDocument);

        public ReportDocument FindDocument(string token) => Describe(Find(token));

        public ReportEntity FindEntity(string token, string handle)
        {
            var document = Find(token);
            if (document == null || !long.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                return null;
            var db = document.Database;
            if (!db.TryGetObjectId(new Handle(value), out var id) || id.IsNull || id.IsErased || !id.IsValid)
                return null;
            using (var tx = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tx.GetObject(id, OpenMode.ForRead, false) is Entity entity)) return null;
                string space = null;
                try { space = (tx.GetObject(entity.OwnerId, OpenMode.ForRead, false) as BlockTableRecord)?.Name; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                var result = new ReportEntity { Handle = entity.Handle.ToString(), Type = entity.GetType().Name, Layer = entity.Layer, Space = space };
                tx.Commit();
                return result;
            }
        }

        internal static Document Find(string token)
        {
            if (token == null) return null;
            foreach (Document document in Application.DocumentManager)
                if (string.Equals(DocumentTargetGuard.Id(document), token, StringComparison.Ordinal)) return document;
            return null;
        }

        internal static ReportDocument Describe(Document document)
        {
            if (document == null) return null;
            string path = null;
            try { if (document.IsNamedDrawing) path = document.Database.Filename; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return new ReportDocument { Token = DocumentTargetGuard.Id(document), Title = System.IO.Path.GetFileName(document.Name), Path = path };
        }
    }
}
