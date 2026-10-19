using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyUiSizingAsync(string sample, string report)
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        bool Near(double left, double right) => Math.Abs(left - right) < .05;
        async Task Settle(MainWindow window) { window.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
        var root = Path.Combine(Path.GetDirectoryName(sample)!, "ui-sizing-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var probe = new MainWindow(false, root) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, WatchFoldersEnabled = false };
        try {
            probe.Show(); await probe.OpenAsync(sample); await probe.activeDisplay; await Settle(probe);
            probe.automaticZoom = false; probe.SetZoom(100); var image = probe.model.Image; var width = probe.Picture.Width; var row = probe.model.ImageThumbnails.DetailRowHeight;
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { UiSize = new(125, 20) }); await Settle(probe);
            Check(Near(probe.FontSize, 20) && Near(probe.ZoomText.FontSize, 12 * 20d / 14), "UI font sizing covers inherited text and explicit bar text");
            Check(Near(probe.TopBars.LayoutTransform.Value.M11, 1.25) && Near(probe.LeftPane.LayoutTransform.Value.M11, 1.25), "Chrome scaling reaches bars and pane");
            Check(probe.Viewport.LayoutTransform.Value.IsIdentity && probe.Picture.LayoutTransform.Value.IsIdentity && Near(width, probe.Picture.Width) && Near(probe.zoom, 100) && ReferenceEquals(image, probe.model.Image), "Image remains at physical pixel zoom independently of UI scale");
            Check(probe.model.ImageThumbnails.DetailRowHeight >= row && probe.ZoomText.ActualHeight + .1 >= 22 * 20d / 14, $"Sufficient row and editor height for larger fonts: row={probe.model.ImageThumbnails.DetailRowHeight}, editor={probe.ZoomText.ActualHeight}, min={probe.ZoomText.MinHeight}");
            var outerLeftWidth = probe.MainGrid.ColumnDefinitions[0].ActualWidth;
            Check(Near(probe.CaptureSavedState().Layout.LeftPaneWidth, outerLeftWidth), "Scaled pane saves its outer width rather than transformed content width");
            probe.ToggleFullscreen(false); probe.UpdateLayout(); probe.UpdateFullscreenBars(new Point(1, 1));
            Check(ReferenceEquals(probe.FullscreenTopHost.Child, probe.TopBars) && Near(probe.TopBars.LayoutTransform.Value.M11, 1.25), "Fullscreen overlays retain sizing after reparenting");
            Check(Near(probe.FullscreenLeftHost.Width, outerLeftWidth), "Scaled pane retains outer width in fullscreen");
            probe.ToggleFullscreen(false);
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { UiSize = new(150, 24), Dark = true }); await Settle(probe);
            var dialog = new SettingsWindow(probe.CurrentPreferences) { Owner = probe, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            probe.CopySettingsTheme(dialog);
            try {
                dialog.Show(); dialog.UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
                Check(Near(dialog.FontSize, 24) && Near(((FrameworkElement)dialog.Content).LayoutTransform.Value.M11, 1.5), "Settings window font and UI scale");
                var heading = Descendants(dialog).OfType<TextBlock>().First(text => text.Text == "設定");
                Check(Near(heading.FontSize, 24 * 24d / 14), $"Explicit dialog heading scales with font size: {heading.FontSize}");
                Check(Descendants(dialog).OfType<ScrollViewer>().Any(scroll => scroll.Content is System.Windows.Controls.Primitives.TabPanel), "Settings tab headers can scroll at large sizes");
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog); SaveRender(bitmap, report + ".large-settings.png");
                dialog.ResetValue("ui-size"); Check(dialog.Values.UiSize == new UiSizeSettings(), "UI sizing can reset independently");
            }
            finally { dialog.Close(); }
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { UiSize = new() }); await Settle(probe);
            Check(Near(probe.FontSize, 14) && Near(probe.ZoomText.FontSize, 12) && Near(probe.ZoomText.MinHeight, 22) && Near(probe.TopBars.LayoutTransform.Value.M11, 1), "Reset restores sizes without compounding");
            var prefsPath = Path.Combine(root, "viewer-preferences.json"); await PreferencesStore.SaveChangeAsync(uiScalePercent: 120, uiFontSize: 18, filePath: prefsPath);
            var restored = new MainWindow(true, root) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000, ConstrainStartupWindowToScreen = false };
            try { restored.Show(); await Settle(restored); Check(restored.CurrentPreferences.UiSize == new UiSizeSettings(120, 18) && Near(restored.FontSize, 18), "UI sizes restore on a fresh window"); }
            finally { restored.Close(); await restored.closePersistence; await restored.TemporaryCleanup; }
        }
        finally { probe.Close(); await probe.TemporaryCleanup; }
    }
}
