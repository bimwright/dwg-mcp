using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    internal static class HealthHandler
    {
        internal static CommandResult Read(JObject identity)
            => CommandResult.Success(HealthSnapshot.Capture(identity, () =>
                DocumentInvoker.InvokeHostQuery(() =>
                {
                    // Health is valid on AutoCAD's start page; never require,
                    // create, lock, save or activate a drawing for this probe.
                    var doc = Application.DocumentManager.MdiActiveDocument;
                    return new JObject
                    {
                        ["has_active_document"] = doc != null,
                        ["document_title"] = doc == null ? null : Path.GetFileName(doc.Name),
                        ["is_quiescent"] = doc == null ? (JToken)JValue.CreateNull() : new JValue(doc.Editor.IsQuiescent)
                    };
                })));
    }
}
