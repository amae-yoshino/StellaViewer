using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool fullscreen;
    private WindowStyle savedStyle;
    private ResizeMode savedResize;
    private WindowState savedState;
    private Rect savedBounds;
    private readonly Dictionary<UIElement, Visibility> savedVisibility = [];
    private GridLength[] savedColumns = [];
    private double savedMinimum;
    private int savedTopIndex, savedBottomIndex, savedLeftIndex, savedRightIndex;
    private bool TopBarInUse => MainMenu.Items.OfType<MenuItem>().Any(item => item.IsSubmenuOpen) || ImageAddress.IsEditing;
    private void FullscreenPointerMove(object sender, MouseEventArgs e) => UpdateFullscreenBars(e.GetPosition(WindowSurface));
    private void FullscreenPointerLeave(object sender, MouseEventArgs e)
    {
        if (TopBarInUse)
            FullscreenLeftHost.Visibility = FullscreenRightHost.Visibility = FullscreenBottomHost.Visibility = Visibility.Collapsed;
        else HideFullscreenBars();
    }
    private void HideFullscreenBars()
    {
        FullscreenTopHost.Visibility = FullscreenLeftHost.Visibility = FullscreenRightHost.Visibility = FullscreenBottomHost.Visibility = Visibility.Collapsed;
    }
    private void UpdateFullscreenBars(Point point)
    {
        if (!fullscreen) return;
        var width = WindowSurface.ActualWidth; var height = WindowSurface.ActualHeight;
        if (point.Y <= 8) FullscreenTopHost.Visibility = Visibility.Visible;
        else if (point.Y > FullscreenTopHost.ActualHeight + 16 && !TopBarInUse) FullscreenTopHost.Visibility = Visibility.Collapsed;
        if (point.X <= 8) FullscreenLeftHost.Visibility = Visibility.Visible;
        else if (point.X > FullscreenLeftHost.ActualWidth + 16) FullscreenLeftHost.Visibility = Visibility.Collapsed;
        if (point.X >= width - 8) FullscreenRightHost.Visibility = Visibility.Visible;
        else if (point.X < width - FullscreenRightHost.ActualWidth - 16) FullscreenRightHost.Visibility = Visibility.Collapsed;
        if (point.Y >= height - 10) FullscreenBottomHost.Visibility = Visibility.Visible;
        else if (point.Y < height - FullscreenBottomHost.ActualHeight - 16) FullscreenBottomHost.Visibility = Visibility.Collapsed;
    }
    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ClampPan()
    {
        var rotated = Math.Abs(Rotation.Angle % 180) > 0;
        var width = rotated ? Picture.Height : Picture.Width;
        var height = rotated ? Picture.Width : Picture.Height;
        var x = Math.Max(0, (width - Viewport.ActualWidth) / 2);
        var y = Math.Max(0, (height - Viewport.ActualHeight) / 2);
        Pan.X = Math.Clamp(Pan.X, -x, x); Pan.Y = Math.Clamp(Pan.Y, -y, y);
        RefreshLoupe();
    }
    private void ToggleFullscreen(bool resizeWindow = true)
    {
        CancelPointer();
        if (!fullscreen)
        {
            preFullscreenLayout = CaptureSavedState().Layout;
            savedStyle = WindowStyle; savedResize = ResizeMode; savedState = WindowState;
            savedBounds = RestoreBounds;
            savedColumns = MainGrid.ColumnDefinitions.Select(c => c.Width).ToArray();
            savedMinimum = MainGrid.ColumnDefinitions[0].MinWidth;
            var leftWidth = MainGrid.ColumnDefinitions[0].ActualWidth;
            // Keep bindings and selection alive while moving panels between hosts.
            TopBars.DataContext = LeftPane.DataContext = InfoPanel.DataContext = BottomBar.DataContext = model;
            foreach (UIElement element in Shell.Children.Cast<UIElement>().Where(e => e != MainGrid)
                .Concat(MainGrid.Children.Cast<UIElement>().Where(e => e != ViewerPane)))
            { savedVisibility[element] = element.Visibility; element.Visibility = Visibility.Collapsed; }
            savedBottomIndex = Shell.Children.IndexOf(BottomBar);
            savedTopIndex = Shell.Children.IndexOf(TopBars);
            savedLeftIndex = MainGrid.Children.IndexOf(LeftPane); savedRightIndex = MainGrid.Children.IndexOf(InfoPanel);
            Shell.Children.Remove(TopBars); Shell.Children.Remove(BottomBar); MainGrid.Children.Remove(LeftPane); MainGrid.Children.Remove(InfoPanel);
            FullscreenTopHost.Child = TopBars; FullscreenLeftHost.Child = LeftPane; FullscreenRightHost.Child = InfoPanel; FullscreenBottomHost.Child = BottomBar;
            FullscreenLeftHost.Width = Math.Min(leftWidth, Math.Max(280, WindowSurface.ActualWidth * .5));
            TopBars.Visibility = LeftPane.Visibility = InfoPanel.Visibility = BottomBar.Visibility = Visibility.Visible;
            HideFullscreenBars(); FullscreenOverlay.Visibility = Visibility.Visible;
            MainGrid.ColumnDefinitions[0].MinWidth = 0;
            foreach (var column in MainGrid.ColumnDefinitions) column.Width = new GridLength(0);
            MainGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            fullscreen = true;
            if (resizeWindow) { WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; }
        }
        else
        {
            fullscreen = false;
            FullscreenOverlay.Visibility = Visibility.Collapsed; HideFullscreenBars();
            FullscreenTopHost.Child = FullscreenLeftHost.Child = FullscreenRightHost.Child = FullscreenBottomHost.Child = null;
            MainGrid.Children.Insert(savedLeftIndex, LeftPane); MainGrid.Children.Insert(savedRightIndex, InfoPanel);
            Shell.Children.Insert(savedTopIndex, TopBars); Shell.Children.Insert(savedBottomIndex, BottomBar);
            foreach (var pair in savedVisibility) pair.Key.Visibility = pair.Value;
            savedVisibility.Clear();
            for (var i = 0; i < savedColumns.Length; i++) MainGrid.ColumnDefinitions[i].Width = savedColumns[i];
            MainGrid.ColumnDefinitions[0].MinWidth = savedMinimum;
            if (resizeWindow)
            {
                WindowState = WindowState.Normal; WindowStyle = savedStyle; ResizeMode = savedResize;
                if (!savedBounds.IsEmpty) { Left = savedBounds.Left; Top = savedBounds.Top; Width = savedBounds.Width; Height = savedBounds.Height; }
                WindowState = savedState;
            }
        }
        FullscreenButton.Content = new IconLabel { Kind = fullscreen ? ViewerIconKind.ExitFullscreen : ViewerIconKind.Fullscreen,
            Text = fullscreen ? "全画面を終了" : "全画面", IconSize = 14 };
        FullscreenMenu.IsChecked = fullscreen;
        ApplyToolbarPlacement();
    }
}
