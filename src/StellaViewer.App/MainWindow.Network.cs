using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool CanReadCurrentFolder()
    {
        if (model.Folder.Length == 0) return true;
        try { NetworkAccess.Validate(BrowsePath.PhysicalPath(model.Folder), model.AllowNetwork); return true; }
        catch (NotSupportedException error) { model.Status = error.Message; return false; }
    }
    private void ApplyNetworkAccess(bool allowed)
    {
        ++openRequestVersion;
        var networkFolder = model.Folder.Length > 0 && NetworkAccess.IsNetworkPath(BrowsePath.PhysicalPath(model.Folder));
        if (!allowed && networkFolder) {
            StopSlideshow();
            if(!metadataWork.IsCompleted)model.MetadataText="ネットワーク閲覧が無効のため、追加の情報読み込みを停止しました。";
            CancelMetadataRead();
            CancelPrefetch();
            StopFolderMonitor(); browse.Cancel(); search.Cancel(); display.Cancel(); locationListing.Cancel();
            loading = loadingLocations = false;
        }
        model.AllowNetwork = allowed;
        if(!allowed&&model.LocationFolder.Length>0&&NetworkAccess.IsNetworkPath(BrowsePath.PhysicalPath(model.LocationFolder)))locationListing.Cancel();
        if(favoritesStore is not null)FavoriteAccessChanged();
        if(directoryShortcutStore is not null)DirectoryShortcutAccessChanged();
        if (networkFolder)
            model.Status = allowed ? "ネットワーク閲覧を許可しました。再読み込みで再開してください。"
                : "ネットワーク閲覧を無効にしました。表示中の画像を保持し、監視と追加読み込みを停止しました。";
    }
}
