using System.Runtime.InteropServices;
namespace StellaViewer.Infrastructure;

internal static class RecycleNative
{
    [DllImport("StellaViewer.FileOperations.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int RecyclePath(string path,out IntPtr identifier,out uint bytes);
    [DllImport("StellaViewer.FileOperations.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    private static extern int RestoreItem(byte[] identifier,string folder,string name);
    internal static byte[] Recycle(string path)
    {
        var hr=RecyclePath(path,out var pointer,out var bytes);
        try{Marshal.ThrowExceptionForHR(hr);if(pointer==IntPtr.Zero||bytes<2||bytes>1024*1024)throw new IOException("ゴミ箱の項目を記録できませんでした。");var data=new byte[bytes];Marshal.Copy(pointer,data,0,data.Length);return data;}
        finally{if(pointer!=IntPtr.Zero)Marshal.FreeCoTaskMem(pointer);}
    }
    internal static void Restore(byte[] identifier,string folder,string name)=>Marshal.ThrowExceptionForHR(RestoreItem(identifier,folder,name));
}
