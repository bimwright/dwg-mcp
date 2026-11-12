using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Bimwright.Dwg.Plugin.Reports.Navigation;
using Bimwright.Dwg.Plugin.Reports.Presentation;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Bimwright.Dwg.Plugin.Reports.Acad
{
    /// <summary>
    /// Link clicks run on AutoCAD's next Idle (application context, no command active), never through the MCP
    /// command queue: no History entry and no target check. Only the newest click waits; an older one is dropped.
    /// </summary>
    internal sealed class AcadNavigationService : INavigationService, IDisposable
    {
        private readonly LatestRequestSlot<ReportNavigationRequest> _pending = new LatestRequestSlot<ReportNavigationRequest>();
        private long _nextTraceId;
        private bool _hooked, _disposed;
        // The request whose drawing was just made active; its next run must find that drawing active.
        private ReportNavigationRequest _activated;

        public event EventHandler<ReportNavigationRequest> Started;
        public event EventHandler<ReportNavigationResult> Completed;

        /// <summary>Main thread (the Reports window).</summary>
        public void Navigate(ReportNavigationRequest request)
        {
            if (request == null || _disposed) return;
            request.TraceId = Interlocked.Increment(ref _nextTraceId);
            request.QueuedTimestamp = Stopwatch.GetTimestamp();
            _pending.Put(request);
            Started?.Invoke(this, request);
            Hook();
        }

        private void OnIdle(object sender, EventArgs e)
        {
            Unhook();
            var request = _pending.Take();
            if (request == null) return;
            var queueMs = (Stopwatch.GetTimestamp() - request.QueuedTimestamp) * 1000.0 / Stopwatch.Frequency;
            var clock = Stopwatch.StartNew();
            ReportNavigationResult result;
            try
            {
                result = AcadEntityNavigator.Navigate(request, ReferenceEquals(request, _activated), out var activationRequested);
                if (activationRequested)
                {
                    // Switching drawings completes before the next Idle; a newer click meanwhile wins.
                    _activated = request;
                    _pending.Put(_pending.Take() ?? request);
                    Hook();
                    return;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug("Reports: navigation failed: " + ex);
                result = new ReportNavigationResult { Outcome = ReportNavigationOutcome.Failed };
            }
            _activated = null;
            PluginLog.Debug("Reports navigation " + request.TraceId + ": queue_ms=" + queueMs.ToString("F1", CultureInfo.InvariantCulture)
                + "; execute_ms=" + clock.Elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + "; outcome=" + result.Outcome);
            Completed?.Invoke(this, result);
        }

        private void Hook()
        {
            if (_hooked || _disposed) return;
            Application.Idle += OnIdle;
            _hooked = true;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            Application.Idle -= OnIdle;
            _hooked = false;
        }

        public void Dispose()
        {
            _disposed = true;
            try { Unhook(); } catch (Exception) { } // AutoCAD is shutting down.
            _pending.Take();
        }
    }
}
