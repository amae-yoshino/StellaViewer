using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StellaViewer.Infrastructure;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool selectingBatch;
    private int imageSelectionAnchor, locationSelectionAnchor;
    private void PublishSelection()
    {
        model.SelectedImagePaths = ImageList.SelectedItems.OfType<FileEntry>().Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        model.SelectedLocationPaths = Locations.SelectedItems.OfType<LocationEntry>().Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
    }
    private bool SelectTileBatch(ListBox list, object entry, ModifierKeys modifiers)
    {
        var index = list.Items.IndexOf(entry); if (index < 0) return false;
        var shift = modifiers.HasFlag(ModifierKeys.Shift); var control = modifiers.HasFlag(ModifierKeys.Control);
        if (!shift && !control) { if (ReferenceEquals(list, ImageList)) imageSelectionAnchor = index; else locationSelectionAnchor = index; return false; }
        selectingBatch = true;
        try {
            if (shift) {
                var anchor = Math.Clamp(ReferenceEquals(list, ImageList) ? imageSelectionAnchor : locationSelectionAnchor, 0, list.Items.Count - 1);
                if (!control) list.UnselectAll();
                for (var i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++) if (!list.SelectedItems.Contains(list.Items[i])) list.SelectedItems.Add(list.Items[i]);
            }
            else {
                if (list.SelectedItems.Contains(entry)) list.SelectedItems.Remove(entry); else list.SelectedItems.Add(entry);
                if (ReferenceEquals(list, ImageList)) imageSelectionAnchor = index; else locationSelectionAnchor = index;
            }
        }
        finally { selectingBatch = false; PublishSelection(); }
        return true;
    }
    private void RestoreBatchSelection(ListBox list, IEnumerable<string> paths)
    {
        var chosen = paths.ToHashSet(StringComparer.Ordinal);
        selectingBatch = true;
        try {
            var matches = list.Items.Cast<object>().Where(item => EntryPath(item) is string path && chosen.Contains(path)).ToArray();
            if (matches.Length == 0) return;
            list.UnselectAll(); foreach (var item in matches) list.SelectedItems.Add(item);
        }
        finally { selectingBatch = false; PublishSelection(); }
    }
    private void TileSelectionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.A || Keyboard.Modifiers != ModifierKeys.Control) return;
        var list = ReferenceEquals(sender, TileList) ? ImageList : Locations;
        selectingBatch = true; try { list.SelectAll(); } finally { selectingBatch = false; PublishSelection(); }
        e.Handled = true;
    }
    private object[] OperationSelection(string? path)
    {
        if (path is null) return [];
        var images = ImageList.SelectedItems.OfType<FileEntry>().ToArray();
        if (images.Any(item => item.Path == path)) return images.Cast<object>().ToArray();
        var locations = Locations.SelectedItems.OfType<LocationEntry>().ToArray();
        if (locations.Any(item => item.Path == path)) return locations.Cast<object>().ToArray();
        return [model.Locations.FirstOrDefault(item => item.Path == path) as object ?? new FileEntry(path, System.IO.Path.GetFileName(path), 0, default)];
    }
    private object[] OperationSelection(object sender, string path) => sender is MenuItem { Parent: ContextMenu { Tag: object[] selection } } ? selection : OperationSelection(path);
}
