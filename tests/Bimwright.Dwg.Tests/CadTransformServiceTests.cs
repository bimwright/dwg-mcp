using System;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Server.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CadTransformServiceTests
    {
        [Theory]
        [InlineData(0d, 0d)]
        [InlineData(90d, Math.PI / 2d)]
        [InlineData(180d, Math.PI)]
        public void DegreesToRadians_ConvertsCommonAngles(double degrees, double expectedRadians)
        {
            var radians = CadTransformService.DegreesToRadians(degrees);

            Assert.Equal(expectedRadians, radians, precision: 12);
        }

        [Theory]
        [InlineData(0.001d)]
        [InlineData(1d)]
        [InlineData(1000d)]
        public void TryReadScale_AcceptsPositiveFiniteScaleUpToLimit(double factor)
        {
            var ok = CadTransformService.TryReadScale(factor, out var value, out var error);

            Assert.True(ok, error);
            Assert.Equal(factor, value);
        }

        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        [InlineData(1000.0001d)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void TryReadScale_RejectsNonPositiveNonFiniteAndTooLargeScale(double factor)
        {
            var ok = CadTransformService.TryReadScale(factor, out _, out var error);

            Assert.False(ok);
            Assert.Contains("scale", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TryParseVector_AcceptsXyAndDefaultsZToZero()
        {
            var ok = CadTransformService.TryParseVector(
                JObject.Parse("{\"x\":1,\"y\":2}"),
                out var vector,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(1d, vector.X);
            Assert.Equal(2d, vector.Y);
            Assert.Equal(0d, vector.Z);
        }

        [Fact]
        public void TryParseVector_AcceptsXyz()
        {
            var ok = CadTransformService.TryParseVector(
                JObject.Parse("{\"x\":1,\"y\":2,\"z\":3}"),
                out var vector,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(1d, vector.X);
            Assert.Equal(2d, vector.Y);
            Assert.Equal(3d, vector.Z);
        }

        [Theory]
        [InlineData("{\"y\":2}", "x")]
        [InlineData("{\"x\":1}", "y")]
        public void TryParseVector_RejectsMissingRequiredCoordinates(string json, string expectedField)
        {
            var ok = CadTransformService.TryParseVector(
                JObject.Parse(json),
                out _,
                out var error);

            Assert.False(ok);
            Assert.Contains(expectedField, error);
        }

        [Theory]
        [InlineData("x", double.NaN)]
        [InlineData("y", double.PositiveInfinity)]
        [InlineData("z", double.NegativeInfinity)]
        public void TryParseVector_RejectsNonFiniteCoordinates(string fieldName, double value)
        {
            var parameters = JObject.Parse("{\"x\":1,\"y\":2,\"z\":3}");
            parameters[fieldName] = value;

            var ok = CadTransformService.TryParseVector(parameters, out _, out var error);

            Assert.False(ok);
            Assert.Contains(fieldName, error);
            Assert.Contains("finite", error);
        }

        [Fact]
        public async Task MoveEntities_ReturnsStructuredErrorForMalformedHandlesJson()
        {
            var response = JObject.Parse(await ModifyTools.MoveEntities(
                "not-json",
                "{\"x\":1,\"y\":2}"));

            Assert.False(response.Value<bool>("ok"));
            Assert.Contains("handles", response.Value<string>("error"));
        }

        [Fact]
        public async Task MoveEntities_ReturnsStructuredErrorForMalformedVectorJson()
        {
            var response = JObject.Parse(await ModifyTools.MoveEntities(
                "[\"7F5AD\"]",
                "not-json"));

            Assert.False(response.Value<bool>("ok"));
            Assert.Contains("vector", response.Value<string>("error"));
        }
    }
}
