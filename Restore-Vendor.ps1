$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'vendor\HIDMaestro.Core.dll'
$expectedDll = '593509D1B48168A986C62273B2876351CAB5F1D7766F94033BBD8B63FD628AE6'
if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $expectedDll) { return }
$cache = Join-Path $PSScriptRoot 'artifacts\downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'HIDMaestro-v2.0.2.zip'
$expectedArchive = 'BEC5FA3936A9A2A0E0D0835974A37196CB6502C36796577167C94B49F62D326C'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri 'https://github.com/hifihedgehog/HIDMaestro/releases/download/v2.0.2/HIDMaestro-v2.0.2.zip' -OutFile $archive
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
Write-Output 'Restored pinned HIDMaestro.Core 2.0.2. No drivers installed.'
