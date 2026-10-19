namespace StellaViewer.Infrastructure;

public sealed record ViewerAppearance(int BackgroundMode = 0, string BackgroundColor = "#202020",
    bool GridVisible = false, int GridSpacing = 64, string GridColor = "#FFFFFF", int GridOpacity = 50,
    string CheckerColor1 = "#F0F0F0", string CheckerColor2 = "#CDCDCD", int CheckerSize = 12)
{
    public static bool IsColor(string? value) => value is { Length: 7 } && value[0] == '#'
        && value.AsSpan(1).ToString().All(Uri.IsHexDigit);

    public ViewerAppearance Normalize() => this with {
        BackgroundMode = BackgroundMode is >= 0 and <= 2 ? BackgroundMode : 0,
        BackgroundColor = IsColor(BackgroundColor) ? BackgroundColor : "#202020",
        GridSpacing = Math.Clamp(GridSpacing, 1, 100000),
        GridColor = IsColor(GridColor) ? GridColor : "#FFFFFF",
        GridOpacity = Math.Clamp(GridOpacity, 0, 100),
        CheckerColor1 = IsColor(CheckerColor1) ? CheckerColor1 : "#F0F0F0",
        CheckerColor2 = IsColor(CheckerColor2) ? CheckerColor2 : "#CDCDCD",
        CheckerSize = Math.Clamp(CheckerSize, 1, 256)
    };

    // Apply only edited fields to the latest shared settings.
    public ViewerAppearance MergeChanges(ViewerAppearance before, ViewerAppearance next) => this with {
        BackgroundMode = next.BackgroundMode != before.BackgroundMode ? next.BackgroundMode : BackgroundMode,
        BackgroundColor = next.BackgroundColor != before.BackgroundColor ? next.BackgroundColor : BackgroundColor,
        GridVisible = next.GridVisible != before.GridVisible ? next.GridVisible : GridVisible,
        GridSpacing = next.GridSpacing != before.GridSpacing ? next.GridSpacing : GridSpacing,
        GridColor = next.GridColor != before.GridColor ? next.GridColor : GridColor,
        GridOpacity = next.GridOpacity != before.GridOpacity ? next.GridOpacity : GridOpacity,
        CheckerColor1 = next.CheckerColor1 != before.CheckerColor1 ? next.CheckerColor1 : CheckerColor1,
        CheckerColor2 = next.CheckerColor2 != before.CheckerColor2 ? next.CheckerColor2 : CheckerColor2,
        CheckerSize = next.CheckerSize != before.CheckerSize ? next.CheckerSize : CheckerSize
    };
}
