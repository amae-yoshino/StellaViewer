using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyRegistrationOrderAsync(string imagePath,string reportPath)
    {
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(reportPath)!,"registration-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var probe=new MainWindow(false,root){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        var paths=new[]{"item2","item10","item30"}.Select(name=>Path.Combine(root,name)).ToArray();foreach(var path in paths)Directory.CreateDirectory(path);
        void Snapshot(Window dialog,string name){dialog.UpdateLayout();var content=(FrameworkElement)dialog.Content;var dpi=VisualTreeHelper.GetDpi(content);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){var bounds=new Rect(0,0,content.ActualWidth,content.ActualHeight);drawing.DrawRectangle(dialog.Background,null,bounds);drawing.DrawRectangle(new VisualBrush(content),null,bounds);}bitmap.Render(visual);SaveRender(bitmap,reportPath+name+".png");}
        try{
            probe.Show();await probe.OpenAsync(imagePath);await probe.activeDisplay;
            var original=probe.decoded;
            probe.decoded=new(2000,1200,2000,1200,[]);probe.automaticZoom=false;probe.SetZoom(300);probe.Pan.X=40;probe.Pan.Y=30;
            probe.FitViewportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));probe.UpdateLayout();
            Check(Math.Abs(probe.zoom-Math.Min(1000,probe.Fit()))<.001&&probe.Pan.X==0&&probe.Pan.Y==0,"Fit button contains a large image and resets pan");
            probe.ActualSizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(probe.zoom==100&&!probe.automaticZoom,"100 percent button selects physical pixel sizing");
            probe.Width+=80;probe.UpdateLayout();Check(probe.zoom==100,"100 percent stays fixed on viewport resize");
            probe.decoded=new(300,200,300,200,[]);probe.enlargeSmallImages=false;
            probe.FitViewportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(probe.zoom>100&&Math.Abs(probe.zoom-Math.Min(1000,probe.Fit()))<.001,"Explicit fit enlarges small images independently of initial-fit setting");
            probe.Width+=80;probe.UpdateLayout();Check(Math.Abs(probe.zoom-Math.Min(1000,probe.Fit()))<.001,"Explicit fit follows viewport resize");
            probe.Rotation.Angle=90;probe.ClearZoomPreset();probe.FitViewportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Math.Abs(probe.zoom-Math.Min(1000,probe.Fit()))<.001,"Fit button accounts for rotation");
            probe.ResetZoom();Check(probe.zoom==100&&!probe.fitViewportZoom,"Initial reset keeps its existing behavior");
            probe.decoded=new(1,1,1,1,[]);probe.FitViewportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(probe.zoom==1000,"Fit obeys maximum zoom");
            probe.decoded=null;probe.FitViewportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));probe.ActualSizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            probe.decoded=original;probe.Rotation.Angle=0;probe.ResetZoom();
            await probe.VerifyZoomPresetTogglesAsync();
            foreach(var path in paths){await probe.favoritesStore.SetAsync(path,FavoriteKind.Folder,true);await probe.directoryShortcutStore.AddAsync(path);}
            await probe.RefreshChangedFavoritesAsync();await probe.RefreshChangedDirectoryShortcutsAsync();
            foreach(var dark in new[]{true,false}){
                probe.ApplyTheme(dark);Snapshot(probe,dark?".viewer-dark":".viewer-light");
                var favorites=probe.CreateFavoritesManagementWindow();favorites.Left=-32000;favorites.Top=-32000;favorites.Show();
                try{
                    var list=(ListBox)favorites.FindName("FavoriteManagementList");var up=(Button)favorites.FindName("MoveFavoriteUpButton");var down=(Button)favorites.FindName("MoveFavoriteDownButton");
                    Check(!up.IsEnabled&&!down.IsEnabled,"Favorite move buttons disabled without selection");
                    list.SelectedIndex=0;Check(!up.IsEnabled&&down.IsEnabled,"Favorite first item boundary");
                    list.SelectedIndex=1;var selected=((FavoriteItem)list.SelectedItem).Path;
                    up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await probe.favoriteManagementAction;
                    Check(list.SelectedIndex==0&&((FavoriteItem)list.SelectedItem).Path==selected,"Favorite move retains selection");
                    Check(probe.model.Favorites.Select(e=>e.Path).SequenceEqual(list.Items.Cast<FavoriteItem>().Select(e=>e.Path)),"Favorite sidebar follows manager order");
                    Check(!up.IsEnabled&&down.IsEnabled,"Favorite boundary updates after move");
                    down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await probe.favoriteManagementAction;
                    Check(list.SelectedIndex==1,"Favorite down button works");
                    list.SelectedIndex=2;Check(up.IsEnabled&&!down.IsEnabled,"Favorite last item boundary");
                    Snapshot(favorites,dark?".favorites-dark":".favorites-light");
                }finally{favorites.Close();}
                var links=probe.CreateDirectoryShortcutsManagementWindow();links.Left=-32000;links.Top=-32000;links.Show();
                try{
                    var list=(ListBox)links.FindName("DirectoryManagementList");var up=(Button)links.FindName("MoveDirectoryUpButton");var down=(Button)links.FindName("MoveDirectoryDownButton");
                    Check(!up.IsEnabled&&!down.IsEnabled,"Folder move buttons disabled without selection");
                    list.SelectedIndex=0;Check(!up.IsEnabled&&down.IsEnabled,"Folder link first item boundary");
                    list.SelectedIndex=1;var selected=((DirectoryShortcutItem)list.SelectedItem).Path;
                    up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await probe.directoryManagementAction;
                    Check(list.SelectedIndex==0&&((DirectoryShortcutItem)list.SelectedItem).Path==selected,"Folder link move retains selection");
                    Check((await new DirectoryShortcutStore(Path.Combine(root,"directory-shortcuts.json")).LoadAsync())[0].Path==selected,"Folder sidebar and persisted order agree");
                    Check(!up.IsEnabled&&down.IsEnabled,"Folder link boundary updates after move");
                    down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await probe.directoryManagementAction;
                    Check(list.SelectedIndex==1,"Folder link down button works");
                    list.SelectedIndex=2;Check(up.IsEnabled&&!down.IsEnabled,"Folder link last item boundary");
                    Snapshot(links,dark?".links-dark":".links-light");
                }finally{links.Close();}
            }
            var peer=new FavoritesStore(Path.Combine(root,"favorites.json"));await peer.SetAsync(paths[0],FavoriteKind.Folder,false);
            await probe.RefreshChangedFavoritesAsync();Check(probe.model.Favorites.Count==2,"Shared removal refreshes reordered favorites");
        }finally{probe.Close();await probe.TemporaryCleanup;}
    }
}
