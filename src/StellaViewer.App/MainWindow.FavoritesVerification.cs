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
    private async Task VerifyFavoritesAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"favorites-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var folder=Path.Combine(root,"登録フォルダ");Directory.CreateDirectory(folder);
        var image=Path.Combine(folder,"favorite.png");File.Copy(imagePath,image);
        var missing=Path.Combine(root,"missing.png");
        var probe=new MainWindow(false,root){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        try{
            probe.Show();await probe.OpenAsync(image);await probe.activeDisplay;
            await probe.SetFavoriteAsync(folder,FavoriteKind.Folder,true);
            await probe.SetFavoriteAsync(image,FavoriteKind.Image,true);
            await probe.SetFavoriteAsync(missing,FavoriteKind.Image,true);
            var pixels=probe.model.Image;
            probe.UpdateLayout();var fraction=probe.CaptureSavedState().Layout.TreeFraction;
            await probe.ShowFavoritesAsync(true);probe.UpdateLayout();
            Check(probe.model.Favorites.Count==2&&probe.model.Favorites.Count(e=>e.Kind==FavoriteKind.Folder)==1,"Favorites shows folder itself and hides missing source");
            Check(ReferenceEquals(pixels,probe.model.Image)&&Math.Abs(probe.CaptureSavedState().Layout.TreeFraction-fraction)<.01,"Favorites toggle preserves image and tree splitter state");
            Check(probe.favoritesTimer!.IsEnabled,"Favorites refresh timer runs while visible");
            var external=new FavoritesStore(Path.Combine(root,"favorites.json"));
            File.Copy(imagePath,missing);
            await external.SetAsync(folder,FavoriteKind.Folder,false);
            await Task.Delay(2400);await probe.favoriteRefresh;
            Check(probe.model.Favorites.Any(e=>e.Path==missing)&&probe.model.Favorites.All(e=>e.Path!=folder),"Favorites polling reflects shared removal and reappearing path");
            File.Delete(missing);await probe.RefreshChangedFavoritesAsync();
            Check(probe.favoriteItems.Any(e=>e.Path==missing)&&probe.model.Favorites.All(e=>e.Path!=missing),"Favorites disappearance retains registration");
            await probe.SetFavoriteAsync(folder,FavoriteKind.Folder,true);
            var target=probe.model.Favorites.Single(e=>e.Path==image);
            probe.FavoriteList.SelectedItem=target;
            await probe.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            await Task.Delay(100);await probe.navigation;await probe.activeDisplay;
            Check(probe.model.HasImage&&probe.FavoriteList.SelectedItem is null,"Favorite activation opens image and allows repeated activation");
            await probe.OpenAsync(imagePath);await probe.activeDisplay;
            probe.FavoriteList.SelectedItem=target;await Task.Delay(100);await probe.navigation;await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path==image,"Same favorite can be activated again after browsing elsewhere");
            await probe.OpenFavoriteAsync(probe.model.Favorites.Single(e=>e.Path==folder));await probe.activeDisplay;
            Check(probe.model.Folder==folder,"Favorite folder opens its own directory");
            var pdf=Path.Combine(Path.GetDirectoryName(imagePath)!,"pdf-checks","pages.pdf");
            Check(FavoriteKindFor(new LocationEntry(pdf,"sample.pdf",false))==FavoriteKind.Pdf,"Physical PDF can be registered as a document");
            await probe.SetFavoriteAsync(pdf,FavoriteKind.Pdf,true);
            await probe.OpenFavoriteAsync(probe.model.Favorites.Single(e=>e.Path==pdf));await probe.activeDisplay;
            Check(probe.model.HasImage&&probe.pdfSession?.PageCount==3&&probe.CurrentFavoritePlace()?.Path==pdf,"Favorite PDF opens document and supports registration from current place");
            Check(FavoriteKindFor(new FileEntry(new PdfLocation(pdf,1).Identity,"1",0,default)) is null,"PDF individual page cannot be registered");
            var zip=Path.Combine(root,"test.zip");
            using(var archive=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(archive,image,"page.png");
            await probe.SetFavoriteAsync(zip,FavoriteKind.Archive,true);
            await probe.OpenFavoriteAsync(probe.model.Favorites.Single(e=>e.Path==zip));await probe.activeDisplay;
            Check(probe.model.HasImage&&probe.CurrentFavoritePlace()?.Path==zip,"Favorite archive opens its image pages");
            Check(FavoriteKindFor(new FileEntry(new ArchiveLocation(zip,"page.png",false).Identity,"page.png",0,default)) is null,"Archive internal image cannot be registered");
            await probe.OpenFavoriteAsync(probe.model.Favorites.Single(e=>e.Path==folder));await probe.activeDisplay;
            probe.ApplyTheme(true);probe.UpdateLayout();
            var content=(FrameworkElement)probe.Content;
            var snapshot=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
            snapshot.Render(content);SaveRender(snapshot,screenshotPath+".favorites.png");
            var dialog=probe.CreateFavoritesManagementWindow();dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-32000;dialog.Top=-32000;
            try{
                dialog.Show();var list=(ListBox)dialog.FindName("FavoriteManagementList");
                Check(list.Items.OfType<FavoriteItem>().Any(e=>e.Path==missing&&e.Availability==FavoriteAvailability.Missing),"Favorites management includes missing registrations");
                list.SelectedItem=list.Items.OfType<FavoriteItem>().Single(e=>e.Path==folder);
                ((Button)dialog.FindName("RemoveFavoriteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await probe.favoriteManagementAction;await probe.favoriteSaveTail;await probe.RefreshChangedFavoritesAsync();
                Check(Directory.Exists(folder)&&(await external.LoadAsync()).All(e=>e.Path!=folder),"Favorites management removes registration without deleting folder");
                dialog.UpdateLayout();var body=(FrameworkElement)dialog.Content;
                var bitmap=new RenderTargetBitmap((int)body.ActualWidth,(int)body.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render(body);SaveRender(bitmap,screenshotPath+".favorites-management.png");
            }finally{dialog.Close();}
            await probe.ShowFavoritesAsync(false);Check(!probe.favoritesTimer.IsEnabled,"Favorites timer stops when folder pane is shown");
            await probe.ShowFavoritesAsync(true);
        }finally{if(!probe.closed){probe.Close();await probe.TemporaryCleanup;}}
        Check(!probe.favoritesTimer!.IsEnabled&&probe.favoriteLifetime.IsCancellationRequested,"Favorites close stops background refresh");
    }
}
