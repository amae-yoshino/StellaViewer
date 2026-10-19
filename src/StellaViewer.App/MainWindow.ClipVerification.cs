using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyClipAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"clip");
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        try {
            probe.Show();
            await probe.OpenAsync(Path.Combine(root,"display.clip"));await probe.activeDisplay;
            Check(probe.model.HasImage&&probe.decoded is {Width:640,Height:480,OriginalWidth:1600,OriginalHeight:1200},"CLIP preview displays at stored pixel dimensions");
            Check(probe.model.Status.Contains("元画像より縮小")&&probe.model.Details.Contains("640×480px")&&probe.model.Details.Contains("再合成は行っていません"),"CLIP reduced preview has persistent status and details notice");
            probe.SetZoom(200);Check(probe.model.Status.Contains("元画像より縮小"),"CLIP notice survives zoom");probe.ResetZoom();
            probe.UpdateLayout();var content=(FrameworkElement)probe.Content;
            var snapshot=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
            snapshot.Render(content);SaveRender(snapshot,screenshotPath+".clip.png");
            await probe.ReloadAsync();await probe.activeDisplay;
            Check(probe.model.HasImage&&probe.model.Status.Contains("元画像より縮小"),"CLIP reload restores preview and notice");
            var zip=Path.Combine(root,"images.zip");
            await probe.OpenAsync(new ArchiveLocation(zip,"chapter/1.clip",false).Identity);await probe.activeDisplay;
            Check(probe.model.HasImage&&!probe.model.Status.Contains("元画像より縮小"),"CLIP full-size stored image is not marked reduced");
            await probe.SetSpreadModeAsync(true);await probe.activeDisplay;
            Check(probe.firstPage?.Info is not null&&probe.secondPage?.Info is not null&&probe.model.Image is DrawingImage
                &&probe.model.Status.Contains("元画像より縮小"),"CLIP spread preserves each preview's source information");
            var store=await probe.GetTemporaryStoreAsync();Check(store.UsedBytes==0,"CLIP archive decode does not create temporary input files");
            await probe.SetSpreadModeAsync(false);
            await probe.OpenAsync(Path.Combine(root,"navigation","1-limit.clip"));await probe.activeDisplay;
            Check(!probe.model.HasImage&&probe.model.Message.Contains("256 MiB"),"CLIP over-limit failure explains reason");
            probe.Move(1);await probe.activeDisplay;Check(probe.model.HasImage,"CLIP error allows next page");
            await probe.OpenAsync(Path.Combine(root,"missing.clip"));await probe.activeDisplay;
            Check(!probe.model.HasImage&&probe.model.Message.Contains("保存済み画像がありません"),"CLIP absent preview fails without recomposition");
            await probe.OpenAsync(imagePath);await probe.activeDisplay;
            Check(probe.model.HasImage&&!probe.model.Status.Contains("CLIP保存済み"),"Normal PNG clears CLIP notice");
        }finally{if(!probe.closed){probe.Close();await probe.TemporaryCleanup;}}
    }
}
