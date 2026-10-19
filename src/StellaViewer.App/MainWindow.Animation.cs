using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private DispatcherTimer? animationTimer;
    private readonly Stopwatch animationClock = new();
    private AnimationTimeline? animationTimeline;
    private BitmapSource[] animationBitmaps = [];
    private bool animationSecondPage;
    private int animationFrame = -1;
    private string? animationNotice;
    internal AnimationLimits AnimationLimits { get; set; } = new();
    private void StopAnimation()
    {
        animationTimer?.Stop(); animationClock.Reset(); animationTimeline = null;
        animationBitmaps = []; animationFrame = -1; animationNotice = null;
    }
    private async Task<AnimationDecodeResult> ReadAnimationAsync(FileEntry entry, CancellationToken token)
    {
        if (entry.IsPdfPage || !MagickImageDecoder.MayAnimate(entry.Name)) return new(null);
        NetworkAccess.Validate(BrowsePath.PhysicalPath(entry.Path), model.AllowNetwork);
        var location = ArchiveLocation.Parse(entry.Path);
        if (location is null) return await decoder.DecodeAnimationAsync(entry.Path, token, AnimationLimits);
        var session = archiveSession ?? throw new InvalidOperationException("書庫が開かれていません。");
        var extension = Path.GetExtension(entry.Name);
        if ((extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || extension.Equals(".apng", StringComparison.OrdinalIgnoreCase))
            && !await session.HasPngAnimationAsync(location, token, model.AllowNetwork)) return new(null);
        var store = await GetTemporaryStoreAsync(); token.ThrowIfCancellationRequested();
        using var file = await store.CreateAsync((output, limit, ct) => session.CopyEntryAsync(location, output, limit, ct, model.AllowNetwork), token);
        var result = await decoder.DecodeAnimationAsync(file.Path, token, AnimationLimits);
        token.ThrowIfCancellationRequested(); session.ValidateUnchanged(); return result;
    }
    private async Task<bool> TryStartAnimationAsync(FileEntry entry, bool second, CancellationToken token)
    {
        AnimationDecodeResult? result;
        try { result = await ReadAnimationAsync(entry, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { result = new(null, "アニメーションを再生できません：" + error.Message + " 先頭フレームを静止表示します。"); }
        if (token.IsCancellationRequested || closed || closingInProgress) return false;
        if (result.Warning is not null) { animationNotice = result.Warning; UpdateStatus(); return true; }
        DecodedAnimation? animation = result.Animation;
        if (animation is null) return false;
        var info = animation.Frames[0].Image with { BgraPixels = [] };
        var timeline = animation.Timeline;
        // Freeze WPF frames on a worker, then release the decoder's BGRA arrays.
        var bitmaps = await Task.Run(() => animation.Frames.Select(frame => {
            token.ThrowIfCancellationRequested(); return BitmapPresenter.Create(frame.Image);
        }).ToArray(), token);
        if (token.IsCancellationRequested || closed || closingInProgress) return false;
        animation = null; result = null;
        animationTimeline = timeline; animationBitmaps = bitmaps; animationSecondPage = second;
        var old = spreadEnabled ? (second ? secondPage?.Info : firstPage?.Info) : decoded;
        if (spreadEnabled) {
            if (second && secondPage is not null) secondPage = secondPage with { Info = info };
            else if (firstPage is not null) firstPage = firstPage with { Info = info };
        }
        else decoded = info;
        ApplyAnimationFrame(0);
        if (old?.Width != info.Width || old?.Height != info.Height) ResetZoom();
        animationNotice = $"アニメーション再生中（{bitmaps.Length:N0}フレーム・無限ループ）";
        animationClock.Restart();
        if (animationTimer is null) {
            animationTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
            animationTimer.Tick += (_, _) => {
                if (animationTimeline is not null && !closed && !closingInProgress) ApplyAnimationFrame(animationTimeline.FrameAt(animationClock.Elapsed.TotalMilliseconds));
            };
        }
        animationTimer.Start(); UpdateStatus(); return true;
    }
    private async Task StartSelectedAnimationAsync(FileEntry entry, CancellationToken token)
    {
        if (!spreadEnabled) { await TryStartAnimationAsync(entry, false, token); return; }
        if (firstPage is { Info: not null } first && await TryStartAnimationAsync(first.Entry, false, token)) return;
        if (secondPage is { Info: not null } second) await TryStartAnimationAsync(second.Entry, true, token);
    }
    private void ApplyAnimationFrame(int index)
    {
        if (index == animationFrame || index < 0 || index >= animationBitmaps.Length) return;
        animationFrame = index;
        if (spreadEnabled) {
            if (animationSecondPage && secondPage is not null) secondPage = secondPage with { Bitmap = animationBitmaps[index] };
            else if (firstPage is not null) firstPage = firstPage with { Bitmap = animationBitmaps[index] };
            ComposeSpread();
        }
        else model.Image = animationBitmaps[index];
        RefreshLoupe();
    }
    private void AppendAnimationNotice()
    {
        if (animationNotice is null) return;
        model.Status += "  " + animationNotice; model.Details += "\n\n" + animationNotice;
    }
}
