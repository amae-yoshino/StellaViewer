using System.IO;
using System.Windows;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyUnflushedListPreferencesAsync(string imagePath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var directory = Path.Combine(Path.GetDirectoryName(imagePath)!, "list-preferences-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var preferencesFile = Path.Combine(directory, "viewer-preferences.json");
        await File.WriteAllTextAsync(preferencesFile, "{}");
        MainWindow Create() => new(true, directory) {
            ConstrainStartupWindowToScreen = false, ShowInTaskbar = false, ShowActivated = false,
            WatchFoldersEnabled = false, Left = -32000, Top = -32000
        };
        var first = Create(); first.Show();
        try {
            // Simulate a view update whose selection notifications were suppressed.
            await first.ApplyPreferencesAsync(first.CurrentPreferences with { ListMode = 2, SortMode = 2, LocationListMode = 0 });
            first.loadingPreferences = true;
            try { first.SelectedLocationSort = 1; }
            finally { first.loadingPreferences = false; }
            Check(PreferencesStore.Load(preferencesFile).ListMode == 1, "Fixture has a genuine unsaved list-mode difference");
            await PreferencesStore.SaveChangeAsync(dark: true, controlWheel: false, filePath: preferencesFile);
            first.Close(); await first.closePersistence;
            var stored = PreferencesStore.Load(preferencesFile);
            Check(stored.ListMode == 2 && stored.SortMode == 2 && stored.LocationListMode == 0 && stored.LocationSortMode == 1,
                "Close flushes all four unqueued list preferences");
            Check(stored.Dark && !stored.ControlWheel, "Close preserves unrelated settings changed by another window");
        }
        finally { if (!first.closeCommitted) { first.Close(); await first.closePersistence; } }

        var second = Create(); second.Show();
        try {
            second.ListModeBox.SelectedIndex = 0; await second.preferenceSaveTail;
            Check(PreferencesStore.Load(preferencesFile).ListMode == 0, "Normal list-mode changes are saved immediately");
            await PreferencesStore.SaveChangeAsync(listMode: 1, sortMode: 0, locationListMode: 2, locationSortMode: 2, filePath: preferencesFile);
            second.Close(); await second.closePersistence;
            var stored = PreferencesStore.Load(preferencesFile);
            Check(stored.ListMode == 1 && stored.SortMode == 0 && stored.LocationListMode == 2 && stored.LocationSortMode == 2,
                "Closing a stale window does not replay its already-saved list values");
        }
        finally { if (!second.closeCommitted) { second.Close(); await second.closePersistence; } }

        var third = Create(); third.Show();
        try {
            await third.ApplyPreferencesAsync(third.CurrentPreferences with { ListMode = 2 });
            await PreferencesStore.SaveChangeAsync(sortMode: 1, locationListMode: 0, locationSortMode: 0, filePath: preferencesFile);
            third.Close(); await third.closePersistence;
            var stored = PreferencesStore.Load(preferencesFile);
            Check(stored.ListMode == 2 && stored.SortMode == 1 && stored.LocationListMode == 0 && stored.LocationSortMode == 0,
                "Closing writes only the locally unsaved list field");
        }
        finally { if (!third.closeCommitted) { third.Close(); await third.closePersistence; } }
    }
}
