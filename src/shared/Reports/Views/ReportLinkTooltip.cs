using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Bimwright.Dwg.Plugin.Localization;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Presentation;

namespace Bimwright.Dwg.Plugin.Reports.Views
{
    // The plugin shells also compile Bimwright.Dwg.Plugin.Reports.Binding; name the WPF type explicitly.
    using Binding = System.Windows.Data.Binding;

    /// <summary>
    /// Tooltip of a report body link: what it points at (type, layer, handle), read from the
    /// stored report, and a last line that says what a click does or, while dimmed, which drawing it belongs to.
    /// </summary>
    internal static class ReportLinkTooltip
    {
        /// <summary>A multi-entity link lists this many entities, then "+N more".</summary>
        internal const int MaxListed = 5;

        public static ToolTip Create(ReportLinkViewModel link)
        {
            var tip = new ToolTip { Style = (Style)ReportsStyles.Dictionary[typeof(ToolTip)] };
            tip.Opened += (s, e) => { if (tip.Content == null) tip.Content = BuildContent(link); };
            return tip;
        }

        private static Panel BuildContent(ReportLinkViewModel link)
        {
            var panel = new StackPanel { MaxWidth = 340 };
            var entities = link.Entities;
            if (entities.Count == 1)
            {
                panel.Children.Add(Line(Describe(entities[0]), true));
                panel.Children.Add(Line(L.T("reports.link.tip.handle", ("handle", entities[0]?.Handle ?? "")), false));
            }
            else if (entities.Count > 1)
            {
                panel.Children.Add(Line(L.T("reports.link.tip.entities", ("count", entities.Count)), true));
                foreach (var entity in entities.Take(MaxListed))
                    panel.Children.Add(Line(Describe(entity) + " · " + (entity?.Handle ?? ""), false));
                if (entities.Count > MaxListed)
                    panel.Children.Add(Line(L.T("reports.link.tip.more", ("count", entities.Count - MaxListed)), false));
            }
            if (panel.Children.Count > 0)
                panel.Children.Add(new Border { Height = 1, Background = ReportsStyles.BorderBrush, Margin = new Thickness(0, 5, 0, 4) });

            var footer = new TextBlock { Foreground = ReportsStyles.TextSecondary, TextWrapping = TextWrapping.Wrap, Tag = "LinkTipFooter" };
            footer.SetBinding(TextBlock.TextProperty, new Binding(nameof(ReportLinkViewModel.IsLive))
            {
                Source = link,
                Converter = new FooterText(L.T("reports.link.tip.select"),
                    L.T("reports.link.otherDrawing", ("drawing", link.DimTooltip)))
            });
            panel.Children.Add(footer);
            return panel;
        }

        /// <summary>Type and layer, for example "Line · A-WALL"; the space only when it is not model space.</summary>
        private static string Describe(ReportEntity entity)
        {
            var space = entity?.Space;
            if (string.Equals(space, "*Model_Space", StringComparison.OrdinalIgnoreCase)) space = null;
            var parts = new[] { entity?.Type, entity?.Layer, space }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            return parts.Length > 0 ? string.Join(" · ", parts) : L.T("reports.link.entity");
        }

        private static TextBlock Line(string text, bool strong) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Foreground = strong ? ReportsStyles.Text : ReportsStyles.TextSecondary,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 1)
        };

        private sealed class FooterText : IValueConverter
        {
            private readonly string _live, _dim;
            public FooterText(string live, string dim) { _live = live; _dim = dim; }
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool live && live ? _live : _dim;
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
        }
    }
}
