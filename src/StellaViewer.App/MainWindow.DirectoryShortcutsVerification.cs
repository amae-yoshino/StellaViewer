using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyDirectoryShortcutsAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"directory-shortcuts-ui",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var target=Path.Combine(root,"作品");var missing=Path.Combine(root,"一時フォルダ");Directory.CreateDirectory(target);Directory.CreateDirectory(missing);
        File.Copy(imagePath,Path.Combine(target,"image.png"));
        var probe=new MainWindow(false,Path.Combine(root,"state")){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        var external=new DirectoryShortcutStore(Path.Combine(root,"state","directory-shortcuts.json"));
        void Snapshot(FrameworkElement content,string suffix){content.UpdateLayout();var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(content);SaveRender(bitmap,screenshotPath+suffix);}
        try{
            probe.Show();await probe.OpenAsync(imagePath);await probe.activeDisplay;
            await probe.AddDirectoryShortcutAsync(target);await probe.AddDirectoryShortcutAsync(missing);
            await external.AddAsync(@"\\stella-shortcuts.invalid\share");Directory.Delete(missing);
            var original=probe.model.Image;
            await probe.ShowDirectoryShortcutsAsync();await probe.RefreshChangedDirectoryShortcutsAsync();probe.UpdateLayout();
            Check(probe.model.DirectoryShortcuts.Count==3&&probe.model.DirectoryShortcuts.Single(e=>e.Path==missing).Availability==FavoriteAvailability.Missing,"Directory shortcut list keeps missing folder visible");
            Check(probe.model.DirectoryShortcuts.Single(e=>e.Path.StartsWith(@"\\")).Availability==FavoriteAvailability.NetworkBlocked,"Blocked network directory remains visible without probing");
            Check(probe.model.Favorites.Count==0&&ReferenceEquals(original,probe.model.Image)&&probe.DirectoryShortcutsPanel.IsVisible&&!probe.FolderBrowserPanel.IsVisible,"Dedicated directories are independent of favorites and do not change current image on toggle");
            var dialog=probe.CreateDirectoryShortcutsManagementWindow();dialog.ShowActivated=false;dialog.ShowInTaskbar=false;
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-32000;
            try{
                dialog.Show();var list=(ListBox)dialog.FindName("DirectoryManagementList");var name=(TextBox)dialog.FindName("DirectoryDisplayName");
                list.SelectedItem=probe.model.DirectoryShortcuts.Single(e=>e.Path==target);name.Text="イラスト置き場";
                ((Button)dialog.FindName("RenameDirectoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await probe.directoryManagementAction;
                Check((await external.LoadAsync()).Single(e=>e.Path==target).Name=="イラスト置き場"&&Directory.Exists(target)&&!Directory.Exists(Path.Combine(root,"イラスト置き場")),"Editor renames display label without renaming target folder");
                list.SelectedItem=probe.model.DirectoryShortcuts.Single(e=>e.Path==target);name.Text=" ";
                ((Button)dialog.FindName("RenameDirectoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await probe.directoryManagementAction;
                Check(((TextBlock)dialog.FindName("DirectoryManagementError")).Text.Length>0&&name.IsEnabled,"Blank shortcut name is rejected without blocking editor");
                name.Text="イラスト置き場";probe.ApplyTheme(true);probe.CopySettingsTheme(dialog);Snapshot((FrameworkElement)dialog.Content,".directory-editor.png");
                list.SelectedItem=probe.model.DirectoryShortcuts.Single(e=>e.Path==missing);
                ((Button)dialog.FindName("RemoveDirectoryButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await probe.directoryManagementAction;
                Check(probe.model.DirectoryShortcuts.All(e=>e.Path!=missing),"Editor removes unavailable directory registration");
            }finally{dialog.Close();}
            await external.RenameAsync(target,"別ウィンドウで変更");await Task.Delay(2400);await probe.directoryRefresh;
            Check(probe.model.DirectoryShortcuts.Single(e=>e.Path==target).Name=="別ウィンドウで変更","Visible directory list polls shared name changes");
            probe.DirectoryShortcutList.SelectedItem=probe.model.DirectoryShortcuts.Single(e=>e.Path==target);await probe.navigation;await probe.activeDisplay;
            Check(probe.model.Folder==target&&probe.model.LocationFolder==target&&probe.model.HasImage,"Directory shortcut opens folder and direct image list");
            await probe.OpenAsync(imagePath);await probe.activeDisplay;
            probe.DirectoryShortcutList.SelectedItem=probe.model.DirectoryShortcuts.Single(e=>e.Path==target);await probe.navigation;await probe.activeDisplay;
            Check(probe.model.Folder==target,"Directory shortcut can be selected again after external navigation");
            await external.AddAsync(missing);await probe.RefreshChangedDirectoryShortcutsAsync();var pixels=probe.model.Image;
            await probe.OpenDirectoryShortcutAsync(probe.model.DirectoryShortcuts.Single(e=>e.Path==missing));
            Check(ReferenceEquals(pixels,probe.model.Image)&&probe.model.Status.Contains("保持"),"Unavailable directory selection preserves loaded image and registration");
            Directory.CreateDirectory(missing);await Task.Delay(2400);await probe.directoryRefresh;
            Check(probe.model.DirectoryShortcuts.Single(e=>e.Path==missing).IsAvailable,"Directory becomes usable again at original path");
            probe.UpdateLayout();probe.SetZoom(200);probe.UpdateLayout();var before=probe.ZoomSlider.Value;
            var thumb=Descendants(probe.ZoomSlider).OfType<Thumb>().Single();
            thumb.RaiseEvent(new DragDeltaEventArgs(12,0){RoutedEvent=Thumb.DragDeltaEvent});
            Check(probe.ZoomSlider.Value>before&&probe.zoom>200,"Circular zoom thumb retains drag behavior");
            var increased=probe.ZoomSlider.Value;Slider.DecreaseLarge.Execute(null,probe.ZoomSlider);
            Check(probe.ZoomSlider.Value<increased,"Zoom track still supports slider page command");probe.ResetZoom();
            Snapshot((FrameworkElement)probe.Content,".directories-dark.png");
            probe.ApplyTheme(false);Snapshot((FrameworkElement)probe.Content,".directories-light.png");
            await probe.ShowFavoritesAsync(true);
            Check(!probe.DirectoryShortcutsPanel.IsVisible&&!probe.directoryTimer!.IsEnabled&&probe.FavoritesPanel.IsVisible,"Switching to favorites stops directory polling");
            await probe.ShowFavoritesAsync(false);Check(probe.FolderBrowserPanel.IsVisible,"Folder view returns after directory shortcuts");
            await probe.ShowDirectoryShortcutsAsync();
        }finally{probe.Close();await probe.TemporaryCleanup;}
        Check(!probe.directoryTimer!.IsEnabled&&probe.directoryLifetime.IsCancellationRequested,"Directory shortcut window close stops refresh work");
    }
}
