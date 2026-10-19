using StellaViewer.Infrastructure;
using System.Windows;
using System.Windows.Controls;

namespace StellaViewer.App;

public partial class MainWindow
{
    private Task favoriteManagementAction=Task.CompletedTask;
    private async void ManageFavoritesClicked(object sender,RoutedEventArgs e)
    {
        await RefreshChangedFavoritesAsync();
        if(closed||closingInProgress)return;
        CreateFavoritesManagementWindow().ShowDialog();
    }
    private Window CreateFavoritesManagementWindow()
    {
        var dialog=new Window{Owner=this,Title="お気に入りの管理",Width=720,Height=420,MinWidth=480,MinHeight=280,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        NameScope.SetNameScope(dialog,new NameScope());
        dialog.SetResourceReference(BackgroundProperty,"PanelBackground");dialog.SetResourceReference(ForegroundProperty,"PanelForeground");
        foreach(var key in new[]{"PanelBackground","PanelForeground","DividerBrush","HoverBrush","SelectionBrush","ScrollThumbBrush","ScrollThumbHoverBrush"})dialog.Resources[key]=Resources[key];
        var root=new DockPanel{Margin=new Thickness(12)};
        var description=new TextBlock{Text="「上へ」「下へ」で表示順を変更できます。見つからない登録先も保持し、登録を削除しても元のファイルやフォルダは変更しません。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};
        var refresh=new Button{Content="更新",Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)};
        var remove=new Button{Content="登録を削除",IsEnabled=false,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)};
        var close=new Button{Content="閉じる",IsCancel=true,Padding=new Thickness(12,5,12,5)};
        var up=new Button{Content="上へ",Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)};
        var down=new Button{Content="下へ",Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,8,0)};
        buttons.Children.Add(up);buttons.Children.Add(down);buttons.Children.Add(refresh);buttons.Children.Add(remove);buttons.Children.Add(close);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var list=new ListBox{ItemsSource=favoriteItems,ItemTemplate=(DataTemplate)Resources["FavoriteManagementTemplate"],HorizontalContentAlignment=HorizontalAlignment.Stretch};
        list.SetResourceReference(BackgroundProperty,"PanelBackground");list.SetResourceReference(ForegroundProperty,"PanelForeground");
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        root.Children.Add(list);dialog.Content=root;
        dialog.RegisterName("FavoriteManagementList",list);dialog.RegisterName("RemoveFavoriteButton",remove);
        dialog.RegisterName("MoveFavoriteUpButton",up);dialog.RegisterName("MoveFavoriteDownButton",down);
        var busy=false;
        void UpdateButtons(){remove.IsEnabled=!busy&&list.SelectedItem is FavoriteItem;up.IsEnabled=remove.IsEnabled&&list.SelectedIndex>0;down.IsEnabled=remove.IsEnabled&&list.SelectedIndex<list.Items.Count-1;}
        void Busy(bool value){busy=value;list.IsEnabled=refresh.IsEnabled=close.IsEnabled=!value;UpdateButtons();}
        list.SelectionChanged+=(_,_)=>UpdateButtons();UpdateButtons();
        async Task RefreshAsync(){Busy(true);try{var path=(list.SelectedItem as FavoriteItem)?.Path;await RefreshChangedFavoritesAsync();list.ItemsSource=favoriteItems;list.SelectedItem=favoriteItems.FirstOrDefault(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,path));}finally{Busy(false);}}
        refresh.Click+=(_,_)=>favoriteManagementAction=RefreshAsync();
        async Task RemoveAsync(){
            if(list.SelectedItem is not FavoriteItem selected)return;
            Busy(true);try{await SetFavoriteAsync(selected.Path,selected.Kind,false);list.ItemsSource=favoriteItems;}finally{Busy(false);}
        }
        async Task MoveAsync(int direction){
            if(list.SelectedItem is not FavoriteItem selected)return;
            Busy(true);
            async Task Save(Task previous){try{await previous;}catch(Exception){}await favoritesStore.MoveAsync(selected.Path,direction);}
            var saving=Save(favoriteSaveTail);favoriteSaveTail=saving;
            try{await saving;model.Status="お気に入りの順序を保存しました。";}
            catch(Exception error){model.Status="お気に入りの順序を保存できませんでした："+error.Message;}
            finally{await RefreshChangedFavoritesAsync();list.ItemsSource=favoriteItems;list.SelectedItem=favoriteItems.FirstOrDefault(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,selected.Path));if(list.SelectedItem is {} item)list.ScrollIntoView(item);Busy(false);}
        }
        remove.Click+=(_,_)=>favoriteManagementAction=RemoveAsync();
        up.Click+=(_,_)=>favoriteManagementAction=MoveAsync(-1);down.Click+=(_,_)=>favoriteManagementAction=MoveAsync(1);
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        close.Click+=(_,_)=>dialog.Close();return dialog;
    }
}
