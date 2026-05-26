using System;
using System.IO;
using Bimwright.Dwg.Plugin.Export;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ExportPathPolicyTests
    {
        [Fact]
        public void Validate_RejectsRelativePath()
        {
            var result = ExportPathPolicy.ValidateAndNormalize(
                "relative/path/file.dxf",
                ".dxf",
                overwriteExisting: true,
                allowRepoOutput: true,
                out var error);

            Assert.Null(result);
            Assert.Contains("absolute path", error);
        }

        [Fact]
        public void Validate_RejectsMismatchedExtension()
        {
            string tempDir = Path.GetTempPath();
            string path = Path.Combine(tempDir, "file.txt");

            var result = ExportPathPolicy.ValidateAndNormalize(
                path,
                ".dxf",
                overwriteExisting: true,
                allowRepoOutput: true,
                out var error);

            Assert.Null(result);
            Assert.Contains("extension must be .dxf", error);
        }

        [Fact]
        public void Validate_ImageAllowsPngJpgJpegBmp()
        {
            string tempDir = Path.GetTempPath();
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp" };

            foreach (var ext in extensions)
            {
                string path = Path.Combine(tempDir, "file" + ext);
                var result = ExportPathPolicy.ValidateAndNormalize(
                    path,
                    ".image",
                    overwriteExisting: true,
                    allowRepoOutput: true,
                    out var error);

                Assert.NotNull(result);
                Assert.Null(error);
            }

            string badPath = Path.Combine(tempDir, "file.gif");
            var badResult = ExportPathPolicy.ValidateAndNormalize(
                badPath,
                ".image",
                overwriteExisting: true,
                allowRepoOutput: true,
                out var badError);

            Assert.Null(badResult);
            Assert.Contains("extension must be .png, .jpg, .jpeg, or .bmp", badError);
        }

        [Fact]
        public void Validate_RejectsExistingFileWithoutOverwrite()
        {
            string path = Path.GetTempFileName();
            try
            {
                var result = ExportPathPolicy.ValidateAndNormalize(
                    path,
                    Path.GetExtension(path),
                    overwriteExisting: false,
                    allowRepoOutput: true,
                    out var error);

                Assert.Null(result);
                Assert.Contains("already exists", error);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Validate_AllowsExistingFileWithOverwrite()
        {
            string path = Path.GetTempFileName();
            try
            {
                var result = ExportPathPolicy.ValidateAndNormalize(
                    path,
                    Path.GetExtension(path),
                    overwriteExisting: true,
                    allowRepoOutput: true,
                    out var error);

                Assert.NotNull(result);
                Assert.Null(error);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Validate_RejectsRepoRootOutputByDefault()
        {
            string repoDir = AppContext.BaseDirectory;
            // Let's walk up to find repo root
            while (!string.IsNullOrEmpty(repoDir))
            {
                if (Directory.Exists(Path.Combine(repoDir, ".git")) ||
                    File.Exists(Path.Combine(repoDir, "Bimwright.Dwg.sln")))
                {
                    break;
                }
                string parent = Path.GetDirectoryName(repoDir);
                if (parent == repoDir) break;
                repoDir = parent;
            }

            Assert.True(Directory.Exists(repoDir), "Repo root must exist for testing.");

            string path = Path.Combine(repoDir, "output.dxf");

            var result = ExportPathPolicy.ValidateAndNormalize(
                path,
                ".dxf",
                overwriteExisting: true,
                allowRepoOutput: false,
                out var error);

            Assert.Null(result);
            Assert.Contains("repository root is rejected", error);
        }

        [Fact]
        public void Validate_AllowsRepoRootOutputIfExplicit()
        {
            string repoDir = AppContext.BaseDirectory;
            // Let's walk up to find repo root
            while (!string.IsNullOrEmpty(repoDir))
            {
                if (Directory.Exists(Path.Combine(repoDir, ".git")) ||
                    File.Exists(Path.Combine(repoDir, "Bimwright.Dwg.sln")))
                {
                    break;
                }
                string parent = Path.GetDirectoryName(repoDir);
                if (parent == repoDir) break;
                repoDir = parent;
            }

            string path = Path.Combine(repoDir, "output.dxf");

            var result = ExportPathPolicy.ValidateAndNormalize(
                path,
                ".dxf",
                overwriteExisting: true,
                allowRepoOutput: true,
                out var error);

            Assert.NotNull(result);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("C:foo.dxf")]
        [InlineData("\\foo.dxf")]
        public void Validate_RejectsPathRootedButNotFullyQualified(string path)
        {
            var result = ExportPathPolicy.ValidateAndNormalize(
                path,
                ".dxf",
                overwriteExisting: true,
                allowRepoOutput: true,
                out var error);

            Assert.Null(result);
            Assert.Contains("fully qualified", error);
        }
    }
}
