using ImageMagick;
using ImageMagick.Configuration;

namespace StellaViewer.Infrastructure;

public static class ImageDecoderSecurity
{
    // Call before the first native ImageMagick operation in the viewer process.
    public static void Initialize()
    {
        var files = ConfigurationFiles.Default;
        files.Policy.Data = """
            <policymap>
              <policy domain="delegate" rights="none" pattern="*" />
              <policy domain="filter" rights="none" pattern="*" />
              <policy domain="coder" rights="none" pattern="*" />
              <policy domain="coder" rights="read" pattern="{PNG,PNG8,PNG24,PNG32,PNG48,PNG64,JPEG,GIF,WEBP,BMP,BMP2,BMP3,RGBA}" />
              <policy domain="path" rights="none" pattern="@*" />
              <policy domain="resource" name="memory" value="512MiB" />
              <policy domain="resource" name="map" value="0" />
              <policy domain="resource" name="disk" value="0" />
            </policymap>
            """;
        MagickNET.Initialize(files);
    }
}
