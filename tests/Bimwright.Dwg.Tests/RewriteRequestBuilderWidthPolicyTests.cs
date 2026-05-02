using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class RewriteRequestBuilderWidthPolicyTests
    {
        [Fact]
        public void Preserve_width_policy_keeps_original_cluster_width()
        {
            var cluster = new ClusterState
            {
                Id = 1,
                AnchorHandle = "A1",
                AllHandles = new List<string> { "A1", "A2" },
                DeleteHandles = new List<string> { "A2" },
                CombinedText = "ab",
                InBlock = false,
                MtextWidth = 120,
                MedianHeight = 10,
                CanPromoteSingleToMText = false
            };

            var request = RewriteRequestBuilder.FromCluster(
                cluster,
                "Day la mot dong tieng Viet dai hon nhieu so voi chu goc",
                hasTranslation: true,
                applyUnicodeStyle: true,
                renderMode: RewriteRenderMode.Auto,
                widthPolicy: RewriteWidthPolicy.Preserve);

            Assert.Equal(RewriteAction.Collapse, request.Action);
            Assert.Equal(120, request.MtextWidth);
        }
    }
}
