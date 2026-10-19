using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool recordRecentLocations = true, recordViewingState = true;
    private RecentLocationStore recentLocationStore=null!;
    private Task recentSaveTail=Task.CompletedTask,recentRefresh=Task.CompletedTask;
    private readonly CancellationTokenSource recentLifetime=new();
    private CancellationTokenSource recentScan=new();
    private DispatcherTimer? recentTimer;
    private long recentGeneration;
    private bool updatingRecentLocations;
    private void InitializeRecentLocations()
    {
        recentLocationStore=new(storageDirectory is null?null:Path.Combine(storageDirectory,"recent-locations.json"),usePreferences||storageDirectory is not null);
        recentTimer=new(){Interval=TimeSpan.FromSeconds(2)};
        recentTimer.Tick+=(_,_)=>{if(RecentLocationsButton.IsChecked==true&&recentRefresh.IsCompleted)_=RefreshRecentLocationsAsync();};
    }
    private async Task RememberRecentPlaceAsync(string place)
    {
        if (!recordRecentLocations) return;
        var saving=SaveAfterAsync(recentSaveTail,()=>recentLocationStore.RecordAsync(place, recordingAllowed: () => !usePreferences || PreferencesStore.Load(preferenceFilePath).RecordRecentLocations));recentSaveTail=saving;
        try{
            await saving;
            if(!closed&&RecentLocationsButton.IsChecked==true)await RefreshChangedRecentLocationsAsync();
        }catch(Exception error){if(!closed)model.Status="履歴を保存できません："+error.Message;}
    }
    private Task RefreshRecentLocationsAsync()
    {
        if(closed||recentLifetime.IsCancellationRequested||!recentRefresh.IsCompleted)return recentRefresh;
        recentScan.Dispose();recentScan=CancellationTokenSource.CreateLinkedTokenSource(recentLifetime.Token);
        return recentRefresh=RefreshRecentLocationsCoreAsync(recentGeneration,recentScan.Token);
    }
    private async Task RefreshRecentLocationsCoreAsync(long generation,CancellationToken token)
    {
        try{
            var rows=await recentLocationStore.LoadAsync(token);
            ShowRegistrationRecoveryNotice();
            if(token.IsCancellationRequested||closed||generation!=recentGeneration)return;
            updatingRecentLocations=true;
            try{model.RecentLocations.Reconcile(rows,e=>RecentLocationStore.Key(e.Location));}finally{updatingRecentLocations=false;}
            RecentLocationsStatus.Text=$"履歴 {rows.Length}件（最大{RecentLocationStore.MaximumEntries}件）";
        }catch(OperationCanceledException){}
        catch(Exception error){if(!closed&&!token.IsCancellationRequested)RecentLocationsStatus.Text="履歴を読み込めません："+error.Message;}
    }
    private async Task RefreshChangedRecentLocationsAsync()
    {
        ++recentGeneration;recentScan.Cancel();await recentRefresh;await RefreshRecentLocationsAsync();
    }
    private void HideRecentLocations()
    {
        RecentLocationsButton.IsChecked=false;RecentLocationsPanel.Visibility=Visibility.Collapsed;recentTimer?.Stop();
    }
    private async Task ShowRecentLocationsAsync()
    {
        await ShowFavoritesAsync(false);
        if(closed||closingInProgress)return;
        if(TreeToggle.IsChecked==true){
            var first=FolderColumns.ColumnDefinitions[0].ActualWidth;var second=FolderColumns.ColumnDefinitions[2].ActualWidth;
            if(first+second>0)hiddenFolderTreeFraction=first/(first+second);
        }
        FolderBrowserPanel.Visibility=Visibility.Collapsed;RecentLocationsPanel.Visibility=Visibility.Visible;RecentLocationsButton.IsChecked=true;
        try{await recentSaveTail;}catch(Exception){/* The initiating open operation reports failed history writes. */}
        await RefreshRecentLocationsAsync();
        if(!closed&&!recentLifetime.IsCancellationRequested&&RecentLocationsButton.IsChecked==true)recentTimer?.Start();
    }
    private async void RecentLocationsClicked(object sender,RoutedEventArgs e)
    {
        if(RecentLocationsPanel.Visibility==Visibility.Visible)await ShowFavoritesAsync(false);else await ShowRecentLocationsAsync();
    }
    private async void ShowRecentLocationsClicked(object sender,RoutedEventArgs e)=>await ShowRecentLocationsAsync();
    private async void RefreshRecentLocationsClicked(object sender,RoutedEventArgs e)=>await RefreshChangedRecentLocationsAsync();
    private async void RecentLocationSelected(object sender,SelectionChangedEventArgs e)
    {
        if(updatingRecentLocations||RecentLocationList.SelectedItem is not RecentLocation item)return;
        updatingRecentLocations=true;try{RecentLocationList.SelectedItem=null;}finally{updatingRecentLocations=false;}
        await(navigation=OpenAsync(item.Location));
    }
    private void StopRecentLocations()
    {recentTimer?.Stop();recentLifetime.Cancel();recentScan.Cancel();}
}
