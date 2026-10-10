using System;
using System.IO;
using Bimwright.Dwg.Server.Logging;

namespace Bimwright.Dwg.Server.Memory
{
    public sealed class JournalLogger
    {
        private readonly CallLogWriter _writer;

        public JournalLogger(string root, Func<bool> enabled = null)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Journal root is required.", nameof(root));
            _writer = new CallLogWriter(Path.Combine(root, "journal.jsonl"), enabled);
        }

        public void Append(JournalEntry entry)
        {
            _writer.Append(entry ?? new JournalEntry());
        }
    }
}
