using System;
using System.Collections.Generic;
using System.Linq;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    /// <summary>Bounded in-memory UI history. No raw params, code bodies, JSON results or disk writer.</summary>
    public sealed class ActivityHistory
    {
        public const int DefaultCapacity = 1000;
        private readonly object _gate = new object();
        private readonly Queue<ActivityHistoryEntry> _entries = new Queue<ActivityHistoryEntry>();
        private readonly int _capacity;
        private long _lastIndex;
        private long _version;

        public ActivityHistory(int capacity = DefaultCapacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public long Version { get { lock (_gate) return _version; } }

        public void Record(McpToastViewModel outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            lock (_gate)
            {
                _entries.Enqueue(new ActivityHistoryEntry(++_lastIndex, outcome));
                while (_entries.Count > _capacity) _entries.Dequeue();
                _version++;
            }
        }

        public ActivityHistoryEntry[] Snapshot()
        {
            lock (_gate) return _entries.Reverse().ToArray();
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _lastIndex = 0;
                _version++;
            }
        }
    }

    public sealed class ActivityHistoryEntry
    {
        internal ActivityHistoryEntry(long index, McpToastViewModel outcome)
        {
            Index = index;
            Timestamp = DateTimeOffset.Now;
            ToolName = Clip(outcome.Title, 160);
            Summary = Clip(outcome.Body, 240);
            Success = outcome.Success;
            Kind = outcome.Kind;
            DurationMs = Math.Max(0, outcome.DurationMs);
            ImagePath = outcome.Success && ToastContentBuilder.IsSafeImagePath(outcome.ThumbnailPath)
                ? outcome.ThumbnailPath : null;
        }

        public long Index { get; }
        public DateTimeOffset Timestamp { get; }
        public string TimeLabel => Timestamp.ToString("HH:mm:ss");
        public string ToolName { get; }
        public string Summary { get; }
        public bool Success { get; }
        public ToolActivityKind Kind { get; }
        public string KindLabel => Kind == ToolActivityKind.Read ? "Read" : "Write";
        public string StatusLabel => Success ? "✓" : "✗";
        public long DurationMs { get; }
        public string ImagePath { get; }

        private static string Clip(string text, int max) => string.IsNullOrEmpty(text)
            ? string.Empty : text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
