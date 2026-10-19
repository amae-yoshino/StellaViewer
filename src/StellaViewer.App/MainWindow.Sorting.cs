using System.Globalization;
using System.Windows.Controls;

namespace StellaViewer.App;

public partial class MainWindow
{
    // Stored sort IDs stay stable when the visible options are reordered.
    private int SelectedImageSort
    {
        get => ReadSortId(SortBox);
        set => SortBox.SelectedValue = Math.Clamp(value, 0, 5).ToString(CultureInfo.InvariantCulture);
    }
    private int SelectedLocationSort
    {
        get => ReadSortId(LocationSortBox);
        set => LocationSortBox.SelectedValue = Math.Clamp(value, 0, 5).ToString(CultureInfo.InvariantCulture);
    }
    private static int ReadSortId(ComboBox selector) => selector.SelectedValue is string value
        && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id is >= 0 and <= 5 ? id : 0;
}
