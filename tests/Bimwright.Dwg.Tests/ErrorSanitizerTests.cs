using Bimwright.Dwg.Plugin;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ErrorSanitizerTests
    {
        [Fact]
        public void Sanitize_MasksPathsSecretsAndStackFrames()
        {
            var raw = "failed at C:\\Users\\Admin\\secret\\file.dwg auth_token=abc123\r\n   at Namespace.Type.Method()";

            var sanitized = ErrorSanitizer.Sanitize(raw);

            Assert.DoesNotContain("C:\\Users\\Admin", sanitized);
            Assert.DoesNotContain("abc123", sanitized);
            Assert.DoesNotContain("Namespace.Type.Method", sanitized);
            Assert.Contains("<path>", sanitized);
            Assert.Contains("<secret>", sanitized);
        }

        [Fact]
        public void Sanitize_MasksWindowsPathsWithSpaces()
        {
            var sanitized = ErrorSanitizer.Sanitize(
                @"AutoCAD refs missing at C:\Program Files\Autodesk\AutoCAD 2024\accoremgd.dll");

            Assert.DoesNotContain("Program Files", sanitized);
            Assert.DoesNotContain("AutoCAD 2024", sanitized);
            Assert.Contains("<path>", sanitized);
        }

        [Fact]
        public void Sanitize_MasksUncPathsWithSpaces()
        {
            var sanitized = ErrorSanitizer.Sanitize(
                @"failed at \\server\Shared Folder\Project A\file.dwg");

            Assert.DoesNotContain("Shared Folder", sanitized);
            Assert.DoesNotContain("Project A", sanitized);
            Assert.Contains("<path>", sanitized);
        }
    }
}
