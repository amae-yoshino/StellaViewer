using System.Text.Json;

namespace StellaViewer.Infrastructure;

public sealed record ViewerPreferences(bool Dark = false, bool ControlWheel = true, int ListMode = 1, bool HideHiddenFolders = true, int ViewerToolbarPlacement = 0, int SortMode = 0, bool RestoreLastView = true, int LocationListMode = 1, int LocationSortMode = 0)
{
    public UiSizeSettings UiSize { get; init; } = new();
    public ListScrollSettings ListScroll { get; init; } = new();
    public int ListFontSize { get; init; } = 14;
    public int SlideshowIntervalSeconds { get; init; } = 3;
    public ViewerAppearance Appearance { get; init; } = new();
    public bool RecordRecentLocations { get; init; } = true;
    public bool RecordViewingState { get; init; } = true;
    public bool SpreadMode { get; init; }
    public bool EnlargeSmallImages { get; init; }
    public bool SoftwareRendering { get; init; }
    public bool AllowNetwork { get; init; }
    public int FolderOpeningMode { get; init; }
    public bool DeleteEnabled { get; init; }
    public bool MinimizeToTray { get; init; }
    public bool AllowMultipleInstances { get; init; }=true;
    public int ImageThumbnailSize { get; init; } = 1;
    public int LocationThumbnailSize { get; init; } = 1;
    public ImageCacheSettings Cache { get; init; } = new();
    public bool RightBinding { get; init; } = true;
    public LoupeSettings Loupe { get; init; } = new();
    public StellaViewer.Core.KeyboardSettings Keyboard { get; init; } = new();
    public StellaViewer.Core.MouseSettings Mouse { get; init; } = new();
}

public static class PreferencesStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellaViewer", "viewer-preferences.json");
    public static ViewerPreferences Load(string? filePath = null)
    {
        // The cache also reads settings in the background; synchronize snapshots with atomic writers.
        using var mutex = new Mutex(false, @"Local\StellaViewer.ViewerPreferences");
        var acquired = false;
        try {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("設定ファイルの使用待ちがタイムアウトしました。");
            return ReadSnapshot(filePath);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
    private static ViewerPreferences ReadSnapshot(string? filePath)
    {
        return RecoverableSettings.Read(filePath ?? FilePath, () => new ViewerPreferences());
    }

    public static Task SaveChangeAsync(bool? recordRecentLocations = null, bool? recordViewingState = null, bool? dark = null, bool? controlWheel = null, string? filePath = null, int? listMode = null, bool? hideHiddenFolders = null, int? viewerToolbarPlacement = null, int? sortMode = null, bool? restoreLastView = null, int? locationListMode = null, int? locationSortMode = null, ViewerAppearance? appearance = null, ViewerAppearance? previousAppearance = null, bool? spreadMode = null, bool? rightBinding = null, double? loupeMagnification = null, int? loupeSize = null, StellaViewer.Core.KeyboardSettings? keyboard = null, StellaViewer.Core.KeyboardSettings? previousKeyboard = null, bool? softwareRendering = null, bool? allowNetwork = null, ImageCacheSettings? cache = null, ImageCacheSettings? previousCache = null, int? folderOpeningMode = null, int? imageThumbnailSize = null, int? locationThumbnailSize = null, bool? deleteEnabled = null, bool? minimizeToTray = null, bool? allowMultipleInstances = null, StellaViewer.Core.MouseSettings? mouse = null, StellaViewer.Core.MouseSettings? previousMouse = null, int? uiScalePercent = null, int? uiFontSize = null, int? listScrollPercent = null, int? listFontSize = null, int? slideshowIntervalSeconds = null, bool? enlargeSmallImages = null) => Task.Run(() =>
    {
        using var mutex = new Mutex(false, @"Local\StellaViewer.ViewerPreferences");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("設定ファイルの使用待ちがタイムアウトしました。");
            var current = ReadSnapshot(filePath);
            var next = current with { SlideshowIntervalSeconds = Math.Clamp(slideshowIntervalSeconds ?? current.SlideshowIntervalSeconds, 1, 60), ListFontSize = Math.Clamp(listFontSize ?? current.ListFontSize, 10, 24), ListScroll = new ListScrollSettings(listScrollPercent ?? current.ListScroll?.AmountPercent ?? 100).Normalize(), UiSize = new UiSizeSettings(uiScalePercent ?? current.UiSize?.ScalePercent ?? 100, uiFontSize ?? current.UiSize?.FontSize ?? 14).Normalize(), RecordRecentLocations = recordRecentLocations ?? current.RecordRecentLocations, RecordViewingState = recordViewingState ?? current.RecordViewingState, Dark = dark ?? current.Dark, ControlWheel = controlWheel ?? current.ControlWheel, ListMode = listMode ?? current.ListMode,
                HideHiddenFolders = hideHiddenFolders ?? current.HideHiddenFolders,
                EnlargeSmallImages = enlargeSmallImages ?? current.EnlargeSmallImages,
                ViewerToolbarPlacement = viewerToolbarPlacement ?? current.ViewerToolbarPlacement,
                SortMode = sortMode ?? current.SortMode, RestoreLastView = restoreLastView ?? current.RestoreLastView,
                LocationListMode = locationListMode ?? current.LocationListMode,
                LocationSortMode = locationSortMode ?? current.LocationSortMode,
                SpreadMode = spreadMode ?? current.SpreadMode, RightBinding = rightBinding ?? current.RightBinding,
                SoftwareRendering = softwareRendering ?? current.SoftwareRendering,
                AllowNetwork = allowNetwork ?? current.AllowNetwork,
                DeleteEnabled = deleteEnabled ?? current.DeleteEnabled,
                MinimizeToTray=minimizeToTray??current.MinimizeToTray,
                AllowMultipleInstances=allowMultipleInstances??current.AllowMultipleInstances,
                FolderOpeningMode = folderOpeningMode is null ? current.FolderOpeningMode : folderOpeningMode==1?1:0,
                ImageThumbnailSize = imageThumbnailSize is null ? current.ImageThumbnailSize : ThumbnailLayout.Normalize(imageThumbnailSize.Value),
                LocationThumbnailSize = locationThumbnailSize is null ? current.LocationThumbnailSize : ThumbnailLayout.Normalize(locationThumbnailSize.Value),
                Cache = cache is null ? current.Cache : (current.Cache ?? new()).MergeChanges(previousCache ?? current.Cache ?? new(), cache).Normalize(),
                Mouse = mouse is null ? current.Mouse : (current.Mouse ?? new()).MergeChanges(previousMouse ?? current.Mouse ?? new(), mouse),
                Keyboard = keyboard is null ? current.Keyboard : (current.Keyboard ?? new()).MergeChanges(previousKeyboard ?? current.Keyboard ?? new(), keyboard),
                Loupe = (current.Loupe ?? new()) with { Magnification = loupeMagnification ?? current.Loupe?.Magnification ?? 2, Size = loupeSize ?? current.Loupe?.Size ?? 240 },
                Appearance = appearance is null ? current.Appearance : (current.Appearance ?? new()).MergeChanges(previousAppearance ?? current.Appearance ?? new(), appearance) };
            var path = filePath ?? FilePath;
            RecoverableSettings.Write(path, next);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    });
}
