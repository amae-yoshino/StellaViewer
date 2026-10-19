using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private DirectoryShortcutStore directoryShortcutStore=null!;
    private Task directoryRefresh=Task.CompletedTask,directorySaveTail=Task.CompletedTask;
    private readonly CancellationTokenSource directoryLifetime=new();
    private CancellationTokenSource directoryScan=new();
    private DispatcherTimer? directoryTimer;
    private long directoryGeneration;
    private bool updatingDirectoryShortcuts;
    private int directoryManagers;
    private bool DirectoryShortcutsActive=>DirectoryShortcutsButton.IsChecked==true||directoryManagers>0;
    private void InitializeDirectoryShortcuts()
    {
        directoryShortcutStore=new(storageDirectory is null?null:Path.Combine(storageDirectory,"directory-shortcuts.json"),usePreferences||storageDirectory is not null);
        directoryTimer=new(){Interval=TimeSpan.FromSeconds(2)};
        directoryTimer.Tick+=(_,_)=>{if(DirectoryShortcutsActive&&directoryRefresh.IsCompleted)_=RefreshDirectoryShortcutsAsync();};
    }
    private Task RefreshDirectoryShortcutsAsync()
    {
        if(closed||directoryLifetime.IsCancellationRequested||!directoryRefresh.IsCompleted)return directoryRefresh;
        directoryScan.Dispose();directoryScan=CancellationTokenSource.CreateLinkedTokenSource(directoryLifetime.Token);
        return directoryRefresh=RefreshDirectoryShortcutsCoreAsync(directoryGeneration,model.AllowNetwork,directoryScan.Token);
    }
    private async Task RefreshDirectoryShortcutsCoreAsync(long generation,bool allowed,CancellationToken token)
    {
        try{
            var entries=await directoryShortcutStore.LoadAsync(token);
            ShowRegistrationRecoveryNotice();
            var items=await DirectoryShortcutStore.CheckAvailabilityAsync(entries,allowed,token);
            if(token.IsCancellationRequested||closed||generation!=directoryGeneration)return;
            var sorted=items;
            updatingDirectoryShortcuts=true;
            try{model.DirectoryShortcuts.Reconcile(sorted,e=>e.Path);}finally{updatingDirectoryShortcuts=false;}
            DirectoryShortcutsStatus.Text=$"フォルダリンク {items.Length}件";
        }catch(OperationCanceledException){}
        catch(Exception error){if(!closed&&!token.IsCancellationRequested)DirectoryShortcutsStatus.Text="一覧を読み込めません："+error.Message;}
    }
    private async Task RefreshChangedDirectoryShortcutsAsync()
    {
        ++directoryGeneration;directoryScan.Cancel();await directoryRefresh;await RefreshDirectoryShortcutsAsync();
    }
    private async Task<bool> ChangeDirectoryShortcutAsync(Func<Task> operation,string message)
    {
        var saving=SaveAfterAsync(directorySaveTail,operation);directorySaveTail=saving;
        try{
            await saving;await RefreshChangedDirectoryShortcutsAsync();
            if(!closed)model.Status=message;return true;
        }catch(Exception error){if(!closed)model.Status="フォルダリンクを保存できません："+error.Message;return false;}
    }
    private async Task AddDirectoryShortcutAsync(string path)
    {
        try{
            NetworkAccess.Validate(path,model.AllowNetwork);
            if(!await Task.Run(()=>Directory.Exists(path))){model.Status="登録するフォルダが見つからないか、利用できません。";return;}
            if(closed||closingInProgress)return;
            await ChangeDirectoryShortcutAsync(()=>directoryShortcutStore.AddAsync(path),"フォルダリンクに登録しました。");
        }catch(Exception error){if(!closed)model.Status="フォルダを登録できません："+error.Message;}
    }
    private async Task ShowDirectoryShortcutsAsync()
    {
        await ShowFavoritesAsync(false);
        if(closed||closingInProgress)return;
        if(TreeToggle.IsChecked==true){
            var first=FolderColumns.ColumnDefinitions[0].ActualWidth;var second=FolderColumns.ColumnDefinitions[2].ActualWidth;
            if(first+second>0)hiddenFolderTreeFraction=first/(first+second);
        }
        DirectoryShortcutsButton.IsChecked=true;DirectoryShortcutsPanel.Visibility=Visibility.Visible;FolderBrowserPanel.Visibility=Visibility.Collapsed;
        await RefreshDirectoryShortcutsAsync();UpdateDirectoryShortcutTimer();
    }
    private void HideDirectoryShortcuts()
    {
        DirectoryShortcutsButton.IsChecked=false;DirectoryShortcutsPanel.Visibility=Visibility.Collapsed;UpdateDirectoryShortcutTimer();
    }
    private void UpdateDirectoryShortcutTimer()
    {
        if(DirectoryShortcutsActive&&!closed&&!directoryLifetime.IsCancellationRequested)directoryTimer?.Start();else directoryTimer?.Stop();
    }
    private async void DirectoryShortcutsClicked(object sender,RoutedEventArgs e)
    {
        if(DirectoryShortcutsPanel.Visibility==Visibility.Visible)await ShowFavoritesAsync(false);else await ShowDirectoryShortcutsAsync();
    }
    private async void ShowDirectoryShortcutsClicked(object sender,RoutedEventArgs e)=>await ShowDirectoryShortcutsAsync();
    private async void RefreshDirectoryShortcutsClicked(object sender,RoutedEventArgs e)=>await RefreshChangedDirectoryShortcutsAsync();
    private async Task PickDirectoryShortcutAsync(Window? owner=null)
    {
        var picker=new OpenFolderDialog{Title="フォルダリンクに追加するフォルダを選択"};
        if(picker.ShowDialog(owner??this)==true)await AddDirectoryShortcutAsync(picker.FolderName);
    }
    private async void AddDirectoryShortcutClicked(object sender,RoutedEventArgs e)=>await PickDirectoryShortcutAsync();
    private async void AddCurrentDirectoryShortcutClicked(object sender,RoutedEventArgs e)
    {
        if(model.LocationFolder.Length>0&&!BrowsePath.IsVirtual(model.LocationFolder))await AddDirectoryShortcutAsync(model.LocationFolder);
    }
    private async void DirectoryShortcutSelected(object sender,SelectionChangedEventArgs e)
    {
        if(updatingDirectoryShortcuts||DirectoryShortcutList.SelectedItem is not DirectoryShortcutItem item)return;
        updatingDirectoryShortcuts=true;try{DirectoryShortcutList.SelectedItem=null;}finally{updatingDirectoryShortcuts=false;}
        await(navigation=OpenDirectoryShortcutAsync(item));
    }
    private async Task OpenDirectoryShortcutAsync(DirectoryShortcutItem item)
    {
        var request=openRequestVersion;
        try{
            NetworkAccess.Validate(item.Path,model.AllowNetwork);
            var exists=await Task.Run(()=>Directory.Exists(item.Path));
            if(closed||closingInProgress||request!=openRequestVersion)return;
            if(!exists){await RefreshChangedDirectoryShortcutsAsync();model.Status="登録先を利用できません。登録は保持しています。";return;}
            await(navigation=OpenAsync(item.Path));
        }catch(Exception error){if(!closed)model.Status="登録先を開けません："+error.Message;}
    }
    private void DirectoryShortcutAccessChanged()
    {
        ++directoryGeneration;directoryScan.Cancel();
        updatingDirectoryShortcuts=true;
        try{
            var items=model.DirectoryShortcuts.Select(e=>!model.AllowNetwork&&NetworkAccess.IsNetworkPath(e.Path)?e with{Availability=FavoriteAvailability.NetworkBlocked}:e).ToArray();
            model.DirectoryShortcuts.Reconcile(items,e=>e.Path);
        }finally{updatingDirectoryShortcuts=false;}
        if(DirectoryShortcutsActive)_=RefreshChangedDirectoryShortcutsAsync();
    }
    private void StopDirectoryShortcuts()
    {directoryTimer?.Stop();directoryLifetime.Cancel();directoryScan.Cancel();}
}
