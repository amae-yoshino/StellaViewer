using System.IO;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyLoadingSafetyAsync(string sample, string report)
    {
        await VerifySecurityRecoveryAsync(sample);
        await File.AppendAllTextAsync(report, "PASS: rejected input navigation and settings recovery\n");
        await VerifyPdfAsync(sample, report + ".pdf.png");
        await File.AppendAllTextAsync(report, "PASS: PDF navigation, spreads and 144 dpi rendering\n");
        await VerifyPdfCacheAsync(sample);
        await File.AppendAllTextAsync(report, "PASS: physical/archive/nested PDF cache and prefetch\n");
        await VerifyAnimationAsync(sample, report + ".animation.png");
        await File.AppendAllTextAsync(report, "PASS: GIF/WebP/APNG animation playback\n");
    }
}
