using System;
using System.Windows;
using System.Windows.Controls;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views.Toast;

internal static class PositionSettingsVisualTests
{
    internal static void Run()
    {
        var options = new ToastPositionOptions(offsetX: 30, offsetY: 50);
        var saved = true;
        var controls = new ToastPositionControls(() => options, value => { options = value; return saved; });
        if (((TextBlock)controls.Children[0]).Text != "Horizontal alignment") throw new Exception("Position label was not localized.");
        ((Button)controls.Children[5]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (options.HasOffset) throw new Exception("Reset did not clear the saved offset.");
        ((ComboBox)controls.Children[1]).SelectedIndex = 1;
        ((ComboBox)controls.Children[3]).SelectedIndex = 1;
        ((CheckBox)controls.Children[4]).IsChecked = true;
        if (!options.Right || !options.Bottom || !options.DragEnabled) throw new Exception("Position controls were not applied.");
        saved = false;
        ((Button)controls.Children[5]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!((TextBlock)controls.Children[6]).Text.Contains("could not be saved")) throw new Exception("Save failure was hidden.");
        Console.WriteLine("PASS: Settings applies corner/drag/reset immediately, localizes labels and reports save failure");
    }
}
