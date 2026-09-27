using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Bimwright.Dwg.Plugin;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public sealed class McpToastHost
    {
        private readonly object _lock = new object();
        private Thread _thread;
        private volatile Dispatcher _dispatcher;
        private volatile McpToastManager _manager;
        private volatile Dispatcher _hostDispatcher;
        private IntPtr _ownerHandle;
        private bool _shutdownRequested;
        private bool _usesDedicatedThread;
        private bool _showBranding;
        private int _idleSeconds;
        private volatile string _instanceIdentity;
        private Func<bool> _frameUsable = () => true;
        private Application _toastApplication;

        public ActivityAggregator Aggregator { get; }

        /// <summary>Session wordmark. Default off. Not written to settings.</summary>
        public bool ShowBranding => _showBranding;

        public McpToastHost()
        {
            _idleSeconds = PluginSettings.LoadToastIdleSeconds();
            Aggregator = new ActivityAggregator(() => _idleSeconds);
        }

        public void SetShowBranding(bool show)
        {
            _showBranding = show;
            PostToManager(manager => manager.ApplyShowBranding());
        }

        public void SetIdleSeconds(int seconds)
        {
            _idleSeconds = PluginSettings.NormalizeToastIdleSeconds(seconds);
        }

        /// <summary>
        /// Footer label such as "AutoCAD 2024". Read when each card is created.
        /// </summary>
        public void SetInstanceIdentity(string identity)
        {
            _instanceIdentity = string.IsNullOrWhiteSpace(identity) ? null : identity;
        }

        public void SetFrameUsableProvider(Func<bool> provider)
        {
            if (provider != null)
                _frameUsable = provider;
        }

        public void SetHostDispatcher(Dispatcher dispatcher)
        {
            if (dispatcher == null)
                return;
            _hostDispatcher = dispatcher;
        }

        public void SetOwnerHandle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;
            _ownerHandle = hwnd;
            PostToManager(manager => manager.SetOwnerHandle(hwnd));
        }

        public void EnsureStarted()
        {
            lock (_lock)
            {
                if (_dispatcher != null || _shutdownRequested)
                    return;

                // AutoCAD (and most WPF hosts) already own Application.Current in this
                // AppDomain. Creating Window on a second STA thread in that case is
                // unstable — use the host dispatcher.
                var hostDispatcher = _hostDispatcher ?? Application.Current?.Dispatcher;
                if (hostDispatcher != null)
                {
                    _dispatcher = hostDispatcher;
                    _manager = CreateManager(_dispatcher);
                    if (_ownerHandle != IntPtr.Zero)
                        _manager.SetOwnerHandle(_ownerHandle);
                    _usesDedicatedThread = false;
                    PluginLog.Debug("McpToastHost: using host WPF dispatcher");
                    return;
                }

                var started = new ManualResetEventSlim(false);
                Exception startupError = null;

                _thread = new Thread(() =>
                {
                    try
                    {
                        if (Application.Current == null)
                        {
                            _toastApplication = new Application
                            {
                                ShutdownMode = ShutdownMode.OnExplicitShutdown
                            };
                        }

                        _dispatcher = Dispatcher.CurrentDispatcher;
                        _manager = CreateManager(_dispatcher);
                        if (_ownerHandle != IntPtr.Zero)
                            _manager.SetOwnerHandle(_ownerHandle);
                    }
                    catch (Exception ex)
                    {
                        startupError = ex;
                        PluginLog.Debug("McpToastHost dedicated-thread startup failed: " + ex.Message);
                    }
                    finally
                    {
                        started.Set();
                    }

                    if (startupError != null)
                        return;

                    try
                    {
                        Dispatcher.Run();
                    }
                    finally
                    {
                        if (_toastApplication != null)
                        {
                            try { _toastApplication.Shutdown(); }
                            catch { }
                            _toastApplication = null;
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "Bimwright.Dwg.Toast"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                started.Wait(TimeSpan.FromSeconds(5));
                _usesDedicatedThread = startupError == null && _dispatcher != null;

                if (startupError != null)
                    _dispatcher = null;
                else
                    PluginLog.Debug("McpToastHost: using dedicated STA thread");
            }
        }

        internal void Post(Action<McpToastManager> action)
        {
            if (_shutdownRequested || action == null)
                return;

            EnsureStarted();
            PostToManager(action);
        }

        public void Post(Action action)
        {
            if (_shutdownRequested || action == null)
                return;

            EnsureStarted();
            var dispatcher = _dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// Dismiss all toasts. Prefer synchronous invoke during shutdown so Topmost
        /// windows are closed before AutoCAD tears down the dispatcher.
        /// </summary>
        public void DismissAll(bool synchronous = false)
        {
            if (_shutdownRequested && !synchronous)
                return;

            var dispatcher = _dispatcher;
            var manager = _manager;
            if (dispatcher == null || manager == null || dispatcher.HasShutdownStarted)
                return;

            void Run()
            {
                try { manager.DismissAllImmediate(); }
                catch (Exception ex) { PluginLog.Debug("McpToastHost dismiss failed: " + ex); }
            }

            if (synchronous)
            {
                try
                {
                    if (dispatcher.CheckAccess())
                        Run();
                    else
                        dispatcher.Invoke(Run, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    PluginLog.Debug("McpToastHost sync dismiss failed: " + ex.Message);
                }
                return;
            }

            PostToManager(m => m.DismissAllImmediate());
        }

        public void Shutdown()
        {
            // Close windows before flipping the shutdown flag so DismissAll can still run.
            DismissAll(synchronous: true);

            lock (_lock)
            {
                _shutdownRequested = true;
            }

            if (!_usesDedicatedThread)
                return;

            var dispatcher = _dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            try
            {
                dispatcher.InvokeShutdown();
            }
            catch
            {
                // Best-effort shutdown during AutoCAD exit.
            }

            var thread = _thread;
            if (thread != null && thread.IsAlive)
                thread.Join(TimeSpan.FromSeconds(2));
        }

        private McpToastManager CreateManager(Dispatcher dispatcher)
        {
            return new McpToastManager(
                dispatcher,
                Aggregator,
                () => _frameUsable(),
                () => _showBranding,
                () => _instanceIdentity);
        }

        private void PostToManager(Action<McpToastManager> action)
        {
            var dispatcher = _dispatcher;
            var manager = _manager;
            if (dispatcher == null || manager == null || dispatcher.HasShutdownStarted)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    action(manager);
                }
                catch (Exception ex)
                {
                    PluginLog.Debug("McpToastHost action failed: " + ex);
                }
            }));
        }
    }
}
