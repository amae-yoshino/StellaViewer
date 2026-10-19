using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyInstancesAsync(string imagePath)
    {
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
        var root=Path.Combine(Path.GetDirectoryName(imagePath)!,"instances",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var target=Path.Combine(root,"画像 space.png");File.Copy(imagePath,target);
        var marker="StellaViewer.Test.Instance."+Guid.NewGuid().ToString("N");
        var probe=new MainWindow(false){WatchFoldersEnabled=false,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};InstanceRouter? router=null;
        async Task<InstanceDelivery> Launch(string? path){
            var result=Path.Combine(root,Guid.NewGuid().ToString("N")+".txt");var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var argument in new[]{"--instance-probe",marker,path??"",result})start.ArgumentList.Add(argument);
            using var process=Process.Start(start)!;await process.WaitForExitAsync();Check(process.ExitCode==0,"Instance client exits cleanly");return Enum.Parse<InstanceDelivery>(await File.ReadAllTextAsync(result));
        }
        try{
            probe.Show();await probe.OpenAsync(imagePath);await probe.activeDisplay;Check(probe.allowMultipleInstances,"Multiple instances remain allowed by default");
            Check(InstanceRouter.Forward(null,marker)==InstanceDelivery.None,"Absent instance is distinguished from unavailable instance");
            router=new InstanceRouter(new WindowInteropHelper(probe).Handle,probe.ReceiveInstanceRequest,marker);
            var settings=new SettingsWindow(probe.CurrentPreferences,probe.ApplyPreferencesAsync);
            try{((CheckBox)settings.FindName("MultipleInstances")).IsChecked=false;await settings.ApplyChangesAsync();Check(!probe.allowMultipleInstances,"Instance setting applies independently");((CheckBox)settings.FindName("MultipleInstances")).IsChecked=true;settings.Close();Check(!probe.allowMultipleInstances,"Unapplied instance change is discarded");}finally{settings.Close();}
            probe.StoreToTray();Check(await Launch(target)==InstanceDelivery.Delivered,"A separate process delivers a Unicode file argument to registered window");await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await probe.instanceRequest;await probe.activeDisplay;
            Check(probe.IsVisible&&!probe.trayHidden&&probe.model.SelectedPath==target,"Existing tray window restores and opens explicit file");
            var image=probe.model.Image;probe.WindowState=WindowState.Minimized;Check(await Launch(null)==InstanceDelivery.Delivered,"Empty launch request reaches existing instance");await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await probe.instanceRequest;
            Check(probe.IsVisible&&probe.WindowState!=WindowState.Minimized&&ReferenceEquals(image,probe.model.Image),"Launch without file restores existing selection without reopening");
            probe.fileManagementBusy=true;try{Check(await Launch(target)==InstanceDelivery.Unavailable,"Busy existing window rejects new request without opening another viewer");}finally{probe.fileManagementBusy=false;}
            var reset=new SettingsWindow(probe.CurrentPreferences);reset.ResetValue("instances");Check(reset.Values.AllowMultipleInstances,"Instance reset restores allow-multiple default");reset.Close();
            var startup=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);probe.InitialStartup=startup.Task;Check(await Launch(imagePath)==InstanceDelivery.Delivered,"Launch during initial restoration is queued");await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Check(probe.model.SelectedPath==target,"Queued launch waits for startup restoration");startup.SetResult();await probe.instanceRequest;await probe.activeDisplay;Check(probe.model.SelectedPath==imagePath,"Explicit launch opens after initial restoration completes");
            router.Dispose();router=null;Check(InstanceRouter.Forward(null,marker,false)==InstanceDelivery.None,"Closing registration removes stale instance marker");
        }finally{router?.Dispose();probe.Close();await probe.TemporaryCleanup;}
    }
}
