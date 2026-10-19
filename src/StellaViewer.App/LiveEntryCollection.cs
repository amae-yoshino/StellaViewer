using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace StellaViewer.App;

public sealed class LiveEntryCollection<T> : ObservableCollection<T>
{
    public LiveEntryCollection() { }
    public LiveEntryCollection(IEnumerable<T> entries) : base(entries) { }

    public void Reconcile(T[] next, Func<T, string> path)
    {
        var wanted = next.Select(path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = this.ToDictionary(path, StringComparer.OrdinalIgnoreCase);
        var edits = 0;
        for (var index = Count - 1; index >= 0; index--) {
            if (wanted.Contains(path(this[index]))) continue;
            if (++edits > 32) { ReplaceAll(next); return; }
            RemoveAt(index);
        }
        for (var index = 0; index < next.Length; index++) {
            var entry = next[index]; var key = path(entry);
            if (index < Count && string.Equals(path(this[index]), key, StringComparison.OrdinalIgnoreCase)) {
                if (!EqualityComparer<T>.Default.Equals(this[index], entry)) {
                    if (++edits > 32) { ReplaceAll(next); return; }
                    this[index] = entry;
                }
                continue;
            }
            if (++edits > 32) { ReplaceAll(next); return; }
            if (existing.TryGetValue(key, out var old)) Move(IndexOf(old), index);
            else Insert(index, entry);
            if (!EqualityComparer<T>.Default.Equals(this[index], entry)) this[index] = entry;
        }
    }

    internal void ReplaceAll(IEnumerable<T> entries)
    {
        Items.Clear(); foreach (var entry in entries) Items.Add(entry);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
