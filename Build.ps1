param([switch]$RunUnitTests)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & (Join-Path $projectRoot 'Restore-Vendor.ps1')
    & (Join-Path $projectRoot 'BuildNative.ps1') -RunUnitTests:$RunUnitTests
    if ($RunUnitTests) {
        dotnet run --project tests/G7Bridge.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Unittests mislukt.' }
    }
    $outputDirectory = Join-Path $projectRoot 'artifacts\package'
    # Always package a clean directory; old research notes must not leak into releases.
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
    $packagePath = [IO.Path]::GetFullPath($outputDirectory)
    if ($packagePath -ne (Join-Path $artifactRoot 'package')) { throw 'Unexpected package path.' }
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Recurse -Force }
    dotnet publish src/G7Bridge.Windows/G7Bridge.Windows.csproj -c Release -r win-x64 --self-contained true -o $outputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publiceren mislukt.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'DEPENDENCIES.md'),(Join-Path $projectRoot 'LICENSE') -Destination $outputDirectory
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $outputDirectory -Recurse -Force
    foreach ($directory in @('drivers','licenses','sources')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot ('vendor\' + $directory)) -Destination $outputDirectory -Recurse -Force
    }
    $manifest=@(Get-ChildItem -LiteralPath $outputDirectory -File -Recurse | Where-Object {$_.Name -ne 'package-files.json'} | ForEach-Object {[IO.Path]::GetRelativePath($outputDirectory,$_.FullName)})
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'package-files.json') -Encoding utf8
    Write-Output ('Package: ' + $outputDirectory)
    Write-Output 'No app, controller or driver has been started.'
}
finally { Pop-Location }
