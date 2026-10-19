using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private void AppendStoredPreviewNotices()
    {
        void Append(FileEntry entry,DecodedImage? info) {
            if (info is null || !entry.Name.EndsWith(".clip",StringComparison.OrdinalIgnoreCase)) return;
            var same = info.Width == info.OriginalWidth && info.Height == info.OriginalHeight;
            var rotated = info.Width == info.OriginalHeight && info.Height == info.OriginalWidth;
            var smaller = !same && !rotated && info.Width <= info.OriginalWidth && info.Height <= info.OriginalHeight;
            var notice = $"CLIP保存済み画像：{info.Width}×{info.Height}px"
                + (same || rotated ? "" : smaller ? "（元画像より縮小されています）" : "（元画像と異なる寸法です）");
            model.Status += "  " + notice;
            model.Details += $"\n\n{entry.Name}\n{notice}\nレイヤーの再合成は行っていません。100%は保存済み画像の画素等倍です。";
        }
        if (spreadEnabled) {
            if (firstPage is not null) Append(firstPage.Entry,firstPage.Info);
            if (secondPage is not null) Append(secondPage.Entry,secondPage.Info);
        }
        else if (displayedEntry is not null) Append(displayedEntry,decoded);
    }
}
