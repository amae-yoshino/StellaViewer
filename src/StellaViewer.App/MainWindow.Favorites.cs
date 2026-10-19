using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private FavoritesStore favoritesStore=null!;
    private FavoriteItem[] favoriteItems=[];
    private Task favoriteRefresh=Task.CompletedTask,favoriteSaveTail=Task.CompletedTask;
    private CancellationTokenSource favoriteScan=new();
    private readonly CancellationTokenSource favoriteLifetime=new();
    private DispatcherTimer? favoritesTimer;
    private long favoriteGeneration;
    private bool updatingFavorites;
    private double? hiddenFolderTreeFraction;
    private void InitializeFavorites()
    {
        favoritesStore=new(storageDirectory is null?null:Path.Combine(storageDirectory,"favorites.json"),usePreferences||storageDirectory is not null);
        favoritesTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        favoritesTimer.Tick+=(_,_)=>{if(FavoritesButton.IsChecked==true&&favoriteRefresh.IsCompleted)_=RefreshFavoritesAsync();};
        Loaded+=async(_,_)=>await RefreshFavoritesAsync();
    }
    private Task RefreshFavoritesAsync()
    {
        if(closed||favoriteLifetime.IsCancellationRequested||!favoriteRefresh.IsCompleted)return favoriteRefresh;
        favoriteScan.Dispose();favoriteScan=CancellationTokenSource.CreateLinkedTokenSource(favoriteLifetime.Token);
        return favoriteRefresh=RefreshFavoritesCoreAsync(favoriteGeneration,model.AllowNetwork,favoriteScan.Token);
    }
    private async Task RefreshFavoritesCoreAsync(long generation,bool allowed,CancellationToken token)
    {
        try{
            var entries=await favoritesStore.LoadAsync(token);
            ShowRegistrationRecoveryNotice();
            var next=await FavoritesStore.CheckAvailabilityAsync(entries,allowed,token);
            if(token.IsCancellationRequested||generation!=favoriteGeneration||closed)return;
            favoriteItems=next;ApplyFavoriteRows();
        }catch(OperationCanceledException){}
        catch(Exception error){if(!closed&&!token.IsCancellationRequested)FavoritesStatus.Text="お気に入りを読み込めません："+error.Message;}
    }
    private void ApplyFavoriteRows()
    {
        var selected=(FavoriteList.SelectedItem as FavoriteItem)?.Path;
        var visible=favoriteItems.Where(e=>e.Availability==FavoriteAvailability.Available).ToArray();
        updatingFavorites=true;
        try{model.Favorites.Reconcile(visible,e=>e.Path);FavoriteList.SelectedItem=visible.FirstOrDefault(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,selected));}
        finally{updatingFavorites=false;}
        FavoritesStatus.Text=$"お気に入り {visible.Length}件 / 登録{favoriteItems.Length}件";
        FavoritesStatus.ToolTip="見つからない登録先と、ネットワーク閲覧が無効の項目は非表示にしています。登録は保持します。";
    }
    private async Task RefreshChangedFavoritesAsync()
    {
        ++favoriteGeneration;favoriteScan.Cancel();await favoriteRefresh;await RefreshFavoritesAsync();
    }
    private async Task SetFavoriteAsync(string path,FavoriteKind kind,bool registered)
    {
        async Task Save(Task previous){try{await previous;}catch(Exception){}await favoritesStore.SetAsync(path,kind,registered);}
        var saving=Save(favoriteSaveTail);favoriteSaveTail=saving;
        try{await saving;await RefreshChangedFavoritesAsync();model.Status=registered?"お気に入りに登録しました。":"お気に入りから削除しました。元のファイルやフォルダは変更していません。";}
        catch(Exception error){if(!closed)model.Status="お気に入りを保存できませんでした："+error.Message;}
    }
    private async Task ShowFavoritesAsync(bool shown)
    {
        HideDirectoryShortcuts();
        HideRecentLocations();
        if(shown&&FolderBrowserPanel.Visibility==Visibility.Visible&&TreeToggle.IsChecked==true){
            var first=FolderColumns.ColumnDefinitions[0].ActualWidth;var second=FolderColumns.ColumnDefinitions[2].ActualWidth;
            if(first+second>0)hiddenFolderTreeFraction=first/(first+second);
        }
        FavoritesButton.IsChecked=shown;FavoritesPanel.Visibility=shown?Visibility.Visible:Visibility.Collapsed;
        FolderBrowserPanel.Visibility=shown?Visibility.Collapsed:Visibility.Visible;
        if(shown)await RefreshFavoritesAsync();
        // Polling is enabled when the favorites pane is visible; refresh requests coalesce while a scan is busy.
        if(shown&&!closed&&!favoriteLifetime.IsCancellationRequested&&FavoritesButton.IsChecked==true)favoritesTimer?.Start();else favoritesTimer?.Stop();
    }
    private async void FavoritesToggled(object sender,RoutedEventArgs e)=>await ShowFavoritesAsync(FavoritesButton.IsChecked==true);
    private async void ShowFavoritesClicked(object sender,RoutedEventArgs e)=>await ShowFavoritesAsync(true);
    private async void ShowFoldersClicked(object sender,RoutedEventArgs e)=>await ShowFavoritesAsync(false);
    private async void RefreshFavoritesClicked(object sender,RoutedEventArgs e)=>await RefreshChangedFavoritesAsync();
    private async void FavoriteSelected(object sender,SelectionChangedEventArgs e)
    {
        if(updatingFavorites||FavoriteList.SelectedItem is not FavoriteItem item)return;
        updatingFavorites=true;try{FavoriteList.SelectedItem=null;}finally{updatingFavorites=false;}
        await OpenFavoriteAsync(item);
    }
    private async Task OpenFavoriteAsync(FavoriteItem item)
    {
        try{
            NetworkAccess.Validate(item.Path,model.AllowNetwork);
            var found=await Task.Run(()=>item.Kind==FavoriteKind.Folder?Directory.Exists(item.Path):File.Exists(item.Path));
            if(closed||closingInProgress)return;
            if(!found){await RefreshChangedFavoritesAsync();model.Status="お気に入りの登録先が見つかりません。登録は保持しています。";return;}
            await (navigation=OpenAsync(item.Path));
        }catch(Exception error){if(!closed)model.Status="お気に入りを開けませんでした："+error.Message;}
    }
    private static FavoriteKind? FavoriteKindFor(object? entry)=>entry switch{
        FavoriteItem favorite=>favorite.Kind,
        FileEntry image when !BrowsePath.IsVirtual(image.Path)=>FavoriteKind.Image,
        LocationEntry folder when !BrowsePath.IsVirtual(folder.Path)&&folder.IsDirectory=>FavoriteKind.Folder,
        LocationEntry pdf when !BrowsePath.IsVirtual(pdf.Path)&&(pdf.IsPdf||Path.GetExtension(pdf.Path).Equals(".pdf",StringComparison.OrdinalIgnoreCase))=>FavoriteKind.Pdf,
        LocationEntry file when !BrowsePath.IsVirtual(file.Path)&&!file.IsPdf&&!Path.GetExtension(file.Path).Equals(".pdf",StringComparison.OrdinalIgnoreCase)=>FavoriteKind.Archive,
        _=>null
    };
    private (string Path,FavoriteKind Kind)? CurrentFavoritePlace()
    {
        if(string.IsNullOrEmpty(model.Folder))return null;
        if(PdfLocation.Parse(model.Folder) is {} pdf&&!BrowsePath.IsVirtual(pdf.DocumentPath))return(pdf.DocumentPath,FavoriteKind.Pdf);
        if(ArchiveLocation.Parse(model.Folder) is {EntryPath:""} archive&&!BrowsePath.IsVirtual(archive.ArchivePath))return(archive.ArchivePath,FavoriteKind.Archive);
        return BrowsePath.IsVirtual(model.Folder)?null:(model.Folder,FavoriteKind.Folder);
    }
    private void FavoritesMenuOpened(object sender,RoutedEventArgs e)
    {
        FavoriteImageMenu.IsEnabled=ImageList.SelectedItem is FileEntry image&&!BrowsePath.IsVirtual(image.Path);
        FavoritePlaceMenu.IsEnabled=CurrentFavoritePlace() is not null;
    }
    private async void FavoriteImageClicked(object sender,RoutedEventArgs e)
    {if(ImageList.SelectedItem is FileEntry image&&!BrowsePath.IsVirtual(image.Path))await SetFavoriteAsync(image.Path,FavoriteKind.Image,true);}
    private async void FavoritePlaceClicked(object sender,RoutedEventArgs e)
    {if(CurrentFavoritePlace() is {} place)await SetFavoriteAsync(place.Path,place.Kind,true);}
    private async void FavoriteItemClicked(object sender,RoutedEventArgs e) => await SetSelectedFavoritesAsync(sender);
    private async Task SetSelectedFavoritesAsync(object sender)
    {
        if(sender is not MenuItem{Tag:string path,CommandParameter:FavoriteKind kind} item||BrowsePath.IsVirtual(path))return;
        var register = item.Header.ToString() != "お気に入りから削除";
        foreach (var entry in OperationSelection(sender, path)) if (EntryPath(entry) is string target && FavoriteKindFor(entry) is FavoriteKind targetKind && !BrowsePath.IsVirtual(target)) await SetFavoriteAsync(target, targetKind, register);
    }
    private void FavoriteAccessChanged()
    {
        ++favoriteGeneration;favoriteScan.Cancel();favoriteItems=[];ApplyFavoriteRows();
        if(FavoritesButton.IsChecked==true)_=RefreshChangedFavoritesAsync();
    }
    private void StopFavorites()
    {favoritesTimer?.Stop();favoriteLifetime.Cancel();favoriteScan.Cancel();}
}
