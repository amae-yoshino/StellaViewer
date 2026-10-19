using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace StellaViewer.App;

public enum ViewerIconKind { None, Back, Forward, Up, Refresh, Settings, RotateLeft, RotateRight, Grid, SinglePage, Spread, Fullscreen, ExitFullscreen, Reset, Info, ChevronRight, Sun, Moon, Folder, FolderOpen, Drive, RemovableDrive, OpticalDrive, Archive, Pdf, Image, Star, History, Play, Pause, Fit }

// Original 24-unit line icons, independent of fonts and raster image resolution.
public sealed class VectorIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ViewerIconKind), typeof(VectorIcon),
        new FrameworkPropertyMetadata(ViewerIconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(VectorIcon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public ViewerIconKind Kind { get => (ViewerIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    private static readonly IReadOnlyDictionary<ViewerIconKind, Geometry> Shapes = CreateShapes();

    static VectorIcon()
    {
        WidthProperty.OverrideMetadata(typeof(VectorIcon), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(VectorIcon), new FrameworkPropertyMetadata(16.0));
    }
    public VectorIcon() { IsHitTestVisible = false; }
    private static IReadOnlyDictionary<ViewerIconKind, Geometry> CreateShapes()
    {
        var data = new Dictionary<ViewerIconKind, string> {
            [ViewerIconKind.Back] = "M19,12 H5 M11,5 L4,12 11,19",
            [ViewerIconKind.Forward] = "M5,12 H19 M13,5 L20,12 13,19",
            [ViewerIconKind.Up] = "M12,20 V4 M5,11 L12,4 19,11",
            [ViewerIconKind.Refresh] = "M20,10 A8,8 0 1 0 20,15 M20,4 V10 H14",
            [ViewerIconKind.Settings] = "M9,3 H15 L16,6 19,7 21,10 19,12 21,14 19,17 16,18 15,21 H9 L8,18 5,17 3,14 5,12 3,10 5,7 8,6 Z M16,12 A4,4 0 1 0 8,12 A4,4 0 1 0 16,12",
            [ViewerIconKind.RotateLeft] = "M4,9 A8,8 0 1 1 4,15 M4,3 V9 H10",
            [ViewerIconKind.RotateRight] = "M20,9 A8,8 0 1 0 20,15 M20,3 V9 H14",
            [ViewerIconKind.Grid] = "M4,4 H20 V20 H4 Z M4,12 H20 M12,4 V20",
            [ViewerIconKind.SinglePage] = "M6,3 H16 L19,6 V21 H6 Z M15,3 V7 H19",
            [ViewerIconKind.Spread] = "M12,5 C9,3 5,3 2,4 V20 C5,19 9,19 12,21 C15,19 19,19 22,20 V4 C19,3 15,3 12,5 Z M12,5 V21",
            [ViewerIconKind.Fullscreen] = "M9,4 H4 V9 M15,4 H20 V9 M4,15 V20 H9 M20,15 V20 H15",
            [ViewerIconKind.ExitFullscreen] = "M4,9 H9 V4 M20,9 H15 V4 M9,20 V15 H4 M15,20 V15 H20",
            [ViewerIconKind.Reset] = "M5,8 A8,8 0 1 1 4,14 M4,3 V9 H10 M9,12 H15 M12,9 V15",
            [ViewerIconKind.Info] = "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M12,10 V17 M12,7 V7.2",
            [ViewerIconKind.ChevronRight] = "M9,5 L16,12 9,19",
            [ViewerIconKind.Sun] = "M17,12 A5,5 0 1 0 7,12 A5,5 0 1 0 17,12 M12,2 V4 M12,20 V22 M2,12 H4 M20,12 H22 M5,5 L6.5,6.5 M17.5,17.5 L19,19 M5,19 L6.5,17.5 M17.5,6.5 L19,5",
            [ViewerIconKind.Moon] = "M20,15 A9,9 0 1 1 9,3 A7,7 0 0 0 20,15 Z",
            [ViewerIconKind.Folder] = "M3,6 H10 L12,8 H21 V20 H3 Z M3,6 V4 H9 L11,6 H20 V8",
            [ViewerIconKind.FolderOpen] = "M3,20 V5 H9 L12,8 H20 V11 M3,20 L6,11 H22 L19,20 Z",
            [ViewerIconKind.Drive] = "M5,4 H19 L22,15 V20 H2 V15 Z M2,15 H22 M16,18 H16.2 M19,18 H19.2",
            [ViewerIconKind.RemovableDrive] = "M8,8 V2 H16 V8 M12,2 V6 M6,8 H18 V22 H6 Z M9,18 H15",
            [ViewerIconKind.OpticalDrive] = "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M15,12 A3,3 0 1 0 9,12 A3,3 0 1 0 15,12 M5,8 L8,5 M16,19 L19,16",
            [ViewerIconKind.Archive] = "M4,3 H20 V21 H4 Z M4,7 H20 M10,3 V7 M12,7 V10 M10,10 H12 M12,12 H14 M10,14 H12 M10,17 H14 V20 H10 Z",
            [ViewerIconKind.Pdf] = "M5,2 H15 L20,7 V22 H5 Z M15,2 V7 H20 M8,11 H17 M8,15 H17 M8,19 H13",
            [ViewerIconKind.Image] = "M3,4 H21 V20 H3 Z M3,17 L9,11 14,16 17,12 21,16 M16,8 H16.2",
            [ViewerIconKind.Star] = "M12,2 L15,8.5 22,9.5 17,14.5 18,22 12,18.5 6,22 7,14.5 2,9.5 9,8.5 Z",
            [ViewerIconKind.History] = "M21,12 A9,9 0 1 0 3,12 A9,9 0 1 0 21,12 M12,6 V12 L16,15",
            [ViewerIconKind.Play] = "M7,4 L20,12 7,20 Z",
            [ViewerIconKind.Pause] = "M7,4 V20 M17,4 V20",
            [ViewerIconKind.Fit] = "M3,8 V3 H8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16 M7,8 H17 V16 H7 Z"
        };
        return data.ToDictionary(item => item.Key, item => { var shape = Geometry.Parse(item.Value); shape.Freeze(); return shape; });
    }
    protected override void OnRender(DrawingContext context)
    {
        if (!Shapes.TryGetValue(Kind, out var shape)) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0) return;
        context.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawGeometry(null, new Pen(Foreground, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, shape);
        context.Pop(); context.Pop();
    }
}

public sealed class IconLabel : StackPanel
{
    private readonly VectorIcon icon = new();
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ViewerIconKind), typeof(IconLabel), new PropertyMetadata(ViewerIconKind.None, Changed));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(IconLabel), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(IconLabel), new PropertyMetadata(16.0, Changed));
    public ViewerIconKind Kind { get => (ViewerIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public IconLabel() {
        Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center; Children.Add(icon); Children.Add(label); Update();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((IconLabel)sender).Update();
    private void Update() {
        icon.Kind = Kind; icon.Width = icon.Height = IconSize; label.Text = Text;
        icon.Visibility = Kind == ViewerIconKind.None ? Visibility.Collapsed : Visibility.Visible;
        icon.Margin = new Thickness(0, 0, string.IsNullOrEmpty(Text) ? 0 : 5, 0);
    }
    public override string ToString() => Text;
}
