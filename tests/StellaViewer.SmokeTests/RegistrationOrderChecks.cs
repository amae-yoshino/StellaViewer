using System.Text.Json;
using StellaViewer.Infrastructure;

internal static class RegistrationOrderChecks
{
    public static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"registration-order",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var a=Path.Combine(root,"item2");var b=Path.Combine(root,"item10");var c=Path.Combine(root,"item30");var d=Path.Combine(root,"item40");
        var checks=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);Console.WriteLine("PASS: "+label);checks++;}
        async Task Reject(Func<Task> action,string label){try{await action();}catch(InvalidOperationException){Check(true,label);return;}throw new InvalidOperationException(label);}
        var favoritePath=Path.Combine(root,"favorites.json");
        await File.WriteAllTextAsync(favoritePath,JsonSerializer.Serialize(new{Version=1,Entries=new[]{new FavoriteEntry(c,FavoriteKind.Folder),new FavoriteEntry(b,FavoriteKind.Folder),new FavoriteEntry(a,FavoriteKind.Folder)}}));
        var favorites=new FavoritesStore(favoritePath);var peer=new FavoritesStore(favoritePath);
        async Task FavoriteOrder(string label,params string[] expected)=>Check((await favorites.LoadAsync()).Select(e=>e.Path).SequenceEqual(expected),label);
        await FavoriteOrder("Legacy favorites keep natural name order",a,b,c);
        await favorites.MoveAsync(b,-1);await FavoriteOrder("Favorite moves up",b,a,c);
        Check((await new FavoritesStore(favoritePath).LoadAsync()).Select(e=>e.Path).SequenceEqual(new[]{b,a,c}),"Favorite manual order survives a new store");
        await peer.SetAsync(d,FavoriteKind.Pdf,true);await favorites.MoveAsync(a,1);await FavoriteOrder("Favorite move merges peer additions",b,c,a,d);
        await peer.SetAsync(b.ToUpperInvariant(),FavoriteKind.Folder,true);
        Check((await favorites.LoadAsync()).Select(e=>e.Path).SequenceEqual(new[]{b,c,a,d},StringComparer.OrdinalIgnoreCase),"Duplicate favorite registration preserves position");
        await favorites.MoveAsync(b,-1);await favorites.MoveAsync(d,1);await FavoriteOrder("Favorite endpoints do not wrap",b.ToUpperInvariant(),c,a,d);
        await peer.SetAsync(c,FavoriteKind.Folder,false);await Reject(()=>favorites.MoveAsync(c,-1),"Removed favorite is not resurrected by stale move");
        await FavoriteOrder("Favorite failed move preserves peer removal",b.ToUpperInvariant(),a,d);
        var linksPath=Path.Combine(root,"directory-shortcuts.json");
        await File.WriteAllTextAsync(linksPath,JsonSerializer.Serialize(new{Version=1,Entries=new[]{new DirectoryShortcut(c,"item30"),new DirectoryShortcut(b,"item10"),new DirectoryShortcut(a,"item2")}}));
        var links=new DirectoryShortcutStore(linksPath);var linksPeer=new DirectoryShortcutStore(linksPath);
        async Task LinkOrder(string label,params string[] expected)=>Check((await links.LoadAsync()).Select(e=>e.Path).SequenceEqual(expected),label);
        await LinkOrder("Legacy folder links keep natural name order",a,b,c);
        await links.MoveAsync(b,-1);await LinkOrder("Folder link moves up",b,a,c);
        await linksPeer.RenameAsync(b,"ZZZ");await linksPeer.AddAsync(d);await links.MoveAsync(a,1);
        await LinkOrder("Folder link move merges peer rename and addition",b,c,a,d);
        Check((await links.LoadAsync())[0].Name=="ZZZ","Renaming preserves manual position and new name");
        await linksPeer.AddAsync(b.ToUpperInvariant());await LinkOrder("Duplicate folder link retains position",b,c,a,d);
        Check((await new DirectoryShortcutStore(linksPath).LoadAsync()).Select(e=>e.Path).SequenceEqual(new[]{b,c,a,d}),"Folder link order survives a new store");
        await links.MoveAsync(b,-1);await links.MoveAsync(d,1);await LinkOrder("Folder link endpoints do not wrap",b,c,a,d);
        await linksPeer.RemoveAsync(c);await Reject(()=>links.MoveAsync(c,-1),"Removed folder link is not resurrected by stale move");
        await LinkOrder("Folder link failed move preserves peer removal",b,a,d);
        var memoryFavorites=new FavoritesStore(persist:false);var memoryLinks=new DirectoryShortcutStore(persist:false);
        foreach(var path in new[]{c,a,b}){await memoryFavorites.SetAsync(path,FavoriteKind.Folder,true);await memoryLinks.AddAsync(path);}
        await memoryFavorites.MoveAsync(a,1);await memoryLinks.MoveAsync(a,1);
        Check((await memoryFavorites.LoadAsync()).Select(e=>e.Path).SequenceEqual(new[]{b,a,c}),"In-memory favorites retain manual order");
        Check((await memoryLinks.LoadAsync()).Select(e=>e.Path).SequenceEqual(new[]{b,a,c}),"In-memory folder links retain manual order");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{await links.MoveAsync(a,-1,cancelled.Token);throw new InvalidOperationException("Cancellation ignored");}catch(OperationCanceledException){Check(true,"Cancelled move leaves order intact");}
        await LinkOrder("Cancelled move has no persistent side effects",b,a,d);
        return checks;
    }
}
