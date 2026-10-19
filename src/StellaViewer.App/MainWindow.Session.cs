using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private readonly string? storageDirectory, preferenceFilePath;
    private SavedViewerState startupState = new(new(), new());
    private WindowLayout? preFullscreenLayout;
    private string? startupWarning;
    private bool restoreLastView = true, closingInProgress, closeCommitted, wasMaximized;
    private Task preferenceSaveTail = Task.CompletedTask, closePersistence = Task.CompletedTask;
    internal bool ConstrainStartupWindowToScreen { get; set; } = true;
    private Task SavePreferencesAsync(Func<Task> operation)
    {
        var previous = preferenceSaveTail;
        return preferenceSaveTail = SaveAfterAsync(previous, operation);
    }
    private static async Task SaveAfterAsync(Task previous, Func<Task> operation)
    {
        try { await previous; } catch { /* The initiating UI operation reports its failure. */ }
        await operation();
    }
    private void InitializeSavedState()
    {
        if (!usePreferences) return;
        try {
            startupState = ViewerStateStore.Load(storageDirectory);
            var layout = startupState.Layout;
            infoPanelWidth = layout.InfoPanelWidth; InfoContent.Width = infoPanelWidth;
            Width = layout.Width; Height = layout.Height;
            if (layout.Left is double left && layout.Top is double top) {
                WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top;
            }
            MainGrid.ColumnDefinitions[0].Width = new GridLength(layout.LeftPaneWidth);
            LeftPane.RowDefinitions[0].Height = new GridLength(layout.FolderPaneHeight);
            FolderColumns.ColumnDefinitions[0].Width = new GridLength(layout.TreeFraction, GridUnitType.Star);
            FolderColumns.ColumnDefinitions[2].Width = new GridLength(1 - layout.TreeFraction, GridUnitType.Star);
            ApplyTreeVisibility(layout.TreeVisible);
            InfoToggle.IsChecked = layout.InfoOpen;
            SourceInitialized += (_, _) => {
                if (ConstrainStartupWindowToScreen) EnsureStartupWindowVisible();
                if (layout.Maximized) WindowState = WindowState.Maximized;
            };
        }
        catch (Exception error) { startupWarning = "前回状態を読み込めませんでした：" + error.Message; }
        StateChanged += (_, _) => { if (!fullscreen && WindowState != WindowState.Minimized) wasMaximized = WindowState == WindowState.Maximized; };
    }
    private void ShowRegistrationRecoveryNotice()
    {
        var notice=RecoverableSettings.TakeNotice(storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StellaViewer"));
        if(notice is not null && !closed)model.Status=notice;
    }
    public async Task RestoreStartupAsync(string? explicitPath = null)
    {
        var recovery = RecoverableSettings.TakeNotice(storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellaViewer"));
        if (recovery is not null) startupWarning = recovery;
        var allowed = model.AllowNetwork;
        var choice = await Task.Run(() => ViewerStateStore.ResolveStartup(startupState.View, explicitPath, restoreLastView && recordViewingState, allowed));
        if (choice.Path is not null) {
            await OpenAsync(choice.Path, recordHistory: false); await activeDisplay;
            if(string.IsNullOrWhiteSpace(explicitPath)&&restoreLastView&&startupState.View.LocationFolder is {Length:>0} location&&location!=model.LocationFolder&&!closed&&!closingInProgress)
                await LoadLocationsAsync(location,browse.Token);
        }
        if (!closed && !closingInProgress && (startupWarning ?? choice.Notification) is string notice) model.Status = notice;
    }
    private SavedViewerState CaptureSavedState()
    {
        var bounds = fullscreen ? savedBounds : RestoreBounds;
        if (bounds.IsEmpty) bounds = new Rect(Left, Top, Width, Height);
        var first = FolderColumns.ColumnDefinitions[0].ActualWidth;
        var second = FolderColumns.ColumnDefinitions[2].ActualWidth;
        var leftWidth = fullscreen ? FullscreenLeftHost.Width : MainGrid.ColumnDefinitions[0].ActualWidth;
        var layout = new WindowLayout(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            fullscreen ? savedState == WindowState.Maximized : WindowState == WindowState.Maximized || (WindowState == WindowState.Minimized && wasMaximized),
            leftWidth, LeftPane.RowDefinitions[0].Height.Value,
            (FolderBrowserPanel.Visibility!=Visibility.Visible||TreeToggle.IsChecked!=true)&&hiddenFolderTreeFraction is double fraction?fraction:
                first + second > 0 ? first / (first + second) : (preFullscreenLayout ?? startupState.Layout).TreeFraction, InfoToggle.IsChecked == true, TreeToggle.IsChecked==true);
        return new((layout with { InfoPanelWidth = infoPanelWidth }).Normalize(), new LastView(model.Folder, (ImageList.SelectedItem as FileEntry)?.Path ?? requestedPath,model.LocationFolder));
    }
    private async Task SaveAndCloseAsync()
    {
        RememberFolderImage();
        var state = CaptureSavedState();
        IsEnabled = false;
        CancelPrefetch(); StopAnimation();
        browse.Cancel(); search.Cancel(); display.Cancel(); locationListing.Cancel();
        try {
            await preferenceSaveTail;
            await favoriteSaveTail;
            await directorySaveTail;
            await recentSaveTail;
            await folderPositionSave;
            await FlushUnsavedListPreferencesAsync();
            await ViewerStateStore.SaveAsync(state.Layout, state.View, storageDirectory, saveView: recordViewingState, savingAllowed: () => !usePreferences || PreferencesStore.Load(preferenceFilePath).RecordViewingState);
            await DisposeTemporaryFilesAsync();
        }
        catch (Exception error) {
            MessageBox.Show(this, "状態を保存できませんでした。今回の状態は次回復元されない場合があります。\n" + error.Message,
                "StellaViewer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        closeCommitted = true; Close();
    }
    private void EnsureStartupWindowVisible()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var bounds)) return;
        var monitor = MonitorFromRect(ref bounds, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var width = Math.Min(bounds.Right - bounds.Left, info.Work.Right - info.Work.Left);
        var height = Math.Min(bounds.Bottom - bounds.Top, info.Work.Bottom - info.Work.Top);
        var x = Math.Clamp(bounds.Left, info.Work.Left, info.Work.Right - width);
        var y = Math.Clamp(bounds.Top, info.Work.Top, info.Work.Bottom - height);
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x14);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
