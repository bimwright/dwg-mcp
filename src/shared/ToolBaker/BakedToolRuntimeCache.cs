using System;
using System.Collections.Concurrent;

namespace Bimwright.Dwg.Plugin.ToolBaker
{
    public sealed class BakedToolRuntimeCache
    {
        private readonly ConcurrentDictionary<string, BakedToolRecord> _records = new ConcurrentDictionary<string, BakedToolRecord>(StringComparer.Ordinal);

        public void AddOrUpdate(BakedToolRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Name))
            {
                return;
            }

            _records[record.Name] = record;
        }

        public bool TryGet(string name, out BakedToolRecord record)
            => _records.TryGetValue(name, out record);
    }
}
