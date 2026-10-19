using System.IO;
using System.Windows;
using System.Windows.Media;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private Task<ArchiveTemporaryStore>? temporaryStoreStartup;
    private Task temporaryCleanup = Task.CompletedTask;
    private Task startupTemporaryCleanup = Task.CompletedTask;
    internal Task TemporaryCleanup => temporaryCleanup;
    private static IEnumerable<Thumbnail> ArchivePdfPreviews(DependencyObject parent)
    {
        if (parent is Thumbnail thumbnail && thumbnail.FilePath is string path
            && ArchiveLocation.Parse(path) is { IsFolder: false } item
            && Path.GetExtension(item.EntryPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) yield return thumbnail;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in ArchivePdfPreviews(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private string TemporaryRoot => Path.Combine(storageDirectory ?? (usePreferences
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellaViewer")
        : Path.Combine(AppContext.BaseDirectory, "test-temp")), "Temp");
    private void InitializeTemporaryCleanup()
    {
        if (!usePreferences) return;
        var root = TemporaryRoot;
        startupTemporaryCleanup = Task.Run(() => {
            try { ArchiveTemporaryStore.CleanupUnused(root); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });
    }
    private async Task<ArchiveTemporaryStore> GetTemporaryStoreAsync()
    {
        await startupTemporaryCleanup;
        ObjectDisposedException.ThrowIf(closed || closingInProgress, this);
        var root = TemporaryRoot;
        var creation = temporaryStoreStartup ??= Task.Run(() => new ArchiveTemporaryStore(root));
        ArchiveTemporaryStore store;
        try { store = await creation; }
        catch { if (ReferenceEquals(creation, temporaryStoreStartup)) temporaryStoreStartup = null; throw; }
        if (!closed && !closingInProgress) model.TemporaryStore = store;
        return store;
    }
    private async Task DisposeTemporaryFilesAsync()
    {
        await locationRefresh;
        CancelMetadataRead();await MetadataIdle;metadataOperations.RemoveAll(operation=>operation.IsCompleted);
        RememberFolderImage();await folderPositionSave;
        model.PdfSession = null; model.ArchiveSession = null; model.TemporaryStore = null;
        pdfSession?.Dispose(); pdfSession = null;
        model.LocationArchiveSession?.Dispose(); model.LocationArchiveSession = null;
        archiveSession?.Dispose(); archiveSession = null;
        await startupTemporaryCleanup;
        if (temporaryStoreStartup is not null) {
            try { await (await temporaryStoreStartup).DisposeAsync(); }
            catch (IOException) { /* The next startup can retry an unused owner directory. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
    private async Task<ArchiveSession> OpenArchiveSourceAsync(string source, CancellationToken token) =>
        await ArchiveSession.OpenAsync(source, token, model.AllowNetwork,
            ArchiveLocation.IsIdentity(source) ? await GetTemporaryStoreAsync() : null, archiveSession);
}
