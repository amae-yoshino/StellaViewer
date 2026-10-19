using System.Windows;
using System.Windows.Controls;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyZoomPresetTogglesAsync()
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void ManualView()
        {
            ClearZoomPreset(); Rotation.Angle = 0; automaticZoom = false;
            decoded = new(2000, 1200, 2000, 1200, []);
            SetZoom(300); Pan.X = 40; Pan.Y = 30; ClampPan();
        }
        foreach (var button in new[] { FitViewportButton, ActualSizeButton }) {
            ManualView(); Click(button);
            Check(Pan.X == 0 && Pan.Y == 0, "Preset centers image");
            Click(button);
            Check(zoom == 300 && Pan.X == 40 && Pan.Y == 30 && !automaticZoom,
                "Second click restores manual zoom and pan for " + button.Name);
            Check(activeZoomPreset is null && zoomReturnState is null, "Restored preset clears saved state");
            Click(button); Click(button);
            Check(zoom == 300 && Pan.X == 40 && Pan.Y == 30, "Preset supports repeated toggle cycles");
        }
        Check(ActualSizeButton.Content.ToString() == "100%表示", "Actual size label is 100%表示");
        ManualView(); Click(FitViewportButton); var fit = zoom; Click(ActualSizeButton); Click(ActualSizeButton);
        Check(Math.Abs(zoom - fit) < .001 && automaticZoom && fitViewportZoom,
            "Switching presets and toggling back restores the immediately preceding fit mode");
        ManualView(); Click(ActualSizeButton); Click(FitViewportButton); Click(FitViewportButton);
        Check(zoom == 100 && !automaticZoom, "Switching presets can restore preceding actual size");
        ManualView(); ResetZoom(); var initial = zoom; Click(ActualSizeButton); Click(ActualSizeButton);
        Check(Math.Abs(zoom - initial) < .001 && automaticZoom && !fitViewportZoom,
            "Preset restores automatic initial sizing");
        Width += 40; UpdateLayout();
        Check(Math.Abs(zoom - AutomaticZoom()) < .001, "Restored automatic mode follows resize");
        ManualView(); Click(FitViewportButton); ZoomSlider.Value = Math.Log(2);
        Check(activeZoomPreset is null && zoomReturnState is null && Math.Abs(zoom - 200) < .001,
            "Manual slider adjustment discards old preset return state");
        Click(FitViewportButton); Click(FitViewportButton);
        Check(Math.Abs(zoom - 200) < .001, "Next toggle restores latest manual zoom");
        Click(ActualSizeButton); ZoomText.IsReadOnly = false; ZoomText.Text = "175"; CommitZoom();
        Check(activeZoomPreset is null && zoom == 175, "Numeric zoom input discards old preset state");
        ManualView(); Click(ActualSizeButton); ResetZoom();
        Check(activeZoomPreset is null && zoomReturnState is null, "Initial reset discards old preset state");
        Click(FitViewportButton); RotateRight(this, new RoutedEventArgs());
        Check(activeZoomPreset is null && zoomReturnState is null, "Rotation reset discards old preset state");
        ManualView(); Click(FitViewportButton); await LoadSelectedAsync();
        Check(activeZoomPreset is null && zoomReturnState is null, "Loading an image discards old preset state");
        Check(automaticZoom && !fitViewportZoom, "New image retains initial display behavior");
    }
}
