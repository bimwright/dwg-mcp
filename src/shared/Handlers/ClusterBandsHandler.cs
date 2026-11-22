using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public sealed class ClusterBandsHandler : IAcadCommand
    {
        public string Name => "cluster_bands";
        public string Description => "Read axis-ordered bands of entity extents in a single drawing space.";
        public CommandSchema Schema => CommandSchemas.ClusterBands;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try { return CommandResult.Success(CadBandQuery.Read(doc, BandQueryOptions.Create(parameters))); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            { return CommandResult.Fail(ex.Message); }
        }
    }
}
