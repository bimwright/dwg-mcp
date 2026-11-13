using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Bimwright.Dwg.Plugin.Localization;
using Bimwright.Dwg.Plugin.Views.Toast;

namespace Bimwright.Dwg.Plugin.Views
{
    /// <summary>
    /// Session-only activity history, opened by the toast body or MCPHISTORY.
    /// Matches the RVT search/status/kind/table/detail interaction without porting
    /// Revit's rerun or persistent journal features into AutoCAD.
    /// </summary>
    public sealed class HistoryWindow : Window
    {
        private readonly ActivityHistory _history;
        private readonly DataGrid _grid;
        private readonly TextBox _search = new TextBox { Width = 180 };
        private readonly ComboBox _status = new ComboBox { Width = 95 };
        private readonly ComboBox _kind = new ComboBox { Width = 95 };
        private readonly CollectionViewSource _view = new CollectionViewSource();
        private readonly ObservableCollection<ActivityHistoryEntry> _entries = new ObservableCollection<ActivityHistoryEntry>();
        private readonly TextBlock _summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBox _what = DetailText();
        private readonly TextBox _input = DetailText();
        private readonly TextBox _output = DetailText();
        private readonly Button _copyWhat;
        private readonly Button _copyInput;
        private readonly Button _copyOutput;
        private readonly TextBlock _copyNote = new TextBlock { Margin = new Thickness(4), Foreground = McpToastTheme.TextSecondary };
        private readonly TextBlock _footer = new TextBlock { Foreground = McpToastTheme.TextSecondary, FontSize = 11 };
        private readonly Button _openImage = new Button { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        private readonly Border _detail;
        private readonly RowDefinition _detailRow;
        private readonly DispatcherTimer _refresh;
        private readonly Action<string> _copyText;
        private long _version = -1;

        public HistoryWindow(ActivityHistory history, string instanceIdentity = null)
            : this(history, instanceIdentity, Clipboard.SetText) { }

        internal HistoryWindow(ActivityHistory history, string instanceIdentity, Action<string> copyText)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _copyText = copyText ?? throw new ArgumentNullException(nameof(copyText));
            Title = L.T("history.title", ("identity", instanceIdentity ?? BrandAssets.ProductName));
            Width = 900; Height = 600; MinWidth = 720; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = McpToastTheme.UiFont;
            FontSize = 13;
            Background = McpToastTheme.Background;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _detailRow = new RowDefinition { Height = new GridLength(0) };
            root.RowDefinitions.Add(_detailRow);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(BuildToolbar());

            _view.Filter += Filter;
            _view.Source = _entries;
            _grid = new DataGrid
            {
                IsReadOnly = true, AutoGenerateColumns = false,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(4), CanUserAddRows = false
            };
            _grid.ItemsSource = _view.View;
            var header = new Style(typeof(DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
            header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            _grid.ColumnHeaderStyle = header;
            AddColumn("#", "Index", 40);
            AddColumn(L.T("history.time"), "TimeLabel", 92);
            AddColumn(L.T("history.tool"), "ToolName", 160);
            var summaryStyle = new Style(typeof(TextBlock));
            summaryStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            summaryStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("Summary")));
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = L.T("history.summary"), Binding = new Binding("Summary"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = summaryStyle
            });
            AddColumn(L.T("history.durationUnit"), "DurationMs", 55);
            var statusStyle = new Style(typeof(TextBlock));
            statusStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            statusStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            statusStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, McpToastTheme.Success));
            var failed = new DataTrigger { Binding = new Binding("Success"), Value = false };
            failed.Setters.Add(new Setter(TextBlock.ForegroundProperty, McpToastTheme.Error));
            statusStyle.Triggers.Add(failed);
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = L.T("history.status"), Binding = new Binding("StatusLabel"), Width = 65, ElementStyle = statusStyle
            });
            _grid.SelectionChanged += (_, __) => ShowSelected();
            Grid.SetRow(_grid, 1); root.Children.Add(_grid);

            var detailBody = new DockPanel { Margin = new Thickness(8) };
            DockPanel.SetDock(_summary, Dock.Top); detailBody.Children.Add(_summary);
            _openImage.Content = L.T("toast.activity.open_image");
            _openImage.HorizontalAlignment = HorizontalAlignment.Left;
            _openImage.Click += (_, __) => OpenSelectedImage();
            DockPanel.SetDock(_openImage, Dock.Top); detailBody.Children.Add(_openImage);
            DockPanel.SetDock(_copyNote, Dock.Bottom); detailBody.Children.Add(_copyNote);
            var tabs = new TabControl();
            tabs.Items.Add(DetailTab(L.T("history.what"), _what, out _copyWhat));
            tabs.Items.Add(DetailTab(L.T("history.input"), _input, out _copyInput));
            tabs.Items.Add(DetailTab(L.T("history.output"), _output, out _copyOutput));
            detailBody.Children.Add(tabs);
            _detail = new Border
            {
                Margin = new Thickness(4), BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = McpToastTheme.MutedAccent, Visibility = Visibility.Collapsed,
                Child = detailBody
            };
            Grid.SetRow(_detail, 2); root.Children.Add(_detail);
            _footer.Margin = new Thickness(12, 8, 12, 12);
            Grid.SetRow(_footer, 3); root.Children.Add(_footer);
            Content = root;

            _search.TextChanged += (_, __) => RefreshEntries(true);
            _status.SelectionChanged += (_, __) => RefreshEntries(true);
            _kind.SelectionChanged += (_, __) => RefreshEntries(true);
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refresh.Tick += OnRefresh;
            Loaded += OnLoaded;
            Closed += OnClosed;
            RefreshEntries(true);
        }

        private void OnRefresh(object sender, EventArgs e) => RefreshEntries();
        private void OnLoaded(object sender, RoutedEventArgs e) { RefreshEntries(true); _refresh.Start(); }
        private void OnClosed(object sender, EventArgs e)
        {
            _refresh.Stop();
            _refresh.Tick -= OnRefresh;
            Loaded -= OnLoaded;
            _view.Filter -= Filter;
            _grid.ItemsSource = null;
            _view.Source = null;
            _entries.Clear();
        }

        private static TextBox DetailText() => new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4),
            FontFamily = new FontFamily("Consolas"), FontSize = 12
        };

        private TabItem DetailTab(string title, TextBox text, out Button copy)
        {
            var body = new DockPanel();
            copy = new Button { Content = L.T("history.copy"), IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4), Padding = new Thickness(8, 2, 8, 2) };
            copy.Click += (_, __) =>
            {
                try { _copyText(text.Text); _copyNote.Text = L.T("history.copied"); }
                catch { _copyNote.Text = L.T("history.copyFailed"); }
            };
            DockPanel.SetDock(copy, Dock.Top); body.Children.Add(copy); body.Children.Add(text);
            return new TabItem { Header = title, Content = body };
        }

        private UIElement BuildToolbar()
        {
            var toolbar = new WrapPanel { Margin = new Thickness(8) };
            void Add(string text, FrameworkElement control)
            {
                toolbar.Children.Add(new TextBlock
                {
                    Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0)
                });
                control.Margin = new Thickness(0, 0, 8, 0);
                toolbar.Children.Add(control);
            }
            Add(L.T("history.search"), _search);
            _status.Items.Add(L.T("history.all"));
            _status.Items.Add(L.T("toast.activity.success"));
            _status.Items.Add(L.T("toast.activity.failed"));
            _status.SelectedIndex = 0;
            Add(L.T("history.status"), _status);
            _kind.Items.Add(L.T("history.all"));
            _kind.Items.Add(L.T("history.read"));
            _kind.Items.Add(L.T("history.write"));
            _kind.SelectedIndex = 0;
            Add(L.T("history.kind"), _kind);
            var refresh = new Button { Content = L.T("history.refresh"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
            refresh.Click += (_, __) => RefreshEntries(true);
            toolbar.Children.Add(refresh);
            var clear = new Button { Content = L.T("history.newSession"), Padding = new Thickness(8, 3, 8, 3) };
            clear.Click += (_, __) => { _history.Clear(); RefreshEntries(true); };
            toolbar.Children.Add(clear);
            return toolbar;
        }

        private void AddColumn(string title, string property, double width) => _grid.Columns.Add(
            new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });

        private void Filter(object sender, FilterEventArgs e)
        {
            var entry = e.Item as ActivityHistoryEntry;
            if (entry == null) { e.Accepted = false; return; }
            var text = _search.Text.Trim();
            e.Accepted = (text.Length == 0 || entry.ToolName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.Summary.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                && (_status.SelectedIndex == 0 || entry.Success == (_status.SelectedIndex == 1))
                && (_kind.SelectedIndex == 0 || entry.Kind == (_kind.SelectedIndex == 1 ? ToolActivityKind.Read : ToolActivityKind.Write));
        }

        private void RefreshEntries(bool force = false)
        {
            var version = _history.Version;
            if (!force && version == _version) return;
            _version = version;
            var selected = _grid.SelectedItem as ActivityHistoryEntry;
            var entries = _history.Snapshot();
            var scroll = FindScroll(_grid);
            var vertical = scroll?.VerticalOffset ?? 0;
            var horizontal = scroll?.HorizontalOffset ?? 0;
            var anchor = scroll?.CanContentScroll == true && vertical > 0 && (int)vertical < _grid.Items.Count
                ? _grid.Items[(int)vertical] : null;
            var keep = new HashSet<ActivityHistoryEntry>(entries);
            for (var i = _entries.Count - 1; i >= 0; i--)
                if (!keep.Contains(_entries[i])) _entries.RemoveAt(i);
            for (var i = 0; i < entries.Length; i++)
                if (i >= _entries.Count || !ReferenceEquals(_entries[i], entries[i])) _entries.Insert(i, entries[i]);
            if (force) _view.View.Refresh();
            _grid.SelectedItem = selected != null && _view.View.Contains(selected) ? selected : null;
            if (scroll != null)
            {
                _grid.UpdateLayout();
                if (anchor != null && _grid.Items.Contains(anchor)) vertical += _grid.Items.IndexOf(anchor) - (int)vertical;
                scroll.ScrollToVerticalOffset(vertical);
                scroll.ScrollToHorizontalOffset(horizontal);
            }
            ShowSelected();
            _footer.Text = L.T("history.footer", ("count", entries.Length));
        }

        private static ScrollViewer FindScroll(DependencyObject root)
        {
            if (root is ScrollViewer scroll) return scroll;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = FindScroll(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private void ShowSelected()
        {
            var entry = _grid.SelectedItem as ActivityHistoryEntry;
            _detail.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
            _detailRow.Height = entry == null ? new GridLength(0) : new GridLength(240);
            _summary.Text = entry == null ? string.Empty : entry.ToolName + " · " + entry.KindLabel + " · "
                + L.T(entry.Success ? "toast.activity.success" : "toast.activity.failed") + " · "
                + L.T("history.duration", ("duration", entry.DurationMs));
            _openImage.Visibility = entry?.ImagePath != null ? Visibility.Visible : Visibility.Collapsed;
            _what.Text = entry?.What ?? string.Empty;
            _input.Text = entry?.Input ?? string.Empty;
            _output.Text = entry?.Output ?? string.Empty;
            _copyWhat.IsEnabled = _what.Text.Length > 0;
            _copyInput.IsEnabled = _input.Text.Length > 0;
            _copyOutput.IsEnabled = _output.Text.Length > 0;
            _copyNote.Text = string.Empty;
        }

        private void OpenSelectedImage()
        {
            var path = (_grid.SelectedItem as ActivityHistoryEntry)?.ImagePath;
            if (!ToastContentBuilder.IsSafeImagePath(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { }
        }
    }
}
