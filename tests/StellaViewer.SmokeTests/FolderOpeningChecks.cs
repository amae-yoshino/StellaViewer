using System.Text.Json;
using StellaViewer.Infrastructure;

internal static class FolderOpeningChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"folder-opening",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var file=Path.Combine(root,"positions.json");var first=new FolderImagePositionStore(file);var second=new FolderImagePositionStore(file);var count=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);count++;Console.WriteLine("PASS: "+message);}
        var a=Path.Combine(root,"a");var b=Path.Combine(root,"b");var a1=Path.Combine(a,"1.png");var a2=Path.Combine(a,"2.png");var b1=Path.Combine(b,"1.png");
        Check(await first.LoadAsync(a) is null,"New folder has no remembered image");
        await Task.WhenAll(first.SaveAsync(a,a1),second.SaveAsync(b,b1));
        Check(await first.LoadAsync(a)==a1&&await first.LoadAsync(b)==b1,"Concurrent folder position changes preserve unrelated folders");
        await second.SaveAsync(a,a2);Check(await first.LoadAsync(a.ToUpperInvariant())==a2,"Folder position replacement uses Windows path identity");
        try{await first.SaveAsync(a,b1);throw new Exception("Mismatching image accepted");}catch(ArgumentException){Check(await first.LoadAsync(a)==a2,"Unrelated image cannot overwrite folder position");}
        var corrupt=Path.Combine(root,"corrupt.json");File.WriteAllText(corrupt,"broken");
        try{await new FolderImagePositionStore(corrupt).SaveAsync(a,a1);throw new Exception("Corrupt positions overwritten");}catch(JsonException){Check(File.ReadAllText(corrupt)=="broken","Corrupt position data is preserved instead of overwritten");}
        var preferences=Path.Combine(root,"preferences.json");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(filePath:preferences,folderOpeningMode:1),PreferencesStore.SaveChangeAsync(filePath:preferences,dark:true));
        var settings=PreferencesStore.Load(preferences);Check(settings.FolderOpeningMode==1&&settings.Dark,"Folder opening setting merges with concurrent theme update");
        Check(new ViewerPreferences().FolderOpeningMode==0,"Default folder opening uses first image");
        var stateRoot=Path.Combine(root,"state");var layout=new WindowLayout(TreeFraction:.6,TreeVisible:false);var view=new LastView(a,a1,b);
        await ViewerStateStore.SaveAsync(layout,view,stateRoot);var saved=ViewerStateStore.Load(stateRoot);
        Check(!saved.Layout.TreeVisible&&saved.Layout.TreeFraction==.6&&saved.View.LocationFolder==b,"Tree visibility and independent browser location survive session storage");
        return count;
    }
}
