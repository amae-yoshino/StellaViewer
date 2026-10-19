# Copyright (c) 2026 amae yoshino. SPDX-License-Identifier: MIT
# See licenses/RelinkTools.LICENSE.txt. Loads the explicitly supplied test DLL.
#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$NativeLibraryPath)
$ErrorActionPreference = 'Stop'
$libraryPath = (Resolve-Path -LiteralPath $NativeLibraryPath).Path
if (-not ('StellaViewerRelinkProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StellaViewerRelinkProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Probe();
    public static string Read(string path)
    {
        var handle = NativeLibrary.Load(path);
        try
        {
            var address = NativeLibrary.GetExport(handle, "StellaViewer_RelinkProbe");
            return Marshal.PtrToStringAnsi(Marshal.GetDelegateForFunctionPointer<Probe>(address)());
        }
        finally { NativeLibrary.Free(handle); }
    }
}
'@
}
$version = [StellaViewerRelinkProbe]::Read($libraryPath)
if ($version -ne '1.23.6-stellaviewer-relink-test') { throw ('Unexpected libheif probe: ' + $version) }
[pscustomobject]@{ result = 'PASS'; modifiedLgplVersion = $version; sha256 = (Get-FileHash -LiteralPath $libraryPath -Algorithm SHA256).Hash }
