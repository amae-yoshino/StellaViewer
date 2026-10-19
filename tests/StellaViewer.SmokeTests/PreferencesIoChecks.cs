using StellaViewer.Infrastructure;

internal static class PreferencesIoChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var directory = Path.Combine(Path.GetFullPath(output),"preferences-io",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,"viewer-preferences.json");
        await PreferencesStore.SaveChangeAsync(dark:true,controlWheel:false,filePath:path);
        using var stop = new CancellationTokenSource();
        var reads = 0;
        var readers = Enumerable.Range(0,4).Select(_ => Task.Run(() => {
            while (!stop.IsCancellationRequested) {
                var current = PreferencesStore.Load(path);
                if (!current.Dark || current.ControlWheel) throw new Exception("Concurrent load observed incomplete preferences");
                Interlocked.Increment(ref reads);
            }
        })).ToArray();
        Exception? failure = null;
        try { for(var i=0;i<100;i++) await PreferencesStore.SaveChangeAsync(listMode:i%3,filePath:path); }
        catch (Exception error) { failure = error; }
        finally { stop.Cancel(); }
        await Task.WhenAll(readers);
        if (failure is not null) throw new InvalidOperationException("Concurrent preferences replacement failed",failure);
        if (reads == 0 || PreferencesStore.Load(path).ListMode != 0) throw new Exception("Preferences IO check did not complete");
        Console.WriteLine($"PASS: Concurrent preferences snapshots permit atomic replacement ({reads} reads)");
        return 1;
    }
}
