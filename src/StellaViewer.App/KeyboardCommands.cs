using System.Windows.Input;
using StellaViewer.Core;

namespace StellaViewer.App;

public static class KeyboardCommands
{
    public static string Name(ViewerCommand command) => command switch {
        ViewerCommand.Previous => "前の画像", ViewerCommand.Next => "次の画像", ViewerCommand.Reload => "再読み込み",
        ViewerCommand.First => "最初の画像を開く", ViewerCommand.Last => "最後の画像を開く",
        ViewerCommand.Fullscreen => "全画面の切り替え", ViewerCommand.ResetZoom => "初期表示に戻す",
        ViewerCommand.RotateLeft => "左へ90°回転", ViewerCommand.RotateRight => "右へ90°回転",
        ViewerCommand.Grid => "グリッドの切り替え", ViewerCommand.Spread => "単ページ／見開きの切り替え",
        ViewerCommand.Loupe => "ルーペの切り替え", ViewerCommand.Back => "戻る", ViewerCommand.Forward => "進む",
        ViewerCommand.Up => "上の階層へ", ViewerCommand.PageJump => "ページ番号を指定", ViewerCommand.Theme => "テーマの切り替え",
        ViewerCommand.Settings => "設定ウィンドウを開く", ViewerCommand.Slideshow => "スライドショーの開始／一時停止", _ => command.ToString()
    };
    public static ShortcutGesture Gesture(Key key, ModifierKeys modifiers) => new ShortcutGesture(key.ToString(), (int)modifiers).Normalize();
    public static KeyboardSettings Normalize(KeyboardSettings? settings)
    {
        var result = (settings ?? new()).Normalize(); var used = new HashSet<ShortcutGesture>();
        foreach (var command in Enum.GetValues<ViewerCommand>()) {
            var valid = new List<ShortcutGesture>();
            foreach (var gesture in result.AllFor(command)) {
                if (!Enum.TryParse<Key>(gesture.Key, true, out var key) || !Enum.IsDefined(key)) continue;
                var normalized = Gesture(key, (ModifierKeys)gesture.Modifiers);
                if (!normalized.IsEmpty && used.Add(normalized)) valid.Add(normalized);
            }
            result.Bindings[command.ToString()] = valid;
        }
        return result;
    }
    public static string Format(ShortcutGesture gesture)
    {
        if (gesture.IsEmpty) return "未割り当て";
        Enum.TryParse<Key>(gesture.Key, true, out var key);
        var name = key switch { Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓", Key.Return => "Enter", _ => key.ToString() };
        return ((gesture.Modifiers & 2) != 0 ? "Ctrl＋" : "") + ((gesture.Modifiers & 1) != 0 ? "Alt＋" : "")
            + ((gesture.Modifiers & 4) != 0 ? "Shift＋" : "") + name;
    }
}
