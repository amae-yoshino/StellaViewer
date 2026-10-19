using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Core;

namespace StellaViewer.App;

public partial class SettingsWindow
{
    private sealed record ShortcutKeyRow(ViewerCommand Command, ShortcutGesture Key, string Separator) { public string Gesture => KeyboardCommands.Format(Key); }
    private sealed record ShortcutRow(ViewerCommand Command, string Name, ShortcutKeyRow[] Keys) { public string EmptyHint => Keys.Length == 0 ? "未割り当て" : ""; }
    private KeyboardSettings editedKeyboard = new();
    private ViewerCommand? capturing;
    private ShortcutGesture? replacing;
    private void RefreshShortcutRows() => ShortcutRows.ItemsSource = Enum.GetValues<ViewerCommand>()
        .Select(command => new ShortcutRow(command, KeyboardCommands.Name(command), editedKeyboard.AllFor(command).Select((key, index) => new ShortcutKeyRow(command, key, index == 0 ? "" : ", ")).ToArray())).ToArray();
    internal bool AssignShortcut(ViewerCommand command, ShortcutGesture gesture, bool replace, out ViewerCommand? conflict)
    {
        conflict = editedKeyboard.Conflict(command, gesture);
        if (conflict is not null && !replace) return false;
        editedKeyboard = editedKeyboard.Assign(command, gesture, replace); RefreshShortcutRows(); return true;
    }
    internal bool EditShortcut(ViewerCommand command, ShortcutGesture? previous, ShortcutGesture gesture, bool replace, out ViewerCommand? conflict)
    {
        conflict = editedKeyboard.Conflict(command, gesture);
        if (conflict is not null && !replace) return false;
        var next = previous is null ? editedKeyboard : editedKeyboard.Remove(command, previous);
        editedKeyboard = next.Add(command, gesture, replace); RefreshShortcutRows(); return true;
    }
    private void AddShortcutClicked(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: ShortcutRow row }) StartCapture(row.Command, null); }
    private void ChangeShortcutClicked(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: ShortcutKeyRow row }) StartCapture(row.Command, row.Key); }
    private void StartCapture(ViewerCommand command, ShortcutGesture? previous)
    {
        capturing = command; replacing = previous; CapturePanel.Visibility = Visibility.Visible;
        CaptureHint.Text = $"「{KeyboardCommands.Name(command)}」のキーを押してください。Escで取り消します。";
        ShortcutCapture.Text = "キー入力待ち…"; ShortcutCapture.Focus();
        CapturePanel.BringIntoView();
    }
    private void StopCapture()
    { capturing = null; replacing = null; CapturePanel.Visibility = Visibility.Collapsed; ApplyButton.Focus(); }
    private void ShortcutCaptureKey(object sender, KeyEventArgs e)
    {
        if (capturing is not ViewerCommand command) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { StopCapture(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) return;
        var gesture = KeyboardCommands.Gesture(key, Keyboard.Modifiers);
        if (gesture.IsEmpty) { CaptureHint.Text = "このキーは割り当てできません。別のキーを押してください。"; return; }
        ShortcutCapture.Text = KeyboardCommands.Format(gesture);
        if (!EditShortcut(command, replacing, gesture, false, out var conflict)) {
            if (MessageBox.Show(this, $"このキーは「{KeyboardCommands.Name(conflict!.Value)}」で使われています。\n元の割り当てを解除して「{KeyboardCommands.Name(command)}」に変更しますか？",
                "キーの重複", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            EditShortcut(command, replacing, gesture, true, out _);
        }
        StopCapture();
    }
    private void ClearShortcutClicked(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: ShortcutKeyRow row }) { editedKeyboard = editedKeyboard.Remove(row.Command, row.Key); RefreshShortcutRows(); StopCapture(); } }
    private void ResetShortcutClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ShortcutRow row }) return;
        StopCapture();
        var gesture = new KeyboardSettings().For(row.Command);
        if (!AssignShortcut(row.Command, gesture, false, out var conflict)) {
            if (MessageBox.Show(this, $"初期キーは「{KeyboardCommands.Name(conflict!.Value)}」で使われています。割り当てを置き換えますか？",
                "キーの重複", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
                AssignShortcut(row.Command, gesture, true, out _);
        }
    }
    private void ResetAllShortcutsClicked(object sender, RoutedEventArgs e)
    { editedKeyboard = new(); RefreshShortcutRows(); StopCapture(); }
}
