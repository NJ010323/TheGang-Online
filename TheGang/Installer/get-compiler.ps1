# Prépare le compilateur officiel en mode portable, sans installation système ni association de fichiers.
param([string]$ToolDirectory = (Join-Path $PSScriptRoot '../artifacts/tools/inno-6.7.3'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
$compiler = Join-Path $ToolDirectory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
New-Item -ItemType Directory -Force $ToolDirectory | Out-Null
$download = Join-Path $ToolDirectory 'innosetup-6.7.3.exe'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
# Digest SHA-256 de l'asset officiel GitHub is-6_7_3, épinglé avec la version du compilateur.
$expected = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
if (!(Test-Path -LiteralPath $download) -or (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) {
    Invoke-WebRequest -Uri $url -OutFile $download -TimeoutSec 180
}
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) { throw 'Compilateur Inno Setup corrompu.' }
$signature = Get-AuthenticodeSignature -LiteralPath $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
    throw 'La signature officielle du compilateur ne peut pas être vérifiée.'
}
$start = [Diagnostics.ProcessStartInfo]::new($download)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
@('/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', "/DIR=$ToolDirectory", "/LOG=$(Join-Path $ToolDirectory 'bootstrap.log')") | ForEach-Object { $start.ArgumentList.Add($_) }
$process = [Diagnostics.Process]::Start($start)
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw 'Préparation du compilateur échouée.' }
return $compiler
