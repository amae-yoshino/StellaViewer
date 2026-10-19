using System.Globalization;
using System.Windows.Data;
using StellaViewer.Infrastructure;
namespace StellaViewer.App;
public sealed class FavoriteIconConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is FavoriteItem item?item.Kind switch {
        FavoriteKind.Folder=>ViewerIconKind.Folder,FavoriteKind.Archive=>ViewerIconKind.Archive,FavoriteKind.Pdf=>ViewerIconKind.Pdf,_=>ViewerIconKind.Image
    }:ViewerIconKind.Star;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
