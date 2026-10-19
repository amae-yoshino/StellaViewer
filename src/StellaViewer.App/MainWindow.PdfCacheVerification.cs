using System.IO;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyPdfCacheAsync(string imagePath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var root = Path.GetDirectoryName(imagePath)!;
        var storage = Path.Combine(root, "pdf-cache-ui-" + Guid.NewGuid().ToString("N"));
        var probe = new MainWindow(false, storage) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        try {
            probe.Show(); await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { Cache = new(8, 0, 2) });
            await probe.OpenAsync(Path.Combine(root, "pdf-checks", "pages.pdf")); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage && probe.imageCache.MemoryCount == 3 && probe.imageCache.MemoryBytes <= 8 * 1024 * 1024, "Physical PDF fills bounded common cache with neighboring pages");
            var decodes = probe.imageCache.Decodes; var hits = probe.imageCache.MemoryHits;
            probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes == decodes && probe.imageCache.MemoryHits > hits, "PDF page turn reuses prefetched pixels in actual viewer");
            await probe.ReloadAsync(); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes > decodes, "F5 invalidates current PDF before rendering and prefetching again");
            probe.SearchBox.Text = "2"; await probe.FilterAsync(); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.Entries.Count == 1 && PdfLocation.Parse(probe.model.SelectedPath!)?.Page == 2, "PDF prefetch follows current filtered page list");
            probe.SearchBox.Text = ""; await probe.FilterAsync(); await probe.activeDisplay; await probe.prefetchWork;
            var image = probe.model.Image;
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { Cache = new(0, 0, 2) }); await probe.prefetchWork;
            Check(probe.imageCache.MemoryBytes == 0 && ReferenceEquals(image, probe.model.Image), "Disabling PDF cache keeps displayed bitmap and releases reusable pages");
            decodes = probe.imageCache.Decodes; probe.Move(1); await probe.activeDisplay;
            Check(probe.imageCache.Decodes == decodes + 1 && probe.imageCache.MemoryCount == 0, "PDF still displays with both caches disabled and no speculative retention");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { Cache = new(8, 0, 2) });
            var archive = Path.Combine(root, "archive-pdf-checks", "book.zip");
            var document = new ArchiveLocation(archive, "日本語/book.pdf", false);
            await probe.OpenAsync(new PdfLocation(document.Identity, 2).Identity); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage && probe.imageCache.MemoryCount >= 3, "Archive PDF pages participate in foreground cache and prefetch");
            decodes = probe.imageCache.Decodes; probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes == decodes, "Archive PDF neighbor does not rerender on navigation");
            await probe.ReloadAsync(); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes > decodes, "Archive PDF F5 bypasses cached page identities");
            var inner = new ArchiveLocation(Path.Combine(root, "nested-checks", "outer.zip"), "books/inner.zip", false);
            var leaf = new ArchiveLocation(inner.Identity, "children/leaf.cbz", false);
            document = new ArchiveLocation(leaf.Identity, "sub/book.pdf", false);
            await probe.OpenAsync(new PdfLocation(document.Identity, 2).Identity); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage, "Nested archive PDF opens with page prefetch enabled");
            decodes = probe.imageCache.Decodes; probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes == decodes, "Nested archive PDF navigation reuses page cache");
            await probe.OpenAsync(imagePath); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.pdfSession is null && probe.model.TemporaryStore?.UsedBytes == 0, "Leaving PDF cache releases all nested temporary inputs");
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
