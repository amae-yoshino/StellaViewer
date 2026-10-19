using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;
public partial class MainWindow
{
    internal async Task VerifyMetadataAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var fixtures=Path.Combine(Path.GetDirectoryName(imagePath)!,"metadata");var root=Path.Combine(fixtures,"ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        foreach(var name in new[]{"parameters.png","comfy.png","plain.png","novel.png","metadata.zip","nested.zip"})File.Copy(Path.Combine(fixtures,name),Path.Combine(root,name));
        var path=Path.Combine(root,"parameters.png");var plain=Path.Combine(root,"plain.png");var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        void Snapshot(string suffix){var content=(FrameworkElement)probe.Content;content.UpdateLayout();var visual=new DrawingVisual();using(var draw=visual.RenderOpen())draw.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);SaveRender(bitmap,screenshotPath+suffix);}
        try{
            probe.Show();await probe.OpenAsync(path);await probe.activeDisplay;
            Check(probe.metadataOperations.Count==0,"Closed information panel performs no metadata reads");probe.InfoToggle.IsChecked=true;await probe.MetadataIdle;
            Check(probe.model.MetadataText.Contains("青い空")&&probe.model.MetadataText.Contains("ぼけ")&&probe.model.MetadataText.Contains("Seed\n42"),"AI sidebar shows Unicode positive, negative and seed fields");
            Check(probe.AiMetadataText.IsReadOnly&&probe.AiMetadataText.Text==probe.model.MetadataText,"AI sidebar is selectable read-only text");
            probe.SetZoom(175);var image=probe.model.Image;var text=probe.model.MetadataText;var version=probe.metadataVersion;probe.SetZoom(200);
            Check(probe.model.MetadataText==text&&probe.metadataVersion==version&&ReferenceEquals(image,probe.model.Image),"Zoom does not reread metadata or change original image");
            probe.ApplyTheme(true);Snapshot(".metadata-dark.png");probe.ApplyTheme(false);Snapshot(".metadata-light.png");
            await probe.OpenAsync(plain);await probe.activeDisplay;await probe.MetadataIdle;Check(probe.model.MetadataText.Contains("メタデータなし")&&!probe.model.MetadataText.Contains("青い空"),"Metadata-free image clears old generated information");
            var cacheHits=probe.imageCache.MemoryHits;await probe.OpenAsync(path);await probe.activeDisplay;await probe.MetadataIdle;Check(probe.imageCache.MemoryHits>cacheHits&&probe.model.MetadataText.Contains("青い空"),"Pixel cache hit still loads original metadata");
            var old=probe.OpenAsync(path);var latest=probe.OpenAsync(plain);await Task.WhenAll(old,latest);await probe.activeDisplay;await probe.MetadataIdle;
            Check(probe.model.SelectedPath==plain&&probe.model.MetadataText.Contains("メタデータなし"),"Rapid navigation never applies old metadata to a newer image");
            await probe.OpenAsync(path);await probe.activeDisplay;await probe.MetadataIdle;image=probe.model.Image;probe.InfoToggle.IsChecked=false;
            File.Copy(Path.Combine(root,"comfy.png"),path,true);probe.InfoToggle.IsChecked=true;await probe.MetadataIdle;
            Check(ReferenceEquals(image,probe.model.Image)&&probe.model.MetadataText.Contains("変更"),"Changed source metadata is not mixed with retained displayed pixels");
            await probe.ReloadAsync();await probe.activeDisplay;await probe.MetadataIdle;Check(probe.model.MetadataText.Contains("ComfyUI prompt")&&probe.model.MetadataText.Contains("positive text"),"Reload refreshes image and metadata together");
            await probe.SetSpreadModeAsync(true);await probe.MetadataIdle;Check(probe.model.MetadataText.StartsWith("parameters.png")&&probe.model.MetadataText.Contains("ComfyUI"),"Spread metadata is explicitly for selected image");await probe.SetSpreadModeAsync(false);await probe.MetadataIdle;
            var book=Path.Combine(root,"metadata.zip");await probe.OpenAsync(new ArchiveLocation(book,"folder",true).Identity);await probe.activeDisplay;await probe.MetadataIdle;
            Check(probe.model.MetadataText.Contains("青い空")&&probe.model.MetadataText.Contains("Seed\n42"),"Archive PNG uses entry metadata without writing an extracted image");
            var inner=new ArchiveLocation(Path.Combine(root,"nested.zip"),"inner.zip",false);await probe.OpenAsync(new ArchiveLocation(inner.Identity,"folder",true).Identity);await probe.activeDisplay;await probe.MetadataIdle;
            Check(probe.model.MetadataText.Contains("青い空")&&probe.model.MetadataText.Contains("parameters.png"),"Nested archive metadata retains original entry identity");
            probe.InfoToggle.IsChecked=false;Check(probe.metadataRead is null,"Closing information panel cancels its read lifetime");
            await probe.OpenAsync(Path.Combine(root,"novel.png"));await probe.activeDisplay;probe.InfoToggle.IsChecked=true;await probe.MetadataIdle;Check(probe.model.MetadataText.Contains("NovelAI")&&probe.model.MetadataText.Contains("flower"),"NovelAI information displays in actual sidebar");
        }finally{probe.Close();await probe.TemporaryCleanup;Check(probe.metadataOperations.Count==0,"Window close waits for metadata readers to finish");}
    }
}
