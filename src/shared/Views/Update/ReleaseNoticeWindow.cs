using System;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bimwright.Dwg.Plugin.Localization;

namespace Bimwright.Dwg.Plugin.Views.Update;

internal sealed class ReleaseNoticeWindow : Window
{
    private readonly string _prompt;
    private readonly Button _copy;

    internal ReleaseNoticeWindow(string current, StableRelease release, string skipPath = null)
    {
        _prompt = BuildAgentPrompt(current, release);

        Title = L.T("releaseNotice.title");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var bg = Brush("#F6F8FB");
        var surface = Brush("#FFFFFF");
        var text = Brush("#1E293B");
        var secondary = Brush("#64748B");
        var line = Brush("#E2E8F0");
        var pillBg = Brush("#F1F5F9");
        var blue = Brush("#007ACC");
        var blueHover = Brush("#0067AD");

        Background = bg;
        Foreground = text;
        ScrollBarFadeBehavior.SetIsEnabled(this, true);

        var root = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };

        // Header: version badge on left, Open release page link on right.
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var currentTag = (current ?? "").StartsWith("v", StringComparison.OrdinalIgnoreCase) ? current : "v" + current;
        var latestTag = release.Tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? release.Tag : "v" + release.VersionText;

        var pillText = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pillText.Children.Add(new TextBlock { Text = currentTag, Foreground = secondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        pillText.Children.Add(new TextBlock { Text = " → ", Foreground = secondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        pillText.Children.Add(new TextBlock { Text = latestTag, Foreground = blue, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var versionPill = new Border
        {
            Background = pillBg,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = pillText,
        };
        Grid.SetColumn(versionPill, 0);
        header.Children.Add(versionPill);

        var openLink = CreateLinkButton(L.T("releaseNotice.open") + " ↗", blue, FontWeights.SemiBold);
        openLink.Click += (_, __) =>
        {
            try { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }); }
            catch (Exception ex) { PluginLog.Debug("Release page could not open: " + ex.GetType().Name); }
        };
        Grid.SetColumn(openLink, 1);
        header.Children.Add(openLink);
        root.Children.Add(header);

        root.Children.Add(new TextBlock
        {
            Text = L.T("releaseNotice.available", ("version", release.VersionText)),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        if (!string.IsNullOrWhiteSpace(release.ReleaseNotes))
        {
            root.Children.Add(new TextBlock
            {
                Text = L.T("releaseNotice.whatsNew"),
                Foreground = secondary,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            });
            root.Children.Add(new Border
            {
                Background = surface,
                BorderBrush = line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBox
                {
                    Text = release.ReleaseNotes,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 160,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = text,
                    FontSize = 12,
                    Padding = new Thickness(10, 8, 10, 8),
                },
            });
        }

        var promptExpanded = string.IsNullOrWhiteSpace(release.ReleaseNotes);
        var promptLabel = L.T("releaseNotice.previewPrompt");
        var promptBox = new Border
        {
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 12),
            Visibility = promptExpanded ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBox
            {
                Text = _prompt,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 130,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Padding = new Thickness(8, 6, 8, 6),
            },
        };
        var promptToggle = CreateLinkButton((promptExpanded ? "▾ " : "▸ ") + promptLabel, secondary, FontWeights.Normal);
        promptToggle.HorizontalAlignment = HorizontalAlignment.Left;
        promptToggle.Margin = new Thickness(0, 0, 0, promptExpanded ? 0 : 8);
        promptToggle.Click += (_, __) =>
        {
            promptExpanded = !promptExpanded;
            promptToggle.Content = (promptExpanded ? "▾ " : "▸ ") + promptLabel;
            promptToggle.Margin = new Thickness(0, 0, 0, promptExpanded ? 0 : 8);
            promptBox.Visibility = promptExpanded ? Visibility.Visible : Visibility.Collapsed;
        };
        root.Children.Add(promptToggle);
        root.Children.Add(promptBox);

        // Footer: single guaranteed row — Skip link on left, primary Copy + Close on right.
        var footerDivider = new Border
        {
            BorderBrush = line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
        };
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var skip = CreateLinkButton(L.T("releaseNotice.skip"), secondary, FontWeights.Normal);
        skip.HorizontalAlignment = HorizontalAlignment.Left;
        skip.Click += (_, __) => { ReleaseNoticeCheck.SkipVersion(skipPath, release.VersionText); Close(); };
        Grid.SetColumn(skip, 0);
        footer.Children.Add(skip);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _copy = CreateRoundedButton(L.T("releaseNotice.copyPrompt"), blue, Brushes.White, blue, blueHover, 104);
        _copy.Margin = new Thickness(0, 0, 8, 0);
        _copy.Click += (_, __) => CopyPrompt();
        var close = CreateRoundedButton(L.T("settings.close"), surface, text, Brush("#CBD5E1"), surface, 72);
        close.IsCancel = true;
        close.Click += (_, __) => Close();
        actions.Children.Add(_copy);
        actions.Children.Add(close);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);

        footerDivider.Child = footer;
        root.Children.Add(footerDivider);
        Content = root;

        Loaded += (_, __) => PlaceBottomRight();
        SizeChanged += (_, __) => ClampInsideWorkArea();
    }

    internal static string BuildAgentPrompt(string current, StableRelease release)
    {
        var currentTag = (current ?? "").StartsWith("v", StringComparison.OrdinalIgnoreCase) ? current : "v" + current;
        var latestTag = release.Tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? release.Tag : "v" + release.VersionText;
        var sb = new StringBuilder();
        sb.AppendLine("Please help me update dwg-mcp (the AutoCAD MCP server and plugin by bimwright).");
        sb.AppendLine("Installed version: " + currentTag);
        sb.AppendLine("Latest version: " + latestTag);
        sb.AppendLine("Release page: " + release.Url);
        sb.AppendLine();
        sb.AppendLine("Steps:");
        sb.AppendLine("1. Open the release page and read the release notes and install instructions (also AGENTS.md in the repository).");
        sb.AppendLine("2. Download the DwgMcp.Setup-v" + release.VersionText + "-win-x64.zip asset and verify it against its .sha256 file.");
        sb.AppendLine("3. Tell me to save my work and close every running AutoCAD before installing, then guide me through running the installer step by step.");
        sb.AppendLine("4. After installing, help me restart AutoCAD and my MCP client, and confirm dwg_health reports " + latestTag + ".");
        sb.Append("Ask me before running any command on my computer.");
        return sb.ToString();
    }

    private void CopyPrompt()
    {
        try
        {
            Clipboard.SetText(_prompt);
            _copy.Content = L.T("releaseNotice.copied");
        }
        catch { }
    }

    private void PlaceBottomRight()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
        }
        catch { }
    }

    private void ClampInsideWorkArea()
    {
        if (!IsLoaded) return;
        try
        {
            var area = SystemParameters.WorkArea;
            if (Top + ActualHeight > area.Bottom - 16)
                Top = Math.Max(area.Top + 16, area.Bottom - ActualHeight - 16);
        }
        catch { }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static Button CreateLinkButton(string label, Brush foreground, FontWeight weight)
    {
        var template = new ControlTemplate(typeof(Button));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        template.VisualTree = presenter;

        return new Button
        {
            Content = label,
            Foreground = foreground,
            FontSize = 12,
            FontWeight = weight,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4, 0, 4),
            MinWidth = 0,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Template = template,
        };
    }

    private static Button CreateRoundedButton(string label, Brush background, Brush foreground, Brush border, Brush hoverBg, double minWidth)
    {
        var template = new ControlTemplate(typeof(Button));
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.BackgroundProperty, background);
        chrome.SetValue(Border.BorderBrushProperty, border);
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(MarginProperty, new Thickness(12, 4, 12, 4));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        chrome.AppendChild(presenter);
        template.VisualTree = chrome;

        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "Chrome"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#007ACC"), "Chrome"));
        template.Triggers.Add(hover);

        return new Button
        {
            Content = label,
            Foreground = foreground,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            MinHeight = 28,
            MinWidth = minWidth,
            Cursor = Cursors.Hand,
            Template = template,
        };
    }
}
