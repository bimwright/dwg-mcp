using System;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Dimensions;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DimensionRequestValidatorTests
    {
        [Theory]
        [InlineData("create_linear_dimension")]
        [InlineData("create_aligned_dimension")]
        public void TryValidateTwoPointDimension_RejectsZeroLengthDimensions(string commandName)
        {
            var point = new CadPointInput(10d, 20d, 0d);

            var ok = DimensionRequestValidator.TryValidateTwoPointDimension(
                commandName,
                point,
                point,
                out var error);

            Assert.False(ok);
            Assert.Contains("start and end points must be different", error);
        }

        [Fact]
        public void TryValidateTwoPointDimension_AcceptsDistinctFinitePoints()
        {
            var ok = DimensionRequestValidator.TryValidateTwoPointDimension(
                "create_linear_dimension",
                new CadPointInput(0d, 0d, 0d),
                new CadPointInput(10d, 0d, 0d),
                out var error);

            Assert.True(ok, error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Line")]
        [InlineData("BlockReference")]
        public void TryValidateRadialTargetType_RejectsMissingOrUnsupportedTargets(string entityTypeName)
        {
            var ok = DimensionRequestValidator.TryValidateRadialTargetType(entityTypeName, out var error);

            Assert.False(ok);
            Assert.Contains("circle or arc", error);
        }

        [Theory]
        [InlineData("Circle")]
        [InlineData("Arc")]
        public void TryValidateRadialTargetType_AcceptsCircleAndArcTargets(string entityTypeName)
        {
            var ok = DimensionRequestValidator.TryValidateRadialTargetType(entityTypeName, out var error);

            Assert.True(ok, error);
        }

        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        public void TryValidateRadialDimensionGeometry_RejectsNonPositiveRadius(double radius)
        {
            var ok = DimensionRequestValidator.TryValidateRadialDimensionGeometry(
                new CadPointInput(0d, 0d, 0d),
                radius,
                new CadPointInput(10d, 0d, 0d),
                out _,
                out var error);

            Assert.False(ok);
            Assert.Contains("radius must be positive", error);
        }

        [Theory]
        [InlineData(0d, 0d)]
        [InlineData(5d, 0d)]
        [InlineData(3d, 4d)]
        public void TryValidateRadialDimensionGeometry_RejectsDimensionLinePointInsideOrOnTargetRadius(double x, double y)
        {
            var ok = DimensionRequestValidator.TryValidateRadialDimensionGeometry(
                new CadPointInput(0d, 0d, 0d),
                5d,
                new CadPointInput(x, y, 0d),
                out _,
                out var error);

            Assert.False(ok);
            Assert.Contains("dimension_line_point must be outside target radius", error);
        }

        [Fact]
        public void TryValidateRadialDimensionGeometry_RejectsNonFiniteDimensionLinePoint()
        {
            var ok = DimensionRequestValidator.TryValidateRadialDimensionGeometry(
                new CadPointInput(0d, 0d, 0d),
                5d,
                new CadPointInput(double.NaN, 0d, 0d),
                out _,
                out var error);

            Assert.False(ok);
            Assert.Contains("dimension_line_point must be finite", error);
        }

        [Fact]
        public void TryValidateRadialDimensionGeometry_ReturnsPositiveLeaderLength()
        {
            var ok = DimensionRequestValidator.TryValidateRadialDimensionGeometry(
                new CadPointInput(0d, 0d, 0d),
                5d,
                new CadPointInput(8d, 0d, 0d),
                out var leaderLength,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(3d, leaderLength, precision: 9);
        }

        [Theory]
        [InlineData(0d, 0d, 0d, 10d, 0d)] // vertical difference with rotation 0
        [InlineData(10d, 0d, 0d, 0d, 90d)] // horizontal difference with rotation 90
        public void TryValidateLinearProjectedDistance_RejectsZeroProjectedMeasurement(
            double sx, double sy, double ex, double ey, double rotation)
        {
            var ok = DimensionRequestValidator.TryValidateLinearProjectedDistance(
                new CadPointInput(sx, sy, 0d),
                new CadPointInput(ex, ey, 0d),
                rotation,
                out var error);

            Assert.False(ok);
            Assert.Contains("projected measurement distance along rotation axis must be greater than zero", error);
        }

        [Theory]
        [InlineData(0d, 0d, 0d, 10d, 90d)] // vertical difference with rotation 90
        [InlineData(0d, 0d, 10d, 0d, 0d)] // horizontal difference with rotation 0
        [InlineData(0d, 0d, 10d, 10d, 45d)] // diagonal difference with rotation 45
        public void TryValidateLinearProjectedDistance_AcceptsNonZeroProjectedMeasurement(
            double sx, double sy, double ex, double ey, double rotation)
        {
            var ok = DimensionRequestValidator.TryValidateLinearProjectedDistance(
                new CadPointInput(sx, sy, 0d),
                new CadPointInput(ex, ey, 0d),
                rotation,
                out var error);

            Assert.True(ok, error);
            Assert.Null(error);
        }

        [Theory]
        [InlineData(0d, 0d)]
        [InlineData(90d, Math.PI / 2d)]
        [InlineData(180d, Math.PI)]
        [InlineData(45d, Math.PI / 4d)]
        public void DegreesToRadians_ConvertsDimensionAngles(double degrees, double expectedRadians)
        {
            var radians = DimensionRequestValidator.DegreesToRadians(degrees);

            Assert.Equal(expectedRadians, radians, precision: 12);
        }
    }
}
