using StellaViewer.Infrastructure;

internal static class ListSettingsChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        Check(new ListScrollSettings().AmountPercent == 100 && new ViewerPreferences().ListFontSize == 14, "List settings defaults");
        Check(new ListScrollSettings(-1).Normalize().AmountPercent == 25, "List scroll lower bound");
        Check(new ListScrollSettings(int.MaxValue).Normalize().AmountPercent == 400, "List scroll upper bound");
        var root = Path.Combine(Path.GetFullPath(output), "list-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "preferences.json"); File.WriteAllText(path, "{\"Dark\":true}");
        Check(PreferencesStore.Load(path).ListFontSize == 14 && PreferencesStore.Load(path).ListScroll.AmountPercent == 100, "Legacy list settings defaults");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(listScrollPercent: 200, filePath: path), PreferencesStore.SaveChangeAsync(listFontSize: 20, filePath: path));
        var saved = PreferencesStore.Load(path);
        Check(saved.ListFontSize == 20 && saved.ListScroll.AmountPercent == 200 && saved.Dark, "Concurrent independent list settings merge");
        var transfer = Path.Combine(root, "export.json"); await SettingsTransfer.ExportAsync(transfer, saved);
        var imported = await SettingsTransfer.ImportAsync(transfer);
        Check(imported.ListFontSize == 20 && imported.ListScroll.AmountPercent == 200, "List settings export and import");
        var normalized = SettingsTransfer.Normalize(new() { ListFontSize = 999, ListScroll = new(-1) });
        Check(normalized.ListFontSize == 24 && normalized.ListScroll.AmountPercent == 25, "Imported list settings normalize");
        return count;
    }
}
