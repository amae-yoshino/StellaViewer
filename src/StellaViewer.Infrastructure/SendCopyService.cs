namespace StellaViewer.Infrastructure;

public sealed record SendCopyItem(string Source,string? Destination,bool Renamed,string? Error);
public sealed record SendCopyResult(IReadOnlyList<SendCopyItem> Items,bool Canceled)
{
    public int Succeeded=>Items.Count(item=>item.Error is null);
    public int Failed=>Items.Count(item=>item.Error is not null);
    public int Renamed=>Items.Count(item=>item.Error is null&&item.Renamed);
}

public static class SendCopyService
{
    public static Task<SendCopyResult> CopyAsync(IEnumerable<string> sources,string destination,
        bool allowNetwork=false,CancellationToken token=default,IProgress<string>? progress=null)
    {
        var snapshot=sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.Run(async()=>{
            if(BrowsePath.IsVirtual(destination))throw new NotSupportedException("コピー先には通常のフォルダを指定してください。");
            var folder=Path.GetFullPath(destination);NetworkAccess.Validate(folder,allowNetwork);
            if(!Directory.Exists(folder))throw new DirectoryNotFoundException("コピー先フォルダが見つかりません。");
            var results=new List<SendCopyItem>();var canceled=false;
            foreach(var source in snapshot){
                if(token.IsCancellationRequested){canceled=true;break;}
                string? temporary=null;string? target=null;Exception? failure=null;
                try{
                    if(BrowsePath.IsVirtual(source))throw new NotSupportedException("書庫内項目・PDFページは送れません。");
                    var full=Path.GetFullPath(source);NetworkAccess.Validate(full,allowNetwork);
                    var name=Path.GetFileName(full);
                    if(name.Length==0||Directory.Exists(full))throw new NotSupportedException("フォルダ自体のコピーは対象外です。");
                    progress?.Report($"{results.Count+1} / {snapshot.Length}：{name}");
                    DateTime created,modified,accessed;
                    await using(var input=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan)){
                        created=File.GetCreationTimeUtc(input.SafeFileHandle);
                        modified=File.GetLastWriteTimeUtc(input.SafeFileHandle);
                        accessed=File.GetLastAccessTimeUtc(input.SafeFileHandle);
                        var candidate=Path.Combine(folder,".stellaviewer-"+Guid.NewGuid().ToString("N")+".tmp");
                        await using var output=new FileStream(candidate,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,FileOptions.Asynchronous);
                        temporary=candidate;
                        await input.CopyToAsync(output,131072,token);await output.FlushAsync(token);
                    }
                    // Set times after closing the writer so its final write cannot replace them.
                    // Do this before publishing: metadata failures must not leave a completed copy.
                    File.SetCreationTimeUtc(temporary,created);
                    File.SetLastWriteTimeUtc(temporary,modified);
                    File.SetLastAccessTimeUtc(temporary,accessed);
                    var stem=Path.GetFileNameWithoutExtension(name);var extension=Path.GetExtension(name);
                    for(var number=0;;number++){
                        token.ThrowIfCancellationRequested();
                        target=Path.Combine(folder,number==0?name:$"{stem} ({number}){extension}");
                        try{File.Move(temporary,target,false);break;}
                        catch(IOException)when(File.Exists(target)||Directory.Exists(target)){ }
                    }
                    temporary=null;
                    results.Add(new(source,target,!StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(target),name),null));
                }
                catch(OperationCanceledException){canceled=true;}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){failure=error;}
                finally{
                    if(temporary is not null){
                        try{File.Delete(temporary);}
                        catch(Exception cleanup)when(cleanup is IOException or UnauthorizedAccessException){failure=new IOException($"{failure?.Message} 一時ファイルを削除できませんでした：{temporary}（{cleanup.Message}）",failure);}
                    }
                }
                if(failure is not null)results.Add(new(source,null,false,failure.Message));
                if(canceled)break;
            }
            return new SendCopyResult(results,canceled);
        });
    }
}
