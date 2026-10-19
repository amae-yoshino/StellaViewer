using System.Runtime.InteropServices;

namespace StellaViewer.Infrastructure;

public static class ExplorerService
{
    public static Task OpenAssociatedAsync(string path, bool allowNetwork = false) => Task.Run(() => {
        path = Path.GetFullPath(path);
        NetworkAccess.Validate(path, allowNetwork);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("対象が見つかりません。", path);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    });
    public static Task RevealAsync(string path, bool allowNetwork = false) => Task.Run(() => {
        path = Path.GetFullPath(path);
        NetworkAccess.Validate(path, allowNetwork);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("対象が見つかりません。", path);
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0));
        IntPtr item = IntPtr.Zero;
        try {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out item, 0, out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0));
        }
        finally { if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item); CoUninitialize(); }
    });
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr item, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr children, uint flags);
}
