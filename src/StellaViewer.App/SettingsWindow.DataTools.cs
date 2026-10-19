using System.IO;
using StellaViewer.Core;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class SettingsWindow
{
    private Func<Task<ImageCacheUsage>>? cacheUsage;
    private Func<bool, bool, Task>? clearCache;
    private Func<Task>? clearHistory;
    private Func<Task>? clearView;
    private int cacheUsageVersion;
    internal void ConfigureDataTools(Func<Task<ImageCacheUsage>> usage, Func<bool, bool, Task> clear, Func<Task> history, Func<Task>? view = null)
    {
        cacheUsage = usage; clearCache = clear; clearHistory = history; clearView = view;
        Loaded += async (_, _) => await RefreshCacheUsageAsync();
    }
    internal async Task RefreshCacheUsageAsync()
    {
        if (cacheUsage is null) return;
        var version = ++cacheUsageVersion;
        try {
            var usage = await cacheUsage();
            if (version != cacheUsageVersion) return;
            CacheUsageText.Text = $"メモリ：{FileSizeText.Format(usage.MemoryBytes)}（{usage.MemoryCount:N0}件）\nディスク：{FileSizeText.Format(usage.DiskBytes)}（{usage.DiskCount:N0}件）";
        }
        catch (Exception error) { if (version == cacheUsageVersion) CacheUsageText.Text = "使用量を確認できません：" + error.Message; }
    }
    private async Task DataOperationAsync(Func<Task> action)
    {
        if (applying) return;
        applying = true; SettingsTabs.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = ApplyButton.IsEnabled = false;
        try { await action(); }
        catch (Exception error) { DataToolsStatus.Text = ErrorText.Text = error.Message; }
        finally { applying = false; SettingsTabs.IsEnabled = SaveButton.IsEnabled = CancelButton.IsEnabled = ApplyButton.IsEnabled = true; }
    }
    private async void RefreshCacheUsageClicked(object sender, RoutedEventArgs e) => await RefreshCacheUsageAsync();
    internal async Task ClearCacheAsync(bool memory, bool disk)
    {
        if (clearCache is null) return;
        await DataOperationAsync(async () => { await clearCache(memory, disk); await RefreshCacheUsageAsync(); });
    }
    private async void ClearCacheClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string scope }) await ClearCacheAsync(scope != "disk", scope != "memory");
    }
    private async void ClearHistoryClicked(object sender, RoutedEventArgs e)
    {
        if (clearHistory is null || MessageBox.Show(this, "最近開いた場所の履歴を全削除しますか？", "履歴の削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await DataOperationAsync(async () => { await clearHistory(); DataToolsStatus.Text = "最近開いた場所の履歴を削除しました。"; });
    }
    private async void ClearViewingStateClicked(object sender, RoutedEventArgs e)
    {
        if (clearView is null || MessageBox.Show(this, "閲覧位置の保存を停止し、前回の閲覧場所・ページとフォルダ別の閲覧位置を削除しますか？", "閲覧位置の削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await DataOperationAsync(async () => { await clearView(); RecordView.IsChecked = false; DataToolsStatus.Text = "閲覧位置の保存を停止し、保存済みの閲覧位置を削除しました。"; });
    }
    internal async Task ImportSettingsAsync(string path)
    {
        var imported = await SettingsTransfer.ImportAsync(path);
        LoadValues(imported); DataToolsStatus.Text = "設定を読み込みました。内容を確認し、保存または適用で反映してください。";
    }
    private async void ImportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "設定をインポート", Filter = "StellaViewerの設定 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await DataOperationAsync(() => ImportSettingsAsync(dialog.FileName));
    }
    private async void ExportSettingsClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "設定をエクスポート", Filter = "StellaViewerの設定 (*.json)|*.json", FileName = "StellaViewer-settings.json", AddExtension = true, DefaultExt = ".json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) == true) await DataOperationAsync(async () => { await SettingsTransfer.ExportAsync(dialog.FileName, Values); DataToolsStatus.Text = "設定をエクスポートしました。"; });
    }
}
