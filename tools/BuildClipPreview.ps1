param([string]$BuildPath)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $BuildPath) { $BuildPath = Join-Path $workspace 'artifacts/native-clip' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio desktop C++ tools are required for CLIP preview support' }
$cmake = Join-Path $installation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
& $cmake -S (Join-Path $workspace 'src/StellaViewer.ClipPreview') -B $BuildPath -G 'Visual Studio 18 2026' -A x64
if ($LASTEXITCODE -ne 0) { throw 'CLIP preview configuration failed' }
& $cmake --build $BuildPath --config Release --parallel 1 -- /nr:false
if ($LASTEXITCODE -ne 0) { throw 'CLIP preview build failed' }
