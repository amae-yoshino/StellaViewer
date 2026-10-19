using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifySendFilesAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"send-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var first=Path.Combine(root,"page01.png");var second=Path.Combine(root,"page02.png");File.Copy(imagePath,first);File.Copy(imagePath,second);
        var archive=Path.Combine(root,"book.cbz");File.WriteAllText(archive,"archive test bytes");var pdf=Path.Combine(root,"document.pdf");File.WriteAllText(pdf,"PDF test bytes");
        var target=Path.Combine(root,"target");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"page01.png"),"keep existing");
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        void Snapshot(FrameworkElement content,string suffix){content.UpdateLayout();var visual=new DrawingVisual();using(var draw=visual.RenderOpen())draw.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);SaveRender(bitmap,screenshotPath+suffix);}
        try{
            probe.Show();await probe.OpenAsync(first);await probe.activeDisplay;probe.SetZoom(175);
            var selected=probe.ImageList.SelectedItem;var image=probe.model.Image;var scale=probe.zoom;
            probe.FileMenuOpened(probe,new());Check(probe.SendFileMenu.IsEnabled,"File menu enables Send for physical image");
            foreach(var entry in new object[]{new FileEntry(first,"page01.png",0,default),new LocationEntry(archive,"book.cbz",false),new LocationEntry(pdf,"document.pdf",false),new LocationEntry(root,"folder",true),new FileEntry(new ArchiveLocation(archive,"page.png",false).Identity,"inner",0,default),new FileEntry(new PdfLocation(pdf,1).Identity,"page",0,default)}){
                var menu=(ContextMenu)probe.Resources["FileContextMenu"];menu.PlacementTarget=new ListBoxItem{DataContext=entry};probe.FileContextMenuOpened(menu,new());
                Check(menu.Items.OfType<MenuItem>().Single(item=>item.Header.ToString()=="送る（コピー）…").IsEnabled==CanSend(entry),"Send menu enabled only for physical files");
            }
            var dialog=probe.CreateSendFilesWindow(first);dialog.ShowActivated=false;dialog.ShowInTaskbar=false;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;
            try{
                dialog.Show();var list=(ListBox)dialog.FindName("Sources");var destination=(TextBox)dialog.FindName("Destination");var button=(Button)dialog.FindName("CopyButton");
                Check(list.Items.Count==4&&list.SelectedItems.Count==1&&((SendSource)list.SelectedItem).Path==first,"Send offers image, archive and PDF candidates with clicked file selected and no folders");
                await dialog.CopyAsync();Check(((TextBlock)dialog.FindName("ProgressText")).Text.Contains("指定"),"Empty destination is rejected without copying");
                list.SelectAll();Check(list.SelectedItems.Count==4,"Send supports multiple selected files");destination.Text=target;
                File.Delete(second);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await dialog.ActiveCopy;
                var summary=((TextBlock)dialog.FindName("ProgressText")).Text;var details=((TextBox)dialog.FindName("ResultDetails")).Text;
                Check(summary.Contains("成功 3件")&&summary.Contains("失敗 1件")&&summary.Contains("名前変更 1件")&&details.Contains("page01 (1).png")&&details.Contains(second),"Send reports successful, failed and renamed copies with details");
                Check(File.ReadAllText(Path.Combine(target,"page01.png"))=="keep existing"&&File.ReadAllBytes(Path.Combine(target,"page01 (1).png")).SequenceEqual(File.ReadAllBytes(first)),"Send UI preserves collision target and source bytes");
                Check(ReferenceEquals(selected,probe.ImageList.SelectedItem)&&ReferenceEquals(image,probe.model.Image)&&Math.Abs(scale-probe.zoom)<.01,"Send leaves viewing selection, image and zoom unchanged");
                probe.ApplyTheme(true);probe.CopySettingsTheme(dialog);Snapshot((FrameworkElement)dialog.Content,".send-dark.png");
                probe.ApplyTheme(false);probe.CopySettingsTheme(dialog);Snapshot((FrameworkElement)dialog.Content,".send-light.png");
            }finally{dialog.Close();}
            var cancel=new SendFilesWindow([new SendSource(first,"page01.png")],first,async(_,_,token,_)=>{await Task.Delay(Timeout.Infinite,token);return new SendReport("","");}){Owner=probe,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};probe.CopySettingsTheme(cancel);
            try{
                cancel.Show();((TextBox)cancel.FindName("Destination")).Text=target;var active=cancel.CopyAsync();
                Check(!((ListBox)cancel.FindName("Sources")).IsEnabled&&((Button)cancel.FindName("CloseButton")).Content.ToString()=="中止","Send disables editing while copy runs and offers cancellation");
                cancel.Close();await active;
                Check(cancel.IsVisible&&((TextBlock)cancel.FindName("ProgressText")).Text.Contains("中止")&&((Button)cancel.FindName("CopyButton")).IsEnabled,"Closing during copy cancels and waits before allowing dialog to close");
            }finally{cancel.Close();}
        }finally{probe.Close();await probe.TemporaryCleanup;}
    }
}
