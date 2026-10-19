using StellaViewer.Infrastructure;

internal static class UiSizeChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        Check(new UiSizeSettings().Normalize() == new UiSizeSettings(100, 14), "UI sizing defaults preserve existing appearance");
        Check(new UiSizeSettings(1, 1).Normalize() == new UiSizeSettings(75, 10), "UI sizing lower bounds");
        Check(new UiSizeSettings(int.MaxValue, int.MaxValue).Normalize() == new UiSizeSettings(150, 24), "UI sizing upper bounds");
        var root = Path.Combine(Path.GetFullPath(output), "ui-size-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "preferences.json"); File.WriteAllText(path, "{\"Dark\":true}");
        Check(PreferencesStore.Load(path).UiSize == new UiSizeSettings() && PreferencesStore.Load(path).Dark, "Legacy preferences gain UI sizing defaults");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(uiScalePercent: 130, filePath: path), PreferencesStore.SaveChangeAsync(uiFontSize: 19, filePath: path));
        Check(PreferencesStore.Load(path).UiSize == new UiSizeSettings(130, 19), "Independent concurrent UI sizing changes merge");
        var transfer = Path.Combine(root, "export.json"); await SettingsTransfer.ExportAsync(transfer, PreferencesStore.Load(path));
        Check((await SettingsTransfer.ImportAsync(transfer)).UiSize == new UiSizeSettings(130, 19), "UI sizing survives settings export and import");
        Check(SettingsTransfer.Normalize(new() { UiSize = new(-1, 999) }).UiSize == new UiSizeSettings(75, 24), "Imported UI sizing is normalized");
        return count;
    }
}
