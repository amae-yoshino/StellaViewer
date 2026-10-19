using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyRequestedFeaturesAsync(string imagePath, string screenshotPath)
    {
        await VerifyLocationGroupingAsync(imagePath,screenshotPath+".grouping.txt");
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
        var root = Path.Combine(Path.GetDirectoryName(screenshotPath)!, "ui-requested-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var probe = new MainWindow(false, root) { WatchFoldersEnabled = false, ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        void Shortcut(string linkPath, string target)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try {
                dynamic shortcut = shell.CreateShortcut(linkPath);
                try { shortcut.TargetPath = target; shortcut.Arguments = "--must-not-execute"; shortcut.Save(); }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        try {
            probe.Show(); var content = (FrameworkElement)probe.Content;
            content.Measure(new Size(1200,760)); content.Arrange(new Rect(0,0,1200,760));
            await probe.OpenAsync(imagePath); await probe.activeDisplay;
            foreach (var selector in new[] { probe.SortBox, probe.LocationSortBox })
                Check(selector.Items.Cast<ComboBoxItem>().Select(item => item.Tag?.ToString()).SequenceEqual(new[] { "0", "3", "1", "4", "2", "5" }),
                    "Sort directions are adjacent while stored sort IDs remain stable");
            var selected = (probe.ImageList.SelectedItem as FileEntry)?.Path;
            probe.SelectedImageSort = 3; await probe.FilterAsync(); await probe.activeDisplay;
            Check(probe.SortBox.SelectedIndex == 1 && probe.CurrentPreferences.SortMode == 3, "Visible sort position does not change persisted meaning");
            Check(probe.model.Entries.Select(e=>e.Path).SequenceEqual(FolderBrowser.SortImages(probe.all,3).Select(e=>e.Path))
                && (probe.ImageList.SelectedItem as FileEntry)?.Path == selected, "Descending image UI preserves selection and order");
            probe.SelectedLocationSort = 3; await probe.SortLocationsAsync();
            Check(probe.model.Locations.SequenceEqual(FolderBrowser.SortLocations(probe.model.Locations,3)), "Descending location UI uses selected direction");
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { EnlargeSmallImages = true });
            Check(probe.zoom > 100 && Math.Abs(probe.zoom-Zoom.Initial(probe.Fit(),true))<.001, "Small image enlarges to viewport with maximum applied");
            probe.SetZoom(200); probe.automaticZoom=false;
            await probe.ApplyPreferencesAsync(probe.CurrentPreferences with { EnlargeSmallImages = false });
            Check(probe.zoom == 200, "Fit preference preserves manual zoom"); probe.ResetZoom();
            Check(probe.zoom == 100, "Disabled small-image setting restores original initial sizing");
            var settings = new SettingsWindow(probe.CurrentPreferences, probe.ApplyPreferencesAsync) { ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual };
            settings.Show(); ((CheckBox)settings.FindName("EnlargeSmall")).IsChecked=true;
            Check(await settings.ApplyChangesAsync() && probe.enlargeSmallImages && settings.IsVisible, "Small-image setting applies without closing settings");
            settings.ResetValue("enlarge-small"); Check(!settings.Values.EnlargeSmallImages,"Small-image reset defaults off"); settings.Close();
            Check(probe.enlargeSmallImages,"Cancel after Apply preserves applied fit setting");
            probe.InfoToggle.IsChecked=true; probe.UpdateLayout();
            var width = probe.InfoContent.ActualWidth;
            probe.InfoResizeGrip.RaiseEvent(new DragDeltaEventArgs(-120,0) { RoutedEvent=Thumb.DragDeltaEvent }); probe.UpdateLayout();
            Check(probe.InfoContent.ActualWidth > width+50 && probe.Viewport.ActualWidth>=100,"Information panel grows while keeping image reachable");
            var savedWidth=probe.CaptureSavedState().Layout.InfoPanelWidth;
            probe.InfoToggle.IsChecked=false; probe.UpdateLayout(); probe.InfoToggle.IsChecked=true; probe.UpdateLayout();
            Check(Math.Abs(probe.InfoContent.ActualWidth-savedWidth)<2,"Information width survives closing and reopening");
            probe.ToggleFullscreen(false); probe.UpdateFullscreenBars(new Point(probe.WindowSurface.ActualWidth-1,100)); probe.UpdateLayout();
            probe.InfoResizeGrip.RaiseEvent(new DragDeltaEventArgs(-40,0) { RoutedEvent=Thumb.DragDeltaEvent }); probe.UpdateLayout();
            Check(probe.infoPanelWidth > savedWidth && probe.FullscreenRightHost.IsVisible,"Fullscreen information overlay is resizable"); probe.ToggleFullscreen(false);
            await ViewerStateStore.SaveAsync(probe.CaptureSavedState().Layout,new(),root);
            await PreferencesStore.SaveChangeAsync(sortMode:4,locationSortMode:5,filePath:probe.preferenceFilePath);
            var restored = new MainWindow(true,root);
            Check(restored.infoPanelWidth == probe.infoPanelWidth,"Information width restores in a new window"); restored.Close(); await restored.TemporaryCleanup;
            Check(restored.SelectedImageSort==4 && restored.SortBox.SelectedIndex==3 && restored.SelectedLocationSort==5,
                "Previously stored descending sort IDs restore to the newly grouped options");

            var link = Path.Combine(root,"image.lnk"); Shortcut(link,imagePath);
            Check(WindowsShortcut.Resolve(link,false)==imagePath,"Windows shortcut resolves physical target without executing arguments");
            await probe.OpenAsync(link); await probe.activeDisplay;
            Check((probe.ImageList.SelectedItem as FileEntry)?.Path==imagePath && probe.model.HasImage,"Opening LNK displays target image");
            var folderLink=Path.Combine(root,"folder.lnk"); Shortcut(folderLink,root);
            await probe.OpenAsync(folderLink); await probe.activeDisplay;
            Check(probe.model.Folder==root,"Opening folder LNK browses target folder");
            var applicationLink=Path.Combine(root,"application.lnk"); Shortcut(applicationLink,Environment.ProcessPath!);
            var rejected=false;try{WindowsShortcut.Resolve(applicationLink,false);}catch(NotSupportedException){rejected=true;}
            Check(rejected,"Executable shortcut target is rejected without launching");
            var missingTarget=Path.Combine(root,"missing.png");File.Copy(imagePath,missingTarget);
            var missingLink=Path.Combine(root,"missing.lnk");Shortcut(missingLink,missingTarget);File.Delete(missingTarget);
            rejected=false;try{WindowsShortcut.Resolve(missingLink,false);}catch(FileNotFoundException){rejected=true;}
            Check(rejected,"Missing shortcut target reports failure without opening an unrelated folder");

            var pdf=Path.Combine(root,"book.pdf");File.WriteAllText(pdf,"registration test only");
            var archive=Path.Combine(root,"book.zip");File.WriteAllText(archive,"registration test only");
            async Task Drop(UIElement target,params string[] paths) {
                var data=new DataObject(DataFormats.FileDrop,paths);
                var constructor=typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single(c=>c.GetParameters().Length==5);
                var args=(DragEventArgs)constructor.Invoke([data,DragDropKeyStates.None,DragDropEffects.Link,target,new Point()]);
                args.RoutedEvent=DragDrop.PreviewDropEvent;
                target.RaiseEvent(args);await probe.registrationDrop;Check(args.Handled,"Registration drop does not bubble to file-opening handler");
            }
            var folderBefore=probe.model.Folder;
            await Drop(probe.FavoritesButton,imagePath,archive,pdf,root,link);
            Check((await probe.favoritesStore.LoadAsync()).Length==4 && probe.model.Folder==folderBefore,"Favorites drop accepts physical viewing targets, resolves LNK and deduplicates");
            await probe.ShowFavoritesAsync(true); await Drop(probe.FavoritesPanel,imagePath);
            Check((await probe.favoritesStore.LoadAsync()).Length==4,"Favorites panel drop supports duplicates without duplicate rows");
            await Drop(probe.DirectoryShortcutsButton,root,imagePath);
            Check((await probe.directoryShortcutStore.LoadAsync()).Length==1 && probe.model.Status.Contains("1件を登録できません"),"Folder-link drop accepts only folders and reports rejected files");
            await probe.directoryShortcutStore.RenameAsync(root,"Custom name");
            await probe.ShowDirectoryShortcutsAsync();await Drop(probe.DirectoryShortcutsPanel,folderLink);
            Check((await probe.directoryShortcutStore.LoadAsync()).Single().Name=="Custom name","Duplicate dropped folder preserves renamed link");
            await Drop(probe.FavoritesButton,@"\\stella-drop.invalid\share\image.png");
            Check((await probe.favoritesStore.LoadAsync()).Length==4 && probe.model.Status.Contains("ネットワーク"),"Drop obeys disabled network setting");
            foreach(var dark in new[]{false,true}) {
                probe.ApplyTheme(dark);probe.UpdateLayout();
                var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(probe.Content as Visual),null,new Rect(0,0,1200,760));
                var bitmap=new RenderTargetBitmap(1200,760,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);SaveRender(bitmap,screenshotPath+(dark?".dark.png":".light.png"));
            }
        }
        finally { probe.Close();await probe.TemporaryCleanup; }
    }
}
