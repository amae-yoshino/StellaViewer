using System.Text.Json;

namespace StellaViewer.Infrastructure;

public sealed record DirectoryShortcut(string Path,string Name);
public sealed record DirectoryShortcutItem(DirectoryShortcut Entry,FavoriteAvailability Availability)
{
    public string Path=>Entry.Path;
    public string Name=>Entry.Name;
    public bool IsAvailable=>Availability==FavoriteAvailability.Available;
    public string StateLabel=>Availability switch{FavoriteAvailability.Available=>"利用可能",FavoriteAvailability.Missing=>"見つかりません",FavoriteAvailability.NetworkBlocked=>"ネットワーク閲覧が無効",_=>"利用できません"};
}

/// <summary>Physical folder shortcuts with names independent of the target directory name.</summary>
public sealed class DirectoryShortcutStore(string? filePath=null,bool persist=true)
{
    private sealed record Document(int Version,DirectoryShortcut[] Entries, bool ManualOrder=false);
    private DirectoryShortcut[] memory=[];
    private readonly object memorySync=new();
    private bool manualOrder;
    private string StorePath=>filePath??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StellaViewer","directory-shortcuts.json");
    public static string NormalizePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||BrowsePath.IsVirtual(path)||!System.IO.Path.IsPathFullyQualified(path))throw new ArgumentException("通常フォルダの絶対パスを指定してください。");
        return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
    }
    public static string NormalizeName(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Any(char.IsControl))throw new ArgumentException("表示名を1行で入力してください。");
        return name.Trim();
    }
    public Task<DirectoryShortcut[]> LoadAsync(CancellationToken token=default)=>Task.Run(()=>Locked(Read,token),token);
    public Task AddAsync(string path,CancellationToken token=default)
    {
        path=NormalizePath(path);var name=new FavoriteEntry(path,FavoriteKind.Folder).Name;
        return ChangeAsync(entries=>entries.Any(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path))?entries:[..entries,new(path,name)],token);
    }
    public Task RenameAsync(string path,string name,CancellationToken token=default)
    {
        path=NormalizePath(path);name=NormalizeName(name);
        return ChangeAsync(entries=>{
            if(!entries.Any(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path)))throw new InvalidOperationException("この登録は別のウィンドウで削除されています。更新してください。");
            return entries.Select(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path)?e with{Name=name}:e).ToArray();
        },token);
    }
    public Task RemoveAsync(string path,CancellationToken token=default)
    {
        path=NormalizePath(path);
        return ChangeAsync(entries=>entries.Where(e=>!StringComparer.OrdinalIgnoreCase.Equals(e.Path,path)).ToArray(),token);
    }
    private Task ChangeAsync(Func<DirectoryShortcut[],DirectoryShortcut[]> change,CancellationToken token)=>Task.Run(()=>Locked(()=>{
        var next=change(Read());token.ThrowIfCancellationRequested();Write(next);return true;
    },token),token);
    private T Locked<T>(Func<T> action,CancellationToken token)
    {
        if(!persist){lock(memorySync){token.ThrowIfCancellationRequested();return action();}}
        using var mutex=new Mutex(false,@"Local\StellaViewer.DirectoryShortcuts");var held=false;
        try{
            try{held=mutex.WaitOne(TimeSpan.FromSeconds(5));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new IOException("フォルダリンクの使用待ちがタイムアウトしました。");
            token.ThrowIfCancellationRequested();return action();
        }finally{if(held)mutex.ReleaseMutex();}
    }
    private DirectoryShortcut[] Read()
    {
        if(!persist)return Ordered(memory);
        var document=RecoverableSettings.Read<Document>(StorePath,()=>new(1, []),ValidDocument);
        if(document is null||document.Version!=1||document.Entries is null)throw new InvalidDataException("フォルダリンクの保存形式に対応していません。");
        var entries=new Dictionary<string,DirectoryShortcut>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in document.Entries){
            if(entry is null)throw new InvalidDataException("登録内容が不正です。");
            var path=NormalizePath(entry.Path);entries[path]=new(path,NormalizeName(entry.Name));
        }
        manualOrder=document.ManualOrder;
        return Ordered(entries.Values.ToArray());
    }
    private DirectoryShortcut[] Ordered(DirectoryShortcut[] entries)=>manualOrder?entries.ToArray():entries.OrderBy(e=>new FileEntry(e.Path,e.Name,0,default),FolderBrowser.NaturalComparer).ToArray();
    public Task MoveAsync(string path,int direction,CancellationToken token=default)
    {
        path=NormalizePath(path);
        if(direction is not (-1 or 1))throw new ArgumentOutOfRangeException(nameof(direction));
        return Task.Run(()=>Locked(()=>{
            var entries=Read();
            var index=Array.FindIndex(entries,e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path));
            if(index<0)throw new InvalidOperationException("この登録は別のウィンドウで削除されています。更新してください。");
            var target=index+direction;
            if(target<0||target>=entries.Length)return false;
            (entries[index],entries[target])=(entries[target],entries[index]);
            token.ThrowIfCancellationRequested();manualOrder=true;Write(entries);return true;
        },token),token);
    }
    private static bool ValidDocument(Document value)
    {
        if(value.Version!=1 || value.Entries is null)return false;
        try {
            foreach(var e in value.Entries){if(e is null)return false;_ = NormalizePath(e.Path);_ = NormalizeName(e.Name);}
            return true;
        } catch(Exception error) when(error is ArgumentException or NotSupportedException or InvalidDataException or OverflowException) {return false;}
    }
    private void Write(DirectoryShortcut[] entries)
    {
        if(!persist){memory=entries;return;}
        RecoverableSettings.Write(StorePath,new Document(1,entries,manualOrder),ValidDocument);
    }
    public static async Task<DirectoryShortcutItem[]> CheckAvailabilityAsync(DirectoryShortcut[] entries,bool allowNetwork,CancellationToken token=default,Func<FavoriteEntry,bool>? exists=null)
    {
        var states=await FavoritesStore.CheckAvailabilityAsync(entries.Select(e=>new FavoriteEntry(e.Path,FavoriteKind.Folder)),allowNetwork,token,exists);
        return entries.Select((entry,index)=>new DirectoryShortcutItem(entry,states[index].Availability)).ToArray();
    }
}
