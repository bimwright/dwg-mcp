using System;
using System.Collections.Generic;
using System.IO;

namespace Bimwright.Dwg.Server.Logging
{
    public sealed class McpLogger
    {
        private readonly Queue<string> _ring = new Queue<string>();
        private readonly int _ringLimit;

        public McpLogger(string path, int ringLimit = 200)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            _ringLimit = ringLimit <= 0 ? 200 : ringLimit;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");
        }

        public string Path { get; }
        public IReadOnlyCollection<string> Recent => _ring.ToArray();

        public void Append(string message)
        {
            var line = DateTimeOffset.UtcNow.ToString("o") + " " + (message ?? string.Empty);
            File.AppendAllText(Path, line + Environment.NewLine);
            _ring.Enqueue(line);
            while (_ring.Count > _ringLimit)
            {
                _ring.Dequeue();
            }
        }
    }
}
