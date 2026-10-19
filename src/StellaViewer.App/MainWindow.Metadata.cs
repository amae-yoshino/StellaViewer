using System.Windows;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;
public partial class MainWindow
{
    private CancellationTokenSource? metadataRead;
    private Task metadataWork=Task.CompletedTask;
    private readonly List<Task> metadataOperations=[];
    private Task MetadataIdle=>Task.WhenAll(metadataOperations.ToArray());
    private int metadataVersion;
    private void CancelMetadataRead(){metadataVersion++;metadataRead?.Cancel();metadataRead?.Dispose();metadataRead=null;}
    private void MetadataPanelOpened(object sender,RoutedEventArgs e){if(IsInitialized)QueueMetadataRead();}
    private void MetadataPanelClosed(object sender,RoutedEventArgs e)=>CancelMetadataRead();
    private void QueueMetadataRead()
    {
        CancelMetadataRead();
        if(closed||closingInProgress||InfoToggle.IsChecked!=true||ImageList.SelectedItem is not FileEntry entry){model.MetadataText="画像を選択してください。";return;}
        if(displayedEntry is{} displayed&&displayed.Path==entry.Path)entry=displayed;
        metadataRead=new();var token=metadataRead.Token;var version=metadataVersion;var session=archiveSession;var allowed=model.AllowNetwork;
        model.MetadataText=entry.Name+"\n\n読み込み中…";metadataWork=ReadAsync();metadataOperations.Add(metadataWork);_=Forget(metadataWork);
        async Task Forget(Task operation){await operation;metadataOperations.Remove(operation);}
        async Task ReadAsync(){
            try{
                PngTextResult result;
                if(PdfLocation.IsIdentity(entry.Path))result=new([]);
                else if(ArchiveLocation.Parse(entry.Path) is{} location)result=await(session??throw new InvalidOperationException("書庫が開かれていません。")).ReadPngMetadataAsync(location,token,allowed);
                else result=await PngTextReader.ReadFileAsync(entry.Path,allowed,token,entry.Length,entry.Modified);
                var text=await Task.Run(()=>AiMetadata.Parse(result).DisplayText(),token);
                if(!token.IsCancellationRequested&&!closed&&version==metadataVersion)model.MetadataText=entry.Name+"\n\n"+text;
            }catch(OperationCanceledException){ }
            catch(Exception error){if(!token.IsCancellationRequested&&!closed&&version==metadataVersion)model.MetadataText=entry.Name+"\n\nメタデータを読み取れませんでした："+error.Message;}
        }
    }
}
