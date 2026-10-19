using System.IO.Compression;
using System.Text;
using ImageMagick;
using SharpCompress.Archives;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

internal static class RequestedFeaturesChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        var entries = new[] {
            new FileEntry("/10.png", "10.png", 30, new(2020, 1, 1)),
            new FileEntry("/2.png", "2.png", 10, new(2022, 1, 1)),
            new FileEntry("/1.png", "1.png", 20, new(2021, 1, 1))
        };
        string[][] expected = [["1.png","2.png","10.png"], ["10.png","1.png","2.png"], ["2.png","1.png","10.png"],
            ["10.png","2.png","1.png"], ["2.png","1.png","10.png"], ["10.png","1.png","2.png"]];
        for (var mode = 0; mode < 6; mode++) {
            Check(FolderBrowser.SortImages(entries, mode).Select(e => e.Name).SequenceEqual(expected[mode]), "Image sorting mode " + mode);
            var locations = entries.Select(e => new LocationEntry(e.Path, e.Name, false, e.Length, e.Modified));
            Check(FolderBrowser.SortLocations(locations, mode).Select(e => e.Name).SequenceEqual(expected[mode]), "Location sorting mode " + mode);
        }
        var unknown = new LocationEntry("/unknown.zip", "unknown.zip", false);
        Check(FolderBrowser.SortLocations([unknown, new("/file", "file", false, 1)], 5)[^1] == unknown, "Unknown location sizes stay last in descending order");
        var mixed=new[]{
            new LocationEntry("/folder10","folder10",true,null,new(2020,1,1)),
            new LocationEntry("/folder2","folder2",true,null,new(2022,1,1)),
            new LocationEntry("/folder1","folder1",true,null,null),
            new LocationEntry("/book10.zip","book10.zip",false,30,new(2020,1,1)),
            new LocationEntry("/book2.pdf","book2.pdf",false,10,new(2022,1,1),true),
            new LocationEntry("/book1.7z","book1.7z",false,20,new(2021,1,1)),
            new LocationEntry("/unknown.rar","unknown.rar",false,null,null)
        };
        string[][] folderOrders=[["folder1","folder2","folder10"],["folder10","folder2","folder1"],["folder1","folder2","folder10"],
            ["folder10","folder2","folder1"],["folder2","folder10","folder1"],["folder1","folder2","folder10"]];
        string[][] fileOrders=[["book1.7z","book2.pdf","book10.zip","unknown.rar"],["book10.zip","book1.7z","book2.pdf","unknown.rar"],
            ["book2.pdf","book1.7z","book10.zip","unknown.rar"],["unknown.rar","book10.zip","book2.pdf","book1.7z"],
            ["book2.pdf","book1.7z","book10.zip","unknown.rar"],["book10.zip","book1.7z","book2.pdf","unknown.rar"]];
        for(var mode=0;mode<6;mode++){
            var sorted=FolderBrowser.SortLocations(mixed,mode);
            Check(sorted.Take(3).All(entry=>entry.IsDirectory)&&sorted.Skip(3).All(entry=>!entry.IsDirectory),"Folders precede archives and PDF in mode "+mode);
            Check(sorted.Select(entry=>entry.Name).SequenceEqual(folderOrders[mode].Concat(fileOrders[mode])),"Each group follows its selected ordering, including unknown metadata: "+mode);
            Check(FolderBrowser.SortLocations(mixed.Reverse(),mode).SequenceEqual(sorted),"Grouping has deterministic input-independent ordering: "+mode);
        }
        Check(Zoom.Initial(250) == 100 && Zoom.Initial(250, true) == 250 && Zoom.Initial(25, true) == 25 && Zoom.Initial(2000, true) == 1000,
            "Optional enlargement preserves reduction and zoom maximum");
        var preferences = Path.Combine(output, "preferences.json");
        await PreferencesStore.SaveChangeAsync(filePath: preferences, sortMode: 5, locationSortMode: 4, enlargeSmallImages: true);
        var saved = PreferencesStore.Load(preferences);
        Check(saved.SortMode == 5 && saved.LocationSortMode == 4 && saved.EnlargeSmallImages, "Descending and small-image settings persist");
        var transfer = Path.Combine(output, "export.json"); await SettingsTransfer.ExportAsync(transfer, saved);
        var imported = await SettingsTransfer.ImportAsync(transfer);
        Check(imported.SortMode == 5 && imported.LocationSortMode == 4 && imported.EnlargeSmallImages, "New settings survive export/import");
        await ViewerStateStore.SaveAsync(new() { InfoPanelWidth = 640 }, new(), Path.Combine(output,"state"));
        Check(ViewerStateStore.Load(Path.Combine(output,"state")).Layout.InfoPanelWidth == 640
            && new WindowLayout { InfoPanelWidth = double.NaN }.Normalize().InfoPanelWidth == 320, "Information width persists and invalid width normalizes");

        var pixels = new byte[640 * 480 * 3]; new Random(173).NextBytes(pixels);
        using var image = new MagickImage(); image.ReadPixels(pixels, new PixelReadSettings(640,480,StorageType.Char,PixelMapping.RGB));
        var decoder = new MagickImageDecoder();
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP }) {
            var path = Path.Combine(output, "large." + (format == MagickFormat.Jpeg ? "jpg" : format.ToString().ToLowerInvariant()));
            image.Write(path, format);
            var normal = await decoder.DecodeAsync(path, default);
            foreach (var compression in new[] { CompressionLevel.NoCompression, CompressionLevel.Optimal }) {
                var zipPath = path + "." + compression + ".zip";
                using (var zip = new ZipArchive(File.Create(zipPath), ZipArchiveMode.Create)) zip.CreateEntryFromFile(path, Path.GetFileName(path), compression);
                using var session = await ArchiveSession.OpenAsync(zipPath, default);
                var decoded = await session.DecodeAsync(new(zipPath, Path.GetFileName(path), false), default);
                Check(normal.BgraPixels.SequenceEqual(decoded.BgraPixels), $"Full ZIP pixels match extracted {format}, {compression}");
            }
        }
        var png = File.ReadAllBytes(Path.Combine(output,"large.png"));
        foreach (var encoding in new[] { CodePagesEncodingProvider.Instance.GetEncoding(932)!, Encoding.UTF8 }) {
            var zipPath = Path.Combine(output, "names-" + encoding.CodePage + ".zip");
            const string name = "日本語/表紙①.png";
            using (var zip = new ZipArchive(File.Create(zipPath), ZipArchiveMode.Create, false, encoding)) {
                using var entry = zip.CreateEntry(name).Open(); entry.Write(png);
            }
            using var session = await ArchiveSession.OpenAsync(zipPath, default);
            Check(session.ContainsImage(name), "Archive name decode " + encoding.CodePage);
            var decoded = await session.DecodeAsync(new(zipPath,name,false), default);
            Check(decoded.Width == 640 && decoded.Height == 480, "Named archive image fully decodes " + encoding.CodePage);
        }
        foreach (var file in new[] { "Rar.solid.rar", "Rar5.solid.rar" }) {
            var path = Path.GetFullPath(Path.Combine("artifacts","archive-investigation",file));
            if (!File.Exists(path)) { Console.WriteLine("SKIP: optional official fixture " + file); continue; }
            using var session = await ArchiveSession.OpenAsync(path, default);
            var folders = new Queue<string>(); folders.Enqueue(""); var images = new List<FileEntry>();
            while (folders.TryDequeue(out var folder)) { images.AddRange(session.Images(folder)); foreach(var item in session.Children(folder).Where(e=>e.IsDirectory)) folders.Enqueue(item.Path); }
            Check(images.Count > 0, file + " contains test images");
            foreach (var entry in images) {
                var location = ArchiveLocation.Parse(entry.Path)!;
                using var copy = new MemoryStream(); await session.CopyEntryAsync(location, copy, 32 * 1024 * 1024, default); copy.Position = 0;
                var extracted = await decoder.DecodeStreamAsync(copy, default, 0);
                var decoded = await session.DecodeAsync(location, default);
                Check(extracted.BgraPixels.SequenceEqual(decoded.BgraPixels), file + " solid image decodes and copies consistently");
            }
        }
        return count;
    }
}
