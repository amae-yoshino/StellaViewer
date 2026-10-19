# Copyright (c) 2026 amae yoshino. SPDX-License-Identifier: MIT
# See licenses/RelinkTools.LICENSE.txt. Requires Prepare-NativeSourceTree.ps1.
# Does not download code, touch release output, or execute upstream checkout scripts.
#requires -Version 7.0
[CmdletBinding()]
param([string]$BuildDirectory, [switch]$ContinueAfterDependencies)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $projectRoot 'artifacts/license-relink-sources' }
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory, (Get-Location).Path)
$configureRoot = Join-Path $buildRoot 'Configure'
$dependenciesRoot = Join-Path $buildRoot 'Dependencies'
$nativeRoot = Join-Path $buildRoot 'Magick.Native'
$imageRoot = Join-Path $nativeRoot 'src/ImageMagick'
$vswhere = 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild/Current/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio 2026 MSBuild is required.' }
foreach ($directory in @($configureRoot, $dependenciesRoot, $imageRoot)) {
    if (-not (Test-Path -LiteralPath $directory)) { throw ('Source directory missing: ' + $directory) }
}

function Invoke-Build($solution, $configuration) {
    & $msbuild $solution /m:2 /t:Build "/p:Configuration=$configuration" /p:Platform=x64 /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $solution) }
}

function Invoke-Configuration($root, $arguments) {
    # Configure cleans its own demo/fuzz outputs; limit it to this dedicated source tree.
    $root = [IO.Path]::GetFullPath($root)
    if (-not $root.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Configuration root escapes the dedicated build tree.'
    }
    $destination = Join-Path $root 'Configure'
    if (-not (Test-Path -LiteralPath $destination)) {
        Copy-Item -LiteralPath $configureRoot -Destination $destination -Recurse
    }
    $process = Start-Process -FilePath (Join-Path $configureRoot 'Artifacts/Release/x64/bin/Configure.Release.x64.exe') -ArgumentList $arguments -WorkingDirectory $destination -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $root 'IM7.Static.x64.sln'))) {
        throw ('Configuration failed: ' + $root)
    }
}

if (-not $ContinueAfterDependencies) {
    Invoke-Build (Join-Path $configureRoot 'Configure.sln') 'Release'
    Invoke-Configuration $dependenciesRoot @('/noWizard','/VS2026','/x64','/static','/noOpenMP','/linkRuntime','/includeNonWindows')
    Invoke-Build (Join-Path $dependenciesRoot 'IM7.Static.x64.sln') 'Release'
}

$artifacts = Join-Path $dependenciesRoot 'Artifacts'
$imageArtifacts = Join-Path $imageRoot 'Artifacts'
[IO.Directory]::CreateDirectory($imageArtifacts) | Out-Null
Get-ChildItem -LiteralPath $artifacts | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $imageArtifacts -Recurse -Force
}
# Only the ImageMagick pass uses prebuilt inputs; leave the dependency source build editable.
Get-ChildItem -LiteralPath (Join-Path $artifacts 'lib') -Filter '*.lib' | ForEach-Object Name | Set-Content -LiteralPath (Join-Path $imageArtifacts 'pre-build-libs.txt') -Encoding ascii
Invoke-Configuration $imageRoot @('/noWizard','/VS2026','/static','/Q8','/opencl','/noDpc','/noHdri','/noOpenMP','/x64','/linkRuntime')
@'
<Project><ItemDefinitionGroup Condition="'$(MSBuildProjectName)' == 'CORE_MagickCore'"><ClCompile><PreprocessorDefinitions>%(PreprocessorDefinitions);MAGICKCORE_LIBRARY_NAME="Magick.Native-Q8-x64.dll"</PreprocessorDefinitions></ClCompile></ItemDefinitionGroup></Project>
'@ | Set-Content -LiteralPath (Join-Path $imageRoot 'Directory.Build.targets') -Encoding utf8
Invoke-Build (Join-Path $imageRoot 'IM7.Static.x64.sln') 'Release'
Invoke-Build (Join-Path $nativeRoot 'Magick.Native.sln') 'ReleaseQ8'
Write-Output (Join-Path $nativeRoot 'src/Magick.Native/bin/ReleaseQ8/x64/Magick.Native-Q8-x64.dll')
