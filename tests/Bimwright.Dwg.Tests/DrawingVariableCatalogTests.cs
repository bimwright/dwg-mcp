using System;
using Bimwright.Dwg.Plugin.Drawing;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DrawingVariableCatalogTests
    {
        [Fact]
        public void Allowlist_ExposesExactlyPlan4Variables()
        {
            var readables = new[] { "CLAYER", "INSUNITS", "LUNITS", "DIMSCALE", "TEXTSIZE", "OSMODE", "ORTHOMODE" };
            var writables = new[] { "CLAYER", "DIMSCALE", "TEXTSIZE", "OSMODE", "ORTHOMODE" };
            var unreadables = new[] { "FILEDIA", "SAVETIME", "BOGUS" };

            foreach (var r in readables)
            {
                Assert.True(SystemVariableCatalog.IsReadable(r), $"Should be readable: {r}");
                Assert.True(SystemVariableCatalog.IsReadable(r.ToLowerInvariant()), $"Case-insensitive should be readable: {r}");
            }

            foreach (var w in writables)
            {
                Assert.True(SystemVariableCatalog.IsWritable(w), $"Should be writable: {w}");
            }

            foreach (var ur in unreadables)
            {
                Assert.False(SystemVariableCatalog.IsReadable(ur), $"Should not be readable: {ur}");
                Assert.False(SystemVariableCatalog.IsWritable(ur), $"Should not be writable: {ur}");
            }

            Assert.False(SystemVariableCatalog.IsWritable("INSUNITS"), "INSUNITS should be read-only.");
            Assert.False(SystemVariableCatalog.IsWritable("LUNITS"), "LUNITS should be read-only.");
        }

        [Theory]
        [InlineData("CLAYER", "0", "0")]
        [InlineData("CLAYER", 123, "123")]
        [InlineData("DIMSCALE", 1, 1.0d)]
        [InlineData("DIMSCALE", "2.5", 2.5d)]
        [InlineData("OSMODE", "3", 3)]
        [InlineData("OSMODE", 16384.0d, 16384)]
        public void Coerce_SuccessfullyCoercesValidValues(string name, object input, object expected)
        {
            var success = SystemVariableCatalog.TryCoerceValue(name, input, out var coerced, out var error);

            Assert.True(success, error);
            Assert.Equal(expected, coerced);
            Assert.Null(error);
        }

        [Fact]
        public void Coerce_RejectsUnknownVariables()
        {
            var success = SystemVariableCatalog.TryCoerceValue("BOGUS", "value", out var coerced, out var error);

            Assert.False(success);
            Assert.Null(coerced);
            Assert.Contains("unknown system variable", error);
        }

        [Fact]
        public void Coerce_RejectsInvalidCoercions()
        {
            var success = SystemVariableCatalog.TryCoerceValue("DIMSCALE", "not-a-number", out var coerced, out var error);

            Assert.False(success);
            Assert.Null(coerced);
            Assert.Contains("cannot be coerced", error);
        }
    }
}
