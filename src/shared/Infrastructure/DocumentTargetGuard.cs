using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    internal static class DocumentTargetGuard
    {
        private sealed class Lifetime { internal readonly string Id = Guid.NewGuid().ToString("N"); }
        private static readonly ConditionalWeakTable<Document, Lifetime> Documents = new ConditionalWeakTable<Document, Lifetime>();
        private static readonly int Pid = Process.GetCurrentProcess().Id;
        private static readonly DateTime StartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        internal static string Id(Document document) => document == null ? null : Documents.GetValue(document, _ => new Lifetime()).Id;

        // Called on AutoCAD's main thread. A reopened file gets a new lifetime ID.
        internal static JObject Capture(Document document) => new JObject
        {
            ["protocol"] = 1,
            ["target_id"] = "acad-" + PluginTarget.AutoCadYear + "-" + Pid,
            ["pid"] = Pid, ["process_start_utc"] = StartUtc,
            ["document_id"] = Id(document), ["document_title"] = document?.Name
        };

        internal static void CheckInstance(JObject expected, string command)
            => TargetContract.CheckInstance(expected, Pid, StartUtc, TargetContract.IsWrite(command));

        internal static void CheckDocument(JObject expected, Document document, string command)
            => TargetContract.CheckDocument(expected, Id(document), TargetContract.IsWrite(command));
    }
}
