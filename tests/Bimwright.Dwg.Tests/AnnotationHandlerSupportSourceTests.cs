using System;
using System.IO;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class AnnotationHandlerSupportSourceTests
    {
        [Fact]
        public void AnnotationAppendSupportDisposesEntityUntilDatabaseOwnsIt()
        {
            var source = ReadAnnotationHandlerSupportSource();

            Assert.Contains("var ownsEntity = true;", source, StringComparison.Ordinal);
            Assert.Contains("finally", source, StringComparison.Ordinal);
            Assert.Contains("if (ownsEntity)", source, StringComparison.Ordinal);
            Assert.Contains("entity.Dispose();", source, StringComparison.Ordinal);
            Assert.Contains("AutoCAD transaction owns the entity after AddNewlyCreatedDBObject.", source, StringComparison.Ordinal);
            Assert.Contains("() => ownsEntity = false", source, StringComparison.Ordinal);

            var appendIndex = source.IndexOf("CadPrimitiveWriter.AppendToCurrentSpace(db, tx, entity)", StringComparison.Ordinal);
            var transferIndex = source.IndexOf("transferOwnership();", StringComparison.Ordinal);

            Assert.True(appendIndex >= 0, "AnnotationHandlerSupport must append via CadPrimitiveWriter.AppendToCurrentSpace.");
            Assert.True(transferIndex > appendIndex, "AnnotationHandlerSupport must transfer ownership only after append succeeds.");
        }

        private static string ReadAnnotationHandlerSupportSource()
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
                "Annotation",
                "AnnotationHandlerSupport.cs"));

            return File.ReadAllText(path);
        }
    }
}
