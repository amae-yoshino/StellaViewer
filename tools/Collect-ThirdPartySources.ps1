# Copyright (c) 2026 amae yoshino. SPDX-License-Identifier: MIT
# See licenses/RelinkTools.LICENSE.txt. Downloads only; never executes upstream code.
#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$manifestPath = Join-Path $projectRoot 'licenses/Magick.Native.SourceManifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$destination = Join-Path $projectRoot 'artifacts/third-party-sources/0.2.0-preview.4'
[IO.Directory]::CreateDirectory($destination) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

$entries = @(
    [pscustomobject]@{ repository = 'dlemstra/Magick.NET'; commit = $manifest.magickNet.commit }
    [pscustomobject]@{ repository = $manifest.native.repository; commit = $manifest.native.commit }
    [pscustomobject]@{ repository = $manifest.imageMagick.repository; commit = $manifest.imageMagick.commit }
    [pscustomobject]@{ repository = $manifest.dependencies.repository; commit = $manifest.dependencies.commit }
    [pscustomobject]@{ repository = $manifest.configure.repository; commit = $manifest.configure.commit }
) + @($manifest.sourceEntries)
$results = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$receiptPath = Join-Path $destination 'download-receipt.json'
$previousHashes = @{}
$recordedHashes = @{}
$recordedPath = Join-Path $projectRoot 'licenses/Magick.Native.SourceDownloads.json'
if (Test-Path -LiteralPath $recordedPath) {
    $recorded = Get-Content -LiteralPath $recordedPath -Raw | ConvertFrom-Json
    foreach ($entry in $recorded.archives) { $recordedHashes[$entry.file] = $entry.sha256 }
}
if (Test-Path -LiteralPath $receiptPath) {
    $previous = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    foreach ($entry in $previous.archives) { $previousHashes[$entry.file] = $entry.sha256 }
}

foreach ($entry in $entries) {
    # Restrict both hosts and file names to this pinned source inventory.
    if ($entry.repository -notmatch '^(ImageMagick|dlemstra)/[A-Za-z0-9.-]+$' -or
        $entry.commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid source manifest entry.' }
    $name = $entry.repository.Replace('/', '-') + '-' + $entry.commit + '.zip'
    $path = Join-Path $destination $name
    $url = 'https://codeload.github.com/' + $entry.repository + '/zip/' + $entry.commit
    try {
        if (Test-Path -LiteralPath $path) {
            if (-not $previousHashes.ContainsKey($name)) {
                throw 'Existing archive has no recorded checksum. Review it before retrying.'
            }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $previousHashes[$name]) {
                throw 'Existing archive differs from recorded checksum.'
            }
        } else {
            Write-Output ('Downloading ' + $entry.repository)
            # A failed download remains .partial and is never treated as a complete archive.
            $partial = $path + '.partial'
            Invoke-WebRequest -Uri $url -OutFile $partial -TimeoutSec 180 -Headers @{ 'User-Agent' = 'StellaViewer-License-Audit' }
            $archive = [IO.Compression.ZipFile]::OpenRead($partial)
            try {
                if ($archive.Entries.Count -eq 0) { throw 'Empty source archive.' }
            } finally { $archive.Dispose() }
            if ($recordedHashes.ContainsKey($name) -and
                (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $recordedHashes[$name]) {
                throw 'Downloaded archive differs from the recorded checksum.'
            }
            Move-Item -LiteralPath $partial -Destination $path
        }
        if ($recordedHashes.ContainsKey($name) -and
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $recordedHashes[$name]) {
            throw 'Archive differs from the recorded checksum.'
        }
        $results.Add([pscustomobject]@{
            repository = $entry.repository; commit = $entry.commit; url = $url
            file = $name; bytes = (Get-Item -LiteralPath $path).Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        })
    } catch {
        $failures.Add($entry.repository + ': ' + $_.Exception.Message)
        Write-Warning $failures[$failures.Count - 1]
    }
    [ordered]@{
        baseline = $manifest.baseline
        status = 'source-snapshots-only-not-a-verified-corresponding-source-bundle'
        checksumPurpose = 'Detect later changes; not independent upstream authentication.'
        archives = @($results.ToArray()); failures = @($failures.ToArray())
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding utf8
}

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'Magick.Native.SourceManifest.json')
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses/Magick.Native.SourceGuide.md') -Destination $destination
Write-Output ('Source snapshots: ' + $results.Count + '/' + $entries.Count)
if ($failures.Count -gt 0) { throw 'Source collection incomplete. See download-receipt.json.' }
