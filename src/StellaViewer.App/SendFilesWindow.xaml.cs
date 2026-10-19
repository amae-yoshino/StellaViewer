using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace StellaViewer.App;

public sealed record SendSource(string Path,string Name);
public sealed record SendReport(string Summary,string Details);

public partial class SendFilesWindow : Window
{
    private readonly Func<string[],string,CancellationToken,IProgress<string>,Task<SendReport>> copy;
    private CancellationTokenSource? operation;
    internal Task ActiveCopy { get; private set; }=Task.CompletedTask;
    public SendFilesWindow(IEnumerable<SendSource> sources,string? selected,
        Func<string[],string,CancellationToken,IProgress<string>,Task<SendReport>> copy)
    {
        this.copy=copy;InitializeComponent();Sources.ItemsSource=sources.ToArray();
        Sources.SelectedItem=Sources.Items.OfType<SendSource>().FirstOrDefault(item=>item.Path==selected);
        UpdateSelection();Closing+=(_,e)=>{if(operation is not null){operation.Cancel();e.Cancel=true;}};
    }
    internal void SelectPaths(IEnumerable<string> paths)
    {
        var selected = paths.ToHashSet(StringComparer.OrdinalIgnoreCase); Sources.UnselectAll();
        foreach (var item in Sources.Items.OfType<SendSource>()) if (selected.Contains(item.Path)) Sources.SelectedItems.Add(item);
    }
    private void UpdateSelection()
    {
        if(SelectionCount is null)return;
        SelectionCount.Text=$"{Sources.SelectedItems.Count}件を選択";
        CopyButton.IsEnabled=operation is null&&Sources.SelectedItems.Count>0;
    }
    private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateSelection();
    private void SelectAllClicked(object sender,RoutedEventArgs e)=>Sources.SelectAll();
    private void ClearSelectionClicked(object sender,RoutedEventArgs e)=>Sources.UnselectAll();
    private void BrowseClicked(object sender,RoutedEventArgs e)
    {
        var picker=new OpenFolderDialog{Title="コピー先フォルダを選択"};
        if(picker.ShowDialog(this)==true)Destination.Text=picker.FolderName;
    }
    private void CopyClicked(object sender,RoutedEventArgs e)=>ActiveCopy=CopyAsync();
    internal async Task CopyAsync()
    {
        if(operation is not null||Sources.SelectedItems.Count==0)return;
        var paths=Sources.SelectedItems.OfType<SendSource>().Select(item=>item.Path).ToArray();
        var destination=Destination.Text.Trim();
        if(destination.Length==0){ProgressText.Text="コピー先フォルダを指定してください。";Destination.Focus();return;}
        operation=new CancellationTokenSource();SetBusy(true);
        ResultDetails.Visibility=Visibility.Collapsed;ResultDetails.Text="";
        try{
            var progress=new Progress<string>(text=>{if(operation is not null)ProgressText.Text=text;});
            var result=await copy(paths,destination,operation.Token,progress);
            ProgressText.Text=result.Summary;ResultDetails.Text=result.Details;
            ResultDetails.Visibility=string.IsNullOrEmpty(result.Details)?Visibility.Collapsed:Visibility.Visible;
        }
        catch(OperationCanceledException){ProgressText.Text="コピーを中止しました。完了済みのファイルは残ります。";}
        catch(Exception error){ProgressText.Text="コピーできませんでした："+error.Message;}
        finally{operation.Dispose();operation=null;SetBusy(false);}
    }
    private void SetBusy(bool busy)
    {
        Sources.IsEnabled=SelectAllButton.IsEnabled=ClearSelectionButton.IsEnabled=Destination.IsEnabled=BrowseButton.IsEnabled=!busy;
        CloseButton.Content=busy?"中止":"閉じる";UpdateSelection();
    }
    private void CloseClicked(object sender,RoutedEventArgs e)
    {if(operation is not null)operation.Cancel();else Close();}
}
