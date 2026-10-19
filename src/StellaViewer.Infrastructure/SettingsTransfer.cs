using System.Text.Json;

namespace StellaViewer.Infrastructure;

public static class SettingsTransfer
{
    private sealed record Document(string Application, int Version, ViewerPreferences Settings);
    public static ViewerPreferences Normalize(ViewerPreferences value) => value with {
        UiSize = (value.UiSize ?? new()).Normalize(),
        ListScroll = (value.ListScroll ?? new()).Normalize(),
        ListFontSize = Math.Clamp(value.ListFontSize, 10, 24),
        SlideshowIntervalSeconds = Math.Clamp(value.SlideshowIntervalSeconds, 1, 60),
        ListMode = Math.Clamp(value.ListMode, 0, 2), LocationListMode = Math.Clamp(value.LocationListMode, 0, 2),
        SortMode = Math.Clamp(value.SortMode, 0, 5), LocationSortMode = Math.Clamp(value.LocationSortMode, 0, 5),
        ViewerToolbarPlacement = Math.Clamp(value.ViewerToolbarPlacement, 0, 2), FolderOpeningMode = value.FolderOpeningMode == 1 ? 1 : 0,
        ImageThumbnailSize = ThumbnailLayout.Normalize(value.ImageThumbnailSize), LocationThumbnailSize = ThumbnailLayout.Normalize(value.LocationThumbnailSize),
        Appearance = (value.Appearance ?? new()).Normalize(), Cache = (value.Cache ?? new()).Normalize(),
        Loupe = (value.Loupe ?? new()).Normalize(), Keyboard = (value.Keyboard ?? new()).Normalize(), Mouse = (value.Mouse ?? new()).Normalize()
    };
    public static Task ExportAsync(string path, ViewerPreferences value) => Task.Run(() => {
        var full = Path.GetFullPath(path); var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, new Document("StellaViewer", 1, Normalize(value)), new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true);
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });
    public static Task<ViewerPreferences> ImportAsync(string path) => Task.Run(() => {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 4 * 1024 * 1024) throw new InvalidDataException("設定ファイルがサイズ上限（4 MiB）を超えています。");
        var document = JsonSerializer.Deserialize<Document>(input);
        if (document is null || document.Application != "StellaViewer" || document.Version != 1 || document.Settings is null)
            throw new InvalidDataException("対応するStellaViewerの設定ファイルではありません。");
        return Normalize(document.Settings);
    });
}
