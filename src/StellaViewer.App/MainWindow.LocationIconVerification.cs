using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyLocationIconsAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!, "location-icons");
        Directory.CreateDirectory(Path.Combine(root, "空のフォルダ"));
        await File.WriteAllTextAsync(Path.Combine(root, "書庫.zip"), "invalid archive");
        await File.WriteAllTextAsync(Path.Combine(root, "文書.PDF"), "PDF preview not yet supported");
        var entries = new[] { new LocationEntry(Path.Combine(root, "空のフォルダ"), "空のフォルダ", true),
            new LocationEntry(Path.Combine(root, "書庫.zip"), "書庫.zip", false),
            new LocationEntry(Path.Combine(root, "文書.PDF"), "文書.PDF", false) };
        var kinds = new[] { ViewerIconKind.Folder, ViewerIconKind.Archive, ViewerIconKind.Pdf };
        var modes = new[] { "LocationNameTemplate", "LocationDetailTemplate", "LocationTileTemplate" };
        var panel = new Grid { Width = 450, Height = 250, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        panel.SetResourceReference(Panel.BackgroundProperty, "PanelBackground");
        foreach (var height in new[] { 34, 86, 130 }) panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        for (var column = 0; column < 3; column++) panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        for (var mode = 0; mode < 3; mode++)
        for (var column = 0; column < 3; column++) {
            var control = new ContentControl { Content = entries[column], ContentTemplate = (DataTemplate)Resources[modes[mode]],
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetRow(control, mode); Grid.SetColumn(control, column); panel.Children.Add(control);
        }
        WindowSurface.Children.Add(panel);
        var savedDark = DarkTheme.IsChecked;
        try {
            UpdateLayout();
            var thumbnails = Descendants(panel).OfType<Thumbnail>().ToArray();
            for (var retry = 0; retry < 100 && thumbnails.Any(t => t.Source is null && t.ToolTip is null); retry++) await Task.Delay(30);
            Check(thumbnails.Length == 6 && thumbnails.All(t => t.Source is null && t.ToolTip is not null),
                "Empty folder, failed archive and unsupported PDF finish without previews");
            foreach (var dark in new[] { false, true }) {
                ApplyTheme(dark); UpdateLayout();
                foreach (ContentControl control in panel.Children) {
                    var column = Grid.GetColumn(control); var mode = Grid.GetRow(control);
                    var icon = Descendants(control).OfType<VectorIcon>().Single();
                    Check(icon.Kind == kinds[column] && icon.IsVisible, "Name, detail and tile show correct fallback kind");
                    var expectedSize = mode == 0 ? 16 : mode == 1 ? 32 : 36;
                    Check(Math.Abs(icon.ActualWidth - expectedSize) < .1 && Math.Abs(icon.ActualHeight - expectedSize) < .1,
                        "Location icon respects template size, with larger fallback in detail and tile modes");
                    Check(icon.Foreground is SolidColorBrush color && (dark ? color.Color.R > 180 : color.Color.R < 100),
                        "Location fallback icon is readable in both themes");
                    if (mode == 0) Check(!Descendants(control).OfType<Thumbnail>().Any(), "Name mode has an icon without decoding a thumbnail");
                }
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(panel), null, new Rect(0, 0, panel.Width, panel.Height));
                var bitmap = new RenderTargetBitmap(450, 250, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual); SaveRender(bitmap, screenshotPath + (dark ? ".location-fallback-dark.png" : ".location-fallback-light.png"));
            }
            var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4); pixel.Freeze();
            foreach (ContentControl control in panel.Children) {
                if (Grid.GetRow(control) == 0) continue;
                var preview = Descendants(control).OfType<Thumbnail>().Single();
                var icon = Descendants(control).OfType<VectorIcon>().Single();
                preview.Source = pixel; UpdateLayout();
                Check(!icon.IsVisible, "Successful preview hides fallback icon");
                preview.Source = null; UpdateLayout();
                Check(icon.IsVisible, "Removing preview restores fallback icon for recycled visual");
            }
        }
        finally { WindowSurface.Children.Remove(panel); ApplyTheme(savedDark); }
    }
}
