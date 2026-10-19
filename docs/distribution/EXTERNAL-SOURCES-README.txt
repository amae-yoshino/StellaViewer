StellaViewer 0.2.0-preview.13 - external library sources and rebuild materials

This optional download contains 44 pinned upstream source archives and
instructions for rebuilding/replacing the native image library. It is not
needed for ordinary image viewing and does not contain StellaViewer's
application source (provided separately under MIT) or personal browsing files.

Read Magick.Native.SourceGuide.md and Magick.Native.Rebuild.md first.
The source archives retain their own licenses and copyright notices.
Magick.NET.Native.Notice.txt contains the original native component notices.
NASM 2.16.01, included in Configure's snapshot, has BSD terms provided in
NASM.BuildTool.LICENSE.txt. The auxiliary scripts and rebuild instructions
are MIT-licensed; see RelinkTools.LICENSE.txt.

Rebuilding requires Windows x64 and the development tools listed in
Magick.Native.Rebuild.md, including Visual Studio's C++ and MFC components.
Magick.Native.SourceManifest.json maps the native library to fixed sources.
Magick.Native.SourceDownloads.json and download-receipt.json list the same
44 source archive checksums; the preparation script reads download-receipt.json.

When distributing the viewer, keep this matching source package available
alongside the application package. These sources and the rights granted by
their licenses remain separate from the viewer's MIT license.
SHA256 values detect changes, but are not independent upstream signatures.
