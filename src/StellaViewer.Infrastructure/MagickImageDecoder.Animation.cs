using ImageMagick;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

public sealed partial class MagickImageDecoder
{
    public static bool MayAnimate(string name) => Path.GetExtension(name).ToLowerInvariant() is ".gif" or ".webp" or ".png" or ".apng";
    public async Task<AnimationDecodeResult> DecodeAnimationAsync(string path, CancellationToken token, AnimationLimits? limits = null)
    {
        limits ??= new();
        if (limits.PixelBytes <= 0 || limits.FrameCount is < 2 or > 100000) throw new ArgumentOutOfRangeException(nameof(limits));
        if (!DecodeWorker.IsWorker) return await DecodeWorker.AnimationAsync(path, limits, token).ConfigureAwait(false);
        await DecodeGate.WaitAsync(token).ConfigureAwait(false);
        try {
            return await Task.Run(() => {
                token.ThrowIfCancellationRequested();
                var stamp = new FileInfo(path); var length = stamp.Length; var modified = stamp.LastWriteTimeUtc;
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                AnimationDecodeResult result;
                try {
                    result = PngAnimationReader.IsPng(source)
                        ? PngAnimationReader.Read(source, limits, token)
                        : ReadNativeAnimation(source, limits, token);
                }
                catch (AnimationLimitException error) { result = new(null, error.Message + " 先頭フレームを静止表示します。"); }
                token.ThrowIfCancellationRequested(); stamp.Refresh();
                if (!stamp.Exists || stamp.Length != length || stamp.LastWriteTimeUtc != modified)
                    throw new IOException("画像が変更されました。再読み込みしてください。");
                return result;
            }, token).ConfigureAwait(false);
        }
        finally { DecodeGate.Release(); }
    }
    private static AnimationDecodeResult ReadNativeAnimation(Stream source, AnimationLimits limits, CancellationToken token)
    {
        var header = ImageReadGuard.Read(source, token); source.Position = 0;
        var settings = new MagickReadSettings { Format = header.Format, FrameCount = checked((uint)limits.FrameCount + 1) };
        using var headers = new MagickImageCollection(); headers.Ping(source, settings);
        token.ThrowIfCancellationRequested();
        if (headers.Count < 2) return new(null);
        ValidateAnimationSize(headers, limits);
        source.Position = 0;
        using var images = new MagickImageCollection(); images.Read(source, settings);
        token.ThrowIfCancellationRequested(); ValidateAnimationSize(images, limits);
        images.Coalesce(); token.ThrowIfCancellationRequested(); ValidateAnimationSize(images, limits);
        var frames = new List<AnimationFrame>(images.Count); long bytes = 0;
        foreach (var image in images) {
            token.ThrowIfCancellationRequested();
            var decoded = ConvertImage(image, token);
            bytes = checked(bytes + decoded.BgraPixels.LongLength);
            if (bytes > limits.PixelBytes) throw new AnimationLimitException("アニメーションの展開画素がメモリ上限を超えています。");
            var delay = image.AnimationTicksPerSecond > 0 && image.AnimationDelay > 0
                ? 1000.0 * image.AnimationDelay / image.AnimationTicksPerSecond : 100;
            frames.Add(new(decoded, delay));
        }
        return frames.Count < 2 ? new(null) : new(new(frames));
    }
    private static void ValidateAnimationSize(MagickImageCollection images, AnimationLimits limits)
    {
        if (images.Count > limits.FrameCount) throw new AnimationLimitException($"アニメーションがフレーム上限（{limits.FrameCount:N0}枚）を超えています。");
        long width = 0, height = 0;
        foreach (var image in images) {
            if (image.Page.X < 0 || image.Page.Y < 0) throw new NotSupportedException("負のフレーム位置には対応していません。");
            width = Math.Max(width, Math.Max(image.Page.Width, checked(image.Width + (long)image.Page.X)));
            height = Math.Max(height, Math.Max(image.Page.Height, checked(image.Height + (long)image.Page.Y)));
        }
        try {
            if (checked(width * height * 4 * images.Count) > limits.PixelBytes)
                throw new AnimationLimitException("アニメーションの展開画素がメモリ上限を超えています。");
        }
        catch (OverflowException) { throw new AnimationLimitException("アニメーションの寸法がメモリ上限を超えています。"); }
    }
    internal sealed class AnimationLimitException(string message) : Exception(message);
    internal static DecodedImage DecodePngAnimationFrame(byte[] png, CancellationToken token, out int orientation)
    {
        using var source = new MemoryStream(png, writable: false);
        var header = ImageReadGuard.Read(source, token);
        if (header.Format != MagickFormat.Png) throw new InvalidDataException("アニメーションフレームがPNGではありません。");
        using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png });
        orientation = (int)image.Orientation;
        return ConvertImage(image, token, autoOrient: false);
    }
}
