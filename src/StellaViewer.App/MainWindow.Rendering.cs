using System.Windows.Interop;
using System.Windows.Media;

namespace StellaViewer.App;

public partial class MainWindow
{
    private bool softwareRendering;
    private void ApplyRenderingMode(bool software)
    {
        var mode = software ? RenderMode.SoftwareOnly : RenderMode.Default;
        if (RenderOptions.ProcessRenderMode != mode) RenderOptions.ProcessRenderMode = mode;
        softwareRendering = software;
    }
}
