param(
    [switch]$RunUnitTests,
    [string]$CompilerPath = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $projectRoot 'Build.ps1') -RunUnitTests:$RunUnitTests }
if ($RunUnitTests -and $SkipBuild) { throw '-RunUnitTests cannot be combined with -SkipBuild.' }
if (-not $CompilerPath) {
    $candidates = @(
        $(if (Get-Command ISCC.exe -ErrorAction SilentlyContinue) { (Get-Command ISCC.exe).Source }),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 6.7.3 or pass -CompilerPath with the full path to ISCC.exe.'
}
[xml]$properties = Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$version = [string]$properties.Project.PropertyGroup.Version
$package = Join-Path $projectRoot 'artifacts\package'
& $CompilerPath ('/DAppVersion='+$version) ('/DPackageDir='+$package) (Join-Path $projectRoot 'installer\G7Bridge.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $projectRoot ('artifacts\installer\G7Bridge-'+$version+'-Setup-x64.exe')
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
($hash+'  '+[IO.Path]::GetFileName($installer)) | Set-Content -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($installer)) 'SHA256SUMS.txt') -Encoding ascii
Write-Output ('Installer: '+$installer)
Write-Output 'The installer was compiled, not executed.'
