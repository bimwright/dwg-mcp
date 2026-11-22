using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Bimwright.Dwg.Plugin.Views.Update;

internal sealed class ReleaseNoticeCoordinator
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(1) };
    private readonly string _currentText;
    private readonly Version _currentVersion;
    private readonly ReleaseMetadataClient _metadata;
    private CancellationTokenSource _cancellation;
    private Task<StableRelease> _check;
    private ReleaseNoticeWindow _window;
    private string _shownVersion;

    internal ReleaseNoticeCoordinator()
    {
        var assembly = typeof(ReleaseNoticeCoordinator).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _currentText = informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3);
        var version = _currentText?.Split('-')[0];
        if (version != null && StableRelease.TryVersion(version, out _currentVersion))
            _metadata = new ReleaseMetadataClient(version);
        _timer.Tick += (_, __) => StartCheck();
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_timer.IsEnabled || _metadata == null) return;
            _timer.Start();
            StartCheck();
            return;
        }

        _timer.Stop();
        if (_check != null)
        {
            var cancellation = _cancellation;
            cancellation.Cancel();
            _ = _check.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
            _check = null;
            _cancellation = null;
        }
        try { _window?.Close(); }
        catch (Exception ex) { PluginLog.Debug("Release notice close failed: " + ex.GetType().Name); }
    }

    private void StartCheck()
    {
        if (_check != null) return;
        _cancellation = new CancellationTokenSource();
        var ct = _cancellation.Token;
        _check = Task.Run(async () =>
        {
            if (!PluginSettings.LoadReleaseNotifications()) return null;
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(PluginSettings.DefaultFilePath), "release-check.txt");
                var release = await ReleaseNoticeCheck.CheckAsync(path, _metadata.GetLatestAsync, ct).ConfigureAwait(false);
                return PluginSettings.LoadReleaseNotifications() ? release : null;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                PluginLog.Debug("Release check unavailable: " + ex.GetType().Name);
                return null;
            }
        });
    }

    // AutoCAD's Idle event is on the UI thread; network and cache access stay on the worker.
    internal void OnIdle()
    {
        if (_check?.IsCompleted != true) return;
        if (Application.MainWindow == null || Application.MainWindow.Handle == IntPtr.Zero) return;

        var release = _check.Status == TaskStatus.RanToCompletion ? _check.Result : null;
        _cancellation.Dispose();
        _cancellation = null;
        _check = null;
        if (release == null || release.VersionText == _shownVersion || release.Version < _currentVersion
            || (release.Version == _currentVersion && !_currentText.Contains("-"))) return;

        try
        {
            _window?.Close();
            var window = new ReleaseNoticeWindow(_currentText, release);
            _window = window;
            window.Closed += (_, __) => _window = null;
            Application.ShowModelessWindow(window);
            _shownVersion = release.VersionText;
        }
        catch (Exception ex)
        {
            try { _window?.Close(); } catch { }
            _window = null;
            PluginLog.Debug("Release notice unavailable: " + ex.GetType().Name);
        }
    }
}
