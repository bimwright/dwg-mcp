using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CadWireTests
    {
        [Fact]
        public void TryParsePoint_AcceptsXyAndDefaultsZToZero()
        {
            var ok = CadWire.TryParsePoint(
                JObject.Parse("{\"x\":1,\"y\":2}"),
                out var point,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(1d, point.X);
            Assert.Equal(2d, point.Y);
            Assert.Equal(0d, point.Z);
        }

        [Fact]
        public void TryParsePoint_AcceptsXyz()
        {
            var ok = CadWire.TryParsePoint(
                JObject.Parse("{\"x\":1,\"y\":2,\"z\":3}"),
                out var point,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(1d, point.X);
            Assert.Equal(2d, point.Y);
            Assert.Equal(3d, point.Z);
        }

        [Fact]
        public void TryParsePoint_RejectsMissingY()
        {
            var ok = CadWire.TryParsePoint(
                JObject.Parse("{\"x\":1}"),
                out _,
                out var error);

            Assert.False(ok);
            Assert.Contains("y", error);
        }

        [Fact]
        public void TryParseHandleValue_AcceptsHexHandle()
        {
            var ok = CadWire.TryParseHandleValue("7F5AD", out var value, out var error);

            Assert.True(ok, error);
            Assert.Equal(0x7F5ADL, value);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(256)]
        public void TryReadAciColor_AcceptsValidColorIndexes(int color)
        {
            var ok = CadWire.TryReadAciColor(
                JObject.FromObject(new { color }),
                "color",
                256,
                out var colorIndex,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(color, colorIndex);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(257)]
        public void TryReadAciColor_RejectsInvalidColorIndexes(int color)
        {
            var ok = CadWire.TryReadAciColor(
                JObject.FromObject(new { color }),
                "color",
                256,
                out _,
                out var error);

            Assert.False(ok);
            Assert.Contains("color", error);
        }
    }
}
