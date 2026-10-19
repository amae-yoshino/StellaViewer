using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyLocationNavigationAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"location-navigation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var first=Path.Combine(root,"first");var second=Path.Combine(root,"second");Directory.CreateDirectory(first);Directory.CreateDirectory(second);
        File.Copy(imagePath,Path.Combine(first,"20.png"));File.Copy(imagePath,Path.Combine(first,"1.png"));File.Copy(imagePath,Path.Combine(second,"2.png"));
        var probe=new MainWindow(false,Path.Combine(root,"state")){WatchFoldersEnabled=true,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        try{
            probe.Show();await probe.OpenAsync(root);await probe.activeDisplay;probe.UpdateLayout();
            var entries=probe.model.Locations.ToArray();var target=entries.Single(e=>e.Path==first);
            probe.Locations.SelectedItem=target;await probe.navigation;await probe.activeDisplay;
            Check(probe.model.Folder==first&&probe.model.HasImage&&probe.model.LocationFolder==root&&probe.model.Locations.SequenceEqual(entries),"Single click changes image source without moving browser list");
            Check((probe.ImageList.SelectedItem as FileEntry)?.Name=="1.png","Default folder opening selects sorted first image rather than first enumeration result");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{FolderOpeningMode=1});
            probe.ImageList.SelectedItem=probe.model.Entries.Single(e=>e.Name=="20.png");await probe.activeDisplay;
            await probe.OpenAsync(second,updateLocations:false);await probe.OpenAsync(first,updateLocations:false);await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Name=="20.png","Last-viewed mode restores each folder selection");
            await probe.folderPositionSave;
            Check(await new FolderImagePositionStore(Path.Combine(root,"state","folder-image-positions.json")).LoadAsync(first)==Path.Combine(first,"20.png"),"Folder selection is persisted independently of last startup location");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{FolderOpeningMode=0});
            await probe.OpenAsync(first,updateLocations:false);await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Name=="1.png","First-image mode overrides saved folder position");
            Check(probe.FolderAddress.CurrentPath==root&&probe.model.CurrentAddress!=root,"Browser breadcrumb stays independent of displayed image address");
            var added=Path.Combine(first,"new-folder");Directory.CreateDirectory(added);
            for(var i=0;i<40&&probe.watchApplyCount==0;i++)await Task.Delay(50);await probe.watchWork;
            Check(probe.model.Locations.SequenceEqual(entries),"Image folder watcher does not insert children into pinned parent list");
            probe.LocationModeBox.SelectedIndex=2;
            probe.LocationTileClicked(new Button{DataContext=entries.Single(e=>e.Path==second)},new());await probe.navigation;await probe.activeDisplay;
            Check(probe.model.Folder==second&&probe.model.LocationFolder==root,"Tile single click preserves browser location");
            // Exercise the same navigation operation used by the double-click handler.
            await probe.OpenAsync(target.Path);await probe.activeDisplay;
            Check(probe.model.LocationFolder==first&&probe.model.Locations.Any(e=>e.Path==added),"Double-click navigation opens selected folder in browser as well");
            await probe.GoBackAsync();await probe.activeDisplay;
            Check(probe.model.Folder==second&&probe.model.LocationFolder==root,"Back restores independent browser and image locations");
            await probe.ReloadAsync();await probe.activeDisplay;
            Check(probe.model.LocationFolder==root,"Reload preserves pinned browser location");
            probe.UpdateLayout();var width=probe.FolderColumns.ColumnDefinitions[0].ActualWidth;var ratio=probe.CaptureSavedState().Layout.TreeFraction;
            probe.ApplyTreeVisibility(false);probe.UpdateLayout();
            Check(probe.Folders.Visibility==Visibility.Collapsed&&probe.FolderColumnSplitter.Visibility==Visibility.Collapsed&&probe.FolderColumns.ColumnDefinitions[0].ActualWidth==0&&!probe.CaptureSavedState().Layout.TreeVisible,"Hidden tree releases its width and saves visibility");
            Check(Math.Abs(probe.CaptureSavedState().Layout.TreeFraction-ratio)<.01,"Hidden tree retains splitter fraction");
            probe.ApplyTreeVisibility(true);probe.UpdateLayout();
            Check(Math.Abs(probe.FolderColumns.ColumnDefinitions[0].ActualWidth-width)<2&&probe.Folders.IsVisible,"Tree toggle restores previous width");
            // Existing archive fixture contains an internal folder and sibling entries.
            var fixture=Path.Combine(Path.GetDirectoryName(imagePath)!,"archive-checks","book.cbz");
            await probe.OpenAsync(fixture);await probe.activeDisplay;
            var folder=probe.model.Locations.First(e=>e.IsDirectory);var source=probe.model.LocationArchiveSession;
            var rows=probe.model.Locations.ToArray();probe.Locations.SelectedItem=folder;await probe.navigation;await probe.activeDisplay;
            Check(probe.model.LocationFolder==new ArchiveLocation(fixture).Identity&&probe.model.Locations.SequenceEqual(rows)&&ReferenceEquals(source,probe.model.LocationArchiveSession),"Archive folder preview keeps parent entries and thumbnail provider");
            await probe.OpenAsync(imagePath,updateLocations:false);await probe.activeDisplay;
            Check(probe.model.LocationArchiveSession==source&&source!.Locations("").Length>0,"Browser archive remains usable after image source changes");
            probe.Folders.Items.Clear();var node=FolderNode(root);probe.Folders.Items.Add(node);node.IsSelected=true;await probe.navigation;
            await probe.OpenAsync(second);await probe.activeDisplay;
            // WPF re-raises this direct preview event at each ancestor for real mouse input.
            probe.TreePointerDown(probe.Folders,new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=node});
            await probe.navigation;await probe.activeDisplay;
            Check(probe.model.Folder==root&&probe.model.LocationFolder==root,"Clicking selected tree node reopens it after external navigation");
        }finally{probe.Close();await probe.TemporaryCleanup;}
        Check(probe.model.LocationArchiveSession is null,"Closing releases browser archive reference");
    }
}
