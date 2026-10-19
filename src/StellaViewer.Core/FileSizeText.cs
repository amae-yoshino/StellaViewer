using System.Globalization;

namespace StellaViewer.Core;

public static class FileSizeText
{
    // Decimal units: KB=1,000 bytes, MB=1,000,000 bytes.
    public static string Format(long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1000; unit++; }
        var digits = value < 10 ? 2 : value < 100 ? 1 : 0;
        if (Math.Round(value, digits) >= 1000 && unit < units.Length - 1)
        { value /= 1000; unit++; digits = 2; }
        return value.ToString(unit == 0 ? "0" : digits == 2 ? "0.##" : digits == 1 ? "0.#" : "0", CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
