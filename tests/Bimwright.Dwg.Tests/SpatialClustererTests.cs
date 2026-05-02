using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class SpatialClustererTests
    {
        private static EntityRecord Ent(
            string handle,
            string text,
            double x,
            double y,
            double height,
            string layer = "PUB_TEXT",
            string blockHandle = null)
        {
            return new EntityRecord
            {
                Handle = handle,
                Text = text,
                X = x,
                Y = y,
                Height = height,
                Layer = layer,
                BlockHandle = blockHandle,
                BoundsMinX = x,
                BoundsMaxX = x + text.Length * height * 0.6,
                BoundsMinY = y - height,
                BoundsMaxY = y
            };
        }

        [Fact]
        public void Single_entity_becomes_single_cluster()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Hello", 0, 0, 10)
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Single(clusters);
            Assert.Equal("A1", clusters[0].AnchorHandle);
            Assert.Empty(clusters[0].DeleteHandles);
        }

        [Fact]
        public void Same_row_fragments_merge_left_to_right()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "80mm",  0,  0, 10),
                Ent("A2", "thick", 20, 0, 10),
                Ent("A3", "C30",   40, 0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Single(clusters);
            Assert.Equal("80mm thick C30", clusters[0].CombinedText);
        }

        [Fact]
        public void Large_x_gap_splits_into_separate_clusters()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Left",  0,   0, 10),
                Ent("A2", "Right", 500, 0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void Consecutive_rows_same_layer_merge_into_paragraph()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Line1", 0, 15, 10),
                Ent("A2", "Line2", 0,  0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Single(clusters);
        }

        [Fact]
        public void Large_row_gap_splits_into_separate_clusters()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Top",    0, 50, 10),
                Ent("A2", "Bottom", 0,  0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void Different_blocks_never_merge()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Alpha", 0, 0, 10, "PUB_TEXT", "B1"),
                Ent("A2", "Beta",  0, 0, 10, "PUB_TEXT", "B2"),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void InBlock_true_when_blockHandle_is_set()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "InnerText", 0, 0, 10, "PUB_TEXT", "B1")
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Single(clusters);
            Assert.True(clusters[0].InBlock);
        }

        [Fact]
        public void Anchor_is_topmost_leftmost()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A2", "Right", 30, 0, 10),
                Ent("A1", "Left",   0, 0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Single(clusters);
            Assert.Equal("A1", clusters[0].AnchorHandle);
        }

        [Fact]
        public void MtextWidth_is_bounding_box_x_span()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Hello", 0,  0, 10),
                Ent("A2", "World", 50, 0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Single(clusters);
            Assert.True(clusters[0].MtextWidth > 0,
                $"Expected MtextWidth > 0 but got {clusters[0].MtextWidth}");
        }

        [Fact]
        public void Moderate_x_gap_splits_into_separate_clusters()
        {
            var entities = new List<EntityRecord>
            {
                Ent("A1", "Left", 0, 0, 10),
                Ent("A2", "Right", 49, 0, 10),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void Different_x_centroids_split_even_with_y_overlap()
        {
            var entities = new List<EntityRecord>
            {
                new EntityRecord { Handle = "A1", Text = "TopLabel", X = 0,   Y = 15, Height = 10,
                    Layer = "PUB_TEXT", BoundsMinX = 0,   BoundsMaxX = 200, BoundsMinY = 5,  BoundsMaxY = 15 },
                new EntityRecord { Handle = "A2", Text = "BotLabel", X = 100, Y = 0,  Height = 10,
                    Layer = "PUB_TEXT", BoundsMinX = 100, BoundsMaxX = 800, BoundsMinY = -10, BoundsMaxY = 0 },
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void Block_labels_at_different_x_columns_split_correctly()
        {
            double h = 190;
            var entities = new List<EntityRecord>
            {
                Ent("A1", "A", 0,   0, h, "PUB_TEXT", "B1"),
                Ent("A2", "B", 564, 0, h, "PUB_TEXT", "B1"),
            };

            var clusters = SpatialClusterer.Cluster(entities);

            Assert.Equal(2, clusters.Count);
        }
    }
}
