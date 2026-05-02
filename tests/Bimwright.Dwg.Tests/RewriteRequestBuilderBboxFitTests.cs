using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class RewriteRequestBuilderBboxFitTests
    {
        private static ClusterState MakeCollapseCluster() => new ClusterState
        {
            Id = 1,
            AnchorHandle = "A1",
            AllHandles = new List<string> { "A1", "A2", "A3" },
            DeleteHandles = new List<string> { "A2", "A3" },
            CombinedText = "abc",
            InBlock = false,
            MtextWidth = 200,
            MtextHeight = 400,
            MedianHeight = 50,
            CanPromoteSingleToMText = false
        };

        [Fact]
        public void Expand_policy_on_collapse_populates_explicit_height_from_sizer()
        {
            var cluster = MakeCollapseCluster();

            var request = RewriteRequestBuilder.FromCluster(
                cluster,
                "Vietnamese translation that is longer than the source",
                hasTranslation: true,
                applyUnicodeStyle: true,
                renderMode: RewriteRenderMode.Auto,
                widthPolicy: RewriteWidthPolicy.Expand);

            Assert.Equal(RewriteAction.Collapse, request.Action);
            Assert.NotNull(request.ExplicitTextHeight);
            Assert.True(request.ExplicitTextHeight > 0);
            Assert.True(request.ExplicitTextHeight <= cluster.MedianHeight,
                "bbox-fit must never return a height larger than the original");
            Assert.Equal(200, request.MtextWidth);
            Assert.StartsWith("bbox_fit", request.LayoutHint);
        }

        [Fact]
        public void Preserve_policy_skips_bbox_fit_and_keeps_original_width()
        {
            var cluster = MakeCollapseCluster();

            var request = RewriteRequestBuilder.FromCluster(
                cluster,
                "Vietnamese translation that is longer than the source",
                hasTranslation: true,
                applyUnicodeStyle: true,
                renderMode: RewriteRenderMode.Auto,
                widthPolicy: RewriteWidthPolicy.Preserve);

            Assert.Equal(RewriteAction.Collapse, request.Action);
            Assert.Null(request.ExplicitTextHeight);
            Assert.Equal(200, request.MtextWidth);
            Assert.Null(request.LayoutHint);
        }

        [Fact]
        public void Update_action_never_populates_explicit_height()
        {
            var cluster = new ClusterState
            {
                Id = 2,
                AnchorHandle = "A1",
                AllHandles = new List<string> { "A1" },
                DeleteHandles = new List<string>(),
                CombinedText = "a",
                InBlock = false,
                MtextWidth = 50,
                MtextHeight = 30,
                MedianHeight = 25,
                CanPromoteSingleToMText = false
            };

            var request = RewriteRequestBuilder.FromCluster(
                cluster,
                "Ngan",
                hasTranslation: true,
                applyUnicodeStyle: true,
                renderMode: RewriteRenderMode.Auto,
                widthPolicy: RewriteWidthPolicy.Expand);

            Assert.Equal(RewriteAction.Update, request.Action);
            Assert.Null(request.ExplicitTextHeight);
        }
    }
}
