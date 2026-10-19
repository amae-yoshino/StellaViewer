using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
namespace StellaViewer.App;

public partial class MainWindow
{
    private TrayIcon? trayIcon;
    private ContextMenu? trayMenu;
    private bool minimizeToTray,trayHidden,restoringTray;
    private WindowState trayRestoreState=WindowState.Normal;
    private void InitializeTray(bool automatic)
    {
        minimizeToTray=automatic;StateChanged+=TrayWindowStateChanged;
        Closed+=(_,_)=>{if(trayMenu is not null)trayMenu.IsOpen=false;trayIcon?.Dispose();trayIcon=null;};
    }
    private void TrayWindowStateChanged(object? sender,EventArgs e)
    {
        if(restoringTray||closed||closingInProgress)return;
        if(WindowState==WindowState.Minimized){if(minimizeToTray)StoreToTray();}
        else trayRestoreState=WindowState;
    }
    private void StoreToTrayClicked(object sender,RoutedEventArgs e)=>StoreToTray();
    private void StoreToTray()
    {
        if(trayHidden||closed||closingInProgress||fileManagementBusy)return;
        try{
            if(trayIcon is null){
                trayIcon=new TrayIcon(new WindowInteropHelper(this).EnsureHandle());
                trayIcon.RestoreRequested+=()=>Dispatcher.BeginInvoke(new Action(RestoreFromTray));
                trayIcon.MenuRequested+=()=>Dispatcher.BeginInvoke(new Action(ShowTrayMenu));
                trayIcon.RegistrationLost+=()=>Dispatcher.BeginInvoke(new Action(()=>{RestoreFromTray();model.Status="トレイアイコンを再登録できなかったため、ウィンドウを復元しました。";}));
            }
            if(!trayIcon.TryShow())throw new InvalidOperationException("タスクトレイにアイコンを登録できませんでした。");
            if(WindowState!=WindowState.Minimized)trayRestoreState=WindowState;
            trayHidden=true;Hide();
        }catch(Exception error){model.Status="トレイへ格納できませんでした："+error.Message;}
    }
    private void RestoreFromTray()
    {
        if(closed||closingInProgress||!trayHidden)return;
        restoringTray=true;
        try{if(trayMenu is not null)trayMenu.IsOpen=false;trayIcon?.Hide();trayHidden=false;Show();WindowState=trayRestoreState;if(ShowActivated)Activate();}
        finally{restoringTray=false;}
    }
    private void ShowTrayMenu()
    {
        if(closed||closingInProgress||!trayHidden)return;
        if(ShowActivated)trayIcon?.Foreground();
        if(trayMenu is not null)trayMenu.IsOpen=false;
        trayMenu=new ContextMenu{Placement=PlacementMode.MousePoint};
        if(!ShowActivated){trayMenu.Placement=PlacementMode.AbsolutePoint;trayMenu.HorizontalOffset=trayMenu.VerticalOffset=-32000;}
        foreach(var key in new[]{"PanelBackground","PanelForeground","DividerBrush","HoverBrush","SelectionBrush"})trayMenu.Resources[key]=Resources[key];
        trayMenu.Resources[typeof(MenuItem)]=Resources[typeof(MenuItem)];trayMenu.Resources[MenuItem.SeparatorStyleKey]=Resources[MenuItem.SeparatorStyleKey];
        trayMenu.SetResourceReference(BackgroundProperty,"PanelBackground");trayMenu.SetResourceReference(ForegroundProperty,"PanelForeground");trayMenu.SetResourceReference(BorderBrushProperty,"DividerBrush");
        var restore=new MenuItem{Header="復元"};restore.Click+=(_,_)=>RestoreFromTray();
        var exit=new MenuItem{Header="終了"};exit.Click+=(_,_)=>{trayMenu.IsOpen=false;Close();};
        trayMenu.Items.Add(restore);trayMenu.Items.Add(new Separator());trayMenu.Items.Add(exit);trayMenu.IsOpen=true;trayMenu.ApplyTemplate();ApplyMenuPopupTheme(trayMenu);
    }
}
