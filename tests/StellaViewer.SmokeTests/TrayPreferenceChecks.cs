using StellaViewer.Infrastructure;
internal static class TrayPreferenceChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"tray-preferences",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var file=Path.Combine(root,"preferences.json");var count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        File.WriteAllText(file,"{\"DeleteEnabled\":true}");Check(!PreferencesStore.Load(file).MinimizeToTray,"Existing preferences retain ordinary minimize behavior");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(filePath:file,minimizeToTray:true),PreferencesStore.SaveChangeAsync(filePath:file,dark:true));
        Check(PreferencesStore.Load(file) is{MinimizeToTray:true,Dark:true,DeleteEnabled:true},"Tray preference merges independently with concurrent changes");
        await PreferencesStore.SaveChangeAsync(filePath:file,minimizeToTray:false);Check(PreferencesStore.Load(file) is{MinimizeToTray:false,DeleteEnabled:true},"Tray reset leaves unrelated deletion setting unchanged");
        Check(PreferencesStore.Load(file).AllowMultipleInstances,"Existing settings continue allowing multiple instances");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(filePath:file,allowMultipleInstances:false),PreferencesStore.SaveChangeAsync(filePath:file,minimizeToTray:true));
        Check(PreferencesStore.Load(file) is{AllowMultipleInstances:false,MinimizeToTray:true,DeleteEnabled:true},"Instance permission persists independently with concurrent tray preference");
        await PreferencesStore.SaveChangeAsync(filePath:file,allowMultipleInstances:true);Check(PreferencesStore.Load(file) is{AllowMultipleInstances:true,MinimizeToTray:true},"Instance reset does not overwrite tray setting");return count;
    }
}
