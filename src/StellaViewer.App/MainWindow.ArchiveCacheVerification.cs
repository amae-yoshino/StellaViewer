using System.IO;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyArchiveCacheAsync(string imagePath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var root = Path.GetDirectoryName(imagePath)!; var data = Path.Combine(root, "archive-cache");
        var probe = new MainWindow(false, Path.Combine(data, "ui-" + Guid.NewGuid().ToString("N"))) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        try {
            probe.Show(); await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { Cache = new(8, 0, 2) });
            var book = Path.Combine(data, "book.zip");
            await probe.OpenAsync(book); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage && probe.model.Entries.Count == 3 && probe.imageCache.MemoryCount == 3, "Archive display and neighbor prefetch populate shared image cache");
            var decodes = probe.imageCache.Decodes; var hits = probe.imageCache.MemoryHits;
            probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes == decodes && probe.imageCache.MemoryHits > hits, "Archive page turn reuses prefetched entry pixels");
            await probe.ReloadAsync(); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes > decodes, "Archive F5 invalidates displayed folder image cache");
            probe.SearchBox.Text = "2"; await probe.FilterAsync(); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.Entries.Count == 1 && ArchiveLocation.Parse(probe.model.SelectedPath!)?.EntryPath == "2.png", "Archive prefetch respects filtered current folder list");
            probe.SearchBox.Text = ""; await probe.FilterAsync(); await probe.activeDisplay; await probe.prefetchWork;
            await probe.SetSpreadModeAsync(true); await probe.prefetchWork;
            Check(probe.firstPage is not null && probe.secondPage is not null && probe.model.HasImage, "Archive spread uses cached pages without affecting composition");
            await probe.SetSpreadModeAsync(false); await probe.prefetchWork;
            var image = probe.model.Image;
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { Cache = new(0, 0, 2) }); await probe.prefetchWork;
            Check(probe.imageCache.MemoryBytes == 0 && ReferenceEquals(image, probe.model.Image), "Archive memory zero releases reuse pixels and retains current image");
            decodes = probe.imageCache.Decodes; probe.Move(1); await probe.activeDisplay;
            Check(probe.imageCache.Decodes == decodes + 1 && probe.imageCache.MemoryCount == 0, "Archive navigation remains functional with caches disabled");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { Cache = new(8, 0, 2) });
            var inner = new ArchiveLocation(Path.Combine(data, "outer.zip"), "inner.zip", false);
            await probe.OpenAsync(new ArchiveLocation(inner.Identity).Identity); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage && probe.model.TemporaryStore?.UsedBytes > 0, "Nested archive image prefetch retains its active input correctly");
            decodes = probe.imageCache.Decodes; probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes == decodes, "Nested archive page turn reuses prefetched pixels");
            await probe.ReloadAsync(); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.imageCache.Decodes > decodes && probe.model.HasImage, "Nested archive F5 bypasses old cached images");
            await probe.OpenAsync(book); await probe.activeDisplay; await probe.prefetchWork;
            for (var i = 0; probe.model.TemporaryStore?.UsedBytes > 0 && i < 50; i++) await Task.Delay(20);
            Check(probe.model.TemporaryStore?.UsedBytes == 0, "Reusable cached pixels do not retain nested archive temporary files");
            var brokenBook = Path.Combine(root, "archive-checks", "book.cbz");
            await probe.OpenAsync(new ArchiveLocation(brokenBook, "2.png", false).Identity); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage && !probe.model.Message.Contains("失敗"), "Broken speculative entry never replaces current image with error");
            probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            probe.Move(1); await probe.activeDisplay;
            Check(probe.model.Message.Contains("読み込み失敗") && probe.model.Entries.Count == 3, "Broken archive image stays visible in list when explicitly selected");
            probe.Move(1); await probe.activeDisplay; await probe.prefetchWork;
            Check(probe.model.HasImage, "Archive page navigation recovers from broken image with cache enabled");
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
