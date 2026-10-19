namespace StellaViewer.Infrastructure;

/// <summary>Retains every temporary input in a nested archive chain, independently of its former owners.</summary>
internal sealed class SourceLeaseGroup : IDisposable
{
    private readonly object sync = new();
    private bool disposed;
    private readonly IDisposable[] references;
    internal TemporaryFileLease[] Files { get; }
    public SourceLeaseGroup(IEnumerable<TemporaryFileLease> files)
    {
        Files = files.ToArray(); var acquired = new List<IDisposable>();
        try { foreach (var file in Files) acquired.Add(file.RetainAlive()); }
        catch { foreach (var reference in acquired) reference.Dispose(); throw; }
        references = acquired.ToArray();
    }
    public SourceLeaseGroup Fork()
    {
        lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); return new(Files); }
    }
    public SourceLeaseGroup Append(TemporaryFileLease file)
    {
        lock (sync) { ObjectDisposedException.ThrowIf(disposed, this); return new([..Files, file]); }
    }
    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; foreach (var reference in references) reference.Dispose(); }
    }
}
