using System.Windows;
using System.Windows.Media;

namespace StellaViewer.App;

// Draw the retained source directly, rather than enlarging a screenshot of the viewer.
public sealed class LoupeOverlay : FrameworkElement
{
    private ImageSource? source;
    private Size imageSize;
    private Matrix imageToViewport;
    private Point sample;
    private double magnification = 2;
    private int size = 240;
    private Brush background = Brushes.Transparent, border = Brushes.White;
    private Brush? grid;
    private double gridOpacity;
    internal Rect LensBounds { get; private set; }

    public LoupeOverlay() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

    internal void Update(ImageSource image, Size dimensions, Matrix transform, Point anchor, double factor, int length,
        Brush backgroundBrush, Brush borderBrush, Brush? gridBrush, double opacity)
    {
        source = image; imageSize = dimensions; imageToViewport = transform; sample = anchor;
        magnification = factor; size = length; background = backgroundBrush; border = borderBrush;
        grid = gridBrush; gridOpacity = opacity; InvalidateVisual();
    }

    internal void Clear()
    {
        source = null; grid = null; LensBounds = Rect.Empty; Visibility = Visibility.Collapsed;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        if (source is null || ActualWidth <= 8 || ActualHeight <= 8) return;
        const double margin = 3;
        var length = Math.Min(size, Math.Min(ActualWidth, ActualHeight) - margin * 2);
        var half = length / 2;
        var center = new Point(Math.Clamp(sample.X, half + margin, ActualWidth - half - margin),
            Math.Clamp(sample.Y, half + margin, ActualHeight - half - margin));
        LensBounds = new Rect(center.X - half, center.Y - half, length, length);
        context.PushClip(new RectangleGeometry(LensBounds));
        context.DrawRectangle(background, null, new Rect(RenderSize));
        var transform = imageToViewport;
        transform.Scale(magnification, magnification);
        transform.Translate(center.X - sample.X * magnification, center.Y - sample.Y * magnification);
        context.PushTransform(new MatrixTransform(transform));
        context.DrawImage(source, new Rect(imageSize));
        if (grid is not null) {
            context.PushOpacity(gridOpacity); context.DrawRectangle(grid, null, new Rect(imageSize)); context.Pop();
        }
        context.Pop(); context.Pop();
        context.DrawRectangle(null, new Pen(Brushes.Black, 3), LensBounds);
        context.DrawRectangle(null, new Pen(border, 1), LensBounds);
    }
}
