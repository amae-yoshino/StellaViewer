using System.Globalization;
using System.Windows.Data;
using StellaViewer.Core;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public sealed class LocationIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is LocationEntry entry
        ? entry.IsDirectory ? ViewerIconKind.Folder
            : entry.Kind == "PDF" ? ViewerIconKind.Pdf : ViewerIconKind.Archive
        : ViewerIconKind.None;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EntryInfoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FileEntry entry ? $"{(entry.IsPdfPage ? "元PDF：" : "")}{FileSizeText.Format(entry.Length)}\n{(entry.IsPdfPage ? "元PDF更新：" : "")}{(entry.Modified == default ? "不明" : entry.Modified.ToString("yyyy/MM/dd HH:mm"))}" : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class SelectedEntryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string path && (values[1] is string selected && path == selected || values[1] is IReadOnlySet<string> paths && paths.Contains(path));
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
