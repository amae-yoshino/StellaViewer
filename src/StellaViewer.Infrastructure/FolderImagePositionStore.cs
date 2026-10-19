using System.Text.Json;

namespace StellaViewer.Infrastructure;

public sealed class FolderImagePositionStore(string? filePath=null,bool persist=true)
{
    private readonly Dictionary<string,string> memory=new(StringComparer.OrdinalIgnoreCase);
    private string StorePath=>filePath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StellaViewer","folder-image-positions.json");
    public Task<string?> LoadAsync(string folder)=>Task.Run(()=>Locked(()=>Read().GetValueOrDefault(FavoritesStore.NormalizePath(folder))));
    public Task ClearAsync()=>Task.Run(()=>Locked(()=>{
        if(!persist){memory.Clear();return true;}
        var path=StorePath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,"{}");File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        return true;
    }));
    public Task SaveAsync(string folder,string image, Func<bool>? recordingAllowed=null)=>Task.Run(()=>Locked(()=>{
        if (recordingAllowed is not null && !recordingAllowed()) return false;
        folder=FavoritesStore.NormalizePath(folder);image=FavoritesStore.NormalizePath(image);
        if(!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(image),folder))throw new ArgumentException("画像の親フォルダが一致しません。");
        var next=Read();next[folder]=image;
        if(!persist){memory[folder]=image;return true;}
        var path=StorePath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,path,true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}return true;
    }));
    private Dictionary<string,string> Read()
    {
        if(!persist)return new(memory,StringComparer.OrdinalIgnoreCase);
        try{using var source=new FileStream(StorePath,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            return new(JsonSerializer.Deserialize<Dictionary<string,string>>(source)??throw new InvalidDataException("フォルダ別の閲覧位置が不正です。"),StringComparer.OrdinalIgnoreCase);}
        catch(FileNotFoundException){return new(StringComparer.OrdinalIgnoreCase);}
        catch(DirectoryNotFoundException){return new(StringComparer.OrdinalIgnoreCase);}
    }
    private T Locked<T>(Func<T> operation)
    {
        using var mutex=new Mutex(false,@"Local\StellaViewer.FolderImagePositions");var held=false;
        try{try{held=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new IOException("閲覧位置の保存待ちがタイムアウトしました。");return operation();}
        finally{if(held)mutex.ReleaseMutex();}
    }
}
