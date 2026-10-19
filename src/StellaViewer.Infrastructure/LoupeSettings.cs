namespace StellaViewer.Infrastructure;

public sealed record LoupeSettings(double Magnification = 2, int Size = 240)
{
    public LoupeSettings Normalize() => new(
        double.IsFinite(Magnification) ? Math.Clamp(Magnification, 1, 16) : 2,
        Math.Clamp(Size, 80, 800));
}
