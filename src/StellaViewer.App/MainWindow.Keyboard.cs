using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using StellaViewer.Core;

namespace StellaViewer.App;

public partial class MainWindow
{
    private KeyboardSettings keyboardSettings = new();
    private void ApplyKeyboardSettings(KeyboardSettings? value)
    {
        keyboardSettings = KeyboardCommands.Normalize(value);
        ReloadMenu.InputGestureText = Hint(ViewerCommand.Reload);
        PreviousPageMenu.InputGestureText = Hint(ViewerCommand.Previous);
        NextPageMenu.InputGestureText = Hint(ViewerCommand.Next);
        FirstPageMenu.InputGestureText = Hint(ViewerCommand.First);
        LastPageMenu.InputGestureText = Hint(ViewerCommand.Last);
        string Hint(ViewerCommand command) => string.Join(" / ", keyboardSettings.AllFor(command).Select(KeyboardCommands.Format));
    }
    private static bool IsKeyboardInput(DependencyObject? source)
    {
        for (var element = source; element is not null;) {
            if (element is TextBoxBase or PasswordBox or ComboBox or MenuItem) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : (element as FrameworkContentElement)?.Parent;
        }
        return false;
    }
    private async Task<bool> TryShortcutAsync(Key key, ModifierKeys modifiers, DependencyObject? source)
    {
        if (IsKeyboardInput(source)) return false;
        var gesture = KeyboardCommands.Gesture(key, modifiers); if (gesture.IsEmpty) return false;
        foreach (var command in Enum.GetValues<ViewerCommand>()) {
            if (!keyboardSettings.AllFor(command).Contains(gesture)) continue;
            await ExecuteViewerCommandAsync(command);
            return true;
        }
        return false;
    }
    private async Task ExecuteViewerCommandAsync(ViewerCommand command)
    {
        try {
            switch (command) {
                case ViewerCommand.Previous: Move(-1); break;
                case ViewerCommand.Next: Move(1); break;
                case ViewerCommand.First: MoveToBoundary(false); break;
                case ViewerCommand.Last: MoveToBoundary(true); break;
                case ViewerCommand.Reload: await ReloadAsync(); break;
                case ViewerCommand.Fullscreen: ToggleFullscreen(); break;
                case ViewerCommand.ResetZoom: ResetZoom(); break;
                case ViewerCommand.RotateLeft: RotateLeft(this, new()); break;
                case ViewerCommand.RotateRight: RotateRight(this, new()); break;
                case ViewerCommand.Grid: await SetGridVisibleAsync(!appearance.GridVisible); break;
                case ViewerCommand.Spread: await SetSpreadModeAsync(!spreadEnabled); break;
                case ViewerCommand.Loupe: ToggleLoupe(Mouse.GetPosition(Viewport)); break;
                case ViewerCommand.Back: await (navigation = GoBackAsync()); break;
                case ViewerCommand.Forward: await (navigation = GoForwardAsync()); break;
                case ViewerCommand.Up: await UpAsync(); break;
                case ViewerCommand.PageJump: FocusPageClick(this, new()); break;
                case ViewerCommand.Theme: ThemeButtonClicked(this, new()); break;
                case ViewerCommand.Settings: ShowSettings(); break;
                case ViewerCommand.Slideshow: SlideshowClicked(this, new()); break;
            }
        }
        catch (Exception error) { model.Status = "操作を実行できませんでした：" + error.Message; }
    }
}
