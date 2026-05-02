using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    /// <summary>
    /// Pure (no AutoCAD API) mapping from <see cref="ClusterState"/> to a
    /// <see cref="RewriteRequest"/> the executor can run.
    /// </summary>
    public static class RewriteRequestBuilder
    {
        private const int SingleTextCollapseWordThreshold = 7;
        private const double SingleTextCollapseWidthFactor = 14.0;
        private const double MultiTextCollapseWidthFactor = 10.0;
        private const double SingleTextMaxExpansionFactor = 2.5;
        private const double MultiTextMaxExpansionFactor = 2.0;
        private const double SingleTextExpansionPaddingFactor = 1.10;
        private const double MultiTextExpansionPaddingFactor = 1.05;

        public static RewriteAction DetermineAction(ClusterState cluster, bool hasTranslation)
            => DetermineAction(cluster, hasTranslation, null, RewriteRenderMode.Auto);

        public static RewriteAction DetermineAction(
            ClusterState cluster,
            bool hasTranslation,
            string newText)
            => DetermineAction(cluster, hasTranslation, newText, RewriteRenderMode.Auto);

        public static RewriteAction DetermineAction(
            ClusterState cluster,
            bool hasTranslation,
            string newText,
            RewriteRenderMode renderMode)
        {
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));

            if (!hasTranslation)
            {
                return RewriteAction.StyleOnly;
            }

            bool isMulti = cluster.AllHandles != null && cluster.AllHandles.Count > 1;
            if (renderMode == RewriteRenderMode.MText &&
                !cluster.InBlock &&
                (isMulti || cluster.CanPromoteSingleToMText))
            {
                return RewriteAction.Collapse;
            }

            if (!isMulti)
            {
                if (cluster.CanPromoteSingleToMText && CountWords(newText) > SingleTextCollapseWordThreshold)
                {
                    return RewriteAction.Collapse;
                }

                return RewriteAction.Update;
            }

            return cluster.InBlock ? RewriteAction.RewriteInBlock : RewriteAction.Collapse;
        }

        public static RewriteRequest FromCluster(
            ClusterState cluster,
            string newText,
            bool hasTranslation,
            bool applyUnicodeStyle,
            RewriteRenderMode renderMode = RewriteRenderMode.Auto,
            RewriteWidthPolicy widthPolicy = RewriteWidthPolicy.Expand,
            double finalScale = FinalTextScalePolicy.DefaultScale)
        {
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));

            var action = DetermineAction(cluster, hasTranslation, newText, renderMode);
            var deleteHandles = cluster.DeleteHandles != null
                ? new List<string>(cluster.DeleteHandles)
                : new List<string>();
            var medianHeight = cluster.MedianHeight > 0 ? (double?)cluster.MedianHeight : null;

            double mtextWidth = 0.0;
            double? explicitTextHeight = null;
            string layoutHint = null;

            bool isSizingAction =
                action == RewriteAction.Collapse ||
                (action == RewriteAction.RewriteInBlock
                    && cluster.DeleteHandles != null
                    && cluster.DeleteHandles.Count > 0);

            if (isSizingAction)
            {
                if (widthPolicy == RewriteWidthPolicy.Expand)
                {
                    var sizer = BboxFitSizer.Compute(
                        action,
                        cluster.MtextWidth,
                        cluster.MtextHeight,
                        cluster.MedianHeight,
                        TextMetrics.CountVisualUnits(newText));

                    if (sizer.Width.HasValue && sizer.TextHeight.HasValue)
                    {
                        mtextWidth = sizer.Width.Value;
                        explicitTextHeight = sizer.TextHeight.Value;
                        layoutHint = sizer.LayoutHint;
                    }
                    else
                    {
                        // Sizer declined (missing bbox/char count) → legacy estimator.
                        mtextWidth = EstimateCollapseWidth(cluster, newText, widthPolicy);
                    }
                }
                else
                {
                    mtextWidth = EstimateCollapseWidth(cluster, newText, widthPolicy);
                }
            }

            return new RewriteRequest
            {
                Action = action,
                AnchorHandle = cluster.AnchorHandle,
                DeleteHandles = deleteHandles,
                NewText = hasTranslation ? newText : null,
                MtextWidth = mtextWidth,
                MedianHeight = medianHeight,
                ApplyUnicodeStyle = applyUnicodeStyle,
                BlockScale = cluster.BlockScale,
                ExplicitTextHeight = explicitTextHeight,
                LayoutHint = layoutHint,
                FinalScale = FinalTextScalePolicy.Clamp(finalScale)
            };
        }

        private static double EstimateCollapseWidth(
            ClusterState cluster,
            string newText,
            RewriteWidthPolicy widthPolicy)
        {
            double baseWidth = cluster.MtextWidth > 0 ? cluster.MtextWidth : 0;
            double floorWidth = 0;
            if (cluster.MedianHeight > 0)
            {
                floorWidth = cluster.MedianHeight * (
                    cluster.CanPromoteSingleToMText
                        ? SingleTextCollapseWidthFactor
                        : MultiTextCollapseWidthFactor);
            }

            double expansionFactor = EstimateExpansionFactor(
                cluster.CombinedText,
                newText,
                cluster.CanPromoteSingleToMText);

            double expandedWidth = baseWidth > 0
                ? baseWidth * expansionFactor
                : 0;

            double requiredWidth = EstimateLongestLineWidth(newText, cluster.MedianHeight);

            if (widthPolicy == RewriteWidthPolicy.Preserve && baseWidth > 0)
            {
                return baseWidth;
            }

            if (widthPolicy == RewriteWidthPolicy.Compact)
            {
                double compactBase = baseWidth > 0 ? baseWidth : floorWidth;
                return Math.Max(compactBase, floorWidth);
            }

            return Math.Max(
                Math.Max(Math.Max(baseWidth, expandedWidth), floorWidth),
                requiredWidth);
        }

        /// <summary>
        /// Returns the MText width needed so the longest intended line in
        /// <paramref name="newText"/> (separated by '\n') fits without wrapping
        /// at the approximate target text height. Uses 0.60 as a conservative
        /// scale factor (SHX → Unicode) since the executor performs the real
        /// height adjustment later.
        /// </summary>
        private static double EstimateLongestLineWidth(string newText, double medianSourceHeight)
        {
            if (string.IsNullOrEmpty(newText) || medianSourceHeight <= 0)
            {
                return 0;
            }

            double approxTargetHeight = medianSourceHeight * 0.6;
            double longest = 0;
            foreach (var line in newText.Split('\n'))
            {
                double lineWidth = TextMetrics.CountVisualUnits(line) * approxTargetHeight * 0.95;
                if (lineWidth > longest)
                {
                    longest = lineWidth;
                }
            }
            return longest * 1.05;
        }

        private static double EstimateExpansionFactor(string sourceText, string targetText, bool isSinglePromotion)
        {
            double sourceUnits = TextMetrics.CountVisualUnits(sourceText);
            double targetUnits = TextMetrics.CountVisualUnits(targetText);
            if (sourceUnits <= 0 || targetUnits <= 0 || targetUnits <= sourceUnits)
            {
                return 1.0;
            }

            double rawRatio = targetUnits / sourceUnits;
            double normalizedRatio = Math.Sqrt(rawRatio);
            double paddedRatio = normalizedRatio * (
                isSinglePromotion
                    ? SingleTextExpansionPaddingFactor
                    : MultiTextExpansionPaddingFactor);
            double maxFactor = isSinglePromotion
                ? SingleTextMaxExpansionFactor
                : MultiTextMaxExpansionFactor;
            return Math.Min(maxFactor, paddedRatio);
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return text
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Count();
        }
    }
}
