using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImageMagick;
using StellaViewer.Infrastructure;

internal static class PerformanceProbe
{
    public static async Task RunAsync(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var folder = Path.Combine(directory, "images-10000"); Directory.CreateDirectory(folder);
        byte[] small;
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 8, 6)) small = image.ToByteArray(MagickFormat.Png);
        for (var i = 0; i < 10000; i++) await File.WriteAllBytesAsync(Path.Combine(folder, $"image{i + 1}.png"), small);
        var pages = Path.Combine(directory, "pages"); Directory.CreateDirectory(pages);
        byte[] large;
        using (var image = new MagickImage("gradient:#142846-#e0ac70", 768, 1024)) large = image.ToByteArray(MagickFormat.Jpeg);
        var files = Enumerable.Range(0, 16).Select(i => Path.Combine(pages, $"page{i + 1}.jpg")).ToArray();
        foreach (var path in files) await File.WriteAllBytesAsync(path, large);
        var clock = Stopwatch.StartNew(); double? firstBatch = null; var entries = new List<FileEntry>();
        await foreach (var batch in FolderBrowser.EnumerateAsync(folder, default)) { firstBatch ??= clock.Elapsed.TotalMilliseconds; entries.AddRange(batch); }
        var enumeration = clock.Elapsed.TotalMilliseconds;
        clock.Restart(); entries.Sort(FolderBrowser.NaturalComparer); var sort = clock.Elapsed.TotalMilliseconds;
        static object Timing(List<double> times) {
            var values = times.Order().ToArray();
            return new { Count = values.Length, MedianMs = values[values.Length / 2], P95Ms = values[(int)Math.Ceiling(values.Length * .95) - 1] };
        }
        static async Task<List<double>> Read(DecodedImageCache cache, string[] paths) {
            var times = new List<double>();
            foreach (var path in paths) { var timer = Stopwatch.StartNew(); await cache.GetAsync(path, default); times.Add(timer.Elapsed.TotalMilliseconds); }
            return times;
        }
        var decoder = new MagickImageDecoder(); var disabled = new DecodedImageCache(decoder); disabled.Configure(new(0, 0, 0));
        var uncached = await Read(disabled, files);
        var memory = new DecodedImageCache(decoder); memory.Configure(new(512, 0, 0));
        await Read(memory, files); var memoryTimes = await Read(memory, files);
        var diskFolder = Path.Combine(directory, "Cache");
        var writer = new DecodedImageCache(decoder, diskFolder); writer.Configure(new(0, 2048, 0));
        var diskFill = await Read(writer, files);
        var disk = new DecodedImageCache(decoder, diskFolder); disk.Configure(new(0, 2048, 0)); var diskTimes = await Read(disk, files);
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var report = new {
            Framework = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
            LogicalProcessors = Environment.ProcessorCount, Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            FileSystem = new DriveInfo(Path.GetPathRoot(directory)!).DriveFormat,
            Dataset = "10000 generated 8x6 PNG files; 16 generated 768x1024 JPEG pages. OS cache is warm/uncontrolled; not real-world acceptance.",
            Folder = folder, EntryCount = entries.Count, FirstBatchMs = firstBatch, EnumerationMs = enumeration, NaturalSortMs = sort,
            Uncached = Timing(uncached), Memory = Timing(memoryTimes), DiskFillIncludingWrite = Timing(diskFill), Disk = Timing(diskTimes),
            MemoryCacheBytes = memory.MemoryBytes, memory.MemoryHits, disk.DiskHits,
            DiskCacheBytes = Directory.EnumerateFiles(diskFolder, "*.svcache").Sum(path => new FileInfo(path).Length),
            PeakWorkingSetBytes = process.PeakWorkingSet64, PrivateBytes = process.PrivateMemorySize64
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "infrastructure.json"), json); Console.WriteLine(json);
    }
}
