using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private ViewerAppearance appearance = new();

    private async void GridToggleClicked(object sender, RoutedEventArgs e)
    {
        try { await SetGridVisibleAsync(GridToggle.IsChecked == true); }
        catch (Exception error) { model.Status = "グリッド表示設定を保存できませんでした：" + error.Message; }
    }

    private async Task SetGridVisibleAsync(bool visible)
    {
        var before = appearance;
        var next = appearance with { GridVisible = visible };
        appearance = next; UpdateImageGrid();
        if (usePreferences)
            await SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(appearance: next, previousAppearance: before, filePath: preferenceFilePath));
    }

    private void ApplyAppearance(ViewerAppearance? value)
    {
        appearance = (value ?? new()).Normalize();
        if (appearance.BackgroundMode == 0)
            Viewport.SetResourceReference(Panel.BackgroundProperty, "ViewerBackground");
        else if (appearance.BackgroundMode == 1)
            Viewport.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(appearance.BackgroundColor));
        else {
            var size = appearance.CheckerSize;
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush((Color)ColorConverter.ConvertFromString(appearance.CheckerColor2)), null, new RectangleGeometry(new Rect(0, 0, size * 2, size * 2))));
            var first = new SolidColorBrush((Color)ColorConverter.ConvertFromString(appearance.CheckerColor1));
            drawing.Children.Add(new GeometryDrawing(first, null, new RectangleGeometry(new Rect(0, 0, size, size))));
            drawing.Children.Add(new GeometryDrawing(first, null, new RectangleGeometry(new Rect(size, size, size, size))));
            var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, size * 2, size * 2) };
            brush.Freeze(); Viewport.Background = brush;
        }
        UpdateImageGrid();
    }

    private void UpdateImageGrid()
    {
        GridToggle.IsChecked = appearance.GridVisible;
        ImageGrid.Visibility = appearance.GridVisible && decoded is not null ? Visibility.Visible : Visibility.Collapsed;
        if (ImageGrid.Visibility != Visibility.Visible || decoded is null) { RefreshLoupe(); return; }
        ImageGrid.Width = Picture.Width; ImageGrid.Height = Picture.Height;
        Canvas.SetLeft(ImageGrid, Canvas.GetLeft(Picture)); Canvas.SetTop(ImageGrid, Canvas.GetTop(Picture));
        var width = appearance.GridSpacing * Picture.Width / decoded.Width;
        var height = appearance.GridSpacing * Picture.Height / decoded.Height;
        var dpi = VisualTreeHelper.GetDpi(this);
        var color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(appearance.GridColor));
        var drawing = new DrawingGroup();
        // Filled strips stay one physical pixel wide while their spacing follows image coordinates.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, width, height))));
        drawing.Children.Add(new GeometryDrawing(color, null, new RectangleGeometry(new Rect(0, 0, Math.Min(width, 1 / dpi.DpiScaleX), height))));
        drawing.Children.Add(new GeometryDrawing(color, null, new RectangleGeometry(new Rect(0, 0, width, Math.Min(height, 1 / dpi.DpiScaleY)))));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, width, height) };
        brush.Freeze();
        if (spreadEnabled) {
            var pages = new DrawingGroup();
            pages.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Picture.Width, Picture.Height))));
            var sx = Picture.Width / decoded.Width; var sy = Picture.Height / decoded.Height;
            void AddGrid(PageView? page, Rect area) {
                if (page?.Info is null) return;
                // Each page starts its own grid at its image origin.
                var tile = brush.Clone();
                tile.Viewport = new Rect(area.X * sx, area.Y * sy, width, height); tile.Freeze();
                pages.Children.Add(new GeometryDrawing(tile, null,
                    new RectangleGeometry(new Rect(area.X * sx, area.Y * sy, area.Width * sx, area.Height * sy))));
            }
            AddGrid(firstPage, firstPageRect); AddGrid(secondPage, secondPageRect);
            var combined = new DrawingBrush(pages); combined.Freeze(); ImageGrid.Fill = combined;
        }
        else ImageGrid.Fill = brush;
        ImageGrid.Opacity = appearance.GridOpacity / 100.0;
        RefreshLoupe();
    }
}
