using Bimwright.Dwg.Plugin.View;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CaptureViewMathTests
    {
        [Fact]
        public void ComputeOutputSize_Landscape_LongerSideIsPixelSize()
        {
            var (width, height) = CaptureViewMath.ComputeOutputSize(1920, 1080, 1600);

            Assert.Equal(1600, width);
            Assert.Equal(900, height); // 1600 * 1080 / 1920
        }

        [Fact]
        public void ComputeOutputSize_Portrait_LongerSideIsPixelSize()
        {
            var (width, height) = CaptureViewMath.ComputeOutputSize(1080, 1920, 1600);

            Assert.Equal(1600, height);
            Assert.Equal(900, width); // 1600 * 1080 / 1920
        }

        [Fact]
        public void ComputeOutputSize_Square_IsSquare()
        {
            var (width, height) = CaptureViewMath.ComputeOutputSize(1000, 1000, 1600);

            Assert.Equal(1600, width);
            Assert.Equal(1600, height);
        }

        [Fact]
        public void ComputeOutputSize_InvalidDisplay_FallsBackToSquarePixelSize()
        {
            var (width, height) = CaptureViewMath.ComputeOutputSize(0, 0, 1600);

            Assert.Equal(1600, width);
            Assert.Equal(1600, height);
        }

        [Fact]
        public void ComputeOutputSize_ClampsPixelSizeAboveMax()
        {
            var (width, height) = CaptureViewMath.ComputeOutputSize(1000, 1000, 100000);

            Assert.Equal(8192, width);
            Assert.Equal(8192, height);
        }

        [Fact]
        public void ComputeOutputSize_ClampsPixelSizeBelowMin()
        {
            var (width, height) = CaptureViewMath.ComputeOutputSize(1000, 1000, 1);

            Assert.Equal(64, width);
            Assert.Equal(64, height);
        }

        [Fact]
        public void ComputeOutputSize_NeverProducesZeroDimension()
        {
            // Extreme aspect ratio must not round the short side down to 0.
            var (width, height) = CaptureViewMath.ComputeOutputSize(10000, 1, 64);

            Assert.True(width >= 1);
            Assert.True(height >= 1);
        }
    }
}
