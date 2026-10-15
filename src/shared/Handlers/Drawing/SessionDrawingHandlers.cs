using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListRecentDrawingsHandler : IAcadSessionCommand
    {
        public string Name => "list_recent_drawings";
        public string Description => "List this AutoCAD product's recent DWG files without opening or locking a drawing.";
        public CommandSchema Schema => CommandSchemas.ListRecentDrawings;
        public CommandResult Execute(Document doc, JToken parameters) => CommandResult.Fail("list_recent_drawings is a standalone session command; it is not supported inside drawing batches or baked tools.");

        public CommandResult ExecuteSession(JToken parameters)
        {
            var validation = SchemaValidator.Validate(Name, parameters, Schema);
            if (!validation.Ok) return CommandResult.Fail(validation.Error);
            var request = parameters as JObject ?? new JObject();
            var maxItems = request.Value<int?>("max_items") ?? 50;
            var name = request.Value<string>("name");
            try
            {
                DrawingSessionContract.ValidateMaxItems(maxItems);
#if NET6_0_OR_GREATER
                if (!OperatingSystem.IsWindows()) return CommandResult.Fail("AutoCAD recent files require Windows registry access.");
#endif
                var root = HostApplicationServices.Current.UserRegistryProductRootKey;
                if (string.IsNullOrWhiteSpace(root)) return CommandResult.Fail("AutoCAD did not report its user registry product root; recent-file state is unknown.");
                using (var key = Registry.CurrentUser.OpenSubKey(root.TrimEnd('\\') + @"\Recent File List", writable: false))
                {
                    var values = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, object>>();
                    if (key != null)
                        foreach (var value in key.GetValueNames())
                            values.Add(new System.Collections.Generic.KeyValuePair<string, object>(value,
                                key.GetValue(value, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
                    var entries = RecentDrawingList.Parse(values, name, maxItems);
                    var files = entries.Select(entry => new
                    {
                        index = entry.Index, path = entry.Path, name = Path.GetFileName(entry.Path),
                        exists = File.Exists(entry.Path), pinned = entry.Pinned
                    }).ToArray();
                    return CommandResult.Success(new
                    {
                        acad_year = PluginTarget.AutoCadYear, source = "registry_recent_file_list", registry_found = key != null,
                        count = files.Length, files, instruction = files.Length == 0 ? DrawingSessionContract.AskForPath : DrawingSessionContract.AskUser
                    });
                }
            }
            catch (Exception ex) { return CommandResult.Fail("Could not read this AutoCAD's recent-file list: " + ErrorSanitizer.Sanitize(ex.Message)); }
        }
    }

    public class OpenDrawingHandler : IAcadSessionCommand
    {
        public string Name => "open_drawing";
        public string Description => "Open a DWG in the AutoCAD session without closing or saving any drawing.";
        public CommandSchema Schema => CommandSchemas.OpenDrawing;
        public CommandResult Execute(Document doc, JToken parameters) => CommandResult.Fail("open_drawing is a standalone session command; it is not supported inside drawing batches or baked tools.");

        public CommandResult ExecuteSession(JToken parameters)
        {
            var validation = SchemaValidator.Validate(Name, parameters, Schema);
            if (!validation.Ok) return CommandResult.Fail(validation.Error);
            string path;
            try
            {
                DrawingSessionContract.ReadTimeoutSeconds(Name, parameters);
                path = DrawingSessionContract.NormalizeDwgPath(parameters.Value<string>("path"));
            }
            catch (Exception ex) { return CommandResult.Fail(ErrorSanitizer.Sanitize(ex.Message)); }
            var activate = parameters.Value<bool?>("activate") ?? true;
            var readOnly = parameters.Value<bool?>("read_only") ?? false;
            var documents = Application.DocumentManager;
            // Open is an application-context operation. Never use a locked drawing,
            // queued command string, background Database.ReadDwgFile, or UI automation.
            if (!documents.IsApplicationContext)
                return CommandResult.Fail("AutoCAD is not in application context. No drawing was opened; retry when idle.");
            if (Convert.ToInt32(Application.GetSystemVariable("SDI")) != 0)
                return CommandResult.Fail("dwg_open_drawing requires multi-document mode (SDI=0). No drawings were closed or opened; ask the user to change the mode.");
            foreach (Document document in documents)
                if (!string.IsNullOrEmpty(document.CommandInProgress))
                    return CommandResult.Fail("AutoCAD has a command in progress. No drawing was opened; finish or cancel it in AutoCAD before retrying.");
            var previous = documents.MdiActiveDocument;
            if (!activate && previous == null)
                return CommandResult.Fail("activate=false requires an existing active drawing to keep active. Use activate=true when no drawing is open.");

            Document opened = null;
            foreach (Document document in documents)
            {
                if (!DrawingSessionContract.IsAbsolutePath(document.Name)) continue;
                if (string.Equals(Path.GetFullPath(document.Name), path, StringComparison.OrdinalIgnoreCase)) { opened = document; break; }
            }
            var alreadyOpen = opened != null;
            if (alreadyOpen && opened.IsReadOnly != readOnly)
                return CommandResult.Fail("The drawing is already open with read_only=" + opened.IsReadOnly.ToString().ToLowerInvariant() +
                    ". Its access mode was left unchanged; use that read_only value. Do not close a drawing with unsaved changes automatically.");
            if (!alreadyOpen && !File.Exists(path))
                return CommandResult.Fail("No file at '" + path + "'. The path must be reachable from the AutoCAD process, not just the MCP client.");
            try
            {
                if (!alreadyOpen) opened = documents.Open(path, readOnly);
                if (opened == null) return CommandResult.Fail("AutoCAD did not return an opened document. Check AutoCAD state before retrying.");
                documents.MdiActiveDocument = activate ? opened : previous;
                return CommandResult.Success(new
                {
                    opened = true, was_already_open = alreadyOpen, title = Path.GetFileName(opened.Name), path = opened.Name,
                    read_only = opened.IsReadOnly, activated = documents.MdiActiveDocument == opened,
                    active_document = documents.MdiActiveDocument?.Name
                });
            }
            catch (Exception ex)
            {
                // An Open/activation error may have left a document open or a native
                // dialog pending. Never close it, save it, or silently retry the Open.
                return CommandResult.Fail("AutoCAD could not finish opening/activating the drawing: " + ErrorSanitizer.Sanitize(ex.Message) +
                    ". A drawing may already be open; inspect AutoCAD state/native dialogs before retrying. This tool did not save or close any drawing.");
            }
        }
    }
}
