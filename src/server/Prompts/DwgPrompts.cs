using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server.Prompts
{
    /// <summary>
    /// MCP prompts: user-invoked workflows. Always registered, since a prompt costs no model context until the
    /// user picks it; a prompt whose toolsets are off renders an enable notice instead of its steps.
    /// </summary>
    [McpServerPromptType]
    public class DwgPrompts
    {
        private static readonly string[] TranslateSets = { "query", "modify", "meta" };
        // --read-only drops write-capable toolsets; the prompt then stops at the translation table.
        private static readonly string[] TranslateReadOnlySets = { "query", "meta" };
        private static readonly string[] ReviewSets = { "query", "meta" };

        [McpServerPrompt(Name = "dwg_translate_text"),
         Description("Translate the text you select in AutoCAD: read clusters, show every translation for confirmation, write back with the Unicode style, verify and report. Read-only sessions stop at the translation table.")]
        public static string TranslateText(
            [Description("Language to translate into, for example Vietnamese or English")] string target_language = "Vietnamese",
            [Description("Language of the drawing text; leave empty to detect it")] string source_language = null)
        {
            return PromptBody.Render(PromptBody.Load("translate_text"), ServerState.IsReadOnly ? TranslateReadOnlySets : TranslateSets, new Dictionary<string, string>
            {
                ["target_language"] = string.IsNullOrWhiteSpace(target_language) ? "Vietnamese" : target_language.Trim(),
                ["source_language"] = string.IsNullOrWhiteSpace(source_language) ? "detect it from the selected text" : source_language.Trim(),
                ["mode"] = ServerState.IsReadOnly
                    ? "READ-ONLY: read and translate only; show the table and do not write or change the session configuration."
                    : "Writes need my confirmation of the translation table first."
            });
        }

        [McpServerPrompt(Name = "dwg_review_large_drawing"),
         Description("Read-only review of a large drawing in bounded pieces: profile, count regions, split dense regions into strips, page through entities, then answer with coverage and entity links.")]
        public static string ReviewLargeDrawing(
            [Description("What to find or check in the drawing")] string question = null,
            [Description("model (default) or layout:<exact layout name>")] string space = "model")
        {
            return PromptBody.Render(PromptBody.Load("review_large_drawing"), ReviewSets, new Dictionary<string, string>
            {
                ["question"] = string.IsNullOrWhiteSpace(question) ? "Not specified. Ask me what to find before reading." : question.Trim(),
                ["space"] = string.IsNullOrWhiteSpace(space) ? "model" : space.Trim()
            });
        }
    }
}
