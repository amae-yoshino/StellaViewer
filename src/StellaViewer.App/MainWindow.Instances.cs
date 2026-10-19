using System.Windows;
using System.Windows.Interop;
namespace StellaViewer.App;
public partial class MainWindow
{
    private bool allowMultipleInstances=true;
    private InstanceRouter? instanceRouter;
    private Task instanceRequest=Task.CompletedTask;
    internal Task InitialStartup { get; set; }=Task.CompletedTask;
    private void InitializeInstanceRouting()
    {
        if(!usePreferences||storageDirectory is not null)return; // UI probes never claim ordinary application requests.
        SourceInitialized+=(_,_)=>instanceRouter=new InstanceRouter(new WindowInteropHelper(this).Handle,ReceiveInstanceRequest);
        Closed+=(_,_)=>{instanceRouter?.Dispose();instanceRouter=null;};
    }
    private bool ReceiveInstanceRequest(string? path)
    {
        if(closed||closingInProgress||fileManagementBusy)return false;
        Dispatcher.BeginInvoke(new Action(()=>instanceRequest=OpenInstanceRequestAsync(path)));return true;
    }
    private async Task OpenInstanceRequestAsync(string? path)
    {
        await InitialStartup;
        if(closed||closingInProgress)return;
        RestoreFromTray();
        restoringTray=true;
        try{if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=trayRestoreState;if(ShowActivated)Activate();}
        finally{restoringTray=false;}
        if(path is not null)await OpenAsync(path);
    }
}
