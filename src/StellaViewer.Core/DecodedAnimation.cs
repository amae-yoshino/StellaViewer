namespace StellaViewer.Core;

public sealed record AnimationFrame(DecodedImage Image, double DelayMilliseconds);
public sealed class DecodedAnimation
{
    public IReadOnlyList<AnimationFrame> Frames { get; }
    public AnimationTimeline Timeline { get; }
    public double DurationMilliseconds => Timeline.DurationMilliseconds;
    public DecodedAnimation(IReadOnlyList<AnimationFrame> frames)
    {
        if (frames.Count < 2) throw new ArgumentException("再生には2枚以上のフレームが必要です。");
        Frames = frames;
        var first = frames[0].Image;
        if (frames.Any(f => f.Image.Width != first.Width || f.Image.Height != first.Height)) throw new NotSupportedException("フレームごとに表示寸法が異なる画像には対応していません。");
        Timeline = new(frames.Select(f => f.DelayMilliseconds));
    }
    public int FrameAt(double elapsedMilliseconds) => Timeline.FrameAt(elapsedMilliseconds);
}
public sealed class AnimationTimeline
{
    private readonly double[] ends;
    public double DurationMilliseconds => ends[^1];
    public AnimationTimeline(IEnumerable<double> delays)
    {
        double total = 0;
        ends = delays.Select(delay => {
            if (!double.IsFinite(delay) || delay <= 0) throw new ArgumentException("フレーム時間が不正です。");
            total += delay; if (!double.IsFinite(total)) throw new ArgumentException("再生時間が大きすぎます。"); return total;
        }).ToArray();
        if (ends.Length == 0) throw new ArgumentException("フレームがありません。");
    }
    public int FrameAt(double elapsedMilliseconds)
    {
        var time = Math.Max(0, elapsedMilliseconds) % DurationMilliseconds;
        var low = 0; var high = ends.Length - 1;
        while (low < high) { var mid = (low + high) / 2; if (time < ends[mid]) high = mid; else low = mid + 1; }
        return low;
    }
}
public sealed record AnimationLimits(long PixelBytes = 256L * 1024 * 1024, int FrameCount = 1000);
public sealed record AnimationDecodeResult(DecodedAnimation? Animation, string? Warning = null);
