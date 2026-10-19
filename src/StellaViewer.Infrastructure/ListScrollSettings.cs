namespace StellaViewer.Infrastructure;

public sealed record ListScrollSettings(int AmountPercent = 100)
{
    public ListScrollSettings Normalize() => new(Math.Clamp(AmountPercent, 25, 400));
}
