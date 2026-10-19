namespace StellaViewer.Core;

public enum ViewerMouseGesture { LeftClick, RightClick, MiddleClick, WheelUp, WheelDown }
public enum ViewerDragButton { Left, Right, Middle, None }

public sealed record MouseSettings
{
    public ViewerDragButton PanButton { get; init; } = ViewerDragButton.Left;
    public bool DragPageEnabled { get; init; }
    public Dictionary<string, ViewerCommand?> Bindings { get; init; } = Defaults();
    private static Dictionary<string, ViewerCommand?> Defaults() => new() {
        [nameof(ViewerMouseGesture.LeftClick)] = ViewerCommand.Next,
        [nameof(ViewerMouseGesture.RightClick)] = ViewerCommand.Previous,
        [nameof(ViewerMouseGesture.MiddleClick)] = ViewerCommand.Loupe,
        [nameof(ViewerMouseGesture.WheelUp)] = ViewerCommand.Previous,
        [nameof(ViewerMouseGesture.WheelDown)] = ViewerCommand.Next
    };
    public ViewerCommand? For(ViewerMouseGesture gesture) => Bindings?.GetValueOrDefault(gesture.ToString());
    public MouseSettings Normalize()
    {
        var result = Defaults();
        foreach (var gesture in Enum.GetValues<ViewerMouseGesture>())
            if (Bindings is not null && Bindings.TryGetValue(gesture.ToString(), out var command))
                result[gesture.ToString()] = command is ViewerCommand value && Enum.IsDefined(value) ? value : null;
        return new() { Bindings = result, PanButton = Enum.IsDefined(PanButton) ? PanButton : ViewerDragButton.Left, DragPageEnabled = DragPageEnabled };
    }
    public MouseSettings Assign(ViewerMouseGesture gesture, ViewerCommand? command)
    {
        var result = Normalize();
        if (Enum.IsDefined(gesture)) result.Bindings[gesture.ToString()] = command is ViewerCommand value && Enum.IsDefined(value) ? value : null;
        return result;
    }
    public MouseSettings MergeChanges(MouseSettings before, MouseSettings next)
    {
        var result = Normalize(); before = before.Normalize(); next = next.Normalize();
        foreach (var gesture in Enum.GetValues<ViewerMouseGesture>())
            if (before.For(gesture) != next.For(gesture)) result.Bindings[gesture.ToString()] = next.For(gesture);
        return result with {
            PanButton = before.PanButton != next.PanButton ? next.PanButton : result.PanButton,
            DragPageEnabled = before.DragPageEnabled != next.DragPageEnabled ? next.DragPageEnabled : result.DragPageEnabled
        };
    }
}
