using System.Windows;
using System.Windows.Input;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private MouseButton? pointerButton;
    private Point pointerOrigin, pointerPosition;
    private bool pointerPans, pointerPages;
    private void BeginPointer(Point point, MouseButton button = MouseButton.Left)
    {
        CancelPointer();
        pointerDown = ImageList.SelectedItem is FileEntry;
        pointerButton = pointerDown ? button : null;
        pointerOrigin = pointerPosition = point;
        var panButton = mouseSettings.PanButton switch {
            ViewerDragButton.Left => MouseButton.Left, ViewerDragButton.Right => MouseButton.Right,
            ViewerDragButton.Middle => MouseButton.Middle, _ => (MouseButton?)null
        };
        pointerPans = pointerDown && CanPan && panButton == button;
        pointerPages = pointerDown && button == MouseButton.Left && mouseSettings.DragPageEnabled && !pointerPans;
        drag = pointerPans ? point : null;
    }
    private void UpdatePointer(Point point)
    {
        if (!pointerDown) return;
        pointerPosition = point;
        var total = point - pointerOrigin;
        if (!dragMoved && Math.Abs(total.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(total.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragMoved = true;
        if (pointerPans && CanPan && drag is Point previous) {
            var delta = point - previous;
            Pan.X += delta.X; Pan.Y += delta.Y; ClampPan(); drag = point;
        }
    }
    private void CancelPointer()
    {
        pointerDown = dragMoved = pointerPans = pointerPages = false; drag = null; pointerButton = null;
        if (Viewport.IsMouseCaptured) Viewport.ReleaseMouseCapture();
    }
    private void EndPointer()
    {
        var clicked = pointerDown && !dragMoved;
        var paged = pointerDown && dragMoved && pointerPages;
        var button = pointerButton;
        var delta = pointerPosition - pointerOrigin;
        CancelPointer();
        if (paged && Math.Abs(delta.X) >= 60 && Math.Abs(delta.X) > Math.Abs(delta.Y)) Move(delta.X < 0 ? 1 : -1);
        else if (clicked && button is MouseButton value) _ = ExecuteMouseCommandAsync(value switch {
            MouseButton.Left => ViewerMouseGesture.LeftClick, MouseButton.Right => ViewerMouseGesture.RightClick,
            _ => ViewerMouseGesture.MiddleClick
        });
    }
    private void ViewerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
        BeginPointer(e.GetPosition(Viewport), e.ChangedButton);
        if (pointerPans || pointerPages) Viewport.CaptureMouse();
        e.Handled = true;
    }
    private void ViewerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return;
        if (pointerButton == e.ChangedButton) { UpdatePointer(e.GetPosition(Viewport)); EndPointer(); }
        e.Handled = true;
    }
    private void PanMove(object sender, MouseEventArgs e)
    {
        var pressed = pointerButton switch {
            MouseButton.Left => e.LeftButton, MouseButton.Right => e.RightButton,
            MouseButton.Middle => e.MiddleButton, _ => MouseButtonState.Released
        };
        if (pressed == MouseButtonState.Pressed) UpdatePointer(e.GetPosition(Viewport));
        else if (pointerDown) CancelPointer();
        UpdateLoupePointer(e.GetPosition(Viewport));
    }
    private void PointerLeft(object sender, MouseEventArgs e)
    {
        if (!Viewport.IsMouseCaptured) CancelPointer();
        loupePointerInside = false; LoupeLayer.Clear();
    }
    private void CaptureLost(object sender, MouseEventArgs e) { pointerDown = dragMoved = pointerPans = pointerPages = false; drag = null; pointerButton = null; }
}
