using System;
using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BboxFitSizerTests
    {
        [Fact]
        public void Update_action_returns_scale_only_sentinel()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Update,
                origW: 200, origH: 400,
                origTextHeight: 50,
                translatedCharCount: 30);

            Assert.Null(r.Width);
            Assert.Null(r.TextHeight);
            Assert.Equal("scale_only", r.LayoutHint);
        }

        [Fact]
        public void StyleOnly_action_returns_scale_only_sentinel()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.StyleOnly,
                origW: 200, origH: 400,
                origTextHeight: 50,
                translatedCharCount: 30);

            Assert.Null(r.Width);
            Assert.Null(r.TextHeight);
            Assert.Equal("scale_only", r.LayoutHint);
        }

        [Theory]
        [InlineData(0, 400, 50, 30)]
        [InlineData(200, 0, 50, 30)]
        [InlineData(200, 400, 0, 30)]
        [InlineData(200, 400, 50, 0)]
        [InlineData(-1, 400, 50, 30)]
        public void Invalid_inputs_fall_back_to_scale_only(
            double w, double h, double hOrig, double n)
        {
            var r = BboxFitSizer.Compute(RewriteAction.Collapse, w, h, hOrig, n);
            Assert.Equal("scale_only", r.LayoutHint);
            Assert.Null(r.Width);
            Assert.Null(r.TextHeight);
        }

        [Fact]
        public void Small_expansion_caps_height_at_original()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Collapse,
                origW: 200, origH: 400,
                origTextHeight: 50,
                translatedCharCount: 8);

            Assert.Equal(200.0, r.Width);
            Assert.Equal(50.0, r.TextHeight);
            Assert.Equal("bbox_fit", r.LayoutHint);
        }

        [Fact]
        public void Normal_expansion_picks_ideal_height_below_original()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Collapse,
                origW: 200, origH: 400,
                origTextHeight: 50,
                translatedCharCount: 40);

            Assert.Equal(200.0, r.Width);
            Assert.Equal(50.0, r.TextHeight);
            Assert.Equal("bbox_fit", r.LayoutHint);
        }

        [Fact]
        public void Heavy_expansion_picks_smaller_height()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Collapse,
                origW: 400, origH: 400,
                origTextHeight: 50,
                translatedCharCount: 120);

            Assert.Equal(400.0, r.Width);
            Assert.InRange(r.TextHeight ?? 0, 45, 50);
            Assert.Equal("bbox_fit", r.LayoutHint);
        }

        [Fact]
        public void Floor_clamp_triggers_height_overflow_hint()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Collapse,
                origW: 100, origH: 100,
                origTextHeight: 40,
                translatedCharCount: 200);

            Assert.Equal(100.0, r.Width);
            Assert.Equal(16.0, r.TextHeight);
            Assert.Equal("bbox_fit+height_overflow", r.LayoutHint);
        }

        [Fact]
        public void Vertical_stack_gets_reflowed_wider()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Collapse,
                origW: 30, origH: 150,
                origTextHeight: 40,
                translatedCharCount: 30);

            Assert.Equal(60.0, r.Width);
            Assert.InRange(r.TextHeight ?? 0, 20, 25);
            Assert.StartsWith("vertical_stack_reflowed", r.LayoutHint);
        }

        [Fact]
        public void Vertical_stack_reflow_with_floor_clamp_composes_hint()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.Collapse,
                origW: 30, origH: 150,
                origTextHeight: 40,
                translatedCharCount: 400);

            Assert.Equal("vertical_stack_reflowed+height_overflow", r.LayoutHint);
            Assert.Equal(16.0, r.TextHeight);
        }

        [Fact]
        public void RewriteInBlock_also_uses_bbox_fit()
        {
            var r = BboxFitSizer.Compute(
                RewriteAction.RewriteInBlock,
                origW: 200, origH: 400,
                origTextHeight: 50,
                translatedCharCount: 40);

            Assert.Equal(200.0, r.Width);
            Assert.Equal(50.0, r.TextHeight);
            Assert.Equal("bbox_fit", r.LayoutHint);
        }
    }
}
