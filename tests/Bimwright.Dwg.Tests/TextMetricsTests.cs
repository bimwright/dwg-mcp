using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TextMetricsTests
    {
        [Fact]
        public void Empty_and_null_return_zero()
        {
            Assert.Equal(0, TextMetrics.CountVisualUnits(null));
            Assert.Equal(0, TextMetrics.CountVisualUnits(""));
            Assert.Equal(0, TextMetrics.CountVisualUnits("   "));
        }

        [Fact]
        public void Cjk_characters_count_as_one_unit_each()
        {
            Assert.Equal(4.0, TextMetrics.CountVisualUnits("除臭墙体"));
        }

        [Fact]
        public void Latin_lowercase_is_0_56_per_char()
        {
            Assert.Equal(0.56 * 5, TextMetrics.CountVisualUnits("abcde"), 3);
        }

        [Fact]
        public void Digits_use_0_62_weight()
        {
            Assert.Equal(0.62 * 4, TextMetrics.CountVisualUnits("1234"), 3);
        }

        [Fact]
        public void Mixed_sentence_sums_per_glyph_weights()
        {
            double total = TextMetrics.CountVisualUnits("Dai oc M16");
            Assert.True(total > 4.0 && total < 7.0, $"unexpected total {total}");
        }
    }
}
