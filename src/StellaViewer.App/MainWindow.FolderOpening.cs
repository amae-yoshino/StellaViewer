using System.IO;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private FolderImagePositionStore folderPositions=null!;
    private readonly Dictionary<string,string> folderPositionSnapshot=new(StringComparer.OrdinalIgnoreCase);
    private Task folderPositionSave=Task.CompletedTask;
    private int folderOpeningMode;
    private bool selectFirstAfterEnumeration,selectingInitialImage;
    private void InitializeFolderOpening(ViewerPreferences preferences)
    {
        folderOpeningMode=preferences.FolderOpeningMode==1?1:0;
        folderPositions=new(storageDirectory is null?null:Path.Combine(storageDirectory,"folder-image-positions.json"),usePreferences||storageDirectory is not null);
    }
    private void RememberFolderImage()
    {
        if(!recordViewingState||folderOpeningMode!=1||folderPositions is null||model.Folder.Length==0||BrowsePath.IsVirtual(model.Folder)||ImageList.SelectedItem is not FileEntry image||BrowsePath.IsVirtual(image.Path))return;
        var folder=model.Folder;var path=image.Path;
        if(folderPositionSnapshot.GetValueOrDefault(folder)==path)return;
        folderPositionSnapshot[folder]=path;
        async Task Save(Task previous){await previous;try{await folderPositions.SaveAsync(folder,path, () => !usePreferences || PreferencesStore.Load(preferenceFilePath).RecordViewingState);}catch(Exception error){if(!closed)model.Status="フォルダの閲覧位置を保存できません："+error.Message;}}
        folderPositionSave=Save(folderPositionSave);
    }
    private async Task<string?> RememberedFolderImageAsync(string folder)
    {
        if(!recordViewingState||folderOpeningMode!=1)return null;
        if(folderPositionSnapshot.TryGetValue(folder,out var path))return path;
        try{await folderPositionSave;return await folderPositions.LoadAsync(folder);}
        catch(Exception error){model.Status="フォルダの閲覧位置を読み込めません："+error.Message;return null;}
    }
}
