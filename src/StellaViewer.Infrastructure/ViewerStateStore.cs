using System.Text.Json;

namespace StellaViewer.Infrastructure;

public sealed record WindowLayout(double? Left = null, double? Top = null, double Width = 1200, double Height = 800,
    bool Maximized = false, double LeftPaneWidth = 400, double FolderPaneHeight = 230, double TreeFraction = 1 / 2.2, bool InfoOpen = false, bool TreeVisible = true)
{
    public double InfoPanelWidth { get; init; } = 320;
    public WindowLayout Normalize() => this with {
        Left = Left is double x && double.IsFinite(x) && Math.Abs(x) < 1_000_000 ? x : null,
        Top = Top is double y && double.IsFinite(y) && Math.Abs(y) < 1_000_000 ? y : null,
        Width = Limit(Width, 800, 16000, 1200), Height = Limit(Height, 500, 16000, 800),
        LeftPaneWidth = Limit(LeftPaneWidth, 280, 2000, 400), FolderPaneHeight = Limit(FolderPaneHeight, 100, 10000, 230),
        TreeFraction = Limit(TreeFraction, .15, .85, 1 / 2.2),
        InfoPanelWidth = Limit(InfoPanelWidth, 120, 2000, 320)
    };
    private static double Limit(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
public sealed record LastView(string Folder = "", string? SelectedPath = null, string? LocationFolder = null);
public sealed record SavedViewerState(WindowLayout Layout, LastView View);
public sealed record StartupSelection(string? Path, string? Notification);

public static class ViewerStateStore
{
    private static string StateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellaViewer");
    public static Task ClearViewAsync(string? directory = null) => Task.Run(() => Locked(() => {
        var root = directory ?? StateDirectory; Directory.CreateDirectory(root);
        var path = Path.Combine(root, "last-view.json");
        // Both generations must be empty, otherwise recovery could revive a deleted location.
        RecoverableSettings.Write(path, new LastView()); RecoverableSettings.Write(path, new LastView());
        foreach (var pattern in new[] { "last-view.json.corrupt-*", "last-view.json.bak.corrupt-*" })
            foreach (var file in Directory.EnumerateFiles(root, pattern)) File.Delete(file);
        return true;
    }));
    public static SavedViewerState Load(string? directory = null) => Locked(() => {
        var root = directory ?? StateDirectory;
        return new SavedViewerState(Read(Path.Combine(root, "window-layout.json"), new WindowLayout()).Normalize(),
            RecoverableSettings.Read(Path.Combine(root, "last-view.json"), () => new LastView(), value => value.Folder is not null));
    });
    public static Task SaveAsync(WindowLayout layout, LastView view, string? directory = null, bool saveView = true, Func<bool>? savingAllowed = null) => Task.Run(() => Locked(() => {
        var root = directory ?? StateDirectory;
        Directory.CreateDirectory(root);
        Write(Path.Combine(root, "window-layout.json"), layout.Normalize());
        if (saveView && (savingAllowed is null || savingAllowed())) Write(Path.Combine(root, "last-view.json"), view);
        return true;
    }));
    private static T Locked<T>(Func<T> operation)
    {
        using var mutex = new Mutex(false, @"LocalStellaViewer.LastSession");
        var acquired = false;
        try {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("前回状態の保存待ちがタイムアウトしました。");
            return operation();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
    private static T Read<T>(string path, T fallback) => RecoverableSettings.Read(path, () => fallback);
    private static void Write<T>(string path, T value) => RecoverableSettings.Write(path, value);
    public static StartupSelection ResolveStartup(LastView view, string? explicitPath, bool restoreLast, bool allowNetwork = false)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return new(explicitPath, null);
        if (!restoreLast || (view.Folder.Length == 0 && string.IsNullOrEmpty(view.SelectedPath))) return new(null, null);
        try {
            var pdf = PdfLocation.Parse(view.SelectedPath ?? "") ?? PdfLocation.Parse(view.Folder);
            if (pdf is not null) {
                var physical = BrowsePath.PhysicalPath(pdf.Identity);
                NetworkAccess.Validate(physical, allowNetwork);
                if (File.Exists(physical)) return new(pdf.Identity, null);
                var parent = Path.GetDirectoryName(physical)!;
                return Directory.Exists(parent) ? new(parent, "前回のPDFが見つからないため、親フォルダを開きました。")
                    : new(null, "前回のPDFと親フォルダが見つかりません。");
            }
            var archive = ArchiveLocation.Parse(view.SelectedPath ?? "") ?? ArchiveLocation.Parse(view.Folder);
            if (archive is not null) {
                var physical = ArchiveLocation.PhysicalPath(archive.Identity);
                NetworkAccess.Validate(physical, allowNetwork);
                if (File.Exists(physical)) return new(archive.Identity, null);
                var parent = Path.GetDirectoryName(physical)!;
                return Directory.Exists(parent) ? new(parent, "前回の書庫が見つからないため、親フォルダを開きました。")
                    : new(null, "前回の書庫と親フォルダが見つかりません。");
            }
            if (!string.IsNullOrEmpty(view.SelectedPath)) {
                NetworkAccess.Validate(view.SelectedPath, allowNetwork);
                if (File.Exists(view.SelectedPath)) return new(view.SelectedPath, null);
            }
            var folder = view.Folder.Length > 0 ? view.Folder : Path.GetDirectoryName(view.SelectedPath!)!;
            NetworkAccess.Validate(folder, allowNetwork);
            if (Directory.Exists(folder)) return new(folder, view.SelectedPath is null ? null : "前回の画像が見つからないため、親フォルダを開きました。");
            return new(null, "前回のフォルダが見つかりません。フォルダまたは画像を開いてください。");
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        { return new(null, "前回の場所を復元できませんでした：" + error.Message); }
    }
}
