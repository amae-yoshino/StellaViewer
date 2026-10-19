using System.Globalization;
using System.Windows.Data;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public sealed class RecentLocationIconConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is RecentLocation item?item.Kind switch{
        "PDF"=>ViewerIconKind.Pdf,"書庫"=>ViewerIconKind.Archive,_=>ViewerIconKind.Folder
    }:ViewerIconKind.History;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
