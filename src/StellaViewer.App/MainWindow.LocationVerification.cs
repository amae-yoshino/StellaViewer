using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyLocationModesAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        await OpenAsync(imagePath); await activeDisplay;
        SetZoom(450);
        var image = model.Image; var selection = ImageList.SelectedItem; var initialZoom = zoom;
        var imageMode = ListModeBox.SelectedIndex;
        var archive = model.Locations.First(e => !e.IsDirectory);
        sortingLocations = true;
        try { Locations.SelectedItem = archive; model.SelectedLocationPath = archive.Path; }
        finally { sortingLocations = false; }
        var expectedLocations = model.Locations.ToArray();
        for (var sort = 0; sort < 3; sort++) {
            SelectedLocationSort = sort; await SortLocationsAsync();
            Check(model.Locations.SequenceEqual(FolderBrowser.SortLocations(expectedLocations, sort)), "Location sorting order");
            Check(ReferenceEquals(Locations.SelectedItem, archive) && model.SelectedLocationPath == archive.Path,
                "Sorting preserves selected location without navigating");
            Check(ReferenceEquals(model.Image, image) && ReferenceEquals(ImageList.SelectedItem, selection) && zoom == initialZoom
                && ListModeBox.SelectedIndex == imageMode, "Sorting locations preserves image, zoom and image list mode");
        }
        SelectedLocationSort = 0; await SortLocationsAsync();
        expectedLocations = model.Locations.ToArray();
        for (var mode = 0; mode < 3; mode++) {
            LocationModeBox.SelectedIndex = mode; UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(ReferenceEquals(model.Image, image) && ReferenceEquals(ImageList.SelectedItem, selection) && zoom == initialZoom
                && ListModeBox.SelectedIndex == imageMode, "Location display mode preserves image and independent image list mode");
            Check(ReferenceEquals(Locations.SelectedItem, archive) && model.SelectedLocationPath == archive.Path,
                "Location selection survives display mode change");
            Check(model.Locations.SequenceEqual(expectedLocations), "Display mode keeps location order");
            if (mode == 0)
                Check(!Descendants(LocationArea).OfType<Thumbnail>().Any(), "Names-only location mode creates no thumbnail visuals");
            if (mode == 2)
                Check(model.LocationTileRows.SelectMany(row => row).SequenceEqual(expectedLocations), "Location tile rows preserve order without duplicates");
            if (mode > 0) {
                var preview = Descendants(LocationArea).OfType<Thumbnail>().FirstOrDefault(t => t.FilePath == archive.Path && t.IsVisible);
                for (var retry = 0; preview is not null && preview.Source is null && retry < 40; retry++) await Task.Delay(50);
                Check(preview?.Source is not null, "Visible archive preview loads in detail and tile modes");
            }
            var content = (FrameworkElement)Content;
            var rendered = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(content); SaveRender(rendered, screenshotPath + $".locations{mode}.png");
        }
        var resetSettings = new SettingsWindow(new ViewerPreferences(ListMode: 2, LocationListMode: 0));
        resetSettings.ResetValue("locations");
        Check(resetSettings.Values.LocationListMode == 1 && resetSettings.Values.ListMode == 2, "Reset location mode keeps image mode");
        resetSettings.Close();
        var folder = model.Locations.First(e => e.IsDirectory);
        var click = new Button { DataContext = folder };
        LocationTileClicked(click, new RoutedEventArgs());
        await navigation; await activeDisplay;
        Check(model.Folder == folder.Path, $"Location tile opens its folder: actual={model.Folder}, target={folder.Path}, modifiers={System.Windows.Input.Keyboard.Modifiers}, selected={Locations.SelectedItems.Count}, sorting={sortingLocations}, status={model.Status}");
        await OpenAsync(imagePath); await activeDisplay;
        LocationModeBox.SelectedIndex = 1;
    }
    private async Task VerifyLocationVirtualizationAsync(string imagePath)
    {
        var folder = Path.Combine(Path.GetDirectoryName(imagePath)!, "locations-many");
        if (!Directory.Exists(folder)) return;
        await OpenAsync(folder); await activeDisplay;
        LocationModeBox.SelectedIndex = 2; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var realized = Descendants(LocationTileList).OfType<ListBoxItem>().Count();
        if (model.Locations.Count != 300 || model.LocationTileRows.Count < 50 || realized == 0 || realized >= 25)
            throw new InvalidOperationException($"Location virtualization failed: entries={model.Locations.Count}, rows={model.LocationTileRows.Count}, realized={realized}");
        LocationModeBox.SelectedIndex = 1;
    }
}
