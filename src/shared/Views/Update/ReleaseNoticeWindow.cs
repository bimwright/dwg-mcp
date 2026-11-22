using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bimwright.Dwg.Plugin.Localization;

namespace Bimwright.Dwg.Plugin.Views.Update;

internal sealed class ReleaseNoticeWindow : Window
{
    internal ReleaseNoticeWindow(string current, StableRelease release)
    {
        Title = L.T("releaseNotice.title");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowActivated = false;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Background = Brushes.White;

        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(new TextBlock
        {
            Text = L.T("releaseNotice.available", ("version", release.VersionText)),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = L.T("releaseNotice.running", ("version", current)),
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 0)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var open = new Button { Content = L.T("releaseNotice.open"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = L.T("settings.close"), Padding = new Thickness(10, 4, 10, 4) };
        open.Click += (_, __) =>
        {
            try { Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }); }
            catch (System.Exception ex) { PluginLog.Debug("Release page could not open: " + ex.GetType().Name); }
        };
        close.Click += (_, __) => Close();
        buttons.Children.Add(open);
        buttons.Children.Add(close);
        body.Children.Add(buttons);
        Content = body;
    }
}
