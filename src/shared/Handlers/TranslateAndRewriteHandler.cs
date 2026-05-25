using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Rewriting;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Preferred translation tool. Reads cluster state previously stored by
    /// get_selected_texts, converts each cluster into a RewriteRequest via
    /// RewriteRequestBuilder, and forwards the batch to RewriteExecutor.
    /// </summary>
    public class TranslateAndRewriteHandler : IAcadCommand
    {
        public string Name => "translate_and_rewrite";
        public string Description => "Rewrite clustered text using translations from get_selected_texts.";
        public CommandSchema Schema => CommandSchemas.TranslateAndRewrite;

        private sealed class TranslationInstruction
        {
            public string NewText { get; set; }
            public RewriteRenderMode RenderMode { get; set; }
            public RewriteWidthPolicy WidthPolicy { get; set; }
        }

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!ClusterStateStore.HasState())
            {
                return CommandResult.Fail("no cluster state — call get_selected_texts first");
            }

            var translationMap = ParseTranslations(parameters);
            double finalScale = ParseFinalScale(parameters);

            var db = doc.Database;
            var unicodeStyleId = UnicodeStyleService.EnsureStyle(db).StyleId;

            var allClusters = ClusterStateStore.GetAll();
            var results = new List<object>();

            foreach (var cluster in allClusters)
            {
                bool hasTranslation = translationMap.TryGetValue(cluster.Id, out var instruction);
                string newText = instruction?.NewText;
                var renderMode = instruction?.RenderMode ?? RewriteRenderMode.Auto;
                var widthPolicy = instruction?.WidthPolicy ?? RewriteWidthPolicy.Expand;

                var request = RewriteRequestBuilder.FromCluster(
                    cluster,
                    newText,
                    hasTranslation,
                    applyUnicodeStyle: true,
                    renderMode: renderMode,
                    widthPolicy: widthPolicy,
                    finalScale: finalScale);

                var result = RewriteExecutor.Execute(db, request, unicodeStyleId);
                results.Add(ToWire(cluster.Id, result));
            }

            return CommandResult.Success(new { results });
        }

        private static Dictionary<int, TranslationInstruction> ParseTranslations(JToken parameters)
        {
            var map = new Dictionary<int, TranslationInstruction>();
            var translationsToken = parameters?["translations"];
            if (translationsToken == null || translationsToken.Type != JTokenType.Array)
            {
                return map;
            }

            foreach (var t in (JArray)translationsToken)
            {
                int tid = t["id"]?.Value<int>() ?? 0;
                string newText = t["new_text"]?.Value<string>() ?? string.Empty;
                RewriteRenderModeNames.TryParse(t["render_mode"]?.Value<string>(), out var renderMode);
                RewriteWidthPolicyNames.TryParse(t["width_policy"]?.Value<string>(), out var widthPolicy);
                if (tid != 0)
                {
                    map[tid] = new TranslationInstruction
                    {
                        NewText = newText,
                        RenderMode = renderMode,
                        WidthPolicy = widthPolicy
                    };
                }
            }

            return map;
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

        private static object ToWire(int clusterId, RewriteResult result)
        {
            return new
            {
                id = clusterId,
                ok = result.Ok,
                new_handle = result.NewHandle,
                action = result.Ok ? RewriteActionNames.ToWire(result.Action) : null,
                layout_hint = result.LayoutHint,
                final_width = result.FinalWidth,
                final_text_height = result.FinalTextHeight,
                error = result.Error
            };
        }
    }
}
