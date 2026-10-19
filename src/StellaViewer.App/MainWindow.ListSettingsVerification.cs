using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyListSettingsAsync()
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
        var probe = new MainWindow(false) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, WatchFoldersEnabled = false };
        async Task Settle() { probe.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); probe.UpdateLayout(); }
        static double Position(ListBox list)
        {
            var row = Descendants(list).OfType<ListBoxItem>().Select(item => (Item: item, Y: item.TransformToAncestor(list).Transform(new Point()).Y))
                .Where(item => item.Y + item.Item.ActualHeight > 0 && item.Y < list.ActualHeight).OrderBy(item => item.Y).First();
            return list.ItemContainerGenerator.IndexFromContainer(row.Item) * (row.Item.ActualHeight + row.Item.Margin.Top + row.Item.Margin.Bottom) - row.Y;
        }
        try {
            probe.Show();
            for (var i = 0; i < 300; i++) { probe.model.Entries.Add(new FileEntry($"missing-{i}.png", $"Image {i}", 0, DateTime.MinValue)); probe.model.Locations.Add(new LocationEntry($"missing-folder-{i}", $"Folder {i}", true)); }
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { UiSize = new(100, 20), ListFontSize = 24, ListScroll = new(200) });
            await Settle();
            Check(Math.Abs(probe.FontSize - 20) < .01 && Math.Abs(probe.ImageList.FontSize - 24) < .01 && Math.Abs(probe.Locations.FontSize - 24) < .01, "List font is independent of application font");
            foreach (var mode in new[] { 0, 1, 2 }) {
                probe.ListModeBox.SelectedIndex = mode; probe.LocationModeBox.SelectedIndex = mode; await Settle();
                foreach (var list in new[] { mode == 2 ? probe.TileList : probe.ImageList, mode == 2 ? probe.LocationTileList : probe.Locations }) {
                    Check(Math.Abs(list.FontSize - 24) < .01, "List font reaches every mode");
                    var name = Descendants(list).OfType<TextBlock>().First(text => text.Text.StartsWith("Image ") || text.Text.StartsWith("Folder "));
                    var expectedFont = mode == 2 ? 12 * 24d / 14 : 24;
                    Check(Math.Abs(name.FontSize - expectedFont) < .01, $"Filename font reaches rendered text: {list.Name}/{mode}, expected={expectedFont}, actual={name.FontSize}");
                    var scroll = FindScrollViewer(list)!;
                    var moves = new List<string>();
                    async Task<double> Move(int percent, int delta) {
                        probe.listScroll = new(percent); scroll.ScrollToVerticalOffset(500); await Settle();
                        var before = Position(list); var offset = scroll.VerticalOffset; var extent = scroll.ExtentHeight;
                        list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent }); await Settle();
                        moves.Add($"{percent}/{delta}: pos={before}->{Position(list)}, offset={offset}->{scroll.VerticalOffset}, extent={extent}->{scroll.ExtentHeight}, viewport={scroll.ViewportHeight}, heights={string.Join(',', Descendants(list).OfType<ListBoxItem>().Select(item => item.ActualHeight).Distinct())}");
                        return Position(list) - before;
                    }
                    if (SystemParameters.WheelScrollLines > 0) {
                        // Allow newly realized rows to settle after a mode/font change;
                        // verify this first movement too, before comparing steady distances.
                        Check(await Move(100, -120) > 0, "First wheel movement after mode/font change");
                        await Move(100, 120); moves.Clear();
                        var normal = await Move(100, -120); var doubled = await Move(200, -120); var halfNotch = await Move(200, -60);
                        Check(normal > 0 && Math.Abs(doubled - 2 * normal) < .1 && Math.Abs(halfNotch - normal) < .1, $"Wheel amount and partial delta: {list.Name}/{mode}: {normal}, {doubled}, {halfNotch}; {string.Join(';', moves)}");
                    }
                }
            }
            var dialog = new SettingsWindow(probe.CurrentPreferences, probe.ApplyPreferencesAsync);
            try {
                dialog.ListFontSize.Value = 18; dialog.ListScrollAmount.Value = 250;
                Check(await dialog.ApplyChangesAsync() && probe.CurrentPreferences.ListFontSize == 18 && probe.listScroll.AmountPercent == 250, "Settings Apply reaches viewer");
                await Settle();
                var caption = Descendants(probe.TileList).OfType<TextBlock>().First(text => text.Text.StartsWith("Image "));
                Check(Math.Abs(caption.FontSize - 12 * 18d / 14) < .01, "Apply changes rendered tile filename");
                dialog.ResetValue("list-font"); dialog.ResetValue("list-scroll");
                Check(dialog.Values.ListFontSize == 14 && dialog.Values.ListScroll.AmountPercent == 100 && dialog.Values.UiSize.FontSize == 20, "List reset is independent of application font");
            }
            finally { dialog.Close(); }
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { ListFontSize = 14 }); await Settle();
            Check(Math.Abs(probe.TileList.FontSize - 14) < .01 && Math.Abs(probe.FontSize - 20) < .01, "List font resets without compounding");
            Check(Math.Abs(Descendants(probe.TileList).OfType<TextBlock>().First(text => text.Text.StartsWith("Image ")).FontSize - 12) < .01, "Reset restores rendered tile filename");
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
