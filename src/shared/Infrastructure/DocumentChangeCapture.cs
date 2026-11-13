using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    internal sealed class DocumentChangeCapture
    {
        [ThreadStatic] private static DocumentChangeCapture _current;
        private const int EnvelopeReserve = 1024;
        private readonly JArray _deltas = new JArray();
        private int _bytes;
        private int _observed;
        private int _nextStep;
        private int? _stepIndex;
        private bool _batch;
        private string _partialReason;

        internal JObject Result { get; private set; }

        internal CommandResult Run(Document doc, string command, Func<CommandResult> execute)
        {
            var key = DrawingKey(doc);
            if (key == null)
            {
                Result = Envelope("identity_unavailable", null, "none");
                return execute();
            }
            if (command != "batch_execute" && !ChangeCaptureContract.Supports(command))
            {
                var unsupported = execute();
                Result = Envelope(unsupported.Ok ? "unsupported" : "outcome_unknown", key, "none");
                return unsupported;
            }
            var previous = _current;
            _batch = command == "batch_execute";
            _current = this;
            CommandResult result;
            try { result = execute(); }
            finally { _current = previous; }

            if (!string.Equals(key, DrawingKey(doc), StringComparison.Ordinal))
                Result = Envelope("identity_unavailable", null, "none");
            else if (command == "batch_execute" && (result.Result as JObject)?.Value<bool?>("rolled_back") == true)
                Result = Envelope("no_committed_changes", key, "none");
            else if (!result.Ok)
                Result = Envelope("outcome_unknown", key, "none");
            else if (_deltas.Count > 0)
            {
                Result = Envelope("captured", key, _partialReason == null ? "full" : "partial");
                Result["deltas"] = _deltas;
                Result["observed"] = _observed;
                if (_partialReason != null) Result["coverage_reason"] = _partialReason;
            }
            else if (_partialReason != null)
            {
                Result = Envelope("unsupported", key, "none");
                Result["coverage_reason"] = _partialReason;
                Result["observed"] = _observed;
            }
            else Result = Envelope("no_committed_changes", key, "none");
            return result;
        }

        internal static CommandResult ExecuteHandler(string command, Func<CommandResult> execute)
        {
            var capture = _current;
            if (capture == null || command == "batch_execute") return execute();
            if (capture._batch) capture._stepIndex = capture._nextStep++;
            var count = capture._deltas.Count;
            var observed = capture._observed;
            var bytes = capture._bytes;
            var reason = capture._partialReason;
            if (ChangeCaptureContract.Tracks(command) && !ChangeCaptureContract.Supports(command))
                capture._partialReason ??= "unsupported_command";
            var completed = false;
            try
            {
                var result = execute();
                completed = result.Ok;
                return result;
            }
            finally
            {
                if (!completed)
                {
                    while (capture._deltas.Count > count) capture._deltas.RemoveAt(capture._deltas.Count - 1);
                    capture._observed = observed;
                    capture._bytes = bytes;
                    capture._partialReason = reason ?? "handler_outcome_unknown";
                }
                capture._stepIndex = null;
            }
        }

        internal static JObject Before(Entity entity, Transaction tx)
            => _current?.SnapshotIfRoom(entity, tx, false);

        internal static void Created(Entity entity, Transaction tx)
        {
            var capture = _current;
            if (capture == null) return;
            capture.Add(entity.Handle.ToString(), "create", null, capture.SnapshotIfRoom(entity, tx, true));
        }

        internal static void Moved(Entity entity, Transaction tx, JObject before)
        {
            var capture = _current;
            if (capture == null) return;
            capture.Add(entity.Handle.ToString(), "move", before, capture.SnapshotIfRoom(entity, tx, false));
        }

        internal static void Erased(string handle, JObject before)
            => _current?.Add(handle, "erase", before, new JObject { ["erased"] = true });

        private JObject SnapshotIfRoom(Entity entity, Transaction tx, bool primitive)
        {
            if (_deltas.Count >= ChangeCaptureContract.MaxDeltas || _bytes >= ChangeCaptureContract.MaxCaptureBytes - EnvelopeReserve)
                return null;
            try
            {
                var owner = tx.GetObject(entity.OwnerId, OpenMode.ForRead) as BlockTableRecord;
                var state = new JObject
                {
                    ["type"] = entity.GetType().Name,
                    ["layer"] = entity.Layer,
                    ["owner_handle"] = entity.OwnerId.Handle.ToString(),
                    ["space"] = owner?.IsLayout == true
                        ? (owner.Name == BlockTableRecord.ModelSpace ? "model" : "paper") : "block"
                };
                if (primitive && entity is Line line)
                {
                    state["start"] = Point(line.StartPoint);
                    state["end"] = Point(line.EndPoint);
                }
                else if (primitive && entity is Circle circle)
                {
                    state["center"] = Point(circle.Center);
                    state["radius"] = circle.Radius;
                }
                else
                {
                    try
                    {
                        var bounds = entity.GeometricExtents;
                        state["extents"] = new JObject { ["min"] = Point(bounds.MinPoint), ["max"] = Point(bounds.MaxPoint) };
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { _partialReason ??= "extents_unavailable"; }
                }
                return state;
            }
            catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception || ex is InvalidOperationException)
            {
                _partialReason ??= "snapshot_unavailable";
                return null;
            }
        }

        private void Add(string handle, string operation, JObject before, JObject after)
        {
            _observed++;
            if (_deltas.Count >= ChangeCaptureContract.MaxDeltas || _bytes >= ChangeCaptureContract.MaxCaptureBytes - EnvelopeReserve)
            {
                _partialReason ??= "capture_limit";
                return;
            }
            if (before == null && after == null) return;
            var delta = new JObject { ["handle"] = handle, ["operation"] = operation, ["before"] = before, ["after"] = after };
            if (_stepIndex.HasValue) delta["step_index"] = _stepIndex.Value;
            var bytes = Encoding.UTF8.GetByteCount(delta.ToString(Formatting.None)) + 1;
            if (_bytes + bytes > ChangeCaptureContract.MaxCaptureBytes - EnvelopeReserve)
            {
                _partialReason ??= "capture_limit";
                _bytes = ChangeCaptureContract.MaxCaptureBytes;
                return;
            }
            _bytes += bytes;
            _deltas.Add(delta);
        }

        private static JObject Envelope(string status, string key, string coverage) => new JObject
        {
            ["version"] = ChangeCaptureContract.Version, ["status"] = status, ["drawing_key"] = key,
            ["coverage"] = coverage, ["observed"] = 0
        };

        private static JObject Point(Point3d point) => new JObject { ["x"] = point.X, ["y"] = point.Y, ["z"] = point.Z };

        private static string DrawingKey(Document doc)
        {
            try
            {
                var path = doc.Name;
                if (!doc.IsNamedDrawing || !DrawingSessionContract.IsAbsolutePath(path) || !File.Exists(path)
                    || !Guid.TryParse(doc.Database.FingerprintGuid, out var fingerprint) || fingerprint == Guid.Empty) return null;
                var normalized = Path.GetFullPath(path).ToUpperInvariant();
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes($"v1\n{normalized}\n{fingerprint:N}")))
                        .Replace("-", "").ToLowerInvariant();
            }
            catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception || ex is IOException || ex is ArgumentException
                || ex is UnauthorizedAccessException || ex is NotSupportedException)
            { return null; }
        }
    }
}
