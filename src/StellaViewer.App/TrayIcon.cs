using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StellaViewer.App;

// The window owns the HICON. This class only borrows it and removes its own tray entry.
internal sealed class TrayIcon : IDisposable
{
    internal const int CallbackMessage=0x8000+73;
    internal static readonly uint TaskbarCreated=RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource source;
    private NotifyIconData data;
    private bool disposed;
    internal bool Registered { get; private set; }
    internal event Action? RestoreRequested,MenuRequested,RegistrationLost;
    internal TrayIcon(IntPtr handle)
    {
        source=HwndSource.FromHwnd(handle)??throw new InvalidOperationException("ウィンドウが準備されていません。");
        var icon=SendMessage(handle,0x7f,new IntPtr(2),IntPtr.Zero);
        if(icon==IntPtr.Zero)icon=SendMessage(handle,0x7f,new IntPtr(1),IntPtr.Zero);
        if(icon==IntPtr.Zero)throw new InvalidOperationException("アプリのアイコンを取得できませんでした。");
        data=new(){Size=(uint)Marshal.SizeOf<NotifyIconData>(),Window=handle,Id=1,Flags=1|2|4|0x80,Callback=(uint)CallbackMessage,Icon=icon,Tip="StellaViewer",Info="",InfoTitle=""};
        source.AddHook(WindowMessage);
    }
    internal bool TryShow()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(Registered)return true;
        if(!ShellNotifyIcon(0,ref data))return false;
        Registered=true;data.Version=4;
        if(!ShellNotifyIcon(4,ref data)){Hide();return false;}
        return true;
    }
    internal void Hide(){if(Registered){ShellNotifyIcon(2,ref data);Registered=false;}}
    private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(disposed)return IntPtr.Zero;
        if((uint)message==TaskbarCreated&&Registered){Hide();if(!TryShow())RegistrationLost?.Invoke();}
        if(message==CallbackMessage&&Registered){
            if(((long)lParam>>16&0xffff)!=data.Id)return IntPtr.Zero;
            var notification=(int)((long)lParam&0xffff);
            if(notification==0x203||notification==0x401)RestoreRequested?.Invoke(); // double click / keyboard select
            else if(notification==0x7b)MenuRequested?.Invoke();
            handled=true;
        }
        return IntPtr.Zero;
    }
    internal void Foreground()=>SetForegroundWindow(data.Window);
    public void Dispose(){if(disposed)return;Hide();source.RemoveHook(WindowMessage);disposed=true;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;public IntPtr Window;public uint Id,Flags,Callback;public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool ShellNotifyIcon(uint message,ref NotifyIconData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetForegroundWindow(IntPtr window);
}
