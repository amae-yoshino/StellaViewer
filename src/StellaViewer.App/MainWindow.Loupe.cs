using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private LoupeSettings loupeSettings = new();
    private bool loupeEnabled, loupePointerInside;
    private Point loupePoint;

    private void ToggleLoupe(Point point)
    {
        loupeEnabled = !loupeEnabled;
        loupePointerInside = true; loupePoint = point; RefreshLoupe();
    }
    private void UpdateLoupePointer(Point point)
    { loupePointerInside = true; loupePoint = point; RefreshLoupe(); }
    private void ViewerPointerEntered(object sender, MouseEventArgs e) => UpdateLoupePointer(e.GetPosition(Viewport));
    private void ImageLayoutChanged(object sender, SizeChangedEventArgs e) => RefreshLoupe();

    private void RefreshLoupe()
    {
        if (LoupeLayer is null) return;
        if (!loupeEnabled || !loupePointerInside || model.Image is null || decoded is null
            || loupePoint.X < 0 || loupePoint.Y < 0 || loupePoint.X > Viewport.ActualWidth || loupePoint.Y > Viewport.ActualHeight
            || !double.IsFinite(Picture.Width) || !double.IsFinite(Picture.Height)) { LoupeLayer.Clear(); return; }
        var transform = Picture.TransformToAncestor(Viewport);
        var origin = transform.Transform(new Point());
        var x = transform.Transform(new Point(1, 0)); var y = transform.Transform(new Point(0, 1));
        var matrix = new Matrix(x.X - origin.X, x.Y - origin.Y, y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y);
        LoupeLayer.Visibility = Visibility.Visible;
        LoupeLayer.Update(model.Image, new Size(Picture.Width, Picture.Height), matrix, loupePoint,
            loupeSettings.Magnification, loupeSettings.Size, Viewport.Background, (Brush)FindResource("AccentBrush"),
            ImageGrid.Visibility == Visibility.Visible ? ImageGrid.Fill : null, ImageGrid.Opacity);
    }
}
