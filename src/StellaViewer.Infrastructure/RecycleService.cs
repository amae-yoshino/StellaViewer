namespace StellaViewer.Infrastructure;

public sealed record RecycledEntry(string OriginalPath,bool IsDirectory,byte[] Identifier);
public sealed record RecycleResult(string Path,string? RestoredPath,RecycledEntry? Entry,string? Error);
public sealed class RecycleService
{
    private static Task<T> RunShellAsync<T>(Func<T> work)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try{result.SetResult(work());}catch(Exception error){result.SetException(error);}}){IsBackground=true,Name="StellaViewer recycle"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;
    }
    private readonly List<RecycledEntry> undo=[];
    private readonly SemaphoreSlim gate=new(1,1);
    public int UndoCount=>undo.Count;
    public async Task<RecycleResult[]> DeleteAsync(IEnumerable<string> paths,bool allowNetwork=false,CancellationToken token=default)
    {
        var input=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();await gate.WaitAsync(token);
        try{return await RunShellAsync(()=>{
            var results=new List<RecycleResult>();var deleted=new List<RecycledEntry>();
            foreach(var path in input.OrderBy(path=>path.Length)){
                if(token.IsCancellationRequested)break;
                try{
                    if(BrowsePath.IsVirtual(path))throw new NotSupportedException("書庫内項目・PDFページは削除対象外です。");
                    var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));NetworkAccess.Validate(full,allowNetwork);
                    if(Path.GetPathRoot(full)==full)throw new NotSupportedException("ドライブ自体は削除できません。");
                    // Select a parent once; selected descendants belong to that same deletion.
                    if(deleted.Any(item=>item.IsDirectory&&full.StartsWith(item.OriginalPath+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))continue;
                    var directory=Directory.Exists(full);
                    if(!directory&&!File.Exists(full))throw new FileNotFoundException("削除対象が見つかりません。",full);
                    var entry=new RecycledEntry(full,directory,RecycleNative.Recycle(full));deleted.Add(entry);results.Add(new(full,null,entry,null));
                }catch(Exception error){results.Add(new(path,null,null,error.Message));}
            }
            if(deleted.Count>0){undo.Clear();undo.AddRange(deleted);}return results.ToArray();
        });}finally{gate.Release();}
    }
    public async Task<RecycleResult[]> RestoreAsync(bool allowNetwork=false,CancellationToken token=default)
    {
        await gate.WaitAsync(token);
        try{return await RunShellAsync(()=>{
            var results=new List<RecycleResult>();
            foreach(var entry in undo.ToArray()){
                if(token.IsCancellationRequested)break;
                try{
                    var path=entry.OriginalPath;NetworkAccess.Validate(path,allowNetwork);
                    var folder=Path.GetDirectoryName(path)!;if(!Directory.Exists(folder))throw new DirectoryNotFoundException("復元先フォルダが見つかりません。");
                    var name=Path.GetFileName(path);var extension=entry.IsDirectory?"":Path.GetExtension(name);var stem=entry.IsDirectory?name:Path.GetFileNameWithoutExtension(name);
                    for(var number=0;;number++){
                        token.ThrowIfCancellationRequested();var candidate=number==0?name:$"{stem} ({number}){extension}";var target=Path.Combine(folder,candidate);
                        if(File.Exists(target)||Directory.Exists(target))continue;
                        try{RecycleNative.Restore(entry.Identifier,folder,candidate);undo.Remove(entry);results.Add(new(path,target,entry,null));break;}
                        catch when(File.Exists(target)||Directory.Exists(target)){ }
                    }
                }catch(OperationCanceledException){break;}
                catch(Exception error){results.Add(new(entry.OriginalPath,null,entry,error.Message));}
            }
            return results.ToArray();
        });}finally{gate.Release();}
    }
    public void ClearUndo()=>undo.Clear();
}
