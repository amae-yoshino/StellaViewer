using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace StellaViewer.App;

public partial class AboutWindow : Window
{
    internal Action<string> CopyText { get; set; } = Clipboard.SetText;
    internal string Information { get; }
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = typeof(AboutWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "不明";
        var parts = version.Split('+', 2);
        var os = WindowsInformation();
        Information = string.Join(Environment.NewLine,
            "StellaViewer", $"バージョン: {parts[0]}", "",
            "実行環境", $"OS: {os.Name}",
            $"Windowsのバージョン: {os.Release}", $"OSビルド: {os.Build}",
            $"OSの種類: {ArchitectureName(RuntimeInformation.OSArchitecture)}",
            $"アプリの動作形式: {ArchitectureName(RuntimeInformation.ProcessArchitecture)}",
            $".NETランタイム: {RuntimeInformation.FrameworkDescription}", "",
            "ビルド情報", $"アセンブリバージョン: {assembly.GetName().Version}",
            $"ビルド識別子: {(parts.Length == 2 ? parts[1] : "なし")}", "画面表示の技術: WPF");
        VersionDetails.Text = Information;
        try { LibraryNotices.Text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            LibraryNotices.Text = "使用ライブラリ情報を読み込めませんでした。配布フォルダの THIRD-PARTY-NOTICES.md と licenses フォルダを確認してください。";
        }
    }
    private static string ArchitectureName(Architecture architecture) => architecture.ToString().ToLowerInvariant()
        + (architecture is Architecture.X64 or Architecture.Arm64 ? "（64ビット）" : architecture is Architecture.X86 or Architecture.Arm ? "（32ビット）" : "");
    private static (string Name, string Release, string Build) WindowsInformation()
    {
        var name = RuntimeInformation.OSDescription;
        var release = "不明";
        var version = Environment.OSVersion.Version;
        var build = $"{version.Major}.{version.Minor}.{version.Build}";
        try {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("ProductName") is string product && !string.IsNullOrWhiteSpace(product)) {
                // Windows 11 may retain "Windows 10" in this registry value. Do not relabel Server editions.
                name = version.Build >= 22000 && key.GetValue("InstallationType") is "Client"
                    ? product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal) : product;
            }
            if (key?.GetValue("DisplayVersion") is string display && !string.IsNullOrWhiteSpace(display)) release = display;
            if (key?.GetValue("UBR") is int revision) build += $".{revision}";
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return (name, release, build);
    }
    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        try { CopyText(Information); CopyStatus.Text = "バージョン・実行環境の情報をコピーしました。"; }
        catch (Exception error) when (error is ExternalException) { CopyStatus.Text = "コピーできませんでした。少し待ってから再試行してください。"; }
    }
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
