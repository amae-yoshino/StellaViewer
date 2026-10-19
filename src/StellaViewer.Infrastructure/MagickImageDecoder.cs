using ImageMagick;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

/// <summary>Static-frame and bounded animation decoding with shared native-work serialization.</summary>
public sealed partial class MagickImageDecoder : IImageDecoder, IImageSizeProbe
{
    internal async Task<long> EstimateStreamPixelBytesAsync(Stream stream, CancellationToken token)
    {
        if (!DecodeWorker.IsWorker) return await DecodeWorker.ProbeAsync(null, stream, token).ConfigureAwait(false);
        await DecodeGate.WaitAsync(token).ConfigureAwait(false);
        try {
            return await Task.Run(() => {
                token.ThrowIfCancellationRequested();
                var prefix = new byte[4]; var count = stream.ReadAtLeast(prefix, 4, false);
                using var input = new PrefixReadStream(prefix.AsMemory(0, count), stream);
                if (count == 4 && prefix.AsSpan().SequenceEqual("CSFC"u8)) {
                    var preview = ClipStoredPreview.ReadAsync(input, token).GetAwaiter().GetResult();
                    return checked((long)preview.Width * preview.Height * 4);
                }
                if (count == 4 && prefix.AsSpan().SequenceEqual("8BPS"u8)) {
                    var header = new PsdMergedReader(input, token).ReadHeader();
                    return checked((long)header.Width * header.Height * 4);
                }
                var rasterHeader = ImageReadGuard.Read(input, token);
                return ImageReadGuard.CheckSize(rasterHeader.Width, rasterHeader.Height);
            }, token).ConfigureAwait(false);
        }
        finally { DecodeGate.Release(); }
    }
    public async Task<long> EstimatePixelBytesAsync(string path, CancellationToken token)
    {
        if (!DecodeWorker.IsWorker) return await DecodeWorker.ProbeAsync(path, null, token).ConfigureAwait(false);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await EstimateStreamPixelBytesAsync(source, token).ConfigureAwait(false);
    }
    // Bound native work across all decoder instances in this process.
    private static readonly SemaphoreSlim DecodeGate = new(1, 1);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".apng", ".bmp", ".gif", ".webp", ".psd", ".clip" };

    public Task<DecodedImage> DecodeAsync(string path, CancellationToken cancellationToken) => DecodeAsync(path, cancellationToken, 0);

    public async Task<DecodedImage> DecodeAsync(string path, CancellationToken cancellationToken, uint thumbnailSize)
    {
        if (!Extensions.Contains(Path.GetExtension(path)))
            throw new NotSupportedException("このデコーダーでは対応していない拡張子です。");

        if (!DecodeWorker.IsWorker) return await DecodeWorker.ImageAsync(path, null, thumbnailSize, cancellationToken).ConfigureAwait(false);

        await DecodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Decode(path, cancellationToken, thumbnailSize), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { DecodeGate.Release(); }
    }

    public static bool Supports(string path) => Extensions.Contains(Path.GetExtension(path));

    public async Task<DecodedImage> DecodeStreamAsync(Stream stream, CancellationToken token, uint thumbnailSize)
    {
        if (!DecodeWorker.IsWorker) return await DecodeWorker.ImageAsync(null, stream, thumbnailSize, token).ConfigureAwait(false);
        await DecodeGate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => DecodeStream(stream, token, thumbnailSize), token).ConfigureAwait(false); }
        finally { DecodeGate.Release(); }
    }

    private static DecodedImage Decode(string path, CancellationToken cancellationToken, uint thumbnailSize)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return DecodeStream(stream, cancellationToken, thumbnailSize);
    }

    private static DecodedImage DecodeStream(Stream stream, CancellationToken cancellationToken, uint thumbnailSize)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prefix = new byte[4];
        var count = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        using var input = new PrefixReadStream(prefix.AsMemory(0, count), stream);
        if (count == 4 && prefix.AsSpan().SequenceEqual("CSFC"u8)) {
            var preview = ClipStoredPreview.ReadAsync(input,cancellationToken).GetAwaiter().GetResult();
            using var previewStream = new MemoryStream(preview.Png, writable: false);
            var previewHeader = ImageReadGuard.Read(previewStream, cancellationToken);
            if (previewHeader.Format != MagickFormat.Png || previewHeader.Width != preview.Width || previewHeader.Height != preview.Height) throw new InvalidDataException("CLIP保存済み画像のヘッダーが一致しません。");
            using var stored = new MagickImage(preview.Png,new MagickReadSettings { Format=MagickFormat.Png,FrameIndex=0,FrameCount=1 });
            if (stored.Width != preview.Width || stored.Height != preview.Height) throw new InvalidDataException("CLIP保存済み画像の寸法が一致しません。");
            return ConvertImage(stored,cancellationToken,thumbnailSize) with { OriginalWidth=preview.OriginalWidth,OriginalHeight=preview.OriginalHeight };
        }
        if (count == 4 && prefix.AsSpan().SequenceEqual("8BPS"u8)) {
            var psd = new PsdMergedReader(input, cancellationToken).Read();
            using var merged = new MagickImage();
            merged.ReadPixels(psd.Rgba, new PixelReadSettings((uint)psd.Header.Width, (uint)psd.Header.Height, StorageType.Char, PixelMapping.RGBA));
            if (psd.Icc is not null) merged.SetProfile(new ColorProfile(psd.Icc));
            if (psd.Exif is not null) {
                var exif = new ExifProfile(psd.Exif); merged.SetProfile(exif);
                var orientation = exif.GetValue(ExifTag.Orientation)?.Value;
                if (orientation is >= 1 and <= 8) merged.Orientation = (Orientation)orientation.Value;
            }
            return ConvertImage(merged, cancellationToken, thumbnailSize);
        }
        var header = ImageReadGuard.Read(input, cancellationToken);
        using var replay = new PrefixReadStream(header.Prefix, input);
        using var image = new MagickImage(replay, new MagickReadSettings { Format = header.Format, FrameIndex = 0, FrameCount = 1 });
        return ConvertImage(image, cancellationToken, thumbnailSize);
    }
    private static DecodedImage ConvertImage(IMagickImage<byte> image, CancellationToken cancellationToken, uint thumbnailSize = 0, bool autoOrient = true)
    {
        ImageReadGuard.CheckSize(image.Width, image.Height);
        var originalWidth = checked((int)image.Width);
        var originalHeight = checked((int)image.Height);
        cancellationToken.ThrowIfCancellationRequested();
        if (autoOrient) image.AutoOrient();
        if (image.GetColorProfile() is not null)
        {
            if (!image.TransformColorSpace(ColorProfiles.SRGB))
                throw new InvalidDataException("ICCプロファイルをsRGBへ変換できませんでした。");
        }
        else if (image.ColorSpace == ColorSpace.CMYK)
            throw new NotSupportedException("ICCプロファイルのないCMYK画像は検証待ちです。");
        else
            image.ColorSpace = ColorSpace.sRGB;

        cancellationToken.ThrowIfCancellationRequested();
        if (thumbnailSize > 0 && (image.Width > thumbnailSize || image.Height > thumbnailSize))
            image.Resize(thumbnailSize, thumbnailSize);
        using var pixels = image.GetPixels();
        var bytes = pixels.ToByteArray(PixelMapping.BGRA)
            ?? throw new InvalidDataException("画像の画素データを取得できませんでした。");
        cancellationToken.ThrowIfCancellationRequested();
        return new(checked((int)image.Width), checked((int)image.Height),
            originalWidth, originalHeight, bytes);
    }
}
