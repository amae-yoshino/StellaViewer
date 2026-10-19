using StellaViewer.Infrastructure;
internal static class RecycleChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"recycle",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var service=new RecycleService();var count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        var image=Path.Combine(root,"画像.png");File.WriteAllText(image,"pixels");var folder=Path.Combine(root,"folder.ext");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"inner.txt"),"inside");
        var deleted=await service.DeleteAsync([image,folder]);
        Console.WriteLine(string.Join(Environment.NewLine,deleted.Select(item=>item.Path+": "+item.Error)));
        try{
            Check(deleted.Length==2&&deleted.All(item=>item.Error is null)&&!File.Exists(image)&&!Directory.Exists(folder)&&service.UndoCount==2,"Recycle file and whole folder with per-item undo identifiers");
            var restored=await service.RestoreAsync();Console.WriteLine(string.Join(Environment.NewLine,restored.Select(item=>item.Path+": "+item.Error)));
            Check(restored.All(item=>item.Error is null)&&File.ReadAllText(image)=="pixels"&&File.ReadAllText(Path.Combine(folder,"inner.txt"))=="inside"&&service.UndoCount==0,"Restore exact file and folder contents from Windows Recycle Bin");
        }finally{if(service.UndoCount>0)await service.RestoreAsync();}
        var created=new DateTime(2014,2,3,4,5,6,DateTimeKind.Utc);var modified=new DateTime(2020,5,6,7,8,10,DateTimeKind.Utc);File.SetCreationTimeUtc(image,created);File.SetLastWriteTimeUtc(image,modified);
        try{
            await service.DeleteAsync([image,folder]);File.WriteAllText(image,"keep new original");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"keep.txt"),"keep folder");
            var collision=await service.RestoreAsync();
            Check(collision.All(item=>item.Error is null)&&File.ReadAllText(image)=="keep new original"&&File.ReadAllText(Path.Combine(root,"画像 (1).png"))=="pixels","Undo uses numbered file name and does not overwrite a new original");
            Check(File.GetCreationTimeUtc(Path.Combine(root,"画像 (1).png"))==created&&File.GetLastWriteTimeUtc(Path.Combine(root,"画像 (1).png"))==modified,"Recycle and undo preserve source creation and modification times");
            Check(Directory.Exists(Path.Combine(root,"folder.ext (1)"))&&File.ReadAllText(Path.Combine(folder,"keep.txt"))=="keep folder","Folder undo collision preserves the entire existing folder and keeps dotted folder name");
        }finally{if(service.UndoCount>0)await service.RestoreAsync();}
        var locked=Path.Combine(root,"locked.png");File.WriteAllText(locked,"locked");var next=Path.Combine(root,"next.png");File.WriteAllText(next,"next");
        try{
            using(var handle=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.None)){
                var partial=await service.DeleteAsync([Path.Combine(root,"missing.png"),locked,next]);
                Check(partial.Count(item=>item.Error is not null)==2&&partial.Count(item=>item.Error is null)==1&&File.Exists(locked),"Missing and locked deletes fail while other selected items recycle");
            }
            var failed=await service.DeleteAsync([Path.Combine(root,"missing-again.png")]);
            Check(failed.All(item=>item.Error is not null)&&service.UndoCount==1,"Entirely failed deletion preserves previous undo batch");
        }finally{if(service.UndoCount>0)await service.RestoreAsync();}
        var parent=Path.Combine(root,"parent");Directory.CreateDirectory(parent);var child=Path.Combine(parent,"child.png");File.WriteAllText(child,"child");
        try{var overlap=await service.DeleteAsync([child,parent,parent]);Check(overlap.Length==1&&service.UndoCount==1,"Overlapping parent and child selections recycle the parent once");}
        finally{if(service.UndoCount>0)await service.RestoreAsync();}
        var retryFolder=Path.Combine(root,"retry");Directory.CreateDirectory(retryFolder);var retryFile=Path.Combine(retryFolder,"retry.png");File.WriteAllText(retryFile,"retry");
        try{
            await service.DeleteAsync([retryFile]);Directory.Move(retryFolder,retryFolder+"-moved");
            var retry=await service.RestoreAsync();Check(retry.Single().Error is not null&&service.UndoCount==1,"Failed undo retains item for retry when parent folder is missing");
            Directory.Move(retryFolder+"-moved",retryFolder);var retried=await service.RestoreAsync();Check(retried.Single().Error is null&&File.ReadAllText(retryFile)=="retry","Undo can retry after the destination becomes available");
        }finally{if(service.UndoCount>0)await service.RestoreAsync();}
        var prohibited=await service.DeleteAsync([new ArchiveLocation(Path.Combine(root,"a.zip"),"x.png",false).Identity,new PdfLocation(Path.Combine(root,"a.pdf"),1).Identity,Path.GetPathRoot(root)!,@"\\stella-recycle.invalid\share\file.png"]);
        Check(prohibited.Length==4&&prohibited.All(item=>item.Error is not null)&&service.UndoCount==0,"Reject virtual items, drive roots and disabled network before Shell operations");
        var prefs=Path.Combine(root,"preferences.json");File.WriteAllText(prefs,"{}");Check(!PreferencesStore.Load(prefs).DeleteEnabled,"Deletion is disabled by default for new and existing settings");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(filePath:prefs,deleteEnabled:true),PreferencesStore.SaveChangeAsync(filePath:prefs,dark:true));
        Check(PreferencesStore.Load(prefs) is{DeleteEnabled:true,Dark:true},"Deletion setting persists independently of concurrent theme change");
        return count;
    }
}
