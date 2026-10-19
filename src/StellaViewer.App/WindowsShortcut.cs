using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

internal static class WindowsShortcut
{
    internal static string Resolve(string path, bool allowNetwork)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
            path = Path.GetFullPath(path);
            NetworkAccess.Validate(path, allowNetwork);
            if (!visited.Add(path) || visited.Count > 8) throw new IOException("ショートカットが循環しているか、リンクの段数が多すぎます。");
            if (!File.Exists(path)) throw new FileNotFoundException("ショートカットが見つかりません。", path);
            var link = (IShellLink)(object)new ShellLink();
            try {
                ((IPersistFile)link).Load(path, 0);
                var target = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                if (target.Length == 0) throw new NotSupportedException("このショートカットには閲覧可能なファイルやフォルダのリンク先がありません。");
                path = Environment.ExpandEnvironmentVariables(target.ToString());
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        NetworkAccess.Validate(path, allowNetwork);
        if (visited.Count > 0) {
            if (!Directory.Exists(path) && !File.Exists(path)) throw new FileNotFoundException("ショートカットのリンク先が見つかりません。", path);
            if (!Directory.Exists(path) && !FolderBrowser.IsImageFile(path) && !FolderBrowser.IsContainerFile(path))
                throw new NotSupportedException("リンク先は閲覧に対応する画像・書庫・PDF・フォルダではありません。");
        }
        return path;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
    }
}
