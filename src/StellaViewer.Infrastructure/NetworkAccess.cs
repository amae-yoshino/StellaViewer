namespace StellaViewer.Infrastructure;

public static class NetworkAccess
{
    public static bool IsNetworkPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full[4..];
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network;
    }
    public static void Validate(string path, bool allowNetwork = false)
    {
        if (!allowNetwork && IsNetworkPath(path))
            throw new NotSupportedException("ネットワーク上の場所は無効です。設定の「ネットワーク」で閲覧を許可してください。");
    }
}
