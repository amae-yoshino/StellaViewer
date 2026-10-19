using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyAppearanceAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var original = appearance;
        RenderTargetBitmap Capture(string suffix) {
            UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Viewport.ActualWidth), (int)Math.Ceiling(Viewport.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawRectangle(new VisualBrush(Viewport), null, new Rect(0, 0, Viewport.ActualWidth, Viewport.ActualHeight));
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + suffix); return bitmap;
        }
        byte[] Pixel(BitmapSource source, int x, int y) {
            var pixel = new byte[4]; source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel;
        }
        try {
            await OpenAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, "alpha.png")); await activeDisplay;
            SetZoom(1000);
            ApplyAppearance(new(1, "#0000FF"));
            var solid = Capture(".background-solid.png");
            var center = Pixel(solid, (int)(Viewport.ActualWidth / 2), (int)(Viewport.ActualHeight / 2));
            Check(center[0] is > 100 and < 150 && center[2] is > 100 and < 150, "Transparent image blends with specified background");
            ApplyAppearance(new(2));
            var checker = Capture(".background-checker.png");
            Check(Pixel(checker, 2, 2)[0] != Pixel(checker, 14, 2)[0], "Checker background has alternating visible tiles");
            ApplyAppearance(new(2, CheckerColor1: "#FF0000", CheckerColor2: "#0000FF", CheckerSize: 20));
            var customChecker = Capture(".background-checker-custom.png");
            Check(Pixel(customChecker, 2, 2)[2] == 255 && Pixel(customChecker, 22, 2)[0] == 255
                && Pixel(customChecker, 22, 22)[2] == 255 && Pixel(customChecker, 42, 2)[2] == 255,
                "Custom checker uses both colors and repeats at twice the chosen cell size");
            SetZoom(500);
            var zoomedChecker = Capture(".background-checker-zoom.png");
            Check(Pixel(zoomedChecker, 22, 2)[0] == 255, "Background cell size remains fixed when image zoom changes");
            ApplyAppearance(new()); ApplyTheme(true);
            Check(Viewport.Background is SolidColorBrush dark && dark.Color.R == 25, "Theme background follows dark theme");
            ApplyTheme(false);
            await OpenAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, "grid.png")); await activeDisplay;
            ApplyAppearance(new(GridVisible: true, GridSpacing: 20, GridColor: "#00FF00", GridOpacity: 100));
            SetZoom(200); UpdateLayout();
            var rendered = Capture(".grid.png");
            var period = 20 * Picture.Width / decoded!.Width;
            var middleLine = Math.Floor(Picture.Width / 2 / period) * period;
            var sampleY = Math.Floor(Picture.Height / 2 / period) * period + 8;
            var linePoint = Picture.TransformToAncestor(Viewport).Transform(new Point(middleLine + .4, sampleY));
            var gapPoint = Picture.TransformToAncestor(Viewport).Transform(new Point(middleLine + period / 2, sampleY));
            var line = Pixel(rendered, (int)linePoint.X, (int)linePoint.Y);
            var gap = Pixel(rendered, (int)gapPoint.X, (int)gapPoint.Y);
            Check(line[1] > gap[1] + 40 && line[2] < gap[2], "Rendered grid lines follow source pixel interval");
            Rotation.Angle = 90; SetZoom(300); Pan.X = 13; Pan.Y = -17; ClampPan(); UpdateLayout();
            Check(ImageGrid.RenderTransform == Picture.RenderTransform && ImageGrid.Width == Picture.Width
                && ImageGrid.Height == Picture.Height && Canvas.GetLeft(ImageGrid) == Canvas.GetLeft(Picture)
                && Canvas.GetTop(ImageGrid) == Canvas.GetTop(Picture), "Grid shares rotated, panned and zoomed image geometry");
            Capture(".grid-rotated.png");
            var selectedBeforeToggle = ImageList.SelectedItem; var imageBeforeToggle = model.Image; var zoomBeforeToggle = zoom;
            await SetGridVisibleAsync(false);
            Check(GridToggle.IsChecked == false && ImageGrid.Visibility == Visibility.Collapsed, "Toolbar hides grid and updates checked state");
            await SetGridVisibleAsync(true);
            Check(GridToggle.IsChecked == true && ImageGrid.Visibility == Visibility.Visible && CurrentPreferences.Appearance.GridVisible,
                "Toolbar shows grid and synchronizes settings");
            Check(ReferenceEquals(selectedBeforeToggle, ImageList.SelectedItem) && ReferenceEquals(imageBeforeToggle, model.Image)
                && zoom == zoomBeforeToggle && Rotation.Angle == 90, "Grid toggle preserves image, selection, zoom and rotation");
            var emptySettings = new SettingsWindow(CurrentPreferences);
            emptySettings.ResetValue("background");
            Check(emptySettings.Values.Appearance.BackgroundMode == 0 && emptySettings.Values.Appearance.GridVisible, "Background reset preserves grid settings");
            emptySettings.ResetValue("grid");
            Check(emptySettings.Values.Appearance == new ViewerAppearance(), "Grid reset restores default fields");
            emptySettings.Close();
            ViewerAppearance? applied = null;
            var settings = new SettingsWindow(CurrentPreferences, next => { applied = next.Appearance; return Task.CompletedTask; }) {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
                ShowInTaskbar = false, ShowActivated = false
            };
            settings.Show();
            ((TabControl)settings.FindName("SettingsTabs")).SelectedIndex = 2;
            var spacingInput = (TextBox)settings.FindName("GridSpacing");
            var checkerSizeInput = (TextBox)settings.FindName("CheckerSize");
            var checkerColorInput = (TextBox)settings.FindName("CheckerColor1");
            checkerSizeInput.Text = "0";
            Check(!await settings.ApplyChangesAsync() && applied is null, "Invalid checker size is rejected");
            checkerSizeInput.Text = "24"; checkerColorInput.Text = "bad";
            Check(!await settings.ApplyChangesAsync() && applied is null, "Invalid checker color is rejected");
            checkerColorInput.Text = "#123abc";
            spacingInput.Text = "0";
            Check(!await settings.ApplyChangesAsync() && applied is null && settings.IsVisible, "Invalid grid input does not apply or close settings");
            spacingInput.Text = "40";
            Check(await settings.ApplyChangesAsync() && applied?.GridSpacing == 40 && settings.IsVisible, "Valid appearance Apply keeps settings open");
            Check(applied is { CheckerSize: 24, CheckerColor1: "#123ABC" }, "Checker settings apply and normalize colors");
            settings.UpdateLayout();
            var settingsContent = (FrameworkElement)settings.Content;
            var settingsBitmap = new RenderTargetBitmap((int)settingsContent.ActualWidth, (int)settingsContent.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            settingsBitmap.Render(settingsContent); SaveRender(settingsBitmap, screenshotPath + ".appearance-settings.png");
            spacingInput.Text = "80"; settings.Close();
            Check(applied?.GridSpacing == 40, "Cancel discards appearance changes after last Apply");
            var resetChecker = new SettingsWindow(CurrentPreferences with { Appearance = applied! });
            resetChecker.ResetValue("checker");
            Check(resetChecker.Values.Appearance is { CheckerColor1: "#F0F0F0", CheckerColor2: "#CDCDCD", CheckerSize: 12, GridSpacing: 40 },
                "Checker reset preserves unrelated appearance values");
            resetChecker.Close();
            ClearImage(); Check(ImageGrid.Visibility == Visibility.Collapsed, "Clearing image also clears grid");
        }
        finally { ApplyTheme(false); ApplyAppearance(original); await OpenAsync(imagePath); await activeDisplay; }
    }
}
