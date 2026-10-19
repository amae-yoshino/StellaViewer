using System.Windows;

namespace StellaViewer.App;

public partial class MainWindow
{
    private enum ZoomPreset { Fit, ActualSize }
    private sealed record ZoomReturnState(double Percent, double X, double Y, bool Automatic, bool FitViewport);
    private ZoomPreset? activeZoomPreset;
    private ZoomReturnState? zoomReturnState;

    private void ClearZoomPreset()
    {
        activeZoomPreset = null;
        zoomReturnState = null;
    }

    private void ToggleZoomPreset(ZoomPreset preset)
    {
        if (decoded is null) return;
        CancelPointer();
        if (activeZoomPreset == preset && zoomReturnState is { } previous) {
            ClearZoomPreset();
            automaticZoom = previous.Automatic;
            fitViewportZoom = previous.FitViewport;
            SetZoom(automaticZoom ? AutomaticZoom() : previous.Percent);
            Pan.X = previous.X; Pan.Y = previous.Y; ClampPan();
            return;
        }
        zoomReturnState = new(zoom, Pan.X, Pan.Y, automaticZoom, fitViewportZoom);
        activeZoomPreset = preset;
        fitViewportZoom = preset == ZoomPreset.Fit;
        automaticZoom = fitViewportZoom;
        Pan.X = Pan.Y = 0;
        SetZoom(fitViewportZoom ? AutomaticZoom() : 100);
    }

    private void FitViewportClicked(object sender, RoutedEventArgs e) => ToggleZoomPreset(ZoomPreset.Fit);
    private void ActualSizeClicked(object sender, RoutedEventArgs e) => ToggleZoomPreset(ZoomPreset.ActualSize);
}
