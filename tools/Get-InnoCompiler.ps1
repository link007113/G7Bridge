$ErrorActionPreference = 'Stop'
$cache = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\tools'
$compiler = Join-Path $cache 'InnoSetup\ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$setup = Join-Path $cache 'innosetup-6.7.3.exe'
Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $setup
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') {
    throw 'Inno Setup compiler hash mismatch.'
}
$arguments = '/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /TASKS="" /DIR="'+(Join-Path $cache 'InnoSetup')+'"'
$process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw ('Inno Setup compiler extraction failed: '+$process.ExitCode) }
return $compiler
