using System.Windows;
using System.Windows.Controls;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class SettingsWindow : Window
{
    private ViewerPreferences baseline;
    private readonly Func<ViewerPreferences, Task>? apply;
    private bool applying;
    internal ViewerPreferences Values => baseline with { SlideshowIntervalSeconds = (int)SlideshowInterval.Value, ListFontSize = (int)ListFontSize.Value, ListScroll = new((int)ListScrollAmount.Value), UiSize = new((int)UiScale.Value, (int)UiFontSize.Value), Dark = Dark.IsChecked == true, ControlWheel = WheelZoom.IsChecked == true,
        EnlargeSmallImages = EnlargeSmall.IsChecked == true,
        ListMode = Mode.SelectedIndex, HideHiddenFolders = Hidden.IsChecked == true, ViewerToolbarPlacement = ToolbarPlacement.SelectedIndex,FolderOpeningMode=FolderOpening.SelectedIndex,
        ImageThumbnailSize=ImageThumbnailSize.SelectedIndex,LocationThumbnailSize=LocationThumbnailSize.SelectedIndex,DeleteEnabled=DeleteEnabled.IsChecked==true,MinimizeToTray=TrayMinimize.IsChecked==true,AllowMultipleInstances=MultipleInstances.IsChecked==true,
        RecordRecentLocations = RecordRecent.IsChecked == true, RecordViewingState = RecordView.IsChecked == true, RestoreLastView = RestoreView.IsChecked == true, LocationListMode = LocationMode.SelectedIndex,
        Appearance = ReadAppearance(), SpreadMode = PageMode.SelectedIndex == 1, RightBinding = BindingDirection.SelectedIndex == 0,
        Loupe = ReadLoupe(), Keyboard = editedKeyboard.Normalize(), Mouse = ReadMouseSettings(), SoftwareRendering = RenderingMode.SelectedIndex == 1, AllowNetwork = NetworkAllowed.IsChecked == true, Cache = ReadCache() };
    public SettingsWindow(ViewerPreferences values, Func<ViewerPreferences, Task>? apply = null)
    {
        baseline = values; this.apply = apply;
        InitializeComponent();
        Closing += (_, e) => { if (applying) e.Cancel = true; };
        LoadValues(values);
    }
    internal void LoadValues(ViewerPreferences values)
    {
        baseline = values;
        var ui = (values.UiSize ?? new()).Normalize(); UiScale.Value = ui.ScalePercent; UiFontSize.Value = ui.FontSize;
        ListScrollAmount.Value = (values.ListScroll ?? new()).Normalize().AmountPercent;
        ListFontSize.Value = Math.Clamp(values.ListFontSize, 10, 24);
        SlideshowInterval.Value = Math.Clamp(values.SlideshowIntervalSeconds, 1, 60);
        RecordRecent.IsChecked = values.RecordRecentLocations; RecordView.IsChecked = values.RecordViewingState;
        Dark.IsChecked = values.Dark; WheelZoom.IsChecked = values.ControlWheel;
        EnlargeSmall.IsChecked = values.EnlargeSmallImages;
        DeleteEnabled.IsChecked=values.DeleteEnabled;
        TrayMinimize.IsChecked=values.MinimizeToTray;
        MultipleInstances.IsChecked=values.AllowMultipleInstances;
        Mode.SelectedIndex = values.ListMode; Hidden.IsChecked = values.HideHiddenFolders;
        LocationMode.SelectedIndex = values.LocationListMode is >= 0 and <= 2 ? values.LocationListMode : 1;
        ToolbarPlacement.SelectedIndex = values.ViewerToolbarPlacement is >= 0 and <= 2 ? values.ViewerToolbarPlacement : 0;
        RestoreView.IsChecked = values.RestoreLastView;
        FolderOpening.SelectedIndex=values.FolderOpeningMode==1?1:0;
        ImageThumbnailSize.SelectedIndex=ThumbnailLayout.Normalize(values.ImageThumbnailSize);LocationThumbnailSize.SelectedIndex=ThumbnailLayout.Normalize(values.LocationThumbnailSize);
        SetAppearance(values.Appearance);
        RenderingMode.SelectedIndex = values.SoftwareRendering ? 1 : 0;
        NetworkAllowed.IsChecked = values.AllowNetwork;
        SetCache(values.Cache);
        PageMode.SelectedIndex = values.SpreadMode ? 1 : 0;
        BindingDirection.SelectedIndex = values.RightBinding ? 0 : 1;
        var loupe = (values.Loupe ?? new()).Normalize();
        LoupeMagnification.Text = loupe.Magnification.ToString(System.Globalization.CultureInfo.CurrentCulture); LoupeSize.Text = loupe.Size.ToString();
        editedKeyboard = KeyboardCommands.Normalize(values.Keyboard); RefreshShortcutRows();
        SetMouseSettings(values.Mouse);
    }
    private LoupeSettings ReadLoupe()
    {
        if (!double.TryParse(LoupeMagnification.Text, out var factor) || !double.IsFinite(factor) || factor is < 1 or > 16)
            throw new InvalidOperationException("ルーペの拡大率は1～16倍で入力してください。");
        if (!int.TryParse(LoupeSize.Text, out var size) || size is < 80 or > 800)
            throw new InvalidOperationException("ルーペの一辺は80～800 DIPの整数で入力してください。");
        return new(factor, size);
    }
    private void SetAppearance(ViewerAppearance? value)
    {
        var settings = (value ?? new()).Normalize();
        BackgroundMode.SelectedIndex = settings.BackgroundMode; BackgroundColor.Text = settings.BackgroundColor;
        CheckerColor1.Text = settings.CheckerColor1; CheckerColor2.Text = settings.CheckerColor2; CheckerSize.Text = settings.CheckerSize.ToString();
        GridVisible.IsChecked = settings.GridVisible; GridSpacing.Text = settings.GridSpacing.ToString();
        GridColor.Text = settings.GridColor; GridOpacity.Value = settings.GridOpacity;
    }
    private ViewerAppearance ReadAppearance()
    {
        if (!ViewerAppearance.IsColor(BackgroundColor.Text.Trim()) || !ViewerAppearance.IsColor(GridColor.Text.Trim())
            || !ViewerAppearance.IsColor(CheckerColor1.Text.Trim()) || !ViewerAppearance.IsColor(CheckerColor2.Text.Trim()))
            throw new InvalidOperationException("色は #RRGGBB の形式で入力してください（例：#202020）。");
        if (!int.TryParse(GridSpacing.Text, out var spacing) || spacing is < 1 or > 100000)
            throw new InvalidOperationException("グリッド間隔は1～100000 pxの整数で入力してください。");
        if (!int.TryParse(CheckerSize.Text, out var checkerSize) || checkerSize is < 1 or > 256)
            throw new InvalidOperationException("市松模様の一辺は1～256 DIPの整数で入力してください。");
        return new(BackgroundMode.SelectedIndex, BackgroundColor.Text.Trim().ToUpperInvariant(),
            GridVisible.IsChecked == true, spacing, GridColor.Text.Trim().ToUpperInvariant(), (int)Math.Round(GridOpacity.Value),
            CheckerColor1.Text.Trim().ToUpperInvariant(), CheckerColor2.Text.Trim().ToUpperInvariant(), checkerSize);
    }
    internal void ShowShortcuts() => SettingsTabs.SelectedIndex = 1;
    internal void ResetValue(string key)
    {
        var defaults = new ViewerPreferences();
        switch (key)
        {
            case "ui-size": UiScale.Value = 100; UiFontSize.Value = 14; break;
            case "list-scroll": ListScrollAmount.Value = 100; break;
            case "list-font": ListFontSize.Value = 14; break;
            case "slideshow": SlideshowInterval.Value = 3; break;
            case "theme": Dark.IsChecked = defaults.Dark; break;
            case "rendering": RenderingMode.SelectedIndex = 0; break;
            case "network": NetworkAllowed.IsChecked = false; break;
            case "cache": SetCache(new()); break;
            case "wheel": WheelZoom.IsChecked = defaults.ControlWheel; break;
            case "enlarge-small": EnlargeSmall.IsChecked = false; break;
            case "hidden": Hidden.IsChecked = defaults.HideHiddenFolders; break;
            case "delete": DeleteEnabled.IsChecked=false;break;
            case "tray": TrayMinimize.IsChecked=false;break;
            case "instances": MultipleInstances.IsChecked=true;break;
            case "list": Mode.SelectedIndex = defaults.ListMode; break;
            case "locations": LocationMode.SelectedIndex = defaults.LocationListMode; break;
            case "toolbar": ToolbarPlacement.SelectedIndex = defaults.ViewerToolbarPlacement; break;
            case "restore": RestoreView.IsChecked = defaults.RestoreLastView; break;
            case "folder-opening": FolderOpening.SelectedIndex=0;break;
            case "image-thumbnail": ImageThumbnailSize.SelectedIndex=1;break;
            case "location-thumbnail": LocationThumbnailSize.SelectedIndex=1;break;
            case "background":
                BackgroundMode.SelectedIndex = defaults.Appearance.BackgroundMode;
                BackgroundColor.Text = defaults.Appearance.BackgroundColor; break;
            case "checker":
                CheckerColor1.Text = defaults.Appearance.CheckerColor1; CheckerColor2.Text = defaults.Appearance.CheckerColor2;
                CheckerSize.Text = defaults.Appearance.CheckerSize.ToString(); break;
            case "grid":
                GridVisible.IsChecked = defaults.Appearance.GridVisible; GridSpacing.Text = defaults.Appearance.GridSpacing.ToString();
                GridColor.Text = defaults.Appearance.GridColor; GridOpacity.Value = defaults.Appearance.GridOpacity; break;
            case "spread": PageMode.SelectedIndex = 0; BindingDirection.SelectedIndex = 0; break;
            case "loupe": LoupeMagnification.Text = "2"; LoupeSize.Text = "240"; break;
            case "keys": editedKeyboard = new(); RefreshShortcutRows(); break;
        }
    }
    private void ResetClicked(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string key }) ResetValue(key); }
    internal async Task<bool> ApplyChangesAsync()
    {
        if (applying) return false;
        applying = true;
        SaveButton.IsEnabled = ApplyButton.IsEnabled = CancelButton.IsEnabled = false; ErrorText.Text = "";
        try {
            var next = Values;
            if (apply is not null) await apply(next);
            baseline = next; return true;
        }
        catch (Exception error) { ErrorText.Text = "設定を反映できませんでした：" + error.Message; return false; }
        finally { applying = false; SaveButton.IsEnabled = ApplyButton.IsEnabled = CancelButton.IsEnabled = true; }
    }
    private async void SaveClicked(object sender, RoutedEventArgs e) { if (await ApplyChangesAsync()) DialogResult = true; }
    private async void ApplyClicked(object sender, RoutedEventArgs e) => await ApplyChangesAsync();
}
