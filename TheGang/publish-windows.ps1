# Prépare un dossier autonome : les joueurs lancent View.exe sans installer cloudflared.
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/win-x64'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$cloudflaredVersion = '2026.10.1'
$expectedSha256 = '4D2490E74082C5D48DBFEDAF92576FA5BD1702379AB88EB7FAA0CFBE8DC891B6'
$cloudflaredUrl = "https://github.com/cloudflare/cloudflared/releases/download/$cloudflaredVersion/cloudflared-windows-amd64.exe"
$licenseUrl = "https://raw.githubusercontent.com/cloudflare/cloudflared/$cloudflaredVersion/LICENSE"

dotnet publish (Join-Path $PSScriptRoot 'View/View.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'La publication du jeu a échoué.' }

$binaryPath = Join-Path $OutputDirectory 'cloudflared.exe'
if (-not (Test-Path -LiteralPath $binaryPath) -or
    (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash -ne $expectedSha256) {
    Invoke-WebRequest -Uri $cloudflaredUrl -OutFile $binaryPath
}
$actualSha256 = (Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
    Remove-Item -LiteralPath $binaryPath
    throw 'La somme SHA-256 de cloudflared ne correspond pas à la version officielle.'
}
Invoke-WebRequest -Uri $licenseUrl -OutFile (Join-Path $OutputDirectory 'cloudflared-LICENSE.txt')

Write-Output "Jeu prêt : $(Join-Path $OutputDirectory 'View.exe')"
