using System.IO;
using System.Text;
using ImageMagick;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifySecurityRecoveryAsync(string sample)
    {
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"red\"/></svg>";
        var denied = false;
        try { using var image = new MagickImage(Encoding.UTF8.GetBytes(svg)); }
        catch (MagickPolicyErrorException) { denied = true; }
        Check(denied, "Native policy rejects SVG independently of managed format validation");
        var root = Path.Combine(Path.GetDirectoryName(sample)!, "security-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var prefs = Path.Combine(root, "viewer-preferences.json");
        await PreferencesStore.SaveChangeAsync(dark: true, restoreLastView: false, filePath: prefs);
        File.WriteAllText(prefs, "broken");
        var images = Path.Combine(root, "images"); Directory.CreateDirectory(images);
        File.WriteAllText(Path.Combine(images, "1.png"), svg); File.Copy(sample, Path.Combine(images, "2.png"));
        var probe = new MainWindow(true, root) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, ConstrainStartupWindowToScreen = false, WatchFoldersEnabled = false };
        try {
            probe.Show(); await probe.RestoreStartupAsync();
            Check(probe.CurrentPreferences.Dark && probe.model.Status.Contains("バックアップから復旧"), "Recovered theme and user-facing recovery notification");
            await probe.OpenAsync(Path.Combine(images, "1.png")); await probe.activeDisplay;
            Check(probe.model.Message.Contains("読み込み失敗") && probe.model.Entries.Count == 2, "Rejected disguised image remains navigable");
            probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage, "Navigation recovers after blocked format under native policy");
            await PreferencesStore.SaveChangeAsync(dark: false, filePath: prefs);
            Check(!PreferencesStore.Load(prefs).Dark, "Recovered settings can be saved again");
        }
        finally { probe.Close(); await probe.closePersistence; await probe.TemporaryCleanup; }
    }
}
