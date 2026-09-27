using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Bimwright.Dwg.Plugin.Localization;
using Bimwright.Dwg.Plugin.ToolCatalog;
using Bimwright.Dwg.Plugin.Views.Toast;

namespace Bimwright.Dwg.Plugin.Views.Settings
{
    /// <summary>
    /// Modeless settings shell. Open it with <see cref="App.ShowOrFocusSettings"/>,
    /// which uses AutoCAD's ShowModelessWindow so the frame keeps keyboard focus.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private readonly CheckBox _toastEnabled;
        private readonly CheckBox _showBranding;
        private readonly ComboBox _idle;
        private readonly TextBlock _transportText;
        private readonly TextBlock _clientText;
        private readonly TextBlock _copiedText;
        private readonly TextBlock _applyNote;
        private readonly Button _copyPort;
        private readonly Button _listener;
        private readonly Button _apply;
        private readonly TextBox _search;
        private readonly TextBlock _toolsStatus;
        private readonly ListView _tools;
        private readonly DispatcherTimer _refresh;
        private int _savedIdle;
        private bool _closing;
        private bool _suppressToastEvents;
        private string _toolsSignature;

        public SettingsWindow()
        {
            Title = L.T("settings.title");
            Width = 560;
            Height = 460;
            MinWidth = 480;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;

            _savedIdle = PluginSettings.LoadToastIdleSeconds();
            var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0) };
            tabs.Items.Add(Page(L.T("settings.tab.general"), BuildGeneral(out _transportText, out _clientText, out _copiedText, out _copyPort, out _listener)));
            tabs.Items.Add(Page(L.T("settings.tab.toast"), BuildToast(out _toastEnabled, out _showBranding, out _idle)));
            tabs.Items.Add(Page(L.T("settings.tab.tools"), BuildTools(out _search, out _toolsStatus, out _tools)));
            tabs.Items.Add(Page(L.T("settings.tab.about"), BuildAbout()));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            _applyNote = new TextBlock
            {
                Foreground = Brushes.IndianRed,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            _apply = new Button { Content = L.T("settings.apply"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
            var cancel = new Button { Content = L.T("settings.cancel"), MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = L.T("settings.close"), MinWidth = 88, IsCancel = true };
            _apply.Click += (_, __) => ApplyIdle();
            cancel.Click += (_, __) => RevertIdle();
            close.Click += (_, __) => Close();
            buttons.Children.Add(_applyNote);
            buttons.Children.Add(_apply);
            buttons.Children.Add(cancel);
            buttons.Children.Add(close);

            var root = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(tabs);
            Content = root;

            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refresh.Tick += (_, __) => RefreshLive();
            Loaded += (_, __) =>
            {
                RefreshLive();
                _refresh.Start();
            };
            Closed += (_, __) => _refresh.Stop();
        }

        private UIElement BuildGeneral(
            out TextBlock transport,
            out TextBlock client,
            out TextBlock copied,
            out Button copy,
            out Button listener)
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(Heading(L.T("settings.general.connection")));
            transport = Body();
            client = Body();
            panel.Children.Add(transport);
            panel.Children.Add(client);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            listener = new Button { MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
            copy = new Button { Content = L.T("settings.general.copy"), MinWidth = 88 };
            copied = new TextBlock { Margin = new Thickness(8, 6, 0, 0), Foreground = Brushes.DimGray };
            var listenerButton = listener;
            listener.Click += (_, __) =>
            {
                if (App.ListenerRunning)
                    App.StopListener(closeSettings: false);
                else
                    App.StartListener();
                RefreshLive();
            };
            copy.Click += (_, __) =>
            {
                var port = App.ListenerPort;
                if (!port.HasValue)
                    return;
                try
                {
                    Clipboard.SetText(port.Value.ToString());
                    _copiedText.Text = L.T("settings.general.copied");
                }
                catch
                {
                    _copiedText.Text = string.Empty;
                }
            };
            row.Children.Add(listenerButton);
            row.Children.Add(copy);
            row.Children.Add(copied);
            panel.Children.Add(row);
            return panel;
        }

        private UIElement BuildToast(out CheckBox enabled, out CheckBox branding, out ComboBox idle)
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            enabled = new CheckBox
            {
                Content = L.T("settings.toast.enabled"),
                IsChecked = App.ToastEnabled,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(enabled);
            panel.Children.Add(Help(L.T("settings.toast.enabled.help")));
            branding = new CheckBox
            {
                Content = L.T("settings.toast.brand"),
                IsChecked = App.ToastNotifier != null && App.ToastNotifier.ShowBranding,
                Margin = new Thickness(0, 12, 0, 0)
            };
            panel.Children.Add(branding);
            panel.Children.Add(Help(L.T("settings.toast.brand.help")));
            panel.Children.Add(new TextBlock
            {
                Text = L.T("settings.toast.idle"),
                Margin = new Thickness(0, 12, 0, 4),
                FontWeight = FontWeights.SemiBold
            });
            idle = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var seconds in PluginSettings.ToastIdleChoices)
            {
                idle.Items.Add(new ComboBoxItem
                {
                    Content = L.T("settings.toast.idle.seconds", ("seconds", seconds)),
                    Tag = seconds
                });
            }
            SelectIdle(idle, _savedIdle);
            panel.Children.Add(idle);
            panel.Children.Add(Help(L.T("settings.toast.idle.help")));
            var env = Environment.GetEnvironmentVariable(PluginSettings.EnvEnableToast);
            if (!string.IsNullOrEmpty(env))
            {
                panel.Children.Add(Help(PluginSettings.EnvEnableToast + " is set and re-applies at next launch."));
            }

            enabled.Checked += (_, __) => { if (!_suppressToastEvents) App.SetToastEnabled(true); };
            enabled.Unchecked += (_, __) => { if (!_suppressToastEvents) App.SetToastEnabled(false); };
            branding.Checked += (_, __) => App.ToastNotifier?.SetShowBranding(true);
            branding.Unchecked += (_, __) => App.ToastNotifier?.SetShowBranding(false);
            idle.SelectionChanged += (_, __) =>
            {
                if (_applyNote != null)
                    _applyNote.Text = string.Empty;
                SyncApply();
            };
            return panel;
        }

        private UIElement BuildTools(out TextBox search, out TextBlock status, out ListView tools)
        {
            var panel = new DockPanel { Margin = new Thickness(8) };
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            searchRow.Children.Add(new TextBlock
            {
                Text = L.T("settings.tools.search"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            search = new TextBox { MinWidth = 220 };
            search.TextChanged += (_, __) => FillTools();
            searchRow.Children.Add(search);
            DockPanel.SetDock(searchRow, Dock.Top);
            panel.Children.Add(searchRow);

            status = new TextBlock { Margin = new Thickness(0, 0, 0, 8), Foreground = Brushes.DimGray };
            DockPanel.SetDock(status, Dock.Top);
            panel.Children.Add(status);

            var grid = new GridView();
            grid.Columns.Add(Column(L.T("settings.tools.name"), "Name", 180));
            grid.Columns.Add(Column(L.T("settings.tools.description"), "Description", 220));
            grid.Columns.Add(Column(L.T("settings.tools.source"), "Source", 70));
            grid.Columns.Add(Column(L.T("settings.tools.timeout"), "TimeoutSeconds", 70));
            var itemStyle = new Style(typeof(ListViewItem));
            itemStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Description")));
            tools = new ListView { View = grid, ItemContainerStyle = itemStyle };
            panel.Children.Add(tools);
            return panel;
        }

        private static UIElement BuildAbout()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            panel.Children.Add(Body(L.T("settings.about.version") + " " + (version == null ? "" : version.ToString())));
            panel.Children.Add(Help(L.T("settings.about.description")));
            panel.Children.Add(Body(L.T("settings.about.author") + ": Bimwright"));
            panel.Children.Add(Body(L.T("settings.about.license") + ": Apache-2.0"));
            var link = new TextBlock { Margin = new Thickness(0, 8, 0, 0) };
            var hyperlink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(L.T("settings.about.github")))
            {
                NavigateUri = new Uri("https://github.com/bimwright/dwg-mcp")
            };
            hyperlink.RequestNavigate += (_, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                    {
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            link.Inlines.Add(hyperlink);
            panel.Children.Add(link);
            return panel;
        }

        private void RefreshLive()
        {
            var running = App.ListenerRunning;
            var pipe = App.ListenerUsesPipe;
            var port = App.ListenerPort;
            _transportText.Text = running
                ? (pipe
                    ? L.T("settings.general.transport") + ": " + L.T("settings.general.pipe")
                        + " · " + L.T("settings.general.port") + ": " + L.T("settings.general.port.na")
                    : L.T("settings.general.transport") + ": TCP · " + L.T("settings.general.port") + ": " + port)
                : L.T("settings.general.stopped");
            _clientText.Text = L.T("settings.general.client") + ": "
                + (App.ClientRecentlyPresent
                    ? L.T("settings.general.client.connected")
                    : L.T("settings.general.client.idle"));
            _listener.Content = running
                ? L.T("settings.general.listener.off")
                : L.T("settings.general.listener.on");
            var canCopy = running && !pipe && port.HasValue;
            _copyPort.IsEnabled = canCopy;
            _copyPort.ToolTip = canCopy ? null : L.T("settings.general.copy.disabled");
            var toastOn = App.ToastEnabled;
            _suppressToastEvents = true;
            try
            {
                if (_toastEnabled.IsChecked != toastOn)
                    _toastEnabled.IsChecked = toastOn;
            }
            finally
            {
                _suppressToastEvents = false;
            }
            _showBranding.IsEnabled = toastOn;
            _idle.IsEnabled = toastOn;
            SyncApply();
            FillTools();
        }

        private void SyncApply()
        {
            if (_apply == null || _idle == null || _toastEnabled == null)
                return;
            _apply.IsEnabled = _toastEnabled.IsChecked == true && SelectedIdle() != _savedIdle;
        }

        private void FillTools()
        {
            var status = ToolCatalogStore.Status;
            var query = (_search.Text ?? string.Empty).Trim();
            // The refresh timer polls every 500 ms. Rebinding ItemsSource resets scroll
            // and selection, so refill only when the inputs actually changed.
            var signature = (int)status + "|" + (status == ToolCatalogStatus.Current
                ? ToolCatalogStore.Snapshot().Length : 0) + "|" + query;
            if (signature == _toolsSignature)
                return;
            _toolsSignature = signature;

            if (status == ToolCatalogStatus.Invalid)
            {
                _toolsStatus.Text = L.T("settings.tools.invalid");
                _tools.ItemsSource = null;
                return;
            }

            if (status != ToolCatalogStatus.Current)
            {
                _toolsStatus.Text = App.ClientRecentlyPresent
                    ? L.T("settings.tools.waiting")
                    : L.T("settings.tools.empty");
                _tools.ItemsSource = null;
                return;
            }

            var rows = new List<ToolCatalogEntry>();
            foreach (var tool in ToolCatalogStore.Snapshot())
            {
                if (query.Length == 0
                    || (tool.Name != null && tool.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (tool.Description != null && tool.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    rows.Add(tool);
                }
            }
            _tools.ItemsSource = rows;
            _toolsStatus.Text = rows.Count == 0
                ? L.T("settings.tools.none")
                : L.T("settings.tools.count", ("count", rows.Count));
        }

        private void ApplyIdle()
        {
            var seconds = SelectedIdle();
            var saved = PluginSettings.SaveToastIdleSeconds(seconds);
            _savedIdle = PluginSettings.NormalizeToastIdleSeconds(seconds);
            App.ToastNotifier?.SetIdleSeconds(_savedIdle);
            SelectIdle(_idle, _savedIdle);
            _apply.IsEnabled = false;
            _applyNote.Text = saved ? string.Empty : L.T("toast.status.saveFailed");
        }

        private void RevertIdle()
        {
            SelectIdle(_idle, _savedIdle);
            _apply.IsEnabled = false;
        }

        private int SelectedIdle()
        {
            var item = _idle.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag is int seconds)
                return seconds;
            return _savedIdle;
        }

        private static void SelectIdle(ComboBox idle, int seconds)
        {
            foreach (var entry in idle.Items)
            {
                var item = entry as ComboBoxItem;
                if (item != null && item.Tag is int value && value == seconds)
                {
                    idle.SelectedItem = item;
                    return;
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_closing && _toastEnabled.IsChecked == true && SelectedIdle() != _savedIdle)
            {
                var answer = MessageBox.Show(
                    this,
                    L.T("settings.discard"),
                    Title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            _closing = true;
            base.OnClosing(e);
        }

        private static TabItem Page(string header, UIElement content)
        {
            return new TabItem { Header = header, Content = content };
        }

        private static TextBlock Heading(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        }

        private static TextBlock Body(string text = "")
        {
            return new TextBlock { Text = text, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap };
        }

        private static TextBlock Help(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
        }

        private static GridViewColumn Column(string header, string path, double width)
        {
            return new GridViewColumn { Header = header, DisplayMemberBinding = new System.Windows.Data.Binding(path), Width = width };
        }
    }
}
