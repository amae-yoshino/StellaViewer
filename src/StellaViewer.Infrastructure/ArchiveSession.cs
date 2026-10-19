using SharpCompress.Archives;
using StellaViewer.Core;

namespace StellaViewer.Infrastructure;

public sealed record ArchiveItem(string Path, string Name, bool IsDirectory, long Length, DateTime? Modified, int Index);

/// <summary>Read-only archive catalog. Images are decoded from entry streams without disk extraction.</summary>
public sealed partial class ArchiveSession : IDisposable
{
    private readonly Dictionary<string, ArchiveItem> items;
    private readonly long sourceLength;
    private readonly DateTime sourceModified;
    private readonly SemaphoreSlim gate = new(1, 1);
    public string SourcePath { get; }
    private readonly string physicalPath;
    private SourceLeaseGroup input = new([]);
    private ArchiveSession? parent;
    private Action? validateOuter;
    private ArchiveSession(string path, string physical, Dictionary<string, ArchiveItem> items, long length, DateTime modified)
    { SourcePath = path; physicalPath = physical; this.items = items; sourceLength = length; sourceModified = modified; }
    public static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".cbz" or ".7z" or ".rar" or ".cbr";

    public static async Task<ArchiveSession> OpenAsync(string path, CancellationToken token, bool allowNetwork = false,
        ArchiveTemporaryStore? store = null, ArchiveSession? reuse = null)
    {
        if (!ArchiveLocation.IsIdentity(path)) path = Path.GetFullPath(path);
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(path), allowNetwork);
        if (reuse is not null && await Task.Run(() => reuse.CloneAncestor(path), token).ConfigureAwait(false) is { } cached) return cached;
        if (ArchiveLocation.Parse(path) is { } nested) {
            if (store is null) throw new InvalidOperationException("入れ子書庫には一時領域が必要です。");
            using var outer = await OpenAsync(nested.ArchivePath, token, allowNetwork, store, reuse).ConfigureAwait(false);
            return await OpenNestedAsync(outer, nested, store, token, allowNetwork).ConfigureAwait(false);
        }
        return await ReadCatalogAsync(path, path, token, allowNetwork).ConfigureAwait(false);
    }
    private static Task<ArchiveSession> ReadCatalogAsync(string path, string logicalPath, CancellationToken token, bool allowNetwork = false) => Task.Run(async () => {
        NetworkAccess.Validate(path, allowNetwork);
        path = Path.GetFullPath(path);
        if (!Supports(ArchiveLocation.Parse(logicalPath)?.EntryPath ?? logicalPath)) throw new NotSupportedException("この書庫形式は非対応です。");
        token.ThrowIfCancellationRequested();
        var before = new FileInfo(path); var length = before.Length; var modified = before.LastWriteTimeUtc;
        if(!DecodeWorker.IsWorker) {
            var entries=await DecodeWorker.ArchiveCatalogAsync(path,logicalPath,token).ConfigureAwait(false);
            var remote=new ArchiveSession(logicalPath,path,entries.ToDictionary(e=>e.Path,StringComparer.Ordinal),length,modified);
            remote.ValidateSource();return remote;
        }
        var catalog = new Dictionary<string, ArchiveItem>(StringComparer.Ordinal);
        using var source = OpenSource(path, token);
        using var archive = OpenArchive(source);
        if (archive.Type is not (SharpCompress.Common.ArchiveType.Zip or SharpCompress.Common.ArchiveType.SevenZip or SharpCompress.Common.ArchiveType.Rar))
            throw new NotSupportedException("ZIP・7z・RAR以外の書庫形式には対応していません。");
        if (!archive.IsComplete || archive.Volumes.Skip(1).Any())
            throw new NotSupportedException("分割書庫または不足した書庫には対応していません。");
        var index = 0; long catalogCharacters = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        void Add(string key, ArchiveItem item) {
            if (!catalog.ContainsKey(key)) {
                if (catalog.Count >= ArchiveReadLimits.MaximumItems || key.Length > ArchiveReadLimits.MaximumCatalogCharacters - catalogCharacters)
                    throw new NotSupportedException("書庫の一覧が上限（100,000項目・パス合計16 Mi文字）を超えています。");
                catalogCharacters += key.Length;
            }
            catalog[key] = item;
        }
        foreach (var entry in archive.Entries) {
            token.ThrowIfCancellationRequested();
            ArchiveReadLimits.CheckTime(timer, token);
            if (index >= ArchiveReadLimits.MaximumItems) throw new NotSupportedException("書庫が項目数の上限100,000件を超えています。");
            var entryIndex = index++;
            if (entry.Size < 0) throw new InvalidDataException("書庫内項目のサイズが不正です。");
            if (entry.IsEncrypted) throw new NotSupportedException("パスワード付きの書庫には対応していません。");
            if (entry.Key is null) continue;
            ArchiveReadLimits.CheckPath(entry.Key);
            var key = ArchiveLocation.NormalizeEntry(entry.Key);
            if (key.Length == 0) continue;
            var item = new ArchiveItem(key, key.Split('/')[^1], entry.IsDirectory, entry.Size, entry.LastModifiedTime, entryIndex);
            if (catalog.TryGetValue(key, out var previous)) {
                if (!previous.IsDirectory || !item.IsDirectory) throw new InvalidDataException("書庫内に重複する項目名があります。");
            }
            Add(key, item);
            var parent = ArchiveLocation.ParentEntry(key);
            while (parent.Length > 0) {
                if (catalog.TryGetValue(parent, out var existing) && !existing.IsDirectory)
                    throw new InvalidDataException("書庫内のファイル名とフォルダ名が競合しています。");
                if (!catalog.ContainsKey(parent)) Add(parent, new(parent, parent.Split('/')[^1], true, 0, null, -1));
                parent = ArchiveLocation.ParentEntry(parent);
            }
        }
        var result = new ArchiveSession(logicalPath, path, catalog, length, modified);
        result.ValidateSource();
        return result;
    }, token);

    public static async Task<ArchiveSession> OpenNestedAsync(ArchiveSession outer, ArchiveLocation item,
        ArchiveTemporaryStore store, CancellationToken token, bool allowNetwork = false)
    {
        if (item.IsFolder || !Supports(item.EntryPath)) throw new NotSupportedException("書庫ではない項目です。");
        if (item.NestingDepth >= ArchiveLocation.MaximumNestingDepth)
            throw new NotSupportedException($"入れ子書庫は{ArchiveLocation.MaximumNestingDepth}段までです。");
        using var retained = outer.RetainInput();
        using var file = await store.CreateAsync((output, limit, cancellation) => outer.CopyEntryAsync(item, output, limit, cancellation, allowNetwork), token).ConfigureAwait(false);
        var result = await ReadCatalogAsync(file.Path, item.Identity, token).ConfigureAwait(false);
        try {
            result.input.Dispose(); result.input = retained.Append(file);
            result.parent = outer; result.validateOuter = outer.ValidateUnchanged;
            result.ValidateSource(); return result;
        }
        catch { result.Dispose(); throw; }
    }
    internal static Task<ArchiveSession> OpenWorkerAsync(string physical,string logical,CancellationToken token)
    {
        if(!DecodeWorker.IsWorker)throw new InvalidOperationException("書庫の解析は読み込みプロセスでのみ実行できます。");
        return ReadCatalogAsync(physical,logical,token,true);
    }
    internal ArchiveItem[] WorkerCatalog()
    {
        if(!DecodeWorker.IsWorker)throw new InvalidOperationException();
        return items.Values.ToArray();
    }
    internal SourceLeaseGroup RetainInput() => input.Fork();
    public void Dispose() => input.Dispose();
    public ArchiveSession CreateView()
    {
        using var retained = RetainInput();
        return CloneReference();
    }
    private ArchiveSession CloneReference()
    {
        var clone = new ArchiveSession(SourcePath, physicalPath, items, sourceLength, sourceModified) { parent = parent, validateOuter = validateOuter };
        clone.input.Dispose(); clone.input = new(input.Files); return clone;
    }
    private ArchiveSession? CloneAncestor(string source)
    {
        using var retained = RetainInput();
        for (var candidate = this; candidate is not null; candidate = candidate.parent)
            if (ArchiveLocation.SameSource(candidate.SourcePath, source)) { candidate.ValidateSource(); return candidate.CloneReference(); }
        return null;
    }
    public ArchiveSession? CreateParentView()
    {
        using var retained = RetainInput();
        if (parent is null) return null;
        parent.ValidateSource(); return parent.CloneReference();
    }

    internal ArchiveItem? FirstPreviewImage()
    {
        ArchiveItem? first = null;
        foreach (var item in items.Values) {
            if (item.IsDirectory || !FolderBrowser.IsImageFile(item.Path)) continue;
            if (first is null || FolderBrowser.NaturalComparer.Compare(new(item.Path, item.Path, 0, default), new(first.Path, first.Path, 0, default)) < 0) first = item;
        }
        return first;
    }
    public bool ContainsFolder(string path) => path.Length == 0 || items.TryGetValue(path, out var entry) && entry.IsDirectory;
    public bool ContainsImage(string path) => items.TryGetValue(path, out var entry) && !entry.IsDirectory && FolderBrowser.IsImageFile(path);
    public ArchiveItem? FindFile(string path) => items.TryGetValue(path, out var entry) && !entry.IsDirectory ? entry : null;
    public void ValidateUnchanged() => ValidateSource();
    public async Task CopyEntryAsync(ArchiveLocation location, Stream destination, long limit, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(SourcePath), allowNetwork);
        using var retained = RetainInput();
        if (!ArchiveLocation.SameSource(SourcePath, location.ArchivePath)) throw new InvalidOperationException("別の書庫の項目です。");
        var item = FindFile(location.EntryPath) ?? throw new FileNotFoundException("書庫内の項目が見つかりません。");
        if (item.Length > limit) throw new IOException("一時展開の容量上限を超えました。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if(!DecodeWorker.IsWorker) {
                ValidateSource();
                await DecodeWorker.ArchiveCopyAsync(physicalPath,SourcePath,item.Path,destination,limit,token).ConfigureAwait(false);
                ValidateSource();return;
            }
            await Task.Run(async () => {
                token.ThrowIfCancellationRequested(); ValidateSource();
                using var source = OpenSource(physicalPath, token);
                using var archive = OpenArchive(source);
                var entry = archive.Entries.ElementAt(item.Index);
                if (entry.Key is null || ArchiveLocation.NormalizeEntry(entry.Key) != item.Path || entry.IsEncrypted)
                    throw new IOException("書庫が変更されました。再読み込みしてください。");
                using var input = OpenItemStream(archive, item, token);
                var buffer = new byte[131072]; long copied = 0;
                while (true) {
                    token.ThrowIfCancellationRequested();
                    var count = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (count == 0) break;
                    if (count > limit - copied) throw new IOException("一時展開の容量上限を超えました。");
                    await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); copied += count;
                }
                if (copied != item.Length) throw new InvalidDataException("書庫内項目の展開サイズが一致しません。");
                ValidateSource();
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public ArchiveItem[] Children(string folder)
    {
        if (!ContainsFolder(folder)) throw new DirectoryNotFoundException("書庫内フォルダが見つかりません。");
        return items.Values.Where(i => ArchiveLocation.ParentEntry(i.Path) == folder).ToArray();
    }
    public FileEntry[] Images(string folder) => Children(folder).Where(i => !i.IsDirectory && FolderBrowser.IsImageFile(i.Path))
        .Select(i => new FileEntry(new ArchiveLocation(SourcePath, i.Path, false).Identity, i.Name, i.Length, i.Modified ?? default)).ToArray();
    public LocationEntry[] Locations(string folder) => Children(folder).Where(i => i.IsDirectory || FolderBrowser.IsContainerFile(i.Path))
        .Select(i => new LocationEntry(new ArchiveLocation(SourcePath, i.Path, i.IsDirectory).Identity, i.Name, i.IsDirectory,
            i.IsDirectory ? null : i.Length, i.Modified, Path.GetExtension(i.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))).ToArray();

    public async Task<DecodedImage> DecodeAsync(ArchiveLocation location, CancellationToken token, uint thumbnailSize = 0, bool allowNetwork = false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(SourcePath), allowNetwork);
        using var retained = RetainInput();
        if (!ArchiveLocation.SameSource(SourcePath, location.ArchivePath)) throw new InvalidOperationException("別の書庫の項目です。");
        var key = location.EntryPath;
        if (location.IsFolder) {
            var first = Images(key).OrderBy(i => i, FolderBrowser.NaturalComparer).FirstOrDefault()
                ?? throw new NotSupportedException("直下にプレビューできる画像がありません。");
            key = ArchiveLocation.Parse(first.Path)!.EntryPath;
        }
        if (!items.TryGetValue(key, out var item) || item.IsDirectory || !FolderBrowser.IsImageFile(key))
            throw new NotSupportedException("この書庫内項目は画像として表示できません。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if(!DecodeWorker.IsWorker) {
                ValidateSource();
                var decoded=await DecodeWorker.ArchiveImageAsync(physicalPath,SourcePath,key,thumbnailSize,token).ConfigureAwait(false);
                ValidateSource();return decoded;
            }
            return await Task.Run(async () => {
                token.ThrowIfCancellationRequested(); ValidateSource();
                using var source = OpenSource(physicalPath, token);
                using var archive = OpenArchive(source);
                var entry = archive.Entries.ElementAt(item.Index);
                if (entry.Key is null || ArchiveLocation.NormalizeEntry(entry.Key) != key || entry.IsEncrypted)
                    throw new IOException("書庫が変更されました。再読み込みしてください。");
                using var stream = OpenItemStream(archive, item, token);
                var image = await new MagickImageDecoder().DecodeStreamAsync(stream, token, thumbnailSize).ConfigureAwait(false);
                ValidateSource(); return image;
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task<bool> HasPngAnimationAsync(ArchiveLocation location, CancellationToken token, bool allowNetwork = false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(SourcePath), allowNetwork);
        using var retained = RetainInput();
        if (!ArchiveLocation.SameSource(SourcePath, location.ArchivePath)) throw new InvalidOperationException("別の書庫の項目です。");
        var item = FindFile(location.EntryPath) ?? throw new FileNotFoundException("書庫内の画像が見つかりません。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try {
            if(!DecodeWorker.IsWorker) {
                ValidateSource();
                var animated=await DecodeWorker.ArchiveAnimationHeaderAsync(physicalPath,SourcePath,item.Path,token).ConfigureAwait(false);
                ValidateSource();return animated;
            }
            return await Task.Run(() => {
                token.ThrowIfCancellationRequested(); ValidateSource();
                using var source = OpenSource(physicalPath, token); using var archive = OpenArchive(source);
                var entry = archive.Entries.ElementAt(item.Index);
                if (entry.Key is null || ArchiveLocation.NormalizeEntry(entry.Key) != location.EntryPath || entry.IsEncrypted)
                    throw new IOException("書庫が変更されました。再読み込みしてください。");
                using var stream = OpenItemStream(archive, item, token);
                var animated = PngAnimationReader.HasAnimationHeader(stream, token); token.ThrowIfCancellationRequested(); ValidateSource(); return animated;
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    private static Stream OpenSource(string path, CancellationToken token) => ArchiveReadLimits.OpenSource(path, token);
    private void ValidateSource()
    {
        validateOuter?.Invoke();
        var source = new FileInfo(physicalPath);
        if (!source.Exists || source.Length != sourceLength || source.LastWriteTimeUtc != sourceModified)
            throw new IOException("書庫が変更されました。再読み込みしてください。");
    }
}
