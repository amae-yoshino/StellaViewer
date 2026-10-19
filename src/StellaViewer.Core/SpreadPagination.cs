namespace StellaViewer.Core;

public readonly record struct SpreadPages(int First, int? Second);

public static class SpreadPagination
{
    public static int GroupCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count / 2 + count % 2;
    }

    public static SpreadPages? GetPages(int count, int anchor, int group)
    {
        var groups = GroupCount(count);
        if (groups == 0) return null;
        if (anchor < 0 || anchor >= count) throw new ArgumentOutOfRangeException(nameof(anchor));
        var offset = Mod(group, groups) * 2L;
        var first = Mod(anchor + offset, count);
        return new(first, offset + 1 < count ? Mod(anchor + offset + 1, count) : null);
    }

    public static int MoveGroup(int count, int group, int delta)
    {
        var groups = GroupCount(count);
        return groups == 0 ? 0 : Mod((long)group + delta, groups);
    }

    private static int Mod(long value, int modulus) => (int)((value % modulus + modulus) % modulus);
}
