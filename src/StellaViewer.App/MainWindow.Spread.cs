using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool spreadEnabled, rightBinding = true, movingSpread;
    private int spreadAnchor, spreadGroup;
    private sealed record PageView(FileEntry Entry, DecodedImage? Info, BitmapSource? Bitmap, string? Error);
    private PageView? firstPage, secondPage;
    private Rect firstPageRect, secondPageRect;

    private void RebaseSpread()
    { spreadAnchor = Math.Max(0, ImageList.SelectedIndex); spreadGroup = 0; }

    private void SelectPageAt(int index)
    {
        if (index < 0 || index >= model.Entries.Count) return;
        if (ImageList.SelectedItems.Count > 1) { selectingBatch = true; try { ImageList.UnselectAll(); } finally { selectingBatch = false; } }
        if (spreadEnabled && ImageList.SelectedIndex == index) {
            PruneMissingEntries();
            RebaseSpread(); activeDisplay = LoadSelectedAsync();
        }
        else ImageList.SelectedIndex = index;
        ScrollSelection();
    }

    private void SyncSpreadButton()
    {
        SpreadToggle.IsChecked = spreadEnabled;
        SpreadToggle.Content = new IconLabel { Kind = spreadEnabled ? ViewerIconKind.Spread : ViewerIconKind.SinglePage, Text = spreadEnabled ? "見開き" : "単ページ" };
    }

    private async void SpreadToggleClicked(object sender, RoutedEventArgs e)
    {
        try { await SetSpreadModeAsync(SpreadToggle.IsChecked == true); }
        catch (Exception error) { model.Status = "見開き設定を保存できませんでした：" + error.Message; }
    }

    private async Task SetSpreadModeAsync(bool enabled)
    {
        spreadEnabled = enabled; SyncSpreadButton(); RebaseSpread();
        var saving = usePreferences ? SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(spreadMode: enabled, filePath: preferenceFilePath)) : Task.CompletedTask;
        await (activeDisplay = LoadSelectedAsync()); await saving;
    }

    private async Task<PageView> ReadPageAsync(FileEntry entry, CancellationToken token)
    {
        try {
            var result = await DecodePageAsync(entry, token);
            token.ThrowIfCancellationRequested();
            return new(entry, result with { BgraPixels = [] }, BitmapPresenter.Create(result), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(entry, null, null, error.Message); }
    }

    private async Task LoadSpreadAsync(CancellationToken token)
    {
        var pair = SpreadPagination.GetPages(model.Entries.Count, spreadAnchor, spreadGroup);
        if (pair is null) return;
        var firstEntry = model.Entries[pair.Value.First];
        var secondEntry = pair.Value.Second is int index ? model.Entries[index] : null;
        var first = await ReadPageAsync(firstEntry, token);
        var second = secondEntry is null ? null : await ReadPageAsync(secondEntry, token);
        if (token.IsCancellationRequested || closed) return;
        firstPage = first; secondPage = second;
        if (first.Info is null && second?.Info is null) {
            model.Message = "読み込み失敗：" + first.Error + (second is null ? "" : "\n" + second.Error);
            return;
        }
        ComposeSpread(); model.Message = ""; ResetZoom();
    }

    private void ComposeSpread()
    {
        if (firstPage is null) return;
        var firstInfo = firstPage.Info ?? secondPage?.Info;
        if (firstInfo is null) return;
        var secondInfo = secondPage?.Info ?? firstInfo;
        var width = checked(firstInfo.Width + secondInfo.Width);
        var height = Math.Max(firstInfo.Height, secondInfo.Height);
        firstPageRect = new(rightBinding ? secondInfo.Width : 0, (height - firstInfo.Height) / 2.0, firstInfo.Width, firstInfo.Height);
        secondPageRect = new(rightBinding ? 0 : firstInfo.Width, (height - secondInfo.Height) / 2.0, secondInfo.Width, secondInfo.Height);
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            DrawPage(context, firstPage, firstPageRect);
            if (secondPage is not null) DrawPage(context, secondPage, secondPageRect);
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing); image.Freeze(); model.Image = image;
        // Only geometry is composite; source bitmaps stay separate and are never resampled into a giant bitmap.
        decoded = new(width, height, width, height, []);
    }

    private void DrawPage(DrawingContext context, PageView page, Rect area)
    {
        if (page.Bitmap is not null) { context.DrawImage(page.Bitmap, area); return; }
        context.PushClip(new RectangleGeometry(area));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 40, 40, 40)), null, area);
        var text = new FormattedText("読み込み失敗\n" + page.Entry.Name + "\n" + page.Error,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), 14,
            Brushes.WhiteSmoke, VisualTreeHelper.GetDpi(this).PixelsPerDip) {
                MaxTextWidth = Math.Max(1, area.Width - 16), MaxTextHeight = Math.Max(1, area.Height - 16)
            };
        context.DrawText(text, new Point(area.Left + 8, area.Top + 8)); context.Pop();
    }

    private void UpdateSpreadStatus(FileEntry selected)
    {
        var pair = SpreadPagination.GetPages(model.Entries.Count, spreadAnchor, spreadGroup);
        if (pair is null) return;
        var pages = pair.Value;
        if (firstPage is not null) {
            int FindPageIndex(string path) {
                for (var i = 0; i < model.Entries.Count; i++)
                    if (string.Equals(model.Entries[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }
            var firstIndex = FindPageIndex(firstPage.Entry.Path);
            var secondIndex = secondPage is null ? -1 : FindPageIndex(secondPage.Entry.Path);
            if (firstIndex >= 0) pages = new(firstIndex, secondIndex >= 0 ? secondIndex : null);
        }
        var position = pages.Second is int second ? $"{pages.First + 1}–{second + 1}" : $"{pages.First + 1}＋空白";
        string Describe(PageView page) => $"{page.Entry.Name}\n"
            + (page.Info is null ? "読み込み失敗：" + page.Error : $"{(page.Entry.IsPdfPage ? $"描画寸法（{PdfSession.RenderDpi} dpi）" : "画像寸法")}：{page.Info.OriginalWidth} × {page.Info.OriginalHeight} px")
            + $"\n{EntrySizeLabel(page.Entry)}{FileSizeText.Format(page.Entry.Length)}\n{EntryDateLabel(page.Entry)}：{(page.Entry.Modified == default ? "不明" : page.Entry.Modified.ToString("yyyy/MM/dd HH:mm"))}\n{BrowsePath.DisplayPath(page.Entry.Path)}";
        var firstLabel = rightBinding ? "右" : "左";
        var secondLabel = rightBinding ? "左" : "右";
        var details = firstPage is null ? selected.Name : $"{firstLabel}ページ\n{Describe(firstPage)}";
        details += secondPage is null ? $"\n\n{secondLabel}ページ：空白" : $"\n\n{secondLabel}ページ\n{Describe(secondPage)}";
        model.Details = details;
        model.Status = $"{position} / {model.Entries.Count}  {zoom:0}%  "
            + (firstPage?.Info is { } a ? $"{firstLabel} {a.OriginalWidth}×{a.OriginalHeight}px " : $"{firstLabel} 読み込み失敗 ")
            + (secondPage?.Info is { } b ? $"{secondLabel} {b.OriginalWidth}×{b.OriginalHeight}px " : secondPage is null ? "" : $"{secondLabel} 読み込み失敗 ")
            + $"{EntrySizeLabel(selected)}{FileSizeText.Format(selected.Length)}  {(selected.IsPdfPage ? "元PDF更新日時：" : "")}{selected.Modified:yyyy/MM/dd HH:mm}";
    }
}
