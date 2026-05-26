using Bimwright.Dwg.Plugin.Annotation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class AnnotationRequestValidationTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryValidateTextContent_RejectsBlankText(string text)
        {
            var ok = AnnotationRequestValidation.TryValidateTextContent(text, "text", out var error);

            Assert.False(ok);
            Assert.Contains("text", error);
        }

        [Fact]
        public void TryValidateLeaderPointCount_RequiresAtLeastTwoPoints()
        {
            var ok = AnnotationRequestValidation.TryValidateLeaderPointCount(1, out var error);

            Assert.False(ok);
            Assert.Contains("at least 2", error);
        }

        [Theory]
        [InlineData(0, 2, "rows")]
        [InlineData(2, 0, "columns")]
        public void TryValidateTableShape_RejectsNonPositiveDimensions(
            int rows,
            int columns,
            string expectedError)
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                rows,
                columns,
                JArray.Parse("[[\"A\",\"B\"],[\"C\",\"D\"]]"),
                out var error);

            Assert.False(ok);
            Assert.Contains(expectedError, error);
        }

        [Fact]
        public void TryValidateTableShape_RejectsCellsOutsideDimensions()
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                1,
                1,
                JArray.Parse("[[\"A\",\"B\"]]"),
                out var error);

            Assert.False(ok);
            Assert.Contains("cells", error);
        }

        [Fact]
        public void TryValidateTableShape_AcceptsCellsWithinDimensions()
        {
            var ok = AnnotationRequestValidation.TryValidateTableShape(
                2,
                2,
                JArray.Parse("[[\"A\",\"B\"],[\"C\",\"D\"]]"),
                out var error);

            Assert.True(ok, error);
        }
    }
}
