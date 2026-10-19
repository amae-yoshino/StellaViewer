using System.Text.Json;
using StellaViewer.Infrastructure;

internal static class FavoritesChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"favorites-checks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var file=Path.Combine(root,"favorites.json");var first=new FavoritesStore(file);var second=new FavoritesStore(file);var count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        async Task Reject<T>(Func<Task> action,string label)where T:Exception{
            try{await action();}catch(T){Check(true,label);return;}throw new Exception(label+" was accepted");
        }
        var a=Path.Combine(root,"画像 space, 1.png");var b=Path.Combine(root,"book.zip");var folder=Path.Combine(root,"フォルダ");Directory.CreateDirectory(folder);
        File.WriteAllText(a,"fixture");File.WriteAllText(b,"archive fixture");
        Check((await first.LoadAsync()).Length==0,"Favorites missing store starts empty");
        await Task.WhenAll(first.SetAsync(a,FavoriteKind.Image,true),second.SetAsync(folder,FavoriteKind.Folder,true));
        Check((await first.LoadAsync()).Length==2,"Favorites concurrent additions preserve both paths");
        await second.SetAsync(a.ToUpperInvariant(),FavoriteKind.Image,true);
        Check((await first.LoadAsync()).Length==2,"Favorites path identity ignores Windows case");
        await second.SetAsync(a,FavoriteKind.Image,true);
        var stale=await first.LoadAsync();await second.SetAsync(b,FavoriteKind.Archive,true);await first.SetAsync(a,FavoriteKind.Image,false);
        var loaded=await first.LoadAsync();Check(stale.Length==2&&loaded.Any(e=>e.Path==b)&&loaded.Any(e=>e.Path==folder)&&loaded.All(e=>e.Path!=a),"Favorites deletion merges into latest store instead of stale UI snapshot");
        await Task.WhenAll(first.SetAsync(a,FavoriteKind.Image,true),second.SetAsync(b,FavoriteKind.Archive,false));
        loaded=await new FavoritesStore(file).LoadAsync();
        Check(loaded.Length==2&&loaded.Any(e=>e.Path==a)&&loaded.All(e=>e.Path!=b),"Favorites concurrent add and removal keep unrelated registrations");
        Check(loaded.Single(e=>e.Path==folder).Kind==FavoriteKind.Folder,"Favorite folder persists as one folder record");
        Check(FavoritesStore.NormalizePath(folder+Path.DirectorySeparatorChar)==folder,"Favorites trims trailing separator for duplicate identity");
        var drive=Path.GetPathRoot(root)!;Check(FavoritesStore.NormalizePath(drive)==drive,"Favorites preserves drive root separator");
        Check(new FavoriteEntry(a,FavoriteKind.Image).Name=="画像 space, 1.png","Favorite display names preserve Unicode spaces and commas");
        await Reject<ArgumentException>(()=>first.SetAsync(new ArchiveLocation(b,"page.png",false).Identity,FavoriteKind.Image,true),"Favorites rejects archive internal identities");
        await Reject<ArgumentException>(()=>first.SetAsync(new PdfLocation(Path.Combine(root,"book.pdf"),1).Identity,FavoriteKind.Image,true),"Favorites rejects individual PDF pages");
        await Reject<ArgumentException>(()=>first.SetAsync("relative.png",FavoriteKind.Image,true),"Favorites rejects unstable relative paths");
        await Reject<ArgumentException>(()=>first.SetAsync(a,(FavoriteKind)99,true),"Favorites rejects unknown kinds");
        var missing=Path.Combine(root,"missing-folder");await first.SetAsync(missing,FavoriteKind.Folder,true);
        var rows=await FavoritesStore.CheckAvailabilityAsync(await first.LoadAsync(),false);
        Check(rows.Single(e=>e.Path==missing).Availability==FavoriteAvailability.Missing,"Favorites missing path is unavailable without removing registration");
        Check((await first.LoadAsync()).Any(e=>e.Path==missing),"Favorites retains missing path in shared data");
        Directory.CreateDirectory(missing);rows=await FavoritesStore.CheckAvailabilityAsync(await first.LoadAsync(),false);
        Check(rows.Single(e=>e.Path==missing).Availability==FavoriteAvailability.Available,"Favorites path reappears when restored at same address");
        var renamed=Path.Combine(root,"renamed.png");File.Move(a,renamed);
        rows=await FavoritesStore.CheckAvailabilityAsync(await first.LoadAsync(),false);
        Check(rows.Single(e=>e.Path==a).Availability==FavoriteAvailability.Missing&&(await first.LoadAsync()).All(e=>e.Path!=renamed),"Favorites does not track file moves or renames");
        File.Move(renamed,a);rows=await FavoritesStore.CheckAvailabilityAsync(await first.LoadAsync(),false);
        Check(rows.Single(e=>e.Path==a).Availability==FavoriteAvailability.Available,"Favorite image returns when original path is restored");
        var network=new FavoriteEntry(@"\\stella-favorites.invalid\share\image.png",FavoriteKind.Image);var calls=0;
        var blocked=await FavoritesStore.CheckAvailabilityAsync([network],false,exists:_=>{calls++;return true;});
        Check(calls==0&&blocked[0].Availability==FavoriteAvailability.NetworkBlocked,"Favorites network restriction runs before any existence I/O");
        var allowed=await FavoritesStore.CheckAvailabilityAsync([network],true,exists:_=>{calls++;return true;});
        Check(calls==1&&allowed[0].Availability==FavoriteAvailability.Available,"Favorites explicit network permission permits availability probe");
        var denied=await FavoritesStore.CheckAvailabilityAsync([new(a,FavoriteKind.Image)],false,exists:_=>throw new UnauthorizedAccessException());
        Check(denied[0].Availability==FavoriteAvailability.Unavailable,"Favorites unavailable source does not abort remaining entries");
        var mismatch=await FavoritesStore.CheckAvailabilityAsync([new(a,FavoriteKind.Folder)],false);
        Check(mismatch[0].Availability==FavoriteAvailability.Missing,"Favorites distinguishes folders from physical files");
        var ephemeralPath=Path.Combine(root,"ephemeral.json");var ephemeral=new FavoritesStore(ephemeralPath,false);await ephemeral.SetAsync(a,FavoriteKind.Image,true);
        Check(!File.Exists(ephemeralPath)&&(await ephemeral.LoadAsync()).Length==1,"Nonpersistent viewer tests do not read or write personal favorites");
        var corrupt=Path.Combine(root,"corrupt.json");File.WriteAllText(corrupt,"broken");var corruptStore=new FavoritesStore(corrupt);
        await corruptStore.SetAsync(a,FavoriteKind.Image,true);
        Check(Directory.GetFiles(root,"corrupt.json.corrupt-*").Length==1,"Broken registration data is preserved before recovery");

        using(var canceled=new CancellationTokenSource()){
            canceled.Cancel();await Reject<OperationCanceledException>(()=>first.LoadAsync(canceled.Token),"Favorites load supports pre-cancellation");
        }
        using(var cancel=new CancellationTokenSource()){
            await Reject<OperationCanceledException>(()=>FavoritesStore.CheckAvailabilityAsync([new(a,FavoriteKind.Image),new(folder,FavoriteKind.Folder)],false,cancel.Token,_=>{cancel.Cancel();return true;}),"Favorites canceled availability scan does not publish partial rows");
        }
        var reads=0;using var done=new CancellationTokenSource();var readers=Enumerable.Range(0,2).Select(_=>Task.Run(async()=>{
            while(!done.IsCancellationRequested){await second.LoadAsync();Interlocked.Increment(ref reads);}
        })).ToArray();
        try{for(var i=0;i<30;i++)await first.SetAsync(a,FavoriteKind.Image,i%2==0);}
        finally{done.Cancel();}await Task.WhenAll(readers);
        Check(reads>0&&(await first.LoadAsync()).Any(e=>e.Path==folder),"Favorites shared reads and atomic replacements coexist");
        Check(!Directory.EnumerateFiles(root,"*.tmp").Any(),"Favorites atomic updates leave no temporary writes behind");
        await first.SetAsync(missing,FavoriteKind.Folder,false);
        Check(Directory.Exists(missing),"Removing a favorite does not remove the original folder");
        return count;
    }
}
