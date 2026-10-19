using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async Task VerifyScrollbarsAsync(string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var viewer = new ScrollViewer { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 640, Height = 640, Background = Brushes.SteelBlue } };
        WindowSurface.Children.Add(viewer); var saved = DarkTheme.IsChecked;
        try {
            UpdateLayout();
            var bars = Descendants(viewer).OfType<ScrollBar>().ToArray();
            var vertical = bars.Single(bar => bar.Orientation == Orientation.Vertical);
            var horizontal = bars.Single(bar => bar.Orientation == Orientation.Horizontal);
            Check(vertical.ActualWidth <= 14 && horizontal.ActualHeight <= 14, "Modern scrollbars keep compact hit area in both orientations");
            async Task Drag(ScrollBar bar, double x, double y) {
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(x, y) { RoutedEvent = Thumb.DragDeltaEvent });
                track.Thumb.RaiseEvent(new DragCompletedEventArgs(x, y, false) { RoutedEvent = Thumb.DragCompletedEvent });
                UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background); UpdateLayout();
            }
            await Drag(vertical, 0, 24);
            Check(viewer.VerticalOffset > 0 && viewer.HorizontalOffset == 0, "Vertical thumb drag scrolls down");
            var previousVertical = viewer.VerticalOffset;
            await Drag(horizontal, 24, 0);
            Check(viewer.HorizontalOffset > 0 && viewer.VerticalOffset == previousVertical, "Horizontal thumb drag scrolls right without vertical movement");
            ScrollBar.PageDownCommand.Execute(null, vertical); UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Check(viewer.VerticalOffset > previousVertical, "Track page command still scrolls a viewport");
            viewer.ScrollToTop(); UpdateLayout();
            viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent }); UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Check(viewer.VerticalOffset > 0, "Wheel scrolling remains available");
            viewer.Content = new Border { Width = 640000, Height = 640000, Background = Brushes.SteelBlue }; UpdateLayout();
            Check(((Track)vertical.Template.FindName("PART_Track", vertical)).Thumb.ActualHeight >= 24
                && ((Track)horizontal.Template.FindName("PART_Track", horizontal)).Thumb.ActualWidth >= 24,
                "Long lists retain a usable minimum thumb length");
            foreach (var dark in new[] { false, true }) {
                ApplyTheme(dark); UpdateLayout();
                var thumb = ((Track)vertical.Template.FindName("PART_Track", vertical)).Thumb;
                var grip = (Border)thumb.Template.FindName("Grip", thumb);
                Check(grip.CornerRadius.TopLeft > 0 && grip.Background is SolidColorBrush color && color.Color.R > 100,
                    "Scrollbar uses rounded visible thumb in both themes");
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(viewer), null, new Rect(0, 0, 180, 180));
                var bitmap = new RenderTargetBitmap(180, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                SaveRender(bitmap, screenshotPath + (dark ? ".scrollbar-dark.png" : ".scrollbar-light.png"));
            }
        }
        finally { WindowSurface.Children.Remove(viewer); ApplyTheme(saved); }
    }
}
