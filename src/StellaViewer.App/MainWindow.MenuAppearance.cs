using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace StellaViewer.App;

public partial class MainWindow
{
    private void MenuSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem item) return;
        item.ApplyTemplate();
        if (item.Template.FindName("PART_Popup", item) is not Popup { Child: FrameworkElement content }) return;
        ApplyMenuPopupTheme(content);
    }
    private static void ApplyMenuPopupTheme(FrameworkElement content)
    {
        // Keep native menu behavior while replacing the fixed light popup chrome.
        var border = FindMenuPopupBorder(content);
        if (border is null) return;
        border.SetResourceReference(Border.BorderBrushProperty, "DividerBrush");
        border.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
        ThemeMenuPopupBackground(content);
    }
    private static void ThemeMenuPopupBackground(DependencyObject element)
    {
        // Item icons, checkmarks and separators belong to the items, not the popup chrome.
        if (element is ItemsPresenter or MenuItem) return;
        // SubmenuOpened can precede creation of the ScrollViewer's visual children.
        // Materialize chrome now so its default light gutter is themed on the first opening too.
        if (element is FrameworkElement visual) visual.ApplyTemplate();
        if (element is Rectangle { Name.Length: 0 } rectangle) rectangle.SetResourceReference(Shape.FillProperty, "PanelBackground");
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            ThemeMenuPopupBackground(VisualTreeHelper.GetChild(element, i));
    }
    private static Border? FindMenuPopupBorder(DependencyObject element)
    {
        if (element is Border border) return border;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindMenuPopupBorder(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }

}
