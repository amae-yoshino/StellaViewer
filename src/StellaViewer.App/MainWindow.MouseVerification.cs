using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyMouseAsync(string imagePath, string screenshotPath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var probe = new MainWindow(false) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        try {
            probe.Show(); await probe.OpenAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, "spread-4", "page1.png")); await probe.activeDisplay;
            probe.BeginPointer(new(20, 20)); probe.EndPointer(); await probe.activeDisplay;
            Check(probe.ImageList.SelectedIndex == 1, "Default left click still advances one image");
            probe.mouseSettings = probe.mouseSettings.Assign(ViewerMouseGesture.LeftClick, ViewerCommand.Previous);
            probe.BeginPointer(new(20, 20)); probe.EndPointer(); await probe.activeDisplay;
            Check(probe.ImageList.SelectedIndex == 0, "Assigned left click executes previous command");
            probe.mouseSettings = probe.mouseSettings.Assign(ViewerMouseGesture.LeftClick, null);
            probe.BeginPointer(new(20, 20)); probe.EndPointer();
            Check(probe.ImageList.SelectedIndex == 0, "Unassigned left click does not navigate");
            probe.SetZoom(1000); probe.UpdateLayout();
            probe.mouseSettings = probe.mouseSettings.Assign(ViewerMouseGesture.LeftClick, ViewerCommand.Next);
            probe.BeginPointer(new(20, 20)); probe.UpdatePointer(new(90, 90)); probe.EndPointer();
            Check(probe.ImageList.SelectedIndex == 0, "Dragging an oversized image suppresses assigned click");
            probe.ResetZoom();
            probe.mouseSettings = probe.mouseSettings.Assign(ViewerMouseGesture.RightClick, ViewerCommand.Next);
            probe.BeginPointer(new(20, 20), MouseButton.Right); probe.EndPointer(); await probe.activeDisplay;
            Check(probe.ImageList.SelectedIndex == 1, "Right click uses assigned command");
            probe.mouseSettings = probe.mouseSettings.Assign(ViewerMouseGesture.MiddleClick, ViewerCommand.ResetZoom);
            probe.SetZoom(175); probe.BeginPointer(new(20, 20), MouseButton.Middle); probe.EndPointer();
            Check(probe.automaticZoom && !probe.loupeEnabled, "Middle click reassignment replaces loupe operation");
            probe.mouseSettings = probe.mouseSettings.Assign(ViewerMouseGesture.WheelDown, ViewerCommand.Previous);
            probe.ViewerWheel(probe.Viewport, new(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent }); await probe.activeDisplay;
            Check(probe.ImageList.SelectedIndex == 0, "Wheel direction uses assigned command");
            await probe.VerifyDragAsync();
            var originalImage = probe.model.Image; var originalZoom = probe.zoom;
            var dialog = new SettingsWindow(probe.CurrentPreferences, probe.ApplyPreferencesAsync) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            try {
                dialog.Show(); dialog.ShowShortcuts();
                var saveButton = (Button)dialog.FindName("SaveButton");
                var buttonBar = (Panel)saveButton.Parent;
                Check(buttonBar.Children.Cast<Button>().Select(button => button.Content?.ToString()).SequenceEqual(new[] { "保存", "キャンセル", "適用" }), "Settings actions are ordered Save, Cancel, Apply");
                var rows = (ItemsControl)dialog.FindName("MouseRows"); rows.BringIntoView(); dialog.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var boxes = Descendants(rows).OfType<ComboBox>().ToArray();
                var panButton = (ComboBox)dialog.FindName("PanButtonBox");
                var dragPage = (CheckBox)dialog.FindName("DragPageEnabled");
                Check(boxes.Length == 5, "Mouse editor exposes five image-area gestures");
                boxes[0].SelectedIndex = 0; boxes[1].SelectedValue = ViewerCommand.Fullscreen;
                panButton.SelectedIndex = (int)ViewerDragButton.Middle; dragPage.IsChecked = true;
                Check(dialog.Values.Mouse.For(ViewerMouseGesture.LeftClick) is null && dialog.Values.Mouse.For(ViewerMouseGesture.RightClick) == ViewerCommand.Fullscreen, "Dropdown supports no action and command assignment");
                Check(probe.mouseSettings.For(ViewerMouseGesture.RightClick) == ViewerCommand.Next, "Draft changes do not affect viewer before Apply");
                Check(await dialog.ApplyChangesAsync() && dialog.IsVisible && probe.mouseSettings.For(ViewerMouseGesture.RightClick) == ViewerCommand.Fullscreen, "Apply changes mouse behavior and keeps settings open");
                Check(probe.mouseSettings.PanButton == ViewerDragButton.Middle && probe.mouseSettings.DragPageEnabled, "Apply commits drag controls with click assignments");
                Check(ReferenceEquals(originalImage, probe.model.Image) && originalZoom == probe.zoom, "Input settings preserve image and zoom");
                foreach (var dark in new[] { true, false }) {
                    probe.ApplyTheme(dark); probe.CopySettingsTheme(dialog); dragPage.BringIntoView(); dialog.UpdateLayout();
                    var content = (FrameworkElement)dialog.Content; var visual = new DrawingVisual();
                    using (var draw = visual.RenderOpen()) draw.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                    SaveRender(bitmap, screenshotPath + (dark ? ".mouse-dark.png" : ".mouse-light.png"));
                }
                boxes[1].SelectedValue = ViewerCommand.Loupe; dialog.Close();
                Check(probe.mouseSettings.For(ViewerMouseGesture.RightClick) == ViewerCommand.Fullscreen, "Cancel discards edits since latest Apply");
            }
            finally { dialog.Close(); }
            await probe.ExecuteMouseCommandAsync(ViewerMouseGesture.RightClick);
            Check(probe.fullscreen, "Mouse assignment can enter fullscreen");
            await probe.ExecuteMouseCommandAsync(ViewerMouseGesture.RightClick); Check(!probe.fullscreen, "Same mouse assignment can exit fullscreen");
            var reset = new SettingsWindow(probe.CurrentPreferences) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            try {
                reset.Show(); reset.ShowShortcuts(); reset.UpdateLayout();
                Descendants(reset).OfType<Button>().Single(button => button.Content?.ToString() == "マウスを初期値に戻す").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(reset.Values.Mouse.For(ViewerMouseGesture.RightClick) == ViewerCommand.Previous && reset.Values.Keyboard.For(ViewerCommand.Next).Key == "RIGHT", "Mouse reset leaves keyboard assignments intact");
                Check(reset.Values.Mouse.PanButton == ViewerDragButton.Left && !reset.Values.Mouse.DragPageEnabled, "Mouse reset restores left pan and disables drag paging");
            }
            finally { reset.Close(); }
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
