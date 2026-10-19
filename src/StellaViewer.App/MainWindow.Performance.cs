using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task MeasurePerformanceAsync(string folder, string resultPath)
    {
        WatchFoldersEnabled = false; ShowInTaskbar = false; ShowActivated = false; Left = Top = -32000; Show();
        await ApplyPreferencesAsync(CurrentPreferences with { Cache = new(0, 0, 0), ListMode = 0 }); UpdateLayout();
        var clock = Stopwatch.StartNew(); double? firstBatch = null; var pulses = new List<double>();
        var previous = clock.Elapsed.TotalMilliseconds;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => { var now = clock.Elapsed.TotalMilliseconds; pulses.Add(Math.Max(0, now - previous - 16)); previous = now; };
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) => {
            if (e.PropertyName == nameof(ViewerModel.ListingStatus) && model.Entries.Count > 0) firstBatch ??= clock.Elapsed.TotalMilliseconds;
        };
        model.PropertyChanged += changed; timer.Start();
        try {
            await OpenAsync(folder); await activeDisplay; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var opened = clock.Elapsed.TotalMilliseconds;
            var entryCount = model.Entries.Count;
            var indices = Enumerable.Range(0, 20).Select(i => (int)((long)i * (entryCount - 1) / 19)).ToArray();
            async Task<List<double>> Switch() {
                var times = new List<double>();
                foreach (var index in indices) {
                    var start = Stopwatch.StartNew(); SelectPageAt(index); await activeDisplay;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); times.Add(start.Elapsed.TotalMilliseconds);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                return times;
            }
            var cold = await Switch();
            await ApplyPreferencesAsync(CurrentPreferences with { Cache = new(512, 0, 0) });
            await Switch(); var warm = await Switch();
            ListModeBox.SelectedIndex = 2; UpdateLayout(); var scroll = new List<double>(); var realized = 0;
            for (var i = 0; i < 20; i++) {
                var start = Stopwatch.StartNew(); TileList.ScrollIntoView(model.TileRows[(int)((long)i * (model.TileRows.Count - 1) / 19)]); UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                scroll.Add(start.Elapsed.TotalMilliseconds); realized = Math.Max(realized, Descendants(TileList).OfType<ListBoxItem>().Count());
            }
            await Task.Delay(40); timer.Stop();
            static object Stats(List<double> values) {
                var times = values.Order().ToArray();
                return new { MedianMs = times[times.Length / 2], P95Ms = times[(int)Math.Ceiling(times.Length * .95) - 1] };
            }
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var report = new {
                Dataset = "10000 generated 8x6 PNG files; offscreen WPF window; software/OS caches uncontrolled; not acceptance thresholds.",
                EntryCount = entryCount, FirstBatchDataMs = firstBatch, OpenAndFirstImageMs = opened,
                UncachedSelection = Stats(cold), MemorySelection = Stats(warm), TileScrollLayout = Stats(scroll),
                MaxInputTimerDelayMs = pulses.DefaultIfEmpty().Max(), RealizedTileRows = realized, TotalTileRows = model.TileRows.Count,
                imageCache.MemoryBytes, imageCache.MemoryHits, imageCache.Decodes,
                PeakWorkingSetBytes = process.PeakWorkingSet64, PrivateBytes = process.PrivateMemorySize64
            };
            await System.IO.File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { timer.Stop(); model.PropertyChanged -= changed; }
    }
}
