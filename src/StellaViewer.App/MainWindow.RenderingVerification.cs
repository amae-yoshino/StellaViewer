using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StellaViewer.App;

public partial class MainWindow
{
    private void VerifyRendering(string outputPrefix)
    {
        if (Icon is null || Icon.Width <= 0 || Icon.Height <= 0)
            throw new InvalidOperationException("Application window icon must load from the packaged asset.");
        if (Math.Abs(SortBox.ActualHeight - SearchBox.ActualHeight) > .1)
            throw new InvalidOperationException("Sort and search controls must have matching heights.");
        foreach (var selector in new[] { SortBox, ListModeBox })
        {
            var selectedContent = (FrameworkElement)selector.Template.FindName("SelectedContent", selector);
            var textCenter = selectedContent.TransformToAncestor(selector).Transform(new Point(0, selectedContent.ActualHeight / 2));
            var chevron = Descendants(selector).OfType<System.Windows.Shapes.Path>().Single();
            var arrowCenter = chevron.TransformToAncestor(selector).Transform(new Point(0, chevron.ActualHeight / 2));
            if (Math.Abs(textCenter.Y - selector.ActualHeight / 2) > 1 || Math.Abs(arrowCenter.Y - textCenter.Y) > 1)
                throw new InvalidOperationException("Selector text and chevron must be vertically centered together.");
        }
        var originalText = ZoomText.Text;
        foreach (var editable in new[] { false, true })
        foreach (var text in new[] { "10", "100", "1000" })
        {
            ZoomText.IsReadOnly = !editable; ZoomText.Text = text; ZoomText.UpdateLayout();
            var character = ZoomText.GetRectFromCharacterIndex(0);
            if (character.IsEmpty || Math.Abs(character.Top + character.Height / 2 - ZoomText.ActualHeight / 2) > 1)
                throw new InvalidOperationException($"Zoom text is not vertically centered: {text}, editable={editable}, bounds={character}");
        }
        ZoomText.IsReadOnly = true; ZoomText.Text = originalText; ZoomText.UpdateLayout();
        var zoomVisual = new DrawingVisual();
        using (var drawing = zoomVisual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(ZoomText), null, new Rect(0, 0, 216, 88));
        var zoomCapture = new RenderTargetBitmap(216, 88, 96, 96, PixelFormats.Pbgra32);
        zoomCapture.Render(zoomVisual); SaveRender(zoomCapture, outputPrefix + ".zoom-input.png");

        // A high frequency checker should become neutral grey when reduced.
        const int sourceSize = 64;
        var pixels = new byte[sourceSize * sourceSize * 4];
        for (var y = 0; y < sourceSize; y++)
        for (var x = 0; x < sourceSize; x++)
        {
            var index = (y * sourceSize + x) * 4;
            var value = (byte)(((x + y) % 2) * 255);
            pixels[index] = pixels[index + 1] = pixels[index + 2] = value; pixels[index + 3] = 255;
        }
        var source = BitmapSource.Create(sourceSize, sourceSize, 96, 96, PixelFormats.Bgra32, null, pixels, sourceSize * 4);
        source.Freeze();
        double RenderDeviation(BitmapScalingMode mode)
        {
            var visual = new System.Windows.Controls.Image { Source = source, Width = 9, Height = 9, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(visual, mode);
            var parent = new System.Windows.Controls.Canvas { Width = 9, Height = 9 };
            parent.Children.Add(visual); parent.Measure(new Size(9, 9)); parent.Arrange(new Rect(0, 0, 9, 9)); parent.UpdateLayout();
            var rendered = new RenderTargetBitmap(9, 9, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(parent);
            var result = new byte[9 * 9 * 4]; rendered.CopyPixels(result, 9 * 4, 0);
            SaveRender(rendered, outputPrefix + $".scaling-{mode}.png");
            double deviation = 0;
            for (var y = 1; y < 8; y++)
            for (var x = 1; x < 8; x++) deviation += Math.Abs(result[(y * 9 + x) * 4] - 127.5);
            return deviation / 49;
        }
        var linear = RenderDeviation(BitmapScalingMode.Linear);
        var highQuality = RenderDeviation(BitmapScalingMode.HighQuality);
        File.WriteAllText(outputPrefix + ".scaling.txt", $"Checker reduction 64 -> 9, grey deviation: Linear={linear:F3}, HighQuality={highQuality:F3}");
        if (highQuality >= linear)
            throw new InvalidOperationException($"High-quality reduction should suppress checker aliasing: linear={linear}, highQuality={highQuality}");
    }
    private static void SaveRender(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
