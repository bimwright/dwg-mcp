using Bimwright.Dwg.Plugin.Cad;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CadPrimitiveValidationTests
    {
        [Theory]
        [InlineData(0d, 0d, 1d, 0d, 10d, "width")]
        [InlineData(0d, 0d, 1d, 5d, 0d, "height")]
        public void TryValidateRectangleCorners_RejectsZeroXyExtent(
            double x1,
            double y1,
            double z1,
            double x2,
            double y2,
            string expectedError)
        {
            var ok = CadPrimitiveValidation.TryValidateRectangleCorners(
                new CadPointInput(x1, y1, z1),
                new CadPointInput(x2, y2, z1 + 25d),
                out var error);

            Assert.False(ok);
            Assert.Contains(expectedError, error);
        }

        [Fact]
        public void TryValidateRectangleCorners_AcceptsNonZeroXyExtent()
        {
            var ok = CadPrimitiveValidation.TryValidateRectangleCorners(
                new CadPointInput(0d, 0d, 0d),
                new CadPointInput(5d, 10d, 25d),
                out var error);

            Assert.True(ok, error);
        }

        [Theory]
        [InlineData(0d, 360d)]
        [InlineData(720d, 1080d)]
        [InlineData(450d, 90d)]
        public void TryValidateArcSweepDegrees_RejectsZeroNormalizedSweep(double startAngle, double endAngle)
        {
            var ok = CadPrimitiveValidation.TryValidateArcSweepDegrees(
                startAngle,
                endAngle,
                out var error);

            Assert.False(ok);
            Assert.Contains("sweep", error);
        }

        [Theory]
        [InlineData(0d, 90d)]
        [InlineData(720d, 1170d)]
        [InlineData(90d, 0d)]
        public void TryValidateArcSweepDegrees_AcceptsNonZeroNormalizedSweep(double startAngle, double endAngle)
        {
            var ok = CadPrimitiveValidation.TryValidateArcSweepDegrees(
                startAngle,
                endAngle,
                out var error);

            Assert.True(ok, error);
        }

        [Fact]
        public void TryValidateEllipseRadiusRatio_RejectsUnderflowedRatio()
        {
            var ok = CadPrimitiveValidation.TryValidateEllipseRadiusRatio(
                double.MaxValue,
                double.Epsilon,
                out var radiusRatio,
                out var error);

            Assert.False(ok);
            Assert.Equal(0d, radiusRatio);
            Assert.Contains("ratio", error);
        }

        [Theory]
        [InlineData(10d, 10d, 1d)]
        [InlineData(10d, 5d, 0.5d)]
        public void TryValidateEllipseRadiusRatio_AcceptsFinitePositiveRatio(
            double majorRadius,
            double minorRadius,
            double expectedRatio)
        {
            var ok = CadPrimitiveValidation.TryValidateEllipseRadiusRatio(
                majorRadius,
                minorRadius,
                out var radiusRatio,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(expectedRatio, radiusRatio);
        }
    }
}
