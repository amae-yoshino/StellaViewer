using System.IO;
using System.Windows;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyPreferencesRepeatedAsync(string imagePath)
    {
        WatchFoldersEnabled = false; ShowInTaskbar = false; ShowActivated = false; Left = -32000; Top = -32000; Show();
        for (var i = 0; i < 10; i++) { await VerifyUnflushedListPreferencesAsync(imagePath); await VerifyPersistenceAsync(imagePath); }
    }
    private async Task VerifyPersistenceAsync(string imagePath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var directory = Path.Combine(Path.GetDirectoryName(imagePath)!, "restart-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var preferencesFile = Path.Combine(directory, "viewer-preferences.json");
        await File.WriteAllTextAsync(preferencesFile, "{}");
        await ViewerStateStore.SaveAsync(new(), new(), directory);
        MainWindow CreateTestWindow() => new(true, directory) {
            ConstrainStartupWindowToScreen = false, ShowInTaskbar = false, ShowActivated = false,
            WatchFoldersEnabled = false,
            Left = -32000, Top = -32000
        };
        var first = CreateTestWindow();
        first.Width = 1300; first.Height = 820; first.Show();
        await first.RestoreStartupAsync(imagePath);
        first.SelectedImageSort = 2; await first.FilterAsync();
        first.ListModeBox.SelectedIndex = 2;
        first.LocationModeBox.SelectedIndex = 0;
        first.SelectedLocationSort = 1;
        var savedAppearance = new ViewerAppearance(2, GridVisible: true, GridSpacing: 32,
            CheckerColor1: "#123456", CheckerColor2: "#ABCDEF", CheckerSize: 28);
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { Appearance = savedAppearance });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(appearance: savedAppearance, filePath: preferencesFile));
        await first.SetGridVisibleAsync(false); await first.SetGridVisibleAsync(true);
        await first.SetSpreadModeAsync(true);
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { RightBinding = false });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(rightBinding: false, filePath: preferencesFile));
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { Loupe = new(3, 320) });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(loupeMagnification: 3, loupeSize: 320, filePath: preferencesFile));
        var customKeyboard = first.keyboardSettings.Assign(StellaViewer.Core.ViewerCommand.Next, new("J", 2));
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { Keyboard = customKeyboard });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(keyboard: customKeyboard, filePath: preferencesFile));
        first.ThemeButtonClicked(first.ThemeButton, new());
        var savedCache = new ImageCacheSettings(16, 8, 1);
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { Cache = savedCache });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(cache: savedCache, filePath: preferencesFile));
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { AllowNetwork = true });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(allowNetwork: true, filePath: preferencesFile));
        await first.ApplyPreferencesAsync(first.CurrentPreferences with { SoftwareRendering = true });
        await first.SavePreferencesAsync(() => PreferencesStore.SaveChangeAsync(softwareRendering: true, filePath: preferencesFile));
        first.MainGrid.ColumnDefinitions[0].Width = new GridLength(440);
        first.LeftPane.RowDefinitions[0].Height = new GridLength(210);
        first.FolderColumns.ColumnDefinitions[0].Width = new GridLength(.6, GridUnitType.Star);
        first.FolderColumns.ColumnDefinitions[2].Width = new GridLength(.4, GridUnitType.Star);
        first.InfoToggle.IsChecked = true; first.UpdateLayout();
        first.SetZoom(500); first.Rotation.Angle = 90;
        first.ToggleFullscreen(false); first.UpdateLayout();
        first.Close(); await first.closePersistence;
        var stored = ViewerStateStore.Load(directory);
        var preferences = PreferencesStore.Load(preferencesFile);
        Check(preferences.ListMode == 2 && preferences.SortMode == 2 && preferences.LocationListMode == 0 && preferences.LocationSortMode == 1,
            $"Closing waits for independent location, image and sort preference writes: stored={preferences.ListMode}/{preferences.SortMode}/{preferences.LocationListMode}/{preferences.LocationSortMode}, UI={first.ListModeBox.SelectedIndex}/{first.SelectedImageSort}/{first.LocationModeBox.SelectedIndex}/{first.SelectedLocationSort}");
        Check(stored.View.SelectedPath == Path.GetFullPath(imagePath) && Math.Abs(stored.Layout.Width - 1300) < 2 && Math.Abs(stored.Layout.Height - 820) < 2,
            "Closing in fullscreen records last image and underlying normal window bounds");
        Check(!stored.Layout.Maximized && Math.Abs(stored.Layout.LeftPaneWidth - 440) < 2 && Math.Abs(stored.Layout.TreeFraction - .6) < .02,
            "Fullscreen does not overwrite normal pane layout");
        var second = CreateTestWindow();
        Check(second.cacheSettings == savedCache, "New window restores independent memory, disk and prefetch settings");
        Check(second.model.AllowNetwork, "New window restores network access preference");
        Check(second.softwareRendering && System.Windows.Media.RenderOptions.ProcessRenderMode == System.Windows.Interop.RenderMode.SoftwareOnly,
            "New window restores saved software rendering before being shown");
        Check(second.appearance == savedAppearance && second.GridToggle.IsChecked == true, "New window restores background and toolbar grid appearance");
        Check(second.spreadEnabled && !second.rightBinding && second.SpreadToggle.IsChecked == true, "New window restores spread mode and binding direction");
        Check(second.loupeSettings == new LoupeSettings(3, 320) && !second.loupeEnabled, "New window restores loupe parameters but starts disabled");
        Check(second.keyboardSettings.For(StellaViewer.Core.ViewerCommand.Next) == new StellaViewer.Core.ShortcutGesture("J", 2)
            && second.NextPageMenu.InputGestureText == "Ctrl＋J", "New window restores keyboard binding and menu hint");
        Check(second.DarkTheme.IsChecked && second.ThemeButton.Content is IconLabel { Text: "ダーク" }, "New window restores theme chosen using bottom bar");
        Check(second.ListModeBox.SelectedIndex == 2 && second.SelectedImageSort == 2 && second.LocationModeBox.SelectedIndex == 0 && second.SelectedLocationSort == 1
            && Math.Abs(second.Width - stored.Layout.Width) < 1 && Math.Abs(second.Height - stored.Layout.Height) < 1,
            "New window restores list mode, sort and window size before display");
        second.Show(); await second.RestoreStartupAsync(); second.UpdateLayout();
        Check((second.ImageList.SelectedItem as FileEntry)?.Path == Path.GetFullPath(imagePath) && second.model.HasImage,
            "New window reopens last selected image after enumeration and sorting");
        Check(second.zoom == StellaViewer.Core.Zoom.Initial(second.Fit()) && second.Rotation.Angle == 0 && second.InfoToggle.IsChecked == true,
            "Viewer restores pane setting while image zoom and rotation reset");
        await second.RestoreStartupAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, "sample.jpg"));
        Check((second.ImageList.SelectedItem as FileEntry)?.Name == "sample.jpg", "Explicit startup argument overrides saved view");
        second.Close(); await second.closePersistence;
        ApplyRenderingMode(false);

        var settingsFile = Path.Combine(directory, "apply-preferences.json");
        var appliedValues = new ViewerPreferences(true, false, 2, false, 1, 2);
        var settings = new SettingsWindow(appliedValues, async next => {
            await PreferencesStore.SaveChangeAsync(dark: next.Dark, controlWheel: next.ControlWheel,
                listMode: next.ListMode, sortMode: next.SortMode, filePath: settingsFile);
            appliedValues = next;
        }) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false };
        settings.Resources["PanelBackground"] = Resources["PanelBackground"];
        settings.Resources["PanelForeground"] = Resources["PanelForeground"];
        settings.Show(); settings.ResetValue("theme");
        Check(await settings.ApplyChangesAsync() && settings.IsVisible && !appliedValues.Dark && appliedValues.SortMode == 2,
            "Apply persists values without closing or resetting unexposed preferences");
        settings.ResetValue("wheel"); settings.Close();
        Check(!PreferencesStore.Load(settingsFile).ControlWheel, "Cancel after Apply discards only subsequent edits");
        var failed = new SettingsWindow(new(), _ => throw new IOException("test save failure")) {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false
        };
        failed.Show();
        Check(!await failed.ApplyChangesAsync() && failed.IsVisible, "Failed Apply keeps settings window open");
        failed.Close();
    }
}
