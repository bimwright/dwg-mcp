using System;
using System.IO;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class AnnotationEntityFactorySourceTests
    {
        [Fact]
        public void AnnotationFactoryDisposesAllocatedEntitiesUntilCallerOwnsThem()
        {
            var source = ReadAnnotationEntityFactorySource();

            Assert.Equal(4, CountOccurrences(source, "var ownsEntity = false;"));
            Assert.Equal(4, CountOccurrences(source, "ownsEntity = true;"));
            Assert.Equal(4, CountOccurrences(source, "return TransferToCaller(created, out entity, ref ownsEntity);"));
            Assert.Equal(4, CountOccurrences(source, "DisposeIfOwned(created, ownsEntity);"));
            Assert.Contains("Caller owns the entity after a successful factory return.", source, StringComparison.Ordinal);
            Assert.Contains("private static void DisposeIfOwned(DBObject entity, bool ownsEntity)", source, StringComparison.Ordinal);
        }

        private static int CountOccurrences(string value, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }

            return count;
        }

        private static string ReadAnnotationEntityFactorySource()
        {
            var path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "shared",
                "Annotation",
                "AnnotationEntityFactory.cs"));

            return File.ReadAllText(path);
        }
    }
}
