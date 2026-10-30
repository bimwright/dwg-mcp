using System;
using System.IO;
using Bimwright.Dwg.Plugin;

namespace Bimwright.Dwg.Tests
{
    public class OutputArtifactTestScope : IDisposable
    {
        protected readonly string ArtifactRoot = Path.Combine(Path.GetTempPath(), "dwg-output-tests-" + Guid.NewGuid().ToString("N"));
        private readonly string _previous;
        public OutputArtifactTestScope() { _previous = OutputArtifactStore.RootOverride; OutputArtifactStore.RootOverride = ArtifactRoot; }
        public void Dispose()
        {
            OutputArtifactStore.RootOverride = _previous;
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(ArtifactRoot).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe test cleanup path");
            if (Directory.Exists(ArtifactRoot)) Directory.Delete(ArtifactRoot, true);
        }
    }
}
