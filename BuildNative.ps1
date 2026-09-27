param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'vendor'),[switch]$RunUnitTests)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio C++ Build Tools ontbreken.' }
$msvc = Get-ChildItem -LiteralPath (Join-Path $vs 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $kits 'Include') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\windows.h') } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $msvc -or -not $sdk) { throw 'MSVC of Windows SDK ontbreekt.' }
$native = Join-Path $PSScriptRoot 'src\G7Bridge.Native'
$vendor = Join-Path $PSScriptRoot 'vendor\gameinput'
$intermediate = Join-Path $PSScriptRoot 'artifacts\native'
New-Item -ItemType Directory -Path $OutputDirectory,$intermediate -Force | Out-Null
$savedInclude = $env:INCLUDE; $savedLib = $env:LIB
try {
    $env:INCLUDE = @($vendor,(Join-Path $msvc.FullName 'include'),(Join-Path $sdk.FullName 'ucrt'),(Join-Path $sdk.FullName 'shared'),(Join-Path $sdk.FullName 'um'),(Join-Path $sdk.FullName 'winrt'),(Join-Path $sdk.FullName 'cppwinrt')) -join ';'
    $env:LIB = @((Join-Path $msvc.FullName 'lib\x64'),(Join-Path $kits ('Lib\'+$sdk.Name+'\ucrt\x64')),(Join-Path $kits ('Lib\'+$sdk.Name+'\um\x64'))) -join ';'
    $compiler = Join-Path $msvc.FullName 'bin\Hostx64\x64\cl.exe'
    if ($RunUnitTests) {
        $unitExe = Join-Path $intermediate 'GameInputOpenTests.exe'
        & $compiler /nologo /utf-8 /std:c++20 /EHsc /O2 /MT /W4 /WX (Join-Path $PSScriptRoot 'tests\native\GameInputOpenTests.cpp') /Fo"$intermediate\" /Fe"$unitExe"
        if ($LASTEXITCODE -ne 0) { throw 'Native unittests bouwen mislukt.' }
        & $unitExe
        if ($LASTEXITCODE -ne 0) { throw 'Native unittests mislukt.' }
        $componentExe = Join-Path $intermediate 'VendorLifecycleTests.exe'
        & $compiler /nologo /utf-8 /std:c++20 /EHsc /O2 /MT /W4 /WX (Join-Path $PSScriptRoot 'tests\native\VendorLifecycleTests.cpp') windowsapp.lib cfgmgr32.lib /Fo"$intermediate\" /Fe"$componentExe"
        if ($LASTEXITCODE -ne 0) { throw 'Component-unittests bouwen mislukt.' }
        & $componentExe
        if ($LASTEXITCODE -ne 0) { throw 'Component-unittests mislukt.' }
    }
    & $compiler /nologo /utf-8 /std:c++20 /EHsc /O2 /MT /W4 /WX /LD (Join-Path $native 'G7Input.cpp') (Join-Path $native 'VendorGip.cpp') (Join-Path $vendor 'GameInput.lib') cfgmgr32.lib windowsapp.lib /Fo"$intermediate\" /link /OUT:"$OutputDirectory\G7Bridge.Native.dll" /IMPLIB:"$intermediate\G7Bridge.Native.lib" /DYNAMICBASE /NXCOMPAT
    if ($LASTEXITCODE -ne 0) { throw 'Native GameInput-adapter bouwen mislukt.' }
} finally { $env:INCLUDE = $savedInclude; $env:LIB = $savedLib }
