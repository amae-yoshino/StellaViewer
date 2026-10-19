using System.Text.Json;
using StellaViewer.Infrastructure;

internal static class DirectoryShortcutChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"directory-shortcuts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"directory-shortcuts.json");var first=new DirectoryShortcutStore(path);var second=new DirectoryShortcutStore(path);var count=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
        async Task Reject<T>(Func<Task> action,string name)where T:Exception{try{await action();}catch(T){Check(true,name);return;}throw new Exception(name+" was accepted");}
        var a=Path.Combine(root,"画像フォルダ");var b=Path.Combine(root,"Books");Directory.CreateDirectory(a);Directory.CreateDirectory(b);
        Check((await first.LoadAsync()).Length==0,"Directory shortcut store starts empty");
        await Task.WhenAll(first.AddAsync(a),second.AddAsync(b));
        Check((await first.LoadAsync()).Length==2,"Concurrent shortcut additions preserve both folders");
        await first.RenameAsync(a," イラスト ");await second.AddAsync(a.ToUpperInvariant()+Path.DirectorySeparatorChar);
        var entries=await first.LoadAsync();
        Check(entries.Length==2&&entries.Single(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,a)).Name=="イラスト","Duplicate registration preserves custom name and Windows path identity");
        Check(Directory.Exists(a)&&!Directory.Exists(Path.Combine(root,"イラスト")),"Shortcut display rename does not rename physical directory");
        await Task.WhenAll(first.RenameAsync(a,"作品"),second.RemoveAsync(b));entries=await first.LoadAsync();
        Check(entries.Length==1&&entries[0].Name=="作品","Concurrent rename and removal merge into latest shortcut data");
        await first.AddAsync(b);var stale=await first.LoadAsync();await second.RemoveAsync(b);
        await Reject<InvalidOperationException>(()=>first.RenameAsync(stale.Single(e=>e.Path==b).Path,"古い画面"),"Rename from stale window cannot resurrect removed shortcut");
        Check((await second.LoadAsync()).All(e=>e.Path!=b),"Removed shortcut stays removed after stale rename");
        await Reject<ArgumentException>(()=>first.RenameAsync(a," \t "),"Empty display name is rejected");
        await Reject<ArgumentException>(()=>first.RenameAsync(a,"a\nb"),"Multiline display name is rejected");
        await Reject<ArgumentException>(()=>first.AddAsync("relative"),"Relative shortcut target is rejected");
        await Reject<ArgumentException>(()=>first.AddAsync(new ArchiveLocation(Path.Combine(root,"book.zip"),"folder").Identity),"Archive internal directory cannot be registered as physical shortcut");
        Check(DirectoryShortcutStore.NormalizePath(Path.GetPathRoot(root)!)==Path.GetPathRoot(root),"Drive root separator is preserved");
        await first.AddAsync(b);Directory.Delete(b);var missing=await DirectoryShortcutStore.CheckAvailabilityAsync(await first.LoadAsync(),false);
        Check(missing.Length==2&&missing.Single(e=>e.Path==b).Availability==FavoriteAvailability.Missing,"Missing directory stays visible as unavailable shortcut");
        await first.RenameAsync(b,"戻る予定");Directory.CreateDirectory(b);
        var restored=await DirectoryShortcutStore.CheckAvailabilityAsync(await first.LoadAsync(),false);
        Check(restored.Single(e=>e.Path==b) is {IsAvailable:true,Name:"戻る予定"},"Returning directory becomes usable and retains custom name");
        var network=new DirectoryShortcut(@"\\stella-shortcuts.invalid\share","ネットワーク");var calls=0;
        var blocked=await DirectoryShortcutStore.CheckAvailabilityAsync([network],false,exists:_=>{calls++;return true;});
        Check(calls==0&&blocked[0].Availability==FavoriteAvailability.NetworkBlocked,"Disabled network shortcut is listed without target I/O");
        var allowed=await DirectoryShortcutStore.CheckAvailabilityAsync([network],true,exists:_=>{calls++;return true;});
        Check(calls==1&&allowed[0].IsAvailable,"Allowed network shortcut can be probed");
        var denied=await DirectoryShortcutStore.CheckAvailabilityAsync([new(a,"拒否")],false,exists:_=>throw new UnauthorizedAccessException());
        Check(denied[0].Availability==FavoriteAvailability.Unavailable,"Access denied does not discard shortcut registration");
        var corrupt=Path.Combine(root,"corrupt.json");File.WriteAllText(corrupt,"broken");
        await new DirectoryShortcutStore(corrupt).AddAsync(a);
        Check(Directory.GetFiles(root,"corrupt.json.corrupt-*").Length==1,"Broken registration data is preserved before recovery");

        using(var cancel=new CancellationTokenSource()){cancel.Cancel();await Reject<OperationCanceledException>(()=>first.AddAsync(a,cancel.Token),"Shortcut update honors cancellation");}
        var reads=0;using var done=new CancellationTokenSource();var reader=Task.Run(async()=>{while(!done.IsCancellationRequested){await second.LoadAsync();Interlocked.Increment(ref reads);}});
        try{for(var i=0;i<20;i++)await first.RenameAsync(a,"作品 "+i);}finally{done.Cancel();}await reader;
        Check(reads>0&&(await first.LoadAsync()).Any(e=>e.Path==b),"Shared shortcut reads coexist with atomic rename updates");
        await first.RemoveAsync(a);Check(Directory.Exists(a),"Removing shortcut never deletes target folder");
        Check(!Directory.EnumerateFiles(root,"*.tmp").Any(),"Shortcut writes clean up temporary documents");
        var ephemeral=Path.Combine(root,"ephemeral.json");var transient=new DirectoryShortcutStore(ephemeral,false);await transient.AddAsync(a);
        Check(!File.Exists(ephemeral)&&(await transient.LoadAsync()).Length==1,"Nonpersistent shortcut tests avoid personal data files");
        return count;
    }
}
