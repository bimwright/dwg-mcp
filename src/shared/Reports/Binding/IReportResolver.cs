using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Binding
{
    public interface IReportResolver
    {
        ReportDocument ActiveDocument { get; }
        /// <summary>An open drawing by document_id, or null.</summary>
        ReportDocument FindDocument(string token);
        /// <summary>A live (not erased) entity by normalized handle, or null.</summary>
        ReportEntity FindEntity(string token, string handle);
    }
}
