using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyTrayAsync(string imagePath,string screenshotPath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        async Task Idle()=>await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
        void Snapshot(FrameworkElement content,string suffix){content.UpdateLayout();var visual=new DrawingVisual();using(var draw=visual.RenderOpen())draw.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));var bitmap=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);SaveRender(bitmap,screenshotPath+suffix);}
        void Notify(int message)=>TrayProbeMessage(new WindowInteropHelper(probe).Handle,TrayIcon.CallbackMessage,IntPtr.Zero,new IntPtr((1<<16)|message));
        try{
            probe.Show();await probe.OpenAsync(imagePath);await probe.activeDisplay;probe.SetZoom(175);
            var image=probe.model.Image;var selection=probe.ImageList.SelectedItem;var scale=probe.zoom;
            Check(!probe.minimizeToTray,"Automatic tray minimization defaults off");
            probe.WindowState=WindowState.Minimized;await Idle();Check(probe.IsVisible&&!probe.trayHidden&&probe.trayIcon is null,"Ordinary minimization remains unchanged by default");probe.WindowState=WindowState.Normal;
            probe.StoreToTray();Check(probe.trayHidden&&!probe.IsVisible&&probe.trayIcon is{Registered:true},"Manual tray action registers real Shell icon before hiding window");
            Notify(0x203);await Idle();Check(probe.IsVisible&&!probe.trayHidden&&probe.trayIcon is{Registered:false},"Shell double-click callback restores window and removes tray icon");
            Check(ReferenceEquals(image,probe.model.Image)&&ReferenceEquals(selection,probe.ImageList.SelectedItem)&&Math.Abs(scale-probe.zoom)<.01,"Tray round trip preserves selected image, bitmap and zoom");
            var settings=new SettingsWindow(probe.CurrentPreferences,probe.ApplyPreferencesAsync){Owner=probe,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};probe.CopySettingsTheme(settings);
            try{
                settings.Show();var checkbox=(CheckBox)settings.FindName("TrayMinimize");Check(checkbox.IsChecked==false,"Tray setting starts unchecked");checkbox.IsChecked=true;await settings.ApplyChangesAsync();Check(probe.minimizeToTray&&settings.IsVisible,"Tray Apply changes setting without closing dialog");
                checkbox.BringIntoView();probe.ApplyTheme(true);probe.CopySettingsTheme(settings);Snapshot((FrameworkElement)settings.Content,".tray-settings-dark.png");probe.ApplyTheme(false);probe.CopySettingsTheme(settings);Snapshot((FrameworkElement)settings.Content,".tray-settings-light.png");
                checkbox.IsChecked=false;settings.Close();Check(probe.minimizeToTray,"Cancel discards unapplied tray change");
            }finally{settings.Close();}
            probe.WindowState=WindowState.Maximized;probe.WindowState=WindowState.Minimized;await Idle();Check(probe.trayHidden&&probe.trayRestoreState==WindowState.Maximized,"Configured minimize stores previous maximized state");
            TrayProbeMessage(new WindowInteropHelper(probe).Handle,(int)TrayIcon.TaskbarCreated,IntPtr.Zero,IntPtr.Zero);await Idle();Check(probe.trayIcon is{Registered:true}&&probe.trayHidden,"Simulated taskbar recreation re-registers only this window icon");
            Notify(0x401);await Idle();Check(probe.WindowState==WindowState.Maximized&&probe.IsVisible,"Keyboard tray callback restores maximized window");probe.WindowState=WindowState.Normal;
            probe.StoreToTray();Notify(0x7b);await Idle();Check(probe.trayMenu is{IsOpen:true}&&probe.trayMenu.Items.OfType<MenuItem>().Select(item=>item.Header.ToString()).SequenceEqual(new[]{"復元","終了"}),"Tray right-click opens Restore and Exit menu");
            probe.trayMenu!.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(probe.IsVisible&&!probe.trayHidden,"Tray menu Restore works");
            var second=new MainWindow(false){ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
            try{second.Show();probe.StoreToTray();second.StoreToTray();Check(probe.trayIcon is{Registered:true}&&second.trayIcon is{Registered:true},"Multiple windows own independent tray entries");second.RestoreFromTray();Check(probe.trayHidden&&probe.trayIcon is{Registered:true},"Restoring one window leaves another tray entry intact");}finally{second.Close();await second.TemporaryCleanup;}
            probe.RestoreFromTray();var reset=new SettingsWindow(probe.CurrentPreferences);reset.ResetValue("tray");Check(!reset.Values.MinimizeToTray,"Tray reset returns to normal minimization");reset.Close();
            var store=Path.Combine(Path.GetDirectoryName(imagePath)!,"tray-state-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(store);
            await PreferencesStore.SaveChangeAsync(filePath:Path.Combine(store,"viewer-preferences.json"),minimizeToTray:true);
            var restored=new MainWindow(true,store){ConstrainStartupWindowToScreen=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};
            try{restored.Show();Check(restored.minimizeToTray,"New window restores persisted tray preference");restored.StoreToTray();var registered=restored.trayIcon;restored.Close();await restored.closePersistence;await restored.TemporaryCleanup;Check(restored.closed&&registered is{Registered:false},"Closing from tray saves state and removes icon");}finally{if(!restored.closed){restored.Close();await restored.closePersistence;}}
            probe.StoreToTray();Notify(0x7b);await Idle();var iconBeforeClose=probe.trayIcon;probe.trayMenu!.Items.OfType<MenuItem>().Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await probe.TemporaryCleanup;Check(probe.closed&&iconBeforeClose is{Registered:false},"Tray Exit closes window and removes icon");
        }finally{if(!probe.closed){probe.Close();await probe.TemporaryCleanup;}}
    }
    [DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern IntPtr TrayProbeMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
}
