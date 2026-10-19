using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class SettingsWindow
{
    private void SetCache(ImageCacheSettings? value)
    {
        var limits = (value ?? new()).Normalize();
        MemoryCacheMiB.Text = limits.MemoryMiB.ToString(); DiskCacheMiB.Text = limits.DiskMiB.ToString(); PrefetchPages.Text = limits.PrefetchPages.ToString();
    }
    private ImageCacheSettings ReadCache()
    {
        if (!int.TryParse(MemoryCacheMiB.Text, out var memory) || memory < 0
            || !int.TryParse(DiskCacheMiB.Text, out var disk) || disk < 0 || !int.TryParse(PrefetchPages.Text, out var pages) || pages < 0)
            throw new InvalidOperationException("キャッシュ容量と先読み枚数は0以上の整数で入力してください。");
        return new(memory, disk, pages);
    }
}
