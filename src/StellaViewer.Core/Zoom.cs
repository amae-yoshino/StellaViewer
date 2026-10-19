namespace StellaViewer.Core;
public static class Zoom
{
    public static double Fit(int w, int h, double vw, double vh) => w <= 0 || h <= 0 || vw <= 0 || vh <= 0 ? 100 : Math.Min(vw / w, vh / h) * 100;
    public static double Minimum(double fit) => Math.Min(10, fit);
    public static double Clamp(double value, double fit) => Math.Clamp(value, Minimum(fit), 1000);
    public static double Initial(double fit, bool enlargeSmallImages = false) => Math.Min(enlargeSmallImages ? 1000 : 100, fit);
}
