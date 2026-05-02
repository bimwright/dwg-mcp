using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Rewriting;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Low-level rewrite primitive. Accepts explicit per-cluster instructions
    /// and forwards them to <see cref="RewriteExecutor"/>. Prefer
    /// translate_and_rewrite for the standard translation workflow.
    /// </summary>
    public class CollapseAndRewriteHandler : IAcadCommand
    {
        public string Name => "collapse_and_rewrite";

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (parameters == null
                || parameters["clusters"] == null
                || parameters["clusters"].Type != JTokenType.Array)
            {
                return CommandResult.Fail("params must be { clusters: [...] }");
            }

            var clustersToken = (JArray)parameters["clusters"];
            bool applyUnicodeStyle = parameters["apply_unicode_style"]?.Value<bool>() ?? false;
            double finalScale = ParseFinalScale(parameters);
            var db = doc.Database;
            var results = new List<object>();
            ObjectId unicodeStyleId = applyUnicodeStyle
                ? UnicodeStyleService.EnsureStyle(db).StyleId
                : ObjectId.Null;

            foreach (var clusterJson in clustersToken)
            {
                var request = ParseClusterRequest(clusterJson, applyUnicodeStyle);
                request.FinalScale = FinalTextScalePolicy.Clamp(finalScale);
                var result = RewriteExecutor.Execute(db, request, unicodeStyleId);
                results.Add(ToWire(result));
            }

            return CommandResult.Success(results);
        }

        private static double ParseFinalScale(JToken parameters)
        {
            var token = parameters?["final_scale"];
            if (token == null || token.Type == JTokenType.Null)
            {
                return FinalTextScalePolicy.DefaultScale;
            }

            try
            {
                return token.Value<double>();
            }
            catch
            {
                return FinalTextScalePolicy.DefaultScale;
            }
        }

        private static RewriteRequest ParseClusterRequest(JToken cluster, bool applyUnicodeStyle)
        {
            var anchor = (string)cluster["anchor_handle"];
            var newText = (string)cluster["new_text"];
            var convertToMText = cluster["convert_to_mtext"]?.Value<bool>() ?? false;
            var mtextWidth = cluster["mtext_width"]?.Value<double>() ?? 0;
            var deleteHandles = ((JArray)(cluster["delete_handles"] ?? new JArray()))
                .Select(t => (string)t)
                .ToList();

            RewriteAction action;
            if (convertToMText)
            {
                action = RewriteAction.Collapse;
            }
            else if (deleteHandles.Count > 0)
            {
                action = RewriteAction.RewriteInBlock;
            }
            else
            {
                action = RewriteAction.Update;
            }

            return new RewriteRequest
            {
                Action = action,
                AnchorHandle = anchor,
                DeleteHandles = deleteHandles,
                NewText = newText,
                MtextWidth = mtextWidth,
                MedianHeight = null,
                ApplyUnicodeStyle = applyUnicodeStyle
            };
        }

        private static object ToWire(RewriteResult result)
        {
            return new
            {
                anchor_handle = result.AnchorHandle,
                ok = result.Ok,
                new_handle = result.NewHandle,
                deleted_count = result.Ok ? (int?)result.DeletedCount : null,
                action = result.Ok ? RewriteActionNames.ToWire(result.Action) : null,
                layout_hint = result.LayoutHint,
                final_width = result.FinalWidth,
                final_text_height = result.FinalTextHeight,
                error = result.Error
            };
        }
    }
}
