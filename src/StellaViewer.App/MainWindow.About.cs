using System.Windows;

namespace StellaViewer.App;

public partial class MainWindow
{
    private AboutWindow CreateAboutWindow()
    {
        var dialog = new AboutWindow { Owner = this };
        foreach (var key in new[] { "PanelBackground", "PanelForeground", "DividerBrush", "HoverBrush", "SelectionBrush", "ScrollThumbBrush", "ScrollThumbHoverBrush" })
            dialog.Resources[key] = Resources[key];
        return dialog;
    }
    private void AboutClicked(object sender, RoutedEventArgs e) => CreateAboutWindow().ShowDialog();
}
