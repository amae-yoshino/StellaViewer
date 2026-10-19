using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyPsdAsync(string imagePath, string screenshotPath)
    {
        void Check(bool value,string name) { if(!value) throw new InvalidOperationException(name); }
        var root = Path.Combine(Path.GetDirectoryName(imagePath)!,"psd");
        var probe = new MainWindow(false) { WatchFoldersEnabled=false, ShowActivated=false, ShowInTaskbar=false, Left=-32000, Top=-32000 };
        try {
            probe.Show();
            await probe.OpenAsync(Path.Combine(root,"display.psd")); await probe.activeDisplay;
            Check(probe.model.HasImage && probe.decoded is {Width:640,Height:480} && probe.model.Message == "", "Physical PSD displays through WPF");
            probe.UpdateLayout();
            var content = (FrameworkElement)probe.Content;
            var snapshot = new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);
            snapshot.Render(content); SaveRender(snapshot,screenshotPath+".psd.png");
            await probe.ReloadAsync(); await probe.activeDisplay;
            Check(probe.model.HasImage && probe.decoded!.Width == 640,"PSD reload recreates display");
            var zip = Path.Combine(root,"psd.zip");
            await probe.OpenAsync(new ArchiveLocation(zip,"chapter/1.psd",false).Identity); await probe.activeDisplay;
            Check(probe.model.HasImage && probe.decoded is {Width:4,Height:2},"Archive PSD displays through WPF");
            await probe.SetSpreadModeAsync(true); await probe.activeDisplay;
            Check(probe.firstPage?.Info is not null && probe.secondPage?.Info is not null && probe.model.Image is DrawingImage,"PSD spread displays both pages");
            await probe.SetSpreadModeAsync(false);
            await probe.OpenAsync(Path.Combine(root,"oversize.psd")); await probe.activeDisplay;
            Check(!probe.model.HasImage && probe.model.Message.Contains("256 MiB"),"PSD limit displays explanatory failure");
            probe.Move(1); await probe.activeDisplay;
            Check(probe.model.HasImage,"PSD failure still permits page navigation");
            await probe.OpenAsync(imagePath); await probe.activeDisplay;
            Check(probe.model.HasImage,"Ordinary images still display after PSD error");
        }
        finally { if(!probe.closed) { probe.Close(); await probe.TemporaryCleanup; } }
    }
}
