$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'vendor\HIDMaestro.Core.dll'
$expectedDll = 'D613BE086178D34DEF0C8D3869E801B55CE16D49B7A6E4516281D067AA730D9A'
if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $expectedDll) { return }
$cache = Join-Path $PSScriptRoot 'artifacts\downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'HIDMaestro-v1.9.0.zip'
$expectedArchive = '1FA4A57B6F2DB9DC943BB81047808FDF097955497B96F22C1944DDD961B59605'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri 'https://github.com/hifihedgehog/HIDMaestro/releases/download/v1.9.0/HIDMaestro-v1.9.0.zip' -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedArchive) { throw 'HIDMaestro archive hash mismatch; download not used.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries | Where-Object { $_.FullName -eq 'HIDMaestro.Core.dll' })
    if ($entries.Count -ne 1) { throw 'HIDMaestro.Core.dll is missing from the pinned archive root.' }
    $temporary = $destination + '.new'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], $temporary, $true)
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $expectedDll) { throw 'HIDMaestro DLL hash mismatch; library not used.' }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
} finally { $zip.Dispose() }
Write-Output 'Restored pinned HIDMaestro.Core 1.9.0. No drivers installed.'
