using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    /// <summary>
    /// Canonical rewrite actions. Wire names live in <see cref="RewriteActionNames"/>.
    /// </summary>
    public enum RewriteAction
    {
        Update,
        Collapse,
        RewriteInBlock,
        StyleOnly
    }

    /// <summary>
    /// Wire-facing string constants for <see cref="RewriteAction"/>. These
    /// appear in the JSON returned by translate_and_rewrite and
    /// collapse_and_rewrite. Do NOT change without updating response
    /// contract docs.
    /// </summary>
    public static class RewriteActionNames
    {
        public const string Update = "update";
        public const string Collapse = "collapse";
        public const string RewriteInBlock = "rewrite_in_block";
        public const string StyleOnly = "style_only";

        public static string ToWire(RewriteAction action)
        {
            switch (action)
            {
                case RewriteAction.Update: return Update;
                case RewriteAction.Collapse: return Collapse;
                case RewriteAction.RewriteInBlock: return RewriteInBlock;
                case RewriteAction.StyleOnly: return StyleOnly;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }

    /// <summary>
    /// Optional caller override for the final rendering strategy.
    /// </summary>
    public enum RewriteRenderMode
    {
        Auto,
        MText
    }

    public static class RewriteRenderModeNames
    {
        public const string Auto = "auto";
        public const string MText = "mtext";

        public static bool TryParse(string wireValue, out RewriteRenderMode mode)
        {
            if (string.IsNullOrWhiteSpace(wireValue) ||
                string.Equals(wireValue, Auto, StringComparison.OrdinalIgnoreCase))
            {
                mode = RewriteRenderMode.Auto;
                return true;
            }

            if (string.Equals(wireValue, MText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wireValue, "prefer_mtext", StringComparison.OrdinalIgnoreCase))
            {
                mode = RewriteRenderMode.MText;
                return true;
            }

            mode = RewriteRenderMode.Auto;
            return false;
        }

        public static string ToWire(RewriteRenderMode mode)
        {
            switch (mode)
            {
                case RewriteRenderMode.Auto: return Auto;
                case RewriteRenderMode.MText: return MText;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }
    }

    public enum RewriteWidthPolicy
    {
        Expand,
        Preserve,
        Compact
    }

    public static class RewriteWidthPolicyNames
    {
        public const string Expand = "expand";
        public const string Preserve = "preserve";
        public const string Compact = "compact";

        public static bool TryParse(string wireValue, out RewriteWidthPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(wireValue) ||
                string.Equals(wireValue, Expand, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wireValue, "auto", StringComparison.OrdinalIgnoreCase))
            {
                policy = RewriteWidthPolicy.Expand;
                return true;
            }

            if (string.Equals(wireValue, Preserve, StringComparison.OrdinalIgnoreCase))
            {
                policy = RewriteWidthPolicy.Preserve;
                return true;
            }

            if (string.Equals(wireValue, Compact, StringComparison.OrdinalIgnoreCase))
            {
                policy = RewriteWidthPolicy.Compact;
                return true;
            }

            policy = RewriteWidthPolicy.Expand;
            return false;
        }

        public static string ToWire(RewriteWidthPolicy policy)
        {
            switch (policy)
            {
                case RewriteWidthPolicy.Expand: return Expand;
                case RewriteWidthPolicy.Preserve: return Preserve;
                case RewriteWidthPolicy.Compact: return Compact;
                default: throw new ArgumentOutOfRangeException(nameof(policy));
            }
        }
    }

    /// <summary>
    /// Declarative description of a single rewrite operation. The builder
    /// (for translation path) or handler JSON parser (for low-level path)
    /// fills this in; the executor consumes it.
    ///
    /// - <see cref="MedianHeight"/> is optional: when null, the executor
    ///   computes the median live from <see cref="AnchorHandle"/> and
    ///   <see cref="DeleteHandles"/> inside the transaction.
    /// - <see cref="MtextWidth"/> is only meaningful for
    ///   <see cref="RewriteAction.Collapse"/>.
    /// </summary>
    public sealed class RewriteRequest
    {
        public RewriteAction Action { get; set; }
        public string AnchorHandle { get; set; }
        public List<string> DeleteHandles { get; set; } = new List<string>();
        public string NewText { get; set; }
        public double MtextWidth { get; set; }
        public double? MedianHeight { get; set; }
        public bool ApplyUnicodeStyle { get; set; }
        public double BlockScale { get; set; } = 1.0;

        /// <summary>
        /// When set, the executor uses this as the final MText TextHeight and
        /// skips the Unicode-style scale factor. When null, the executor falls
        /// back to the legacy <see cref="UnicodeStyleService.ComputeTargetHeight"/>
        /// path (used by low-level callers that only provide width).
        /// </summary>
        public double? ExplicitTextHeight { get; set; }

        /// <summary>
        /// Free-form tag describing which layout branch produced the width/height
        /// (e.g. "bbox_fit", "vertical_stack_reflowed+height_overflow",
        /// "scale_only"). Propagated to the JSON response for observability.
        /// </summary>
        public string LayoutHint { get; set; }

        /// <summary>
        /// Per-request multiplier consumed by <see cref="FinalTextScalePolicy"/>
        /// after sizing. Clamped into
        /// [<see cref="FinalTextScalePolicy.MinScale"/>,
        ///  <see cref="FinalTextScalePolicy.MaxScale"/>] at apply time.
        /// Defaults to <see cref="FinalTextScalePolicy.DefaultScale"/>.
        /// </summary>
        public double FinalScale { get; set; } = FinalTextScalePolicy.DefaultScale;
    }

    public sealed class RewriteResult
    {
        public bool Ok { get; set; }
        public string AnchorHandle { get; set; }
        public string NewHandle { get; set; }
        public RewriteAction Action { get; set; }
        public int DeletedCount { get; set; }
        public string Error { get; set; }
        public string LayoutHint { get; set; }
        public double FinalWidth { get; set; }
        public double FinalTextHeight { get; set; }
    }
}
