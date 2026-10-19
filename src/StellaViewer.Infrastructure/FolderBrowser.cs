using System.Runtime.InteropServices;
using System.Threading.Channels;
namespace StellaViewer.Infrastructure;
public sealed record FileEntry(string Path, string Name, long Length, DateTime Modified)
{
    public string DisplayPath => BrowsePath.DisplayPath(Path);
    public bool IsPdfPage => PdfLocation.IsIdentity(Path);
}
public sealed record LocationEntry(string Path, string Name, bool IsDirectory, long? Length = null, DateTime? Modified = null, bool IsPdf = false)
{
    public string DisplayPath => BrowsePath.DisplayPath(Path);
    public string Kind => IsDirectory ? "フォルダ" : IsPdf || System.IO.Path.GetExtension(Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "PDF" : "書庫";
    public string Information => Kind + (Length is long length ? "  " + StellaViewer.Core.FileSizeText.Format(length) : "")
        + (Modified is DateTime date ? $"  {date:yyyy/MM/dd HH:mm}" : "");
}
public static class FolderBrowser
{
    public static bool IsContainerFile(string path) => ContainerExtensions.Contains(System.IO.Path.GetExtension(path));
    private static readonly HashSet<string> ContainerExtensions = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar", ".cbz", ".cbr", ".pdf" };
    public static bool IsVisibleFolder(DirectoryInfo folder, bool hideHiddenFolders) => !hideHiddenFolders || (folder.Attributes & FileAttributes.Hidden) == 0;
    public static string[] GetSubfolders(string path, bool hideHiddenFolders, bool allowNetwork = false)
    {
        NetworkAccess.Validate(path, allowNetwork);
        return new DirectoryInfo(path).EnumerateDirectories().Where(d => IsVisibleFolder(d, hideHiddenFolders)).Select(d => d.FullName).ToArray();
    }
    public static async IAsyncEnumerable<LocationEntry[]> EnumerateLocationsAsync(string folder, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token, bool hideHiddenFolders = true, bool allowNetwork = false)
    {
        var channel = Channel.CreateBounded<LocationEntry[]>(2);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var producer = Task.Run(async () => {
            try {
                NetworkAccess.Validate(folder, allowNetwork);
                var batch = new List<LocationEntry>(128);
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos()) {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (entry is not DirectoryInfo && !IsContainerFile(entry.FullName)) continue;
                    if (entry is DirectoryInfo directory && !IsVisibleFolder(directory, hideHiddenFolders)) continue;
                    try { batch.Add(new(entry.FullName, entry.Name, entry is DirectoryInfo, entry is FileInfo file ? file.Length : null, entry.LastWriteTime)); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    if (batch.Count < 128) continue;
                    await channel.Writer.WriteAsync(batch.ToArray(), lifetime.Token); batch.Clear();
                }
                if (batch.Count > 0) await channel.Writer.WriteAsync(batch.ToArray(), lifetime.Token);
                channel.Writer.TryComplete();
            } catch (Exception error) { channel.Writer.TryComplete(error); }
        }, CancellationToken.None);
        try { await foreach (var batch in channel.Reader.ReadAllAsync(token)) yield return batch; }
        finally { lifetime.Cancel(); await producer; }
    }
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".apng", ".bmp", ".gif", ".webp", ".psd", ".clip" };
    public static bool IsImageFile(string path) => Extensions.Contains(System.IO.Path.GetExtension(path));
    public static void ValidateLocal(string path)
    {
        NetworkAccess.Validate(path);
    }
    public static async IAsyncEnumerable<FileEntry[]> EnumerateAsync(string folder, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token, bool allowNetwork = false)
    {
        var channel = Channel.CreateBounded<FileEntry[]>(2);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var producer = Task.Run(async () => {
            try {
                NetworkAccess.Validate(folder, allowNetwork);
                var batch = new List<FileEntry>(128);
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles()) {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (!Extensions.Contains(file.Extension)) continue;
                    try { batch.Add(new(file.FullName, file.Name, file.Length, file.LastWriteTime)); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    if (batch.Count < 128) continue;
                    await channel.Writer.WriteAsync(batch.ToArray(), lifetime.Token); batch.Clear();
                }
                if (batch.Count > 0) await channel.Writer.WriteAsync(batch.ToArray(), lifetime.Token);
                channel.Writer.TryComplete();
            } catch (Exception error) { channel.Writer.TryComplete(error); }
        }, CancellationToken.None);
        try { await foreach (var batch in channel.Reader.ReadAllAsync(token)) yield return batch; }
        finally { lifetime.Cancel(); await producer; }
    }
    public static LocationEntry[] SortLocations(IEnumerable<LocationEntry> entries, int mode)
    {
        var names = Comparer<LocationEntry>.Create((a, b) => {
            var result = StrCmpLogicalW(a.Name, b.Name);
            return result != 0 ? result : StringComparer.Ordinal.Compare(a.Path, b.Path);
        });
        var groups=entries.OrderBy(e=>!e.IsDirectory);
        return (mode switch {
            1 => groups.ThenBy(e => e.Modified is null).ThenBy(e => e.Modified).ThenBy(e => e, names),
            2 => groups.ThenBy(e => e.Length is null).ThenBy(e => e.Length).ThenBy(e => e, names),
            3 => groups.ThenByDescending(e => e, names),
            4 => groups.ThenBy(e => e.Modified is null).ThenByDescending(e => e.Modified).ThenBy(e => e, names),
            5 => groups.ThenBy(e => e.Length is null).ThenByDescending(e => e.Length).ThenBy(e => e, names),
            _ => groups.ThenBy(e => e, names)
        }).ToArray();
    }
    public static FileEntry[] SortImages(IEnumerable<FileEntry> entries, int mode) => (mode switch {
        1 => entries.OrderBy(e => e.Modified).ThenBy(e => e, NaturalComparer),
        2 => entries.OrderBy(e => e.Length).ThenBy(e => e, NaturalComparer),
        3 => entries.OrderByDescending(e => e, NaturalComparer),
        4 => entries.OrderByDescending(e => e.Modified).ThenBy(e => e, NaturalComparer),
        5 => entries.OrderByDescending(e => e.Length).ThenBy(e => e, NaturalComparer),
        _ => entries.OrderBy(e => e, NaturalComparer)
    }).ToArray();
    public static IComparer<FileEntry> NaturalComparer { get; } = Comparer<FileEntry>.Create((a, b) => {
        var result = StrCmpLogicalW(a.Name, b.Name);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a.Path, b.Path);
    });
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int StrCmpLogicalW(string a, string b);
}
