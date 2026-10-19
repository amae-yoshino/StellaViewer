using StellaViewer.Core;

namespace StellaViewer.App;

public partial class MainWindow
{
    private MouseSettings mouseSettings = new();
    private Task ExecuteMouseCommandAsync(ViewerMouseGesture gesture) => mouseSettings.For(gesture) is ViewerCommand command
        ? ExecuteViewerCommandAsync(command) : Task.CompletedTask;
}
