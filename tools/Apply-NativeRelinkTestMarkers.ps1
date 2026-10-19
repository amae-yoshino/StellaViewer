# Copyright (c) 2026 amae yoshino. SPDX-License-Identifier: MIT
# See licenses/RelinkTools.LICENSE.txt. Applies only to a dedicated external source tree.
#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory, (Get-Location).Path)
$heifPath = Join-Path $buildRoot 'Dependencies/Dependencies/heif/libheif/api/libheif/heif_library.cc'
$nativePath = Join-Path $buildRoot 'Magick.Native/src/Magick.Native/Magick.c'
$heif = [IO.File]::ReadAllText($heifPath)
$native = [IO.File]::ReadAllText($nativePath)
if (-not $heif.Contains('return (LIBHEIF_VERSION);') -or -not $native.Contains('return GetMagickVersion(NULL);')) {
    throw 'Expected pristine source lines were not found. Refusing to apply markers.'
}
$heif = $heif.Replace('return (LIBHEIF_VERSION);', 'return (LIBHEIF_VERSION "-stellaviewer-relink-test");')
$native = $native.Replace('return GetMagickVersion(NULL);', 'return "ImageMagick 7.1.2-33 Q8 x64 / stellaviewer-relink-test";')
$native += @'

// Test-only export to verify the modified LGPL component was actually linked.
extern "C" const char *heif_get_version(void);
extern "C" MAGICK_NATIVE_EXPORT const char *StellaViewer_RelinkProbe(void)
{
  return heif_get_version();
}
'@
[IO.File]::WriteAllText($heifPath, $heif, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($nativePath, $native, [Text.UTF8Encoding]::new($false))
Write-Output 'Test markers applied. Original source headers and licenses retained.'
