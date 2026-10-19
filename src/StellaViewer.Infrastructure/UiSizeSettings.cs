namespace StellaViewer.Infrastructure;

public sealed record UiSizeSettings(int ScalePercent = 100, int FontSize = 14)
{
    public UiSizeSettings Normalize() => new(Math.Clamp(ScalePercent, 75, 150), Math.Clamp(FontSize, 10, 24));
}
