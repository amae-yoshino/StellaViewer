namespace StellaViewer.Infrastructure;

public sealed record FolderChangeBatch(string[] Paths, bool RescanRequired, string? Warning = null);

// Coalesce notifications into bounded batches; no image bytes are read by the watcher.
public sealed class FolderChangeMonitor : IDisposable
{
    private const int MaximumPaths = 4096;
    private readonly object gate = new();
    private readonly HashSet<string> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher watcher;
    private readonly Timer timer;
    private readonly Action<FolderChangeBatch> changed;
    private bool disposed, rescan;
    private string? warning;

    public FolderChangeMonitor(string folder, Action<FolderChangeBatch> changed, bool allowNetwork = false)
    {
        NetworkAccess.Validate(folder, allowNetwork);
        this.changed = changed;
        watcher = new(folder) {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime
        };
        timer = new Timer(Flush, null, Timeout.Infinite, Timeout.Infinite);
        watcher.Created += OnChanged; watcher.Changed += OnChanged; watcher.Deleted += OnChanged;
        watcher.Renamed += (_, e) => { Add(e.OldFullPath); Add(e.FullPath); };
        watcher.Error += (_, e) => {
            lock (gate) {
                if (disposed) return;
                rescan = true; pending.Clear(); warning = e.GetException().Message;
                timer.Change(200, Timeout.Infinite);
            }
        };
        try { watcher.EnableRaisingEvents = true; }
        catch { timer.Dispose(); watcher.Dispose(); throw; }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Add(e.FullPath);
    private void Add(string path)
    {
        lock (gate) {
            if (disposed) return;
            var wasEmpty = pending.Count == 0 && !rescan;
            if (!rescan) {
                pending.Add(path);
                if (pending.Count > MaximumPaths) { pending.Clear(); rescan = true; }
            }
            // Fixed first-event delay also makes progress during a continuous stream of changes.
            if (wasEmpty) timer.Change(200, Timeout.Infinite);
        }
    }

    private void Flush(object? state)
    {
        FolderChangeBatch batch;
        lock (gate) {
            if (disposed || pending.Count == 0 && !rescan) return;
            batch = new(pending.ToArray(), rescan, warning);
            pending.Clear(); rescan = false; warning = null;
        }
        changed(batch);
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; pending.Clear(); }
        watcher.Dispose(); timer.Dispose();
    }
}
