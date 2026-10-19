using SharpCompress.Archives;
namespace StellaViewer.Infrastructure;
public sealed partial class ArchiveSession
{
    public async Task<PngTextResult> ReadPngMetadataAsync(ArchiveLocation location,CancellationToken token,bool allowNetwork=false)
    {
        NetworkAccess.Validate(ArchiveLocation.PhysicalPath(SourcePath),allowNetwork);
        using var retained=RetainInput();
        if(!ArchiveLocation.SameSource(SourcePath,location.ArchivePath))throw new InvalidOperationException("別の書庫の項目です。");
        var item=FindFile(location.EntryPath)??throw new FileNotFoundException("書庫内の画像が見つかりません。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try{
            if(!DecodeWorker.IsWorker) {
                ValidateSource();
                var metadata=await DecodeWorker.ArchiveTextAsync(physicalPath,SourcePath,item.Path,token).ConfigureAwait(false);
                ValidateSource();return metadata;
            }
            return await Task.Run(()=>{
            token.ThrowIfCancellationRequested();ValidateSource();using var source=OpenSource(physicalPath, token);using var archive=OpenArchive(source);var entry=archive.Entries.ElementAt(item.Index);
            if(entry.Key is null||ArchiveLocation.NormalizeEntry(entry.Key)!=item.Path||entry.IsEncrypted)throw new IOException("書庫が変更されました。再読み込みしてください。");
            using var input=OpenItemStream(archive, item, token);var result=PngTextReader.Read(input,token);ValidateSource();return result;
        },token).ConfigureAwait(false);}finally{gate.Release();}
    }
}
