using System.Windows;
using System.Windows.Input;
using StellaViewer.Core;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyDragAsync()
    {
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
        var saved = mouseSettings;
        void Gesture(Point end, MouseButton button = MouseButton.Left) { BeginPointer(new(100, 100), button); UpdatePointer(end); EndPointer(); }
        try {
            mouseSettings = new(); ResetZoom();
            var index = ImageList.SelectedIndex;
            Gesture(new(0, 100));
            Check(ImageList.SelectedIndex == index && Pan.X == 0 && Pan.Y == 0, "Disabled drag paging neither pans fitted image nor invokes click");
            mouseSettings = mouseSettings with { DragPageEnabled = true };
            Gesture(new(41, 100));
            Check(ImageList.SelectedIndex == index, "Drag below 60 DIP does not page or fall back to click");
            Gesture(new(100, 0));
            Check(ImageList.SelectedIndex == index, "Vertical drag does not page");
            Gesture(new(40, 100)); await activeDisplay;
            Check(ImageList.SelectedIndex == (index + 1) % model.Entries.Count, "Left drag at 60 DIP advances exactly one image");
            Gesture(new(160, 100)); await activeDisplay;
            Check(ImageList.SelectedIndex == index, "Right drag goes to previous image");
            BeginPointer(new(100, 100)); UpdatePointer(new(0, 100)); UpdatePointer(new(100, 100)); EndPointer();
            Check(ImageList.SelectedIndex == index, "Returning drag to origin does not accidentally click or page");
            foreach (var button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle }) {
                mouseSettings = new MouseSettings { PanButton = button switch { MouseButton.Left => ViewerDragButton.Left, MouseButton.Right => ViewerDragButton.Right, _ => ViewerDragButton.Middle }, DragPageEnabled = true };
                SetZoom(1000); UpdateLayout(); Check(CanPan, "Fixture must exceed viewport for pan tests");
                Pan.X = Pan.Y = 0; var loupe = loupeEnabled;
                Gesture(new(140, 140), button);
                Check((Pan.X != 0 || Pan.Y != 0) && ImageList.SelectedIndex == index && loupeEnabled == loupe, "Selected drag button pans without paging or clicking");
                Gesture(new(20, 100), button);
                Check(ImageList.SelectedIndex == index, "Pan takes priority over paging for the same button");
            }
            mouseSettings = new MouseSettings { PanButton = ViewerDragButton.None };
            Pan.X = Pan.Y = 0; Gesture(new(200, 100));
            Check(Pan.X == 0 && Pan.Y == 0 && ImageList.SelectedIndex == index, "Disabled pan does not move oversized image or trigger click after drag");
            mouseSettings = new MouseSettings { PanButton = ViewerDragButton.Right }; ResetZoom();
            BeginPointer(new(100, 100), MouseButton.Right); EndPointer(); await activeDisplay;
            Check(ImageList.SelectedIndex == (index - 1 + model.Entries.Count) % model.Entries.Count, "Pan button still clicks when not dragged");
            index = ImageList.SelectedIndex;
            BeginPointer(new(100, 100)); UpdatePointer(new(0, 100)); CancelPointer(); EndPointer();
            Check(ImageList.SelectedIndex == index, "Cancelled drag never pages");
            BeginPointer(new(100, 100)); UpdatePointer(new(0, 100)); CaptureLost(this, new(Mouse.PrimaryDevice, Environment.TickCount)); EndPointer();
            Check(ImageList.SelectedIndex == index, "Lost capture never executes pending click or drag paging");
        }
        finally { CancelPointer(); mouseSettings = saved; ResetZoom(); }
    }
}
