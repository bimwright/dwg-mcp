using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class FinalTextScalePolicyTests
    {
        [Fact]
        public void Apply_scales_height_by_provided_scale()
        {
            Assert.Equal(16.0, FinalTextScalePolicy.Apply(20.0, 0.80), 3);
            Assert.Equal(15.0, FinalTextScalePolicy.Apply(20.0, 0.75), 3);
        }

        [Fact]
        public void Apply_keeps_non_positive_values_unchanged()
        {
            Assert.Equal(0.0, FinalTextScalePolicy.Apply(0.0, 0.80), 3);
            Assert.Equal(-5.0, FinalTextScalePolicy.Apply(-5.0, 0.80), 3);
        }

        [Theory]
        [InlineData(0.30, 0.50)]
        [InlineData(0.49, 0.50)]
        [InlineData(0.50, 0.50)]
        [InlineData(0.70, 0.70)]
        [InlineData(0.90, 0.90)]
        [InlineData(0.95, 0.90)]
        [InlineData(1.50, 0.90)]
        public void Clamp_bounds_scale_into_allowed_range(double input, double expected)
        {
            Assert.Equal(expected, FinalTextScalePolicy.Clamp(input), 3);
        }

        [Fact]
        public void Clamp_non_positive_or_nan_returns_default()
        {
            Assert.Equal(FinalTextScalePolicy.DefaultScale, FinalTextScalePolicy.Clamp(0.0), 3);
            Assert.Equal(FinalTextScalePolicy.DefaultScale, FinalTextScalePolicy.Clamp(-1.0), 3);
            Assert.Equal(FinalTextScalePolicy.DefaultScale, FinalTextScalePolicy.Clamp(double.NaN), 3);
        }

        [Fact]
        public void Default_scale_is_0_80_and_bounds_are_0_50_and_0_90()
        {
            Assert.Equal(0.80, FinalTextScalePolicy.DefaultScale, 3);
            Assert.Equal(0.50, FinalTextScalePolicy.MinScale, 3);
            Assert.Equal(0.90, FinalTextScalePolicy.MaxScale, 3);
        }
    }
}
