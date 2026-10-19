using System.Text.Json;
using StellaViewer.Infrastructure;

internal static class RecentLocationChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"recent-locations",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var file=Path.Combine(root,"recent-locations.json");var first=new RecentLocationStore(file);var second=new RecentLocationStore(file);var count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        async Task Reject<T>(Func<Task> action,string label)where T:Exception{try{await action();}catch(T){Check(true,label);return;}throw new Exception(label+" was accepted");}
        var a=Path.Combine(root,"gallery");var b=Path.Combine(root,"books");
        Check((await first.LoadAsync()).Length==0,"Recent places start empty");
        await Task.WhenAll(first.RecordAsync(a),second.RecordAsync(b));var rows=await first.LoadAsync();
        Check(rows.Length==2&&rows.Any(e=>e.Location==a)&&rows.Any(e=>e.Location==b),"Concurrent history updates preserve both places");
        await first.RecordAsync(a.ToUpperInvariant()+Path.DirectorySeparatorChar);rows=await second.LoadAsync();
        Check(rows.Length==2&&RecentLocationStore.Key(rows[0].Location)==RecentLocationStore.Key(a),"Reopening same Windows folder moves it to front without duplication");
        var zip=Path.Combine(root,"book.cbz");var archive=new ArchiveLocation(zip);
        await first.RecordAsync(archive.Identity);await second.RecordAsync(new ArchiveLocation(zip.ToUpperInvariant()).Identity);
        Check((await first.LoadAsync()).Length==3,"Archive physical source case does not duplicate history");
        var upper=new ArchiveLocation(zip,"A");var lower=new ArchiveLocation(zip,"a");await first.RecordAsync(upper.Identity);await first.RecordAsync(lower.Identity);
        Check(RecentLocationStore.Key(upper.Identity)!=RecentLocationStore.Key(lower.Identity),"Archive internal folder identities remain case sensitive");
        var nested=new ArchiveLocation(zip,"inner.zip",false);var inside=new ArchiveLocation(nested.Identity,"chapter");
        await first.RecordAsync(inside.Identity);
        Check(new RecentLocation(inside.Identity,DateTimeOffset.UtcNow).DisplayPath.Contains("inner.zip")&&!new RecentLocation(inside.Identity,default).DisplayPath.Contains("stella-"),"Nested history displays human readable hierarchy");
        await Reject<ArgumentException>(()=>first.RecordAsync(new ArchiveLocation(zip,"image.png",false).Identity),"Individual archive image cannot be stored as recent place");
        var pdf=new PdfLocation(Path.Combine(root,"book.pdf"),12);await first.RecordAsync(pdf.Identity);rows=await first.LoadAsync();
        Check(PdfLocation.Parse(rows[0].Location) is {Page:null}&&rows[0].Name=="book.pdf"&&rows[0].Kind=="PDF","PDF history records document without page number");
        var archivePdf=new PdfLocation(new ArchiveLocation(zip,"book.pdf",false).Identity,3);await first.RecordAsync(archivePdf.Identity);
        Check(PdfLocation.Parse((await first.LoadAsync())[0].Location) is {Page:null}&&new RecentLocation(archivePdf.Identity,default).Name=="book.pdf","Archive PDF history records document without individual page");
        Check(RecentLocationStore.NormalizePlace(Path.GetPathRoot(root)!)==Path.GetPathRoot(root),"Recent drive root retains trailing separator");
        await Reject<ArgumentException>(()=>first.RecordAsync("relative"),"Recent place requires absolute or structured location");
        var network=@"\\stella-history.invalid\share";await first.RecordAsync(network);
        Check((await first.LoadAsync())[0].Location==network,"History reads and writes network addresses without accessing target");
        var old=new RecentLocationStore(Path.Combine(root,"bounded.json"));
        for(var i=0;i<205;i++)await old.RecordAsync(Path.Combine(root,"folder"+i));var bounded=await old.LoadAsync();
        Check(bounded.Length==200&&bounded[0].Location.EndsWith("folder204")&&bounded[^1].Location.EndsWith("folder5"),"Recent history retains newest 200 places in visit order");
        await old.RecordAsync(bounded[^1].Location);bounded=await old.LoadAsync();
        Check(bounded.Length==200&&bounded[0].Location.EndsWith("folder5"),"Reopening oldest retained place does not grow history");
        var corrupt=Path.Combine(root,"corrupt.json");File.WriteAllText(corrupt,"broken");
        await new RecentLocationStore(corrupt).RecordAsync(a);
        Check(Directory.GetFiles(root,"corrupt.json.corrupt-*").Length==1,"Broken registration data is preserved before recovery");

        using(var cancel=new CancellationTokenSource()){cancel.Cancel();await Reject<OperationCanceledException>(()=>first.RecordAsync(a,cancel.Token),"Recent history supports canceled updates");}
        var reads=0;using var stop=new CancellationTokenSource();var reader=Task.Run(async()=>{while(!stop.IsCancellationRequested){await second.LoadAsync();Interlocked.Increment(ref reads);}});
        try{for(var i=0;i<20;i++)await first.RecordAsync(a);}finally{stop.Cancel();}await reader;
        Check(reads>0&&(await first.LoadAsync()).Any(e=>e.Location==network),"History readers coexist with atomic replacements without losing other places");
        Check(!Directory.EnumerateFiles(root,"*.tmp").Any(),"History leaves no temporary write files");
        var ephemeralPath=Path.Combine(root,"ephemeral.json");var ephemeral=new RecentLocationStore(ephemeralPath,false);await ephemeral.RecordAsync(a);
        Check(!File.Exists(ephemeralPath)&&(await ephemeral.LoadAsync()).Length==1,"Isolated nonpersistent history avoids personal files");
        return count;
    }
}
