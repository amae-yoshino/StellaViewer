using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellaViewer.Infrastructure;

/// <summary>Internal identity for navigation and persistence, never an editable address.</summary>
public sealed record ArchiveLocation(string ArchivePath, string EntryPath = "", bool IsFolder = true)
{
    public const int MaximumNestingDepth = 8;
    private const string Prefix = "stella-archive:";
    [JsonIgnore] public string Identity => Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));
    [JsonIgnore] public string Name => EntryPath.Length == 0 ? Parse(ArchivePath)?.Name ?? Path.GetFileName(ArchivePath) : EntryPath.Split('/')[^1];
    [JsonIgnore] public string Description => DisplayPath(ArchivePath) + (EntryPath.Length == 0 ? "" : " › " + EntryPath.Replace("/", " › "));
    [JsonIgnore] public int NestingDepth => Parse(ArchivePath) is { } parent ? parent.NestingDepth + 1 : 0;
    [JsonIgnore] public ArchiveLocation Folder => IsFolder ? this : this with { EntryPath = ParentEntry(EntryPath), IsFolder = true };
    public static string ParentEntry(string path) => path.LastIndexOf('/') is var i && i >= 0 ? path[..i] : "";
    public static bool IsIdentity(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);
    public static ArchiveLocation? Parse(string value) => Parse(value, 0);
    private static ArchiveLocation? Parse(string value, int depth)
    {
        if (!IsIdentity(value)) return null;
        if (depth > MaximumNestingDepth) throw new NotSupportedException($"入れ子書庫は{MaximumNestingDepth}段までです。");
        try {
            var location = JsonSerializer.Deserialize<ArchiveLocation>(Convert.FromBase64String(value[Prefix.Length..]));
            if (location is null || location.ArchivePath is null || location.EntryPath is null
                || NormalizeEntry(location.EntryPath) != location.EntryPath) throw new FormatException();
            var parent = Parse(location.ArchivePath, depth + 1);
            if (parent is not null ? parent.IsFolder || !ArchiveSession.Supports(parent.EntryPath)
                : !Path.IsPathFullyQualified(location.ArchivePath)) throw new FormatException();
            return location;
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidDataException) {
            throw new InvalidDataException("保存された書庫内の位置情報が不正です。", error);
        }
    }
    public static string PhysicalPath(string value) { while (Parse(value) is { } location) value = location.ArchivePath; return value; }
    public static bool SameSource(string first, string second) => IsIdentity(first) || IsIdentity(second)
        ? StringComparer.Ordinal.Equals(first, second) : StringComparer.OrdinalIgnoreCase.Equals(first, second);
    public static string DisplayPath(string value) => Parse(value)?.Description ?? value;
    public static string NormalizeEntry(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (path.Length == 0) return "";
        if (path.StartsWith('/') || path.Contains(':') || path.Contains('\0')
            || path.Split('/').Any(p => p is "" or "." or ".."))
            throw new InvalidDataException("安全に扱えない書庫内のパスがあります。");
        return path;
    }
}
