using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using SkiaSharp;
using StellaViewer.Infrastructure;

internal static class SecurityRecoveryChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
        async Task Reject(Func<Task> action, string label) {
            try { await action(); } catch (Exception error) when (error is NotSupportedException or InvalidDataException) { Check(true, label); return; }
            throw new Exception(label + " was accepted");
        }
        var root = Path.Combine(Path.GetFullPath(output), "security-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var decoder = new MagickImageDecoder(); var path = Path.Combine(root, "disguised.png");
        await File.WriteAllTextAsync(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"red\"/></svg>");
        await Reject(() => decoder.DecodeAsync(path, default), "Disguised SVG is rejected before native decode");
        await Reject(() => decoder.EstimatePixelBytesAsync(path, default), "Prefetch probe rejects disguised SVG");
        var png = await File.ReadAllBytesAsync(Path.Combine(output, "sample.png"));
        var huge = png.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(16), 8193); BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(20), 8192);
        var large = Path.Combine(root, "large.png"); await File.WriteAllBytesAsync(large, huge);
        await Reject(() => decoder.DecodeAsync(large, default), "Oversized PNG rejected from header before allocation");
        await Reject(() => decoder.DecodeAsync(large, default, 96), "Thumbnail cannot bypass source pixel limit");
        await Reject(() => decoder.EstimatePixelBytesAsync(large, default), "Prefetch cannot bypass source pixel limit");
        using (var stream = new MemoryStream(huge)) await Reject(() => decoder.DecodeStreamAsync(stream, default, 96), "Archive stream obeys pixel limit");
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(16), 8192); await File.WriteAllBytesAsync(large, huge);
        Check(await decoder.EstimatePixelBytesAsync(large, default) == 256L * 1024 * 1024, "Exactly 256 MiB passes header size boundary");
        var gif = Encoding.ASCII.GetBytes("GIF89a").Concat(new byte[] { 255, 255, 255, 255 }).ToArray();
        var bmp = new byte[26]; bmp[0] = 66; bmp[1] = 77; BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40); BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 8193); BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -8192);
        byte[] jpeg = [255, 216, 255, 192, 0, 8, 8, 255, 255, 255, 255, 3];
        var webp = new byte[30]; Encoding.ASCII.GetBytes("RIFF").CopyTo(webp, 0); Encoding.ASCII.GetBytes("WEBPVP8X").CopyTo(webp, 8); webp[16] = 10; webp[24] = webp[25] = webp[26] = webp[27] = webp[28] = webp[29] = 255;
        foreach (var fixture in new[] { ("GIF", gif), ("BMP", bmp), ("JPEG", jpeg), ("WebP", webp) }) {
            using var input = new MemoryStream(fixture.Item2);
            await Reject(() => decoder.DecodeStreamAsync(input, default, 0), fixture.Item1 + " oversized header rejected before native allocation");
        }
        var pdfPath = Path.Combine(root, "large.pdf");
        using (var doc = SKDocument.CreatePdf(pdfPath)) { doc.BeginPage(5000, 5000).Clear(SKColors.Red); doc.EndPage(); doc.Close(); }
        using (var pdf = await PdfSession.OpenAsync(pdfPath, default)) {
            await Reject(() => pdf.DecodeAsync(1, default), "Oversized PDF page rejected before rasterization");
            await Reject(() => pdf.DecodeAsync(1, default, 96), "PDF preview obeys source pixel limit");
        }
        var tarPath = Path.Combine(root, "disguised.zip");
        using (var writer = new TarWriter(File.Create(tarPath))) writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "page.png") { DataStream = new MemoryStream(png) });
        await Reject(() => LocationPreview.DecodeAsync(tarPath, default), "Archive preview rejects TAR disguised as ZIP");
        var unsafeBook = Path.Combine(root, "unsafe.zip");
        using (var zip = new ZipArchive(File.Create(unsafeBook), ZipArchiveMode.Create)) { using var entry = zip.CreateEntry("../page.png").Open(); entry.Write(png); }
        await Reject(() => LocationPreview.DecodeAsync(unsafeBook, default), "Archive preview uses catalog path validation");
        var normalBook = Path.Combine(root, "normal.zip");
        using (var zip = new ZipArchive(File.Create(normalBook), ZipArchiveMode.Create)) { using var entry = zip.CreateEntry("sub/page.png").Open(); entry.Write(png); }
        Check((await LocationPreview.DecodeAsync(normalBook, default)).Width == 8, "Validated archive preview retains child-folder representative");

        var prefs = Path.Combine(root, "viewer-preferences.json");
        await PreferencesStore.SaveChangeAsync(dark: true, filePath: prefs);
        Check(File.Exists(prefs + ".bak"), "First settings save creates usable backup");
        await PreferencesStore.SaveChangeAsync(controlWheel: false, filePath: prefs);
        File.WriteAllText(prefs, "broken json");
        Check(PreferencesStore.Load(prefs).Dark && PreferencesStore.Load(prefs).ControlWheel, "Broken settings restore previous good snapshot");
        Check(Directory.GetFiles(root, "viewer-preferences.json.corrupt-*").Length == 1, "Broken settings preserved for inspection");
        Check(RecoverableSettings.TakeNotice(root)?.Contains("バックアップから復旧") == true && RecoverableSettings.TakeNotice(root) is null, "Recovery notice delivered once");
        await PreferencesStore.SaveChangeAsync(controlWheel: false, filePath: prefs);
        Check(!PreferencesStore.Load(prefs).ControlWheel, "Settings remain editable after recovery");
        File.WriteAllText(prefs, "null"); File.WriteAllText(prefs + ".bak", "broken backup");
        var defaults = PreferencesStore.Load(prefs);
        Check(!defaults.Dark && !defaults.DeleteEnabled && !defaults.AllowNetwork, "Invalid primary and backup restore safe defaults");
        Check(Directory.GetFiles(root, "viewer-preferences.json.bak.corrupt-*").Length == 1, "Broken backup also preserved");
        await PreferencesStore.SaveChangeAsync(dark: true, filePath: prefs);
        // A lock or access error must not be mistaken for corruption and overwrite user settings.
        using (var locked = File.Open(prefs, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            var failed = false; try { PreferencesStore.Load(prefs); } catch (IOException) { failed = true; }
            Check(failed, "Locked settings report I/O failure without recovery");
        }
        Check(PreferencesStore.Load(prefs).Dark, "Locked settings retain saved contents");
        using (var big = File.Open(prefs, FileMode.Create)) big.SetLength(4 * 1024 * 1024 + 1);
        Check(!PreferencesStore.Load(prefs).Dark, "Oversized settings recover from previous backup without unbounded parsing");
        await ViewerStateStore.SaveAsync(new(Width: 1333), new(root), root);
        File.WriteAllText(Path.Combine(root, "window-layout.json"), "{");
        File.WriteAllText(Path.Combine(root, "last-view.json"), "{\"Folder\":null}");
        var state = ViewerStateStore.Load(root);
        Check(state.Layout.Width == 1333 && state.View.Folder == root, "Window and browsing state recover independently");
        Check((await decoder.DecodeAsync(Path.Combine(output, "sample.png"), default)).Width == 8, "Decoder usable after all rejected inputs");
        return checks;
    }
}
