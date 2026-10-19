using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyThumbnailSizingAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        async Task WaitForPreviews(DependencyObject parent)
        {
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var previews=Descendants(parent).OfType<Thumbnail>().Where(t=>t.IsVisible).ToArray();
            for(var i=0;previews.Any(t=>t.Source is null)&&i<100;i++)await Task.Delay(40);
            Check(previews.Length>0&&previews.All(t=>t.Source is not null),"Visible image thumbnails finish loading");
        }
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"thumbnail-settings");var source=Path.Combine(root,"page01.png");
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        void Snapshot(FrameworkElement content,string suffix){content.UpdateLayout();var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);SaveRender(bitmap,screenshotPath+suffix);}
        try{
            probe.Show();await probe.OpenAsync(source);await probe.activeDisplay;probe.SetZoom(175);
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{ListMode=2,LocationListMode=2});probe.UpdateLayout();
            var selected=(probe.ImageList.SelectedItem as FileEntry)?.Path;var displayed=probe.model.Image;var zoomBefore=probe.zoom;var widths=new List<double>();var columns=new List<int>();
            var dialog=new SettingsWindow(probe.CurrentPreferences,probe.ApplyPreferencesAsync){Owner=probe,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};
            try{
                dialog.Show();var tabs=(TabControl)dialog.FindName("SettingsTabs");dialog.UpdateLayout();
                Check(tabs.TabStripPlacement==Dock.Left,"Settings tabs are arranged down the left side");
                var tabBounds=tabs.Items.OfType<TabItem>().Select(tab=>tab.TransformToAncestor(tabs).Transform(new Point(0,0))).ToArray();
                Check(tabBounds.Zip(tabBounds.Skip(1)).All(pair=>pair.First.Y<pair.Second.Y&&Math.Abs(pair.First.X-pair.Second.X)<1),"All settings tabs form one vertical column");
                var imageSize=(ComboBox)dialog.FindName("ImageThumbnailSize");var locationSize=(ComboBox)dialog.FindName("LocationThumbnailSize");
                for(var size=0;size<4;size++){
                    imageSize.SelectedIndex=size;locationSize.SelectedIndex=0;
                    Check(await dialog.ApplyChangesAsync()&&dialog.IsVisible,"Thumbnail size Apply keeps settings open");probe.UpdateLayout();
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    widths.Add(probe.model.ImageThumbnails.TileImageWidth);columns.Add(probe.tileColumns);
                    Check(probe.model.ImageThumbnailSize==size&&probe.model.LocationThumbnailSize==0,"Image and location thumbnail presets apply independently");
                    Check((probe.ImageList.SelectedItem as FileEntry)?.Path==selected&&ReferenceEquals(probe.model.Image,displayed)&&Math.Abs(probe.zoom-zoomBefore)<.01,"Thumbnail size change preserves selected image and zoom");
                    Check(probe.model.TileRows.SelectMany(e=>e).SequenceEqual(probe.model.Entries),"Resized virtual tile rows preserve image order without duplication");
                }
                Check(widths.Zip(widths.Skip(1)).All(pair=>pair.First<pair.Second)&&columns.First()>columns.Last(),"Larger thumbnails reduce tile column count");
                var visible=Descendants(probe.TileList).OfType<Thumbnail>().First(t=>t.IsVisible);
                for(var i=0;visible.Source is null&&i<80;i++)await Task.Delay(40);
                Check(visible.Width>96&&visible.Source is BitmapSource decodedThumbnail&&decodedThumbnail.PixelWidth>96,"Large thumbnails decode at increased pixel resolution");
                imageSize.SelectedIndex=2;locationSize.SelectedIndex=3;await dialog.ApplyChangesAsync();
                probe.ApplyTheme(true);probe.CopySettingsTheme(dialog);imageSize.BringIntoView();dialog.UpdateLayout();
                Snapshot((FrameworkElement)dialog.Content,".thumbnail-settings-dark.png");
                probe.ApplyTheme(false);probe.CopySettingsTheme(dialog);Snapshot((FrameworkElement)dialog.Content,".thumbnail-settings-light.png");
                imageSize.SelectedIndex=0;dialog.Close();
                Check(probe.model.ImageThumbnailSize==2&&probe.model.LocationThumbnailSize==3,"Cancel discards thumbnail edits after last Apply");
            }finally{dialog.Close();}
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{ListMode=1,LocationListMode=1});probe.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var detail=Descendants(probe.ImageList).OfType<Thumbnail>().First(t=>t.IsVisible);
            Check(Math.Abs(detail.Width-probe.model.ImageThumbnails.DetailWidth)<.01&&detail.Width>68,"Detail image preview uses configured size");
            var folderPreview=Descendants(probe.LocationArea).OfType<Thumbnail>().First(t=>t.IsVisible);
            Check(Math.Abs(folderPreview.Width-probe.model.LocationThumbnails.DetailWidth)<.01&&folderPreview.Width>56,"Folder detail preview uses its separate size");
            await WaitForPreviews(probe.ImageList);
            Snapshot((FrameworkElement)probe.Content,".thumbnail-details.png");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{ListMode=2,LocationListMode=2});probe.UpdateLayout();
            await WaitForPreviews(probe.TileList);
            Snapshot((FrameworkElement)probe.Content,".thumbnail-tiles.png");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{ListMode=0,LocationListMode=0});probe.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(!Descendants(probe.ListArea).OfType<Thumbnail>().Any()&&!Descendants(probe.LocationArea).OfType<Thumbnail>().Any(),"Names-only modes still create no thumbnail visuals");
            var reset=new SettingsWindow(probe.CurrentPreferences);reset.ResetValue("image-thumbnail");
            Check(reset.Values is{ImageThumbnailSize:1,LocationThumbnailSize:3},"Image thumbnail reset leaves folder preset unchanged");reset.Close();
            var storage=Path.Combine(root,"restore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(storage);
            await PreferencesStore.SaveChangeAsync(filePath:Path.Combine(storage,"viewer-preferences.json"),imageThumbnailSize:3,locationThumbnailSize:0);
            var restored=new MainWindow(true,storage){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
            try{restored.Show();Check(restored.CurrentPreferences is{ImageThumbnailSize:3,LocationThumbnailSize:0},"Next window restores independent thumbnail settings");}
            finally{restored.Close();await restored.closePersistence;await restored.TemporaryCleanup;}
        }finally{probe.Close();await probe.TemporaryCleanup;}
    }
}
