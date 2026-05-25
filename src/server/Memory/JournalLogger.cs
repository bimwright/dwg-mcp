using System;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Memory
{
    public sealed class JournalLogger
    {
        private readonly string _path;

        public JournalLogger(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Journal root is required.", nameof(root));
            Directory.CreateDirectory(root);
            _path = Path.Combine(root, "journal.jsonl");
        }

        public void Append(JournalEntry entry)
        {
            File.AppendAllText(_path, JsonConvert.SerializeObject(entry ?? new JournalEntry(), Formatting.None) + Environment.NewLine);
        }
    }
}
