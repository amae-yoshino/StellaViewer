using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;

namespace StellaViewer.App;

public partial class MainWindow
{
    internal void VerifyAbout(string screenshotPath)
    {
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
        var savedDark = DarkTheme.IsChecked == true;
        var selected = ImageList.SelectedItem; var image = model.Image;
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        try {
            Check(AboutMenu.Header.ToString() == "バージョン情報…", "Help contains About entry");
            foreach (var dark in new[] { false, true }) {
                ApplyTheme(dark);
                var dialog = CreateAboutWindow();
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000;
                dialog.ShowActivated = false;
                try {
                    dialog.Show(); dialog.UpdateLayout();
                    Check(dialog.Information.Contains($"バージョン: {version}") && dialog.Information.Contains(".NET 10.")
                        && dialog.Information.Contains("アプリの動作形式: x64（64ビット）")
                        && dialog.Information.Contains("Windowsのバージョン: ") && dialog.Information.Contains("OSビルド: ")
                        && !dialog.Information.Contains("X64"), "About reports readable Windows details, runtime and lowercase architecture");
                    var notices = (TextBox)dialog.FindName("LibraryNotices");
                    Check(notices.Text.Contains("Magick.NET") && notices.Text.Contains("SharpCompress") && notices.Text.Contains("PDFtoImage") && notices.Text.Contains("bzPSD") && notices.Text.Contains("SQLite 3.54.0"),
                        "About reads distributed third-party notices");
                    Check(((TextBox)dialog.FindName("VersionDetails")).IsReadOnly && notices.IsReadOnly, "About details are read-only and selectable");
                    var foreground = (SolidColorBrush)dialog.FindResource("PanelForeground");
                    Check(dark ? foreground.Color.R > 180 : foreground.Color.R < 80, "About follows current theme");
                    string? copied = null; dialog.CopyText = text => copied = text;
                    ((Button)dialog.FindName("CopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(copied == dialog.Information, "Copy includes version and environment without touching real clipboard");
                    dialog.CopyText = _ => throw new System.Runtime.InteropServices.ExternalException("clipboard busy");
                    ((Button)dialog.FindName("CopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(((TextBlock)dialog.FindName("CopyStatus")).Text.Contains("コピーできません"), "Clipboard failure keeps About usable");
                    ((TextBlock)dialog.FindName("CopyStatus")).Text = "";
                    var content = (FrameworkElement)dialog.Content; var visual = new DrawingVisual();
                    using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual); SaveRender(bitmap, screenshotPath + (dark ? ".about-dark.png" : ".about-light.png"));
                    ((TabControl)((TabItem)notices.Parent).Parent).SelectedIndex = 1; dialog.UpdateLayout();
                    Check(notices.IsVisible, "Library tab can be opened");
                }
                finally { dialog.Close(); }
            }
            Check(ReferenceEquals(selected, ImageList.SelectedItem) && ReferenceEquals(image, model.Image), "About preserves current image and selection");
        }
        finally { ApplyTheme(savedDark); }
    }
}
