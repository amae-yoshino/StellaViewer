using System.IO;
using System.IO.Compression;
using System.Windows;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyLocationGroupingAsync(string imagePath,string reportPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(reportPath)!,"location-groups-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root,"z-folder2"));Directory.CreateDirectory(Path.Combine(root,"z-folder10"));
        File.Copy(imagePath,Path.Combine(root,"page.png"));
        // These entries exercise listing only; never open their placeholder payloads.
        foreach(var name in new[]{"1.zip","2.pdf","10.cbz"})File.WriteAllText(Path.Combine(root,name),"listing fixture");
        var outer=Path.Combine(root,"outer.zip");
        using(var zip=new ZipArchive(File.Create(outer),ZipArchiveMode.Create)){
            zip.CreateEntryFromFile(imagePath,"page.png",CompressionLevel.NoCompression);
            zip.CreateEntryFromFile(imagePath,"z-folder2/page.png",CompressionLevel.NoCompression);
            zip.CreateEntryFromFile(imagePath,"z-folder10/page.png",CompressionLevel.NoCompression);
            using(var entry=zip.CreateEntry("1.zip").Open())entry.Write([1]);
            using(var entry=zip.CreateEntry("2.pdf").Open())entry.Write([1]);
        }
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        try{
            probe.Show();probe.LocationModeBox.SelectedIndex=0;
            foreach(var source in new[]{root,outer}){
                await probe.OpenAsync(source);await probe.activeDisplay;
                var selected=probe.model.Locations.First(entry=>entry.IsDirectory).Path;
                probe.RestoreLocationSelection([selected],selected);
                var image=probe.model.Image;var folder=probe.model.Folder;
                Check(probe.model.Locations.Any(entry=>entry.IsDirectory)&&probe.model.Locations.Any(entry=>!entry.IsDirectory),"Mixed fixture includes both groups");
                for(var view=0;view<3;view++){
                    probe.LocationModeBox.SelectedIndex=view;
                    for(var sort=0;sort<6;sort++){
                        probe.SelectedLocationSort=sort;await probe.SortLocationsAsync();probe.UpdateLayout();
                        var items=probe.model.Locations.ToArray();var folderCount=items.Count(entry=>entry.IsDirectory);
                        Check(items.Take(folderCount).All(entry=>entry.IsDirectory)&&items.Skip(folderCount).All(entry=>!entry.IsDirectory),$"Folder-first grouping in source {source}, view {view}, sort {sort}");
                        Check(probe.model.SelectedLocationPath==selected&&probe.model.Folder==folder&&ReferenceEquals(probe.model.Image,image),"Sorting preserves selection and displayed image without opening the group");
                        if(view==2)Check(probe.model.LocationTileRows.SelectMany(row=>row).SequenceEqual(items),"Tile rows follow grouped list order");
                    }
                }
            }
            await File.WriteAllTextAsync(reportPath,"PASS: folder-first ordering for physical and archive lists, name/detail/tile modes, all six sort modes, selection and image preservation");
        }
        finally{probe.Close();await probe.TemporaryCleanup;}
    }
}
