namespace StellaViewer.Core;

// BGRA bytes are owned by this result; consumers must not mutate them.
public sealed record DecodedImage(int Width, int Height, int OriginalWidth,
    int OriginalHeight, byte[] BgraPixels)
{
    public int Stride => checked(Width * 4);
}

public interface IImageDecoder
{
    Task<DecodedImage> DecodeAsync(string path, CancellationToken cancellationToken);
}

public interface IImageSizeProbe
{
    Task<long> EstimatePixelBytesAsync(string path, CancellationToken cancellationToken);
}
