using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyPageNavigationAsync(string imagePath, string screenshotPath)
    {
        WatchFoldersEnabled = false;
        ShowInTaskbar = false; ShowActivated = false; Left = -32000; Top = -32000;
        Show();
        var content = (FrameworkElement)Content;
        content.Measure(new Size(1200, 760)); content.Arrange(new Rect(0, 0, 1200, 760));
        await VerifyKeyboardAsync(imagePath, screenshotPath);
        await VerifyArchivesAsync(imagePath, screenshotPath);
        await VerifyPdfAsync(imagePath, screenshotPath);
    }

    private async Task VerifyKeyboardAsync(string imagePath, string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var saved = CurrentPreferences;
        try {
            await ApplyPreferencesAsync(CurrentPreferences with { Keyboard = new(), SpreadMode = false, Dark = false });
            await OpenAsync(Path.Combine(Path.GetDirectoryName(imagePath)!, "spread-4", "page1.png")); await activeDisplay;
            Check(keyboardSettings.AllFor(ViewerCommand.First).Count == 0 && keyboardSettings.AllFor(ViewerCommand.Last).Count == 0,
                "Boundary commands start unassigned and preserve existing key choices");
            var boundaryKeys = keyboardSettings;
            ApplyKeyboardSettings(keyboardSettings.Add(ViewerCommand.First, new("HOME", 2)).Add(ViewerCommand.Last, new("END", 2)));
            Check(FirstPageMenu.InputGestureText == "Ctrl＋Home" && LastPageMenu.InputGestureText == "Ctrl＋End", "Boundary menu hints follow settings");
            Check(await TryShortcutAsync(Key.End, ModifierKeys.Control, Viewport), "Assigned last shortcut executes"); await activeDisplay;
            Check(ImageList.SelectedIndex == model.Entries.Count - 1, "Last shortcut selects list end");
            FirstPageMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await activeDisplay;
            Check(ImageList.SelectedIndex == 0, "First menu selects list start");
            LastPageMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await activeDisplay;
            Check(ImageList.SelectedIndex == model.Entries.Count - 1, "Last menu selects list end");
            Check(!await TryShortcutAsync(Key.Home, ModifierKeys.Control, SearchBox), "Boundary shortcut preserves text editing");
            SearchBox.Text = "page2"; await FilterAsync(); await activeDisplay;
            await ExecuteViewerCommandAsync(ViewerCommand.Last); await activeDisplay;
            Check(model.Entries.Count == 1 && (ImageList.SelectedItem as FileEntry)?.Name == "page2.png", "Boundary uses filtered list");
            SearchBox.Text = "no-match-boundary"; await FilterAsync(); await activeDisplay;
            await ExecuteViewerCommandAsync(ViewerCommand.First); await ExecuteViewerCommandAsync(ViewerCommand.Last);
            Check(ImageList.SelectedIndex == -1 && model.Image is null, "Empty boundaries remain empty without error");
            SearchBox.Text = ""; await FilterAsync(); await activeDisplay;
            SelectedImageSort = 2; await FilterAsync(); await activeDisplay;
            await ExecuteViewerCommandAsync(ViewerCommand.First); await activeDisplay;
            Check((ImageList.SelectedItem as FileEntry) == model.Entries[0], "First uses current sorted order");
            await SetSpreadModeAsync(true);
            await ExecuteViewerCommandAsync(ViewerCommand.Last); await activeDisplay;
            Check(ImageList.SelectedIndex == model.Entries.Count - 1 && firstPage?.Entry == model.Entries[^1]
                && secondPage?.Entry == model.Entries[0], "Spread last rebases to final image with existing circular pairing");
            Check(await TryShortcutAsync(Key.Home, ModifierKeys.Control, Viewport), "Assigned first shortcut executes"); await activeDisplay;
            Check(ImageList.SelectedIndex == 0 && firstPage?.Entry == model.Entries[0], "Spread first rebases to list start");
            await SetSpreadModeAsync(false); SelectedImageSort = 0; await FilterAsync();
            ApplyKeyboardSettings(boundaryKeys); SelectPageAt(0); await activeDisplay;
            Check(keyboardSettings.AllFor(ViewerCommand.Settings).Count == 0, "Settings command starts without an invented default shortcut");
            var originalKeyboard = keyboardSettings;
            ApplyKeyboardSettings(keyboardSettings.Add(ViewerCommand.Settings, new("F8")).Add(ViewerCommand.Settings, new("F9")));
            foreach (var key in new[] { Key.F8, Key.F9 }) {
                var opened = false; var initialImage = model.Image; var initialSelection = ImageList.SelectedItem;
                var closeDialog = Dispatcher.InvokeAsync(() => {
                    var dialog = OwnedWindows.OfType<SettingsWindow>().SingleOrDefault();
                    if (dialog is null) return;
                    opened = dialog.IsVisible; dialog.Close();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check(await TryShortcutAsync(key, ModifierKeys.None, Viewport), "Assigned settings shortcut executes");
                await closeDialog;
                Check(opened && !OwnedWindows.OfType<SettingsWindow>().Any(), "Settings shortcut opens the real modal window and permits closing");
                Check(ReferenceEquals(initialImage, model.Image) && ReferenceEquals(initialSelection, ImageList.SelectedItem), "Opening settings preserves image and selection");
            }
            ApplyKeyboardSettings(originalKeyboard);
            var bar = (Panel)ThemeButton.Parent;
            Check(bar.Children.IndexOf(ThemeButton) + 1 == bar.Children.IndexOf(FullscreenButton), "Theme button is immediately left of fullscreen button");
            var image = model.Image; var selected = ImageList.SelectedItem; var initialZoom = zoom;
            ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            UpdateLayout();
            Check(DarkTheme.IsChecked && ThemeButton.Content is IconLabel { Text: "ダーク", Kind: ViewerIconKind.Moon }, "Bottom bar switches to dark theme and synchronizes menu");
            Check(Descendants(ThemeButton).OfType<VectorIcon>().First().Foreground is SolidColorBrush text && text.Color.R > 180,
                "Theme button icon follows dark foreground");
            Check(ReferenceEquals(image, model.Image) && ReferenceEquals(selected, ImageList.SelectedItem) && zoom == initialZoom,
                "Theme button preserves displayed image, selection and zoom");
            ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!DarkTheme.IsChecked && ThemeButton.Content is IconLabel { Text: "ライト", Kind: ViewerIconKind.Sun }, "Theme button switches back to light");
            Check(await TryShortcutAsync(Key.Right, ModifierKeys.None, Viewport), "Default next shortcut executes"); await activeDisplay;
            Check(ImageList.SelectedIndex == 1, "Default next shortcut advances one image");
            Check(!await TryShortcutAsync(Key.Right, ModifierKeys.Control, Viewport), "Unassigned modifiers do not trigger plain shortcut");
            var index = ImageList.SelectedIndex;
            Check(!await TryShortcutAsync(Key.Right, ModifierKeys.None, SearchBox) && ImageList.SelectedIndex == index,
                "Text input does not trigger image shortcuts");
            Check(!await TryShortcutAsync(Key.Right, ModifierKeys.None, SortBox), "Combo box input keeps normal navigation");
            Check(!await TryShortcutAsync(Key.F5, ModifierKeys.None, ReloadMenu), "Menu key handling stays with menu");
            var custom = keyboardSettings.Assign(ViewerCommand.Next, new("J", 2))
                .Assign(ViewerCommand.Reload, new("F6")).Assign(ViewerCommand.Grid, new("G"))
                .Assign(ViewerCommand.Theme, new("T", 2));
            ApplyKeyboardSettings(custom);
            Check(NextPageMenu.InputGestureText == "Ctrl＋J" && ReloadMenu.InputGestureText == "F6", "Menu hints track assigned keys");
            Check(!await TryShortcutAsync(Key.Right, ModifierKeys.None, Viewport) && !await TryShortcutAsync(Key.F5, ModifierKeys.None, Viewport),
                "Old key bindings stop executing after reassignment");
            Check(await TryShortcutAsync(Key.J, ModifierKeys.Control, Viewport), "Modified shortcut executes"); await activeDisplay;
            Check(ImageList.SelectedIndex == index + 1, "Modified shortcut changes selected image");
            Check(await TryShortcutAsync(Key.G, ModifierKeys.None, Viewport) && ImageGrid.Visibility == Visibility.Visible, "Shortcut invokes grid toggle");
            await TryShortcutAsync(Key.G, ModifierKeys.None, Viewport);
            var beforeTheme = DarkTheme.IsChecked;
            await TryShortcutAsync(Key.T, ModifierKeys.Control, Viewport);
            Check(DarkTheme.IsChecked != beforeTheme, "Assigned theme shortcut synchronizes bottom button");
            await TryShortcutAsync(Key.F6, ModifierKeys.None, Viewport); await activeDisplay;
            Check((ImageList.SelectedItem as FileEntry)?.Name == "page3.png", "Assigned reload shortcut preserves selected file");

            var multiple = keyboardSettings.Add(ViewerCommand.Next, new("RIGHT"));
            ApplyKeyboardSettings(multiple);
            Check(NextPageMenu.InputGestureText == "Ctrl＋J / →", "Menu hints show all assigned shortcuts");
            Check(await TryShortcutAsync(Key.Left, ModifierKeys.None, Viewport), "Previous shortcut still executes"); await activeDisplay;
            Check(await TryShortcutAsync(Key.Right, ModifierKeys.None, Viewport), "Second shortcut executes same command"); await activeDisplay;
            Check((ImageList.SelectedItem as FileEntry)?.Name == "page3.png", "Second shortcut advances exactly one image");
            ApplyKeyboardSettings(multiple.Remove(ViewerCommand.Next, new("RIGHT")));
            Check(!await TryShortcutAsync(Key.Right, ModifierKeys.None, Viewport), "Removed shortcut stops executing");
            Check(await TryShortcutAsync(Key.J, ModifierKeys.Control, Viewport), "Other shortcut remains active after removal"); await activeDisplay;

            KeyboardSettings? applied = null;
            var settings = new SettingsWindow(new ViewerPreferences(), next => { applied = next.Keyboard; return Task.CompletedTask; }) {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000,
                ShowInTaskbar = false, ShowActivated = false
            };
            Check(!settings.AssignShortcut(ViewerCommand.Next, new("LEFT"), false, out var conflict) && conflict == ViewerCommand.Previous,
                "Editor reports duplicate without applying");
            Check(settings.Values.Keyboard.For(ViewerCommand.Next).Key == "RIGHT", "Rejected duplicate keeps binding unchanged");
            Check(settings.AssignShortcut(ViewerCommand.Next, new("LEFT"), true, out _)
                && settings.Values.Keyboard.For(ViewerCommand.Previous).IsEmpty, "Editor replaces duplicate only when confirmed");
            settings.ResetValue("keys"); settings.AssignShortcut(ViewerCommand.Next, new("J", 2), false, out _);
            Check(settings.EditShortcut(ViewerCommand.Next, null, new("RIGHT"), false, out _)
                && settings.Values.Keyboard.AllFor(ViewerCommand.Next).Count == 2, "Editor adds second key without replacing first");
            Check(settings.EditShortcut(ViewerCommand.Previous, null, new("K"), false, out _), "Editor adds alternate previous key");
            Check(!settings.EditShortcut(ViewerCommand.Next, new("RIGHT"), new("LEFT"), false, out _)
                && settings.Values.Keyboard.AllFor(ViewerCommand.Next).Any(g => g.Key == "RIGHT"), "Rejected edit keeps old key");
            Check(settings.EditShortcut(ViewerCommand.Next, new("RIGHT"), new("LEFT"), true, out _)
                && settings.Values.Keyboard.For(ViewerCommand.Previous).Key == "K"
                && settings.Values.Keyboard.AllFor(ViewerCommand.Next).Count == 2, "Confirmed edit moves one key and preserves alternatives");
            settings.Show(); settings.ShowShortcuts();
            Check(await settings.ApplyChangesAsync() && settings.IsVisible && applied?.For(ViewerCommand.Next).Key == "J" && applied.AllFor(ViewerCommand.Next).Count == 2, "Apply commits shortcut edits and keeps settings open");
            settings.UpdateLayout();
            var content = (FrameworkElement)settings.Content; var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); SaveRender(bitmap, screenshotPath + ".keyboard-settings.png");
            Button EditorButton(string label) => Descendants(settings).OfType<Button>().First(b => Equals(b.Content, label));
            void CaptureKey(Key key) => ((TextBox)settings.FindName("ShortcutCapture")).RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            EditorButton("追加").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((FrameworkElement)settings.FindName("CapturePanel")).Visibility == Visibility.Visible, "Add button starts capture");
            CaptureKey(Key.Escape);
            Check(settings.Values.Keyboard.AllFor(ViewerCommand.Previous).Count == 1, "Escape cancels capture without changes");
            EditorButton("追加").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); CaptureKey(Key.N); settings.UpdateLayout();
            Check(settings.Values.Keyboard.AllFor(ViewerCommand.Previous).Count == 2, "Captured key adds another shortcut");
            EditorButton("解除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); settings.UpdateLayout();
            Check(settings.Values.Keyboard.AllFor(ViewerCommand.Previous).Single().Key == "N"
                && settings.Values.Keyboard.AllFor(ViewerCommand.Next).Count == 2, "Remove button removes only chosen key");
            EditorButton("キーを初期化").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(settings.Values.Keyboard.AllFor(ViewerCommand.Next).Single().Key == "RIGHT"
                && settings.Values.Keyboard.AllFor(ViewerCommand.Previous).Single().Key == "LEFT", "Reset restores original single-key defaults");
            settings.AssignShortcut(ViewerCommand.Next, new(), false, out _); settings.Close();
            Check(applied?.For(ViewerCommand.Next).Key == "J", "Cancel after Apply does not mutate applied shortcut snapshot");
        }
        finally { await ApplyPreferencesAsync(saved); await OpenAsync(imagePath); await activeDisplay; }
    }
}
