using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyRenderingModeAsync(string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var saved = CurrentPreferences;
        SettingsWindow Create() => new(CurrentPreferences, ApplyPreferencesAsync) {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
            ShowInTaskbar = false, ShowActivated = false
        };
        try {
            var image = model.Image; var selected = ImageList.SelectedItem; var scale = zoom;
            var angle = Rotation.Angle; var x = Pan.X; var y = Pan.Y;
            var dialog = Create(); dialog.Show();
            ((TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 5;
            var mode = (ComboBox)dialog.FindName("RenderingMode"); mode.SelectedIndex = 1;
            Check(await dialog.ApplyChangesAsync() && dialog.IsVisible && softwareRendering
                && RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly, "Apply changes renderer without closing settings");
            Check(ReferenceEquals(image, model.Image) && ReferenceEquals(selected, ImageList.SelectedItem)
                && scale == zoom && angle == Rotation.Angle && x == Pan.X && y == Pan.Y,
                "Changing renderer preserves image, selection, scale, rotation and pan");
            dialog.UpdateLayout();
            var content = (FrameworkElement)dialog.Content;
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + ".rendering-settings.png");
            var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
            Check(bytes.Any(value => value != 0), "Software rendering produces visible settings content");
            dialog.ResetValue("rendering");
            Check(!dialog.Values.SoftwareRendering, "Renderer reset selects automatic mode");
            dialog.Close();
            Check(softwareRendering && RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly,
                "Cancel discards renderer edits after last Apply");
            var reset = Create(); reset.ResetValue("rendering");
            Check(await reset.ApplyChangesAsync() && !softwareRendering && RenderOptions.ProcessRenderMode == RenderMode.Default,
                "Applying initial value restores WPF automatic renderer");
            reset.Close();
        }
        finally { await ApplyPreferencesAsync(saved); }
    }
}
