using System;
using System.IO;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class OffsetEntitiesHandlerSourceTests
    {
        [Fact]
        public void OffsetHandlerTracksAppendedObjectsForPerHandleRollback()
        {
            var source = ReadOffsetHandlerSource();

            Assert.Contains("var appendedObjectIds = new List<ObjectId>();", source, StringComparison.Ordinal);
            Assert.Contains("EraseAppendedEntities(tx, appendedObjectIds)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void OffsetHandlerRequiresWritableSourceOwner()
        {
            var source = ReadOffsetHandlerSource();

            Assert.DoesNotContain("db.CurrentSpaceId", source, StringComparison.Ordinal);
            Assert.Contains("TryOpenOwnerForWrite(tx, source, out var target, out var ownerError)", source, StringComparison.Ordinal);
        }

        private static string ReadOffsetHandlerSource()
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
                "Handlers",
                "OffsetEntitiesHandler.cs"));

            return File.ReadAllText(path);
        }
    }
}
