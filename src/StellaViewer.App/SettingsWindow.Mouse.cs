using System.Windows;
using System.Windows.Controls;
using StellaViewer.Core;

namespace StellaViewer.App;

public partial class SettingsWindow
{
    private sealed record MouseCommandOption(ViewerCommand? Command, string Name);
    private sealed class MouseBindingRow
    {
        public required ViewerMouseGesture Gesture { get; init; }
        public required string Name { get; init; }
        public required MouseCommandOption[] Options { get; init; }
        public ViewerCommand? Command { get; set; }
    }
    private MouseBindingRow[] mouseRows = [];
    private void SetMouseSettings(MouseSettings? value)
    {
        var settings = (value ?? new()).Normalize();
        MouseCommandOption[] options = [new(null, "何もしない"), ..Enum.GetValues<ViewerCommand>().Select(command => new MouseCommandOption(command, KeyboardCommands.Name(command)))];
        mouseRows = Enum.GetValues<ViewerMouseGesture>().Select(gesture => new MouseBindingRow {
            Gesture = gesture, Name = gesture switch {
                ViewerMouseGesture.LeftClick => "左クリック", ViewerMouseGesture.RightClick => "右クリック",
                ViewerMouseGesture.MiddleClick => "ホイールクリック", ViewerMouseGesture.WheelUp => "ホイール上", _ => "ホイール下"
            }, Options = options, Command = settings.For(gesture)
        }).ToArray();
        MouseRows.ItemsSource = mouseRows;
        PanButtonBox.SelectedIndex = (int)settings.PanButton;
        DragPageEnabled.IsChecked = settings.DragPageEnabled;
    }
    private MouseSettings ReadMouseSettings() => new MouseSettings {
        Bindings = mouseRows.ToDictionary(row => row.Gesture.ToString(), row => row.Command),
        PanButton = (ViewerDragButton)PanButtonBox.SelectedIndex, DragPageEnabled = DragPageEnabled.IsChecked == true
    }.Normalize();
    private void ResetMouseClicked(object sender, RoutedEventArgs e) => SetMouseSettings(new());
}
