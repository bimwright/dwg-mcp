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

            Assert.Contains("Path.IsPathRooted(blockPath)", source, StringComparison.Ordinal);
            Assert.Contains("File.Exists(blockPath)", source, StringComparison.Ordinal);
            Assert.Contains("ReadDwgFile", source, StringComparison.Ordinal);

            var absolutePathGuardIndex = source.IndexOf("Path.IsPathRooted(blockPath)", StringComparison.Ordinal);
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
