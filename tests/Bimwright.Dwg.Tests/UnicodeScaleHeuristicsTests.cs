using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class UnicodeScaleHeuristicsTests
    {
        // Constants from UnicodeStyleService (which depends on AutoCAD API,
        // so we reference literals directly here)
        private const string StyleName = "Bimwright_Unicode";
        private const string FontFileName = "OpenSans-CondensedLight.ttf";
        private const string FontTypeFace = "Open Sans Condensed Light";

        [Fact]
        public void DetermineScaleFactor_returns_unity_for_unicode_style()
        {
            Assert.Equal(
                1.0,
                UnicodeScaleHeuristics.DetermineScaleFactor(StyleName, FontFileName, FontTypeFace));
        }

        [Fact]
        public void DetermineScaleFactor_returns_shx_scale_for_shx_fonts()
        {
            Assert.Equal(
                UnicodeScaleHeuristics.ShxHeightScale,
                UnicodeScaleHeuristics.DetermineScaleFactor("STANDARD", "simplex.shx", null));
        }

        [Fact]
        public void DetermineScaleFactor_returns_truetype_scale_for_ttf_fonts()
        {
            Assert.Equal(
                UnicodeScaleHeuristics.TrueTypeHeightScale,
                UnicodeScaleHeuristics.DetermineScaleFactor("ROMANS", "arial.ttf", "Arial"));
        }

        [Fact]
        public void DetermineScaleFactor_returns_unknown_scale_when_style_metadata_is_sparse()
        {
            Assert.Equal(
                UnicodeScaleHeuristics.UnknownHeightScale,
                UnicodeScaleHeuristics.DetermineScaleFactor("STANDARD", null, null));
        }
    }
}
