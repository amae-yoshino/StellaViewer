using System.Globalization;
using System.Windows.Data;

namespace StellaViewer.App;

public sealed class FolderExpansionIconConverter : IValueConverter
{
    public static FolderExpansionIconConverter Instance { get; } = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? ViewerIconKind.FolderOpen : ViewerIconKind.Folder;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
