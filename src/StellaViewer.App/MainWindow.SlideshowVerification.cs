using System.Diagnostics;
using System.IO;
using System.Windows;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifySlideshowAsync(string sample)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var root = Path.Combine(Path.GetDirectoryName(sample)!, "slideshow-ui-" + Guid.NewGuid().ToString("N"));
        var images = Path.Combine(root, "images"); var other = Path.Combine(root, "other"); Directory.CreateDirectory(images); Directory.CreateDirectory(other);
        for (var i = 1; i <= 3; i++) File.Copy(sample, Path.Combine(images, $"page{i:00}.png"));
        await File.WriteAllTextAsync(Path.Combine(images, "page04.png"), "invalid image");
        var probe = new MainWindow(false, Path.Combine(root, "state")) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, WatchFoldersEnabled = false };
        try {
            probe.Show(); await probe.OpenAsync(images); await probe.activeDisplay;
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { SlideshowIntervalSeconds = 1, Cache = new(0, 0, 0) });
            await probe.ExecuteViewerCommandAsync(ViewerCommand.Slideshow);
            Check(probe.slideshowPlaying && probe.SlideshowToggle.IsChecked == true && probe.slideshowTimer!.Interval.TotalSeconds == 1, "Slideshow starts from command and toolbar synchronizes");
            var first = probe.ImageList.SelectedIndex; var clock = Stopwatch.StartNew();
            while (probe.ImageList.SelectedIndex == first && clock.ElapsedMilliseconds < 5000) await Task.Delay(30);
            await probe.activeDisplay;
            Check(probe.ImageList.SelectedIndex == first + 1, "Real timer advances a page");
            probe.SlideshowClicked(probe, new()); var paused = probe.ImageList.SelectedIndex;
            await Task.Delay(1100);
            Check(!probe.slideshowPlaying && probe.ImageList.SelectedIndex == paused, "Pause cancels timer advancement");
            probe.SelectPageAt(3); await probe.activeDisplay;
            Check(probe.model.Message.Contains("読み込み失敗"), "Slideshow fixture has a failed last page");
            probe.SlideshowClicked(probe, new()); await probe.AdvanceSlideshowAsync();
            Check(probe.ImageList.SelectedIndex == 0 && probe.model.Image is not null, "Slideshow wraps from failed last page to first");
            probe.Move(1); await probe.activeDisplay;
            Check(probe.slideshowPlaying && probe.slideshowTimer!.IsEnabled && probe.slideshowTimer.Interval.TotalSeconds == 1, "Manual paging retains playback and resets delay");
            var display = probe.activeDisplay; var busy = new TaskCompletionSource(); probe.activeDisplay = busy.Task; var index = probe.ImageList.SelectedIndex;
            await probe.AdvanceSlideshowAsync();
            Check(probe.ImageList.SelectedIndex == index, "Busy decoder does not receive overlapping advance");
            probe.activeDisplay = display; busy.SetResult();
            probe.StopSlideshow();
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { SpreadMode = true }); probe.SelectPageAt(0); await probe.activeDisplay;
            probe.SlideshowClicked(probe, new()); await probe.AdvanceSlideshowAsync();
            Check(probe.ImageList.SelectedIndex == 2, "Slideshow advances a spread as one unit");
            await probe.OpenAsync(other);
            Check(!probe.slideshowPlaying && !probe.slideshowTimer!.IsEnabled, "Opening another location stops slideshow");
            probe.SlideshowClicked(probe, new()); Check(!probe.slideshowPlaying, "Empty list does not start slideshow");
            await probe.OpenAsync(images); await probe.activeDisplay;
            var dialog = new SettingsWindow(probe.CurrentPreferences, probe.ApplyPreferencesAsync);
            try {
                dialog.SlideshowInterval.Value = 7; Check(await dialog.ApplyChangesAsync() && probe.slideshowIntervalSeconds == 7, "Slideshow settings Apply");
                dialog.ResetValue("slideshow"); Check(dialog.Values.SlideshowIntervalSeconds == 3, "Slideshow independent reset");
            }
            finally { dialog.Close(); }
            probe.SlideshowClicked(probe, new()); probe.Close();
            Check(!probe.slideshowPlaying && !probe.slideshowTimer!.IsEnabled, "Close stops slideshow");
        }
        finally { if (!probe.closed) probe.Close(); await probe.TemporaryCleanup; }
    }
}
