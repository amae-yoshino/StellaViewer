using System.Text.Json;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

internal static class MouseChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS: " + label); }
        var initial = new MouseSettings();
        Check(initial.For(ViewerMouseGesture.LeftClick) == ViewerCommand.Next && initial.For(ViewerMouseGesture.RightClick) == ViewerCommand.Previous
            && initial.For(ViewerMouseGesture.MiddleClick) == ViewerCommand.Loupe && initial.For(ViewerMouseGesture.WheelUp) == ViewerCommand.Previous
            && initial.For(ViewerMouseGesture.WheelDown) == ViewerCommand.Next, "Mouse defaults preserve all five existing viewer gestures");
        var legacy = JsonSerializer.Deserialize<ViewerPreferences>("{\"Dark\":true}")!;
        Check(legacy.Mouse.For(ViewerMouseGesture.MiddleClick) == ViewerCommand.Loupe, "Old preferences acquire existing mouse defaults");
        var none = initial.Assign(ViewerMouseGesture.LeftClick, null);
        Check(none.For(ViewerMouseGesture.LeftClick) is null && initial.For(ViewerMouseGesture.LeftClick) == ViewerCommand.Next, "Unassigned mouse gesture is explicit and leaves old snapshot unchanged");
        var roundtrip = JsonSerializer.Deserialize<MouseSettings>(JsonSerializer.Serialize(none))!.Normalize();
        Check(roundtrip.For(ViewerMouseGesture.LeftClick) is null && roundtrip.For(ViewerMouseGesture.WheelDown) == ViewerCommand.Next, "Disabled mouse operation persists alongside defaults");
        var invalid = new MouseSettings { Bindings = new() { ["RightClick"] = (ViewerCommand)999, ["unknown"] = ViewerCommand.Theme } }.Normalize();
        Check(invalid.For(ViewerMouseGesture.RightClick) is null && invalid.For(ViewerMouseGesture.LeftClick) == ViewerCommand.Next && invalid.Bindings.Count == 5, "Unknown mouse keys are excluded and invalid commands cannot execute");
        Check(new MouseSettings { Bindings = null! }.Normalize().For(ViewerMouseGesture.MiddleClick) == ViewerCommand.Loupe, "Null mouse bindings recover safe defaults");
        var first = initial.Assign(ViewerMouseGesture.RightClick, ViewerCommand.ResetZoom);
        var second = initial.Assign(ViewerMouseGesture.LeftClick, ViewerCommand.Fullscreen);
        var merged = first.MergeChanges(initial, second);
        Check(merged.For(ViewerMouseGesture.RightClick) == ViewerCommand.ResetZoom && merged.For(ViewerMouseGesture.LeftClick) == ViewerCommand.Fullscreen, "Independent mouse edits merge without erasing another window");
        Check(first.MergeChanges(initial, initial.Assign(ViewerMouseGesture.RightClick, null)).For(ViewerMouseGesture.RightClick) is null, "Later edit of same mouse gesture wins, including unassignment");
        var root = Path.Combine(Path.GetFullPath(output), "mouse-settings"); Directory.CreateDirectory(root); var path = Path.Combine(root, "preferences.json");
        File.WriteAllText(path, "{}");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(mouse: first, previousMouse: initial, filePath: path),
            PreferencesStore.SaveChangeAsync(mouse: second, previousMouse: initial, filePath: path), PreferencesStore.SaveChangeAsync(dark: true, filePath: path));
        var saved = PreferencesStore.Load(path);
        Check(saved.Dark && saved.Mouse.For(ViewerMouseGesture.RightClick) == ViewerCommand.ResetZoom && saved.Mouse.For(ViewerMouseGesture.LeftClick) == ViewerCommand.Fullscreen, "Concurrent preference saves merge independent mouse edits and theme");
        await PreferencesStore.SaveChangeAsync(mouse: initial, previousMouse: saved.Mouse, filePath: path);
        var reset = PreferencesStore.Load(path);
        Check(reset.Dark && reset.Mouse.For(ViewerMouseGesture.RightClick) == ViewerCommand.Previous && reset.Keyboard.For(ViewerCommand.Next).Key == "RIGHT", "Mouse reset preserves keyboard and unrelated preferences");
        Check(legacy.Mouse.PanButton == ViewerDragButton.Left && !legacy.Mouse.DragPageEnabled, "Old mouse preferences default to left pan and disabled drag paging");
        var drag = initial with { PanButton = ViewerDragButton.Middle, DragPageEnabled = true };
        var dragRoundtrip = JsonSerializer.Deserialize<MouseSettings>(JsonSerializer.Serialize(drag))!.Normalize();
        Check(dragRoundtrip.PanButton == ViewerDragButton.Middle && dragRoundtrip.DragPageEnabled, "Drag button and paging survive serialization");
        Check(drag.Assign(ViewerMouseGesture.LeftClick, null).PanButton == ViewerDragButton.Middle && drag.Assign(ViewerMouseGesture.LeftClick, null).DragPageEnabled, "Click editing preserves drag settings");
        Check((initial with { PanButton = (ViewerDragButton)999 }).Normalize().PanButton == ViewerDragButton.Left, "Invalid drag button falls back to existing left button");
        var independent = first.MergeChanges(initial, drag);
        Check(independent.For(ViewerMouseGesture.RightClick) == ViewerCommand.ResetZoom && independent.PanButton == ViewerDragButton.Middle && independent.DragPageEnabled, "Drag changes merge independently from click settings");
        File.WriteAllText(path, "{}");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(mouse: initial with { PanButton = ViewerDragButton.Right }, previousMouse: initial, filePath: path),
            PreferencesStore.SaveChangeAsync(mouse: initial with { DragPageEnabled = true }, previousMouse: initial, filePath: path));
        var combined = PreferencesStore.Load(path);
        Check(combined.Mouse.PanButton == ViewerDragButton.Right && combined.Mouse.DragPageEnabled, "Concurrent pan button and drag paging edits do not erase each other");
        return count;
    }
}
