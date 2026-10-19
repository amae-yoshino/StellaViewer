using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private Task directoryManagementAction=Task.CompletedTask;
    private async void ManageDirectoryShortcutsClicked(object sender,RoutedEventArgs e)
    {
        await RefreshChangedDirectoryShortcutsAsync();
        if(!closed&&!closingInProgress)CreateDirectoryShortcutsManagementWindow().ShowDialog();
    }
    private Window CreateDirectoryShortcutsManagementWindow()
    {
        var dialog=new Window{Owner=this,Title="フォルダリンクの編集",Width=680,Height=500,MinWidth=520,MinHeight=360,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        NameScope.SetNameScope(dialog,new NameScope());CopySettingsTheme(dialog);
        dialog.SetResourceReference(BackgroundProperty,"PanelBackground");dialog.SetResourceReference(ForegroundProperty,"PanelForeground");
        var root=new DockPanel{Margin=new Thickness(16)};dialog.Content=root;
        var description=new TextBlock{Text="「上へ」「下へ」で表示順を変更できます。表示名の変更や登録の削除で、実際のフォルダ名や内容は変更しません。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
        DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
        var footer=new StackPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        footer.Children.Add(new TextBlock{Text="表示名",Margin=new Thickness(0,0,0,5)});
        var name=new TextBox{IsEnabled=false,MinHeight=28};footer.Children.Add(name);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)};footer.Children.Add(error);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0)};footer.Children.Add(buttons);
        Button MakeButton(string text){var button=new Button{Content=text,Padding=new Thickness(10,5,10,5),Margin=new Thickness(3)};buttons.Children.Add(button);return button;}
        var up=MakeButton("上へ");var down=MakeButton("下へ");var add=MakeButton("追加…");var refresh=MakeButton("更新");var rename=MakeButton("表示名を変更");var remove=MakeButton("登録を削除");var close=MakeButton("閉じる");close.IsCancel=true;
        var list=new ListBox{ItemsSource=model.DirectoryShortcuts,ItemTemplate=(DataTemplate)Resources["DirectoryShortcutTemplate"],HorizontalContentAlignment=HorizontalAlignment.Stretch};
        list.SetResourceReference(BackgroundProperty,"PanelBackground");list.SetResourceReference(ForegroundProperty,"PanelForeground");
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);root.Children.Add(list);
        dialog.RegisterName("DirectoryManagementList",list);dialog.RegisterName("DirectoryDisplayName",name);dialog.RegisterName("RenameDirectoryButton",rename);dialog.RegisterName("RemoveDirectoryButton",remove);dialog.RegisterName("DirectoryManagementError",error);
        dialog.RegisterName("MoveDirectoryUpButton",up);dialog.RegisterName("MoveDirectoryDownButton",down);
        var busy=false;
        void UpdateMoveButtons(){up.IsEnabled=!busy&&list.SelectedIndex>0;down.IsEnabled=!busy&&list.SelectedIndex>=0&&list.SelectedIndex<list.Items.Count-1;}
        void UpdateSelection(){var selected=list.SelectedItem as DirectoryShortcutItem;name.IsEnabled=rename.IsEnabled=remove.IsEnabled=selected is not null&&!busy;name.Text=selected?.Name??"";error.Text="";UpdateMoveButtons();}
        void Busy(bool value){busy=value;list.IsEnabled=add.IsEnabled=refresh.IsEnabled=close.IsEnabled=!value;name.IsEnabled=rename.IsEnabled=remove.IsEnabled=!value&&list.SelectedItem is DirectoryShortcutItem;UpdateMoveButtons();}
        list.SelectionChanged+=(_,_)=>{if(!busy)UpdateSelection();};UpdateSelection();
        add.Click+=async(_,_)=>{Busy(true);try{await PickDirectoryShortcutAsync(dialog);}finally{Busy(false);UpdateSelection();}};
        refresh.Click+=async(_,_)=>{Busy(true);try{await RefreshChangedDirectoryShortcutsAsync();}finally{Busy(false);UpdateSelection();}};
        async Task RenameAsync(){
            if(list.SelectedItem is not DirectoryShortcutItem selected)return;
            string value;
            try{value=DirectoryShortcutStore.NormalizeName(name.Text);}catch(ArgumentException invalid){error.Text=invalid.Message;return;}
            Busy(true);
            try{var ok=await ChangeDirectoryShortcutAsync(()=>directoryShortcutStore.RenameAsync(selected.Path,value),"表示名を変更しました。実際のフォルダ名は変更していません。");if(!ok)error.Text=model.Status;else list.SelectedItem=model.DirectoryShortcuts.FirstOrDefault(e=>e.Path==selected.Path);}
            finally{Busy(false);}
        }
        async Task RemoveAsync(){
            if(list.SelectedItem is not DirectoryShortcutItem selected)return;
            Busy(true);var succeeded=false;
            try{succeeded=await ChangeDirectoryShortcutAsync(()=>directoryShortcutStore.RemoveAsync(selected.Path),"登録を削除しました。元のフォルダは変更していません。");}
            finally{Busy(false);UpdateSelection();if(!succeeded)error.Text=model.Status;}
        }
        async Task MoveAsync(int direction){
            if(list.SelectedItem is not DirectoryShortcutItem selected)return;
            Busy(true);var ok=false;
            try{ok=await ChangeDirectoryShortcutAsync(()=>directoryShortcutStore.MoveAsync(selected.Path,direction),"フォルダリンクの順序を保存しました。");
                if(!ok)await RefreshChangedDirectoryShortcutsAsync();
                list.SelectedItem=model.DirectoryShortcuts.FirstOrDefault(e=>StringComparer.OrdinalIgnoreCase.Equals(e.Path,selected.Path));
                if(list.SelectedItem is {} item)list.ScrollIntoView(item);
            }finally{Busy(false);UpdateSelection();if(!ok)error.Text=model.Status;}
        }
        up.Click+=(_,_)=>directoryManagementAction=MoveAsync(-1);down.Click+=(_,_)=>directoryManagementAction=MoveAsync(1);
        rename.Click+=(_,_)=>directoryManagementAction=RenameAsync();
        remove.Click+=(_,_)=>directoryManagementAction=RemoveAsync();
        dialog.Closing+=(_,e)=>{if(busy)e.Cancel=true;};close.Click+=(_,_)=>dialog.Close();
        dialog.Loaded+=(_,_)=>{directoryManagers++;UpdateDirectoryShortcutTimer();};
        dialog.Closed+=(_,_)=>{directoryManagers--;UpdateDirectoryShortcutTimer();};
        return dialog;
    }
}
