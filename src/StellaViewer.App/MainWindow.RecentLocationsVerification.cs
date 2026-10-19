using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyRecentLocationsAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"recent-locations-ui",Guid.NewGuid().ToString("N"));var gallery=Path.Combine(root,"gallery");Directory.CreateDirectory(gallery);
        var image=Path.Combine(gallery,"DO-NOT-STORE-IMAGE.png");File.Copy(imagePath,image);File.Copy(imagePath,Path.Combine(gallery,"second.png"));
        var storage=Path.Combine(root,"state");var probe=new MainWindow(false,storage){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        var external=new RecentLocationStore(Path.Combine(storage,"recent-locations.json"));
        void Snapshot(string suffix){probe.UpdateLayout();var content=(FrameworkElement)probe.Content;var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);SaveRender(bitmap,screenshotPath+suffix);}
        try{
            probe.Show();await probe.OpenAsync(image);await probe.activeDisplay;
            var initial=await external.LoadAsync();Check(initial.Length==1&&initial[0].Location==gallery,"Opening an image records only its parent folder");
            Check(!File.ReadAllText(Path.Combine(storage,"recent-locations.json")).Contains("DO-NOT-STORE-IMAGE"),"Persistent recent history contains no selected image filename");
            probe.Move(1);await probe.activeDisplay;
            Check((await external.LoadAsync()).Single().OpenedAt==initial[0].OpenedAt,"Image page turn does not add or reorder recent places");
            var pdf=Path.Combine(Path.GetDirectoryName(imagePath)!,"pdf-checks","pages.pdf");
            await probe.OpenAsync(pdf);await probe.activeDisplay;var documentVisit=(await external.LoadAsync())[0];
            Check(PdfLocation.Parse(documentVisit.Location) is {Page:null}&&documentVisit.Kind=="PDF","Opened PDF is stored as document without individual page");
            probe.Move(1);await probe.activeDisplay;
            Check((await external.LoadAsync())[0].OpenedAt==documentVisit.OpenedAt,"PDF page turns do not generate history rows");
            var zip=Path.Combine(Path.GetDirectoryName(imagePath)!,"archive-checks","book.cbz");
            await probe.OpenAsync(zip);await probe.activeDisplay;
            Check((await external.LoadAsync())[0].Location==new ArchiveLocation(zip).Identity,"Opened archive is recorded as archive root");
            var internalFolder=new ArchiveLocation(zip,"日本語");await probe.OpenAsync(internalFolder.Identity);await probe.activeDisplay;
            Check((await external.LoadAsync())[0].Location==internalFolder.Identity,"Archive subfolder is recorded without individual image path");
            var failed=Path.Combine(root,"bad.pdf");File.WriteAllText(failed,"not PDF");var beforeFailure=await external.LoadAsync();
            await probe.OpenAsync(failed);await probe.activeDisplay;
            Check((await external.LoadAsync()).SequenceEqual(beforeFailure),"Failed document open does not enter recent history");
            var pixels=probe.model.Image;await probe.ShowRecentLocationsAsync();
            Check(ReferenceEquals(pixels,probe.model.Image)&&probe.RecentLocationsPanel.IsVisible&&!probe.FolderBrowserPanel.IsVisible,"Opening recent panel preserves displayed image");
            Check(probe.DirectoryShortcutsButton.Content is IconLabel{Kind:ViewerIconKind.Folder}&&probe.RecentLocationsButton.Content is IconLabel{Kind:ViewerIconKind.History},"Folder links and history buttons show their own icons");
            probe.RecentLocationList.SelectedItem=probe.model.RecentLocations.Single(e=>e.Location==gallery);await probe.navigation;await probe.activeDisplay;
            Check(probe.model.Folder==gallery&&probe.model.LocationFolder==gallery&&(await external.LoadAsync())[0].Location==gallery,"Recent item reopens place and moves it to front");
            var network=@"\\stella-history.invalid\share";await external.RecordAsync(network);await Task.Delay(2400);await probe.recentRefresh;
            Check(probe.model.RecentLocations.First().Location==network,"Visible recent history reflects other-window visits without target I/O");
            pixels=probe.model.Image;probe.RecentLocationList.SelectedItem=probe.model.RecentLocations.First();await probe.navigation;
            Check(ReferenceEquals(pixels,probe.model.Image)&&probe.model.Status.Contains("ネットワーク"),"Disabled network history click preserves current image");
            var missing=Path.Combine(root,"missing");await external.RecordAsync(missing);await probe.RefreshChangedRecentLocationsAsync();
            probe.RecentLocationList.SelectedItem=probe.model.RecentLocations.First();await probe.navigation;
            Check(ReferenceEquals(pixels,probe.model.Image)&&probe.model.Status.Contains("見つかりません"),"Unavailable recent folder preserves loaded image");
            probe.ApplyTheme(true);Snapshot(".recent-dark.png");probe.ApplyTheme(false);Snapshot(".recent-light.png");
            await probe.ShowDirectoryShortcutsAsync();Check(!probe.RecentLocationsPanel.IsVisible&&!probe.recentTimer!.IsEnabled,"Switching from history to folder links stops recent polling");
            await probe.ShowRecentLocationsAsync();Check(!probe.DirectoryShortcutsPanel.IsVisible&&!probe.directoryTimer!.IsEnabled,"History and folder links remain independent panes");
            var restored=new MainWindow(false,storage){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
            try{restored.Show();await restored.ShowRecentLocationsAsync();Check(restored.model.RecentLocations.Any(e=>e.Location==gallery),"New window loads persisted folder history");}
            finally{restored.Close();await restored.TemporaryCleanup;}
        }finally{probe.Close();await probe.TemporaryCleanup;}
        Check(!probe.recentTimer!.IsEnabled&&probe.recentLifetime.IsCancellationRequested,"Closing stops history list refresh");
    }
}
