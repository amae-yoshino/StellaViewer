using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private string? recentContextPlace;
    private void RecentContextPointer(object sender, MouseButtonEventArgs e)
    {
        recentContextPlace = (ItemsControl.ContainerFromElement(RecentLocationList, e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext is RecentLocation place ? place.Location : null;
        e.Handled = true;
    }
    private async Task RemoveRecentHistoryAsync(string? place)
    {
        var saving = SaveAfterAsync(recentSaveTail, () => recentLocationStore.RemoveAsync(place)); recentSaveTail = saving;
        await saving; await RefreshChangedRecentLocationsAsync();
        model.Status = place is null ? "最近開いた場所の履歴を全削除しました。" : "履歴から削除しました。";
    }
    private Task ClearRecentHistoryAsync() => RemoveRecentHistoryAsync(null);
    private async Task ClearSavedViewingStateAsync()
    {
        if (usePreferences) await SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(recordViewingState: false, filePath: preferenceFilePath));
        recordViewingState = false;
        try { await folderPositionSave; } catch { }
        await folderPositions.ClearAsync(); await ViewerStateStore.ClearViewAsync(storageDirectory);
        folderPositionSnapshot.Clear(); startupState = startupState with { View = new() };
        model.Status = "閲覧位置の保存を停止し、保存済みの閲覧位置を削除しました。";
    }
    private async void ClearRecentLocationsClicked(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "最近開いた場所の履歴を全削除しますか？", "履歴の削除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await ClearRecentHistoryAsync(); } catch (Exception error) { model.Status = "履歴を削除できません：" + error.Message; }
    }
    private async void RemoveRecentLocationClicked(object sender, RoutedEventArgs e)
    {
        if (recentContextPlace is not string place) return;
        try { await RemoveRecentHistoryAsync(place); } catch (Exception error) { model.Status = "履歴を削除できません：" + error.Message; }
    }
}
