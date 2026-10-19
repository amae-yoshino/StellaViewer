using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyLocationScrollAsync(string imagePath, string reportPath)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var root=Path.Combine(Path.GetDirectoryName(reportPath)!,"scroll-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        for(var i=0;i<80;i++)Directory.CreateDirectory(Path.Combine(root,$"folder{i:000}"));
        var child=Path.Combine(root,"folder060");
        for(var i=0;i<80;i++)Directory.CreateDirectory(Path.Combine(child,$"child{i:000}"));
        File.Copy(imagePath,Path.Combine(root,"page.png"));File.Copy(imagePath,Path.Combine(child,"page.png"));
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        async Task Settle(){await Dispatcher.InvokeAsync(()=>probe.UpdateLayout(),DispatcherPriority.ContextIdle);await Dispatcher.InvokeAsync(()=>probe.UpdateLayout(),DispatcherPriority.ContextIdle);}
        async Task<LocationScrollPosition> Scroll(double fraction){await Settle();var scroll=FindScrollViewer(probe.ActiveLocationList)!;scroll.ScrollToVerticalOffset(scroll.ScrollableHeight*fraction);await Settle();Check(scroll.VerticalOffset>20,"Fixture list scrolls before navigation");return probe.CaptureLocationScroll()!;}
        async Task Verify(LocationScrollPosition expected,string label){await Settle();var actual=probe.CaptureLocationScroll()!;Check(expected.AnchorPath is not null?actual.AnchorPath==expected.AnchorPath&&Math.Abs(actual.AnchorTop-expected.AnchorTop)<2:Math.Abs(actual.Offset-expected.Offset)<2,$"{label}: expected {expected}, actual {actual}");}
        try{
            probe.Show();probe.ApplyTreeVisibility(false);
            for(var mode=0;mode<3;mode++){
                probe.LocationModeBox.SelectedIndex=mode;
                await probe.OpenAsync(root);await probe.activeDisplay;probe.RestoreLocationSelection([child],child);var parentOffset=await Scroll(.65);
                await probe.OpenAsync(child);await probe.activeDisplay;var childSelection=Path.Combine(child,"child040");probe.RestoreLocationSelection([childSelection],childSelection);var childOffset=await Scroll(.4);
                await probe.GoBackAsync();await probe.activeDisplay;await Verify(parentOffset,$"Back restores parent location scroll, mode {mode}");Check(probe.model.SelectedLocationPath==child&&probe.model.Folder==root,"Back restores folder selection without opening it");
                await probe.GoForwardAsync();await probe.activeDisplay;await Verify(childOffset,$"Forward restores child location scroll, mode {mode}");Check(probe.model.SelectedLocationPath==childSelection&&probe.model.Folder==child,"Forward restores folder selection without opening it");
                await probe.UpAsync();await probe.activeDisplay;await Verify(parentOffset,$"Up restores parent location scroll, mode {mode}");
            }
            // A pinned browser can stay in one folder while image navigation changes.
            var pinnedOffset=await Scroll(.25);await probe.OpenAsync(child,updateLocations:false);await probe.activeDisplay;
            await Scroll(.75);await probe.GoBackAsync();await probe.activeDisplay;
            await Verify(pinnedOffset,"Back restores the history snapshot even when browser folder stayed the same");
            var zipPath=Path.Combine(root,"folders.zip");
            using(var zip=new ZipArchive(File.Create(zipPath),ZipArchiveMode.Create)){
                zip.CreateEntryFromFile(imagePath,"page.png",CompressionLevel.NoCompression);
                zip.CreateEntryFromFile(imagePath,"case/page.png",CompressionLevel.NoCompression);zip.CreateEntryFromFile(imagePath,"Case/page.png",CompressionLevel.NoCompression);
                for(var i=0;i<80;i++)zip.CreateEntryFromFile(imagePath,$"folder{i:000}/page.png",CompressionLevel.NoCompression);
            }
            await probe.OpenAsync(zipPath);await probe.activeDisplay;var selectedArchiveFolder=new ArchiveLocation(zipPath,"folder060").Identity;probe.RestoreLocationSelection([selectedArchiveFolder],selectedArchiveFolder);var archiveOffset=await Scroll(.55);
            var archiveChild=new ArchiveLocation(zipPath,"folder060").Identity;
            await probe.OpenAsync(archiveChild);await probe.activeDisplay;
            await probe.GoBackAsync();await probe.activeDisplay;await Verify(archiveOffset,"Back restores archive folder list scroll");Check(probe.model.SelectedLocationPath==selectedArchiveFolder,"Back restores archive folder selection");
            await probe.OpenAsync(archiveChild);await probe.activeDisplay;
            await probe.UpAsync();await probe.activeDisplay;await Verify(archiveOffset,"Up restores archive folder list scroll");
            var upper=new ArchiveLocation(zipPath,"Case").Identity;var lower=new ArchiveLocation(zipPath,"case").Identity;
            probe.RestoreLocationSelection([upper],upper);
            Check(probe.Locations.SelectedItems.Count==1,"Archive selection remains case-sensitive");
            await probe.OpenAsync(lower);await probe.GoBackAsync();await probe.activeDisplay;
            Check(probe.model.SelectedLocationPath==upper&&probe.Locations.SelectedItems.Count==1,"Archive history does not select a different-case folder");
            await File.WriteAllTextAsync(reportPath,"PASS: location scroll restored for name/detail/tile Back, Forward and Up, pinned browser history snapshots and archive folders");
        }
        finally{probe.Close();await probe.TemporaryCleanup;}
    }
}
