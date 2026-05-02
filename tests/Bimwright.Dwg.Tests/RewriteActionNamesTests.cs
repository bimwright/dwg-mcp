using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class RewriteActionNamesTests
    {
        [Fact]
        public void Update_wire_name_is_update()
        {
            Assert.Equal("update", RewriteActionNames.ToWire(RewriteAction.Update));
            Assert.Equal("update", RewriteActionNames.Update);
        }

        [Fact]
        public void Collapse_wire_name_is_collapse()
        {
            Assert.Equal("collapse", RewriteActionNames.ToWire(RewriteAction.Collapse));
            Assert.Equal("collapse", RewriteActionNames.Collapse);
        }

        [Fact]
        public void RewriteInBlock_wire_name_is_rewrite_in_block()
        {
            Assert.Equal("rewrite_in_block", RewriteActionNames.ToWire(RewriteAction.RewriteInBlock));
            Assert.Equal("rewrite_in_block", RewriteActionNames.RewriteInBlock);
        }

        [Fact]
        public void StyleOnly_wire_name_is_style_only()
        {
            Assert.Equal("style_only", RewriteActionNames.ToWire(RewriteAction.StyleOnly));
            Assert.Equal("style_only", RewriteActionNames.StyleOnly);
        }

        [Fact]
        public void RenderMode_wire_name_is_mtext()
        {
            Assert.Equal("mtext", RewriteRenderModeNames.ToWire(RewriteRenderMode.MText));
            Assert.Equal("mtext", RewriteRenderModeNames.MText);
        }

        [Fact]
        public void RenderMode_parse_accepts_auto_and_mtext()
        {
            Assert.True(RewriteRenderModeNames.TryParse("auto", out var autoMode));
            Assert.Equal(RewriteRenderMode.Auto, autoMode);

            Assert.True(RewriteRenderModeNames.TryParse("mtext", out var mtextMode));
            Assert.Equal(RewriteRenderMode.MText, mtextMode);
        }

        [Fact]
        public void WidthPolicy_parse_accepts_preserve_expand_and_compact()
        {
            Assert.True(RewriteWidthPolicyNames.TryParse("preserve", out var preserve));
            Assert.Equal(RewriteWidthPolicy.Preserve, preserve);

            Assert.True(RewriteWidthPolicyNames.TryParse("expand", out var expand));
            Assert.Equal(RewriteWidthPolicy.Expand, expand);

            Assert.True(RewriteWidthPolicyNames.TryParse("compact", out var compact));
            Assert.Equal(RewriteWidthPolicy.Compact, compact);
        }
    }
}
