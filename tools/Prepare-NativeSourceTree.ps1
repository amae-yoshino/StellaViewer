# Copyright (c) 2026 amae yoshino. SPDX-License-Identifier: MIT
# See licenses/RelinkTools.LICENSE.txt. No upstream checkout/reset scripts are run.
#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$SourceDirectory,
    [string]$BuildDirectory,
    [string]$ManifestPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $projectRoot 'artifacts/third-party-sources/0.2.0-preview.4' }
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $projectRoot 'artifacts/license-relink-sources' }
if (-not $ManifestPath) { $ManifestPath = Join-Path $projectRoot 'licenses/Magick.Native.SourceManifest.json' }
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory, (Get-Location).Path)
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory, (Get-Location).Path)
if (Test-Path -LiteralPath $buildRoot) { throw 'Build tree already exists; refusing to overwrite.' }
$receipt = Get-Content -LiteralPath (Join-Path $sourceRoot 'download-receipt.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json

function Expand-ReviewedSource($repository, $destination) {
    $record = @($receipt.archives | Where-Object repository -eq $repository)
    if ($record.Count -ne 1) { throw ('Expected one source record: ' + $repository) }
    if ($record[0].file -notmatch '^(ImageMagick|dlemstra)-[A-Za-z0-9.-]+-[a-f0-9]{40}\.zip$') {
        throw 'Unexpected source archive name.'
    }
    $path = Join-Path $sourceRoot $record[0].file
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $record[0].sha256) {
        throw ('Source hash mismatch: ' + $repository)
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $rootName = $archive.Entries[0].FullName.Split('/')[0] + '/'
        $destination = [IO.Path]::GetFullPath($destination)
        [IO.Directory]::CreateDirectory($destination) | Out-Null
        $prefix = $destination + [IO.Path]::DirectorySeparatorChar
        foreach ($entry in $archive.Entries) {
            if (-not $entry.FullName.StartsWith($rootName, [StringComparison]::Ordinal)) { throw 'Unexpected archive root.' }
            $relative = $entry.FullName.Substring($rootName.Length)
            if ($relative.Length -eq 0) { continue }
            if ($relative.Contains(':') -or $relative.Contains('\')) { throw 'Unexpected archive path.' }
            $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
            if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive escapes destination.' }
            if ($entry.FullName.EndsWith('/')) { [IO.Directory]::CreateDirectory($target) | Out-Null; continue }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
        }
    } finally { $archive.Dispose() }
    Write-Output ('Prepared ' + $repository)
}

Expand-ReviewedSource 'dlemstra/Magick.Native' (Join-Path $buildRoot 'Magick.Native')
Expand-ReviewedSource 'ImageMagick/ImageMagick' (Join-Path $buildRoot 'Magick.Native/src/ImageMagick/ImageMagick')
Expand-ReviewedSource 'ImageMagick/Dependencies' (Join-Path $buildRoot 'Dependencies')
Expand-ReviewedSource 'ImageMagick/Configure' (Join-Path $buildRoot 'Configure')
foreach ($entry in $manifest.sourceEntries) {
    if ($entry.repository -notmatch '^ImageMagick/[A-Za-z0-9.-]+$') { throw 'Unexpected dependency name.' }
    $name = $entry.repository.Split('/')[1]
    $group = if ($name -eq 'fontconfig') { 'NonWindowsDependencies' } else { 'Dependencies' }
    Expand-ReviewedSource $entry.repository (Join-Path $buildRoot ('Dependencies/' + $group + '/' + $name))
}
Write-Output 'Source tree prepared; no upstream build or checkout scripts executed.'
