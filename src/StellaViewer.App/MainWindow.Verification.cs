using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException(name); }
        var content = (FrameworkElement)Content;
        WatchFoldersEnabled = false;
        ShowInTaskbar = false; ShowActivated = false; Left = -32000; Top = -32000;
        Show();
        content.Measure(new Size(1200, 760)); content.Arrange(new Rect(0, 0, 1200, 760));
        await OpenAsync(imagePath); await activeDisplay;
        Check(model.HasImage, "Initial image must load");
        content.UpdateLayout(); VerifyRendering(screenshotPath);
        Check(MainMenu.Items.Count == 8 && FullscreenButton.IsEnabled, "Menu and fullscreen button are available");
        void DragDivider(System.Windows.Controls.GridSplitter divider, double x, double y)
        {
            divider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            divider.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(x, y) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            divider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(x, y, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            content.UpdateLayout();
        }
        content.UpdateLayout();
        var originalLeftWidth = LeftPane.ActualWidth;
        DragDivider(LeftWidthSplitter, 40, 0);
        Check(LeftPane.ActualWidth > originalLeftWidth + 30, "Left pane width divider works");
        DragDivider(LeftWidthSplitter, -40, 0);
        var originalFolderHeight = LeftPane.RowDefinitions[0].ActualHeight;
        DragDivider(LeftRowSplitter, 0, 24);
        Check(LeftPane.RowDefinitions[0].ActualHeight > originalFolderHeight + 15 && ListArea.ActualHeight >= 80, "Vertical divider resizes folder and image areas");
        DragDivider(LeftRowSplitter, 0, -24);
        var originalTreeWidth = FolderColumns.ColumnDefinitions[0].ActualWidth;
        DragDivider(FolderColumnSplitter, 14, 0);
        Check(FolderColumns.ColumnDefinitions[0].ActualWidth > originalTreeWidth + 8, "Tree and folder column divider works");
        DragDivider(FolderColumnSplitter, -14, 0);
        Check(Math.Abs(InfoPanel.ActualWidth - 48) < 1, "Collapsed info rail is 48 pixels wide");
        InfoToggle.IsChecked = true; content.UpdateLayout();
        Check(Math.Abs(InfoPanel.ActualWidth - 368) < 1, "Expanded info area doubles content width to 320 pixels");
        InfoToggle.IsChecked = false; content.UpdateLayout();
        await ApplyPreferencesAsync(CurrentPreferences with { ViewerToolbarPlacement = 1 }); content.UpdateLayout();
        Check(System.Windows.Controls.DockPanel.GetDock(ViewerToolbar) == System.Windows.Controls.Dock.Bottom && ViewerToolbar.IsVisible, "Toolbar can be placed below image");
        await ApplyPreferencesAsync(CurrentPreferences with { ViewerToolbarPlacement = 2 }); content.UpdateLayout();
        Check(!ViewerToolbar.IsVisible, "Toolbar can be hidden");
        await ApplyPreferencesAsync(CurrentPreferences with { ViewerToolbarPlacement = 0 }); content.UpdateLayout();
        Check(ViewerToolbar.IsVisible && System.Windows.Controls.DockPanel.GetDock(ViewerToolbar) == System.Windows.Controls.Dock.Top, "Toolbar returns to top");
        Check(FolderAddress.CurrentPath == Path.GetDirectoryName(imagePath), "Folder breadcrumbs track current folder");
        Check(ImageAddress.CurrentPath == Path.GetFullPath(imagePath), "Top breadcrumbs include selected image");
        Check(model.Locations.Any(item => item.IsDirectory), "Child folders are listed beside the tree");
        // Finish initial layout/focus work before exercising an editor in an inactive test window.
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ImageAddress.BeginEdit();
        Check(ImageAddress.IsEditing, "Breadcrumb address editor opens");
        ImageAddress.SubmitAddress(imagePath); await navigation; await activeDisplay;
        Check(!ImageAddress.IsEditing && model.HasImage, "Address entry navigates to image");
        Check((ImageList.SelectedItem as FileEntry)?.Path == Path.GetFullPath(imagePath), "Sort must preserve requested image");
        SelectedImageSort = 2; await FilterAsync();
        Check((ImageList.SelectedItem as FileEntry)?.Path == Path.GetFullPath(imagePath), "Size sort preserves selected image");
        Check(model.Entries.Select(e => e.Length).SequenceEqual(model.Entries.Select(e => e.Length).Order()), "Size order");
        SelectedImageSort = 0; await FilterAsync();
        SetZoom(9000); Check(Math.Abs(zoom - 1000) < .0001, "Maximum zoom clamp");
        SetZoom(-1); Check(Math.Abs(zoom - StellaViewer.Core.Zoom.Minimum(Fit())) < .0001, "Minimum zoom clamp");
        ResetZoom(); Check(zoom <= 100, "Initial zoom does not enlarge small images");
        Check(!CanPan, "Fitted image must not enable dragging");
        var beforeClick = ImageList.SelectedIndex;
        BeginPointer(new Point(10, 10)); UpdatePointer(new Point(40, 40));
        Check(Pan.X == 0 && Pan.Y == 0, "Fitted image cannot pan");
        EndPointer(); await activeDisplay;
        Check(ImageList.SelectedIndex == beforeClick, "Fitted drag with drag paging disabled suppresses click navigation");
        BeginPointer(new Point(10, 10)); EndPointer(); await activeDisplay;
        Check(ImageList.SelectedIndex == (beforeClick + 1) % model.Entries.Count, "Left click advances image");
        decoded = decoded! with { Width = 2000, Height = 1000 };
        SetZoom(200); Pan.X = 30; Pan.Y = 20;
        var rotationSelection = ImageList.SelectedItem; var rotationImage = model.Image;
        RotateRight(this, new RoutedEventArgs());
        Check(Rotation.Angle == 90 && Math.Abs(zoom - StellaViewer.Core.Zoom.Initial(Fit())) < .000001
            && Pan.X == 0 && Pan.Y == 0 && !CanPan && automaticZoom,
            "Right rotation refits portrait bounds, recenters and enables automatic fit");
        Check(ReferenceEquals(rotationSelection, ImageList.SelectedItem) && ReferenceEquals(rotationImage, model.Image),
            "Rotation preserves selected image without decoding again");
        SetZoom(200); Pan.X = 20; Pan.Y = 30;
        RotateLeft(this, new RoutedEventArgs());
        Check(Rotation.Angle == 0 && Math.Abs(zoom - StellaViewer.Core.Zoom.Initial(Fit())) < .000001
            && Pan.X == 0 && Pan.Y == 0 && !CanPan, "Left rotation refits landscape bounds and recenters");
        SetZoom(100);
        Check(CanPan, "Oversized image enables dragging");
        var beforeDrag = ImageList.SelectedIndex;
        var expectedPanX = Math.Min(30, Math.Max(0, (Picture.Width - Viewport.ActualWidth) / 2));
        var expectedPanY = Math.Min(30, Math.Max(0, (Picture.Height - Viewport.ActualHeight) / 2));
        BeginPointer(new Point(10, 10)); UpdatePointer(new Point(40, 40)); EndPointer();
        Check(Math.Abs(Pan.X - expectedPanX) < .0001 && Math.Abs(Pan.Y - expectedPanY) < .0001 && ImageList.SelectedIndex == beforeDrag, $"Dragging pans within viewport bounds without page turn: pan={Pan.X},{Pan.Y}, expected={expectedPanX},{expectedPanY}");
        content.UpdateLayout();
        Check(Math.Abs(Picture.ActualWidth * VisualTreeHelper.GetDpi(Viewport).DpiScaleX - 2000) < 1, "Oversized image retains full physical pixel width at 100%");
        BeginPointer(new Point(0, 0)); UpdatePointer(new Point(10000, 10000)); EndPointer();
        Check(Pan.X <= (Picture.Width - Viewport.ActualWidth) / 2 + .01, "Pan clamps to image edge");
        model.Image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4);
        content.UpdateLayout();
        var panRender = new RenderTargetBitmap((int)Viewport.ActualWidth, (int)Viewport.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var panVisual = new DrawingVisual();
        using (var drawing = panVisual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(Viewport), null, new Rect(0, 0, Viewport.ActualWidth, Viewport.ActualHeight));
        panRender.Render(panVisual);
        var edgePixel = new byte[4];
        panRender.CopyPixels(new Int32Rect(2, 2, 1, 1), edgePixel, 4, 0);
        var debugEncoder = new PngBitmapEncoder(); debugEncoder.Frames.Add(BitmapFrame.Create(panRender)); using (var debugStream = File.Create(screenshotPath + ".pan.png")) debugEncoder.Save(debugStream);
        Check(edgePixel[2] == 255 && edgePixel[1] == 0, $"Panning exposes full image without layout clipping: {string.Join(",", edgePixel)}");
        var fullscreenIndex = ImageList.SelectedIndex;
        ToggleFullscreen(false);
        content.UpdateLayout();
        Check(fullscreen && !TopBars.IsVisible && !BottomBar.IsVisible && !LeftPane.IsVisible && !InfoPanel.IsVisible && ImageList.SelectedIndex == fullscreenIndex,
            "Fullscreen initially hides all edge bars without paging");
        var fullscreenView = new Size(Viewport.ActualWidth, Viewport.ActualHeight);
        var fullscreenZoom = zoom;
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth / 2, 1)); content.UpdateLayout();
        Check(MainMenu.IsVisible && ImageAddress.IsVisible && BackButton.IsVisible && ForwardButton.IsVisible,
            "Top edge reveals menu, address and navigation controls");
        Check(Viewport.ActualWidth == fullscreenView.Width && Viewport.ActualHeight == fullscreenView.Height && zoom == fullscreenZoom,
            "Top bar overlays image without changing viewport or zoom");
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth / 2, FullscreenTopHost.ActualHeight - 2)); content.UpdateLayout();
        Check(TopBars.IsVisible, "Top bar stays visible while pointer is inside it");
        ImageAddress.BeginEdit();
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth / 2, WindowSurface.ActualHeight / 2));
        Check(TopBars.IsVisible, "Address input remains accessible while editing");
        ImageAddress.SubmitAddress("");
        UpdateFullscreenBars(new Point(1, WindowSurface.ActualHeight / 2)); content.UpdateLayout();
        Check(LeftPane.IsVisible && !InfoPanel.IsVisible && !BottomBar.IsVisible, "Left edge reveals left pane");
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth - 1, WindowSurface.ActualHeight / 2)); content.UpdateLayout();
        Check(InfoPanel.IsVisible && !LeftPane.IsVisible, "Right edge reveals info rail");
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth / 2, WindowSurface.ActualHeight - 1)); content.UpdateLayout();
        Check(BottomBar.IsVisible && Viewport.ActualWidth == fullscreenView.Width && Viewport.ActualHeight == fullscreenView.Height,
            "Bottom bar overlays image without resizing viewport");
        UpdateFullscreenBars(new Point(1, WindowSurface.ActualHeight - 1)); content.UpdateLayout();
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth / 2, 1)); content.UpdateLayout();
        var fullscreenRender = new RenderTargetBitmap((int)WindowSurface.ActualWidth, (int)WindowSurface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        fullscreenRender.Render(WindowSurface);
        var fullscreenEncoder = new PngBitmapEncoder(); fullscreenEncoder.Frames.Add(BitmapFrame.Create(fullscreenRender));
        using (var stream = File.Create(screenshotPath + ".fullscreen.png")) fullscreenEncoder.Save(stream);
        UpdateFullscreenBars(new Point(WindowSurface.ActualWidth / 2, WindowSurface.ActualHeight / 2)); content.UpdateLayout();
        Check(!TopBars.IsVisible && !LeftPane.IsVisible && !InfoPanel.IsVisible && !BottomBar.IsVisible, "Moving away hides fullscreen bars");
        ToggleFullscreen(false);
        content.UpdateLayout();
        Check(!fullscreen && FolderAddress.IsVisible && MainMenu.IsVisible && ImageAddress.IsVisible && ReferenceEquals(Shell.Children[0], TopBars), "Fullscreen restores panels and top bar order");
        ResetZoom();
        Check(!CanPan && Pan.X == 0 && Pan.Y == 0, "Reset clears pan and disables dragging");
        Viewport.Width = 500; Viewport.Height = 400; content.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var smallerZoom = zoom;
        Viewport.Width = double.NaN; Viewport.Height = double.NaN; content.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(zoom > smallerZoom && zoom <= 100, "Initial zoom follows growing viewport");
        automaticZoom = false; SetZoom(100);
        Viewport.Width = 500; Viewport.Height = 400; content.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(zoom == 100, "Manual zoom survives viewport resize");
        Viewport.Width = double.NaN; Viewport.Height = double.NaN; content.UpdateLayout();
        SearchBox.Text = "no-match-356a629d"; await FilterAsync(); await activeDisplay;
        Check(model.Entries.Count == 0 && model.Image is null, "Empty search clears old image");
        SearchBox.Text = "sample"; await FilterAsync(); await activeDisplay;
        Check(model.Entries.Count > 0 && ImageList.SelectedIndex == 0 && model.HasImage, "Search selects first result");
        SearchBox.Text = ""; await FilterAsync();
        var stale = OpenAsync(Path.GetDirectoryName(imagePath)!);
        var latest = OpenAsync(imagePath);
        await Task.WhenAll(stale, latest); await activeDisplay;
        Check(model.HasImage && (ImageList.SelectedItem as FileEntry)?.Path == Path.GetFullPath(imagePath), "Latest folder wins");
        var failurePath = model.Entries.First(e => e.Name == "broken.png").Path;
        await OpenAsync(failurePath); await activeDisplay;
        Check(!model.HasImage && model.Message.StartsWith("読み込み失敗"), "Failure keeps selected item");
        var failedIndex = ImageList.SelectedIndex;
        BeginPointer(new Point(10, 10)); EndPointer();
        Check(ImageList.SelectedIndex == (failedIndex + 1) % model.Entries.Count, "Click advances immediately after decode failure");
        await activeDisplay;
        await OpenAsync(imagePath); await activeDisplay;
        SearchBox.Text = "sample"; SelectedImageSort = 2; await FilterAsync(); await activeDisplay;
        content.UpdateLayout();
        FindScrollViewer(ImageList)?.ScrollToVerticalOffset(2);
        content.UpdateLayout();
        var savedOffset = FindScrollViewer(ImageList)?.VerticalOffset ?? 0;
        var savedPath = (ImageList.SelectedItem as FileEntry)?.Path;
        await OpenAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, "previews")); await activeDisplay;
        Check(model.CanGoBack, "Changing folders enables Back");
        await GoBackAsync(); await activeDisplay; content.UpdateLayout();
        Check((ImageList.SelectedItem as FileEntry)?.Path == savedPath && SearchBox.Text == "sample" && SelectedImageSort == 2,
            "Back restores folder selection and listing conditions");
        Check(Math.Abs((FindScrollViewer(ImageList)?.VerticalOffset ?? 0) - savedOffset) < .01, "Back restores list scroll offset");
        Check(model.CanGoForward, "Back enables Forward");
        await GoForwardAsync(); await activeDisplay;
        Check(model.Folder.EndsWith("previews") && !model.CanGoForward, "Forward restores destination");
        await GoBackAsync(); await activeDisplay;
        Check((ImageList.SelectedItem as FileEntry)?.Path == savedPath, "Back after Forward restores selected image");
        var historyCount = history.Count;
        await ReloadAsync(); await activeDisplay;
        Check(history.Count == historyCount, "Reload does not append history");
        SearchBox.Text = ""; SelectedImageSort = 0; await FilterAsync(); await activeDisplay;
        var hiddenFolder = Path.Combine(Path.GetDirectoryName(imagePath)!, "hidden-fixture", "private");
        await OpenAsync(Path.Combine(hiddenFolder, "sample.png")); await activeDisplay;
        Check(!model.CanGoForward, "New folder navigation clears Forward history");
        Check(model.HasImage && model.Folder == hiddenFolder, "Direct address can open hidden folder");
        await OpenAsync(Path.GetDirectoryName(hiddenFolder)!); await activeDisplay;
        Check(model.Locations.All(e => e.Path != hiddenFolder), "Hidden folder omitted from locations");
        await ApplyPreferencesAsync(CurrentPreferences with { HideHiddenFolders = false });
        Check(model.Locations.Any(e => e.Path == hiddenFolder), "Settings refresh folder listing immediately");
        await ApplyPreferencesAsync(CurrentPreferences with { HideHiddenFolders = true });
        Check(model.Locations.All(e => e.Path != hiddenFolder), "Hidden folder disappears after reapplying setting");
        await OpenAsync(imagePath); await activeDisplay;
        var settings = new SettingsWindow(new ViewerPreferences(true, false, 2, false));
        settings.ResetValue("hidden");
        Check(settings.Values.HideHiddenFolders && settings.Values.Dark && !settings.Values.ControlWheel && settings.Values.ListMode == 2,
            "Reset only affects chosen setting");
        settings.Resources["PanelBackground"] = Resources["PanelBackground"];
        settings.Resources["PanelForeground"] = Resources["PanelForeground"];
        settings.ShowInTaskbar = false; settings.ShowActivated = false; settings.WindowStartupLocation = WindowStartupLocation.Manual;
        settings.Left = settings.Top = -32000; settings.Show(); settings.UpdateLayout();
        ApplyTheme(true); CopySettingsTheme(settings); settings.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var settingsContent = (FrameworkElement)settings.Content;
        var settingsRender = new RenderTargetBitmap((int)settings.ActualWidth, (int)settings.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        settingsRender.Render(settingsContent);
        var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsRender));
        using (var stream = File.Create(screenshotPath + ".settings.png")) settingsEncoder.Save(stream);
        settings.Close();
        ApplyTheme(false);
        Check(!CurrentPreferences.Dark && controlWheel, "Uncommitted settings window does not change active preferences");
        ImageList.ScrollIntoView(ImageList.SelectedItem);
        SetZoom(1000);
        content.Measure(new Size(1200, 760)); content.Arrange(new Rect(0, 0, 1200, 760)); content.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await Task.Delay(300);
        var selectedPath = (ImageList.SelectedItem as FileEntry)?.Path;
        var displayed = model.Image;
        for (var mode = 0; mode < 3; mode++)
        {
            ListModeBox.SelectedIndex = mode;
            content.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            Check((ImageList.SelectedItem as FileEntry)?.Path == selectedPath && ReferenceEquals(displayed, model.Image), "Display mode keeps selection and decoded image");
            if (mode == 2)
                Check(model.TileRows.SelectMany(row => row).SequenceEqual(model.Entries), "Tile rows preserve order without duplication");
            if (mode == 0)
                Check(!Descendants(ImageList).OfType<Thumbnail>().Any(), "Names-only mode creates no thumbnails");
            var rendered = new RenderTargetBitmap(1200, 760, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var stream = File.Create(screenshotPath + $".mode{mode}.png"); encoder.Save(stream);
        }
        ApplyTheme(true); content.UpdateLayout();
        var treeProbe = FolderNode(Path.Combine(Path.GetDirectoryName(imagePath)!, "フォルダ表示の確認"));
        treeProbe.Tag = null; treeProbe.Items.Clear();
        var treeChild = FolderNode(Path.Combine(Path.GetDirectoryName(imagePath)!, "子フォルダ"));
        treeChild.Tag = null; treeChild.Items.Clear();
        Check(treeProbe.Header is IconLabel { Kind: ViewerIconKind.Folder }, "Collapsed folder has a folder icon");
        treeProbe.IsExpanded = true; treeChild.IsSelected = true;
        treeProbe.Items.Add(treeChild); Folders.Items.Add(treeProbe); content.UpdateLayout();
        Check(treeProbe.Header is IconLabel { Kind: ViewerIconKind.FolderOpen }
            && treeChild.Header is IconLabel { Kind: ViewerIconKind.Folder }, "Expanded folder icon tracks expansion without changing child icon");
        foreach (var (type, kind) in new[] { (DriveType.Fixed, ViewerIconKind.Drive),
            (DriveType.Removable, ViewerIconKind.RemovableDrive), (DriveType.CDRom, ViewerIconKind.OpticalDrive) }) {
            var driveNode = FolderNode("Z:\\", type);
            Check(driveNode.Header is IconLabel label && label.Kind == kind && label.Text == "Z:\\"
                && (string?)driveNode.Tag == "Z:\\", "Drive icons distinguish fixed, removable and optical drives without accessing their contents");
        }
        Check(Descendants(treeProbe).OfType<VectorIcon>().All(icon => icon.Foreground is SolidColorBrush brush && brush.Color.R > 180),
            "Tree folder icons inherit readable dark foreground");
        Check(treeProbe.Foreground is SolidColorBrush parentText && parentText.Color.R > 180 && treeChild.Foreground is SolidColorBrush childText && childText.Color.R > 180,
            "Dark tree text stays light for parent and selected child");
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var darkRender = new RenderTargetBitmap(1200, 760, 96, 96, PixelFormats.Pbgra32); darkRender.Render(content);
        var darkEncoder = new PngBitmapEncoder(); darkEncoder.Frames.Add(BitmapFrame.Create(darkRender));
        using (var stream = File.Create(screenshotPath + ".dark.png")) darkEncoder.Save(stream);
        Folders.Items.Remove(treeProbe);
        ApplyTheme(false);
        content.UpdateLayout();
        Check(Folders.Items.OfType<System.Windows.Controls.TreeViewItem>().All(node => node.Header is IconLabel { Kind: ViewerIconKind.Drive or ViewerIconKind.RemovableDrive or ViewerIconKind.OpticalDrive }),
            "Every drive root uses an icon header");
        var lightTreeIcons = Descendants(Folders).OfType<VectorIcon>().ToArray();
        Check(lightTreeIcons.Length > 0 && lightTreeIcons.All(icon => icon.Foreground is SolidColorBrush brush && brush.Color.R < 100),
            "Drive icons update to readable light-theme foreground");
        await VerifyLocationModesAsync(imagePath, screenshotPath);
        await VerifyLocationIconsAsync(imagePath, screenshotPath);
        await VerifyFileActionsAsync(imagePath);
        await VerifySendFilesAsync(imagePath,screenshotPath);
        await VerifyDeletionAsync(imagePath,screenshotPath);
        await VerifyTrayAsync(imagePath,screenshotPath);
        await VerifyInstancesAsync(imagePath);
        await VerifyMetadataAsync(imagePath,screenshotPath);
        await VerifyMouseAsync(imagePath,screenshotPath);
        await VerifyLocationNavigationAsync(imagePath,screenshotPath);
        await VerifyFavoritesAsync(imagePath, screenshotPath);
        await VerifyDirectoryShortcutsAsync(imagePath,screenshotPath);
        await VerifyRecentLocationsAsync(imagePath,screenshotPath);
        await VerifyThumbnailSizingAsync(imagePath,screenshotPath);
        await VerifyAppearanceAsync(imagePath, screenshotPath);
        await VerifySpreadAsync(imagePath, screenshotPath);
        await VerifyLoupeAsync(imagePath, screenshotPath);
        await VerifyAnimationAsync(imagePath, screenshotPath);
        await VerifyPsdAsync(imagePath, screenshotPath);
        await VerifyClipAsync(imagePath, screenshotPath);
        await VerifyKeyboardAsync(imagePath, screenshotPath);
        foreach (var dark in new[] { false, true }) {
            ApplyTheme(dark);
            var menu = (System.Windows.Controls.MenuItem)MainMenu.Items[1];
            menu.ApplyTemplate();
            if (menu.Template.FindName("PART_Popup", menu) is System.Windows.Controls.Primitives.Popup nativePopup) nativePopup.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
            menu.IsSubmenuOpen = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (menu.Template.FindName("PART_Popup", menu) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement popup }) {
                popup.UpdateLayout();
                var snapshot = new RenderTargetBitmap((int)Math.Ceiling(popup.ActualWidth), (int)Math.Ceiling(popup.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                snapshot.Render(popup); SaveRender(snapshot, screenshotPath + (dark ? ".menu-dark.png" : ".menu-light.png"));
            }
            menu.IsSubmenuOpen = false;
            var contextMenu = (System.Windows.Controls.ContextMenu)FindResource("FileContextMenu");
            contextMenu.PlacementTarget = ImageList.ItemContainerGenerator.ContainerFromItem(ImageList.SelectedItem) as UIElement ?? ImageList;
            contextMenu.IsOpen = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            contextMenu.UpdateLayout();
            var contextSnapshot = new RenderTargetBitmap((int)Math.Ceiling(contextMenu.ActualWidth), (int)Math.Ceiling(contextMenu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            contextSnapshot.Render(contextMenu); SaveRender(contextSnapshot, screenshotPath + (dark ? ".context-menu-dark.png" : ".context-menu-light.png"));
            contextMenu.IsOpen = false;
        }
        ApplyTheme(false);
        VerifyAbout(screenshotPath);
        await VerifyScrollbarsAsync(screenshotPath);
        await VerifyTileScrollingAsync();
        await VerifyListSettingsAsync();
        await VerifyTileLayoutAsync();
        await VerifySlideshowAsync(imagePath);
        await VerifyRenderingModeAsync(screenshotPath);
        await VerifyNetworkAsync(imagePath, screenshotPath);
        await VerifyCacheAsync(imagePath, screenshotPath);
        await VerifyPdfCacheAsync(imagePath);
        await VerifyArchiveCacheAsync(imagePath);
        await VerifySecurityRecoveryAsync(imagePath);
        await VerifyDataToolsAsync(imagePath, screenshotPath);
        await VerifyUiSizingAsync(imagePath, screenshotPath);
        await VerifyArchivesAsync(imagePath, screenshotPath);
        await VerifyPdfAsync(imagePath, screenshotPath);
        await VerifyArchivePdfAsync(imagePath, screenshotPath);
        await VerifyNestedArchivesAsync(imagePath, screenshotPath);
        await VerifyWatchingAsync(imagePath);
        await VerifyUnflushedListPreferencesAsync(imagePath);
        await VerifyPersistenceAsync(imagePath);
        var fixtureFolder = Path.Combine(Path.GetDirectoryName(imagePath)!, "folder-enumeration");
        if (Directory.Exists(fixtureFolder))
        {
            await OpenAsync(fixtureFolder); await activeDisplay;
            content.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var realizedRows = Descendants(TileList).OfType<System.Windows.Controls.ListBoxItem>().Count();
            Check(model.TileRows.Count > 50 && realizedRows > 0 && realizedRows < 25,
                $"Tile rows remain virtualized regardless of scroll position: rows={model.TileRows.Count}, realized={realizedRows}");
        }
        await VerifyLocationVirtualizationAsync(imagePath);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
