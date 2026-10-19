using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Interop;
namespace StellaViewer.App;

internal enum InstanceDelivery { None,Delivered,Unavailable }
internal sealed class InstanceRouter : IDisposable
{
    internal static string Scope{get{using var identity=WindowsIdentity.GetCurrent();return "StellaViewer.Instance.78efb44e."+identity.User!.Value;}}
    private const int CopyDataMessage=0x4a,PayloadTag=0x53564f50;
    private readonly HwndSource source;
    private readonly string marker;
    private readonly Func<string?,bool> receive;
    private bool disposed;
    internal InstanceRouter(IntPtr hwnd,Func<string?,bool> receive,string? marker=null)
    {
        this.marker=marker??Scope;this.receive=receive;
        source=HwndSource.FromHwnd(hwnd)??throw new InvalidOperationException("ウィンドウがありません。");
        if(!SetProp(hwnd,this.marker,new IntPtr(Environment.ProcessId)))throw new InvalidOperationException("起動先の登録に失敗しました。");
        source.AddHook(WindowMessage);
    }
    private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(disposed||message!=CopyDataMessage||lParam==IntPtr.Zero)return IntPtr.Zero;
        var payload=Marshal.PtrToStructure<Payload>(lParam);
        if(payload.Tag!=new IntPtr(PayloadTag))return IntPtr.Zero;
        handled=true;
        if(payload.Bytes<2||payload.Bytes>65536||payload.Bytes%2!=0||payload.Data==IntPtr.Zero||Marshal.ReadInt16(payload.Data,payload.Bytes-2)!=0)return IntPtr.Zero;
        var text=Marshal.PtrToStringUni(payload.Data,payload.Bytes/2-1)??"";
        if(text.Contains('\0'))return IntPtr.Zero;
        return receive(text.Length==0?null:text)?new IntPtr(1):IntPtr.Zero;
    }
    internal static InstanceDelivery Forward(string? path,string? marker=null,bool excludeOwnProcess=true)
    {
        var scope=marker??Scope;IntPtr target=IntPtr.Zero;
        EnumWindows((hwnd,_)=>{
            var process=GetProp(hwnd,scope);
            if(process!=IntPtr.Zero&&(!excludeOwnProcess||process!=new IntPtr(Environment.ProcessId))){target=hwnd;return false;}return true;
        },IntPtr.Zero);
        if(target==IntPtr.Zero)return InstanceDelivery.None;
        var bytes=Encoding.Unicode.GetBytes((path??"")+"\0");if(bytes.Length>65536)return InstanceDelivery.Unavailable;
        var pointer=Marshal.AllocHGlobal(bytes.Length);
        try{
            Marshal.Copy(bytes,0,pointer,bytes.Length);var payload=new Payload{Tag=new IntPtr(PayloadTag),Bytes=bytes.Length,Data=pointer};
            AllowSetForegroundWindow((uint)GetProp(target,scope).ToInt64());
            var delivered=SendMessageTimeout(target,CopyDataMessage,IntPtr.Zero,ref payload,2,3000,out var result);
            return delivered!=IntPtr.Zero&&result==new IntPtr(1)?InstanceDelivery.Delivered:InstanceDelivery.Unavailable;
        }finally{Marshal.FreeHGlobal(pointer);}
    }
    public void Dispose(){if(disposed)return;RemoveProp(source.Handle,marker);source.RemoveHook(WindowMessage);disposed=true;}
    [StructLayout(LayoutKind.Sequential)]private struct Payload{public IntPtr Tag;public int Bytes;public IntPtr Data;}
    private delegate bool EnumerateWindow(IntPtr hwnd,IntPtr state);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool EnumWindows(EnumerateWindow callback,IntPtr state);
    [DllImport("user32.dll",EntryPoint="SetPropW",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetProp(IntPtr hwnd,string name,IntPtr value);
    [DllImport("user32.dll",EntryPoint="GetPropW",CharSet=CharSet.Unicode)]private static extern IntPtr GetProp(IntPtr hwnd,string name);
    [DllImport("user32.dll",EntryPoint="RemovePropW",CharSet=CharSet.Unicode)]private static extern IntPtr RemoveProp(IntPtr hwnd,string name);
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW")]private static extern IntPtr SendMessageTimeout(IntPtr hwnd,int message,IntPtr wParam,ref Payload payload,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool AllowSetForegroundWindow(uint processId);
}
