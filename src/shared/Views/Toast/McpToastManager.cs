using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    /// <summary>
    /// Reconciles the WPF toast surface with the pure activity state machine.
    /// One window slot: the aggregator owns card lifetime.
    /// </summary>
    internal sealed class McpToastManager
    {
        private const double EdgeMargin = 16;
        private const int TickMilliseconds = 100;

        private readonly Dispatcher _dispatcher;
        private readonly ActivityAggregator _aggregator;
        private readonly Func<bool> _isFrameUsable;
        private readonly Func<bool> _showBranding;
        private readonly Func<string> _instanceIdentity;
        private readonly DispatcherTimer _timer;
        private McpToastWindow _window;
        private IntPtr _ownerHandle;

        public McpToastManager(
            Dispatcher dispatcher,
            ActivityAggregator aggregator,
            Func<bool> isFrameUsable = null,
            Func<bool> showBranding = null,
            Func<string> instanceIdentity = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
            _isFrameUsable = isFrameUsable ?? (() => true);
            _showBranding = showBranding ?? (() => false);
            _instanceIdentity = instanceIdentity ?? (() => null);

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(TickMilliseconds)
            };
            _timer.Tick += OnTimerTick;
        }

        public void SetOwnerHandle(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
                _ownerHandle = hwnd;
        }

        public void ApplyShowBranding()
        {
            EnsureDispatcher();
            _window?.SetShowBranding(_showBranding());
        }

        public void Render()
        {
            EnsureDispatcher();
            var render = _aggregator.TakeRender();
            switch (render.Phase)
            {
                case ActivityCardPhase.Visible:
                    ReconcileVisible(render.Card);
                    return;
                case ActivityCardPhase.Closing:
                    ReconcileClosing(render.Card);
                    return;
                default:
                    ForceCloseWindow();
                    StopTimerIfNoWindow();
                    return;
            }
        }

        internal void Tick(bool frameUsable)
        {
            EnsureDispatcher();
            if (_window == null)
                return;
            if (_aggregator.Tick(frameUsable))
                Render();
        }

        public void DismissAllImmediate()
        {
            EnsureDispatcher();
            _timer.Stop();
            var window = _window;
            _window = null;
            if (window != null)
            {
                try { window.CloseImmediate(); }
                catch { }
            }
            _aggregator.Reset();
            _aggregator.TakeRender();
        }

        public void Dispose()
        {
            if (_dispatcher.CheckAccess())
                _timer.Stop();
        }

        private void ReconcileVisible(ActivitySnapshot card)
        {
            if (card == null)
            {
                ForceCloseWindow();
                StopTimerIfNoWindow();
                return;
            }

            if (_window != null && _window.CardId == card.CardId)
            {
                _window.Update(card);
                EnsureTimer();
                return;
            }

            ForceCloseWindow();
            CreateWindow(card);
        }

        private void ReconcileClosing(ActivitySnapshot card)
        {
            if (card == null)
            {
                StopTimerIfNoWindow();
                return;
            }

            if (_window != null && _window.CardId == card.CardId)
            {
                _window.BeginClose();
                EnsureTimer();
                return;
            }

            ForceCloseWindow();
            _aggregator.CardClosed(card.CardId);
            StopTimerIfNoWindow();
        }

        private void CreateWindow(ActivitySnapshot card)
        {
            var window = new McpToastWindow(
                card,
                OnWindowClosed,
                OnDismissRequested,
                OnCardClicked,
                OnPointerEntered,
                OnPointerLeft,
                instanceIdentity: _instanceIdentity());
            _window = window;
            window.SetShowBranding(_showBranding());
            AttachOwner(window);
            PositionWindow(window);
            window.CapturePointerBaseline();
            window.Show();
            window.PlayEnterAnimation();
            EnsureTimer();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_window == null)
            {
                _timer.Stop();
                return;
            }

            bool frameUsable;
            try { frameUsable = _isFrameUsable(); }
            catch { frameUsable = true; }
            Tick(frameUsable);
        }

        private void OnWindowClosed(McpToastWindow window, long cardId)
        {
            EnsureDispatcher();
            if (!ReferenceEquals(_window, window) || window.CardId != cardId)
                return;
            _window = null;
            _aggregator.CardClosed(cardId);
            StopTimerIfNoWindow();
        }

        private void OnDismissRequested(long cardId)
        {
            EnsureDispatcher();
            if (_window == null || _window.CardId != cardId)
                return;
            if (_aggregator.Dismiss(cardId))
                Render();
        }

        private void OnCardClicked(long cardId)
        {
            EnsureDispatcher();
            if (_window == null || _window.CardId != cardId)
                return;
            if (_aggregator.Dismiss(cardId))
                Render();
        }

        private void OnPointerEntered(long cardId)
        {
            EnsureDispatcher();
            if (_window != null && _window.CardId == cardId)
                _aggregator.PointerEntered(cardId);
        }

        private void OnPointerLeft(long cardId)
        {
            EnsureDispatcher();
            if (_window != null && _window.CardId == cardId)
                _aggregator.PointerLeft(cardId);
        }

        private void ForceCloseWindow()
        {
            var window = _window;
            if (window == null)
                return;
            _window = null;
            try { window.CloseImmediate(); }
            catch { }
            StopTimerIfNoWindow();
        }

        private void EnsureTimer()
        {
            if (!_timer.IsEnabled && _window != null)
                _timer.Start();
        }

        private void StopTimerIfNoWindow()
        {
            if (_window == null)
                _timer.Stop();
        }

        private void AttachOwner(McpToastWindow window)
        {
            var owner = GetValidOwnerHandle();
            if (owner == IntPtr.Zero)
                return;
            try { new WindowInteropHelper(window).Owner = owner; }
            catch { }
        }

        private void PositionWindow(McpToastWindow window)
        {
            var owner = GetValidOwnerHandle();
            double left = EdgeMargin;
            double top = EdgeMargin;
            if (owner != IntPtr.Zero && GetWindowRect(owner, out var rect))
            {
                GetOwnerDpiScale(owner, out var dpiX, out var dpiY);
                left = rect.Left * dpiX + EdgeMargin;
                top = rect.Top * dpiY + EdgeMargin;
            }
            if (!IsFinite(left)) left = EdgeMargin;
            if (!IsFinite(top)) top = EdgeMargin;
            window.SetPosition(top, left);
        }

        private IntPtr GetValidOwnerHandle()
        {
            if (_ownerHandle != IntPtr.Zero && IsWindow(_ownerHandle))
                return _ownerHandle;
            if (_ownerHandle != IntPtr.Zero && !IsWindow(_ownerHandle))
                _ownerHandle = IntPtr.Zero;
            return _ownerHandle;
        }

        private static void GetOwnerDpiScale(IntPtr hwnd, out double dpiX, out double dpiY)
        {
            dpiX = 1.0;
            dpiY = 1.0;
            try
            {
                var dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    dpiX = 96.0 / dpi;
                    dpiY = dpiX;
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch
            {
            }
        }

        private void EnsureDispatcher()
        {
            if (!_dispatcher.CheckAccess())
                throw new InvalidOperationException("McpToastManager must run on the toast dispatcher thread.");
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
