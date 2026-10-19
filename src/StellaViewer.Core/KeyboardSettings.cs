using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellaViewer.Core;

public enum ViewerCommand { Previous, Next, Reload, Fullscreen, ResetZoom, RotateLeft, RotateRight, Grid, Spread, Loupe, Back, Forward, Up, PageJump, Theme, Settings, Slideshow, First, Last }
public sealed record ShortcutGesture(string Key = "", int Modifiers = 0)
{
    public bool IsEmpty => string.IsNullOrEmpty(Key);
    public ShortcutGesture Normalize()
    {
        var key = (Key ?? "").Trim().ToUpperInvariant();
        if (key.Length == 0) return new();
        if (key.Length > 32 || key.Any(c => !char.IsAsciiLetterOrDigit(c)) || Modifiers is < 0 or > 7
            || key is "ESCAPE" or "TAB" or "NONE" or "SYSTEM" or "IMEPROCESSED" or "DEADCHARPROCESSED"
                or "LEFTCTRL" or "RIGHTCTRL" or "LEFTSHIFT" or "RIGHTSHIFT" or "LEFTALT" or "RIGHTALT" or "LWIN" or "RWIN"
            || key == "F4" && (Modifiers & 1) != 0) return new();
        return new(key, Modifiers);
    }
}

public sealed record KeyboardSettings
{
    [JsonConverter(typeof(ShortcutBindingsConverter))]
    public Dictionary<string, List<ShortcutGesture>> Bindings { get; init; } = Defaults();
    private static Dictionary<string, List<ShortcutGesture>> Defaults() => Enum.GetValues<ViewerCommand>().ToDictionary(c => c.ToString(),
        c => c switch { ViewerCommand.Previous => [new("LEFT")], ViewerCommand.Next => [new("RIGHT")], ViewerCommand.Reload => [new("F5")], _ => new List<ShortcutGesture>() });
    public IReadOnlyList<ShortcutGesture> AllFor(ViewerCommand command) => Bindings?.GetValueOrDefault(command.ToString()) ?? [];
    public ShortcutGesture For(ViewerCommand command) => AllFor(command).FirstOrDefault() ?? new();
    public KeyboardSettings Normalize()
    {
        var result = Defaults();
        foreach (var command in Enum.GetValues<ViewerCommand>())
            if (Bindings is not null && Bindings.TryGetValue(command.ToString(), out var gestures))
                result[command.ToString()] = (gestures ?? []).Select(g => (g ?? new()).Normalize()).Where(g => !g.IsEmpty).Distinct().ToList();
        return new() { Bindings = result };
    }
    public ViewerCommand? Conflict(ViewerCommand command, ShortcutGesture gesture)
    {
        gesture = gesture.Normalize(); if (gesture.IsEmpty) return null;
        foreach (var other in Enum.GetValues<ViewerCommand>())
            if (other != command && AllFor(other).Contains(gesture)) return other;
        return null;
    }
    public KeyboardSettings Add(ViewerCommand command, ShortcutGesture gesture, bool replace = false)
    {
        var result = Normalize(); gesture = gesture.Normalize();
        if (gesture.IsEmpty) return result;
        if (result.Conflict(command, gesture) is ViewerCommand conflict) {
            if (!replace) throw new InvalidOperationException("このキーは別の操作に割り当てられています。");
            result.Bindings[conflict.ToString()].Remove(gesture);
        }
        var list = result.Bindings[command.ToString()];
        if (!list.Contains(gesture)) list.Add(gesture);
        return result;
    }
    public KeyboardSettings Remove(ViewerCommand command, ShortcutGesture gesture)
    {
        var result = Normalize(); result.Bindings[command.ToString()].Remove(gesture.Normalize()); return result;
    }
    public KeyboardSettings Assign(ViewerCommand command, ShortcutGesture gesture, bool replace = false)
    {
        var result = Normalize(); result.Bindings[command.ToString()].Clear(); return result.Add(command, gesture, replace);
    }
    public KeyboardSettings MergeChanges(KeyboardSettings before, KeyboardSettings next)
    {
        var result = Normalize(); before = before.Normalize(); next = next.Normalize();
        foreach (var command in Enum.GetValues<ViewerCommand>()) {
            var oldKeys = before.AllFor(command); var newKeys = next.AllFor(command);
            var list = result.Bindings[command.ToString()];
            list.RemoveAll(g => oldKeys.Contains(g) && !newKeys.Contains(g));
            foreach (var added in newKeys.Except(oldKeys)) if (!list.Contains(added)) list.Add(added);
        }
        var used = new HashSet<ShortcutGesture>();
        foreach (var gesture in result.Bindings.Values.SelectMany(g => g))
            if (!used.Add(gesture)) throw new InvalidOperationException("他のウィンドウで同じキーの割り当てが変更されました。設定を開き直して確認してください。");
        return result;
    }
}

// Read the original single-gesture format as well as the current arrays.
public sealed class ShortcutBindingsConverter : JsonConverter<Dictionary<string, List<ShortcutGesture>>>
{
    public override Dictionary<string, List<ShortcutGesture>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new Dictionary<string, List<ShortcutGesture>>();
        if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in document.RootElement.EnumerateObject()) {
            var value = property.Value;
            result[property.Name] = value.ValueKind switch {
                JsonValueKind.Object => [value.Deserialize<ShortcutGesture>(options) ?? new()],
                JsonValueKind.Array => value.Deserialize<List<ShortcutGesture>>(options) ?? [],
                _ => []
            };
        }
        return result;
    }
    public override void Write(Utf8JsonWriter writer, Dictionary<string, List<ShortcutGesture>> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value) { writer.WritePropertyName(entry.Key); JsonSerializer.Serialize(writer, entry.Value, options); }
        writer.WriteEndObject();
    }
}
