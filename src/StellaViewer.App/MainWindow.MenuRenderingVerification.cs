using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal async Task VerifyMenuRenderingAsync(string resultPath)
    {
        var originalDark = DarkTheme.IsChecked == true;
        var originalSoftware = softwareRendering;
        var report = new StringBuilder();
        async Task Idle() { await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        void Snapshot(FrameworkElement element, string suffix)
        {
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element); SaveRender(bitmap, resultPath + suffix + ".png");
        }
        void Dump(DependencyObject element, int depth = 0)
        {
            report.Append(' ', depth * 2).Append(element.GetType().Name);
            if (element is FrameworkElement visual) report.Append($" {visual.Name} {visual.ActualWidth}x{visual.ActualHeight} {visual.Visibility}");
            if (element is Border border) report.Append($" Background={border.Background} Border={border.BorderBrush}");
            if (element is System.Windows.Shapes.Rectangle rectangle) report.Append($" Fill={rectangle.Fill}");
            report.AppendLine();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Dump(VisualTreeHelper.GetChild(element, i), depth + 1);
        }
        void CheckChrome(DependencyObject element)
        {
            if (element is ItemsPresenter or MenuItem) return;
            if (element is System.Windows.Shapes.Rectangle { Name.Length: 0, Fill: SolidColorBrush fill }
                && fill.Color != ((SolidColorBrush)Resources["PanelBackground"]).Color)
                throw new InvalidOperationException("Menu gutter retains the default light color on first opening");
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) CheckChrome(VisualTreeHelper.GetChild(element, i));
        }
        try {
            foreach (var software in new[] { false, true }) {
                ApplyRenderingMode(software);
                foreach (var dark in new[] { true, false }) {
                    DarkTheme.IsChecked = dark;
                    ApplyTheme(dark);
                    var name = $"{(software ? "software" : "auto")}-{(dark ? "dark" : "light")}";
                    for (var cycle = 0; cycle < 12; cycle++) {
                        var menu = (MenuItem)MainMenu.Items[cycle % MainMenu.Items.Count];
                        menu.IsSubmenuOpen = true; await Idle();
                        if (menu.Template.FindName("PART_Popup", menu) is not Popup { Child: FrameworkElement content, IsOpen: true }) throw new InvalidOperationException("Menu popup did not open");
                        CheckChrome(content);
                        if (cycle == 1) {
                            report.AppendLine(name); Dump(content); Snapshot(content, "." + name);
                            if (!software && !dark) await File.WriteAllTextAsync(resultPath + ".template.xaml", XamlWriter.Save(menu.Template));
                            var nested = menu.Items.OfType<MenuItem>().First(item => item.HasItems);
                            nested.IsSubmenuOpen = true; await Idle();
                            if (nested.Template.FindName("PART_Popup", nested) is Popup { Child: FrameworkElement child }) { CheckChrome(child); Snapshot(child, "." + name + "-nested"); }
                            nested.IsSubmenuOpen = false;
                        }
                        menu.IsSubmenuOpen = false; await Idle();
                    }
                    var contextMenu = (ContextMenu)FindResource("FileContextMenu");
                    contextMenu.PlacementTarget = ImageList; contextMenu.IsOpen = true; await Idle();
                    try { CheckChrome(contextMenu); Snapshot(contextMenu, "." + name + "-context"); }
                    finally { contextMenu.IsOpen = false; }
                    Snapshot((FrameworkElement)Content, "." + name + "-closed");
                }
            }
            await File.WriteAllTextAsync(resultPath + ".visuals.txt", report.ToString());
        }
        finally { DarkTheme.IsChecked = originalDark; ApplyRenderingMode(originalSoftware); ApplyTheme(originalDark); }
    }
}
