using System.Text.Json;
using System.Text.Json.Serialization;

namespace StellaViewer.Infrastructure;

/// <summary>Internal document/page identity; page numbers are one-based.</summary>
public sealed record PdfLocation(string DocumentPath, int? Page = null)
{
    private const string Prefix = "stella-pdf:";
    [JsonIgnore] public string Identity => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
    [JsonIgnore] public string Description => ArchiveLocation.DisplayPath(DocumentPath) + (Page is int page ? $" › ページ {page}" : "");
    public static bool IsIdentity(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);
    public static PdfLocation? Parse(string value)
    {
        if (!IsIdentity(value)) return null;
        try {
            var result = JsonSerializer.Deserialize<PdfLocation>(Convert.FromBase64String(value[Prefix.Length..]));
            if (result is null || result.DocumentPath is null || result.Page is <= 0)
                throw new FormatException();
            var archive = ArchiveLocation.Parse(result.DocumentPath);
            if (archive is not null ? archive.IsFolder || !Path.GetExtension(archive.EntryPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                : !Path.IsPathFullyQualified(result.DocumentPath) || !Path.GetExtension(result.DocumentPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
            return result;
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException) {
            throw new InvalidDataException("保存されたPDFページの位置情報が不正です。", error);
        }
    }
}

public static class BrowsePath
{
    public static bool IsVirtual(string value) => ArchiveLocation.IsIdentity(value) || PdfLocation.IsIdentity(value);
    public static string PhysicalPath(string value) => ArchiveLocation.PhysicalPath(PdfLocation.Parse(value)?.DocumentPath ?? value);
    public static string DisplayPath(string value) => PdfLocation.Parse(value)?.Description ?? ArchiveLocation.DisplayPath(value);
}
