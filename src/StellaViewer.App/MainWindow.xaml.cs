using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using StellaViewer.Core;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;
public partial class MainWindow : Window
{
    private readonly ViewerModel model = new();
    private readonly MagickImageDecoder decoder = new();
    private List<FileEntry> all = [];
    private CancellationTokenSource browse = new(), display = new(), search = new();
    private bool loading, updating, zoomUpdating, closed;
    private double zoom = 100;
    private bool automaticZoom = true;
    private bool fitViewportZoom;
    private int openRequestVersion;
    private DecodedImage? decoded;
    private Point? drag;
    private bool pointerDown, dragMoved;
    private string? requestedPath;
    private Task activeDisplay = Task.CompletedTask;
    private readonly bool usePreferences;
    private bool controlWheel = true;
    public MainWindow(bool usePreferences = true, string? storageDirectory = null)
    {
        this.usePreferences = usePreferences;
        this.storageDirectory = storageDirectory;
        preferenceFilePath = storageDirectory is null ? null : Path.Combine(storageDirectory, "viewer-preferences.json");
        InitializeComponent(); DataContext = model;
        var preferences = new ViewerPreferences();
        if (usePreferences)
        {
            try { preferences = PreferencesStore.Load(preferenceFilePath); }
            catch (Exception error) { model.Message = "設定を読み込めませんでした：" + error.Message; }
        }
        recordRecentLocations = preferences.RecordRecentLocations; recordViewingState = preferences.RecordViewingState;
        cacheSettings = (preferences.Cache ?? new()).Normalize();
        deleteEnabled=preferences.DeleteEnabled;UpdateDeleteMenus();
        InitializeTray(preferences.MinimizeToTray);
        allowMultipleInstances=preferences.AllowMultipleInstances;InitializeInstanceRouting();
        var cacheDirectory = usePreferences ? Path.Combine(storageDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StellaViewer"), "Cache") : null;
        imageCache = new(decoder, cacheDirectory, usePreferences ? () => PreferencesStore.Load(preferenceFilePath).Cache ?? new() : null);
        imageCache.Configure(cacheSettings);
        if (usePreferences) _ = imageCache.MaintainDiskAsync();
        DarkTheme.IsChecked = preferences.Dark;
        model.ImageThumbnailSize=preferences.ImageThumbnailSize;model.LocationThumbnailSize=preferences.LocationThumbnailSize;
        enlargeSmallImages = preferences.EnlargeSmallImages; controlWheel = preferences.ControlWheel; hideHiddenFolders = preferences.HideHiddenFolders; ApplyTheme(preferences.Dark);
        ListModeBox.SelectedIndex = preferences.ListMode is >= 0 and <= 2 ? preferences.ListMode : 1;
        ApplyListMode();
        LocationModeBox.SelectedIndex = preferences.LocationListMode is >= 0 and <= 2 ? preferences.LocationListMode : 1;
        ApplyLocationMode();
        SelectedLocationSort = preferences.LocationSortMode is >= 0 and <= 5 ? preferences.LocationSortMode : 0;
        viewerToolbarPlacement = preferences.ViewerToolbarPlacement is >= 0 and <= 2 ? preferences.ViewerToolbarPlacement : 0;
        ApplyToolbarPlacement();
        SelectedImageSort = preferences.SortMode is >= 0 and <= 5 ? preferences.SortMode : 0;
        restoreLastView = preferences.RestoreLastView;
        ApplyAppearance(preferences.Appearance);
        spreadEnabled = preferences.SpreadMode; rightBinding = preferences.RightBinding; SyncSpreadButton();
        loupeSettings = (preferences.Loupe ?? new()).Normalize();
        ApplyKeyboardSettings(preferences.Keyboard);
        mouseSettings = (preferences.Mouse ?? new()).Normalize();
        savedListPreferences = ReadListPreferences();
        loadingPreferences = false;
        ApplyRenderingMode(preferences.SoftwareRendering);
        model.AllowNetwork = preferences.AllowNetwork;
        foreach (var drive in DriveInfo.GetDrives().Where(d => model.AllowNetwork || d.DriveType != DriveType.Network)) Folders.Items.Add(FolderNode(drive.Name, drive.DriveType));
        InitializeSavedState();
        InitializeTemporaryCleanup();
        InitializeFavorites();
        InitializeDirectoryShortcuts();
        InitializeRecentLocations();
        InitializeFolderOpening(preferences);
        slideshowIntervalSeconds = Math.Clamp(preferences.SlideshowIntervalSeconds, 1, 60); listFontSize = Math.Clamp(preferences.ListFontSize, 10, 24); ApplyUiSizing(preferences.UiSize); listScroll = (preferences.ListScroll ?? new()).Normalize();
    }
    private static TreeViewItem FolderNode(string path, DriveType? driveType = null)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        var node = new TreeViewItem { Tag = path };
        var header = new IconLabel { Text = name.Length > 0 ? name : path, IconSize = 16 };
        if (driveType is not null || name.Length == 0)
            header.Kind = driveType switch { DriveType.Removable => ViewerIconKind.RemovableDrive, DriveType.CDRom => ViewerIconKind.OpticalDrive, _ => ViewerIconKind.Drive };
        else header.SetBinding(IconLabel.KindProperty, new System.Windows.Data.Binding(nameof(TreeViewItem.IsExpanded)) {
            Source = node, Converter = FolderExpansionIconConverter.Instance
        });
        node.Header = header;
        node.Items.Add(new TreeViewItem { Header = "読み込み…" }); return node;
    }
    private async void FolderExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem node || node.Tag is not string path || node.Items.Count != 1 || node.Items[0] is not TreeViewItem { Tag: null }) return;
        e.Handled = true;
        // Mark in progress before yielding so repeated expansion does not duplicate requests.
        ((TreeViewItem)node.Items[0]).Tag = false;
        var version = folderTreeVersion; var hideHidden = hideHiddenFolders; var allowed = model.AllowNetwork;
        try {
            var folders = await Task.Run(() => FolderBrowser.GetSubfolders(path, hideHidden, allowed));
            if (closed || version != folderTreeVersion) return;
            node.Items.Clear(); foreach (var folder in folders) node.Items.Add(FolderNode(folder));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { node.Items.Clear(); node.ToolTip = error.Message; }
    }
    private async void FolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (e.NewValue is TreeViewItem { Tag: string path }) await (navigation=OpenAsync(path)); }
    private async void OpenFolderClick(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FolderName); }
    private async void UpClick(object sender, RoutedEventArgs e) => await UpAsync();
    private async void OnDrop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths) await OpenAsync(paths[0]); }
    public async Task OpenAsync(string path, bool recordHistory = true, bool updateLocations = true, bool reuseLocations = true)
    {
        if (closed || closingInProgress) return;
        StopSlideshow();
        RememberFolderImage();
        selectFirstAfterEnumeration=false;
        CancelPrefetch();
        var requestVersion = ++openRequestVersion;
        try {
            if (PdfLocation.Parse(path) is { } pdfTarget) { await OpenPdfAsync(pdfTarget, recordHistory, requestVersion, updateLocations: updateLocations); return; }
            if (ArchiveLocation.Parse(path) is { } archiveTarget) { await OpenArchiveAsync(archiveTarget, recordHistory, requestVersion, updateLocations: updateLocations); return; }
        }
        catch (Exception error) { model.Status = error.Message; return; }
        string folder;
        bool isFile, folderExists;
        try {
            NetworkAccess.Validate(path, model.AllowNetwork); path = Path.GetFullPath(path);
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
                var shortcut = path; var allowed = model.AllowNetwork;
                path = await Task.Run(() => WindowsShortcut.Resolve(shortcut, allowed));
                if (requestVersion != openRequestVersion || closed || closingInProgress) return;
                NetworkAccess.Validate(path, model.AllowNetwork);
            }
            var target = path;
            var probe = await Task.Run(() => {
                var file = File.Exists(target);
                var directory = file ? Path.GetDirectoryName(target)! : target;
                return (File: file, Folder: directory, Exists: Directory.Exists(directory));
            });
            if (requestVersion != openRequestVersion || closed || closingInProgress) return;
            isFile = probe.File; folder = probe.Folder; folderExists = probe.Exists;
        }
        catch (Exception error) { if (requestVersion == openRequestVersion) model.Status = error.Message; return; }
        if (isFile && FolderBrowser.IsContainerFile(path)) {
            if (ArchiveSession.Supports(path)) await OpenArchiveAsync(new(path), recordHistory, requestVersion, updateLocations: updateLocations);
            else await OpenPdfAsync(new(path), recordHistory, requestVersion, updateLocations: updateLocations);
            return;
        }
        if (!folderExists) { model.Status = "フォルダが見つかりません：" + folder; return; }
        if (recordHistory && model.Folder.Length > 0 && !string.Equals(folder, model.Folder, StringComparison.OrdinalIgnoreCase)) RememberLocation();
        StopFolderMonitor();
        archiveSession?.Dispose(); archiveSession = null; model.ArchiveSession = null;
        pdfSession?.Dispose(); pdfSession = null; model.PdfSession = null;
        var initialPath = isFile ? path : await RememberedFolderImageAsync(folder);
        if(requestVersion!=openRequestVersion||closed||closingInProgress)return;
        requestedPath = initialPath;
        selectFirstAfterEnumeration=!isFile&&initialPath is null;
        browse.Cancel(); browse.Dispose(); browse = new(); search.Cancel();
        var token = browse.Token;
        // This local list belongs to this enumeration, never to a later folder request.
        var collected = new List<FileEntry>(); all = collected;
        loading = true; model.Folder = folder; model.CurrentAddress = folder;
        var locationsTask = updateLocations ? LoadLocationsAsync(folder, token, preferCached: reuseLocations) : Task.CompletedTask;
        updating = true; model.Entries = []; updating = false; SyncTiles(true);
        ClearImage(); model.ListingStatus = "列挙中・順序は仮（0件）";
        var monitorStartup = StartFolderMonitorAsync(folder, token);
        try {
            await foreach (var batch in FolderBrowser.EnumerateAsync(folder, token, model.AllowNetwork)) {
                if (token.IsCancellationRequested) return;
                collected.AddRange(batch);
                foreach (var entry in batch.Where(Matches)) model.Entries.Add(entry);
                SyncTiles(false);
                model.ListingStatus = $"列挙中・順序は仮（{collected.Count:N0}件）";
                SelectRequestedOrFirst();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            }
            var sorted = await Task.Run(() => collected.OrderBy(e => e, FolderBrowser.NaturalComparer).ToList(), token);
            if (token.IsCancellationRequested) return;
            all = sorted; loading = false;
            if(selectFirstAfterEnumeration){updating=true;try{ImageList.SelectedItem=null;}finally{updating=false;}}
            await FilterAsync();if(token.IsCancellationRequested||requestVersion!=openRequestVersion)return;
            selectFirstAfterEnumeration=false;await locationsTask; await monitorStartup;
            if (!token.IsCancellationRequested) {
                await activeDisplay;
                if(!token.IsCancellationRequested&&requestVersion==openRequestVersion)await RememberRecentPlaceAsync(folder);
                if (!token.IsCancellationRequested) StartPrefetch();
            }
        } catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) { loading = false; model.Message = error.Message; model.ListingStatus = "列挙失敗"; } }
    }
    private bool Matches(FileEntry entry) => entry.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase);
    private void SelectRequestedOrFirst()
    {
        selectingInitialImage=true;
        try{
        if (requestedPath is not null) {
            var requested = model.Entries.FirstOrDefault(e => e.Path.Equals(requestedPath, StringComparison.OrdinalIgnoreCase));
            if (requested is not null) { ImageList.SelectedItem = requested; requestedPath = null; }
            else if (loading) return;
        }
        if (ImageList.SelectedItem is null && model.Entries.Count > 0) ImageList.SelectedIndex = 0;
        }finally{selectingInitialImage=false;}
    }
    private async void SearchChanged(object sender, TextChangedEventArgs e) { if (IsInitialized && !restoringNavigation) await FilterAsync(); }
    private async Task FilterAsync()
    {
        if (!CanReadCurrentFolder()) return;
        search.Cancel(); search.Dispose(); search = new();
        var token = search.Token; var folderToken = browse.Token;
        var snapshot = all.ToArray(); var query = SearchBox.Text;
        var selected = (ImageList.SelectedItem as FileEntry)?.Path;
        var selectedPaths = ImageList.SelectedItems.OfType<FileEntry>().Select(item => item.Path).ToArray();
        var sort = SelectedImageSort;
        var isLoading = loading;
        try {
            var result = await Task.Run(() => {
                IEnumerable<FileEntry> filtered = snapshot.Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (!isLoading) return FolderBrowser.SortImages(filtered, sort);
                return filtered.ToArray();
            }, token);
            if (token.IsCancellationRequested || folderToken.IsCancellationRequested) return;
            if (loading && all.Count > snapshot.Length) result = [..result, ..all.Skip(snapshot.Length).Where(Matches)];
            updating = true; model.Entries = new LiveEntryCollection<FileEntry>(result); SyncTiles(true);
            ImageList.SelectedItem = result.FirstOrDefault(e => e.Path == selected); updating = false;
            if (spreadEnabled) {
                updating = true;
                try { SelectRequestedOrFirst(); } finally { updating = false; }
                RebaseSpread(); activeDisplay = LoadSelectedAsync();
            }
            else SelectRequestedOrFirst();
            if (!loading && ImageList.SelectedItem is not null) ScrollSelection();
            if (ImageList.SelectedItem is null) ClearImage();
            model.ListingStatus = loading ? $"列挙中・順序は仮（{all.Count:N0}件）" : $"{result.Length:N0}件 / 全{all.Count:N0}件";
            if (selectedPaths.Length > 1) RestoreBatchSelection(ImageList, selectedPaths);
            UpdateStatus();
        } catch (OperationCanceledException) { }
    }
    private async void ImageSelected(object sender, SelectionChangedEventArgs e) { if (selectingBatch) return; PublishSelection(); if (!updating && Keyboard.Modifiers == ModifierKeys.None && ImageList.SelectedIndex >= 0) imageSelectionAnchor = ImageList.SelectedIndex; if (!slideshowAdvancing && ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0 || ImageList.SelectedItems.Count > 1)) return; if (!updating) { if(!selectingInitialImage)selectFirstAfterEnumeration=false;PruneMissingEntries(); if (!movingSpread) RebaseSpread(); model.SelectedPath = (ImageList.SelectedItem as FileEntry)?.Path; await (activeDisplay = LoadSelectedAsync()); } }
    private void ClearImage()
    {
        if (model.Entries.Count == 0) StopSlideshow();
        CancelMetadataRead();model.MetadataText="画像を選択してください。";
        CancelPrefetch(); StopAnimation();
        ClearZoomPreset(); display.Cancel(); decoded = null; model.Image = null; model.Message = "画像がありません";
        displayRevision++; displayedEntry = null; missingDisplayedPaths.Clear(); changedDisplayedPaths.Clear();
        firstPage = secondPage = null;
        ImageGrid.Visibility = Visibility.Collapsed;
        LoupeLayer.Clear();
        CancelPointer();
        model.Details = "画像を選択してください";
        model.CurrentAddress = model.Folder;
        model.Status = "0件"; Rotation.Angle = 0; Pan.X = Pan.Y = 0;
    }
    private async Task LoadSelectedAsync()
    {
        slideshowTimer?.Stop();
        var revision = ++slideshowDisplayRevision;
        try { await LoadSelectedCoreAsync(); }
        finally { if (revision == slideshowDisplayRevision) RestartSlideshowDelay(); }
    }
    private async Task LoadSelectedCoreAsync()
    {
        if (!CanReadCurrentFolder()) return;
        ClearImage(); display.Dispose(); display = new(); var token = display.Token;
        if (ImageList.SelectedItem is not FileEntry entry) return;
        displayedEntry = entry;
        model.Message = "読み込み中…";
        try {
            if (spreadEnabled) {
                await LoadSpreadAsync(token);
                if (!token.IsCancellationRequested) { UpdateStatus();QueueMetadataRead(); await StartSelectedAnimationAsync(entry, token); StartPrefetch(); }
                return;
            }
            var result = await DecodePageAsync(entry, token);
            if (token.IsCancellationRequested || closed) return;
            model.Image = BitmapPresenter.Create(result);
            decoded = result with { BgraPixels = [] }; model.Message = ""; ResetZoom();
            await StartSelectedAnimationAsync(entry, token);
        } catch (OperationCanceledException) { }
        catch (Exception error) { if (!token.IsCancellationRequested) model.Message = "読み込み失敗：" + error.Message; }
        UpdateStatus();
        if (!token.IsCancellationRequested){QueueMetadataRead();StartPrefetch();}
    }
    private double Fit()
    {
        if (decoded is null) return 100;
        var dpi = VisualTreeHelper.GetDpi(Viewport); var rotated = Math.Abs(Rotation.Angle % 180) > 0;
        return Zoom.Fit(rotated ? decoded.Height : decoded.Width, rotated ? decoded.Width : decoded.Height, Viewport.ActualWidth * dpi.DpiScaleX, Viewport.ActualHeight * dpi.DpiScaleY);
    }
    private void SetZoom(double value)
    {
        if (decoded is null) return;
        zoom = Zoom.Clamp(value, Fit()); var dpi = VisualTreeHelper.GetDpi(Viewport);
        Picture.Width = decoded.Width * zoom / 100 / dpi.DpiScaleX; Picture.Height = decoded.Height * zoom / 100 / dpi.DpiScaleY;
        Canvas.SetLeft(Picture, (Viewport.ActualWidth - Picture.Width) / 2);
        Canvas.SetTop(Picture, (Viewport.ActualHeight - Picture.Height) / 2);
        UpdateImageGrid();
        ClampPan();
        zoomUpdating = true; ZoomSlider.Minimum = Math.Log(Zoom.Minimum(Fit()) / 100); ZoomSlider.Value = Math.Log(zoom / 100);
        ZoomText.Text = Math.Round(zoom).ToString("0"); zoomUpdating = false; UpdateStatus();
        if (!CanPan) { Pan.X = Pan.Y = 0; CancelPointer(); }
    }
    private void ResetZoom() { ClearZoomPreset(); fitViewportZoom = false; automaticZoom = true; Pan.X = Pan.Y = 0; SetZoom(Zoom.Initial(Fit(), enlargeSmallImages)); }
    private double AutomaticZoom()=>fitViewportZoom?Zoom.Clamp(Fit(),Fit()):Zoom.Initial(Fit(),enlargeSmallImages);
    private void ResetClick(object sender, RoutedEventArgs e) => ResetZoom();
    private void ZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!zoomUpdating && IsInitialized) { ClearZoomPreset(); automaticZoom = false; SetZoom(Math.Exp(e.NewValue) * 100); } }
    private void ZoomEdit(object sender, MouseButtonEventArgs e) { ZoomText.IsReadOnly = false; ZoomText.Focus(); ZoomText.SelectAll(); e.Handled = true; }
    private void CommitZoom()
    {
        if (ZoomText.IsReadOnly) return;
        if (decimal.TryParse(ZoomText.Text, out var value) && value == decimal.Truncate(value)) { ClearZoomPreset(); automaticZoom = false; SetZoom((double)value); }
        else ZoomText.Text = Math.Round(zoom).ToString("0");
        ZoomText.IsReadOnly = true;
    }
    private void ZoomEntered(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitZoom(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ZoomText.Text = Math.Round(zoom).ToString("0"); ZoomText.IsReadOnly = true; e.Handled = true; }
    }
    private void ZoomLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitZoom();
    private void ViewportChanged(object sender, SizeChangedEventArgs e) { if (decoded is not null) SetZoom(automaticZoom ? AutomaticZoom() : zoom); }
    private void RotateLeft(object sender, RoutedEventArgs e) { Rotation.Angle -= 90; ResetZoom(); }
    private void RotateRight(object sender, RoutedEventArgs e) { Rotation.Angle += 90; ResetZoom(); }
    private void Move(int delta)
    {
        if (!CanReadCurrentFolder()) return;
        var count = model.Entries.Count; if (count == 0) return;
        if (MovePastMissing(delta)) return;
        if (spreadEnabled) {
            spreadGroup = SpreadPagination.MoveGroup(count, spreadGroup, delta);
            var pair = SpreadPagination.GetPages(count, spreadAnchor, spreadGroup);
            movingSpread = true;
            try { ImageList.SelectedIndex = pair!.Value.First; }
            finally { movingSpread = false; }
            ScrollSelection(); return;
        }
        ImageList.SelectedIndex = (Math.Max(0, ImageList.SelectedIndex) + delta + count) % count; ScrollSelection();
    }
    private async void ViewerWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && controlWheel) {
            ClearZoomPreset(); automaticZoom = false;
            var point = e.GetPosition(Viewport); var oldZoom = zoom;
            SetZoom(zoom * Math.Pow(1.1, e.Delta / 120.0)); var ratio = zoom / oldZoom;
            Pan.X = point.X - Viewport.ActualWidth / 2 - (point.X - Viewport.ActualWidth / 2 - Pan.X) * ratio;
            Pan.Y = point.Y - Viewport.ActualHeight / 2 - (point.Y - Viewport.ActualHeight / 2 - Pan.Y) * ratio;
            ClampPan();
        } else if(e.Delta!=0) await ExecuteMouseCommandAsync(e.Delta < 0 ? StellaViewer.Core.ViewerMouseGesture.WheelDown : StellaViewer.Core.ViewerMouseGesture.WheelUp);
    }
    private bool CanPan => decoded is not null && zoom > Fit() + 0.000001;
    private void PageEntered(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && int.TryParse(PageBox.Text, out var number) && TryJumpToPage(number)) e.Handled = true;
    }
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && fullscreen) { ToggleFullscreen(); e.Handled = true; return; }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = KeyboardCommands.Gesture(key, Keyboard.Modifiers);
        if (IsKeyboardInput(e.OriginalSource as DependencyObject) || gesture.IsEmpty || !keyboardSettings.Bindings.Values.Any(keys => keys.Contains(gesture))) return;
        e.Handled = true;
        await TryShortcutAsync(key, Keyboard.Modifiers, e.OriginalSource as DependencyObject);
    }
    private void UpdateStatus()
    {
        model.SelectedPath = (ImageList.SelectedItem as FileEntry)?.Path;
        model.CurrentAddress = model.SelectedPath ?? model.Folder;
        if (ImageList.SelectedItem is not FileEntry selected) { model.Status = "0件"; AppendWatchNotice(); return; }
        var entry = displayedEntry?.Path == selected.Path ? displayedEntry : selected;
        if (spreadEnabled) { UpdateSpreadStatus(entry); AppendWatchNotice(); AppendAnimationNotice(); AppendStoredPreviewNotices(); return; }
        var dimensions = decoded is null ? "" : $"{decoded.OriginalWidth}×{decoded.OriginalHeight}px  {zoom:0}%  ";
        model.Status = $"{ImageList.SelectedIndex + 1} / {model.Entries.Count}  {dimensions}{EntrySizeLabel(entry)}{FileSizeText.Format(entry.Length)}  {(entry.IsPdfPage ? "元PDF更新日時：" : "")}{(entry.Modified == default ? "日時不明" : entry.Modified.ToString("yyyy/MM/dd HH:mm"))}";
        model.Details = $"{entry.Name}\n\n{(decoded is null ? "" : $"{(entry.IsPdfPage ? $"描画寸法（{PdfSession.RenderDpi} dpi）" : "画像寸法")}：{decoded.OriginalWidth} × {decoded.OriginalHeight} px\n")}{EntrySizeLabel(entry)}{FileSizeText.Format(entry.Length)}（{entry.Length:N0} bytes）\n\n{EntryDateLabel(entry)}\n{(entry.Modified == default ? "不明" : entry.Modified.ToString("yyyy/MM/dd HH:mm"))}\n\n{BrowsePath.DisplayPath(entry.Path)}";
        AppendWatchNotice();
        if (imageCache.Warning is string warning) model.Status += "  " + warning;
        AppendAnimationNotice();
        AppendStoredPreviewNotices();
    }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if(fileManagementBusy){e.Cancel=true;return;}
        StopSlideshow();
        if (usePreferences && !closeCommitted) {
            e.Cancel = true;
            if (!closingInProgress) { closingInProgress = true; closePersistence = SaveAndCloseAsync(); }
            return;
        }
        closed = true; StopFavorites(); StopDirectoryShortcuts(); StopRecentLocations(); StopAnimation(); CancelPrefetch(); StopFolderMonitor(); browse.Cancel(); search.Cancel(); display.Cancel(); locationListing.Cancel();
        recycleService.ClearUndo();
        temporaryCleanup = DisposeTemporaryFilesAsync();
    }
    private void ApplyTheme(bool dark)
    {
        Resources["PanelBackground"] = new SolidColorBrush(dark ? Color.FromRgb(40, 43, 48) : Color.FromRgb(246, 247, 249));
        Resources["PanelForeground"] = dark ? Brushes.WhiteSmoke : Brushes.Black;
        Resources["ZoomForeground"] = dark ? Brushes.White : Brushes.Black;
        Resources["ZoomThumbFill"] = dark ? Brushes.Black : Brushes.White;
        Resources["ViewerBackground"] = new SolidColorBrush(dark ? Color.FromRgb(25, 27, 30) : Color.FromRgb(224, 226, 230));
        Resources["DividerBrush"] = new SolidColorBrush(dark ? Color.FromRgb(65, 69, 76) : Color.FromRgb(204, 209, 216));
        Resources["ScrollThumbBrush"] = new SolidColorBrush(dark ? Color.FromRgb(127, 139, 155) : Color.FromRgb(145, 155, 169));
        Resources["ScrollThumbHoverBrush"] = new SolidColorBrush(dark ? Color.FromRgb(178, 192, 211) : Color.FromRgb(104, 120, 141));
        Resources["HoverBrush"] = new SolidColorBrush(dark ? Color.FromRgb(55, 61, 70) : Color.FromRgb(232, 237, 244));
        Resources["SelectionBrush"] = new SolidColorBrush(dark ? Color.FromRgb(43, 67, 99) : Color.FromRgb(220, 235, 254));
        Resources[SystemColors.MenuBrushKey] = Resources["PanelBackground"];
        ThemeButton.Content = new IconLabel { Kind = dark ? ViewerIconKind.Moon : ViewerIconKind.Sun, Text = dark ? "ダーク" : "ライト", IconSize = 14 };
        ThemeButton.ToolTip = dark ? "ライトテーマへ切り替え" : "ダークテーマへ切り替え";
    }
    private void ThemeButtonClicked(object sender, RoutedEventArgs e)
    {
        DarkTheme.IsChecked = !DarkTheme.IsChecked;
        PreferenceClick(DarkTheme, e);
    }
    private async void PreferenceClick(object sender, RoutedEventArgs e)
    {
        ApplyTheme(DarkTheme.IsChecked == true);
        if (!usePreferences) return;
        try
        {
            var dark = sender == DarkTheme ? DarkTheme.IsChecked : (bool?)null;
            await SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(dark: dark, filePath: preferenceFilePath));
        }
        catch (Exception error) { model.Status = "設定を保存できませんでした：" + error.Message; }
    }
    private async void SortingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || loadingPreferences) return;
        var mode = SelectedImageSort;
        var saving = usePreferences ? SaveListPreferencesAsync(imageSort: mode) : Task.CompletedTask;
        try { if (!restoringNavigation) await FilterAsync(); await saving; }
        catch (Exception error) { model.Status = "並べ替えを保存できませんでした：" + error.Message; }
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _ = Dispatcher.InvokeAsync(() => { if (!closed) SetZoom(automaticZoom ? AutomaticZoom() : zoom); });
    }
}
