using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin.Localization;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    /// <summary>Bounded sanitized in-memory UI history. No code bodies or disk writer.</summary>
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

        public void Record(McpToastViewModel outcome, JToken parameters = null, string resultJson = null, string error = null, string description = null)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            lock (_gate)
            {
                _entries.Enqueue(new ActivityHistoryEntry(++_lastIndex, outcome, parameters, resultJson, error, description));
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
        internal ActivityHistoryEntry(long index, McpToastViewModel outcome, JToken parameters, string resultJson, string error, string description)
        {
            Index = index;
            Timestamp = DateTimeOffset.Now;
            var code = ActivityHistoryDetails.MayEchoCode(outcome.CommandName, parameters);
            var body = code ? L.T(outcome.Success ? "history.codeCompleted" : "history.codeFailed") : ActivityHistoryDetails.Text(outcome.Body);
            ToolName = Clip(ActivityHistoryDetails.Text(outcome.Title), 160);
            Summary = Clip(body, 240);
            Success = outcome.Success;
            Kind = outcome.Kind;
            DurationMs = Math.Max(0, outcome.DurationMs);
            What = ActivityHistoryDetails.Text(ToolName + " · " + KindLabel + " · " + StatusLabel + " · "
                + L.T("history.duration", ("duration", DurationMs)) + "\n\n" + body + "\n\n" + description);
            Input = ActivityHistoryDetails.Json(parameters);
            Output = code ? ActivityHistoryDetails.CodeOutputOmitted : outcome.Success
                ? ActivityHistoryDetails.Result(resultJson) : ActivityHistoryDetails.Text(error ?? outcome.Summary);
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
        public string KindLabel => L.T(Kind == ToolActivityKind.Read ? "history.read" : "history.write");
        public string StatusLabel => Success ? "✓" : "✗";
        public long DurationMs { get; }
        public string ImagePath { get; }
        public string What { get; }
        public string Input { get; }
        public string Output { get; }

        private static string Clip(string text, int max) => string.IsNullOrEmpty(text)
            ? string.Empty : text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
