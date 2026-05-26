using System;
using System.IO;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DimensionHandlerSourceTests
    {
        [Fact]
        public void CommandDispatcherRegistersDimensionCommands()
        {
            var source = ReadSharedSource("Infrastructure", "CommandDispatcher.cs");

            Assert.Contains("\"create_linear_dimension\"", source, StringComparison.Ordinal);
            Assert.Contains("\"create_aligned_dimension\"", source, StringComparison.Ordinal);
            Assert.Contains("\"create_radial_dimension\"", source, StringComparison.Ordinal);
            Assert.Contains("\"create_diameter_dimension\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void DimensionFactoryDisposesAllocatedEntitiesUntilCallerOwnsThem()
        {
            var source = ReadSharedSource("Dimensions", "DimensionEntityFactory.cs");

            Assert.Contains("Caller owns the dimension after a successful factory return.", source, StringComparison.Ordinal);
            Assert.Contains("private static void DisposeIfOwned(Dimension entity, bool ownsEntity)", source, StringComparison.Ordinal);
            Assert.True(CountOccurrences(source, "var ownsEntity = false;") >= 4);
            Assert.True(CountOccurrences(source, "ownsEntity = true;") >= 4);
            Assert.True(CountOccurrences(source, "return TransferToCaller(created, out entity, ref ownsEntity);") >= 4);
            Assert.True(CountOccurrences(source, "DisposeIfOwned(created, ownsEntity);") >= 4);
        }

        [Fact]
        public void DimensionHandlerSupportTransfersOwnershipOnlyAfterAppend()
        {
            var source = ReadSharedSource("Handlers", "Dimensions", "DimensionHandlerSupport.cs");

            Assert.Contains("var ownsEntity = true;", source, StringComparison.Ordinal);
            Assert.Contains("entity.Dispose();", source, StringComparison.Ordinal);
            Assert.Contains("AutoCAD transaction owns the dimension after AddNewlyCreatedDBObject.", source, StringComparison.Ordinal);

            var appendIndex = source.IndexOf("CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity)", StringComparison.Ordinal);
            var transferIndex = source.IndexOf("transferOwnership();", StringComparison.Ordinal);

            Assert.True(appendIndex >= 0, "DimensionHandlerSupport must append via CadPrimitiveWriter.AppendToCurrentSpace.");
            Assert.True(transferIndex > appendIndex, "DimensionHandlerSupport must transfer ownership only after append succeeds.");
        }

        [Fact]
        public void DimensionStyleResolverRejectsPresentBlankStyleNames()
        {
            var source = ReadSharedSource("Dimensions", "DimensionStyleResolver.cs");

            Assert.Contains("bool hasStyleName", source, StringComparison.Ordinal);
            Assert.Contains("style_name must be a non-empty dimension style name", source, StringComparison.Ordinal);
            Assert.Contains("dimension style not found", source, StringComparison.Ordinal);
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

        private static string ReadSharedSource(params string[] parts)
        {
            var pathParts = new string[parts.Length + 7];
            pathParts[0] = AppContext.BaseDirectory;
            pathParts[1] = "..";
            pathParts[2] = "..";
            pathParts[3] = "..";
            pathParts[4] = "..";
            pathParts[5] = "..";
            pathParts[6] = Path.Combine("src", "shared");
            Array.Copy(parts, 0, pathParts, 7, parts.Length);

            return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts)));
        }
    }
}
