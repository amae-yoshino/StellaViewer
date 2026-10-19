using System.Collections.Concurrent;
using System.Text.Json;

namespace StellaViewer.Infrastructure;

/// <summary>Caller holds its existing cross-process mutex throughout read, recovery and replacement.</summary>
public static class RecoverableSettings
{
    private static readonly ConcurrentDictionary<string, string> notices = new(StringComparer.OrdinalIgnoreCase);
    private const long MaximumBytes = 4 * 1024 * 1024;
    private static T ReadFile<T>(string path, Func<T, bool>? valid)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("設定ファイルがサイズ上限を超えています。");
        var value = JsonSerializer.Deserialize<T>(stream);
        if (value is null || valid is not null && !valid(value)) throw new InvalidDataException("設定内容が不正です。");
        return value;
    }
    private static bool Missing(Exception error) => error is FileNotFoundException or DirectoryNotFoundException;
    private static bool Broken(Exception error) => error is JsonException or InvalidDataException;
    private static void Quarantine(string path)
    {
        if (File.Exists(path)) File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"));
    }
    public static T Read<T>(string path, Func<T> defaults, Func<T, bool>? valid = null)
    {
        path = Path.GetFullPath(path);
        bool missing;
        try { return ReadFile(path, valid); }
        catch (Exception error) when (Missing(error) || Broken(error)) { missing = Missing(error); }
        var backup = path + ".bak";
        if (missing && !File.Exists(backup)) return defaults();
        T recovered; bool fromBackup;
        try { recovered = ReadFile(backup, valid); fromBackup = true; }
        catch (Exception error) when (Missing(error) || Broken(error)) {
            if (Broken(error)) Quarantine(backup);
            recovered = defaults(); fromBackup = false;
        }
        // Do not silently treat access errors or locked files as corruption.
        if (!missing) Quarantine(path);
        Write(path, recovered, valid);
        notices[path] = $"{Path.GetFileName(path)}を{(fromBackup ? "バックアップから復旧" : "初期値に復旧")}しました。破損したファイルは退避しています。";
        return recovered;
    }
    public static void Write<T>(string path, T value, Func<T, bool>? valid = null)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = path + ".bak";
        try {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(output, value); output.Flush(true);
            }
            _ = ReadFile<T>(temporary, valid);
            if (File.Exists(path)) {
                try { _ = ReadFile<T>(path, valid); }
                catch (Exception error) when (Broken(error)) { Quarantine(path); }
            }
            if (File.Exists(path)) File.Replace(temporary, path, backup);
            else {
                File.Move(temporary, path);
                if (!File.Exists(backup)) File.Copy(path, backup, overwrite: false);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string? TakeNotice(string directory)
    {
        var root = Path.GetFullPath(directory); var messages = new List<string>();
        foreach (var path in notices.Keys)
            if (string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase) && notices.TryRemove(path, out var message)) messages.Add(message);
        return messages.Count == 0 ? null : string.Join(Environment.NewLine, messages);
    }
}
