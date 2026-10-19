using System.IO;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private ArchiveSession? archiveSession;
    private async Task OpenArchiveAsync(ArchiveLocation target, bool recordHistory, int request, ArchiveSession? prepared = null, bool updateLocations = true)
    {
        if (closed || closingInProgress) { prepared?.Dispose(); return; }
        browse.Cancel(); search.Cancel(); CancelPrefetch();
        browse.Dispose(); browse = new(); var token = browse.Token;
        ArchiveSession? pending = prepared;
        try {
            NetworkAccess.Validate(ArchiveLocation.PhysicalPath(target.ArchivePath), model.AllowNetwork);
            var session = prepared ?? (archiveSession is not null && ArchiveLocation.SameSource(archiveSession.SourcePath, target.ArchivePath)
                ? archiveSession : await OpenArchiveSourceAsync(target.ArchivePath, token));
            if (!ReferenceEquals(session, archiveSession)) pending = session;
            if (token.IsCancellationRequested || request != openRequestVersion || closed) return;
            if (!target.IsFolder && Path.GetExtension(target.EntryPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) {
                await OpenPdfAsync(new(target.Identity), recordHistory, request, session, updateLocations);
                if (ReferenceEquals(archiveSession, session)) pending = null;
                return;
            }
            if (!target.IsFolder && !FolderBrowser.IsImageFile(target.EntryPath)) {
                if (!ArchiveSession.Supports(target.EntryPath)) throw new NotSupportedException("この書庫内項目は閲覧できません。");
                var nested = await ArchiveSession.OpenNestedAsync(session, target, await GetTemporaryStoreAsync(), token, model.AllowNetwork);
                await OpenArchiveAsync(new(target.Identity), recordHistory, request, nested, updateLocations);
                return;
            }
            var folder = target.Folder; string? warning = null;
            if (!session.ContainsFolder(folder.EntryPath)) {
                folder = new(target.ArchivePath); warning = "前回の書庫内フォルダが見つからないため、書庫の先頭を表示します。";
            }
            var contents = await Task.Run(() => (Images: session.Images(folder.EntryPath), HasPdf: session.Locations(folder.EntryPath).Any(i => i.IsPdf)), token);
            if (contents.HasPdf) {
                try { await GetTemporaryStoreAsync(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Image browsing needs no temporary storage. */ }
            }
            if (token.IsCancellationRequested || request != openRequestVersion || closed) return;
            if (recordHistory && model.Folder.Length > 0 && model.Folder != folder.Identity) RememberLocation();
            StopFolderMonitor();
            pdfSession?.Dispose(); pdfSession = null; model.PdfSession = null;
            if (!ReferenceEquals(archiveSession, session)) archiveSession?.Dispose();
            archiveSession = session; model.ArchiveSession = session;
            pending = null;
            requestedPath = !target.IsFolder && session.ContainsImage(target.EntryPath) ? target.Identity : null;
            if (!target.IsFolder && requestedPath is null) warning = "前回の画像が見つからないため、書庫内フォルダの先頭を表示します。";
            loading = false; model.Folder = folder.Identity;
            updating = true; model.Entries = []; updating = false; SyncTiles(true); ClearImage();
            all = contents.Images.ToList();
            if (updateLocations) await LoadLocationsAsync(folder.Identity, token);
            if (token.IsCancellationRequested || request != openRequestVersion) return;
            await FilterAsync(); await activeDisplay;
            if(!token.IsCancellationRequested&&request==openRequestVersion)await RememberRecentPlaceAsync(folder.Identity);
            if (!token.IsCancellationRequested && warning is not null) model.Status = warning;
        }
        catch (OperationCanceledException) { }
        catch (FileNotFoundException error) when (error.FileName is null && ArchiveLocation.Parse(target.ArchivePath) is not null) {
            if (!token.IsCancellationRequested && request == openRequestVersion) {
                await OpenAsync(ArchiveLocation.Parse(target.ArchivePath)!.Folder.Identity, recordHistory: false);
                model.Status = "前回の入れ子書庫が見つからないため、外側の書庫内フォルダを開きました。";
            }
        }
        catch (Exception error) { if (!token.IsCancellationRequested && request == openRequestVersion) model.Status = "書庫を開けませんでした：" + error.Message; }
        finally { pending?.Dispose(); }
    }
    private Task<DecodedImage> DecodePageAsync(FileEntry entry, CancellationToken token)
    {
        if (PdfLocation.Parse(entry.Path) is { Page: int page } pdf) {
            if (pdfSession is null || !StringComparer.OrdinalIgnoreCase.Equals(pdfSession.SourcePath, pdf.DocumentPath))
                throw new InvalidOperationException("PDFが開かれていません。");
            return imageCache.GetPdfAsync(pdfSession, page, token, model.AllowNetwork);
        }
        var location = ArchiveLocation.Parse(entry.Path);
        return location is null ? imageCache.GetAsync(entry.Path, token, model.AllowNetwork)
            : imageCache.GetArchiveAsync(archiveSession ?? throw new InvalidOperationException("書庫が開かれていません。"), location, token, model.AllowNetwork);
    }
    private async Task UpAsync()
    {
        if (PdfLocation.Parse(model.Folder) is { } pdf) {
            if (ArchiveLocation.Parse(pdf.DocumentPath) is { } archivePdf) {
                await OpenAsync(archivePdf.Folder.Identity);
                sortingLocations = true;
                try {
                    Locations.SelectedItem = model.Locations.FirstOrDefault(i => i.Path == archivePdf.Identity);
                    model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
                }
                finally { sortingLocations = false; }
                return;
            }
            if (Path.GetDirectoryName(pdf.DocumentPath) is { } directory) {
                await OpenAsync(directory);
                sortingLocations = true;
                try {
                    Locations.SelectedItem = model.Locations.FirstOrDefault(i => StringComparer.OrdinalIgnoreCase.Equals(i.Path, pdf.DocumentPath));
                    model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
                }
                finally { sortingLocations = false; }
            }
            return;
        }
        if (ArchiveLocation.Parse(model.Folder) is { } location) {
            if (location.EntryPath.Length > 0) await OpenAsync((location with { EntryPath = ArchiveLocation.ParentEntry(location.EntryPath) }).Identity);
            else if (ArchiveLocation.Parse(location.ArchivePath) is { } outerItem) {
                var previousRequest = openRequestVersion;
                try {
                    var current = archiveSession;
                    var parentView = current is null ? null : await Task.Run(current.CreateParentView);
                    if (closed || closingInProgress || previousRequest != openRequestVersion) { parentView?.Dispose(); return; }
                    if (parentView is not null) await OpenArchiveAsync(outerItem.Folder, true, ++openRequestVersion, parentView);
                    else await OpenAsync(outerItem.Folder.Identity);
                    sortingLocations = true;
                    try {
                        Locations.SelectedItem = model.Locations.FirstOrDefault(i => i.Path == outerItem.Identity);
                        model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
                    }
                    finally { sortingLocations = false; }
                }
                catch (Exception error) { if (!closed && previousRequest == openRequestVersion) model.Status = "上へ移動できませんでした：" + error.Message; }
            }
            else if (Path.GetDirectoryName(location.ArchivePath) is { } parent) {
                await OpenAsync(parent);
                sortingLocations = true;
                try {
                    Locations.SelectedItem = model.Locations.FirstOrDefault(i => StringComparer.OrdinalIgnoreCase.Equals(i.Path, location.ArchivePath));
                    model.SelectedLocationPath = (Locations.SelectedItem as LocationEntry)?.Path;
                }
                finally { sortingLocations = false; }
            }
        }
        else if (model.Folder.Length > 0 && Directory.GetParent(model.Folder) is { } parent) await OpenAsync(parent.FullName);
    }
}
