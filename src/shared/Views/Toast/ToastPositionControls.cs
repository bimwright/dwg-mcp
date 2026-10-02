using System;
using System.Windows;
using System.Windows.Controls;
using Bimwright.Dwg.Plugin.Localization;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    internal sealed class ToastPositionControls : StackPanel
    {
        internal ToastPositionControls(Func<ToastPositionOptions> read, Func<ToastPositionOptions, bool> save)
        {
            var options = read();
            var horizontal = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            horizontal.Items.Add(L.T("settings.toast.left"));
            horizontal.Items.Add(L.T("settings.toast.right"));
            horizontal.SelectedIndex = options.Right ? 1 : 0;
            var vertical = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            vertical.Items.Add(L.T("settings.toast.top"));
            vertical.Items.Add(L.T("settings.toast.bottom"));
            vertical.SelectedIndex = options.Bottom ? 1 : 0;
            var drag = new CheckBox { Content = L.T("settings.toast.drag"), IsChecked = options.DragEnabled };
            var reset = new Button { Content = L.T("settings.toast.reset"), HorizontalAlignment = HorizontalAlignment.Left };
            var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = McpToastTheme.Error };
            void Apply(ToastPositionOptions value)
            {
                options = value;
                feedback.Text = save(value) ? string.Empty : L.T("toast.status.saveFailed");
            }
            horizontal.SelectionChanged += (_, __) => Apply(options.WithCorner(horizontal.SelectedIndex == 1, vertical.SelectedIndex == 1));
            vertical.SelectionChanged += (_, __) => Apply(options.WithCorner(horizontal.SelectedIndex == 1, vertical.SelectedIndex == 1));
            drag.Checked += (_, __) => Apply(options.WithDrag(true));
            drag.Unchecked += (_, __) => Apply(options.WithDrag(false));
            reset.Click += (_, __) => Apply(options.WithOffset(null, null));
            Children.Add(new TextBlock { Text = L.T("settings.toast.horizontal"), Margin = new Thickness(0, 12, 0, 4) });
            Children.Add(horizontal);
            Children.Add(new TextBlock { Text = L.T("settings.toast.vertical"), Margin = new Thickness(0, 8, 0, 4) });
            Children.Add(vertical);
            Children.Add(drag);
            Children.Add(reset);
            Children.Add(feedback);
        }
    }
}
