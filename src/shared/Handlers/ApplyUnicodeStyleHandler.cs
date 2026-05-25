using System;
using System.Collections.Generic;
using System.Globalization;
using Bimwright.Dwg.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Ensures the "Bimwright_Unicode" text style exists (using Open Sans,
    /// downloading the font if necessary), then reassigns target entities to
    /// that style. Targets are either explicit handles (params.handles) or
    /// the current pickfirst selection as fallback. Intended to be called
    /// AFTER update_texts / collapse_and_rewrite when translating to a
    /// non-ASCII language on drawings whose default SHX fonts lack the
    /// required glyphs.
    ///
    /// Height rule: scaling is style-aware and idempotent. SHX sources are
    /// downscaled more than TrueType sources, while entities already on the
    /// Unicode style keep their current height instead of shrinking again.
    /// </summary>
    public class ApplyUnicodeStyleHandler : IAcadCommand
    {
        public string Name => "apply_unicode_style";
        public string Description => "Apply the Bimwright Unicode text style to selected or explicit text entities.";
        public CommandSchema Schema => CommandSchemas.ApplyUnicodeStyle;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            try
            {
                var style = UnicodeStyleService.EnsureStyle(doc.Database);
                var layerName = McpLayerService.EnsureWhiteLayer(doc.Database);
                var targetIds = ResolveTargets(doc, parameters);
                int reassigned = UnicodeStyleService.ApplyToTargets(doc.Database, targetIds, style.StyleId, layerName);

                return CommandResult.Success(new
                {
                    style_created = style.StyleCreated,
                    font_downloaded = style.FontDownloaded,
                    font_path = style.FontPath,
                    font_face = UnicodeStyleService.FontTypeFace,
                    layer = layerName,
                    height_scale_mode = "smart",
                    shx_height_scale = UnicodeScaleHeuristics.ShxHeightScale,
                    true_type_height_scale = UnicodeScaleHeuristics.TrueTypeHeightScale,
                    unknown_height_scale = UnicodeScaleHeuristics.UnknownHeightScale,
                    reassigned_count = reassigned
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the set of ObjectIds to operate on. If params.handles is a
        /// non-empty JSON array, those handles are resolved (invalid ones are
        /// silently skipped). Otherwise falls back to the current pickfirst
        /// selection. The explicit-handles path is important after operations
        /// like collapse_and_rewrite where pickfirst is stale.
        /// </summary>
        private static List<ObjectId> ResolveTargets(Document doc, JToken parameters)
        {
            var ids = new List<ObjectId>();
            var db = doc.Database;

            if (parameters != null && parameters["handles"] is JArray handles && handles.Count > 0)
            {
                foreach (var h in handles)
                {
                    var s = (string)h;
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hv)) continue;
                    try
                    {
                        var oid = db.GetObjectId(false, new Handle(hv), 0);
                        if (!oid.IsNull) ids.Add(oid);
                    }
                    catch { }
                }
                return ids;
            }

            var sel = doc.Editor.SelectImplied();
            if (sel.Status == PromptStatus.OK && sel.Value != null)
            {
                foreach (SelectedObject so in sel.Value)
                {
                    ids.Add(so.ObjectId);
                }
            }
            return ids;
        }
    }
}
