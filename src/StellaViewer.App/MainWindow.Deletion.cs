using System.IO;
using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;
public partial class MainWindow
{
    private readonly RecycleService recycleService=new();
    private bool deleteEnabled,fileManagementBusy;
    private Task deletionWork=Task.CompletedTask;
    private static bool IsPhysicalDeletable(object? entry)=>EntryPath(entry) is string path&&!BrowsePath.IsVirtual(path)&&Path.GetPathRoot(path)!=Path.TrimEndingDirectorySeparator(path);
    private void UpdateDeleteMenus()
    {
        if(DeleteFileMenu is null)return;
        DeleteFileMenu.IsEnabled=deleteEnabled&&!fileManagementBusy&&!loading&&!loadingLocations&&IsPhysicalDeletable(ImageList.SelectedItem);
        UndoDeleteMenu.IsEnabled=!fileManagementBusy&&recycleService.UndoCount>0;
    }
    private IEnumerable<SendSource> DeleteCandidates(string path)=>new[]{new SendSource(path,Path.GetFileName(path))}
        .Concat(model.Entries.Where(IsPhysicalDeletable).Select(entry=>new SendSource(entry.Path,entry.Name)))
        .Concat(model.Locations.Where(IsPhysicalDeletable).Select(entry=>new SendSource(entry.Path,entry.Name)))
        .DistinctBy(entry=>entry.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    private void DeleteFilesClicked(object sender,RoutedEventArgs e)
    {
        if(!deleteEnabled){model.Status="削除は無効です。設定の「表示と操作」から有効にできます。";return;}
        if(fileManagementBusy||loading||loadingLocations){model.Status="読み込み・操作の完了後に削除してください。";return;}
        if(ActionPath(sender) is not string path||BrowsePath.IsVirtual(path))return;
        try{NetworkAccess.Validate(path,model.AllowNetwork);CreateDeleteWindow(path, OperationSelection(sender, path).Where(IsPhysicalDeletable).Select(EntryPath).OfType<string>().ToArray()).ShowDialog();}
        catch(Exception error){model.Status="削除画面を開けませんでした："+error.Message;}
    }
    private Window CreateDeleteWindow(string path, string[]? selected = null)
    {
        var dialog=new Window{Owner=this,Title="ゴミ箱へ移動する項目の確認",FontSize=14,Width=760,Height=540,MinWidth=600,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        NameScope.SetNameScope(dialog,new NameScope());CopySettingsTheme(dialog);dialog.SetResourceReference(BackgroundProperty,"PanelBackground");dialog.SetResourceReference(ForegroundProperty,"PanelForeground");
        var root=new DockPanel{Margin=new Thickness(16)};root.SetResourceReference(BackgroundProperty,"PanelBackground");dialog.Content=root;
        var description=new TextBlock{Text="ゴミ箱へ移動する項目を選択してください。Ctrl／Shiftで複数選択できます。フォルダは中身もまとめて移動します。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
        var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var acknowledge=new CheckBox{Content="選択した項目をゴミ箱へ移動することを確認しました。",Margin=new Thickness(0,0,0,8)};acknowledge.SetResourceReference(ForegroundProperty,"PanelForeground");footer.Children.Add(acknowledge);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};footer.Children.Add(status);
        var details=new TextBox{IsReadOnly=true,Visibility=Visibility.Collapsed,MaxHeight=110,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,6,0,0)};footer.Children.Add(details);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};footer.Children.Add(buttons);
        var execute=new Button{Content="ゴミ箱へ移動",MinWidth=120,IsEnabled=false};var close=new Button{Content="閉じる",MinWidth=90,Margin=new Thickness(8,0,0,0)};buttons.Children.Add(execute);buttons.Children.Add(close);
        var list=new ListBox{ItemsSource=DeleteCandidates(path),SelectionMode=SelectionMode.Extended,ItemTemplate=(DataTemplate)Resources["OperationSourceTemplate"],HorizontalContentAlignment=HorizontalAlignment.Stretch};ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);list.SetResourceReference(BackgroundProperty,"PanelBackground");list.SetResourceReference(ForegroundProperty,"PanelForeground");root.Children.Add(list);
        var chosen = (selected ?? [path]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list.Items.OfType<SendSource>()) if (chosen.Contains(item.Path)) list.SelectedItems.Add(item);
        var busy=false;CancellationTokenSource? operation=null;
        void Update(){execute.IsEnabled=!busy&&deleteEnabled&&acknowledge.IsChecked==true&&list.SelectedItems.Count>0;}
        acknowledge.Checked+=(_,_)=>Update();acknowledge.Unchecked+=(_,_)=>Update();list.SelectionChanged+=(_,_)=>{acknowledge.IsChecked=false;Update();};
        async Task Run(){
            if(!execute.IsEnabled)return;var selected=list.SelectedItems.OfType<SendSource>().Select(item=>item.Path).ToArray();
            busy=true;operation=new();list.IsEnabled=acknowledge.IsEnabled=false;close.Content="中止";Update();status.Text="ゴミ箱へ移動しています…";
            try{var result=await DeletePathsAsync(selected,operation.Token);status.Text=OperationSummary(result,false);details.Text=OperationDetails(result,false);details.Visibility=Visibility.Visible;list.ItemsSource=DeleteCandidates(path).Where(item=>!result.Any(r=>r.Error is null&&IsPathWithin(item.Path,r.Path))).ToArray();}
            catch(Exception error){status.Text="削除できませんでした："+error.Message;}
            finally{operation.Dispose();operation=null;busy=false;list.IsEnabled=acknowledge.IsEnabled=true;close.Content="閉じる";acknowledge.IsChecked=false;Update();}
        }
        execute.Click+=(_,_)=>deletionWork=Run();close.Click+=(_,_)=>{if(busy)operation?.Cancel();else dialog.Close();};dialog.Closing+=(_,e)=>{if(busy){operation?.Cancel();e.Cancel=true;}};
        dialog.RegisterName("DeleteSources",list);dialog.RegisterName("DeleteAcknowledge",acknowledge);dialog.RegisterName("DeleteExecute",execute);dialog.RegisterName("DeleteStatus",status);dialog.RegisterName("DeleteDetails",details);return dialog;
    }
    private static bool IsPathWithin(string path,string removed)
    {
        var physical=BrowsePath.PhysicalPath(path);return string.Equals(physical,removed,StringComparison.OrdinalIgnoreCase)||physical.StartsWith(Path.TrimEndingDirectorySeparator(removed)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    private static string OperationSummary(RecycleResult[] result,bool restoring)=>$"{(restoring?"取り消し":"ゴミ箱への移動")}：成功 {result.Count(item=>item.Error is null)}件、失敗 {result.Count(item=>item.Error is not null)}件"+
        (restoring?$"、名前変更 {result.Count(item=>item.Error is null&&!string.Equals(item.Path,item.RestoredPath,StringComparison.OrdinalIgnoreCase))}件。":"。");
    private static string OperationDetails(RecycleResult[] result,bool restoring)=>string.Join(Environment.NewLine,result.Select(item=>item.Error is not null?$"失敗：{item.Path} — {item.Error}":restoring?$"復元：{item.Path} → {item.RestoredPath}":$"ゴミ箱へ移動：{item.Path}"));
    private async Task<RecycleResult[]> DeletePathsAsync(string[] paths,CancellationToken token=default)
    {
        if(!deleteEnabled)throw new InvalidOperationException("削除は設定で無効になっています。");
        fileManagementBusy=true;StopFolderMonitor();CancelPrefetch();search.Cancel();
        CancelMetadataRead();
        var displayInterrupted=!activeDisplay.IsCompleted;if(displayInterrupted)display.Cancel();
        var thumbnails=Descendants(ListArea).Concat(Descendants(LocationArea)).OfType<Thumbnail>().Distinct().ToArray();
        var stoppedThumbnails=Task.WhenAll(thumbnails.Select(thumbnail=>thumbnail.StopAsync()));
        var before=model.Entries.ToArray();var current=ImageList.SelectedItem as FileEntry;var currentIndex=Array.IndexOf(before,current);
        try{
            await Task.WhenAll(stoppedThumbnails,prefetchWork,watchWork,activeDisplay,MetadataIdle);
            var results=await recycleService.DeleteAsync(paths,model.AllowNetwork,token);var removed=results.Where(item=>item.Error is null).Select(item=>item.Path).ToArray();
            bool Gone(string path)=>removed.Any(item=>IsPathWithin(path,item));
            if(removed.Length>0){
                all.RemoveAll(entry=>Gone(entry.Path));var remaining=before.Where(entry=>!Gone(entry.Path)).ToArray();
                var next=current is not null&&!Gone(current.Path)?current:before.Skip(Math.Max(0,currentIndex+1)).FirstOrDefault(entry=>!Gone(entry.Path))??before.Take(Math.Max(0,currentIndex)).LastOrDefault(entry=>!Gone(entry.Path));
                updating=true;try{model.Entries=new LiveEntryCollection<FileEntry>(remaining);ImageList.SelectedItem=next;SyncTiles(true);}finally{updating=false;}
                if(current!=next||spreadEnabled){StopAnimation();display.Cancel();missingDisplayedPaths.Clear();changedDisplayedPaths.Clear();RebaseSpread();if(next is null)ClearImage();else await(activeDisplay=LoadSelectedAsync());}
                if(model.Folder.Length>0&&Gone(model.Folder)){archiveSession?.Dispose();archiveSession=null;model.ArchiveSession=null;pdfSession?.Dispose();pdfSession=null;model.PdfSession=null;model.Folder="";model.CurrentAddress="";}
                var location=model.LocationFolder;
                if(location.Length>0&&Gone(location)){location=Path.GetDirectoryName(BrowsePath.PhysicalPath(location))??"";while(location.Length>0&&Gone(location))location=Path.GetDirectoryName(location)??"";}
                if(location.Length>0)await LoadLocationsAsync(location,browse.Token);
                model.ListingStatus=$"{model.Entries.Count:N0}件 / 全{all.Count:N0}件";UpdateStatus();
            }
            model.Status=OperationSummary(results,false);return results;
        }finally{if(displayInterrupted&&ImageList.SelectedItem is FileEntry&&model.Image is null)await(activeDisplay=LoadSelectedAsync());foreach(var thumbnail in thumbnails.Where(item=>item.IsLoaded))thumbnail.Refresh();fileManagementBusy=false;UpdateDeleteMenus();if(model.Folder.Length>0&&!BrowsePath.IsVirtual(model.Folder))await StartFolderMonitorAsync(model.Folder,browse.Token);StartPrefetch();if(InfoToggle.IsChecked==true&&ImageList.SelectedItem is FileEntry)QueueMetadataRead();}
    }
    private async void UndoDeleteClicked(object sender,RoutedEventArgs e)
    {
        var result=await RestoreDeletionAsync();if(result.Length>0)MessageBox.Show(this,OperationSummary(result,true)+Environment.NewLine+OperationDetails(result,true),"削除の取り消し",MessageBoxButton.OK,MessageBoxImage.Information);
    }
    private async Task<RecycleResult[]> RestoreDeletionAsync()
    {
        if(fileManagementBusy||recycleService.UndoCount==0)return [];fileManagementBusy=true;IsEnabled=false;
        try{
            var result=await recycleService.RestoreAsync(model.AllowNetwork);
            if(model.Folder.Length>0&&!BrowsePath.IsVirtual(model.Folder))await ReloadAsync();
            else if(model.LocationFolder.Length>0)await LoadLocationsAsync(model.LocationFolder,browse.Token);
            model.Status=OperationSummary(result,true);return result;
        }finally{fileManagementBusy=false;IsEnabled=true;UpdateDeleteMenus();}
    }
}
