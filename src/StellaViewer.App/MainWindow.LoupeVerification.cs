using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyLoupeMatrixAsync(string imagePath,string reportPath)
    {
        var saved=CurrentPreferences;
        var rendering=System.Windows.Media.RenderOptions.ProcessRenderMode;
        try {
            foreach(var software in new[]{false,true}) {
                System.Windows.Media.RenderOptions.ProcessRenderMode=software?System.Windows.Interop.RenderMode.SoftwareOnly:System.Windows.Interop.RenderMode.Default;
                foreach(var dark in new[]{false,true})foreach(var scale in new[]{100,125,150}) {
                    var label=$"{(software?"software":"default")}-{(dark?"dark":"light")}-{scale}";
                    await ApplyPreferencesAsync(saved with{Dark=dark,UiSize=saved.UiSize with{ScalePercent=scale}});
                    await VerifyLoupeAsync(imagePath,reportPath+"."+label);
                    await File.AppendAllTextAsync(reportPath,"PASS: loupe pixels/rotation/grid/spread/alpha "+label+Environment.NewLine);
                }
            }
        }
        finally{System.Windows.Media.RenderOptions.ProcessRenderMode=rendering;await ApplyPreferencesAsync(saved);}
    }
    internal async Task VerifyLoupeAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var saved = CurrentPreferences;
        RenderTargetBitmap Capture(string suffix) {
            UpdateLayout(); RefreshLoupe(); UpdateLayout();
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawRectangle(new VisualBrush(Viewport), null, new Rect(0, 0, Viewport.ActualWidth, Viewport.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Viewport.ActualWidth), (int)Math.Ceiling(Viewport.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + suffix); return bitmap;
        }
        byte[] Pixel(BitmapSource bitmap, Point point) {
            var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0); return pixel;
        }
        Point SourcePoint(double x, double y) => Picture.TransformToAncestor(Viewport).Transform(
            new Point(x * Picture.Width / decoded!.Width, y * Picture.Height / decoded.Height));
        Point Center() => new(LoupeLayer.LensBounds.Left + LoupeLayer.LensBounds.Width / 2,
            LoupeLayer.LensBounds.Top + LoupeLayer.LensBounds.Height / 2);
        void MiddleClick() {
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.MouseDownEvent };
            Viewport.RaiseEvent(down);
            Check(down.Handled, "Viewport handles middle click for loupe");
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.MouseUpEvent };
            Viewport.RaiseEvent(up); Check(up.Handled, "Middle release does not become page navigation");
        }
        try {
            await ApplyPreferencesAsync(CurrentPreferences with { SpreadMode = false, Loupe = new(), Appearance = new() });
            await OpenAsync(imagePath); await activeDisplay;
            ClearImage();
            const int width = 400, height = 300;
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) {
                var offset = (y * width + x) * 4; pixels[offset + (x < 200 ? 2 : 0)] = 255; pixels[offset + 3] = 255;
            }
            model.Image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            decoded = new(width, height, width, height, []); model.Message = ""; automaticZoom = false; SetZoom(100); UpdateLayout();
            var image = model.Image; var selected = ImageList.SelectedItem; var initialZoom = zoom;
            Check(!loupeEnabled, "Loupe starts disabled");
            MiddleClick(); UpdateLoupePointer(SourcePoint(170, 150));
            var rendered = Capture(".loupe-square.png");
            Check(loupeEnabled && LoupeLayer.Visibility == Visibility.Visible && LoupeLayer.LensBounds.Width == 240
                && LoupeLayer.LensBounds.Height == 240, "Middle click toggles persistent square loupe");
            Check(ReferenceEquals(image, model.Image) && ReferenceEquals(selected, ImageList.SelectedItem) && zoom == initialZoom,
                "Middle click leaves selected page and main image scale unchanged");
            var center = Center(); var first = Pixel(rendered, center);
            var offsetVector = SourcePoint(210, 150) - SourcePoint(170, 150);
            var next = Pixel(rendered, center + offsetVector * 2);
            Check(first[2] > 230 && first[0] < 20 && next[0] > 230 && next[2] < 20,
                "Loupe draws source pixels at twice displayed scale around pointer");
            RotateRight(this, new RoutedEventArgs()); SetZoom(300); Pan.X = 15; Pan.Y = -12; ClampPan(); UpdateLayout();
            UpdateLoupePointer(SourcePoint(190, 150));
            rendered = Capture(".loupe-rotated.png"); center = Center();
            offsetVector = SourcePoint(205, 150) - SourcePoint(190, 150);
            first = Pixel(rendered, center); next = Pixel(rendered, center + offsetVector * 2);
            Check(first[2] > 230 && next[0] > 230, "Loupe sampling follows rotation, zoom and pan");
            Rotation.Angle = 0; SetZoom(100); UpdateLayout();
            ApplyAppearance(new(GridVisible: true, GridSpacing: 20, GridColor: "#00FF00", GridOpacity: 100));
            UpdateLoupePointer(SourcePoint(170, 150)); rendered = Capture(".loupe-grid.png");
            offsetVector = SourcePoint(180.25, 150) - SourcePoint(170, 150);
            var gridPixel = Pixel(rendered, Center() + offsetVector * 2);
            Check(gridPixel[1] > 200 && gridPixel[2] < 50, $"Loupe enlarges image-coordinate grid with source: BGRA={string.Join(',',gridPixel)}, sample={Center()+offsetVector*2}, zoom={zoom}, UI={CurrentPreferences.UiSize.ScalePercent}");
            ApplyAppearance(new());
            UpdateLoupePointer(new Point(1, 1)); Capture(".loupe-edge.png");
            Check(LoupeLayer.LensBounds.Left >= 0 && LoupeLayer.LensBounds.Top >= 0
                && LoupeLayer.LensBounds.Right <= Viewport.ActualWidth && LoupeLayer.LensBounds.Bottom <= Viewport.ActualHeight,
                "Square loupe stays inside viewer at edges");
            loupePointerInside = false; RefreshLoupe();
            Check(loupeEnabled && LoupeLayer.Visibility == Visibility.Collapsed, "Pointer leave hides loupe without disabling mode");
            UpdateLoupePointer(SourcePoint(170, 150)); Check(LoupeLayer.Visibility == Visibility.Visible, "Pointer return restores loupe");
            MiddleClick(); Check(!loupeEnabled && LoupeLayer.Visibility == Visibility.Collapsed, "Second middle click disables loupe");

            var root = Path.GetDirectoryName(imagePath)!;
            await OpenAsync(Path.Combine(root, "spread-4", "page2.png")); await activeDisplay; await SetSpreadModeAsync(true); UpdateLayout();
            ToggleLoupe(SourcePoint(firstPageRect.X + firstPageRect.Width / 2, firstPageRect.Y + firstPageRect.Height / 2));
            rendered = Capture(".loupe-spread.png"); first = Pixel(rendered, Center());
            Check(first[0] > 230 && first[2] < 20, "Loupe supports right-bound DrawingImage spread source");
            var pageBefore = ImageList.SelectedItem;
            var previousControlWheel = controlWheel; controlWheel = false;
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent };
            Viewport.RaiseEvent(wheel); controlWheel = previousControlWheel;
            await activeDisplay; UpdateLayout(); RefreshLoupe();
            Check(!ReferenceEquals(pageBefore, ImageList.SelectedItem) && loupeEnabled && LoupeLayer.Visibility == Visibility.Visible,
                "Wheel still advances pages while loupe mode stays enabled");

            await SetSpreadModeAsync(false);
            await OpenAsync(Path.Combine(root, "alpha.png")); await activeDisplay;
            ApplyAppearance(new(1, "#0000FF")); SetZoom(1000); UpdateLayout();
            UpdateLoupePointer(SourcePoint(1, 1)); rendered = Capture(".loupe-alpha.png"); first = Pixel(rendered, Center());
            Check(first[0] is > 100 and < 150 && first[2] is > 100 and < 150, "Loupe preserves source alpha over configured background");

            LoupeSettings? applied = null;
            var precise = new SettingsWindow(CurrentPreferences with { Loupe = new(2.125, 240) });
            Check(precise.Values.Loupe.Magnification == 2.125, "Opening settings preserves entered loupe precision"); precise.Close();
            var dialog = new SettingsWindow(CurrentPreferences, nextSettings => { applied = nextSettings.Loupe; return Task.CompletedTask; }) {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
                ShowInTaskbar = false, ShowActivated = false
            };
            dialog.Show(); ((TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 4;
            var factor = (TextBox)dialog.FindName("LoupeMagnification"); var size = (TextBox)dialog.FindName("LoupeSize");
            factor.Text = "0";
            Check(!await dialog.ApplyChangesAsync() && applied is null && dialog.IsVisible, "Invalid loupe input does not apply or close settings");
            factor.Text = "3"; size.Text = "320";
            Check(await dialog.ApplyChangesAsync() && applied == new LoupeSettings(3, 320) && dialog.IsVisible,
                "Loupe settings Apply keeps dialog open");
            dialog.ResetValue("loupe"); dialog.Close();
            Check(applied == new LoupeSettings(3, 320), "Cancel drops loupe changes made after Apply");
            await ApplyPreferencesAsync(CurrentPreferences with { Loupe = applied! });
            Capture(".loupe-size.png");
            Check(LoupeLayer.LensBounds.Width == 320, "Applied loupe size changes rendering");
            await OpenAsync(Path.Combine(root, "broken.png")); await activeDisplay;
            Check(loupeEnabled && LoupeLayer.Visibility == Visibility.Collapsed, "Failed image clears loupe source");
            ClearImage(); Check(LoupeLayer.Visibility == Visibility.Collapsed, "Empty display has no loupe");
        }
        finally {
            loupeEnabled = loupePointerInside = false; LoupeLayer.Clear();
            await ApplyPreferencesAsync(saved); await OpenAsync(imagePath); await activeDisplay;
        }
    }
}
