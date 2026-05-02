using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class RewriteRequestBuilderTests
    {
        private static ClusterState Cluster(
            int id,
            string anchor,
            List<string> allHandles,
            bool inBlock = false,
            double mtextWidth = 0,
            double medianHeight = 0,
            bool canPromoteSingleToMText = false,
            string combinedText = "sample")
        {
            var deleteHandles = new List<string>();
            foreach (var h in allHandles)
            {
                if (h != anchor)
                {
                    deleteHandles.Add(h);
                }
            }

            return new ClusterState
            {
                Id = id,
                AnchorHandle = anchor,
                AllHandles = allHandles,
                DeleteHandles = deleteHandles,
                CombinedText = combinedText,
                InBlock = inBlock,
                MtextWidth = mtextWidth,
                MedianHeight = medianHeight,
                Layer = "PUB_TEXT",
                CanPromoteSingleToMText = canPromoteSingleToMText
            };
        }

        [Fact]
        public void DetermineAction_single_entity_with_translation_is_update()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" });

            Assert.Equal(RewriteAction.Update,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true));
        }

        [Fact]
        public void DetermineAction_single_promotable_long_translation_is_collapse()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, canPromoteSingleToMText: true);

            Assert.Equal(RewriteAction.Collapse,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true,
                    newText: "Mot dong dich nay co nhieu hon bay chu"));
        }

        [Fact]
        public void DetermineAction_single_promotable_short_translation_stays_update()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, canPromoteSingleToMText: true);

            Assert.Equal(RewriteAction.Update,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true,
                    newText: "Sau chu van la update"));
        }

        [Fact]
        public void DetermineAction_single_non_promotable_long_translation_stays_update()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, canPromoteSingleToMText: false);

            Assert.Equal(RewriteAction.Update,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true,
                    newText: "Mot dong dich nay co nhieu hon bay chu"));
        }

        [Fact]
        public void DetermineAction_render_mode_mtext_forces_safe_single_to_collapse()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, canPromoteSingleToMText: true);

            Assert.Equal(RewriteAction.Collapse,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true,
                    newText: "Ngan",
                    renderMode: RewriteRenderMode.MText));
        }

        [Fact]
        public void DetermineAction_render_mode_mtext_does_not_break_in_block_clusters()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, inBlock: true, canPromoteSingleToMText: false);

            Assert.Equal(RewriteAction.Update,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true,
                    newText: "Mot dong rat dai",
                    renderMode: RewriteRenderMode.MText));
        }

        [Fact]
        public void DetermineAction_single_entity_without_translation_is_style_only()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" });

            Assert.Equal(RewriteAction.StyleOnly,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: false));
        }

        [Fact]
        public void DetermineAction_multi_entity_top_level_with_translation_is_collapse()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2", "A3" }, inBlock: false);

            Assert.Equal(RewriteAction.Collapse,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true));
        }

        [Fact]
        public void DetermineAction_multi_entity_in_block_with_translation_is_rewrite_in_block()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2" }, inBlock: true);

            Assert.Equal(RewriteAction.RewriteInBlock,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: true));
        }

        [Fact]
        public void DetermineAction_multi_entity_without_translation_is_style_only()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2" });

            Assert.Equal(RewriteAction.StyleOnly,
                RewriteRequestBuilder.DetermineAction(c, hasTranslation: false));
        }

        [Fact]
        public void FromCluster_update_action_has_no_delete_handles()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" });

            var req = RewriteRequestBuilder.FromCluster(c, "new", hasTranslation: true, applyUnicodeStyle: true);

            Assert.Equal(RewriteAction.Update, req.Action);
            Assert.Equal("A1", req.AnchorHandle);
            Assert.Empty(req.DeleteHandles);
            Assert.Equal("new", req.NewText);
            Assert.True(req.ApplyUnicodeStyle);
        }

        [Fact]
        public void FromCluster_single_promoted_to_collapse_uses_width_floor()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" },
                mtextWidth: 5, medianHeight: 2.5, canPromoteSingleToMText: true, combinedText: "ab");

            var req = RewriteRequestBuilder.FromCluster(
                c,
                "Mot dong dich nay co nhieu hon bay chu",
                hasTranslation: true,
                applyUnicodeStyle: true);

            Assert.Equal(RewriteAction.Collapse, req.Action);
            Assert.Empty(req.DeleteHandles);
            Assert.Equal(35.0, req.MtextWidth);
        }

        [Fact]
        public void FromCluster_render_mode_mtext_forces_collapse_even_for_short_translation()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" },
                mtextWidth: 100, medianHeight: 2.5, canPromoteSingleToMText: true, combinedText: "ab");

            var req = RewriteRequestBuilder.FromCluster(
                c,
                "Ngan",
                hasTranslation: true,
                applyUnicodeStyle: true,
                renderMode: RewriteRenderMode.MText);

            Assert.Equal(RewriteAction.Collapse, req.Action);
            Assert.True(req.MtextWidth >= 100);
        }

        [Fact]
        public void FromCluster_collapse_preserves_mtext_width_and_delete_handles()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2", "A3" },
                inBlock: false, mtextWidth: 120.5, medianHeight: 2.5, combinedText: "collapsed");

            var req = RewriteRequestBuilder.FromCluster(c, "collapsed", hasTranslation: true, applyUnicodeStyle: true);

            Assert.Equal(RewriteAction.Collapse, req.Action);
            Assert.Equal("A1", req.AnchorHandle);
            Assert.Equal(new[] { "A2", "A3" }, req.DeleteHandles);
            Assert.Equal(120.5, req.MtextWidth);
            Assert.Equal(2.5, req.MedianHeight);
            Assert.Equal("collapsed", req.NewText);
        }

        [Fact]
        public void FromCluster_single_promoted_to_collapse_expands_width_for_long_translation()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" },
                mtextWidth: 100, medianHeight: 2.5, canPromoteSingleToMText: true, combinedText: "ab");

            var req = RewriteRequestBuilder.FromCluster(
                c,
                "Mot dong dich nay co nhieu hon bay chu",
                hasTranslation: true,
                applyUnicodeStyle: true);

            Assert.Equal(RewriteAction.Collapse, req.Action);
            Assert.Equal(250.0, req.MtextWidth);
        }

        [Fact]
        public void FromCluster_multi_collapse_expands_width_for_longer_translation()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2" },
                inBlock: false, mtextWidth: 200, medianHeight: 5, combinedText: "abc");

            var req = RewriteRequestBuilder.FromCluster(
                c,
                "Mot ban dich tieng Viet dai hon dang ke",
                hasTranslation: true,
                applyUnicodeStyle: true);

            Assert.Equal(RewriteAction.Collapse, req.Action);
            Assert.True(req.MtextWidth > 200);
        }

        [Fact]
        public void FromCluster_rewrite_in_block_preserves_delete_handles()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2" },
                inBlock: true, medianHeight: 3.0);

            var req = RewriteRequestBuilder.FromCluster(c, "inblock", hasTranslation: true, applyUnicodeStyle: false);

            Assert.Equal(RewriteAction.RewriteInBlock, req.Action);
            Assert.Equal("A1", req.AnchorHandle);
            Assert.Equal(new[] { "A2" }, req.DeleteHandles);
            Assert.Equal("inblock", req.NewText);
            Assert.Equal(3.0, req.MedianHeight);
            Assert.False(req.ApplyUnicodeStyle);
        }

        [Fact]
        public void FromCluster_style_only_carries_delete_handles_for_style_propagation()
        {
            var c = Cluster(1, "A1", new List<string> { "A1", "A2", "A3" });

            var req = RewriteRequestBuilder.FromCluster(c, null, hasTranslation: false, applyUnicodeStyle: true);

            Assert.Equal(RewriteAction.StyleOnly, req.Action);
            Assert.Equal(new[] { "A2", "A3" }, req.DeleteHandles);
            Assert.Null(req.NewText);
            Assert.True(req.ApplyUnicodeStyle);
        }

        [Fact]
        public void FromCluster_zero_median_height_becomes_null_for_executor()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, medianHeight: 0);

            var req = RewriteRequestBuilder.FromCluster(c, "new", hasTranslation: true, applyUnicodeStyle: false);

            Assert.Null(req.MedianHeight);
        }

        [Fact]
        public void FromCluster_positive_median_height_is_preserved()
        {
            var c = Cluster(1, "A1", new List<string> { "A1" }, medianHeight: 2.5);

            var req = RewriteRequestBuilder.FromCluster(c, "new", hasTranslation: true, applyUnicodeStyle: false);

            Assert.Equal(2.5, req.MedianHeight);
        }
    }
}
