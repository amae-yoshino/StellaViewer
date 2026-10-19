using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private ListScrollSettings listScroll = new();

    private void ListWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox list || FindScrollViewer(list) is not ScrollViewer scroll) return;
        // Match WPF's 16-DIP line and respect Windows' zero-lines/page settings.
        var lines = SystemParameters.WheelScrollLines;
        var distance = lines < 0 ? scroll.ViewportHeight : 16d * lines;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 120d * distance * listScroll.AmountPercent / 100d);
        e.Handled = true;
    }
}
