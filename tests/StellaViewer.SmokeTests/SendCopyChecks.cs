using StellaViewer.Infrastructure;

internal static class SendCopyChecks
{
    private sealed class CallbackProgress(Action<string> action):IProgress<string>{public void Report(string value)=>action(value);}
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"send-copy",Guid.NewGuid().ToString("N"));
        var source=Path.Combine(root,"source");var target=Path.Combine(root,"target");Directory.CreateDirectory(source);Directory.CreateDirectory(target);
        var count=0;void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        var image=Path.Combine(source,"画像 space.png");await File.WriteAllTextAsync(image,"original pixels");
        var archive=Path.Combine(source,"book.cbz");await File.WriteAllTextAsync(archive,"archive bytes");
        var pdf=Path.Combine(source,"book.pdf");await File.WriteAllTextAsync(pdf,"PDF bytes");
        var created=new DateTime(2015,2,3,4,5,6,DateTimeKind.Utc);
        var modified=new DateTime(2020,6,7,8,9,10,DateTimeKind.Utc);
        var accessed=new DateTime(2023,8,9,10,11,12,DateTimeKind.Utc);
        foreach(var path in new[]{image,archive,pdf}){File.SetCreationTimeUtc(path,created);File.SetLastWriteTimeUtc(path,modified);File.SetLastAccessTimeUtc(path,accessed);}
        bool SameTimes(string path)=>File.GetCreationTimeUtc(path)==created&&File.GetLastWriteTimeUtc(path)==modified&&File.GetLastAccessTimeUtc(path)==accessed;
        var copied=await SendCopyService.CopyAsync([image,archive,pdf,image],target);
        Check(copied.Succeeded==3&&copied.Failed==0&&copied.Renamed==0,"Send copies distinct image, archive and PDF files");
        Check(copied.Items.All(item=>SameTimes(item.Destination!)),"Image, archive and PDF copies preserve creation, modification and access times");
        Check(await File.ReadAllTextAsync(Path.Combine(target,"画像 space.png"))=="original pixels"&&await File.ReadAllTextAsync(image)=="original pixels","Send preserves exact bytes and leaves originals unchanged");
        await File.WriteAllTextAsync(Path.Combine(target,"画像 space (1).png"),"keep numbered");
        var renamed=await SendCopyService.CopyAsync([image],target);
        Check(SameTimes(renamed.Items[0].Destination!),"Numbered copies preserve original file timestamps");
        Check(renamed.Renamed==1&&Path.GetFileName(renamed.Items[0].Destination)=="画像 space (2).png"&&await File.ReadAllTextAsync(Path.Combine(target,"画像 space (1).png"))=="keep numbered","Send selects next free numbered name without overwriting existing files");
        Directory.CreateDirectory(Path.Combine(target,"book (1).pdf"));
        var dirCollision=await SendCopyService.CopyAsync([pdf],target);
        Check(Path.GetFileName(dirCollision.Items[0].Destination)=="book (2).pdf","Directory at destination name is also protected");
        var mixed=await SendCopyService.CopyAsync([Path.Combine(source,"missing.png"),source,archive],target);
        Check(mixed.Succeeded==1&&mixed.Failed==2&&mixed.Items[0].Error is not null,"Missing source and folder are reported while remaining copies continue");
        var concurrent=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>SendCopyService.CopyAsync([image],target)));
        Check(concurrent.All(r=>r.Succeeded==1)&&concurrent.Select(r=>r.Items[0].Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count()==6,"Concurrent copies reserve distinct names atomically");
        var same=await SendCopyService.CopyAsync([image],source);
        Check(same.Renamed==1&&await File.ReadAllTextAsync(image)=="original pixels","Copying into source folder makes numbered copy without changing original");
        Check(File.GetCreationTimeUtc(image)==created&&File.GetLastWriteTimeUtc(image)==modified,"Sending leaves source creation and modification times unchanged");
        var virtualPath=new ArchiveLocation(archive,"page.png").Identity;
        var blocked=await SendCopyService.CopyAsync([virtualPath,@"\\stella-send.invalid\share\page.png",pdf],target);
        Check(blocked.Failed==2&&blocked.Succeeded==1&&blocked.Items[1].Error!.Contains("ネットワーク"),"Virtual items and disabled network sources are rejected before access while local copies continue");
        try{await SendCopyService.CopyAsync([image],@"\\stella-send.invalid\share");throw new Exception("Network target accepted");}
        catch(NotSupportedException){Check(true,"Disabled network destination is rejected before existence check");}
        var lockPath=Path.Combine(source,"locked.png");await File.WriteAllTextAsync(lockPath,"locked");
        using(var locked=new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
            var failed=await SendCopyService.CopyAsync([lockPath,pdf],target);
            Check(failed.Failed==1&&failed.Succeeded==1,"Locked source failure does not abort subsequent copies");
        }
        using var cancel=new CancellationTokenSource();var progressCount=0;
        var canceled=await SendCopyService.CopyAsync([image,archive,pdf],target,token:cancel.Token,progress:new CallbackProgress(_=>{if(++progressCount==2)cancel.Cancel();}));
        Check(canceled.Canceled&&canceled.Succeeded==1&&canceled.Failed==0,"Cancel retains completed copy and skips remaining files");
        Check(!Directory.EnumerateFiles(target,".stellaviewer-*.tmp").Any(),"Temporary copies are removed after failure and cancellation");
        return count;
    }
}
