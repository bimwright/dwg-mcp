using System;
using System.IO;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BlockSourceContractTests
    {
        [Fact]
        public void BlockDefinitionResolverOnlyImportsAbsoluteExistingDwgPaths()
        {
            var source = ReadSharedSource("Blocks", "BlockDefinitionResolver.cs");

            Assert.Contains("IsFullyQualifiedPath(blockPath)", source, StringComparison.Ordinal);
            Assert.Contains("private static bool IsFullyQualifiedPath(string path)", source, StringComparison.Ordinal);
            Assert.Contains("Path.GetFullPath(path)", source, StringComparison.Ordinal);
            Assert.Contains("StringComparison.OrdinalIgnoreCase", source, StringComparison.Ordinal);
            Assert.DoesNotContain("if (!Path.IsPathRooted(blockPath))", source, StringComparison.Ordinal);
            Assert.Contains("File.Exists(blockPath)", source, StringComparison.Ordinal);
            Assert.Contains("ReadDwgFile", source, StringComparison.Ordinal);

            var absolutePathGuardIndex = source.IndexOf("IsFullyQualifiedPath(blockPath)", StringComparison.Ordinal);
            var existsGuardIndex = source.IndexOf("File.Exists(blockPath)", StringComparison.Ordinal);
            var importIndex = source.IndexOf("ReadDwgFile", StringComparison.Ordinal);

            Assert.True(absolutePathGuardIndex >= 0, "BlockDefinitionResolver must reject relative block_path values before DWG import.");
            Assert.True(existsGuardIndex >= 0, "BlockDefinitionResolver must reject missing block_path values before DWG import.");
            Assert.True(importIndex > absolutePathGuardIndex, "BlockDefinitionResolver must check absolute paths before ReadDwgFile.");
            Assert.True(importIndex > existsGuardIndex, "BlockDefinitionResolver must check file existence before ReadDwgFile.");
        }

        [Fact]
        public void BlockAttributeServiceMatchesTagsCaseInsensitivelyAndReportsStrictMisses()
        {
            var source = ReadSharedSource("Blocks", "BlockAttributeService.cs");

            Assert.Contains("StringComparer.OrdinalIgnoreCase", source, StringComparison.Ordinal);
            Assert.Contains("strictTags", source, StringComparison.Ordinal);
            Assert.Contains("missing_tags", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ExplodeBlockHandlerKeepsCleanupOwnershipUntilTransactionOwnsEntity()
        {
            var source = ReadSharedSource("Handlers", "Blocks", "BlockHandlers.cs");

            var appendIndex = source.IndexOf("owner.AppendEntity(entity);", StringComparison.Ordinal);
            var addNewIndex = source.IndexOf("tx.AddNewlyCreatedDBObject(entity, true);", StringComparison.Ordinal);
            var cleanupReleaseIndex = source.IndexOf("unappended.Remove(entity);", StringComparison.Ordinal);
            var handleIndex = source.IndexOf("createdHandles.Add(entity.Handle.ToString());", StringComparison.Ordinal);

            Assert.True(appendIndex >= 0, "ExplodeBlockHandler must append exploded entities to the source owner.");
            Assert.True(addNewIndex > appendIndex, "ExplodeBlockHandler must register the appended entity with the transaction after append.");
            Assert.True(cleanupReleaseIndex > addNewIndex, "ExplodeBlockHandler must release local cleanup ownership only after AddNewlyCreatedDBObject succeeds.");
            Assert.True(handleIndex > cleanupReleaseIndex, "ExplodeBlockHandler should record created handles only after transaction ownership transfer.");
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
