# Third-party notices

StellaViewer's own components are licensed under MIT; see LICENSE.txt (copyright amae yoshino).
That license does not replace or limit the licenses of the third-party components below.
The files in licenses must accompany binary distribution. Their presence alone is not a determination that all source/relinking obligations have been fulfilled.
For the matching external source package and rebuild instructions, see licenses/Magick.Native.SourceGuide.md.

.NET Windows x64 apphost 10.0.13 (MIT): https://github.com/dotnet/dotnet,
source commit 4099f83977f4d29604a9aeb971fb0a7bc1d5f699.
The apphost is the native executable launcher, even in a framework-dependent distribution.
The unmodified LICENSE.TXT and THIRD-PARTY-NOTICES.TXT from the official
Microsoft.NETCore.App.Host.win-x64 10.0.13 NuGet package are included as
licenses/DotNET.AppHost.LICENSE.txt and licenses/DotNET.AppHost.ThirdPartyNotices.txt.
The .NET Desktop Runtime itself is not bundled.

Microsoft C++ runtime code is statically linked into the release native helpers.
See licenses/Microsoft.VC.Runtime.md for the
applicable Microsoft terms and maintenance considerations. Compiler/SDK files,
MFC development files and debug runtimes are not part of the viewer package.

CLIP stored-preview helper: SQLite 3.54.0 (public domain), https://sqlite.org/copyright.html.
The fixed source archive and verified checksums are recorded in licenses/SQLite.PublicDomain.txt and src/StellaViewer.ClipPreview/CMakeLists.txt.
SQLite and the MSVC runtime are statically linked into StellaViewer.ClipPreview.exe. Python is not required.
The CLIP container/schema investigation and preview query are based on wamsoft/clipparse commit 322a273c0e059d1dee9132af78289efa12a59522 (MIT); its license is in licenses/clipparse.LICENSE.txt.
The viewer uses a dedicated stored-PNG extractor, without the clipparse layer parser/compositor.

bzPSD: https://github.com/bizzehdee/bzPSD (BSD-3-Clause), pinned to commit 06b3f52dd77a42a20328dd315ab5939a2d846c64.
Unmodified upstream C# sources are vendored in third_party/bzPSD/src; individual SHA256 hashes are recorded in upstream.json.
The full upstream license is included in licenses/bzPSD.LICENSE.txt and in published output. The upstream 2013 source-header copyright and BSD terms are additionally distributed in licenses/bzPSD.SourceNotice.txt and retained in the source files.
StellaViewer uses its RGB planar-to-RGBA conversion with a separate bounded, read-only merged-image parser; it does not load or render individual layers.

Magick.NET-Q8-x64 15.0.0 and its locked Magick.NET.Core dependency.

- Magick.NET: https://github.com/dlemstra/Magick.NET — Apache License 2.0; attribution in licenses/Magick.NET.Copyright.txt. The full Apache License 2.0 text is included in the native notice below.
- ImageMagick 7.1.2-33 and native delegates: complete upstream notice in licenses/Magick.NET.Native.Notice.txt (including ImageMagick license and delegate licenses).

Pinned source provenance and rebuild instructions are documented in
licenses/Magick.Native.SourceManifest.json and licenses/Magick.Native.SourceGuide.md.
Source snapshots and build/relink instructions are prepared separately from the
application. The
matching source collection must actually be made available with the binary release;
these notice documents alone are not a substitute for providing those sources.

The separate native-source collection also contains Configure's NASM 2.16.01
build tool (BSD-2-Clause). Its official license is preserved in
licenses/NASM.BuildTool.LICENSE.txt. NASM is a build-time tool and is not a
runtime dependency of the viewer application.

The unmodified Copyright.txt from Magick.NET.Core 15.0.0 and Notice.txt from Magick.NET-Q8-x64 15.0.0 are copied into licenses and included in build/publish output. SharpCompress is additionally distributed as described below.

SharpCompress 0.50.4: https://github.com/adamhathcock/sharpcompress (MIT).
The upstream license, including bundled-code notices, is included in licenses/SharpCompress.LICENSE.txt.
Used for read-only archive browsing and thumbnails.

PDF rendering dependencies (versions fixed by packages.lock.json):

- PDFtoImage 5.4.0: https://github.com/sungaila/PDFtoImage (MIT). The upstream license at package commit 5948628cb2cf7d070c7fd28b1b4496db39e3afcd is included in licenses/PDFtoImage.LICENSE.txt.
- SkiaSharp 4.150.1 / SkiaSharp.NativeAssets.Win32 4.150.1: https://github.com/mono/SkiaSharp. The unmodified package license and native third-party notices are included in licenses/SkiaSharp.LICENSE.txt and licenses/SkiaSharp.Native.Notices.txt.
- bblanchon.PDFium.Win32 152.0.7961: https://github.com/bblanchon/pdfium-binaries. The upstream repository license at package commit c6529b58791d142002f819beb46e370e668797d7 is included in licenses/PDFium.Binaries.LICENSE.txt. The NuGet metadata declares Apache-2.0; the matching native binary distribution supplies MIT distribution terms and PDFium/third-party terms, all preserved under licenses/PDFium. Apache-2.0 text is also included in its llvm-libc notice.

The native PDFium license set was obtained from the chromium/7961 win-x64 release. Its pdfium.dll SHA256 matches the NuGet native DLL exactly: D3D9F4B7C9DABE3363F30779C5C3C715C47332749FA590E4B4A2B8B6780CB1C4. No binary from the downloaded license archive replaces the NuGet dependency. Windows x64 publish output includes pdfium.dll and libSkiaSharp.dll; other-platform runtime packages in the lock files are not part of this distribution.
