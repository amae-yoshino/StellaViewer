using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyNetworkAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var saved = CurrentPreferences;
        const string remote = @"\\stella-test.invalid\share";
        try {
            await ApplyPreferencesAsync(CurrentPreferences with { AllowNetwork = false });
            await OpenAsync(imagePath); await activeDisplay;
            var image = model.Image; var selected = ImageList.SelectedItem; var folder = model.Folder;
            await OpenAsync(remote);
            Check(ReferenceEquals(image, model.Image) && model.Folder == folder && model.Status.Contains("ネットワーク"),
                "Direct network path is rejected before probing and preserves current image");
            history.Push(new(remote, null, "", 0, 1, 0));
            var count = history.Count; await GoBackAsync();
            Check(history.Count == count && ReferenceEquals(image, model.Image), "Blocked history is retained without opening remote folder");
            history.Pop(); UpdateHistoryButtons();
            var startup = ViewerStateStore.ResolveStartup(new(remote), null, true);
            Check(startup.Path is null && startup.Notification!.Contains("ネットワーク"), "Disabled startup restoration rejects network path without contacting server");
            var dialog = new SettingsWindow(CurrentPreferences, ApplyPreferencesAsync) {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false
            };
            dialog.Show(); ((TabControl)dialog.FindName("SettingsTabs")).SelectedIndex = 6;
            ((CheckBox)dialog.FindName("NetworkAllowed")).IsChecked = true;
            Check(await dialog.ApplyChangesAsync() && dialog.IsVisible && model.AllowNetwork,
                "Network permission Apply updates window without closing settings");
            dialog.UpdateLayout(); var content = (FrameworkElement)dialog.Content;
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + ".network-settings.png");
            dialog.ResetValue("network"); Check(!dialog.Values.AllowNetwork, "Network reset selects disabled"); dialog.Close();
            Check(model.AllowNetwork, "Cancel discards network change after last Apply");
            // Simulate an already loaded remote location; no test contacts a server.
            model.Folder = remote; ApplyNetworkAccess(false);
            Check(ReferenceEquals(image, model.Image) && browse.IsCancellationRequested && display.IsCancellationRequested
                && locationListing.IsCancellationRequested && folderMonitor is null && !model.AllowNetwork,
                "Disabling network preserves pixels and cancels listing, decoding and monitoring");
            Move(1); await ReloadAsync(); await LoadSelectedAsync();
            Check(ReferenceEquals(image, model.Image) && ReferenceEquals(selected, ImageList.SelectedItem),
                "Disabled network rejects page turn, reload and new decoding while preserving current image");
            model.Folder = folder;
        }
        finally {
            model.Folder = Path.GetDirectoryName(imagePath)!;
            await ApplyPreferencesAsync(saved); await OpenAsync(imagePath); await activeDisplay;
        }
    }
}
