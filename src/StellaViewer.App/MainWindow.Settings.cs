using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool hideHiddenFolders = true, enlargeSmallImages;
    private int folderTreeVersion;
    private ViewerPreferences CurrentPreferences => new(DarkTheme.IsChecked == true, controlWheel,
        ListModeBox.SelectedIndex, hideHiddenFolders, viewerToolbarPlacement, SelectedImageSort, restoreLastView, LocationModeBox.SelectedIndex, SelectedLocationSort) {
            EnlargeSmallImages = enlargeSmallImages, SlideshowIntervalSeconds = slideshowIntervalSeconds, ListFontSize = listFontSize, ListScroll = listScroll, UiSize = uiSize, Appearance = appearance, SpreadMode = spreadEnabled, RightBinding = rightBinding, Loupe = loupeSettings, Keyboard = keyboardSettings.Normalize(), Mouse = mouseSettings.Normalize(), SoftwareRendering = softwareRendering, AllowNetwork = model.AllowNetwork, Cache = cacheSettings, FolderOpeningMode=folderOpeningMode, ImageThumbnailSize=model.ImageThumbnailSize, LocationThumbnailSize=model.LocationThumbnailSize, RecordRecentLocations=recordRecentLocations,RecordViewingState=recordViewingState,DeleteEnabled=deleteEnabled,MinimizeToTray=minimizeToTray,AllowMultipleInstances=allowMultipleInstances };

    private void SettingsClicked(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowSettings(bool showShortcuts = false)
    {
        SettingsWindow? dialog = null;
        dialog = new SettingsWindow(CurrentPreferences, async next =>
        {
            var before = CurrentPreferences;
            if (usePreferences)
                await SavePreferencesAsync(async () => {
                    await PreferencesStore.SaveChangeAsync(
                    listScrollPercent: next.ListScroll.AmountPercent != before.ListScroll.AmountPercent ? next.ListScroll.AmountPercent : null,
                    listFontSize: next.ListFontSize != before.ListFontSize ? next.ListFontSize : null,
                    enlargeSmallImages: next.EnlargeSmallImages != before.EnlargeSmallImages ? next.EnlargeSmallImages : null,
                    slideshowIntervalSeconds: next.SlideshowIntervalSeconds != before.SlideshowIntervalSeconds ? next.SlideshowIntervalSeconds : null,
                    uiScalePercent: next.UiSize.ScalePercent != before.UiSize.ScalePercent ? next.UiSize.ScalePercent : null,
                    uiFontSize: next.UiSize.FontSize != before.UiSize.FontSize ? next.UiSize.FontSize : null,
                    recordRecentLocations: next.RecordRecentLocations != before.RecordRecentLocations ? next.RecordRecentLocations : null,
                    recordViewingState: next.RecordViewingState != before.RecordViewingState ? next.RecordViewingState : null,
                    sortMode: next.SortMode != before.SortMode ? next.SortMode : null,
                    locationSortMode: next.LocationSortMode != before.LocationSortMode ? next.LocationSortMode : null,
                    dark: next.Dark != before.Dark ? next.Dark : null,
                    controlWheel: next.ControlWheel != before.ControlWheel ? next.ControlWheel : null,
                    listMode: next.ListMode != before.ListMode ? next.ListMode : null,
                    locationListMode: next.LocationListMode != before.LocationListMode ? next.LocationListMode : null,
                    hideHiddenFolders: next.HideHiddenFolders != before.HideHiddenFolders ? next.HideHiddenFolders : null,
                    viewerToolbarPlacement: next.ViewerToolbarPlacement != before.ViewerToolbarPlacement ? next.ViewerToolbarPlacement : null,
                    restoreLastView: next.RestoreLastView != before.RestoreLastView ? next.RestoreLastView : null,
                    folderOpeningMode: next.FolderOpeningMode != before.FolderOpeningMode ? next.FolderOpeningMode : null,
                    deleteEnabled:next.DeleteEnabled!=before.DeleteEnabled?next.DeleteEnabled:null,
                    minimizeToTray:next.MinimizeToTray!=before.MinimizeToTray?next.MinimizeToTray:null,
                    allowMultipleInstances:next.AllowMultipleInstances!=before.AllowMultipleInstances?next.AllowMultipleInstances:null,
                    imageThumbnailSize: next.ImageThumbnailSize != before.ImageThumbnailSize ? next.ImageThumbnailSize : null,
                    locationThumbnailSize: next.LocationThumbnailSize != before.LocationThumbnailSize ? next.LocationThumbnailSize : null,
                    appearance: next.Appearance, previousAppearance: before.Appearance,
                    spreadMode: next.SpreadMode != before.SpreadMode ? next.SpreadMode : null,
                    rightBinding: next.RightBinding != before.RightBinding ? next.RightBinding : null,
                    loupeMagnification: next.Loupe.Magnification != before.Loupe.Magnification ? next.Loupe.Magnification : null,
                    loupeSize: next.Loupe.Size != before.Loupe.Size ? next.Loupe.Size : null,
                    keyboard: next.Keyboard, previousKeyboard: before.Keyboard,
                    mouse: next.Mouse, previousMouse: before.Mouse,
                    softwareRendering: next.SoftwareRendering != before.SoftwareRendering ? next.SoftwareRendering : null,
                    allowNetwork: next.AllowNetwork != before.AllowNetwork ? next.AllowNetwork : null,
                    cache: next.Cache, previousCache: before.Cache,
                    filePath: preferenceFilePath);
                    RecordSavedListPreferences(
                        imageMode: next.ListMode != before.ListMode ? next.ListMode : null,
                        imageSort: next.SortMode != before.SortMode ? next.SortMode : null,
                        locationMode: next.LocationListMode != before.LocationListMode ? next.LocationListMode : null,
                        locationSort: next.LocationSortMode != before.LocationSortMode ? next.LocationSortMode : null);
                });
            await ApplyPreferencesAsync(next);
            CopySettingsTheme(dialog!);
        }) { Owner = this };
        CopySettingsTheme(dialog);
        dialog.ConfigureDataTools(imageCache.GetUsageAsync, async (memory, disk) => { CancelPrefetch(); await prefetchWork; await imageCache.ClearAsync(memory, disk); }, ClearRecentHistoryAsync, ClearSavedViewingStateAsync);
        if (showShortcuts) dialog.ShowShortcuts();
        dialog.ShowDialog();
    }
    private void CopySettingsTheme(Window dialog)
    {
        foreach (var key in new[] { "PanelBackground", "PanelForeground", "DividerBrush", "HoverBrush", "SelectionBrush", "ScrollThumbBrush", "ScrollThumbHoverBrush" })
            dialog.Resources[key] = Resources[key];
        UiSizing.ConfigureDialog(dialog, uiSize);
    }
    private async Task ApplyPreferencesAsync(ViewerPreferences next)
    {
        var resetInitialFit = enlargeSmallImages != next.EnlargeSmallImages;
        var resortImages = SelectedImageSort != next.SortMode;
        var resortLocations = SelectedLocationSort != next.LocationSortMode;
        var refreshFolders = hideHiddenFolders != next.HideHiddenFolders || model.AllowNetwork != next.AllowNetwork;
        if (model.AllowNetwork != next.AllowNetwork) ApplyNetworkAccess(next.AllowNetwork);
        var refreshSpread = spreadEnabled != next.SpreadMode || rightBinding != next.RightBinding;
        var refreshCache = cacheSettings != (next.Cache ?? new()).Normalize();
        loadingPreferences = true;
        try
        {
            DarkTheme.IsChecked = next.Dark;
            enlargeSmallImages = next.EnlargeSmallImages; controlWheel = next.ControlWheel; hideHiddenFolders = next.HideHiddenFolders;
            folderOpeningMode=next.FolderOpeningMode==1?1:0;
            recordRecentLocations=next.RecordRecentLocations; recordViewingState=next.RecordViewingState;
            deleteEnabled=next.DeleteEnabled;UpdateDeleteMenus();
            minimizeToTray=next.MinimizeToTray;
            allowMultipleInstances=next.AllowMultipleInstances;
            model.ImageThumbnailSize=next.ImageThumbnailSize;model.LocationThumbnailSize=next.LocationThumbnailSize;
            ApplyTheme(next.Dark);
            listFontSize = Math.Clamp(next.ListFontSize, 10, 24);
            ApplyUiSizing(next.UiSize);
            listScroll = (next.ListScroll ?? new()).Normalize();
            slideshowIntervalSeconds = Math.Clamp(next.SlideshowIntervalSeconds, 1, 60); RestartSlideshowDelay();
            ApplyRenderingMode(next.SoftwareRendering);
            ApplyAppearance(next.Appearance);
            loupeSettings = (next.Loupe ?? new()).Normalize(); RefreshLoupe();
            ApplyKeyboardSettings(next.Keyboard);
            CancelPointer(); mouseSettings = (next.Mouse ?? new()).Normalize();
            spreadEnabled = next.SpreadMode; rightBinding = next.RightBinding; SyncSpreadButton();
            ListModeBox.SelectedIndex = next.ListMode;
            ApplyListMode();
            LocationModeBox.SelectedIndex = next.LocationListMode is >= 0 and <= 2 ? next.LocationListMode : 1;
            ApplyLocationMode();
            SelectedImageSort = next.SortMode is >= 0 and <= 5 ? next.SortMode : 0;
            SelectedLocationSort = next.LocationSortMode is >= 0 and <= 5 ? next.LocationSortMode : 0;
            restoreLastView = next.RestoreLastView;
            viewerToolbarPlacement = next.ViewerToolbarPlacement is >= 0 and <= 2 ? next.ViewerToolbarPlacement : 0;
            ApplyToolbarPlacement();
        }
        finally { loadingPreferences = false; }
        if (!recordRecentLocations) { try { await recentSaveTail; } catch { } }
        if (!recordViewingState) { try { await folderPositionSave; } catch { } }
        if (refreshCache) await ApplyCacheSettingsAsync(next.Cache);
        if (resetInitialFit && automaticZoom) ResetZoom();
        if (refreshSpread) { RebaseSpread(); await (activeDisplay = LoadSelectedAsync()); }
        if (resortImages) await FilterAsync();
        if (resortLocations && !refreshFolders) await SortLocationsAsync();
        if (!refreshFolders) return;
        folderTreeVersion++;
        // Rebuild lazy nodes so previously loaded hidden children cannot remain visible.
        Folders.Items.Clear();
        foreach (var drive in System.IO.DriveInfo.GetDrives().Where(d => model.AllowNetwork || d.DriveType != System.IO.DriveType.Network))
            Folders.Items.Add(FolderNode(drive.Name, drive.DriveType));
        if (model.LocationFolder.Length > 0 && !browse.IsCancellationRequested) await LoadLocationsAsync(model.LocationFolder, browse.Token);
    }
}
