using System;
using System.Diagnostics;
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
        private readonly TextBlock _summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _footer = new TextBlock { Foreground = McpToastTheme.TextSecondary, FontSize = 11 };
        private readonly Button _openImage = new Button { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        private readonly Border _detail;
        private readonly RowDefinition _detailRow;
        private readonly DispatcherTimer _refresh;
        private long _version = -1;

        public HistoryWindow(ActivityHistory history, string instanceIdentity = null)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
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
            _grid = new DataGrid
            {
                IsReadOnly = true, AutoGenerateColumns = false,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(4), CanUserAddRows = false
            };
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
            AddColumn("ms", "DurationMs", 55);
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

            var detailBody = new StackPanel { Margin = new Thickness(8) };
            detailBody.Children.Add(_summary);
            _openImage.Content = L.T("toast.activity.open_image");
            _openImage.HorizontalAlignment = HorizontalAlignment.Left;
            _openImage.Click += (_, __) => OpenSelectedImage();
            detailBody.Children.Add(_openImage);
            _detail = new Border
            {
                Margin = new Thickness(4), BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = McpToastTheme.MutedAccent, Visibility = Visibility.Collapsed,
                Child = new ScrollViewer { Content = detailBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            };
            Grid.SetRow(_detail, 2); root.Children.Add(_detail);
            _footer.Margin = new Thickness(12, 8, 12, 12);
            Grid.SetRow(_footer, 3); root.Children.Add(_footer);
            Content = root;

            _search.TextChanged += (_, __) => RefreshEntries(true);
            _status.SelectionChanged += (_, __) => RefreshEntries(true);
            _kind.SelectionChanged += (_, __) => RefreshEntries(true);
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refresh.Tick += (_, __) => RefreshEntries();
            Loaded += (_, __) => { RefreshEntries(true); _refresh.Start(); };
            Closed += (_, __) => _refresh.Stop();
            RefreshEntries(true);
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
            _view.Source = entries;
            _grid.ItemsSource = _view.View;
            if (selected != null && _view.View.Contains(selected))
                _grid.SelectedItem = selected;
            ShowSelected();
            _footer.Text = L.T("history.footer", ("count", entries.Length));
        }

        private void ShowSelected()
        {
            var entry = _grid.SelectedItem as ActivityHistoryEntry;
            _detail.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
            _detailRow.Height = entry == null ? new GridLength(0) : new GridLength(150);
            _summary.Text = entry == null ? string.Empty : entry.ToolName + " · " + entry.KindLabel + " · "
                + (entry.Success ? L.T("toast.activity.success") : L.T("toast.activity.failed")) + "\n\n" + entry.Summary;
            _openImage.Visibility = entry?.ImagePath != null ? Visibility.Visible : Visibility.Collapsed;
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
