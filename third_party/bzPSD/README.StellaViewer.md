# bzPSD source dependency

Official source: https://github.com/bizzehdee/bzPSD
Pinned commit: 06b3f52dd77a42a20328dd315ab5939a2d846c64
License: BSD-3-Clause (LICENSE; also distributed in licenses/bzPSD.LICENSE.txt).
The source-header copyright and BSD terms are also distributed in licenses/bzPSD.SourceNotice.txt.

All upstream C# files are copied without changes. upstream.json records their SHA256 values.
StellaViewer.BzPsd.csproj is a local build wrapper targeting .NET 10. Nullable annotations are
retained; nullable warnings and SDK code analyzers are not enabled for the upstream sources.

The viewer does not call PsdFile.Load, CompositeLayers or Save. Its bounded, read-only merged
image adapter validates and reads the PSD sections and channel rows, then calls bzPSD's
ImageDecoder.DecodeImage with the selected merged RGB/alpha channels. Color conversion and
orientation are handled by the existing display pipeline. No Photoshop layer is rendered.
