using System.IO;
using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private void FileMenuOpened(object sender,RoutedEventArgs e){SendFileMenu.IsEnabled=CanSend(ImageList.SelectedItem);UpdateDeleteMenus();}
    private static bool CanSend(object? entry)=>entry switch{
        FileEntry image=>!BrowsePath.IsVirtual(image.Path),
        LocationEntry location=>!location.IsDirectory&&!BrowsePath.IsVirtual(location.Path),
        FavoriteItem favorite=>favorite.Kind!=FavoriteKind.Folder&&!BrowsePath.IsVirtual(favorite.Path),
        _=>false
    };
    private IEnumerable<SendSource> SendCandidates(string path)
    {
        var images=model.Entries.Where(CanSend).Select(entry=>new SendSource(entry.Path,entry.Name));
        var locations=model.Locations.Where(CanSend).Select(entry=>new SendSource(entry.Path,entry.Name));
        return new[]{new SendSource(path,Path.GetFileName(path))}.Concat(images).Concat(locations)
            .DistinctBy(entry=>entry.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private SendFilesWindow CreateSendFilesWindow(string path, string[]? selected = null)
    {
        var dialog=new SendFilesWindow(SendCandidates(path),path,async(paths,destination,token,progress)=>{
            var result=await SendCopyService.CopyAsync(paths,destination,model.AllowNetwork,token,progress);
            var summary=$"{(result.Canceled?"中止しました。 ":"")}コピー成功 {result.Succeeded}件、失敗 {result.Failed}件、名前変更 {result.Renamed}件。";
            model.Status=summary;
            var details=string.Join(Environment.NewLine,result.Items.Select(item=>item.Error is not null
                ?$"失敗：{item.Source} — {item.Error}"
                :$"{(item.Renamed?"名前変更してコピー":"コピー完了")}：{item.Source} → {item.Destination}"));
            return new SendReport(summary,details);
        }){Owner=this};
        CopySettingsTheme(dialog); if (selected is not null) dialog.SelectPaths(selected); return dialog;
    }
    private void SendFilesClicked(object sender,RoutedEventArgs e)
    {
        var entry=sender is MenuItem{Tag:string target}?new FileEntry(target,Path.GetFileName(target),0,default):ImageList.SelectedItem;
        if(!CanSend(entry)||EntryPath(entry) is not string path){model.Status="通常の画像・書庫・PDFファイルを選択してください。";return;}
        // Source and destination access is validated again by the worker before any I/O.
        try{NetworkAccess.Validate(path,model.AllowNetwork);CreateSendFilesWindow(path, OperationSelection(sender, path).Where(CanSend).Select(EntryPath).OfType<string>().ToArray()).ShowDialog();}
        catch(Exception error){model.Status="送る画面を開けませんでした："+error.Message;}
    }
}
