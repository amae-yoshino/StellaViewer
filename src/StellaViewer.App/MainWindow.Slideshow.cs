using System.Windows;
using System.Windows.Threading;

namespace StellaViewer.App;

public partial class MainWindow
{
    private DispatcherTimer? slideshowTimer;
    private bool slideshowPlaying, slideshowAdvancing;
    private int slideshowIntervalSeconds = 3, slideshowDisplayRevision;

    private void SlideshowClicked(object sender, RoutedEventArgs e)
    {
        if (slideshowPlaying) StopSlideshow();
        else if (model.Entries.Count > 0 && CanReadCurrentFolder()) {
            slideshowPlaying = true;
            SlideshowToggle.IsChecked = true;
            SlideshowToggle.Content = new IconLabel { Kind = ViewerIconKind.Pause, Text = "一時停止" };
            SlideshowToggle.ToolTip = "スライドショーを一時停止";
            RestartSlideshowDelay();
        }
        else { SlideshowToggle.IsChecked = false; model.Status = "スライドショーを開始するには画像一覧を開いてください。"; }
    }

    private void StopSlideshow()
    {
        slideshowPlaying = false; slideshowTimer?.Stop();
        SlideshowToggle.IsChecked = false;
        SlideshowToggle.Content = new IconLabel { Kind = ViewerIconKind.Play, Text = "スライドショー" };
        SlideshowToggle.ToolTip = "スライドショーを開始（間隔は設定から変更）";
    }

    private void RestartSlideshowDelay()
    {
        slideshowTimer?.Stop();
        if (!slideshowPlaying || closed || closingInProgress || model.Entries.Count == 0) return;
        if (slideshowTimer is null) {
            slideshowTimer = new DispatcherTimer(DispatcherPriority.Background);
            slideshowTimer.Tick += async (_, _) => await AdvanceSlideshowAsync();
        }
        slideshowTimer.Interval = TimeSpan.FromSeconds(slideshowIntervalSeconds);
        slideshowTimer.Start();
    }

    private async Task AdvanceSlideshowAsync()
    {
        slideshowTimer?.Stop();
        if (!slideshowPlaying || closed || closingInProgress) return;
        if (model.Entries.Count == 0 || !CanReadCurrentFolder()) { StopSlideshow(); return; }
        if (loading || !activeDisplay.IsCompleted) { RestartSlideshowDelay(); return; }
        try {
            slideshowAdvancing = true;
            try { Move(1); }
            finally { slideshowAdvancing = false; }
            await activeDisplay; RestartSlideshowDelay();
        }
        catch (Exception error) { StopSlideshow(); model.Status = "スライドショーを停止しました：" + error.Message; }
    }
}
