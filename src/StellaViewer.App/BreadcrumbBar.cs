using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public sealed class PathNavigationEventArgs(string path) : EventArgs
{
    public string Path { get; } = path;
}

public sealed class BreadcrumbBar : UserControl
{
    public static readonly DependencyProperty CurrentPathProperty = DependencyProperty.Register(
        nameof(CurrentPath), typeof(string), typeof(BreadcrumbBar), new PropertyMetadata("", PathChanged));
    public string CurrentPath { get => (string)GetValue(CurrentPathProperty); set => SetValue(CurrentPathProperty, value); }
    public event EventHandler<PathNavigationEventArgs>? NavigateRequested;
    private readonly Grid layout = new() { Background = Brushes.Transparent, MinHeight = 28 };
    private readonly StackPanel crumbs = new() { Orientation = Orientation.Horizontal };
    private readonly TextBox editor = new() { Visibility = Visibility.Collapsed, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer scroll;

    public BreadcrumbBar()
    {
        scroll = new ScrollViewer { Content = crumbs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 400 };
        layout.Children.Add(scroll); layout.Children.Add(editor); Content = layout;
        layout.MouseLeftButtonDown += (_, e) => { if (editor.Visibility != Visibility.Visible) { BeginEdit(); e.Handled = true; } };
        editor.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) { SubmitAddress(editor.Text); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndEdit(); e.Handled = true; }
        };
        editor.LostKeyboardFocus += (_, _) => EndEdit();
        SizeChanged += (_, _) => scroll.MaxWidth = Math.Max(0, ActualWidth - 36);
        ToolTip = "階層をクリックして移動／余白をクリックしてアドレスを入力";
    }
    internal void BeginEdit() { editor.Text = BrowsePath.PhysicalPath(CurrentPath); editor.Visibility = Visibility.Visible; scroll.Visibility = Visibility.Collapsed; editor.Focus(); editor.SelectAll(); }
    internal bool IsEditing => editor.Visibility == Visibility.Visible;
    internal void SubmitAddress(string text)
    {
        var path = text.Trim().Trim('"'); EndEdit();
        if (path.Length > 0 && !BrowsePath.IsVirtual(path)) NavigateRequested?.Invoke(this, new(path));
    }
    private void EndEdit() { editor.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible; }
    private static void PathChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e) => ((BreadcrumbBar)obj).Rebuild();
    private void Rebuild()
    {
        crumbs.Children.Clear();
        if (string.IsNullOrEmpty(CurrentPath)) return;
        var pdf = PdfLocation.Parse(CurrentPath);
        var archive = ArchiveLocation.Parse(pdf?.DocumentPath ?? CurrentPath);
        var physical = BrowsePath.PhysicalPath(CurrentPath);
        var root = Path.GetPathRoot(physical);
        if (string.IsNullOrEmpty(root)) return;
        AddCrumb(root, root);
        var accumulated = root;
        foreach (var segment in physical[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = Path.Combine(accumulated, segment);
            crumbs.Children.Add(new VectorIcon { Kind = ViewerIconKind.ChevronRight, Width = 12, Height = 12, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
            AddCrumb(segment, accumulated);
        }
        if (archive is not null) {
            var levels = new Stack<ArchiveLocation>();
            for (var level = archive; level is not null; level = ArchiveLocation.Parse(level.ArchivePath)) levels.Push(level);
            foreach (var level in levels) {
                var entry = "";
                foreach (var segment in level.EntryPath.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
                    entry = entry.Length == 0 ? segment : entry + "/" + segment;
                    crumbs.Children.Add(new VectorIcon { Kind = ViewerIconKind.ChevronRight, Width = 12, Height = 12, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
                    AddCrumb(segment, (level with { EntryPath = entry, IsFolder = entry != level.EntryPath || level.IsFolder }).Identity);
                }
            }
        }
        if (pdf?.Page is int page) {
            crumbs.Children.Add(new VectorIcon { Kind = ViewerIconKind.ChevronRight, Width = 12, Height = 12, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center });
            AddCrumb($"ページ {page}", pdf.Identity);
        }
        _ = Dispatcher.InvokeAsync(() => scroll.ScrollToHorizontalOffset(scroll.ExtentWidth), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void AddCrumb(string label, string target)
    {
        var button = new Button { Content = label, ToolTip = BrowsePath.DisplayPath(target), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        button.SetResourceReference(ForegroundProperty, "PanelForeground");
        button.Click += (_, e) => { NavigateRequested?.Invoke(this, new(target)); e.Handled = true; };
        crumbs.Children.Add(button);
    }
}
