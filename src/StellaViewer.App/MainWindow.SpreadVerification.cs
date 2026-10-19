using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifySpreadAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var saved = CurrentPreferences;
        var root = Path.GetDirectoryName(imagePath)!;
        async Task OpenPage(int count, int page) {
            await OpenAsync(Path.Combine(root, $"spread-{count}", $"page{page}.png")); await activeDisplay;
        }
        void Pair(string first, string? second) => Check(firstPage?.Entry.Name == first && secondPage?.Entry.Name == second,
            $"Spread expected {first} / {second ?? "blank"}, actual {firstPage?.Entry.Name} / {secondPage?.Entry.Name}");
        void Capture(string suffix) {
            UpdateLayout(); var content = (FrameworkElement)Content;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + suffix);
        }
        try {
            SearchBox.Text = ""; SelectedImageSort = 0; await FilterAsync(); await activeDisplay;
            await OpenPage(4, 2); var selected = ImageList.SelectedItem;
            SetZoom(450); Rotation.Angle = 90;
            await SetSpreadModeAsync(true); Pair("page2.png", "page3.png");
            Check(SpreadToggle.IsChecked == true && ReferenceEquals(selected, ImageList.SelectedItem)
                && Rotation.Angle == 0 && Pan.X == 0 && Pan.Y == 0 && zoom == Zoom.Initial(Fit()), "Spread toggle preserves selected page and resets transform");
            Check(rightBinding && firstPageRect.Left > secondPageRect.Left && model.Image is DrawingImage,
                "Right-bound spread uses separate source images without composite raster allocation");
            Check(model.Status.StartsWith("2–3 / 4") && model.Details.Contains("右ページ") && model.Details.Contains("左ページ"), "Spread position and per-page information");
            Capture(".spread-right.png");
            Move(1); await activeDisplay; Pair("page4.png", "page1.png");
            Move(1); await activeDisplay; Pair("page2.png", "page3.png");
            Move(-1); await activeDisplay; Pair("page4.png", "page1.png");
            await OpenPage(5, 2); Pair("page2.png", "page3.png");
            Move(1); await activeDisplay; Pair("page4.png", "page5.png");
            Move(1); await activeDisplay; Pair("page1.png", null);
            Check(model.Status.StartsWith("1＋空白 / 5") && decoded!.Width == firstPage!.Info!.Width * 2, "Odd spread reserves one blank page");
            Capture(".spread-blank.png");
            Move(1); await activeDisplay; Pair("page2.png", "page3.png");
            Move(-1); await activeDisplay; Pair("page1.png", null);
            SelectPageAt(ImageList.SelectedIndex); await activeDisplay; Pair("page1.png", "page2.png");
            await ApplyPreferencesAsync(CurrentPreferences with { RightBinding = false });
            Check(firstPageRect.Left < secondPageRect.Left && !CurrentPreferences.RightBinding, "Left binding changes page placement");
            await ApplyPreferencesAsync(CurrentPreferences with { Appearance = appearance with { GridVisible = true, GridSpacing = 20 } });
            SetZoom(200); RotateRight(this, new RoutedEventArgs());
            Check(Rotation.Angle == 90 && ImageGrid.Visibility == Visibility.Visible, "Grid and rotation apply to complete spread");
            Check(zoom == Zoom.Initial(Fit()) && Pan.X == 0 && Pan.Y == 0 && !CanPan,
                "Rotation refits and centers the complete spread");
            Capture(".spread-left-rotated-grid.png");
            SelectPageAt(3); await activeDisplay;
            SearchBox.Text = "page"; await FilterAsync(); await activeDisplay;
            Pair("page4.png", "page5.png"); Check(spreadGroup == 0, "Search rebases spread at retained selection");
            SelectedImageSort = 2; await FilterAsync(); await activeDisplay;
            Check(firstPage?.Entry.Path == (ImageList.SelectedItem as FileEntry)?.Path && spreadGroup == 0, "Sort preserves selected page and rebases spread");
            SearchBox.Text = "page1"; await FilterAsync(); await activeDisplay; Pair("page1.png", null);
            SearchBox.Text = "no matching page"; await FilterAsync(); await activeDisplay;
            Check(model.Image is null && ImageGrid.Visibility == Visibility.Collapsed, "Zero-result spread search clears image and grid");
            SearchBox.Text = ""; SelectedImageSort = 0; await FilterAsync(); await activeDisplay;
            await OpenPage(1, 1); Pair("page1.png", null);
            Move(-1); await activeDisplay; Pair("page1.png", null);
            await OpenAsync(Path.Combine(root, "spread-failure", "page1.png")); await activeDisplay;
            Check(firstPage?.Error is not null && secondPage?.Info is not null && model.HasImage, "One failed page leaves partner visible");
            Capture(".spread-failure.png");
            Move(1); await activeDisplay;
            Check(!model.HasImage && model.Message.Contains("読み込み失敗"), "Both failed pages retain failure message");
            BeginPointer(new Point(10, 10)); EndPointer(); await activeDisplay;
            Check(firstPage?.Entry.Name == "page1.png" && secondPage?.Info is not null, "Click advances past failed spread");
            var old = OpenAsync(Path.Combine(root, "spread-4", "page2.png"));
            await OpenPage(5, 4); await old; await activeDisplay; Pair("page4.png", "page5.png");
            await SetSpreadModeAsync(false);
            Check(model.Image is BitmapSource && SpreadToggle.IsChecked == false && (ImageList.SelectedItem as FileEntry)?.Name == "page4.png",
                "Switching back to single page preserves current first page");
            var defaults = new SettingsWindow(new ViewerPreferences { SpreadMode = true, RightBinding = false });
            defaults.ResetValue("spread");
            Check(!defaults.Values.SpreadMode && defaults.Values.RightBinding, "Spread settings reset independently"); defaults.Close();
        }
        finally {
            SearchBox.Text = ""; await ApplyPreferencesAsync(saved);
            await OpenAsync(imagePath); await activeDisplay;
        }
    }
}
