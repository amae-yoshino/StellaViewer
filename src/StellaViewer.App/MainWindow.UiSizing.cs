using System.Windows;
using System.Windows.Media;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private UiSizeSettings uiSize = new();
    private int listFontSize = 14;
    private void ApplyUiSizing(UiSizeSettings? value)
    {
        uiSize = (value ?? new()).Normalize();
        Resources["UiInfoRailWidth"] = 48 * Math.Max(1, uiSize.FontSize / 14d);
        Resources["UiInfoButtonWidth"] = 40 * Math.Max(1, uiSize.FontSize / 14d);
        Resources["UiZoomWidth"] = 54 * Math.Max(1, uiSize.FontSize / 14d);
        Resources["UiPageWidth"] = 58 * Math.Max(1, uiSize.FontSize / 14d);
        UiSizing.Apply(this, uiSize);
        var factor = uiSize.ScalePercent / 100d;
        foreach (var panel in new FrameworkElement[] { TopBars, LeftPane, InfoPanel, BottomBar, ViewerToolbar }) panel.LayoutTransform = new ScaleTransform(factor, factor);
        foreach (var list in new FrameworkElement[] { ImageList, TileList, Locations, LocationTileList }) UiSizing.ApplyList(list, listFontSize);
        model.UiFontFactor = listFontSize / 14d;
        SyncTiles(true); SyncLocationTiles(true);
        foreach (Window dialog in OwnedWindows) UiSizing.ConfigureDialog(dialog, uiSize);
    }
}
