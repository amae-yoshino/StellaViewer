using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace StellaViewer.App;

public partial class MainWindow
{
    private async void TreePointerDown(object sender,MouseButtonEventArgs e)
    {
        for(var source=e.OriginalSource as DependencyObject;source is not null&&source!=Folders;
            source=source is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source)){
            if(source is ToggleButton)return;
            if(source is not TreeViewItem node)continue;
            if(node.IsSelected&&node.Tag is string path)await(navigation=OpenAsync(path));
            return;
        }
    }
    private GridLength visibleTreeWidth=new(1,GridUnitType.Star),visibleLocationsWidth=new(1.2,GridUnitType.Star);
    private void TreeVisibilityClicked(object sender,RoutedEventArgs e)=>ApplyTreeVisibility(TreeToggle.IsChecked==true);
    private void ApplyTreeVisibility(bool visible)
    {
        var column=FolderColumns.ColumnDefinitions[0];var splitter=FolderColumns.ColumnDefinitions[1];var locations=FolderColumns.ColumnDefinitions[2];
        if(!visible&&Folders.Visibility==Visibility.Visible){
            visibleTreeWidth=column.Width;visibleLocationsWidth=locations.Width;
            var total=column.ActualWidth+locations.ActualWidth;
            hiddenFolderTreeFraction=total>0?column.ActualWidth/total:(preFullscreenLayout??startupState.Layout).TreeFraction;
            column.MinWidth=0;column.Width=new GridLength(0);splitter.Width=new GridLength(0);locations.Width=new GridLength(1,GridUnitType.Star);
        }else if(visible&&Folders.Visibility!=Visibility.Visible){
            column.MinWidth=70;column.Width=visibleTreeWidth;splitter.Width=new GridLength(7);locations.Width=visibleLocationsWidth;
        }
        TreeToggle.IsChecked=visible;Folders.Visibility=FolderColumnSplitter.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
    }
}
