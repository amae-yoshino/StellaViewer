namespace StellaViewer.Infrastructure;

public sealed record FolderItemChange(string Path, FileEntry? Image, LocationEntry? Location, bool Missing = false, string? Error = null);

public static class FolderChanges
{
    public static Task<FolderItemChange[]> ReadAsync(string folder, IEnumerable<string> paths, bool hideHiddenFolders,
        CancellationToken token, bool allowNetwork = false) => Task.Run(() => Read(folder, paths, hideHiddenFolders, token, allowNetwork), token);

    public static Task<FolderItemChange[]> RescanAsync(string folder, IEnumerable<string> knownPaths, bool hideHiddenFolders,
        CancellationToken token, bool allowNetwork = false) => Task.Run(() => {
            NetworkAccess.Validate(folder, allowNetwork);
            var paths = new HashSet<string>(knownPaths, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos()) {
                token.ThrowIfCancellationRequested(); paths.Add(entry.FullName);
            }
            return Read(folder, paths, hideHiddenFolders, token, allowNetwork);
        }, token);

    private static FolderItemChange[] Read(string folder, IEnumerable<string> paths, bool hideHiddenFolders, CancellationToken token, bool allowNetwork)
    {
        NetworkAccess.Validate(folder, allowNetwork);
        var fullFolder = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder));
        var changes = new List<FolderItemChange>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
            token.ThrowIfCancellationRequested();
            var fullPath = System.IO.Path.GetFullPath(path);
            if (!string.Equals(System.IO.Path.GetDirectoryName(fullPath), fullFolder, StringComparison.OrdinalIgnoreCase)) continue;
            try {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.Directory) != 0) {
                    var directory = new DirectoryInfo(fullPath);
                    changes.Add(new(fullPath, null, hideHiddenFolders && (attributes & FileAttributes.Hidden) != 0 ? null
                        : new LocationEntry(fullPath, directory.Name, true, null, directory.LastWriteTime)));
                }
                else {
                    var file = new FileInfo(fullPath);
                    var length = file.Length; var date = file.LastWriteTime;
                    changes.Add(new(fullPath,
                        FolderBrowser.IsImageFile(fullPath) ? new(fullPath, file.Name, length, date) : null,
                        FolderBrowser.IsContainerFile(fullPath) ? new(fullPath, file.Name, false, length, date) : null));
                }
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { changes.Add(new(fullPath, null, null, true)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { changes.Add(new(fullPath, null, null, Error: error.Message)); }
        }
        return changes.ToArray();
    }
}
