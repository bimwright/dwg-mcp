using System.Linq;
using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class SpatialClustererOptionsTests
    {
        [Fact]
        public void Weak_grouping_keeps_nearby_leader_labels_separate()
        {
            var entities = new[]
            {
                Label("A1", "45 du bend", 100, 100),
                Label("A2", "return pipe", 101, 84),
                Label("A3", "flow meter", 102, 68),
            }.ToList();

            var normal = SpatialClusterer.Cluster(entities, ClusterOptions.Normal);
            var weak = SpatialClusterer.Cluster(entities, ClusterOptions.Weak);

            Assert.Single(normal);
            Assert.Equal(3, weak.Count);
            Assert.All(weak, c => Assert.Single(c.AllHandles));
        }

        [Fact]
        public void Weak_skips_vertical_paragraph_merge_even_when_sub_rows_would_merge_under_normal()
        {
            var entities = new[]
            {
                Label("A1", "line one", 100, 100),
                Label("A2", "line two", 100, 89),
                Label("A3", "line three", 100, 78),
            }.ToList();

            var normal = SpatialClusterer.Cluster(entities, ClusterOptions.Normal);
            var weak = SpatialClusterer.Cluster(entities, ClusterOptions.Weak);

            Assert.Single(normal);
            Assert.Equal(3, weak.Count);
            Assert.All(weak, c => Assert.Single(c.AllHandles));
        }

        [Fact]
        public void Cluster_state_preserves_entity_details_for_escape_hatch_rewrites()
        {
            var entities = new[]
            {
                Label("A1", "45 du bend", 100, 100),
                Label("A2", "return pipe", 101, 84),
            }.ToList();

            var cluster = Assert.Single(SpatialClusterer.Cluster(entities, ClusterOptions.Normal));

            Assert.Equal(new[] { "A1", "A2" }, cluster.Entities.Select(e => e.Handle).ToArray());
            Assert.Equal(new[] { "45 du bend", "return pipe" }, cluster.Entities.Select(e => e.Text).ToArray());
            Assert.True(cluster.MtextHeight > 0,
                $"expected MtextHeight > 0 but was {cluster.MtextHeight}");
        }

        private static EntityRecord Label(string handle, string text, double x, double y)
            => new EntityRecord
            {
                Handle = handle,
                Kind = "DBText",
                Text = text,
                X = x,
                Y = y,
                Height = 10,
                Layer = "TEXT",
                BoundsMinX = x,
                BoundsMaxX = x + 80,
                BoundsMinY = y - 10,
                BoundsMaxY = y,
                BlockScale = 1.0
            };
    }
}
