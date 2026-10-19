using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace StellaViewer.App;

public partial class MainWindow
{
    private int viewerToolbarPlacement;
    private double infoPanelWidth = 320;
    private void InfoResizeDragged(object sender, DragDeltaEventArgs e)
    {
        infoPanelWidth = Math.Clamp(InfoContent.ActualWidth - e.HorizontalChange, 120, InfoContent.MaxWidth);
        InfoContent.Width = infoPanelWidth;
        e.Handled = true;
    }
    private void ApplyToolbarPlacement()
    {
        DockPanel.SetDock(ViewerToolbar, viewerToolbarPlacement == 1 ? Dock.Bottom : Dock.Top);
        ViewerToolbar.BorderThickness = viewerToolbarPlacement == 1 ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1);
        ViewerToolbar.Visibility = fullscreen || viewerToolbarPlacement == 2 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ShowPageJump()
    {
        if (model.Entries.Count == 0) { model.Status = "移動できる画像がありません。"; return; }
        var dialog = new Window { Owner = this, Title = "番号を指定して移動", Width = 340, Height = 190,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)Resources["PanelBackground"], Foreground = (System.Windows.Media.Brush)Resources["PanelForeground"] };
        foreach (var key in new[] { "PanelBackground", "PanelForeground", "DividerBrush", "HoverBrush", "SelectionBrush" }) dialog.Resources[key] = Resources[key];
        var panel = new StackPanel { Margin = new Thickness(20) };
        var label = new TextBlock { Text = $"移動先の画像番号（1～{model.Entries.Count:N0}）", Margin = new Thickness(0, 0, 0, 10) };
        var entry = new TextBox { Text = (ImageList.SelectedIndex + 1).ToString() };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        var accept = new Button { Content = "移動", IsDefault = true };
        accept.Click += (_, _) => {
            if (!int.TryParse(entry.Text, out var number) || number < 1 || number > model.Entries.Count)
            { label.Text = $"1～{model.Entries.Count:N0}の整数を入力してください。"; entry.Focus(); entry.SelectAll(); return; }
            if (TryJumpToPage(number)) dialog.Close();
        };
        buttons.Children.Add(cancel); buttons.Children.Add(accept);
        panel.Children.Add(label); panel.Children.Add(entry); panel.Children.Add(buttons); dialog.Content = panel;
        dialog.Loaded += (_, _) => { entry.Focus(); entry.SelectAll(); };
        dialog.ShowDialog();
    }
    private void PaneLayoutChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainGrid is null || LeftPane is null || ImageControls is null || InfoPanel is null) return;
        // Keep both the image and the lists reachable when a pane or the window shrinks.
        var leftColumn = MainGrid.ColumnDefinitions[0];
        leftColumn.MaxWidth = Math.Max(leftColumn.MinWidth, MainGrid.ActualWidth - MainGrid.ColumnDefinitions[3].ActualWidth - 8 - 120);
        if (InfoContent is not null && MainGrid.ActualWidth > 0) {
            var factor = uiSize.ScalePercent / 100d;
            var available = fullscreen ? WindowSurface.ActualWidth - 120 : MainGrid.ActualWidth - leftColumn.ActualWidth - 8 - 120;
            InfoContent.MaxWidth = Math.Clamp(available / factor - (double)Resources["UiInfoRailWidth"], 120, 2000);
        }
        LeftPane.RowDefinitions[2].MinHeight = ImageControls.ActualHeight + 80;
        var folderRow = LeftPane.RowDefinitions[0];
        folderRow.MaxHeight = Math.Max(folderRow.MinHeight, LeftPane.ActualHeight - ImageControls.ActualHeight - 7 - 80);
    }
}
