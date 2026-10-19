using System.IO;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private PdfSession? pdfSession;
    private async Task OpenPdfAsync(PdfLocation target, bool recordHistory, int request, ArchiveSession? sourceArchive = null, bool updateLocations = true)
    {
        browse.Cancel(); search.Cancel(); CancelPrefetch();
        browse.Dispose(); browse = new(); var token = browse.Token;
        PdfSession? pending = null;
        ArchiveSession? pendingArchive = null;
        Thumbnail[] paused = [];
        try {
            NetworkAccess.Validate(BrowsePath.PhysicalPath(target.Identity), model.AllowNetwork);
            var archiveDocument = ArchiveLocation.Parse(target.DocumentPath);
            if (archiveDocument is not null && pdfSession?.SourcePath != target.DocumentPath) {
                await GetTemporaryStoreAsync();
                paused = ArchivePdfPreviews(LocationArea).ToArray();
                await Task.WhenAll(paused.Select(thumbnail => thumbnail.StopAsync()));
                token.ThrowIfCancellationRequested();
            }
            var parentArchive = sourceArchive ?? (archiveDocument is null ? null
                : archiveSession is not null && ArchiveLocation.SameSource(archiveSession.SourcePath, archiveDocument.ArchivePath)
                    ? archiveSession : await OpenArchiveSourceAsync(archiveDocument.ArchivePath, token));
            if (!ReferenceEquals(parentArchive, archiveSession)) pendingArchive = parentArchive;
            var session = pdfSession is not null && ArchiveLocation.SameSource(pdfSession.SourcePath, target.DocumentPath)
                ? pdfSession : archiveDocument is null ? await PdfSession.OpenAsync(target.DocumentPath, token, model.AllowNetwork)
                    : await PdfSession.OpenArchiveAsync(parentArchive!, archiveDocument, await GetTemporaryStoreAsync(), token, model.AllowNetwork);
            if (!ReferenceEquals(session, pdfSession)) pending = session;
            if (token.IsCancellationRequested || request != openRequestVersion || closed) return;
            var pages = await Task.Run(session.Pages, token);
            if (token.IsCancellationRequested || request != openRequestVersion || closed) return;
            var folder = new PdfLocation(session.SourcePath).Identity;
            if (recordHistory && model.Folder.Length > 0 && model.Folder != folder) RememberLocation();
            StopFolderMonitor();
            if (!ReferenceEquals(archiveSession, parentArchive)) archiveSession?.Dispose();
            archiveSession = parentArchive; model.ArchiveSession = parentArchive; pendingArchive = null;
            if (!ReferenceEquals(pdfSession, session)) pdfSession?.Dispose();
            pdfSession = session; model.PdfSession = session;
            pending = null;
            var page = target.Page is int number && number <= session.PageCount ? number : 1;
            requestedPath = new PdfLocation(session.SourcePath, page).Identity;
            loading = false; model.Folder = folder;
            updating = true; model.Entries = []; updating = false; SyncTiles(true); ClearImage();
            all = pages.ToList();
            if (updateLocations) await LoadLocationsAsync(folder, token);
            if (token.IsCancellationRequested || request != openRequestVersion) return;
            await FilterAsync(); await activeDisplay;
            if(!token.IsCancellationRequested&&request==openRequestVersion)await RememberRecentPlaceAsync(folder);
            if (!token.IsCancellationRequested && target.Page > session.PageCount)
                model.Status = "前回のPDFページが見つからないため、先頭ページを表示します。";
        }
        catch (OperationCanceledException) { }
        catch (FileNotFoundException error) when (error.FileName is null && ArchiveLocation.Parse(target.DocumentPath) is not null) {
            if (!token.IsCancellationRequested && request == openRequestVersion) {
                await OpenAsync(ArchiveLocation.Parse(target.DocumentPath)!.Folder.Identity, recordHistory: false);
                model.Status = "前回の書庫内PDFが見つからないため、書庫内フォルダを開きました。";
            }
        }
        catch (Exception error) { if (!token.IsCancellationRequested && request == openRequestVersion) model.Status = "PDFを開けませんでした：" + error.Message; }
        finally {
            pending?.Dispose();
            pendingArchive?.Dispose();
            if (!closed && !closingInProgress && request == openRequestVersion && (!PdfLocation.IsIdentity(model.Folder)||!updateLocations))
                foreach (var thumbnail in paused) thumbnail.Refresh();
        }
    }
    private bool TryJumpToPage(int number)
    {
        if (number < 1) return false;
        if (pdfSession is { } session && SearchBox.Text.Length == 0) {
            if (number > session.PageCount) return false;
            var index = model.Entries.ToList().FindIndex(entry => PdfLocation.Parse(entry.Path)?.Page == number);
            if (index < 0) return false;
            SelectPageAt(index); return true;
        }
        if (number > model.Entries.Count) return false;
        SelectPageAt(number - 1); return true;
    }
    private static string EntrySizeLabel(FileEntry entry) => entry.IsPdfPage ? "元PDF容量：" : "";
    private static string EntryDateLabel(FileEntry entry) => entry.IsPdfPage ? "元PDF更新日時" : "更新日時";
}
