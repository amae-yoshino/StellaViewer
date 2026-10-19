using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;
public partial class MainWindow
{
    internal async Task VerifyDeletionAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"delete-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var pages=Enumerable.Range(1,3).Select(i=>Path.Combine(root,$"page{i:D2}.png")).ToArray();foreach(var page in pages)File.Copy(imagePath,page);
        var folder=Path.Combine(root,"folder");Directory.CreateDirectory(folder);File.Copy(imagePath,Path.Combine(folder,"inside.png"));
        var probe=new MainWindow(false){WatchFoldersEnabled=true,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        void Snapshot(FrameworkElement content,string suffix){content.UpdateLayout();var visual=new DrawingVisual();using(var draw=visual.RenderOpen())draw.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);SaveRender(bitmap,screenshotPath+suffix);}
        void Hide(Window window){window.ShowActivated=false;window.ShowInTaskbar=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=window.Top=-32000;window.Show();}
        try{
            probe.Show();await probe.OpenAsync(pages[1]);await probe.activeDisplay;
            probe.UpdateDeleteMenus();Check(!probe.DeleteFileMenu.IsEnabled&&!probe.CurrentPreferences.DeleteEnabled,"Deletion defaults off in menus and preferences");
            try{await probe.DeletePathsAsync([pages[1]]);throw new InvalidOperationException("Disabled deletion ran");}catch(InvalidOperationException error)when(error.Message.Contains("無効")){ }
            Check(File.Exists(pages[1]),"Disabled deletion cannot reach Shell service");
            var settings=new SettingsWindow(probe.CurrentPreferences,probe.ApplyPreferencesAsync){Owner=probe};probe.CopySettingsTheme(settings);
            try{Hide(settings);var enabled=(CheckBox)settings.FindName("DeleteEnabled");Check(enabled.IsChecked==false,"Settings shows disabled deletion initially");enabled.IsChecked=true;await settings.ApplyChangesAsync();Check(probe.deleteEnabled&&probe.DeleteFileMenu.IsEnabled,"Apply enables deletion without closing settings");enabled.IsChecked=false;settings.Close();Check(probe.deleteEnabled,"Cancel discards unapplied deletion setting");}finally{settings.Close();}
            var dialog=probe.CreateDeleteWindow(pages[1]);
            try{
                Hide(dialog);var execute=(Button)dialog.FindName("DeleteExecute");var confirm=(CheckBox)dialog.FindName("DeleteAcknowledge");var list=(ListBox)dialog.FindName("DeleteSources");
                Check(!execute.IsEnabled&&confirm.IsChecked!=true&&list.Items.OfType<SendSource>().Any(item=>item.Path==folder),"Delete screen requires confirmation and includes folders");
                probe.ApplyTheme(true);probe.CopySettingsTheme(dialog);Snapshot((FrameworkElement)dialog.Content,".delete-dark.png");probe.ApplyTheme(false);probe.CopySettingsTheme(dialog);Snapshot((FrameworkElement)dialog.Content,".delete-light.png");
                confirm.IsChecked=true;execute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await probe.deletionWork;
                Check(!File.Exists(pages[1])&&(probe.ImageList.SelectedItem as FileEntry)?.Path==pages[2]&&probe.recycleService.UndoCount==1,"Confirmed deletion selects next image and retains undo batch");
            }finally{dialog.Close();}
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{DeleteEnabled=false});probe.UpdateDeleteMenus();Check(!probe.DeleteFileMenu.IsEnabled&&probe.UndoDeleteMenu.IsEnabled,"Disabling deletion keeps undo available");
            await probe.RestoreDeletionAsync();Check(File.Exists(pages[1])&&probe.recycleService.UndoCount==0,"Undo works while new deletion is disabled");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with{DeleteEnabled=true});await probe.OpenAsync(pages[2]);await probe.activeDisplay;
            await probe.DeletePathsAsync([pages[2]]);Check((probe.ImageList.SelectedItem as FileEntry)?.Path==pages[1],"Deleting last image selects previous image");await probe.RestoreDeletionAsync();
            await probe.OpenAsync(pages[0]);await probe.activeDisplay;await probe.SetSpreadModeAsync(true);
            await probe.DeletePathsAsync([pages[1]]);Check(!probe.model.Entries.Any(item=>item.Path==pages[1])&&(probe.ImageList.SelectedItem as FileEntry)?.Path==pages[0],"Deleting spread partner preserves start image and rebuilds spread");await probe.RestoreDeletionAsync();await probe.SetSpreadModeAsync(false);
            var multiple=probe.CreateDeleteWindow(pages[0]);
            try{
                Hide(multiple);var list=(ListBox)multiple.FindName("DeleteSources");list.SelectAll();var confirm=(CheckBox)multiple.FindName("DeleteAcknowledge");confirm.IsChecked=true;((Button)multiple.FindName("DeleteExecute")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await probe.deletionWork;
                Check(pages.All(path=>!File.Exists(path))&&!Directory.Exists(folder)&&probe.model.Entries.Count==0&&probe.model.Image is null,"Multiple deletion includes folder and clears image when no pages remain");
                Check(probe.recycleService.UndoCount==4,"Multiple deletion is one undo batch");
            }finally{multiple.Close();}
            await probe.RestoreDeletionAsync();Check(pages.All(File.Exists)&&File.Exists(Path.Combine(folder,"inside.png")),"One undo restores multiple files and folder together");
            await probe.OpenAsync(Path.Combine(folder,"inside.png"));await probe.activeDisplay;var parentResult=await probe.DeletePathsAsync([folder]);Check(probe.model.Image is null&&probe.model.Folder.Length==0,"Deleting opened parent folder clears removed image container: "+OperationDetails(parentResult,false)+$" folder={probe.model.Folder}, image={probe.model.Image is not null}");await probe.RestoreDeletionAsync();
            var reset=new SettingsWindow(probe.CurrentPreferences);reset.ResetValue("delete");Check(!reset.Values.DeleteEnabled,"Deletion setting resets independently to disabled");reset.Close();
            var isolated=new MainWindow(false){ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};try{isolated.Show();Check(isolated.recycleService.UndoCount==0,"New windows do not inherit another deletion history");}finally{isolated.Close();await isolated.TemporaryCleanup;}
        }finally{
            if(probe.recycleService.UndoCount>0)await probe.recycleService.RestoreAsync();probe.Close();await probe.TemporaryCleanup;
        }
    }
}
