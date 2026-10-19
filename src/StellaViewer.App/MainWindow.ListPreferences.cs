using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private sealed record ListPreferences(int ImageMode, int ImageSort, int LocationMode, int LocationSort);
    private ListPreferences savedListPreferences = new(1, 0, 1, 0);
    private ListPreferences ReadListPreferences() => new(ListModeBox.SelectedIndex, SelectedImageSort,
        LocationModeBox.SelectedIndex, SelectedLocationSort);
    private void RecordSavedListPreferences(int? imageMode = null, int? imageSort = null, int? locationMode = null, int? locationSort = null)
    {
        savedListPreferences = savedListPreferences with {
            ImageMode = imageMode ?? savedListPreferences.ImageMode, ImageSort = imageSort ?? savedListPreferences.ImageSort,
            LocationMode = locationMode ?? savedListPreferences.LocationMode, LocationSort = locationSort ?? savedListPreferences.LocationSort
        };
    }
    private Task SaveListPreferencesAsync(int? imageMode = null, int? imageSort = null, int? locationMode = null, int? locationSort = null)
        => SavePreferencesAsync(async () => {
            await PreferencesStore.SaveChangeAsync(listMode: imageMode, sortMode: imageSort,
                locationListMode: locationMode, locationSortMode: locationSort, filePath: preferenceFilePath);
            RecordSavedListPreferences(imageMode, imageSort, locationMode, locationSort);
        });
    private Task FlushUnsavedListPreferencesAsync()
    {
        var current = ReadListPreferences(); var saved = savedListPreferences;
        if (current == saved) return Task.CompletedTask;
        // Write only local unsaved changes; a stale window must not overwrite shared values.
        return SaveListPreferencesAsync(
            imageMode: current.ImageMode != saved.ImageMode ? current.ImageMode : null,
            imageSort: current.ImageSort != saved.ImageSort ? current.ImageSort : null,
            locationMode: current.LocationMode != saved.LocationMode ? current.LocationMode : null,
            locationSort: current.LocationSort != saved.LocationSort ? current.LocationSort : null);
    }
}
