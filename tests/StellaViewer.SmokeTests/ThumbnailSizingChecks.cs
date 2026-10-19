using ImageMagick;
using StellaViewer.Infrastructure;

internal static class ThumbnailSizingChecks
{
    internal static async Task<int> RunAsync(string output)
    {
        var root=Path.Combine(Path.GetFullPath(output),"thumbnail-settings");Directory.CreateDirectory(root);var count=0;
        void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS: "+label);}
        var normal=ThumbnailLayout.Create(1,false);var folder=ThumbnailLayout.Create(1,true);
        Check(normal is {DetailWidth:68,DetailHeight:64,DetailRowHeight:80,TileWidth:112,TileHeight:124,DecodeEdge:96}&&folder is{DetailWidth:56,DetailHeight:56,DetailRowHeight:68},"Standard thumbnail preset preserves current dimensions");
        var sizes=Enumerable.Range(0,4).Select(i=>ThumbnailLayout.Create(i,false)).ToArray();
        Check(sizes.Zip(sizes.Skip(1)).All(pair=>pair.First.TileWidth<pair.Second.TileWidth&&pair.First.DetailWidth<pair.Second.DetailWidth),"Four presets increase detail and tile thumbnail dimensions");
        var narrow=ThumbnailLayout.Create(3,true,120);
        Check(narrow.TileWidth<=96&&narrow.DetailWidth<=57.6&&narrow.TileIconSize<=narrow.TileImageHeight,"Large thumbnail fits narrow location pane without clipping");
        Check(ThumbnailLayout.Normalize(-1)==1&&ThumbnailLayout.Normalize(4)==1,"Invalid thumbnail presets fall back to standard");
        var preferenceRoot=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(preferenceRoot);var file=Path.Combine(preferenceRoot,"preferences.json");
        File.WriteAllText(file,"{\"Dark\":true}");
        Check(PreferencesStore.Load(file) is{ImageThumbnailSize:1,LocationThumbnailSize:1},"Existing preferences without size fields use standard thumbnails");
        await Task.WhenAll(PreferencesStore.SaveChangeAsync(filePath:file,imageThumbnailSize:3),PreferencesStore.SaveChangeAsync(filePath:file,locationThumbnailSize:0));
        Check(PreferencesStore.Load(file) is{Dark:true,ImageThumbnailSize:3,LocationThumbnailSize:0},"Independent thumbnail size saves merge without losing theme or other list setting");
        await PreferencesStore.SaveChangeAsync(filePath:file,imageThumbnailSize:-1);
        Check(PreferencesStore.Load(file) is{ImageThumbnailSize:1,LocationThumbnailSize:0},"Thumbnail reset leaves other list size unchanged");
        var source=Path.Combine(root,"page01.png");using(var image=new MagickImage(MagickColors.CornflowerBlue,1024,768))image.Write(source);
        for(var i=2;i<=12;i++)File.Copy(source,Path.Combine(root,$"page{i:D2}.png"),true);
        var folderPath=Path.Combine(root,"preview-folder");Directory.CreateDirectory(folderPath);File.Copy(source,Path.Combine(folderPath,"page.png"),true);
        var small=await LocationPreview.DecodeAsync(source,default,false,72);var large=await LocationPreview.DecodeAsync(source,default,false,192);
        Check(small.Width==72&&large.Width==192&&small.OriginalWidth==1024&&large.OriginalWidth==1024,"Thumbnail decoder requests preset resolution while preserving original dimensions");
        var folderPreview=await LocationPreview.DecodeAsync(folderPath,default,false,192);
        Check(folderPreview.Width==192&&folderPreview.Height==144,"Folder preview supports selected thumbnail resolution");
        return count;
    }
}
