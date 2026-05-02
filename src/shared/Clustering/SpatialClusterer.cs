using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Dwg.Plugin
{
    public sealed class EntityRecord
    {
        public string Handle { get; set; }
        public string Kind { get; set; }
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Height { get; set; }
        public string Layer { get; set; }
        public string BlockHandle { get; set; }  // null = top-level
        public double BoundsMinX { get; set; }
        public double BoundsMaxX { get; set; }
        public double BoundsMinY { get; set; }
        public double BoundsMaxY { get; set; }
        public double BlockScale { get; set; } = 1.0;  // uniform scale of containing block reference (1.0 if top-level)
    }

    public sealed class ClusterOptions
    {
        public static readonly ClusterOptions Weak = new ClusterOptions
        {
            RowGapMultiplier = 1.0,
            ColumnSplitGapMultiplier = 1.2,
            ParagraphRowGapMultiplier = 1.2,
            CentroidCloseMultiplier = 3.0,
            MergeParagraphs = false
        };

        public static readonly ClusterOptions Normal = new ClusterOptions
        {
            RowGapMultiplier = 1.5,
            ColumnSplitGapMultiplier = 2.0,
            ParagraphRowGapMultiplier = 2.0,
            CentroidCloseMultiplier = 6.0,
            MergeParagraphs = true
        };

        public static readonly ClusterOptions Strong = new ClusterOptions
        {
            RowGapMultiplier = 2.0,
            ColumnSplitGapMultiplier = 3.0,
            ParagraphRowGapMultiplier = 3.0,
            CentroidCloseMultiplier = 8.0,
            MergeParagraphs = true
        };

        public double RowGapMultiplier { get; set; }
        public double ColumnSplitGapMultiplier { get; set; }
        public double ParagraphRowGapMultiplier { get; set; }
        public double CentroidCloseMultiplier { get; set; }

        /// <summary>
        /// When false, step 6 (paragraph merge) is skipped entirely — every
        /// sub-row produced by step 5 becomes its own cluster. Row-level
        /// (horizontal) grouping is preserved; vertical merging is disabled.
        /// </summary>
        public bool MergeParagraphs { get; set; } = true;

        public static ClusterOptions FromWire(string value)
        {
            if (string.Equals(value, "weak", StringComparison.OrdinalIgnoreCase))
            {
                return Weak;
            }

            if (string.Equals(value, "strong", StringComparison.OrdinalIgnoreCase))
            {
                return Strong;
            }

            return Normal;
        }
    }

    public static class SpatialClusterer
    {
        private const string TopLevel = "__TOP__";

        public static List<ClusterState> Cluster(List<EntityRecord> entities)
            => Cluster(entities, ClusterOptions.Normal);

        public static List<ClusterState> Cluster(List<EntityRecord> entities, ClusterOptions options)
        {
            options = options ?? ClusterOptions.Normal;
            if (entities == null || entities.Count == 0)
                return new List<ClusterState>();

            // Compute global median height for fallback
            double globalMedian = Median(entities.Select(e => e.Height).ToList());

            var result = new List<ClusterState>();
            int idCounter = 1;

            // Step 1: Group by BlockHandle
            var groups = entities
                .GroupBy(e => e.BlockHandle ?? TopLevel)
                .ToList();

            foreach (var group in groups)
            {
                string blockHandle = group.Key;
                bool inBlock = blockHandle != TopLevel;
                var groupEntities = group.ToList();

                double medianHeight = Median(groupEntities.Select(e => e.Height).ToList());
                if (medianHeight <= 0) medianHeight = globalMedian;
                if (medianHeight <= 0) medianHeight = 1.0;

                // Step 2: Sort by Y descending
                var sortedByY = groupEntities.OrderByDescending(e => e.Y).ToList();

                // Step 3: Row detection — consecutive entities with Y gap under the configured threshold → same row
                var rows = new List<List<EntityRecord>>();
                var currentRow = new List<EntityRecord> { sortedByY[0] };

                for (int i = 1; i < sortedByY.Count; i++)
                {
                    double yGap = Math.Abs(currentRow.Last().Y - sortedByY[i].Y);
                    if (yGap < medianHeight * options.RowGapMultiplier)
                    {
                        currentRow.Add(sortedByY[i]);
                    }
                    else
                    {
                        rows.Add(currentRow);
                        currentRow = new List<EntityRecord> { sortedByY[i] };
                    }
                }
                rows.Add(currentRow);

                // Step 4: Within each row, sort by X ascending
                for (int r = 0; r < rows.Count; r++)
                    rows[r] = rows[r].OrderBy(e => e.X).ToList();

                // Step 5: Column split within each row — X gap above the configured threshold → separate cluster
                // Build sub-rows (row segments separated by large X gaps)
                var subRows = new List<SubRow>();
                foreach (var row in rows)
                {
                    var currentSegment = new List<EntityRecord> { row[0] };
                    for (int i = 1; i < row.Count; i++)
                    {
                        double xGap = row[i].BoundsMinX - currentSegment.Last().BoundsMaxX;
                        if (xGap > medianHeight * options.ColumnSplitGapMultiplier)
                        {
                            subRows.Add(new SubRow
                            {
                                Entities = currentSegment,
                                RepresentativeY = currentSegment.Max(e => e.Y),
                                Layer = MajorityLayer(currentSegment)
                            });
                            currentSegment = new List<EntityRecord> { row[i] };
                        }
                        else
                        {
                            currentSegment.Add(row[i]);
                        }
                    }
                    subRows.Add(new SubRow
                    {
                        Entities = currentSegment,
                        RepresentativeY = currentSegment.Max(e => e.Y),
                        Layer = MajorityLayer(currentSegment)
                    });
                }

                // Step 6: Paragraph merge — consecutive sub-rows with same layer,
                // X-overlapping, and close enough under the configured thresholds → same cluster.
                // Skipped entirely when options.MergeParagraphs == false (horizontal-only mode).
                var clusters = new List<List<SubRow>>();
                if (!options.MergeParagraphs)
                {
                    foreach (var sr in subRows)
                    {
                        clusters.Add(new List<SubRow> { sr });
                    }
                }
                else
                {
                    var currentCluster = new List<SubRow> { subRows[0] };

                    for (int i = 1; i < subRows.Count; i++)
                    {
                        SubRow prev = subRows[i - 1];
                        SubRow curr = subRows[i];

                        double rowGap = Math.Abs(prev.RepresentativeY - curr.RepresentativeY);
                        bool sameLayer = prev.Layer == curr.Layer;
                        bool xOverlap = XOverlaps(prev.Entities, curr.Entities);
                        bool centroidsClose = CentroidsClose(prev.Entities, curr.Entities, medianHeight, options.CentroidCloseMultiplier);

                        if (sameLayer && xOverlap && centroidsClose && rowGap < medianHeight * options.ParagraphRowGapMultiplier)
                        {
                            currentCluster.Add(curr);
                        }
                        else
                        {
                            clusters.Add(currentCluster);
                            currentCluster = new List<SubRow> { curr };
                        }
                    }
                    clusters.Add(currentCluster);
                }

                // Step 7: Build ClusterState for each cluster
                foreach (var clusterSubRows in clusters)
                {
                    var allEntities = clusterSubRows.SelectMany(sr => sr.Entities).ToList();

                    // Anchor = highest Y then leftmost X
                    var anchor = allEntities
                        .OrderByDescending(e => e.Y)
                        .ThenBy(e => e.X)
                        .First();

                    // Combined text = space-joined in reading order (top-to-bottom, left-to-right)
                    var orderedEntities = allEntities
                        .OrderByDescending(e => e.Y)
                        .ThenBy(e => e.X)
                        .ToList();
                    string combinedText = string.Join(" ", orderedEntities.Select(e => e.Text));

                    double boundsMinX = allEntities.Min(e => e.BoundsMinX);
                    double boundsMaxX = allEntities.Max(e => e.BoundsMaxX);
                    double boundsMinY = allEntities.Min(e => e.BoundsMinY);
                    double boundsMaxY = allEntities.Max(e => e.BoundsMaxY);
                    double mtextWidth  = boundsMaxX - boundsMinX;
                    double mtextHeight = boundsMaxY - boundsMinY;

                    var allHandles = allEntities.Select(e => e.Handle).ToList();
                    var deleteHandles = allHandles.Where(h => h != anchor.Handle).ToList();

                    result.Add(new ClusterState
                    {
                        Id = idCounter++,
                        AnchorHandle = anchor.Handle,
                        Entities = orderedEntities,
                        AllHandles = allHandles,
                        DeleteHandles = deleteHandles,
                        CombinedText = combinedText,
                        InBlock = inBlock,
                        MtextWidth = mtextWidth,
                        MtextHeight = mtextHeight,
                        MedianHeight = medianHeight,
                        Layer = clusterSubRows[0].Layer,
                        BlockScale = allEntities[0].BlockScale,
                        CanPromoteSingleToMText = !inBlock
                            && allEntities.Count == 1
                            && string.Equals(anchor.Kind, "DBText", StringComparison.Ordinal)
                    });
                }
            }

            return result;
        }

        private static bool XOverlaps(List<EntityRecord> rowA, List<EntityRecord> rowB)
        {
            double minA = rowA.Min(e => e.BoundsMinX);
            double maxA = rowA.Max(e => e.BoundsMaxX);
            double minB = rowB.Min(e => e.BoundsMinX);
            double maxB = rowB.Max(e => e.BoundsMaxX);
            return minA <= maxB && minB <= maxA;
        }

        private static bool CentroidsClose(
            List<EntityRecord> rowA,
            List<EntityRecord> rowB,
            double medianHeight,
            double multiplier)
        {
            double centroidA = rowA.Average(e => (e.BoundsMinX + e.BoundsMaxX) / 2.0);
            double centroidB = rowB.Average(e => (e.BoundsMinX + e.BoundsMaxX) / 2.0);
            return Math.Abs(centroidA - centroidB) < medianHeight * multiplier;
        }

        private static string MajorityLayer(List<EntityRecord> entities)
        {
            return entities
                .GroupBy(e => e.Layer)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            if (sorted.Count % 2 == 0)
                return (sorted[mid - 1] + sorted[mid]) / 2.0;
            return sorted[mid];
        }

        private class SubRow
        {
            public List<EntityRecord> Entities { get; set; }
            public double RepresentativeY { get; set; }
            public string Layer { get; set; }
        }
    }
}
