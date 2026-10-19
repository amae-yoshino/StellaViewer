using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Core;

namespace StellaViewer.App;

public static class BitmapPresenter
{
    public static BitmapSource Create(DecodedImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96,
            PixelFormats.Bgra32, null, image.BgraPixels, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }
}
