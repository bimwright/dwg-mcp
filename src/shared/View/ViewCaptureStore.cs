using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.View
{
    // DTO-only, bounded session history. The document-lock executor serializes access.
    internal sealed class ViewCaptureStore
    {
        private readonly int capacity;
        private readonly Func<DateTimeOffset> now;
        private readonly Dictionary<string, (JObject Capture, DateTimeOffset Expires)> entries =
            new Dictionary<string, (JObject, DateTimeOffset)>(StringComparer.Ordinal);
        private readonly Queue<string> order = new Queue<string>();

        internal ViewCaptureStore(int capacity = 64, Func<DateTimeOffset> now = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        internal void Remember(JObject capture)
        {
            string id = (string)capture["capture_id"];
            if (entries.ContainsKey(id)) throw new InvalidOperationException("Duplicate capture ID.");
            while (order.Count >= capacity) entries.Remove(order.Dequeue());
            entries.Add(id, ((JObject)capture.DeepClone(), now().AddMinutes(30)));
            order.Enqueue(id);
        }

        internal JObject Get(string id)
        {
            if (id == null || !entries.TryGetValue(id, out var entry) || entry.Expires <= now())
                throw new InvalidOperationException("Unknown or expired source_capture_id; take a new capture. History lasts 30 minutes, at most 64 captures, in this AutoCAD process.");
            return (JObject)entry.Capture.DeepClone();
        }
    }
}
