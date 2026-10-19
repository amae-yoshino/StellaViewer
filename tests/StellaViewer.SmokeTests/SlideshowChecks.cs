using StellaViewer.Infrastructure;

internal static class SlideshowChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        Check(new ViewerPreferences().SlideshowIntervalSeconds == 3, "Slideshow default interval");
        var root = Path.Combine(Path.GetFullPath(output), "slideshow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "preferences.json"); File.WriteAllText(path, "{}");
        Check(PreferencesStore.Load(path).SlideshowIntervalSeconds == 3, "Legacy slideshow default");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(slideshowIntervalSeconds: 8, filePath: path), PreferencesStore.SaveChangeAsync(listFontSize: 20, filePath: path));
        var saved = PreferencesStore.Load(path);
        Check(saved.SlideshowIntervalSeconds == 8 && saved.ListFontSize == 20, "Slideshow setting merges independently");
        await PreferencesStore.SaveChangeAsync(slideshowIntervalSeconds: -1, filePath: path);
        Check(PreferencesStore.Load(path).SlideshowIntervalSeconds == 1, "Slideshow minimum interval");
        await PreferencesStore.SaveChangeAsync(slideshowIntervalSeconds: 999, filePath: path);
        Check(PreferencesStore.Load(path).SlideshowIntervalSeconds == 60, "Slideshow maximum interval");
        var exported = Path.Combine(root, "settings.json"); await SettingsTransfer.ExportAsync(exported, saved);
        Check((await SettingsTransfer.ImportAsync(exported)).SlideshowIntervalSeconds == 8, "Slideshow settings transfer");
        Check(SettingsTransfer.Normalize(new() { SlideshowIntervalSeconds = 999 }).SlideshowIntervalSeconds == 60, "Slideshow import normalization");
        return count;
    }
}
