using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using StellaViewer.Infrastructure;

internal static class PdfChecks
{
    public static async Task<int> RunAsync(string output)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
        async Task Reject<T>(Func<Task> action, string label) where T : Exception {
            try { await action(); } catch (T) { Check(true, label); return; }
            throw new Exception(label + " was accepted");
        }
        var root = Path.Combine(output, "pdf-checks"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "pages.pdf");
        using (var document = SKDocument.CreatePdf(path)) {
            var colors = new[] { SKColors.Red, SKColors.Lime, SKColors.Blue };
            for (var i = 0; i < colors.Length; i++) {
                var canvas = document.BeginPage(144, 216);
                canvas.Clear(colors[i]);
                using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
                canvas.DrawRect(30, 60, 84, 96, paint);
                paint.Color = SKColors.Black;
                using var font = new SKFont(SKTypeface.Default, 16);
                canvas.DrawText($"Page {i + 1}", 40, 110, SKTextAlign.Left, font, paint);
                document.EndPage();
            }
            document.Close();
        }
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var session = await PdfSession.OpenAsync(path, default);
        Check(session.PageCount == 3 && session.Pages().Length == 3, "PDF catalog contains one item per document page");
        var location = new PdfLocation(path, 2);
        Check(PdfLocation.Parse(location.Identity) == location && PdfLocation.Parse(path) is null
            && BrowsePath.PhysicalPath(location.Identity) == path && BrowsePath.DisplayPath(location.Identity).Contains("ページ 2"),
            "PDF internal page identity preserves document and page without exposing raw identity");
        Check(session.Pages().Select(e => PdfLocation.Parse(e.Path)!.Page).SequenceEqual(new int?[] { 1, 2, 3 })
            && session.Pages().All(e => e.IsPdfPage && e.Length == new FileInfo(path).Length), "PDF page order and source file metadata are explicit");
        var first = await session.DecodeAsync(1, default);
        Check(first.Width == 288 && first.Height == 432 && first.OriginalWidth == 288 && first.BgraPixels.Length == 288 * 432 * 4,
            "PDF raster dimensions use approved 144 dpi");
        Check(first.BgraPixels[2] > 240 && first.BgraPixels[0] < 10 && first.BgraPixels[3] == 255,
            "PDF raster is opaque straight BGRA with correct red channel");
        var second = await session.DecodeAsync(2, default);
        Check(second.BgraPixels[1] > 240 && second.BgraPixels[2] < 10, "PDF page navigation renders requested page rather than first page");
        var thumbnail = await session.DecodeAsync(3, default, 96);
        Check(thumbnail.Width == 64 && thumbnail.Height == 96 && thumbnail.OriginalWidth == 288 && thumbnail.BgraPixels[0] > 240,
            "PDF page thumbnails are bounded while retaining full raster dimensions");
        var preview = await LocationPreview.DecodeAsync(path, default);
        Check(preview.Width <= 96 && preview.Height <= 96 && preview.BgraPixels[2] > 240, "physical PDF location preview uses first document page");
        var parallel = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => session.DecodeAsync((i - 1) % 3 + 1, default, 96)));
        Check(parallel.All(p => p.Height == 96), "PDF process-wide serialization supports concurrent thumbnail and page requests");
        var after = SHA256.HashData(await File.ReadAllBytesAsync(path));
        Check(before.SequenceEqual(after), "PDF source stays unchanged during catalog, rasterization and thumbnails");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Reject<OperationCanceledException>(() => PdfSession.OpenAsync(path, canceled.Token), "PDF opening honors pre-cancellation");
        await Reject<OperationCanceledException>(() => session.DecodeAsync(1, canceled.Token), "PDF rasterization honors pre-cancellation");
        await Reject<ArgumentOutOfRangeException>(() => session.DecodeAsync(0, default), "PDF zero page is rejected");
        await Reject<ArgumentOutOfRangeException>(() => session.DecodeAsync(4, default), "PDF page beyond document end is rejected");
        await Reject<NotSupportedException>(() => PdfSession.OpenAsync(@"\\stella-test.invalid\share\book.pdf", default), "PDF network policy blocks UNC before opening");
        var damaged = Path.Combine(root, "broken.pdf"); await File.WriteAllTextAsync(damaged, "not a PDF");
        await Reject<InvalidDataException>(() => PdfSession.OpenAsync(damaged, default), "damaged PDF reports readable failure");
        var protectedPath = Path.Combine(root, "protected.pdf"); CreateProtectedPdf(protectedPath);
        if (OperatingSystem.IsWindows()) {
            using var stream = File.OpenRead(protectedPath);
            Check(PDFtoImage.Conversion.GetPageCount(stream, password: "test") == 1, "password fixture opens with its correct password");
        }
        await Reject<NotSupportedException>(() => PdfSession.OpenAsync(protectedPath, default), "password protected PDF is rejected without password prompt");
        Check((await session.DecodeAsync(1, default)).Width == 288, "PDF renderer recovers after password and malformed-file failures");
        var startup = ViewerStateStore.ResolveStartup(new(new PdfLocation(path).Identity, location.Identity), null, true);
        Check(startup.Path == location.Identity, "startup resolves last PDF document page");
        var explicitChoice = ViewerStateStore.ResolveStartup(new(new PdfLocation(path).Identity, location.Identity), Path.Combine(output, "sample.png"), true);
        Check(explicitChoice.Path == Path.Combine(output, "sample.png"), "explicit startup image takes precedence over saved PDF page");
        var mutablePath = Path.Combine(root, "mutable.pdf"); File.Copy(path, mutablePath, true);
        var mutable = await PdfSession.OpenAsync(mutablePath, default);
        await File.AppendAllTextAsync(mutablePath, "\n% changed\n");
        await Reject<IOException>(() => mutable.DecodeAsync(1, default), "changed PDF snapshot requests reload before rendering");
        var missing = new PdfLocation(Path.Combine(root, "missing.pdf"), 2);
        var fallback = ViewerStateStore.ResolveStartup(new(new PdfLocation(missing.DocumentPath).Identity, missing.Identity), null, true);
        Check(fallback.Path == root && fallback.Notification is not null, "missing saved PDF falls back to physical parent with notice");
        await Reject<InvalidDataException>(() => Task.Run(() => PdfLocation.Parse("stella-pdf:invalid")), "corrupt saved PDF identity is rejected");
        return checks;
    }

    // Minimal Standard Security Handler revision 2 fixture. No viewer encryption support is added.
    private static void CreateProtectedPdf(string path)
    {
        byte[] padding = [0x28,0xBF,0x4E,0x5E,0x4E,0x75,0x8A,0x41,0x64,0x00,0x4E,0x56,0xFF,0xFA,0x01,0x08,
            0x2E,0x2E,0x00,0xB6,0xD0,0x68,0x3E,0x80,0x2F,0x0C,0xA9,0xFE,0x64,0x53,0x69,0x7A];
        byte[] Pad(string password) { var bytes = Encoding.ASCII.GetBytes(password).Take(32).ToArray(); return [..bytes, ..padding.Take(32 - bytes.Length)]; }
        byte[] Rc4(byte[] key, byte[] value) {
            var state = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(); var j = 0;
            for (var i = 0; i < 256; i++) { j = (j + state[i] + key[i % key.Length]) & 255; (state[i], state[j]) = (state[j], state[i]); }
            var x = 0; j = 0; var result = new byte[value.Length];
            for (var n = 0; n < value.Length; n++) { x = (x + 1) & 255; j = (j + state[x]) & 255; (state[x], state[j]) = (state[j], state[x]); result[n] = (byte)(value[n] ^ state[(state[x] + state[j]) & 255]); }
            return result;
        }
        var id = MD5.HashData(Encoding.ASCII.GetBytes("StellaViewer protected synthetic fixture"));
        var owner = Rc4(MD5.HashData(Pad("owner"))[..5], Pad("test"));
        var key = MD5.HashData([..Pad("test"), ..owner, 0xFC, 0xFF, 0xFF, 0xFF, ..id])[..5];
        var user = Rc4(key, padding);
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 144 216] >>",
            $"<< /Filter /Standard /V 1 /R 2 /Length 40 /P -4 /O <{Convert.ToHexString(owner)}> /U <{Convert.ToHexString(user)}> >>"];
        var text = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = text.Length; text.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append($"{offset:0000000000} 00000 n \n");
        text.Append($"trailer\n<< /Size 5 /Root 1 0 R /Encrypt 4 0 R /ID [<{Convert.ToHexString(id)}> <{Convert.ToHexString(id)}>] >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, text.ToString(), Encoding.ASCII);
    }
}
